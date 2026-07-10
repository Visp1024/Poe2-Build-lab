# Node Power v2 Implementation Plan (пул Lua-воркеров, шаги, редизайн панели)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ускорить расчёт тепловой карты дерева пулом фоновых LuaHost-воркеров (ленивый прогрев при первом расчёте), починить колонку «шаги до ноды» и переделать панель Power Report в домашний стиль TraderWindow.

**Architecture:** Расчёт power разбивается на батчи и раздаётся через общую очередь N воркерам (`LuaWorkerPool`, каждый — свой NLua-state со своим потоком). Воркеры — одноразовые считающие копии: перед расчётом грузят XML билда с главного хоста. Чистая логика раздачи/слияния (`PowerDispatcher`/`NodePowerOrchestrator`) отделена от Lua и тестируется фейками. Старый `BuildNodePower` (PoB-корутина) остаётся эталоном в паритет-тесте. Спека: `docs/superpowers/specs/2026-07-11-tree-power-v2-design.md`.

**Tech Stack:** C# / .NET 9, NLua (Lua 5.4), Avalonia 12 + SukiUI, CommunityToolkit.Mvvm, xUnit.

## Global Constraints

- Вся работа — на ветке `feat/tree-node-power-heatmap` (Task 1 вливает в неё `main`).
- Расчётная логика PoB не меняется: воркеры зовут `GetMiscCalculator`/`CalculatePowerStat`/`CalculateCombinedOffDefStat` из `src/Classes/CalcsTab.lua`, файлы `src/` не трогаем.
- NLua `LuaTable.Keys` теряет элементы на длинных списках — массовые данные возвращать только плоской строкой `\x1F` (строки) / `\t` (поля), парсить в C# (паттерн `GetItemTooltipLines`).
- Один Lua-state — один поток: каждый `LuaWorker` сериализует доступ своим `SemaphoreSlim(1,1)`; к главному хосту из пула не обращаться (все обращения к нему делает VM до/после раздачи батчей).
- Перед любым чтением путей/расчётом на главном хосте — флаш `build.spec._fastAllocDirty` через `BuildAllDependsAndPaths` (как в `GetNodeHoverInfo`).
- Тесты — только в `PBLEngine.Tests` (xUnit, `[Collection("LuaHost")]` + `IClassFixture<LuaHostFixture>`). PBLApp/PBLApp.Core проверяются сборкой + скриншотом.
- Изменения UI завершаются `/pbl-verify` (скриншот обязателен, зелёной сборки мало).
- Строки UI — через `Strings.resx`/`Strings.ru.resx` + `{loc:Tr Key}`; ComboBox — канонический рецепт TraderWindow (plain SukiUI, без Background/template-переопределений, локализованный список + SelectedIndex/SelectedItem, light-dismiss).
- Коммит после каждой задачи; сообщения `feat(tree-power): …` / `fix(tree-power): …` / `test(engine): …`.
- Число воркеров: préf-ключ `tree.powerWorkers` в `AppPreferences` («auto» → `Math.Clamp(Environment.ProcessorCount - 2, 1, 4)`, `0` → пул выключен).

---

### Task 1: Влить main в feat/tree-node-power-heatmap

**Files:**
- Modify: конфликтные файлы merge (ожидаемо `PBLApp/Views/TreeTabView.axaml`, `PBLApp.Core/TreeTabViewModel.cs`, `PBLApp.Core/Localization/Strings*.resx`, `PBLEngine/LuaHost.cs`, `PBLApp/Ipc/IpcServer.cs`, `PBLMcp/VisualTools.cs`)

**Interfaces:**
- Produces: ветка `feat/tree-node-power-heatmap`, собирающаяся с кодом трейдера и релиза 0.2; все существующие тесты зелёные.

- [ ] **Step 1: Merge**

```bash
git checkout feat/tree-node-power-heatmap
git merge main
```

Правила разрешения конфликтов: обе стороны добавляли независимые куски — сохранять **обе** (power-панель и тулбар-тоггл с ветки; трейдерные «Подбор»-кнопки, resx-ключи и IPC-ручки с main). В `LuaHost.cs` конфликтов по смыслу быть не должно (power-методы — отдельный блок). В resx при конфликте объединить оба набора `<data>`-узлов.

- [ ] **Step 2: Сборка и тесты**

```powershell
dotnet build PBLApp; dotnet test PBLEngine.Tests
```
Expected: build OK, все тесты PASS (включая `NodePowerTests` с ветки).

- [ ] **Step 3: Быстрая ручная проверка UI**

Запустить `/pbl-check` либо `dotnet run --project PBLApp`, открыть билд → вкладка Дерево: тоггл «Тепловая карта» есть, панель открывается, кнопка «Сгенерировать» работает (расчёт можно отменить, не дожидаясь конца). Трейдер («Подбор» на слоте Items) открывается.

- [ ] **Step 4: Commit merge**

```bash
git commit  # если merge остановился на конфликтах; иначе merge-коммит уже создан
```

---

### Task 2: Engine — `GetPowerNodeList` + корректные шаги

Root-cause текущих неверных шагов (зафиксировать в commit message): v1 эмитил для **взятых** нод `#node.depends` (это число зависящих от ноды нод, на старте дерева — десятки, отсюда мусорные «шаги»), для кластерных — захардкоженную `1`, и не отличал недостижимые ноды (`#node.path == 0`) от достижимых за 1 шаг. Семантика v2: `Steps` = `#node.path` (число невзятых нод в пути, **включая саму ноду**) — ровно то, что показывает ховер (`GetNodeHoverInfo` → `pathLen`); для взятых, кластерных и недостижимых — `null`.

**Files:**
- Modify: `PBLEngine/NodePower.cs` (добавить `PowerNodeInfo`)
- Modify: `PBLEngine/LuaHost.cs` (новый метод рядом с `BuildNodePower`)
- Test: `PBLEngine.Tests/NodePowerTests.cs`

**Interfaces:**
- Produces:
  - `record PowerNodeInfo(int Id, string ModKey, string Name, string Type, bool Alloc, bool IsCluster, int? Steps)`
  - `LuaHost.GetPowerNodeList() : IReadOnlyList<PowerNodeInfo>` — отфильтрованный список нод для расчёта: типы Normal/Notable/Keystone, не-асцендентные, `modKey ~= ''`, не granted, не скрытые залоченной асцендой; плюс невзятые кластерные нотабли.

- [ ] **Step 1: Failing test**

Добавить в `PBLEngine.Tests/NodePowerTests.cs` (класс уже грузит фикстурный билд в конструкторе):

```csharp
[Fact]
public void GetPowerNodeList_StepsMatchHoverPathLen()
{
    var list = _host.GetPowerNodeList();
    Assert.NotEmpty(list);

    // Взятые и кластерные — без шагов.
    Assert.All(list.Where(n => n.Alloc || n.IsCluster), n => Assert.Null(n.Steps));

    // Для выборки достижимых невзятых нод Steps == pathLen из ховера
    // (ховер — независимый, визуально проверенный источник длины пути).
    var sample = list.Where(n => !n.Alloc && !n.IsCluster && n.Steps is > 0)
                     .OrderBy(n => n.Id).Take(20).ToList();
    Assert.NotEmpty(sample);
    foreach (var n in sample)
    {
        var hover = _host.GetNodeHoverInfo(n.Id);
        Assert.NotNull(hover);
        Assert.Equal(hover!.PathLen, n.Steps);
    }
}

[Fact]
public void GetPowerNodeList_FiltersAscendancyAndEmptyModKey()
{
    var list = _host.GetPowerNodeList();
    Assert.All(list, n => Assert.NotEqual("", n.ModKey));
    Assert.All(list, n => Assert.Contains(n.Type, new[] { "Normal", "Notable", "Keystone" }));
}
```

(Проверить имя свойства длины пути в `NodeHoverInfo` — в v1 это седьмое поле `pathLen`; если свойство называется иначе, использовать его.)

- [ ] **Step 2: Убедиться, что тест падает**

```powershell
dotnet test PBLEngine.Tests --filter GetPowerNodeList
```
Expected: FAIL — `GetPowerNodeList` не существует.

- [ ] **Step 3: DTO + реализация**

В `PBLEngine/NodePower.cs`:

```csharp
/// <summary>One candidate node for the power calc. <see cref="Steps"/> is the
/// number of points to spend to take the node (unallocated path incl. itself);
/// null for allocated / cluster / unreachable nodes.</summary>
public record PowerNodeInfo(
    int Id, string ModKey, string Name, string Type,
    bool Alloc, bool IsCluster, int? Steps);
```

В `PBLEngine/LuaHost.cs` (рядом с `GetPowerStatList`):

