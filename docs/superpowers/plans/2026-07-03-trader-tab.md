# Trader Tab Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Вкладка «Трейдер» в PBLApp: поиск апгрейдов по слоту на pathofexile.com/trade2 (взвешенный запрос как PoB Trader), с ценами, Δ DPS/EHP, примеркой, whisper и открытием в браузере.

**Architecture:** Lua-классы `TradeQueryGenerator`/`TradeQueryRequests`/`TradeQueryRateLimiter` переиспользуются без изменений через новый glue-модуль `PBLEngine/lua/trader.lua`. C# даёт HTTP-мост (`launch.DownloadPage` поверх HttpClient), «прокачку» очередей, OAuth (PKCE, client_id=pob) и MVVM-UI. Спек: `docs/superpowers/specs/2026-07-03-trader-tab-design.md`.

**Tech Stack:** .NET 9, NLua (Lua 5.4), Avalonia 12, CommunityToolkit.Mvvm, xUnit.

## Global Constraints

- Файлы `src/Classes/Trade*.lua`, `src/Classes/PoEAPI.lua` НЕ менять (upstream-sync).
- Весь доступ к NLua-состоянию сериализуется: у `TraderService`-слоя один `SemaphoreSlim(1,1)`; ни один HTTP-callback не трогает Lua вне его.
- Все сетевые вызовы — через инжектируемый `HttpMessageHandler` (тесты без сети).
- Все строки UI — через `Strings.resx`/`Strings.ru.resx` + `{loc:Tr Key}`; хардкод английского в XAML запрещён.
- Тесты: xUnit, `LuaHostFixture` (shared, init ~40 c). Запуск: `dotnet test PBLEngine.Tests/PBLEngine.Tests.csproj --filter "..."` из корня репо.
- Коммит после каждой задачи. Ветка: `feat/trader-tab`.
- OAuth: client_id=`pob`, scopes `account:profile account:leagues account:characters account:trade` — дословно как `src/Classes/PoEAPI.lua:5-10`.

**Ключевые Lua-контракты (справка для всех задач):**
- `TradeQueryGenerator:StartQuery(slot, options)` (`src/Classes/TradeQueryGenerator.lua:786`) — `slot` = объект с `slotName`,`selItemId`; `options.statWeights = {{stat="FullDPS", weightMult=1.0}, ...}`, опц. `maxPrice`, `maxPriceType`, `includeCorrupted`, `includeMirrored`, `includeRunes`, `maxLevel`, `jewelType`. Создаёт `calcContext.co`; открывает попап `main:OpenPopup` (headless-безопасен, `Modules/Main.lua:1650`).
- `TradeQueryGenerator:OnFrame()` (`:757`) — резюмит корутину; по завершении зовёт `FinishQuery()` → `self.requesterCallback(self.requesterContext, queryJson, errMsg)` (`:1096`) и `main:ClosePopup()`. `requesterCallback/-Context` мы задаём вручную (метод `RequestQuery` НЕ используем — он строит UI-попап).
- Корутина yield'ится каждые ~50 мс по `GetTime()` (`:710-715`).
- `TradeQueryRequests:SearchWithQueryWeightAdjusted(realm, league, queryJson, callback, {callbackQueryId=fn})` (`src/Classes/TradeQueryRequests.lua:104`) — callback(items, errMsg); items[i] = `{amount, currency, priceType, item_string, whisper, trader, weight, id}` (`:430-439`).
- `TradeQueryRequests:ProcessQueue(onRateLimit)` (`:23`) — качать периодически; `onRateLimit(waitTimeSec)` опционален. Bearer добавляется если `main.api.authToken` задан (`:64-66`).
- `launch:DownloadPage(url, callback, params)` — callback(`{header=..., body=...}`, errMsg). `header` должен содержать статус-строку `HTTP/1.1 NNN ...` (её матчит `PoEAPI.lua:189`) и заголовки `X-Rate-Limit-*`.
- `build.calcsTab:GetMiscCalculator()` → `calcFunc, baseOutput`; `calcFunc({repSlotName=..., repItem=item})` → output-таблица (`FullDPS`, `TotalEHP`, ...).
- Классы уже загружены headless: `require("lcurl.safe")` возвращает nil (`src/HeadlessWrapper.lua:178-181`), `LaunchSubScript` — NOP (`:121`), `Data/QueryMods.lua` закоммичен, dkjson/base64/sha2 грузятся (top-level require в этих классах уже отработал при старте).

---

### Task 1: HTTP-мост — `launch.DownloadPage` поверх HttpClient

**Files:**
- Create: `PBLEngine/lua/trader_http.lua`
- Create: `PBLEngine/LuaHostTrader.cs` (partial class LuaHost; при необходимости добавить `partial` в объявление `PBLEngine/LuaHost.cs`)
- Test: `PBLEngine.Tests/TraderHttpTests.cs`

**Interfaces:**
- Consumes: `LuaHost.State` (NLua), `HeadlessWrapper` окружение.
- Produces:
  - C#: `LuaHost.TraderHttpHandler { get; set; }` (тип `HttpMessageHandler?`, null → реальный `SocketsHttpHandler`); `void EnsureTraderHttp()` (идемпотентно регистрирует мост); `int DrainTraderHttp()` (вызывать ТОЛЬКО под Lua-семафором; возвращает число доставленных ответов); `bool TraderHttpIdle { get; }`.
  - Lua: рабочий `launch.DownloadPage`.

- [ ] **Step 1: Написать `PBLEngine/lua/trader_http.lua`**

```lua
-- PBL trader HTTP bridge: реализует launch:DownloadPage поверх C# HttpClient.
-- C# регистрирует функцию PBL_HttpStart(id, url, header, body) и доставляет
-- ответы вызовом _pblHttpComplete на Lua-потоке.
_pblHttp = { nextId = 1, pending = {} }

function launch:DownloadPage(url, callback, params)
    params = params or {}
    local id = _pblHttp.nextId
    _pblHttp.nextId = id + 1
    _pblHttp.pending[id] = callback
    PBL_HttpStart(id, url, params.header, params.body)
end

function _pblHttpComplete(id, body, header, errMsg)
    local cb = _pblHttp.pending[id]
    _pblHttp.pending[id] = nil
    if cb then
        cb({ body = body, header = header }, errMsg)
    end
end
```

- [ ] **Step 2: Написать failing-тест**

`PBLEngine.Tests/TraderHttpTests.cs` — фейковый handler + вызов из Lua:

