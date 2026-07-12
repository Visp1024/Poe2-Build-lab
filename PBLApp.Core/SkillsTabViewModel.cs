using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PBLApp.Core.Localization;
using PBLEngine;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace PBLApp.ViewModels;

// ── Gem attribute classification ──────────────────────────────────────────────

/// <summary>Primary attribute of a gem, derived from its requirement colour.</summary>
public enum GemAttr { None, Str, Dex, Int }

/// <summary>Active attribute filter tab in the support-gem picker.</summary>
public enum GemTab { All, Str, Dex, Int, Special }

public static class GemAttrUtil
{
    // Colours assigned by LuaHost.gemColor: Str=red, Dex=green, Int=blue, else neutral.
    public static GemAttr FromColor(string? color) => color switch
    {
        "#F38BA8" => GemAttr.Str,
        "#A6E3A1" => GemAttr.Dex,
        "#89B4FA" => GemAttr.Int,
        _         => GemAttr.None,
    };
}

// ── GemNameItem ───────────────────────────────────────────────────────────────

/// <summary>Item shown in a gem name dropdown. ToString() returns Name so the
/// editable ComboBox text is set correctly when the user selects an entry.</summary>
public sealed class GemNameItem
{
    public string Name        { get; }
    public string DisplayName => GameTranslationService.TGem(Name);
    public string Color       { get; }

    // Lazy tooltip: loaded on first access, cached afterwards.
    private readonly Func<string, IReadOnlyList<GemTooltipEntry>?>? _tooltipLoader;
    private IReadOnlyList<GemTooltipEntry>? _tooltipEntries;
    private bool _tooltipLoaded;

    public IReadOnlyList<GemTooltipEntry>? TooltipEntries
    {
        get
        {
            if (!_tooltipLoaded)
            {
                _tooltipLoaded  = true;
                _tooltipEntries = _tooltipLoader?.Invoke(Name);
            }
            return _tooltipEntries;
        }
    }

    public GemNameItem(string name, string color,
        Func<string, IReadOnlyList<GemTooltipEntry>?>? tooltipLoader = null)
    {
        Name            = name;
        Color           = color;
        _tooltipLoader  = tooltipLoader;
    }

    public override string ToString() => Name;
}

// ── GemViewModel ─────────────────────────────────────────────────────────────

public partial class GemViewModel : ObservableObject
{
    private readonly SkillsTabViewModel _parent;
    private bool _syncing;
    private string _committedName = "";

    public int GroupIndex { get; set; }
    public int GemIndex   { get; set; }

    public bool IsEmpty => GemIndex == 0;

    /// <summary>Raw (English) committed gem name, or "" when empty. Used by the IPC
    /// text-inspection tools to identify a gem without going through DisplayText.</summary>
    public string CommittedName => _committedName;

    // Text currently shown in the ComboBox (updated on every keystroke).
    // Drives filtering; NOT the committed/saved value.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilteredGemNames))]
    [NotifyPropertyChangedFor(nameof(DisplayText))]
    private string _searchText = "";

    [ObservableProperty]
    private decimal _level = 19;

