using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PBLApp.Core.Localization;

public sealed class GameTranslationService
{
    // Asm MUST be declared before Instance: Instance = new() triggers the constructor
    // which calls Load() → LoadMap() → Asm.GetManifestResourceStream(). If Asm were
    // declared after Instance it would be null during that first Load(), causing all
    // LoadMap() calls to throw (caught silently) and return empty dictionaries.
    private static readonly Assembly Asm = typeof(GameTranslationService).Assembly;

    public static readonly GameTranslationService Instance = new();

    private Dictionary<string, string> _gems              = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string> _items             = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string> _uniques           = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string> _magicPrefixes     = new(StringComparer.Ordinal);
    private Dictionary<string, string> _magicSuffixes     = new(StringComparer.Ordinal);
    private Dictionary<string, string> _passiveStats      = new(StringComparer.Ordinal);
    private Dictionary<string, string> _passiveNames      = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string> _gemMeta           = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string> _gemTags           = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string> _skillDescriptions = new(StringComparer.Ordinal);
    private Dictionary<string, string> _configSections    = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string> _configLabels      = new(StringComparer.Ordinal);
    private Dictionary<string, string> _runes             = new(StringComparer.OrdinalIgnoreCase);
    private string _loadedLang = "";

    private GameTranslationService()
    {
        LocalizationService.Instance.LanguageChanged += (_, _) => Load(LocalizationService.Instance.CurrentLanguage);
        Load(LocalizationService.Instance.CurrentLanguage);
    }

    private void Load(string lang)
    {
        if (lang == _loadedLang) return;
        _loadedLang = lang;

        if (lang == "en")
        {
            _gems              = new(StringComparer.OrdinalIgnoreCase);
            _items             = new(StringComparer.OrdinalIgnoreCase);
            _uniques           = new(StringComparer.OrdinalIgnoreCase);
            _magicPrefixes     = new(StringComparer.Ordinal);
            _magicSuffixes     = new(StringComparer.Ordinal);
            _passiveStats      = new(StringComparer.Ordinal);
            _passiveNames      = new(StringComparer.OrdinalIgnoreCase);
            _gemMeta           = new(StringComparer.OrdinalIgnoreCase);
            _gemTags           = new(StringComparer.OrdinalIgnoreCase);
            _skillDescriptions = new(StringComparer.Ordinal);
            _configSections    = new(StringComparer.OrdinalIgnoreCase);
            _configLabels      = new(StringComparer.Ordinal);
            _runes             = new(StringComparer.OrdinalIgnoreCase);
            return;
        }

        _gems              = LoadMap($"PBLApp.ViewModels.Translations.gems_{lang}.json");
        _items             = LoadMap($"PBLApp.ViewModels.Translations.items_{lang}.json");
        _uniques           = LoadMap($"PBLApp.ViewModels.Translations.unique_names_{lang}.json");
        LoadMagicAffixes(lang);
        _passiveStats      = LoadMap($"PBLApp.ViewModels.Translations.passive_nodes_{lang}.json",
                                     StringComparer.Ordinal);
        _passiveNames      = LoadMap($"PBLApp.ViewModels.Translations.passive_names_{lang}.json");
        _gemMeta           = LoadMap($"PBLApp.ViewModels.Translations.gem_meta_{lang}.json");
        _gemTags           = LoadMap($"PBLApp.ViewModels.Translations.gem_tags_{lang}.json");
        _skillDescriptions = LoadMap($"PBLApp.ViewModels.Translations.skill_descriptions_{lang}.json",
                                     StringComparer.Ordinal);
        _configSections    = LoadMap($"PBLApp.ViewModels.Translations.config_sections_{lang}.json");
        _configLabels      = LoadMap($"PBLApp.ViewModels.Translations.config_labels_{lang}.json",
                                     StringComparer.Ordinal);
        _runes             = LoadMap($"PBLApp.ViewModels.Translations.runes_{lang}.json");
    }

    /// <summary>Translate a rune / soul core / idol name.</summary>
    public string Rune(string englishName) =>
        _runes.TryGetValue(englishName, out var ru) ? ru : englishName;

    private static readonly JsonSerializerOptions JsonOpts = new();

    private void LoadMagicAffixes(string lang)
    {
        try
        {
            using var stream = Asm.GetManifestResourceStream(
                $"PBLApp.ViewModels.Translations.magic_affixes_{lang}.json");
            if (stream is null) return;
            using var doc = JsonDocument.Parse(new StreamReader(stream).ReadToEnd());
            if (doc.RootElement.TryGetProperty("prefixes", out var p))
                foreach (var kv in p.EnumerateObject())
                    _magicPrefixes[kv.Name] = kv.Value.GetString() ?? "";
            if (doc.RootElement.TryGetProperty("suffixes", out var s))
                foreach (var kv in s.EnumerateObject())
                    _magicSuffixes[kv.Name] = kv.Value.GetString() ?? "";
        }
        catch { }
    }