```csharp
/// <summary>Node candidates for the power calc, with correct "points to take"
/// steps. Mirrors PowerBuilder's eligibility filter (types, modKey, granted,
/// locked-ascendancy unlockConstraint) + unallocated cluster notables.</summary>
public IReadOnlyList<PowerNodeInfo> GetPowerNodeList()
{
    var raw = State.DoString(@"
        if not (build and build.spec) then return '' end
        if build.spec._fastAllocDirty then
            build.spec:BuildAllDependsAndPaths()
            build.spec._fastAllocDirty = false
        end
        -- granted-мапа доступна только после BuildOutput; создаём при необходимости
        local ct = build.calcsTab
        if not (ct.mainEnv and ct.mainEnv.grantedPassives) then ct:BuildOutput() end
        local granted = (ct.mainEnv and ct.mainEnv.grantedPassives) or {}
        local rows = {}
        local function emit(node, isCluster)
            local steps = -1
            if not node.alloc and not isCluster then
                local p = node.path and #node.path or 0
                if p > 0 then steps = p end
            end
            rows[#rows+1] = table.concat({
                node.id or 0,
                node.modKey or '',
                (node.dn or node.name or ''):gsub('[\t\31]', ' '),
                node.type or 'Normal',
                node.alloc and 1 or 0,
                isCluster and 1 or 0,
                steps
            }, '\t')
        end
        for nodeId, node in pairs(build.spec.nodes) do
            if (node.type == 'Normal' or node.type == 'Notable' or node.type == 'Keystone')
               and not node.ascendancyName
               and node.modKey ~= '' and not granted[nodeId] then
                local hidden = false
                if node.unlockConstraint then
                    for _, unlockId in ipairs(node.unlockConstraint.nodes) do
                        local un = build.spec.nodes[unlockId]
                        if un and un.ascendancyName and not un.alloc then hidden = true break end
                    end
                end
                if not hidden then emit(node, false) end
            end
        end
        for _, node in pairs(build.spec.tree.clusterNodeMap or {}) do
            if not node.alloc and node.modKey ~= '' and not granted[node.id]
               and (node.type == 'Normal' or node.type == 'Notable' or node.type == 'Keystone') then
                emit(node, true)
            end
        end
        return table.concat(rows, '\31')
    ");
    var list = new List<PowerNodeInfo>();
    var blob = raw is { Length: > 0 } ? raw[0] as string ?? "" : "";
    foreach (var row in blob.Split('\x1F', StringSplitOptions.RemoveEmptyEntries))
    {
        var f = row.Split('\t');
        if (f.Length < 7) continue;
        int steps = int.TryParse(f[6], out var s) ? s : -1;
        list.Add(new PowerNodeInfo(
            int.TryParse(f[0], out var id) ? id : 0,
            f[1], f[2], f[3], f[4] == "1", f[5] == "1",
            steps > 0 ? steps : null));
    }
    return list;
}
```

- [ ] **Step 4: Тесты зелёные**

```powershell
dotnet test PBLEngine.Tests --filter GetPowerNodeList
```
Expected: PASS (оба новых).

- [ ] **Step 5: Commit**

```bash
git add PBLEngine PBLEngine.Tests
git commit -m "fix(engine): GetPowerNodeList — steps = points-to-take (#node.path); alloc/cluster/unreachable = null

v1 показывал для взятых нод #node.depends (десятки 'шагов'), для кластерных — 1."
```

---

### Task 3: Engine — сессия расчёта power (Begin / ComputeBatch / ComputePathBatch / End)

API, которое выполняется **на любом** хосте (воркер или главный при фолбэке). Формулы и кэш по `modKey` — 1-в-1 из `CalcsTabClass:PowerBuilder` (`src/Classes/CalcsTab.lua:521`).

**Files:**
- Modify: `PBLEngine/NodePower.cs` (v2-формы `NodePowerEntry`)
- Modify: `PBLEngine/LuaHost.cs`
- Modify: `PBLApp.Core/TreeTabViewModel.cs` (только компиляционная адаптация к новым типам DTO)
- Test: `PBLEngine.Tests/NodePowerTests.cs`

**Interfaces:**
- Consumes: `PowerNodeInfo` из Task 2.
- Produces:
  - `record PowerBatchRow(int Id, double Power, double Offence, double Defence, string PowerStr)`
  - `record PathPowerRow(int Id, double PathPower, string PerPointStr)`
  - `LuaHost.BeginPowerSession(string? statKey)` — билд уже загружен; `BuildOutput`, выбор `powerStat`, `GetMiscCalculator`, кэш и id→node-мапа в глобале `_pblPS`.
  - `LuaHost.ComputePowerBatch(IReadOnlyList<int> ids) : List<PowerBatchRow>`
  - `LuaHost.ComputePathPowerBatch(IReadOnlyList<int> ids) : List<PathPowerRow>`
  - `LuaHost.EndPowerSession()` — зачистка `_pblPS`.
  - `NodePowerEntry` v2: `record NodePowerEntry(int Id, string Name, string Type, bool Alloc, int? Steps, double Power, double? PathPower, double Offence, double Defence, string PowerStr, string? PerPointStr)` (было: `int PathDist`, ненуллабельные `PathPower`/`PerPointStr`).

- [ ] **Step 1: Failing test — сессия на одном хосте сходится с PoB-корутиной (паритет)**

```csharp
[Fact]
public void PowerSession_MatchesBuildNodePower_OnSmallDepth()
{
    // Эталон: оригинальная PoB-корутина, ограниченная глубиной 3 (быстро).
    var reference = _host.BuildNodePower("FullDPS", maxDepth: 3);
    var refById = reference.Entries.Where(e => !e.Alloc && e.Power != 0)
                                   .ToDictionary(e => e.Id, e => e.Power);
    Assert.NotEmpty(refById);

    // Кандидаты: те же ноды через session-API.
    var ids = refById.Keys.OrderBy(i => i).Take(40).ToList();
    _host.BeginPowerSession("FullDPS");
    try
    {
        var rows = _host.ComputePowerBatch(ids);
        Assert.Equal(ids.Count, rows.Count);
        foreach (var r in rows)
        {
            var expected = refById[r.Id];
            Assert.True(Math.Abs(r.Power - expected) <= Math.Abs(expected) * 1e-6 + 1e-9,
                $"node {r.Id}: session={r.Power} reference={expected}");
        }
    }
    finally { _host.EndPowerSession(); }
}

[Fact]
public void PowerSession_DoesNotPerturbMainOutput()
{
    // Спека: расчёт не должен менять статы билда. Снимок до/после сессии.
    var before = _host.GetStat("TotalDPS");
    _host.BeginPowerSession("FullDPS");
    try
    {
        var ids = _host.GetPowerNodeList().Where(n => !n.Alloc && n.Steps is > 0)
                       .Take(10).Select(n => n.Id).ToList();
        _host.ComputePowerBatch(ids);
    }
    finally { _host.EndPowerSession(); }
    _host.RecalcStats();
    Assert.Equal(before, _host.GetStat("TotalDPS"));
}

[Fact]
public void PowerSession_PathBatch_ReturnsPathPowerForMultiStepNodes()
{
    var nodes = _host.GetPowerNodeList()
        .Where(n => !n.Alloc && !n.IsCluster && n.Steps is > 1).Take(5).ToList();
    Assert.NotEmpty(nodes);
    _host.BeginPowerSession("FullDPS");
    try
    {
        _host.ComputePowerBatch(nodes.Select(n => n.Id).ToList());
        var rows = _host.ComputePathPowerBatch(nodes.Select(n => n.Id).ToList());
        Assert.Equal(nodes.Count, rows.Count);
        Assert.All(rows, r => Assert.False(string.IsNullOrEmpty(r.PerPointStr)));
    }
    finally { _host.EndPowerSession(); }
}
```

- [ ] **Step 2: Убедиться, что тесты падают**

```powershell
dotnet test PBLEngine.Tests --filter PowerSession
```
Expected: FAIL — методы не существуют.

- [ ] **Step 3: Реализация session-API**

В `PBLEngine/NodePower.cs` добавить:

```csharp
/// <summary>Raw per-node result of one power batch (unformatted deltas +
/// PoB-formatted display string).</summary>
public record PowerBatchRow(int Id, double Power, double Offence, double Defence, string PowerStr);

/// <summary>Deferred path-power result (lazy top-K phase).</summary>
public record PathPowerRow(int Id, double PathPower, string PerPointStr);
```

и заменить `NodePowerEntry` на v2-форму (см. Interfaces). В `LuaHost.cs`:

