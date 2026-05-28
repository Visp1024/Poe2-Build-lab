
# Plan: Migration to Avalonia UI

**Goal:** Replace the SimpleGraphic C++/DirectX renderer with a C# Avalonia UI (MVVM).
Lua calculation engine stays intact; only the UI layer is rewritten.

**Motivations:** Cyrillic / i18n support, modern UI (DPI scaling, theming), long-term maintainability.

---

## What stays in Lua vs. what moves to C#

### Stays in Lua (calculation engine)
- `Modules/Calcs.lua`, `CalcSetup.lua`, `CalcOffence.lua`, `CalcDefence.lua`, all other `Calc*.lua`
- `Modules/ModParser.lua`, `Modules/ModTools.lua`
- `Modules/Build.lua` — calculation part only (`loadBuild`, `calculateOutput`)
- `Classes/ModDB.lua`, `ModList.lua`, `ModStore.lua`
- All `Data/` files (`Gems.lua`, `ModCache.lua`, etc.)

### Moves to C# (UI layer)
- All tab classes: `CalcsTab`, `TreeTab`, `SkillsTab`, `ItemsTab`, `ConfigTab`, `NotesTab`
- All control primitives: `ButtonControl`, `EditControl`, `DropDownControl`, etc.
- `Modules/Main.lua` → C# app entry point
- `Modules/BuildList.lua` → ViewModel + View

---

## Lua binding: NLua (Lua 5.4)

**Choice: NLua 1.7.3** — .NET 9, Lua 5.4 via KeraLua.

- MoonSharp (pure .NET Lua 5.2) is 5–10× slower without JIT
- NLua 5.4 requires compat shims for LuaJIT 5.1 code (see `lua/compat.lua`)

---

## Solution layout

```
PBLEngine/        Shared class library — NLua host, BuildModel, DTOs
PBLApp.Core/      MVVM ViewModels — no Avalonia dependency
PBLApp/           Avalonia 12 UI (XAML views, ViewLocator)
PBLMcp/           MCP stdio server for agent testing
PBLEngine.Tests/  xUnit tests (79 passing)
```

---

## Phases

### Phase 0 — Spike / Proof of concept ✅ DONE
Lua calc engine runs inside C# via NLua. `build.calcsTab.mainOutput` readable from C#.

### Phase 1 — Lua host + build model ✅ DONE
`LuaHost` / `BuildModel` classes. Startup error-free. 644 stats. `INotifyPropertyChanged` fires on recalc.
- `PBLEngine/lua/compat.lua` — LuaJIT 5.1 → Lua 5.4 shims
- `LuaHost.cs` — owns Lua state, `Initialize()`, `NewBuild()`, `LoadBuildFromXml()`
- `BuildModel.cs` — typed stat properties + `INotifyPropertyChanged`

### Phase 2 — Main window + build list ✅ DONE
- `MainWindowViewModel` + `ViewLocator` (VM → View mapping)
- `BuildListViewModel` — file tree from `%LOCALAPPDATA%\PathOfBuilding\Builds`
- `BuildPageViewModel` — async load, loading spinner → stats display
- Navigation: BuildList ↔ BuildPage

### Phase 3 — Build screen shell + lightweight tabs ✅ DONE
- `BuildPage` with `TabControl`
- **NotesTab** — plain text editor + Save
- **New Build** — creates XML in Builds dir
- **ConfigTab** — data-driven from Lua config tables (checkboxes, dropdowns, counts)
- **Import/Export** — share codes (base64url + zlib); clipboard copy

### Phase 4 — CalcsTab ✅ DONE
- Stat tree: Offence / Defence / Resistances sections
- Breakdown panel — click stat → shows Lua breakdown lines
- Sidebar summary strip always visible (DPS / Life / ES / Mana / Armour / Eva)
- `GetStatBreakdown()`, `GetModifierTable()` in LuaHost

### Phase 5 — CalcsTab extended + SkillsTab (basic) ✅ DONE
- CalcsTab: Duration, Radius, ailments, Max Hit Taken sections
- CastOn / trigger group skill selector in CalcsTab
- SkillsTab (basic): socket group list → gem list, Set as Main Skill Group
- MCP tools: `app_get_gems`, `app_list_skills`, `app_select_skill`
- 66 xUnit tests

