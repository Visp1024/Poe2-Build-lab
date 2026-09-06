# UPDATE_PIPELINE.md

Процедура обновления форка под новые версии PoE2 / upstream PathOfBuilding-PoE2.

Состоит из трёх независимых, последовательно запускаемых шагов:

1. **`scripts/sync-upstream.ps1`** — подтянуть Lua-логику из апстрима.
2. **`scripts/regen-modcache.ps1`** — перегенерировать `src/Data/ModCache.lua` через PoB с зажатым Ctrl (GGPK не нужен).
3. **`scripts/regen-data-ggpk.ps1`** — перегенерировать остальные `src/Data/*` и `src/Export/*` через Dat View (GGPK нужен).
4. **`scripts/regen-localization.ps1`** — пересобрать переводы под новые данные.

Маркер прогресса — `.upstream-sync.yaml` (хранит последний синхронизированный upstream sha).

---

## Состояние на 2026-09-06

| Слой              | Текущая версия | Последний upstream sync            | Гэп |
|-------------------|----------------|------------------------------------|-----|
| Lua-код апстрима  | 0.23.1         | `7d6f530cb` Release 0.23.1         | коммиты апстрима 28.07–04.09 не взяты сознательно |
| `src/Data/`       | смесь          | 0.23.1 + GGPK-реген 4 скриптов     | mods / essence / uModsToText — см. ниже |
| `src/TreeData/`   | 0_5            | 0.23.1                             | нового дерева у апстрима нет |
| Локализация       | частично       | GGPK-дамп 04.09                    | русские шаблоны статов протухают, см. ниже |
| Avalonia UI       | актуальна      | n/a (наш код)                      | breaking changes апстрима не было |

Клиент PoE2 на машине сборки (`D:\Games\steamapps\common\Path of Exile 2`,
`Bundles2` от 04.09.2026) **новее апстрима** — поэтому GGPK-реген даёт данные,
которых в релизе PoB ещё нет.

### Что мешает довести GGPK-реген до конца

| Скрипт        | Чего не хватает | Последствие |
|---------------|-----------------|-------------|
| `mods`        | `SpawnWeight` не именован в pathofexile-dat-schema (см. заглушку в `ColumnMappings.lua`) | скрипт отрабатывает, но `weightVal` у КАЖДОГО мода выходит пустым — молчаливая порча данных. Прогонять нельзя, пока схему не починят |
| `essence`     | таблицы `LiquidEmotionOutcomes` нет в схеме вовсе | падает на загрузке |
| `uModsToText` | `ModGrantedSkills` (в схеме колонка `Skill`, скрипт ждёт `SkillGem`) + цепочка через `ItemExperiencePerLevel` | падает на загрузке |

`HashStats` из `src/Modules/Common.lua` продублирована в `PBLDataExport/lua/HeadlessRunner.lua`:
с 0.23.x `mods.lua` считает через неё trade-хэш.

### Локализация: русский источник repoe-fork умер

`https://repoe-fork.github.io/poe2/Russian/**` отдаёт **HTTP 404** — все русские
дампы. Последствия регена:

- `gem_stats_templates.json` — пересобирается, но БЕЗ русского текста
  (19275 ru-строк → 0). Откатывать вывод обязательно.
- `passive_nodes_ru.json` — вместо 2746 записей пишет 47. Тоже откатывать.
- `gen_passive_names_ru.py` — падает с exit 1 по той же причине.

Что регенерируется нормально (источник — русские таблицы самого GGPK):
`items_ru.json`, `passive_names_ru.json`, `skill_descriptions_ru.json`.

