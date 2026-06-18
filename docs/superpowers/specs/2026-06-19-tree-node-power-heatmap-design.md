# Design: Passive-tree node-power heat map + Power Report

**Date:** 2026-06-19
**Status:** Approved (design phase)
**Area:** PBLEngine + PBLApp.Core + PBLApp (Tree tab), IPC/MCP, localisation

## Goal

Port the original Path of Building "Power Report" / node heat-map feature to the
Avalonia client: colour every unallocated passive-tree node by its contribution
("power") to a selected stat, and show a sortable Power Report table ranking
nodes by that contribution. Lets the user see, at a glance, the strongest nodes
to allocate for a chosen goal (DPS, Life, EHP, a single resistance, …).

This is the C# port of the existing Lua feature; calculation stays in Lua.

## Scope (decisions locked in brainstorming)

- **Both halves of the original feature**: heat-map colouring of the tree *and* a
  sortable Power Report list. (Not "single best node".)
- **Full `data.powerStatList`** (~40 stats) selectable from a dropdown, with the
  combined **Offence/Defence** mode as the default.
- **UI placement**: controls in the Tree-tab toolbar; the Power Report renders in
  a **side panel inside the Tree tab** (not a separate window).
- **Recompute model**: off by default. Toggle on (or change stat) → compute once
  with a progress indicator and cache. After any build edit the panel shows a
  **"stale" badge + manual Refresh button** — no automatic background recompute.
- **Computation strategy: Approach A** — reuse PoB's own `CalcsTab:BuildPower()`
  coroutine driven from C#, for maximal parity and minimal new Lua. (Approach B,
  a bespoke C# loop re-implementing BuildPower, was rejected — divergence risk +
  worse for upstream syncs.)

## Non-goals

- No per-edit live recompute (explicitly manual refresh).
- No new colour-theme picker UI — ship one default theme (RED/BLUE) as a constant
  with a seam for a future setting.
- No node-power-max-depth control surfaced in UI initially (engine supports it;
  default = unlimited).

## How the original works (reference)

- `CalcsTab:PowerBuilder()` (`src/Classes/CalcsTab.lua`) is a coroutine that walks
  every unallocated node, runs `calcFunc({ addNodes = { [node]=true } })` via
  `GetMiscCalculator`, and stores `node.power.singleStat` (selected stat delta) and
  `node.power.pathPower` (delta for the whole path to the node). For the default
  mode it stores `node.power.offence` / `node.power.defence`. It caches results by
  `node.modKey`, honours `nodePowerMaxDepth`, includes cluster notables, and
  tracks `powerMax` (per-channel maxima) for normalisation. Allocated nodes get a
  removal-delta instead. Progress is reported via `build.powerBuilderProgressCallback`.
- `PassiveTreeView` colours nodes: `statCol = (max(power,0) / powerMax * 1.5) ^ 0.5`,
  mapped to channels per `main.nodePowerTheme` (RED/BLUE, RED/GREEN, GREEN/BLUE).
  Offence/Defence mode maps `offence`→one channel, `defence`→another.
- `TreeTab:BuildPowerReportList(stat)` builds the table rows: `name`, `power`
  (single-stat Δ), `pathPower` (Δ per point = pathPower/pathDist), `allocated`,
  `type`, `pathDist`, sorted by power (respecting `lowerIsBetter`). Formatting
  (`fmt`, `pc`, `mod`, `lowerIsBetter`) is pulled from the matching
  `build.displayStats` entry.

## Architecture

### Component 1 — Engine API (`PBLEngine/LuaHost.cs` + DTOs)

Three new methods plus DTOs. All run on the LuaHost serialised executor.

- `IReadOnlyList<PowerStatOption> GetPowerStatList()`
  - Returns `data.powerStatList`: `{ StatKey (nullable), Label, CombinedOffDef,
    IgnoreForNodes, LowerIsBetter, Fmt }`. Drives the dropdown. Filters out the
    item-only entry (`ignoreForNodes` / `itemField`) for the node heat map but
    keeps the `combinedOffDef` default entry.

- `NodePowerResult BuildNodePower(string? statKey, int? maxDepth, Action<int>? onProgress)`
  - Sets `calcsTab.powerStat` to the matching `powerStatList` entry (or the
    `combinedOffDef` entry when `statKey == null`).
  - Flushes `_fastAllocDirty` (`BuildAllDependsAndPaths`) first — power/path diffs
    require a consistent depends/path tree (same guard as `GetNodeHoverInfo`).
  - Sets `calcsTab.nodePowerMaxDepth = maxDepth` (nil = unlimited).
  - Drives the coroutine: `calcsTab.powerBuildFlag = true`, then loops
    `calcsTab:BuildPower()` until `calcsTab.powerBuilder == nil`, reading a percent
    written by an injected `powerBuilderProgressCallback` and invoking `onProgress`
    between resumes (so the UI gets a real progress bar).
  - Returns `NodePowerResult { Mode (single|offdef), Max { SingleStat, Offence,
    Defence }, Entries }`.

- The per-node data ships **inside** `BuildNodePower`'s result via a flat
  `\x1F`/`\t`-delimited Lua string parsed in C# (avoids the NLua `LuaTable.Keys`
  drop-on-long-list bug seen in `GetItemTooltipLines`). Each entry:
  `id, name, type, power, pathPower, pathDist, alloc`. Selection mirrors
  `BuildPowerReportList`: `type ∈ {Normal, Notable, Keystone}`, not ascendancy;
  plus unallocated cluster notables.

