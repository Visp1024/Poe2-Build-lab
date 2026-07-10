# Дизайн: конфигурируемая тема приложения (Dark / Light / System)

**Дата:** 2026-07-11
**Статус:** утверждён к реализации
**Итерация:** уровень 2 из плана «темизация». Полная кастомизация цветов пользователем — **отдельная будущая итерация**, здесь не делается.

## Цель

Дать пользователю выбирать оформление приложения PBLApp: **тёмная**, **светлая** или **системная** (следует за темой Windows). Выбор сохраняется между запусками и применяется **мгновенно**, без перезапуска.

## Контекст (текущее состояние)

- UI-фреймворк: **SukiUI 7.0.1** поверх Avalonia 12.0.3, `net9.0`.
- Тема жёстко зафиксирована: `RequestedThemeVariant="Dark"` в `PBLApp/App.axaml`. Механизма переключения нет.
- **Токены цветов уже написаны для обоих вариантов** в `PBLApp/Themes/Tokens.Colors.axaml` через `ResourceDictionary.ThemeDictionaries` с ключами `x:Key="Dark"` и `x:Key="Light"`. Идентичный набор ключей, на каждый `Color` — парный `SolidColorBrush` с суффиксом `Brush`. Это база: светлая палитра токенов существует.
- Persistence готов: `PBLApp.Core/AppPreferences.cs` — key→value стор в `%LOCALAPPDATA%\PathOfBuilding\prefs.json` (`Get/Set/GetBool/SetBool`).
- Правильный theme-aware паттерн уже есть: `BrushKeyConverter` в `PBLApp/Converters/TooltipKindConverter.cs` резолвит brush через `app.TryGetResource(key, app.ActualThemeVariant, ...)`.
- Отдельного окна настроек приложения **нет**: `PBLApp/Views/SettingsWindow.axaml` хостит `ConfigTabViewModel` (внутриигровой конфиг билда PoB), к теме отношения не имеет.
- Хардкод цветов в обход токенов: ~28 точек в XAML-вьюхах + пласт в C# (`TreeCanvas.cs` 84, `TreeTabView.axaml.cs` 18, `StyledHoverTooltip.cs` 11, `TooltipKindConverter` badge-палитра 9).

## Решения (согласовано с пользователем)

1. **Размещение переключателя:** новое **окно настроек приложения** (`AppSettingsWindow`), не трогаем существующий `SettingsWindow`. Точка входа — кнопка-шестерёнка в шапке `BuildListView` рядом с селектором языка.
2. **Варианты темы:** Dark / Light / **Системная** (три). Системная = Avalonia `ThemeVariant.Default` (следует за Windows).
3. **Дерево пассивок (`TreeCanvas.cs`):** в этой итерации **остаётся тёмным**. Рендер не переписываем. Полноценная адаптация полотна — будущая итерация.
4. **Объём чистки хардкода:** только **видимые основные экраны** (список билдов, Items, Skills, Calcs и связанные диалоги). Редкие места (Trader, второстепенные диалоги) помечаются как отложенные, чистятся позже.
5. **По умолчанию:** тема при первом запуске — **Системная**. Применение — **мгновенное** (runtime `RequestedThemeVariant`, без перезапуска).

## Архитектура

### Компонент 1 — `ThemeService`

Единственная точка управления темой. Расположение: `PBLApp` (нужен доступ к `Application.Current`; в `PBLApp.Core` нет ссылки на Avalonia).

```
enum AppThemeMode { System, Dark, Light }

class ThemeService
    AppThemeMode Current { get; }
    void InitializeAtStartup()      // читает prefs, применяет до показа окна
    void Apply(AppThemeMode mode)   // выставляет RequestedThemeVariant + пишет prefs
```

- Маппинг: `System → ThemeVariant.Default`, `Dark → ThemeVariant.Dark`, `Light → ThemeVariant.Light`.
- Хранение: `AppPreferences` ключ `AppTheme`, значения `"System"|"Dark"|"Light"`. Отсутствует/неизвестно → `System`.
- Один источник правды; ViewModel и App дёргают только его.

**Что делает:** централизует чтение/запись выбора темы и применение к приложению.
**Как использовать:** `InitializeAtStartup()` при старте; `Apply(mode)` при выборе в UI.
**Зависимости:** `AppPreferences`, `Application.Current`.

### Компонент 2 — инициализация в `App`

- `PBLApp/App.axaml`: **убрать** `RequestedThemeVariant="Dark"` из корневого `<Application>`.
- `PBLApp/App.axaml.cs` → `OnFrameworkInitializationCompleted`: вызвать `ThemeService.InitializeAtStartup()` до присваивания `MainWindow`, чтобы окно сразу поднялось в нужном варианте (без вспышки).

### Компонент 3 — `AppSettingsWindow` + `AppSettingsView`

- Новое окно `PBLApp/Views/AppSettingsWindow.axaml` (+ code-behind). SukiUI-стиль окна как у существующих модальных окон.
- Содержимое в этой итерации — одна секция **«Оформление»**: селектор темы.
- Селектор — по канону из памяти `dropdown-style-canonical`: обычный SukiUI `ComboBox`, локализованный список строк + `SelectedIndex` биндинг, `MaxWidth` против клипа. Без Background/template-override.
- Задел на будущее: в это окно позже добавится секция кастомизации цветов; структура — вертикальный стек секций.

### Компонент 4 — `AppSettingsViewModel` (PBLApp.Core)

