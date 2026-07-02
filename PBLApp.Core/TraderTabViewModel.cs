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
/// Вкладка «Трейдер»: поиск апгрейдов по слоту на pathofexile.com/trade2.
/// Конвейер строки: генерация взвешенного запроса (Lua-корутина) → поиск+fetch →
/// фоновые Δ-статы. Работает без логина до стадии «открыть на сайте».
/// </summary>
public partial class TraderTabViewModel : ViewModelBase
{
    internal LuaHost Host { get; }
    private readonly Action? _onStatsChanged;
    private readonly TraderWebApi _webApi;
    private readonly PoeOAuthService _oauth;
    private IReadOnlyDictionary<string, double> _rates =
        new Dictionary<string, double>();

    /// <summary>Одновременно идёт максимум один поиск (генератор — один на билд).</summary>
    internal SemaphoreSlim SearchGate { get; } = new(1, 1);

    public ObservableCollection<TraderSlotRowViewModel> Slots { get; } = [];
    public ObservableCollection<string> Leagues { get; } = [];

    [ObservableProperty]
    private string _selectedLeague = "";

    [ObservableProperty]
    private string _leagueLoadError = "";

    // Веса статов: пресеты выставляют пары множителей
    [ObservableProperty] private double _dpsWeight = 1.0;
    [ObservableProperty] private double _ehpWeight = 0.5;

    [ObservableProperty] private string _maxPrice = "";
    [ObservableProperty] private int _maxPriceCurrencyIndex;

    /// <summary>Валюты лимита цены — как currencyTable в TradeQueryGenerator.lua:772-784.</summary>
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

    public IReadOnlyList<string> CurrencyNames { get; } = Currencies.Select(c => c.Name).ToList();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalTryOnText))]
    [NotifyPropertyChangedFor(nameof(HasTryOns))]
    private double _totalTryOnDivs;

    public bool HasTryOns => TotalTryOnDivs > 0;

    public string TotalTryOnText => string.Format(
        LocalizationService.Get("Trader_TotalTryOn"),
        TotalTryOnDivs.ToString("0.##", CultureInfo.InvariantCulture));

    /// <summary>Подключается View'ом (паттерн PromptRenameAsync).</summary>
    public Func<string, Task>? CopyToClipboardAsync { get; set; }

    public bool IsLoggedIn => _oauth.IsLoggedIn;
    public string? AccountName => _oauth.AccountName;

    public TraderTabViewModel(LuaHost host, BuildModel build, Action? onStatsChanged = null,
        TraderWebApi? webApi = null, PoeOAuthService? oauth = null)
    {
        Host = host;
        _onStatsChanged = onStatsChanged;
        _webApi = webApi ?? new TraderWebApi();
        _oauth = oauth ?? new PoeOAuthService(host);
        _oauth.InjectIntoLua();
        RefreshSlots();
        _ = InitLeaguesAsync();
    }

    // ── Слоты ────────────────────────────────────────────────────────────────

    public void RefreshSlots()
    {
        var json = Host.GetTraderSlotsJson();
        Slots.Clear();
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return;
        foreach (var el in doc.RootElement.EnumerateArray())
        {
            var slotName = el.GetProperty("slotName").GetString() ?? "";
            var itemName = el.TryGetProperty("itemName", out var i) ? i.GetString() ?? "" : "";
            Slots.Add(new TraderSlotRowViewModel(this, slotName, itemName));
        }
    }

    internal static string LocalizeSlot(string slotName)
    {
        var loc = LocalizationService.Get("Slot_" + slotName.Replace(" ", ""));
        return loc.StartsWith('[') ? slotName : loc;
    }

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
        catch (Exception ex)
        {
            LeagueLoadError = ex.Message;
        }
    }

    partial void OnSelectedLeagueChanged(string value)
    {
        if (string.IsNullOrEmpty(value)) return;
        _ = LoadRatesAsync(value);
    }

    private async Task LoadRatesAsync(string league)
    {
        try { _rates = await _webApi.GetCurrencyRatesAsync(league); }
        catch { /* курсы — best-effort, без них нет колонки «в дивинах» */ }
    }

    /// <summary>Курс валюты в дивинах; null — курс не загружен.</summary>
    public double? RateFor(string currencyId) =>
        _rates.TryGetValue(currencyId, out var v) ? v : null;

    // ── Веса и опции ─────────────────────────────────────────────────────────

    [RelayCommand]
    private void ApplyPreset(string preset)
    {
        (DpsWeight, EhpWeight) = preset switch
        {
            "dps" => (1.0, 0.1),
            "ehp" => (0.1, 1.0),
            _ => (1.0, 0.5), // balance
        };
    }

    public string StatWeightsJson =>
        JsonSerializer.Serialize(new object[]
        {
            new { stat = "FullDPS", weightMult = DpsWeight },
            new { stat = "TotalEHP", weightMult = EhpWeight },
        });

    public string OptionsJson
    {
        get
        {
            var opts = new Dictionary<string, object?>
            {
                ["statWeights"] = new object[]
                {
                    new { stat = "FullDPS", weightMult = DpsWeight },
                    new { stat = "TotalEHP", weightMult = EhpWeight },
                },
                ["includeCorrupted"] = true,
                ["includeMirrored"] = false,
            };
            if (double.TryParse(MaxPrice, NumberStyles.Float, CultureInfo.InvariantCulture, out var mp) && mp > 0)
            {
                opts["maxPrice"] = mp;
                var id = Currencies[Math.Clamp(MaxPriceCurrencyIndex, 0, Currencies.Count - 1)].Id;
                if (id is not null) opts["maxPriceType"] = id;
            }
            return JsonSerializer.Serialize(opts);
        }
    }

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

    internal static void OpenInBrowser(string url) =>
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

    // ── Примерка ─────────────────────────────────────────────────────────────

    internal void NotifyTryOn()
    {
        TotalTryOnDivs = Slots
            .SelectMany(s => s.Results)
            .Where(r => r.IsTriedOn)
            .Sum(r => r.DivValue ?? 0);
        RefreshSlots();
        _onStatsChanged?.Invoke();
    }
}