### Phase 6 — SkillsTab (full gem view) ✅ DONE
- `GemEntry` record with Color (attribute-based: Str=#F38BA8, Dex=#A6E3A1, Int=#89B4FA)
- `LuaHost.GetGemsInGroup()` — color from `gemData.reqStr/Dex/Int`
- `LuaHost.GetMainSkillGroupIndex()`, `SetActiveSkillGroup()`
- `SkillsTabViewModel` + `SkillGroupViewModel` + `GemViewModel`
- Set as Main Skill Group → triggers `build.Refresh()` + CalcsTab sync
- 13 new xUnit tests (total 79)

### Phase 7 — SkillsTab UI redesign ✅ DONE
- Left panel: active gem names colored by attribute
- Right panel: active gem header (Level/Quality step=5) + 5 support slots
- CastOn groups: support slots show active gems (IsSupport=false)
- Gem name dropdowns: colored items via `GemNameItem(Name, Color)` + `ItemTemplate`
- Filtering while typing; reset to last valid name on bad commit
- Arrow click shows full list when field is filled (`SearchText == _committedName`)
- `LuaHost.AddSkillGroupWithGem()`, `GetGemColors()`

### Phase 8 — ItemsTab (view + unequip) ✅ DONE
- `ItemEntry` record: Name, BaseName, Rarity, ItemLevel, Enchants/Implicits/Explicits
- `LuaHost.GetEquippedItems()` — reads `activeItemSet[slotName].selItemId`
- `LuaHost.UnequipItem()` — calls `slot:SetSelItemId(0)` (correct API; direct write to activeItemSet does NOT trigger recalc)
- `ItemsTabViewModel`: 20 named slot properties, SelectedMods with separator lines
- `ItemsTabView.axaml`: 3-column schematic figure layout (Helmet/Body/W1/W2/Rings/Belt/Boots/Amulet/Flasks/Charms/Arm/Leg)
- Right panel: item name colored by rarity + mods (enchant=#74C7EC, implicit=#BAC2DE, explicit=#CDD6F4)
- Delete button → `slot:SetSelItemId(0)` + `_build.Refresh()` + CalcsTab sync

---

### Phase 9 — TreeTab (passive tree viewer) ✅ DONE

Interactive passive tree with pan/zoom, node toggle, class/ascendancy switching, and sprite rendering.

**Core tree rendering:**
- [x] Read allocated nodes from `build.spec.allocNodes`
- [x] Read class-specific node positions/icons via `build.spec.nodes` (with `ReplaceNode` metatable)
- [x] Render on Avalonia custom `Control` (screen-space DrawingContext)
- [x] Node types: Normal, Notable, Keystone, Ascendancy, Socket, ClassStart, Mastery
- [x] Hover info panel — node name + type + stats (top-left overlay)
- [x] Node search / highlight by keyword (yellow ring)
- [x] Click to toggle node allocation; pan vs click: < 5px move = click
- [x] "Can allocate" detection — highlight nodes adjacent to allocated ones

**Visual quality:**
- [x] Sprite rendering from PoE2 DDS.ZST asset sheets (converted with `tools/convert_tree_assets.py`)
- [x] Icon clipped to circle with 60% black dim overlay for unallocated nodes
- [x] Frame sprites: `PSSkillFrame`, `NotableFrameAllocated`, `KeystoneFrameAllocated`, etc. with fallback ring
- [x] Global overlay fallback: `tree.nodeOverlay[type]` for Normal/Notable/Keystone (per-node override checked first)
- [x] Keystones: 8-sided polygon (procedural) + multi-layer glow emphasis
- [x] Connection pens: unalloc/canAlloc/alloc with distinct thickness
- [x] Ascendancy nodes in distinct color palette

**Class & ascendancy switching:**
- [x] Class selector + Ascendancy selector in toolbar
- [x] `LuaHost.SelectClass(classId, ascendClassId)` — calls `spec:SelectClass` + `spec:SelectAscendClass`
- [x] Nodes refresh on class/ascendancy change (`Nodes` is `[ObservableProperty]`)
- [x] Confirmation dialog on class change when user has allocated nodes (> 2 start nodes)
- [x] `LuaHost.ResetAllocatedNodes()` — clears all `allocNodes` before `SelectClass`
- [x] `_suppressClassChangeCheck` guard to prevent recursive handler when reverting selection
- [x] `Func<Task<bool>>? ConfirmClassChange` callback: PBLApp.Core fires it, View layer shows Avalonia dialog

**Ascendancy sub-tree display:**
- [x] Ascendancy nodes rendered separately from main tree; centroid brought to world (0,0) via `_ascendOffsets`
- [x] Background image (`Classes{AscendancyName}` sprite from `ascendancy-background_1500_1500_BC7` sheet)
- [x] Background center = `(_offsetX, _offsetY)` (screen position of effective world origin = centroid)
- [x] Background size = `maxNodeDistanceFromCentroid × 1.4 × scale` (computed per-ascendancy in `ComputeAscendOffsets`)

**Radius visualization:**
- [x] Jewel radius rings on Socket hover: Small/Medium/Large/Very Large (world: 1200/1380/1560/1800)
- [x] "Entwined Realities" / `intuitiveLeapLikeNodes` radius halos on allocated Keystones
- [x] `LuaHost.GetRadiusEmitters()` — reads `build.spec.intuitiveLeapLikeNodes`, emits `(nodeId, outerWorld)` pairs

**Key implementation facts:**
- `LuaHost.GetTreeData()` iterates `build.spec.nodes` (not `tree.nodes`) so class-specific icons/stats are correct
- Frame overlay lookup: `local ov = node.nodeOverlay or tree.nodeOverlay[type] or {}`
- `_ascendOffsets[name] = (-centroidX, -centroidY)`; screen center of offset ascendancy = `(_offsetX, _offsetY)`
- `_ascendRadii[name]` = max node distance from centroid, used for background half-size
- Stats/linkedIds packed as newline/comma-joined strings to minimise NLua round-trips
- `_fitNeeded` flag: auto-fit to main-tree extent on first render when Bounds are known

---

### Phase 10 — ItemsTab extended (item management) ✅ DONE (core item editor)

Full item management suite. Core editor (sub-tasks 1–3) complete:

**Item editor (inline, auto-opens on item click):**
- [x] `ItemEditorViewModel` — rarity selector, category/base picker, unique picker, item-level field
- [x] `ItemEditorView.axaml` — left panel (base/unique picker) + right panel (mod picker + selected mods)
- [x] Auto-open editor: clicking a slot or pool item shows the editor on the right without a separate edit button
- [x] `IsEditingExisting` mode: left panel + rarity toggles hidden; only mod list + mod picker shown
- [x] Mod picker filters by base type via `LuaHost.GetItemAffixes(baseName)`
- [x] Prefix / Suffix slot limits enforced; group deduplication (one tier per affix group)
- [x] Unavailable mods filtered from picker (not just greyed out)
- [x] Slider-based numeric mod editing — single `(N-M)` range → `Slider` + `TickFrequency=1`; value highlighted gold (`#F9E2AF`)
- [x] Mods are non-editable as text (`TextBlock` only); number colored differently from description text
- [x] Equip strip (pool items): slot ComboBox + Equip / Unequip buttons
- [x] Save → `LuaHost.UpdateItemFromText()` or `ImportItemFromText()`; Delete → `DeleteItemFromPool()`
- [x] Import from raw text panel (existing)

**Parsing / serialization fixes:**
- [x] `ParseExistingRaw` rewritten for PoB `Implicits: N` format (no `--------` separators)
- [x] `{range:X}`, `{implicit}`, `{crafted}` prefixes stripped from mod lines
- [x] `_fallbackBaseName` preserved for save when SelectedBase not in list
- [x] `FindMatchingAffix` resolves Prefix/Suffix type and group after base is loaded (exact + skeleton match)
- [x] Slider initial value taken from rolled text, not always max
- [x] `Item Level:` parsed from existing raw text

**Remaining sub-tasks:**
4. **Item search** — filter pool by desired stats / mod text
5. **Jewel sockets** — items in passive tree socket nodes
6. **Item sets** — save multiple equipment sets and switch between them (`itemSets` in Lua)
7. **Weapon swap** — Weapon 1/2 Swap slots; bind skill groups to a specific weapon set

---

### Phase 11 — Polish (ongoing)
- [x] i18n / Cyrillic — ResX-based localisation (Strings.resx + .ru.resx), TrExtension markup, GameTranslationService for game-data records (class/ascendancy names, gem names). Avalonia 12 indexer-binding refresh quirk worked around with a Binding+IValueConverter on CurrentLanguage.
- [x] Branding — "PoE2 Build Lab" window title, multi-res icon.ico, BuildList header logo
- [ ] HiDPI / DPI scaling
- [ ] Dark / light theme
- [ ] GitHub Actions CI
- [ ] **Jewel mod sliders** — PoB strips `(N-M)` templates from jewel raw lines after rolling (only concrete values remain, e.g. `5%`). `modLine.range` is preserved but `modList[i].min/max` is not directly accessible for `JewelFunc`-type radius mods. Need a Lua helper that walks `explicitModLines[k].modList` per-stat, looks up affix min/max from `data.itemMods.Jewel` (or equivalent), and reconstructs `(min-max)` templates so `ParseUniqueRaw` / `ExplicitModViewModel.ParseSingleIntRange` can detect ranges and render sliders. Complex because radius/threshold jewels use wrapper mod types.

---

### Phase 12 — Distribution & packaging ✅ DONE

Self-contained single-file Windows build for end-user distribution. Final size **~117 MB** (down from ~760 MB first cut).

**Publish profile** (`PBLApp/Properties/PublishProfiles/win-x64.pubxml`):
- `SelfContained=true`, `PublishSingleFile=true`, `IncludeNativeLibrariesForSelfExtract=true`, `EnableCompressionInSingleFile=true`. No .NET 9 install required on target PCs.
- Build command: `dotnet publish PBLApp/PBLApp.csproj -p:PublishProfile=win-x64 -o publish/win-x64`
- `ReadyToRun` and `PublishTrimmed` deliberately off (NLua/Avalonia rely on reflection that the trimmer can't see).

**Size optimisations applied:**
- `Assets/TreeData/**` moved from `<AvaloniaResource>` to `<Content>` with `CopyToOutputDirectory` / `CopyToPublishDirectory`. `TreeAssetStore` reads them as files anyway — embedding 217 MB into `PBLApp.dll` was dead weight. DLL dropped 220 MB → 3 MB.
- `LuaHost.Initialize`: `compat.lua` resolved via `AppContext.BaseDirectory`, not `Assembly.GetExecutingAssembly().Location` (the latter is empty under `PublishSingleFile=true`).
- `TreeAssetStore.TryLoad`: prefers `AppContext.BaseDirectory/Assets/TreeData/<ver>`, falls back to in-tree dev path.
- `CopyEngineFiles` MSBuild target excludes `src/TreeData/**/*.{dds,dds.zst,png,jpg}` on publish (assets for the original C++/DirectX renderer; not used by C#/Lua engine). `tree.lua` / `tree.json` kept for all versions so legacy builds load.
- `CopyEngineFiles` also re-copies `Assets/TreeData/**` explicitly after Publish — the SDK silently drops large BC7/BC1 PNGs from single-file publish output even when correctly marked as Content. Root cause TBD; explicit copy works around it.

**Tree asset WebP repack** (`tools/convert_tree_to_webp.py`):
- Tree sprite sheets re-encoded as **one WebP per row** (each tile is `sprite_w × sprite_h`). Original sheets are up to 1500×49500 px, way past WebP's 16383-px dimension limit — vertical tiling sidesteps it.
- `manifest.json` sheet entries: `"file"` replaced by `"tiles": ["..._0.webp", "..._1.webp", ...]` + `"tile_h"`.
- `TreeAssetStore.GetSprite`: for sheet-backed sprites, picks tile = `s.Y / TileH`, rebases local Y; tiles are bitmap-cached per filename. Manifest parser falls back to legacy `"file"` for single-tile sheets.
- Encode: tiles > 1 MB raw RGBA → lossy WebP q85 (backgrounds), smaller → lossless (crisp node icons). Method=6 for best ratio.
- Result: `Assets/TreeData/0_4` 217 MB → 27 MB (-87%). Biggest wins: `ascendancy-background_1500_1500_BC7` 127 MB → 5.3 MB, `mastery-active-effect_776_768_BC7` 35 MB → 5 MB.

**Crash diagnostics** (so the next end-user crash is debuggable):
- `Program.cs` installs handlers for `AppDomain.UnhandledException` + `TaskScheduler.UnobservedTaskException`; both write to `%LOCALAPPDATA%\PathOfBuilding2\crash.log` with timestamp, `BaseDir`, CWD, and full stack trace.
- `App.OnFrameworkInitializationCompleted` handles `Dispatcher.UIThread.UnhandledException` with `e.Handled = true` (process stays alive so the user can see the error on screen).
- `BuildListViewModel.NewBuildAsync` / `OpenBuild` wrapped in try/catch; errors surface via `StatusMessage` (red line above the toolbar) instead of crashing the app. Previously any exception from `await _hostTask` → silent process exit.

---

### Phase 13 — PoB-styled item tooltip + deep localisation ✅ DONE

End-to-end overhaul of the item display: in-game-style scroll tooltip on the right panel, full Russian localisation of every mod string the test build emits, expanded slot figure that fits localised names without overlap.

**Tooltip rendering** (`PBLApp.Core/ItemTooltipViewModel.cs`, `PBLApp/Views/ItemTooltipView.axaml`):
- `LuaHost.GetItemTooltipLines(itemId, slotName?)` wraps PoB's `ItemsTab:AddItemTooltip` with a stub Tooltip object that accumulates structured `AddLine` / `AddSeparator` calls. When `slotName` is passed, PoB appends the unequip-delta block automatically (via `main.slotOnlyTooltips = true`).
- `ItemTooltipViewModel.Load` parses PoB's inline `^x<hex>` / `^<digit>` colour codes into `TooltipSegment` runs; when the line gets a Russian translation the segments collapse to one in the dominant colour, then numbers are re-split into a contrasting accent (gold on cornflowerblue mods, white on grey labels).
- `ItemTooltipView` renders the line list in a styled `Border` (dark midnight bg + warm gold outline, no asset artwork). `FontSize` is scaled 0.85× from PoB pixels; size ≥ 20 → bold.
- Right-pane state machine: `IsRightPaneEmpty` / `IsTooltipMode` / `IsEditMode` — toolbar "Edit" flips to `ItemEditorViewModel`, editor close returns to tooltip.
- Lua-stub filters: drop `FONTIN SC` orange flavour text and `Tip: Press Ctrl+D…` footer at source.

**Localisation** (`PBLApp.Core/Localization/GameTranslationService.cs`):
- New dictionaries: `unique_names_ru.json` (~75 unique names), `magic_affixes_ru.json` (prefixes + suffixes maps for magic items).
- Engine extensions in `TooltipLine()`:
  - Exact lookup chain: `_passiveStats` → `_items` (bases) → `_uniques` → `_itemModExact`.
  - **Magic item name parser** (`TryTranslateMagicName`): finds the longest known base substring (with word boundaries), splits the line into `<prefix> <base> <suffix>`, looks each part up separately. Unknown affixes fall through as English so partial translation still reads.
  - **Template match** with number redaction (`\d+(?:[,.]\d+)?` → `#`): two template dicts — `_passiveStats` for shared stats, `_itemModTemplates` for ~50 hand-written item-mod patterns (resistances, +# to Level of all X skills, Allies in your Presence …, recovery, charge consumption, regen rates, Notable/Small Passive Skills in Radius also grant …).
  - **Compound rewrites**: requirements line ("Requires Level 16, 15 Dex" → "Требуется уровень 16, 15 ловкости"), Bonded:, "Grants Skill: Level N <gem>", "Notable Passive Skills in Radius also grant <stat>" (recursive — inner stat re-runs through the same engine), "(Not supported in PoB yet)" trailing marker.
  - **Delta-block formatter**: regex `^[+-]?\d[\d,.]*[m%]?\s+<label>(\s+\(±N.N%\))?$` handles thousand-separated numbers, percent and metres suffixes, looks the label up in `_deltaLabels`.
- ItemEditor mods (`ExplicitModViewModel`): `TranslatedText` / `TranslatedPartBefore` / `TranslatedPartAfter` lazy computed properties; XAML rebinds keep the gold number-accent on slider mods. Affix picker uses `TranslatedStatText`. Implicit-mod string list rendered via `TooltipKindConverter.TranslateLine` value converter.

**Slot figure layout** (`PBLApp/Views/ItemsTabView.axaml`):
- Left column widened 440 → 540 px, canvas 420×450 → 500×620 px.
- All 22 named slots repositioned with ≥ 5 px clearance — explicit list in commit log:

  | Slot           | Left | Top | W   | H   |
  |---             |---:  |---: |---: |---: |
  | Helmet         | 210  | 5   | 95  | 75  |
  | Amulet         | 310  | 5   | 80  | 45  |
  | Weapon 1 / Swap| 25   | 85  | 105 | 220 |
  | Body Armour    | 195  | 85  | 135 | 165 |
  | Weapon 2 / Swap| 370  | 85  | 105 | 220 |
  | Ring 1         | 135  | 255 | 60  | 45  |
  | Belt           | 200  | 255 | 95  | 45  |
  | Ring 2         | 300  | 255 | 65  | 45  |
  | Gloves         | 25   | 315 | 105 | 115 |
  | Charm 1/2/3    | 195  | 315/352/389 | 135 | 32 |
  | Boots          | 370  | 315 | 105 | 115 |
  | Flask 1        | 25   | 445 | 105 | 145 |
  | Flask 2        | 370  | 445 | 105 | 145 |
  | Toggle I       | 55   | 55  | —   | —   |
  | Toggle II      | 400  | 55  | —   | —   |

- `ItemSlotViewModel.LeftDisplayName`: for UNIQUE/RELIC shows "`<unique>, <base>`" (both localised); for RARE/MAGIC/NORMAL shows only the localised base (PoB's random rare prefix is dropped from the figure label). Pool list splits into `PoolTopName` (uniques only, hidden for others via `HasTopName`) + `PoolBaseName`.

**Test-build audit**: walked every slot (helmet/body/weapons/rings/gloves/boots/belt/amulet/flasks/charms/jewel sockets) via IPC and grepped tooltip lines — initial 119 untranslated → final 0 (unique flavour text excluded by design).

**New IPC endpoints** (`PBLApp/Ipc/IpcServer.cs`) and matching MCP tools (`PBLMcp/VisualTools.cs`):
- `GET /items/get-tooltip` — structured tooltip lines + segments JSON (used for the audit).
- `POST /items/edit-current` / `POST /items/cancel-edit` — flip right pane between tooltip and editor.
- `/items/state` extended with `isTooltipMode`, `isEditMode`, `isRightPaneEmpty`.

### Phase 14 — Skills tab polish + detachable tabs + Notes window ✅ DONE

Pass over the Skills tab and the overall tabbing UX based on direct user feedback.

**Skills tab — group header (`PBLApp/Views/SkillsTabView.axaml`)**:
- Combo-box rows enlarged to 28 px high with 15 pt text that vertically fills the row (was 26/12 with text hugging the top). `gem-combo` style sets `VerticalContentAlignment=Center` on the ComboBox + its templated `ContentPresenter`, and a `ComboBox.gem-combo TextBlock` selector centers items in the dropdown.
- The active gem in the right-pane header is now a **read-only** label (15 pt SemiBold, coloured by attribute) — it can no longer be re-typed there. Renaming the active skill goes through the support-row slot or the "New" dropdown on the left. Removes a long-standing footgun where typing in the header could replace the wrong gem.
- Inline "УР." / "КА." labels in front of the level/quality `NumericUpDown`s, matching the column headers used for support rows below. Column layout now `*,Auto,88,Auto,88,Auto,Auto`.

**Skills tab — tooltip rendering**:
- New `TooltipTextSegment` record on `GemTooltipEntry`; `ProcessTooltipLines` builds segments by regex-splitting each line on numbers (`[+\-]?\d+(?:[.,]\d+)?%?`) and tagging the digits with `#FAB387` orange against the line's base colour. `name` / `tag` / `desc` lines are passed through without number highlight (no useful numbers; would just confuse the eye).
- `PBLApp/Controls/InlinesHelper.cs` — attached property `ctl:InlinesHelper.Segments` that populates `TextBlock.Inlines` with coloured `Run`s. Bound across all five gem-tooltip templates in `SkillsTabView`.
- `PBLApp.Core/Translations/gem_tags_ru.json` grew from 45 → 73 entries. Missing tags found by enumerating `data.gems[*].tagString` in the live engine: Ammunition, Banner, Barrageable, Bear, Command, Companion, Conditional, Detonator, Grenade, Hazard, Herald, Invocation, Lineage, Merging, Meta, Orb, Payoff, Persistent, Plant, Remnant, Shapeshift, Staged, Storm, Sustained, Werewolf, Wind, Wyvern.

**Detachable tabs (`PBLApp/Views/TabWindow.axaml` + `BuildPageView`)**:
- Every TabItem header now carries a small `↗` button (`Button.popout` style — transparent, blue-on-hover). Click opens the tab's VM in a standalone `TabWindow` (900×650, `ContentControl Content="{Binding}"` resolves via `ViewLocator`).
- One window per tab key — re-clicking activates the existing window. `BuildPageView` keeps a `Dictionary<string, TabWindow> _tabWindows`.
- While a tab is popped out, the corresponding TabItem **hides** in the main TabControl. State lives on `BuildPageViewModel` as six `[ObservableProperty]` bools (`IsItemsPoppedOut`, …); each `TabItem.IsVisible="{Binding !IsXxxPoppedOut}"`. If the currently-selected tab is the one being popped out, selection jumps to `FirstVisibleTabIndex()`. Closing the window flips the flag back and the tab returns.

**Notes split out (`PBLApp/Views/NotesWindow.axaml`)**:
- Notes is no longer a TabItem. Header bar gained a "Заметки" button (`OpenNotes_Click`) that shows a dedicated `NotesWindow` (700×500). VM (`NotesTabViewModel`) is created in `BuildPageViewModel.LoadAsync` as before and just handed to the window's `DataContext`.
- `BuildPageViewModel.TabKeys` reduced from 7 → 6 entries — **`visual_select_tab("Notes")` is no longer valid**. Tab indices for Config/ImportExport shifted left by one.

**Localisation**:
- New `Tab_PopOut` string (`Strings.resx` / `Strings.ru.resx`) used as the `↗` button's `ToolTip.Tip`.

### Phase 15 — Config redesign + Settings/Import windows + window persistence ✅ DONE

**Config tab — PoB-style layout (`PBLApp/Views/ConfigTabView.axaml`, `ConfigTabViewModel`)**:
- Replaced flat vertical section list with a 3-column `Grid` of bordered "group-box" sections (mimics the original PoB fieldset look — section title + framed body).
- Sections are distributed across the three columns by a **greedy balancer**: walk sections in original order, append each into whichever column is currently shortest. `Weight(section) = Options.Count + 8 × (text-type options)` so the multi-line Custom Modifiers textarea (≈ 160 px) doesn't unbalance the layout. Replaces an earlier hard-coded `Section → Column` map.
- Each section title is a `Button.section-title` with `Command="{Binding ToggleCommand}"` and a `▾`/`▸` chevron — click collapses/expands the section body. State (`IsExpanded`) lives on `ConfigSectionViewModel`.
- Layout switched from "title button overlapping the border top edge (negative margin)" to plain `StackPanel { titleButton, Border(items) }` — fixes the title being clipped above the ScrollViewer viewport on the first row.
- New `text` config option type plumbed end-to-end: `LuaHost.GetConfigOptions` includes it in the filter, `ConfigOptionViewModel.OnTextValueChanged` skips numeric validation for `text`, XAML renders a multi-line `TextBox` spanning both columns for `customMods` (Custom Modifiers section is now functional).

**Header-button windows (`BuildPageView`)**:
- Removed `Config` and `ImportExport` TabItems from the main TabControl; added two header-bar buttons "Настройки" / "Импорт / Экспорт" next to the existing "Заметки". Each opens its own dedicated window.
- `PBLApp/Views/SettingsWindow.axaml(+cs)` — hosts `ConfigTabViewModel` only (no inner tabs).
- `PBLApp/Views/ImportExportWindow.axaml(+cs)` — hosts `ImportTabViewModel`.
- `BuildPageViewModel.TabKeys` reduced from 6 → 4 entries (Items/Tree/Skills/Calcs only). `visual_select_tab` for "Config" / "ImportExport" / "Notes" all rejected — these live in side windows now.
- Pop-out flags `IsConfigPoppedOut` / `IsImportExportPoppedOut` removed; `PopOutTab_Click` switch no longer maps those keys.

**Window persistence (`PBLApp/Controls/WindowDefaults.cs`)**:
- All app windows (`MainWindow`, `NotesWindow`, `SettingsWindow`, `ImportExportWindow`, `TabWindow`) call `WindowDefaults.Apply(this)` in their constructor.
- On `Opened`: read `%LOCALAPPDATA%\PathOfBuilding\window_state.json` (key = `GetType().Name`). If a saved entry exists, restore `Position`, `Width`, `Height`, and `WindowState=Maximized`; size is clamped to `WorkingArea / Scaling` and position is clamped so the window stays on-screen (handles disconnected monitors). If no entry: apply Full-HD default (1920×1080 in DIPs), clamped — XAML's `WindowStartupLocation` handles initial position (`CenterScreen` on `MainWindow`, `CenterOwner` on the rest).
- On `Closing`: each window writes its current rect + maximised flag back to the JSON map (best-effort, errors swallowed).

### Phase 16 — Tree perf overhaul + items polish + tooltip with stat diff ✅ DONE

**Passive tree — per-click latency 208 ms → ≈ 0.1 ms (≈ 2000× on the hot path)**:

- *Deferred recalc + debounce* (`LuaHost.cs`, `TreeTabViewModel.cs`): `AllocNode` / `DeallocNode` gained a `deferRecalc=true` argument that skips `runCallback('OnFrame')` and `Calcs.buildOutput`; the caller schedules a debounced `RecalcStats()` 120 ms later via `System.Timers.Timer`. Burst-click latency 847 ms → 664 ms initially.
- *canAlloc cache* (`TreeCanvas.cs`): the "can allocate" `HashSet<int>` recomputed every paint (≈ 10-30 ms on the full tree) now only refreshes when `AllocatedIds` or `Nodes` reference changes — pan/zoom/hover repaints reuse the cached set.
- *Batched IPC* (`LuaHost.GetAllocatedAndEmitters`): single `DoString` returns both alloc IDs and radius emitters, halving NLua marshalling per click.
- *Layered rendering* (`TreeCanvas.EnsureStaticLayer`): the ~5000 neutral-pen connection lines now bake into a `RenderTargetBitmap` at a `refScale` clamped to `min(max(_scale × 1.2, 0.15), 4000 / maxWorld)` so the bitmap fits within `RenderTargetBitmap`'s 4096 px limit even with PoE2's 32k × 33k world. Render does one `DrawImage` for static edges, overlays only the alloc / canAlloc connections per frame. Nodes still render per-frame (an earlier node-bake pass was reverted because small unallocated icons lost detail under bilinear downscale).
- *Delta-update `node.path`* (`LuaHost.AllocNode` fast path): the dominant cost inside `spec:AllocNode` (40-180 ms) was `BuildAllDependsAndPaths`. The new fast path walks `node.path`, marks each `alloc=true`, runs an incremental BFS that only relaxes shortest paths in the affected neighbourhood, and sets `spec._fastAllocDirty=true`. `DeallocNode` and `RecalcStats` flush the dirty flag (one full rebuild per burst, not per click). Falls back to upstream `spec:AllocNode` for any risky case (Keystone / Socket / containJewelSocket / intuitive-leap / multi-choice / unlockConstraint / conqueredBy on the target or any path node). Verified byte-identical stat snapshots between fast and full paths over a 10-click burst.

**Modern tree node hover tooltip** (`TreeCanvas.DrawHoverInfo`, `NodeHoverInfo.cs`, `LuaHost.GetNodeHoverInfo`):
- New `LuaHost.GetNodeHoverInfo(nodeId)` returns a `NodeHoverInfo` packet: node name + type + ascendancy + alloc state + path distance + mod text + stat diff (via `build.calcsTab:GetMiscCalculator(build)` → `calcFunc({addNodes={[node]=true}})` walked against `build.displayStats`) + per-point delta when path length > 1. Flushes `_fastAllocDirty` first so depends/paths are consistent.
- `TreeCanvas.HoverInfoProvider` StyledProperty wired by `TreeTabView.axaml.cs` from `TreeTabViewModel.GetNodeHoverInfo`. Result cached per-node; cache invalidated when `AllocatedIds` reference changes.
- Render redesigned with design-token brushes (`BgMantle` background, `BorderStrong` 6-dip rounded border, Inter typography). Title coloured by node type (Notable / AscendStart → Brand400 gold, Keystone → Warning gold, Socket → Info blue, Mastery → AttrInt purple, Normal → TextPrimary / Success when alloc'd). Mod lines highlight every numeric token in Brand400. Stat-diff lines: value in Success/Danger, label in TextPrimary, percent in TextSecondary, per-point bracket in TextMuted.
- Position: tooltip placed beside the hovered node (right of icon at `nx + nodeR + 18`, flips to left if it would overflow the canvas, vertically centred on node Y, clamped to bounds).
- Empty-diff hint: "No measurable change for current build" when the node has mods but no `displayStats` are affected (skill-specialised nodes for skills the player isn't using).

**Localisation of the tooltip** (`calc_labels_ru.json`, `Strings.resx`):
- New `PBLApp.Core/Translations/calc_labels_ru.json` with ~130 stat label translations (`Total Life` → `Жизнь`, `Average Damage` → `Сред. урон`, `Fire Resistance` → `Сопр. огню`, …) covering the full `BuildDisplayStats` set.
- `GameTranslationService.CalcLabel` + static `TCalcLabel` load on language change. Embedded in `PBLApp.Core.csproj`.
- `Tree_Tip_*` keys in `Strings.resx` / `Strings.ru.resx` for the four diff headers (alloc / unalloc / path-alloc / path-unalloc), the per-point suffix, the "no measurable change" note, and the points-to-allocate footer.
- Lua side returns header *keys* (`alloc` / `unalloc` / `pathAlloc` / `pathUnalloc`) and a raw per-point value; C# wraps with the localised `[value за очко]` bracket.

**Cast-on triggers fix** (`SkillsTabViewModel`, `LuaHost.GetSkillGroups`, `ConfigOptionDto.SkillGroupEntry`):
- `GetSkillGroups` now emits an `IsTrigger` flag — true when the first non-support gem on the group has `SkillType.Triggers` (32) or `SkillType.Meta` (122) on `skillTypes`.
- `SkillGroupViewModel.IsTrigger`, `SkillsTabViewModel.TriggerSlotGemNameItems` (active ⊕ support combined), `IsTriggerGroup(groupIdx)` helper.
- Empty support slots under a trigger group now offer triggered active spell gems (`Cast on Crit` / `Cast on Shock` / `Cast on Block` / `Cast on Minion Death` / etc.).
- `RebuildGroupGems` re-fetches `IsTrigger` after the active gem changes so the flag flips on the same click that pastes a meta gem name.

**Items tab polish**:
- *Hover tooltips* on every slot-cell `Border` and pool `Button` — plain-text packet (`ItemSlotViewModel.HoverTooltipText` / `ItemsTabViewModel.GetPoolHoverTooltipText`) pulls full mod text via `LuaHost.GetItemTooltipLines`, strips inline `^x` colour codes and `{range:0}`/`{crafted}` template placeholders. `ToolTip.ShowDelay=400`, `Placement=Pointer`.
- *Tighter ellipsis* for narrow / 1×1 slots — `slot-fallback-name-compact` style (font 9, line-height 10) on Amulet, Ring1, Ring2, Belt, Charm1/2/3 so item names fit without clipping at default zoom.
- *Magic-item base parser boundary fix* (`GameTranslationService.TryTranslateMagicName`): replaced `' '` boundary check with `IsWordChar` (letter / digit / apostrophe). Now `Ring` doesn't match inside `Ringmaster`, and bases adjacent to punctuation match correctly. The longest-match preference is preserved (`Heavy Belt` still beats `Belt`).

**Localisation — quick wins**:
- `CalcsTabView.axaml` final hardcoded `Text="Type"` replaced with `{loc:Tr Calc_ColType}` (P1.2 closed).
- `BuildPageView.axaml` sidebar DPS/Life/ES/Mana/Armour/Eva already localised in an earlier session (P1.1 confirmed closed).

**Item icons — 44 of 47 missing uniques recovered** (`tools/fetch_missing_uniques.mjs`):
- `cdn.poe2db.tw/gen/image/<path>.webp` returns 403 for some uniques (Queen of the Forest, Headhunter, Kalandra's Touch, Ming's Heart, etc.). Each poe2db item page however embeds the signed GGG CDN URL — `web.poecdn.com/gen/image/<base64>/<hash>/<file>.png`.
- New scraper slugifies each missing unique name, GETs the poe2db item page, extracts the signed URL with regex, downloads the PNG into the cache under the same DDS-mirrored path, and updates `icon_map.json`. 44/47 ok; three Sekhema's Resolve variants need a different slug pattern.
- `PBLApp.csproj` Content / `CopyEngineFiles` globs widened from `**/*.webp` to `**/*.*` so the new PNG files travel into the publish output. Avalonia `Bitmap` sniffs format by magic bytes, so `.png` content saved with `.png` extension loads in the slot grid like any other cached icon.

---

## Backlog (deferred / future work)

### Items / display
- [ ] **Phase 10 #4 — Item search** in the pool by stat / mod text.
- [ ] **Phase 10 #6 — Item sets** (PoB's `itemSets` — save multiple equipment configurations, switch between them).
- [ ] Unique flavour text translation (`unique_flavour_ru.json`) — generate from GGPK `Words.datc64` when schema becomes available, or hand-edit per unique.
- [ ] Three Sekhema's Resolve element variants — slug mismatch on poe2db, need manual URL mapping.
- [ ] Upgrade plain-text hover tooltips on slots / pool items to the full styled `ItemTooltipView` content (lazy-build VM on `ToolTip.Opening`).

### Localisation depth
- [ ] Extend `unique_names_ru.json` beyond the ~75 hand-translated PoE2 uniques to full coverage (~600+).
- [ ] Extend `magic_affixes_ru.json` — current ~80 prefixes / ~70 suffixes is best-effort. Need to enumerate every PoE2 magic affix from GGPK or community translation set.
- [ ] Generate `gem_descriptions_ru.json` (flavour) from GGPK when accessible.
- [ ] Extend `calc_labels_ru.json` — current ~130 entries cover the main `BuildDisplayStats` set; add minion-side labels and rarer secondary stats as users report English bleed-through.
- [ ] **Jewel mod sliders** — PoB strips `(N-M)` templates from jewel raw lines after rolling (only concrete values remain, e.g. `5%`). `modLine.range` is preserved but `modList[i].min/max` is not directly accessible for `JewelFunc`-type radius mods. Need a Lua helper that walks `explicitModLines[k].modList` per-stat, looks up affix min/max from `data.itemMods.Jewel` (or equivalent), and reconstructs `(min-max)` templates so `ParseUniqueRaw` / `ExplicitModViewModel.ParseSingleIntRange` can detect ranges and render sliders. Complex because radius/threshold jewels use wrapper mod types.

### Large features
- [ ] **CalcsTab / stats window redesign** — текущая вёрстка `CalcsTabView` повторяет PoB 1:1: вертикальный список секций с одинаковыми серыми заголовками, нет быстрого способа найти конкретный стат, на широких мониторах справа пустое пространство. Что сделать: (1) **поиск** — `TextBox` в шапке вкладки, фильтрует строки по имени стата и алиасам (`crit`, `crit chance`, `критический удар`), подсветка совпадений жёлтым; (2) **цветовая дифференциация** по доменам — Offence (красно-оранжевая палитра), Defence (синяя), Life/ES/Mana (зелёная/бирюзовая/фиолетовая), Resistances (по стихиям: огонь/холод/молния/хаос), Modifiers/Charges (нейтрально-золотой); цвет на левом border-accent у строки + опционально pill-badge у числа; (3) **компоновка** — переход с одной колонки на адаптивный grid (2-3 колонки в зависимости от ширины окна, аналогично Phase 15 ConfigTab balancer), важные секции (DPS, EHP, Life, Resistances) закреплены сверху-слева крупными карточками; (4) **сворачивание секций** + персист состояния в `window_state.json`; (5) **закреплённые («pinned») статы** — пользователь может пометить любую строку звёздочкой, она клонируется в верхний sticky-блок «Избранное» и виден всегда; (6) **breakdown-tooltip** на hover — текущий клик-в-popup сохранить, но добавить лёгкое hover-превью первых 3-5 строк breakdown'а; (7) пересмотреть какие секции реально нужны рядовому пользователю vs «глубокий разбор» — последние спрятать под toggle `Подробный режим`. Затронет `CalcsTabView.axaml(+cs)`, `CalcsTabViewModel`, `StatSectionViewModel`, потребует категоризации статов (новый `StatCategory` enum + маппинг по имени или через атрибут в Lua-секциях).
- [ ] **Stat graphs / charts** — построение графиков зависимости одного стата от другого по диапазону входной переменной. UX: новая вкладка `Графики` (или модальное окно из CalcsTab) с выбором: X-оси (Skill Level 1-20, Enemy Resistance -100..90, Player Life %, Number of Power Charges, гем-уровень основного скилла, число поциний усиления, …), Y-оси (Total DPS, Effective Hit Pool, Life, Mana, любая ключевая метрика из `Calcs.buildOutput`), и опционально серий (вторая шкала / второй стат). Алгоритм: в цикле по точкам X выставлять временный override в `build` (через `ConfigInput` или прямую инъекцию в actor mods), гонять `Calcs.buildOutput`, собирать (x, y) пары — пересчёт идёт на C#-стороне без UI-кадров; прогресс-бар на длинных рядах. Рендер: LiveChartsCore 2.x (Avalonia-совместимый) или собственный `Control` на DrawingContext, если хотим без зависимости. Сохранение пресетов графиков рядом с билдом (`<Graphs>` секция в build XML). Экспорт PNG/CSV. Полезные дефолтные пресеты: `DPS vs Skill Gem Level`, `DPS vs Enemy Armour`, `EHP vs Fire Res`, `Reservation Efficiency vs Number of Auras`.
- [ ] **Build comparison** — сравнение текущего билда с другим выбранным (вторым из BuildList). UX: кнопка `Сравнить с…` в `BuildPageView` header → диалог выбора второго билда из дерева → раскрывается правая панель с двумя колонками (`Текущий` / `Сравнение`) и колонкой дельт. Что сравнивать: (1) все ключевые stat'ы (Life/ES/Mana, Armour/Evasion/Block, все DPS-варианты, resists, ailment-thresholds) с цветом дельты (зелёный/красный); (2) пассивные ноды — diff множеств `allocNodes` с группировкой "только в A" / "только в B" / "общие"; (3) экипировка по слотам — список различий (mod-уровни, базы, уники); (4) активные скиллы / поддержки; (5) Config-инпуты. Реализация: загружать второй билд во второй `LuaHost` (или последовательно в текущий — но дороже), снимать snapshot всех `Calcs.buildOutput` + spec + items, диффить в C# (`BuildComparisonService`). Без правок второго билда (read-only). Опция: «Применить из B в текущий» для отдельных частей (скиллы, дерево, предмет в слот).

### Passive tree
- [ ] **Path preview on hover** — при наведении курсора на неаллоцированную ноду подсветить кратчайший путь от ближайшей аллоцированной ноды к hover-таргету: рёбра пути рисуются пунктиром поверх обычных connections, промежуточные ноды подсвечиваются (полу-яркая обводка, отличная от `canAlloc`-ring). BFS по `node.linked` из множества `allocNodes`, кэш результата на `(allocSet-hash, hoverNodeId)`. Снимать подсветку при уходе курсора или клике.
- [ ] **Async optimistic alloc** — `spec:AllocNode` всё ещё 40-180 ms (intrinsic). Поднять отзывчивость можно оптимистичным UI: помечать ноду alloc на C#-стороне *до* Lua-вызова, перерисовать, затем гонять Lua в `Task.Run` (с `SemaphoreSlim` для сериализации NLua). Главная сложность — путь allocate-нод может включать промежуточные, которые мы не знаем заранее в C#; нужен read-only снимок `node.path` с предыдущего refresh. Опционально показать spinner поверх ноды на время Lua-операции.
- [ ] **Tree-view node bake**: вторая попытка запекать ноды в `static layer`. Прошлая (commit d119426 → revert) портила мелкие unalloc-иконки через bilinear downscale. Идея: запекать на ≥ 2 LOD-уровнях (`refScale × 1`, `refScale × 2`) и переключать по zoom, либо запекать только frame-сприты, иконки оставлять live.

### UI polish
- [ ] HiDPI / DPI scaling pass.
- [ ] Dark / light theme switch.

### Infrastructure
- [ ] GitHub Actions CI — publish workflow + xUnit run.
- [ ] Auto-update tooling for end users (download self-contained build delta).
- [ ] `PBLExport` regeneration scripts for `passive_nodes_ru.json` / `passive_names_ru.json` after game patches (already wired, just need scheduled run).
- [ ] Trimming experiment: per-assembly trim hints for NLua / Avalonia to reduce single-file size below 50 MB.

---

## Key architectural facts

| Topic | Detail |
|---|---|
| Lua item unequip | `slot:SetSelItemId(0)` — not `activeItemSet[slot].selItemId = 0` |
| DPS=0 on open | `SetActiveSkillGroup` must set both `build.mainSocketGroup` AND `calcsTab.input.skill_number` |
| Per-type Min/Max | CalcOffence writes them only in CALCS mode; `GetAllStats()` merges calcsOutput + mainOutput |
| Gem color | `gemData.reqStr/Dex/Int` dominance: Str=#F38BA8, Dex=#A6E3A1, Int=#89B4FA |
| `gem.grantedEffect` | nil for name-spec gems; use `gem.gemData.grantedEffect` |
| CastOn groups | `LuaHost.GetSkillGroups` reports an `IsTrigger` flag — first non-support gem with `SkillType.Triggers` (32) OR `SkillType.Meta` (122) on its `skillTypes`. Empty support slots in trigger groups offer combined active+support list via `SkillsTabViewModel.TriggerSlotGemNameItems`. |
| Tree fast alloc | `LuaHost.AllocNode(id, deferRecalc=true)` skips `BuildAllDependsAndPaths` (40-180 ms saved per click); does incremental BFS over `node.path` then sets `spec._fastAllocDirty`. `RecalcStats` and `DeallocNode` flush the dirty flag before reading depends. Falls back to upstream `spec:AllocNode` when target or any path node has intuitive-leap / multi-choice / Keystone / Socket / containJewelSocket / unlockConstraint / conqueredBy. |
| Tree static layer | `TreeCanvas.EnsureStaticLayer` bakes connections into a `RenderTargetBitmap` at `refScale = min(max(_scale × 1.2, 0.15), 4000 / max(worldW, worldH))`. The 4000 cap keeps PoE2's 32 k × 33 k tree inside Avalonia's 4096-px `RenderTargetBitmap` limit. Re-bake triggered by `Nodes` ref, `AscendancyFilter` change, or `_scale` past 1.5 × `refScale`. Node bake was tried and reverted — small unallocated icons lost detail when the bitmap was downscaled. |
| Tree hover tooltip | `LuaHost.GetNodeHoverInfo(nodeId)` → `NodeHoverInfo` packet (mods + stat diff via `build.calcsTab:GetMiscCalculator` + path distance). `TreeCanvas.HoverInfoProvider` callback set by view; per-node cache invalidated on `AllocatedIds` ref change. Header strings returned as keys (`alloc` / `unalloc` / `pathAlloc` / `pathUnalloc`) so C# resolves via `Strings.resx`. Stat labels translated via `calc_labels_ru.json` (`GameTranslationService.TCalcLabel`). |
| `item:BuildRaw()` format | PoB internal format uses `Implicits: N` (no `--------` separators). After `Implicits: N`, exactly N lines are implicits; rest are explicits. Mod lines may have `{range:X}`, `{implicit}`, `{crafted}` prefixes — strip with `^(\{[^}]+\})+`. |
| Item editor mod types | `GetItemAffixes(baseName)` returns affix list after `TrySelectBase`. Match loaded mod text to affixes via exact match then skeleton match (strip `\(?\d+(?:[.-]\d+)?\)?`). |
| Item editor auto-open | `OnSelectedSlotNameChanged` / `OnSelectedPoolItemIdChanged` create `ItemEditorViewModel` directly; no separate edit button. `IsEditingExisting=true` hides base/rarity pickers. |
| Single-file publish path | `Assembly.GetExecutingAssembly().Location` is empty under `PublishSingleFile=true`. Always resolve "files next to exe" via `AppContext.BaseDirectory`. |
| Tree sheet tiles | WebP dimension limit is 16383 px; PoE2 sheets are up to 49500 px tall. Sheets are split into per-row WebP tiles; `TreeAssetStore` picks `tile = y / tile_h` and rebases local Y. Re-run `tools/convert_tree_to_webp.py` after regenerating assets. |
| Crash log location | `%LOCALAPPDATA%\PathOfBuilding2\crash.log`. Three handlers feed it: AppDomain, TaskScheduler, Dispatcher.UIThread. |
| Tooltip line translation | `GameTranslationService.TooltipLine(plain)` is the single entry point used by `ItemTooltipViewModel`, `ExplicitModViewModel.TranslatedText`, affix picker (`TranslatedStatText`), and the implicit-mod list (via `TooltipKindConverter.TranslateLine` converter). When extending translations always extend this engine, not the consumers. |
| Magic item naming | `<Prefix> <BaseName> of the <Suffix>` — `TryTranslateMagicName` locates the longest known base substring (word boundary check), splits, and looks each part up in `_magicPrefixes` / `_items` / `_magicSuffixes` separately. Adding bases without affixes? Add to `items_*.json`. Adding affixes? Edit `magic_affixes_*.json` directly. |
| Number highlighting | Translator collapses multi-segment lines into one segment; `ItemTooltipViewModel.HighlightNumbers` then re-splits digits/percents into a contrasting colour run (gold on cornflowerblue, white on grey). Don't restore the original PoB segment colouring — translation breaks the per-token boundaries. |
| Slot layout sanity | Every rectangle on the figure canvas (500×620) must have ≥ 5 px clearance from its neighbours. Recompute on Width/Left changes by hand; there's no auto-layout. |
| Right-pane state | `IsRightPaneEmpty` (no item) / `IsTooltipMode` (`ItemTooltip != null`, no editor) / `IsEditMode` (editor open) are computed booleans that drive `IsVisible` on three sibling panels in `ItemsTabView`. |
