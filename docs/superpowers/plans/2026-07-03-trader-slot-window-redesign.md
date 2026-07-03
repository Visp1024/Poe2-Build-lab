# Trader Slot-Window Redesign — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Заменить отдельную вкладку «Трейдер» на пер-слот подбор: полоска «🔍 Подбор» внутри ячейки каждого слота предмета открывает единое перенацеливаемое немодальное окно подбора для этого слота.

**Architecture:** Единый `TraderTabViewModel` расщепляется на `TraderSession` (глобальное: логин, лига, курсы, веса, required-по-слотам) и `TraderWindowViewModel` (пер-слот: слот, результаты, поиск). Движковый/Lua/OAuth слой (`LuaHost.*`, `TraderWebApi`, `PoeOAuthService`) переиспользуется **без изменений**. Вкладка сносится; окно владеется `BuildPageView` (паттерн `OpenNotes_Click` — один инстанс + `Activate()`), перенацеливается через `Retarget(slot)`.

**Tech Stack:** C# .NET 9, Avalonia 12, CommunityToolkit.Mvvm, NLua (Lua 5.4), xUnit.

## Global Constraints

- **Не трогать** `src/Modules/*.lua`, `src/Classes/*.lua`, движковые калк-файлы, `PBLEngine/lua/trader.lua`, `LuaHostTrader.cs` — весь Lua/engine/OAuth слой переиспользуется как есть.
- Веса статов — **общие на билд**, персистятся движком в XML-узел `TradeSearchWeights` (совместимо с оригинальным PoB). Источник — `LuaHost.GetTraderWeights*Json` / `SetTraderWeights`.
- Окно — **единственный инстанс**, перенацеливается на новый слот (не создаётся второе).
- Окно — немодальное, `Show(owner)`, `WindowStartupLocation=CenterOwner`, фиксированный дефолтный размер, resizable. Позиция **не** персистится (как Notes/Settings — в приложении нет пер-оконного персиста).
- Среда: Windows/PowerShell; **нет .sln** — сборка по проектам. Перед сборкой `PBLApp` убивать процессы `PBLApp*`. Пересборка `PBLMcp` требует убить его процессы → MCP отваливается → пользователь делает `/mcp` reconnect.
- Тесты: `dotnet test PBLEngine.Tests --nologo` из корня репо.
- Сборка UI: `dotnet build PBLApp/PBLApp.csproj --nologo`. Сборка core: `dotnet build PBLApp.Core/PBLApp.Core.csproj --nologo`.
- Коммиты завершать трейлером: `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>` (через here-string `@'...'@`).
- UI-задачи финишировать `/pbl-verify` (скриншот обязателен, не только зелёная сборка).
- Итоговый визуал целится в утверждённый макет `scratchpad/trader-mockups.html` (house-style + трейд-акценты; точные токены Dark-темы из `PBLApp/Themes/Tokens.Colors.axaml`).

---

## File Structure

**Создаются:**
- `PBLApp.Core/TraderSession.cs` — глобальный VM трейдера (логин, лига, курсы, веса, required-по-слотам, кэш имён предметов слотов). Содержит `TraderWeightEntryViewModel`.
- `PBLApp.Core/TraderWindowViewModel.cs` — пер-слот VM окна (слот, результаты, поиск, required-UI). Содержит `TraderResultViewModel`, `TraderRequiredFilterViewModel`, `TraderAvailableStatViewModel`.
- `PBLApp/Views/TraderWindow.axaml` (+ `.axaml.cs`) — окно подбора.

**Удаляются:**
- `PBLApp.Core/TraderTabViewModel.cs` (логика уезжает в два новых файла).
- `PBLApp/Views/TraderTabView.axaml` (+ `.axaml.cs`).

**Модифицируются:**
- `PBLApp.Core/BuildPageViewModel.cs` — убрать вкладку Trader; добавить `TraderSession`, `EnsureTraderSession()`, `ActiveTraderWindow`, `RequestOpenTrader`.
- `PBLApp/Views/BuildPageView.axaml` — убрать RadioButton вкладки Trader.
- `PBLApp/Views/BuildPageView.axaml.cs` — `OpenTrader(slot)` (single-instance + retarget), проброс `RequestOpenTrader`.
- `PBLApp.Core/ItemsTabViewModel.cs` — `Action<string>? OpenTraderForSlot`.
- `PBLApp/Views/ItemsTabView.axaml` — полоска «Подбор» в ячейках слотов + стили.
- `PBLApp/Views/ItemsTabView.axaml.cs` — обработчик клика полоски → `OpenTraderForSlot`.
- `PBLApp/Ipc/IpcServer.cs` — `/trader/*` на `TraderSession` + `ActiveTraderWindow`; новый `/trader/open`.
- `PBLMcp/VisualTools.cs` — `visual_trader_open`; правка описаний.
- `PBLEngine.Tests/TraderTabViewModelTests.cs` — миграция 3 VM-тестов на новые VM.
- `PBLApp.Core/Localization/Strings.resx` + `Strings.ru.resx` — новые ключи, удалить `Tab_Trader`, `Trader_TotalTryOn`.
- `AVALONIA_MIGRATION_PLAN.md` — отметить редизайн.

---

## Task 1: Split VM — TraderSession + TraderWindowViewModel, retire the Trader tab

**Files:**
- Create: `PBLApp.Core/TraderSession.cs`
- Create: `PBLApp.Core/TraderWindowViewModel.cs`
- Delete: `PBLApp.Core/TraderTabViewModel.cs`
- Delete: `PBLApp/Views/TraderTabView.axaml`, `PBLApp/Views/TraderTabView.axaml.cs`
- Modify: `PBLApp.Core/BuildPageViewModel.cs`
- Modify: `PBLApp/Views/BuildPageView.axaml` (remove Trader RadioButton)
- Modify: `PBLApp/Ipc/IpcServer.cs:1403-1471`
- Modify: `PBLMcp/VisualTools.cs:624-650`
- Test: `PBLEngine.Tests/TraderTabViewModelTests.cs`

**Interfaces:**
- Consumes (unchanged engine API): `LuaHost.GetTraderSlotsJson()`, `GetTraderWeightStatsJson()`, `GetTraderWeightsJson()`, `SetTraderWeights(json)`, `GetTradeStatsForSlotJson(slot)`, `GenerateTradeQueryAsync(slot, optionsJson, null, ct)`, `ApplyRequiredStatsAsync(queryJson, requiredJson, ct)`, `SearchTradeAsync(league, queryJson, ct)`, `ComputeListingDiffAsync(slot, itemText, statWeightsJson, ct)`, `TryOnListingAsync(slot, itemText, ct)`. Services `TraderWebApi`, `PoeOAuthService`.
- Produces:
  - `TraderSession(LuaHost host, BuildModel build, Action? onStatsChanged = null, TraderWebApi? webApi = null, PoeOAuthService? oauth = null)` — global VM. Public: `ObservableCollection<string> Leagues`, `string SelectedLeague`, `string LeagueLoadError`, `bool IsLoggedIn`, `string? AccountName`, `IRelayCommand LoginCommand`, `IRelayCommand LogoutCommand`, `ObservableCollection<TraderWeightEntryViewModel> WeightStats`, `IEnumerable<TraderWeightEntryViewModel> FilteredWeightStats`, `string WeightSearch`, `int ActiveWeightCount`, `bool HasActiveWeights`, `string WeightsButtonText`, `IRelayCommand ApplyPresetCommand`, `IRelayCommand ResetWeightsCommand`, `string StatWeightsJson`, `IReadOnlyList<string> CurrencyNames`, `Func<string,Task>? CopyToClipboardAsync`, `double? RateFor(string)`, `IReadOnlyCollection<string> KnownSlots`, `string SlotItemName(string slot)`. Internal: `LuaHost Host`, `SemaphoreSlim SearchGate`, `Dictionary<string, ObservableCollection<TraderRequiredFilterViewModel>> RequiredBySlot`, `object[] ActiveWeightObjects()`, `void NotifyTryOn()`, static `string TranslateItemName(string)`, static `string LocalizeSlot(string)`, static `string CurrencyDisplay(string)`, static `void OpenInBrowser(string)`, `static IReadOnlyList<(string Name, string? Id)> Currencies`.
  - `TraderWindowViewModel(TraderSession session, string slotName)` — per-slot VM. Public: `TraderSession Session`, `string SlotName`, `string DisplayName`, `string CurrentItemName`, `string Status`, `bool IsBusy`, `string? LastQueryJson`, `bool HasStatCategory`, `string MaxPrice`, `int MaxPriceCurrencyIndex`, `ObservableCollection<TraderResultViewModel> Results`, `ObservableCollection<TraderRequiredFilterViewModel> RequiredFilters`, `ObservableCollection<TraderAvailableStatViewModel> AvailableStats`, `string RequiredSearch`, `IEnumerable<TraderAvailableStatViewModel> FilteredAvailableStats`, `string RequiredButtonText`, `IRelayCommand LoadAvailableStatsCommand`, `IRelayCommand<TraderAvailableStatViewModel> AddRequiredCommand`, `IAsyncRelayCommand SearchCommand`, `IRelayCommand CancelCommand`, `IRelayCommand OpenOnSiteCommand`, `void Retarget(string slotName)`, `string OptionsJson`. Internal: `string RequiredJson`, `void OnRequiredChanged()`.

