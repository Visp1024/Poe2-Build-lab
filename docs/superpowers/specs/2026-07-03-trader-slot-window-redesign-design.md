# Трейдер: переход от вкладки к пер-слот окну подбора — дизайн

**Дата:** 2026-07-03
**Ветка:** `feat/trader-tab` (продолжение — прежняя нес­мёрженная работа по трейдеру замещается этим редизайном)
**Статус:** утверждён пользователем, готов к плану

## Проблема

Текущая фича «Трейдер» живёт отдельной вкладкой (index 5) со списком всех 18 слотов.
Пользователь хочет иное взаимодействие:

1. Отдельная вкладка уходит целиком.
2. Под каждым слотом предмета — кнопка подбора.
3. По клику открывается отдельное окно подбора, нацеленное именно на этот слот.
4. Поработать над оформлением окна (house-style + аккуратные трейд-акценты).

**Ключевой принцип:** движковый и Lua-слой (генератор запроса, поиск, дифф, веса,
required-фильтры, OAuth) **не меняются** — переиспользуются как есть. Меняется только
слой представления: снос вкладки, полоска подбора на слоте, новое окно, расщепление
единого `TraderTabViewModel` на общую сессию + пер-слот VM.

## Утверждённые решения (из брейншторма)

| Развилка | Решение |
|---|---|
| Как вызывать подбор | Всегда видимая полоска «🔍 Подбор» **внутри** ячейки слота (у нижнего края) |
| Чем открывать | Отдельное **немодальное** окно (паттерн Заметок/Настроек) |
| Логин + лига | В **шапке окна подбора**, общие для всех слотов (глобальное состояние) |
| Веса статов | **Общие на билд** (как в PoB), персист в XML `TradeSearchWeights` |
| Раскладка окна | Компакт: панель управления сверху (лига/логин, `[Веса]`/`[Фильтры]`/цена/`Искать`), результаты во всю ширину под ней |
| Оформление | House-style + аккуратные акценты (цветные бейджи ΔDPS/ΔEHP, карточки, иконки валют) |
| Инстансы окна | **Один** инстанс, перенацеливается на новый слот при клике другого «Подбор» |

## Архитектура

### Разделение состояния

Текущий `TraderTabViewModel` расщепляется на два уровня:

- **`TraderSession`** — один на страницу билда, держит **глобальное** состояние трейдера:
  - `PoeOAuthService` (логин/токены),
  - выбранная лига + список лиг (`TraderWebApi.GetLeaguesAsync`),
  - курсы валют (`TraderWebApi.GetCurrencyRatesAsync`, кэш),
  - **веса статов** — общие на билд (`WeightStats`, push в `Host.SetTraderWeights`, персист
    в XML `TradeSearchWeights` через существующий `ItemsTab`-сериализатор — совместимость с
    оригинальным PoB сохраняется),
  - словарь required-фильтров по слотам `слот → List<RequiredFilter>` (сессия; переоткрытие
    слота в рамках сессии восстанавливает его фильтры),
  - ссылка на `LuaHost` и вся его trader-обвязка (`LuaHostTrader`: генератор, поиск, дифф,
    required) — **без изменений**.
- **`TraderWindowViewModel`** — пер-слот VM (DataContext окна):
  - целевой слот (`SlotName`, локализованный заголовок),
  - ссылка на `TraderSession` (лига/логин/веса/курсы берутся оттуда),
  - required-фильтры для текущего слота (проксируют в `session.RequiredBySlot[slot]`),
  - результаты поиска (`ObservableCollection<TraderResultViewModel>`),
  - `SearchCommand`, `IsSearching`, статус/ошибки,
  - при перенацеливании (`Retarget(slotName)`): меняет слот, чистит результаты,
    подтягивает required и доступные статы новой категории.

`TraderSession` создаётся лениво при первом открытии окна (как раньше `TraderTab` — его
конструктор гоняет заметную Lua-работу: `QueryMods` + скан статов слота). Живёт на
`BuildPageViewModel`.

### Владение окном и перенацеливание