DTOs (new `PBLEngine/NodePower.cs`): `PowerStatOption`, `NodePowerEntry`,
`NodePowerMax`, `NodePowerResult`.

### Component 2 — Tree-tab view-model (`PBLApp.Core/TreeTabViewModel.cs`)

- New observable state: `HeatmapEnabled` (bool), `PowerStatOptions`
  (`ObservableCollection<PowerStatOption>`), `SelectedPowerStat`,
  `IsPowerBuilding` (progress), `PowerBuildProgress` (0-100), `IsPowerStale`
  (bool), `PowerReport` (`ObservableCollection<NodePowerRowViewModel>`).
- `BuildPowerAsync()` — calls `LuaHost.BuildNodePower` on a background task,
  marshals progress to the UI thread, fills `PowerReport` (sorted), pushes the
  per-node power map + `Max` into the canvas, clears `IsPowerStale`.
- Toggling `HeatmapEnabled` on (or changing `SelectedPowerStat`) triggers a build.
  Toggling off clears the canvas overlay and hides the panel.
- Existing edit hooks (alloc/dealloc, and the build-changed signal already used
  for recalc) set `IsPowerStale = true` when the heat map is on — they do **not**
  auto-rebuild.
- `NodePowerRowViewModel` — `Name, Type, PowerStr, PerPointStr, NodeId,
  IsAllocated`, plus localised colour. Row activation → existing focus-node path
  (`visual_tree_focus_node` equivalent already in the VM/IPC).

### Component 3 — Tree colouring (`PBLApp/Controls/TreeCanvas.cs` + new `NodePowerColorizer.cs`)

- `TreeCanvas` gains a `NodePowerOverlay` property: the per-node power map + `Max`
  + mode. When non-null and a node is unallocated, its fill/ring is tinted by
  `NodePowerColorizer.ColorFor(power, max, mode)`.
- `NodePowerColorizer` (new, keeps `TreeCanvas` from bloating): replicates PoB's
  `(max(power,0)/powerMax*1.5)^0.5` curve and the RED/BLUE theme channel mapping;
  Offence/Defence mode blends `offence`/`defence` channels. Theme is a constant
  for now (seam for a future ConfigTab setting).
- Colour cache invalidated when the overlay reference changes (mirrors the
  existing `canAlloc` HashSet cache pattern from Phase 16).

### Component 4 — IPC / MCP / localisation / tests

- IPC (`PBLApp/Ipc/IpcServer.cs`): `POST /tree/power-build {stat}` (kicks the
  async build, returns when done), `GET /tree/power-report` (rows JSON), and
  `/tree/state` extended with `heatmapEnabled`, `powerStat`, `isPowerStale`,
  `powerBuildProgress`.
- MCP (`PBLMcp/VisualTools.cs`): `visual_tree_power_build(stat)` and
  `visual_tree_power_report()` for screenshot-driven verification.
- Localisation: stat labels already exist in `calc_labels_ru.json`
  (`GameTranslationService.CalcLabel`). Add `Strings.resx`/`.ru.resx` keys:
  `Tree_Heatmap`, `Tree_PowerReport`, column headers (`Tree_Power_Node`,
  `Tree_Power_Type`, `Tree_Power_Delta`, `Tree_Power_PerPoint`), `Tree_Power_Stale`,
  `Tree_Power_Refresh`, `Tree_Power_Building`.
- Tests (`PBLEngine.Tests`): `BuildNodePower` returns a non-empty report; the
  top-ranked node has positive power for a DPS stat; Offence/Defence mode returns
  both channel maxima; `GetPowerStatList` is non-empty and contains the off/def
  default. Manual finish: `/pbl-verify` — screenshot showing the heat-mapped tree
  + populated Power Report panel.

## Data flow

1. User toggles "Тепловая карта" / picks a stat in the Tree toolbar.
2. `TreeTabViewModel.BuildPowerAsync` → `LuaHost.BuildNodePower(statKey)` on a
   background task; progress streamed to `PowerBuildProgress`.
3. Lua sets `powerStat`, drives `BuildPower()` to completion, returns
   `NodePowerResult` (max + flat entries).
4. VM fills `PowerReport` (sorted) and hands the per-node map to `TreeCanvas`
   via `NodePowerOverlay`; canvas repaints with `NodePowerColorizer` tints.
5. Build edit while on → `IsPowerStale = true`; panel shows stale badge + Refresh.
6. Refresh / stat change → back to step 2.

## Error handling

- `BuildNodePower` wrapped in try/catch in the VM; on failure set a panel error
  line (mirrors `BuildListViewModel` status-line pattern) and leave the previous
  overlay intact rather than crashing the tree.
- If `powerMax` channel is 0 (no node moves the stat), colourise nothing (avoid
  divide-by-zero) and show an empty report with a "no measurable change" note.
- Coroutine drive loop bounded by a sane max-resume guard so a Lua bug can't hang
  the UI thread; on overrun, abort and report.

## Testing strategy

- xUnit (engine): see Component 4.
- Parity sanity: not a calc change, but run one `scripts/parity.ps1` build to
  confirm `BuildNodePower` side effects (setting `powerStat`) don't perturb
  `mainOutput` (it shouldn't — it only writes `node.power`/`powerMax`).
- Visual: `/pbl-verify` screenshot of heat map + report; `/pbl-check` for stat
  switching.

## Open seams (future, out of scope)

- Colour-theme picker (RED/BLUE / RED/GREEN / GREEN/BLUE) in Config.
- `nodePowerMaxDepth` UI control.
- Compare-build power report (PoB's `ComparePowerReportListControl`).