```csharp
using System.Net;
using PBLEngine;
using Xunit;

namespace PBLEngine.Tests;

/// <summary>Подменный handler: отдаёт заранее заданные ответы и записывает запросы.</summary>
public sealed class FakeHttpHandler : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = [];
    public Func<HttpRequestMessage, HttpResponseMessage> Responder { get; set; } =
        _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
        // Content читаем сразу — иначе тело исчезнет после dispose запроса
        if (request.Content is not null) await request.Content.LoadIntoBufferAsync(ct);
        Requests.Add(request);
        return Responder(request);
    }
}

[Collection("LuaHost")]
public class TraderHttpTests : IClassFixture<LuaHostFixture>
{
    private readonly LuaHost _host;
    public TraderHttpTests(LuaHostFixture fixture) => _host = fixture.Host;

    [Fact]
    public async Task DownloadPage_DeliversBodyHeaderAndStatusLine_ToLuaCallback()
    {
        var fake = new FakeHttpHandler();
        fake.Responder = _ =>
        {
            var resp = new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new StringContent("hello-body") };
            resp.Headers.Add("X-Rate-Limit-Ip", "8:10:60");
            return resp;
        };
        _host.TraderHttpHandler = fake;
        _host.EnsureTraderHttp();

        _host.State.DoString(@"
            _testResult = nil
            launch:DownloadPage('https://example.test/x', function(response, errMsg)
                _testResult = { body = response.body, header = response.header, err = errMsg }
            end)");

        await WaitForDrainAsync();

        var body = (string)_host.State.DoString("return _testResult.body")[0];
        var header = (string)_host.State.DoString("return _testResult.header")[0];
        Assert.Equal("hello-body", body);
        Assert.Contains("HTTP/1.1 200", header);                 // статус-строка — её матчит PoEAPI.lua:189
        Assert.Contains("X-Rate-Limit-Ip: 8:10:60", header);     // rate-limit заголовки доходят до Lua
    }

    [Fact]
    public async Task DownloadPage_WithBody_SendsPost_WithHeaders()
    {
        var fake = new FakeHttpHandler();
        _host.TraderHttpHandler = fake;
        _host.EnsureTraderHttp();

        _host.State.DoString(@"
            _testDone = false
            launch:DownloadPage('https://example.test/api', function() _testDone = true end,
                { header = 'Content-Type: application/json\nAuthorization: Bearer tok123',
                  body = '{""q"":1}' })");
        await WaitForDrainAsync();

        var req = Assert.Single(fake.Requests);
        Assert.Equal(HttpMethod.Post, req.Method);
        Assert.Equal("Bearer tok123", req.Headers.Authorization!.ToString());
        Assert.Equal("{\"q\":1}", await req.Content!.ReadAsStringAsync());
        Assert.True((bool)_host.State.DoString("return _testDone")[0]);
    }

    [Fact]
    public async Task DownloadPage_NetworkError_PassesErrMsg()
    {
        var fake = new FakeHttpHandler { Responder = _ => throw new HttpRequestException("boom") };
        _host.TraderHttpHandler = fake;
        _host.EnsureTraderHttp();
        _host.State.DoString(@"
            _testErr = nil
            launch:DownloadPage('https://example.test/x', function(r, e) _testErr = e end)");
        await WaitForDrainAsync();
        var err = (string)_host.State.DoString("return _testErr")[0];
        Assert.Contains("boom", err);
    }

    private async Task WaitForDrainAsync()
    {
        for (var i = 0; i < 100; i++)
        {
            if (_host.TraderHttpIdle && _host.DrainTraderHttp() > 0) return;
            if (_host.TraderHttpIdle) { _host.DrainTraderHttp(); }
            await Task.Delay(50);
            _host.DrainTraderHttp();
        }
        Assert.Fail("HTTP bridge did not complete in 5s");
    }
}
```

- [ ] **Step 3: Убедиться, что тест падает**

Run: `dotnet test PBLEngine.Tests/PBLEngine.Tests.csproj --filter "FullyQualifiedName~TraderHttp"`
Expected: FAIL — `LuaHost` не содержит `TraderHttpHandler`/`EnsureTraderHttp` (ошибка компиляции).

- [ ] **Step 4: Реализовать `PBLEngine/LuaHostTrader.cs`**

```csharp
using System.Collections.Concurrent;
using System.Text;

namespace PBLEngine;

public partial class LuaHost
{
    private sealed record HttpCompletion(long Id, string? Body, string? Header, string? Error);

    private readonly ConcurrentQueue<HttpCompletion> _httpCompletions = new();
    private int _httpInFlight;
    private HttpClient? _traderHttpClient;
    private bool _traderHttpRegistered;

    /// <summary>Подменяется в тестах. Установка до EnsureTraderHttp().</summary>
    public HttpMessageHandler? TraderHttpHandler { get; set; }

    /// <summary>true когда нет запросов в полёте (ответы могут ждать в очереди).</summary>
    public bool TraderHttpIdle => Volatile.Read(ref _httpInFlight) == 0;

    public void EnsureTraderHttp()
    {
        if (_traderHttpRegistered) return;
        _traderHttpClient = new HttpClient(TraderHttpHandler ?? new SocketsHttpHandler())
            { Timeout = TimeSpan.FromSeconds(30) };
        _traderHttpClient.DefaultRequestHeaders.UserAgent.ParseAdd("PathOfBuilding-PBLApp/1.0");
        State.RegisterFunction("PBL_HttpStart", this,
            typeof(LuaHost).GetMethod(nameof(TraderHttpStart),
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!);
        var luaPath = Path.Combine(EngineLuaDir, "trader_http.lua"); // EngineLuaDir — тот же каталог, откуда LuaHost грузит compat.lua; если такого свойства нет, вычислить рядом с ним
        State.DoString(File.ReadAllText(luaPath), "trader_http.lua");
        _traderHttpRegistered = true;
    }

    // Вызывается ИЗ Lua (на Lua-потоке). Снимает данные и уходит в пул — Lua не блокируется.
    private void TraderHttpStart(long id, string url, string? header, string? body)
    {
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
                            req.Headers.TryAddWithoutValidation(name, value);
                    }
                }
                using var resp = await _traderHttpClient!.SendAsync(req);
                var respBody = await resp.Content.ReadAsStringAsync();
                var sb = new StringBuilder();
                sb.Append($"HTTP/1.1 {(int)resp.StatusCode} {resp.ReasonPhrase}\r\n");
                foreach (var h in resp.Headers)
                    foreach (var v in h.Value) sb.Append($"{h.Key}: {v}\r\n");
                foreach (var h in resp.Content.Headers)
                    foreach (var v in h.Value) sb.Append($"{h.Key}: {v}\r\n");
                // Ошибочные статусы отдаём Lua как errMsg + header: код 429/401 разбирает сам Lua
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

    /// <summary>Доставить готовые HTTP-ответы Lua-callback'ам. Только под Lua-семафором.</summary>
    public int DrainTraderHttp()
    {
        var n = 0;
        while (_httpCompletions.TryDequeue(out var c))
        {
            State["_pblHttpBody"] = c.Body;
            State["_pblHttpHeader"] = c.Header;
            State["_pblHttpErr"] = c.Error;
            State.DoString(
                $"_pblHttpComplete({c.Id}, _pblHttpBody, _pblHttpHeader, _pblHttpErr)");
            n++;
        }
        return n;
    }
}
```

Примечание: если в `LuaHost.cs` класс не `partial` — добавить ключевое слово. Если нет готового свойства пути к `PBLEngine/lua/` — найти, как `Initialize` находит `compat.lua`, и переиспользовать тот же способ.

- [ ] **Step 5: Прогнать тесты**

Run: `dotnet test PBLEngine.Tests/PBLEngine.Tests.csproj --filter "FullyQualifiedName~TraderHttp"`
Expected: PASS (3 теста).

- [ ] **Step 6: Проверить `.csproj` — `trader_http.lua` копируется в output** (по образцу `compat.lua` в `PBLEngine/PBLEngine.csproj`; добавить `<None Update="lua\trader_http.lua" CopyToOutputDirectory="PreserveNewest"/>` если там так).

- [ ] **Step 7: Commit**

```bash
git add PBLEngine/lua/trader_http.lua PBLEngine/LuaHostTrader.cs PBLEngine.Tests/TraderHttpTests.cs PBLEngine/PBLEngine.csproj PBLEngine/LuaHost.cs
git commit -m "feat(trader): launch.DownloadPage bridge over HttpClient"
```

---

### Task 2: Lua-glue `trader.lua` — инициализация трейд-классов headless

**Files:**
- Create: `PBLEngine/lua/trader.lua`
- Modify: `PBLEngine/LuaHostTrader.cs` (метод `EnsureTraderInit`)
- Test: `PBLEngine.Tests/TraderInitTests.cs`

**Interfaces:**
- Consumes: Task 1 (`EnsureTraderHttp`).
- Produces:
  - Lua-глобаль `PBLTrader` c полями `requests`, `generator` и функциями последующих задач.
  - C#: `void EnsureTraderInit()` — идемпотентно: `EnsureTraderHttp()` + загрузка `trader.lua` + `PBLTrader.Init()`. Пере-инициализация генератора при смене билда — внутри `PBLTrader.Init()`.

- [ ] **Step 1: Failing-тест**

