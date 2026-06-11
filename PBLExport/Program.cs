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
// 2. items_ru.json  (from repoe-fork)
// ---------------------------------------------------------------------------
var baseItemsEn = Path.Combine(TransDir, "base_items_en.json");
var baseItemsRu = Path.Combine(TransDir, "base_items_ru.json");
if (File.Exists(baseItemsEn) && File.Exists(baseItemsRu))
{
    Console.WriteLine("Building items_ru.json...");
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
    WriteJson(Path.Combine(TransDir, "items_ru.json"), itemMap, opts);
}
else
{
    Console.WriteLine($"[SKIP] items_ru.json — repoe-fork dumps missing ({baseItemsEn})");
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

    WriteJson(Path.Combine(TransDir, "skill_descriptions_ru.json"), descMap, opts);
}
else
{
    Console.WriteLine($"[SKIP] skill_descriptions_ru.json — GGPK export not found at {skillEnPath}");
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
