# LOCALIZATION_PLAN.md

Статус локализации приложения PBLApp (Path of Building PoE2 — Avalonia UI).
Обновлять по мере выполнения задач.

---

## Текущая архитектура локализации

| Слой | Механизм | Файлы |
|------|----------|-------|
| UI-строки (кнопки, заголовки) | `{loc:Tr Key}` → `Strings.resx` / `Strings.ru.resx` | `PBLApp.Core/Localization/` |
| Названия гемов | `GameTranslationService.TGem()` → JSON | `gems_ru.json` |
| Названия предметов | `GameTranslationService.TItem()` → JSON | `items_ru.json` |
| Теги гемов | `GameTranslationService.TGemTagLine()` → JSON | `gem_tags_ru.json` |
| Мета-строки тултипа гема | `GameTranslationService.TGemMetaLine()` → JSON | `gem_meta_ru.json` |
| Стат-описания гемов | `StatDescriptionEngine.Describe()` → JSON-шаблоны | `gem_stats_templates.json` |
| Стат-строки пассивных нод | `GameTranslationService.TPassiveStat()` → JSON | `passive_nodes_ru.json` |
| Названия пассивных нод | `GameTranslationService.TPassiveName()` → JSON | `passive_names_ru.json` |
| Классы и восхождения | `GameTranslationService.TClassName()` → JSON | `class_names_ru.json` |

---

## Покрытие (2026-06-12, дерево 0_5 / игра 0.5.1)

