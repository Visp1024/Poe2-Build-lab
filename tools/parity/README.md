# tools/parity

Параллельный паритет-тест: прогоняет один и тот же build XML через **оригинальный PoB** (LuaJIT runtime, `runtime/<exe>`) и **наш PBLEngine** (NLua 5.4), сверяет stats. Канонический способ проверить, что наша 5.4-переадаптация не сломала calc.

**Текущий статус (2026-06-12, PoE2 0.20.0): 11/11 билдов — полный паритет** (3 локальных + 8 community, ~9000 сравнений stat'ов, 0 mismatches). Первый найденный parity-баг (`Int/StrRequirementsOnWeapon` ×2) исправлен в `eb79ef1c6` — см. раздел «Findings» ниже.

## Компоненты

```
src/Launch.lua              # env-var hook: POB_PARITY_BUILD => dofile dump_stats
tools/parity/dump_stats.lua  # внутри оригинального PoB: загрузка XML → calc → JSON
PBLParity/                   # C# console: оркестрирует оба движка + diff
scripts/parity.ps1           # тонкий PowerShell wrapper
```

## Использование

```pwsh
# все билды из %LOCALAPPDATA%\PathOfBuilding2\Builds:
pwsh ./scripts/parity.ps1

# конкретный билд:
pwsh ./scripts/parity.ps1 -Build "C:\path\build.xml"

# подробно (с MATCH-строками) + JSON-отчёт:
pwsh ./scripts/parity.ps1 -All -Json out\parity.json

# напрямую через PBLParity:
dotnet run --project PBLParity -- "C:\path\build.xml" --max=20
```

## Как это работает

1. **Оригинальный PoB**: PBLParity запускает `runtime/<exe>` с тремя env vars
   (`POB_PARITY_BUILD`, `POB_PARITY_OUT`, `POB_PARITY_SCRIPT`). Хук в `src/Launch.lua`
   после `main:Init` ловит env и `dofile`'ит `dump_stats.lua`, который грузит билд
   через `buildMode:Init`, триггерит calc, дампит `mainOutput`+`calcsOutput`
   в JSON, и `os.exit(0)`. Окно PoB мелькает на секунду, потом закрывается.

2. **Наш PBLEngine**: PBLParity напрямую инициализирует `PBLEngine.LuaHost`,
   вызывает `LoadBuildFromXml` и `GetAllStats` — тот же набор stats в той же
   форме.

3. **Diff**: object-level сравнение по ключам, для чисел относительная
   погрешность (default `1e-6`). Различает: `MATCH` / `CLOSE` (в пределах
   tolerance) / `MISMATCH` / `ONLY-ORIG` / `ONLY-PBL`.

## Exit codes

| Код | Что значит |
|-----|------------|
| 0   | Полный паритет |
| 2   | Найдены significant mismatches (число mismatches + ONLY-* с non-default значением > 0) |
| 1   | Системная ошибка (билд не найден, dump не отработал в timeout, и т.д.) |

## Установка baseline

Подтверждённые паритетные билды на 0.20.0:

| Build | Stats | Status |
|-------|-------|--------|
| `New Build.xml`    | 787 | 0 mismatch |
| `New Build 2.xml`  | 852 | 0 mismatch |
| `New Build 3.xml`  | 814 | 0 mismatch |

## Community builds (через `fetch_community_builds.py`)

`tools/parity/fetch_community_builds.py` тянет публично-расшаренные билды с pobarchives.com → pobb.in/raw, декодирует base64+zlib в XML. Это PoE2 шары от реальных игроков. Запуск:

```pwsh
python tools/parity/fetch_community_builds.py --count 10 --out tools/parity/community_builds
```

Текущий снимок (8 разноклассовых билдов, после фикса `eb79ef1c6`):

| Build (pobarchives ID) | Level / Class | Stats | Match | Mismatch |
|------------------------|---------------|-------|-------|----------|
| `3MrEDKwx` | L97 Druid/Oracle           | 853 | 853 | 0 ✓ |
| `3icirQcy` | L97 Druid/Oracle           | 851 | 851 | 0 ✓ |
| `9T3EGRVR` | L15 Druid/Oracle           | 797 | 797 | 0 ✓ |
| `BJXPrbg9` | L82 Huntress/Ritualist     | 692 | 692 | 0 ✓ |
| `CqX3fXBg` | L97 Sorceress/Stormweaver  | 847 | 847 | 0 ✓ |
| `D4F8P8DU` | L95 Sorceress/Chronomancer | 837 | 837 | 0 ✓ |
| `FtcWJKWW` | L89 Witch/Infernalist      | 865 | 865 | 0 ✓ |
| `Hfk8mUNU` | L94 Mercenary/Witchhunter  | 781 | 781 | 0 ✓ |

XML-фикстуры закоммичены в `tools/parity/community_builds/` — будущие upstream-синки можно прогонять на ровно том же входе. Прогнать весь набор:

```pwsh
Get-ChildItem tools/parity/community_builds/*.xml | ForEach-Object {
    pwsh ./scripts/parity.ps1 -Build $_.FullName
}
```

## Findings

### #1 — `Int/StrRequirementsOnWeapon` ×2 (RESOLVED, `eb79ef1c6`)

**Симптом:** на первом прогоне community-свипа 3/8 билдов расходились на одной семье stat'ов — `Int/StrRequirementsOnWeapon` со значением ≈ 2× оригинала (74→157, 58→122, 24→48). Локальные `New Build 1-3` баг не триггерили — у них нет оружия с гранящими скиллы range-модами.

**Первая гипотеза была неверной:** подозревали `calcLib.mod` multi-key lookup в `CalcPerform.lua:1795` (max vs sum). На деле `reqMultWeapon` был невиновен.

**Реальная причина:** `src/Modules/ItemTools.lua:applyRange` интерполирует range-моды вида `(1-20)` и `tostring()`'ит округлённый результат обратно в строку для повторного матча ModParser'ом. В LuaJIT 5.1 integer-valued float печатается как `"11"`, в Lua 5.4 — как `"11.0"`. Integer-only паттерны ModParser (`grants skill: level (%d+) (.+)`) не матчат `"Level 11.0"`, ребилд молча проваливается, и modList сохраняет level=20 от раннего безусловного range=1 парса в `Item.lua` — гранящиеся скиллы оказываются на max level, и их attribute requirements взлетают.

**Фикс:** прогонять каждое значение, рендерящееся обратно в строку, через `math.tointeger` (3 места в `applyRange`). Тот же класс багов, что и полифилл `math.tointeger` в `src/Launch.lua` и фикс `CalcOffence.lua:2660`: Lua 5.4 различает integer/float там, где LuaJIT их схлопывает, и расхождение утекает через string formatting.

**Мораль:** community-свип поймал баг на первом же прогоне, который синтетические фикстуры не задевали. Разнообразие реальных билдов — основная ценность этого набора.

## Известные ограничения

- **Окно PoB мелькает**. SimpleGraphic GUI без headless-режима. `os.exit(0)` после дампа закрывает быстро (≈3-5 сек), но это всё равно UI.
- **Только scalar-stats**. `mainOutput`/`calcsOutput` имеют вложенные таблицы (например `Minion.*`); сейчас collector берёт только number/string/boolean из верхнего уровня. Расширить — pre-walk таблиц с dot-notation путями.
- **Tolerance flag** актуален только для чисел. Для строк всегда строгое равенство.
- **Тайм-аут по умолчанию 60 сек** на запуск orig PoB. ModCache генерация при первом запуске может занять дольше — увеличить через `--timeout=N`.

## Следующие расширения

- Walk вложенных таблиц (`Minion.TotalDPS`, `MainHand.PhysicalMin`, etc.)
- CI-режим: запускать на наборе golden builds в GitHub Actions перед мержем upstream-sync
- Сравнение detailed breakdowns (`build.calcsTab.calcsEnv.player.breakdown[stat]`)
