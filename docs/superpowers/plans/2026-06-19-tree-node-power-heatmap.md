# Tree Node-Power Heat Map + Power Report — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Port PoB's node-power heat map + Power Report to the Avalonia client — colour every unallocated passive node by its contribution to a chosen stat and list the strongest nodes in a sortable side panel.

**Architecture:** Reuse PoB's `CalcsTab:BuildPower()` coroutine, driven from C# (`LuaHost.BuildNodePower`). Engine returns per-node power + maxima as a flat delimited string (NLua long-list-safe). `TreeTabViewModel` owns toggle/stat/stale state and the report rows; `TreeCanvas` tints unallocated nodes via a pure `NodePowerColorizer`. Off by default; manual Refresh after build edits (no auto-recompute).

**Tech Stack:** C# / .NET 9, NLua (Lua 5.4), Avalonia 12, CommunityToolkit.Mvvm, xUnit. Calculation stays in Lua.

## Global Constraints

- Calculation logic stays in Lua; C# only orchestrates and renders.
- NLua `LuaTable.Keys` silently drops entries on long sequences — return bulk per-node data as a flat `\x1F`-row / `\t`-field delimited string and split in C# (same pattern as `GetItemTooltipLines` / `GetNodeHoverInfo`).
- Before any `GetMiscCalculator` / power calc, flush `build.spec._fastAllocDirty` via `BuildAllDependsAndPaths` (consistency guard, identical to `GetNodeHoverInfo`).
- NLua is single-threaded: every `LuaHost` call runs through the existing background-thread/semaphore discipline in `TreeTabViewModel` (`_luaQueue`). Never touch the Lua state concurrently.
- UI/render changes finish with `/pbl-verify` (screenshot) per repo CLAUDE.md auto-rule. Engine changes finish with `dotnet test PBLEngine.Tests`.
- Tests live in `PBLEngine.Tests` only (xUnit, `[Collection("LuaHost")]` + `IClassFixture<LuaHostFixture>`). PBLApp / PBLApp.Core have no test project — verify those by build + screenshot.
- Default colour theme is RED/BLUE as a constant (seam for a future setting). No theme picker in this plan.
- No `nodePowerMaxDepth` UI control (engine accepts it; default unlimited / `null`).

---

### Task 1: Engine DTOs + `GetPowerStatList`

**Files:**
- Create: `PBLEngine/NodePower.cs`
- Modify: `PBLEngine/LuaHost.cs` (add `GetPowerStatList`; insert near `GetTreeData` ~line 2764)
- Test: `PBLEngine.Tests/NodePowerTests.cs`

**Interfaces:**
- Produces:
  - `record PowerStatOption(string? StatKey, string Label, bool CombinedOffDef, bool IgnoreForNodes, bool LowerIsBetter)`
  - `record NodePowerEntry(int Id, string Name, string Type, bool Alloc, int PathDist, double Power, double PathPower, double Offence, double Defence, string PowerStr, string PerPointStr)`
  - `record NodePowerMax(double SingleStat, double Offence, double Defence)`
  - `record NodePowerResult(bool OffDefMode, NodePowerMax Max, IReadOnlyList<NodePowerEntry> Entries)`
  - `LuaHost.GetPowerStatList() : IReadOnlyList<PowerStatOption>`

- [ ] **Step 1: Write the failing test**

Create `PBLEngine.Tests/NodePowerTests.cs`:

```csharp
using PBLEngine;
using System.Linq;
using Xunit;

namespace PBLEngine.Tests;

[Collection("LuaHost")]
public class NodePowerTests : IClassFixture<LuaHostFixture>
{
    private readonly LuaHost _host;

    public NodePowerTests(LuaHostFixture fixture)
    {
        _host = fixture.Host;
        _host.NewBuild();
    }

    [Fact]
    public void GetPowerStatList_ReturnsOptions_IncludingOffDefDefault()
    {
        var list = _host.GetPowerStatList();

        Assert.NotEmpty(list);
        Assert.Contains(list, o => o.CombinedOffDef);            // Offence/Defence default
        Assert.Contains(list, o => o.StatKey == "FullDPS");      // a known single stat
    }

    [Fact]
    public void GetPowerStatList_ExcludesItemOnlyEntries()
    {
        var list = _host.GetPowerStatList();

        // The "Name" entry (ignoreForNodes/itemField) must not appear in the node heat-map list.
        Assert.DoesNotContain(list, o => o.IgnoreForNodes);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test PBLEngine.Tests --filter "FullyQualifiedName~NodePowerTests"`
Expected: FAIL — `LuaHost` has no `GetPowerStatList`.

- [ ] **Step 3: Create the DTO file**

Create `PBLEngine/NodePower.cs`:

```csharp
namespace PBLEngine;

/// <summary>One selectable entry in the tree heat-map stat dropdown, sourced from
/// Lua <c>data.powerStatList</c>. <see cref="CombinedOffDef"/> marks the default
/// "Offence/Defence" mode (no single stat).</summary>
public record PowerStatOption(
    string? StatKey,        // e.g. "FullDPS"; null for the Offence/Defence entry
    string  Label,          // English label from powerStatList (UI translates separately)
    bool    CombinedOffDef,
    bool    IgnoreForNodes, // item-only entries (filtered out of the node heat map)
    bool    LowerIsBetter
);

/// <summary>Per-node power result. <see cref="Power"/> is the raw single-stat delta
/// (or offence in Off/Def mode) used for colouring and sorting; <see cref="PowerStr"/>
/// / <see cref="PerPointStr"/> are PoB-formatted display strings for the report.</summary>
public record NodePowerEntry(
    int     Id,
    string  Name,
    string  Type,           // Normal / Notable / Keystone
    bool    Alloc,
    int     PathDist,
    double  Power,
    double  PathPower,
    double  Offence,
    double  Defence,
    string  PowerStr,
    string  PerPointStr
);

/// <summary>Per-channel maxima used to normalise colour brightness.</summary>
public record NodePowerMax(double SingleStat, double Offence, double Defence);

/// <summary>Full heat-map result for the current stat selection.</summary>
public record NodePowerResult(
    bool OffDefMode,
    NodePowerMax Max,
    IReadOnlyList<NodePowerEntry> Entries
);
```

