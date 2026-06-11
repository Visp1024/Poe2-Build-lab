# tools/parity

Параллельный паритет-тест: прогоняет один и тот же build XML через **оригинальный PoB** (LuaJIT runtime, `runtime/<exe>`) и **наш PBLEngine** (NLua 5.4), сверяет stats. Канонический способ проверить, что наша 5.4-переадаптация не сломала calc.

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

Текущий снимок (8 разноклассовых билдов):

| Build (pobarchives ID) | Level / Class | Stats | Match | Mismatch |
|------------------------|---------------|-------|-------|----------|
| `3MrEDKwx` | L97 Druid/Oracle           | 853 | 853 | 0 ✓ |
| `3icirQcy` | L97 Druid/Oracle           | 851 | 848 | **3** ✗ |
| `9T3EGRVR` | L15 Druid/Oracle           | 797 | 796 | **1** ✗ |
| `BJXPrbg9` | L82 Huntress/Ritualist     | 692 | 690 | **2** ✗ |
| `CqX3fXBg` | L97 Sorceress/Stormweaver  | 847 | 847 | 0 ✓ |
| `D4F8P8DU` | L95 Sorceress/Chronomancer | 837 | 837 | 0 ✓ |
| `FtcWJKWW` | L89 Witch/Infernalist      | 865 | 865 | 0 ✓ |
| `Hfk8mUNU` | L94 Mercenary/Witchhunter  | 781 | 781 | 0 ✓ |

**5/8 идеальный паритет**, **3/8 валятся на одной семье stat'ов** — `Int/StrRequirementsOnWeapon`. Все три провала имеют значение ≈ 2× оригинала (74→157, 58→122, 24→48).

### Известный bug (FIRST PARITY FINDING)

`src/Modules/CalcPerform.lua:1832` — `req = m_floor(reqSource[attr] * reqMultWeapon)`.

`reqMultWeapon` собирается из `calcLib.mod(modDB, nil, "GlobalAttributeRequirements", "GlobalItemAttributeRequirements", "GlobalWeaponAttributeRequirements")` (line 1795).

Этот мульт в наших NLua-расчётах даёт ~2.0 для некоторых билдов, тогда как оригинальный LuaJIT — 1.0. Сценарий триггерится конкретными items (вероятно weapons с attribute-requirement модами). Тестовые билды `New Build 1-3` не задевают этот path — нужен реальный community-билд с правильным weapon mod для воспроизведения.

To-do (отдельная сессия):
1. Минимальный repro билд (только weapon + соответствующие моды).
2. Trace `calcLib.mod` поведения обеих движков на этих модах.
3. Скорее всего связано с тем, как NLua обрабатывает multi-key mod-lookups вида `("A", "B", "C")` — может суммировать там где LuaJIT берёт max.

## Известные ограничения

- **Окно PoB мелькает**. SimpleGraphic GUI без headless-режима. `os.exit(0)` после дампа закрывает быстро (≈3-5 сек), но это всё равно UI.
- **Только scalar-stats**. `mainOutput`/`calcsOutput` имеют вложенные таблицы (например `Minion.*`); сейчас collector берёт только number/string/boolean из верхнего уровня. Расширить — pre-walk таблиц с dot-notation путями.
- **Tolerance flag** актуален только для чисел. Для строк всегда строгое равенство.
- **Тайм-аут по умолчанию 60 сек** на запуск orig PoB. ModCache генерация при первом запуске может занять дольше — увеличить через `--timeout=N`.

## Следующие расширения

- Walk вложенных таблиц (`Minion.TotalDPS`, `MainHand.PhysicalMin`, etc.)
- CI-режим: запускать на наборе golden builds в GitHub Actions перед мержем upstream-sync
- Сравнение detailed breakdowns (`build.calcsTab.calcsEnv.player.breakdown[stat]`)