```csharp
[Collection("LuaHost")]
public class TraderInitTests : IClassFixture<LuaHostFixture>
{
    private readonly LuaHost _host;
    public TraderInitTests(LuaHostFixture fixture) { _host = fixture.Host; _host.NewBuild(); }

    [Fact]
    public void EnsureTraderInit_CreatesGeneratorAndRequests()
    {
        _host.EnsureTraderInit();
        Assert.Equal("table", (string)_host.State.DoString("return type(PBLTrader.generator)")[0]);
        Assert.Equal("table", (string)_host.State.DoString("return type(PBLTrader.requests)")[0]);
        Assert.Equal("table", (string)_host.State.DoString("return type(main.api)")[0]);
        // generator привязан к текущему itemsTab
        Assert.True((bool)_host.State.DoString(
            "return PBLTrader.generator.itemsTab == build.itemsTab")[0]);
    }

    [Fact]
    public void GetSlotsJson_ReturnsBaseSlots()
    {
        _host.EnsureTraderInit();
        var json = (string)_host.State.DoString("return PBLTrader.GetSlotsJson()")[0];
        Assert.Contains("\"Helmet\"", json);
        Assert.Contains("\"Body Armour\"", json);
        Assert.Contains("\"Ring 1\"", json);
    }
}
```

Run: `dotnet test PBLEngine.Tests/PBLEngine.Tests.csproj --filter "FullyQualifiedName~TraderInit"`
Expected: FAIL — нет `EnsureTraderInit`.

- [ ] **Step 2: Написать `PBLEngine/lua/trader.lua`**

```lua
-- PBL trader glue: headless-обвязка над TradeQueryGenerator/TradeQueryRequests.
-- НЕ трогает src/Classes/* — только использует их.
local dkjson = require("dkjson")

PBLTrader = PBLTrader or {}

-- Слоты как в оригинальном PoB Trader (TradeQuery.lua:19)
PBLTrader.baseSlots = { "Weapon 1", "Weapon 2", "Weapon 1 Swap", "Weapon 2 Swap",
    "Helmet", "Body Armour", "Gloves", "Boots", "Amulet", "Ring 1", "Ring 2", "Ring 3",
    "Belt", "Charm 1", "Charm 2", "Charm 3", "Flask 1", "Flask 2" }

function PBLTrader.Init()
    if not main.api then
        main.api = new("PoEAPI", main.lastToken, main.lastRefreshToken, main.tokenExpiry)
    end
    PBLTrader.requests = PBLTrader.requests or new("TradeQueryRequests")
    -- Генератор держит ссылку на itemsTab — пересоздаём при смене билда
    if not PBLTrader.generator or PBLTrader.generator.itemsTab ~= build.itemsTab then
        PBLTrader.generator = new("TradeQueryGenerator", { itemsTab = build.itemsTab })
    end
end

function PBLTrader.GetSlotsJson()
    local out = {}
    for _, slotName in ipairs(PBLTrader.baseSlots) do
        local slot = build.itemsTab.slots[slotName]
        if slot then
            local item = slot.selItemId and build.itemsTab.items[slot.selItemId]
            table.insert(out, {
                slotName = slotName,
                itemName = item and (item.name or item.baseName) or "",
            })
        end
    end
    return dkjson.encode(out)
end
```

- [ ] **Step 3: Добавить в `PBLEngine/LuaHostTrader.cs`**

```csharp
    private bool _traderInitLoaded;

    public void EnsureTraderInit()
    {
        EnsureTraderHttp();
        if (!_traderInitLoaded)
        {
            State.DoString(File.ReadAllText(Path.Combine(EngineLuaDir, "trader.lua")), "trader.lua");
            _traderInitLoaded = true;
        }
        State.DoString("PBLTrader.Init()");
    }
```

И `<None Update="lua\trader.lua" CopyToOutputDirectory="PreserveNewest"/>` в `.csproj`.

- [ ] **Step 4: Прогнать тесты** — PASS. Если `build.itemsTab.slots[slotName]` — не словарь по имени (проверить `src/Classes/ItemsTab.lua`: поле `self.slots`), поправить выборку на итерацию `orderedSlots`.

- [ ] **Step 5: Commit**

```bash
git add PBLEngine/lua/trader.lua PBLEngine/LuaHostTrader.cs PBLEngine.Tests/TraderInitTests.cs PBLEngine/PBLEngine.csproj
git commit -m "feat(trader): headless PBLTrader glue (generator+requests init, slots json)"
```

---

### Task 3: Генерация взвешенного запроса с прогрессом и отменой

**Files:**
- Modify: `PBLEngine/lua/trader.lua`
- Modify: `PBLEngine/LuaHostTrader.cs`
- Test: `PBLEngine.Tests/TraderGenerateTests.cs`

**Interfaces:**
- Consumes: Task 2.
- Produces:
  - C#: `Task<TraderQueryResult> GenerateTradeQueryAsync(string slotName, string optionsJson, IProgress<int>? resumeProgress, CancellationToken ct)`.
  - `public sealed record TraderQueryResult(string? QueryJson, string? Error);`
  - `optionsJson` пример: `{"statWeights":[{"stat":"FullDPS","weightMult":1.0},{"stat":"TotalEHP","weightMult":0.5}],"includeCorrupted":true,"includeMirrored":false,"maxPrice":50,"maxPriceType":"divine"}`.
  - Внутренний Lua-API: `PBLTrader.StartGenerate(slotName, optionsJson)` → `"started"|"error: ..."`; `PBLTrader.StepGenerate()` → `done:boolean`; `PBLTrader.CancelGenerate()`; `PBLTrader.GetGenerateResult()` → `queryJson|nil, err|nil`.

- [ ] **Step 1: Failing-тест**

```csharp
[Collection("LuaHost")]
public class TraderGenerateTests : IClassFixture<LuaHostFixture>
{
    private readonly LuaHost _host;
    public TraderGenerateTests(LuaHostFixture fixture) { _host = fixture.Host; _host.NewBuild(); }

    private const string Options =
        """{"statWeights":[{"stat":"FullDPS","weightMult":1.0},{"stat":"TotalEHP","weightMult":0.5}],"includeCorrupted":false,"includeMirrored":false}""";

    [Fact(Timeout = 300_000)]
    [Trait("Category", "Slow")] // сотни calcFunc-прогонов; запускать явно
    public async Task GenerateTradeQuery_ForHelmet_ProducesWeightQuery()
    {
        var result = await _host.GenerateTradeQueryAsync("Helmet", Options, null, CancellationToken.None);
        Assert.Null(result.Error);
        Assert.NotNull(result.QueryJson);
        Assert.Contains("\"type\":\"weight\"", result.QueryJson);
        Assert.Contains("armour.helmet", result.QueryJson);
    }

    [Fact(Timeout = 60_000)]
    public async Task GenerateTradeQuery_Cancellation_LeavesCleanState()
    {
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(500);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _host.GenerateTradeQueryAsync("Helmet", Options, null, cts.Token));
        // корутина снята, попап закрыт — повторный запуск работает
        Assert.Equal("nil", (string)_host.State.DoString(
            "return type(PBLTrader.generator.calcContext and PBLTrader.generator.calcContext.co)")[0]);
    }
}
```

Run: `dotnet test PBLEngine.Tests/PBLEngine.Tests.csproj --filter "FullyQualifiedName~TraderGenerate"`
Expected: FAIL (нет метода).

- [ ] **Step 2: Lua-часть в `trader.lua`**

