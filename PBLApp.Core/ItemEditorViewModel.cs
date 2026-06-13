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
using System.Text.RegularExpressions;

namespace PBLApp.ViewModels;

// ─────────────────────────────────────────────────────────────────────────────
// ExplicitModViewModel  — one row in the "Selected mods" list
// ─────────────────────────────────────────────────────────────────────────────

public sealed partial class ExplicitModViewModel : ObservableObject
{
    private readonly ItemEditorViewModel _editor;

    [ObservableProperty] private string _text;

    public string AffixType  { get; }
    public string AffixLabel { get; }
    public string Group      { get; }   // for duplicate prevention

    public string TypeColor  => AffixType == "Prefix" ? "#89B4FA"
                              : AffixType == "Suffix" ? "#A6E3A1"
                              : "Transparent";

    /// <summary>Unified one-letter badge for the mod row (P/S/I/C or empty).</summary>
    public string TypeBadgeLabel =>
        IsCorruption            ? "C" :
        IsImplicit              ? "I" :
        AffixType == "Prefix"   ? "P" :
        AffixType == "Suffix"   ? "S" : "";

    /// <summary>Color used for the unified badge.</summary>
    public string TypeBadgeColor =>
        IsCorruption            ? "#DD0022" :       // red — corruption
        IsImplicit              ? "#74C7EC" :       // cyan — implicit
        AffixType == "Prefix"   ? "#89B4FA" :       // blue — prefix
        AffixType == "Suffix"   ? "#A6E3A1" :       // green — suffix
        "Transparent";

    /// <summary>True for mods added via the Corrupted-implicit picker. Used to paint
    /// the row in a distinct way (red badge + crimson tint) so corruption looks
    /// dangerous and unambiguous next to regular affixes.</summary>
    public bool IsCorruption { get; init; }

    /// <summary>Brush key for the row's background when corruption — gives the entire
    /// mod line a faint crimson tint so the picker → list pipeline is obviously
    /// "this is a corruption implicit, not a normal affix".</summary>
    public string RowBackgroundColor => IsCorruption ? "#2A1015" : "Transparent";

    /// <summary>Border colour for the corruption row's left edge — bold accent.</summary>
    public string RowAccentColor => IsCorruption ? "#DD0022" : "Transparent";

    // ── Slider (only for mods with exactly one integer range) ────────────────

    public bool   HasSlider  { get; }
    public double SliderMin  { get; }
    public double SliderMax  { get; }
    public string SliderLabel => $"{(int)SliderMin}–{(int)SliderMax}";

    private readonly string? _template;  // e.g. "+{0} to Strength"
    private bool _isFloatRange;          // true if the range bounds aren't both integers

    [ObservableProperty] private double _sliderValue;

    private string FormatSliderValue(double v) =>
        _isFloatRange ? v.ToString("0.##", CultureInfo.InvariantCulture)
                      : ((int)Math.Round(v)).ToString(CultureInfo.InvariantCulture);

    partial void OnSliderValueChanged(double val)
    {
        if (HasSlider && _template is not null)
            Text = string.Format(_template, FormatSliderValue(val));
    }

    // ── Split-text parts for colored value display ──────────────────────────

    public string PartBefore     { get; private set; } = "";
    public string PartNumber     { get; private set; } = "";
    public string PartAfter      { get; private set; } = "";
    public bool   HasColoredValue => HasSlider && !string.IsNullOrEmpty(PartNumber);

    // ── Localised display ────────────────────────────────────────────────────

    /// <summary>Russian-localised full mod line (for non-slider mods).</summary>
    public string TranslatedText =>
        PBLApp.Core.Localization.GameTranslationService.Instance.TooltipLine(Text);

    public string TranslatedPartBefore => ComputeTranslatedPart(after: false);
    public string TranslatedPartAfter  => ComputeTranslatedPart(after: true);

    private string ComputeTranslatedPart(bool after)
    {
        if (!HasSlider) return "";
        var translated = TranslatedText;
        if (string.IsNullOrEmpty(PartNumber))
            return after ? "" : translated;
        var m = Regex.Match(translated, @"-?\d+(?:\.\d+)?");
        if (!m.Success) return after ? "" : translated;
        return after ? translated[(m.Index + m.Length)..] : translated[..m.Index];
    }

    partial void OnTextChanged(string val)
    {
        UpdateTextParts(val);
        OnPropertyChanged(nameof(PartBefore));
        OnPropertyChanged(nameof(PartNumber));
        OnPropertyChanged(nameof(PartAfter));
        OnPropertyChanged(nameof(HasColoredValue));
        OnPropertyChanged(nameof(TranslatedText));
        OnPropertyChanged(nameof(TranslatedPartBefore));
        OnPropertyChanged(nameof(TranslatedPartAfter));
        _editor?.NotifyEffectiveStatsChanged();
    }

    private void UpdateTextParts(string text)
    {
        if (!HasSlider)
        {
            PartBefore = text; PartNumber = ""; PartAfter = "";
            return;
        }
        var m = Regex.Match(text, @"\d+");
        if (m.Success)
        {
            PartBefore = text[..m.Index];
            PartNumber = m.Value;
            PartAfter  = text[(m.Index + m.Length)..];
        }
        else
        {
            PartBefore = text; PartNumber = ""; PartAfter = "";
        }
    }

    public IRelayCommand RemoveCommand { get; }

    // ── Unique-mod metadata (preserved across save) ──────────────────────────
    /// <summary>Original raw line (with `{range:X}` and other PoB prefixes) for unique items.</summary>
    private readonly string? _rawLineForSave;
    /// <summary>True if this mod represents a unique-item entry with editable {range:X}.</summary>
    public bool IsUniqueRangeMod => _rawLineForSave is not null;
    /// <summary>For unique mods, whether this line is part of the item's implicit block.</summary>
    public bool IsImplicit { get; }
    /// <summary>True if this mod row should not be user-removable (uniques + implicits are fixed).</summary>
    public bool IsRemovable => !IsUniqueRangeMod && !IsImplicit;

    /// <summary>Current normalized range fraction in [0,1] derived from the slider.</summary>
    public double RangeFraction =>
        SliderMax > SliderMin ? (SliderValue - SliderMin) / (SliderMax - SliderMin) : 0.5;

    public ExplicitModViewModel(ItemEditorViewModel editor, string text,
                                string affixType = "", string affixName = "",
                                string group = "", string originalStatText = "",
                                string? rawLineForSave = null,
                                double? initialRangeFraction = null,
                                bool isImplicit = false)
    {
        _editor       = editor;
        _text         = text;
        AffixType     = affixType;
        AffixLabel    = affixType switch { "Prefix" => "P", "Suffix" => "S", _ => "" };
        Group         = group;
        IsImplicit    = isImplicit;
        _rawLineForSave = rawLineForSave;

        // Try to build a slider from the original stat text (single integer range only)
        var src = string.IsNullOrEmpty(originalStatText) ? text : originalStatText;
        var range = ParseSingleIntRange(src);
        if (range is not null)
        {
            HasSlider = true;
            SliderMin = range.Value.min;
            SliderMax = range.Value.max;
            _template = range.Value.template;
            _isFloatRange = Math.Floor(range.Value.min) != range.Value.min
                         || Math.Floor(range.Value.max) != range.Value.max;

            if (initialRangeFraction.HasValue)
            {
                // Unique-mod path: position slider from the {range:X} fraction in the raw line.
                _sliderValue = range.Value.min
                             + Math.Clamp(initialRangeFraction.Value, 0.0, 1.0)
                               * (range.Value.max - range.Value.min);
                // Rewrite display text to the rolled value
                _text = string.Format(_template, FormatSliderValue(_sliderValue));
            }
            else
            {
                // Use the current rolled value from 'text' as the slider's initial position.
                var numMatch = Regex.Match(text, @"-?\d+(?:\.\d+)?");
                if (numMatch.Success
                    && double.TryParse(numMatch.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double currentVal)
                    && currentVal >= range.Value.min && currentVal <= range.Value.max)
                    _sliderValue = currentVal;
                else
                    _sliderValue = range.Value.max;
            }
        }

        // Initialise split text parts (backing field set directly — no property change event)
        UpdateTextParts(_text);

        RemoveCommand = new RelayCommand(() => _editor.RemoveExplicitMod(this));
    }

    /// <summary>For unique mods: returns the raw line to write into the item raw, with the
    /// current `{range:X}` substituted. For other mods, just returns Text.</summary>
    public string BuildRawLineForSave()
    {
        if (_rawLineForSave is null) return Text;
        var rf = Math.Round(RangeFraction, 3)
                     .ToString("0.###", CultureInfo.InvariantCulture);
        // Strip ALL existing {range:X} markers anywhere in the line (PoB places the
        // range marker before each {variant:N} group; double-marker raws break parsing).
        var cleaned = Regex.Replace(_rawLineForSave, @"\{range:[^}]+\}", "");
        // Strip {variant:N} markers as well. The editor only emits mods for the currently
        // selected variant; preserving the variant prefix on save then leaves the parsed
        // item without a Selected Variant marker, and PoB's CheckModLineVariant drops the
        // mods (silent disappearance of e.g. Bones of Ullr's mods after edit + save).
        cleaned = Regex.Replace(cleaned, @"\{variant:[^}]+\}", "");
        return $"{{range:{rf}}}{cleaned}";
    }