    [ObservableProperty]
    private decimal _quality = 20;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NameForeground))]
    private bool _isEnabled = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AvailableGemNameItems))]
    [NotifyPropertyChangedFor(nameof(FilteredGemNames))]
    [NotifyPropertyChangedFor(nameof(ShowLevelQuality))]
    private bool _isSupport = false;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NameForeground))]
    private string _color = "#CDD6F4";

    public string NameForeground => IsEnabled ? _color : "#585B70";

    /// <summary>Level/Quality controls are only meaningful for skill gems — show
    /// them for an active gem or a skill gem inserted into a support slot, but
    /// never for support gems or empty slots.</summary>
    public bool ShowLevelQuality => !IsEmpty && !IsSupport;

    [ObservableProperty]
    private IReadOnlyList<GemTooltipEntry>? _tooltipEntries;

    // ── Picker filter state (support-gem dropdown) ─────────────────────────

    /// <summary>Active attribute-filter tab in the picker.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilteredGemNames))]
    private GemTab _tab = GemTab.All;

    /// <summary>In a trigger/meta group the picker can list either the triggered
    /// skills (false) or supports (true). Ignored for normal groups.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilteredGemNames))]
    [NotifyPropertyChangedFor(nameof(AvailableGemNameItems))]
    [NotifyPropertyChangedFor(nameof(SkillsMode))]
    private bool _supportMode;

    /// <summary>True when this slot belongs to a trigger/meta group (Cast on …),
    /// where the Skills/Support toggle is shown.</summary>
    public bool IsTriggerSlot => _parent.IsTriggerGroup(GroupIndex);

    /// <summary>True for the Skills sub-mode of a trigger group (no attribute tabs).</summary>
    public bool SkillsMode => IsTriggerSlot && !SupportMode;

    /// <summary>Triggered skills currently installed in this group (Skills mode counter).</summary>
    public int SkillsHave => _parent.SkillSlotsInGroup(GroupIndex);

    // Per-tab counters (installed / max), refreshed when the picker opens.
    public int TabAllHave => _parent.TotalSupportInstalled();
    public int TabAllMax  => _parent.TotalSupportMax();
    public int TabStrHave => _parent.AttrInstalled(GemAttr.Str);
    public int TabStrMax  => _parent.AttrMax(GemAttr.Str);
    public int TabDexHave => _parent.AttrInstalled(GemAttr.Dex);
    public int TabDexMax  => _parent.AttrMax(GemAttr.Dex);
    public int TabIntHave => _parent.AttrInstalled(GemAttr.Int);
    public int TabIntMax  => _parent.AttrMax(GemAttr.Int);
    // Special gems carry no attribute requirement, so there is no attribute-based
    // slot cap — only the installed count is shown (no /max).
    public int TabSpecialHave => _parent.AttrInstalled(GemAttr.None);

    /// <summary>Recompute the tab counters (call when opening the picker, since
    /// installed counts depend on every group's current contents).</summary>
    public void RefreshPickerCounts()
    {
        OnPropertyChanged(nameof(TabAllHave)); OnPropertyChanged(nameof(TabAllMax));
        OnPropertyChanged(nameof(TabStrHave)); OnPropertyChanged(nameof(TabStrMax));
        OnPropertyChanged(nameof(TabDexHave)); OnPropertyChanged(nameof(TabDexMax));
        OnPropertyChanged(nameof(TabIntHave)); OnPropertyChanged(nameof(TabIntMax));
        OnPropertyChanged(nameof(TabSpecialHave));
        OnPropertyChanged(nameof(SkillsHave));
        OnPropertyChanged(nameof(IsTriggerSlot));
        OnPropertyChanged(nameof(SkillsMode));
        OnPropertyChanged(nameof(FilteredGemNames));
    }

    public void RefreshTooltip()
    {
        if (GroupIndex <= 0 || GemIndex <= 0) { TooltipEntries = null; return; }
        var raw = _parent.GetGemTooltipLines(GroupIndex, GemIndex);
        if (raw.Count == 0) { TooltipEntries = null; return; }

        TooltipEntries = SkillsTabViewModel.ProcessTooltipLines(raw, hasStats: true,
            describeStats: stats => StatDescriptionEngine.Instance.Describe(stats));
    }

    /// <summary>Toggled by the editable ComboBox's GotFocus/LostFocus handlers
    /// so the DisplayText setter can tell user input apart from Avalonia's
    /// template-init back-propagation (which pushes Text="" once the editable
    /// ComboBox materialises). True only while the user is actively editing.</summary>
    public bool UserEditing { get; set; }

    // Translated display text for the ComboBox. When the gem is committed (not being
    // typed), shows the Russian name. While the user is actively typing, shows the
    // raw input so filtering works. The setter forwards to SearchText, but rejects
    // empty pushes from non-user sources to survive ComboBox template initialisation.
    public string DisplayText
    {
        get => !string.IsNullOrEmpty(_committedName) && _searchText == _committedName
            ? GameTranslationService.TGem(_committedName)
            : _searchText;
        set
        {
            if (!UserEditing && string.IsNullOrEmpty(value) && !string.IsNullOrEmpty(_committedName))
            {
                // Avalonia's editable ComboBox pushes its empty internal Text back into
                // the source during template apply. Reject and re-publish our value so
                // the ComboBox picks up the committed name.
                OnPropertyChanged(nameof(DisplayText));
                return;
            }
            SearchText = value;
        }
    }

    // Full list for the current gem type (active or support).
    // In trigger / meta groups (Cast on Crit, Cast on Shock, …), the support
    // dropdown also surfaces active spell gems so the user can pick the
    // triggered skill (which lives at gemList[2+] as isSupport=false).
    public IReadOnlyList<GemNameItem> AvailableGemNameItems
    {
        get
        {
            // Trigger/meta group: Skills sub-mode lists triggered active gems,
            // Support sub-mode lists supports. Normal slots use the gem's own type.
            if (_parent.IsTriggerGroup(GroupIndex))
                return SupportMode ? _parent.SupportGemNameItems : _parent.ActiveGemNameItems;
            return IsSupport ? _parent.SupportGemNameItems : _parent.ActiveGemNameItems;
        }
    }

    // Filtered subset shown in the dropdown while the user types.
    // When SearchText equals the committed name (field just displaying saved value),
    // show the full list so clicking the arrow always opens a full dropdown.
    // Supports filtering by both English name and translated display name, and by
    // the active attribute tab (skipped in trigger Skills mode — no attr tabs there).
    public IReadOnlyList<GemNameItem> FilteredGemNames
    {
        get
        {
            IEnumerable<GemNameItem> all = AvailableGemNameItems;

            if (!SkillsMode && Tab != GemTab.All)
            {
                var want = Tab switch
                {
                    GemTab.Str     => GemAttr.Str,
                    GemTab.Dex     => GemAttr.Dex,
                    GemTab.Int     => GemAttr.Int,
                    GemTab.Special => GemAttr.None,
                    _              => GemAttr.None,
                };
                all = all.Where(g => GemAttrUtil.FromColor(g.Color) == want);
            }

            if (!string.IsNullOrEmpty(_searchText) && _searchText != _committedName)
                all = all.Where(g =>
                    g.Name.Contains(_searchText, StringComparison.OrdinalIgnoreCase) ||
                    g.DisplayName.Contains(_searchText, StringComparison.OrdinalIgnoreCase));

            return all as IReadOnlyList<GemNameItem> ?? all.ToList();
        }
    }

    // Kept for backward compat (CanExecute, etc.)
    public IRelayCommand RemoveCommand { get; }

    public GemViewModel(SkillsTabViewModel parent, int groupIndex, int gemIndex, GemEntry e)
    {
        _parent        = parent;
        GroupIndex     = groupIndex;
        GemIndex       = gemIndex;
        _syncing       = true;
        _searchText    = e.Name;
        _committedName = e.Name;
        _level         = e.Level;
        _quality       = e.Quality;
        _isEnabled     = e.IsEnabled;
        _isSupport     = e.IsSupport;
        _color         = e.Color;
        _syncing       = false;
        RemoveCommand  = new RelayCommand(
            () => _parent.RemoveGem(GroupIndex, GemIndex),
            () => !IsEmpty);
        RefreshTooltip();
    }

    public void UpdateFrom(GemEntry e, int gemIndex)
    {
        GemIndex = gemIndex;
        _syncing = true;
        _searchText    = e.Name;
        _committedName = e.Name;
        _level         = e.Level;
        _quality       = e.Quality;
        _isEnabled     = e.IsEnabled;
        _isSupport     = e.IsSupport;
        _color         = e.Color;
        _syncing = false;
        OnPropertyChanged(nameof(SearchText));
        OnPropertyChanged(nameof(DisplayText));
        OnPropertyChanged(nameof(Level));
        OnPropertyChanged(nameof(Quality));
        OnPropertyChanged(nameof(IsEnabled));
        OnPropertyChanged(nameof(IsSupport));
        OnPropertyChanged(nameof(Color));
        OnPropertyChanged(nameof(NameForeground));
        OnPropertyChanged(nameof(AvailableGemNameItems));
        OnPropertyChanged(nameof(FilteredGemNames));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(ShowLevelQuality));
        ((RelayCommand)RemoveCommand).NotifyCanExecuteChanged();
        RefreshTooltip();
    }

    /// <summary>Called by code-behind on selection or Enter key.
    /// Validates the typed text; resets to previous name if invalid.</summary>
    public void CommitName()
    {
        if (_syncing) return;
        var text = _searchText.Trim();

        if (IsEmpty && string.IsNullOrWhiteSpace(text)) return;

        // Validate against the full available list (case-insensitive)
        var allNames = (IsSupport && _parent.IsTriggerGroup(GroupIndex))
            ? _parent.TriggerSlotGemNames
            : (IsSupport ? _parent.SupportGemNames : _parent.ActiveGemNames);
        var exact = allNames.FirstOrDefault(n => string.Equals(n, text, StringComparison.OrdinalIgnoreCase));

        if (exact == null)
        {
            // Unknown name — reset to the last committed value
            SearchText = _committedName;
            return;
        }

        // Normalise casing to canonical name
        if (text != exact) SearchText = exact;

        _committedName = exact;
        OnPropertyChanged(nameof(DisplayText));
        RefreshTooltip();
        _parent.CommitGemName(GroupIndex, GemIndex, exact);
    }

    partial void OnLevelChanged(decimal value)
    {
        if (_syncing || IsEmpty) return;
        _parent.SyncGemLevel(GroupIndex, GemIndex, (int)Math.Max(1, value));
    }

    partial void OnQualityChanged(decimal value)
    {
        if (_syncing || IsEmpty) return;
        _parent.SyncGemQuality(GroupIndex, GemIndex, (int)Math.Max(0, value));
    }

    partial void OnIsEnabledChanged(bool value)
    {
        if (_syncing || IsEmpty) return;
        _parent.SyncGemEnabled(GroupIndex, GemIndex, value);
    }
}