- [ ] **Step 1: Migrate the VM test to the new types (write the failing test)**

Replace the three VM tests in `PBLEngine.Tests/TraderTabViewModelTests.cs` (keep the two `TryOnListing_*` tests that call `_host` directly). New body of the three tests — replace lines 42-92 with:

```csharp
    [Fact]
    public void Session_PopulatesWeights_AndPresetsChangeWeights()
    {
        var session = new TraderSession(_host, new BuildModel(_host),
            webApi: new PBLApp.Core.Trader.TraderWebApi(new FakeHttpHandler()));

        Assert.NotEmpty(session.WeightStats);
        Assert.Contains(session.KnownSlots, s => s == "Helmet");
        Assert.Contains(session.KnownSlots, s => s == "Body Armour");

        session.ApplyPresetCommand.Execute("ehp");
        var fullDps = session.WeightStats.First(w => w.Stat == "FullDPS");
        var totalEhp = session.WeightStats.First(w => w.Stat == "TotalEHP");
        Assert.Equal(0.1, fullDps.WeightMult, 6);
        Assert.Equal(1.0, totalEhp.WeightMult, 6);
        Assert.True(session.HasActiveWeights);
        Assert.Contains("FullDPS", session.StatWeightsJson);
    }

    [Fact]
    public void Window_BuildsOptionsJson_FromSessionWeightsAndMaxPrice()
    {
        var session = new TraderSession(_host, new BuildModel(_host),
            webApi: new PBLApp.Core.Trader.TraderWebApi(new FakeHttpHandler()));
        session.ApplyPresetCommand.Execute("dps");
        var win = new TraderWindowViewModel(session, "Helmet");

        Assert.Contains("\"statWeights\"", win.OptionsJson);
        Assert.Contains("FullDPS", win.OptionsJson);
    }

    [Fact]
    public void Window_RequiredFilters_LoadAddAndBuildJson_PersistPerSlotInSession()
    {
        var session = new TraderSession(_host, new BuildModel(_host),
            webApi: new PBLApp.Core.Trader.TraderWebApi(new FakeHttpHandler()));
        var win = new TraderWindowViewModel(session, "Helmet");
        Assert.True(win.HasStatCategory);

        win.LoadAvailableStatsCommand.Execute(null);
        Assert.NotEmpty(win.AvailableStats);

        var stat = win.AvailableStats[0];
        win.AddRequiredCommand.Execute(stat);
        win.RequiredFilters[0].Min = "75";

        Assert.Contains(stat.Id, win.RequiredJson);
        Assert.Contains("\"min\":75", win.RequiredJson);

        // персист в сессии: перенацелить на другой слот и обратно — фильтр на месте
        win.Retarget("Gloves");
        Assert.Empty(win.RequiredFilters);
        win.Retarget("Helmet");
        Assert.Single(win.RequiredFilters);
        Assert.Equal(stat.Id, win.RequiredFilters[0].Id);
    }
```

- [ ] **Step 2: Run the test to verify it fails to compile**

Run: `dotnet test PBLEngine.Tests --nologo`
Expected: FAIL — `TraderSession` / `TraderWindowViewModel` do not exist.

- [ ] **Step 3: Create `PBLApp.Core/TraderSession.cs`**

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PBLApp.Core;
using PBLApp.Core.Localization;
using PBLApp.Core.Trader;
using PBLEngine;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PBLApp.ViewModels;

/// <summary>
/// Глобальное состояние трейдера на билд: логин, лига, курсы валют, общие веса
/// статов (персист в XML), required-фильтры по слотам (сессия). Пер-слот поиск
/// живёт в <see cref="TraderWindowViewModel"/>, который ссылается на эту сессию.
/// Движковый/Lua/OAuth слой переиспользуется без изменений.
/// </summary>
public partial class TraderSession : ViewModelBase
{
    internal LuaHost Host { get; }
    private readonly Action? _onStatsChanged;
    private readonly TraderWebApi _webApi;
    private readonly PoeOAuthService _oauth;
    private IReadOnlyDictionary<string, double> _rates = new Dictionary<string, double>();
    private bool _suppressWeightPush;
    private readonly Dictionary<string, string> _slotItemNames = new();

    /// <summary>Одновременно идёт максимум один поиск (генератор — один на билд).</summary>
    internal SemaphoreSlim SearchGate { get; } = new(1, 1);

    /// <summary>Required-фильтры по слотам — живут на сессию (переоткрытие слота восстанавливает).</summary>
    internal Dictionary<string, ObservableCollection<TraderRequiredFilterViewModel>> RequiredBySlot { get; } = new();

    public ObservableCollection<string> Leagues { get; } = [];
    [ObservableProperty] private string _selectedLeague = "";
    [ObservableProperty] private string _leagueLoadError = "";

    // ── Веса статов (общие на билд) ──────────────────────────────────────────
    public ObservableCollection<TraderWeightEntryViewModel> WeightStats { get; } = [];
    [ObservableProperty] private string _weightSearch = "";

    public static readonly IReadOnlyList<(string Name, string? Id)> Currencies =
    [
        ("Exalted Orb Equivalent", null),
        ("Exalted Orb", "exalted"),
        ("Chaos Orb", "chaos"),
        ("Divine Orb", "divine"),
        ("Orb of Augmentation", "aug"),
        ("Orb of Transmutation", "transmute"),
        ("Regal Orb", "regal"),
        ("Vaal Orb", "vaal"),
        ("Orb of Annulment", "annul"),
        ("Orb of Alchemy", "alch"),
        ("Mirror of Kalandra", "mirror"),
    ];

    public IReadOnlyList<string> CurrencyNames { get; } = Currencies
        .Select(c => c.Id is null
            ? LocalizationService.Get("Trader_CurrencyEquiv")
            : GameTranslationService.TItem(c.Name))
        .ToList();

    internal static string CurrencyDisplay(string currencyId)
    {
        foreach (var (name, id) in Currencies)
            if (id == currencyId)
                return GameTranslationService.TItem(name);
        return currencyId;
    }

    /// <summary>Имя предмета через игровой перевод. Lua отдаёт составное «Title, BaseName» —
    /// переводим части отдельно (TItem не знает композитов).</summary>
    internal static string TranslateItemName(string name)
    {
        if (string.IsNullOrEmpty(name)) return "";
        var idx = name.IndexOf(", ", StringComparison.Ordinal);
        if (idx > 0)
        {
            var title = name[..idx];
            var baseName = name[(idx + 2)..];
            return GameTranslationService.TItem(title) + ", " + GameTranslationService.TItem(baseName);
        }
        return GameTranslationService.TItem(name);
    }

    internal static string LocalizeSlot(string slotName)
    {
        var loc = LocalizationService.Get("Slot_" + slotName.Replace(" ", ""));
        return loc.StartsWith('[') ? slotName : loc;
    }

    internal static void OpenInBrowser(string url) =>
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

    /// <summary>Подключается View'ом (паттерн PromptRenameAsync) — копирование whisper в буфер.</summary>
    public Func<string, Task>? CopyToClipboardAsync { get; set; }

    public bool IsLoggedIn => _oauth.IsLoggedIn;
    public string? AccountName => _oauth.AccountName;

    public IReadOnlyCollection<string> KnownSlots => _slotItemNames.Keys;

    public string SlotItemName(string slot) =>
        _slotItemNames.TryGetValue(slot, out var n) ? n : "";

    public IEnumerable<TraderWeightEntryViewModel> FilteredWeightStats
    {
        get
        {
            var search = WeightSearch?.Trim() ?? "";
            var all = WeightStats.OrderByDescending(w => w.WeightMult > 0).AsEnumerable();
            if (!string.IsNullOrEmpty(search))
                all = all.Where(w =>
                    w.Label.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    w.Stat.Contains(search, StringComparison.OrdinalIgnoreCase));
            return all;
        }
    }

    public int ActiveWeightCount => WeightStats.Count(w => w.WeightMult > 0);
    public string WeightsButtonText =>
        string.Format(LocalizationService.Get("Trader_Weights"), ActiveWeightCount);
    public bool HasActiveWeights => ActiveWeightCount > 0;

    public TraderSession(LuaHost host, BuildModel build, Action? onStatsChanged = null,
        TraderWebApi? webApi = null, PoeOAuthService? oauth = null)
    {
        Host = host;
        _onStatsChanged = onStatsChanged;
        _webApi = webApi ?? new TraderWebApi();
        _oauth = oauth ?? new PoeOAuthService(host);
        _oauth.InjectIntoLua();
        RefreshSlotItemNames();

        using var allDoc = JsonDocument.Parse(host.GetTraderWeightStatsJson());
        foreach (var el in allDoc.RootElement.EnumerateArray())
        {
            var stat = el.GetProperty("stat").GetString() ?? "";
            var label = el.TryGetProperty("label", out var lb) ? lb.GetString() ?? stat : stat;
            WeightStats.Add(new TraderWeightEntryViewModel(this, stat, label));
        }

        _suppressWeightPush = true;
        try
        {
            using var savedDoc = JsonDocument.Parse(host.GetTraderWeightsJson());
            foreach (var el in savedDoc.RootElement.EnumerateArray())
            {
                var stat = el.GetProperty("stat").GetString() ?? "";
                var mult = el.TryGetProperty("weightMult", out var wm) ? wm.GetDouble() : 0.0;
                var entry = WeightStats.FirstOrDefault(w => w.Stat == stat);
                if (entry is not null) entry.WeightMult = mult;
            }
        }
        finally { _suppressWeightPush = false; }

        _ = InitLeaguesAsync();
    }

