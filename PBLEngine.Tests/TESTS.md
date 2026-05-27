# PBLEngine.Tests — структура и соглашения

## Быстрый старт

```powershell
dotnet test PBLEngine.Tests/PBLEngine.Tests.csproj
```

Все тесты запускаются последовательно (один экземпляр LuaHost), время — ~25 с.

---

## Архитектура

### LuaHost инициализируется один раз

`LuaHostFixture` (IClassFixture) запускает `host.Initialize()` единожды на весь тестовый класс.
Init занимает ~40 с — именно поэтому все классы шарят один экземпляр через `[Collection("LuaHost")]`.

Каждый тест начинается с `_host.NewBuild()` в конструкторе, чтобы изолировать состояние.

```csharp
[Collection("LuaHost")]
public class MyTests : IClassFixture<LuaHostFixture>
{
    private readonly LuaHost _host;

    public MyTests(LuaHostFixture fixture)
    {
        _host = fixture.Host;
        _host.NewBuild();   // ← сброс состояния перед каждым тестом
    }
}
```

### Как добавить скилл в тест

Используй `PasteSocketGroup` через `State` глобал (не интерполяцию — ломается на `\n` и кавычках):

```csharp
private void AddSkill(string gemLine)
{
    _host.State["_testSkillText"] = gemLine.Replace("\n", "\r\n");
    _host.State.DoString(@"
        build.skillsTab:PasteSocketGroup(_testSkillText)
        runCallback('OnFrame')
        if build.calcsTab then build.calcsTab:BuildOutput() end
    ");
    _host.State["_testSkillText"] = null;
}
```

Формат строки gem: `"GemName level/quality [DISABLED] count"`, несколько гемов — через `\n`:

```csharp
AddSkill("Lightning Warp 20/20  1");
AddSkill("Cast on Minion Death 20/20  1\nSpark 20/20  1");
```

### Как прочитать стат

```csharp
private double Stat(string key)
{
    var stats = _host.GetAllStats();
    return stats.TryGetValue(key, out var v) ? Convert.ToDouble(v) : 0;
}
```

`GetAllStats()` мёржит `mainOutput` + `calcsOutput`. Per-type Min/Max (LightningMin, ColdMax и т.д.)
приходят только из `calcsOutput` — требуют предварительного вызова `SetActiveSkillGroup`.

---

## Файлы тестов

| Файл | Что тестирует |
|------|---------------|
| `LuaHostTests.cs` | SaveBuildToXml, LoadBuildFromXml, GetNotes/SetNotes, GetConfigOptions, SetConfigValue |
| `BuildModelTests.cs` | BuildModel.Notes, BuildModel.AllStats, PropertyChanged |
| `RegressionTests.cs` | math.pow шим, mainOutput nil crash, SaveBuildToXml после сброса |
| `BuildCodecTests.cs` | BuildCodec.Encode/Decode (Base64+zlib), round-trip, LooksLikeCode |
| `SkillGroupTests.cs` | GetSkillGroups, GetActiveSkillsInGroup, SetActiveSkillGroup, trigger-группы |

---

## SkillGroupTests — детали

Покрывает API, добавленное в Phase 5 для поддержки Cast on Minion Death и других trigger-скиллов.

**GetSkillGroups:**
- Возвращает non-null список
- После добавления скилла — группа находится по имени
- Trigger-группа (`Cast on Minion Death + Spark`) отображается через `displayLabel`, а не только через первый скилл
- Все индексы > 0, все имена непустые

**GetActiveSkillsInGroup:**
- Одиночный скилл → 1 запись с правильным именем
- Trigger-группа → ≥ 2 записи, содержит оба скилла
- Все индексы > 0, все имена непустые

**SetActiveSkillGroup:**
- Не бросает исключений
- После выбора Lightning Warp: TotalDPS > 0, Speed > 0
- После выбора активного скилла внутри trigger-группы: `mainActiveSkill` в Lua обновляется
- LightningMin > 0 (CALCS-режим), LightningMax >= LightningMin

---

## Ключевые Lua-факты для написания тестов

- `build.skillsTab.socketGroupList` — массив всех socket groups (1-based)
- `group.displayLabel` — вычисленное имя группы (только после MAIN-прогона CalcSetup)
- `group.displaySkillList` — список активных скиллов в группе (после прогона)
- `group.mainActiveSkill` — индекс выбранного скилла в displaySkillList (для MAIN)
- `group.mainActiveSkillCalcs` — то же для CALCS-режима
- `build.mainSocketGroup` — индекс текущей активной группы
- `build.calcsTab.input.skill_number` — индекс группы для CALCS-прогона

Ailment ключи:
- Атаки: `ShockChance`, `IgniteChance` (combined hit+crit)
- Спеллы: `ShockChanceOnHit`, `ShockChanceOnCrit`, `IgniteChanceOnHit`, `IgniteChanceOnCrit`

Per-type урон (только в CALCS): `LightningMin`, `LightningMax`, `ColdMin`, `ColdMax`, `FireMin`, `FireMax`, `PhysicalMin`, `PhysicalMax`, `ChaosMin`, `ChaosMax`
