using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PBLApp.Core.Localization;
using PBLEngine;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

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

    // ── Tree nodes ─────────────────────────────────────────────────────────

    [ObservableProperty] private IReadOnlyList<TreeNodeDto> _nodes = [];
    [ObservableProperty] private IReadOnlySet<int> _allocatedIds = new HashSet<int>();

    [ObservableProperty]
    private IReadOnlyList<(int NodeId, double RadiusWorld)> _radiusEmitters
        = Array.Empty<(int, double)>();

    public int NodeCount      => Nodes.Count;
    public int AllocatedCount => AllocatedIds.Count;
    public string AllocatedLabel =>
        string.Format(LocalizationService.Get("Tree_AllocatedFmt"), AllocatedCount);
    public string NodesLabel =>
        string.Format(LocalizationService.Get("Tree_NodesFmt"), NodeCount);

    // ── Ascendancy backgrounds ─────────────────────────────────────────────

    public IReadOnlyDictionary<string, (double X, double Y)> AscendancyBackgrounds { get; }

    // ── Class & ascendancy selection ───────────────────────────────────────

    public IReadOnlyList<ClassDisplayVm> Classes { get; }

    [ObservableProperty] private ClassDisplayVm?  _selectedClass;
    [ObservableProperty] private AscendDisplayVm? _selectedAscend;

    public ObservableCollection<AscendDisplayVm> AvailableAscendancies { get; } = [];

    public string AscendancyFilter =>
        SelectedAscend is { Id: > 0 } asc ? asc.Name : "";

    // ── Search ─────────────────────────────────────────────────────────────

    [ObservableProperty] private string _searchText = "";

    // ── Commands ───────────────────────────────────────────────────────────

    /// <summary>Left-click: allocate node (or change attribute if already allocated attribute node).</summary>
    public IAsyncRelayCommand<int?> AllocNodeCommand { get; }

    /// <summary>Right-click: deallocate node.</summary>
    public IAsyncRelayCommand<int?> DeallocNodeCommand { get; }

    public string RepoRoot { get; }

    // ── Class-change confirmation ──────────────────────────────────────────

    /// <summary>Set by the View layer to show a confirmation dialog. Returns true = proceed.</summary>
    public Func<Task<bool>>? ConfirmClassChange { get; set; }

    /// <summary>
    /// Set by the View layer to show the attribute-selection dialog.
    /// Returns 1=Strength, 2=Dexterity, 3=Intelligence, 0=cancelled.
    /// </summary>
    public Func<Task<int>>? SelectAttribute { get; set; }

    private ClassDisplayVm? _lastAppliedClass;
    private bool _suppressClassChangeCheck;

    // ── Constructor ────────────────────────────────────────────────────────

    public TreeTabViewModel(LuaHost host, Action? onStatsChanged = null)
    {
        _host           = host;
        _onStatsChanged = onStatsChanged;
        RepoRoot        = host.RepoRoot;

        var (nodes, allocated) = host.GetTreeData();
        _nodes          = nodes;
        _allocatedIds   = allocated;
        _radiusEmitters = _host.GetRadiusEmitters();

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

        LocalizationService.Instance.LanguageChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(AllocatedLabel));
            OnPropertyChanged(nameof(NodesLabel));
        };
    }

    // ── Property change handlers ───────────────────────────────────────────

    partial void OnSelectedClassChanged(ClassDisplayVm? value)
    {
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
                result = _host.AllocNode(nodeId);
            }

            if (result != 0)
            {
                if (fullRefresh) RefreshNodes(); else RefreshAllocated();
                _onStatsChanged?.Invoke();
            }
        }
    }

    private Task DeallocNodeAsync(int nodeId, CancellationToken ct = default)
    {
        int result = _host.DeallocNode(nodeId);
        if (result != 0)
        {
            RefreshAllocated();
            _onStatsChanged?.Invoke();
        }
        return Task.CompletedTask;
    }

    private void RefreshNodes()
    {
        var (nodes, allocated) = _host.GetTreeData();
        Nodes          = nodes;
        AllocatedIds   = allocated;
        RadiusEmitters = _host.GetRadiusEmitters();
        OnPropertyChanged(nameof(NodeCount));
        OnPropertyChanged(nameof(NodesLabel));
        OnPropertyChanged(nameof(AllocatedCount));
        OnPropertyChanged(nameof(AllocatedLabel));
    }

    private void RefreshAllocated()
    {
        AllocatedIds   = _host.GetAllocatedNodeIds();
        RadiusEmitters = _host.GetRadiusEmitters();
        OnPropertyChanged(nameof(AllocatedCount));
        OnPropertyChanged(nameof(AllocatedLabel));
    }
}