    /// <summary>Перечитать имена предметов по слотам (для заголовка окна и Δ-диффа).</summary>
    public void RefreshSlotItemNames()
    {
        _slotItemNames.Clear();
        using var doc = JsonDocument.Parse(Host.GetTraderSlotsJson());
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return;
        foreach (var el in doc.RootElement.EnumerateArray())
        {
            var slotName = el.GetProperty("slotName").GetString() ?? "";
            var itemName = el.TryGetProperty("itemName", out var i) ? i.GetString() ?? "" : "";
            _slotItemNames[slotName] = TranslateItemName(itemName);
        }
    }

    // ── Веса ─────────────────────────────────────────────────────────────────

    [RelayCommand]
    private void ApplyPreset(string preset)
    {
        var (dps, ehp) = preset switch
        {
            "dps" => (1.0, 0.1),
            "ehp" => (0.1, 1.0),
            _ => (1.0, 0.5),
        };
        BatchSetWeights(() =>
        {
            foreach (var w in WeightStats) w.WeightMult = 0;
            SetWeight("FullDPS", dps);
            SetWeight("TotalEHP", ehp);
        });
    }

    [RelayCommand]
    private void ResetWeights()
    {
        BatchSetWeights(() =>
        {
            foreach (var w in WeightStats) w.WeightMult = 0;
            SetWeight("FullDPS", 1.0);
            SetWeight("TotalEHP", 0.5);
        });
    }

    private void SetWeight(string stat, double value)
    {
        var entry = WeightStats.FirstOrDefault(w => w.Stat == stat);
        if (entry is not null) entry.WeightMult = value;
    }

    internal void OnWeightsChanged()
    {
        if (_suppressWeightPush) return;
        Host.SetTraderWeights(StatWeightsJson);
        OnPropertyChanged(nameof(ActiveWeightCount));
        OnPropertyChanged(nameof(WeightsButtonText));
        OnPropertyChanged(nameof(HasActiveWeights));
        OnPropertyChanged(nameof(FilteredWeightStats));
        OnPropertyChanged(nameof(StatWeightsJson));
    }

    private void BatchSetWeights(Action apply)
    {
        _suppressWeightPush = true;
        try { apply(); }
        finally { _suppressWeightPush = false; }
        OnWeightsChanged();
    }

    partial void OnWeightSearchChanged(string value) =>
        OnPropertyChanged(nameof(FilteredWeightStats));

    internal object[] ActiveWeightObjects() =>
        WeightStats.Where(w => w.WeightMult > 0)
            .Select(w => (object)new { stat = w.Stat, weightMult = w.WeightMult })
            .ToArray();

    public string StatWeightsJson => JsonSerializer.Serialize(ActiveWeightObjects());

    // ── Лиги и курсы ─────────────────────────────────────────────────────────

    private async Task InitLeaguesAsync()
    {
        try
        {
            var leagues = await _webApi.GetLeaguesAsync();
            Leagues.Clear();
            foreach (var l in leagues) Leagues.Add(l);
            if (Leagues.Count > 0 && string.IsNullOrEmpty(SelectedLeague))
                SelectedLeague = Leagues[0];
            LeagueLoadError = "";
        }
        catch (Exception ex) { LeagueLoadError = ex.Message; }
    }

    partial void OnSelectedLeagueChanged(string value)
    {
        if (string.IsNullOrEmpty(value)) return;
        _ = LoadRatesAsync(value);
    }

    private async Task LoadRatesAsync(string league)
    {
        try { _rates = await _webApi.GetCurrencyRatesAsync(league); }
        catch { /* курсы — best-effort */ }
    }

    public double? RateFor(string currencyId) =>
        _rates.TryGetValue(currencyId, out var v) ? v : null;

    // ── Логин ────────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task LoginAsync()
    {
        await _oauth.LoginAsync(OpenInBrowser);
        OnPropertyChanged(nameof(IsLoggedIn));
        OnPropertyChanged(nameof(AccountName));
    }

    [RelayCommand]
    private void Logout()
    {
        _oauth.Logout();
        OnPropertyChanged(nameof(IsLoggedIn));
        OnPropertyChanged(nameof(AccountName));
    }

    // ── Примерка ─────────────────────────────────────────────────────────────

    internal void NotifyTryOn()
    {
        RefreshSlotItemNames();
        _onStatsChanged?.Invoke();
    }
}

/// <summary>Один стат в списке весов; изменение WeightMult уведомляет сессию.</summary>
public partial class TraderWeightEntryViewModel : ViewModelBase
{
    private readonly TraderSession _owner;

    public string Stat { get; }
    public string Label { get; }

    [ObservableProperty] private double _weightMult;

    public TraderWeightEntryViewModel(TraderSession owner, string stat, string rawLabel)
    {
        _owner = owner;
        Stat = stat;
        Label = GameTranslationService.TCalcLabel(rawLabel);
    }

