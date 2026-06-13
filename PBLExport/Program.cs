using System.Text;
using System.Text.Json;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;

string repoRoot = FindRepoRoot();
string TransDir = Path.Combine(repoRoot, "PBLApp.Core", "Translations");
string GgpkDir  = Path.Combine(repoRoot, "PBLExport", "ggpk_export", "tables");

static string FindRepoRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir != null && !File.Exists(Path.Combine(dir.FullName, "PBLHost.sln"))
                       && !File.Exists(Path.Combine(dir.FullName, "manifest.xml")))
        dir = dir.Parent;
    if (dir == null)
        throw new InvalidOperationException("Не найден корень репо (искал PBLHost.sln / manifest.xml).");
    return dir.FullName;
}

var opts = new JsonSerializerOptions
{
    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    WriteIndented = false
};

// ---------------------------------------------------------------------------
// 1. gems_ru.json  (from repoe-fork)
// ---------------------------------------------------------------------------
var skillGemsEn = Path.Combine(TransDir, "skill_gems_en.json");
var skillGemsRu = Path.Combine(TransDir, "skill_gems_ru.json");
if (File.Exists(skillGemsEn) && File.Exists(skillGemsRu))
{
    Console.WriteLine("Building gems_ru.json...");
    var enGems = JsonDocument.Parse(File.ReadAllText(skillGemsEn));
    var ruGems = JsonDocument.Parse(File.ReadAllText(skillGemsRu));
    var gemMap = new Dictionary<string, string>();

    foreach (var prop in enGems.RootElement.EnumerateObject())
    {
        var key = prop.Name;
        if (!prop.Value.TryGetProperty("base_item", out var enBase)) continue;
        if (!enBase.TryGetProperty("display_name", out var enNameEl)) continue;
        var enName = enNameEl.GetString();
        if (string.IsNullOrEmpty(enName)) continue;

        if (!ruGems.RootElement.TryGetProperty(key, out var ruEntry)) continue;
        if (!ruEntry.TryGetProperty("base_item", out var ruBase)) continue;
        if (!ruBase.TryGetProperty("display_name", out var ruNameEl)) continue;
        var ruName = ruNameEl.GetString();
        if (!string.IsNullOrEmpty(ruName) && ruName != enName)
            gemMap[enName] = ruName;
    }
    WriteJson(Path.Combine(TransDir, "gems_ru.json"), gemMap, opts);
}
else
{
    Console.WriteLine($"[SKIP] gems_ru.json — repoe-fork dumps missing ({skillGemsEn})");
    Console.WriteLine("       Drop skill_gems_{en,ru}.json from RePoE/PoE2 fork into PBLApp.Core/Translations/");
}