// ── SkillGroupViewModel ───────────────────────────────────────────────────────

public partial class SkillGroupViewModel : ObservableObject
{
    private readonly SkillsTabViewModel _parent;
    private bool _syncing;

    public int Index { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ListItemColor))]
    private bool _isEnabled = true;

    [ObservableProperty] private bool _isMain;

    /// <summary>Whether this group's damage is rolled into the build's Full DPS
    /// (PoB's group.includeInFullDPS). Drives the header checkbox and the list chip.</summary>
    [ObservableProperty] private bool _includeInFullDps;

    /// <summary>True when this is the active group shown in the editor pane.
    /// Maintained by the parent on SelectedGroup changes; drives the single
    /// selection highlight on the group-list row (no ListBox selection chrome).</summary>
    [ObservableProperty] private bool _isSelected;

    /// <summary>True when the group's active gem is a Meta / Trigger skill
    /// (Cast on Crit / Cast on Shock / Cast on Block / Cast on Minion Death, etc.).
    /// In trigger groups, the dropdown for "support" slots also surfaces active
    /// spell gems so the user can pick the triggered skill.</summary>
    [ObservableProperty] private bool _isTrigger;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveGemName))]
    [NotifyPropertyChangedFor(nameof(ActiveGemColor))]
    [NotifyPropertyChangedFor(nameof(ListItemColor))]
    private GemViewModel? _activeGem;

    public string ActiveGemName  => _activeGem != null
        ? GameTranslationService.TGem(_activeGem.SearchText)
        : "(empty)";
    public string ActiveGemColor => _activeGem?.Color ?? "#585B70";
    public string ListItemColor  => IsEnabled ? ActiveGemColor : "#585B70";

    public ObservableCollection<GemViewModel> SupportSlots { get; } = [];

    /// <summary>True when this group is granted by a tree node / item (the calc
    /// engine regenerates it on every recalc). Such groups can't be removed and
    /// show their origin instead.</summary>
    public bool   IsGranted   { get; }
    /// <summary>Display name of the granting source (e.g. the ascendancy node), or "".</summary>
    public string SourceLabel { get; }
    public bool   CanRemove   => !IsGranted;
    /// <summary>Localised "From: <node>" badge shown on granted groups.</summary>
    public string SourceBadge => IsGranted
        ? string.Format(LocalizationService.Get("Skill_GrantedBy"),
                        GameTranslationService.TPassiveName(SourceLabel))
        : "";

    public IRelayCommand AddGemCommand     { get; }
    public IRelayCommand RemoveGroupCommand { get; }

    public SkillGroupViewModel(SkillsTabViewModel parent, SkillGroupEntry entry, bool isMain,
                               IEnumerable<GemEntry> gems)
    {
        _parent  = parent;
        Index    = entry.Index;
        _syncing = true;
        _isMain    = isMain;
        _isEnabled = entry.IsEnabled;
        _isTrigger = entry.IsTrigger;
        _includeInFullDps = entry.IncludeInFullDPS;
        _syncing = false;

        IsGranted   = !string.IsNullOrEmpty(entry.Source);
        SourceLabel = entry.SourceLabel;

        AddGemCommand      = new RelayCommand(() => _parent.AddGem(Index));
        RemoveGroupCommand = new RelayCommand(() => _parent.RemoveGroup(Index), () => CanRemove);

        RebuildGems(gems);
    }

    partial void OnIsEnabledChanged(bool value)
    {
        if (_syncing) return;
        _parent.SyncGroupEnabled(Index, value);
    }

    partial void OnIncludeInFullDpsChanged(bool value)
    {
        if (_syncing) return;
        _parent.SyncGroupIncludeInFullDps(Index, value);
    }

    public void RebuildGems(IEnumerable<GemEntry> gems)
    {
        var list = gems.ToList();

        // First non-support gem is the active skill shown in the header
        GemEntry? activeEntry = null;
        int       activeIdx   = 0;
        for (int i = 0; i < list.Count; i++)
        {
            if (!list[i].IsSupport) { activeEntry = list[i]; activeIdx = i + 1; break; }
        }

        if (activeEntry != null)
        {
            if (ActiveGem == null)
                ActiveGem = new GemViewModel(_parent, Index, activeIdx, activeEntry);
            else
                ActiveGem.UpdateFrom(activeEntry, activeIdx);
        }
        else
        {
            ActiveGem = null;
        }

        // Rebuild support slots.
        // For CastOn groups, gems 2+ are active (isSupport=false) — include them too.
        // GemViewModel.FilteredGemNames uses AvailableGemNameItems which auto-switches
        // to ActiveGemNameItems when IsSupport=false, so dropdowns show the right list.
        SupportSlots.Clear();
        for (int i = 0; i < list.Count; i++)
        {
            if (i + 1 == activeIdx) continue; // skip the main active gem
            SupportSlots.Add(new GemViewModel(_parent, Index, i + 1, list[i]));
        }
        while (SupportSlots.Count < 5)
            SupportSlots.Add(MakeEmptySlot());
    }

    private GemViewModel MakeEmptySlot() =>
        new(_parent, Index, 0, new GemEntry("", 19, 20, true, true));
}

