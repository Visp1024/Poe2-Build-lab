using PBLEngine;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PBLApp.Core.Trader;

/// <summary>
/// OAuth-логин на pathofexile.com для trade-API — зеркалит PoEAPI:FetchAuthToken
/// (src/Classes/PoEAPI.lua:81-139): PKCE S256, client_id=pob, redirect на
/// localhost со случайным портом. Токены хранятся в AppPreferences и
/// инжектятся в Lua main.api; refresh дальше делает сам Lua через DownloadPage-мост.
/// </summary>
public sealed class PoeOAuthService
{
    private const string Scopes = "account:profile account:leagues account:characters account:trade";
    private const string PrefAccess = "TraderAccessToken";
    private const string PrefRefresh = "TraderRefreshToken";
    private const string PrefExpiry = "TraderTokenExpiry";
    private const string PrefAccount = "TraderAccountName";

    private readonly LuaHost _host;
    private readonly HttpClient _http;

    public PoeOAuthService(LuaHost host, HttpMessageHandler? handler = null)
    {
        _host = host;
        _http = handler is null
            ? new HttpClient(new SocketsHttpHandler(), disposeHandler: true)
            : new HttpClient(handler, disposeHandler: false);
        _http.Timeout = TimeSpan.FromSeconds(30);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("PathOfBuilding-PBLApp/1.0");
    }

    public bool IsLoggedIn =>
        !string.IsNullOrEmpty(AppPreferences.Get(PrefAccess)) ||
        !string.IsNullOrEmpty(AppPreferences.Get(PrefRefresh));

    public string? AccountName =>
        AppPreferences.Get(PrefAccount) is { Length: > 0 } n ? n : null;

    // ── PKCE (тестируется без сети) ─────────────────────────────────────────

    public static (string Verifier, string Challenge) CreatePkcePair()
    {
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        return (verifier, challenge);
    }

    /// <summary>Разрешённые redirect-порты клиента "pob" (LaunchServer.lua:11) —
    /// GGG редиректит только на них, случайный порт не сработает.</summary>
    public static readonly int[] RedirectPorts = [49082, 49083, 49084];

    // redirect_uri добавляется без URL-кодирования — ровно как LaunchServer.lua:32
    public static string BuildAuthorizeUrl(string state, string challenge, int port) =>
        "https://www.pathofexile.com/oauth/authorize?client_id=pob&response_type=code"
        + "&scope=" + Scopes.Replace(" ", "%20")
        + "&state=" + state
        + "&code_challenge=" + challenge
        + "&code_challenge_method=S256"
        + $"&redirect_uri=http://localhost:{port}";

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');

    // ── Логин ────────────────────────────────────────────────────────────────

    /// <summary>Полный флоу: браузер → localhost-redirect → обмен кода на токен →
    /// prefs + инжект в Lua. false: отмена/state mismatch/ошибка обмена.</summary>
    public async Task<bool> LoginAsync(Action<string> openBrowser, CancellationToken ct = default)
    {
        var (verifier, challenge) = CreatePkcePair();
        var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();

        var (listener, port) = BindRegisteredPort();
        if (listener is null) return false; // все три порта заняты
        using var _ = listener;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        using var abort = timeout.Token.Register(() => { try { listener.Stop(); } catch { } });

        openBrowser(BuildAuthorizeUrl(state, challenge, port));

        string? code, gotState;
        try
        {
            var ctx = await listener.GetContextAsync();
            code = ctx.Request.QueryString["code"];
            gotState = ctx.Request.QueryString["state"];
            var html = Encoding.UTF8.GetBytes(
                "<html><body><h3>PathBuildLab: авторизация завершена, вкладку можно закрыть.</h3></body></html>");
            ctx.Response.ContentType = "text/html; charset=utf-8";
            await ctx.Response.OutputStream.WriteAsync(html, timeout.Token);
            ctx.Response.Close();
        }
        catch when (timeout.Token.IsCancellationRequested) { return false; }
        finally { try { listener.Stop(); } catch { } }

        if (string.IsNullOrEmpty(code) || gotState != state)
            return false;

        // Обмен кода на токен — форма как в PoEAPI.lua:120
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = "pob",
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = $"http://localhost:{port}",
            ["scope"] = Scopes,
            ["code_verifier"] = verifier,
        });
        using var resp = await _http.PostAsync("https://www.pathofexile.com/oauth/token", form, timeout.Token);
        if (!resp.IsSuccessStatusCode) return false;
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(timeout.Token));
        var root = doc.RootElement;
        var access = root.GetProperty("access_token").GetString();
        var refresh = root.TryGetProperty("refresh_token", out var r) ? r.GetString() : null;
        var expiresIn = root.TryGetProperty("expires_in", out var e) ? e.GetInt64() : 3600;
        if (string.IsNullOrEmpty(access)) return false;

        var expiry = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + expiresIn;
        AppPreferences.Set(PrefAccess, access);
        AppPreferences.Set(PrefRefresh, refresh ?? "");
        AppPreferences.Set(PrefExpiry, expiry.ToString());

        await FetchAccountNameAsync(access, timeout.Token);
        InjectIntoLua();
        return true;
    }

    private async Task FetchAccountNameAsync(string access, CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.pathofexile.com/profile");
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", access);
            using var resp = await _http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode) return;
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            if (doc.RootElement.TryGetProperty("name", out var n) && n.GetString() is { Length: > 0 } name)
                AppPreferences.Set(PrefAccount, name);
        }
        catch { /* имя аккаунта — best-effort */ }
    }

    public void Logout()
    {
        AppPreferences.Set(PrefAccess, "");
        AppPreferences.Set(PrefRefresh, "");
        AppPreferences.Set(PrefExpiry, "");
        AppPreferences.Set(PrefAccount, "");
        _host.SetTradeAuth(null, null, null);
    }

    /// <summary>Прокинуть сохранённые токены в Lua (main.api) — звать после инициализации хоста.</summary>
    public void InjectIntoLua()
    {
        var access = AppPreferences.Get(PrefAccess);
        if (string.IsNullOrEmpty(access)) return;
        var refresh = AppPreferences.Get(PrefRefresh);
        long? expiry = long.TryParse(AppPreferences.Get(PrefExpiry), out var e) ? e : null;
        _host.SetTradeAuth(access, string.IsNullOrEmpty(refresh) ? null : refresh, expiry);
    }

    private static (HttpListener? Listener, int Port) BindRegisteredPort()
    {
        foreach (var port in RedirectPorts)
        {
            var listener = new HttpListener();
            listener.Prefixes.Add($"http://localhost:{port}/");
            try
            {
                listener.Start();
                return (listener, port);
            }
            catch (HttpListenerException)
            {
                try { listener.Close(); } catch { }
            }
        }
        return (null, 0);
    }
}