- `[ObservableProperty] int SelectedThemeIndex` (0=Системная, 1=Тёмная, 2=Светлая — порядок фиксируем и мапим на enum).
- В `PBLApp.Core` нет ссылки на Avalonia, поэтому применение темы делается **через абстракцию**: VM принимает делегат/интерфейс `IThemeSwitcher { void Apply(AppThemeMode) }`, реализуемый `ThemeService` в `PBLApp`. Это сохраняет `PBLApp.Core` свободным от Avalonia и тестируемым.
- Локализованные подписи вариантов берутся через существующий `LocalizationService`.

### Компонент 5 — точка входа в шапке `BuildListView`

- Кнопка-шестерёнка рядом с селектором языка (`BuildListView.axaml` / `.axaml.cs`), открывает `AppSettingsWindow` как модальное окно поверх главного.

### Компонент 6 — чистка хардкода (видимые экраны)

Замена хардкода на ссылки на токены, чтобы элементы переживали смену темы.

**XAML → `DynamicResource`** (приоритетные видимые вьюхи):
- `BuildListView.axaml` — `Foreground="White"` ×2, hex ×2.
- `ItemsTabView.axaml` — hex ×4.
- `ItemEditorView.axaml` — hex ×9 (в т.ч. «corrupted» `#DD0022`).
- `SkillsTabView.axaml` — `Foreground="White"`, hex.
- `CalcsTabView.axaml` — hex.
- `ConfirmDialog.axaml`, `PromptDialog.axaml` — `Foreground="White"`.
- Где подходящего токена нет — добавить ключ **в оба варианта** (`Dark` и `Light`) в `Tokens.Colors.axaml`.

**C# → `TryGetResource(key, ActualThemeVariant)`** (по образцу `BrushKeyConverter`), для элементов на видимых экранах:
- `StyledHoverTooltip.cs` — константы цветов (11).
- `TooltipKindConverter.cs` — `RuneAugTypeBrushConverter` badge-палитра (9).
- `TreeTabView.axaml.cs` — кисти UI-панелей поверх дерева (18). *(само полотно дерева — вне scope.)*

**Отложено (не в этой итерации, пометить TODO-комментарием):**
- `TraderWindow.axaml` — hex ×4 (градиенты/оверлей).
- `TreeCanvas.cs` — вся палитра рендера дерева (остаётся тёмной; допускается лёгкая группировка палитры в один именованный блок с комментарием-планом на будущую token-resolve адаптацию — **без** переписывания рендера).

## Поток данных

```
Старт → App.OnFrameworkInitializationCompleted
      → ThemeService.InitializeAtStartup()
      → AppPreferences.Get("AppTheme") → mode
      → Application.Current.RequestedThemeVariant = map(mode)
      → окно поднимается в нужном варианте

Пользователь → шестерёнка в шапке → AppSettingsWindow
      → выбор в ComboBox → AppSettingsViewModel.SelectedThemeIndex
      → IThemeSwitcher.Apply(mode)
      → ThemeService.Apply: RequestedThemeVariant = map(mode) + AppPreferences.Set("AppTheme", mode)
      → DynamicResource-потребители перекрашиваются мгновенно
```

## Обработка ошибок

- Неизвестное/битое значение в prefs → фолбэк на `System`, без исключения.
- Ошибка записи prefs → тема всё равно применяется в рантайме (не блокирует UI); ошибка логируется как в существующем `AppPreferences`.
- C#-резолв через `TryGetResource` не нашёл ключ → фолбэк-цвет (как уже сделано в `BrushKeyConverter`, `#E4E7EE`), чтобы не падать при отрисовке.

## Тестирование

- **`ThemeService` / маппинг** — юнит-тест: `AppThemeMode` ↔ `ThemeVariant`, round-trip через `AppPreferences` (Dark→сохранить→прочитать→Dark), неизвестное значение → `System`.
- **`AppSettingsViewModel`** — юнит-тест с фейковым `IThemeSwitcher`: смена `SelectedThemeIndex` вызывает `Apply` с правильным enum.
- **Визуальная проверка** — обязательный `/pbl-verify` (правило CLAUDE.md: любые правки под `Views/**`, `*ViewModel.cs`, стили/конвертеры/ассеты → скриншот). Снять оба состояния: переключение Dark→Light на списке билдов и на вкладке Items; убедиться, что диалоги и тултипы читаемы в светлой теме, а полотно дерева остаётся тёмным осознанно (без «серых на сером» артефактов вокруг).

## Границы итерации (YAGNI)

- ❌ Кастомизация произвольных цветов пользователем — следующая итерация.
- ❌ Светлое полотно дерева пассивок (`TreeCanvas` рендер) — следующая итерация.
- ❌ Перенос селектора языка в окно настроек — оставляем в шапке как есть.
- ❌ Чистка хардкода в редких экранах (Trader и пр.) — отложено, помечено TODO.

## Ключевые файлы

- `PBLApp/App.axaml`, `PBLApp/App.axaml.cs` — снять статический variant, инициализация.
- `PBLApp/Services/ThemeService.cs` — **новый**.
- `PBLApp.Core/AppSettingsViewModel.cs` — **новый**; `IThemeSwitcher` абстракция (в Core).
- `PBLApp/Views/AppSettingsWindow.axaml` (+`.axaml.cs`) — **новый**.
- `PBLApp/Views/BuildListView.axaml` (+`.axaml.cs`) — кнопка-шестерёнка.
- `PBLApp/Themes/Tokens.Colors.axaml` — добавить недостающие ключи (в оба варианта).
- `PBLApp.Core/AppPreferences.cs` — используется как есть (ключ `AppTheme`).
- Вьюхи-нарушители (видимые): `BuildListView`, `ItemsTabView`, `ItemEditorView`, `SkillsTabView`, `CalcsTabView`, `ConfirmDialog`, `PromptDialog`.
- C#: `StyledHoverTooltip.cs`, `TooltipKindConverter.cs`, `TreeTabView.axaml.cs`.
