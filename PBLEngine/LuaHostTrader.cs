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

    // ВАЖНО: не добавлять ConfigureAwait(false) в trader-цепочках — DoString обязан выполняться на UI-потоке (единственный поток NLua-состояния).
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
        finally
        {
            State["_pblSlotName"] = null;
            State["_pblOptions"] = null;
            _traderLua.Release();
        }

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
        finally
        {
            State["_pblLeague"] = null;
            State["_pblQuery"] = null;
            _traderLua.Release();
        }

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
            if (state.TryGetProperty("done", out var done) && done.GetBoolean())
            {
                try
                {
                    return ParseSearchState(state);
                }
                catch (KeyNotFoundException ex)
                {
                    // Диагностика: реальный ответ API может не иметь ожидаемого поля —
                    // сохраняем сырой JSON, чтобы увидеть, какого именно.
                    var dump = Path.Combine(Path.GetTempPath(), "pbl-trader-search-state.json");
                    try { File.WriteAllText(dump, stateJson); } catch { }
                    throw new InvalidOperationException(
                        $"Trade search state parse failed: {ex.Message} (raw json: {dump})", ex);
                }
            }
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
                // Все поля опциональны: Lua кладёт nil (dkjson опускает ключ) для
                // whisper/trader у ~b/o-лотов и т.п. — жёсткий GetProperty падал.
                static string Str(System.Text.Json.JsonElement el, string key) =>
                    el.TryGetProperty(key, out var v) &&
                    v.ValueKind == System.Text.Json.JsonValueKind.String ? v.GetString() ?? "" : "";
                listings.Add(new TraderListing(
                    Str(it, "id"),
                    Str(it, "item_string"),
                    it.TryGetProperty("amount", out var am) &&
                        am.ValueKind == System.Text.Json.JsonValueKind.Number ? am.GetDouble() : 0,
                    Str(it, "currency"),
                    Str(it, "priceType"),
                    Str(it, "whisper"),
                    Str(it, "trader"),
                    double.TryParse(Str(it, "weight"), System.Globalization.NumberStyles.Float,
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

    // ── Примерка ─────────────────────────────────────────────────────────────

    /// <summary>Импортирует itemText в пул и экипирует в слот (Undo вернёт).
    /// false — предмет не распарсился.</summary>
    public async Task<bool> TryOnListingAsync(string slotName, string itemText, CancellationToken ct)
    {
        await _traderLua.WaitAsync(ct);
        try
        {
            State["_pblItemText"] = itemText;
            State["_pblSlotName"] = slotName;
            var r = State.DoString(@"
                if not (build and build.itemsTab) then return false end
                build.itemsTab:CreateDisplayItemFromRaw(_pblItemText)
                local d = build.itemsTab.displayItem
                if not (d and d.baseName) then return false end
                if d.itemSocketCount and d.itemSocketCount > 0 and d.UpdateRunes
                    and d.base and d.base.tags then
                    pcall(function()
                        d:UpdateRunes()
                        if d.BuildAndParseRaw then d:BuildAndParseRaw() end
                    end)
                end
                build.itemsTab:AddDisplayItem(true)  -- noAutoEquip = true
                local newId = d.id
                local slot = build.itemsTab.slots[_pblSlotName]
                if slot and newId and build.itemsTab.items[newId] then
                    slot:SetSelItemId(newId)
                    build.itemsTab:PopulateSlots()
                    build.itemsTab:AddUndoState()
                    build.buildFlag = true
                end
                return true");
            TriggerRecalc();
            return r is { Length: > 0 } && r[0] is bool b && b;
        }
        finally
        {
            State["_pblItemText"] = null;
            State["_pblSlotName"] = null;
            _traderLua.Release();
        }
    }

    /// <summary>Слоты трейдера с именами текущих предметов (JSON из PBLTrader.GetSlotsJson).</summary>
    public string GetTraderSlotsJson()
    {
        _traderLua.Wait();
        try
        {
            EnsureTraderInit();
            return (string)State.DoString("return PBLTrader.GetSlotsJson()")[0];
        }
        finally { _traderLua.Release(); }
    }

    public string GetTraderWeightStatsJson()
    {
        _traderLua.Wait();
        try { EnsureTraderInit(); return (string)State.DoString("return PBLTrader.GetWeightStatsJson()")[0]; }
        finally { _traderLua.Release(); }
    }

    public string GetTraderWeightsJson()
    {
        _traderLua.Wait();
        try { EnsureTraderInit(); return (string)State.DoString("return PBLTrader.GetWeightsJson()")[0]; }
        finally { _traderLua.Release(); }
    }

    public void SetTraderWeights(string weightsJson)
    {
        _traderLua.Wait();
        try
        {
            EnsureTraderInit();
            State["_pblWeights"] = weightsJson;
            State.DoString("PBLTrader.SetWeightsJson(_pblWeights)");
        }
        finally
        {
            State["_pblWeights"] = null;
            _traderLua.Release();
        }
    }

    /// <summary>Trade-статы, доступные категории слота (для required-фильтров).</summary>
    public string GetTradeStatsForSlotJson(string slotName)
    {
        _traderLua.Wait();
        try
        {
            EnsureTraderInit();
            State["_pblSlotName"] = slotName;
            return (string)State.DoString("return PBLTrader.GetTradeStatsForSlotJson(_pblSlotName)")[0];
        }
        finally
        {
            State["_pblSlotName"] = null;
            _traderLua.Release();
        }
    }

    /// <summary>Добавляет and-группу required-фильтров в готовый query JSON.</summary>
    public async Task<string?> ApplyRequiredStatsAsync(
        string queryJson, string requiredJson, CancellationToken ct)
    {
        await _traderLua.WaitAsync(ct);
        try
        {
            EnsureTraderInit();
            State["_pblQuery"] = queryJson;
            State["_pblRequired"] = requiredJson;
            var r = State.DoString("return PBLTrader.ApplyRequiredStats(_pblQuery, _pblRequired)");
            return r is { Length: > 0 } ? r[0] as string : null;
        }
        finally
        {
            State["_pblQuery"] = null;
            State["_pblRequired"] = null;
            _traderLua.Release();
        }
    }

    // ── OAuth-токены ─────────────────────────────────────────────────────────

    /// <summary>Инжектит trade-токены в Lua: main.lastToken/... + main.api.
    /// Дальнейший refresh делает сам PoEAPI:ValidateAuth через DownloadPage-мост.</summary>
    public void SetTradeAuth(string? accessToken, string? refreshToken, long? tokenExpiry)
    {
        _traderLua.Wait();
        try
        {
            EnsureTraderInit();
            State["_pblTok"] = accessToken;
            State["_pblRef"] = refreshToken;
            State["_pblExp"] = tokenExpiry.HasValue ? (double)tokenExpiry.Value : null;
            State.DoString(@"
                main.lastToken = _pblTok
                main.lastRefreshToken = _pblRef
                main.tokenExpiry = _pblExp
                if main.api then
                    main.api.authToken = _pblTok
                    main.api.refreshToken = _pblRef
                    main.api.tokenExpiry = _pblExp or 0
                end");
        }
        finally
        {
            State["_pblTok"] = null;
            State["_pblRef"] = null;
            State["_pblExp"] = null;
            _traderLua.Release();
        }
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
        finally
        {
            State["_pblSlotName"] = null;
            State["_pblItemText"] = null;
            State["_pblWeights"] = null;
            _traderLua.Release();
        }
    }

    /// <summary>Доставить готовые HTTP-ответы Lua-callback'ам.
    /// Вызывать только с потока, владеющего Lua-состоянием (или под trader-семафором).</summary>
    public int DrainTraderHttp()
    {
        var n = 0;
        while (_httpCompletions.TryDequeue(out var c))
        {
            try
            {
                State["_pblHttpBody"] = c.Body;
                State["_pblHttpHeader"] = c.Header;
                State["_pblHttpErr"] = c.Error;
                State.DoString($"_pblHttpComplete({c.Id}, _pblHttpBody, _pblHttpHeader, _pblHttpErr)");
            }
            finally
            {
                State["_pblHttpBody"] = null;
                State["_pblHttpHeader"] = null;
                State["_pblHttpErr"] = null;
            }
            n++;
        }
        return n;
    }
}
