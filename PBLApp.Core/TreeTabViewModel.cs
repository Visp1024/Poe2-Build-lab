using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PBLApp.Core.Localization;
using PBLApp.Core.Items;
using PBLEngine;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;

namespace PBLApp.ViewModels;

/// <summary>Display wrapper around <see cref="ClassEntry"/> with a translatable name.</summary>
public sealed class ClassDisplayVm : ObservableObject
{
    public ClassEntry Entry { get; }
    public int Id   => Entry.Id;
    public string Name => Entry.Name;
    public IReadOnlyList<AscendDisplayVm> Ascendancies { get; }
    public string DisplayName => GameTranslationService.TClassName(Entry.Name);

    public ClassDisplayVm(ClassEntry e)
    {
        Entry = e;
        Ascendancies = e.Ascendancies.Select(a => new AscendDisplayVm(a)).ToList();
        LocalizationService.Instance.LanguageChanged += (_, _) =>
            OnPropertyChanged(nameof(DisplayName));
    }

    public override string ToString() => DisplayName;
}

/// <summary>Display wrapper around <see cref="AscendEntry"/> with a translatable name.</summary>
public sealed class AscendDisplayVm : ObservableObject
{
    public AscendEntry Entry { get; }
    public int Id   => Entry.Id;
    public string Name => Entry.Name;
    public string DisplayName => GameTranslationService.TClassName(Entry.Name);

    public AscendDisplayVm(AscendEntry e)
    {
        Entry = e;
        LocalizationService.Instance.LanguageChanged += (_, _) =>
            OnPropertyChanged(nameof(DisplayName));
    }

    public override string ToString() => DisplayName;
}

public partial class TreeTabViewModel : ViewModelBase
{
    private readonly LuaHost _host;
    private readonly Action? _onStatsChanged;
    private readonly Action? _onItemsChanged;

    // ── Tree nodes ─────────────────────────────────────────────────────────

    [ObservableProperty] private IReadOnlyList<TreeNodeDto> _nodes = [];
    [ObservableProperty] private IReadOnlySet<int> _allocatedIds = new HashSet<int>();

    [ObservableProperty]
    private IReadOnlyList<(int NodeId, double RadiusWorld)> _radiusEmitters
        = Array.Empty<(int, double)>();

    /// <summary>Persistent radius rings for allocated jewel sockets that hold a
    /// radius jewel. Outer/Inner are world units; Variable marks Thread-of-Hope-like
    /// annulus jewels (Inner &gt; 0).</summary>
    [ObservableProperty]
    private IReadOnlyList<(int NodeId, double Outer, double Inner, bool Variable)> _jewelRadii
        = Array.Empty<(int, double, double, bool)>();

    /// <summary>Jewel art to paint on each allocated socket that holds a jewel, so
    /// the socketed jewel is visible on the tree.</summary>
    [ObservableProperty]
    private IReadOnlyList<(int NodeId, string BaseName, string Title, bool Unique)> _jewelIcons
        = Array.Empty<(int, string, string, bool)>();

    public int NodeCount      => Nodes.Count;
    public int AllocatedCount => AllocatedIds.Count;
    public string AllocatedLabel =>
        string.Format(LocalizationService.Get("Tree_AllocatedFmt"), AllocatedCount);
    public string NodesLabel =>
        string.Format(LocalizationService.Get("Tree_NodesFmt"), NodeCount);

    // ── Passive-point budget (used / max), incl. weapon-set point pools ────────
    private PointUsage? _pointUsage;

    public string PointsLabel =>
        _pointUsage is { } p
            ? string.Format(LocalizationService.Get("Tree_PointsFmt"), p.PassivesUsed, p.PassivesMax)
            : "";

    /// <summary>True when more passives are allocated than the budget allows — PoB shows
    /// this in red with a warning.</summary>
    public bool IsPassivesOverBudget =>
        _pointUsage is { } p && p.PassivesUsed > p.PassivesMax;

    /// <summary>Weapon-set points are only surfaced when relevant: a set has nodes
    /// allocated, or Weapon Master / Witchhunter granted extra weapon-set points.</summary>
    public bool HasWeaponSetPoints =>
        _pointUsage is { } p && (p.WeaponSet1Used > 0 || p.WeaponSet2Used > 0 || p.ExtraWeaponSetPoints > 0);

    public string WeaponSetLabel =>
        _pointUsage is { } p
            ? string.Format(LocalizationService.Get("Tree_WeaponSetFmt"),
                            p.WeaponSet1Used, p.WeaponSet2Used, p.WeaponSetMax)
            : "";

