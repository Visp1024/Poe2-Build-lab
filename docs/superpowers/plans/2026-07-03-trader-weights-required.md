# Trader Stat Weights + Required Stats Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Настраиваемый список весов статов (как «Adjust search weights» в PoB, персист в XML билда) и пер-слот required-мин-фильтры в запросе трейдера.

**Architecture:** Источник истины весов — существующий `build.itemsTab.tradeQuery.statSortSelectionList` (headless ItemsTab уже создаёт tradeQuery и сериализует список в узел `TradeSearchWeights` — `src/Classes/ItemsTab.lua:1056,1142-1158,1238-1256`); Lua-glue даёт get/set + обогащение transform из `data.powerStatList`. Required-фильтры — пост-обработка queryJson в Lua (`ApplyRequiredStats`), генератор не трогаем. Спек: `docs/superpowers/specs/2026-07-03-trader-weights-required-design.md`.

**Tech Stack:** как Phase 18 (NLua glue `PBLEngine/lua/trader.lua`, partial `LuaHostTrader.cs`, CommunityToolkit.Mvvm, Avalonia Flyout, xUnit).

## Global Constraints

- `src/Classes/*.lua`, `src/Modules/*.lua` НЕ менять.
- Все trader-обращения к Lua — через методы `LuaHostTrader.cs` под `_traderLua`-семафором.
- Строки UI — через `Strings.resx`/`Strings.ru.resx` (`{loc:Tr Key}`).
- Тесты: `dotnet test PBLEngine.Tests/PBLEngine.Tests.csproj --filter "..."` из корня; `LuaHostFixture` + `[Collection("LuaHost")]`.
- Коммит после каждой задачи, ветка `feat/trader-tab`.

**Ключевые контракты (справка):**
- `data.powerStatList` (`src/Modules/Data.lua:127`): entries `{stat, label, ignoreForItems?, transform?, ...}`; фильтр попапа оригинала: `not stat.ignoreForItems and stat.label ~= "Name"` (`TradeQuery.lua:635-647`).
- `build.itemsTab.tradeQuery.statSortSelectionList`: `[{stat, label, weightMult, transform?}]`; создаётся пустым при загрузке билда (`ItemsTab.lua:1056`), заполняется из XML c восстановлением label/transform из powerStatList (`:1142-1158`), сохраняется при `weightMult > 0` (`:1238-1256`).
- Категория слота: `LoadModule("Classes/TradeHelpers").getTradeCategory(slotName, item)` → `(queryStr, categoryLabel)`; мод доступен категории если `entry[categoryLabel] ~= nil` (`TradeQueryGenerator.lua:657`); `entry.tradeMod = {id, text, type}`; типы модов: `PBLTrader.generator.modData["Explicit"|"Implicit"|...]`.
- Формат запроса: `query.stats` — массив групп; weight-группа уже стоит `stats[1]`; required-группа: `{type="and", filters={{id=..., value={min=...}}}}` (value опускается, если min нет).

---

### Task 1: Lua-glue весов — доступные статы, get/set сохранённых, обогащение transform

**Files:**
- Modify: `PBLEngine/lua/trader.lua`
- Modify: `PBLEngine/LuaHostTrader.cs`
- Test: `PBLEngine.Tests/TraderWeightsTests.cs`