```csharp
/// <summary>Prepares this host for ComputePowerBatch calls: BuildOutput,
/// powerStat selection, misc calculator + modKey cache, id→node map.
/// The host must already have the target build loaded.</summary>
public void BeginPowerSession(string? statKey)
{
    State["_pblStatKey"] = statKey;
    State.DoString(@"
        if build.spec._fastAllocDirty then
            build.spec:BuildAllDependsAndPaths()
            build.spec._fastAllocDirty = false
        end
        local ct = build.calcsTab
        ct:BuildOutput()
        local sel
        if _pblStatKey == nil or _pblStatKey == '' then
            for _, s in ipairs(data.powerStatList) do if s.combinedOffDef then sel = s break end end
        else
            for _, s in ipairs(data.powerStatList) do if s.stat == _pblStatKey then sel = s break end end
        end
        ct.powerStat = sel
        local calcFunc, calcBase = ct:GetMiscCalculator()
        local displayStat = { fmt = '.1f' }
        if sel and sel.stat then
            for _, ds in ipairs(build.displayStats) do
                if ds.stat == sel.stat then displayStat = ds break end
            end
        end
        local byId = {}
        for id, node in pairs(build.spec.nodes) do byId[id] = node end
        for _, node in pairs(build.spec.tree.clusterNodeMap or {}) do
            if node.id then byId[node.id] = node end
        end
        _pblPS = {
            ct = ct, sel = sel, calcFunc = calcFunc, calcBase = calcBase,
            cache = {}, byId = byId,
            useFullDPS = (sel and sel.stat == 'FullDPS') or false,
            scale = (displayStat.pc or displayStat.mod) and 100 or 1,
            fmt = displayStat.fmt or '.1f',
        }
        _pblPS.fmtNum = function(v)
            if v ~= 0 and math.abs(v) < 1 then return string.format('%.3g', v) end
            local s = string.format('%' .. _pblPS.fmt, v)
            if formatNumSep then s = formatNumSep(s) end
            return s
        end
    ");
    State["_pblStatKey"] = null;
}

/// <summary>Computes single-node power deltas for the given node ids.
/// Allocated nodes get the removal delta (single-stat mode only, like PoB).</summary>
public List<PowerBatchRow> ComputePowerBatch(IReadOnlyList<int> ids)
{
    State["_pblIds"] = string.Join(",", ids);
    var res = State.DoString(@"
        local ps = _pblPS
        local rows = {}
        for idStr in _pblIds:gmatch('[^,]+') do
            local node = ps.byId[tonumber(idStr)]
            if node then
                local power, off, def = 0, 0, 0
                if not node.alloc then
                    local key = node.modKey
                    if not ps.cache[key] then
                        ps.cache[key] = ps.calcFunc({ addNodes = { [node] = true } }, ps.useFullDPS)
                    end
                    local out = ps.cache[key]
                    if ps.sel and ps.sel.stat then
                        power = ps.ct:CalculatePowerStat(ps.sel, out, ps.calcBase)
                    else
                        off, def = ps.ct:CalculateCombinedOffDefStat(out, ps.calcBase)
                        power = off
                    end
                elseif ps.sel and ps.sel.stat then
                    local key = node.modKey .. '_remove'
                    if not ps.cache[key] then
                        ps.cache[key] = ps.calcFunc({ removeNodes = { [node] = true } }, ps.useFullDPS)
                    end
                    power = ps.ct:CalculatePowerStat(ps.sel, ps.cache[key], ps.calcBase)
                end
                rows[#rows+1] = table.concat({
                    node.id, power, off, def, ps.fmtNum(power * ps.scale)
                }, '\t')
            end
        end
        return table.concat(rows, '\31')
    ");
    State["_pblIds"] = null;
    var list = new List<PowerBatchRow>();
    var blob = res is { Length: > 0 } ? res[0] as string ?? "" : "";
    foreach (var row in blob.Split('\x1F', StringSplitOptions.RemoveEmptyEntries))
    {
        var f = row.Split('\t');
        if (f.Length < 5) continue;
        list.Add(new PowerBatchRow(ParseI(f[0]), ParseD(f[1]), ParseD(f[2]), ParseD(f[3]), f[4]));
    }
    return list;

    static int ParseI(string s) => int.TryParse(s, out var v) ? v : 0;
    static double ParseD(string s) =>
        double.TryParse(s, System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0;
}

/// <summary>Computes whole-path power (lazy top-K phase) for unallocated
/// reachable nodes; perPoint = pathPower / steps.</summary>
public List<PathPowerRow> ComputePathPowerBatch(IReadOnlyList<int> ids)
{
    State["_pblIds"] = string.Join(",", ids);
    var res = State.DoString(@"
        local ps = _pblPS
        local rows = {}
        for idStr in _pblIds:gmatch('[^,]+') do
            local node = ps.byId[tonumber(idStr)]
            if node and not node.alloc and node.path and #node.path > 0 then
                local steps = #node.path
                local pathPower
                if steps <= 1 then
                    local out = ps.cache[node.modKey]
                              or ps.calcFunc({ addNodes = { [node] = true } }, ps.useFullDPS)
                    if ps.sel and ps.sel.stat then
                        pathPower = ps.ct:CalculatePowerStat(ps.sel, out, ps.calcBase)
                    else
                        pathPower = select(1, ps.ct:CalculateCombinedOffDefStat(out, ps.calcBase))
                    end
                else
                    local pathNodes = {}
                    for _, pn in ipairs(node.path) do pathNodes[pn] = true end
                    local out = ps.calcFunc({ addNodes = pathNodes }, ps.useFullDPS)
                    if ps.sel and ps.sel.stat then
                        pathPower = ps.ct:CalculatePowerStat(ps.sel, out, ps.calcBase)
                    else
                        pathPower = select(1, ps.ct:CalculateCombinedOffDefStat(out, ps.calcBase))
                    end
                end
                rows[#rows+1] = table.concat({
                    node.id, pathPower, ps.fmtNum(pathPower / steps * ps.scale)
                }, '\t')
            end
        end
        return table.concat(rows, '\31')
    ");
    State["_pblIds"] = null;
    var list = new List<PathPowerRow>();
    var blob = res is { Length: > 0 } ? res[0] as string ?? "" : "";
    foreach (var row in blob.Split('\x1F', StringSplitOptions.RemoveEmptyEntries))
    {
        var f = row.Split('\t');
        if (f.Length < 3) continue;
        list.Add(new PathPowerRow(
            int.TryParse(f[0], out var id) ? id : 0,
            double.TryParse(f[1], System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture, out var p) ? p : 0,
            f[2]));
    }
    return list;
}

/// <summary>Clears session globals installed by BeginPowerSession.</summary>
public void EndPowerSession() => State.DoString("_pblPS = nil");
```

- [ ] **Step 4: Компиляционная адаптация v1 к NodePowerEntry v2**

В `LuaHost.BuildNodePower` (dump-часть, эмит строк) заменить эмит pathDist на семантику v2: для alloc/cluster эмитить `-1`, для остальных `#node.path` (или `-1`, если 0); в C#-парсере `-1` → `Steps = null`, `PerPointStr = null`, `PathPower = null` для alloc/cluster. В `PBLApp.Core/TreeTabViewModel.cs` поправить обращения к переименованным членам (минимально, полноценная переделка — Task 6). В `PBLApp/Ipc/IpcServer.cs` (`TreePowerReport`, ~строка 1447) поправить сериализацию под nullable-поля.

- [ ] **Step 5: Все тесты зелёные**

```powershell
dotnet test PBLEngine.Tests
```
Expected: PASS, включая существующие `NodePowerTests` v1 (cancellation и пр.) и оба новых.

- [ ] **Step 6: Commit**

```bash
git add PBLEngine PBLEngine.Tests PBLApp.Core PBLApp
git commit -m "feat(engine): power-session API (Begin/ComputeBatch/ComputePathBatch/End) + NodePowerEntry v2 (nullable Steps/PathPower)"
```

---

### Task 4: Engine — `PowerDispatcher` + `NodePowerOrchestrator` (чистая логика, фейки)

**Files:**
- Create: `PBLEngine/PowerDispatcher.cs`
- Test: `PBLEngine.Tests/PowerDispatcherTests.cs` (обычный класс, БЕЗ LuaHost-фикстуры — только фейки)

**Interfaces:**
- Consumes: `PowerBatchRow`, `PathPowerRow`, `PowerNodeInfo`, `NodePowerEntry`/`NodePowerMax`/`NodePowerResult`.
- Produces:

```csharp
public interface IPowerWorker
{
    /// <summary>Готовит воркер к расчёту (загрузка XML при необходимости + BeginPowerSession).</summary>
    Task PrepareAsync(string buildXml, string? statKey, CancellationToken ct);
    Task<List<PowerBatchRow>> ComputeBatchAsync(int[] ids, CancellationToken ct);
    Task<List<PathPowerRow>> ComputePathBatchAsync(int[] ids, CancellationToken ct);
    /// <summary>EndPowerSession; не бросает.</summary>
    Task FinishAsync();
}

public static class NodePowerOrchestrator
{
    public const int BatchSize = 25;
    public const int PathTopK  = 100;

    /// <summary>Раздаёт расчёт по воркерам через общую очередь батчей; воркеры
    /// подключаются по мере готовности своих Task. Фаза 1 (power) → 0–80%
    /// прогресса, фаза 2 (pathPower топ-K) → 80–100%. Упавший воркер выбывает,
    /// его батч возвращается в очередь; если выбыли все — InvalidOperationException.</summary>
    public static Task<NodePowerResult> RunAsync(
        string buildXml,
        string? statKey,
        bool offDefMode,
        IReadOnlyList<PowerNodeInfo> nodes,
        IReadOnlyList<Task<IPowerWorker>> workerTasks,
        Action<int>? onProgress,
        CancellationToken ct);
}
```