В `BuildPageView.axaml.cs` — поле `TraderWindow? _traderWindow` и метод `OpenTrader(slotName)`
по образцу `OpenNotes_Click`:

```
OpenTrader(slotName):
  vm.EnsureTraderSession()                 // ленивое создание сессии
  if _traderWindow != null:
     _traderWindow.DataContext.Retarget(slotName)   // перенацелить
     _traderWindow.Activate(); return
  var winVm = new TraderWindowViewModel(vm.TraderSession, slotName)
  _traderWindow = new TraderWindow { DataContext = winVm }
  _traderWindow.Closed += (_,_) => _traderWindow = null
  _traderWindow.Show(owner)
```

Позиция/размер окна персистятся паттерном Phase 15 (как прочие окна).

### Снос вкладки

- Удалить `PBLApp/Views/TraderTabView.axaml` + `.axaml.cs`.
- В `PBLApp/Views/BuildPageView.axaml` — убрать RadioButton-вкладку Trader.
- В `BuildPageViewModel`:
  - `TabKeys` → `["Items", "Tree", "Skills", "Calcs", "Config"]` (без "Trader"),
  - убрать `IsTraderTab`, `CurrentTabContent` case 5, `TraderTab`-свойство, pop-out для Trader
    (`IsTraderPoppedOut`, ветки в `IsTabPoppedOut`/`SetTabPoppedOut`),
  - убрать ленивое создание `TraderTab` в `OnSelectedTabIndexChanged` (index 5),
  - добавить `TraderSession? TraderSession` + `EnsureTraderSession()`.
- `TraderTabViewModel` → переименовать/расщепить в `TraderSession` + `TraderWindowViewModel`.
  Классы-хелперы (`TraderResultViewModel`, `TraderWeightEntryViewModel`,
  `TraderRequiredFilterViewModel`, `TraderAvailableStatViewModel`) переезжают как есть.

### Полоска подбора на слоте

**Ограничение:** слоты упакованы на `Canvas 540×530`, места **между** ячейками нет.
Поэтому полоска рисуется **внутри** ячейки у нижнего края (оверлей поверх нижней кромки
иконки), а не под ней.

- Полупрозрачная строка (`VerticalAlignment=Bottom`) высотой ~16px во всю ширину ячейки:
  фон `BgMantle` с прозрачностью, `🔍 Подбор` на широких слотах, только `🔍` на узких
  (кольца 70×70, талисманы 50×100, пояс 145×60).
- Отдельный `PointerPressed`-обработчик на полоске с `e.Handled = true`, чтобы клик не
  всплывал в тунельный slot-select. Обработчик читает `slot:*` из `Tag` родительской ячейки
  и зовёт `OpenTrader(slot)`.
- Показывается на всех слотах независимо от icon-build.
- Точную посадку (высота, узкие слоты icon-only, читаемость поверх иконки) выверить
  скриншотами через `/pbl-verify`; при тесноте на кольцах/талисманах — fallback на icon-only.
- Полоска добавляется в существующие slot-ячейки (`Border.slot-cell`) в `ItemsTabView.axaml`
  — общий стиль + узкий вариант; VM слота (`ItemSlotViewModel`) не трогаем, имя слота уже в `Tag`.

### Визуальный ориентир

Утверждённый HTML-макет (5 экранов: полоска подбора на слотах, окно до поиска, окно с
результатами, флайаут весов, флайаут фильтров) собран в точных токенах Dark-темы приложения
и одобрен пользователем. Финальный Avalonia-UI целится в него; выверка через `/pbl-verify`.

### Окно подбора (раскладка)

```
┌─ Подбор: Шлем ────────────────────────────────┐
│ Лига ▾   Вы: Account   [Войти/Выйти]           │  ← шапка (из session)
│ [Веса (2)] [Фильтры (1)]   Цена ≤ [20] [div ▾] │  ← панель управления
│                                     [ Искать ]  │
├────────────────────────────────────────────────┤
│ РЕЗУЛЬТАТЫ (во всю ширину)                      │
│ ┌────────────────────────────────────────────┐ │
│ │ Шлем A          12 div   ΔDPS +5%  ΔEHP +2% │ │
│ │ Продавец  [Примерить] [Whisper] [На сайте]  │ │
│ └────────────────────────────────────────────┘ │
│ …                                              │
└────────────────────────────────────────────────┘
```

