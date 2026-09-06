# Страница репозитория: что вставить руками

Всё, что нельзя закоммитить, — настройки на github.com/Visp1024/Poe2-Build-lab.
Ниже готовые тексты для копипасты и чеклист перед тем, как делать репозиторий
публичным.

---

## 1. Поле «About» (шестерёнка справа вверху на главной странице)

**Description** (лимит GitHub — 350 символов):

```
Планировщик билдов для Path of Exile 2 на русском языке. Форк Path of Building Community с нативным интерфейсом на .NET 9 / Avalonia: дерево пассивок с тепловой картой мощности, редактор предметов, подбор апгрейдов через официальный трейд, полная разбивка расчётов.
```

**Website:**

```
https://boosty.to/poe2buildlab
```

**Topics:**

```
path-of-exile-2  poe2  path-of-exile  build-planner  path-of-building
avalonia  avaloniaui  dotnet  csharp  lua  nlua  gaming  russian
```

Галочки в блоке About: **Releases** — включить, **Packages** и
**Deployments** — выключить.

---

## 2. Настройки репозитория

| Где | Что |
|---|---|
| Settings → General → Features | **Issues** — вкл. **Discussions** — по желанию (общение и так в Telegram). **Wiki**, **Projects** — выкл. |
| Settings → General → Pull Requests | «Allow squash merging» — вкл; «Automatically delete head branches» — вкл. |
| Settings → General → Social preview | Загрузить картинку 1280×640 — скриншот интерфейса с названием. Именно она показывается при репосте ссылки в Telegram. |
| Settings → Branches | Защита `main`: требовать прохождение CI-проверки `Build & test (Windows)` перед мержем. |
| Settings → Actions → General | Workflow permissions: «Read and write» (нужно `release.yml` для создания релиза). |

---

## 3. Закреплённое сообщение / первый релиз

При переводе в публичный статус стоит сразу оформить последний релиз через
`.github/workflows/release.yml` (push тега `v0.3`), чтобы на странице был
работающий «Download» — README на него ссылается.

---

## 4. Чеклист перед публикацией

Проверено в рамках задачи #36 (по состоянию на 2026-09-06):

- [x] **Секреты** — в рабочем дереве и в трекнутых файлах нет токенов, ключей,
      паролей, приватных ключей и `POESESSID`. Проверено grep'ом по
      `ghp_*`, `glpat-*`, `AKIA*`, `BEGIN PRIVATE KEY`, `api_key/secret/token = "…"`.
- [x] **Личные пути** — `D:\Projects\…` и `C:\Users\…` в трекнутых файлах не
      встречаются (чинилось в задачах #29 и #32).
- [x] **`.claude/`** — трекаются только 5 файлов команд `pbl-*`, без
      `settings.local.json`.
- [x] **Лицензия** — MIT, файл `LICENSE`, копирайт апстрима сохранён.
- [x] **CI апстрима удалён** — 8 workflow'ов PoB (backport в PoB1, публикация
      beta-ветки по расписанию, NSIS-инсталлятор, spellcheck и т. д.) убраны
      целиком; на их месте `ci.yml` и `release.yml` под этот проект.
- [x] **`.kanban-slot`** — служебный файл доски Makestead, добавлен в
      `.gitignore` (не трекался и раньше).

### Замечания аудита

- **Требует решения человека: `client_id=pob` в OAuth трейдера** (`PBLApp.Core/Trader/PoeOAuthService.cs`).
  Это публичный PKCE-клиент, зарегистрированный GGG на *оригинальный* Path of
  Building; мы унаследовали его из `src/Classes/PoEAPI.lua`. Секрета в нём нет
  (PKCE), но в публичном форке это чужой идентификатор приложения — если GGG
  когда-нибудь начнёт разделять клиентов, вход в трейдер сломается. Правильный
  путь — зарегистрировать собственный OAuth-клиент у GGG. Не блокирует
  публикацию, но стоит держать в виду.

- **Мусор в корне репозитория** — вычищено (та же задача #36):

  | Удалено | Что это было |
  |---|---|
  | `REPORT_2026-05-28.md` | одноразовый отчёт по автономной сессии |
  | `fix_ascendancy_positions.py` | разовый скрипт-правка позиций восхождений, звался только из `RELEASE.md` |
  | `runtime-win32.zip` | 6,4 МБ бинарника в git; на него не ссылались ни скрипты, ни `manifest.xml` |
  | `RELEASE.md` | релизный процесс апстрима (NSIS + его GitHub Actions, которые тоже удалены) — к нашему приложению неприменим, актуален `RELEASE_PBLApp.md` |

  **Оставлены, хотя выглядят мусором:** `help.txt` и `changelog.txt` — это живые
  файлы Lua-приложения. `help.txt` читает `src/Modules/Main.lua:1390`,
  `changelog.txt` — `Main.lua` и `src/UpdateCheck.lua`; оба перечислены в
  `manifest.xml` с контрольными суммами, а `changelog.txt` вдобавок тянется
  из апстрима (`sync_paths` в `.upstream-sync.yaml`).

- **Внутренние документы в корне** (`CLAUDE.md`, `AVALONIA_MIGRATION_PLAN.md`,
  `LOCALIZATION_PLAN.md`, `UPDATE_PIPELINE.md`, `CUSTOM_MOD_INJECTIONS.md`) —
  это рабочие материалы, но для открытого репозитория они скорее плюс: показывают,
  как проект устроен. Оставлены как есть.

---

## 5. Осталось после этой задачи

- ~~**Скриншоты для README**~~ — сняты 2026-09-06 и лежат в
  `docs/assets/screenshots/` (`tree.png`, `items.png`, `skills.png`,
  `calcs.png`). Задачу **#38** можно закрывать.
- **Social preview 1280×640** — всё ещё нужна отдельная картинка для
  Settings → General → Social preview; годится кадр дерева с подписью проекта.
- **Подпись сборок** — маршрут описан в [`CODE_SIGNING.md`](CODE_SIGNING.md):
  SignPath Foundation, бесплатно для опенсорса, но требует, чтобы релиз собирался
  в CI. Теперь собирается — можно подавать заявку после публикации репозитория.
