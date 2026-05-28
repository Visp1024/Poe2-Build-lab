namespace PBLEngine;

public record ConfigOption(
    string Var,
    string Label,
    string Type,
    string Section,
    string CurrentValue,
    ConfigListItem[] ListOptions
);

public record ConfigListItem(string Val, string Label);

public record SkillGroupEntry(int Index, string Name, string Label = "", bool IsEnabled = true, bool IsTrigger = false);

public record ActiveSkillEntry(int Index, string Name);

public record ModifierEntry(string Value, string ModType, string Source, string SourceName);

public record GemEntry(string Name, int Level, int Quality, bool IsEnabled, bool IsSupport, string Color = "#CDD6F4");

/// <summary>
/// One rendered line from PoB's item tooltip builder.
/// Kind = "text" | "separator". For text lines the raw <see cref="Text"/> may
/// contain inline ^x&lt;hex&gt; colour codes; segment them at render time.
/// </summary>
public record ItemTooltipLine(
    string Kind,
    int Size,
    string Text,
    bool Centered,
    int Block,
    string? Font = null);

public record ItemEntry(
    string Name,
    string BaseName,
    string Rarity,
    int ItemLevel,
    IReadOnlyList<string> Enchants,
    IReadOnlyList<string> Implicits,
    IReadOnlyList<string> Explicits
);

public record ItemPoolEntry(
    int    Id,
    string Name,
    string BaseName,
    string Rarity,
    int    ItemLevel,
    string PrimarySlot,   // slot type the item fits in (e.g. "Helmet", "Weapon 1")
    string EquippedSlot   // actual slot it is equipped in, "" = not equipped
);

public record BaseItemEntry(
    string Name,
    string Category,
    string Implicit,  // implicit mod text (may be multi-line, separated by \n)
    int    LevelReq,
    string Type = "", // e.g. "Helmet", "Boots", "Belt", "One Hand Sword" — needed for rune compatibility
    int    SocketCount = 0   // default rune socket count for this base
);

public record AffixEntry(
    string Id,
    string AffixName,  // e.g. "of the Bear"
    string StatText,   // e.g. "+(13-16) to Strength"
    string AffixType,  // "Prefix" or "Suffix"
    int    Level,
    string Group       // e.g. "Strength"
);

public record UniqueItemEntry(
    string Name,
    string BaseName,
    string Category,
    string LookupKey   // PoB internal key in main.uniqueDB.list (usually "Title, BaseName")
);

public record JewelSocketEntry(
    int    NodeId,
    string NodeName,      // tree node name (e.g. "Heart of the Warrior") if available
    string SlotName,      // e.g. "Jewel 12345" — for SelectSlot / equip API
    bool   IsAllocated,
    ItemEntry? Item       // null if socket empty
);

/// <summary>Intrinsic base defence/utility values from data.itemBases[baseName].</summary>
public record BaseDefaults(
    int Armour,
    int Evasion,
    int EnergyShield,
    int Ward,
    int Spirit,
    int CharmSlots,
    int BaseQualityPct   // base item's innate quality (e.g. 20 for Lattice Sandals)
);

/// <summary>One rune entry from data.itemMods.Runes.</summary>
public record RuneEntry(
    string Name,
    IReadOnlyList<string> SlotTypes,           // e.g. ["helmet"], ["weapon", "caster"]
    IReadOnlyDictionary<string, IReadOnlyList<string>> ModsByType   // per slot-type → mod lines
);
