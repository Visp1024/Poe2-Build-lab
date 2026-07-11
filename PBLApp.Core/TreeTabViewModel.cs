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

    // ── Heat map / Power Report ────────────────────────────────────────────
    [ObservableProperty] private bool _heatmapEnabled;
    [ObservableProperty] private double _powerPanelWidth = 320;
    [ObservableProperty] private PowerStatVm? _selectedPowerStat;
    [ObservableProperty] private bool _isPowerBuilding;
    [ObservableProperty] private int  _powerBuildProgress;
    [ObservableProperty] private bool _isPowerStale;

    public IReadOnlyList<PowerStatVm> PowerStatOptions { get; private set; } = [];
    public ObservableCollection<NodePowerRowViewModel> PowerReport { get; } = [];

    /// <summary>Latest heat-map result handed to the canvas; null = no overlay.</summary>
    public NodePowerResult? PowerOverlay { get; private set; }

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
        PowerOverlayChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Runs the heat-map calc on a background thread (serialised through
    /// the shared Lua queue), then fills the report + canvas overlay.</summary>
    private async Task BuildPowerAsync()
    {
        var stat = SelectedPowerStat?.Option;
        if (stat == null) return;

        await _luaQueue.WaitAsync();
        var cts = new System.Threading.CancellationTokenSource();
        _powerCts = cts;
        try
        {
            IsPowerBuilding = true;
            // BuildNodePower runs Lua on a background thread for a long time. NLua is
            // single-threaded, so — exactly like RunBackgroundToggle — we raise
            // IsToggleBusy for the duration so the canvas hover provider skips its own
            // UI-thread Lua call (GetNodeHoverInfo). Without this, a hover-render that
            // coincides with the build touches the Lua state concurrently and the native
            // KeraLua state crashes the process (no managed exception, hard kill).
            IsToggleBusy = true;
            PowerBuildProgress = 0;
            var ctx = SynchronizationContext.Current;
            void Progress(int pc)
            {
                if (ctx != null) ctx.Post(_ => PowerBuildProgress = pc, null);
                else PowerBuildProgress = pc;
            }

            var result = await Task.Run(() => _host.BuildNodePower(stat.StatKey, null, Progress, cts.Token));

            if (cts.IsCancellationRequested || !HeatmapEnabled)
            {
                // Heat map was turned off or cancelled while this build was in flight — keep it cleared.
                PowerOverlay = null;
                PowerOverlayChanged?.Invoke(this, EventArgs.Empty);
                return;
            }

            PowerOverlay = result;
            PowerReport.Clear();
            bool lower = stat.LowerIsBetter;
            var ordered = lower
                ? result.Entries.OrderBy(e => e.Power)
                : result.Entries.OrderByDescending(e => e.Power);
            foreach (var e in ordered)
            {
                bool good = lower ? e.Power < 0 : e.Power > 0;
                PowerReport.Add(new NodePowerRowViewModel
                {
                    NodeId      = e.Id,
                    Name        = GameTranslationService.TPassiveName(e.Name),
                    Type        = e.Type,
                    PowerStr    = e.PowerStr,
                    PerPointStr = e.PerPointStr ?? "",
                    IsAllocated = e.Alloc,
                    PowerColor  = good ? "#A6E3A1" : "#F38BA8",   // green / red
                });
            }
            OnPropertyChanged(nameof(HasPowerReport));
            IsPowerStale = false;
            PowerOverlayChanged?.Invoke(this, EventArgs.Empty);
        }
        catch { /* leave previous overlay/report intact on failure */ }
        finally
        {
            IsPowerBuilding = false;
            IsToggleBusy = false;
            if (ReferenceEquals(_powerCts, cts)) _powerCts = null;
            cts.Dispose();
            _luaQueue.Release();
        }
    }

    private void FocusReportRow(NodePowerRowViewModel? row)
    {
        if (row != null) FocusCanvasNode?.Invoke(row.NodeId, null);
    }

    /// <summary>Marks the heat map out of date after a build edit (no auto-rebuild).</summary>
    private void MarkPowerStale()
    {
        if (HeatmapEnabled && !IsPowerBuilding) IsPowerStale = true;
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
    public string PowerStr    { get; init; } = "";
    public string PerPointStr { get; init; } = "";
    public bool   IsAllocated { get; init; }
    public string PowerColor  { get; init; } = "#CDD6F4";
}