    private void RefreshPointUsage()
    {
        _pointUsage = _host.GetPointUsage();
        OnPropertyChanged(nameof(PointsLabel));
        OnPropertyChanged(nameof(IsPassivesOverBudget));
        OnPropertyChanged(nameof(WeaponSetLabel));
        OnPropertyChanged(nameof(HasWeaponSetPoints));
    }

    // ── Ascendancy backgrounds ─────────────────────────────────────────────

    public IReadOnlyDictionary<string, AscendancyBgDto> AscendancyBackgrounds { get; }

    // ── Class & ascendancy selection ───────────────────────────────────────

    public IReadOnlyList<ClassDisplayVm> Classes { get; }

    [ObservableProperty] private ClassDisplayVm?  _selectedClass;
    [ObservableProperty] private AscendDisplayVm? _selectedAscend;

    public ObservableCollection<AscendDisplayVm> AvailableAscendancies { get; } = [];

    public string AscendancyFilter =>
        SelectedAscend is { Id: > 0 } asc ? asc.Name : "";

    /// <summary>Sprite name of the current class plate ("ClassesMonk", ...) drawn at
    /// the tree center; the canvas prefers the chosen ascendancy's plate when set.</summary>
    public string ClassBackgroundImage =>
        SelectedClass != null ? "Classes" + SelectedClass.Name : "";

    // ── Search ─────────────────────────────────────────────────────────────

    [ObservableProperty] private string _searchText = "";

    // ── Commands ───────────────────────────────────────────────────────────

    /// <summary>Left-click: allocate node (or change attribute if already allocated attribute node).</summary>
    public IAsyncRelayCommand<int?> AllocNodeCommand { get; }

    /// <summary>Right-click: deallocate node.</summary>
    public IAsyncRelayCommand<int?> DeallocNodeCommand { get; }

    public string RepoRoot { get; }

    /// <summary>Tree version of the loaded spec (e.g. "0_5"); drives asset bundle selection.</summary>
    public string TreeVersion { get; }

    // ── Class-change confirmation ──────────────────────────────────────────

    /// <summary>Set by the View layer to show a confirmation dialog. Returns true = proceed.</summary>
    public Func<Task<bool>>? ConfirmClassChange { get; set; }

    /// <summary>
    /// Set by the View layer to show the attribute-selection dialog.
    /// Returns 1=Strength, 2=Dexterity, 3=Intelligence, 0=cancelled.
    /// </summary>
    public Func<Task<int>>? SelectAttribute { get; set; }

    // ── View-control bridge (set by TreeTabView, driven by the IPC tools) ──
    // The tree's zoom / pan / focus live in the TreeCanvas (View layer); these
    // callbacks let the ViewModel (and through it, IPC automation) drive them.

    /// <summary>Reads the canvas view as (scale, worldCenterX, worldCenterY).</summary>
    public Func<(double Scale, double CenterX, double CenterY)>? GetCanvasView { get; set; }

    /// <summary>Sets zoom and/or world-centre on the canvas (null = keep current).</summary>
    public Action<double?, double?, double?>? SetCanvasView { get; set; }

    /// <summary>Centres + optionally zooms the canvas on a node id. Returns false if absent.</summary>
    public Func<int, double?, bool>? FocusCanvasNode { get; set; }

    /// <summary>Multiplies zoom around the viewport centre.</summary>
    public Action<double>? ZoomCanvas { get; set; }

    /// <summary>Scrolls the canvas by a screen-pixel delta.</summary>
    public Action<double, double>? PanCanvas { get; set; }

    /// <summary>Programmatically open the jewel picker for a socket node (IPC test hook).</summary>
    public Action<int>? TriggerSocketPicker { get; set; }

    private ClassDisplayVm? _lastAppliedClass;
    private bool _suppressClassChangeCheck;

    // ── Stat-refresh debounce ──────────────────────────────────────────────
    // Allocating / deallocating a node fires Calcs.buildOutput on the Lua side
    // (heavy) AND a downstream model.Refresh + CalcsTab.Refresh on the C# side
    // (also heavy because it rebuilds VMs). Spamming clicks on the tree paid
    // that cost N times. Debounce the downstream stats refresh so a burst of
    // clicks pays it once, ~120 ms after the last click. The tree's own
    // "allocated" colouring updates immediately — only the sidebar / Calcs
    // tab numbers lag by the debounce window.
    private readonly System.Timers.Timer _statsDebounce = new(120) { AutoReset = false };
    private System.Action? _statsDispatcher;