- [ ] **Step 4: Implement `GetPowerStatList` in LuaHost**

Add to `PBLEngine/LuaHost.cs` (just above `public (List<TreeNodeDto> Nodes, ...) GetTreeData()`):

```csharp
/// <summary>Selectable stats for the tree heat map, from Lua <c>data.powerStatList</c>.
/// Item-only entries (<c>ignoreForNodes</c>/<c>itemField</c>) are filtered out — the
/// node heat map can't colour by an item field.</summary>
public IReadOnlyList<PowerStatOption> GetPowerStatList()
{
    var list = new List<PowerStatOption>();
    var result = State.DoString(@"
        local out = {}
        for _, s in ipairs(data.powerStatList or {}) do
            if not s.ignoreForNodes then
                out[#out+1] = {
                    s.stat or '',
                    s.label or s.stat or '',
                    s.combinedOffDef and 1 or 0,
                    s.ignoreForNodes and 1 or 0,
                    s.lowerIsBetter and 1 or 0
                }
            end
        end
        return out
    ");
    if (result is not { Length: > 0 } || result[0] is not LuaTable t) return list;
    foreach (var k in t.Keys)
    {
        if (t[k] is not LuaTable row) continue;
        var statKey = row[1L] as string ?? "";
        list.Add(new PowerStatOption(
            string.IsNullOrEmpty(statKey) ? null : statKey,
            row[2L] as string ?? "",
            row[3L] is long c && c == 1L,
            row[4L] is long ig && ig == 1L,
            row[5L] is long lb && lb == 1L));
    }
    return list;
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test PBLEngine.Tests --filter "FullyQualifiedName~NodePowerTests"`
Expected: PASS (2 tests).

- [ ] **Step 6: Commit**

```bash
git add PBLEngine/NodePower.cs PBLEngine/LuaHost.cs PBLEngine.Tests/NodePowerTests.cs
git commit -m "feat(engine): power-stat list + node-power DTOs"
```

---

### Task 2: Engine `BuildNodePower` (drive PoB's coroutine)

**Files:**
- Modify: `PBLEngine/LuaHost.cs` (add `BuildNodePower` after `GetPowerStatList`)
- Test: `PBLEngine.Tests/NodePowerTests.cs`

**Interfaces:**
- Consumes: `PowerStatOption`, `NodePowerEntry`, `NodePowerMax`, `NodePowerResult` (Task 1).
- Produces: `LuaHost.BuildNodePower(string? statKey, int? maxDepth = null, Action<int>? onProgress = null) : NodePowerResult`

- [ ] **Step 1: Write the failing tests**

Append to `PBLEngine.Tests/NodePowerTests.cs`:

```csharp
    [Fact]
    public void BuildNodePower_SingleStat_TopNodeHasPositivePower()
    {
        var res = _host.BuildNodePower("FullDPS");

        Assert.False(res.OffDefMode);
        Assert.NotEmpty(res.Entries);
        // Strongest node for a DPS stat must move the stat upward.
        var top = res.Entries.OrderByDescending(e => e.Power).First();
        Assert.True(top.Power > 0, $"top node power was {top.Power}");
        Assert.True(res.Max.SingleStat > 0);
    }

    [Fact]
    public void BuildNodePower_OffDefMode_ReturnsBothChannels()
    {
        var res = _host.BuildNodePower(null);   // null = Offence/Defence default

        Assert.True(res.OffDefMode);
        Assert.NotEmpty(res.Entries);
        Assert.True(res.Max.Offence  > 0, "no offence maximum");
        Assert.True(res.Max.Defence  > 0, "no defence maximum");
    }

    [Fact]
    public void BuildNodePower_ReportsProgressToCompletion()
    {
        int last = -1;
        _host.BuildNodePower("Life", null, pc => last = pc);

        Assert.True(last >= 0);   // callback fired at least once
    }

    [Fact]
    public void BuildNodePower_OnlyReturnsAllocatableTreeNodeTypes()
    {
        var res = _host.BuildNodePower("Life");

        Assert.All(res.Entries, e =>
            Assert.Contains(e.Type, new[] { "Normal", "Notable", "Keystone" }));
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test PBLEngine.Tests --filter "FullyQualifiedName~NodePowerTests"`
Expected: FAIL — no `BuildNodePower`.

- [ ] **Step 3: Implement `BuildNodePower`**

Add to `PBLEngine/LuaHost.cs` after `GetPowerStatList`:

