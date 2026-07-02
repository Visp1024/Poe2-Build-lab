using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PBLEngine;

// Trader-часть LuaHost: HTTP-мост для launch:DownloadPage и обвязка PBLTrader.
// Lua-классы трейдера (TradeQueryGenerator/-Requests/-RateLimiter) не меняются —
// им нужен только рабочий launch:DownloadPage, которого нет в HeadlessWrapper.
public sealed partial class LuaHost
{
    private sealed record HttpCompletion(long Id, string? Body, string? Header, string? Error);

    private readonly ConcurrentQueue<HttpCompletion> _httpCompletions = new();
    private int _httpInFlight;
    private HttpClient? _traderHttpClient;
    private HttpMessageHandler? _activeHttpHandler;
    private bool _traderHttpRegistered;

    /// <summary>Подменный handler для тестов; null → реальный SocketsHttpHandler.
    /// Учитывается при каждом EnsureTraderHttp (клиент пересоздаётся при смене).</summary>
    public HttpMessageHandler? TraderHttpHandler { get; set; }

    /// <summary>true когда нет HTTP-запросов в полёте (готовые ответы могут ждать в очереди).</summary>
    public bool TraderHttpIdle => Volatile.Read(ref _httpInFlight) == 0;

    private static string EngineLuaDir => Path.Combine(AppContext.BaseDirectory, "lua");

