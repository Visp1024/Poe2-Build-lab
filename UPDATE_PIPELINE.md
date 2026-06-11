# UPDATE_PIPELINE.md

Процедура обновления форка под новые версии PoE2 / upstream PathOfBuilding-PoE2.

Состоит из трёх независимых, последовательно запускаемых шагов:

1. **`scripts/sync-upstream.ps1`** — подтянуть Lua-логику из апстрима.
2. **`scripts/regen-modcache.ps1`** — перегенерировать `src/Data/ModCache.lua` через PoB с зажатым Ctrl (GGPK не нужен).
3. **`scripts/regen-data-ggpk.ps1`** — перегенерировать остальные `src/Data/*` и `src/Export/*` через Dat View (GGPK нужен).
4. **`scripts/regen-localization.ps1`** — пересобрать переводы под новые данные.

Маркер прогресса — `.upstream-sync.yaml` (хранит последний синхронизированный upstream sha).

---

## Состояние на 2026-06-11

| Слой              | Текущая версия | Последний upstream sync           | Гэп |
|-------------------|----------------|-----------------------------------|-----|
| Lua-код апстрима  | 0.15.0         | `3e1b71c92` Release 0.15.0        | 5 минорных релизов до 0.20.0 |
| `src/Data/`       | 0.15.0 dump    | initial commit `2cf882d` (28 май) | требует regen |
| `src/TreeData/`   | 0.15.0         | initial commit                    | проверить наличие новых веток (0_5) |
| Локализация       | 0.15.0 GGPK    | initial commit                    | regen после GGPK |
| Avalonia UI       | актуальна      | n/a (наш код)                     | трогаем только при breaking changes upstream |

Upstream HEAD: `558e2a5 Release 0.20.0` (через `git fetch upstream`).

---

## Чеклист — большое обновление (0.15 → 0.20, big-bang)

### Pre-flight

- [ ] Рабочее дерево чистое: `git status` — empty
- [ ] PBLApp собирается и проходит `/pbl-verify` на текущем main
- [ ] Зафиксирован baseline скриншот для визуальной регрессии
- [ ] **Исправить пути в `PBLExport/Program.cs:6-7`** — сейчас они указывают на `D:\Work\PathOfBuilding-PoE2`, должны на `PathBuildLab`. Без этого regen-localization запишет файлы не туда.

### 1. Sync upstream

```pwsh
pwsh ./scripts/sync-upstream.ps1 -DryRun        # посмотреть размер патча
pwsh ./scripts/sync-upstream.ps1                # применить
```

- [ ] Скрипт создал ветку `upstream-sync/<date>`
- [ ] 3-way apply прошёл без конфликтов **либо** конфликты разрешены вручную (`git status` после ошибки)
- [ ] `dotnet build PBLHost.sln` зелёный
- [ ] Маркер `.upstream-sync.yaml` обновлён (`last_synced_sha` + `version` + `date`)
- [ ] Коммит: `sync(upstream): 0.15.0 -> 0.20.0`

**Зоны типичных конфликтов:**
- `src/Modules/ModParser.lua` — мы патчили `string.format` float→int (см. `CLAUDE.md` про Lua 5.4)
- `src/Modules/ItemTools.lua`, `src/Modules/CalcOffence.lua` — те же 5.4 правки
- `runtime/lua/compat.lua` — наш, апстрим его не трогает, конфликта не должно быть

### 2a. Regen ModCache (без GGPK)

```pwsh
pwsh ./scripts/regen-modcache.ps1
```

- [ ] PoB запущен с **зажатым LeftCtrl до полной загрузки** (≈30-60 сек)
- [ ] Хэш `src/Data/ModCache.lua` изменился — скрипт это проверяет сам
- [ ] Коммит: `data: regen ModCache for 0.20.0` (всегда обязательный — без свежего ModCache тесты падают на лету)

### 2b. Regen GGPK dumps (headless, через PBLDataExport)

```pwsh
pwsh ./scripts/regen-data-ggpk.ps1 -Scripts costs,bases,skills
# либо без свежего дампа (переиспользовать JSON):
pwsh ./scripts/regen-data-ggpk.ps1 -NoDump -Scripts costs
```

Архитектура:

```
pathofexile-dat     (Node CLI, headless, читает Bundles2)
  └→ PBLExport/ggpk_export/tables/English/*.json
       └→ PBLDataExport (C# + NLua headless host)
            ├→ JsonDatFile.lua shim (mirrors src/Export/Classes/Dat64File.lua)
            └→ runs src/Export/Scripts/*.lua unchanged → writes src/Data/*.lua
```

- [ ] `PBLExport/ggpk_export/config.json` содержит все таблицы и колонки, нужные выбранным скриптам
- [ ] Foreign-row references задекларированы в `PBLDataExport/Program.cs` (см. словарь `refs`)
- [ ] `git diff --stat src/Data/` — изменения по делу
- [ ] Коммит: `data: regen GGPK dumps for <version>`

### Расширение на новый Script

Когда нужно добавить новый Export script (например `bases`):