```csharp
/// <summary>Runs PoB's <c>CalcsTab:BuildPower()</c> for the given stat (null =
/// the combined Offence/Defence default) and returns per-node power + maxima.
/// Drives the Lua coroutine to completion, surfacing progress via
/// <paramref name="onProgress"/>. Heavy — call on a background thread.</summary>
public NodePowerResult BuildNodePower(string? statKey, int? maxDepth = null, Action<int>? onProgress = null)
{
    State["_powerStatKey"]  = statKey;                                  // nil → off/def
    State["_powerMaxDepth"] = maxDepth.HasValue ? (long?)maxDepth.Value : null;

    // 1. Flush fast-alloc dirty state, pick the stat entry, arm the builder.
    State.DoString(@"
        if build and build.spec and build.spec._fastAllocDirty then
            build.spec:BuildAllDependsAndPaths()
            build.spec._fastAllocDirty = false
        end
        local ct = build.calcsTab
        local sel
        if _powerStatKey == nil or _powerStatKey == '' then
            for _, s in ipairs(data.powerStatList) do if s.combinedOffDef then sel = s break end end
        else
            for _, s in ipairs(data.powerStatList) do if s.stat == _powerStatKey then sel = s break end end
        end
        ct.powerStat = sel
        ct.nodePowerMaxDepth = _powerMaxDepth
        _powerPct = 0
        build.powerBuilderProgressCallback = function(pc) _powerPct = pc end
        ct.powerBuildFlag = true
    ");

    // 2. Drive the coroutine: each BuildPower() resumes it once; loop until dead.
    int guard = 0, lastPct = -1;
    while (true)
    {
        var r = State.DoString(@"
            local ct = build.calcsTab
            ct:BuildPower()
            return (ct.powerBuilder == nil), (_powerPct or 0)
        ");
        int pct = r is { Length: > 1 } && r[1] is long p ? (int)p
                : r is { Length: > 1 } && r[1] is double pd ? (int)pd : 0;
        if (pct != lastPct) { lastPct = pct; onProgress?.Invoke(pct); }
        bool done = r is { Length: > 0 } && r[0] is bool b && b;
        if (done) { onProgress?.Invoke(100); break; }
        if (++guard > 100_000) break;   // safety: never hang the caller on a Lua bug
    }

    // 3. Dump per-node power + maxima as a flat string (NLua long-list-safe).
    //    Mirrors TreeTab:BuildPowerReportList for the formatted strings.
    var dump = State.DoString(@"
        local ct = build.calcsTab
        local sel = ct.powerStat
        local offDef = (sel == nil) or (sel.combinedOffDef == true)
        local pm = ct.powerMax or { singleStat=0, offence=0, defence=0 }

        -- formatting from the matching displayStat (fallback .1f)
        local displayStat = { fmt = '.1f' }
        if sel and sel.stat then
            for _, ds in ipairs(build.displayStats) do
                if ds.stat == sel.stat then displayStat = ds break end
            end
        end
        local scale = (displayStat.pc or displayStat.mod) and 100 or 1

        local function fmtNum(v)
            local s = string.format('%' .. (displayStat.fmt or '.1f'), v)
            if formatNumSep then s = formatNumSep(s) end
            return s
        end

        local rows = {}
        local function emit(node, isAlloc, pathDist)
            local power     = (node.power and node.power.singleStat or 0)
            local pathPower = (node.power and node.power.pathPower or 0)
            local offence   = (node.power and node.power.offence or 0)
            local defence   = (node.power and node.power.defence or 0)
            local powerStr   = fmtNum(power * scale)
            local perPoint   = (pathDist and pathDist > 0) and (pathPower / pathDist) or pathPower
            local perPointStr = fmtNum(perPoint * scale)
            rows[#rows+1] = table.concat({
                node.id or 0,
                (node.dn or node.name or ''):gsub('[\t\31]', ' '),
                node.type or 'Normal',
                isAlloc and 1 or 0,
                pathDist or 1,
                power, pathPower, offence, defence,
                powerStr, perPointStr
            }, '\t')
        end

        for nodeId, node in pairs(build.spec.nodes) do
            local isAlloc = node.alloc or (ct.mainEnv and ct.mainEnv.grantedPassives[nodeId])
            if (node.type == 'Normal' or node.type == 'Notable' or node.type == 'Keystone')
               and not node.ascendancyName then
                local pathDist
                if isAlloc then
                    pathDist = (#(node.depends or {}) == 0) and 1 or #node.depends
                else
                    pathDist = (#(node.path or {}) == 0) and 1 or #node.path
                end
                emit(node, isAlloc, pathDist)
            end
        end
        -- cluster notables (unallocated) — pathDist column = 1
        for _, node in pairs(build.spec.tree.clusterNodeMap or {}) do
            if not node.alloc and (node.type == 'Notable' or node.type == 'Normal' or node.type == 'Keystone') then
                emit(node, false, 1)
            end
        end

        return (offDef and 1 or 0), pm.singleStat or 0, pm.offence or 0, pm.defence or 0,
               table.concat(rows, '\31')
    ");

    bool offDefMode = dump is { Length: > 0 } && dump[0] is long od && od == 1L;
    double ToD(object? o) => o is double d ? d : o is long l ? l : 0;
    var max = new NodePowerMax(ToD(dump?[1]), ToD(dump?[2]), ToD(dump?[3]));
    var blob = dump is { Length: > 4 } ? dump[4] as string ?? "" : "";

    var entries = new List<NodePowerEntry>();
    foreach (var rowStr in blob.Split('\x1F', StringSplitOptions.RemoveEmptyEntries))
    {
        var f = rowStr.Split('\t');
        if (f.Length < 11) continue;
        entries.Add(new NodePowerEntry(
            int.TryParse(f[0], out var id) ? id : 0,
            f[1],
            f[2],
            f[3] == "1",
            int.TryParse(f[4], out var pd) ? pd : 1,
            ParseD(f[5]), ParseD(f[6]), ParseD(f[7]), ParseD(f[8]),
            f[9], f[10]));
    }

    State["_powerStatKey"]  = null;
    State["_powerMaxDepth"] = null;
    return new NodePowerResult(offDefMode, max, entries);

    static double ParseD(string s) =>
        double.TryParse(s, System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0;
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test PBLEngine.Tests --filter "FullyQualifiedName~NodePowerTests"`
Expected: PASS (6 tests total).

- [ ] **Step 5: Parity sanity — power build must not perturb main stats**

Run: `pwsh ./scripts/parity.ps1 -Build "tools/parity/community_builds/9T3EGRVR_Jo4Ab10WYcC8.xml"`
Expected: exit 0 (parity). `BuildNodePower` only writes `node.power`/`powerMax`; `mainOutput` is unchanged.

- [ ] **Step 6: Commit**

```bash
git add PBLEngine/LuaHost.cs PBLEngine.Tests/NodePowerTests.cs
git commit -m "feat(engine): BuildNodePower drives PoB power coroutine"
```

---

### Task 3: Localisation keys

**Files:**
- Modify: `PBLApp.Core/Localization/Strings.resx`
- Modify: `PBLApp.Core/Localization/Strings.ru.resx`