Ключевые правила реализации `RunAsync`:
- Батчи: ноды сортируются по `ModKey` (группы одинаковых малых нод попадают в один батч → кэш по modKey работает), режутся по `BatchSize` в `ConcurrentQueue<int[]>`.
- На каждый `workerTask` — задача-потребитель: `await workerTask` → `PrepareAsync` → цикл `TryDequeue` → `ComputeBatchAsync`; исключение потребителя = вернуть недосчитанный батч в очередь и выйти. `Task.WhenAll` потребителей; если очередь непуста и живых нет — `InvalidOperationException("all workers failed")`.
- Отмена: `ct` пробрасывается в дежурные вызовы; после отмены вернуть пустой результат (`Entries = []`), как v1.
- Слияние: `Power`/`Offence`/`Defence` из строк; `NodePowerMax` = max по невзятым не-кластерным нодам (`Steps != null`) каждого канала (min 0).
- Фаза 2: топ-`PathTopK` невзятых достижимых нод по `|Power|` → батчи по 10 → `ComputePathBatchAsync` теми же живыми потребителями; строки без результата фазы 2 получают `PathPower = null`, `PerPointStr = null`. Для нод со `Steps == 1` фаза 2 не нужна: `PathPower = Power`, `PerPointStr = PowerStr`.
- Прогресс: `done/total` нод → 0–80; фаза 2 → 80–100; финально 100.
- `FinishAsync` для всех подготовленных воркеров в `finally`.

- [ ] **Step 1: Failing tests (фейковый воркер)**

`PBLEngine.Tests/PowerDispatcherTests.cs`:

```csharp
using PBLEngine;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace PBLEngine.Tests;

public class PowerDispatcherTests
{
    private sealed class FakeWorker : IPowerWorker
    {
        public int Prepared, Batches;
        public bool FailOnBatch;
        public int DelayMs;
        public Task PrepareAsync(string xml, string? stat, CancellationToken ct)
        { Prepared++; return Task.CompletedTask; }
        public async Task<List<PowerBatchRow>> ComputeBatchAsync(int[] ids, CancellationToken ct)
        {
            if (FailOnBatch) throw new InvalidOperationException("boom");
            if (DelayMs > 0) await Task.Delay(DelayMs, ct);
            Batches++;
            return ids.Select(i => new PowerBatchRow(i, i * 0.5, 0, 0, (i * 0.5).ToString())).ToList();
        }
        public Task<List<PathPowerRow>> ComputePathBatchAsync(int[] ids, CancellationToken ct)
            => Task.FromResult(ids.Select(i => new PathPowerRow(i, i * 0.4, "pp" + i)).ToList());
        public Task FinishAsync() => Task.CompletedTask;
    }

    private static List<PowerNodeInfo> Nodes(int n) =>
        Enumerable.Range(1, n).Select(i =>
            new PowerNodeInfo(i, "mk" + (i % 7), "N" + i, "Normal", false, false, 1 + i % 5)).ToList();

    [Fact]
    public async Task RunAsync_AllNodesComputed_AcrossWorkers()
    {
        var w1 = new FakeWorker(); var w2 = new FakeWorker();
        var result = await NodePowerOrchestrator.RunAsync(
            "<xml/>", "FullDPS", false, Nodes(120),
            new[] { Task.FromResult<IPowerWorker>(w1), Task.FromResult<IPowerWorker>(w2) },
            null, CancellationToken.None);
        Assert.Equal(120, result.Entries.Count);
        Assert.Equal(1, w1.Prepared); Assert.Equal(1, w2.Prepared);
        Assert.True(w1.Batches > 0 && w2.Batches > 0, "оба воркера получили батчи");
    }

    [Fact]
    public async Task RunAsync_LateWorkerJoins()
    {
        var w1 = new FakeWorker { DelayMs = 30 };
        var late = new FakeWorker();
        var lateTask = Task.Delay(100).ContinueWith(_ => (IPowerWorker)late);
        var result = await NodePowerOrchestrator.RunAsync(
            "<xml/>", null, true, Nodes(200),
            new[] { Task.FromResult<IPowerWorker>(w1), lateTask },
            null, CancellationToken.None);
        Assert.Equal(200, result.Entries.Count);
        Assert.True(late.Batches > 0, "поздний воркер подключился к очереди");
    }

    [Fact]
    public async Task RunAsync_FailedWorker_BatchRequeued_OthersFinish()
    {
        var bad = new FakeWorker { FailOnBatch = true };
        var good = new FakeWorker();
        var result = await NodePowerOrchestrator.RunAsync(
            "<xml/>", "FullDPS", false, Nodes(100),
            new[] { Task.FromResult<IPowerWorker>(bad), Task.FromResult<IPowerWorker>(good) },
            null, CancellationToken.None);
        Assert.Equal(100, result.Entries.Count);   // ничего не потеряно
    }

    [Fact]
    public async Task RunAsync_AllWorkersFail_Throws()
    {
        var bad = new FakeWorker { FailOnBatch = true };
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            NodePowerOrchestrator.RunAsync("<xml/>", "FullDPS", false, Nodes(10),
                new[] { Task.FromResult<IPowerWorker>(bad) }, null, CancellationToken.None));
    }

    [Fact]
    public async Task RunAsync_Cancellation_ReturnsEmpty()
    {
        using var cts = new CancellationTokenSource();
        var slow = new FakeWorker { DelayMs = 50 };
        var run = NodePowerOrchestrator.RunAsync("<xml/>", "FullDPS", false, Nodes(500),
            new[] { Task.FromResult<IPowerWorker>(slow) }, null, cts.Token);
        cts.CancelAfter(60);
        var result = await run;
        Assert.Empty(result.Entries);
    }

    [Fact]
    public async Task RunAsync_TopK_GetsPathPower_StepsOneReusesPower()
    {
        var w = new FakeWorker();
        var result = await NodePowerOrchestrator.RunAsync(
            "<xml/>", "FullDPS", false, Nodes(150),
            new[] { Task.FromResult<IPowerWorker>(w) }, null, CancellationToken.None);
        var steps1 = result.Entries.Where(e => e.Steps == 1).ToList();
        Assert.All(steps1, e => Assert.Equal(e.Power, e.PathPower));
        var topWithPath = result.Entries.Where(e => e.PathPower != null).Count();
        Assert.True(topWithPath >= Math.Min(100, 150), $"топ-K досчитан (got {topWithPath})");
    }
}
```

- [ ] **Step 2: Убедиться, что не компилируется/падает**

```powershell
dotnet test PBLEngine.Tests --filter PowerDispatcher
```
Expected: FAIL — `IPowerWorker`/`NodePowerOrchestrator` не существуют.

- [ ] **Step 3: Реализация `PBLEngine/PowerDispatcher.cs`**

