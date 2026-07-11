using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PBLApp.Core;
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

    // ── Heat map / Power Report ────────────────────────────────────────────
    [ObservableProperty] private bool _heatmapEnabled;
    [ObservableProperty] private double _powerPanelWidth = 320;
    [ObservableProperty] private PowerStatVm? _selectedPowerStat;
    [ObservableProperty] private bool _isPowerBuilding;
    // True only while the calc actually runs on the main host (pool disabled, or the
    // fallback retry after "all workers failed") — the only paths that need the modal
    // overlay, since the pool path leaves the main host free for UI interaction.
    [ObservableProperty] private bool _isPowerBuildingModal;
    [ObservableProperty] private int  _powerBuildProgress;
    [ObservableProperty] private bool _isPowerStale;
    [ObservableProperty] private int    _powerSortIndex;   // 0 = by stat gain, 1 = per point
    [ObservableProperty] private string _powerFilter = "";
    [ObservableProperty] private string _powerWorkersStatus = "";
    [ObservableProperty] private string? _powerError;
    [ObservableProperty] private int    _workerCountIndex;  // 0=Auto,1=1,2=2,3=4,4=Off

    private NodePowerResult? _lastPowerResult;

    public IReadOnlyList<PowerStatVm> PowerStatOptions { get; private set; } = [];
    public ObservableCollection<NodePowerRowViewModel> PowerReport { get; } = [];

    /// <summary>Latest heat-map result handed to the canvas; null = no overlay.</summary>
    public NodePowerResult? PowerOverlay { get; private set; }

    /// <summary>Ids of the 10 unallocated, non-cluster nodes with the strongest
    /// <see cref="PowerOverlay"/> power (direction-aware for lower-is-better
    /// stats), handed to the canvas for the yellow accent rings. Recomputed
    /// alongside <see cref="PowerOverlay"/>; null/empty when there's no overlay.</summary>
    public IReadOnlyList<int>? PowerTopIds { get; private set; }

    /// <summary>Raised when <see cref="PowerOverlay"/> changes so the View can push
    /// it onto the TreeCanvas (the canvas is a View-layer control).</summary>
    public event EventHandler? PowerOverlayChanged;

    public string PowerStaleLabel => LocalizationService.Get("Tree_Power_Stale");
    public string PowerEmptyLabel  => LocalizationService.Get("Tree_Power_Empty");
    public bool   HasPowerReport   => PowerReport.Count > 0;

    // ── Commands ───────────────────────────────────────────────────────────

    /// <summary>Left-click: allocate node (or change attribute if already allocated attribute node).</summary>
    public IAsyncRelayCommand<int?> AllocNodeCommand { get; }

    /// <summary>Right-click: deallocate node.</summary>
    public IAsyncRelayCommand<int?> DeallocNodeCommand { get; }

    public IRelayCommand RefreshPowerCommand { get; }
    public IRelayCommand GeneratePowerCommand { get; }
    public IRelayCommand CancelPowerCommand   { get; }
    public IRelayCommand<NodePowerRowViewModel?> FocusReportRowCommand { get; }

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

    /// <summary>Hover a node by id, or auto-pick an unallocated node a few hops
    /// from the allocated tree when id is null, so the path-preview overlay can
    /// be captured (IPC test hook). Returns the chosen node id, name, path length.</summary>
    public Func<int?, (int Id, string Name, int PathLen)?>? HoverCanvasNode { get; set; }

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

    // ── Async optimistic allocation ────────────────────────────────────────
    // spec:AllocNode / spec:DeallocNode cost ~130 ms each (intrinsic upstream
    // path/depends rebuild). Running them on the UI thread froze it per click.
    // Instead we update the allocated set optimistically (C# predicts the path)
    // and repaint instantly, then run the heavy Lua on a background thread,
    // serialized by a semaphore so NLua is never touched concurrently. While a
    // background op runs, IsToggleBusy makes the hover provider skip its Lua call
    // (the only other tree Lua the UI thread would issue). The real state is
    // reconciled from Lua once the burst drains.
    private readonly SemaphoreSlim _luaQueue = new(1, 1);
    private System.Threading.CancellationTokenSource? _powerCts;
    [ObservableProperty] private bool _isToggleBusy;   // bound to TreeCanvas.IsBusy for the loading spinner
    private int _pendingToggles;
    private Dictionary<int, TreeNodeDto>? _nodeIndex;

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

        PowerStatOptions = host.GetPowerStatList().Select(o => new PowerStatVm(o)).ToList();
        _selectedPowerStat = PowerStatOptions.FirstOrDefault(o => o.Option.CombinedOffDef)
                          ?? PowerStatOptions.FirstOrDefault();
        _workerCountIndex = MapWorkerPrefToIndex(AppPreferences.Get("tree.powerWorkers"));
        RefreshPowerCommand   = new RelayCommand(() => _ = BuildPowerAsync());
        GeneratePowerCommand  = new RelayCommand(() => _ = BuildPowerAsync());
        CancelPowerCommand    = new RelayCommand(() => _powerCts?.Cancel());
        FocusReportRowCommand = new RelayCommand<NodePowerRowViewModel?>(FocusReportRow);

        LocalizationService.Instance.LanguageChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(AllocatedLabel));
            OnPropertyChanged(nameof(NodesLabel));
            OnPropertyChanged(nameof(PointsLabel));
            OnPropertyChanged(nameof(WeaponSetLabel));
            OnPropertyChanged(nameof(PowerStaleLabel));
            OnPropertyChanged(nameof(PowerEmptyLabel));
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
        MarkPowerStale();
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

        // Attribute nodes route through the attribute picker and are rare. Still
        // gate their Lua behind the same queue so they never overlap a running
        // background toggle.
        if (node.IsAttribute && SelectAttribute != null)
        {
            bool wasAlloc = AllocatedIds.Contains(nodeId);
            int attrIndex = await SelectAttribute();
            if (attrIndex == 0) return;
            await _luaQueue.WaitAsync();
            try
            {
                IsToggleBusy = true;
                int r = await Task.Run(() => wasAlloc
                    ? _host.ChangeAttributeNode(nodeId, attrIndex)
                    : _host.AllocAttributeNode(nodeId, attrIndex));
                if (r != 0) await Task.Run(() => _host.RecalcStats());
                IsToggleBusy = false;
                if (r != 0) { RefreshNodes(); RefreshPointUsage(); _onStatsChanged?.Invoke(); }
            }
            finally { IsToggleBusy = false; _luaQueue.Release(); }
            return;
        }

        // Already allocated, non-attribute → LMB is a no-op (RMB deallocates).
        if (AllocatedIds.Contains(nodeId)) return;

        // Optimistic: predict the path C#-side and paint it allocated now, then
        // run the real allocation in the background.
        var path = PredictAllocPath(nodeId);
        if (path.Count == 0) return;
        var set = new HashSet<int>(AllocatedIds);
        foreach (var p in path) set.Add(p);
        AllocatedIds = set;

        Interlocked.Increment(ref _pendingToggles);
        IsToggleBusy = true;
        _ = RunBackgroundToggle(nodeId, allocate: true);
    }

    private Task DeallocNodeAsync(int nodeId, CancellationToken ct = default)
    {
        if (!AllocatedIds.Contains(nodeId)) return Task.CompletedTask;

        // Optimistic: drop the node now (dependents are corrected on reconcile).
        var set = new HashSet<int>(AllocatedIds);
        set.Remove(nodeId);
        AllocatedIds = set;

        Interlocked.Increment(ref _pendingToggles);
        IsToggleBusy = true;
        _ = RunBackgroundToggle(nodeId, allocate: false);
        return Task.CompletedTask;
    }

    /// <summary>Shortest path (node ids) from the allocated tree to
    /// <paramref name="target"/> over node links — what <c>spec:AllocNode</c>
    /// will allocate. Empty when unreachable. Main-tree ↔ ascendancy boundary
    /// is not crossed. Used to paint the allocation optimistically.</summary>
    private List<int> PredictAllocPath(int target)
    {
        var byId = _nodeIndex ??= Nodes.ToDictionary(n => n.Id);
        if (!byId.TryGetValue(target, out var tNode)) return [];
        var alloc = AllocatedIds;
        if (alloc.Count == 0 || alloc.Contains(target)) return [];

        var visited = new HashSet<int>(alloc);
        var parent  = new Dictionary<int, int>();
        var queue   = new Queue<int>(alloc);
        bool found  = false;
        while (queue.Count > 0)
        {
            int cur = queue.Dequeue();
            if (cur == target) { found = true; break; }
            if (!byId.TryGetValue(cur, out var curNode)) continue;
            bool curAsc = !string.IsNullOrEmpty(curNode.AscendancyName);
            foreach (var lid in curNode.LinkedIds)
            {
                if (!visited.Add(lid)) continue;
                if (!byId.TryGetValue(lid, out var lnode)) continue;
                if (curAsc != !string.IsNullOrEmpty(lnode.AscendancyName)) continue;
                parent[lid] = cur;
                queue.Enqueue(lid);
            }
        }
        if (!found) return [];
        var path = new List<int> { target };
        int n = target;
        while (parent.TryGetValue(n, out var p)) { if (alloc.Contains(p)) break; path.Add(p); n = p; }
        return path;
    }

    /// <summary>Runs the heavy <c>spec:AllocNode/DeallocNode</c> on a background
    /// thread, serialized so NLua is single-threaded, then (when the burst has
    /// drained) recalcs stats and reconciles the allocated set from Lua.</summary>
    private async Task RunBackgroundToggle(int nodeId, bool allocate)
    {
        await _luaQueue.WaitAsync();
        try
        {
            // IsToggleBusy stays true for the whole burst (set on enqueue, cleared
            // when the queue drains) so the spinner doesn't flicker between ops.
            await Task.Run(() =>
            {
                if (allocate) _host.AllocNode(nodeId, deferRecalc: true);
                else          _host.DeallocNode(nodeId, deferRecalc: true);
            });

            // Only the last toggle of a burst pays the stats recalc + reconcile.
            bool last = Volatile.Read(ref _pendingToggles) <= 1;
            if (last)
            {
                await Task.Run(() => _host.RecalcStats());

                // Back on the UI thread, semaphore still held → no concurrent Lua.
                RefreshAllocated();
                RefreshPointUsage();
                _onStatsChanged?.Invoke();
                MarkPowerStale();
            }
        }
        catch { /* best-effort; reconcile below restores truth */ }
        finally
        {
            _luaQueue.Release();
            if (Interlocked.Decrement(ref _pendingToggles) == 0)
                IsToggleBusy = false;
        }
    }

    /// <summary>Test/IPC hook: toggle a node (alloc if unallocated, else dealloc)
    /// through the real command path and return the synchronous wall-clock cost.
    /// When <paramref name="nodeId"/> is null, auto-picks an unallocated node
    /// directly adjacent to the allocated tree. Returns (id, nowAllocated, ms).</summary>
    public async Task<(int Id, bool Allocated, double Ms)?> ToggleNodeTimed(int? nodeId)
    {
        int id;
        if (nodeId is { } given) id = given;
        else
        {
            var cand = Nodes.FirstOrDefault(n =>
                !AllocatedIds.Contains(n.Id) &&
                !n.IsAttribute &&   // attribute nodes open the picker dialog → would block
                n.Type is not ("Keystone" or "Socket" or "Mastery" or "ClassStart" or "AscendClassStart") &&
                string.IsNullOrEmpty(n.AscendancyName) &&
                n.LinkedIds.Any(l => AllocatedIds.Contains(l)));
            if (cand == null) return null;
            id = cand.Id;
        }

        // Attribute nodes would trigger the attribute-picker dialog and hang a
        // headless timing run, so the timing hook doesn't handle them.
        var picked = Nodes.FirstOrDefault(n => n.Id == id);
        if (picked is { IsAttribute: true }) return null;

        bool wasAllocated = AllocatedIds.Contains(id);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        if (wasAllocated) await DeallocNodeAsync(id);
        else              await AllocNodeAsync(id);
        sw.Stop();
        return (id, AllocatedIds.Contains(id), sw.Elapsed.TotalMilliseconds);
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
    // Skip the Lua hover lookup while a background toggle owns the Lua state —
    // the canvas falls back to the node's cached stats, so no concurrent NLua.
    public NodeHoverInfo? GetNodeHoverInfo(int nodeId) =>
        IsToggleBusy ? null : _host.GetNodeHoverInfo(nodeId);

    partial void OnHeatmapEnabledChanged(bool value)
    {
        if (value) return;                 // panel shows; generation is manual (Generate button)
        _powerCts?.Cancel();
        PowerReport.Clear();
        OnPropertyChanged(nameof(HasPowerReport));
        IsPowerStale = false;
        PowerOverlay = null;
        PowerTopIds = null;
        PowerOverlayChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Runs the heat-map calc via the process-wide worker pool (falling
    /// back to the main host when the pool is disabled or all workers died),
    /// then fills the report + canvas overlay. See <see cref="TreePowerService"/>
    /// and <c>PBLEngine.NodePowerOrchestrator</c> for the pool-dispatch details.</summary>
    private async Task BuildPowerAsync()
    {
        var stat = SelectedPowerStat?.Option;
        if (stat is null || IsPowerBuilding) return;
        _powerCts?.Cancel();
        var cts = new System.Threading.CancellationTokenSource();
        _powerCts = cts;
        var ctx = SynchronizationContext.Current;
        void Post(Action a) { if (ctx != null) ctx.Post(_ => a(), null); else a(); }
        LuaWorkerPool? pool = null;
        bool usedMainHost = false;   // any RunAsync that touches _host → hover must be gated
        try
        {
            IsPowerBuilding = true;
            IsPowerBuildingModal = false;
            _staleWhileBuilding = false;
            PowerBuildProgress = 0;
            PowerError = null;

            // 1. Everything the main host needs to hand off — before batches are
            //    dispatched (its Lua is not touched again until the end). Held under
            //    _luaQueue: a RunBackgroundToggle burst (AllocNode/RecalcStats) may
            //    still be running on _host from the thread pool (click node → stale →
            //    immediate Refresh), and two threads in the native Lua state crash it.
            //    Released right after the snapshot — pool paths never touch _host
            //    again, and the fallback worker re-acquires the gate in PrepareAsync.
            string? xml = null; IReadOnlyList<PowerNodeInfo> nodes = [];
            await _luaQueue.WaitAsync();
            try
            {
                await Task.Run(() =>
                {
                    xml   = _host.SaveBuildToXml();
                    nodes = _host.GetPowerNodeList();
                });
            }
            finally { _luaQueue.Release(); }
            if (string.IsNullOrEmpty(xml) || nodes.Count == 0) return;

            // 2. Workers: the pool (lazily warming up) or a fallback onto the main host.
            pool = TreePowerService.GetPool(RepoRoot);
            IReadOnlyList<Task<IPowerWorker>> workers;
            if (pool != null)
            {
                workers = pool.EnsureStarted();
                // ReadyChanged fires on a background (pool) thread — marshal through
                // the UI SynchronizationContext captured above, not one read inside
                // the handler (which would see no context and update off-thread).
                // The subscription is scoped to this build (removed in finally):
                // the status label only matters while a calc is running, the static
                // pool outlives this VM (never let it pin us), and a pool recreated
                // with a new size would make a lingering subscription a stale no-op.
                _poolReadyHandler = () => Post(UpdateWorkersStatus);
                pool.ReadyChanged += _poolReadyHandler;
                Post(UpdateWorkersStatus);
            }
            else
            {
                workers = [Task.FromResult<IPowerWorker>(new MainHostPowerWorker(_host, _luaQueue))];
                // Fallback runs Lua on the main host from a background thread; raise
                // IsToggleBusy (v1 behaviour) so GetNodeHoverInfo skips its UI-thread
                // Lua call — a concurrent hover would crash the native KeraLua state.
                // Pool-backed builds deliberately do NOT set this: _host is idle then.
                usedMainHost = true;
                IsToggleBusy = true;
                IsPowerBuildingModal = true;
            }

            void Progress(int pc) => Post(() => PowerBuildProgress = pc);
            NodePowerResult result;
            try
            {
                result = await NodePowerOrchestrator.RunAsync(
                    xml!, stat.StatKey, stat.CombinedOffDef, nodes, workers, Progress, cts.Token);
            }
            catch (InvalidOperationException) when (pool != null)
            {
                // All pool workers died — fall back to the main host (same hover
                // gating as the pool-disabled path above).
                usedMainHost = true;
                IsToggleBusy = true;
                IsPowerBuildingModal = true;
                result = await NodePowerOrchestrator.RunAsync(
                    xml!, stat.StatKey, stat.CombinedOffDef, nodes,
                    [Task.FromResult<IPowerWorker>(new MainHostPowerWorker(_host, _luaQueue))],
                    Progress, cts.Token);
            }

            if (cts.IsCancellationRequested || !HeatmapEnabled)
            {
                // Heat map was turned off or cancelled while this build was in flight — keep it cleared.
                PowerOverlay = null;
                PowerTopIds = null;
                PowerOverlayChanged?.Invoke(this, EventArgs.Empty);
                return;
            }

            _lastPowerResult = result;
            PowerOverlay = result;
            PowerTopIds = ComputePowerTopIds(result, stat.LowerIsBetter);
            RebuildPowerRows();               // applies current sort + filter
            // A tree edit that landed mid-calc (now possible on the non-modal pool path,
            // see F1/F2) set _staleWhileBuilding via MarkPowerStale instead of IsPowerStale
            // directly (which that method no-ops while IsPowerBuilding) — the report we
            // just computed came from a snapshot taken before that edit, so it must still
            // show stale rather than being blindly cleared here.
            IsPowerStale = _staleWhileBuilding;
            PowerOverlayChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            // A cancelled fallback surfaces as InvalidOperationException("all workers
            // failed") — OCE in PrepareAsync marks the lone main-host worker dead and
            // the pool-null path has no `when (pool != null)` retry to swallow it.
            // Deliberate cancellation must exit silently, like the in-flight check above.
            if (cts.IsCancellationRequested) return;
            PowerError = ex.Message;          // surfaced in the panel (existing pattern)
        }
        finally
        {
            IsPowerBuilding = false;
            IsPowerBuildingModal = false;
            // Flush a mid-calc edit on the cancel/error exits too: those return past
            // the success path's `IsPowerStale = _staleWhileBuilding`, and the next run
            // resets the flag — without this, the on-screen (old) report would stay
            // unmarked as fresh. Order matters: only after IsPowerBuilding=false, so a
            // MarkPowerStale racing in right here takes the direct IsPowerStale=true
            // branch instead of writing a _staleWhileBuilding we've already consumed.
            if (_staleWhileBuilding && HeatmapEnabled && _lastPowerResult != null)
                IsPowerStale = true;
            if (usedMainHost)
            {
                // Don't blindly clear: a toggle enqueued while we held the Lua gate
                // set IsToggleBusy for its own burst — keep it up until that drains.
                IsToggleBusy = Volatile.Read(ref _pendingToggles) > 0;
            }
            if (pool != null && _poolReadyHandler != null)
            {
                pool.ReadyChanged -= _poolReadyHandler;
                _poolReadyHandler = null;
            }
            if (ReferenceEquals(_powerCts, cts)) _powerCts = null;
            cts.Dispose();
        }
    }

    /// <summary><see cref="IPowerWorker"/> that runs batches on the already-loaded
    /// main <see cref="LuaHost"/> — used when the worker pool is disabled (prefs
    /// "tree.powerWorkers"="0") or when every pool worker has died. Prepare/Finish
    /// hold <paramref name="luaGate"/> (the VM's tree-toggle serialization
    /// semaphore) for the whole session so a concurrent node alloc/dealloc never
    /// touches the same NLua state mid-batch — only <see cref="RunBackgroundToggle"/>
    /// and this worker ever call into <c>_host</c> from a background thread.</summary>
    private sealed class MainHostPowerWorker : IPowerWorker
    {
        private readonly LuaHost _worker;
        private readonly SemaphoreSlim _gate;
        private int _held;   // 0/1 via Interlocked so the gate can never double-release

        public MainHostPowerWorker(LuaHost host, SemaphoreSlim gate) { _worker = host; _gate = gate; }

        private void ReleaseGate()
        {
            if (Interlocked.Exchange(ref _held, 0) == 1) _gate.Release();
        }

        public async Task PrepareAsync(string buildXml, string? statKey, CancellationToken ct)
        {
            await _gate.WaitAsync(ct);
            Volatile.Write(ref _held, 1);
            try
            {
                // buildXml is not reloaded — the main host's build is already the source of truth.
                await Task.Run(() => _worker.BeginPowerSession(statKey), ct);
            }
            catch
            {
                // The orchestrator only calls FinishAsync on successfully prepared
                // workers. If BeginPowerSession throws — or the Task.Run delegate is
                // cancelled before it starts (Cancel clicked right after Generate) —
                // release the gate here, or every future tree toggle would deadlock
                // behind a permanently-held semaphore.
                ReleaseGate();
                throw;
            }
        }

        public Task<List<PowerBatchRow>> ComputeBatchAsync(int[] ids, CancellationToken ct)
            => Task.Run(() => _worker.ComputePowerBatch(ids), ct);

        public Task<List<PathPowerRow>> ComputePathBatchAsync(int[] ids, CancellationToken ct)
            => Task.Run(() => _worker.ComputePathPowerBatch(ids), ct);

        public Task FinishAsync() => Task.Run(() =>
        {
            try { _worker.EndPowerSession(); }
            finally { ReleaseGate(); }
        });
    }

    /// <summary>10 unallocated, non-cluster nodes (<see cref="NodePowerEntry.Steps"/> is
    /// null for allocated/cluster nodes — see <c>NodePower.cs</c>) with the strongest
    /// power, for the canvas's accent rings. Direction mirrors <see cref="RebuildPowerRows"/>:
    /// ascending (most negative = biggest win) for lower-is-better stats, descending
    /// otherwise; zero-power entries are excluded either way.</summary>
    private static List<int> ComputePowerTopIds(NodePowerResult result, bool lowerIsBetter)
    {
        IEnumerable<NodePowerEntry> src = result.Entries.Where(e => e.Steps != null && e.Power != 0);
        src = lowerIsBetter ? src.OrderBy(e => e.Power) : src.OrderByDescending(e => e.Power);
        return src.Take(10).Select(e => e.Id).ToList();
    }

    /// <summary>Sort + filter <see cref="_lastPowerResult"/> into <see cref="PowerReport"/>.
    /// Re-run whenever the result, sort mode, or filter text changes.</summary>
    private void RebuildPowerRows()
    {
        PowerReport.Clear();
        var r = _lastPowerResult;
        if (r == null) { OnPropertyChanged(nameof(HasPowerReport)); return; }
        bool lower = SelectedPowerStat?.Option.LowerIsBetter == true;
        IEnumerable<NodePowerEntry> src = r.Entries.Where(e => e.Power != 0);
        if (!string.IsNullOrWhiteSpace(PowerFilter))
            src = src.Where(e => TranslatedName(e).Contains(PowerFilter, StringComparison.OrdinalIgnoreCase));
        src = PowerSortIndex == 1
            // "per point": the topped-up entries (path-power computed) come first, by per-point value;
            // rows without PathPower key to 0 and would otherwise tie in arbitrary order, so break
            // the tie by |Power| descending (strongest raw stat gain first).
            ? src.OrderByDescending(e => e.PathPower != null)
                 .ThenByDescending(e => PerPoint(e) * (lower ? -1 : 1))
                 .ThenByDescending(e => Math.Abs(e.Power))
            : (lower ? src.OrderBy(e => e.Power) : src.OrderByDescending(e => e.Power));
        foreach (var e in src.Take(400))
            PowerReport.Add(MakeRow(e, lower));
        OnPropertyChanged(nameof(HasPowerReport));

        static double PerPoint(NodePowerEntry e) =>
            e.PathPower is { } pp && e.Steps is > 0 ? pp / e.Steps.Value : 0;
    }

    private NodePowerRowViewModel MakeRow(NodePowerEntry e, bool lower)
    {
        bool good = lower ? e.Power < 0 : e.Power > 0;
        return new NodePowerRowViewModel
        {
            NodeId      = e.Id,
            Name        = TranslatedName(e),
            Type        = e.Type,
            TypeBadge   = LocalizationService.Get("Tree_Power_Type_" + e.Type),
            IsAllocated = e.Alloc,
            PowerStr    = e.PowerStr,
            PerPointStr = e.PerPointStr ?? "—",
            StepsStr    = e.Steps is { } s
                            ? string.Format(LocalizationService.Get("Tree_Power_StepsFmt"), s) : "",
            PowerColor  = good ? "#A6E3A1" : "#F38BA8",
        };
    }

    private static string TranslatedName(NodePowerEntry e) => GameTranslationService.TPassiveName(e.Name);

    /// <summary>Subscribed to <c>LuaWorkerPool.ReadyChanged</c> only for the duration
    /// of a single <see cref="BuildPowerAsync"/> (added after <c>EnsureStarted</c>,
    /// removed in its <c>finally</c>). Scoping it to the build keeps the static,
    /// process-lifetime pool from pinning this VM (and its captured UI context)
    /// after the tab is gone, and sidesteps stale subscriptions when the pool is
    /// recreated with a different worker count.</summary>
    private Action? _poolReadyHandler;

    private void UpdateWorkersStatus()
    {
        // Read-only lookup: GetPool mutates (can create/Dispose the pool if the
        // worker-count pref changed since this build started), which must never
        // happen from a status-label poll mid-warmup — Current only ever reads
        // the pool already handed to this build's workers.
        var pool = TreePowerService.Current;
        PowerWorkersStatus = pool == null ? ""
            : string.Format(LocalizationService.Get(
                  pool.Ready < pool.Size && IsPowerBuilding ? "Tree_Power_Warmup" : "Tree_Power_Workers"),
              pool.Ready, pool.Size);
    }

    partial void OnPowerSortIndexChanged(int value) => RebuildPowerRows();
    partial void OnPowerFilterChanged(string value) => RebuildPowerRows();

    /// <summary>0=Auto,1=1,2=2,3=4,4=Off → persisted "tree.powerWorkers" pref
    /// ("auto"/"1"/"2"/"4"/"0"). Applied the next time <see cref="TreePowerService.GetPool"/>
    /// is asked for a pool (current in-flight builds keep their workers).</summary>
    partial void OnWorkerCountIndexChanged(int value)
    {
        AppPreferences.Set("tree.powerWorkers", value switch
        {
            1 => "1", 2 => "2", 3 => "4", 4 => "0", _ => "auto",
        });
    }

    private static int MapWorkerPrefToIndex(string? raw) => raw switch
    {
        "1" => 1, "2" => 2, "4" => 3, "0" => 4, _ => 0,
    };

    private void FocusReportRow(NodePowerRowViewModel? row)
    {
        if (row != null) FocusCanvasNode?.Invoke(row.NodeId, null);
    }

    // Set by MarkPowerStale when an edit lands while a power calc is already running
    // (now possible on the non-modal pool path) — the in-flight result was snapshotted
    // before the edit, so it must be flagged stale once it lands rather than the
    // in-progress build's success path blindly clearing IsPowerStale. Reset at the
    // start of each BuildPowerAsync run.
    private bool _staleWhileBuilding;

    /// <summary>Marks the heat map out of date after a build edit (no auto-rebuild).</summary>
    private void MarkPowerStale()
    {
        if (!HeatmapEnabled) return;
        if (IsPowerBuilding) { _staleWhileBuilding = true; return; }
        IsPowerStale = true;
    }

    partial void OnNodesChanged(IReadOnlyList<TreeNodeDto> value) => _nodeIndex = null;

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
        MarkPowerStale();
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

/// <summary>Display wrapper for a heat-map stat option with a localised label.</summary>
public sealed class PowerStatVm
{
    public PowerStatOption Option { get; }
    public PowerStatVm(PowerStatOption o) { Option = o; }
    public string DisplayName => Option.CombinedOffDef
        ? LocalizationService.Get("Tree_Power_OffDef")
        : GameTranslationService.TCalcLabel(Option.Label);
    public override string ToString() => DisplayName;
}

/// <summary>One row in the Power Report side panel.</summary>
public sealed class NodePowerRowViewModel
{
    public int    NodeId      { get; init; }
    public string Name        { get; init; } = "";
    public string Type        { get; init; } = "Normal";
    /// <summary>Localized node-type badge (e.g. "Notable" / "Нотабль").</summary>
    public string TypeBadge   { get; init; } = "";
    public string PowerStr    { get; init; } = "";
    public string PerPointStr { get; init; } = "";
    /// <summary>"for N pt" when the per-point value was computed for this entry
    /// (top-K by path power), empty otherwise.</summary>
    public string StepsStr    { get; init; } = "";
    public bool   IsAllocated { get; init; }
    public string PowerColor  { get; init; } = "#CDD6F4";
}