```lua
function PBLTrader.StartGenerate(slotName, optionsJson)
    PBLTrader.Init()
    local options, _, jsonErr = dkjson.decode(optionsJson)
    if not options then return "error: bad options json: " .. tostring(jsonErr) end
    local slot = build.itemsTab.slots[slotName]
    if not slot then return "error: unknown slot " .. slotName end

    PBLTrader.genDone, PBLTrader.genQuery, PBLTrader.genErr = false, nil, nil
    PBLTrader.generator.requesterContext = nil
    PBLTrader.generator.requesterCallback = function(_, queryJson, errMsg)
        PBLTrader.genQuery, PBLTrader.genErr, PBLTrader.genDone = queryJson, errMsg, true
    end
    PBLTrader.generator:StartQuery(slot, options)
    -- StartQuery молча выходит для неподдерживаемых категорий (:840-843)
    if not (PBLTrader.generator.calcContext and PBLTrader.generator.calcContext.co) then
        PBLTrader.genDone, PBLTrader.genErr = true, PBLTrader.genErr or "unsupported item category"
    end
    return "started"
end

function PBLTrader.StepGenerate()
    if not PBLTrader.genDone then
        PBLTrader.generator:OnFrame()   -- resume корутины; по смерти сам зовёт FinishQuery → callback
    end
    return PBLTrader.genDone
end

function PBLTrader.CancelGenerate()
    local g = PBLTrader.generator
    if g and g.calcContext and g.calcContext.co then
        g.calcContext.co = nil
        main:ClosePopup()
    end
    PBLTrader.genDone, PBLTrader.genQuery, PBLTrader.genErr = true, nil, "cancelled"
end

function PBLTrader.GetGenerateResult()
    return PBLTrader.genQuery, PBLTrader.genErr
end
```

- [ ] **Step 3: C#-часть в `LuaHostTrader.cs`**

Один общий семафор для всех trader-обращений к Lua:

```csharp
    private readonly SemaphoreSlim _traderLua = new(1, 1);

    public sealed record TraderQueryResult(string? QueryJson, string? Error);

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
            await Task.Yield(); // отпустить семафор — UI/другие вызовы не голодают
        }

        await _traderLua.WaitAsync(CancellationToken.None);
        try
        {
            var r = State.DoString("return PBLTrader.GetGenerateResult()");
            return new TraderQueryResult(r[0] as string, r[1] as string);
        }
        finally { _traderLua.Release(); }
    }
```

- [ ] **Step 4: Прогнать тесты** — PASS (Slow-тест до пары минут — сотни пересчётов).

- [ ] **Step 5: Commit**

```bash
git add PBLEngine/lua/trader.lua PBLEngine/LuaHostTrader.cs PBLEngine.Tests/TraderGenerateTests.cs
git commit -m "feat(trader): weighted query generation (coroutine-driven, cancellable)"
```

---

### Task 4: Поиск и fetch результатов через TradeQueryRequests

**Files:**
- Modify: `PBLEngine/lua/trader.lua`
- Modify: `PBLEngine/LuaHostTrader.cs`
- Test: `PBLEngine.Tests/TraderSearchTests.cs`

**Interfaces:**
- Consumes: Tasks 1–3.
- Produces:
  - C#: `Task<TraderSearchResult> SearchTradeAsync(string league, string queryJson, CancellationToken ct)` (realm фиксирован `"poe2"`).
  - `public sealed record TraderListing(string Id, string ItemText, double Amount, string Currency, string PriceType, string Whisper, string Seller, double Weight);`
  - `public sealed record TraderSearchResult(IReadOnlyList<TraderListing> Listings, string? QueryId, string? Error, double RateLimitWaitSec);`
  - Lua: `PBLTrader.StartSearch(realm, league, queryJson)`, `PBLTrader.Pump()`, `PBLTrader.GetSearchStateJson()`.

- [ ] **Step 1: Failing-тест с канонированными ответами API**

```csharp
[Collection("LuaHost")]
public class TraderSearchTests : IClassFixture<LuaHostFixture>
{
    private readonly LuaHost _host;
    public TraderSearchTests(LuaHostFixture fixture) { _host = fixture.Host; _host.NewBuild(); }

    // Минимальный search-ответ: 2 результата
    private const string SearchJson =
        """{"id":"testquery1","complexity":10,"result":["hashA","hashB"],"total":2}""";

    // Fetch-ответ в форме, которую разбирает FetchResultBlock (TradeQueryRequests.lua:272-444):
    // explicitMods — объекты {description, flags}; listing.price/whisper/account обязательны.
    private const string FetchJson =
        """
        {"result":[
          {"id":"hashA",
           "item":{"rarity":"RARE","name":"Doom Crown","typeLine":"Advanced Warrior Greathelm","ilvl":81,
                   "properties":[{"name":"Armour","values":[["500",0]]}],
                   "requirements":[{"name":"Level","values":[["65",0]]}],
                   "explicitMods":[{"description":"+120 to maximum Life","flags":{}}],
                   "pseudoMods":["Sum: 123.4"]},
           "listing":{"price":{"amount":5,"currency":"divine","type":"buyout"},
                      "whisper":"@Seller1 Hi, I would like to buy your Doom Crown",
                      "account":{"name":"Seller1"}}},
          {"id":"hashB",
           "item":{"rarity":"RARE","name":"Grim Visor","typeLine":"Advanced Warrior Greathelm","ilvl":80,
                   "explicitMods":[{"description":"+90 to maximum Life","flags":{}}]},
           "listing":{"price":{"amount":2,"currency":"exalted","type":"buyout"},
                      "whisper":"@Seller2 Hi, I would like to buy your Grim Visor",
                      "account":{"name":"Seller2"}}}
        ]}
        """;

    private FakeHttpHandler Setup()
    {
        var fake = new FakeHttpHandler();
        fake.Responder = req =>
        {
            var url = req.RequestUri!.ToString();
            var json = url.Contains("/api/trade2/search/") ? SearchJson
                     : url.Contains("/api/trade2/fetch/") ? FetchJson
                     : "{}";
            return new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        };
        _host.TraderHttpHandler = fake;
        return fake;
    }

    [Fact(Timeout = 30_000)]
    public async Task SearchTrade_ParsesListings()
    {
        var fake = Setup();
        var result = await _host.SearchTradeAsync("Standard",
            """{"query":{"status":{"option":"online"}},"sort":{"statgroup.0":"desc"}}""",
            CancellationToken.None);

        Assert.Null(result.Error);
        Assert.Equal("testquery1", result.QueryId);
        Assert.Equal(2, result.Listings.Count);
        var first = result.Listings[0];
        Assert.Equal(5, first.Amount);
        Assert.Equal("divine", first.Currency);
        Assert.Equal("Seller1", first.Seller);
        Assert.StartsWith("@Seller1", first.Whisper);
        Assert.Contains("Doom Crown", first.ItemText);
        Assert.Contains("+120 to maximum Life", first.ItemText);
    }

    [Fact(Timeout = 30_000)]
    public async Task SearchTrade_WithAuthToken_SendsBearer()
    {
        var fake = Setup();
        _host.EnsureTraderInit();
        _host.State.DoString("main.api.authToken = 'tok-abc'");
        await _host.SearchTradeAsync("Standard", """{"query":{}}""", CancellationToken.None);
        Assert.Contains(fake.Requests,
            r => r.Headers.Authorization?.ToString() == "Bearer tok-abc");
        _host.State.DoString("main.api.authToken = nil");
    }
}
```

Run: `dotnet test PBLEngine.Tests/PBLEngine.Tests.csproj --filter "FullyQualifiedName~TraderSearch"`
Expected: FAIL.

- [ ] **Step 2: Lua-часть в `trader.lua`**

```lua
function PBLTrader.StartSearch(realm, league, queryJson)
    PBLTrader.Init()
    PBLTrader.searchDone, PBLTrader.searchErr = false, nil
    PBLTrader.searchItems, PBLTrader.searchQueryId = nil, nil
    PBLTrader.rateLimitWait = 0
    PBLTrader.requests:SearchWithQueryWeightAdjusted(realm, league, queryJson,
        function(items, errMsg)
            PBLTrader.searchItems, PBLTrader.searchErr = items, errMsg
            PBLTrader.searchDone = true
        end,
        { callbackQueryId = function(id) PBLTrader.searchQueryId = id end })
end

function PBLTrader.Pump()
    PBLTrader.requests:ProcessQueue(function(waitTime)
        PBLTrader.rateLimitWait = waitTime or 0
    end)
end

function PBLTrader.GetSearchStateJson()
    return dkjson.encode({
        done = PBLTrader.searchDone or false,
        err = PBLTrader.searchErr,
        queryId = PBLTrader.searchQueryId,
        rateLimitWait = PBLTrader.rateLimitWait or 0,
        items = PBLTrader.searchItems,
    })
end
```