    partial void OnWeightMultChanged(double value) => _owner.OnWeightsChanged();
}
```

- [ ] **Step 4: Create `PBLApp.Core/TraderWindowViewModel.cs`**

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PBLApp.Core.Localization;
using PBLEngine;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PBLApp.ViewModels;

/// <summary>
/// Пер-слот окно подбора: генерация взвешенного запроса → поиск+fetch → Δ-статы.
/// Глобальные вещи (логин, лига, веса, курсы) берутся из <see cref="Session"/>.
/// Перенацеливается на новый слот через <see cref="Retarget"/> (один инстанс окна).
/// </summary>
public partial class TraderWindowViewModel : ViewModelBase
{
    public TraderSession Session { get; }
    private CancellationTokenSource? _cts;
    private string _cachedStatsJson = "";

    [ObservableProperty] private string _slotName = "";
    [ObservableProperty] private string _displayName = "";
    [ObservableProperty] private string _currentItemName = "";
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _lastQueryJson;
    [ObservableProperty] private bool _hasStatCategory;
    [ObservableProperty] private string _requiredSearch = "";

    [ObservableProperty] private string _maxPrice = "";
    [ObservableProperty] private int _maxPriceCurrencyIndex;

    public ObservableCollection<TraderResultViewModel> Results { get; } = [];
    public ObservableCollection<TraderAvailableStatViewModel> AvailableStats { get; } = [];

    /// <summary>Required-фильтры активного слота — коллекция живёт в сессии (персист по слоту).</summary>
    public ObservableCollection<TraderRequiredFilterViewModel> RequiredFilters =>
        Session.RequiredBySlot.TryGetValue(SlotName, out var list) ? list : _emptyRequired;
    private static readonly ObservableCollection<TraderRequiredFilterViewModel> _emptyRequired = [];

    public string RequiredButtonText =>
        string.Format(LocalizationService.Get("Trader_Required"), RequiredFilters.Count);

    public IRelayCommand LoadAvailableStatsCommand { get; }
    public IRelayCommand<TraderAvailableStatViewModel> AddRequiredCommand { get; }

    public TraderWindowViewModel(TraderSession session, string slotName)
    {
        Session = session;
        LoadAvailableStatsCommand = new RelayCommand(LoadAvailableStats);
        AddRequiredCommand = new RelayCommand<TraderAvailableStatViewModel>(stat =>
        {
            if (stat is null || RequiredFilters.Any(f => f.Id == stat.Id)) return;
            RequiredFilters.Add(new TraderRequiredFilterViewModel(stat.Id, stat.Text, this));
            OnRequiredChanged();
        });
        Retarget(slotName);
    }

    /// <summary>Перенацелить окно на другой слот: сменить слот, очистить результаты,
    /// подтянуть required (из сессии) и доступные статы новой категории.</summary>
    public void Retarget(string slotName)
    {
        _cts?.Cancel();
        SlotName = slotName;
        DisplayName = TraderSession.LocalizeSlot(slotName);
        CurrentItemName = Session.SlotItemName(slotName);
        Status = "";
        LastQueryJson = null;
        Results.Clear();
        AvailableStats.Clear();
        RequiredSearch = "";
        IsBusy = false;

        _cachedStatsJson = Session.Host.GetTradeStatsForSlotJson(slotName);
        HasStatCategory = !IsEmptyStats(_cachedStatsJson);
        Session.RequiredBySlot.TryAdd(slotName, []);

        OpenOnSiteCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(RequiredFilters));
        OnPropertyChanged(nameof(RequiredButtonText));
        OnPropertyChanged(nameof(RequiredJson));
        OnPropertyChanged(nameof(FilteredAvailableStats));
    }

    private static bool IsEmptyStats(string json)
    {
        var t = json?.Trim() ?? "";
        return t is "[]" or "{}" || string.IsNullOrEmpty(t);
    }

    public IEnumerable<TraderAvailableStatViewModel> FilteredAvailableStats
    {
        get
        {
            if (AvailableStats.Count == 0 && HasStatCategory) LoadAvailableStats();
            var search = RequiredSearch?.Trim() ?? "";
            IEnumerable<TraderAvailableStatViewModel> all = AvailableStats;
            if (!string.IsNullOrEmpty(search))
                all = all.Where(s => s.Text.Contains(search, StringComparison.OrdinalIgnoreCase));
            return all;
        }
    }

    private void LoadAvailableStats()
    {
        if (AvailableStats.Count > 0 || string.IsNullOrEmpty(_cachedStatsJson)) return;
        try
        {
            using var doc = JsonDocument.Parse(_cachedStatsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return;
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                var id = el.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? "" : "";
                var rawText = el.TryGetProperty("text", out var textProp) ? textProp.GetString() ?? "" : "";
                var text = GameTranslationService.TTooltipLine(rawText);
                if (string.IsNullOrEmpty(text) || text.StartsWith('[')) text = rawText;
                AvailableStats.Add(new TraderAvailableStatViewModel(id, text));
            }
        }
        catch { /* malformed JSON — leave empty */ }
    }

    partial void OnRequiredSearchChanged(string value) =>
        OnPropertyChanged(nameof(FilteredAvailableStats));

    internal void OnRequiredChanged()
    {
        OnPropertyChanged(nameof(RequiredButtonText));
        OnPropertyChanged(nameof(RequiredJson));
    }

    internal string RequiredJson
    {
        get
        {
            var nodes = new System.Text.Json.Nodes.JsonArray();
            foreach (var f in RequiredFilters)
            {
                var obj = new System.Text.Json.Nodes.JsonObject { ["id"] = f.Id };
                if (double.TryParse(f.Min, NumberStyles.Float, CultureInfo.InvariantCulture, out var min))
                    obj["min"] = min == Math.Floor(min) ? (long)min : min;
                nodes.Add(obj);
            }
            return nodes.ToJsonString();
        }
    }

    /// <summary>Опции генерации: общие веса сессии + локальный лимит цены.</summary>
    public string OptionsJson
    {
        get
        {
            var opts = new Dictionary<string, object?>
            {
                ["statWeights"] = Session.ActiveWeightObjects(),
                ["includeCorrupted"] = true,
                ["includeMirrored"] = false,
            };
            if (double.TryParse(MaxPrice, NumberStyles.Float, CultureInfo.InvariantCulture, out var mp) && mp > 0)
            {
                opts["maxPrice"] = mp;
                var id = TraderSession.Currencies[Math.Clamp(MaxPriceCurrencyIndex, 0, TraderSession.Currencies.Count - 1)].Id;
                if (id is not null) opts["maxPriceType"] = id;
            }
            return JsonSerializer.Serialize(opts);
        }
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        if (string.IsNullOrEmpty(CurrentItemName))
        {
            Status = LocalizationService.Get("Trader_EmptySlot");
            return;
        }
        if (!Session.HasActiveWeights) { Status = LocalizationService.Get("Trader_NoWeights"); return; }
        if (IsBusy) return;
        _cts = new CancellationTokenSource();
        IsBusy = true;
        Results.Clear();
        await Session.SearchGate.WaitAsync();
        try
        {
            Status = LocalizationService.Get("Trader_StatusGenerating");
            var q = await Session.Host.GenerateTradeQueryAsync(SlotName, OptionsJson, null, _cts.Token);
            if (q.Error is not null) { Status = q.Error; return; }
            LastQueryJson = q.QueryJson;
            OpenOnSiteCommand.NotifyCanExecuteChanged();

            if (RequiredFilters.Count > 0)
            {
                var patched = await Session.Host.ApplyRequiredStatsAsync(q.QueryJson!, RequiredJson, _cts.Token);
                if (patched is not null) LastQueryJson = patched;
            }

            if (!Session.IsLoggedIn) { Status = LocalizationService.Get("Trader_NeedLogin"); return; }

            Status = LocalizationService.Get("Trader_StatusSearching");
            var search = await Session.Host.SearchTradeAsync(Session.SelectedLeague, LastQueryJson!, _cts.Token);
            if (search.Error is not null) { Status = search.Error; return; }
            foreach (var l in search.Listings)
                Results.Add(new TraderResultViewModel(this, Session, l));

            for (var i = 0; i < Results.Count; i++)
            {
                _cts.Token.ThrowIfCancellationRequested();
                Status = string.Format(LocalizationService.Get("Trader_StatusDiffing"), i + 1, Results.Count);
                var diff = await Session.Host.ComputeListingDiffAsync(
                    SlotName, Results[i].Listing.ItemText, Session.StatWeightsJson, _cts.Token);
                Results[i].ApplyDiff(diff);
            }
            SortResults();
            Status = Results.Count == 0 ? LocalizationService.Get("Trader_NoResults") : "";
        }
        catch (OperationCanceledException) { Status = LocalizationService.Get("Trader_Cancelled"); }
        catch (Exception ex) { Status = ex.Message; }
        finally
        {
            Session.SearchGate.Release();
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    private bool CanOpenOnSite() => !string.IsNullOrEmpty(LastQueryJson);

    [RelayCommand(CanExecute = nameof(CanOpenOnSite))]
    private void OpenOnSite()
    {
        var host = LocalizationService.Instance.CurrentLanguage == "ru"
            ? "https://ru.pathofexile.com"
            : "https://www.pathofexile.com";
        var url = $"{host}/trade2/search/{Uri.EscapeDataString(Session.SelectedLeague)}"
                + $"?q={Uri.EscapeDataString(LastQueryJson!)}";
        TraderSession.OpenInBrowser(url);
    }

    private void SortResults()
    {
        var sorted = Results
            .OrderByDescending(r => r.ValuePerDiv ?? double.MinValue)
            .ThenByDescending(r => r.StatValue ?? double.MinValue)
            .ToList();
        Results.Clear();
        foreach (var r in sorted) Results.Add(r);
    }
}

/// <summary>Один результат поиска: цена, Δ-статы, примерка, whisper.</summary>
public partial class TraderResultViewModel : ViewModelBase
{
    private readonly TraderWindowViewModel _win;
    private readonly TraderSession _session;

    public LuaHost.TraderListing Listing { get; }

    [ObservableProperty] private double? _dpsDiff;
    [ObservableProperty] private double? _ehpDiff;
    [ObservableProperty] private double? _statValue;
    [ObservableProperty] private bool _isTriedOn;

    /// <summary>Имя предмета для карточки — из ItemText (в record TraderListing имени нет,
    /// а движок не трогаем). Формат PoB: строка 0 = «Rarity: X», 1 = имя, 2 = база.</summary>
    public string ItemName
    {
        get
        {
            var lines = Listing.ItemText.Split('\n',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (lines.Length >= 2 && lines[0].StartsWith("Rarity:", StringComparison.OrdinalIgnoreCase))
            {
                var title = lines[1];
                var baseName = lines.Length >= 3 ? lines[2] : "";
                var composite = string.IsNullOrEmpty(baseName) ? title : $"{title}, {baseName}";
                return TraderSession.TranslateItemName(composite);
            }
            return lines.Length > 0 ? lines[0] : "";
        }
    }
    public string PriceText => $"{Listing.Amount:0.##} {TraderSession.CurrencyDisplay(Listing.Currency)}";

    public double? DivValue =>
        _session.RateFor(Listing.Currency) is { } rate ? Listing.Amount * rate : null;

    public double? ValuePerDiv =>
        StatValue is { } sv && DivValue is { } dv && dv > 0 ? sv / dv : null;

    public TraderResultViewModel(TraderWindowViewModel win, TraderSession session, LuaHost.TraderListing listing)
    {
        _win = win;
        _session = session;
        Listing = listing;
    }

    public void ApplyDiff(LuaHost.TraderDiff? diff)
    {
        if (diff is null) return;
        DpsDiff = diff.DpsDiff;
        EhpDiff = diff.EhpDiff;
        StatValue = diff.StatValue;
        OnPropertyChanged(nameof(ValuePerDiv));
    }

    [RelayCommand]
    private async Task TryOnAsync()
    {
        var ok = await _session.Host.TryOnListingAsync(_win.SlotName, Listing.ItemText, CancellationToken.None);
        if (ok)
        {
            IsTriedOn = true;
            _session.NotifyTryOn();
            _win.CurrentItemName = _session.SlotItemName(_win.SlotName);
        }
    }

    private bool CanCopyWhisper() => Listing.Whisper.Length > 0;

    [RelayCommand(CanExecute = nameof(CanCopyWhisper))]
    private async Task CopyWhisperAsync()
    {
        if (_session.CopyToClipboardAsync is { } copy)
            await copy(Listing.Whisper);
    }
}

/// <summary>Один обязательный фильтр по стату в окне слота.</summary>
public partial class TraderRequiredFilterViewModel : ViewModelBase
{
    private readonly TraderWindowViewModel _win;

    public string Id { get; }
    public string Text { get; }

    [ObservableProperty] private string _min = "";

    public IRelayCommand RemoveCommand { get; }

    public TraderRequiredFilterViewModel(string id, string text, TraderWindowViewModel win)
    {
        Id = id;
        Text = text;
        _win = win;
        RemoveCommand = new RelayCommand(() =>
        {
            _win.RequiredFilters.Remove(this);
            _win.OnRequiredChanged();
        });
    }

    partial void OnMinChanged(string value) => _win.OnRequiredChanged();
}

/// <summary>Один доступный trade-стат для добавления в required-фильтры.</summary>
public sealed class TraderAvailableStatViewModel
{
    public string Id { get; }
    public string Text { get; }
    public TraderAvailableStatViewModel(string id, string text) { Id = id; Text = text; }
}
```

- [ ] **Step 5: Delete the old files**

```bash
git rm PBLApp.Core/TraderTabViewModel.cs PBLApp/Views/TraderTabView.axaml PBLApp/Views/TraderTabView.axaml.cs
```

