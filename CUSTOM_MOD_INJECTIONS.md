# Custom-mod injections (duplication registry)

Some ascendancy mechanics that upstream Path of Building **does not implement yet**
are reproduced in this fork by **injecting parsed modifiers** through PoB's own
custom-modifier path (`ConfigTab` → `configTab.modList`, which `CalcSetup` merges).
This avoids touching the calc pipeline, but creates a **duplication risk**: if a
future upstream sync implements the mechanic natively, our injection would apply the
effect a *second* time.

**Every such injection MUST be listed here and guarded by a test.** The guard tests
live in [`PBLEngine.Tests/CustomModInjectionGuardTests.cs`](PBLEngine.Tests/CustomModInjectionGuardTests.cs)
and assert the *upstream non-implementation signal* for each feature (e.g. "this node
still parses N mods"). They run with `dotnet test`.

## Process on every PoB backend (src/) update

1. Sync upstream `src/`.
2. Run `dotnet test` (or specifically the `CustomModInjectionGuard` tests).
3. **If a guard fails**, upstream has changed/implemented that mechanic. Do **not**
   just bump the expected number — review whether upstream now applies the effect
   itself. If so, **remove our injection** (and this registry entry + its guard) to
   avoid double-counting. If it was only a cosmetic data change, update the guard.

## Registered injections

### 1. Runic Meridians — body-tattoo Rune sockets
- **Source tag:** `Tattoo`
- **Where:** `src/Classes/ConfigTab.lua` → `ConfigTabClass:ApplyTattooMods` (called from
  `BuildModList`). App side: `TattoosViewModel`, `LuaHost.GetTattooState/SetTattooRune`.
- **What we inject:** the mod lines of the Runes the user socketed into the 5 body
  sockets (1 helmet, 2 body armour, 1 gloves, 1 boots), resolved per slot type.
- **Why it's safe today:** the **Runic Meridians** tree node (id 39552 in 0_5) is purely
  descriptive — it parses **0 mods**; nothing in the engine grants or applies body Rune
  sockets. Our injection is the *only* source of these mods.
- **Guard signal:** the `Runic Meridians` node still parses **0** mods.
  If it ever parses > 0, upstream likely implemented it → review/remove injection.

### 2. Crystalline Phylactery — socketed-jewel doubling  *(see guard; injection added with the feature)*
- **Source tag:** `Phylactery`
- **Where:** `src/Classes/ConfigTab.lua` → `ConfigTabClass:ApplyPhylacteryMods`.
- **What we inject:** the socketed jewel's mod lines applied **twice** (base + the node's
  "100% increased Effect of bonuses gained from Socketed Jewel").
- **Why it's safe today:** the **Crystalline Phylactery** node (id 17788 in 0_5) parses
  **only 1 mod** (the `50% more Mana Cost … if no Energy Shield` penalty). The jewel
  socket ("Can Socket a non-Unique Basic Jewel") and the "100% increased Effect" line are
  **not** implemented natively.
- **Guard signal:** the `Crystalline Phylactery` node still parses exactly **1** mod.
  If it parses more, upstream likely added the socket/effect → review/remove injection.