    // ── Constructor ────────────────────────────────────────────────────────

    public TreeTabViewModel(LuaHost host, Action? onStatsChanged = null, Action? onItemsChanged = null)
    {
        _host           = host;
        _onStatsChanged = onStatsChanged;
        _onItemsChanged = onItemsChanged;
        RepoRoot        = host.RepoRoot;
        TreeVersion     = host.GetTreeVersion();

        _statsDebounce.Elapsed += (_, _) =>
        {
            // Heavy recalc runs once after the burst settles. Note that
            // RecalcStats is also dispatched back to the UI thread because
            // many model/Calcs queries touch ObservableCollection in CalcsTab.
            var d = _statsDispatcher;
            if (d != null) d();
            else { _host.RecalcStats(); _onStatsChanged?.Invoke(); }
        };

        var (nodes, allocated) = host.GetTreeData();
        _nodes          = nodes;
        _allocatedIds   = allocated;
        _radiusEmitters = _host.GetRadiusEmitters();
        _jewelRadii     = _host.GetSocketedJewelRadii();
        _jewelIcons     = _host.GetSocketedJewelIcons();

        AscendancyBackgrounds = _host.GetAscendancyBackgrounds();

        Classes = host.GetClassData().Select(c => new ClassDisplayVm(c)).ToList();

        var (classId, ascId, _, _) = host.GetCurrentClassInfo();
        _selectedClass      = Classes.FirstOrDefault(c => c.Id == classId) ?? Classes.FirstOrDefault();
        _lastAppliedClass   = _selectedClass;
        RebuildAscendancies();
        _selectedAscend     = AvailableAscendancies.FirstOrDefault(a => a.Id == ascId)
                           ?? AvailableAscendancies.FirstOrDefault();

        AllocNodeCommand  = new AsyncRelayCommand<int?>(
            (n, ct) => n.HasValue ? AllocNodeAsync(n.Value, ct)  : Task.CompletedTask);
        DeallocNodeCommand = new AsyncRelayCommand<int?>(
            (n, ct) => n.HasValue ? DeallocNodeAsync(n.Value, ct) : Task.CompletedTask);

        RefreshPointUsage();

        LocalizationService.Instance.LanguageChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(AllocatedLabel));
            OnPropertyChanged(nameof(NodesLabel));
            OnPropertyChanged(nameof(PointsLabel));
            OnPropertyChanged(nameof(WeaponSetLabel));
        };
    }

    // ── Property change handlers ───────────────────────────────────────────

    partial void OnSelectedClassChanged(ClassDisplayVm? value)
    {
        OnPropertyChanged(nameof(ClassBackgroundImage));
        if (value == null || _suppressClassChangeCheck) return;

        // Ask for confirmation when user has manually allocated nodes
        // AllocatedIds typically contains 1 (class start) or 2 (+ ascendancy start)
        bool hasUserNodes = AllocatedIds.Count > 2;

        if (hasUserNodes && ConfirmClassChange != null)
        {
            _ = HandleClassChangeAsync(_lastAppliedClass, value);
            return;
        }

        _lastAppliedClass = value;
        RebuildAscendancies();
        SelectedAscend = AvailableAscendancies.FirstOrDefault();
    }

    private async Task HandleClassChangeAsync(ClassDisplayVm? prevClass, ClassDisplayVm? newClass)
    {
        bool confirmed = await ConfirmClassChange!();
        if (!confirmed)
        {
            _suppressClassChangeCheck = true;
            SelectedClass = prevClass;
            _suppressClassChangeCheck = false;
            return;
        }

        _lastAppliedClass = newClass;
        _host.ResetAllocatedNodes();
        RebuildAscendancies();
        SelectedAscend = AvailableAscendancies.FirstOrDefault();
    }

    partial void OnSelectedAscendChanged(AscendDisplayVm? value)
    {
        if (value == null || SelectedClass == null) return;
        var (curClass, curAsc, _, _) = _host.GetCurrentClassInfo();
        if (SelectedClass.Id != curClass || value.Id != curAsc)
        {
            _host.SelectClass(SelectedClass.Id, value.Id);
            RefreshNodes();
            _onStatsChanged?.Invoke();
        }
        OnPropertyChanged(nameof(AscendancyFilter));
        OnPropertyChanged(nameof(AllocatedCount));
        OnPropertyChanged(nameof(AllocatedLabel));
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private void RebuildAscendancies()
    {
        AvailableAscendancies.Clear();
        if (SelectedClass == null) return;
        foreach (var a in SelectedClass.Ascendancies)
            AvailableAscendancies.Add(a);
    }

    private async Task AllocNodeAsync(int nodeId, CancellationToken ct = default)
    {
        var node = Nodes.FirstOrDefault(n => n.Id == nodeId);
        if (node == null) return;

        bool isAllocated = AllocatedIds.Contains(nodeId);

        if (isAllocated)
        {
            // LMB on already-allocated attribute node → let user change the attribute
            if (node.IsAttribute && SelectAttribute != null)
            {
                int attrIndex = await SelectAttribute();
                if (attrIndex == 0) return;
                int r = _host.ChangeAttributeNode(nodeId, attrIndex);
                if (r != 0) { RefreshNodes(); _onStatsChanged?.Invoke(); }
            }
            // LMB on any other allocated node → ignore (use RMB to dealloc)
        }
        else
        {
            // Allocate the node
            int result;
            bool fullRefresh = false;

            if (node.IsAttribute && SelectAttribute != null)
            {
                int attrIndex = await SelectAttribute();
                if (attrIndex == 0) return;
                result = _host.AllocAttributeNode(nodeId, attrIndex);
                fullRefresh = true; // name/stats change after attribute switch
            }
            else
            {
                result = _host.AllocNode(nodeId, deferRecalc: true);
            }

            if (result != 0)
            {
                if (fullRefresh) RefreshNodes(); else RefreshAllocated();
                ScheduleStatsRefresh();
            }
        }
    }

    private Task DeallocNodeAsync(int nodeId, CancellationToken ct = default)
    {
        int result = _host.DeallocNode(nodeId, deferRecalc: true);
        if (result != 0)
        {
            RefreshAllocated();
            ScheduleStatsRefresh();
        }
        return Task.CompletedTask;
    }

    private void RefreshNodes()
    {
        var (nodes, allocated) = _host.GetTreeData();
        Nodes          = nodes;
        AllocatedIds   = allocated;
        RadiusEmitters = _host.GetRadiusEmitters();
        JewelRadii     = _host.GetSocketedJewelRadii();
        JewelIcons     = _host.GetSocketedJewelIcons();
        OnPropertyChanged(nameof(NodeCount));
        OnPropertyChanged(nameof(NodesLabel));
        OnPropertyChanged(nameof(AllocatedCount));
        OnPropertyChanged(nameof(AllocatedLabel));
        RefreshPointUsage();
    }

    /// <summary>Captures the current synchronization context (UI thread) on first
    /// call so the debounce timer callback (which fires on a thread-pool thread)
    /// re-enters the UI dispatcher when invoking <see cref="_onStatsChanged"/>.</summary>
    private void ScheduleStatsRefresh()
    {
        if (_onStatsChanged == null) return;
        if (_statsDispatcher == null)
        {
            var ctx = SynchronizationContext.Current;
            _statsDispatcher = ctx != null
                ? () => ctx.Post(_ => { _host.RecalcStats(); RefreshPointUsage(); _onStatsChanged(); }, null)
                : () => { _host.RecalcStats(); RefreshPointUsage(); _onStatsChanged(); };
        }
        _statsDebounce.Stop();
        _statsDebounce.Start();
    }

    /// <summary>Fetch the modern hover-info packet (mod text, stat diff,
    /// path distance) for a tree node. Used by <c>TreeCanvas</c> to populate
    /// the new hover tooltip.</summary>
    public NodeHoverInfo? GetNodeHoverInfo(int nodeId) => _host.GetNodeHoverInfo(nodeId);

    private void RefreshAllocated()
    {
        // Single round-trip into Lua: both alloc set and radius emitters come
        // back from one DoString — saves an NLua marshal per click.
        var (alloc, emitters) = _host.GetAllocatedAndEmitters();
        AllocatedIds   = alloc;
        RadiusEmitters = emitters;
        // Allocating / deallocating a socket adds or removes its jewel ring + art.
        JewelRadii     = _host.GetSocketedJewelRadii();
        JewelIcons     = _host.GetSocketedJewelIcons();
        OnPropertyChanged(nameof(AllocatedCount));
        OnPropertyChanged(nameof(AllocatedLabel));
        RefreshPointUsage();
    }

    /// <summary>Re-query the socketed jewel visuals (radius rings + socket art).
    /// Called by the build page after an Items-tab operation (socketing / removing
    /// a jewel) that the tree would otherwise not learn about.</summary>
    public void RefreshJewelRadii()
    {
        JewelRadii = _host.GetSocketedJewelRadii();
        JewelIcons = _host.GetSocketedJewelIcons();
    }

    // ── In-tree jewel picker ───────────────────────────────────────────────
    // Clicking an allocated jewel socket opens a small picker listing every jewel
    // (pool + already socketed). Choosing one sockets it here; choosing one that's
    // already in another socket swaps the two.

    public ObservableCollection<JewelPickerOptionVm> JewelPickerOptions { get; } = [];

    [ObservableProperty] private bool _isJewelPickerOpen;
    [ObservableProperty] private string _jewelPickerTitle = "";
    [ObservableProperty] private double _jewelPickerX;
    [ObservableProperty] private double _jewelPickerY;

    private int _jewelPickerNodeId;

    /// <summary>Populate and open the jewel picker for the given socket node.</summary>
    public void OpenJewelPicker(int nodeId)
    {
        _jewelPickerNodeId = nodeId;
        JewelPickerTitle = LocalizationService.Get("Tree_JewelPicker_Title");

        var jewels = _host.GetSocketableJewels();
        JewelPickerOptions.Clear();

        // "Empty" first — current selection when the socket holds nothing.
        bool socketEmpty = !jewels.Any(j => j.CurrentSocketNodeId == nodeId);
        JewelPickerOptions.Add(new JewelPickerOptionVm
        {
            ItemId      = 0,
            DisplayName = LocalizationService.Get("Tree_JewelPicker_Empty"),
            NameColor   = "#9AA4B2",
            IsCurrent   = socketEmpty,
        });

        // Current socket's jewel first, then pool jewels, then jewels in other sockets.
        foreach (var j in jewels
                     .OrderBy(j => j.CurrentSocketNodeId == nodeId ? 0 : j.CurrentSocketNodeId == 0 ? 1 : 2)
                     .ThenBy(j => j.Name, StringComparer.OrdinalIgnoreCase))
        {
            bool isCurrent = j.CurrentSocketNodeId == nodeId;
            bool elsewhere = j.CurrentSocketNodeId != 0 && !isCurrent;
            var unique     = j.Rarity is "UNIQUE" or "RELIC" ? j.Name : null;
            JewelPickerOptions.Add(new JewelPickerOptionVm
            {
                ItemId      = j.ItemId,
                DisplayName = string.IsNullOrEmpty(j.Name) ? j.BaseName : j.Name,
                NameColor   = ItemSlotViewModel.RarityToColor(j.Rarity),
                IconPath    = ItemIconService.Instance.Resolve(j.BaseName, unique),
                StatusText  = isCurrent ? LocalizationService.Get("Tree_JewelPicker_Current")
                            : elsewhere ? LocalizationService.Get("Tree_JewelPicker_OtherSocket")
                            : "",
                IsCurrent   = isCurrent,
            });
        }

        IsJewelPickerOpen = true;
    }

    [RelayCommand]
    private void PickJewel(JewelPickerOptionVm? option)
    {
        IsJewelPickerOpen = false;
        if (option == null || option.IsCurrent) return;   // no change

        _host.SetSocketJewel(_jewelPickerNodeId, option.ItemId);
        RefreshJewelRadii();          // socket art + radius ring
        _onStatsChanged?.Invoke();    // stats / sidebar / calcs
        _onItemsChanged?.Invoke();    // Items-tab jewel slot panel
    }

    [RelayCommand]
    private void CloseJewelPicker() => IsJewelPickerOpen = false;

    /// <summary>Pick a jewel by item id from the currently-open picker (IPC test hook).</summary>
    public void PickJewelById(int itemId) =>
        PickJewel(JewelPickerOptions.FirstOrDefault(o => o.ItemId == itemId));
}

/// <summary>One row in the in-tree jewel picker.</summary>
public sealed class JewelPickerOptionVm
{
    public int     ItemId      { get; init; }
    public string  DisplayName { get; init; } = "";
    public string  NameColor   { get; init; } = "#CDD6F4";
    public string? IconPath    { get; init; }
    public bool    HasIcon     => !string.IsNullOrEmpty(IconPath);
    public string  StatusText  { get; init; } = "";
    public bool    HasStatus   => !string.IsNullOrEmpty(StatusText);
    /// <summary>True for the jewel already in this socket (or "Empty" when the
    /// socket is empty) — selecting it is a no-op.</summary>
    public bool    IsCurrent   { get; init; }
}