- **`[Веса (N)]`** — флайаут с редактором **общих** весов (поиск стата, множители 0–1,
  пресеты DPS/EHP/баланс, сброс). Правки идут в `session` → `Host.SetTraderWeights` → XML.
- **`[Фильтры (N)]`** — флайаут required-фильтров категории **текущего слота** (поиск,
  выбранные с min и ✕, полный список статов категории). Хранятся в `session.RequiredBySlot`.
- Результаты: карточки house-style, ΔDPS/ΔEHP цветными бейджами (`StringFormat='+0.#;-0.#;0'`),
  цена + иконка/код валюты, value/div, продавец, `[Примерить] [Whisper] [Открыть на сайте]`.
- «Открыть на сайте»: `ru.pathofexile.com` при RU, иначе `www.` (как раньше).

### Поток поиска (без изменений в движке)

`SearchCommand` повторяет текущий `TraderSlotRowViewModel.SearchAsync`:
гейт «есть активные веса» → `GenerateTradeQueryAsync(slot)` → патч required через
`ApplyRequiredStatsAsync` → гейт логина → `SearchTradeAsync` → диффы (`ComputeListingDiffAsync`)
→ сортировка по value/div. Отличие лишь в том, что VM теперь пер-слот, а не строка в списке.

### Edge-cases

- **Пустой слот:** подбор работает. Категория берётся из имени слота
  (`tradeHelpers.getTradeCategory(slotName, item)` с fallback при `item == nil`),
  базлайн диффа — пустой предмет (дельта = полный вклад кандидата). Проверить, что генератор
  корректно строит запрос без экипированного предмета; при необходимости — минимальная
  правка только в Lua-glue (`trader.lua`), не в неизменяемых `src/Modules`/`src/Classes`.

### IPC / MCP

- `/trader/state`, `/trader/search`, `/trader/set-league` перенацелить на **активное окно**:
  - `visual_trader_open(slot)` — новый: зовёт `OpenTrader(slot)` (открыть/перенацелить окно),
  - `state`/`search`/`set-league` работают с `_traderWindow?.DataContext` (или сессией для лиги),
  - если окно не открыто — `state` возвращает `{ open: false }`.
- `PBLMcp/VisualTools.cs`: `VisualTraderOpen` (новый), `VisualTraderState/Search/SetLeague`
  адаптировать под окно.

## Тестирование

**Остаются без изменений** (логика движка/OAuth не меняется):
`TraderHttpTests`, `TraderInitTests`, `TraderGenerateTests`, `TraderSearchTests`,
`TraderDiffTests`, `TraderWebApiTests`, `PoeOAuthTests`.

**Мигрируют** с `TraderTabViewModel` на новые VM:
- `TraderWeightsTests` → на `TraderSession` (веса — глобальные, персист).
- `TraderRequiredTests` → на `TraderWindowViewModel` (required пер-слот, `RequiredBySlot`).
- `TraderTabViewModelTests` → расщепить: сессионные проверки (лига/логин/веса/курсы) на
  `TraderSession`, слотовые (поиск/дифф/сортировка/перенацеливание) на `TraderWindowViewModel`.

**Новые:**
- `TraderWindowViewModel.Retarget` чистит результаты и подтягивает required нового слота.
- Пустой слот: поиск строит валидный запрос (категория по имени слота).

Оформление окна и полоски — навык `frontend-design` + обязательный `/pbl-verify` со
скриншотами (полоска на разных слотах, окно с результатами, флайауты весов/фильтров).

## Вне области

- Мульти-окна (несколько окон подбора одновременно) — отклонено, один перенацеливаемый инстанс.
- Пер-слот веса — отклонено, веса общие на билд.
- Пакетный обзор всех слотов сразу — уходит вместе с вкладкой (осознанно).
- Изменения в `src/Modules/*.lua`, `src/Classes/*.lua`, движковых калк-файлах — не трогаем.