Русские `stat_descriptions` лежат в GGPK-дампе (`tables/Russian/`) — переезд
генераторов на него вместо repoe-fork остаётся отдельной задачей.

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
| `skillGemList.lua`  | 8418 строк, +24 строки (новые гемы) | реальный live game content | SkillGems/GemEffects/GrantedEffects/GrantedEffectStatSets/GrantedEffectLabels/SupportGems/SkillGemSupports + 5 renames (`DisplayedName→DisplayName`, `Str/Dex/Int`, `StatSet→GrantedEffectStatSets`, `Label→LabelType`, `SkillGem→ActiveGem`) + computed `IsSupport` из `GemType` |
| `essence.lua`       | 86 строк, диф по значениям | data drift (Tier колонка деградировала к 0; ID получили `Lesser/Greater/Deafening` префиксы; EssenceMods refs указывают на новые индексы) | Essences/EssenceMods/EssenceTargetItemCategories + computed `DropLevel = {Tier}` + rename `Mod/DisplayMod → Mod1/Mod2` |
| `uModsToText.lua` (после `mods.lua` в одной цепочке) | 30 файлов Uniques | **29/30 байт-в-байт**, 1 файл — −1 лишний пробел в начале строки | `isValueInArray`/`isValueInTable` хелперы; читает `Data/ModItemExclusive.lua` и `Data/ModVeiled.lua`, которые `mods.lua` сгенерил в предыдущем шаге |

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
| 2026-06-11 | 0.15.0    | 0.20.0    | merged via `upstream-sync/20260611` | Big-bang sync; см. раздел «Уроки большого синка» ниже |

---

## Уроки большого синка 0.15.0 → 0.20.0 (для следующего оператора)

Все находки и грабли из реального первого синка. Документировано чтобы следующее обновление не нарывалось на те же мины.

### 1. Initial commit нашего форка ≠ Release-tag апстрима

`manifest.xml` исходно показывал `<Version number="0.15.0" />`, но содержимое `src/Modules/Main.lua` Initial commit'а содержит фичи (`migrateAugments`, `LoadModule("Modules/CalcFormat")`, `GetVirtualScreenSize()`), которые ушли в upstream **позже** Release 0.15.0. Минимум diff (35 файлов) получался с `d674b18db Fix options menu overflowing screen boundaries (#1840)` — это где-то между 0.15.0 и 0.16.0.

**Следствие**: `merge-base` между нашим main и любым upstream tag/HEAD пустой. Прямой `git merge upstream/dev` невозможен.

**Решение**: 3-way патч через `git diff <last_synced>..<target> -- <sync_paths> | git apply --3way`. SHA «last_synced» хранится в `.upstream-sync.yaml` и при первом синке был выставлен на `3e1b71c92d…` (Release 0.15.0) как best-effort приближение. После каждого синка маркер обновляется на актуальный upstream SHA.

### 2. Стратегия разрешения 3-way конфликтов

После `git apply --3way` для большого синка ~50 файлов конфликтовали. Базовое распределение:

| Категория | Файлов | Стратегия | Почему |
|-----------|--------|-----------|--------|
| `src/Data/**`, `src/Export/**` | ~30 | `git checkout --theirs` | Авто-генеренные дампы; наши «локальные правки» в Initial были застывшим снапшотом старой версии — заведомо неактуальны. Регенерация через `regen-data-ggpk` после мержа всё равно перезапишет. |
| `src/Classes/**` | ~10 | `git checkout --theirs` | Оригинальный Lua UI PoB. Мы не используем — рендерим через Avalonia (`PBLApp`). Что в upstream — то и берём. |
| `src/Modules/**` | 6 | `git checkout --theirs` + ручной патч | Здесь живут наши NLua/Lua 5.4 правки (CalcOffence buffer, ModParser format). Брал upstream и **пере-применял документированные правки** (см. ниже). |

### 3. Документированные NLua / Lua 5.4 правки, которые ОБЯЗАНЫ выжить

После любого синка проверять и при необходимости пере-применять:

- **`src/Launch.lua`** — polyfill `math.tointeger`. LuaJIT 2.0 в нашем `runtime/lua51.dll` его не имеет. Падение: `Modules/ItemTools.lua:57: attempt to call field 'tointeger' (a nil value)`.
- **`src/Modules/CalcOffence.lua:2660`** — `m_floor(entry.capped)` перед `string.len`, плюс `buffers.chance[...] or ""`. Падение под NLua: `attempt to concatenate a nil value (field 'cappedBuffer')` (integer/integer = float в 5.4 ломает `string.len` indexing).
- **`src/Modules/ItemTools.lua:57`** — `math.tointeger(displayVal)` для целочисленной нормализации; уже подобрана upstream'ом, конфликта обычно нет.

CLAUDE.md → раздел «Critical Lua 5.4 differences vs LuaJIT» — каноничный источник.

### 4. Runtime exe называется `Path{space}of{space}Building-PoE2.exe`

Это **не баг кодировки** — upstream хранит файл с буквальными литералами `{space}`. Скрипты `regen-modcache.ps1` / `regen-data-ggpk.ps1` автоматом копируют его в `Path of Building-PoE2.exe` при первом запуске. Локальная копия в `.gitignore`. Не пытаться `git mv` upstream-имя.

### 5. ModCache regen — через env var, не через Ctrl

Старая инструкция (зажать Ctrl при старте PoB) ненадёжна: фокус уходит на окно, `IsKeyDown("CTRL")` не успевает зафиксироваться. Используем env var (`Modules/Main.lua:122` принимает оба):

```pwsh
$env:REGENERATE_MOD_CACHE = "1"
Start-Process .\runtime\<exe> -PassThru
```

`regen-modcache.ps1` уже так делает + проверяет sha256 файла до/после. **Закрывать PoB нужно штатно** — реген пишется в `main:Shutdown`.

### 6. PBLHost.sln упоминается в CLAUDE.md, но не существует

`scripts/sync-upstream.ps1` исходно собирал `PBLHost.sln` после patch'а. Заменено на `PBLEngine.csproj`. Если CLAUDE.md обновится с настоящим sln — синхронизировать.

### 7. PBLDataExport — headless альтернатива Dat View

`scripts/regen-data-ggpk.ps1` ходит через `PBLDataExport` (см. развёрнутый раздел про column-mapping выше). Покрытие сейчас: 7 Export Scripts (`costs`, `flavourText`, `modScalability`, `mods`, `skillGemList`, `essence`, `uModsToText`). Остальные требуют доп. инфры (`.it`/`.ot` extraction, GIMP/NVTT и пр.) — список deferred выше.