```csharp
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PBLEngine;

public interface IPowerWorker
{
    Task PrepareAsync(string buildXml, string? statKey, CancellationToken ct);
    Task<List<PowerBatchRow>> ComputeBatchAsync(int[] ids, CancellationToken ct);
    Task<List<PathPowerRow>> ComputePathBatchAsync(int[] ids, CancellationToken ct);
    Task FinishAsync();
}

public static class NodePowerOrchestrator
{
    public const int BatchSize     = 25;
    public const int PathBatchSize = 10;
    public const int PathTopK      = 100;

    public static async Task<NodePowerResult> RunAsync(
        string buildXml, string? statKey, bool offDefMode,
        IReadOnlyList<PowerNodeInfo> nodes,
        IReadOnlyList<Task<IPowerWorker>> workerTasks,
        Action<int>? onProgress, CancellationToken ct)
    {
        var byId = nodes.ToDictionary(n => n.Id);
        // Группы одинаковых modKey — в один батч, чтобы работал кэш воркера.
        var ordered = nodes.OrderBy(n => n.ModKey, StringComparer.Ordinal).Select(n => n.Id).ToList();
        var queue = new ConcurrentQueue<int[]>(Chunk(ordered, BatchSize));

        int total = nodes.Count, done = 0;
        var rows = new ConcurrentDictionary<int, PowerBatchRow>();
        var prepared = new ConcurrentBag<IPowerWorker>();

        async Task Consume(Task<IPowerWorker> wt, ConcurrentQueue<int[]> q,
                           Func<IPowerWorker, int[], Task<int>> work)
        {
            IPowerWorker w;
            try { w = await wt.WaitAsync(ct); } catch { return; }
            try
            {
                if (!prepared.Contains(w))
                {
                    await w.PrepareAsync(buildXml, statKey, ct);
                    prepared.Add(w);
                }
                while (!ct.IsCancellationRequested && q.TryDequeue(out var batch))
                {
                    try
                    {
                        int n = await work(w, batch);
                        int d = Interlocked.Add(ref done, n);
                        onProgress?.Invoke(Math.Min(80, (int)(d * 80.0 / Math.Max(1, total))));
                    }
                    catch (OperationCanceledException) { q.Enqueue(batch); return; }
                    catch { q.Enqueue(batch); return; }   // воркер мёртв — батч назад, выходим
                }
            }
            catch { /* Prepare умер — воркер выбывает */ }
        }

        try
        {
            // Фаза 1: power.
            await Task.WhenAll(workerTasks.Select(wt => Consume(wt, queue, async (w, batch) =>
            {
                foreach (var r in await w.ComputeBatchAsync(batch, ct)) rows[r.Id] = r;
                return batch.Length;
            })));
            if (ct.IsCancellationRequested)
                return new NodePowerResult(offDefMode, new NodePowerMax(0, 0, 0), []);
            if (!queue.IsEmpty)
                throw new InvalidOperationException("all workers failed");

            // Фаза 2: pathPower для топ-K достижимых невзятых нод c Steps > 1.
            var top = rows.Values
                .Where(r => byId[r.Id] is { Alloc: false, IsCluster: false, Steps: > 1 })
                .OrderByDescending(r => Math.Abs(r.Power))
                .Take(PathTopK).Select(r => r.Id).ToList();
            var pathRows = new ConcurrentDictionary<int, PathPowerRow>();
            if (top.Count > 0)
            {
                var pathQueue = new ConcurrentQueue<int[]>(Chunk(top, PathBatchSize));
                done = 0; total = top.Count;
                await Task.WhenAll(workerTasks.Select(wt => Consume(wt, pathQueue, async (w, batch) =>
                {
                    foreach (var r in await w.ComputePathBatchAsync(batch, ct)) pathRows[r.Id] = r;
                    return batch.Length;
                })));
                if (ct.IsCancellationRequested)
                    return new NodePowerResult(offDefMode, new NodePowerMax(0, 0, 0), []);
                if (!pathQueue.IsEmpty)
                    throw new InvalidOperationException("all workers failed");
            }
            onProgress?.Invoke(100);

            // Слияние.
            double maxS = 0, maxO = 0, maxD = 0;
            var entries = new List<NodePowerEntry>(rows.Count);
            foreach (var n in nodes)
            {
                if (!rows.TryGetValue(n.Id, out var r)) continue;
                if (n is { Alloc: false, IsCluster: false, Steps: not null })
                {
                    maxS = Math.Max(maxS, r.Power);
                    maxO = Math.Max(maxO, r.Offence);
                    maxD = Math.Max(maxD, r.Defence);
                }
                double? pathPower = null; string? perPointStr = null;
                if (n is { Alloc: false, IsCluster: false, Steps: 1 })
                { pathPower = r.Power; perPointStr = r.PowerStr; }
                else if (pathRows.TryGetValue(n.Id, out var pr))
                { pathPower = pr.PathPower; perPointStr = pr.PerPointStr; }
                entries.Add(new NodePowerEntry(n.Id, n.Name, n.Type, n.Alloc, n.Steps,
                    r.Power, pathPower, r.Offence, r.Defence, r.PowerStr, perPointStr));
            }
            return new NodePowerResult(offDefMode, new NodePowerMax(maxS, maxO, maxD), entries);
        }
        finally
        {
            foreach (var w in prepared)
                try { await w.FinishAsync(); } catch { }
        }
    }

    private static IEnumerable<int[]> Chunk(List<int> ids, int size)
    {
        for (int i = 0; i < ids.Count; i += size)
            yield return ids.Skip(i).Take(size).ToArray();
    }
}
```

Примечание: `prepared.Contains(w)` на `ConcurrentBag` — O(n) при n ≤ 8, приемлемо; воркер попадает в один `Consume` на фазу, повторный `PrepareAsync` между фазами не выполняется, т.к. воркер уже в `prepared`.

- [ ] **Step 4: Тесты зелёные**

```powershell
dotnet test PBLEngine.Tests --filter PowerDispatcher
```
Expected: 6 PASS.

- [ ] **Step 5: Commit**

```bash
git add PBLEngine PBLEngine.Tests
git commit -m "feat(engine): NodePowerOrchestrator — batch queue over IPowerWorker, late-join, requeue-on-failure, lazy top-K pathPower"
```

---

### Task 5: Engine — `LuaWorkerPool` (реальные воркеры) + интеграционный паритет-тест

**Files:**
- Create: `PBLEngine/LuaWorkerPool.cs`
- Test: `PBLEngine.Tests/LuaWorkerPoolTests.cs`

**Interfaces:**
- Consumes: `IPowerWorker`, session-API из Task 3.
- Produces:

```csharp
/// <summary>Пул фоновых LuaHost-воркеров для параллельного расчёта power.
/// Создание дешёвое; реальные хосты поднимаются лениво из EnsureStarted().</summary>
public sealed class LuaWorkerPool : IDisposable
{
    public LuaWorkerPool(string repoRoot, int size);
    public int Size { get; }
    /// <summary>Число воркеров, завершивших Initialize.</summary>
    public int Ready { get; }
    public event Action? ReadyChanged;
    /// <summary>Идемпотентно стартует инициализацию воркеров (стаггер 3 с между
    /// стартами) и возвращает их Task — вход для NodePowerOrchestrator.
    /// Мёртвые воркеры заменяются новыми Task.</summary>
    public IReadOnlyList<Task<IPowerWorker>> EnsureStarted();
    public void Dispose();   // Dispose всех хостов, отмена не начатых инициализаций
}
```

Внутренний `LuaWorker : IPowerWorker` — обёртка над своим `LuaHost`:
- `SemaphoreSlim(1,1)` вокруг каждого обращения к state (один state — один вызов за раз);
- `PrepareAsync` = `LoadBuildFromXml(xml, "PowerWorker")` + `BeginPowerSession(statKey)`;
- `ComputeBatchAsync`/`ComputePathBatchAsync` = `Task.Run` соответствующих методов под семафором;
- `FinishAsync` = `EndPowerSession()`, исключения глотаются;
- первое же исключение NLua помечает воркер мёртвым (`IsDead = true`); пул при следующем `EnsureStarted()` заменяет мёртвых новыми.

- [ ] **Step 1: Failing test**

```csharp
using PBLEngine;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace PBLEngine.Tests;

[Collection("LuaHost")]
public class LuaWorkerPoolTests : IClassFixture<LuaHostFixture>
{
    private readonly LuaHost _host;
    public LuaWorkerPoolTests(LuaHostFixture fixture)
    {
        _host = fixture.Host;
        var buildPath = Path.Combine(FindRepoRoot(), "tools", "parity", "community_builds",
            "CqX3fXBg_S0q7p54SFGHC.xml");
        _host.LoadBuildFromXml(File.ReadAllText(buildPath), "PoolTest");
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "src"))) return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("no src/ ancestor");
    }

    [Fact]
    public async Task Pool_ParityWithReferenceCoroutine()
    {
        // Эталон: PoB-корутина на главном хосте, глубина 3 (десятки нод — быстро).
        var reference = _host.BuildNodePower("FullDPS", maxDepth: 3);
        var refById = reference.Entries.Where(e => !e.Alloc && e.Power != 0)
                                       .ToDictionary(e => e.Id, e => e.Power);
        Assert.NotEmpty(refById);

        var xml = _host.SaveBuildToXml();
        Assert.False(string.IsNullOrEmpty(xml));
        var nodes = _host.GetPowerNodeList().Where(n => refById.ContainsKey(n.Id)).ToList();

        using var pool = new LuaWorkerPool(FindRepoRoot(), size: 1);
        var result = await NodePowerOrchestrator.RunAsync(
            xml!, "FullDPS", false, nodes, pool.EnsureStarted(), null, CancellationToken.None);

        Assert.Equal(nodes.Count, result.Entries.Count);
        foreach (var e in result.Entries)
        {
            var expected = refById[e.Id];
            Assert.True(Math.Abs(e.Power - expected) <= Math.Abs(expected) * 1e-6 + 1e-9,
                $"node {e.Id} ({e.Name}): pool={e.Power} reference={expected}");
        }
        Assert.True(pool.Ready >= 1);
    }
}
```

- [ ] **Step 2: Убедиться, что падает** (`dotnet test PBLEngine.Tests --filter LuaWorkerPool`) — FAIL, класса нет.