- [ ] **Step 6: Rewire `BuildPageViewModel.cs` — remove the Trader tab, add session hooks**

In `PBLApp.Core/BuildPageViewModel.cs`:

Remove `[NotifyPropertyChangedFor(nameof(IsTraderTab))]` from the `SelectedTabIndex` attributes (line 31).

Change `TabKeys` (lines 36-37) to:
```csharp
    public static readonly string[] TabKeys =
        ["Items", "Tree", "Skills", "Calcs", "Config"];
```

Remove `IsTraderTab` (line 50). Remove the `5 => TraderTab,` line from `CurrentTabContent` (line 60). Remove `[ObservableProperty] private bool _isTraderPoppedOut;` (line 70) and the `"Trader" => IsTraderPoppedOut,` / `case "Trader": IsTraderPoppedOut = value; break;` branches (lines 78, 90). Remove the `public TraderTabViewModel? TraderTab { get; private set; }` property (line 115).

Replace the whole `OnSelectedTabIndexChanged` method (lines 135-146) with session/window plumbing:
```csharp
    /// <summary>Активное окно подбора (владелец — BuildPageView). Задаётся при открытии/
    /// перенацеливании окна, обнуляется при закрытии. Читается IPC для /trader/*.</summary>
    public TraderWindowViewModel? ActiveTraderWindow { get; set; }

    /// <summary>Просит View открыть/перенацелить окно подбора на слот. Ставит BuildPageView.</summary>
    public Action<string>? RequestOpenTrader { get; set; }

    public TraderSession? TraderSession { get; private set; }

    /// <summary>Лениво создаёт сессию трейдера (её конструктор гоняет заметную Lua-работу:
    /// QueryMods + скан статов слотов), незачем платить при каждом открытии билда.</summary>
    public TraderSession EnsureTraderSession()
    {
        if (TraderSession is null && _host is not null && Build is not null)
            TraderSession = new TraderSession(_host, Build,
                onStatsChanged: () => { CalcsTab?.Refresh(); ItemsTab?.Refresh(); SkillsTab?.Refresh(); });
        return TraderSession!;
    }
```

In `LoadAsync`, remove the comment block about lazy `TraderTab` (lines 191-193) and remove the (already absent) `TraderTab` assignment. After `ItemsTab` is created (line 185), wire the strip route:
```csharp
            ItemsTab.OpenTraderForSlot = slot => RequestOpenTrader?.Invoke(slot);
```

- [ ] **Step 7: Remove the Trader RadioButton from `BuildPageView.axaml`**

Find the RadioButton whose content/Tag is `Trader` (the tab strip, near the other tab RadioButtons) and delete that single `<RadioButton …>…</RadioButton>` element. (Search the file for `Trader` — there is exactly one tab RadioButton plus possibly a pop-out affordance; remove the tab entry. Do not touch other tabs.)

- [ ] **Step 8: Add `OpenTraderForSlot` to `ItemsTabViewModel`**

In `PBLApp.Core/ItemsTabViewModel.cs`, add near the other public delegates/props:
```csharp
    /// <summary>Открыть окно подбора для слота. Ставит BuildPageViewModel; вызывает
    /// код-бихайнд полоски «Подбор». Живёт здесь, чтобы работать и в pop-out окне вкладки.</summary>
    public Action<string>? OpenTraderForSlot { get; set; }
```

- [ ] **Step 9: Rewire IPC `/trader/*` in `IpcServer.cs:1403-1471`**

Replace the whole `// ── Trader tab ──` block (lines 1403-1471) with session/window-based handlers:
```csharp
    // ── Trader (session + active window) ─────────────────────────────────────

    private static TraderSession? GetTraderSession()
        => (GetMainVm()?.CurrentPage as BuildPageViewModel)?.TraderSession;

    private static TraderWindowViewModel? GetTraderWindow()
        => (GetMainVm()?.CurrentPage as BuildPageViewModel)?.ActiveTraderWindow;

    private static object TraderState()
    {
        var s = GetTraderSession();
        if (s is null) return new { open = false, error = "Trader session not created." };
        var w = GetTraderWindow();
        return new
        {
            ok = true,
            windowOpen = w is not null,
            slot = w?.SlotName,
            league = s.SelectedLeague,
            leagues = s.Leagues.ToArray(),
            leagueLoadError = s.LeagueLoadError.Length > 0 ? s.LeagueLoadError : null,
            loggedIn = s.IsLoggedIn,
            account = s.AccountName,
            activeWeightCount = s.ActiveWeightCount,
            statWeightsJson = s.StatWeightsJson,
            status = w?.Status,
            busy = w?.IsBusy ?? false,
            hasQuery = w?.LastQueryJson is not null,
            results = w?.Results.Select(r => new
            {
                price = r.Listing.Amount,
                currency = r.Listing.Currency,
                seller = r.Listing.Seller,
                dps = r.DpsDiff,
                ehp = r.EhpDiff,
                value = r.StatValue,
                valuePerDiv = r.ValuePerDiv,
                triedOn = r.IsTriedOn,
            }).ToArray(),
        };
    }

    private static object TraderSearch(string body)
    {
        var w = GetTraderWindow();
        if (w is null) return new { error = "No trader window open. Call /trader/open first." };
        w.SearchCommand.Execute(null); // fire-and-forget; прогресс виден через /trader/state
        return new { ok = true, started = w.SlotName };
    }

    private static object TraderSetLeague(string body)
    {
        var s = GetTraderSession();
        if (s is null) return new { error = "Trader session not created." };
        var req = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(body) ?? new();
        if (req.TryGetValue("league", out var l) && l.ValueKind == JsonValueKind.String &&
            l.GetString() is { Length: > 0 } league)
        {
            s.SelectedLeague = league;
            return new { ok = true, league };
        }
        return new { error = "Provide 'league'." };
    }
```
(The `/trader/open` endpoint is added in Task 2. Leave the routing table entries at lines 183-185 as-is for now — they still resolve to `TraderState`/`TraderSearch`/`TraderSetLeague`.)

- [ ] **Step 10: Update MCP descriptions in `VisualTools.cs:624-650`**

Change wording "Trader tab" → "trader window" in the three existing tool `Description`s (`VisualTraderState`, `VisualTraderSearch`, `VisualTraderSetLeague`). `VisualTraderSearch`'s slot param no longer starts a per-slot row; note it "searches in the currently open trader window; open one with visual_trader_open first" (the `visual_trader_open` tool itself is added in Task 2). Keep method bodies unchanged.

- [ ] **Step 11: Build core + UI + MCP**

Run (kill PBLApp first):
```powershell
Get-Process | Where-Object { $_.Name -like 'PBLApp*' } | Stop-Process -Force -ErrorAction SilentlyContinue
dotnet build PBLApp/PBLApp.csproj --nologo
dotnet build PBLMcp/PBLMcp.csproj --nologo
```
Expected: both succeed with 0 errors.

- [ ] **Step 12: Run the tests**

Run: `dotnet test PBLEngine.Tests --nologo`
Expected: PASS — all trader tests green (the 3 migrated VM tests + unchanged engine/OAuth/weights/required tests).

- [ ] **Step 13: Commit**

```bash
git add -A
git commit -m @'
refactor(trader): split TraderTabViewModel into TraderSession + TraderWindowViewModel, remove Trader tab

Движок/Lua/OAuth не тронуты. Вкладка снесена; глобальное состояние (логин/лига/
веса/курсы/required-по-слотам) в TraderSession, пер-слот поиск в TraderWindowViewModel.
IPC/MCP переведены на сессию + активное окно. VM-тесты мигрированы.

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
'@
```

---

## Task 2: TraderWindow view + open/retarget wiring + headless entry

**Files:**
- Create: `PBLApp/Views/TraderWindow.axaml`
- Create: `PBLApp/Views/TraderWindow.axaml.cs`
- Modify: `PBLApp/Views/BuildPageView.axaml.cs` (add `OpenTrader`, wire `RequestOpenTrader`)
- Modify: `PBLApp/Ipc/IpcServer.cs` (add `/trader/open` route + handler)
- Modify: `PBLMcp/VisualTools.cs` (add `VisualTraderOpen`)

**Interfaces:**
- Consumes: `BuildPageViewModel.EnsureTraderSession()`, `BuildPageViewModel.ActiveTraderWindow` (settable), `BuildPageViewModel.RequestOpenTrader`, `TraderWindowViewModel(session, slot)`, `TraderWindowViewModel.Retarget(slot)`, `TraderSession.CopyToClipboardAsync`.
- Produces: window opens/retargets for a slot; `/trader/open` IPC endpoint (`{ slot }`) and `visual_trader_open` MCP tool.

- [ ] **Step 1: Create `PBLApp/Views/TraderWindow.axaml`**

