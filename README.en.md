# PoE2 Build Lab

**A build planner for Path of Exile 2 — with a native UI and a full Russian translation.**

A fork of [Path of Building Community (PoE2)](https://github.com/PathOfBuildingCommunity/PathOfBuilding-PoE2)
with the interface rewritten on .NET 9 / Avalonia and end-to-end Russian
localisation. The calculation engine is the original, battle-tested PoB one.

[Русская версия](README.md) · [Telegram](https://t.me/PoE2BuildLab) · [Boosty](https://boosty.to/poe2buildlab)

<!-- SCREENSHOTS: filled in after task #37 (item icons). Files live in docs/assets/screenshots/ -->
<p float="middle">
  <img alt="Tree tab" src="docs/assets/screenshots/tree.png" width="48%" />
  <img alt="Items tab" src="docs/assets/screenshots/items.png" width="48%" />
</p>
<p float="middle">
  <img alt="Skills tab" src="docs/assets/screenshots/skills.png" width="48%" />
  <img alt="Calcs tab" src="docs/assets/screenshots/calcs.png" width="48%" />
</p>

## Download

Prebuilt binaries are on the [Releases](https://github.com/Visp1024/Poe2-Build-lab/releases) page.

- Windows 10/11, x64;
- no installer — unpack the archive anywhere and run it;
- **no .NET runtime required** — it is bundled inside the build.

## How it differs from upstream Path of Building

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

## Features

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

## Community and support

- **Telegram** — [@PoE2BuildLab](https://t.me/PoE2BuildLab): release
  announcements, questions, bug reports, discussion.
- **Boosty** — [boosty.to/poe2buildlab](https://boosty.to/poe2buildlab):
  support development.

Bugs and suggestions are also welcome in
[Issues](https://github.com/Visp1024/Poe2-Build-lab/issues).

## Credits

This project exists thanks to
[Path of Building Community](https://github.com/PathOfBuildingCommunity) and
Openarl's original Path of Building — the whole calculation engine, mod database
and build logic come from there. Upstream is merged back into this fork
regularly.

Path of Exile 2 is a trademark of Grinding Gear Games. This project is not
affiliated with or endorsed by GGG.

## Licence

[MIT](LICENSE) — the same as upstream Path of Building.

Third-party licences (PUC-Rio Lua, engine dependencies and data) are collected
in [LICENSE.md](LICENSE.md). The licensing information is considered to be part
of the documentation.

## Development

Build, test and contribution instructions are in
[CONTRIBUTING.md](CONTRIBUTING.md); the architecture of the C# side is described
in [AVALONIA_MIGRATION_PLAN.md](AVALONIA_MIGRATION_PLAN.md), and the release
process in [RELEASE_PBLApp.md](RELEASE_PBLApp.md).