- [ ] **Step 3: C#-часть — pump-цикл**

```csharp
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
                    it.TryGetProperty("weight", out var w) &&
                        double.TryParse(w.GetString(), System.Globalization.CultureInfo.InvariantCulture, out var wd) ? wd : 0));
            }
        }
        return new TraderSearchResult(
            listings,
            state.TryGetProperty("queryId", out var q) ? q.GetString() : null,
            state.TryGetProperty("err", out var e) && e.ValueKind == System.Text.Json.JsonValueKind.String ? e.GetString() : null,
            state.TryGetProperty("rateLimitWait", out var rl) ? rl.GetDouble() : 0);
    }
```

- [ ] **Step 4: Прогнать тесты** — PASS. Если fetch-фикстура отвергается парсером — свериться с `TradeQueryRequests.lua:272-444` и поправить фикстуру (не код).

- [ ] **Step 5: Commit**

```bash
git add PBLEngine/lua/trader.lua PBLEngine/LuaHostTrader.cs PBLEngine.Tests/TraderSearchTests.cs
git commit -m "feat(trader): search+fetch pipeline over TradeQueryRequests with pump loop"
```

---

### Task 5: Δ-статы результата (примерочный calcFunc)

**Files:**
- Modify: `PBLEngine/lua/trader.lua`
- Modify: `PBLEngine/LuaHostTrader.cs`
- Test: `PBLEngine.Tests/TraderDiffTests.cs`

**Interfaces:**
- Consumes: Task 2.
- Produces:
  - C#: `Task<TraderDiff?> ComputeListingDiffAsync(string slotName, string itemText, string statWeightsJson, CancellationToken ct)`.
  - `public sealed record TraderDiff(double DpsDiff, double EhpDiff, double StatValue);`
  - Lua: `PBLTrader.ComputeDiffJson(slotName, itemString, statWeightsJson)`.

- [ ] **Step 1: Failing-тест**

```csharp
[Collection("LuaHost")]
public class TraderDiffTests : IClassFixture<LuaHostFixture>
{
    private readonly LuaHost _host;
    public TraderDiffTests(LuaHostFixture fixture) { _host = fixture.Host; _host.NewBuild(); }

    private const string ItemText =
        "Rarity: RARE\nDoom Crown\nAdvanced Warrior Greathelm\nArmour: 500\nItem Level: 81\nImplicits: 0\n+120 to maximum Life";

    [Fact(Timeout = 60_000)]
    public async Task ComputeListingDiff_LifeHelmet_PositiveEhp()
    {
        var diff = await _host.ComputeListingDiffAsync("Helmet", ItemText,
            """[{"stat":"FullDPS","weightMult":1.0},{"stat":"TotalEHP","weightMult":0.5}]""",
            CancellationToken.None);
        Assert.NotNull(diff);
        Assert.True(diff!.EhpDiff > 0, $"EhpDiff={diff.EhpDiff}");
    }
}
```

Run: `dotnet test PBLEngine.Tests/PBLEngine.Tests.csproj --filter "FullyQualifiedName~TraderDiff"` → FAIL.

- [ ] **Step 2: Lua-часть**

```lua
function PBLTrader.ComputeDiffJson(slotName, itemString, statWeightsJson)
    PBLTrader.Init()
    local weights = dkjson.decode(statWeightsJson) or {}
    local calcFunc, baseOutput = build.calcsTab:GetMiscCalculator()
    if not calcFunc then return dkjson.encode({ err = "no calculator" }) end
    local item = new("Item", itemString)
    local output = calcFunc({ repSlotName = slotName, repItem = item })
    local function dps(o) return o.FullDPS or o.CombinedDPS or o.TotalDPS or 0 end
    local statValue = PBLTrader.generator.WeightedRatioOutputs(baseOutput, output, weights) * 1000
    return dkjson.encode({
        dpsDiff = dps(output) - dps(baseOutput),
        ehpDiff = (output.TotalEHP or 0) - (baseOutput.TotalEHP or 0),
        statValue = statValue,
    })
end
```

- [ ] **Step 3: C#-часть**

```csharp
    public sealed record TraderDiff(double DpsDiff, double EhpDiff, double StatValue);

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
```

- [ ] **Step 4: Прогнать** → PASS.
- [ ] **Step 5: Commit**

```bash
git add PBLEngine/lua/trader.lua PBLEngine/LuaHostTrader.cs PBLEngine.Tests/TraderDiffTests.cs
git commit -m "feat(trader): per-listing stat diff via misc calculator"
```

---

### Task 6: Лиги и курсы валют (чистый C#)

**Files:**
- Create: `PBLApp.Core/Trader/TraderWebApi.cs`
- Test: `PBLEngine.Tests/TraderWebApiTests.cs` (проект уже ссылается на PBLApp.Core? если нет — добавить `<ProjectReference>` в `PBLEngine.Tests.csproj`)

**Interfaces:**
- Produces:
  - `public sealed class TraderWebApi(HttpMessageHandler? handler = null)`
  - `Task<IReadOnlyList<string>> GetLeaguesAsync(CancellationToken ct)` — GET `https://www.pathofexile.com/api/leagues?type=main&compact=1&realm=poe2`, фильтр `id` содержащих `"SSF"`.
  - `Task<IReadOnlyDictionary<string, double>> GetCurrencyRatesAsync(string league, CancellationToken ct)` — GET `https://poe.ninja/poe2/api/economy/exchange/current/overview?type=Currency&league={league}`; результат: currencyId → стоимость в дивинах (поле `primaryValue`, как в `TradeQuery.lua:160-163`); кэш в памяти на 1 час на лигу.

- [ ] **Step 1: Failing-тест** — канонированный JSON лиг (`[{"id":"Standard"},{"id":"SSF Standard"},{"id":"Rise of the Abyssal"}]`) и курса (минимальный фрагмент poe.ninja-ответа с двумя валютами: сверить точную форму с парсером в `TradeQuery.lua:126-170` — там `json_data` итерируется по списку валют с полями `id`/`primaryValue`; повторить эту форму). Ассерты: SSF отфильтрован; `rates["exalted"] > 0`; повторный вызов не делает второй HTTP-запрос (кэш).
- [ ] **Step 2: Реализовать `TraderWebApi`** (обычный `HttpClient` поверх переданного handler, `System.Text.Json`).
- [ ] **Step 3: Прогнать** → PASS.
- [ ] **Step 4: Commit** — `feat(trader): leagues + poe.ninja currency rates client`.

---

### Task 7: OAuth PKCE (PoeOAuthService)

**Files:**
- Create: `PBLApp.Core/Trader/PoeOAuthService.cs`
- Test: `PBLEngine.Tests/PoeOAuthTests.cs`

**Interfaces:**
- Consumes: `AppPreferences` (`PBLApp.Core/AppPreferences.cs`: `Get/Set(string,string)`), `LuaHost` (инжект токена).
- Produces:
```csharp
public sealed class PoeOAuthService(LuaHost host, HttpMessageHandler? handler = null)
{
    public bool IsLoggedIn { get; }               // access-токен есть и не истёк, или есть refresh
    public string? AccountName { get; }           // из prefs
    public Task<bool> LoginAsync(Action<string> openBrowser, CancellationToken ct); // полный PKCE-флоу
    public void Logout();                          // чистит prefs + main.api:ResetDetails-эквивалент
    public void InjectIntoLua();                   // main.lastToken/... + main.api.authToken
    // статика, тестируемая без сети:
    public static (string Verifier, string Challenge) CreatePkcePair();
    public static string BuildAuthorizeUrl(string state, string challenge);
}
```
- Ключи prefs: `TraderAccessToken`, `TraderRefreshToken`, `TraderTokenExpiry` (unix-секунды строкой), `TraderAccountName`.

