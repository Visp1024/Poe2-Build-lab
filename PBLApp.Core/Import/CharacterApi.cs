using PBLApp.Core.Trader;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PBLApp.Core.Import;

/// <summary>Один персонаж в списке аккаунта.</summary>
public sealed record CharacterSummary(string Name, string Class, int Level, string League);

/// <summary>Почему запрос к API персонажей не удался — коды разбираются так же,
/// как в ImportTab:DownloadCharacterList (src/Classes/ImportTab.lua:449-476).</summary>
public enum CharacterApiError
{
    None,
    /// <summary>Входа нет либо токен отозван (401).</summary>
    NotAuthenticated,
    /// <summary>Профиль аккаунта скрыт настройками приватности (403).</summary>
    PrivateProfile,
    /// <summary>Аккаунт или персонаж не найден (404).</summary>
    NotFound,
    /// <summary>Слишком частые запросы (429); <c>RetryAfterSeconds</c> — сколько ждать.</summary>
    RateLimited,
    /// <summary>Сеть, таймаут, нечитаемый ответ.</summary>
    Network,
}

/// <summary>Результат запроса: либо значение, либо код ошибки с текстом для UI.</summary>
public sealed record CharacterApiResult<T>(
    T? Value, CharacterApiError Error = CharacterApiError.None,
    string? Message = null, int RetryAfterSeconds = 0)
{
    public bool Ok => Error == CharacterApiError.None && Value is not null;
}

/// <summary>
/// Прямые (без Lua) запросы к OAuth-API персонажей PoE2 — зеркалит
/// PoEAPI:DownloadCharacterList / :DownloadCharacter (src/Classes/PoEAPI.lua:200-216).
/// Токен берётся у <see cref="PoeOAuthService"/> (тот же вход, что у «Трейдера»),
/// handler инжектируется в тестах.
/// </summary>
public sealed class CharacterApi
{
    private const string BaseUrl = "https://api.pathofexile.com/character/poe2";

    private readonly Func<CancellationToken, Task<string?>> _token;
    private readonly HttpClient _http;

    public CharacterApi(PoeOAuthService oauth, HttpMessageHandler? handler = null)
        : this(oauth.GetAccessTokenAsync, handler) { }

    /// <summary>Токен берётся у переданного провайдера — так тесты обходятся без
    /// настоящих prefs и сети.</summary>
    public CharacterApi(Func<CancellationToken, Task<string?>> tokenProvider,
        HttpMessageHandler? handler = null)
    {
        _token = tokenProvider;
        _http = handler is null
            ? new HttpClient(new SocketsHttpHandler(), disposeHandler: true)
            : new HttpClient(handler, disposeHandler: false);
        _http.Timeout = TimeSpan.FromSeconds(30);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("PathOfBuilding-PBLApp/1.0");
    }

    /// <summary>Список персонажей аккаунта. Пустой список — валидный ответ
    /// (на аккаунте нет персонажей PoE2), его отличает от ошибки <c>Ok</c>.</summary>
    public async Task<CharacterApiResult<IReadOnlyList<CharacterSummary>>> GetCharactersAsync(
        CancellationToken ct = default)
    {
        var body = await GetAsync(BaseUrl, ct);
        if (!body.Ok)
            return new CharacterApiResult<IReadOnlyList<CharacterSummary>>(
                null, body.Error, body.Message, body.RetryAfterSeconds);

        try
        {
            using var doc = JsonDocument.Parse(body.Value!);
            var list = new List<CharacterSummary>();
            if (doc.RootElement.TryGetProperty("characters", out var chars) &&
                chars.ValueKind == JsonValueKind.Array)
            {
                foreach (var c in chars.EnumerateArray())
                {
                    var name = Str(c, "name");
                    if (string.IsNullOrEmpty(name)) continue;
                    list.Add(new CharacterSummary(
                        name,
                        Str(c, "class") ?? "?",
                        c.TryGetProperty("level", out var l) && l.ValueKind == JsonValueKind.Number
                            ? l.GetInt32() : 0,
                        Str(c, "league") ?? "?"));
                }
            }
            return new CharacterApiResult<IReadOnlyList<CharacterSummary>>(list);
        }
        catch (JsonException ex)
        {
            return new CharacterApiResult<IReadOnlyList<CharacterSummary>>(
                null, CharacterApiError.Network, ex.Message);
        }
    }

    /// <summary>Сырой JSON одного персонажа — уходит в Lua как есть
    /// (там его разбирает dkjson и штатный ImportTab).</summary>
    public Task<CharacterApiResult<string>> GetCharacterJsonAsync(
        string name, CancellationToken ct = default)
        => GetAsync(BaseUrl + "/" + Uri.EscapeDataString(name), ct);

    private static string? Str(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private async Task<CharacterApiResult<string>> GetAsync(string url, CancellationToken ct)
    {
        var token = await _token(ct);
        if (string.IsNullOrEmpty(token))
            return new CharacterApiResult<string>(null, CharacterApiError.NotAuthenticated);

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            using var resp = await _http.SendAsync(req, ct);

            if (resp.IsSuccessStatusCode)
                return new CharacterApiResult<string>(await resp.Content.ReadAsStringAsync(ct));

            return resp.StatusCode switch
            {
                HttpStatusCode.Unauthorized => new CharacterApiResult<string>(null, CharacterApiError.NotAuthenticated),
                HttpStatusCode.Forbidden    => new CharacterApiResult<string>(null, CharacterApiError.PrivateProfile),
                HttpStatusCode.NotFound     => new CharacterApiResult<string>(null, CharacterApiError.NotFound),
                (HttpStatusCode)429         => new CharacterApiResult<string>(
                    null, CharacterApiError.RateLimited, null, RetryAfter(resp)),
                _ => new CharacterApiResult<string>(
                    null, CharacterApiError.Network, "HTTP " + (int)resp.StatusCode),
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return new CharacterApiResult<string>(null, CharacterApiError.Network, ex.Message);
        }
    }

    /// <summary>Сколько ждать после 429: заголовок Retry-After, иначе X-Rate-Limit-*-State
    /// (GGG кладёт туда «сработавший» период), иначе минута.</summary>
    private static int RetryAfter(HttpResponseMessage resp)
    {
        if (resp.Headers.TryGetValues("Retry-After", out var values))
            foreach (var v in values)
                if (int.TryParse(v, out var seconds) && seconds > 0)
                    return seconds;
        return 60;
    }
}