// ---------------------------------------------------------------------------
// 2. items_ru.json  (from GGPK export via pathofexile-dat; falls back to repoe-fork)
//    repoe-fork dropped the Russian/ dumps (404 as of 0.5), so the official
//    GGPK BaseItemTypes table is now the primary source. Join EN→RU by Id
//    (the Metadata path). Legacy entries the current export lacks (e.g. PoE1
//    Sentinel content PoB still references) are preserved from the prior file.
// ---------------------------------------------------------------------------
var itemsOut       = Path.Combine(TransDir, "items_ru.json");
var baseItemsEnGgpk = Path.Combine(GgpkDir, "English", "BaseItemTypes.json");
var baseItemsRuGgpk = Path.Combine(GgpkDir, "Russian", "BaseItemTypes.json");
var baseItemsEn = Path.Combine(TransDir, "base_items_en.json");
var baseItemsRu = Path.Combine(TransDir, "base_items_ru.json");
if (File.Exists(baseItemsEnGgpk) && File.Exists(baseItemsRuGgpk))
{
    Console.WriteLine("Building items_ru.json (from GGPK)...");
    var biEn = JsonDocument.Parse(File.ReadAllText(baseItemsEnGgpk)).RootElement;
    var biRu = JsonDocument.Parse(File.ReadAllText(baseItemsRuGgpk)).RootElement;

    // Index RU names by Id (Metadata path).
    var ruById = new Dictionary<string, string>(StringComparer.Ordinal);
    foreach (var row in biRu.EnumerateArray())
    {
        if (!row.TryGetProperty("Id", out var idEl)) continue;
        if (!row.TryGetProperty("Name", out var nameEl)) continue;
        var id = idEl.GetString() ?? "";
        var nm = nameEl.GetString() ?? "";
        if (id.Length > 0 && nm.Length > 0) ruById[id] = nm;
    }

    var itemMap = new SortedDictionary<string, string>(StringComparer.Ordinal);
    foreach (var row in biEn.EnumerateArray())
    {
        if (!row.TryGetProperty("Id", out var idEl)) continue;
        if (!row.TryGetProperty("Name", out var nameEl)) continue;
        var id     = idEl.GetString() ?? "";
        var enName = nameEl.GetString() ?? "";
        if (enName.Length == 0 || enName.StartsWith("[DNT")) continue;
        if (!ruById.TryGetValue(id, out var ruName)) continue;
        if (ruName.Length == 0 || ruName.StartsWith("[DNT")) continue;
        if (ruName != enName) itemMap[enName] = ruName;
    }

    // Hand entries for PoB-internal pseudo-bases that have no GGPK row
    // (Shrine Sceptre variants carry a "(Purity of …)" suffix PoB appends;
    // "Lighting" is PoB's own misspelling of Lightning).
    var handItems = new Dictionary<string, string>
    {
        ["Shrine Sceptre (Purity of Cold)"]     = "Скипетр святыни (Спасение от холода)",
        ["Shrine Sceptre (Purity of Fire)"]     = "Скипетр святыни (Спасение от огня)",
        ["Shrine Sceptre (Purity of Lighting)"] = "Скипетр святыни (Спасение от молний)",
    };
    foreach (var (en, ru) in handItems) itemMap.TryAdd(en, ru);

    // Preserve prior entries the current GGPK export doesn't cover (GGPK wins
    // on conflicts because TryAdd never overwrites an existing key).
    if (File.Exists(itemsOut))
    {
        var prev = JsonSerializer.Deserialize<Dictionary<string, string>>(
                       File.ReadAllText(itemsOut)) ?? new();
        foreach (var (en, ru) in prev) itemMap.TryAdd(en, ru);
    }

    WriteJson(itemsOut, itemMap, opts);
}
else if (File.Exists(baseItemsEn) && File.Exists(baseItemsRu))
{
    Console.WriteLine("Building items_ru.json (from repoe-fork)...");
    var enItems = JsonDocument.Parse(File.ReadAllText(baseItemsEn));
    var ruItems = JsonDocument.Parse(File.ReadAllText(baseItemsRu));
    var itemMap = new Dictionary<string, string>();

    foreach (var prop in enItems.RootElement.EnumerateObject())
    {
        var key = prop.Name;
        if (!prop.Value.TryGetProperty("name", out var enNameEl)) continue;
        var enName = enNameEl.GetString();
        if (string.IsNullOrEmpty(enName)) continue;

        if (!ruItems.RootElement.TryGetProperty(key, out var ruEntry)) continue;
        if (!ruEntry.TryGetProperty("name", out var ruNameEl)) continue;
        var ruName = ruNameEl.GetString();
        if (!string.IsNullOrEmpty(ruName) && ruName != enName)
            itemMap[enName] = ruName;
    }
    WriteJson(itemsOut, itemMap, opts);
}
else
{
    Console.WriteLine($"[SKIP] items_ru.json — neither GGPK tables ({baseItemsEnGgpk}) nor repoe-fork dumps ({baseItemsEn}) present");
}

// ---------------------------------------------------------------------------
// 3. passive_names_ru.json  (from GGPK export via pathofexile-dat)
// ---------------------------------------------------------------------------
var passiveEnPath = Path.Combine(GgpkDir, "English", "PassiveSkills.json");
var passiveRuPath = Path.Combine(GgpkDir, "Russian", "PassiveSkills.json");