1. **Таблицы в `config.json`**: пройти `src/Export/Scripts/bases.lua`, выписать каждый `dat("Foo")` и доступы к колонкам (`row.Bar`). Добавить запись в `PBLExport/ggpk_export/config.json`:
   ```json
   { "name": "BaseItemTypes", "columns": ["Id", "Name", "ItemClass", ...] }
   ```
2. **Foreign refs в `PBLDataExport/Program.cs`** (словарь `refs`): для каждой Key/ShortKey колонки указать целевую таблицу. Это превратит integer-индексы из pathofexile-dat в row-объекты, чтобы `row.ItemClass.Id` работал в скрипте без правок.
3. Прогон: `pwsh ./scripts/regen-data-ggpk.ps1 -NoDump -Scripts bases`. Итерировать пока скрипт не отработает зелёным.
4. Запустить с `-NoDump` снят — будет полный pathofexile-dat дамп + регенерация.

### Известные ограничения PBLDataExport (по состоянию на 2026-06-11)

- Доказан только `costs.lua` end-to-end. Остальные 19 скриптов требуют расширения config + refs.
- `:ReadCell(rowIndex, colIndex)` не реализован — Scripts должны индексировать по имени колонки, не по позиции.
- `getFile()` (бинарные ассеты — иконки, текстуры) пока стаб — скрипты `assets.lua` потребуют отдельной интеграции с pathofexile-dat's files-mode.
- Схема `refs` сейчас захардкожена в C#. Долгосрочно — генерировать из `PBLExport/ggpk_export/schema.min.json` (или из spec.lua).

### Главное открытие live-прогона: **schema drift между PoB и pathofexile-dat**