- [ ] **Step 3: Реализация `PBLEngine/LuaWorkerPool.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PBLEngine;

/// <summary>Пул фоновых LuaHost-воркеров. Дешёвый в создании; хосты поднимаются
/// лениво из EnsureStarted() со стаггером 3 с (инициализация одного ~40 с).</summary>
public sealed class LuaWorkerPool : IDisposable
{
    private readonly string _repoRoot;
    private readonly object _gate = new();
    private readonly CancellationTokenSource _disposeCts = new();
    private List<(Task<IPowerWorker> Task, LuaWorker? Worker)>? _slots;
    private int _ready;

    public LuaWorkerPool(string repoRoot, int size) { _repoRoot = repoRoot; Size = size; }

    public int Size { get; }
    public int Ready => Volatile.Read(ref _ready);
    public event Action? ReadyChanged;

    public IReadOnlyList<Task<IPowerWorker>> EnsureStarted()
    {
        lock (_gate)
        {
            _slots ??= Enumerable.Range(0, Size).Select(i => Spawn(i * 3000)).ToList();
            // Замена мёртвых воркеров (стаггер не нужен — это редкая штучная замена).
            for (int i = 0; i < _slots.Count; i++)
                if (_slots[i].Worker is { IsDead: true })
                {
                    Interlocked.Decrement(ref _ready);
                    _slots[i] = Spawn(0);
                }
            return _slots.Select(s => s.Task).ToList();
        }
    }

    private (Task<IPowerWorker>, LuaWorker?) Spawn(int delayMs)
    {
        LuaWorker? created = null;
        var task = Task.Run(async () =>
        {
            if (delayMs > 0) await Task.Delay(delayMs, _disposeCts.Token);
            var host = new LuaHost();
            host.Initialize(_repoRoot);
            created = new LuaWorker(host);
            Interlocked.Increment(ref _ready);
            ReadyChanged?.Invoke();
            return (IPowerWorker)created;
        }, _disposeCts.Token);
        var slot = (task, created);
        // Привязать созданный worker к слоту после завершения (для поиска мёртвых).
        task.ContinueWith(t =>
        {
            if (t.Status != TaskStatus.RanToCompletion) return;
            lock (_gate)
            {
                if (_slots == null) return;
                var i = _slots.FindIndex(s => ReferenceEquals(s.Task, t));
                if (i >= 0) _slots[i] = (t, (LuaWorker)t.Result);
            }
        }, TaskScheduler.Default);
        return slot;
    }

    public void Dispose()
    {
        _disposeCts.Cancel();
        lock (_gate)
        {
            if (_slots == null) return;
            foreach (var (task, worker) in _slots)
            {
                worker?.Dispose();
                if (worker == null && task.Status == TaskStatus.RanToCompletion)
                    ((LuaWorker)task.Result).Dispose();
            }
            _slots = null;
        }
    }
}

/// <summary>IPowerWorker поверх собственного LuaHost: один state — один вызов
/// за раз (семафор); первое исключение NLua помечает воркер мёртвым.</summary>
internal sealed class LuaWorker : IPowerWorker, IDisposable
{
    private readonly LuaHost _host;
    private readonly SemaphoreSlim _lock = new(1, 1);
    public bool IsDead { get; private set; }

    public LuaWorker(LuaHost host) => _host = host;

    private async Task<T> Run<T>(Func<T> f, CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try { return await Task.Run(f, ct); }
        catch (OperationCanceledException) { throw; }
        catch { IsDead = true; throw; }
        finally { _lock.Release(); }
    }

    public Task PrepareAsync(string buildXml, string? statKey, CancellationToken ct)
        => Run<object?>(() =>
        {
            _host.LoadBuildFromXml(buildXml, "PowerWorker");
            _host.BeginPowerSession(statKey);
            return null;
        }, ct);

    public Task<List<PowerBatchRow>> ComputeBatchAsync(int[] ids, CancellationToken ct)
        => Run(() => _host.ComputePowerBatch(ids), ct);

    public Task<List<PathPowerRow>> ComputePathBatchAsync(int[] ids, CancellationToken ct)
        => Run(() => _host.ComputePathPowerBatch(ids), ct);

    public async Task FinishAsync()
    {
        if (IsDead) return;
        try { await Run<object?>(() => { _host.EndPowerSession(); return null; }, CancellationToken.None); }
        catch { /* best-effort */ }
    }

    public void Dispose() { try { _host.Dispose(); } catch { } _lock.Dispose(); }
}
```

Упрощение при реализации приветствуется (например, слоты как `class WorkerSlot` вместо кортежей), но контракт `EnsureStarted`/`Ready`/`ReadyChanged`/`Dispose` и правило «мёртвый воркер заменяется при следующем EnsureStarted» — обязательны.

- [ ] **Step 4: Тест зелёный** (займёт ~1–2 мин: инициализация воркера ~40 с + два расчёта глубины 3).

```powershell
dotnet test PBLEngine.Tests --filter LuaWorkerPool
```
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add PBLEngine PBLEngine.Tests
git commit -m "feat(engine): LuaWorkerPool — lazy staggered LuaHost workers + pool-vs-coroutine parity test"
```

---

### Task 6: Core — интеграция пула в `TreeTabViewModel`

**Files:**
- Create: `PBLApp.Core/TreePowerService.cs`
- Modify: `PBLApp.Core/TreeTabViewModel.cs` (регион «Heat map / Power Report», `BuildPowerAsync`, `NodePowerRowViewModel`)
- Modify: `PBLApp.Core/Localization/Strings.resx`, `Strings.ru.resx`

**Interfaces:**
- Consumes: `LuaWorkerPool`, `NodePowerOrchestrator`, `GetPowerNodeList`, session-API (фолбэк), `AppPreferences`.
- Produces (для View, Task 7):
  - VM-свойства: `PowerWorkersStatus : string` («воркеры: 2/4» / «прогрев воркеров: 1/4» / пусто при выключенном пуле), `PowerSortIndex : int` (0 = по приросту, 1 = за очко), `PowerFilter : string`, `WorkerCountIndex : int` (0=Авто,1=1,2=2,3=4,4=Выкл).
  - `NodePowerRowViewModel`: `+ StepsStr : string` («за 3 оч.» / пустая), `+ TypeBadge : string` (локализованный тип), `PerPointStr` может быть «—».

- [ ] **Step 1: `TreePowerService`** — процессный держатель пула (живёт между открытиями билдов):

```csharp
using System;
using PBLEngine;

namespace PBLApp.Core;

/// <summary>Process-wide holder of the power-calc worker pool. The pool is
/// created lazily on the first heat-map calculation and survives build
/// re-opens (worker init is ~40 s — never throw it away).</summary>
public static class TreePowerService
{
    private static LuaWorkerPool? _pool;
    private static readonly object Gate = new();

    /// <summary>Configured worker count: prefs key "tree.powerWorkers"
    /// ("auto" | "0".."8"), default auto = clamp(cores-2, 1, 4). 0 → пул выключен.</summary>
    public static int ConfiguredSize()
    {
        var raw = AppPreferences.Get("tree.powerWorkers");
        if (int.TryParse(raw, out var n)) return Math.Clamp(n, 0, 8);
        return Math.Clamp(Environment.ProcessorCount - 2, 1, 4);
    }

    /// <summary>Возвращает пул (создавая при первом обращении) или null при size=0.
    /// Если сохранённый размер изменился — старый пул утилизируется.</summary>
    public static LuaWorkerPool? GetPool(string repoRoot)
    {
        var size = ConfiguredSize();
        lock (Gate)
        {
            if (size == 0) { _pool?.Dispose(); _pool = null; return null; }
            if (_pool != null && _pool.Size != size) { _pool.Dispose(); _pool = null; }
            return _pool ??= new LuaWorkerPool(repoRoot, size);
        }
    }
}
```

- [ ] **Step 2: `BuildPowerAsync` v2** в `TreeTabViewModel` (заменяет тело v1; вызов `_host.BuildNodePower` уходит из VM полностью):

```csharp
private async Task BuildPowerAsync()
{
    var stat = SelectedPowerStat?.Option;
    if (stat is null || IsPowerBuilding) return;
    _powerCts?.Cancel();
    var cts = new System.Threading.CancellationTokenSource();
    _powerCts = cts;
    var ctx = System.Threading.SynchronizationContext.Current;
    void Post(Action a) { if (ctx != null) ctx.Post(_ => a(), null); else a(); }
    try
    {
        IsPowerBuilding = true;
        PowerBuildProgress = 0;

        // 1. Всё, что нужно от главного хоста, — до раздачи батчей (его Lua
        //    больше не трогаем до конца расчёта).
        string? xml = null; IReadOnlyList<PowerNodeInfo> nodes = [];
        await Task.Run(() =>
        {
            xml   = _host.SaveBuildToXml();
            nodes = _host.GetPowerNodeList();
        });
        if (string.IsNullOrEmpty(xml) || nodes.Count == 0) return;

        // 2. Воркеры: пул (лениво стартует прогрев) либо фолбэк на главный хост.
        var pool = TreePowerService.GetPool(RepoRoot);
        IReadOnlyList<Task<IPowerWorker>> workers;
        if (pool != null)
        {
            workers = pool.EnsureStarted();
            pool.ReadyChanged -= OnPoolReadyChanged;
            pool.ReadyChanged += OnPoolReadyChanged;
            Post(UpdateWorkersStatus);
        }
        else
        {
            workers = [Task.FromResult<IPowerWorker>(new MainHostPowerWorker(_host))];
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
            // Все воркеры умерли — считаем на главном хосте.
            result = await NodePowerOrchestrator.RunAsync(
                xml!, stat.StatKey, stat.CombinedOffDef, nodes,
                [Task.FromResult<IPowerWorker>(new MainHostPowerWorker(_host))],
                Progress, cts.Token);
        }

        if (cts.IsCancellationRequested || !HeatmapEnabled)
        {
            PowerOverlay = null;
            PowerOverlayChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        _lastPowerResult = result;
        PowerOverlay = result;
        RebuildPowerRows();               // применяет сортировку и фильтр
        IsPowerStale = false;
        PowerOverlayChanged?.Invoke(this, EventArgs.Empty);
    }
    catch (Exception ex)
    {
        PowerError = ex.Message;          // строка ошибки в панели (existing pattern)
    }
    finally
    {
        IsPowerBuilding = false;
        if (ReferenceEquals(_powerCts, cts)) _powerCts = null;
    }
}
```

`MainHostPowerWorker` — приватный класс VM (`IPowerWorker` поверх `_host`); `PrepareAsync` НЕ грузит XML — билд уже в хосте. Параллельные обращения к главному хосту исключены гвардом `IsPowerBuilding` (алок/деаллок и пересчёты во время расчёта блокируются существующей логикой):

```csharp
private sealed class MainHostPowerWorker(LuaHost host) : IPowerWorker
{
    public Task PrepareAsync(string buildXml, string? statKey, System.Threading.CancellationToken ct)
        => Task.Run(() => host.BeginPowerSession(statKey), ct);   // XML не грузим — источник правды
    public Task<List<PowerBatchRow>> ComputeBatchAsync(int[] ids, System.Threading.CancellationToken ct)
        => Task.Run(() => host.ComputePowerBatch(ids), ct);
    public Task<List<PathPowerRow>> ComputePathBatchAsync(int[] ids, System.Threading.CancellationToken ct)
        => Task.Run(() => host.ComputePathPowerBatch(ids), ct);
    public Task FinishAsync() => Task.Run(() => host.EndPowerSession());
}
```

- [ ] **Step 3: Сортировка, фильтр, строки**

```csharp
[ObservableProperty] private int    _powerSortIndex;   // 0 = по приросту, 1 = за очко
[ObservableProperty] private string _powerFilter = "";
[ObservableProperty] private string _powerWorkersStatus = "";
[ObservableProperty] private string? _powerError;
private NodePowerResult? _lastPowerResult;

partial void OnPowerSortIndexChanged(int value) => RebuildPowerRows();
partial void OnPowerFilterChanged(string value) => RebuildPowerRows();

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
        // «за очко»: досчитанный топ-K первым (по perPoint), остальные ниже по power
        ? src.OrderByDescending(e => e.PathPower != null)
             .ThenByDescending(e => PerPoint(e) * (lower ? -1 : 1))
        : (lower ? src.OrderBy(e => e.Power) : src.OrderByDescending(e => e.Power));
    foreach (var e in src.Take(400))
        PowerReport.Add(MakeRow(e, lower));
    OnPropertyChanged(nameof(HasPowerReport));

    static double PerPoint(NodePowerEntry e) =>
        e.PathPower is { } pp && e.Steps is > 0 ? pp / e.Steps.Value : 0;
}
```

```csharp
private NodePowerRowViewModel MakeRow(NodePowerEntry e, bool lower)
{
    bool good = lower ? e.Power < 0 : e.Power > 0;
    return new NodePowerRowViewModel
    {
        NodeId      = e.Id,
        Name        = TranslatedName(e),                       // как v1 (перевод имени ноды)
        TypeBadge   = LocalizationService.Get("Tree_Power_Type_" + e.Type),
        IsAllocated = e.Alloc,
        PowerStr    = e.PowerStr,
        PerPointStr = e.PerPointStr ?? "—",
        StepsStr    = e.Steps is { } s
                        ? string.Format(LocalizationService.Get("Tree_Power_StepsFmt"), s) : "",
        PowerColor  = good ? "#A6E3A1" : "#F38BA8",
    };
}

