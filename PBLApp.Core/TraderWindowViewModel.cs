using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PBLApp.Core.Localization;
using PBLEngine;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
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
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WindowTitle))]
    private string _displayName = "";

    /// <summary>Заголовок окна: «&lt;слот&gt; — Подбор», суффикс локализуется.</summary>
    public string WindowTitle =>
        string.Format(LocalizationService.Get("Trader_WindowTitle"), DisplayName);
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

    /// <summary>Пусто ли в списке результатов — для подсказки-заглушки области результатов.</summary>
    public bool HasResults => Results.Count > 0;

    /// <summary>Варианты сортировки результатов (как в PoB Trader).</summary>
    public IReadOnlyList<string> SortOptions { get; } =
    [
        LocalizationService.Get("Trader_SortValuePerDiv"),
        LocalizationService.Get("Trader_SortPrice"),
        LocalizationService.Get("Trader_SortValue"),
        LocalizationService.Get("Trader_SortDps"),
        LocalizationService.Get("Trader_SortEhp"),
    ];
    [ObservableProperty] private int _selectedSortIndex;
    partial void OnSelectedSortIndexChanged(int value) => SortResults();

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
        Results.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasResults));
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
        IEnumerable<TraderResultViewModel> sorted = SelectedSortIndex switch
        {
            // Цена: дешёвые сначала (в дивинах для кросс-валютного порядка, иначе сырая сумма).
            1 => Results.OrderBy(r => r.DivValue ?? r.Listing.Amount),
            2 => Results.OrderByDescending(r => r.StatValue ?? double.MinValue),
            3 => Results.OrderByDescending(r => r.DpsDiff ?? double.MinValue),
            4 => Results.OrderByDescending(r => r.EhpDiff ?? double.MinValue),
            // По умолчанию — прирост за цену, затем чистый прирост.
            _ => Results.OrderByDescending(r => r.ValuePerDiv ?? double.MinValue)
                        .ThenByDescending(r => r.StatValue ?? double.MinValue),
        };
        var list = sorted.ToList();
        Results.Clear();
        foreach (var r in list) Results.Add(r);
    }
}

/// <summary>Один результат поиска: цена, Δ-статы, примерка, whisper.</summary>
public partial class TraderResultViewModel : ViewModelBase
{
    private readonly TraderWindowViewModel _win;
    private readonly TraderSession _session;

