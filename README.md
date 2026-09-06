# PoE2 Build Lab

<table>
<tr>
<td width="50%" valign="top">

**Планировщик билдов для Path of Exile 2 — на русском языке, с нативным
интерфейсом.**

Форк [Path of Building Community (PoE2)][upstream] с полностью переписанным
интерфейсом на .NET 9 / Avalonia и сквозной русской локализацией. Движок
расчётов — оригинальный, проверенный PoB.

→ [Русская версия ниже](#русский)

</td>
<td width="50%" valign="top">

**A build planner for Path of Exile 2 — with a native UI and a full Russian
translation.**

A fork of [Path of Building Community (PoE2)][upstream] with the interface
rewritten on .NET 9 / Avalonia and end-to-end Russian localisation. The
calculation engine is the original, battle-tested PoB one.

→ [English version below](#english)

</td>
</tr>
</table>

[Telegram](https://t.me/PoE2BuildLab) · [Boosty](https://boosty.to/poe2buildlab) · [Releases](https://github.com/Visp1024/Poe2-Build-lab/releases)

<p float="middle">
  <img alt="Дерево пассивных умений с тепловой картой мощности / Passive tree with the node power heat-map" src="docs/assets/screenshots/tree.png" width="48%" />
  <img alt="Предметы и тултип в стиле игры / Items tab with an in-game-style tooltip" src="docs/assets/screenshots/items.png" width="48%" />
</p>
<p float="middle">
  <img alt="Умения и гемы / Skills and gems" src="docs/assets/screenshots/skills.png" width="48%" />
  <img alt="Полная разбивка расчётов / Full calculation breakdown" src="docs/assets/screenshots/calcs.png" width="48%" />
</p>

---

<a id="русский"></a>

## Русский

### Скачать

Готовые сборки — на странице [Releases](https://github.com/Visp1024/Poe2-Build-lab/releases).

- Windows 10/11, x64;
- устанавливать ничего не нужно: архив распаковывается в любую папку и запускается;
- .NET на компьютере **не требуется** — он уже внутри сборки.

### Чем отличается от оригинального Path of Building

| | Оригинальный PoB2 | PoE2 Build Lab |
|---|---|---|
| Интерфейс | Lua + собственный рендерер SimpleGraphic (DirectX) | нативный .NET 9 / Avalonia |
| Язык | только английский | **русский** (интерфейс, гемы, статы, ноды дерева, моды предметов) |
| Движок расчётов | Lua 5.1 / LuaJIT | тот же код на Lua 5.4 через NLua |
| Окна | одно окно, вкладки внутри | вкладки отрываются в отдельные окна, заметки и настройки — свои окна |

Расчёты — это буквально тот же самый Lua-код, что и в апстриме: он не
переписывался, а был перенесён на Lua 5.4 и покрыт автоматической проверкой
паритета — каждый билд прогоняется через оба движка и все статы сравниваются
до шестого знака.

### Возможности

**Дерево пассивных умений**
- полное дерево PoE2 с классами и восхождениями, поиск по узлам;
- **тепловая карта мощности узлов** — показывает, какой ещё не взятый узел
  даёт больше всего выбранного стата (ДПС, EHP и т. д.) на вложенное очко,
  с учётом того, сколько очков до него нужно потратить;
- расчёт идёт в пуле фоновых воркеров, интерфейс не замирает.

**Предметы**
- фигура персонажа со всеми слотами, пул предметов, базы и уникальные;
- тултипы в стиле игры — с русским переводом каждой строки модов;
- редактор предметов: база, редкость, аффиксы с ползунками роллов, качество,
  руны, порча;
- при наведении показывается разница статов с уже надетым предметом;
- **подбор апгрейдов через официальный трейд** — под каждым слотом кнопка
  «Подбор», окно ищет предметы на pathofexile.com/trade2 по выбранным весам
  статов (вход по официальному OAuth GGG).

**Умения**
- группы умений, активные и поддерживающие гемы, уровни и качество;
- переведённые описания гемов и теги;
- моды с сокетов и гемы, выдаваемые предметами, применяются автоматически.

**Расчёты**
- полная разбивка урона и защит из PoB — как именно получилась каждая цифра;
- сводка ключевых статов в шапке билда.

**Прочее**
- импорт персонажа с pathofexile.com, импорт и экспорт кодов билдов;
- заметки к билду в отдельном окне;
- конфигурация боя (ауры, баффы, заряды, проклятия, сопротивления врага…);
- размеры и положение окон запоминаются между запусками.

### Сообщество и поддержка

- **Telegram** — [@PoE2BuildLab](https://t.me/PoE2BuildLab): анонсы версий,
  вопросы, баг-репорты, обсуждение.
- **Boosty** — [boosty.to/poe2buildlab](https://boosty.to/poe2buildlab):
  поддержать разработку.

Баги и предложения также можно оставлять в
[Issues](https://github.com/Visp1024/Poe2-Build-lab/issues).

### Благодарности

Проект существует благодаря
[Path of Building Community](https://github.com/PathOfBuildingCommunity) и
изначальному Path of Building Openarl'а — весь движок расчётов, база данных
модов и логика билдов пришли оттуда. Апстрим регулярно вливается обратно в этот
форк.

Path of Exile 2 — торговая марка Grinding Gear Games. Проект не связан с GGG и
не поддерживается ими.

### Лицензия

[MIT](LICENSE) — как и у оригинального Path of Building.

Лицензии сторонних компонентов (Lua PUC-Rio, зависимости движка и данные)
собраны в [LICENSE.md](LICENSE.md). Лицензионная информация считается частью
документации.

### Разработка

Инструкции по сборке, тестам и вкладу в код — в
[CONTRIBUTING.md](CONTRIBUTING.md); архитектура C#-части описана в
[AVALONIA_MIGRATION_PLAN.md](AVALONIA_MIGRATION_PLAN.md), процесс релиза — в
[RELEASE_PBLApp.md](RELEASE_PBLApp.md).

---

<a id="english"></a>

## English

### Download

Prebuilt binaries are on the [Releases](https://github.com/Visp1024/Poe2-Build-lab/releases) page.

- Windows 10/11, x64;
- no installer — unpack the archive anywhere and run it;
- **no .NET runtime required** — it is bundled inside the build.

### How it differs from upstream Path of Building

| | Upstream PoB2 | PoE2 Build Lab |
|---|---|---|
| UI | Lua + the custom SimpleGraphic renderer (DirectX) | native .NET 9 / Avalonia |
| Language | English only | **Russian** (UI, gems, stats, tree nodes, item mods) |
| Calc engine | Lua 5.1 / LuaJIT | the same code on Lua 5.4 via NLua |
| Windows | one window, tabs inside | tabs detach into their own windows; notes and settings are separate windows |

The calculations are literally the same Lua code as upstream. It was not
rewritten — it was ported to Lua 5.4 and covered by an automated parity check:
every fixture build is run through both engines and all stats are compared down
to the sixth decimal.

### Features

**Passive skill tree**
- the full PoE2 tree with classes and ascendancies, node search;
- **node power heat-map** — shows which un-allocated node gives the most of a
  chosen stat (DPS, EHP, …) per point spent, accounting for how many points it
  takes to reach it;
- computed in a pool of background workers, so the UI never freezes.

**Items**
- character figure with every slot, an item pool, bases and uniques;
- in-game-style tooltips, with every mod line translated;
- item editor: base, rarity, affixes with roll sliders, quality, runes, corruption;
- hovering an item shows the stat delta against what is currently equipped;
- **trade upgrade search** — a "Search" strip under every slot opens a window
  that queries pathofexile.com/trade2 by your chosen stat weights (signed in
  through GGG's official OAuth).

**Skills**
- skill groups, active and support gems, levels and quality;
- translated gem descriptions and tags;
- socketed-gem modifiers and item-granted supports are applied automatically.

**Calcs**
- PoB's full offence/defence breakdown — exactly how each number was derived;
- a summary of the key stats in the build header.

**Other**
- character import from pathofexile.com, build code import/export;
- build notes in their own window;
- combat configuration (auras, buffs, charges, curses, enemy resistances, …);
- window sizes and positions are remembered between launches.

### Community and support

- **Telegram** — [@PoE2BuildLab](https://t.me/PoE2BuildLab): release
  announcements, questions, bug reports, discussion.
- **Boosty** — [boosty.to/poe2buildlab](https://boosty.to/poe2buildlab):
  support development.

Bugs and suggestions are also welcome in
[Issues](https://github.com/Visp1024/Poe2-Build-lab/issues).

### Credits

This project exists thanks to
[Path of Building Community](https://github.com/PathOfBuildingCommunity) and
Openarl's original Path of Building — the whole calculation engine, mod database
and build logic come from there. Upstream is merged back into this fork
regularly.

Path of Exile 2 is a trademark of Grinding Gear Games. This project is not
affiliated with or endorsed by GGG.

### Licence

[MIT](LICENSE) — the same as upstream Path of Building.

Third-party licences (PUC-Rio Lua, engine dependencies and data) are collected
in [LICENSE.md](LICENSE.md). The licensing information is considered to be part
of the documentation.

### Development

Build, test and contribution instructions are in
[CONTRIBUTING.md](CONTRIBUTING.md); the architecture of the C# side is described
in [AVALONIA_MIGRATION_PLAN.md](AVALONIA_MIGRATION_PLAN.md), and the release
process in [RELEASE_PBLApp.md](RELEASE_PBLApp.md).

[upstream]: https://github.com/PathOfBuildingCommunity/PathOfBuilding-PoE2