if (File.Exists(passiveEnPath) && File.Exists(passiveRuPath))
{
    Console.WriteLine("Building passive_names_ru.json (from GGPK)...");

    var passiveEn = JsonDocument.Parse(File.ReadAllText(passiveEnPath)).RootElement;
    var passiveRu = JsonDocument.Parse(File.ReadAllText(passiveRuPath)).RootElement;

    // Index RU by PassiveSkillGraphId
    var ruByGraphId = new Dictionary<int, string>();
    foreach (var node in passiveRu.EnumerateArray())
    {
        if (!node.TryGetProperty("PassiveSkillGraphId", out var idEl)) continue;
        if (!node.TryGetProperty("Name", out var nameEl)) continue;
        var id = idEl.GetInt32();
        var name = nameEl.GetString() ?? "";
        if (id > 0 && !string.IsNullOrEmpty(name) && !name.StartsWith("[DNT"))
            ruByGraphId[id] = name;
    }

    var passiveNameMap = new SortedDictionary<string, string>(StringComparer.Ordinal);
    foreach (var node in passiveEn.EnumerateArray())
    {
        if (!node.TryGetProperty("PassiveSkillGraphId", out var idEl)) continue;
        if (!node.TryGetProperty("Name", out var nameEl)) continue;
        var id = idEl.GetInt32();
        var enName = nameEl.GetString() ?? "";
        if (id <= 0 || string.IsNullOrEmpty(enName) || enName.StartsWith("[DNT")) continue;

        if (!ruByGraphId.TryGetValue(id, out var ruName)) continue;
        if (enName != ruName && !passiveNameMap.ContainsKey(enName))
            passiveNameMap[enName] = ruName;
    }

    // Hand translations for nodes GGG hasn't localised yet (RU == EN in GGPK
    // as of 0.5.1). Re-checked on every regen: once official RU appears,
    // the GGPK value wins because TryAdd doesn't overwrite it.
    var handNames = new Dictionary<string, string>
    {
        ["Bond of the Ape"]   = "Узы обезьяны",
        ["Bond of the Cat"]   = "Узы кошки",
        ["Bond of the Mamba"] = "Узы мамбы",
        ["Bond of the Owl"]   = "Узы совы",
        ["Bond of the Viper"] = "Узы гадюки",
        ["Bond of the Wolf"]  = "Узы волка",
        ["Elemental"]         = "Стихии",
        ["Physical"]          = "Физический урон",
    };
    foreach (var (en, ru) in handNames)
        passiveNameMap.TryAdd(en, ru);

    WriteJson(Path.Combine(TransDir, "passive_names_ru.json"), passiveNameMap, opts);
}
else
{
    Console.WriteLine($"[SKIP] passive_names_ru.json — GGPK export not found at {passiveEnPath}");
    Console.WriteLine("       Run: cd PBLExport/ggpk_export && pathofexile-dat");
}

// ---------------------------------------------------------------------------
// 4. skill_descriptions_ru.json  (from GGPK export via pathofexile-dat)
// ---------------------------------------------------------------------------
var skillEnPath = Path.Combine(GgpkDir, "English", "ActiveSkills.json");
var skillRuPath = Path.Combine(GgpkDir, "Russian", "ActiveSkills.json");

if (File.Exists(skillEnPath) && File.Exists(skillRuPath))
{
    Console.WriteLine("Building skill_descriptions_ru.json (from GGPK)...");

    var skillEn = JsonDocument.Parse(File.ReadAllText(skillEnPath)).RootElement;
    var skillRu = JsonDocument.Parse(File.ReadAllText(skillRuPath)).RootElement;

    // Index EN by Id
    var enById = new Dictionary<string, string>(StringComparer.Ordinal);
    foreach (var skill in skillEn.EnumerateArray())
    {
        if (!skill.TryGetProperty("Id", out var idEl)) continue;
        if (!skill.TryGetProperty("Description", out var descEl)) continue;
        var id   = idEl.GetString() ?? "";
        var desc = StripMarkup(descEl.GetString() ?? "");
        if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(desc))
            enById[id] = desc;
    }

    var descMap = new SortedDictionary<string, string>(StringComparer.Ordinal);
    foreach (var skill in skillRu.EnumerateArray())
    {
        if (!skill.TryGetProperty("Id", out var idEl)) continue;
        if (!skill.TryGetProperty("Description", out var descEl)) continue;
        var id     = idEl.GetString() ?? "";
        var ruDesc = StripMarkup(descEl.GetString() ?? "");
        if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(ruDesc)) continue;
        if (ruDesc.StartsWith("[DNT")) continue;

        if (!enById.TryGetValue(id, out var enDesc)) continue;
        if (enDesc == ruDesc || enDesc.StartsWith("[DNT")) continue;
        if (!descMap.ContainsKey(enDesc))
            descMap[enDesc] = ruDesc;
    }

    // Support gem descriptions live in GemEffects.SupportText, not ActiveSkills —
    // without this block none of the ~490 support texts get translated.
    var gemFxEnPath = Path.Combine(GgpkDir, "English", "GemEffects.json");
    var gemFxRuPath = Path.Combine(GgpkDir, "Russian", "GemEffects.json");
    if (File.Exists(gemFxEnPath) && File.Exists(gemFxRuPath))
    {
        var fxEn = JsonDocument.Parse(File.ReadAllText(gemFxEnPath)).RootElement;
        var fxRu = JsonDocument.Parse(File.ReadAllText(gemFxRuPath)).RootElement;

        var fxEnById = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var fx in fxEn.EnumerateArray())
        {
            if (!fx.TryGetProperty("Id", out var idEl)) continue;
            if (!fx.TryGetProperty("SupportText", out var txtEl)) continue;
            var id  = idEl.GetString() ?? "";
            var txt = StripMarkup(txtEl.GetString() ?? "");
            if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(txt))
                fxEnById[id] = txt;
        }

        var before = descMap.Count;
        foreach (var fx in fxRu.EnumerateArray())
        {
            if (!fx.TryGetProperty("Id", out var idEl)) continue;
            if (!fx.TryGetProperty("SupportText", out var txtEl)) continue;
            var id    = idEl.GetString() ?? "";
            var ruTxt = StripMarkup(txtEl.GetString() ?? "");
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(ruTxt)) continue;
            if (ruTxt.StartsWith("[DNT")) continue;

            if (!fxEnById.TryGetValue(id, out var enTxt)) continue;
            if (enTxt == ruTxt || enTxt.StartsWith("[DNT")) continue;
            if (!descMap.ContainsKey(enTxt))
                descMap[enTxt] = ruTxt;
        }
        Console.WriteLine($"  +{descMap.Count - before} support texts from GemEffects");
    }
    else
    {
        Console.WriteLine($"[SKIP] support texts — GemEffects export not found at {gemFxEnPath}");
    }

    WriteJson(Path.Combine(TransDir, "skill_descriptions_ru.json"), descMap, opts);
}
else
{
    Console.WriteLine($"[SKIP] skill_descriptions_ru.json — GGPK export not found at {skillEnPath}");
}