**Interfaces:**
- Produces (C#, используют Tasks 2 и 4):
  - `string GetTraderWeightStatsJson()` → `[{"stat":"FullDPS","label":"Full DPS"},...]` (все доступные статы).
  - `string GetTraderWeightsJson()` → сохранённые веса билда `[{"stat":..,"label":..,"weightMult":..}]`; если список пуст — дефолт `FullDPS 1.0 / TotalEHP 0.5` (и он же записывается в список).
  - `void SetTraderWeights(string weightsJson)` — заменяет `tradeQuery.statSortSelectionList` (label/transform восстанавливаются из powerStatList; записи с `weightMult <= 0` отбрасываются).
- Produces (Lua, используется внутри): `PBLTrader._enrichWeights(list)` — для каждого `{stat, weightMult}` подставляет `label`/`transform` из `data.powerStatList`.

- [ ] **Step 1: Failing-тест**

```csharp
using PBLEngine;
using Xunit;

namespace PBLEngine.Tests;

[Collection("LuaHost")]
public class TraderWeightsTests : IClassFixture<LuaHostFixture>
{
    private readonly LuaHost _host;

    public TraderWeightsTests(LuaHostFixture fixture)
    {
        _host = fixture.Host;
        _host.NewBuild();
    }

    [Fact]
    public void GetWeightStats_ContainsFullDpsAndEhp_ExcludesIgnored()
    {
        var json = _host.GetTraderWeightStatsJson();
        Assert.Contains("\"FullDPS\"", json);
        Assert.Contains("\"TotalEHP\"", json);
        // ignoreForItems-статы не предлагаются (Offence/Defence — Data.lua:128)
        Assert.DoesNotContain("Offence/Defence", json);
    }

    [Fact]
    public void GetWeights_EmptyBuild_ReturnsDefaultPair()
    {
        var json = _host.GetTraderWeightsJson();
        Assert.Contains("\"FullDPS\"", json);
        Assert.Contains("\"TotalEHP\"", json);
    }

    [Fact]
    public void SetWeights_RoundTripsThroughBuildXml()
    {
        _host.SetTraderWeights(
            """[{"stat":"FullDPS","weightMult":0.7},{"stat":"Life","weightMult":1.0}]""");
        var xml = _host.SaveBuildToXml();
        Assert.Contains("TradeSearchWeights", xml);
        Assert.Contains("Life", xml);

        _host.LoadBuildFromXml(xml, "roundtrip");
        var json = _host.GetTraderWeightsJson();
        Assert.Contains("\"Life\"", json);
        Assert.Contains("0.7", json);
    }

    [Fact]
    public void SetWeights_DropsZeroAndUnknownStats()
    {
        _host.SetTraderWeights(
            """[{"stat":"FullDPS","weightMult":0},{"stat":"NoSuchStat","weightMult":1.0},{"stat":"TotalEHP","weightMult":0.5}]""");
        var json = _host.GetTraderWeightsJson();
        Assert.DoesNotContain("\"FullDPS\"", json);
        Assert.DoesNotContain("NoSuchStat", json);
        Assert.Contains("\"TotalEHP\"", json);
    }
}
```

- [ ] **Step 2: Убедиться, что падает**

Run: `dotnet test PBLEngine.Tests/PBLEngine.Tests.csproj --filter "FullyQualifiedName~TraderWeights"`
Expected: FAIL — компиляция (нет методов).

- [ ] **Step 3: Lua-часть в `trader.lua`** (после `PBLTrader.Init`):

```lua
-- ── Веса статов ──────────────────────────────────────────────────────────────
-- Источник истины — build.itemsTab.tradeQuery.statSortSelectionList: его уже
-- сериализует ItemsTab (узел TradeSearchWeights, ItemsTab.lua:1142/1238),
-- т.е. персист в XML билда и совместимость с оригинальным PoB бесплатны.

local function findPowerStat(statKey)
	for _, entry in ipairs(data.powerStatList) do
		if entry.stat == statKey then return entry end
	end
end

function PBLTrader._enrichWeights(list)
	local out = {}
	for _, w in ipairs(list or {}) do
		local ps = w.stat and findPowerStat(w.stat)
		if ps and (tonumber(w.weightMult) or 0) > 0 then
			table.insert(out, {
				stat = ps.stat, label = ps.label, transform = ps.transform,
				weightMult = tonumber(w.weightMult),
			})
		end
	end
	return out
end

function PBLTrader.GetWeightStatsJson()
	local out = {}
	for _, stat in ipairs(data.powerStatList) do
		-- тот же фильтр, что попап оригинала (TradeQuery.lua:635-647)
		if not stat.ignoreForItems and stat.label ~= "Name" and stat.stat then
			table.insert(out, { stat = stat.stat, label = stat.label })
		end
	end
	return dkjson.encode(out)
end

function PBLTrader._weightsList()
	PBLTrader.Init()
	local tq = build.itemsTab.tradeQuery
	tq.statSortSelectionList = tq.statSortSelectionList or {}
	if #tq.statSortSelectionList == 0 then
		tq.statSortSelectionList = PBLTrader._enrichWeights({
			{ stat = "FullDPS", weightMult = 1.0 },
			{ stat = "TotalEHP", weightMult = 0.5 },
		})
	end
	return tq.statSortSelectionList
end

function PBLTrader.GetWeightsJson()
	local out = {}
	for _, w in ipairs(PBLTrader._weightsList()) do
		table.insert(out, { stat = w.stat, label = w.label, weightMult = w.weightMult })
	end
	return dkjson.encode(out)
end

function PBLTrader.SetWeightsJson(weightsJson)
	PBLTrader.Init()
	local list = dkjson.decode(weightsJson) or {}
	build.itemsTab.tradeQuery.statSortSelectionList = PBLTrader._enrichWeights(list)
end
```

- [ ] **Step 4: C#-обёртки в `LuaHostTrader.cs`** (рядом с `GetTraderSlotsJson`; тот же паттерн `_traderLua.Wait()`):

```csharp
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
            State["_pblWeights"] = null;
        }
        finally { _traderLua.Release(); }
    }
```

- [ ] **Step 5: Прогнать** — PASS (4 теста).
- [ ] **Step 6: Commit** — `feat(trader): weight stats list + saved weights via itemsTab.tradeQuery (XML round-trip)`.

---

### Task 2: Генерация/диффы используют сохранённые веса; VM-флайаут настройки весов

**Files:**
- Modify: `PBLEngine/lua/trader.lua` (`StartGenerate`, `ComputeDiffJson` — enrichment)
- Modify: `PBLApp.Core/TraderTabViewModel.cs`
- Modify: `PBLApp/Views/TraderTabView.axaml`
- Modify: `PBLApp.Core/Localization/Strings.resx`, `Strings.ru.resx`
- Test: `PBLEngine.Tests/TraderWeightsTests.cs` (дополнить)

**Interfaces:**
- Consumes: Task 1.
- Produces (используется Task 4 и View):

```csharp
public partial class TraderWeightEntryViewModel : ViewModelBase
{
    public string Stat { get; }
    public string Label { get; }        // локализовано через GameTranslationService.TCalcLabel
    [ObservableProperty] double _weightMult;   // 0 = выключен
    // изменение веса дёргает owner.OnWeightsChanged()
}

public partial class TraderTabViewModel  // дополнения
{
    public ObservableCollection<TraderWeightEntryViewModel> WeightStats { get; } // ВСЕ статы
    [ObservableProperty] string _weightSearch;          // фильтр флайаута
    public IEnumerable<TraderWeightEntryViewModel> FilteredWeightStats { get; }
    public int ActiveWeightCount { get; }
    public string WeightsButtonText { get; }            // "Настроить веса… (N)"
    public bool HasActiveWeights => ActiveWeightCount > 0;
    public IRelayCommand ResetWeightsCommand { get; }   // FullDPS=1.0, TotalEHP=0.5
    // StatWeightsJson / OptionsJson теперь собираются из WeightStats (weightMult>0)
    // ApplyPresetCommand выставляет веса в WeightStats (dps: FullDPS=1.0,TotalEHP=0.1;
    // ehp: FullDPS=0.1,TotalEHP=1.0; balance: 1.0/0.5), остальные статы — 0
    internal void OnWeightsChanged(); // push в Lua (SetTraderWeights) + PropertyChanged
}
```
- Lua: `StartGenerate` и `ComputeDiffJson` прогоняют декодированные `statWeights` через `PBLTrader._enrichWeights` (восстанавливает transform, которого нет в JSON).

- [ ] **Step 1: Дополнить `TraderWeightsTests.cs` failing-тестом на enrichment**

```csharp
    [Fact(Timeout = 300_000)]
    [Trait("Category", "Slow")]
    public async Task Generate_WithCustomWeights_ProducesQuery()
    {
        // Life — стат с прямым output-ключом; проверяем что кастомный список работает end-to-end
        var result = await _host.GenerateTradeQueryAsync("Helmet",
            """{"statWeights":[{"stat":"Life","weightMult":1.0}],"includeCorrupted":false,"includeMirrored":false}""",
            null, System.Threading.CancellationToken.None);
        Assert.Null(result.Error);
        Assert.Contains("\"type\":\"weight\"", result.QueryJson);
    }
```

Run: `dotnet test PBLEngine.Tests/PBLEngine.Tests.csproj --filter "FullyQualifiedName~Generate_WithCustomWeights"`
Expected: сейчас, вероятно, PASS и без enrichment (Life без transform) — тест фиксирует контракт; enrichment проверяется визуально статом с transform. Если PASS — продолжать (это регрессионный тест).

- [ ] **Step 2: Lua enrichment** — в `PBLTrader.StartGenerate` после `dkjson.decode(optionsJson)`:

```lua
	options.statWeights = PBLTrader._enrichWeights(options.statWeights or {})
	if #options.statWeights == 0 then return "error: no stat weights selected" end
```

и в `PBLTrader.ComputeDiffJson` после `local weights = dkjson.decode(statWeightsJson) or {}`:

```lua
	weights = PBLTrader._enrichWeights(weights)
```

- [ ] **Step 3: VM** — в `TraderTabViewModel`:
  - удалить `_dpsWeight`/`_ehpWeight`;
  - в конструкторе после `RefreshSlots()`: построить `WeightStats` из `host.GetTraderWeightStatsJson()` (все статы, weightMult=0), затем наложить `host.GetTraderWeightsJson()`;
  - `TraderWeightEntryViewModel` (в том же файле): `partial void OnWeightMultChanged(double value) => _owner.OnWeightsChanged();`
  - `OnWeightsChanged()`: `Host.SetTraderWeights(StatWeightsJson)` + `OnPropertyChanged` для `ActiveWeightCount/WeightsButtonText/HasActiveWeights/OptionsJson/StatWeightsJson`;
  - `StatWeightsJson` = `JsonSerializer.Serialize(WeightStats.Where(w => w.WeightMult > 0).Select(w => new { stat = w.Stat, weightMult = w.WeightMult }))`; `OptionsJson` — как раньше, но statWeights отсюда;
  - `ApplyPreset`: обнулить все, выставить пары как в Interfaces; затем `OnWeightsChanged()`;
  - `ResetWeightsCommand`: то же с FullDPS=1.0/TotalEHP=0.5;
  - `FilteredWeightStats`: фильтр по `WeightSearch` (Contains, OrdinalIgnoreCase, по Label и Stat), активные — первыми;
  - `Label` через `GameTranslationService.TCalcLabel(label)`;
  - в `TraderSlotRowViewModel.SearchAsync` в начало: `if (!_owner.HasActiveWeights) { Status = LocalizationService.Get("Trader_NoWeights"); return; }`.
- [ ] **Step 4: View** — в панели настроек заменить два NumericUpDown на:

```xml
<Button Content="{Binding WeightsButtonText}">
    <Button.Flyout>
        <Flyout Placement="BottomEdgeAlignedLeft">
            <StackPanel Width="420" Spacing="6">
                <TextBox Text="{Binding WeightSearch, Mode=TwoWay}"
                         Watermark="{loc:Tr Trader_WeightSearch}" />
                <ScrollViewer MaxHeight="420">
                    <ItemsControl ItemsSource="{Binding FilteredWeightStats}">
                        <ItemsControl.ItemTemplate>
                            <DataTemplate x:DataType="vm:TraderWeightEntryViewModel">
                                <Grid ColumnDefinitions="*,120" Margin="0,1">
                                    <TextBlock Grid.Column="0" Text="{Binding Label}"
                                               VerticalAlignment="Center"
                                               TextTrimming="CharacterEllipsis" />
                                    <NumericUpDown Grid.Column="1"
                                                   Value="{Binding WeightMult, Mode=TwoWay}"
                                                   Minimum="0" Maximum="1" Increment="0.05"
                                                   FormatString="0.0#" />
                                </Grid>
                            </DataTemplate>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>
                </ScrollViewer>
                <Button Content="{loc:Tr Trader_WeightReset}"
                        Command="{Binding ResetWeightsCommand}" HorizontalAlignment="Right" />
            </StackPanel>
        </Flyout>
    </Button.Flyout>
</Button>
```
  (пресеты-кнопки остаются слева от неё; `WeightsButtonText` уже содержит счётчик).
- [ ] **Step 5: Локализация** — добавить в оба resx: `Trader_Weights` (EN `Adjust weights… ({0})` / RU `Настроить веса… ({0})`), `Trader_WeightSearch` (EN `Search stat…` / RU `Поиск стата…`), `Trader_WeightReset` (EN `Reset` / RU `Сброс`), `Trader_NoWeights` (EN `Select at least one stat weight` / RU `Выберите хотя бы один стат в весах`).
- [ ] **Step 6: VM-тест** (в `TraderTabViewModelTests.cs`):

```csharp
    [Fact]
    public void WeightStats_LoadedAndPresetChangesOptionsJson()
    {
        var vm = new TraderTabViewModel(_host, new BuildModel(_host),
            webApi: new PBLApp.Core.Trader.TraderWebApi(new FakeHttpHandler()));
        Assert.NotEmpty(vm.WeightStats);
        vm.ApplyPresetCommand.Execute("ehp");
        Assert.Contains("TotalEHP", vm.StatWeightsJson);
        Assert.True(vm.HasActiveWeights);
        var full = vm.WeightStats.First(w => w.Stat == "FullDPS");
        Assert.Equal(0.1, full.WeightMult, 6);
    }
```
- [ ] **Step 7: Прогнать всё trader-связанное + `dotnet build PBLApp/PBLApp.csproj`** — PASS/0 errors.
- [ ] **Step 8: Commit** — `feat(trader): configurable stat weight list (PoB-style) with flyout UI`.

---

### Task 3: Lua-glue required-фильтров — статы категории слота + ApplyRequiredStats

**Files:**
- Modify: `PBLEngine/lua/trader.lua`
- Modify: `PBLEngine/LuaHostTrader.cs`
- Test: `PBLEngine.Tests/TraderRequiredTests.cs`

**Interfaces:**
- Produces (C#, использует Task 4):
  - `string GetTradeStatsForSlotJson(string slotName)` → `[{"id":"explicit.stat_...","text":"#% increased ..."}]`, отсортировано по text, без дублей; пустой массив для слотов без категории.
  - `Task<string?> ApplyRequiredStatsAsync(string queryJson, string requiredJson, CancellationToken ct)` → queryJson с добавленной and-группой; `requiredJson`: `[{"id":"...","min":75}]` (min опционален); null при ошибке декода.

- [ ] **Step 1: Failing-тест**

```csharp
using PBLEngine;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace PBLEngine.Tests;

[Collection("LuaHost")]
public class TraderRequiredTests : IClassFixture<LuaHostFixture>
{
    private readonly LuaHost _host;

    public TraderRequiredTests(LuaHostFixture fixture)
    {
        _host = fixture.Host;
        _host.NewBuild();
    }

    [Fact]
    public void TradeStatsForSlot_Helmet_NonEmptyExplicitIds()
    {
        var json = _host.GetTradeStatsForSlotJson("Helmet");
        Assert.Contains("explicit.stat_", json);
        Assert.Contains("\"text\"", json);
    }

    [Fact]
    public void TradeStatsForSlot_UnknownSlot_EmptyArray()
    {
        var json = _host.GetTradeStatsForSlotJson("No Such Slot");
        Assert.Equal("[]", json.Trim());
    }

    [Fact(Timeout = 30_000)]
    public async Task ApplyRequiredStats_AddsAndGroup_KeepsWeightGroup()
    {
        const string query =
            """{"query":{"status":{"option":"securable"},"stats":[{"type":"weight","value":{"min":100},"filters":[]}]},"sort":{"statgroup.0":"desc"}}""";
        var result = await _host.ApplyRequiredStatsAsync(query,
            """[{"id":"explicit.stat_111","min":75},{"id":"explicit.stat_222"}]""",
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.Contains("\"type\":\"weight\"", result);
        Assert.Contains("\"type\":\"and\"", result);
        Assert.Contains("explicit.stat_111", result);
        Assert.Contains("\"min\":75", result);
        Assert.Contains("explicit.stat_222", result);
    }
}
```

Run: `dotnet test PBLEngine.Tests/PBLEngine.Tests.csproj --filter "FullyQualifiedName~TraderRequired"` → FAIL.

- [ ] **Step 2: Lua-часть в `trader.lua`** (вверху файла уже есть `local dkjson`; добавить `local tradeHelpers = LoadModule("Classes/TradeHelpers")`):

```lua
-- ── Required-фильтры ─────────────────────────────────────────────────────────

function PBLTrader.GetTradeStatsForSlotJson(slotName)
	PBLTrader.Init()
	local slot = build.itemsTab.slots[slotName]
	if not slot then return "[]" end
	local existingItem = slot.selItemId and build.itemsTab.items[slot.selItemId]
	local _, itemCategory = tradeHelpers.getTradeCategory(slotName, existingItem)
	if not itemCategory then return "[]" end
	if itemCategory == "Jewel" then itemCategory = "AnyJewel" end

	local seen, out = {}, {}
	for _, modType in ipairs({ "Explicit", "Implicit" }) do
		for _, entry in pairs(PBLTrader.generator.modData[modType] or {}) do
			-- доступность категории — как в GenerateModWeights (TradeQueryGenerator.lua:657)
			if entry[itemCategory] ~= nil and not seen[entry.tradeMod.id] then
				seen[entry.tradeMod.id] = true
				table.insert(out, { id = entry.tradeMod.id, text = entry.tradeMod.text })
			end
		end
	end
	table.sort(out, function(a, b) return a.text < b.text end)
	return dkjson.encode(out)
end

function PBLTrader.ApplyRequiredStats(queryJson, requiredJson)
	local query = dkjson.decode(queryJson)
	local required = dkjson.decode(requiredJson)
	if not (query and query.query and required) then return nil end
	local filters = {}
	for _, r in ipairs(required) do
		if r.id then
			table.insert(filters, { id = r.id, value = r.min and { min = r.min } or nil })
		end
	end
	if #filters == 0 then return queryJson end
	query.query.stats = query.query.stats or {}
	table.insert(query.query.stats, { type = "and", filters = filters })
	return dkjson.encode(query)
end
```

- [ ] **Step 3: C#-обёртки в `LuaHostTrader.cs`**:

```csharp
    /// <summary>Trade-статы, доступные категории слота (для required-фильтров).</summary>
    public string GetTradeStatsForSlotJson(string slotName)
    {
        _traderLua.Wait();
        try
        {
            EnsureTraderInit();
            State["_pblSlotName"] = slotName;
            var r = (string)State.DoString("return PBLTrader.GetTradeStatsForSlotJson(_pblSlotName)")[0];
            State["_pblSlotName"] = null;
            return r;
        }
        finally { _traderLua.Release(); }
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
            State["_pblQuery"] = null;
            State["_pblRequired"] = null;
            return r is { Length: > 0 } ? r[0] as string : null;
        }
        finally { _traderLua.Release(); }
    }
```

- [ ] **Step 4: Прогнать** — PASS. Если `getTradeCategory` для "Flask 1"/"Charm 1" возвращает категорию без записей в modData — допустимо (пустой список, кнопка скроется).
- [ ] **Step 5: Commit** — `feat(trader): required stat filters glue (slot trade stats + and-group injection)`.

---

### Task 4: Required-фильтры в UI строки слота + врезка в конвейер поиска

**Files:**
- Modify: `PBLApp.Core/TraderTabViewModel.cs`
- Modify: `PBLApp/Views/TraderTabView.axaml`
- Modify: `PBLApp.Core/Localization/Strings.resx`, `Strings.ru.resx`
- Test: `PBLEngine.Tests/TraderTabViewModelTests.cs` (дополнить)

**Interfaces:**
- Consumes: Task 3 (`GetTradeStatsForSlotJson`, `ApplyRequiredStatsAsync`), Task 2 (`HasActiveWeights`).
- Produces:

```csharp
public partial class TraderRequiredFilterViewModel : ViewModelBase
{
    public string Id { get; }
    public string Text { get; }                 // локализация: GameTranslationService.TTooltipLine(text), фолбэк EN
    [ObservableProperty] private string _min;   // пусто = только наличие
    public IRelayCommand RemoveCommand { get; }
}

public partial class TraderSlotRowViewModel  // дополнения
{
    public ObservableCollection<TraderRequiredFilterViewModel> RequiredFilters { get; }
    public ObservableCollection<TraderAvailableStatViewModel> AvailableStats { get; } // лениво при открытии
    [ObservableProperty] private string _requiredSearch;
    public IEnumerable<TraderAvailableStatViewModel> FilteredAvailableStats { get; }
    public string RequiredButtonText { get; }   // "Фильтры (N)"
    public bool HasStatCategory { get; }        // false → кнопка скрыта
    public IRelayCommand LoadAvailableStatsCommand { get; }   // вызывается при открытии флайаута
    public IRelayCommand<TraderAvailableStatViewModel> AddRequiredCommand { get; }
    internal string RequiredJson { get; }       // [{"id":..,"min":75}] (min только если парсится)
}

public sealed class TraderAvailableStatViewModel(string id, string text) { public string Id; public string Text; }
```

- [ ] **Step 1: Failing VM-тест** (в `TraderTabViewModelTests.cs`):

```csharp
    [Fact]
    public void RequiredFilters_LoadAddAndBuildJson()
    {
        var vm = new TraderTabViewModel(_host, new BuildModel(_host),
            webApi: new PBLApp.Core.Trader.TraderWebApi(new FakeHttpHandler()));
        var helmet = vm.Slots.First(s => s.SlotName == "Helmet");
        Assert.True(helmet.HasStatCategory);

        helmet.LoadAvailableStatsCommand.Execute(null);
        Assert.NotEmpty(helmet.AvailableStats);

        var stat = helmet.AvailableStats[0];
        helmet.AddRequiredCommand.Execute(stat);
        helmet.RequiredFilters[0].Min = "75";

        Assert.Contains(stat.Id, helmet.RequiredJson);
        Assert.Contains("\"min\":75", helmet.RequiredJson);
    }
```

Run: `dotnet test PBLEngine.Tests/PBLEngine.Tests.csproj --filter "FullyQualifiedName~RequiredFilters_LoadAdd"` → FAIL.

- [ ] **Step 2: VM-реализация** — по Interfaces; ключевые точки:
  - `HasStatCategory`: лениво — при первом обращении `Host.GetTradeStatsForSlotJson(SlotName) != "[]"`? Нет — это Lua-вызов в геттере; вместо этого вычислить при создании строки один раз (в `RefreshSlots` дорого 18×; допустимо — вызов лёгкий, без calc). Если окажется медленно — кэшировать в статике по slotName.
  - `LoadAvailableStats`: парсит JSON от `GetTradeStatsForSlotJson`, `Text` = `GameTranslationService.TTooltipLine(raw)` c фолбэком raw (если вернулся пустой/равный ключу — оставить raw).
  - `AddRequired`: не добавлять дубль id; после добавления `OnPropertyChanged(nameof(RequiredButtonText))`.
  - `RequiredJson`: `min` включается только если `double.TryParse(Min, InvariantCulture)`.
  - В `SearchAsync` после генерации:

```csharp
            if (RequiredFilters.Count > 0)
            {
                var patched = await _owner.Host.ApplyRequiredStatsAsync(
                    q.QueryJson!, RequiredJson, _cts.Token);
                if (patched is not null) LastQueryJson = patched;
            }
```
  (и дальше использовать `LastQueryJson` вместо `q.QueryJson` при `SearchTradeAsync`).
- [ ] **Step 3: View** — в строке слота, рядом с «Найти апгрейды»:

```xml
<Button Content="{Binding RequiredButtonText}"
        IsVisible="{Binding HasStatCategory}" Margin="0,0,6,0">
    <Button.Flyout>
        <Flyout Placement="BottomEdgeAlignedRight">
            <StackPanel Width="460" Spacing="6">
                <ItemsControl ItemsSource="{Binding RequiredFilters}">
                    <ItemsControl.ItemTemplate>
                        <DataTemplate x:DataType="vm:TraderRequiredFilterViewModel">
                            <Grid ColumnDefinitions="*,90,Auto" Margin="0,1">
                                <TextBlock Grid.Column="0" Text="{Binding Text}"
                                           VerticalAlignment="Center" TextTrimming="CharacterEllipsis" />
                                <TextBox Grid.Column="1" Text="{Binding Min, Mode=TwoWay}"
                                         Watermark="{loc:Tr Trader_ReqMin}" Margin="6,0" />
                                <Button Grid.Column="2" Content="✕" Command="{Binding RemoveCommand}" />
                            </Grid>
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>
                <TextBox Text="{Binding RequiredSearch, Mode=TwoWay}"
                         Watermark="{loc:Tr Trader_ReqSearch}" />
                <ScrollViewer MaxHeight="300">
                    <ItemsControl ItemsSource="{Binding FilteredAvailableStats}">
                        <ItemsControl.ItemTemplate>
                            <DataTemplate x:DataType="vm:TraderAvailableStatViewModel">
                                <Button Content="{Binding Text}" HorizontalAlignment="Stretch"
                                        HorizontalContentAlignment="Left"
                                        Command="{Binding $parent[ItemsControl].((vm:TraderSlotRowViewModel)DataContext).AddRequiredCommand}"
                                        CommandParameter="{Binding}" />
                            </DataTemplate>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>
                </ScrollViewer>
            </StackPanel>
        </Flyout>
    </Button.Flyout>
</Button>
```
  Плюс в code-behind/XAML: `Flyout.Opening` → `LoadAvailableStatsCommand` (в Avalonia — `Button.Flyout` события нет в XAML: вызвать `LoadAvailableStatsCommand` из `AddRequiredCommand`-независимого места — проще выполнить загрузку в первом открытии через attached-обработчик в code-behind `FlyoutBase.GetAttachedFlyout`… самый простой путь: грузить в `RefreshSlots` НЕ надо; грузить лениво в геттере `FilteredAvailableStats` при пустом `AvailableStats` и `HasStatCategory`).
- [ ] **Step 4: Локализация** — `Trader_Required` (EN `Filters ({0})` / RU `Фильтры ({0})`), `Trader_ReqSearch` (EN `Search trade stat…` / RU `Поиск стата…`), `Trader_ReqMin` (EN `min` / RU `мин`).
- [ ] **Step 5: Прогнать VM-тест + сборку** — PASS / 0 errors.
- [ ] **Step 6: Commit** — `feat(trader): per-slot required stat filters with flyout UI`.

---

### Task 5: Финальная верификация

- [ ] **Step 1:** `dotnet test PBLEngine.Tests/PBLEngine.Tests.csproj` — все PASS.
- [ ] **Step 2:** `/pbl-verify`: открыть билд → вкладка Трейдер → скриншот панели весов (флайаут открыть нельзя headless-инструментами — проверить кнопку и счётчик; при возможности добавить в `/trader/state` поля `activeWeights` и `requiredCount` для строк и проверить через `visual_trader_state`).
- [ ] **Step 3:** Живой поиск со своими весами + required-фильтром (вручную пользователем или через `visual_trader_search` + `visual_trader_state`).
- [ ] **Step 4:** Обновить `AVALONIA_MIGRATION_PLAN.md` (Phase 18: добавить строку о весах/required).
- [ ] **Step 5: Commit** — `docs(trader): note weights+required in migration plan`.

---

## Порядок

Task 1 → Task 2; Task 3 → Task 4; (1,2) и (3,4) независимы между собой; Task 5 последним.