    public LuaHost.TraderListing Listing { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DpsDiffColorHex))]
    private double? _dpsDiff;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EhpDiffColorHex))]
    private double? _ehpDiff;
    [ObservableProperty] private double? _statValue;
    [ObservableProperty] private bool _isTriedOn;

    /// <summary>Имя и база предмета из ItemText (в record TraderListing имени нет, движок не
    /// трогаем). Формат PoB: строка 0 = «Rarity: X», 1 = имя, 2 = база (для RARE/UNIQUE).</summary>
    public string ItemTitle { get; }
    public string ItemBase { get; }
    public bool HasBase => ItemBase.Length > 0;
    /// <summary>Хекс-цвет имени по редкости — как у слотов (Avalonia парсит строку в кисть).</summary>
    public string NameColorHex { get; }

    public string PriceText => $"{Listing.Amount:0.##} {TraderSession.CurrencyDisplay(Listing.Currency)}";

    public double? DivValue =>
        _session.RateFor(Listing.Currency) is { } rate ? Listing.Amount * rate : null;

    public double? ValuePerDiv =>
        StatValue is { } sv && DivValue is { } dv && dv > 0 ? sv / dv : null;

    /// <summary>Цвет дельты по знаку: зелёный прирост / красный спад / серый ноль.</summary>
    public string DpsDiffColorHex => SignColor(DpsDiff);
    public string EhpDiffColorHex => SignColor(EhpDiff);
    private static string SignColor(double? v) =>
        v is null || v.Value == 0 ? "#6E7689" : v.Value > 0 ? "#7FC78A" : "#D87171";

    /// <summary>Стилизованный hover-тултип (как у слотов): заголовок цветом редкости,
    /// метки/числа модов дифференцированы. Строим из ItemText — заголовок + строки модов
    /// (метадата вроде «Item Level:»/«Implicits:» отфильтрована, моды переведены).</summary>
    public HoverTooltipModel HoverTooltip => new(BuildHoverText(), NameColorHex);

    private string BuildHoverText()
    {
        var lines = Listing.ItemText.Split('\n',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var sb = new StringBuilder();
        sb.Append(HasBase ? $"{ItemTitle}, {ItemBase}" : ItemTitle);
        int start = HasBase ? 3 : 2; // пропустить Rarity + имя (+ база для RARE/UNIQUE/RELIC)
        for (int i = start; i < lines.Length; i++)
        {
            if (IsMetadataLine(lines[i])) continue;
            var clean = CleanModLine(lines[i]);
            if (clean.Length == 0) continue;
            sb.Append('\n').Append(GameTranslationService.Instance.TooltipLine(clean));
        }
        return sb.ToString();
    }

    // PoB-цветовые эскейпы (^xRRGGBB / ^d) и ведущие аннотации-теги
    // ([enchant]/{crafted}/(rune)/[desecrated]…) — чистим до перевода, иначе они
    // и в тултип протекают, и ломают сопоставление перевода мода.
    private static readonly Regex ColorRx = new(@"\^x[0-9A-Fa-f]{6}|\^[0-9]", RegexOptions.Compiled);
    private static readonly Regex LeadingTagRx =
        new(@"^(?:\s*(?:\[[^\]]*\]|\{[^}]*\}|\([^)]*\)))+\s*", RegexOptions.Compiled);

    private static string CleanModLine(string line)
    {
        line = ColorRx.Replace(line, "");
        line = LeadingTagRx.Replace(line, "");
        return line.Trim();
    }

    private static readonly string[] MetaPrefixes =
    [
        "Rarity:", "Item Level:", "Implicits:", "Requirements:", "Sockets:", "Level:",
        "Quality:", "Armour:", "Evasion Rating:", "Energy Shield:", "Ward:", "Stack Size:",
        "Radius:", "Limited to:", "Unique ID:", "Note:", "Corrupted", "LevelReq:", "Rune:",
    ];

    private static bool IsMetadataLine(string line)
    {
        if (line.Length == 0 || line[0] == '-') return true; // «--------» разделители
        foreach (var p in MetaPrefixes)
            if (line.StartsWith(p, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    public TraderResultViewModel(TraderWindowViewModel win, TraderSession session, LuaHost.TraderListing listing)
    {
        _win = win;
        _session = session;
        Listing = listing;

        var (rarity, title, baseName) = ParseName(listing.ItemText);
        ItemTitle = title;
        ItemBase = baseName;
        NameColorHex = ItemSlotViewModel.RarityToColor(rarity);
    }

    private static (string rarity, string title, string baseName) ParseName(string itemText)
    {
        var lines = itemText.Split('\n',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length >= 2 && lines[0].StartsWith("Rarity:", StringComparison.OrdinalIgnoreCase))
        {
            var rarity = lines[0]["Rarity:".Length..].Trim().ToUpperInvariant();
            // RARE/UNIQUE/RELIC: строка 1 = имя, строка 2 = база. NORMAL/MAGIC: строка 1 —
            // уже базовое/магическое имя, отдельной базы нет.
            var hasSeparateBase = rarity is "RARE" or "UNIQUE" or "RELIC" && lines.Length >= 3;
            var title = TraderSession.TranslateItemName(lines[1]);
            var baseName = hasSeparateBase ? TraderSession.TranslateItemName(lines[2]) : "";
            return (rarity, title, baseName);
        }
        return ("", lines.Length > 0 ? lines[0] : "", "");
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