// ── SkillsTabViewModel ────────────────────────────────────────────────────────

public partial class SkillsTabViewModel : ViewModelBase
{
    private readonly LuaHost    _host;
    private readonly BuildModel _build;
    private readonly Action?    _onStatsChanged;
    private readonly Action?    _onGroupsChanged;

    // Cache: gem name → processed tooltip entries (null = no tooltip)
    private Dictionary<string, IReadOnlyList<GemTooltipEntry>?> _gemNameTooltipCache = new();

    // Full name+color lists (for dropdown items)
    public IReadOnlyList<GemNameItem> ActiveGemNameItems  { get; private set; }
    public IReadOnlyList<GemNameItem> SupportGemNameItems { get; private set; }

    // Combined list (actives first, then supports) shown in "support" slot
    // dropdowns when the group is a trigger / meta group.
    public IReadOnlyList<GemNameItem> TriggerSlotGemNameItems { get; private set; }

    // Plain string lists (for validation)
    public IReadOnlyList<string> ActiveGemNames  { get; }
    public IReadOnlyList<string> SupportGemNames { get; }
    public IReadOnlyList<string> TriggerSlotGemNames { get; }

    /// <summary>True if the group at <paramref name="groupIdx"/> is a trigger /
    /// meta group whose support slots may host an active spell gem.</summary>
    public bool IsTriggerGroup(int groupIdx)
    {
        foreach (var g in Groups)
            if (g.Index == groupIdx) return g.IsTrigger;
        return false;
    }