**Interfaces:**
- Produces resource keys consumed by Tasks 4 & 6 via `LocalizationService.Get(key)` / `{loc:Tr key}`:
  `Tree_Heatmap`, `Tree_PowerReport`, `Tree_Power_Node`, `Tree_Power_Type`,
  `Tree_Power_Delta`, `Tree_Power_PerPoint`, `Tree_Power_Stale`,
  `Tree_Power_Refresh`, `Tree_Power_Building`, `Tree_Power_Empty`.

- [ ] **Step 1: Add English keys**

In `PBLApp.Core/Localization/Strings.resx`, add (anywhere among the `<data>` nodes; match existing indentation):

```xml
  <data name="Tree_Heatmap" xml:space="preserve"><value>Heat map</value></data>
  <data name="Tree_PowerReport" xml:space="preserve"><value>Power Report</value></data>
  <data name="Tree_Power_OffDef" xml:space="preserve"><value>Offence/Defence</value></data>
  <data name="Tree_Power_Node" xml:space="preserve"><value>Node</value></data>
  <data name="Tree_Power_Type" xml:space="preserve"><value>Type</value></data>
  <data name="Tree_Power_Delta" xml:space="preserve"><value>Δ</value></data>
  <data name="Tree_Power_PerPoint" xml:space="preserve"><value>Δ / point</value></data>
  <data name="Tree_Power_Stale" xml:space="preserve"><value>Out of date — build changed</value></data>
  <data name="Tree_Power_Refresh" xml:space="preserve"><value>Refresh</value></data>
  <data name="Tree_Power_Building" xml:space="preserve"><value>Calculating…</value></data>
  <data name="Tree_Power_Empty" xml:space="preserve"><value>No measurable change for this stat</value></data>
```

- [ ] **Step 2: Add Russian keys**

In `PBLApp.Core/Localization/Strings.ru.resx`, add:

```xml
  <data name="Tree_Heatmap" xml:space="preserve"><value>Тепловая карта</value></data>
  <data name="Tree_PowerReport" xml:space="preserve"><value>Отчёт о силе</value></data>
  <data name="Tree_Power_OffDef" xml:space="preserve"><value>Атака/Защита</value></data>
  <data name="Tree_Power_Node" xml:space="preserve"><value>Нода</value></data>
  <data name="Tree_Power_Type" xml:space="preserve"><value>Тип</value></data>
  <data name="Tree_Power_Delta" xml:space="preserve"><value>Δ</value></data>
  <data name="Tree_Power_PerPoint" xml:space="preserve"><value>Δ / очко</value></data>
  <data name="Tree_Power_Stale" xml:space="preserve"><value>Устарело — билд изменён</value></data>
  <data name="Tree_Power_Refresh" xml:space="preserve"><value>Обновить</value></data>
  <data name="Tree_Power_Building" xml:space="preserve"><value>Расчёт…</value></data>
  <data name="Tree_Power_Empty" xml:space="preserve"><value>Нет измеримого влияния на этот стат</value></data>
```

- [ ] **Step 3: Build to verify resx compiles**

Run: `dotnet build PBLApp.Core`
Expected: build succeeds (resx designer regenerates without error).

- [ ] **Step 4: Commit**

```bash
git add PBLApp.Core/Localization/Strings.resx PBLApp.Core/Localization/Strings.ru.resx
git commit -m "i18n: heat-map / power-report strings"
```

---

### Task 4: ViewModel — heat-map state, build, report rows, stale

**Files:**
- Modify: `PBLApp.Core/TreeTabViewModel.cs`
- (No unit test — PBLApp.Core has no test project; verified by build here, screenshot in Task 8.)

**Interfaces:**
- Consumes: `LuaHost.GetPowerStatList`, `LuaHost.BuildNodePower`, `NodePowerResult`, `NodePowerEntry` (Tasks 1-2); localisation keys (Task 3).
- Produces (consumed by Tasks 5-7):
  - `PowerStatVm` (display wrapper, `Option`, `DisplayName`)
  - `NodePowerRowViewModel(int NodeId, string Name, string Type, string PowerStr, string PerPointStr, bool IsAllocated, string PowerColor)`
  - VM members: `bool HeatmapEnabled`, `IReadOnlyList<PowerStatVm> PowerStatOptions`, `PowerStatVm? SelectedPowerStat`, `bool IsPowerBuilding`, `int PowerBuildProgress`, `bool IsPowerStale`, `ObservableCollection<NodePowerRowViewModel> PowerReport`, `NodePowerResult? PowerOverlay`, `event EventHandler? PowerOverlayChanged`, `IRelayCommand RefreshPowerCommand`, `IRelayCommand<NodePowerRowViewModel?> FocusReportRowCommand`.

- [ ] **Step 1: Add the row + stat display types**

At the bottom of `PBLApp.Core/TreeTabViewModel.cs` (after `JewelPickerOptionVm`), add:

```csharp
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
```

(`Tree_Power_OffDef` is defined in Task 3.)

- [ ] **Step 2: Add observable state + collections to `TreeTabViewModel`**

In the `// ── Search ──` region area of `TreeTabViewModel` (after `_searchText`), add:

```csharp
    // ── Heat map / Power Report ────────────────────────────────────────────
    [ObservableProperty] private bool _heatmapEnabled;
    [ObservableProperty] private PowerStatVm? _selectedPowerStat;
    [ObservableProperty] private bool _isPowerBuilding;
    [ObservableProperty] private int  _powerBuildProgress;
    [ObservableProperty] private bool _isPowerStale;

    public IReadOnlyList<PowerStatVm> PowerStatOptions { get; }
    public ObservableCollection<NodePowerRowViewModel> PowerReport { get; } = [];

    /// <summary>Latest heat-map result handed to the canvas; null = no overlay.</summary>
    public NodePowerResult? PowerOverlay { get; private set; }

    /// <summary>Raised when <see cref="PowerOverlay"/> changes so the View can push
    /// it onto the TreeCanvas (the canvas is a View-layer control).</summary>
    public event EventHandler? PowerOverlayChanged;

    public string PowerStaleLabel  => LocalizationService.Get("Tree_Power_Stale");
    public string PowerEmptyLabel   => LocalizationService.Get("Tree_Power_Empty");
    public bool   HasPowerReport    => PowerReport.Count > 0;
```