Главное при работе с PBLDataExport:
- **NLua не маршалит `List<Dictionary>`** — `SEHException` в `MetaFunctions.GetMethod`. Конфиг передаём через temp JSON, который Lua-side декодит через dkjson.
- **Windows + npx**: `Process.Start("npx.cmd")` ловит `MODULE_NOT_FOUND` из-за Node `npm-prefix` resolution. Идём через `cmd.exe /c npx ...`.
- **Schema drift**: `pathofexile-dat-schema` (https://github.com/poe-tool-dev/dat-schema) и `src/Export/spec.lua` — два независимых проекта. Колонки расходятся. Перед добавлением новой таблицы — `python scripts/inspect-schema.py <Table>`, потом сравнить с `grep -A30 '<table>=' src/Export/spec.lua`, и закрыть разрыв в `ColumnMappings.lua` (rename + computed).
- **Unnamed columns** в pathofexile-dat schema **не экспортируются**. Пример: `Mods.SpawnWeight_Values` — i32-массив без имени → `weightVal = { }` в выходе. Лечится либо вкладом в upstream-schema, либо computed-fallback из других таблиц.

### 8. Локализация: внешние зависимости и опечатка PoBApp/PBLApp

`scripts/regen-localization.ps1` запускает `PBLExport` + 3 python-генератора:

| Скрипт | Источник | Состояние |
|--------|----------|-----------|
| `PBLExport` блоки 3-4 (passive_names_ru, skill_descriptions_ru) | pathofexile-dat GGPK dumps | ✓ работает |
| `PBLExport` блоки 1-2 (gems_ru, items_ru) | **repoe-fork дампы** в `PBLApp.Core/Translations/skill_gems_{en,ru}.json` и `base_items_{en,ru}.json` | требует ручной выгрузки из RePoE/PoE2; без них skip |
| `gen_gem_stats_ru.py` | repoe-fork stat_translations через HTTP | ✓ работает |
| `gen_passive_names_ru.py` | repoe-fork skills через HTTP | падает (404 на `/Russian/skills.min.json` — формат изменился) |
| `gen_passive_ru.py` | repoe-fork stat templates через HTTP | сматчил 2/2492 (формат шаблонов изменился) — оставить **существующий прод-файл**, не перезаписывать |

**Историческая опечатка**: все три python-скрипта писали в `../PoBApp.Core/Translations/` вместо `../PBLApp.Core/Translations/`. Молча создавали левую папку рядом с репо, прод-файлы не обновлялись. Исправлено в коммите `604d4b9f8`.

`PBLExport/Program.cs` блоки 1-2 раньше падали при отсутствии repoe-fork файлов (hard `File.ReadAllText`). Обёрнуты в `File.Exists` — теперь skip + warning.

### 9. Тест-кейс `customMods` Label

`GetConfigOptions_AllHaveNonEmptyLabel` падал из-за `customMods` (textarea внутри секции «Custom Modifiers», у textarea пустой Label — это by-design). Ослаблено условие: `type == "text"` исключение допустимо. См. коммит `0266197d7`.

### 10. CRLF/LF предупреждения

Windows git c `core.autocrlf=true` — каждое касание Lua/JSON файла даёт warning «LF will be replaced by CRLF». Безопасно игнорировать, поведение не меняется.

### 11. Время полного цикла

Полный live-цикл (clean fetch + sync + ModCache regen + data-export full pass через PBLDataExport + localization regen + verify) занимает **20-40 минут** при наличии установленного PoE2. Без PoE2 (только sync + verify через старые данные) — 5 минут.

### 12. На что смотреть в `/pbl-verify` после большого синка

Минимум:
- Build открывается без ошибок (LuaHost init green)
- Header stats (DPS/HP/Mana/Spirit) посчитаны
- Tree tab переключается и ноды кликабельны
- Items tab показывает экипировку + runes
- RU локализация — все табы и хедер переведены
- Tooltip гема при ховере (если есть скилл)

При первом синке 0.15→0.20 верификация прошла на «New Build 2» — DPS 4 861, всё корректно.

### 13. `sync_paths` со списком подкаталогов пропускал корневые файлы `src/`

Первый big-bang синк перечислял в `.upstream-sync.yaml` подкаталоги (`src/Modules/`, `src/Classes/`, …) — и **корневые файлы `src/` не попадали ни под один pathspec**. Что пропустили и чем это обернулось:

- `src/GameVersions.lua` — без регистрации `0_5` в `treeVersionList` приложение грузило дерево 0_4: не было новых восхождений Spirit Walker (Huntress) и Martial Artist (Monk), хотя сами данные `src/TreeData/0_5/` синк принёс. Симптом коварный: данные на диске есть, calc-поддержка (Idolatry-моды) есть, а UI показывает старое дерево.
- `src/HeadlessWrapper.lua` — upstream-фикс: `newBuild()` теперь вызывает `wipeGlobalCache()`; без него возможны устаревшие кэш-данные между билдами в headless-хосте (наш NLua использует именно этот путь).
- `src/LaunchServer.lua` — IPv4-bind фикс LuaSocket.

Исправлено 2026-06-12: `sync_paths` теперь содержит `src/` целиком. Это безопасно для форк-только файлов внутри `src/` (например, локалей): патч строится из diff'а upstream'а, наших файлов в нём нет по определению. **Урок: pathspec'и в маркере должны покрывать каталог целиком; точечные списки тихо отстают, когда upstream трогает файл вне списка.**