`src/Export/spec.lua` отражает PoE2-схему *на момент когда PoB-команда последний раз делала Export*. `pathofexile-dat-schema` (https://github.com/poe-tool-dev/dat-schema) — отдельный проект, ведётся независимо и отслеживает текущую игру. Эти две схемы расходятся.

Пример (validFor=2 → PoE2), таблица `CostTypes`:

| PoB spec.lua            | pathofexile-dat-schema |
|-------------------------|------------------------|
| `Resource` (String)     | **колонка удалена**    |
| `Stat` (Key → Stats)    | `Stat` (foreignrow → Stats) ✓ |
| `ResourceString` (String) | **переименована в `FormatText`** |
| `Divisor` (Int)         | `Divisor` ✓             |
| `PerMinute` (Bool)      | `PerMinute` ✓           |

Результат: live-`Costs.lua` получает `Resource = "nil"` и `ResourceString = "nil"` (буквальные строки от `tostring(nil)`), но `Stat` и `Divisor` верные.

**Column-mapping слой** (`PBLDataExport/lua/ColumnMappings.lua`, реализован):

```lua
return {
    UniqueStashLayout = {
        rename = { ItemVisualIdentityKey = "ItemVisualIdentity" },  -- column renamed in current schema
    },
    CostTypes = {
        rename = { FormatText = "ResourceString" },
        computed = {
            -- "Resource" was dropped from current schema entirely; reconstruct from Stat.Id.
            Resource = function(row)
                local stat = row.Stat and rawget(row.Stat, "Id") or ""
                local resourceByStat = { ["base_mana_cost"] = "Mana", ... }
                return resourceByStat[stat]
            end,
        },
    },
}
```

Применяется в `HeadlessRunner.lua` после `resolveRefs()` — computed-функции уже видят `row.Stat.Id` как row-объект.

**Доказанные end-to-end Scripts** (headless live-выход = production):

| Script              | Размер | Расхождение с production | Что потребовалось |
|---------------------|--------|--------------------------|-------------------|
| `costs.lua`         | 119 строк | 0 байт (полный матч)  | column-mapping (rename + computed) |
| `flavourText.lua`   | 3735 строк | 1 символ (`Mjölner` vs `Mjolner` — реальный апдейт игры) | column-mapping + новые таблицы + `sanitiseText` |
| `modScalability.lua`| 15064 строки | 0 байт (полный матч)  | `getFile` + `statdesc` инфра |
| `mods.lua`          | 8 файлов, ~10K строк всего | структурно совпадает, отличается только `weightVal` (см. ниже) | Mods/ModType/Tags/ModFamily config + 5 renames + `SpawnWeight`/`NodeType` computed + helpers (`copyTable`, `round`, `intToBytes`, `murmurHash2`, `LoadModule`, `ReadCellText` через spec.lua) |

**Schema-gap при mods.lua**: `Mods.SpawnWeight_Values` (i32 array значений весов) — колонка без имени в `pathofexile-dat-schema`, pathofexile-dat её не экспортирует. Скрипт выдаёт `weightVal = { }` вместо реального списка. Все остальные поля (тип, affix, описание, statOrder, group, weightKey, modTags, tradeHashes) идентичны production. Лечится либо вкладом колонки в `poe-tool-dev/dat-schema`, либо отдельным проходом вычисления весов из других таблиц.

**Инфра-слой 2: getFile + statdesc** (`PBLDataExport/lua/HeadlessRunner.lua`):

- `config.json:files[]` — список бинарных путей в Bundles2 (`Data/StatDescriptions/*.csd`, в перспективе `Metadata/.../*.ot`).
- pathofexile-dat пишет их в `PBLExport/ggpk_export/files/` с заменой `/` → `@`.
- Lua-global `getFile(path)` читает из этого каталога (с кэшем).
- Хелперы из `src/Modules/Common.lua` пере-реализованы локально: `convertUTF16to8` (для UTF-16LE→UTF-8 .csd), `codePointToUTF8`, `pairsSortByKey`, `escapeGGGString`, `t_insert`.
- `dofile("statdesc.lua")` загружает PoB-овскую stat-desc библиотеку (`loadStatFile`/`describeStats`/`describeScalability`) поверх наших stub'ов. После этого скрипты используют её как обычно.

**Workflow добавления нового Script:**

1. `python scripts/inspect-schema.py <TableName>` — посмотреть pathofexile-dat-schema колонки.
2. Сравнить с `grep -A30 '<tablename>=' src/Export/spec.lua`.
3. Дополнить `PBLExport/ggpk_export/config.json` (таблицы + колонки которые знает текущая схема).
4. Дополнить `PBLDataExport/Program.cs` словарь `refs` (foreign-row referenced).
5. Если есть rename/удалённые колонки → запись в `PBLDataExport/lua/ColumnMappings.lua`.
6. Если script зовёт `getFile`/`describeStats`/etc — стаббить в `HeadlessRunner.lua`.
7. `pwsh ./scripts/regen-data-ggpk.ps1 -NoDump -Scripts <name>` итерировать пока не зелёный.
8. Сравнить выход с production через `git diff -- src/Data/<X>.lua`. Принимать только delta которая объясняется реальным изменением игры.

Утилита `scripts/inspect-schema.py` — фетчит текущую `schema.min.json` в TEMP и выводит human-readable columns для заданной таблицы.

### 3. Regen localization

```pwsh
pwsh ./scripts/regen-localization.ps1
```

- [ ] `pathofexile-dat` отработал — таблицы в `PBLExport/ggpk_export/tables/{English,Russian}/`
- [ ] `dotnet run PBLExport` собрал `gems_ru.json`, `items_ru.json`, `passive_names_ru.json`
- [ ] Python-скрипты отработали — `passive_nodes_ru.json`, `gem_stats_templates.json`, `skill_descriptions_ru.json`
- [ ] Покрытие в `LOCALIZATION_PLAN.md` обновлено (счётчики)
- [ ] Коммит: `loc: regen for 0.20.0`

### 4. Verification

- [ ] `/pbl-build`
- [ ] `/pbl-test` — xUnit для PBLEngine.Tests
- [ ] `/pbl-verify` — визуальный регресс UI
- [ ] Открыть тестовый билд, проверить:
  - DPS не уплыл больше чем на 1-2% (или объяснить почему уплыл — обычно баланс)
  - Новые уникальные из 0.16-0.20 видны в каталоге
  - Новые ноды/аскенды на дереве отрисовываются
  - RU перевод видим на тултипах гемов и нод

### 5. Merge в main

- [ ] PR `upstream-sync/<date>` → `main`
- [ ] В PR-описание — сводка изменений: «5 релизов апстрима, +N уникальных, +M нод, +K скиллов»
- [ ] После мержа — обновить `manifest.xml` `<Version number="0.20.0" />` (если не подтянулось патчем)

---

## Инкрементальный режим (порелизный, минорное обновление апстрима)

То же самое, но `scripts/sync-upstream.ps1 -TargetRef <upstream-tag>` с одной версией за раз:

```pwsh
pwsh ./scripts/sync-upstream.ps1 -TargetRef v0.16.0
# … разрешить конфликты, прогнать data/loc если нужно, смержить, повторить для 0.17 и т.д.
```

Это режим для обычной работы — раз в неделю-две притаскивать одну версию.

---

## Известные подводные камни

- **NLua / Lua 5.4** — апстрим пишет под LuaJIT 5.1. Перед каждым синком проверять: новые `tostring(integer)` или `n / 1` в апстриме могут сломать ModParser. См. `CLAUDE.md → C# Projects → Critical Lua 5.4 differences`.
- **ModCache.lua обязателен к коммиту** — без него старт PoB генерит его на лету (≈30 сек), а в тестах падает.
- **runtime DLL не трогаем** — апстрим иногда обновляет SimpleGraphic, но наш PBLApp работает мимо рендера, и DLL-апдейты для нас бесполезны и опасны (LuaJIT vs Lua 5.4).
- **TreeData** — каждая новая версия дерева в `src/TreeData/X_Y/` это отдельный zip. Апстрим добавляет директорию — наш патч её перенесёт автоматически.
- **`Path of Building-PoE2.exe`** — в репе хранится с экранированным пробелом, проверять что git не «переименовал».

---

## История синков

| Дата       | От        | До        | PR  | Заметки |
|------------|-----------|-----------|-----|---------|
| 2026-05-28 | —         | 0.15.0    | —   | Initial fork snapshot |
| _TBD_      | 0.15.0    | 0.20.0    | _#_ | Big-bang sync |