    /// <summary>Returns (min, max, template) if stat has exactly one (N-M) numeric range.
    /// Supports integer and float bounds (e.g. "(0.5-1.5)%").</summary>
    private static (double min, double max, string template)? ParseSingleIntRange(string stat)
    {
        var matches = Regex.Matches(stat, @"\((-?\d+(?:\.\d+)?)-(-?\d+(?:\.\d+)?)\)");
        if (matches.Count != 1) return null;
        var m = matches[0];
        if (!double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double min)) return null;
        if (!double.TryParse(m.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double max)) return null;
        string template = stat[..m.Index] + "{0}" + stat[(m.Index + m.Length)..];
        return (min, max, template);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// AffixEntryViewModel  — one row in the "Available mods" picker
// ─────────────────────────────────────────────────────────────────────────────

public sealed class AffixEntryViewModel
{
    private readonly ItemEditorViewModel _editor;
    public AffixEntry Entry      { get; }
    public string TypeColor => Entry.AffixType == "Prefix" ? "#89B4FA" : "#A6E3A1";
    public string TypeLabel => Entry.AffixType == "Prefix" ? "P" : "S";
    /// <summary>Localised stat text shown in the affix picker.</summary>
    public string TranslatedStatText =>
        PBLApp.Core.Localization.GameTranslationService.Instance.TooltipLine(Entry.StatText);
    public IRelayCommand AddCommand { get; }

    public AffixEntryViewModel(ItemEditorViewModel editor, AffixEntry entry)
    {
        _editor    = editor;
        Entry      = entry;
        AddCommand = new RelayCommand(
            () =>
            {
                if (_editor.IsCorruptionPickerMode) _editor.AddCorruptionAffix(entry);
                else _editor.AddAffix(entry);
            },
            () => _editor.IsCorruptionPickerMode || _editor.CanAddAffix(entry));
    }

    public void RefreshCanAdd() => (AddCommand as RelayCommand)?.NotifyCanExecuteChanged();
}

// ─────────────────────────────────────────────────────────────────────────────
// RuneSocketViewModel  — one rune/idol slot row in the editor
// ─────────────────────────────────────────────────────────────────────────────

public sealed partial class RuneSocketViewModel : ObservableObject
{
    private readonly ItemEditorViewModel _editor;
    public int Index { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty), nameof(DisplayName), nameof(ModSummary))]
    private RuneEntry? _selectedRune;

    public bool   IsEmpty       => SelectedRune is null;
    public string DisplayName   => SelectedRune is null
        ? "—"
        : PBLApp.Core.Localization.GameTranslationService.Instance.Rune(SelectedRune.Name);
    public string ModSummary    => SelectedRune is null
        ? ""
        : string.Join(" • ",
            _editor.GetRuneModsFor(SelectedRune)
                .Select(m => PBLApp.Core.Localization.GameTranslationService.Instance.TooltipLine(m)));

    /// <summary>Runes compatible with the current item base type.</summary>
    public IReadOnlyList<RuneEntry> AvailableRunes => _editor.CompatibleRunes;

    public RelayCommand            ClearCommand   { get; }
    public RelayCommand<RuneEntry> PickCommand    { get; }

    public RuneSocketViewModel(ItemEditorViewModel editor, int index, RuneEntry? initial)
    {
        _editor       = editor;
        Index         = index;
        _selectedRune = initial;
        ClearCommand  = new RelayCommand(() => SelectedRune = null);
        PickCommand   = new RelayCommand<RuneEntry>(r => SelectedRune = r);
    }

    partial void OnSelectedRuneChanged(RuneEntry? value) => _editor.RaisePropertyChanged(nameof(ItemEditorViewModel.HasAnyRunes));

    public void RaisePropertyChanged(string name) => OnPropertyChanged(name);
}

// ─────────────────────────────────────────────────────────────────────────────
// ItemEditorViewModel
// ─────────────────────────────────────────────────────────────────────────────

public partial class ItemEditorViewModel : ViewModelBase
{
    private readonly LuaHost               _host;
    private readonly Action<ItemEditorViewModel, bool> _onClose;  // bool = saved
    private readonly int?                  _editingItemId;    // null = create new
    private string                         _fallbackBaseName = "";   // used when SelectedBase is null

    // ── Rarity ──────────────────────────────────────────────────────────────

