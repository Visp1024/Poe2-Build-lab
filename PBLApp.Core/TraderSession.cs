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

    /// <summary>Лиги трейд-сайта на случай, когда список не скачался (VPN/403):
    /// без них дропдаун пуст и поиск невозможен вообще.</summary>
    private static readonly string[] FallbackLeagues =
        ["Standard", "Hardcore", "Solo Self-Found", "Hardcore SSF"];

    private const string PrefLeague = "TraderLeague";

    private async Task InitLeaguesAsync()
    {
        var saved = AppPreferences.Get(PrefLeague);
        try
        {
            var leagues = await _webApi.GetLeaguesAsync();
            Leagues.Clear();
            foreach (var l in leagues) Leagues.Add(l);
            LeagueLoadError = "";
        }
        catch
        {
            // список лиг живёт на том же хосте, что и поиск: 403 от VPN валит и его
            Leagues.Clear();
            foreach (var l in FallbackLeagues) Leagues.Add(l);
            LeagueLoadError = LocalizationService.Get("Trader_LeaguesOffline");
        }
        // прошлая лига могла быть приватной — её нет ни в одном списке, но
        // выбор пользователя важнее полноты дропдауна
        if (!string.IsNullOrEmpty(saved) && !Leagues.Contains(saved))
            Leagues.Insert(0, saved);
        if (string.IsNullOrEmpty(SelectedLeague))
        {
            // стартовое значение не пишем обратно в настройки: сохраняем только
            // сознательный выбор пользователя
            _suppressLeaguePersist = true;
            try
            {
                SelectedLeague = saved is { Length: > 0 } ? saved
                    : Leagues.Count > 0 ? Leagues[0] : "";
            }
            finally { _suppressLeaguePersist = false; }
        }
    }

    private bool _suppressLeaguePersist;

    partial void OnSelectedLeagueChanged(string value)
    {
        if (string.IsNullOrEmpty(value)) return;
        if (!_suppressLeaguePersist) AppPreferences.Set(PrefLeague, value);
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