Compact layout matching the approved mockup (header: league + login; control bar: `[Веса] [Фильтры] цена [Искать]`; results full-width cards). Use exact theme brushes.

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:vm="using:PBLApp.ViewModels"
        xmlns:loc="using:PBLApp.Localization"
        xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
        xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
        mc:Ignorable="d" d:DesignWidth="560" d:DesignHeight="620"
        x:Class="PBLApp.Views.TraderWindow"
        x:DataType="vm:TraderWindowViewModel"
        Title="{Binding DisplayName, StringFormat={}{0} — Подбор}"
        Width="560" Height="640" MinWidth="440" MinHeight="420"
        Background="{DynamicResource BgBaseBrush}"
        WindowStartupLocation="CenterOwner">

    <Window.Styles>
        <Style Selector="Border.bar">
            <Setter Property="Background" Value="{DynamicResource BgMantleBrush}" />
            <Setter Property="BorderBrush" Value="{DynamicResource BorderSubtleBrush}" />
            <Setter Property="BorderThickness" Value="0,0,0,1" />
            <Setter Property="Padding" Value="12,10" />
        </Style>
        <Style Selector="TextBlock.muted">
            <Setter Property="Foreground" Value="{DynamicResource TextMutedBrush}" />
        </Style>
        <Style Selector="Border.card">
            <Setter Property="Background" Value="{DynamicResource BgSurfaceBrush}" />
            <Setter Property="BorderBrush" Value="{DynamicResource BorderSubtleBrush}" />
            <Setter Property="BorderThickness" Value="1" />
            <Setter Property="CornerRadius" Value="7" />
            <Setter Property="Padding" Value="10,8" />
            <Setter Property="Margin" Value="0,0,0,8" />
        </Style>
        <Style Selector="Border.badge">
            <Setter Property="CornerRadius" Value="5" />
            <Setter Property="Padding" Value="6,2" />
            <Setter Property="BorderThickness" Value="1" />
        </Style>
        <Style Selector="Button.go">
            <Setter Property="Background" Value="{DynamicResource Brand500Brush}" />
            <Setter Property="BorderBrush" Value="{DynamicResource Brand400Brush}" />
            <Setter Property="Foreground" Value="{DynamicResource TextOnBrandBrush}" />
            <Setter Property="FontWeight" Value="Bold" />
        </Style>
    </Window.Styles>

    <DockPanel>

        <!-- ── Шапка: лига + логин (из сессии) ────────────────────────────── -->
        <Border DockPanel.Dock="Top" Classes="bar">
            <Grid ColumnDefinitions="Auto,Auto,*,Auto,Auto" VerticalAlignment="Center">
                <TextBlock Grid.Column="0" Text="{loc:Tr Trader_League}" Classes="muted"
                           VerticalAlignment="Center" Margin="0,0,8,0" />
                <ComboBox Grid.Column="1" MinWidth="180" Height="34"
                          ItemsSource="{Binding Session.Leagues}"
                          SelectedItem="{Binding Session.SelectedLeague, Mode=TwoWay}" />
                <TextBlock Grid.Column="2" Classes="muted" VerticalAlignment="Center" Margin="16,0"
                           Text="{Binding Session.LeagueLoadError}"
                           IsVisible="{Binding Session.LeagueLoadError, Converter={x:Static StringConverters.IsNotNullOrEmpty}}" />
                <TextBlock Grid.Column="3" VerticalAlignment="Center" Margin="0,0,10,0"
                           Text="{Binding Session.AccountName}"
                           IsVisible="{Binding Session.IsLoggedIn}" />
                <StackPanel Grid.Column="4" Orientation="Horizontal" Spacing="6">
                    <Button Content="{loc:Tr Trader_Login}" Command="{Binding Session.LoginCommand}"
                            IsVisible="{Binding !Session.IsLoggedIn}" />
                    <Button Content="{loc:Tr Trader_Logout}" Command="{Binding Session.LogoutCommand}"
                            IsVisible="{Binding Session.IsLoggedIn}" />
                </StackPanel>
            </Grid>
        </Border>

        <!-- ── Панель управления: веса / фильтры / цена / искать ──────────── -->
        <Border DockPanel.Dock="Top" Classes="bar">
            <StackPanel Spacing="8">
                <StackPanel Orientation="Horizontal" Spacing="10" VerticalAlignment="Center">
                    <!-- Веса (флайаут, общие на билд — из сессии) -->
                    <Button Content="{Binding Session.WeightsButtonText}">
                        <Button.Flyout>
                            <Flyout Placement="BottomEdgeAlignedLeft">
                                <StackPanel Width="360" Spacing="6">
                                    <StackPanel Orientation="Horizontal" Spacing="6">
                                        <Button Content="{loc:Tr Trader_PresetDps}"
                                                Command="{Binding Session.ApplyPresetCommand}" CommandParameter="dps" />
                                        <Button Content="{loc:Tr Trader_PresetEhp}"
                                                Command="{Binding Session.ApplyPresetCommand}" CommandParameter="ehp" />
                                        <Button Content="{loc:Tr Trader_PresetBalance}"
                                                Command="{Binding Session.ApplyPresetCommand}" CommandParameter="balance" />
                                        <Button Content="{loc:Tr Trader_WeightReset}"
                                                Command="{Binding Session.ResetWeightsCommand}" HorizontalAlignment="Right" />
                                    </StackPanel>
                                    <TextBox Text="{Binding Session.WeightSearch, Mode=TwoWay}"
                                             Watermark="{loc:Tr Trader_WeightSearch}" />
                                    <ScrollViewer MaxHeight="360">
                                        <ItemsControl ItemsSource="{Binding Session.FilteredWeightStats}">
                                            <ItemsControl.ItemTemplate>
                                                <DataTemplate x:DataType="vm:TraderWeightEntryViewModel">
                                                    <Grid ColumnDefinitions="*,110" Margin="0,1">
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
                                </StackPanel>
                            </Flyout>
                        </Button.Flyout>
                    </Button>

                    <!-- Фильтры (флайаут, required пер-слот) -->
                    <Button Content="{Binding RequiredButtonText}" IsVisible="{Binding HasStatCategory}">
                        <Button.Flyout>
                            <Flyout Placement="BottomEdgeAlignedLeft">
                                <StackPanel Width="420" Spacing="6">
                                    <ItemsControl ItemsSource="{Binding RequiredFilters}">
                                        <ItemsControl.ItemTemplate>
                                            <DataTemplate x:DataType="vm:TraderRequiredFilterViewModel">
                                                <Grid ColumnDefinitions="*,90,Auto" Margin="0,1">
                                                    <TextBlock Grid.Column="0" Text="{Binding Text}"
                                                               VerticalAlignment="Center"
                                                               TextTrimming="CharacterEllipsis" />
                                                    <TextBox Grid.Column="1" Text="{Binding Min, Mode=TwoWay}"
                                                             Watermark="{loc:Tr Trader_ReqMin}" Margin="6,0" />
                                                    <Button Grid.Column="2" Content="✕" Command="{Binding RemoveCommand}" />
                                                </Grid>
                                            </DataTemplate>
                                        </ItemsControl.ItemTemplate>
                                    </ItemsControl>
                                    <TextBox Text="{Binding RequiredSearch, Mode=TwoWay}"
                                             Watermark="{loc:Tr Trader_ReqSearch}" />
                                    <ScrollViewer MaxHeight="280">
                                        <ItemsControl ItemsSource="{Binding FilteredAvailableStats}">
                                            <ItemsControl.ItemTemplate>
                                                <DataTemplate x:DataType="vm:TraderAvailableStatViewModel">
                                                    <Button Content="{Binding Text}"
                                                            HorizontalAlignment="Stretch"
                                                            HorizontalContentAlignment="Left"
                                                            Command="{Binding $parent[ItemsControl].((vm:TraderWindowViewModel)DataContext).AddRequiredCommand}"
                                                            CommandParameter="{Binding}" />
                                                </DataTemplate>
                                            </ItemsControl.ItemTemplate>
                                        </ItemsControl>
                                    </ScrollViewer>
                                </StackPanel>
                            </Flyout>
                        </Button.Flyout>
                    </Button>

                    <TextBlock Text="{loc:Tr Trader_MaxPrice}" Classes="muted" VerticalAlignment="Center" Margin="8,0,0,0" />
                    <TextBox Text="{Binding MaxPrice, Mode=TwoWay}" Width="70" Watermark="—" />
                    <ComboBox ItemsSource="{Binding Session.CurrencyNames}"
                              SelectedIndex="{Binding MaxPriceCurrencyIndex, Mode=TwoWay}" MinWidth="150" />

                    <Button Grid.Column="0" Classes="go" Content="{loc:Tr Trader_FindUpgrades}"
                            Command="{Binding SearchCommand}" IsEnabled="{Binding !IsBusy}"
                            Margin="8,0,0,0" />
                    <Button Content="{loc:Tr Trader_Cancel}" Command="{Binding CancelCommand}"
                            IsVisible="{Binding IsBusy}" />
                    <Button Content="{loc:Tr Trader_OpenSite}" Command="{Binding OpenOnSiteCommand}" />
                </StackPanel>
                <TextBlock Text="{Binding Status}" Classes="muted"
                           IsVisible="{Binding Status, Converter={x:Static StringConverters.IsNotNullOrEmpty}}" />
            </StackPanel>
        </Border>

        <!-- ── Результаты во всю ширину ───────────────────────────────────── -->
        <ScrollViewer Padding="12,10">
            <ItemsControl ItemsSource="{Binding Results}">
                <ItemsControl.ItemTemplate>
                    <DataTemplate x:DataType="vm:TraderResultViewModel">
                        <Border Classes="card" ToolTip.Tip="{Binding Listing.ItemText}">
                            <StackPanel Spacing="8">
                                <Grid ColumnDefinitions="*,Auto">
                                    <TextBlock Grid.Column="0" Text="{Binding ItemName}"
                                               FontWeight="SemiBold" TextTrimming="CharacterEllipsis"
                                               VerticalAlignment="Center" />
                                    <TextBlock Grid.Column="1" Text="{Binding PriceText}"
                                               FontWeight="Bold" VerticalAlignment="Center" />
                                </Grid>
                                <StackPanel Orientation="Horizontal" Spacing="7">
                                    <Border Classes="badge"
                                            Background="#182a1c" BorderBrush="#2c4a33">
                                        <TextBlock Foreground="{DynamicResource SuccessBrush}" FontWeight="Bold" FontSize="11"
                                                   Text="{Binding DpsDiff, StringFormat='ΔDPS +0.#;ΔDPS -0.#;ΔDPS 0'}" />
                                    </Border>
                                    <Border Classes="badge"
                                            Background="#182a1c" BorderBrush="#2c4a33">
                                        <TextBlock Foreground="{DynamicResource SuccessBrush}" FontWeight="Bold" FontSize="11"
                                                   Text="{Binding EhpDiff, StringFormat='ΔEHP +0.#;ΔEHP -0.#;ΔEHP 0'}" />
                                    </Border>
                                    <TextBlock Classes="muted" VerticalAlignment="Center" FontSize="11"
                                               Text="{Binding ValuePerDiv, StringFormat='· {0:0.##}/div'}" />
                                </StackPanel>
                                <Grid ColumnDefinitions="*,Auto,Auto">
                                    <TextBlock Grid.Column="0" Classes="muted" VerticalAlignment="Center" FontSize="11"
                                               Text="{Binding Listing.Seller}" TextTrimming="CharacterEllipsis" />
                                    <Button Grid.Column="1" Margin="0,0,6,0" Content="{loc:Tr Trader_TryOn}"
                                            Command="{Binding TryOnCommand}" IsEnabled="{Binding !IsTriedOn}" />
                                    <Button Grid.Column="2" Content="{loc:Tr Trader_Whisper}"
                                            Command="{Binding CopyWhisperCommand}" />
                                </Grid>
                            </StackPanel>
                        </Border>
                    </DataTemplate>
                </ItemsControl.ItemTemplate>
            </ItemsControl>
        </ScrollViewer>
    </DockPanel>