/// <summary>Строка слота: кнопка поиска, статус стадий, список результатов.</summary>
public partial class TraderSlotRowViewModel : ViewModelBase
{
    private readonly TraderTabViewModel _owner;
    private CancellationTokenSource? _cts;

    public string SlotName { get; }
    public string DisplayName { get; }

    [ObservableProperty] private string _currentItemName;
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _lastQueryJson;

    public ObservableCollection<TraderResultViewModel> Results { get; } = [];

    public TraderSlotRowViewModel(TraderTabViewModel owner, string slotName, string itemName)
    {
        _owner = owner;
        SlotName = slotName;
        DisplayName = TraderTabViewModel.LocalizeSlot(slotName);
        _currentItemName = itemName;
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        if (IsBusy) return;
        _cts = new CancellationTokenSource();
        IsBusy = true;
        Results.Clear();
        await _owner.SearchGate.WaitAsync();
        try
        {
            Status = LocalizationService.Get("Trader_StatusGenerating");
            var q = await _owner.Host.GenerateTradeQueryAsync(
                SlotName, _owner.OptionsJson, null, _cts.Token);
            if (q.Error is not null) { Status = q.Error; return; }
            LastQueryJson = q.QueryJson;
            OpenOnSiteCommand.NotifyCanExecuteChanged();

            if (!_owner.IsLoggedIn)
            {
                Status = LocalizationService.Get("Trader_NeedLogin");
                return;
            }

            Status = LocalizationService.Get("Trader_StatusSearching");
            var search = await _owner.Host.SearchTradeAsync(
                _owner.SelectedLeague, q.QueryJson!, _cts.Token);
            if (search.Error is not null) { Status = search.Error; return; }
            foreach (var l in search.Listings)
                Results.Add(new TraderResultViewModel(this, _owner, l));

            for (var i = 0; i < Results.Count; i++)
            {
                _cts.Token.ThrowIfCancellationRequested();
                Status = string.Format(
                    LocalizationService.Get("Trader_StatusDiffing"), i + 1, Results.Count);
                var diff = await _owner.Host.ComputeListingDiffAsync(
                    SlotName, Results[i].Listing.ItemText, _owner.StatWeightsJson, _cts.Token);
                Results[i].ApplyDiff(diff);
            }
            SortResults();
            Status = "";
        }
        catch (OperationCanceledException)
        {
            Status = LocalizationService.Get("Trader_Cancelled");
        }
        catch (Exception ex)
        {
            Status = ex.Message;
        }
        finally
        {
            _owner.SearchGate.Release();
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    private bool CanOpenOnSite() => !string.IsNullOrEmpty(LastQueryJson);

    [RelayCommand(CanExecute = nameof(CanOpenOnSite))]
    private void OpenOnSite()
    {
        // Формат ссылки — как TradeQuery.lua:1244
        var url = $"https://www.pathofexile.com/trade2/search/{Uri.EscapeDataString(_owner.SelectedLeague)}"
                + $"?q={Uri.EscapeDataString(LastQueryJson!)}";
        TraderTabViewModel.OpenInBrowser(url);
    }

    /// <summary>Сортировка по умолчанию: «прирост за цену», затем чистый прирост.</summary>
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
    private readonly TraderSlotRowViewModel _row;
    private readonly TraderTabViewModel _owner;

    public LuaHost.TraderListing Listing { get; }

    [ObservableProperty] private double? _dpsDiff;
    [ObservableProperty] private double? _ehpDiff;
    [ObservableProperty] private double? _statValue;
    [ObservableProperty] private bool _isTriedOn;

    public string PriceText => $"{Listing.Amount:0.##} {Listing.Currency}";

    /// <summary>Цена в дивинах; null — курс валюты неизвестен.</summary>
    public double? DivValue =>
        _owner.RateFor(Listing.Currency) is { } rate ? Listing.Amount * rate : null;

    /// <summary>Ключ сортировки «прирост за цену».</summary>
    public double? ValuePerDiv =>
        StatValue is { } sv && DivValue is { } dv && dv > 0 ? sv / dv : null;

    public TraderResultViewModel(TraderSlotRowViewModel row, TraderTabViewModel owner,
        LuaHost.TraderListing listing)
    {
        _row = row;
        _owner = owner;
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
        var ok = await _owner.Host.TryOnListingAsync(
            _row.SlotName, Listing.ItemText, CancellationToken.None);
        if (ok)
        {
            IsTriedOn = true;
            _owner.NotifyTryOn();
        }
    }

    private bool CanCopyWhisper() => Listing.Whisper.Length > 0;

    [RelayCommand(CanExecute = nameof(CanCopyWhisper))]
    private async Task CopyWhisperAsync()
    {
        if (_owner.CopyToClipboardAsync is { } copy)
            await copy(Listing.Whisper);
    }
}
