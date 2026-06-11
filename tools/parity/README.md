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

## Известные ограничения

- **Окно PoB мелькает**. SimpleGraphic GUI без headless-режима. `os.exit(0)` после дампа закрывает быстро (≈3-5 сек), но это всё равно UI.
- **Только scalar-stats**. `mainOutput`/`calcsOutput` имеют вложенные таблицы (например `Minion.*`); сейчас collector берёт только number/string/boolean из верхнего уровня. Расширить — pre-walk таблиц с dot-notation путями.
- **Tolerance flag** актуален только для чисел. Для строк всегда строгое равенство.
- **Тайм-аут по умолчанию 60 сек** на запуск orig PoB. ModCache генерация при первом запуске может занять дольше — увеличить через `--timeout=N`.

## Следующие расширения

- Walk вложенных таблиц (`Minion.TotalDPS`, `MainHand.PhysicalMin`, etc.)
- CI-режим: запускать на наборе golden builds в GitHub Actions перед мержем upstream-sync
- Сравнение detailed breakdowns (`build.calcsTab.calcsEnv.player.breakdown[stat]`)