private void OnPoolReadyChanged() { /* маршал в UI-поток */ UpdateWorkersStatus(); }

private void UpdateWorkersStatus()
{
    var pool = TreePowerService.GetPool(RepoRoot);
    PowerWorkersStatus = pool == null ? ""
        : string.Format(LocalizationService.Get(
              pool.Ready < pool.Size && IsPowerBuilding ? "Tree_Power_Warmup" : "Tree_Power_Workers"),
          pool.Ready, pool.Size);
}
```

(`NodePowerRowViewModel` получает новые init-свойства `TypeBadge` и `StepsStr : string`; `Type`-колонку v1 заменяет бейдж.)

- [ ] **Step 4: Настройка числа воркеров** — свойство `WorkerCountIndex` (0=Авто,1=1,2=2,3=4,4=Выкл) ↔ `AppPreferences.Set("tree.powerWorkers", …)` (`"auto"/"1"/"2"/"4"/"0"`); применяется при следующем `GetPool`.

- [ ] **Step 5: Строки локализации** — добавить в `Strings.resx` / `Strings.ru.resx`:

| Key | en | ru |
|---|---|---|
| Tree_Power_StepsFmt | for {0} pt | за {0} оч. |
| Tree_Power_Warmup | warming workers: {0}/{1} | прогрев воркеров: {0}/{1} |
| Tree_Power_Workers | workers: {0}/{1} | воркеры: {0}/{1} |
| Tree_Power_SortPower | By stat gain | По приросту |
| Tree_Power_SortPerPoint | Per point | За очко |
| Tree_Power_Filter | Filter nodes… | Фильтр по имени… |
| Tree_Power_Type_Notable | Notable | Нотабль |
| Tree_Power_Type_Keystone | Keystone | Кистоун |
| Tree_Power_Type_Normal | Small | Малая |
| Tree_Power_WorkerCount | Workers | Потоки |
| Tree_Power_WorkerAuto | Auto | Авто |
| Tree_Power_WorkerOff | Off | Выкл |

- [ ] **Step 6: Сборка + тесты**

```powershell
dotnet build PBLApp; dotnet test PBLEngine.Tests
```
Expected: OK / PASS.

- [ ] **Step 7: Commit**

```bash
git add PBLApp.Core
git commit -m "feat(tree-power): pool-backed BuildPowerAsync — lazy warmup status, sort/filter rows, worker-count pref, main-host fallback"
```

---

### Task 7: UI — редизайн панели и тулбара (стиль TraderWindow)

**Files:**
- Modify: `PBLApp/Views/TreeTabView.axaml` (тулбар + панель Power Report)
- Modify: `PBLApp/Views/TreeTabView.axaml.cs` (если меняются обработчики; двойной тап по карточке остаётся)

**Interfaces:**
- Consumes: VM-свойства из Task 6.

- [ ] **Step 1: Тулбар** — от power-контролов остаётся только существующий `ToggleButton {loc:Tr Tree_Heatmap}` (строка ~87); всё остальное (если после merge в тулбаре остались стат-комбо/кнопки) удалить.

- [ ] **Step 2: Панель** — заменить содержимое power-панели (регион строк ~166–240) на:

```xml
<Border DockPanel.Dock="Right" Width="{Binding PowerPanelWidth}"
        MinWidth="260" MaxWidth="680" IsVisible="{Binding HeatmapEnabled}"
        Background="{DynamicResource BgSurfaceBrush}"
        BorderBrush="{DynamicResource BorderSubtleBrush}" BorderThickness="1,0,0,0">
  <DockPanel Margin="10">
    <!-- resize thumb: существующий Thumb + PowerPanelThumb_DragDelta без изменений -->

    <!-- Шапка -->
    <StackPanel DockPanel.Dock="Top" Spacing="8">
      <TextBlock Text="{loc:Tr Tree_PowerReport}" FontWeight="SemiBold" FontSize="15"/>
      <Grid ColumnDefinitions="*,Auto">
        <ComboBox Grid.Column="0" MaxWidth="340" HorizontalAlignment="Stretch"
                  ItemsSource="{Binding PowerStatOptions}"
                  SelectedItem="{Binding SelectedPowerStat}"/>
        <Button Grid.Column="1" Margin="8,0,0,0" Classes="Flat"
                Content="{loc:Tr Tree_Power_Generate}"
                Command="{Binding GeneratePowerCommand}"
                IsVisible="{Binding !IsPowerBuilding}"/>
        <Button Grid.Column="1" Margin="8,0,0,0"
                Content="{loc:Tr Cancel}"
                Command="{Binding CancelPowerCommand}"
                IsVisible="{Binding IsPowerBuilding}"/>
      </Grid>
      <Grid ColumnDefinitions="Auto,*,Auto" >
        <TextBlock Grid.Column="0" Text="{loc:Tr Tree_Power_WorkerCount}"
                   VerticalAlignment="Center" Opacity="0.7" FontSize="12"/>
        <ComboBox Grid.Column="1" Margin="6,0" MinWidth="90" FontSize="12"
                  SelectedIndex="{Binding WorkerCountIndex}">
          <ComboBoxItem Content="{loc:Tr Tree_Power_WorkerAuto}"/>
          <ComboBoxItem Content="1"/> <ComboBoxItem Content="2"/> <ComboBoxItem Content="4"/>
          <ComboBoxItem Content="{loc:Tr Tree_Power_WorkerOff}"/>
        </ComboBox>
        <TextBlock Grid.Column="2" Text="{Binding PowerWorkersStatus}"
                   VerticalAlignment="Center" Opacity="0.7" FontSize="12"/>
      </Grid>
      <StackPanel Orientation="Horizontal" Spacing="6" IsVisible="{Binding IsPowerBuilding}">
        <ProgressBar Minimum="0" Maximum="100" Value="{Binding PowerBuildProgress}" Width="180"/>
        <TextBlock Text="{loc:Tr Tree_Power_Building}" Opacity="0.7" FontSize="12"
                   VerticalAlignment="Center"/>
      </StackPanel>
      <Border IsVisible="{Binding IsPowerStale}" CornerRadius="6" Padding="8,5"
              Background="{DynamicResource BgRaisedBrush}"
              BorderBrush="{DynamicResource BorderSubtleBrush}" BorderThickness="1">
        <StackPanel Orientation="Horizontal" Spacing="8">
          <TextBlock Text="{Binding PowerStaleLabel}" VerticalAlignment="Center" FontSize="12"/>
          <Button Content="{loc:Tr Tree_Power_Refresh}" Classes="Flat" Padding="8,3"
                  Command="{Binding RefreshPowerCommand}"/>
        </StackPanel>
      </Border>
      <TextBlock Text="{Binding PowerError}" Foreground="#F38BA8" FontSize="12"
                 IsVisible="{Binding PowerError, Converter={x:Static ObjectConverters.IsNotNull}}"
                 TextWrapping="Wrap"/>
      <!-- Сортировка + фильтр -->
      <Grid ColumnDefinitions="Auto,*">
        <ComboBox Grid.Column="0" MinWidth="130" FontSize="12"
                  SelectedIndex="{Binding PowerSortIndex}">
          <ComboBoxItem Content="{loc:Tr Tree_Power_SortPower}"/>
          <ComboBoxItem Content="{loc:Tr Tree_Power_SortPerPoint}"/>
        </ComboBox>
        <TextBox Grid.Column="1" Margin="6,0,0,0" FontSize="12"
                 Watermark="{loc:Tr Tree_Power_Filter}"
                 Text="{Binding PowerFilter, Mode=TwoWay}"/>
      </Grid>
    </StackPanel>

    <TextBlock DockPanel.Dock="Top" Text="{Binding PowerEmptyLabel}"
               IsVisible="{Binding !HasPowerReport}" Opacity="0.6" Margin="0,10"/>

    <!-- Карточки -->
    <ListBox x:Name="PowerReportList" ItemsSource="{Binding PowerReport}"
             Background="Transparent" DoubleTapped="PowerReportList_DoubleTapped"
             ScrollViewer.HorizontalScrollBarVisibility="Disabled" Margin="0,8,0,0">
      <ListBox.ItemTemplate>
        <DataTemplate x:DataType="vm:NodePowerRowViewModel">
          <Border Background="{DynamicResource BgMantleBrush}"
                  BorderBrush="{DynamicResource BorderSubtleBrush}" BorderThickness="1"
                  CornerRadius="6" Padding="10,6" Margin="0,2">
            <StackPanel Spacing="2">
              <DockPanel>
                <Border DockPanel.Dock="Right" CornerRadius="4" Padding="6,1"
                        Background="{DynamicResource BgRaisedBrush}"
                        BorderBrush="{DynamicResource BorderSubtleBrush}" BorderThickness="1">
                  <TextBlock Text="{Binding TypeBadge}" FontSize="10" Opacity="0.8"/>
                </Border>
                <TextBlock Text="{Binding Name}" FontWeight="SemiBold" FontSize="13"
                           TextTrimming="CharacterEllipsis"/>
              </DockPanel>
              <DockPanel>
                <TextBlock DockPanel.Dock="Right" Text="{Binding StepsStr}"
                           FontSize="11" Opacity="0.65" VerticalAlignment="Center"/>
                <StackPanel Orientation="Horizontal" Spacing="10">
                  <TextBlock Text="{Binding PowerStr}" Foreground="{Binding PowerColor}"
                             FontSize="13"/>
                  <TextBlock Text="{Binding PerPointStr}" FontSize="11" Opacity="0.75"
                             VerticalAlignment="Center"/>
                </StackPanel>
              </DockPanel>
            </StackPanel>
          </Border>
        </DataTemplate>
      </ListBox.ItemTemplate>
    </ListBox>
  </DockPanel>