- [ ] **Step 1: Failing-тесты (без сети)**

```csharp
public class PoeOAuthTests
{
    [Fact]
    public void CreatePkcePair_ChallengeIsBase64UrlSha256OfVerifier()
    {
        var (verifier, challenge) = PoeOAuthService.CreatePkcePair();
        using var sha = System.Security.Cryptography.SHA256.Create();
        var expected = Convert.ToBase64String(sha.ComputeHash(Encoding.ASCII.GetBytes(verifier)))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        Assert.Equal(expected, challenge);
        Assert.DoesNotContain('+', verifier); Assert.DoesNotContain('=', verifier);
    }

    [Fact]
    public void BuildAuthorizeUrl_HasPobClientIdAndScopes()
    {
        var url = PoeOAuthService.BuildAuthorizeUrl("aabbccdd11223344", "CHLG");
        Assert.StartsWith("https://www.pathofexile.com/oauth/authorize?", url);
        Assert.Contains("client_id=pob", url);
        Assert.Contains("response_type=code", url);
        Assert.Contains("code_challenge=CHLG", url);
        Assert.Contains("code_challenge_method=S256", url);
        Assert.Contains("account%3Atrade", url); // scope URL-encoded
    }
}
```

Run: `dotnet test PBLEngine.Tests/PBLEngine.Tests.csproj --filter "FullyQualifiedName~PoeOAuth"` → FAIL.

- [ ] **Step 2: Реализовать сервис**

Ключевые точки (зеркалим `src/Classes/PoEAPI.lua:81-139`):
- `CreatePkcePair`: 32 случайных байта → base64url (verifier); challenge = base64url(SHA256(verifier)).
- `BuildAuthorizeUrl`: `https://www.pathofexile.com/oauth/authorize?client_id=pob&response_type=code&scope=account:profile%20account:leagues%20account:characters%20account:trade&state={state}&code_challenge={challenge}&code_challenge_method=S256` (redirect_uri в authorize НЕ передаётся — как в оригинале).
- `LoginAsync`: `HttpListener` на `http://localhost:{свободный порт}/`; `openBrowser(authorizeUrl)`; ждать GET с `?code=&state=`; проверить state (mismatch → false); ответить страницей «Можно закрыть вкладку»; POST `https://www.pathofexile.com/oauth/token` form `client_id=pob&grant_type=authorization_code&code={code}&redirect_uri=http://localhost:{port}&scope=account:profile account:leagues account:characters account:trade&code_verifier={verifier}`; распарсить `access_token`,`refresh_token`,`expires_in`; сохранить в prefs; GET `https://api.pathofexile.com/profile` с Bearer → `name` → prefs; `InjectIntoLua()`.
- `InjectIntoLua`: под trader-семафором хоста (добавить в `LuaHostTrader.cs` метод `void SetTradeAuth(string? access, string? refresh, long expiry)` который делает `main.lastToken=...; main.lastRefreshToken=...; main.tokenExpiry=...; if main.api then main.api.authToken=...; main.api.refreshToken=...; main.api.tokenExpiry=... end`).
- Refresh при старте не нужен: Lua `ValidateAuth` сам обновит через `launch:DownloadPage` (мост из Task 1) при первом использовании.

- [ ] **Step 3: Прогнать** → PASS.
- [ ] **Step 4: Commit** — `feat(trader): PoE OAuth PKCE service (client_id=pob) with Lua token injection`.

---

### Task 8: TraderTabViewModel — строки слотов и конвейер поиска

**Files:**
- Create: `PBLApp.Core/TraderTabViewModel.cs` (в нём же `TraderSlotRowViewModel`, `TraderResultViewModel` — как соседние вкладки держат вложенные VM)
- Modify: `PBLEngine/LuaHostTrader.cs` (метод `TryOnListing`)
- Test: `PBLEngine.Tests/TraderTabViewModelTests.cs`

**Interfaces:**
- Consumes: `GenerateTradeQueryAsync`, `SearchTradeAsync`, `ComputeListingDiffAsync`, `TraderWebApi`, `PoeOAuthService`, `LuaHost.State` (`PBLTrader.GetSlotsJson()`), существующий `LuaHost.ImportItemFromText` (`PBLEngine/LuaHost.cs:1293`) и `EquipItemToSlot` (`:1321`).
- Produces (используется Task 9 и 10):

```csharp
public partial class TraderTabViewModel : ViewModelBase
{
    public TraderTabViewModel(LuaHost host, BuildModel build, Action? onStatsChanged = null);
    public ObservableCollection<TraderSlotRowViewModel> Slots { get; }
    public ObservableCollection<string> Leagues { get; }
    [ObservableProperty] string _selectedLeague;
    public bool IsLoggedIn { get; }            // делегирует PoeOAuthService
    public string? AccountName { get; }
    public IAsyncRelayCommand LoginCommand { get; }
    public IRelayCommand LogoutCommand { get; }
    // Веса: пресеты выставляют пары множителей
    [ObservableProperty] double _dpsWeight;    // default 1.0
    [ObservableProperty] double _ehpWeight;    // default 0.5
    public IRelayCommand<string> ApplyPresetCommand { get; } // "dps"→(1.0,0.1) "ehp"→(0.1,1.0) "balance"→(1.0,0.5)
    [ObservableProperty] string? _maxPrice;    // пусто = без лимита
    [ObservableProperty] int _maxPriceCurrencyIndex; // индексы как currencyTable в TradeQueryGenerator.lua:772-784
    public string StatWeightsJson { get; }     // собирает [{"stat":"FullDPS",...},{"stat":"TotalEHP",...}]
    public string OptionsJson { get; }         // + maxPrice/maxPriceType/includeCorrupted=true/includeMirrored=false
    public Func<string, Task>? CopyToClipboardAsync { get; set; }  // View подключает (паттерн PromptRenameAsync)
    public double? RateFor(string currencyId); // курс валюты в дивинах из кэша TraderWebApi, null если не загружен
    [ObservableProperty] double _totalTryOnDivs; // сумма DivValue всех примеренных результатов (шапка)
    public void RefreshSlots();                // перечитать GetSlotsJson
}

public partial class TraderSlotRowViewModel : ViewModelBase
{
    public string SlotName { get; }        // ключ для Lua
    public string DisplayName { get; }     // локализованное имя слота
    [ObservableProperty] string _currentItemName;
    [ObservableProperty] string _status;   // "", Generating…, Searching…, Diffing k/n, RateLimit N s, error text
    [ObservableProperty] bool _isBusy;
    public ObservableCollection<TraderResultViewModel> Results { get; }
    public IAsyncRelayCommand SearchCommand { get; }   // generate → search → diffs, отменяемо
    public IRelayCommand CancelCommand { get; }
    public IRelayCommand OpenOnSiteCommand { get; }    // после generate доступно и без логина
    [ObservableProperty] string? _lastQueryJson;
}

public partial class TraderResultViewModel : ViewModelBase
{
    public TraderListing Listing { get; }
    [ObservableProperty] double? _dpsDiff;     // null = ещё считается
    [ObservableProperty] double? _ehpDiff;
    [ObservableProperty] double? _statValue;
    public double? DivValue { get; }            // Amount * курс валюты (Task 6), null если курса нет
    public double? ValuePerDiv { get; }         // StatValue / DivValue
    public IAsyncRelayCommand TryOnCommand { get; }   // после успеха: IsTriedOn=true, владелец пересчитывает TotalTryOnDivs
    [ObservableProperty] bool _isTriedOn;
    public IAsyncRelayCommand CopyWhisperCommand { get; }
}
```

- [ ] **Step 1: Добавить `LuaHost.TryOnListing` + failing-тест**