    private static Dictionary<string, string> LoadMap(string resourceName,
        IEqualityComparer<string>? comparer = null)
    {
        comparer ??= StringComparer.OrdinalIgnoreCase;
        try
        {
            using var stream = Asm.GetManifestResourceStream(resourceName);
            if (stream is null) return new(comparer);
            var json = new StreamReader(stream).ReadToEnd();
            var raw = JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOpts);
            if (raw is null) return new(comparer);
            var result = new Dictionary<string, string>(raw.Count, comparer);
            foreach (var kv in raw) result[kv.Key] = kv.Value;
            return result;
        }
        catch
        {
            return new(comparer);
        }
    }

    public string Gem(string englishName) =>
        _gems.TryGetValue(englishName, out var ru) ? ru : englishName;

    public string Item(string englishName) =>
        _items.TryGetValue(englishName, out var ru) ? ru : englishName;

    /// <summary>Translate a unique item name (the part before the comma in PoB's combined name).</summary>
    public string Unique(string englishName) =>
        _uniques.TryGetValue(englishName, out var ru) ? ru : englishName;

    public string PassiveStat(string englishStat) =>
        _passiveStats.TryGetValue(englishStat, out var ru) ? ru : englishStat;

    // ── Item tooltip line translator ───────────────────────────────────────
    //
    // PoB's AddItemTooltip emits a mix of stat strings ("+54 to maximum Life"),
    // base-type info ("Helmet", "Evasion Rating: 37"), requirements, headers,
    // and delta lines. Translation strategy, in order of preference:
    //   1) exact match against passive_nodes_ru.json (covers many shared stats)
    //   2) pattern match: replace integers/floats with "#", look up the
    //      template, and re-inject the original numbers into the translated text
    //   3) literal table for tooltip headers / type names / delta labels
    //   4) compound rewrites for delta lines like "-19 Dexterity (-3.5%)"

    private static readonly Regex NumberRx = new(@"-?\d+(?:[,.]\d+)?", RegexOptions.Compiled);

    /// <summary>Translate a single tooltip line (text without colour codes).</summary>
    public string TooltipLine(string line)
    {
        if (string.IsNullOrEmpty(line) || _loadedLang == "en") return line;

        // 1) exact: passive stats, item names, unique names, then built-in item-mod patterns.
        if (_passiveStats.TryGetValue(line, out var ru)) return ru;
        if (_items.TryGetValue(line, out ru)) return ru;
        if (_uniques.TryGetValue(line, out ru)) return ru;
        if (_itemModExact.TryGetValue(line, out ru)) return ru;

        // 1a) Magic item name: "<Prefix> <BaseName> of <Suffix>".
        //     Detected by locating the longest known base substring inside the line.
        var magic = TryTranslateMagicName(line);
        if (magic is not null) return magic;

        // 2) template ("+54 to maximum Life" → "+# to maximum Life")
        var template = NumberRx.Replace(line, "#");
        if (template != line)
        {
            if (_passiveStats.TryGetValue(template, out ru) ||
                _itemModTemplates.TryGetValue(template, out ru))
            {
                var numbers = new List<string>();
                foreach (Match m in NumberRx.Matches(line)) numbers.Add(m.Value);
                int idx = 0;
                return Regex.Replace(ru, "#", _ => idx < numbers.Count ? numbers[idx++] : "#");
            }
        }

        // 3) literal (Corrupted / Helmet / Requires…). Single-token first, then
        //    "label: value" forms.
        if (_tooltipLiterals.TryGetValue(line, out var lit)) return lit;

        // 3a) rune / soul-core / idol name (used as the value of "Rune: <name>" lines).
        if (_runes.TryGetValue(line, out var runeRu)) return runeRu;

        // "Helmet" / "Body Armour" → translate as a type token
        if (_tooltipTypes.TryGetValue(line, out var tt)) return tt;

        // "Evasion Rating: 37" / "Charm Slots: 3" — split label, keep value
        var colon = line.IndexOf(": ", StringComparison.Ordinal);
        if (colon > 0)
        {
            var label = line[..colon];
            var rest  = line[(colon + 2)..];
            if (_tooltipLiterals.TryGetValue(label, out var rul))
                return rul + ": " + TooltipLine(rest);
            if (_tooltipTypes.TryGetValue(label, out var rut))
                return rut + ": " + rest;
        }

        // 4) "Requires Level 16, 15 Dex, 15 Int" — special-case to keep numbers in place
        if (line.StartsWith("Requires ", StringComparison.Ordinal))
        {
            var rest = line[9..];
            // Replace each attribute token
            foreach (var (en, rul) in _attrTokens)
                rest = Regex.Replace(rest, $@"\b{en}\b", rul);
            rest = rest.Replace("Level ", "уровень ");
            return "Требуется " + rest;
        }

        // 5a) "Removing this item from <slot> will give you:"
        const string remPrefix = "Removing this item from ";
        const string remSuffix = " will give you:";
        if (line.StartsWith(remPrefix, StringComparison.Ordinal) && line.EndsWith(remSuffix, StringComparison.Ordinal))
        {
            var slot = line[remPrefix.Length..^remSuffix.Length];
            return $"Снятие предмета со слота «{TooltipType(slot)}» даст вам:";
        }
        const string eqPrefix = "Equipping this item in ";
        const string eqSuffix = " will give you:";
        if (line.StartsWith(eqPrefix, StringComparison.Ordinal) && line.EndsWith(eqSuffix, StringComparison.Ordinal))
        {
            var slot = line[eqPrefix.Length..^eqSuffix.Length];
            return $"Экипировка в слот «{TooltipType(slot)}» даст вам:";
        }

        // 5b) delta lines: "-19 Dexterity", "+304 Effective Hit Pool (-3.5%)",
        //                  "-1,065 Cold Max Hit (-19.4%)", "-15% Fire Resistance"
        // Value may carry a unit suffix (% or 'm' for metres on Presence Radius lines).
        var deltaMatch = Regex.Match(line, @"^([+-]?\d[\d,.]*[m%]?)\s+(.+?)(\s+\([+-]?\d+(?:\.\d+)?%\))?$");
        if (deltaMatch.Success)
        {
            var value  = deltaMatch.Groups[1].Value;
            var label  = deltaMatch.Groups[2].Value;
            var suffix = deltaMatch.Groups[3].Value;
            if (_deltaLabels.TryGetValue(label, out var dlbl))
                return $"{value} {dlbl}{suffix}";
        }

        // 6) Recursive prefix handlers — translate the inner stat using the same engine.
        foreach (var (en, rup) in _recursivePrefixes)
        {
            if (line.StartsWith(en, StringComparison.Ordinal))
                return rup + TooltipLine(line[en.Length..]);
        }

        // 7) "Grants Skill: Level N <skill>" — translate the gem name
        var grantsMatch = Regex.Match(line, @"^Grants Skill: Level (\d+(?:-\d+)?|\(\d+-\d+\))\s+(.+)$");
        if (grantsMatch.Success)
        {
            var lvl = grantsMatch.Groups[1].Value;
            var gem = grantsMatch.Groups[2].Value;
            return $"Дарует навык: уровень {lvl} {Gem(gem)}";
        }

        // 8) "(Not supported in PoB yet)" suffix — translate the rest and append the marker
        const string notSupported = " (Not supported in PoB yet)";
        if (line.EndsWith(notSupported, StringComparison.Ordinal))
        {
            var head = line[..^notSupported.Length];
            return TooltipLine(head) + " (не поддерживается в PoB)";
        }

        return line;
    }

    // Tooltip headers / single-token literals.
    private static readonly Dictionary<string, string> _tooltipLiterals = new(StringComparer.Ordinal)
    {
        ["Corrupted"]                       = "Осквернён",
        ["Player"]                          = "Игрок",
        ["Player:"]                         = "Игрок:",
        ["Minion"]                          = "Прислужник",
        ["Minion:"]                         = "Прислужник:",
        ["Tip: Press Ctrl+D to disable the display of stat differences."]
                                            = "Совет: нажмите Ctrl+D, чтобы скрыть сравнение характеристик.",
        // base stat labels
        ["Evasion Rating"]                  = "Уклонение",
        ["Energy Shield"]                   = "Энергетический щит",
        ["Armour"]                          = "Броня",
        ["Ward"]                            = "Защита",
        ["Block Chance"]                    = "Шанс блока",
        ["Charm Slots"]                     = "Ячейки оберегов",
        ["Spirit"]                          = "Дух",
        ["Quality"]                         = "Качество",
        ["Item Level"]                      = "Уровень предмета",
        ["Sockets"]                         = "Сокеты",
        ["Rune"]                            = "Руна",
        ["None"]                            = "—",
        ["Physical Damage"]                 = "Физический урон",
        ["Elemental Damage"]                = "Стихийный урон",
        ["Chaos Damage"]                    = "Урон хаосом",
        ["Elemental DPS"]                   = "Стихийный DPS",
        ["Total DPS"]                       = "Общий DPS",
        ["Critical Hit Chance"]             = "Шанс крит. удара",
        ["Attacks per Second"]              = "Атак в секунду",
        ["Reload Time"]                     = "Перезарядка",
        ["Weapon Range"]                    = "Дальность оружия",
    };

    // Slot / item type names that appear as a separate centered line.
    private static readonly Dictionary<string, string> _tooltipTypes = new(StringComparer.Ordinal)
    {
        ["Helmet"]            = "Шлем",
        ["Body Armour"]       = "Нагрудник",
        ["Gloves"]            = "Перчатки",
        ["Boots"]             = "Сапоги",
        ["Belt"]              = "Пояс",
        ["Amulet"]            = "Амулет",
        ["Ring"]              = "Кольцо",
        ["Shield"]            = "Щит",
        ["Quiver"]            = "Колчан",
        ["Flask"]             = "Флакон",
        ["Life Flask"]        = "Флакон жизни",
        ["Mana Flask"]        = "Флакон маны",
        ["Charm"]             = "Оберег",
        ["Jewel"]             = "Самоцвет",
        ["Wand"]              = "Жезл",
        ["Sceptre"]           = "Скипетр",
        ["Staff"]             = "Посох",
        ["Bow"]               = "Лук",
        ["Crossbow"]          = "Арбалет",
        ["Sword"]             = "Меч",
        ["One Handed Sword"]  = "Одноручный меч",
        ["Two Handed Sword"]  = "Двуручный меч",
        ["Axe"]               = "Топор",
        ["One Handed Axe"]    = "Одноручный топор",
        ["Two Handed Axe"]    = "Двуручный топор",
        ["Mace"]              = "Булава",
        ["One Handed Mace"]   = "Одноручная булава",
        ["Two Handed Mace"]   = "Двуручная булава",
        ["Dagger"]            = "Кинжал",
        ["Claw"]              = "Коготь",
        ["Spear"]             = "Копьё",
        ["Flail"]             = "Цеп",
        ["Focus"]             = "Сосредоточение",
        ["Buckler"]           = "Баклер",
    };

    // Common item-mod patterns. Keys are number-redacted (digits → '#'). The
    // translator replaces digits in the input with '#', looks the template up
    // here, then re-injects the original numbers into the Russian template.
    private static readonly Dictionary<string, string> _itemModTemplates = new(StringComparer.Ordinal)
    {
        // attributes
        ["+# to Strength"]                = "+# к силе",
        ["+# to Dexterity"]               = "+# к ловкости",
        ["+# to Intelligence"]            = "+# к интеллекту",
        ["+# to all Attributes"]          = "+# ко всем характеристикам",
        // life / mana / es
        ["+# to maximum Life"]            = "+# к максимуму здоровья",
        ["+# to maximum Mana"]            = "+# к максимуму маны",
        ["+# to maximum Energy Shield"]   = "+# к максимуму энерг. щита",
        ["#% increased maximum Life"]     = "+#% к максимуму здоровья",
        ["#% increased maximum Mana"]     = "+#% к максимуму маны",
        ["#% increased maximum Energy Shield"] = "+#% к максимуму энерг. щита",
        ["+# to Spirit"]                  = "+# к духу",
        // regen / leech
        ["#% of Life Regenerated per second"]    = "#% здоровья восполняется в секунду",
        ["#% of Mana Regenerated per second"]    = "#% маны восполняется в секунду",
        ["+#% to Life Regeneration Rate"]        = "+#% к скорости восполнения здоровья",
        // resistances
        ["+#% to Fire Resistance"]        = "+#% к сопр. огню",
        ["+#% to Cold Resistance"]        = "+#% к сопр. холоду",
        ["+#% to Lightning Resistance"]   = "+#% к сопр. молнии",
        ["+#% to Chaos Resistance"]       = "+#% к сопр. хаосу",
        ["+#% to all Elemental Resistances"] = "+#% ко всем стих. сопр.",
        // damage
        ["#% increased Damage"]           = "#% увелич. урона",
        ["#% reduced Damage"]             = "#% уменьш. урона",
        ["#% increased Physical Damage"]  = "#% увелич. физ. урона",
        ["#% increased Elemental Damage"] = "#% увелич. стих. урона",
        ["#% increased Spell Damage"]     = "#% увелич. урона заклинаний",
        ["#% increased Attack Damage"]    = "#% увелич. урона атак",
        ["Adds # to # Physical Damage"]   = "Добавляет #-# физ. урона",
        ["Adds # to # Fire Damage"]       = "Добавляет #-# огн. урона",
        ["Adds # to # Cold Damage"]       = "Добавляет #-# хол. урона",
        ["Adds # to # Lightning Damage"]  = "Добавляет #-# молн. урона",
        ["Adds # to # Chaos Damage"]      = "Добавляет #-# урона хаосом",
        // crit
        ["+#% to Critical Hit Chance"]    = "+#% к шансу крит. удара",
        ["+#% to Critical Damage Bonus"]  = "+#% к бонусу крит. урона",
        ["#% increased Critical Hit Chance"] = "#% увелич. шанса крит. удара",
        // speed
        ["#% increased Attack Speed"]     = "#% увелич. скорости атаки",
        ["#% increased Cast Speed"]       = "#% увелич. скорости каста",
        ["#% increased Movement Speed"]   = "#% увелич. скорости передвижения",
        // armour / evasion / es
        ["#% increased Armour"]           = "#% увелич. брони",
        ["#% increased Evasion Rating"]   = "#% увелич. уклонения",
        ["#% increased Energy Shield"]    = "#% увелич. энерг. щита",
        ["#% increased Armour and Energy Shield"] = "#% увелич. брони и энерг. щита",
        ["#% increased Evasion and Energy Shield"] = "#% увелич. уклонения и энерг. щита",
        // misc
        ["Allies in your Presence deal #% increased Damage"]
            = "Союзники рядом наносят на #% больше урона",
        ["Allies in your Presence have #% increased Cast Speed"]
            = "Союзники рядом имеют +#% к скорости каста",
        ["Allies in your Presence have #% increased Attack Speed"]
            = "Союзники рядом имеют +#% к скорости атаки",
        ["Allies in your Presence have #% increased Critical Hit Chance"]
            = "Союзники рядом имеют +#% к шансу крит. удара",
        ["Allies in your Presence have #% increased Critical Damage Bonus"]
            = "Союзники рядом имеют +#% к бонусу крит. урона",
        ["Allies in your Presence deal # to # added Attack Fire Damage"]
            = "Союзники рядом наносят дополнительно #-# огн. урона атаками",
        ["Allies in your Presence deal # to # added Attack Lightning Damage"]
            = "Союзники рядом наносят дополнительно #-# молн. урона атаками",
        ["Aura Skills have #% increased Magnitudes"]
            = "Умения-ауры имеют +#% к величине эффектов",
        ["#% increased Stun Threshold"]   = "#% увелич. порога оглушения",
        ["#% reduced Attribute Requirements"] = "#% уменьш. требований к характеристикам",
        ["+#% to Block Chance"]           = "+#% к шансу блока",
        // resistances — combined / per socket / etc
        ["+#% to Fire and Chaos Resistances"]    = "+#% к сопр. огню и хаосу",
        ["+#% to Fire and Cold Resistances"]     = "+#% к сопр. огню и холоду",
        ["+#% to Fire and Lightning Resistances"] = "+#% к сопр. огню и молнии",
        ["+#% to Cold and Lightning Resistances"] = "+#% к сопр. холоду и молнии",
        ["+#% to Cold and Chaos Resistances"]    = "+#% к сопр. холоду и хаосу",
        ["+#% to Lightning and Chaos Resistances"] = "+#% к сопр. молнии и хаосу",
        ["+#% to all Elemental Resistances per Socket filled"]
            = "+#% ко всем стих. сопр. за каждый занятый сокет",
        // levels of skills
        ["+# to Level of all Spell Skills"]          = "+# к уровню всех заклинаний",
        ["+# to Level of all Attack Skills"]         = "+# к уровню всех атак",
        ["+# to Level of all Minion Skills"]         = "+# к уровню всех умений прислужников",
        ["+# to Level of all Fire Spell Skills"]     = "+# к уровню всех огн. заклинаний",
        ["+# to Level of all Cold Spell Skills"]     = "+# к уровню всех хол. заклинаний",
        ["+# to Level of all Lightning Spell Skills"] = "+# к уровню всех молн. заклинаний",
        ["+# to Level of all Chaos Spell Skills"]    = "+# к уровню всех заклинаний хаоса",
        ["+# to Level of all Physical Spell Skills"] = "+# к уровню всех физ. заклинаний",
        // gain extra elemental damage
        ["Gain #% of Damage as Extra Fire Damage"]      = "Прибавляет #% урона как доп. огн. урон",
        ["Gain #% of Damage as Extra Cold Damage"]      = "Прибавляет #% урона как доп. хол. урон",
        ["Gain #% of Damage as Extra Lightning Damage"] = "Прибавляет #% урона как доп. молн. урон",
        ["Gain #% of Damage as Extra Chaos Damage"]     = "Прибавляет #% урона как доп. урон хаосом",
        // crit / spell mods
        ["#% increased Critical Hit Chance for Spells"]   = "#% увелич. шанса крит. удара заклинаний",
        ["#% increased Critical Damage Bonus for Spells"] = "#% увелич. бонуса крит. урона заклинаний",
        ["+#% to Critical Damage Bonus for Spells"]       = "+#% к бонусу крит. урона заклинаний",
        // spirit
        ["+# to Spirit per Socket filled"]   = "+# к духу за каждый занятый сокет",
        ["#% increased Spirit"]              = "#% увелич. духа",
        ["+# to maximum Mana per Socket filled"]   = "+# к макс. мане за каждый занятый сокет",
        ["+# to maximum Life per Socket filled"]   = "+# к макс. здоровью за каждый занятый сокет",
        // regen / recovery
        ["#% increased Mana Regeneration Rate"]   = "#% увелич. скорости восполнения маны",
        ["#% increased Life Regeneration Rate"]   = "#% увелич. скорости восполнения здоровья",
        ["#% increased Energy Shield Recharge Rate"] = "#% увелич. скорости перезарядки энерг. щита",
        // rarity / quantity
        ["#% increased Rarity of Items found"]   = "#% увелич. редкости находимых предметов",
        ["#% increased Quantity of Items found"] = "#% увелич. количества находимых предметов",
        // charm / flask
        ["Has # Charm Slots"]                = "Содержит # ячеек оберегов",
        ["Has # Charm Slot"]                 = "Содержит # ячейку оберегов",
        ["Consumes # of # Charges on use"]   = "Тратит # из # зарядов при использовании",
        ["#% increased Charges gained"]      = "#% увелич. получения зарядов",
        ["#% reduced Charges per use"]       = "#% уменьш. зарядов за использование",
        ["#% increased Charges"]             = "#% увелич. зарядов",
        ["#% increased Duration"]            = "#% увелич. длительности",
        ["#% reduced Amount Recovered"]      = "#% уменьш. восстанавливаемого количества",
        // movement / area
        ["#% reduced Presence Area of Effect"] = "#% уменьш. области присутствия",
        // armour/evasion/es combined
        ["#% increased Armour, Evasion and Energy Shield"]
            = "#% увелич. брони, уклонения и энерг. щита",
        // reservation
        ["#% increased Reservation Efficiency of Skills which create Undead Minions"]
            = "#% увелич. эффективности резервирования умений, создающих нежить",
        // bonded conditional (suffix added by Bonded: handler, but inner stat needs templates)
        ["#% increased Mana Cost Efficiency while on Low Mana"]
            = "#% увелич. эффективности маны при низком запасе маны",
        // rune mods (Soul Cores etc.)
        ["Leeches #% of Physical Damage as Mana"]   = "Похищение #% физ. урона в виде маны",
        ["Leeches #% of Physical Damage as Life"]   = "Похищение #% физ. урона в виде здоровья",
        ["+#% of Armour also applies to Cold Damage"]      = "+#% брони также применяется к холодному урону",
        ["+#% of Armour also applies to Lightning Damage"] = "+#% брони также применяется к молниевому урону",
        ["+#% of Armour also applies to Fire Damage"]      = "+#% брони также применяется к огненному урону",
        ["#% faster start of Energy Shield Recharge"]      = "#% ускорение начала перезарядки энерг. щита",
        ["#% increased Skill Effect Duration"]      = "#% увелич. длительности эффекта умений",
        ["#% increased Cooldown Recovery Rate"]     = "#% увелич. скорости восстановления отката",
        ["+# to Maximum Rage"]                      = "+# к максимуму ярости",
        ["#% increased Curse Duration"]             = "#% увелич. длительности проклятий",
        ["#% increased Poison Duration"]            = "#% увелич. длительности отравления",
        ["+#% Critical Hit Chance"]                 = "+#% к шансу крит. удара",
        ["+#% Critical Damage Bonus"]               = "+#% к бонусу крит. урона",
        ["#% increased Stun Threshold"]             = "#% увелич. порога оглушения",
        ["#% reduced Mana Cost of Skills"]          = "#% уменьш. стоимости маны умений",
        ["#% increased Damage while Shapeshifted"] = "#% увелич. урона в форме оборотня",
        // flask
        ["#% more Recovery if used while on Low Mana"] = "#% увелич. восстановления при низком запасе маны",
        ["Charge gain modifier: +#%"]       = "Модификатор получения зарядов: +#%",
        ["Charge gain modifier: -#%"]       = "Модификатор получения зарядов: -#%",
        // damage with subtype
        ["#% increased Damage with Plant Skills"] = "#% увелич. урона умениями растений",
        // flask recovery details
        ["Recovers # Life instantly"]         = "Мгновенно восстанавливает # здоровья",
        ["Recovers # Mana instantly"]         = "Мгновенно восстанавливает # маны",
        ["Recovers # Life over # Seconds"]    = "Восстанавливает # здоровья за # сек.",
        ["Recovers # Mana over # Seconds"]    = "Восстанавливает # маны за # сек.",
        ["Life recovered: # instantly"]       = "Восстановлено здоровья: # мгновенно",
        ["Mana recovered: # instantly"]       = "Восстановлено маны: # мгновенно",
        ["Life recovered: # over #s"]         = "Восстановлено здоровья: # за # с.",
        ["Mana recovered: # over #s"]         = "Восстановлено маны: # за # с.",
        ["Life recovered: # over #.##s"]      = "Восстановлено здоровья: # за # с.",
        ["Mana recovered: # over #.##s"]      = "Восстановлено маны: # за # с.",
        // charm timing / triggers
        ["Lasts # Seconds"]                   = "Длится # сек.",
        ["Lasts #.# Seconds"]                 = "Длится # сек.",
        // ignite / shock
        ["#% increased Ignite Magnitude"]     = "#% увелич. величины поджигов",
        ["#% increased Critical Spell Damage Bonus"] = "#% увелич. бонуса крит. урона заклинаний",
        ["#% reduced Ignite Duration on you"] = "#% уменьш. длительности поджигов на вас",
        ["#% increased Elemental Damage"]     = "#% увелич. стих. урона",
    };

    /// <summary>Exact (no-number) item-mod translations, e.g. boolean flags.</summary>
    private static readonly Dictionary<string, string> _itemModExact = new(StringComparer.Ordinal)
    {
        ["Cannot be Frozen"]              = "Невозможно заморозить",
        ["Cannot be Shocked"]             = "Невозможно поразить молнией",
        ["Cannot be Ignited"]             = "Невозможно поджечь",
        ["Cannot be Poisoned"]            = "Невозможно отравить",
        ["Immune to Freeze"]              = "Иммунитет к заморозке",
        ["Immune to Ignite"]              = "Иммунитет к поджигу",
        ["Immune to Shock"]               = "Иммунитет к шоку",
        ["Immune to Poison"]              = "Иммунитет к отравлению",
        ["Immune to Chill"]               = "Иммунитет к охлаждению",
        ["Instant Recovery"]              = "Мгновенное восстановление",
        ["Used when you kill a Rare or Unique enemy"]
            = "Срабатывает при убийстве редкого или уникального врага",
        ["Used when you Hit a Rare or Unique enemy"]
            = "Срабатывает при попадании по редкому или уникальному врагу",
        ["Used when you are hit by an enemy"]
            = "Срабатывает при получении удара от врага",
        ["Used when you reach Low Life"]
            = "Срабатывает при низком запасе здоровья",
        ["Used when you reach Low Mana"]
            = "Срабатывает при низком запасе маны",
        ["Upgrades Radius to Large"]      = "Увеличивает радиус до большого",
        ["Upgrades Radius to Medium"]     = "Увеличивает радиус до среднего",
        ["Upgrades Radius to Very Large"] = "Увеличивает радиус до очень большого",
    };

    /// <summary>
    /// Recursive prefix handlers: matched lines split into "&lt;prefix&gt; &lt;rest&gt;"
    /// where rest is translated through TooltipLine again. Order matters — longest
    /// prefix first.
    /// </summary>
    private static readonly (string, string)[] _recursivePrefixes = new[]
    {
        ("Notable Passive Skills in Radius also grant ", "Заметные пассивные узлы в радиусе также дают: "),
        ("Small Passive Skills in Radius also grant ",   "Малые пассивные узлы в радиусе также дают: "),
        ("Bonded: ",                                     "Связано: "),
    };

    private static readonly Dictionary<string, string> _attrTokens = new(StringComparer.Ordinal)
    {
        ["Str"] = "силы", ["Dex"] = "ловкости", ["Int"] = "интеллекта",
    };

    // Delta-block stat labels (without the leading +/- value).
    private static readonly Dictionary<string, string> _deltaLabels = new(StringComparer.Ordinal)
    {
        // attributes
        ["Strength"]      = "Сила",
        ["Dexterity"]     = "Ловкость",
        ["Intelligence"]  = "Интеллект",
        // life / mana / es
        ["Total Life"]    = "Общее здоровье",
        ["Total Mana"]    = "Общая мана",
        ["Total ES"]      = "Общий ЭЩ",
        ["Mana Regen"]    = "Регенерация маны",
        ["Life Regen"]    = "Регенерация здоровья",
        // defences
        ["Effective Hit Pool"]    = "Эфф. запас удара",
        ["Phys Max Hit"]          = "Макс. физ. удар",
        ["Fire Max Hit"]          = "Макс. огн. удар",
        ["Cold Max Hit"]          = "Макс. хол. удар",
        ["Lightning Max Hit"]     = "Макс. молн. удар",
        ["Elemental Max Hit"]     = "Макс. стих. удар",
        ["Chaos Max Hit"]         = "Макс. удар хаосом",
        ["Evasion Rating"]        = "Уклонение",
        ["Evade Chance"]          = "Шанс уклонения",
        ["Armour"]                = "Броня",
        ["Energy Shield"]         = "Энерг. щит",
        // requirements (negative — easier requirements)
        ["Strength Required"]     = "требование силы",
        ["Dexterity Required"]    = "требование ловкости",
        ["Intelligence Required"] = "требование интеллекта",
        // offence
        ["Average Damage"]        = "Средний урон",
        ["Hit DPS"]               = "DPS удара",
        ["Full DPS"]              = "Полный DPS",
        ["Combined DPS"]          = "Общий DPS",
        ["Effective Crit Chance"] = "Эфф. шанс крит. удара",
        ["Hit Chance"]            = "Шанс попадания",
        ["Attack/Cast Rate"]      = "Скорость атаки/каста",
        // resistances (delta value already has %, label has no number)
        ["Fire Resistance"]       = "Сопр. огню",
        ["Cold Resistance"]       = "Сопр. холоду",
        ["Lightning Resistance"]  = "Сопр. молнии",
        ["Chaos Resistance"]      = "Сопр. хаосу",
        ["Fire Res. Over Max"]    = "Сопр. огню сверх макс.",
        ["Cold Res. Over Max"]    = "Сопр. холоду сверх макс.",
        ["Lightning Res. Over Max"] = "Сопр. молнии сверх макс.",
        ["Chaos Res. Over Max"]   = "Сопр. хаосу сверх макс.",
        // spirit / movement
        ["Total Spirit"]          = "Общий дух",
        ["Unreserved Spirit"]     = "Свободный дух",
        ["Movement Speed Modifier"] = "Модификатор скорости передвижения",
        ["Phys. Damage Reduction"] = "Снижение физ. урона",
        ["Presence Radius"]       = "Радиус присутствия",
    };

    /// <summary>Resolve a slot/type token used inside tooltip lines; falls back to original.</summary>
    public string TooltipType(string name) =>
        _tooltipTypes.TryGetValue(name, out var ru) ? ru : name;

    /// <summary>
    /// Try to translate a magic item name "&lt;Prefix&gt; &lt;BaseName&gt; of &lt;Suffix&gt;"
    /// by locating the longest known base name substring and splitting around it.
    /// Returns null if no base match is found or both affixes are unknown.
    /// </summary>
    private string? TryTranslateMagicName(string line)
    {
        if (_items.Count == 0) return null;

        // Find the longest base name that appears inside the line surrounded by
        // word boundaries (start-of-line or space; end-of-line or space).
        string? bestBase = null;
        int bestIdx = -1;
        foreach (var k in _items.Keys)
        {
            var idx = line.IndexOf(k, StringComparison.Ordinal);
            if (idx < 0) continue;
            // boundary check
            if (idx > 0 && line[idx - 1] != ' ') continue;
            var endPos = idx + k.Length;
            if (endPos < line.Length && line[endPos] != ' ') continue;
            if (bestBase is null || k.Length > bestBase.Length)
            {
                bestBase = k; bestIdx = idx;
            }
        }
        if (bestBase is null) return null;

        var prefix = bestIdx > 0 ? line[..bestIdx].TrimEnd() : "";
        var suffixPart = bestIdx + bestBase.Length < line.Length
            ? line[(bestIdx + bestBase.Length)..].TrimStart()
            : "";

        // Reject the case where there's no affix at all — that would be a bare base name,
        // handled by the earlier _items lookup.
        if (prefix.Length == 0 && suffixPart.Length == 0) return null;

        var baseRu = _items.TryGetValue(bestBase, out var br) ? br : bestBase;

        var parts = new List<string>(3);
        if (prefix.Length > 0)
            parts.Add(_magicPrefixes.TryGetValue(prefix, out var pr) ? pr : prefix);
        parts.Add(baseRu);
        if (suffixPart.Length > 0)
            parts.Add(_magicSuffixes.TryGetValue(suffixPart, out var sr) ? sr : suffixPart);

        return string.Join(" ", parts);
    }

    public static string TTooltipLine(string line) => Instance.TooltipLine(line);
    public static string TTooltipType(string name) => Instance.TooltipType(name);

    public string PassiveName(string englishName) =>
        _passiveNames.TryGetValue(englishName, out var ru) ? ru : englishName;

    public string SkillDescription(string englishDesc) =>
        _skillDescriptions.TryGetValue(englishDesc, out var ru) ? ru : englishDesc;

    // "Level: 20 (Max)"  →  "Уровень: 20 (Макс)"
    public string GemMetaLine(string line)
    {
        if (_gemMeta.Count == 0) return line;
        var colonIdx = line.IndexOf(": ", StringComparison.Ordinal);
        string label = colonIdx >= 0 ? line[..colonIdx] : line;
        string rest  = colonIdx >= 0 ? line[(colonIdx + 2)..] : "";
        if (_gemMeta.TryGetValue(label, out var tlabel))
            label = tlabel;
        if (rest.Length > 0)
        {
            // translate inline value words (order matters — longer first)
            foreach (var kv in _gemMeta)
                rest = rest.Replace(kv.Key, kv.Value, StringComparison.OrdinalIgnoreCase);
            return $"{label}: {rest}";
        }
        return label;
    }

    // "Spell, Duration, Physical"  →  "Заклинание, Длительность, Физический"
    public string GemTagLine(string tagString)
    {
        if (_gemTags.Count == 0) return tagString;
        var parts = tagString.Split(", ");
        for (int i = 0; i < parts.Length; i++)
            if (_gemTags.TryGetValue(parts[i].Trim(), out var t))
                parts[i] = t;
        return string.Join(", ", parts);
    }

    /// <summary>Translate a Config tab section name (e.g. "General" → "Общее").</summary>
    public string ConfigSection(string section) =>
        _configSections.TryGetValue(section, out var ru) ? ru : section;

    // Small inline dict of class/ascendancy names — there are only 6 classes and
    // ~20 ascendancies in PoE2, so no need for a separate JSON file.
    // Keys are the English names from PoB game data.
    private static readonly Dictionary<string, Dictionary<string, string>> _classAscNamesByLang = new()
    {
        ["ru"] = new(StringComparer.OrdinalIgnoreCase)
        {
            // Classes
            ["Huntress"]   = "Охотница",
            ["Warrior"]    = "Воин",
            ["Mercenary"]  = "Наёмник",
            ["Druid"]      = "Друид",
            ["Witch"]      = "Ведьма",
            ["Sorceress"]  = "Чародейка",
            ["TEMPLAR"]    = "ЖРЕЦ",
            // Generic
            ["None"]       = "Нет",
            // Huntress ascendancies
            ["Amazon"]            = "Амазонка",
            ["Ritualist"]         = "Ритуалистка",
            // Warrior ascendancies
            ["Titan"]             = "Титан",
            ["Warbringer"]        = "Воитель",
            ["Smith of Kitava"]   = "Кузнец Китавы",
            // Mercenary ascendancies
            ["Tactician"]         = "Тактик",
            ["Witchhunter"]       = "Охотник на ведьм",
            ["Gemling Legionnaire"] = "Самоцветный легионер",
            // Druid ascendancies
            ["Oracle"]            = "Оракул",
            ["Shaman"]            = "Шаман",
            // Witch ascendancies
            ["Infernalist"]       = "Инферналистка",
            ["Blood Mage"]        = "Кровавый маг",
            ["Lich"]              = "Лич",
            ["Abyssal Lich"]      = "Лич бездны",
            // Sorceress ascendancies
            ["Stormweaver"]       = "Ткач бури",
            ["Chronomancer"]      = "Хрономанка",
            ["Disciple of Varashta"] = "Послушница Варашты",
        },
    };

    /// <summary>Translate a character class or ascendancy display name (e.g. "Warrior" → "Воин").</summary>
    public string ClassOrAscendancyName(string englishName)
    {
        if (string.IsNullOrEmpty(englishName)) return englishName;
        if (_classAscNamesByLang.TryGetValue(_loadedLang, out var map)
            && map.TryGetValue(englishName, out var t))
            return t;
        return englishName;
    }

    /// <summary>
    /// Translate a Config tab option label. Key is the stable <c>var</c> name from ConfigOptions.lua.
    /// Falls back to the original English label if no translation is found.
    /// </summary>
    public string ConfigLabel(string var, string englishLabel) =>
        _configLabels.TryGetValue(var, out var ru) ? ru : englishLabel;

    public static string TGem(string name)                    => Instance.Gem(name);
    public static string TItem(string name)                   => Instance.Item(name);
    public static string TPassiveStat(string stat)            => Instance.PassiveStat(stat);
    public static string TPassiveName(string name)            => Instance.PassiveName(name);
    public static string TGemMetaLine(string line)            => Instance.GemMetaLine(line);
    public static string TGemTagLine(string tagString)        => Instance.GemTagLine(tagString);
    public static string TSkillDescription(string englishDesc) => Instance.SkillDescription(englishDesc);
    public static string TConfigSection(string section)       => Instance.ConfigSection(section);
    public static string TConfigLabel(string var, string label) => Instance.ConfigLabel(var, label);
    public static string TClassName(string englishName)      => Instance.ClassOrAscendancyName(englishName);
}