    public enum ItemRarity { Normal, Magic, Rare, Unique }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNormal), nameof(IsMagic), nameof(IsRare),
                              nameof(IsUnique), nameof(IsNotUnique),
                              nameof(MaxPrefixes), nameof(MaxSuffixes),
                              nameof(PrefixSuffixLabel),
                              nameof(DisplayName), nameof(DisplayBaseName), nameof(RarityColor))]
    private ItemRarity _rarity = ItemRarity.Rare;

    /// <summary>Top-line item name as shown in the tooltip header.
    /// For uniques: the unique's title. Otherwise: rarity-translated base name fallback.</summary>
    public string DisplayName =>
        IsUnique && SelectedUnique is not null
            ? PBLApp.Core.Localization.GameTranslationService.Instance.Unique(SelectedUnique.Name)
            : DisplayBaseName;

    /// <summary>Base name in the tooltip subtitle (localised via items dictionary).</summary>
    public string DisplayBaseName =>
        PBLApp.Core.Localization.GameTranslationService.Instance.Item(
            IsUnique && SelectedUnique is not null ? SelectedUnique.BaseName
            : SelectedBase?.Name ?? _fallbackBaseName);

    /// <summary>Hex colour for the title text, by rarity (PoE convention).</summary>
    public string RarityColor => Rarity switch
    {
        ItemRarity.Normal => "#C8C8C8",
        ItemRarity.Magic  => "#8888FF",
        ItemRarity.Rare   => "#FFFF77",
        ItemRarity.Unique => "#AF6025",
        _                 => "#C8C8C8"
    };

    public bool IsNormal    => Rarity == ItemRarity.Normal;
    public bool IsMagic     => Rarity == ItemRarity.Magic;
    public bool IsRare      => Rarity == ItemRarity.Rare;
    public bool IsUnique    => Rarity == ItemRarity.Unique;
    public bool IsNotUnique => !IsUnique;
    public int  MaxPrefixes => Rarity switch { ItemRarity.Rare => 3, ItemRarity.Magic => 1, _ => 0 };
    public int  MaxSuffixes => Rarity switch { ItemRarity.Rare => 3, ItemRarity.Magic => 1, _ => 0 };
    public string PrefixSuffixLabel =>
        string.Format(LocalizationService.Get("Editor_PrefixSuffixLabel"),
                      PrefixCount, MaxPrefixes, SuffixCount, MaxSuffixes);

    // ── Base selection ───────────────────────────────────────────────────────

    public ObservableCollection<string> Categories  { get; } = [];
    public ObservableCollection<BaseItemEntry> Bases { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilteredBases))]
    private string _selectedCategory = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilteredBases))]
    private string _baseSearch = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName), nameof(DisplayBaseName))]
    private BaseItemEntry? _selectedBase;

    public IEnumerable<BaseItemEntry> FilteredBases =>
        string.IsNullOrWhiteSpace(BaseSearch)
            ? Bases
            : Bases.Where(b =>
                b.Name.Contains(BaseSearch, StringComparison.OrdinalIgnoreCase) ||
                GameTranslationService.Instance.Item(b.Name)
                    .Contains(BaseSearch, StringComparison.OrdinalIgnoreCase));

    // ── Item details ────────────────────────────────────────────────────────

    [ObservableProperty] private int _itemLevel = 80;

    /// <summary>Item quality in percent (0–30). Emitted as `Quality: N` in raw text.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EffectiveArmour), nameof(EffectiveEvasion),
                              nameof(EffectiveEnergyShield), nameof(EffectiveWard))]
    private int _quality = 0;

    /// <summary>Innate quality% baked into the base item (PoE2 bases ship with base.quality, usually 20).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EffectiveArmour), nameof(EffectiveEvasion),
                              nameof(EffectiveEnergyShield), nameof(EffectiveWard))]
    private int _baseQualityPct = 0;

    /// <summary>Whether the item is corrupted. Cosmetic flag only — does NOT lock
    /// mod editing here (PoB game semantics would, but for build planning we want
    /// the user to keep tweaking sliders / removing mods on corrupted gear).</summary>
    [ObservableProperty]
    private bool _isCorrupted = false;

    /// <summary>Mod editing is always allowed in the planner; kept as a property
    /// so existing bindings continue to compile and any future read-only logic
    /// has a single switch to flip.</summary>
    public bool CanEditMods => true;

    // ── Base defence / spirit / charm slot stats ─────────────────────────────
    // Values default to 0 (= "not present in raw"). The UI hides rows where
    // both BaseX == 0 AND HasX == false.

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasArmour), nameof(HasAnyBaseStat), nameof(IsArmourOverridden))]
    private int _baseArmour = 0;
    public bool HasArmour => BaseArmour > 0 || DefaultArmour > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEvasion), nameof(HasAnyBaseStat), nameof(IsEvasionOverridden))]
    private int _baseEvasion = 0;
    public bool HasEvasion => BaseEvasion > 0 || DefaultEvasion > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEnergyShield), nameof(HasAnyBaseStat), nameof(IsEnergyShieldOverridden))]
    private int _baseEnergyShield = 0;
    public bool HasEnergyShield => BaseEnergyShield > 0 || DefaultEnergyShield > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasWard), nameof(HasAnyBaseStat), nameof(IsWardOverridden))]
    private int _baseWard = 0;
    public bool HasWard => BaseWard > 0 || DefaultWard > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSpirit), nameof(HasAnyBaseStat), nameof(IsSpiritOverridden))]
    private int _baseSpirit = 0;
    public bool HasSpirit => BaseSpirit > 0 || DefaultSpirit > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCharmSlots), nameof(HasAnyBaseStat), nameof(IsCharmSlotsOverridden))]
    private int _baseCharmSlots = 0;
    public bool HasCharmSlots => BaseCharmSlots > 0 || DefaultCharmSlots > 0;

    /// <summary>True if any base stat is shown (drives section visibility).</summary>
    public bool HasAnyBaseStat =>
        HasArmour || HasEvasion || HasEnergyShield || HasWard || HasSpirit || HasCharmSlots
        || DefaultArmour > 0 || DefaultEvasion > 0 || DefaultEnergyShield > 0
        || DefaultWard > 0 || DefaultSpirit > 0 || DefaultCharmSlots > 0;

    // ── Effective (calculated) base stat values ──────────────────────────────
    // Formula: default * (1 + baseQuality/100 + userQuality/100 + sumOfPercentMods/100)
    // Percent mods are summed by scanning ExplicitMods for shapes like
    // "X% increased Armour", "X% increased Armour and Energy Shield", etc.

    /// <summary>Sum of percent-increase mods that apply to the given defence stat ("Armour"/"Evasion"/"Energy Shield").</summary>
    private double SumPercentModsFor(params string[] stats)
    {
        double sum = 0;
        var rx = new System.Text.RegularExpressions.Regex(@"(\d+)%\s+(?:увелич\.|increased)\s+(.+?)(?:$|[,;])",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        foreach (var m in ExplicitMods)
        {
            // ExplicitMods.Text is the english source; check both english+russian markers via Text.
            foreach (System.Text.RegularExpressions.Match match in rx.Matches(m.Text))
            {
                if (!int.TryParse(match.Groups[1].Value, out int pct)) continue;
                var what = match.Groups[2].Value.ToLowerInvariant();
                foreach (var s in stats)
                {
                    var sl = s.ToLowerInvariant();
                    if (what.Contains(sl)) { sum += pct; break; }
                }
            }
        }
        return sum;
    }

    private int ComputeEffective(int baseValue, params string[] stats)
    {
        if (baseValue <= 0) return 0;
        double pct = (BaseQualityPct + Quality + SumPercentModsFor(stats)) / 100.0;
        return (int)Math.Round(baseValue * (1.0 + pct));
    }

    public int EffectiveArmour       => ComputeEffective(DefaultArmour,       "armour", "armour and", "global defences");
    public int EffectiveEvasion      => ComputeEffective(DefaultEvasion,      "evasion", "armour and", "global defences");
    public int EffectiveEnergyShield => ComputeEffective(DefaultEnergyShield, "energy shield", "armour and", "global defences");
    public int EffectiveWard         => ComputeEffective(DefaultWard,         "ward");

    /// <summary>Notify the UI that effective stats may have changed (called when a mod row's text changes).</summary>
    public void NotifyEffectiveStatsChanged()
    {
        OnPropertyChanged(nameof(EffectiveArmour));
        OnPropertyChanged(nameof(EffectiveEvasion));
        OnPropertyChanged(nameof(EffectiveEnergyShield));
        OnPropertyChanged(nameof(EffectiveWard));
    }

    // ── Intrinsic base defaults (from data.itemBases[baseName]) ──────────────
    // These are read-only, source-of-truth values for the selected base item.
    // They serve as the placeholder/comparison for the editable Base* override.

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasArmour), nameof(HasAnyBaseStat), nameof(IsArmourOverridden), nameof(EffectiveArmour), nameof(DefaultArmourHint))]
    private int _defaultArmour = 0;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEvasion), nameof(HasAnyBaseStat), nameof(IsEvasionOverridden), nameof(EffectiveEvasion), nameof(DefaultEvasionHint))]
    private int _defaultEvasion = 0;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEnergyShield), nameof(HasAnyBaseStat), nameof(IsEnergyShieldOverridden), nameof(EffectiveEnergyShield), nameof(DefaultEnergyShieldHint))]
    private int _defaultEnergyShield = 0;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasWard), nameof(HasAnyBaseStat), nameof(IsWardOverridden), nameof(EffectiveWard), nameof(DefaultWardHint))]
    private int _defaultWard = 0;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSpirit), nameof(HasAnyBaseStat), nameof(IsSpiritOverridden))]
    private int _defaultSpirit = 0;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCharmSlots), nameof(HasAnyBaseStat), nameof(IsCharmSlotsOverridden))]
    private int _defaultCharmSlots = 0;

    // Localized "(base: N)" hints shown next to each defence stat row.
    public string DefaultArmourHint       => string.Format(LocalizationService.Get("Editor_BaseValueHint"), DefaultArmour);
    public string DefaultEvasionHint      => string.Format(LocalizationService.Get("Editor_BaseValueHint"), DefaultEvasion);
    public string DefaultEnergyShieldHint => string.Format(LocalizationService.Get("Editor_BaseValueHint"), DefaultEnergyShield);
    public string DefaultWardHint         => string.Format(LocalizationService.Get("Editor_BaseValueHint"), DefaultWard);

    /// <summary>True if the editor's value diverges from the intrinsic default → emit override on save.</summary>
    public bool IsArmourOverridden       => BaseArmour       > 0 && BaseArmour       != DefaultArmour;
    public bool IsEvasionOverridden      => BaseEvasion      > 0 && BaseEvasion      != DefaultEvasion;
    public bool IsEnergyShieldOverridden => BaseEnergyShield > 0 && BaseEnergyShield != DefaultEnergyShield;
    public bool IsWardOverridden         => BaseWard         > 0 && BaseWard         != DefaultWard;
    public bool IsSpiritOverridden       => BaseSpirit       > 0 && BaseSpirit       != DefaultSpirit;
    public bool IsCharmSlotsOverridden   => BaseCharmSlots   > 0 && BaseCharmSlots   != DefaultCharmSlots;

    /// <summary>Loads intrinsic stats from data.itemBases for the given base name.</summary>
    private void LoadBaseDefaults(string baseName)
    {
        if (string.IsNullOrEmpty(baseName)) return;
        try
        {
            var d = _host.GetBaseDefaults(baseName);
            DefaultArmour       = d.Armour;
            DefaultEvasion      = d.Evasion;
            DefaultEnergyShield = d.EnergyShield;
            DefaultWard         = d.Ward;
            DefaultSpirit       = d.Spirit;
            DefaultCharmSlots   = d.CharmSlots;
            BaseQualityPct      = d.BaseQualityPct;
            // If editor's override slots are empty, prime them with defaults so the user
            // sees the actual base values and can edit them directly.
            if (BaseArmour       == 0) BaseArmour       = DefaultArmour;
            if (BaseEvasion      == 0) BaseEvasion      = DefaultEvasion;
            if (BaseEnergyShield == 0) BaseEnergyShield = DefaultEnergyShield;
            if (BaseWard         == 0) BaseWard         = DefaultWard;
            if (BaseSpirit       == 0) BaseSpirit       = DefaultSpirit;
            if (BaseCharmSlots   == 0) BaseCharmSlots   = DefaultCharmSlots;
        }
        catch { /* base lookup is best-effort */ }
    }

    // ── Runes / Idols ────────────────────────────────────────────────────────

    /// <summary>All runes loaded from data.itemMods.Runes (sorted alphabetically).</summary>
    private List<RuneEntry> _allRunes = [];

    /// <summary>Subset of <see cref="_allRunes"/> applicable to the current base type.</summary>
    public List<RuneEntry> CompatibleRunes { get; private set; } = [];

    public ObservableCollection<RuneSocketViewModel> RuneSockets { get; } = [];

    /// <summary>Maximum number of rune sockets allowed for the selected base.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAddRune), nameof(HasRuneSockets))]
    private int _maxRunes = 0;

    public bool HasRuneSockets => MaxRunes > 0;
    public bool CanAddRune     => RuneSockets.Count < MaxRunes && CanEditMods;
    public bool HasAnyRunes    => RuneSockets.Any(s => s.SelectedRune is not null);

    /// <summary>Resolves the slot-type for the current base ("helmet" / "boots" / etc).</summary>
    private string CurrentSlotType()
    {
        // For uniques the BaseItemEntry isn't selected — use the unique's Category
        // (which Lua already sets to item.base.type when listing uniques).
        var t = (SelectedBase?.Type
              ?? SelectedUnique?.Category
              ?? _fallbackBaseType ?? "").ToLowerInvariant();
        if (string.IsNullOrEmpty(t)) return "";
        // PoB rune table uses lowercased base.type plus generic categories (weapon/armour/caster).
        return t;
    }

    /// <summary>Returns the mod lines this rune contributes when slotted into the current base.</summary>
    public IReadOnlyList<string> GetRuneModsFor(RuneEntry rune)
    {
        var slotType = CurrentSlotType();
        if (string.IsNullOrEmpty(slotType)) return [];
        if (rune.ModsByType.TryGetValue(slotType, out var direct)) return direct;
        // Fall back through generic categories ordered by what the slot type is:
        // armour pieces (boots/helmet/gloves/body/belt) -> "armour" before "weapon";
        // weapons -> "weapon" first; focuses/casters -> their own first.
        string[] generics = slotType switch
        {
            "boots" or "helmet" or "gloves" or "body armour" or "belt" or "armour" or "shield"
                => ["armour", "caster", "weapon", "focus"],
            "focus"  => ["focus", "caster", "armour", "weapon"],
            "caster" => ["caster", "weapon", "armour", "focus"],
            _        => ["weapon", "armour", "caster", "focus"]
        };
        foreach (var g in generics)
            if (rune.ModsByType.TryGetValue(g, out var gm)) return gm;
        return [];
    }

    /// <summary>Refreshes <see cref="CompatibleRunes"/> based on the current base type.</summary>
    private void RefreshCompatibleRunes()
    {
        var slotType = CurrentSlotType();
        CompatibleRunes = string.IsNullOrEmpty(slotType)
            ? []
            : _allRunes.Where(r => r.ModsByType.ContainsKey(slotType)
                                || r.ModsByType.ContainsKey("weapon")
                                || r.ModsByType.ContainsKey("armour")
                                || r.ModsByType.ContainsKey("caster"))
                       .Where(r => GetRuneModsFor(r).Count > 0)
                       .ToList();
        OnPropertyChanged(nameof(CompatibleRunes));
        foreach (var s in RuneSockets) s.RaisePropertyChanged(nameof(RuneSocketViewModel.AvailableRunes));
    }

    private string? _fallbackBaseType;   // captured from raw when SelectedBase not available

    /// <summary>Exposes protected OnPropertyChanged so child VMs can poke us.</summary>
    public void RaisePropertyChanged(string name) => OnPropertyChanged(name);

    public RelayCommand AddRuneSocketCommand    { get; private set; } = null!;
    public RelayCommand<RuneSocketViewModel> RemoveRuneSocketCommand { get; private set; } = null!;

    // ── Unique selector ─────────────────────────────────────────────────────

    private List<UniqueItemEntry> _allUniques = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilteredUniques))]
    private string _uniqueSearch = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName), nameof(DisplayBaseName))]
    private UniqueItemEntry? _selectedUnique;

    public IEnumerable<UniqueItemEntry> FilteredUniques =>
        string.IsNullOrWhiteSpace(UniqueSearch)
            ? _allUniques
            : _allUniques.Where(u =>
                u.Name.Contains(UniqueSearch, StringComparison.OrdinalIgnoreCase) ||
                u.BaseName.Contains(UniqueSearch, StringComparison.OrdinalIgnoreCase) ||
                GameTranslationService.Instance.Unique(u.Name)
                    .Contains(UniqueSearch, StringComparison.OrdinalIgnoreCase) ||
                GameTranslationService.Instance.Item(u.BaseName)
                    .Contains(UniqueSearch, StringComparison.OrdinalIgnoreCase));

    // ── Implicits (read-only from base; unique items put theirs in ExplicitMods) ────

    public ObservableCollection<string> ImplicitMods { get; } = [];

    // ── Explicit mods (selected) ─────────────────────────────────────────────

    public ObservableCollection<ExplicitModViewModel> ExplicitMods { get; } = [];

    public int PrefixCount => ExplicitMods.Count(m => m.AffixType == "Prefix");
    public int SuffixCount => ExplicitMods.Count(m => m.AffixType == "Suffix");

    // ── Mod type filter helpers ──────────────────────────────────────────────

    public bool IsFilterAll    => ModTypeFilter == "All";
    public bool IsFilterPrefix => ModTypeFilter == "Prefix";
    public bool IsFilterSuffix => ModTypeFilter == "Suffix";

    // ── Mod picker ───────────────────────────────────────────────────────────

    private List<AffixEntryViewModel> _allAffixes = [];
    private List<AffixEntryViewModel> _corruptionAffixes = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilteredAffixes))]
    private string _modSearch = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilteredAffixes), nameof(IsFilterAll), nameof(IsFilterPrefix), nameof(IsFilterSuffix))]
    private string _modTypeFilter = "All";  // "All" / "Prefix" / "Suffix"

    /// <summary>True while the user is interacting with the corruption picker — kept as a
    /// flag so AddCommand on AffixEntryViewModel can branch (corruption vs regular add).</summary>
    public bool IsCorruptionPickerMode => IsCorruptionPickerOpen;

    public IEnumerable<AffixEntryViewModel> FilteredAffixes
    {
        get
        {
            var q = _allAffixes.Where(a => CanAddAffix(a.Entry));
            if (ModTypeFilter != "All")
                q = q.Where(a => a.Entry.AffixType == ModTypeFilter);
            if (!string.IsNullOrWhiteSpace(ModSearch))
                q = q.Where(a =>
                    a.Entry.StatText.Contains(ModSearch, StringComparison.OrdinalIgnoreCase) ||
                    a.Entry.AffixName.Contains(ModSearch, StringComparison.OrdinalIgnoreCase) ||
                    a.Entry.Group.Contains(ModSearch, StringComparison.OrdinalIgnoreCase));
            return q.Take(200);
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilteredCorruptionAffixes))]
    private string _corruptionSearch = "";

    /// <summary>Separate filtered list for the dedicated corruption picker — keeps the
    /// regular affix picker logic intact and avoids cap / Prefix-Suffix filter rules
    /// that don't apply to corruption implicits.</summary>
    public IEnumerable<AffixEntryViewModel> FilteredCorruptionAffixes
    {
        get
        {
            IEnumerable<AffixEntryViewModel> q = _corruptionAffixes;
            if (!string.IsNullOrWhiteSpace(CorruptionSearch))
                q = q.Where(a =>
                    a.Entry.StatText.Contains(CorruptionSearch, StringComparison.OrdinalIgnoreCase) ||
                    a.Entry.AffixName.Contains(CorruptionSearch, StringComparison.OrdinalIgnoreCase) ||
                    a.Entry.Group.Contains(CorruptionSearch, StringComparison.OrdinalIgnoreCase));
            return q.Take(400);
        }
    }

    // ── Affix picker visibility (collapsed by default — toggled via "+ Add Mod") ──

    [ObservableProperty] private bool _isAffixPickerOpen = false;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCorruptionPickerMode))]
    private bool _isCorruptionPickerOpen = false;

    [RelayCommand]
    private void OpenCorruptionPicker()
    {
        var baseName = SelectedBase?.Name
                    ?? SelectedUnique?.BaseName
                    ?? _fallbackBaseName;
        if (!string.IsNullOrEmpty(baseName))
        {
            var entries = _host.GetItemCorruptedAffixes(baseName);
            _corruptionAffixes = entries
                .Select(e => new AffixEntryViewModel(this, e))
                .ToList();
        }
        IsCorruptionPickerOpen = !IsCorruptionPickerOpen;
        // Close the regular picker so the two don't overlap visually.
        if (IsCorruptionPickerOpen) IsAffixPickerOpen = false;
        CorruptionSearch = "";
        OnPropertyChanged(nameof(FilteredCorruptionAffixes));
    }

    /// <summary>Adds a corruption mod into ExplicitMods as an implicit row. Called by
    /// AffixEntryViewModel.AddCommand when the picker is in corruption mode.</summary>
    public void AddCorruptionAffix(AffixEntry e)
    {
        var text = MaxRollText(e.StatText);
        ExplicitMods.Insert(0, new ExplicitModViewModel(this, text,
            originalStatText: e.StatText,
            affixType:        "",
            isImplicit:       false)
        {
            IsCorruption = true,
        });
        IsCorruptionPickerOpen = false;
        SaveError = "";
    }

    // ── Save / delete state ──────────────────────────────────────────────────

    [ObservableProperty] private string _saveError = "";

    public int?   EditingItemId     => _editingItemId;
    public bool   IsEditingExisting => _editingItemId.HasValue;
    public string EditorTitle       => LocalizationService.Get(
        _editingItemId.HasValue ? "Editor_TitleEdit" : "Editor_TitleCreate");

    // Injected delete action — set by ItemsTabViewModel after construction
    private Action? _onDeleteItem;
    public RelayCommand? DeleteCommand { get; private set; }
    public bool CanDelete => DeleteCommand is not null;

    public void SetDeleteAction(Action action)
    {
        _onDeleteItem = action;
        DeleteCommand = new RelayCommand(action);
        OnPropertyChanged(nameof(DeleteCommand));
        OnPropertyChanged(nameof(CanDelete));
    }

    // ── Commands ─────────────────────────────────────────────────────────────

    public RelayCommand CancelCommand        { get; }
    public RelayCommand SaveCommand          { get; }
    public RelayCommand ToggleAffixPickerCommand { get; }

    // Rarity setters
    public RelayCommand SetNormalCommand     { get; }
    public RelayCommand SetMagicCommand      { get; }
    public RelayCommand SetRareCommand       { get; }
    public RelayCommand SetUniqueCommand     { get; }

    // Mod-type filter setters
    public RelayCommand SetFilterAllCommand    { get; }
    public RelayCommand SetFilterPrefixCommand { get; }
    public RelayCommand SetFilterSuffixCommand { get; }

    // Base / Unique selection via command parameter
    public RelayCommand<BaseItemEntry>   SelectBaseCommand   { get; }
    public RelayCommand<UniqueItemEntry> SelectUniqueCommand { get; }

    // ── Constructor ──────────────────────────────────────────────────────────

    /// <summary>Create new item editor.</summary>
    public ItemEditorViewModel(LuaHost host, Action<ItemEditorViewModel, bool> onClose)
        : this(host, onClose, null, null) { }

    /// <summary>Edit existing item from pool.</summary>
    public ItemEditorViewModel(LuaHost host, Action<ItemEditorViewModel, bool> onClose,
                               int? editingItemId, string? existingRawText)
    {
        _host          = host;
        _onClose       = onClose;
        _editingItemId = editingItemId;

        CancelCommand = new RelayCommand(() => _onClose(this, false));
        SaveCommand   = new RelayCommand(Save);
        ToggleAffixPickerCommand = new RelayCommand(() => IsAffixPickerOpen = !IsAffixPickerOpen);

        LocalizationService.Instance.LanguageChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(EditorTitle));
            OnPropertyChanged(nameof(PrefixSuffixLabel));
            OnPropertyChanged(nameof(DefaultArmourHint));
            OnPropertyChanged(nameof(DefaultEvasionHint));
            OnPropertyChanged(nameof(DefaultEnergyShieldHint));
            OnPropertyChanged(nameof(DefaultWardHint));
            OnPropertyChanged(nameof(FilteredBases));
            OnPropertyChanged(nameof(FilteredUniques));
        };

        SetNormalCommand = new RelayCommand(() => Rarity = ItemRarity.Normal);
        SetMagicCommand  = new RelayCommand(() => Rarity = ItemRarity.Magic);
        SetRareCommand   = new RelayCommand(() => Rarity = ItemRarity.Rare);
        SetUniqueCommand = new RelayCommand(() => Rarity = ItemRarity.Unique);

        SetFilterAllCommand    = new RelayCommand(() => ModTypeFilter = "All");
        SetFilterPrefixCommand = new RelayCommand(() => ModTypeFilter = "Prefix");
        SetFilterSuffixCommand = new RelayCommand(() => ModTypeFilter = "Suffix");

        SelectBaseCommand   = new RelayCommand<BaseItemEntry>(b   => { if (b is not null) SelectedBase   = b; });
        SelectUniqueCommand = new RelayCommand<UniqueItemEntry>(u => { if (u is not null) SelectedUnique = u; });

        AddRuneSocketCommand = new RelayCommand(() =>
        {
            if (!CanAddRune) return;
            RuneSockets.Add(new RuneSocketViewModel(this, RuneSockets.Count + 1, null));
            OnPropertyChanged(nameof(CanAddRune));
            OnPropertyChanged(nameof(HasAnyRunes));
        });
        RemoveRuneSocketCommand = new RelayCommand<RuneSocketViewModel>(s =>
        {
            if (s is null) return;
            RuneSockets.Remove(s);
            OnPropertyChanged(nameof(CanAddRune));
            OnPropertyChanged(nameof(HasAnyRunes));
        });

        LoadBaseCategories();
        LoadUniques();
        try { _allRunes = _host.GetAllRunes(); } catch { _allRunes = []; }

        if (existingRawText is { Length: > 0 })
            ParseExistingRaw(existingRawText);
    }

    // ── Initialisation ────────────────────────────────────────────────────────

    private void LoadBaseCategories()
    {
        var cats = _host.GetItemBaseCategories();
        foreach (var c in cats) Categories.Add(c);
        if (Categories.Count > 0)
        {
            SelectedCategory = Categories[0];
            LoadBasesForCategory(SelectedCategory);
        }
    }

    private void LoadUniques()
    {
        _allUniques = _host.GetUniqueItems();
        OnPropertyChanged(nameof(FilteredUniques));
    }

    private void LoadBasesForCategory(string category)
    {
        Bases.Clear();
        var bases = _host.GetItemBasesForCategory(category);
        foreach (var b in bases) Bases.Add(b);
        SelectedBase = Bases.FirstOrDefault();
        OnPropertyChanged(nameof(FilteredBases));
        if (SelectedBase is not null) OnBaseSelected(SelectedBase);
    }

    partial void OnSelectedCategoryChanged(string val)
    {
        if (!string.IsNullOrEmpty(val)) LoadBasesForCategory(val);
    }

    private bool _suppressBaseSelectedSideEffects;

    partial void OnSelectedBaseChanged(BaseItemEntry? val)
    {
        if (val is not null && !_suppressBaseSelectedSideEffects) OnBaseSelected(val);
    }

    /// <summary>Quietly aligns <see cref="SelectedBase"/> with the unique's base item without
    /// running OnBaseSelected (which would wipe the unique's hydrated mods/rune sockets).
    /// Used when opening the editor on an existing unique so CurrentSlotType resolves to the
    /// real base type (e.g. "boots") instead of the default Bases[0] (often "Gold Amulet"),
    /// which fed wrong mod summaries to runes like Soul Core of Citaqualotl.</summary>
    private void AlignBaseForUnique(string baseName)
    {
        if (string.IsNullOrEmpty(baseName)) return;
        var match = Bases.FirstOrDefault(b =>
            string.Equals(b.Name, baseName, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            foreach (var cat in Categories)
            {
                var bases = _host.GetItemBasesForCategory(cat);
                var found = bases.FirstOrDefault(b =>
                    string.Equals(b.Name, baseName, StringComparison.OrdinalIgnoreCase));
                if (found is not null)
                {
                    Bases.Clear();
                    foreach (var b in bases) Bases.Add(b);
                    _selectedCategory = cat;
                    OnPropertyChanged(nameof(SelectedCategory));
                    OnPropertyChanged(nameof(FilteredBases));
                    match = found;
                    break;
                }
            }
        }
        if (match is null) return;
        _suppressBaseSelectedSideEffects = true;
        try { SelectedBase = match; }
        finally { _suppressBaseSelectedSideEffects = false; }
        MaxRunes = match.SocketCount > 0 ? Math.Max(MaxRunes, match.SocketCount) : MaxRunes;
        RefreshCompatibleRunes();
        // Surface ModSummary updates on already-hydrated rune sockets — they were built
        // before SelectedBase was correct, so AvailableRunes / display text are stale.
        foreach (var s in RuneSockets) s.RaisePropertyChanged(nameof(RuneSocketViewModel.ModSummary));
    }

    partial void OnSelectedUniqueChanged(UniqueItemEntry? u)
    {
        SaveError = "";
        // When the user picks a different unique, refresh implicit / explicit lists
        // from main.uniqueDB so the right pane shows that unique's actual mods.
        if (u is not null)
        {
            LoadUniqueModsByKey(u.LookupKey);
            AlignBaseForUnique(u.BaseName);
            RefreshCompatibleRunes();
        }
    }

    private void LoadUniqueModsByKey(string lookupKey)
    {
        if (string.IsNullOrEmpty(lookupKey)) return;
        var raw = _host.GetUniqueItemRaw(lookupKey);
        if (string.IsNullOrEmpty(raw)) return;
        ParseUniqueRaw(raw, replaceItemLevel: false, rolledRaw: null);
    }

    /// <summary>Parses a unique item raw text and populates ExplicitMods.
    /// <para>If <paramref name="rolledRaw"/> is provided (editing an existing item from the pool),
    /// the SOURCE raw (with <c>(N-M)</c> ranges and <c>{variant:N}</c> markers) is used as the
    /// structural template, while the rolled values from <paramref name="rolledRaw"/> are
    /// merged into each matching mod by skeleton match — preserving sliders for uniques.</para>
    /// Variant filtering: only lines for the selected variant are included (defaults to highest).
    /// </summary>
    private void ParseUniqueRaw(string sourceRaw, bool replaceItemLevel, string? rolledRaw = null)
    {
        ImplicitMods.Clear();
        ExplicitMods.Clear();

        var lines = sourceRaw.Split('\n')
                       .Select(l => l.Trim())
                       .Where(l => l.Length > 0)
                       .ToList();

        int i = 0;

        // Skip Rarity: UNIQUE
        if (i < lines.Count && lines[i].StartsWith("Rarity:", StringComparison.OrdinalIgnoreCase))
            i++;
        // Skip title and baseName (1–2 lines)
        if (i < lines.Count && !IsRawMetadataLine(lines[i])) i++;
        if (i < lines.Count && !IsRawMetadataLine(lines[i])) i++;

        // Scan metadata until "Implicits: N". Also count Variant: lines and capture
        // the source raw's default Selected Variant / Selected Alt Variant ids.
        int implicitCount = 0;
        int variantCount  = 0;
        var sourceSelectedVariants = new HashSet<int>();
        while (i < lines.Count)
        {
            var line = lines[i];
            if (line.StartsWith("Implicits:", StringComparison.OrdinalIgnoreCase))
            {
                int.TryParse(line[10..].Trim(), out implicitCount);
                i++;
                break;
            }
            if (line.StartsWith("Variant:", StringComparison.OrdinalIgnoreCase))
                variantCount++;
            if (line.StartsWith("Selected Variant:", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(line[17..].Trim(), out int srcSv))
                sourceSelectedVariants.Add(srcSv);
            var altSrc = Regex.Match(line,
                @"^Selected Alt Variant(?:\s+(Two|Three|Four|Five))?:\s*(\d+)\s*$",
                RegexOptions.IgnoreCase);
            if (altSrc.Success && int.TryParse(altSrc.Groups[2].Value, out int srcAlt))
                sourceSelectedVariants.Add(srcAlt);
            i++;
        }

        // From the rolled raw (the item the user is editing), pull metadata and per-mod values.
        // Each rolled mod maps skeleton -> (strippedText, rangeFraction?) so the slider can be
        // re-seated at the saved {range:X} value on reopen.
        Dictionary<string, (string text, double? range)>? rolledBySkeleton = null;
        // Multi-axis variant set. PoB items can have several variant axes
        // (Selected Variant + up to 5 "Selected Alt Variant N"), each picking
        // one mod-group from the source raw. A mod is included if its
        // {variant:N} prefix matches ANY axis's currently selected id.
        var selectedVariants = new HashSet<int>(sourceSelectedVariants);
        if (selectedVariants.Count == 0 && variantCount > 0) selectedVariants.Add(variantCount);
        int parsedSockets   = 0;
        var parsedRunes     = new List<string>();
        if (!string.IsNullOrEmpty(rolledRaw))
        {
            rolledBySkeleton = new Dictionary<string, (string, double?)>();
            var rolledLines = rolledRaw.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0);
            foreach (var rl in rolledLines)
            {
                if (rl.StartsWith("Selected Variant:", StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(rl[17..].Trim(), out int sv))
                    { selectedVariants.Clear(); selectedVariants.Add(sv); }
                // Selected Alt Variant N: alternate axes — each contributes its own variant id.
                var altM = Regex.Match(rl,
                    @"^Selected Alt Variant(?:\s+(Two|Three|Four|Five))?:\s*(\d+)\s*$",
                    RegexOptions.IgnoreCase);
                if (altM.Success && int.TryParse(altM.Groups[2].Value, out int altV))
                    selectedVariants.Add(altV);
                if (replaceItemLevel
                    && rl.StartsWith("Item Level:", StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(rl[11..].Trim(), out int lvl))
                    ItemLevel = lvl;
                if (rl.StartsWith("Quality:", StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(rl[8..].Trim().TrimEnd('%'), out int q))
                    Quality = q;
                if (string.Equals(rl, "Corrupted", StringComparison.OrdinalIgnoreCase))
                    IsCorrupted = true;
                if (rl.StartsWith("Sockets:", StringComparison.OrdinalIgnoreCase))
                    parsedSockets = rl[8..].Count(c => c == 'S');
                if (rl.StartsWith("Rune:", StringComparison.OrdinalIgnoreCase))
                    parsedRunes.Add(rl[5..].Trim());
                TryParseBaseStatLine(rl);
                if (IsRawMetadataLine(rl)) continue;
                var rangeFrac = ExtractRangeFraction(rl);
                var stripped  = StripRawModPrefixes(rl);
                if (string.IsNullOrEmpty(stripped)) continue;
                var skel = GetModSkeleton(stripped);
                if (skel.Length > 0 && !rolledBySkeleton.ContainsKey(skel))
                    rolledBySkeleton[skel] = (stripped, rangeFrac);
            }
        }

        // Hydrate rune sockets from the rolled raw of the unique item.
        if (parsedSockets > 0)
        {
            MaxRunes = Math.Max(MaxRunes, parsedSockets);
            RuneSockets.Clear();
            for (int idx = 0; idx < parsedSockets; idx++)
            {
                string? rn = idx < parsedRunes.Count ? parsedRunes[idx] : null;
                RuneEntry? rune = null;
                if (!string.IsNullOrEmpty(rn) && !string.Equals(rn, "None", StringComparison.OrdinalIgnoreCase))
                    rune = _allRunes.FirstOrDefault(r => string.Equals(r.Name, rn, StringComparison.OrdinalIgnoreCase));
                RuneSockets.Add(new RuneSocketViewModel(this, idx + 1, rune));
            }
            OnPropertyChanged(nameof(CanAddRune));
            OnPropertyChanged(nameof(HasAnyRunes));
        }

        // Add one row for each source mod line that belongs to the selected variant.
        void AddUniqueModRow(string rawLine, bool isImplicit)
        {
            if (IsRawMetadataLine(rawLine)) return;

            // Variant filter: {variant:1,2,3} → list of allowed variant indices.
            // Item may have several variant axes selected (Selected Variant +
            // Selected Alt Variant N), each contributing its own current id; a mod
            // passes if its variant list overlaps the union of currently-selected ids.
            var vMatch = Regex.Match(rawLine, @"^\{variant:([0-9,]+)\}");
            if (vMatch.Success)
            {
                var allowed = vMatch.Groups[1].Value.Split(',')
                                  .Select(s => int.TryParse(s, out int n) ? n : 0)
                                  .ToHashSet();
                if (!allowed.Overlaps(selectedVariants)) return;
            }

            var rangeFraction = ExtractRangeFraction(rawLine);
            var stripped      = StripRawModPrefixes(rawLine);
            if (string.IsNullOrEmpty(stripped)) return;

            // If we have rolled values from an existing item, replace the display text
            // with the rolled mod so the slider starts at the actual current roll.
            string displayText = stripped;
            double? initFraction = rangeFraction;
            if (rolledBySkeleton is not null)
            {
                var skel = GetModSkeleton(stripped);
                if (skel.Length > 0 && rolledBySkeleton.TryGetValue(skel, out var rolled))
                {
                    displayText = rolled.text;
                    // Prefer the rolled raw's {range:X} if present (preserves last saved
                    // slider position); fall back to numeric parse via initFraction=null.
                    initFraction = rolled.range;
                }
            }

            ExplicitMods.Add(new ExplicitModViewModel(this, displayText,
                originalStatText:     stripped,       // ← keeps the (N-M) range for the slider
                rawLineForSave:       rawLine,
                initialRangeFraction: initFraction,
                isImplicit:           isImplicit));
        }

        for (int k = 0; k < implicitCount && i < lines.Count; k++, i++)
            AddUniqueModRow(lines[i], isImplicit: true);

        for (; i < lines.Count; i++)
            AddUniqueModRow(lines[i], isImplicit: false);

        // Re-resolve rune compatibility now that SelectedUnique / parsed sockets are set.
        RefreshCompatibleRunes();

        // Intrinsic base defaults — derive from the unique's BaseName.
        var baseName = SelectedUnique?.BaseName;
        if (!string.IsNullOrEmpty(baseName)) LoadBaseDefaults(baseName);
    }

    private static double? ExtractRangeFraction(string rawLine)
    {
        var m = Regex.Match(rawLine, @"\{range:([0-9.]+)\}");
        if (!m.Success) return null;
        return double.TryParse(m.Groups[1].Value, NumberStyles.Float,
                               CultureInfo.InvariantCulture, out double x)
            ? x
            : null;
    }

    partial void OnRarityChanged(ItemRarity _)
    {
        RefreshAffixCanAdd();
        OnPropertyChanged(nameof(PrefixSuffixLabel));
        SaveError = "";
    }

    private void OnBaseSelected(BaseItemEntry b)
    {
        // Remove all previous implicits from the unified list (explicits keep their place)
        var toRemove = ExplicitMods.Where(m => m.IsImplicit).ToList();
        foreach (var m in toRemove) ExplicitMods.Remove(m);

        // Re-emit base implicits as ExplicitModViewModel rows with sliders
        ImplicitMods.Clear();   // legacy collection kept for callers that may iterate it; empty
        if (!string.IsNullOrEmpty(b.Implicit))
        {
            int insertAt = 0;
            foreach (var raw in b.Implicit.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var line = raw.Trim();
                if (string.IsNullOrEmpty(line)) continue;
                // Use max-rolled value for display; ParseSingleIntRange picks up (N-M) for slider bounds.
                var text = MaxRollText(line);
                ExplicitMods.Insert(insertAt++, new ExplicitModViewModel(this, text,
                    originalStatText: line,
                    isImplicit:       true));
            }
        }

        // Reload available affixes
        var affixes = _host.GetItemAffixes(b.Name);
        _allAffixes = affixes
            .Select(e => new AffixEntryViewModel(this, e))
            .ToList();
        OnPropertyChanged(nameof(FilteredAffixes));

        // Base intrinsic defaults (Armour/Evasion/ES/etc.)
        LoadBaseDefaults(b.Name);

        // Runes: refresh compatibility + cap allocated sockets to MaxRunes
        MaxRunes = b.SocketCount;
        RefreshCompatibleRunes();
        while (RuneSockets.Count > MaxRunes) RuneSockets.RemoveAt(RuneSockets.Count - 1);
        OnPropertyChanged(nameof(CanAddRune));
        OnPropertyChanged(nameof(HasAnyRunes));

        SaveError = "";
    }

    // ── Mod operations ────────────────────────────────────────────────────────

    public bool CanAddAffix(AffixEntry e)
    {
        if (e.AffixType == "Prefix" && PrefixCount >= MaxPrefixes) return false;
        if (e.AffixType == "Suffix" && SuffixCount >= MaxSuffixes) return false;
        // Each affix group can appear at most once (no duplicate mod families)
        if (!string.IsNullOrEmpty(e.Group) &&
            ExplicitMods.Any(m => !string.IsNullOrEmpty(m.Group) && m.Group == e.Group))
            return false;
        return true;
    }

    public void AddAffix(AffixEntry e)
    {
        if (!CanAddAffix(e)) return;
        // Default roll = max value; user can edit the TextBox afterwards
        var text = MaxRollText(e.StatText);
        ExplicitMods.Add(new ExplicitModViewModel(this, text,
            affixType: e.AffixType,
            affixName: e.AffixName,
            group:     e.Group,
            originalStatText: e.StatText));
        OnPropertyChanged(nameof(PrefixCount));
        OnPropertyChanged(nameof(SuffixCount));
        OnPropertyChanged(nameof(PrefixSuffixLabel));
        RefreshAffixCanAdd();
        NotifyEffectiveStatsChanged();
    }

    public void RemoveExplicitMod(ExplicitModViewModel mod)
    {
        ExplicitMods.Remove(mod);
        OnPropertyChanged(nameof(PrefixCount));
        OnPropertyChanged(nameof(SuffixCount));
        OnPropertyChanged(nameof(PrefixSuffixLabel));
        RefreshAffixCanAdd();
        NotifyEffectiveStatsChanged();
    }

    private void RefreshAffixCanAdd()
    {
        // Re-evaluate the filtered list — unavailable mods simply disappear
        OnPropertyChanged(nameof(FilteredAffixes));
    }

    // ── Pre-populate from existing raw text ───────────────────────────────────

    /// <summary>
    /// Parses the PoB internal raw format produced by item:BuildRaw().
    /// Format: Rarity / Title? / BaseName / metadata lines / "Implicits: N" / N implicit lines / explicit lines
    /// NOTE: No "--------" separators — uses the "Implicits: N" count marker instead.
    /// </summary>
    private void ParseExistingRaw(string raw)
    {
        var lines = raw.Split('\n')
                       .Select(l => l.Trim())
                       .Where(l => l.Length > 0)
                       .ToList();

        int i = 0;

        // 1. Rarity line
        if (i < lines.Count && lines[i].StartsWith("Rarity:", StringComparison.OrdinalIgnoreCase))
        {
            var r = lines[i][8..].Trim().ToUpperInvariant();
            Rarity = r switch
            {
                "NORMAL" => ItemRarity.Normal,
                "MAGIC"  => ItemRarity.Magic,
                "RARE"   => ItemRarity.Rare,
                "UNIQUE" => ItemRarity.Unique,
                _        => ItemRarity.Rare
            };
            i++;
        }

        // For unique items, hand off to the dedicated unique parser (preserves {range:X}
        // and exposes a slider per editable roll).
        if (Rarity == ItemRarity.Unique)
        {
            // Bind SelectedUnique from the raw's title + baseName lines.
            // Match priority: title alone, then "title, baseName" (PoB's full DB key).
            string? title    = (i     < lines.Count && !IsRawMetadataLine(lines[i]))     ? lines[i]     : null;
            string? baseName = (i + 1 < lines.Count && !IsRawMetadataLine(lines[i + 1])) ? lines[i + 1] : null;
            UniqueItemEntry? match = null;
            if (title is not null)
            {
                match = _allUniques.FirstOrDefault(u =>
                            string.Equals(u.Name, title, StringComparison.OrdinalIgnoreCase));
                if (match is null && baseName is not null)
                {
                    var fullKey = title + ", " + baseName;
                    match = _allUniques.FirstOrDefault(u =>
                                string.Equals(u.LookupKey, fullKey, StringComparison.OrdinalIgnoreCase));
                }
            }
            if (match is not null) _selectedUnique = match;
            OnPropertyChanged(nameof(SelectedUnique));

            // Pull the SOURCE raw (with (N-M) ranges + {variant:N} markers) so we can
            // build sliders, even though the existing rolled raw has no ranges.
            string? sourceRaw = null;
            if (match is not null)
            {
                sourceRaw = _host.GetUniqueItemRaw(match.LookupKey);
                if (string.IsNullOrWhiteSpace(sourceRaw)) sourceRaw = null;
            }
            ParseUniqueRaw(sourceRaw ?? raw, replaceItemLevel: true, rolledRaw: sourceRaw is null ? null : raw);
            if (match is not null) AlignBaseForUnique(match.BaseName);
            return;
        }

        // 2. One or two name lines before metadata.
        //    RARE/UNIQUE with a title: first = title (user/unique name), second = baseName.
        //    NORMAL/MAGIC or PoB-crafted RARE without title: single line = (namePrefix)baseName(nameSuffix).
        string? parsedTitle    = null;
        string? parsedBaseName = null;

        if (i < lines.Count && !IsRawMetadataLine(lines[i]))
        {
            parsedTitle = lines[i]; i++;
        }
        if (i < lines.Count && !IsRawMetadataLine(lines[i]))
        {
            parsedBaseName = parsedTitle;  // promote: first was title, second is baseName
            parsedTitle    = lines[i - 1]; // (already captured above)
            parsedBaseName = lines[i]; i++;
        }

        // If only one name line found: it IS the base name
        if (parsedBaseName is null)
            parsedBaseName = parsedTitle;

        // 3. Scan metadata lines, grab ItemLevel/Sockets/Rune lines, stop at "Implicits: N"
        int implicitCount = 0;
        int parsedSockets = 0;
        var parsedRunes = new List<string>();   // per-socket rune names ("None" allowed)
        while (i < lines.Count)
        {
            var line = lines[i];
            if (line.StartsWith("Implicits:", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(line[10..].Trim(), out int n))
                    implicitCount = n;
                i++;
                break;
            }
            if (line.StartsWith("Item Level:", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(line[11..].Trim(), out int lvl))
                    ItemLevel = lvl;
            }
            if (line.StartsWith("Quality:", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(line[8..].Trim().TrimEnd('%'), out int q))
                Quality = q;
            if (string.Equals(line, "Corrupted", StringComparison.OrdinalIgnoreCase))
                IsCorrupted = true;
            if (line.StartsWith("Sockets:", StringComparison.OrdinalIgnoreCase))
                parsedSockets = line[8..].Count(c => c == 'S');
            if (line.StartsWith("Rune:", StringComparison.OrdinalIgnoreCase))
                parsedRunes.Add(line[5..].Trim());
            TryParseBaseStatLine(line);
            i++;
        }

        // 4. Collect implicit mod texts (rolled values)
        var implicitTexts = new List<string>();
        for (int skip = 0; skip < implicitCount && i < lines.Count; skip++, i++)
        {
            var t = StripRawModPrefixes(lines[i]);
            if (!string.IsNullOrEmpty(t) && !IsRawMetadataLine(t))
                implicitTexts.Add(t);
        }

        // 5. Collect explicit mod texts (don't add to ExplicitMods yet)
        var explicitTexts = new List<string>();
        for (; i < lines.Count; i++)
        {
            var modText = StripRawModPrefixes(lines[i]);
            if (!string.IsNullOrEmpty(modText) && !IsRawMetadataLine(modText))
                explicitTexts.Add(modText);
        }

        // 6. Store fallback base name and load affixes via TrySelectBase.
        //    This must come BEFORE adding mods so that _allAffixes is populated
        //    and OnBaseSelected has primed the base implicits with sliders.
        _fallbackBaseName = parsedBaseName ?? "";
        if (!string.IsNullOrEmpty(_fallbackBaseName))
            TrySelectBase(_fallbackBaseName);

        // 6a. Hydrate rune sockets from the raw — overrides MaxRunes from base.SocketCount
        //     so legacy items / off-base sockets keep their state.
        if (parsedSockets > 0)
        {
            MaxRunes = Math.Max(MaxRunes, parsedSockets);
            RuneSockets.Clear();
            for (int idx = 0; idx < parsedSockets; idx++)
            {
                string? rn = idx < parsedRunes.Count ? parsedRunes[idx] : null;
                RuneEntry? rune = null;
                if (!string.IsNullOrEmpty(rn) && !string.Equals(rn, "None", StringComparison.OrdinalIgnoreCase))
                    rune = _allRunes.FirstOrDefault(r => string.Equals(r.Name, rn, StringComparison.OrdinalIgnoreCase));
                RuneSockets.Add(new RuneSocketViewModel(this, idx + 1, rune));
            }
            OnPropertyChanged(nameof(CanAddRune));
            OnPropertyChanged(nameof(HasAnyRunes));
        }

        // 6b. Override the freshly-inserted base implicit rows with the rolled values
        //     from the raw, matched by skeleton (keeps slider ranges from the base).
        if (implicitTexts.Count > 0)
        {
            var implicitRows = ExplicitMods.Where(m => m.IsImplicit).ToList();
            foreach (var rolled in implicitTexts)
            {
                var skel = GetModSkeleton(rolled);
                var match = implicitRows.FirstOrDefault(r => GetModSkeleton(r.Text) == skel
                                                          || GetModSkeleton(StripRawModPrefixes(r.BuildRawLineForSave())) == skel);
                if (match is not null) { match.Text = rolled; implicitRows.Remove(match); }
                else                    ExplicitMods.Add(new ExplicitModViewModel(this, rolled, isImplicit: true));
            }
        }

        // 7. Add explicit mods with resolved type info
        foreach (var modText in explicitTexts)
        {
            var matched = FindMatchingAffix(modText);
            if (matched is not null)
            {
                ExplicitMods.Add(new ExplicitModViewModel(this, modText,
                    affixType:        matched.AffixType,
                    affixName:        matched.AffixName,
                    group:            matched.Group,
                    originalStatText: matched.StatText));
            }
            else
            {
                ExplicitMods.Add(new ExplicitModViewModel(this, modText));
            }
        }

        OnPropertyChanged(nameof(PrefixCount));
        OnPropertyChanged(nameof(SuffixCount));
        OnPropertyChanged(nameof(PrefixSuffixLabel));
    }

    /// <summary>Returns true if the line is a known PoB item metadata line (not a mod).</summary>
    private static bool IsRawMetadataLine(string line) =>
        line.StartsWith("Item Level:",        StringComparison.OrdinalIgnoreCase) ||
        line.StartsWith("Crafted:",           StringComparison.OrdinalIgnoreCase) ||
        line.StartsWith("Prefix:",            StringComparison.OrdinalIgnoreCase) ||
        line.StartsWith("Suffix:",            StringComparison.OrdinalIgnoreCase) ||
        line.StartsWith("LevelReq:",          StringComparison.OrdinalIgnoreCase) ||
        line.StartsWith("Implicits:",         StringComparison.OrdinalIgnoreCase) ||
        line.StartsWith("Quality:",           StringComparison.OrdinalIgnoreCase) ||
        line.StartsWith("Sockets:",           StringComparison.OrdinalIgnoreCase) ||
        line.StartsWith("Rune:",              StringComparison.OrdinalIgnoreCase) ||
        line.StartsWith("Unique ID:",         StringComparison.OrdinalIgnoreCase) ||
        line.StartsWith("League:",            StringComparison.OrdinalIgnoreCase) ||
        line.StartsWith("Variant:",           StringComparison.OrdinalIgnoreCase) ||
        line.StartsWith("Selected Variant:",  StringComparison.OrdinalIgnoreCase) ||
        line.StartsWith("Has Alt Variant",    StringComparison.OrdinalIgnoreCase) ||
        line.StartsWith("Selected Alt Variant",StringComparison.OrdinalIgnoreCase)||
        line.StartsWith("Cluster Jewel",      StringComparison.OrdinalIgnoreCase) ||
        line.StartsWith("Armour:",            StringComparison.OrdinalIgnoreCase) ||
        line.StartsWith("Evasion:",           StringComparison.OrdinalIgnoreCase) ||
        line.StartsWith("Energy Shield:",     StringComparison.OrdinalIgnoreCase) ||
        line.StartsWith("Ward:",              StringComparison.OrdinalIgnoreCase) ||
        line.StartsWith("Spirit:",            StringComparison.OrdinalIgnoreCase) ||
        line.StartsWith("Charm Slots:",       StringComparison.OrdinalIgnoreCase) ||
        line.StartsWith("Catalyst:",          StringComparison.OrdinalIgnoreCase) ||
        line.StartsWith("Talisman Tier:",     StringComparison.OrdinalIgnoreCase) ||
        line.StartsWith("Limited to:",        StringComparison.OrdinalIgnoreCase) ||
        line.StartsWith("Radius:",            StringComparison.OrdinalIgnoreCase) ||
        line.StartsWith("Unreleased:",        StringComparison.OrdinalIgnoreCase) ||
        line.StartsWith("Requires Class",     StringComparison.OrdinalIgnoreCase) ||
        line.StartsWith("CatalystQuality:",   StringComparison.OrdinalIgnoreCase) ||
        line.StartsWith("--------") ||
        line == "Mirrored" || line == "Corrupted" ||
        line == "Twice Corrupted" || line == "Sanctified";

    /// <summary>Strips leading {tag} prefixes from a PoB mod line, e.g. "{range:0.5}{crafted}+50 to Life" → "+50 to Life".</summary>
    private static string StripRawModPrefixes(string line) =>
        Regex.Replace(line, @"^(\{[^}]+\})+", "").Trim();

    // ── Mod-type resolution helpers ────────────────────────────────────────────

    /// <summary>
    /// Tries to find the affix entry that produced the given rolled mod text.
    /// Exact match first; then among skeleton-equal affixes, pick the tier whose
    /// numeric ranges contain the rolled values (so we get the correct tier and
    /// therefore the correct slider range).
    /// </summary>
    private AffixEntry? FindMatchingAffix(string modText)
    {
        if (_allAffixes.Count == 0) return null;

        // 1. Exact match (fixed mods with no range)
        var exact = _allAffixes.FirstOrDefault(a =>
            string.Equals(a.Entry.StatText, modText, StringComparison.OrdinalIgnoreCase));
        if (exact is not null) return exact.Entry;

        // 2. Skeleton match — collect all tiers with the same template.
        var modSkel = GetModSkeleton(modText);
        var skelMatches = _allAffixes
            .Where(a => string.Equals(GetModSkeleton(a.Entry.StatText), modSkel,
                                       StringComparison.OrdinalIgnoreCase))
            .Select(a => a.Entry)
            .ToList();
        if (skelMatches.Count == 0) return null;
        if (skelMatches.Count == 1) return skelMatches[0];

        // 3. Among tiers, prefer the one whose (N-M) range contains the rolled value.
        var rolledValues = Regex.Matches(modText, @"-?\d+(?:\.\d+)?")
            .Select(m => double.TryParse(m.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? (double?)d : null)
            .Where(v => v.HasValue).Select(v => v!.Value).ToList();
        if (rolledValues.Count == 0) return skelMatches[0];

        AffixEntry? best = null;
        int bestScore = -1;
        foreach (var tier in skelMatches)
        {
            var ranges = Regex.Matches(tier.StatText, @"\((-?\d+(?:\.\d+)?)-(-?\d+(?:\.\d+)?)\)")
                .Select(m => (
                    Min: double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture),
                    Max: double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture)))
                .ToList();
            if (ranges.Count == 0) continue;
            int score = 0;
            for (int idx = 0; idx < Math.Min(ranges.Count, rolledValues.Count); idx++)
            {
                var (mn, mx) = ranges[idx];
                if (rolledValues[idx] >= mn && rolledValues[idx] <= mx) score++;
            }
            if (score > bestScore) { bestScore = score; best = tier; }
        }
        return best ?? skelMatches[0];
    }

    /// <summary>Strips digits and numeric-range punctuation to get a comparable template.</summary>
    private static string GetModSkeleton(string text)
    {
        // Remove patterns like: 50  (40-60)  (40)  40-60
        var s = Regex.Replace(text, @"\(?\d+(?:[.-]\d+)?\)?", "");
        // Collapse multiple spaces
        return Regex.Replace(s, @"\s{2,}", " ").Trim();
    }

    /// <summary>Finds and activates the matching base entry across all categories.</summary>
    private void TrySelectBase(string baseName)
    {
        // Try currently loaded category first (fast path)
        var match = Bases.FirstOrDefault(b =>
            string.Equals(b.Name, baseName, StringComparison.OrdinalIgnoreCase));
        if (match is not null) { SelectedBase = match; return; }

        // Search all other categories
        foreach (var cat in Categories)
        {
            var bases = _host.GetItemBasesForCategory(cat);
            var found = bases.FirstOrDefault(b =>
                string.Equals(b.Name, baseName, StringComparison.OrdinalIgnoreCase));
            if (found is not null)
            {
                Bases.Clear();
                foreach (var b in bases) Bases.Add(b);
                _selectedCategory = cat;   // backing field to avoid reloading bases again
                OnPropertyChanged(nameof(SelectedCategory));
                SelectedBase = found;
                OnPropertyChanged(nameof(FilteredBases));
                OnBaseSelected(found);
                return;
            }
        }
    }

    // ── Build raw text ────────────────────────────────────────────────────────

    private string BuildRawText()
    {
        var sb = new StringBuilder();

        if (IsUnique && SelectedUnique is not null)
        {
            sb.AppendLine("Rarity: UNIQUE");
            // Title line: PoB stores uniques under key "title, baseName" but BuildRaw writes title alone.
            // Use Name (display title), then BaseName on the next line.
            sb.AppendLine(SelectedUnique.Name);
            sb.AppendLine(SelectedUnique.BaseName);
            if (ItemLevel > 0)
                sb.AppendLine($"Item Level: {ItemLevel}");
            if (Quality > 0)
                sb.AppendLine($"Quality: {Quality}");

            EmitBaseStats(sb);
            EmitSocketsAndRunes(sb);

            // The list is split internally into implicits (IsImplicit=true) and explicits,
            // even though the UI shows one merged list. PoB's parser needs the Implicits:N header.
            var implicits = ExplicitMods.Where(m => m.IsImplicit).ToList();
            var explicits = ExplicitMods.Where(m => !m.IsImplicit).ToList();

            sb.AppendLine($"Implicits: {implicits.Count}");
            foreach (var m in implicits)
                sb.AppendLine(m.BuildRawLineForSave());
            foreach (var m in explicits)
                sb.AppendLine(m.BuildRawLineForSave());

            if (IsCorrupted)
                sb.AppendLine("Corrupted");
        }
        else
        {
            // Use SelectedBase name if available, otherwise fall back to the preserved base name
            var baseName = SelectedBase?.Name ?? _fallbackBaseName;
            if (string.IsNullOrEmpty(baseName)) return sb.ToString();

            var rarityStr = Rarity switch
            {
                ItemRarity.Normal => "NORMAL",
                ItemRarity.Magic  => "MAGIC",
                ItemRarity.Unique => "UNIQUE",
                _                 => "RARE"
            };
            sb.AppendLine($"Rarity: {rarityStr}");
            // PoB requires two name lines for RARE items (title + baseName). For
            // PoB-crafted rares without a real title we emit baseName twice; the
            // slot/pool display logic in ItemSlotViewModel deduplicates this case
            // and shows just the base name.
            if (Rarity == ItemRarity.Rare)
                sb.AppendLine(baseName);
            sb.AppendLine(baseName);
            if (ItemLevel > 0)
                sb.AppendLine($"Item Level: {ItemLevel}");
            if (Quality > 0)
                sb.AppendLine($"Quality: {Quality}");

            EmitBaseStats(sb);
            EmitSocketsAndRunes(sb);

            // Implicits + explicits are stored in the same collection (ExplicitMods)
            // distinguished by IsImplicit. Emit them in two separated blocks with the
            // {implicit} prefix for implicit rows.
            var implRows = ExplicitMods.Where(m => m.IsImplicit).ToList();
            var explRows = ExplicitMods.Where(m => !m.IsImplicit).ToList();

            if (implRows.Count > 0)
            {
                sb.AppendLine("--------");
                foreach (var m in implRows)
                    sb.AppendLine($"{{implicit}}{m.Text}");
            }

            if (explRows.Count > 0)
            {
                sb.AppendLine("--------");
                foreach (var m in explRows)
                    sb.AppendLine(m.Text);
            }

            if (IsCorrupted)
                sb.AppendLine("Corrupted");
        }

        return sb.ToString();
    }

    /// <summary>Parses Armour/Evasion/Energy Shield/Ward/Spirit/Charm Slots lines from one raw line.
    /// Returns true if the line was consumed as a base-stat line.</summary>
    private bool TryParseBaseStatLine(string line)
    {
        if (line.StartsWith("Armour:", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(line[7..].Trim(), out int a)) { BaseArmour = a; return true; }
        if (line.StartsWith("Evasion:", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(line[8..].Trim(), out int ev)) { BaseEvasion = ev; return true; }
        if (line.StartsWith("Energy Shield:", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(line[14..].Trim(), out int es)) { BaseEnergyShield = es; return true; }
        if (line.StartsWith("Ward:", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(line[5..].Trim(), out int w)) { BaseWard = w; return true; }
        if (line.StartsWith("Spirit:", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(line[7..].Trim(), out int sp)) { BaseSpirit = sp; return true; }
        if (line.StartsWith("Charm Slots:", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(line[12..].Trim(), out int cs)) { BaseCharmSlots = cs; return true; }
        return false;
    }

    /// <summary>Emits the editor's base-stat OVERRIDE lines into the raw — but only
    /// when the user's value diverges from the intrinsic default. Equal to default
    /// means "no override needed" and PoB will derive from the base itself.</summary>
    private void EmitBaseStats(StringBuilder sb)
    {
        if (BaseArmour       > 0 && BaseArmour       != DefaultArmour)       sb.AppendLine($"Armour: {BaseArmour}");
        if (BaseEvasion      > 0 && BaseEvasion      != DefaultEvasion)      sb.AppendLine($"Evasion: {BaseEvasion}");
        if (BaseEnergyShield > 0 && BaseEnergyShield != DefaultEnergyShield) sb.AppendLine($"Energy Shield: {BaseEnergyShield}");
        if (BaseWard         > 0 && BaseWard         != DefaultWard)         sb.AppendLine($"Ward: {BaseWard}");
        if (BaseSpirit       > 0 && BaseSpirit       != DefaultSpirit)       sb.AppendLine($"Spirit: {BaseSpirit}");
        if (BaseCharmSlots   > 0 && BaseCharmSlots   != DefaultCharmSlots)   sb.AppendLine($"Charm Slots: {BaseCharmSlots}");
    }

    /// <summary>Emits Sockets:/Rune: lines if the item has any allocated sockets.</summary>
    private void EmitSocketsAndRunes(StringBuilder sb)
    {
        if (RuneSockets.Count == 0) return;
        sb.AppendLine("Sockets: " + string.Join(" ", Enumerable.Repeat("S", RuneSockets.Count)));
        foreach (var sock in RuneSockets)
            sb.AppendLine("Rune: " + (sock.SelectedRune?.Name ?? "None"));
    }

    // ── Save ─────────────────────────────────────────────────────────────────

    private void Save()
    {
        SaveError = "";

        if (IsUnique && SelectedUnique is null)
        {
            SaveError = LocalizationService.Get("Editor_SaveSelectUnique");
            return;
        }
        if (!IsUnique && SelectedBase is null && string.IsNullOrEmpty(_fallbackBaseName))
        {
            SaveError = LocalizationService.Get("Editor_SaveSelectBase");
            return;
        }

        var raw = BuildRawText();

        bool ok;
        if (_editingItemId.HasValue)
        {
            ok = _host.UpdateItemFromText(_editingItemId.Value, raw);
            if (!ok) SaveError = LocalizationService.Get("Editor_SaveUpdateFailed");
        }
        else
        {
            ok = _host.ImportItemFromText(raw);
            if (!ok) SaveError = LocalizationService.Get("Editor_SaveCreateFailed");
        }

        if (ok) _onClose(this, true);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Given a stat text like "+(13-16) to Strength", returns the max-rolled version
    /// "+16 to Strength". Leaves non-range text unchanged.
    /// </summary>
    private static string MaxRollText(string statText)
    {
        return Regex.Replace(statText, @"\((\d+)-(\d+)\)", m =>
        {
            var max = m.Groups[2].Value;
            return max;
        });
    }
}