</Border>
```

(Точная разметка resize-Thumb — сохранить существующую с ветки; вставить блок так, чтобы Thumb остался первым элементом DockPanel. Ключ `Cancel` в resx уже существует — проверить; если его нет, добавить en «Cancel» / ru «Отмена».)

- [ ] **Step 3: Сборка**

```powershell
dotnet build PBLApp
```
Expected: OK.

- [ ] **Step 4: `/pbl-verify`** — открыть тестовый билд → Дерево → включить «Тепловую карту» → «Сгенерировать» → дождаться → скриншоты: (а) панель с карточками (имя+бейдж, прирост+«за N оч.», «—» у недосчитанных), статус воркеров; (б) переключить сортировку «За очко» + фильтр — `/pbl-check`. Проверить глазами по скриншоту, не только сборкой.

- [ ] **Step 5: Commit**

```bash
git add PBLApp
git commit -m "feat(tree-power): trader-style report panel — cards, sort selector, name filter, worker status; toolbar keeps only the toggle"
```

---

### Task 8: UI — кольца-акценты топ-10 на дереве

**Files:**
- Modify: `PBLApp/Controls/TreeCanvas.cs`
- Modify: `PBLApp.Core/TreeTabViewModel.cs` (передача топ-10 id)

**Interfaces:**
- Consumes: `NodePowerResult` overlay (уже прокинут в canvas), `PowerOverlayChanged`.
- Produces: `TreeCanvas.PowerTopIds : IReadOnlyList<int>?` (styled property, как `NodePowerOverlay`).

- [ ] **Step 1: VM** — при установке `PowerOverlay` вычислить `PowerTopIds` = 10 невзятых не-кластерных нод с максимальным `Power` (`Steps != null`, `Power > 0`); прокинуть в canvas тем же путём, что `NodePowerOverlay` (код-бихайнд `TreeTabView.axaml.cs` подписан на `PowerOverlayChanged`).

- [ ] **Step 2: Canvas** — styled property `PowerTopIdsProperty` (`AffectsRender`); в `Render` после блока hover path-preview rings добавить (паттерн тот же — `dc.DrawEllipse` поверх арта):

```csharp
// ── Top-10 power accent rings ───────────────────────────────────────
if (PowerTopIds is { Count: > 0 } topIds && NodePowerOverlay != null)
{
    foreach (var tid in topIds)
    {
        if (alloc!.Contains(tid) || !_nodeById.TryGetValue(tid, out var tn)) continue;
        var (tx, ty) = W2S(tn);
        double iconHalfPx = GetIconHalfWorld(tn.Type) * _scale;
        bool useSprites   = AssetStore != null && iconHalfPx >= MinIconScreenPx;
        double orPx       = useSprites ? iconHalfPx : GetRadius(tn.Type);
        dc.DrawEllipse(null, PowerTopPen, new Point(tx, ty), orPx + 4.0, orPx + 4.0);
    }
}
```

`PowerTopPen` — статический `Pen` рядом с `PathPreviewNodePen`, цвет `#F9E2AF` (жёлтый акцент), толщина 2, чтобы отличался и от оранжевого ховера, и от heat-тона.

- [ ] **Step 3: Сборка + `/pbl-check`** — скриншот дерева с включённой картой: топ-10 нод обведены жёлтым, при зум-ауте кольца видны.

- [ ] **Step 4: Commit**

```bash
git add PBLApp PBLApp.Core
git commit -m "feat(tree-canvas): top-10 power accent rings"
```

---

### Task 9: IPC/MCP + замер памяти + финальная сверка

**Files:**
- Modify: `PBLApp/Ipc/IpcServer.cs` (`/tree/state`, `/tree/power-report`)
- Modify: `AVALONIA_MIGRATION_PLAN.md` (строка состояния фичи)

**Interfaces:**
- Consumes: VM-свойства Task 6.

- [ ] **Step 1: IPC** — в `/tree/state` (регион ~строка 1175) добавить `workersReady`, `workersTotal` (из `TreePowerService`-пула, 0/0 при выключенном), `powerSortIndex`, `powerFilter`; в `/tree/power-report` (~строка 1447) — поля `steps` (nullable int) и `perPoint` (строка или null). MCP-тулзы `visual_tree_power_*` не меняются (payload passthrough).

- [ ] **Step 2: Замер памяти** — вручную: запустить клиент, открыть билд, «Сгенерировать» с пулом Авто; в PowerShell:

```powershell
Get-Process PBLApp | Select-Object WorkingSet64
```
до первого расчёта и после прогрева. Зафиксировать МБ/воркер в commit message. Если > ~500 МБ/воркер — сменить дефолт `ConfiguredSize()` авто-режима на `Math.Clamp(cores-2, 1, 2)`.

- [ ] **Step 3: Скоростная сверка** — на реальном билде замерить время «Сгенерировать» при «Выкл» (фолбэк, один хост) и «Авто»; записать в commit message (ожидание: ≥3× на 4 воркерах).

- [ ] **Step 4: Полный прогон**

```powershell
dotnet test PBLEngine.Tests
```
Expected: все PASS. Затем `/pbl-verify` — финальный скриншот панели + карты.

- [ ] **Step 5: Обновить `AVALONIA_MIGRATION_PLAN.md`** — в Backlog отметить heat-map фичу как v2 (пул воркеров, шаги, редизайн), одной-двумя строками.

- [ ] **Step 6: Commit**

```bash
git add PBLApp AVALONIA_MIGRATION_PLAN.md
git commit -m "feat(tree-power): IPC worker status + report steps/perPoint; memory+speed numbers in message"
```
