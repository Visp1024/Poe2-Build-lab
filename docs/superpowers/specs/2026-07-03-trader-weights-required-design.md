# Трейдер: настраиваемые веса статов (как в PoB) + Required stats

**Дата:** 2026-07-03
**Статус:** одобрено (дизайн)
**База:** вкладка «Трейдер» (Phase 18, `docs/superpowers/specs/2026-07-03-trader-tab-design.md`)

## Решения, принятые с пользователем

- **Веса статов:** полноценный настраиваемый список как в оригинальном
  «Adjust search weights» (все статы из `data.powerStatList`, множитель 0–1,
  0 = выключен), вместо жёсткой пары DPS/EHP.
- **Required stats:** ручные мин-фильтры как на trade-сайте («and»-группа в
  запросе), **пер-слот**, выбор из **полного списка trade-статов категории
  слота** (Data/QueryMods.lua) с поиском.
- **Персист:** веса — в XML билда тем же узлом, что у оригинала
  (`TradeSearchWeights`, совместимость с PoB); required — только сессия билда.

## 1. Веса статов

**Lua (`PBLEngine/lua/trader.lua`):**
- `PBLTrader.GetWeightStatsJson()` → `[{stat, label}]` из `data.powerStatList`,
  фильтр `not stat.ignoreForItems and label ~= "Name"` (как попап оригинала,
  TradeQuery.lua:635-647). `transform`-статы помечаются флагом `hasTransform`;
  при сборке `statWeights` в Lua transform подставляется из powerStatList по
  ключу (C# передаёт только stat+weightMult).
- Загрузка/сохранение узла `TradeSearchWeights` — тем же механизмом, что
  оригинальный ItemsTab (точные вызовы фиксируются в плане реализации);
  headless-обвязка читает список при открытии билда и пишет при изменении.

**C# / UI:**
- В панели настроек кнопка «Настроить веса… (N)» вместо пары NumericUpDown;
  флайаут: поиск по названию, список статов с NumericUpDown 0–1 (шаг 0.05,
  0 = выключен), локализованные подписи (пайплайн перевода calc-меток),
  кнопка «Сброс» → дефолт FullDPS=1.0 / TotalEHP=0.5.
- Пресеты «Урон/Выживаемость/Баланс» остаются и просто заполняют список
  (FullDPS/TotalEHP c соответствующими множителями).
- Активные статы → `options.statWeights` генерации, расчёт диффов и «ценности».
- `StatWeightsJson`/`OptionsJson` собираются из списка; формат entry:
  `{stat, weightMult}` (+ transform подставляет Lua).

## 2. Required stats

**Lua:**
- `PBLTrader.GetTradeStatsJson(slotName)` → `[{id, text}]` — trade-статы,
  доступные категории слота: `getTradeCategory(slotName)` → категория →
  записи `generator.modData[type]` (Explicit/Implicit), у которых есть данные
  этой категории. Отсортировано по text, без дублей id.
- `PBLTrader.ApplyRequiredStats(queryJson, requiredJson)` → queryJson′:
  dkjson-декод, в `query.stats` добавляется группа
  `{type="and", filters=[{id=..., value={min=...}}]}` (min опускается, если
  не задан — «стат обязан присутствовать»), энкод. Генератор и src/Classes
  не меняются.

**C# / UI:**
- У строки слота кнопка «Фильтры (N)»: флайаут с поиском по статам категории,
  выбранные — список «стат + мин. значение (пустое = любое)» с удалением.
- Тексты статов переводятся существующим пайплайном перевода модов
  (best-effort; непереведённые — EN).
- При поиске: `GenerateTradeQueryAsync` → если у слота есть required —
  `ApplyRequiredStats` → дальше обычный конвейер. «Открыть на trade-сайте»
  использует уже модифицированный query.
- Живут в VM строки слота (сессия билда), не сохраняются.

## Ошибки и крайние случаи

- Все веса = 0 → «Найти апгрейды» задизейблена, подсказка «выберите хотя бы
  один стат».
- Слот без trade-категории → кнопка «Фильтры» скрыта.
- Пустой min → фильтр без value (наличие стата обязательно).
- Битый `TradeSearchWeights` в XML → тихий откат к дефолту.

## Тесты

- Lua через DoString: `GetWeightStatsJson` содержит FullDPS и TotalEHP;
  `GetTradeStatsJson("Helmet")` непуст и содержит id вида `explicit.stat_*`;
  `ApplyRequiredStats` добавляет and-группу и сохраняет weight-группу.
- C#: генерация + required → в итоговом JSON обе stats-группы; VM — изменение
  весов меняет `OptionsJson`; round-trip весов через сохранение билда.
- Финал — `/pbl-verify` со скриншотами флайаутов.

## Вне скоупа

- Персист required-фильтров.
- Max-значения фильтров (только min).
- Взвешивание required-статов (они только фильтруют).