`PBLEngine/LuaHostTrader.cs`:
```csharp
    /// <summary>Импортирует itemText в пул и экипирует в слот. Возвращает false при ошибке парсинга.</summary>
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
                if not d then return false end
                build.itemsTab:AddDisplayItem(true)
                local newId = d.id
                local slot = build.itemsTab.slots[_pblSlotName]
                if slot and newId then
                    slot:SetSelItemId(newId)
                    build.itemsTab:PopulateSlots()
                    build.itemsTab:AddUndoState()
                end
                return true");
            TriggerRecalc();
            return r is { Length: > 0 } && r[0] is bool b && b;
        }
        finally { _traderLua.Release(); }
    }
```
(Точные вызовы `SetSelItemId`/`PopulateSlots` сверить с тем, как это делает `EquipItemToSlot` в `LuaHost.cs:1321-1338`, и переиспользовать его код, передав id только что добавленного предмета.)

Тест `TraderTabViewModelTests.cs`:
```csharp
    [Fact(Timeout = 60_000)]
    public async Task TryOnListing_EquipsItemAndChangesLife()
    {
        var before = _host.GetStat("Life");
        var ok = await _host.TryOnListingAsync("Helmet",
            "Rarity: RARE\nDoom Crown\nAdvanced Warrior Greathelm\nItem Level: 81\nImplicits: 0\n+120 to maximum Life",
            CancellationToken.None);
        Assert.True(ok);
        Assert.True(Convert.ToDouble(_host.GetStat("Life")) > Convert.ToDouble(before));
    }
```
(сигнатуру `GetStat` сверить с существующей в LuaHost — она уже есть по CLAUDE.md.)

- [ ] **Step 2: Прогнать** → сначала FAIL, реализовать, PASS.

- [ ] **Step 3: Реализовать `TraderTabViewModel`**

Ключевой конвейер `SearchCommand` (в `TraderSlotRowViewModel`, все Lua-вызовы через методы хоста — они сами сериализуются):

```csharp
    private async Task SearchAsync()
    {
        _cts = new CancellationTokenSource();
        IsBusy = true; Results.Clear();
        try
        {
            Status = LocalizationService.Get("Trader_StatusGenerating");
            var q = await _host.GenerateTradeQueryAsync(SlotName, _owner.OptionsJson, null, _cts.Token);
            if (q.Error is not null) { Status = q.Error; return; }
            LastQueryJson = q.QueryJson;

            if (!_owner.IsLoggedIn) { Status = LocalizationService.Get("Trader_NeedLogin"); return; }

            Status = LocalizationService.Get("Trader_StatusSearching");
            var search = await _host.SearchTradeAsync(_owner.SelectedLeague, q.QueryJson!, _cts.Token);
            if (search.Error is not null) { Status = search.Error; return; }
            foreach (var l in search.Listings)
                Results.Add(new TraderResultViewModel(this, l, _owner.RateFor(l.Currency)));

            for (var i = 0; i < Results.Count; i++)
            {
                _cts.Token.ThrowIfCancellationRequested();
                Status = string.Format(LocalizationService.Get("Trader_StatusDiffing"), i + 1, Results.Count);
                var diff = await _host.ComputeListingDiffAsync(
                    SlotName, Results[i].Listing.ItemText, _owner.StatWeightsJson, _cts.Token);
                Results[i].ApplyDiff(diff);
            }
            SortResultsByValuePerDiv();
            Status = "";
        }
        catch (OperationCanceledException) { Status = LocalizationService.Get("Trader_Cancelled"); }
        finally { IsBusy = false; }
    }
```

`OpenOnSiteCommand` (формат — `TradeQuery.lua:1244`):
```csharp
    var url = $"https://www.pathofexile.com/trade2/search/{Uri.EscapeDataString(_owner.SelectedLeague)}?q={Uri.EscapeDataString(LastQueryJson!)}";
    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
```

Лиги грузятся fire-and-forget в конструкторе (`TraderWebApi.GetLeaguesAsync` + `GetCurrencyRatesAsync` при смене лиги); ошибки сети — в свойство `LeagueLoadError`, не исключения.

- [ ] **Step 4: Лёгкие VM-тесты** — конструктор на фикстуре наполняет `Slots` (18 или меньше — по фактическим слотам), `ApplyPresetCommand("ehp")` меняет веса, `OptionsJson` содержит `"statWeights"`. PASS.

- [ ] **Step 5: Commit** — `feat(trader): TraderTabViewModel with per-slot search pipeline and try-on`.

---

### Task 9: Вкладка в UI — View, регистрация, локализация

**Files:**
- Modify: `PBLApp.Core/BuildPageViewModel.cs` (TabKeys, IsTraderTab, CurrentTabContent, pop-out, создание в LoadAsync ~строка 164)
- Create: `PBLApp/Views/TraderTabView.axaml` + `TraderTabView.axaml.cs`
- Modify: `PBLApp/Views/BuildPageView.axaml` (кнопка вкладки в tab strip — по образцу существующих RadioButton)
- Modify: `PBLApp.Core/Localization/Strings.resx`, `Strings.ru.resx`

**Interfaces:**
- Consumes: Task 8.
- Produces: рабочая вкладка в приложении; ключи локализации `Tab_Trader`, `Trader_*`.

- [ ] **Step 1: BuildPageViewModel** — точечные правки:
  - `TabKeys = ["Items", "Tree", "Skills", "Calcs", "Config", "Trader"]` (`:35-36`);
  - `[NotifyPropertyChangedFor(nameof(IsTraderTab))]` к `_selectedTabIndex` (`:25-33`);
  - `public bool IsTraderTab { get => SelectedTabIndex == 5; set { if (value) SelectedTabIndex = 5; } }` (после `:48`);
  - `5 => TraderTab` в `CurrentTabContent` (`:51-59`);
  - `_isTraderPoppedOut` + ветки в `IsTabPoppedOut`/`SetTabPoppedOut` (`:63-86`);
  - `public TraderTabViewModel? TraderTab { get; private set; }` (`:102-109`);
  - в `LoadAsync` после создания `ItemsTab` (`:164`):
    `TraderTab = new TraderTabViewModel(host, model, onStatsChanged: () => { CalcsTab.Refresh(); ItemsTab?.Refresh(); });`
    и `OnPropertyChanged(nameof(TraderTab));` рядом с остальными (`:176-180`).