    // ── Support-gem attribute limits ───────────────────────────────────────
    // Max support gems of an attribute = final character attribute ÷ 5
    // (1 slot per 5 points). "Installed" counts support gems of that attribute
    // across ALL skill groups.

    private int FinalAttr(string key) =>
        _build.AllStats.TryGetValue(key, out var v) && v is not null
            ? (int)Math.Floor(Convert.ToDouble(v)) : 0;

    public int AttrMax(GemAttr a) => a switch
    {
        GemAttr.Str => FinalAttr("Str") / 5,
        GemAttr.Dex => FinalAttr("Dex") / 5,
        GemAttr.Int => FinalAttr("Int") / 5,
        _ => 0,
    };

    public int AttrInstalled(GemAttr a)
    {
        int n = 0;
        foreach (var g in Groups)
            foreach (var slot in g.SupportSlots)
                if (!slot.IsEmpty && slot.IsSupport && GemAttrUtil.FromColor(slot.Color) == a)
                    n++;
        return n;
    }

    public int TotalSupportInstalled()
    {
        int n = 0;
        foreach (var g in Groups)
            foreach (var slot in g.SupportSlots)
                if (!slot.IsEmpty && slot.IsSupport) n++;
        return n;
    }

    public int TotalSupportMax() =>
        AttrMax(GemAttr.Str) + AttrMax(GemAttr.Dex) + AttrMax(GemAttr.Int);

    /// <summary>Count of triggered active-skill gems in a group's slots (non-support,
    /// non-empty) — the "Skills" counter for Cast-on/meta groups.</summary>
    public int SkillSlotsInGroup(int groupIdx)
    {
        var g = Groups.FirstOrDefault(x => x.Index == groupIdx);
        if (g is null) return 0;
        int n = 0;
        foreach (var s in g.SupportSlots)
            if (!s.IsEmpty && !s.IsSupport) n++;
        return n;
    }

    public ObservableCollection<SkillGroupViewModel> Groups { get; } = [];

