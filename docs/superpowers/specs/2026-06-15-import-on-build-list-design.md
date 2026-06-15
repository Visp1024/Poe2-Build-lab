# Дизайн: перенос импорта на экран списка билдов

**Дата:** 2026-06-15
**Статус:** утверждён к реализации

## Цель

Развести импорт и экспорт по смыслу:

- **Импорт = создать новый билд** → переезжает на экран списка билдов (`BuildListView`), доступен дополнительной кнопкой в карточке создания.
- **Экспорт = поделиться текущим билдом** → остаётся на экране открытого билда (`BuildPageView`), кнопка в строке вкладок переименовывается в «Экспорт».

Окно `ImportExportWindow` и `ImportTabView` переиспользуются в обоих местах через флаги видимости секций.

## Решения (из брейншторма)

- UI импорта на списке: **вторичная кнопка внутри карточки создания** («⤓ Импорт из кода» под «+ Создать билд»).
- Ввод кода: **переиспользовать окно `ImportExportWindow`** в режиме «только импорт».
- Имя импортированного билда: **выводится из самого билда** (`className - ascendClassName (Lлвл)`), коллизия → суффикс « 2», fallback «Imported Build».
- ViewModel: **один `ImportTabViewModel` с двумя режимами** (а не две VM) — минимум кода, view уже использует `x:CompileBindings="False"` и общую разметку обеих секций.

## Архитектура

### 1. `ImportTabViewModel` — два режима

Добавить:

- `enum ImportExportMode { BuildExport, ListImport }`.
- bool-свойства `ShowImport` / `ShowExport`, выставляемые по режиму; к ним привязывается `IsVisible` секций импорта/экспорта в `ImportTabView.axaml`.

**Режим `BuildExport`** (существующий конструктор `(LuaHost host, BuildModel build, string xmlPath)`):
- `ShowExport = true`, `ShowImport = false`.
- Поведение экспорта без изменений (`GenerateCode`, `CopyCode`).

**Режим `ListImport`** (новый конструктор `(string buildsFolder, Action<string> onImported)`):
- `ShowImport = true`, `ShowExport = false`.
- `build` / `xmlPath` / `host` не нужны (`BuildCodec.Decode` — чистый C#); поля делаются nullable.
- `ImportAsync`:
  1. `BuildCodec.Decode(code)` → XML; валидация наличия `<PathOfBuilding`.
  2. Имя через `BuildNaming.FromXml(xml)`; `UniqueName(buildsFolder, name)` для коллизий.
  3. Записать XML в файл `buildsFolder/<name>.xml`.
  4. Вызвать `onImported(filePath)` — закрытие окна и открытие нового билда выполняет вызывающая сторона.

`ImportAsync` ветвится по `Mode`.

### 2. `BuildNaming` (новый, `PBLApp.Core`)

Маленький статический помощник `public static string FromXml(string xml)`:
- regex-ы `className="..."`, `ascendClassName="..."`, ` level="(\d+)"` (те же, что в `BuildEntryViewModel`).
- Формат `"{className} - {ascendClassName} (L{level})"`, повторяющий схему имён существующих файлов билдов.
- Если `className` пуст → `"Imported Build"`.

`UniqueName` остаётся в `BuildListViewModel` (или переносится рядом с `BuildNaming`, если так чище при реализации).

### 3. Экран списка — кнопка импорта в карточке создания

`BuildListView.axaml`:
- В блоке `IsCreate` карточки: под «+ / Создать билд» — разделитель и вторичная кликабельная строка «⤓ Импорт из кода» (`Border`/`Button` со своим `PointerPressed`, гасящим всплытие, чтобы не сработало «создать пустой билд»).

`BuildListView.axaml.cs`:
- Обработчик `ImportCard_PointerPressed` → вызывает команду VM, которая через хук показывает окно.
- Хук `ShowImportWindow` (см. ниже) реализуется в code-behind: создаёт `ImportExportWindow { DataContext = listImportVm }`, показывает модально/поверх владельца (по образцу `BuildPageView.OpenImportExport_Click`).

`BuildListViewModel`:
- Хук `Func<ImportTabViewModel, Task>? ShowImportWindow { get; set; }` (по образцу `ConfirmDeleteAsync`), выставляется view в `OnDataContextChanged`.
- Команда `OpenImportCommand`: собирает `ImportTabViewModel` в режиме `ListImport` с `buildsFolder = GetBuildsPath()` (или текущая папка) и `onImported = path => { var entry = new BuildEntryViewModel(name, path, Build); _openBuild(entry); }`; затем `await ShowImportWindow(vm)`.
- VM также сигналит об успехе (через `onImported`), после чего окно закрывается — закрытие инициирует view (подписка на изменение или прямой вызов из `onImported`). Деталь закрытия окна решается при реализации; базовый вариант — `onImported` поднимает событие, на которое подписан code-behind, и закрывает окно.

### 4. Экран билда — только экспорт

`BuildPageView.axaml`:
- Кнопка строки вкладок `Tab_ImportExport` → новый ключ `Tab_Export` («Экспорт»).

`BuildPageViewModel`:
- `ImportTab` строится в режиме `BuildExport` (как сейчас) → окно показывает только секцию экспорта.

`ImportExportWindow.axaml`:
- Заголовок `Title` привязать к режиму (импорт/экспорт) либо сделать общий нейтральный; по умолчанию — переключаемый заголовок через свойство VM.

### 5. Локализация

Новые ключи в `Strings.resx` и `Strings.ru.resx`:
- `Tab_Export` — «Export» / «Экспорт».
- `List_ImportCard` — «Import from code» / «Импорт из кода».
- (опц.) заголовки окна для двух режимов, если делаем переключаемый `Title`.

## Затрагиваемые файлы

- `PBLApp.Core/ImportTabViewModel.cs` — режимы, новый конструктор, ветвление `ImportAsync`.
- `PBLApp.Core/BuildNaming.cs` — новый помощник.
- `PBLApp.Core/BuildListViewModel.cs` — `OpenImportCommand`, хук `ShowImportWindow`.
- `PBLApp/Views/BuildListView.axaml(.cs)` — кнопка импорта в карточке + показ окна.
- `PBLApp/Views/ImportTabView.axaml` — `IsVisible` секций по `ShowImport`/`ShowExport`.
- `PBLApp/Views/ImportExportWindow.axaml` — заголовок по режиму.
- `PBLApp/Views/BuildPageView.axaml` — `Tab_Export`.
- `PBLApp.Core/Localization/Strings.resx` + `Strings.ru.resx` — новые ключи.

## Проверка

- Сборка `dotnet build PBLHost.sln`.
- Завершить через `/pbl-verify` (правило проекта для любых UI-изменений): экран списка (карточка с кнопкой импорта + диалог), экран билда (кнопка «Экспорт», окно без секции импорта), прогон импорта реального кода → новый файл с корректным именем открывается.

## Вне рамок

- Импорт по URL / из профиля PoE.com (только share-код, как сейчас).
- Изменение формата share-кода (`BuildCodec`).