// ---------------------------------------------------------------------------
// 5. class_names_ru.json  (Characters + Ascendancy tables — official RU names
//    for classes and ascendancies; the UI Tree tab dropdowns use these)
// ---------------------------------------------------------------------------
var classMap = new SortedDictionary<string, string>(StringComparer.Ordinal);
foreach (var tbl in new[] { "Characters", "Ascendancy" })
{
    var enPath = Path.Combine(GgpkDir, "English", tbl + ".json");
    var ruPath = Path.Combine(GgpkDir, "Russian", tbl + ".json");
    if (!File.Exists(enPath) || !File.Exists(ruPath))
    {
        Console.WriteLine($"[SKIP] class names — {tbl} export not found at {enPath}");
        continue;
    }
    var enRows = JsonDocument.Parse(File.ReadAllText(enPath)).RootElement;
    var ruRows = JsonDocument.Parse(File.ReadAllText(ruPath)).RootElement;
    var ruByIdx = new Dictionary<int, string>();
    foreach (var r in ruRows.EnumerateArray())
        if (r.TryGetProperty("_index", out var ix) && r.TryGetProperty("Name", out var nm))
            ruByIdx[ix.GetInt32()] = nm.GetString() ?? "";
    foreach (var r in enRows.EnumerateArray())
    {
        if (!r.TryGetProperty("_index", out var ix) || !r.TryGetProperty("Name", out var nm)) continue;
        var en = nm.GetString() ?? "";
        var ru = ruByIdx.GetValueOrDefault(ix.GetInt32(), "");
        if (string.IsNullOrEmpty(en) || string.IsNullOrEmpty(ru)) continue;
        if (en.StartsWith("[DNT") || en == ru) continue;
        classMap[en] = ru;
    }
}
if (classMap.Count > 0)
{
    classMap.TryAdd("None", "Нет");
    WriteJson(Path.Combine(TransDir, "class_names_ru.json"), classMap, opts);
}

Console.WriteLine("Done.");

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------
static void WriteJson<TKey, TValue>(string path, IDictionary<TKey, TValue> map,
    JsonSerializerOptions opts)
{
    File.WriteAllText(path, JsonSerializer.Serialize(map, opts), Encoding.UTF8);
    Console.WriteLine($"  {map.Count} entries → {path} ({new FileInfo(path).Length} bytes)");
}

// Strip game rich-text markup: [key|display] -> display, [key] -> key
static string StripMarkup(string text)
{
    if (string.IsNullOrEmpty(text)) return text;
    var s = Regex.Replace(text, @"\[([^\|\]]+)\|([^\]]*)\]", "$2");
    s = Regex.Replace(s, @"\[([^\]]*)\]", "$1");
    return s.Trim();
}