- [ ] **Step 3: Initialise options + commands in the constructor**

In the `TreeTabViewModel` constructor, after `RefreshPointUsage();`, add:

```csharp
        PowerStatOptions = host.GetPowerStatList().Select(o => new PowerStatVm(o)).ToList();
        _selectedPowerStat = PowerStatOptions.FirstOrDefault(o => o.Option.CombinedOffDef)
                          ?? PowerStatOptions.FirstOrDefault();
        RefreshPowerCommand   = new RelayCommand(() => _ = BuildPowerAsync());
        FocusReportRowCommand = new RelayCommand<NodePowerRowViewModel?>(FocusReportRow);
```

And declare the command properties near the other `Command` declarations:

```csharp
    public IRelayCommand RefreshPowerCommand { get; }
    public IRelayCommand<NodePowerRowViewModel?> FocusReportRowCommand { get; }
```

- [ ] **Step 4: Add the build, toggle handlers, and focus**

Add these methods to `TreeTabViewModel` (near `GetNodeHoverInfo`):

```csharp
    partial void OnHeatmapEnabledChanged(bool value)
    {
        if (value) _ = BuildPowerAsync();
        else
        {
            PowerReport.Clear();
            OnPropertyChanged(nameof(HasPowerReport));
            IsPowerStale = false;
            PowerOverlay = null;
            PowerOverlayChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    partial void OnSelectedPowerStatChanged(PowerStatVm? value)
    {
        if (HeatmapEnabled && value != null) _ = BuildPowerAsync();
    }

    /// <summary>Runs the heat-map calc on a background thread (serialised through
    /// the shared Lua queue), then fills the report + canvas overlay.</summary>
    private async Task BuildPowerAsync()
    {
        var stat = SelectedPowerStat?.Option;
        if (stat == null) return;

        await _luaQueue.WaitAsync();
        try
        {
            IsPowerBuilding = true;
            PowerBuildProgress = 0;
            var ctx = SynchronizationContext.Current;
            void Progress(int pc)
            {
                if (ctx != null) ctx.Post(_ => PowerBuildProgress = pc, null);
                else PowerBuildProgress = pc;
            }

            var result = await Task.Run(() => _host.BuildNodePower(stat.StatKey, null, Progress));

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
                    PerPointStr = e.PerPointStr,
                    IsAllocated = e.Alloc,
                    PowerColor  = good ? "#A6E3A1" : "#F38BA8",   // green / red
                });
            }
            OnPropertyChanged(nameof(HasPowerReport));
            IsPowerStale = false;
            PowerOverlayChanged?.Invoke(this, EventArgs.Empty);
        }
        catch { /* leave previous overlay/report intact on failure */ }
        finally { IsPowerBuilding = false; _luaQueue.Release(); }
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
```

If `GameTranslationService.TPassiveName` does not exist, fall back to `e.Name` (the Lua side already used `node.dn`). Verify the symbol; the tree node name translator is used by `TreeCanvas` — match that call.

- [ ] **Step 5: Trigger stale on build edits**

In `RunBackgroundToggle`, inside the `if (last)` block (after `_onStatsChanged?.Invoke();`), add `MarkPowerStale();`. Also add `MarkPowerStale();` at the end of `OnSelectedAscendChanged` (after the existing `OnPropertyChanged` calls) and inside `PickJewel` (after `_onStatsChanged?.Invoke();`). These are the in-tab edit paths.

- [ ] **Step 6: Build to verify it compiles**

Run: `dotnet build PBLApp.Core`
Expected: build succeeds. (Resolve `TPassiveName`/`TtCalcLabel` symbol names against `GameTranslationService` if the build flags them.)

- [ ] **Step 7: Commit**

```bash
git add PBLApp.Core/TreeTabViewModel.cs PBLApp.Core/Localization/Strings.resx PBLApp.Core/Localization/Strings.ru.resx
git commit -m "feat(tree-vm): heat-map state, power build, report rows"
```

---

### Task 5: Canvas — colorizer + overlay tint

**Files:**
- Create: `PBLApp/Controls/NodePowerColorizer.cs`
- Modify: `PBLApp/Controls/TreeCanvas.cs` (add `NodePowerOverlay` StyledProperty; tint unallocated nodes in `DrawNodeAt`)
- (Verified by build + Task 8 screenshot.)

**Interfaces:**
- Consumes: `NodePowerResult`, `NodePowerEntry`, `NodePowerMax` (Task 1).
- Produces: `NodePowerColorizer.ColorFor(NodePowerEntry e, NodePowerMax max, bool offDef) : Color?` and `TreeCanvas.NodePowerOverlayProperty`.

- [ ] **Step 1: Create the colorizer**

Create `PBLApp/Controls/NodePowerColorizer.cs`:

```csharp
using System;
using Avalonia.Media;
using PBLEngine;

namespace PBLApp.Controls;

/// <summary>Pure colour math for the tree heat map, ported from PoB's
/// <c>PassiveTreeView</c>: brightness = (max(power,0)/powerMax * 1.5) ^ 0.5,
/// mapped to the RED/BLUE theme (blue = strong contribution, red = weak/negative).
/// Off/Def mode blends an offence (blue) and defence (green) channel.</summary>
public static class NodePowerColorizer
{
    private static double Curve(double power, double max) =>
        max <= 0 ? 0 : Math.Min(1.0, Math.Pow(Math.Max(power, 0) / max * 1.5, 0.5));

    /// <summary>Tint for a node, or null when it contributes nothing (leave default).</summary>
    public static Color? ColorFor(NodePowerEntry e, NodePowerMax max, bool offDef)
    {
        if (offDef)
        {
            double off = Curve(e.Offence, max.Offence);
            double def = Curve(e.Defence, max.Defence);
            if (off <= 0 && def <= 0) return null;
            // offence → blue, defence → green (RED/BLUE-style, with green for defence)
            byte b = (byte)(60 + off * 195);
            byte g = (byte)(40 + def * 180);
            return Color.FromArgb(220, 30, g, b);
        }

        double t = Curve(e.Power, max.SingleStat);
        if (t <= 0) return null;
        // RED/BLUE: low = dim red, high = bright blue.
        byte rr = (byte)(180 * (1 - t) + 30);
        byte bb = (byte)(80 + t * 175);
        return Color.FromArgb(220, rr, 40, bb);
    }
}
```

- [ ] **Step 2: Add the StyledProperty + lookup on TreeCanvas**

In `PBLApp/Controls/TreeCanvas.cs`, alongside the other `StyledProperty` declarations (~line 64), add:

```csharp
    public static readonly StyledProperty<NodePowerResult?> NodePowerOverlayProperty =
        AvaloniaProperty.Register<TreeCanvas, NodePowerResult?>(nameof(NodePowerOverlay));

    public NodePowerResult? NodePowerOverlay
    {
        get => GetValue(NodePowerOverlayProperty);
        set => SetValue(NodePowerOverlayProperty, value);
    }
```

Ensure `using PBLEngine;` is present (it already is — `TreeNodeDto` lives there). Register the property for render invalidation: find the static constructor / `AffectsRender<TreeCanvas>(...)` call and add `NodePowerOverlayProperty` to its argument list.

Add a cached lookup field near `_canAllocCache` (~line 245):

```csharp
    private System.Collections.Generic.Dictionary<int, Color>? _powerColorCache;
    private object? _powerOverlayRef;
```

And a helper:

```csharp
    private Color? PowerColorFor(int nodeId)
    {
        var overlay = NodePowerOverlay;
        if (overlay is null) { _powerColorCache = null; _powerOverlayRef = null; return null; }
        if (!ReferenceEquals(overlay, _powerOverlayRef) || _powerColorCache is null)
        {
            _powerColorCache = new();
            foreach (var e in overlay.Entries)
            {
                var c = NodePowerColorizer.ColorFor(e, overlay.Max, overlay.OffDefMode);
                if (c is { } col) _powerColorCache[e.Id] = col;
            }
            _powerOverlayRef = overlay;
        }
        return _powerColorCache.TryGetValue(nodeId, out var v) ? v : (Color?)null;
    }
```

- [ ] **Step 3: Apply the tint to unallocated nodes**

`DrawNodeAt` is `static`; pass the tint in. In `DrawNode` (instance, ~line 699) compute the colour and forward it:

```csharp
    private void DrawNode(DrawingContext dc, TreeNodeDto node,
                          double sx, double sy, double r,
                          bool alloc, bool canAlloc, bool search, bool hover) =>
        DrawNodeAt(dc, node, sx, sy, r, alloc, canAlloc, search, hover, _scale, AssetStore,
                   alloc ? null : PowerColorFor(node.Id));
```

Change the `DrawNodeAt` signature to accept `Color? powerTint` (add as the last parameter; update the static-layer bake call site that also invokes `DrawNodeAt` to pass `null`). After the node body is drawn but before the overlays block (just before `// ── Overlays (always drawn) ──`, ~line 758), add:

```csharp
        if (powerTint is { } pt)
        {
            // Heat-map glow ring around the node, scaled to icon size.
            double glowR = (useSprites ? iconHalfPx : r) + 3;
            dc.DrawEllipse(new SolidColorBrush(pt, 0.55), null, center, glowR, glowR);
            dc.DrawEllipse(null, new Pen(new SolidColorBrush(pt), 2), center, glowR, glowR);
        }
```

(The glow-ring approach tints without fighting the node sprite/dim layer — clearer than recolouring the icon fill.)

- [ ] **Step 4: Build to verify it compiles**

Run: `dotnet build PBLApp`
Expected: build succeeds.

- [ ] **Step 5: Commit**

```bash
git add PBLApp/Controls/NodePowerColorizer.cs PBLApp/Controls/TreeCanvas.cs
git commit -m "feat(tree-canvas): node-power heat-map tinting"
```

---

### Task 6: View — toolbar toggle, stat dropdown, report panel, binding

**Files:**
- Modify: `PBLApp/Views/TreeTabView.axaml`
- Modify: `PBLApp/Views/TreeTabView.axaml.cs`
- (Verified by build + Task 8 screenshot.)

**Interfaces:**
- Consumes: `TreeTabViewModel` members + `NodePowerRowViewModel` + `PowerStatVm` (Task 4), `TreeCanvas.NodePowerOverlay` (Task 5), localisation keys (Task 3).

- [ ] **Step 1: Add toolbar controls**

In `TreeTabView.axaml`, find the tree toolbar (the panel holding class/ascendancy/search controls) and append:

```xml
<ToggleButton Content="{loc:Tr Tree_Heatmap}"
              IsChecked="{Binding HeatmapEnabled}"
              Margin="8,0,0,0"/>
<ComboBox ItemsSource="{Binding PowerStatOptions}"
          SelectedItem="{Binding SelectedPowerStat}"
          IsEnabled="{Binding HeatmapEnabled}"
          MinWidth="170" Margin="6,0,0,0"
          ToolTip.Tip="{loc:Tr Tree_PowerReport}"/>
```

- [ ] **Step 2: Add the report side panel**

Wrap the existing tree-canvas host in a `Grid` with a collapsible right column (or add a column if it is already a Grid). Add the panel as the right column, visible only when the heat map is on:

```xml
<Border Grid.Column="1" Width="320"
        IsVisible="{Binding HeatmapEnabled}"
        Background="{DynamicResource BgMantle}"
        BorderBrush="{DynamicResource BorderSubtle}" BorderThickness="1,0,0,0">
  <DockPanel Margin="8">
    <TextBlock DockPanel.Dock="Top" Text="{loc:Tr Tree_PowerReport}"
               FontWeight="SemiBold" FontSize="15" Margin="0,0,0,6"/>

    <!-- progress -->
    <StackPanel DockPanel.Dock="Top" Orientation="Horizontal"
                IsVisible="{Binding IsPowerBuilding}" Margin="0,0,0,6">
      <ProgressBar Minimum="0" Maximum="100" Value="{Binding PowerBuildProgress}"
                   Width="180" Height="6"/>
      <TextBlock Text="{loc:Tr Tree_Power_Building}" Margin="8,0,0,0"
                 Foreground="{DynamicResource TextSecondary}"/>
    </StackPanel>

    <!-- stale badge -->
    <Border DockPanel.Dock="Top" IsVisible="{Binding IsPowerStale}"
            Background="#3A2A10" CornerRadius="4" Padding="6,4" Margin="0,0,0,6">
      <StackPanel Orientation="Horizontal">
        <TextBlock Text="{Binding PowerStaleLabel}" VerticalAlignment="Center"
                   Foreground="#F9E2AF"/>
        <Button Content="{loc:Tr Tree_Power_Refresh}" Margin="8,0,0,0"
                Command="{Binding RefreshPowerCommand}"/>
      </StackPanel>
    </Border>

    <!-- empty hint -->
    <TextBlock DockPanel.Dock="Top" Text="{Binding PowerEmptyLabel}"
               IsVisible="{Binding !HasPowerReport}"
               Foreground="{DynamicResource TextMuted}" TextWrapping="Wrap"/>

    <!-- report rows -->
    <ListBox ItemsSource="{Binding PowerReport}"
             SelectionMode="Single">
      <ListBox.ItemTemplate>
        <DataTemplate>
          <Grid ColumnDefinitions="*,70,70">
            <TextBlock Grid.Column="0" Text="{Binding Name}" TextTrimming="CharacterEllipsis"/>
            <TextBlock Grid.Column="1" Text="{Binding PowerStr}" TextAlignment="Right"
                       Foreground="{Binding PowerColor}"/>
            <TextBlock Grid.Column="2" Text="{Binding PerPointStr}" TextAlignment="Right"
                       Foreground="{DynamicResource TextSecondary}"/>
          </Grid>
        </DataTemplate>
      </ListBox.ItemTemplate>
    </ListBox>
  </DockPanel>
</Border>
```

Wire row activation to focus the node — add to the `ListBox`:
`SelectionChanged` handler in code-behind, or a double-tap gesture calling `FocusReportRowCommand`. Use the code-behind handler in Step 3.

- [ ] **Step 3: Bind overlay + row focus in code-behind**

In `TreeTabView.axaml.cs`, where the `TreeCanvas` and `DataContext` are wired (the existing `OnDataContextChanged` / loaded handler), subscribe to the VM's overlay event and push it onto the canvas; also bind the `FocusCanvasNode` callback (it already exists for IPC). Add:

```csharp
    private void HookPowerOverlay(TreeTabViewModel vm, TreeCanvas canvas)
    {
        vm.PowerOverlayChanged += (_, _) => canvas.NodePowerOverlay = vm.PowerOverlay;
        canvas.NodePowerOverlay = vm.PowerOverlay;
    }
```

Call `HookPowerOverlay(vm, TreeCanvasControl)` from the same place the other canvas callbacks (`FocusCanvasNode`, `SetCanvasView`) are assigned. For report-row activation, add a `DoubleTapped` (or `SelectionChanged`) handler on the ListBox that invokes `vm.FocusReportRowCommand.Execute((sender as ListBox)?.SelectedItem)`.

- [ ] **Step 4: Build to verify it compiles**

Run: `dotnet build PBLApp`
Expected: build succeeds (XAML compiles, no missing bindings).

- [ ] **Step 5: Commit**

```bash
git add PBLApp/Views/TreeTabView.axaml PBLApp/Views/TreeTabView.axaml.cs
git commit -m "feat(tree-view): heat-map toggle + Power Report panel"
```

---

### Task 7: IPC endpoints + MCP tools

**Files:**
- Modify: `PBLApp/Ipc/IpcServer.cs` (route + handlers; extend `TreeState`)
- Modify: `PBLMcp/VisualTools.cs` (two MCP tool wrappers)

**Interfaces:**
- Consumes: `TreeTabViewModel` heat-map members (Task 4).
- Produces IPC routes `POST /tree/power-build`, `GET /tree/power-report`; MCP tools `visual_tree_power_build(stat?)`, `visual_tree_power_report()`.

- [ ] **Step 1: Add routes**

In `IpcServer.cs`, in the route switch (after `"/tree/pick-jewel"`, ~line 176), add:

```csharp
                "/tree/power-build"       => await OnUiAsync(() => TreePowerBuild(body)),
                "/tree/power-report"      => await OnUi(TreePowerReport),
```

- [ ] **Step 2: Add handlers**

After `TreePickJewel`, add:

```csharp
    private static async Task<object> TreePowerBuild(string body)
    {
        if (GetTreeVm() is not { } t) return new { error = "TreeTab not ready." };
        var req = string.IsNullOrWhiteSpace(body)
            ? new Dictionary<string, JsonElement>()
            : JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(body) ?? new();

        string? stat = req.TryGetValue("stat", out var s) && s.ValueKind == JsonValueKind.String
            ? s.GetString() : null;

        t.HeatmapEnabled = true;
        if (stat != null)
            t.SelectedPowerStat = t.PowerStatOptions
                .FirstOrDefault(o => o.Option.StatKey == stat) ?? t.SelectedPowerStat;

        // OnHeatmapEnabledChanged / OnSelectedPowerStatChanged kicked the build;
        // wait for it to drain so the screenshot reflects the result.
        for (int i = 0; i < 600 && (t.IsPowerBuilding || t.PowerOverlay is null); i++)
            await Task.Delay(50);

        return new { ok = true, building = t.IsPowerBuilding, rows = t.PowerReport.Count };
    }

    private static object TreePowerReport()
    {
        if (GetTreeVm() is not { } t) return new { error = "TreeTab not ready." };
        return new
        {
            enabled  = t.HeatmapEnabled,
            stat     = t.SelectedPowerStat?.Option.StatKey,
            isStale  = t.IsPowerStale,
            progress = t.PowerBuildProgress,
            rows = t.PowerReport.Take(30).Select(r => new
            {
                id = r.NodeId, name = r.Name, type = r.Type,
                power = r.PowerStr, perPoint = r.PerPointStr, alloc = r.IsAllocated,
            }).ToArray(),
        };
    }
```

Extend `TreeState` (~line 1157) with: `heatmapEnabled = t.HeatmapEnabled, powerStat = t.SelectedPowerStat?.Option.StatKey, isPowerStale = t.IsPowerStale, powerBuildProgress = t.PowerBuildProgress,`.

- [ ] **Step 3: Add MCP tools**

In `PBLMcp/VisualTools.cs`, near the other tree tools, add:

```csharp
    [McpServerTool(Name = "visual_tree_power_build")]
    [Description("Enable the tree heat map and build node power for a stat (e.g. FullDPS, Life; omit for Offence/Defence).")]
    public static async Task<string> VisualTreePowerBuild(string? stat = null)
    {
        try { return await IpcClient.CallAsync("POST", "/tree/power-build", new { stat }); }
        catch (Exception e) { return Err(e); }
    }

    [McpServerTool(Name = "visual_tree_power_report")]
    [Description("Read the current Power Report rows + heat-map state from the tree tab.")]
    public static async Task<string> VisualTreePowerReport()
    {
        try { return await IpcClient.CallAsync("GET", "/tree/power-report"); }
        catch (Exception e) { return Err(e); }
    }
```

Match the existing helper names in the file (`IpcClient.CallAsync` overloads, `Err`) — mirror an adjacent tool like `visual_tree_focus_node` exactly for signature/error style.

- [ ] **Step 4: Build to verify**

Run: `dotnet build PBLApp && dotnet build PBLMcp`
Expected: both succeed.

- [ ] **Step 5: Commit**

```bash
git add PBLApp/Ipc/IpcServer.cs PBLMcp/VisualTools.cs
git commit -m "feat(ipc/mcp): tree power-build + power-report endpoints"
```

---

### Task 8: Visual verification + final sweep

**Files:** none (verification only).

- [ ] **Step 1: Full engine test run**

Run: `dotnet test PBLEngine.Tests`
Expected: all green (existing + 6 new NodePower tests).

- [ ] **Step 2: Visual verify**

Run `/pbl-verify`. Then via the `pbl-engine` MCP server:
1. `visual_open_build` a build with allocated nodes.
2. `visual_select_tab` → Tree.
3. `visual_tree_power_build` with `stat="FullDPS"`.
4. `visual_screenshot` — confirm: heat-map glow rings on unallocated nodes (bright on high-DPS nodes), the Power Report panel populated on the right, top rows positive (green).
5. `visual_tree_power_report` — confirm rows JSON, the top node has a positive `power` string.
6. `visual_set_language` ru → screenshot: panel header "Отчёт о силе", column/labels localised.

Read the PNGs back into the conversation per the CLAUDE.md auto-rule.

- [ ] **Step 3: Stale path check**

Toggle a node (`visual_tree_toggle_node`/`/tree/toggle-node`), then `visual_tree_power_report` → `isStale: true`; click Refresh (or `visual_tree_power_build`) → `isStale: false` and rows updated. Screenshot the stale badge before refresh.

- [ ] **Step 4: Final commit (if any verification tweaks were needed)**

```bash
git add -A
git commit -m "test(tree): verify heat-map + power report end to end"
```

---

## Self-Review

**Spec coverage:**
- Heat-map colouring → Tasks 5, 6. Power Report list → Tasks 4, 6. ✓
- Full `powerStatList` + Off/Def default → Tasks 1, 4. ✓
- Side panel inside Tree tab → Task 6. ✓
- Toggle off-by-default + manual Refresh + stale badge → Tasks 4 (`MarkPowerStale`, no auto-rebuild), 6. ✓
- Approach A (reuse `BuildPower`) → Task 2. ✓
- Engine API (`GetPowerStatList`, `BuildNodePower`, DTOs) → Tasks 1, 2. ✓
- `NodePowerColorizer` separate class → Task 5. ✓
- IPC/MCP → Task 7. Localisation → Task 3. Tests → Tasks 1, 2, 8. ✓
- Error handling (try/catch keeps prior overlay; div-by-zero guarded in `Curve`; coroutine guard) → Tasks 2, 4, 5. ✓
- Parity sanity → Task 2 Step 5. ✓

**Placeholder scan:** No TBD/TODO; every code step has concrete content. Two symbol-verification notes (`GameTranslationService.TPassiveName`, `IpcClient`/`Err` helper names, `AffectsRender` registration) are explicit "match the existing symbol" instructions, not placeholders — the implementer confirms the exact name against adjacent code.

**Type consistency:** `NodePowerResult`/`NodePowerEntry`/`NodePowerMax`/`PowerStatOption` defined in Task 1 and used unchanged in Tasks 2/4/5/7. `BuildNodePower(string?, int?, Action<int>?)` signature consistent between Task 2 definition and Task 4 call. `NodePowerRowViewModel` / `PowerStatVm` defined in Task 4, consumed in Tasks 6/7. `NodePowerOverlay` property name consistent Tasks 5↔6. Localisation keys defined in Task 3 (incl. `Tree_Power_OffDef`) match all `loc:Tr` / `LocalizationService.Get` references in Tasks 4/6.