    public void EnsureTraderHttp()
    {
        if (!_traderHttpRegistered)
        {
            State.RegisterFunction("PBL_HttpStart", this,
                typeof(LuaHost).GetMethod(nameof(TraderHttpStart),
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!);
            var luaPath = Path.Combine(EngineLuaDir, "trader_http.lua");
            State.DoString(File.ReadAllText(luaPath), "@trader_http.lua");
            _traderHttpRegistered = true;
        }
        if (_traderHttpClient is null || !ReferenceEquals(_activeHttpHandler, TraderHttpHandler))
        {
            _traderHttpClient?.Dispose();
            _activeHttpHandler = TraderHttpHandler;
            // Инжектированный handler не диспозим вместе с клиентом — им владеет тест
            _traderHttpClient = TraderHttpHandler is null
                ? new HttpClient(new SocketsHttpHandler(), disposeHandler: true)
                : new HttpClient(TraderHttpHandler, disposeHandler: false);
            _traderHttpClient.Timeout = TimeSpan.FromSeconds(30);
            _traderHttpClient.DefaultRequestHeaders.UserAgent.ParseAdd("PathOfBuilding-PBLApp/1.0");
        }
    }

    private bool _traderInitLoaded;

    /// <summary>Идемпотентно: HTTP-мост + загрузка trader.lua + PBLTrader.Init()
    /// (пере-инициализация генератора при смене билда — внутри Init).</summary>
    public void EnsureTraderInit()
    {
        EnsureTraderHttp();
        if (!_traderInitLoaded)
        {
            State.DoString(File.ReadAllText(Path.Combine(EngineLuaDir, "trader.lua")), "@trader.lua");
            _traderInitLoaded = true;
        }
        State.DoString("PBLTrader.Init()");
    }

    // Вызывается ИЗ Lua (на Lua-потоке). Снимает данные и уходит в пул — Lua не блокируется.
    private void TraderHttpStart(long id, string url, string? header, string? body)
    {
        var client = _traderHttpClient!;
        Interlocked.Increment(ref _httpInFlight);
        _ = Task.Run(async () =>
        {
            try
            {
                using var req = new HttpRequestMessage(
                    body is null ? HttpMethod.Get : HttpMethod.Post, url);
                if (body is not null)
                    req.Content = new StringContent(body);
                if (header is not null)
                {
                    foreach (var line in header.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                    {
                        var idx = line.IndexOf(':');
                        if (idx <= 0) continue;
                        var name = line[..idx].Trim();
                        var value = line[(idx + 1)..].Trim();
                        if (name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                        {
                            if (req.Content is not null)
                                req.Content.Headers.ContentType =
                                    new System.Net.Http.Headers.MediaTypeHeaderValue(value);
                        }
                        else
                        {
                            req.Headers.TryAddWithoutValidation(name, value);
                        }
                    }
                }
                using var resp = await client.SendAsync(req);
                var respBody = await resp.Content.ReadAsStringAsync();
                var sb = new StringBuilder();
                sb.Append($"HTTP/1.1 {(int)resp.StatusCode} {resp.ReasonPhrase}\r\n");
                foreach (var h in resp.Headers)
                    foreach (var v in h.Value) sb.Append($"{h.Key}: {v}\r\n");
                foreach (var h in resp.Content.Headers)
                    foreach (var v in h.Value) sb.Append($"{h.Key}: {v}\r\n");
                // Не-2xx отдаём как errMsg + header: коды 429/401 разбирает сам Lua-код трейдера
                string? err = resp.IsSuccessStatusCode
                    ? null : $"Response code: {(int)resp.StatusCode}";
                _httpCompletions.Enqueue(new HttpCompletion(id, respBody, sb.ToString(), err));
            }
            catch (Exception ex)
            {
                _httpCompletions.Enqueue(new HttpCompletion(id, null, null, ex.Message));
            }
            finally { Interlocked.Decrement(ref _httpInFlight); }
        });
    }

    // ── Генерация взвешенного запроса ────────────────────────────────────────

    /// <summary>Сериализует все trader-обращения к Lua-состоянию.</summary>
    private readonly SemaphoreSlim _traderLua = new(1, 1);

    public sealed record TraderQueryResult(string? QueryJson, string? Error);

    /// <summary>Гонит корутину TradeQueryGenerator до готового query JSON.
    /// resumeProgress получает число сделанных resume'ов (индикатор активности).</summary>
    public async Task<TraderQueryResult> GenerateTradeQueryAsync(
        string slotName, string optionsJson, IProgress<int>? resumeProgress, CancellationToken ct)
    {
        await _traderLua.WaitAsync(ct);
        try
        {
            EnsureTraderInit();
            State["_pblSlotName"] = slotName;
            State["_pblOptions"] = optionsJson;
            var start = (string)State.DoString(
                "return PBLTrader.StartGenerate(_pblSlotName, _pblOptions)")[0];
            if (start.StartsWith("error:"))
                return new TraderQueryResult(null, start[6..].Trim());
        }
        finally { _traderLua.Release(); }

        var resumes = 0;
        while (true)
        {
            if (ct.IsCancellationRequested)
            {
                await _traderLua.WaitAsync(CancellationToken.None);
                try { State.DoString("PBLTrader.CancelGenerate()"); }
                finally { _traderLua.Release(); }
                ct.ThrowIfCancellationRequested();
            }
            await _traderLua.WaitAsync(ct);
            bool done;
            try { done = (bool)State.DoString("return PBLTrader.StepGenerate()")[0]; }
            finally { _traderLua.Release(); }
            resumeProgress?.Report(++resumes);
            if (done) break;
            await Task.Yield(); // отпускаем семафор — UI/другие вызовы не голодают
        }

        await _traderLua.WaitAsync(CancellationToken.None);
        try
        {
            var r = State.DoString("return PBLTrader.GetGenerateResult()");
            return new TraderQueryResult(r[0] as string, r[1] as string);
        }
        finally { _traderLua.Release(); }
    }

    // ── Поиск и fetch ────────────────────────────────────────────────────────

    public sealed record TraderListing(
        string Id, string ItemText, double Amount, string Currency,
        string PriceType, string Whisper, string Seller, double Weight);

    public sealed record TraderSearchResult(
        IReadOnlyList<TraderListing> Listings, string? QueryId, string? Error, double RateLimitWaitSec);

    /// <summary>Поиск + fetch (realm фиксирован "poe2"). Качает очередь TradeQueryRequests
    /// и доставляет HTTP-ответы, пока Lua не отдаст результаты.</summary>
    public async Task<TraderSearchResult> SearchTradeAsync(
        string league, string queryJson, CancellationToken ct)
    {
        await _traderLua.WaitAsync(ct);
        try
        {
            EnsureTraderInit();
            State["_pblLeague"] = league;
            State["_pblQuery"] = queryJson;
            State.DoString("PBLTrader.StartSearch('poe2', _pblLeague, _pblQuery)");
        }
        finally { _traderLua.Release(); }

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            string stateJson;
            await _traderLua.WaitAsync(ct);
            try
            {
                DrainTraderHttp();
                State.DoString("PBLTrader.Pump()");
                DrainTraderHttp();
                stateJson = (string)State.DoString("return PBLTrader.GetSearchStateJson()")[0];
            }
            finally { _traderLua.Release(); }

            var state = System.Text.Json.JsonDocument.Parse(stateJson).RootElement;
            if (state.GetProperty("done").GetBoolean())
                return ParseSearchState(state);
            await Task.Delay(150, ct);
        }
    }

    private static TraderSearchResult ParseSearchState(System.Text.Json.JsonElement state)
    {
        var listings = new List<TraderListing>();
        if (state.TryGetProperty("items", out var items) &&
            items.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            foreach (var it in items.EnumerateArray())
            {
                listings.Add(new TraderListing(
                    it.GetProperty("id").GetString() ?? "",
                    it.GetProperty("item_string").GetString() ?? "",
                    it.GetProperty("amount").GetDouble(),
                    it.GetProperty("currency").GetString() ?? "",
                    it.TryGetProperty("priceType", out var pt) ? pt.GetString() ?? "" : "",
                    it.GetProperty("whisper").GetString() ?? "",
                    it.GetProperty("trader").GetString() ?? "",
                    it.TryGetProperty("weight", out var w) && w.GetString() is { } ws &&
                        double.TryParse(ws, System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out var wd) ? wd : 0));
            }
        }
        return new TraderSearchResult(
            listings,
            state.TryGetProperty("queryId", out var q) &&
                q.ValueKind == System.Text.Json.JsonValueKind.String ? q.GetString() : null,
            state.TryGetProperty("err", out var e) &&
                e.ValueKind == System.Text.Json.JsonValueKind.String ? e.GetString() : null,
            state.TryGetProperty("rateLimitWait", out var rl) &&
                rl.ValueKind == System.Text.Json.JsonValueKind.Number ? rl.GetDouble() : 0);
    }

    // ── Дифф результата ──────────────────────────────────────────────────────

    public sealed record TraderDiff(double DpsDiff, double EhpDiff, double StatValue);

    /// <summary>Считает Δ DPS/EHP и взвешенную ценность результата (calcFunc с repItem).
    /// null — предмет не распарсился или калькулятор недоступен.</summary>
    public async Task<TraderDiff?> ComputeListingDiffAsync(
        string slotName, string itemText, string statWeightsJson, CancellationToken ct)
    {
        await _traderLua.WaitAsync(ct);
        try
        {
            EnsureTraderInit();
            State["_pblSlotName"] = slotName;
            State["_pblItemText"] = itemText;
            State["_pblWeights"] = statWeightsJson;
            var json = (string)State.DoString(
                "return PBLTrader.ComputeDiffJson(_pblSlotName, _pblItemText, _pblWeights)")[0];
            var root = System.Text.Json.JsonDocument.Parse(json).RootElement;
            if (root.TryGetProperty("err", out _)) return null;
            return new TraderDiff(
                root.GetProperty("dpsDiff").GetDouble(),
                root.GetProperty("ehpDiff").GetDouble(),
                root.GetProperty("statValue").GetDouble());
        }
        finally { _traderLua.Release(); }
    }

    /// <summary>Доставить готовые HTTP-ответы Lua-callback'ам.
    /// Вызывать только с потока, владеющего Lua-состоянием (или под trader-семафором).</summary>
    public int DrainTraderHttp()
    {
        var n = 0;
        while (_httpCompletions.TryDequeue(out var c))
        {
            State["_pblHttpBody"] = c.Body;
            State["_pblHttpHeader"] = c.Header;
            State["_pblHttpErr"] = c.Error;
            State.DoString($"_pblHttpComplete({c.Id}, _pblHttpBody, _pblHttpHeader, _pblHttpErr)");
            State["_pblHttpBody"] = null;
            State["_pblHttpHeader"] = null;
            State["_pblHttpErr"] = null;
            n++;
        }
        return n;
    }
}