</Window>
```

Note: the `Classes="go"` button sits inside the horizontal StackPanel — remove the stray `Grid.Column="0"` attribute (copy artifact) when transcribing; StackPanel children don't use Grid.Column.

- [ ] **Step 2: Create `PBLApp/Views/TraderWindow.axaml.cs`**

Wire clipboard (whisper copy) like the old TraderTabView code-behind:
```csharp
using Avalonia.Controls;
using Avalonia.Input.Platform;
using PBLApp.ViewModels;
using System.Threading.Tasks;

namespace PBLApp.Views;

public partial class TraderWindow : Window
{
    public TraderWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is TraderWindowViewModel vm)
                vm.Session.CopyToClipboardAsync = CopyAsync;
        };
    }

    private async Task CopyAsync(string text)
    {
        if (Clipboard is { } cb) await cb.SetTextAsync(text);
    }
}
```

- [ ] **Step 3: Add `OpenTrader` + wire `RequestOpenTrader` in `BuildPageView.axaml.cs`**

Add a field next to `_notesWindow` (line 15):
```csharp
    private TraderWindow? _traderWindow;
```

In the `DataContextChanged` handler (lines 26-30), also set the trader route:
```csharp
        DataContextChanged += (_, _) =>
        {
            if (DataContext is BuildPageViewModel vm)
            {
                vm.PromptRenameAsync = PromptRenameAsync;
                vm.RequestOpenTrader = OpenTrader;
            }
        };
```

Add the method (mirror `OpenNotes_Click`, with retarget + ActiveTraderWindow bookkeeping):
```csharp
    private void OpenTrader(string slot)
    {
        if (DataContext is not BuildPageViewModel vm) return;
        var session = vm.EnsureTraderSession();

        if (_traderWindow is not null)
        {
            try
            {
                if (_traderWindow.DataContext is TraderWindowViewModel wvm) wvm.Retarget(slot);
                _traderWindow.Activate();
                return;
            }
            catch { _traderWindow = null; }
        }

        var owner = TopLevel.GetTopLevel(this) as Window;
        var winVm = new TraderWindowViewModel(session, slot);
        _traderWindow = new TraderWindow { DataContext = winVm };
        vm.ActiveTraderWindow = winVm;
        _traderWindow.Closed += (_, _) =>
        {
            _traderWindow = null;
            vm.ActiveTraderWindow = null;
        };
        if (owner is not null) _traderWindow.Show(owner);
        else _traderWindow.Show();
    }
```

Note: on retarget, keep `ActiveTraderWindow` pointing at the same `wvm` (its `SlotName` changed) — no reassignment needed since it's the same instance.

- [ ] **Step 4: Add `/trader/open` IPC route + handler in `IpcServer.cs`**

In the routing table (near lines 183-185), add:
```csharp
                "/trader/open"       => await OnUi(() => TraderOpen(body)),
```
Add the handler next to `TraderState` (Task 1 block):
```csharp
    private static object TraderOpen(string body)
    {
        if (GetMainVm()?.CurrentPage is not BuildPageViewModel bp)
            return new { error = "No build page." };
        var req = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(body) ?? new();
        var slot = req.TryGetValue("slot", out var s) && s.ValueKind == JsonValueKind.String
            ? s.GetString() : null;
        if (string.IsNullOrEmpty(slot)) return new { error = "Provide 'slot'." };
        if (bp.RequestOpenTrader is null) return new { error = "Trader open route not wired." };
        bp.RequestOpenTrader(slot);
        return new { ok = true, opened = slot };
    }
```

- [ ] **Step 5: Add `VisualTraderOpen` MCP tool in `VisualTools.cs`**

Next to `VisualTraderSearch` (line 638):
```csharp
    [McpServerTool]
    [Description(
        "Open (or retarget) the trader window for an equipment slot (e.g. 'Helmet', " +
        "'Body Armour'). One reusable window; call before visual_trader_search.")]
    public async Task<string> VisualTraderOpen(string slot)
    {
        try { return await IpcClient.CallAsync("POST", "/trader/open", new { slot }); }
        catch (Exception ex) { return Error(ex); }
    }
```

- [ ] **Step 6: Build UI + MCP**

```powershell
Get-Process | Where-Object { $_.Name -like 'PBLApp*' } | Stop-Process -Force -ErrorAction SilentlyContinue
dotnet build PBLApp/PBLApp.csproj --nologo
dotnet build PBLMcp/PBLMcp.csproj --nologo
```
Expected: 0 errors.

- [ ] **Step 7: Verify headlessly with `/pbl-verify` (screenshot the window)**

Ask the user to `/mcp` reconnect (PBLMcp was rebuilt). Then run the `/pbl-verify` flow, and after the build loads, drive the window open via MCP:
- `mcp__pbl-engine__visual_trader_open` with `slot="Helmet"`
- `mcp__pbl-engine__visual_screenshot` → `Read` the PNG.
Expected: a window titled «Шлем — Подбор» with the league/login header, `[Веса] [Фильтры] цена [Искать]` control bar, empty results area. Fix any binding/layout regression before proceeding.

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m @'
feat(trader): TraderWindow view + open/retarget wiring + visual_trader_open

Немодальное окно подбора (один инстанс, Retarget на слот) по образцу OpenNotes.
IPC /trader/open + MCP visual_trader_open открывают/перенацеливают окно.

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
'@
```

---

## Task 3: «Подбор» strip on equipment slots

**Files:**
- Modify: `PBLApp/Views/ItemsTabView.axaml` (styles + strip in each figure slot)
- Modify: `PBLApp/Views/ItemsTabView.axaml.cs` (strip click handler)

**Interfaces:**
- Consumes: `ItemsTabViewModel.OpenTraderForSlot` (Task 1).
- Produces: clicking the strip opens the trader window for that slot; click on the rest of the cell is unaffected (existing tunnel select).

- [ ] **Step 1: Add strip styles to `ItemsTabView.axaml` `<UserControl.Styles>`**

After the `Border.slot-cell.selected` style (line 42), add:
```xml
        <!-- «Подбор» strip: overlay inside the slot cell at the bottom edge. Own
             PointerPressed (e.Handled) so it doesn't trigger the tunnel slot-select. -->
        <Style Selector="Border.trader-strip">
            <Setter Property="VerticalAlignment" Value="Bottom" />
            <Setter Property="Height" Value="17" />
            <Setter Property="Background" Value="#E615171D" />
            <Setter Property="BorderBrush" Value="#22FFFFFF" />
            <Setter Property="BorderThickness" Value="0,1,0,0" />
            <Setter Property="Cursor" Value="Hand" />
        </Style>
        <Style Selector="Border.trader-strip:pointerover">
            <Setter Property="Background" Value="#EE262A35" />
        </Style>
        <Style Selector="Border.trader-strip TextBlock">
            <Setter Property="Foreground" Value="{DynamicResource Brand400Brush}" />
            <Setter Property="FontSize" Value="9.5" />
            <Setter Property="FontWeight" Value="SemiBold" />
            <Setter Property="HorizontalAlignment" Value="Center" />
            <Setter Property="VerticalAlignment" Value="Center" />
        </Style>
```

- [ ] **Step 2: Add the strip as the last child of each figure slot's inner `Grid`**