    [ObservableProperty] private SkillGroupViewModel? _selectedGroup;
    [ObservableProperty] private string _newGroupGemName = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilteredActiveGemNameItems))]
    private string _newGroupSearch = "";

    /// <summary>Filtered list of active gem names for the "+ Add Skill" picker popup.
    /// Filters by both English name and translated display name (case-insensitive).</summary>
    public IReadOnlyList<GemNameItem> FilteredActiveGemNameItems
    {
        get
        {
            var s = _newGroupSearch;
            if (string.IsNullOrWhiteSpace(s)) return ActiveGemNameItems;
            return ActiveGemNameItems.Where(g =>
                g.Name.Contains(s, StringComparison.OrdinalIgnoreCase) ||
                g.DisplayName.Contains(s, StringComparison.OrdinalIgnoreCase)
            ).ToList();
        }
    }

    /// <summary>Pick a skill from the "+ Add Skill" popup. Creates a new group
    /// with the chosen gem and selects it. Clears the search text afterwards.</summary>
    public void AddGroupFromPicker(string gemName)
    {
        if (string.IsNullOrWhiteSpace(gemName)) return;
        _host.AddSkillGroupWithGem(gemName);
        Refresh();
        SelectedGroup = Groups.LastOrDefault();
        _onGroupsChanged?.Invoke();
        NewGroupSearch = "";
    }

    public SkillsTabViewModel(LuaHost host, BuildModel build,
        Action? onStatsChanged  = null,
        Action? onGroupsChanged = null)
    {
        _host            = host;
        _build           = build;
        _onStatsChanged  = onStatsChanged;
        _onGroupsChanged = onGroupsChanged;

        var (activeNames, supportNames) = host.GetAvailableGemNames();
        var colors = host.GetGemColors();

        ActiveGemNames  = activeNames;
        SupportGemNames = supportNames;
        TriggerSlotGemNames = activeNames.Concat(supportNames).ToList();
        ActiveGemNameItems  = BuildGemNameItems(activeNames,  colors);
        SupportGemNameItems = BuildGemNameItems(supportNames, colors);
        TriggerSlotGemNameItems = ActiveGemNameItems.Concat(SupportGemNameItems).ToList();

        LocalizationService.Instance.LanguageChanged += (_, _) =>
        {
            // Clear tooltip cache so translated strings are regenerated on next hover
            _gemNameTooltipCache = new();
            ActiveGemNameItems  = BuildGemNameItems(ActiveGemNames,  colors);
            SupportGemNameItems = BuildGemNameItems(SupportGemNames, colors);
            TriggerSlotGemNameItems = ActiveGemNameItems.Concat(SupportGemNameItems).ToList();
            OnPropertyChanged(nameof(ActiveGemNameItems));
            OnPropertyChanged(nameof(SupportGemNameItems));
            OnPropertyChanged(nameof(TriggerSlotGemNameItems));
            Refresh();
        };
        Refresh();
    }

    public void Refresh()
    {
        var mainIdx = _host.GetMainSkillGroupIndex();
        var prevIdx = SelectedGroup?.Index;
        Groups.Clear();
        foreach (var g in _host.GetSkillGroups())
        {
            var gems = _host.GetGemsInGroup(g.Index);
            Groups.Add(new SkillGroupViewModel(this, g, g.Index == mainIdx, gems));
        }
        SelectedGroup = Groups.FirstOrDefault(g => g.Index == prevIdx)
                     ?? Groups.FirstOrDefault(g => g.IsMain)
                     ?? Groups.FirstOrDefault();
    }

    [RelayCommand(CanExecute = nameof(CanSetAsMain))]
    private void SetAsMain()
    {
        if (SelectedGroup is null) return;
        _host.SetActiveSkillGroup(SelectedGroup.Index);
        _build.Refresh();
        var mainIdx = SelectedGroup.Index;
        foreach (var g in Groups)
            g.IsMain = g.Index == mainIdx;
        _onGroupsChanged?.Invoke();
    }

    private bool CanSetAsMain() => SelectedGroup is not null;

    /// <summary>Refresh IsMain flags and sync SelectedGroup to the new main when the
    /// main skill is changed from outside (e.g. the unified top selector in BuildPageView).
    /// Avoids the heavy full Refresh().</summary>
    public void RefreshMainFlag()
    {
        var mainIdx = _host.GetMainSkillGroupIndex();
        SkillGroupViewModel? newMain = null;
        foreach (var g in Groups)
        {
            g.IsMain = g.Index == mainIdx;
            if (g.IsMain) newMain = g;
        }
        if (newMain is not null && !ReferenceEquals(SelectedGroup, newMain))
            SelectedGroup = newMain;
    }

    partial void OnSelectedGroupChanged(SkillGroupViewModel? value)
    {
        // Drive the per-row IsSelected flag ourselves so the group list can render
        // a single, clean selection highlight instead of ListBox selection chrome.
        foreach (var g in Groups)
            g.IsSelected = ReferenceEquals(g, value);
        SetAsMainCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void AddGroup()
    {
        var name = NewGroupGemName.Trim();
        if (!string.IsNullOrEmpty(name))
            _host.AddSkillGroupWithGem(name);
        else
            _host.AddSkillGroup();

        Refresh();
        SelectedGroup = Groups.LastOrDefault();
        _onGroupsChanged?.Invoke();
    }

    // ── Called by SkillGroupViewModel ──────────────────────────────────────

    public void RemoveGroup(int groupIdx)
    {
        _host.RemoveSkillGroup(groupIdx);
        _build.Refresh();
        Refresh();
        _onGroupsChanged?.Invoke();
    }

    public void AddGem(int groupIdx)
    {
        _host.AddGemToGroup(groupIdx);
        RebuildGroupGems(groupIdx);
        AfterModify();
    }

    // ── Called by GemViewModel ─────────────────────────────────────────────

    public void RemoveGem(int groupIdx, int gemIdx)
    {
        _host.RemoveGemFromGroup(groupIdx, gemIdx);
        RebuildGroupGems(groupIdx);
        AfterModify();
    }

    public void SyncGemLevel(int groupIdx, int gemIdx, int level)
    {
        _host.SetGemLevel(groupIdx, gemIdx, level);
        AfterModify();
    }

    public void SyncGemQuality(int groupIdx, int gemIdx, int quality)
    {
        _host.SetGemQuality(groupIdx, gemIdx, quality);
        AfterModify();
    }

    public void SyncGemEnabled(int groupIdx, int gemIdx, bool enabled)
    {
        _host.SetGemEnabled(groupIdx, gemIdx, enabled);
        AfterModify();
    }

    public void CommitGemName(int groupIdx, int gemIdx, string name)
    {
        if (gemIdx == 0)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            _host.AddGemToGroup(groupIdx);
            var allGems = _host.GetGemsInGroup(groupIdx);
            gemIdx = allGems.Count;
        }
        _host.SetGemName(groupIdx, gemIdx, name);
        RebuildGroupGems(groupIdx);
        AfterModify();
    }

    public void SyncGroupEnabled(int groupIdx, bool enabled)
    {
        _host.SetGroupEnabled(groupIdx, enabled);
        AfterModify();
    }

    public void SyncGroupIncludeInFullDps(int groupIdx, bool include)
    {
        _host.SetGroupIncludeInFullDPS(groupIdx, include);
        AfterModify();
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private void RebuildGroupGems(int groupIdx)
    {
        var group = Groups.FirstOrDefault(g => g.Index == groupIdx);
        if (group == null) return;
        // Re-derive IsTrigger from Lua because the user might have just typed
        // a meta gem name into the active slot — its skillTypes appear only
        // after ProcessSocketGroup runs in LuaHost.SetGemName.
        foreach (var entry in _host.GetSkillGroups())
            if (entry.Index == groupIdx) { group.IsTrigger = entry.IsTrigger; break; }
        group.RebuildGems(_host.GetGemsInGroup(groupIdx));
    }

    public IReadOnlyList<GemTooltipLine> GetGemTooltipLines(int groupIdx, int gemIdx) =>
        _host.GetGemTooltip(groupIdx, gemIdx);

    // ── Dropdown tooltip helpers ───────────────────────────────────────────

    private IReadOnlyList<GemNameItem> BuildGemNameItems(
        IReadOnlyList<string> names, Dictionary<string, string> colors) =>
        names.Select(n => new GemNameItem(n, colors.GetValueOrDefault(n, "#CDD6F4"),
            GetTooltipEntriesForGemName)).ToList();

    /// <summary>Lazy tooltip loader for dropdown <see cref="GemNameItem"/>s.
    /// Results are cached per gem name; cache is invalidated on language change.</summary>
    public IReadOnlyList<GemTooltipEntry>? GetTooltipEntriesForGemName(string gemName)
    {
        if (_gemNameTooltipCache.TryGetValue(gemName, out var cached)) return cached;
        var raw = _host.GetGemTooltipByName(gemName);
        var result = raw.Count == 0 ? null : ProcessTooltipLines(raw, hasStats: false);
        _gemNameTooltipCache[gemName] = result;
        return result;
    }

    /// <summary>Shared tooltip-line processing (used by <see cref="GemViewModel.RefreshTooltip"/>
    /// and the dropdown tooltip loader). When <paramref name="hasStats"/> is false,
    /// raw_stats lines are omitted since there is no live calcLib context.</summary>
    internal static IReadOnlyList<GemTooltipEntry>? ProcessTooltipLines(
        IReadOnlyList<GemTooltipLine> raw, bool hasStats = true,
        Func<Dictionary<string, double>, List<string>>? describeStats = null)
    {
        var entries = new List<GemTooltipEntry>();
        bool skipStats = false;

        for (int i = 0; i < raw.Count; i++)
        {
            var l = raw[i];
            switch (l.Kind)
            {
                case "raw_stats":
                    if (!hasStats) break; // skip stat block entirely in dropdown mode
                    if (describeStats != null)
                    {
                        // parse and render localised stats
                        var parts = l.Text.Split('|');
                        var dict = new Dictionary<string, double>(StringComparer.Ordinal);
                        for (int p = 1; p < parts.Length; p++)
                        {
                            var eq = parts[p].IndexOf('=');
                            if (eq <= 0) continue;
                            if (double.TryParse(parts[p][(eq+1)..],
                                System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out var d))
                                dict[parts[p][..eq]] = d;
                        }
                        var ruLines = describeStats(dict);
                        if (ruLines.Count > 0)
                        {
                            foreach (var line in ruLines)
                                entries.Add(MakeEntry(line, "#89B4FA"));
                            skipStats = true;
                        }
                        else skipStats = false;
                    }
                    break;

                case "stat":
                    // English fallback (PoB's describeStats) when no localised raw_stats
                    // template matched. Skill-stat phrasing ("while in X", "Supported
                    // Skills have…", "per Demonflame") isn't covered by the item-mod
                    // templates, so translation here needs proper gem_stats_templates.json
                    // coverage (data pipeline) rather than a reuse of TooltipLine.
                    if (!skipStats)
                        entries.Add(MakeEntry(l.Text, "#89B4FA"));
                    break;

                case "sep":
                    if (skipStats && i > 0 && raw[i-1].Kind != "raw_stats") skipStats = false;
                    entries.Add(new GemTooltipEntry("", "#313244", IsSep: true));
                    break;

                case "name":
                    entries.Add(MakeEntry(GameTranslationService.TGem(l.Text), "#CDD6F4", highlight: false));
                    break;
                case "tag":
                    entries.Add(MakeEntry(GameTranslationService.TGemTagLine(l.Text), "#585B70", highlight: false));
                    break;
                case "meta":
                    entries.Add(MakeEntry(GameTranslationService.TGemMetaLine(l.Text), "#A6ADC8"));
                    break;
                case "desc":
                    entries.Add(MakeEntry(GameTranslationService.TSkillDescription(l.Text), "#F9E2AF", highlight: false));
                    break;
                default:
                    entries.Add(MakeEntry(l.Text, "#CDD6F4"));
                    break;
            }
        }

        return entries.Count > 0 ? entries : null;
    }

    // Numeric tokens (integers, decimals, percentages, signed) highlighted in a contrasting colour.
    private const string NumberHighlightColor = "#FAB387";
    private static readonly System.Text.RegularExpressions.Regex NumberRegex =
        new(@"[+\-]?\d+(?:[.,]\d+)?%?", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static GemTooltipEntry MakeEntry(string text, string baseColor, bool highlight = true)
    {
        if (string.IsNullOrEmpty(text))
            return new GemTooltipEntry(text, baseColor,
                Segments: new[] { new TooltipTextSegment(text, baseColor) });

        var matches = highlight ? NumberRegex.Matches(text) : null;
        if (matches == null || matches.Count == 0)
            return new GemTooltipEntry(text, baseColor,
                Segments: new[] { new TooltipTextSegment(text, baseColor) });

        var segs = new List<TooltipTextSegment>(matches.Count * 2 + 1);
        int pos = 0;
        foreach (System.Text.RegularExpressions.Match m in matches)
        {
            if (m.Index > pos)
                segs.Add(new TooltipTextSegment(text[pos..m.Index], baseColor));
            segs.Add(new TooltipTextSegment(m.Value, NumberHighlightColor));
            pos = m.Index + m.Length;
        }
        if (pos < text.Length)
            segs.Add(new TooltipTextSegment(text[pos..], baseColor));

        return new GemTooltipEntry(text, baseColor, Segments: segs);
    }

    private void AfterModify()
    {
        _build.Refresh();
        _onStatsChanged?.Invoke();
    }
}