- [ ] **Step 2: Кнопка вкладки** — открыть `PBLApp/Views/BuildPageView.axaml`, найти tab-strip RadioButton'ы (`IsChecked="{Binding IsCalcsTab...}"`), продублировать для `IsTraderTab` с `Content="{loc:Tr Tab_Trader}"` и той же видимостью по pop-out.
- [ ] **Step 3: `TraderTabView.axaml`** — layout по спеку и хаус-стилю (памятка `pblapp-ui-style-conventions`): шапка (ComboBox лиги h36 BgSurface/BorderSubtle radius6, кнопка логина/имя аккаунта), панель весов (3 RadioButton-пресета + два NumericUpDown, Max Price EditBox + ComboBox валюты), ItemsControl строк слотов; в строке — Expander с DataGrid/ItemsControl результатов (колонки: цена, Δ DPS, Δ EHP, ценность/див, продавец, кнопки Примерить/Whisper/Сайт). В code-behind подключить `CopyToClipboardAsync = text => TopLevel.GetTopLevel(this)!.Clipboard!.SetTextAsync(text)` (паттерн DataContextChanged как в `BuildPageView.axaml.cs:26-30`).
- [ ] **Step 3a: Тултип результата** — на строке результата `ToolTip.Tip` со стилизованным списком строк `Listing.ItemText` (моноширинно, разделитель после имени). Перевод модов — тем же C#-путём, каким ItemsTab переводит строки тултипа (см. `GameTranslationService`/TooltipLine-фолбэк в `PBLApp.Core`; если он статический метод «перевести строку мода» — применить к каждой строке ItemText). Если перевод построчно неприменим без Item-объекта — оставить EN-текст (best-effort, не блокер).
- [ ] **Step 3b: Шапка** — показать `TotalTryOnDivs` («Примерено на N div», ключ `Trader_TotalTryOn`: EN "Tried on: {0} div" / RU "Примерено на {0} див").
- [ ] **Step 4: Локализация** — добавить в оба resx: `Tab_Trader` (EN "Trader" / RU "Трейдер"), `Trader_Login` ("Sign in via pathofexile.com"/"Войти через pathofexile.com"), `Trader_Logout` ("Sign out"/"Выйти"), `Trader_FindUpgrades` ("Find upgrades"/"Найти апгрейды"), `Trader_Cancel` ("Cancel"/"Отмена"), `Trader_Cancelled` ("Cancelled"/"Отменено"), `Trader_TryOn` ("Try on"/"Примерить"), `Trader_Whisper` ("Whisper"/"Whisper"), `Trader_OpenSite` ("Open on trade site"/"Открыть на trade-сайте"), `Trader_NeedLogin` ("Sign in to see results in-app"/"Войдите, чтобы видеть результаты в приложении"), `Trader_StatusGenerating` ("Weighing mods…"/"Взвешиваем моды…"), `Trader_StatusSearching` ("Searching…"/"Ищем…"), `Trader_StatusDiffing` ("Comparing {0}/{1}…"/"Сравниваем {0}/{1}…"), `Trader_StatusRateLimit` ("Rate limited, wait {0}s"/"Лимит запросов, ждём {0} с"), `Trader_PresetDps` ("DPS"/"Урон"), `Trader_PresetEhp` ("Survival"/"Выживаемость"), `Trader_PresetBalance` ("Balanced"/"Баланс"), `Trader_MaxPrice` ("Max price"/"Макс. цена"), `Trader_ColPrice` ("Price"/"Цена"), `Trader_ColDps` ("Δ DPS"/"Δ Урон"), `Trader_ColEhp` ("Δ EHP"/"Δ EHP"), `Trader_ColValue` ("Value/div"/"Ценность/див"), `Trader_ColSeller` ("Seller"/"Продавец").
- [ ] **Step 5: Сборка** — Run: `dotnet build PBLHost/PBLHost.sln`. Expected: 0 errors.
- [ ] **Step 6: Commit** — `feat(trader): Trader tab UI (view, tab registration, RU/EN strings)`.

---

### Task 10: IPC-эндпоинты и MCP-инструменты

**Files:**
- Modify: `PBLApp/Ipc/IpcServer.cs` (switch `:124-184` + обработчики)
- Modify: `PBLMcp/VisualTools.cs`

**Interfaces:**
- Consumes: Task 9 (`BuildPageViewModel.TraderTab`).
- Produces: MCP-инструменты `visual_trader_state`, `visual_trader_search`, `visual_trader_set_league`. (`visual_select_tab "Trader"` заработал автоматически через TabKeys.)

- [ ] **Step 1: IpcServer** — в switch:
```csharp
"/trader/state"      => await OnUi(TraderState),
"/trader/search"     => await OnUi(() => TraderSearch(body)),
"/trader/set-league" => await OnUi(() => TraderSetLeague(body)),
```
Обработчики (по образцу `SelectTab` `:271-293`):
```csharp
private static object TraderState()
{
    if ((GetMainVm()?.CurrentPage as BuildPageViewModel)?.TraderTab is not { } t)
        return new { error = "TraderTab not ready." };
    return new
    {
        ok = true,
        league = t.SelectedLeague,
        leagues = t.Leagues.ToArray(),
        loggedIn = t.IsLoggedIn,
        account = t.AccountName,
        slots = t.Slots.Select(s => new
        {
            slot = s.SlotName, item = s.CurrentItemName, status = s.Status, busy = s.IsBusy,
            results = s.Results.Select(r => new
            {
                price = r.Listing.Amount, currency = r.Listing.Currency,
                seller = r.Listing.Seller, dps = r.DpsDiff, ehp = r.EhpDiff, value = r.StatValue,
            }).ToArray(),
        }).ToArray(),
    };
}

private static object TraderSearch(string body)
{
    var req = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(body) ?? new();
    if ((GetMainVm()?.CurrentPage as BuildPageViewModel)?.TraderTab is not { } t)
        return new { error = "TraderTab not ready." };
    var slotName = req.TryGetValue("slot", out var s) ? s.GetString() : null;
    var row = t.Slots.FirstOrDefault(r => r.SlotName.Equals(slotName, StringComparison.OrdinalIgnoreCase));
    if (row is null) return new { error = $"Unknown slot '{slotName}'." };
    row.SearchCommand.Execute(null); // fire-and-forget; прогресс виден через /trader/state
    return new { ok = true, started = slotName };
}

private static object TraderSetLeague(string body)
{
    var req = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(body) ?? new();
    if ((GetMainVm()?.CurrentPage as BuildPageViewModel)?.TraderTab is not { } t)
        return new { error = "TraderTab not ready." };
    if (req.TryGetValue("league", out var l) && l.GetString() is { Length: > 0 } league)
    { t.SelectedLeague = league; return new { ok = true, league }; }
    return new { error = "Provide 'league'." };
}
```
- [ ] **Step 2: VisualTools** — три метода по шаблону:
```csharp
[McpServerTool]
[Description("Get Trader tab state: league, login, per-slot search status and results.")]
public async Task<string> VisualTraderState()
{
    try { return await IpcClient.CallAsync("GET", "/trader/state"); }
    catch (Exception ex) { return Error(ex); }
}

[McpServerTool]
[Description("Start upgrade search for an equipment slot on the Trader tab (async; poll visual_trader_state).")]
public async Task<string> VisualTraderSearch(string slot)
{
    try { return await IpcClient.CallAsync("POST", "/trader/search", new { slot }); }
    catch (Exception ex) { return Error(ex); }
}

[McpServerTool]
[Description("Select trade league on the Trader tab.")]
public async Task<string> VisualTraderSetLeague(string league)
{
    try { return await IpcClient.CallAsync("POST", "/trader/set-league", new { league }); }
    catch (Exception ex) { return Error(ex); }
}
```
- [ ] **Step 3: Сборка** — `dotnet build PBLHost/PBLHost.sln` (PBLMcp пересобрать обязательно — `.mcp.json` использует `--no-build`). Expected: 0 errors.
- [ ] **Step 4: Commit** — `feat(trader): IPC endpoints + MCP visual_trader_* tools`.

---

### Task 11: Финальная верификация и документация

**Files:**
- Modify: `AVALONIA_MIGRATION_PLAN.md` (отметить фичу в списке фаз/backlog)

- [ ] **Step 1: Полный прогон тестов** — Run: `dotnet test PBLEngine.Tests/PBLEngine.Tests.csproj`. Expected: все PASS (Slow-тесты можно исключить фильтром, но прогнать хотя бы раз).
- [ ] **Step 2: `/pbl-verify`** — пересборка, запуск клиента с IPC, открыть тестовый билд, `visual_select_tab Trader`, скриншот: шапка (лига, кнопка входа), панель весов, строки слотов. Зелёная сборка недостаточна — смотреть скриншот.
- [ ] **Step 3: Живой smoke (вручную, по желанию пользователя)** — логин через pathofexile.com, поиск по одному слоту, примерка результата. Это единственный шаг с реальной сетью.
- [ ] **Step 4: Обновить `AVALONIA_MIGRATION_PLAN.md`** — добавить строку о Phase «Trader tab» (что сделано, известные ограничения: jewel-слоты и поиск похожих вне скоупа).
- [ ] **Step 5: Commit** — `docs(trader): mark Trader tab phase in migration plan`.

---

## Порядок и зависимости

```
Task 1 (HTTP-мост) ──► Task 2 (glue) ──► Task 3 (generate) ──► Task 8 (VM) ──► Task 9 (UI) ──► Task 10 (MCP) ──► Task 11
                          │                                        ▲
                          ├──► Task 4 (search/fetch) ──────────────┤
                          ├──► Task 5 (diff) ──────────────────────┤
Task 6 (leagues/ninja) ────────────────────────────────────────────┤
Task 7 (OAuth) ────────────────────────────────────────────────────┘
```
Tasks 4–7 независимы между собой (после своих предпосылок) — можно выполнять в любом порядке.