For each equipment slot `Border` on the `Canvas` (Helmet, Amulet, Weapon 1, Weapon 1 Swap, Body Armour, Ring 1, Ring 2, Weapon 2, Weapon 2 Swap, Belt, Gloves, Boots, Charm 1, Charm 2, Charm 3, Flask 1, Flask 2), add a strip `Border` as the **last child of that slot's `<Grid>`** (so it overlays at the bottom). Two variants:

**Wide slots** (Helmet, Weapon 1, Weapon 1 Swap, Body Armour, Weapon 2, Weapon 2 Swap, Belt, Gloves, Boots, Flask 1, Flask 2) — icon + label:
```xml
                        <Border Classes="trader-strip" Tag="trader:Helmet"
                                PointerPressed="TraderStrip_Pressed">
                            <TextBlock Text="🔍 Подбор" />
                        </Border>
```
**Narrow slots** (Amulet, Ring 1, Ring 2, Charm 1, Charm 2, Charm 3) — icon only:
```xml
                        <Border Classes="trader-strip" Tag="trader:Amulet"
                                PointerPressed="TraderStrip_Pressed">
                            <TextBlock Text="🔍" />
                        </Border>
```
Set each strip's `Tag` to `trader:<SlotName>` matching that cell's `slot:<SlotName>` (e.g. `slot:Weapon 1 Swap` → `Tag="trader:Weapon 1 Swap"`). The `loc:Tr Trader_Strip` binding is applied in Task 4 (start with the literal to keep this task self-contained; Task 4 swaps the two literals `🔍 Подбор` / `🔍` for localized resources).

- [ ] **Step 3: Add the click handler to `ItemsTabView.axaml.cs`**

```csharp
    private void TraderStrip_Pressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        if (sender is not Control { Tag: string tag } || !tag.StartsWith("trader:")) return;
        e.Handled = true; // не отдаём клик тунельному slot-select
        var slot = tag["trader:".Length..];
        if (DataContext is ItemsTabViewModel vm)
            vm.OpenTraderForSlot?.Invoke(slot);
    }
```
(If `ItemsTabView.axaml.cs` lacks a `using Avalonia.Controls;`, add it.)

- [ ] **Step 4: Build UI**

```powershell
Get-Process | Where-Object { $_.Name -like 'PBLApp*' } | Stop-Process -Force -ErrorAction SilentlyContinue
dotnet build PBLApp/PBLApp.csproj --nologo
```
Expected: 0 errors.

- [ ] **Step 5: Verify with `/pbl-verify` (screenshot the Items figure + opened window)**

Run `/pbl-verify`; open a build; screenshot the Items tab. Expected: every equipment slot shows the bottom «🔍 Подбор» / «🔍» strip; the strip is readable over item icons; narrow slots (rings, charms, amulet) show icon-only. Then drive a strip: since MCP can't click the strip directly, use `mcp__pbl-engine__visual_trader_open` `slot="Body Armour"` to confirm the same window path, screenshot. Fix cramped/unreadable strips (e.g. drop the label to icon-only on any wide slot that clips) before proceeding.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m @'
feat(trader): «Подбор» strip on equipment slots opens the trader window

Полоска-оверлей у нижней кромки ячейки (широкие — «🔍 Подбор», узкие — «🔍»);
свой PointerPressed с e.Handled, чтобы не триггерить slot-select.

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
'@
```

---

## Task 4: Localization, docs, final verification

**Files:**
- Modify: `PBLApp.Core/Localization/Strings.resx`
- Modify: `PBLApp.Core/Localization/Strings.ru.resx`
- Modify: `PBLApp/Views/ItemsTabView.axaml` (swap strip literals for `loc:Tr`)
- Modify: `AVALONIA_MIGRATION_PLAN.md`

**Interfaces:**
- Consumes: existing `loc:Tr` markup extension, `LocalizationService.Get`.
- Produces: new keys `Trader_EmptySlot`, `Trader_StripLabel`, `Trader_StripIcon`; removed dead keys `Tab_Trader`, `Trader_TotalTryOn`.

- [ ] **Step 1: Add/remove resource keys in `Strings.resx`**

Remove the `<data name="Tab_Trader" …>` and `<data name="Trader_TotalTryOn" …>` entries. Add:
```xml
  <data name="Trader_EmptySlot" xml:space="preserve">
    <value>Equip an item in this slot to search for an upgrade.</value>
  </data>
  <data name="Trader_StripLabel" xml:space="preserve">
    <value>🔍 Find</value>
  </data>
  <data name="Trader_StripIcon" xml:space="preserve">
    <value>🔍</value>
  </data>
```

- [ ] **Step 2: Mirror keys in `Strings.ru.resx`**

Remove the same two dead keys. Add:
```xml
  <data name="Trader_EmptySlot" xml:space="preserve">
    <value>Экипируйте предмет в этот слот, чтобы подобрать апгрейд.</value>
  </data>
  <data name="Trader_StripLabel" xml:space="preserve">
    <value>🔍 Подбор</value>
  </data>
  <data name="Trader_StripIcon" xml:space="preserve">
    <value>🔍</value>
  </data>
```

- [ ] **Step 3: Swap strip literals for localized resources in `ItemsTabView.axaml`**

For every wide-slot strip `TextBlock`, replace `Text="🔍 Подбор"` with `Text="{loc:Tr Trader_StripLabel}"`. For every narrow-slot strip, replace `Text="🔍"` with `Text="{loc:Tr Trader_StripIcon}"`.

- [ ] **Step 4: Update `AVALONIA_MIGRATION_PLAN.md`**

Add a short phase note under the trader entry: the Trader tab was replaced by a per-slot «Подбор» strip that opens a single reusable, retargetable `TraderWindow`; `TraderTabViewModel` split into `TraderSession` (global: login/league/weights/rates/required-per-slot) + `TraderWindowViewModel` (per-slot search); engine/Lua/OAuth unchanged; empty-slot search shows a friendly hint (true category-from-slotname search deferred — needs generator work in off-limits `src/Classes`).

- [ ] **Step 5: Build + test**

```powershell
Get-Process | Where-Object { $_.Name -like 'PBLApp*' } | Stop-Process -Force -ErrorAction SilentlyContinue
dotnet build PBLApp/PBLApp.csproj --nologo
```
Run: `dotnet test PBLEngine.Tests --nologo`
Expected: build 0 errors; all tests pass (no test references the removed keys).

- [ ] **Step 6: Full `/pbl-verify` pass (RU + EN)**

Run `/pbl-verify`; screenshot: Items figure with strips (RU), open «Подбор» window (`visual_trader_open` Helmet), weights flyout, filters flyout. Then `mcp__pbl-engine__visual_set_language` `en` and re-snap the window to confirm English strings («🔍 Find», window title, empty-slot hint). Fix any missing translation or layout issue.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m @'
i18n(trader): strip strings + empty-slot hint; drop dead Tab_Trader/TotalTryOn; migration-plan note

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
'@
```

---

## Self-Review

**1. Spec coverage:**
- Снос вкладки → Task 1 (BuildPageViewModel, BuildPageView, delete view). ✓
- Полоска «Подбор» на слотах → Task 3. ✓
- Немодальное перенацеливаемое окно → Task 2 (`OpenTrader` + `Retarget`). ✓
- Логин + лига в шапке окна (общие) → Task 2 XAML binds `Session.*`. ✓
- Веса общие на билд, персист XML → unchanged engine + `TraderSession` (Task 1). ✓
- Компактная раскладка (панель сверху, результаты снизу) → Task 2 XAML. ✓
- house-style + трейд-акценты (бейджи ΔDPS/ΔEHP, карточки) → Task 2 XAML. ✓
- Один инстанс, перенацеливается → Task 2 `OpenTrader`. ✓
- Разделение состояния (Session/Window) → Task 1. ✓
- Required пер-слот, персист в сессии → Task 1 (`RequiredBySlot`) + test. ✓
- IPC/MCP на окно + `visual_trader_open` → Task 1 (state/search/league) + Task 2 (open). ✓
- Тесты: engine/OAuth/weights/required без изменений; VM-тесты мигрированы → Task 1. ✓
- **Deviation:** пустой слот — вместо полноценного поиска по категории показывается дружелюбная подсказка (Task 1 guard + Task 4 string). Полный empty-slot поиск требует правки генератора в off-limits `src/Classes` — сознательно отложено. Флагнуть пользователю при старте исполнения.

**2. Placeholder scan:** нет TBD/«add error handling»/«similar to». Единственное «если» — Task 2 Step-1 note про артефакт `Grid.Column="0"` в go-кнопке (убрать при переносе, StackPanel не использует Grid.Column) — с точным действием. `ItemName` парсится из `ItemText` (в record `TraderListing` нет `Name`, движок не трогаем). ✓

**3. Type consistency:** `TraderSession` / `TraderWindowViewModel` сигнатуры совпадают между Interfaces-блоками, кодом и XAML-биндингами (`Session.Leagues`, `Session.WeightsButtonText`, `RequiredButtonText`, `AddRequiredCommand`, `SearchCommand`, `OpenOnSiteCommand`, `Retarget`). IPC читает `ActiveTraderWindow`/`TraderSession`, заданные в Task 1. `OpenTraderForSlot` (ItemsTabViewModel) ↔ `RequestOpenTrader` (BuildPageViewModel) ↔ `OpenTrader` (BuildPageView) согласованы. ✓