Измеряется `python tools/loc_coverage.py` (моделирует рантайм-фоллбэки lookup'а):

| Слой | Покрытие | Остаток |
|------|----------|---------|
| Имена нод дерева 0_5 | **100%** (1994/1994) | — |
| Стат-строки нод дерева 0_5 | **100%** (2625/2625) | — |
| Описания скиллов и саппортов | **99.7%** (976/979) | 3 внутренних монстро-скилла без RU в самой игре |

Что было сломано и как починено (сессия 2026-06-12):

1. **486 описаний саппортов не переводились вовсе** — `skill_descriptions_ru.json` строился только из `ActiveSkills.Description`; тексты саппортов живут в `GemEffects.SupportText`. Добавлен блок в `PBLExport/Program.cs`. Плюс: лукап `SkillDescription` теперь пробует `Trim()` (Lua-строки имеют хвостовые пробелы) и фоллбэк по longest-common-prefix от 50 символов — переживает дрифт формулировок, когда GGPK уходит на патч вперёд от upstream `Gems.lua`.
2. **253 стат-строки нод 0_5 отсутствовали** — `passive_nodes_ru.json` был от 24 мая (до дерева 0_5), а repoe-fork генератор сломан. Новый генератор **`PBLExport/gen_passive_nodes_csd.py`**: парсит GGPK `.csd` (EN+RU шаблоны в одном файле), матчит уже отрендеренные EN-строки tree.json по EN-шаблону, подставляет числа в RU-шаблон. Покрыл 254/254 (с учётом ручного словаря `HAND_FIXES` для статов, которые GGG сама ещё не локализовала — у их блоков в `.csd` нет языковых секций).
3. **Классы и восхождения** — рукописный словарь в `GameTranslationService` был неполным (не было Ranger, Monk и всех их восхождений; Spirit Walker/Martial Artist появились с 0_5) и местами неофициальным (Huntress — «Копейщица», а не «Охотница»; Warbringer — «Вестник войны»; Chronomancer — «Хрономант»...). Заменён на генерируемый **`class_names_ru.json`** из GGPK-таблиц `Characters` + `Ascendancy` (добавлены в `ggpk_export/config.json`). `PassiveName` получил фоллбэк в `ClassOrAscendancyName` — закрывает стартовые ноды восхождений (Invoker, Lich, Warbringer, Gemling Legionnaire).
4. **8 имён нод** (Bond of the Ape/Cat/Mamba/Owl/Viper/Wolf, Elemental, Physical) — в GGPK RU == EN (GGG не локализовала). Ручные переводы как supplemental в `Program.cs` через `TryAdd` — официальный перевод автоматически победит при появлении.

## ВЫПОЛНЕНО ✅

- [x] Все системные UI-строки (кнопки, вкладки, заголовки) — `Strings.resx` полная паритет EN/RU (96 ключей)
- [x] 968 названий гемов (`gems_ru.json`)
- [x] 3198 названий предметов (`items_ru.json`)
- [x] 46 тегов гемов (`gem_tags_ru.json`)
- [x] Мета-строки тултипа гема: Level, Cast Time, Cost, Reservation, Cooldown и пр. (`gem_meta_ru.json`)
- [x] **2491/2492 (99.96%) стат-описаний пассивных нод** (`passive_nodes_ru.json`) — см. раздел ниже
- [x] Движок `StatDescriptionEngine` — рендерит русские стат-строки гемов из repoe-fork шаблонов (`gem_stats_templates.json`)
- [x] Тултипы TreeCanvas — имя ноды + стат-строки (с переводом через PassiveStat)
- [x] **2721 названий пассивных нод** (`passive_names_ru.json`) — из GGPK `PassiveSkills.datc64` (после sync upstream 0.20.0: +467 нод)
- [x] **752 описания навыков/гемов** (`skill_descriptions_ru.json`) — флейвор-текст из GGPK `ActiveSkills.datc64` (после sync: +61)
- [x] **13163 шаблонов стат-описаний** (`gem_stats_templates.json`) — из repoe-fork (после sync: +6500, размер вырос вдвое до 2.4 MB)
- [x] Инструмент экспорта: `pathofexile-dat` (npm) + конфиг в `PBLExport/ggpk_export/config.json`
- [x] `PBLExport/Program.cs` — генерирует оба файла из GGPK-экспорта автоматически
- [x] **Персистентность языка между сессиями** — исправлено сохранение/восстановление выбранного языка

---

## Архитектура gen_passive_ru.py (важные нюансы)

Скрипт `PBLExport/gen_passive_ru.py` переводит `tree.json` стат-строки в русский через repoe-fork.

### Источник данных
- `tree.json` содержит **уже отрендеренные** английские стат-строки (не stat ID)
- repoe-fork хранит параметризованные шаблоны: stat ID → EN template → RU template
- Задача скрипта: для каждой EN строки найти подходящий шаблон, извлечь числа, отрендерить RU

### Фазы сопоставления

**Фаза 1 — точное совпадение по regex:**
- 23 файла stat_translations из repoe-fork (пассивные → гемы → общие)
- 24468 паттернов (после expand multi-line)
- Скипаются: 166 записей с несовместимыми форматами

**Фаза 2 — prefix-матч для усечённых строк:**
- tree.json обрезает длинные строки (> 30 символов без полного совпадения)
- Проверяем: regex совпадает ровно до конца строки stat

**Фаза 3 — "Grants Skill: X" постпроцессинг:**
- 45 записей вида `"Grants Skill: Acidic Concoction"`
- Переводятся как `"Дарует навык: {RU имя}"` через `gems_ru.json`

**Фаза 4 — "Grants N Passive Skill Point(s)" постпроцессинг:**
- 2 записи (`Grants 1 Passive Skill Point`, `Grants 4 Passive Skill Point`)
- Не присутствуют в repoe-fork вообще
- Переводятся хардкодом с русской PLU-формой: `"Даёт N очка/очков пассивного навыка"`

### Ключевые баги/нюансы repoe-fork

| Проблема | Причина | Исправление |
|----------|---------|-------------|
| `format: "ignore"` (string) вместо `["ignore"]` | repoe-fork shorthand | `normalise_format()` — всегда список |
| Многострочные шаблоны (`\n`) | repoe-fork хранит весь кейстон как одну строку | Сплит по `\n`, один паттерн на строку |
| Bullet-символы `◆ ● •` в начале строк | tree.json убирает их | `strip_bullet()` при обработке multi-line |
| `{1}`, `{2}` (не с нуля) в sub-lines | Индексы из общего format[], a не per-line | `remap_placeholders()` → {0}, {1},... |
| `format: "ignore"` для literal шаблонов | Шаблоны без `{N}` (напр. "Culling Strike") | `build_regex()` возвращает literal regex |

### Оставшиеся непереведённые (1 строка)
- `'100 Passive Skill Points become Weapon Set Skill Points'` — уникальный стат, отсутствует в любых repoe-fork stat_translations файлах. Предположительно хранится в `Words.datc64` (без schema для PoE2).

### Результат
```
Переведено: 2491/2492 (99.96%)
Непереведено: 1
Размер passive_nodes_ru.json: 355 КБ, 2491 запись
```

---

## Архитектура GGPK-экспорта

PoE2 не использует PoE1-формат GGPK с текстовыми файлами. Все данные в бинарных `.datc64` таблицах в Steam Bundles2.

### Инструмент
`pathofexile-dat` (npm CLI) — читает `_.index.bin` + Bundles2, экспортирует `.datc64` по схеме из `pathofexile-dat-schema`.

- **Важно:** `validFor: 1` = PoE1, `validFor: 2` = PoE2 в schema
- Запуск из `PBLExport/ggpk_export/`: `npx pathofexile-dat export --config config.json --output-dir tables`
- Конфиг: `PBLExport/ggpk_export/config.json` (steam path, таблицы, колонки)

### Таблицы, которые мы экспортируем

| Таблица | Что получаем | Использование |
|---------|-------------|---------------|
| `PassiveSkills` | Id, Name, PassiveSkillGraphId, Stats[], Stat1-5Value | passive_names_ru.json |
| `ActiveSkills` | Id, DisplayedName, Description | skill_descriptions_ru.json |
| `Stats` | Id (stat ID strings) | Маппинг индекс → stat ID |

### Таблицы без schema (PoE2)
- `Words.datc64` — слова/фразы для stat description engine; schema `validFor: 3` (не PoE2)
- `StatDescriptionFunctions.datc64` — функции рендеринга; аналогично нет schema

### Почему PoE2 нет stat description text files
В PoE1 stat descriptions хранились как `Metadata/StatDescriptions/*.txt`. В PoE2 эта система заменена на `Words.datc64` + `StatDescriptionFunctions.datc64`. Текстовые пути (`stat_descriptions.txt` и т.д.) больше не существуют в бандлах PoE2.

### Маппинг PassiveSkills → tree.json
- PassiveSkills: поле `PassiveSkillGraphId` (integer) = ID ноды в дереве
- tree.json: поле ноды `skill` (integer) = PassiveSkillGraphId
- Маппинг 1:1 через это поле; NPC/служебные ноды (без GraphId) не имеют соответствия

### Stats[] — числовые индексы
`PassiveSkills.Stats` — массив row-индексов в таблице `Stats` (не строковые ID).
Нужно экспортировать таблицу `Stats` с колонкой `Id` для получения строковых stat ID.

---

## ПРИОРИТЕТ 1 — Быстрые правки (< 1 часа) 🔴

### 1.1 Хардкод в BuildPageView.axaml
**Файл:** `PBLApp/Views/BuildPageView.axaml`, строки 49, 56, 63, 70, 77, 84

Строки `"DPS:"`, `"Life:"`, `"ES:"`, `"Mana:"`, `"Armour:"`, `"Eva:"` хардкодены.

**Решение:** добавить 6 ключей в `Strings.resx` / `Strings.ru.resx` и заменить на `{loc:Tr ...}`.

Предлагаемые ключи и переводы:
```
Stat_DPS    = "DPS"          / "ДПС"
Stat_Life   = "Life"         / "Жизнь"
Stat_ES     = "ES"           / "ЩЭ"
Stat_Mana   = "Mana"         / "Мана"
Stat_Armour = "Armour"       / "Броня"
Stat_Eva    = "Eva"          / "Укл."
```

### 1.2 Хардкод в CalcsTabView.axaml
**Файл:** `PBLApp/Views/CalcsTabView.axaml`, строка 285

Строка `Text="Type"` в заголовке столбца таблицы модификаторов.

**Решение:** добавить ключ `Calc_ColType = "Type" / "Тип"` и заменить.

---

## ПРИОРИТЕТ 2 — Средние задачи (несколько часов) 🟡

### ~~2.1 / 2.2 Стат-описания гемов~~ ✅ ВЫПОЛНЕНО 2026-06-13 (источник переключён repoe-fork → GGPK .csd)
**Что было сломано:** repoe-fork **убрал русскую локализацию** stat_translations — путь `/poe2/Russian/...` теперь отдаёт 404 (английский `/poe2/...` ещё 200). Поэтому `gen_gem_stats_ru.py` молча генерировал EN-only: в шипнутом `gem_stats_templates.json` (13163 записи) было **0 RU-вариантов**, и статы гемов в тултипах практически не переводились (116/289 строк на английском, билд Witch-Infernalist).

**Решение:** новый генератор **`PBLExport/gen_gem_stats_csd.py`** — берёт RU из тех же GGPK `.csd` (StatDescriptions), что и `gen_passive_nodes_csd.py`. Блоки `.csd` ключатся по stat-id и содержат EN+RU шаблоны с той же моделью плейсхолдеров/handler'ов/условий, что понимает `StatDescriptionEngine`, поэтому скрипт эмитит **ту же схему** (`i`/`en`/`ru`, `t`/`f`/`h`/`c`). Merge сохраняет прежние записи для id-tuple, которых нет в `.csd` (без регрессии). Это закрыло и generic stat_descriptions (2.2) — он среди `.csd` (приоритет последний).

**Результат:** 13167 записей, **12984 с RU** (было 0), файл 2.36→4.97 МБ. Непереведённых строк тултипов: 116→37, из них реально английских всего **5** (спецстаты одного навыка Demon Form).

**Остаток (нужен ре-экспорт GGPK):** per-skill оверрайды (`specific_skill_stat_descriptions/*.csd`, напр. «… while in Demon Form») не входят в `ggpk_export/config.json` "files" → не выгружены. `gen_gem_stats_csd.py` уже авто-подхватывает `*specific_skill_stat_descriptions*.csd` (высший приоритет): добавить их в экспорт, `npx pathofexile-dat export`, затем перезапустить генератор — гэп закроется без правок кода.

Регенерация: `python PBLExport/gen_gem_stats_csd.py` (нужны только локальные `.csd` в `ggpk_export/files/`).

### ~~2.3 Флейвор-текст гемов (описание лора)~~ ✅ ВЫПОЛНЕНО
`skill_descriptions_ru.json` — 691 запись из GGPK `ActiveSkills.datc64`. Тултип гема теперь показывает описание на русском (`GameTranslationService.TSkillDescription`).

### ~~2.4 Названия пассивных нод дерева~~ ✅ ВЫПОЛНЕНО
`passive_names_ru.json` содержит 2254 записи из GGPK `PassiveSkills.datc64`. Регенерация: `cd PBLExport/ggpk_export && pathofexile-dat`, затем `dotnet run --project PBLExport`.

---

## ПРИОРИТЕТ 3 — Крупные задачи (требуют GGPK-доступа) 🔵

### ~~3.1 Полный перевод названий пассивных нод~~ ✅ ВЫПОЛНЕНО
2254 нод из GGPK (было 24). Покрытие ~98%.

### 3.2 Перевод флейвор-текстов (гемы, предметы)
Описания лора гемов, уникальных предметов — требуют GGPK.

### 3.3 CalcsTab — секции и строки
**Проверить:** все ли названия секций/строк в CalcsTabView переведены.
Часть строк может приходить из Lua как английские ключи (`output["PlayerStat"]`).
Нужна аудитория что Lua возвращает ключи или уже переведённые строки.

### 3.4 Items tab — implicit/explicit моды предметов
Тексты модов предметов ("+10 to Strength") — приходят из Lua.
Теоретически могут переводиться через `passive_nodes_ru.json` (тот же формат),
но тестирование не проводилось.

---

## Файлы генерации переводов

| Скрипт | Что генерирует | Источник данных |
|--------|---------------|----------------|
| `PBLExport/gen_passive_nodes_csd.py` | дополняет `passive_nodes_ru.json` | **GGPK .csd** (EN+RU шаблоны; канонический путь) |
| `PBLExport/gen_passive_ru.py` | `passive_nodes_ru.json` | repoe-fork stat_translations — **сломан** (формат изменился), заменён csd-генератором |
| `PBLExport/gen_passive_names_ru.py` | `passive_names_ru.json` | repoe-fork skills.min.json — **сломан** (404), заменён Program.cs |
| `PBLExport/gen_gem_stats_csd.py` | `gem_stats_templates.json` | **GGPK .csd** (EN+RU; канонический — repoe-fork RU теперь 404) |
| `PBLExport/gen_item_mod_templates_csd.py` | `item_mod_templates_ru.json` (17201 шаблонов модов предметов; грузится как `_itemModTemplatesCsd`, OrdinalIgnoreCase) | **GGPK .csd** (number-redacted EN→RU; симулирует рантайм-редакцию `TooltipLine`) |
| `PBLExport/gen_gem_stats_ru.py` | `gem_stats_templates.json` | repoe-fork — **RU удалён (404)**, заменён csd-генератором |
| `PBLExport/Program.cs` | `passive_names_ru.json`, `skill_descriptions_ru.json` (ActiveSkills + GemEffects.SupportText), `class_names_ru.json` (Characters + Ascendancy) | GGPK tables (pathofexile-dat export) |
| `tools/loc_coverage.py` | — (метрика покрытия трёх слоёв) | tree.json + Skills/*.lua против Translations/*.json |

> ⚠️ **Источник RU: GGPK `.csd`, НЕ repoe-fork.** repoe-fork удалил русскую локализацию
> (`https://repoe-fork.github.io/poe2/Russian/...` → 404; английский `/poe2/...` ещё жив).
> Все RU-генераторы на repoe-fork (`gen_passive_ru.py`, `gen_passive_names_ru.py`,
> `gen_gem_stats_ru.py`) **устарели** — RU теперь тянется из `.csd`, выгружаемых
> pathofexile-dat в `PBLExport/ggpk_export/files/` (см. `config.json` → "files").

Для регенерации при обновлении данных игры:
```bash
# 1. Из GGPK (нужен Steam с PoE2) — выгружает .csd (EN+RU) и таблицы
cd PBLExport/ggpk_export
npx pathofexile-dat export --config config.json --output-dir tables
cd ..

# 2. Таблицы → passive_names / skill_descriptions / class_names / items и т.д.
dotnet run --project .                 # PBLExport/Program.cs

# 3. .csd → стат-шаблоны (только локальные .csd, без интернета)
python gen_passive_nodes_csd.py        # passive_nodes_ru.json
python gen_gem_stats_csd.py            # gem_stats_templates.json (RU из .csd)
python gen_item_mod_templates_csd.py   # item_mod_templates_ru.json (RU из .csd)
```

---

## Регенерация переводов из GGPK

При обновлении игры нужно повторить:

```bash
# 1. Обновить GGPK-экспорт
cd PBLExport/ggpk_export
npx pathofexile-dat export --config config.json --output-dir tables
# Экспортирует: PassiveSkills, Stats, ActiveSkills (EN + RU) в tables/

# 2. Пересобрать все Translation-файлы
cd ..
dotnet run --project .                 # PBLExport/Program.cs → PBLApp.Core/Translations/
python gen_passive_nodes_csd.py        # стат-строки нод (RU из .csd)
python gen_gem_stats_csd.py            # стат-шаблоны гемов (RU из .csd)
python gen_item_mod_templates_csd.py   # шаблоны модов предметов (RU из .csd)
```

> Чтобы закрыть per-skill оверрайды (напр. «… while in Demon Form»): добавить
> `Data/StatDescriptions/specific_skill_stat_descriptions/*.csd` в `config.json` "files",
> ре-экспортнуть, перезапустить `gen_gem_stats_csd.py` (он их авто-подхватит).

---

## Архитектура переключения языка (важные нюансы)

### Ловушка: порядок статических полей в C#

Все три singleton-сервиса локализации попали в одну ловушку: `Instance = new()` стояло первым статическим полем, а поля, используемые в конструкторе, — после. Static fields инициализируются строго в порядке объявления. Конструктор запускался до их инициализации.

| Класс | Поле (нужно в конструкторе) | Симптом |
|-------|---------------------------|---------|
| `LocalizationService` | `SettingsFile` (путь к language.txt) | `File.Exists("")` → всегда "en", язык не сохранялся |
| `GameTranslationService` | `Asm` (Assembly для embedded resources) | `Asm.GetManifestResourceStream()` → NRE в catch → пустые словари, всё по-английски |

**Правило:** в каждом из этих классов `Instance = new()` должно стоять **последним** среди статических полей (или хотя бы после всех полей, используемых в конструкторе).

### Avalonia ComboBox и SelectionChanged

Avalonia сбрасывает `SelectedIndex` в 0 при первом рендеринге ComboBox (если не указан `SelectedItem`/`SelectedIndex` в XAML). Это происходит ПОСЛЕ `Loaded`, поэтому синхронизировать через `SelectedIndex = i` в конструкторе или в `Loaded` — бесполезно.

**Решение:**
- Убрать `SelectionChanged` из XAML.
- Подписаться на `DropDownClosed` в коде (стреляет только при реальном выборе пользователя).
- Синхронизировать `SelectedIndex` через `Dispatcher.UIThread.Post(..., DispatcherPriority.Render)` — выполняется после рендеринга, когда Avalonia уже не сбросит значение.
- Подписываться на `LanguageChanged` → `SyncLangCombo` только в `OnLoaded`; отписываться в `OnUnloaded`.

### Eager initialization GameTranslationService

`_ = GameTranslationService.Instance` в `App.axaml.cs.OnFrameworkInitializationCompleted()` — инициализирует сервис до создания любых View. Это гарантирует:
1. `Load("ru")` вызывается сразу (если язык уже "ru" из файла).
2. Подписка на `LanguageChanged` активна с момента старта.
3. Когда `SkillsTabViewModel` создаётся (асинхронно, после загрузки билда), `_gems`/`_passiveNames` уже заполнены.

---

## Оставшиеся ограничения

- **1 непереведённая стат-строка:** `'100 Passive Skill Points become Weapon Set Skill Points'` — отсутствует в repoe-fork. Вероятно, в `Words.datc64` (no schema для PoE2).
- `Words.datc64` и `StatDescriptionFunctions.datc64`: содержат raw текстовые данные stat description engine, но community dat-schema не имеет определений колонок для PoE2 (только `validFor: 3`). Без schema экспортировать через `pathofexile-dat` нельзя.
- Описания уникальных предметов — не в `ActiveSkills.datc64`, нужен другой источник (GGPK `UniqueStashLayout` / `Words.datc64`)
- 187 нод без имён в GGPK (служебные/устаревшие — не нужны)

---

*Обновлено: 2026-06-13*

<!-- Changelog:
2026-05-24 — Исправлены баги языкового сервиса:
  - LocalizationService: SettingsFile перемещён выше Instance (порядок статических полей)
  - GameTranslationService: Asm перемещён выше Instance (та же ловушка)
  - BuildListView/BuildPageView: SelectionChanged → DropDownClosed + Dispatcher.Post(Render)
  - App.axaml.cs: eager init GameTranslationService.Instance
-->
