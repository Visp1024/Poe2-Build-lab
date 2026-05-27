using ModelContextProtocol.Server;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace PBLMcp;

[McpServerToolType]
public class PBLTools(PBLState state)
{
    // ── Build lifecycle ────────────────────────────────────────────────────

    [McpServerTool]
    [Description("Create a new empty build and return its core stats.")]
    public string NewBuild()
    {
        try
        {
            state.Host.NewBuild();
            return FormatStats(state.Host.GetAllStats(), CoreStatKeys);
        }
        catch (Exception ex) { return Error(ex); }
    }

    [McpServerTool]
    [Description(
        "Load a Path of Building build from an XML file path or raw XML string. " +
        "Pass either a full file path (e.g. C:\\...\\MyBuild.xml) or the raw XML content. " +
        "Returns core stats after loading.")]
    public string LoadBuild(
        [Description("Full path to a .xml build file, OR the raw XML text of the build.")] string source)
    {
        try
        {
            string xml, name;
            if (File.Exists(source))
            {
                xml  = File.ReadAllText(source);
                name = Path.GetFileNameWithoutExtension(source);
            }
            else
            {
                xml  = source;
                name = "Imported Build";
            }
            state.Host.LoadBuildFromXml(xml, name);
            return FormatStats(state.Host.GetAllStats(), CoreStatKeys);
        }
        catch (Exception ex) { return Error(ex); }
    }

    [McpServerTool]
    [Description("Return the XML of the currently loaded build.")]
    public string GetBuildXml()
    {
        try { return state.Host.SaveBuildToXml() ?? "(no build loaded)"; }
        catch (Exception ex) { return Error(ex); }
    }

    // ── Stats ──────────────────────────────────────────────────────────────

    [McpServerTool]
    [Description(
        "Get stats from the current build. " +
        "Pass keys='' to get all ~600 stats, or a comma-separated list like " +
        "'TotalDPS,Life,Mana,Armour'. " +
        "Common keys: TotalDPS, AverageDamage, Speed, CritChance, CritMultiplier, " +
        "HitChance, Life, Mana, EnergyShield, Armour, Evasion, PhysicalReduction, ManaCost.")]
    public string GetStats(
        [Description("Comma-separated stat keys to return, or empty for all.")] string keys = "")
    {
        try
        {
            var all = state.Host.GetAllStats();
            if (string.IsNullOrWhiteSpace(keys))
                return FormatStats(all, filter: null);

            var requested = keys
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return FormatStats(all, requested);
        }
        catch (Exception ex) { return Error(ex); }
    }

    // ── Build modification ─────────────────────────────────────────────────

    [McpServerTool]
    [Description(
        "Set custom mods on the current build and recalculate. " +
        "Each mod on its own line, e.g. '+100 to maximum life\\n10% increased maximum Energy Shield'. " +
        "Pass empty string to clear. Returns updated core stats.")]
    public string SetCustomMods(
        [Description("Newline-separated mod strings.")] string mods)
    {
        try
        {
            state.Host.State["_mods"] = mods;
            state.Host.State.DoString(@"
                build.configTab.input.customMods = _mods
                build.configTab:BuildModList()
                runCallback('OnFrame')
            ");
            state.Host.State["_mods"] = null;
            return FormatStats(state.Host.GetAllStats(), CoreStatKeys);
        }
        catch (Exception ex) { return Error(ex); }
    }

    [McpServerTool]
    [Description(
        "Add a skill group using PoB paste format. " +
        "Format: 'GemName level/quality  slotIndex\\n' per gem. " +
        "Example: 'Ball Lightning 20/20  1\\nAdded Lightning Damage Support 20/20  1\\n'. " +
        "Returns updated core stats.")]
    public string AddSkill(
        [Description("Skill group in PoB paste format.")] string skillGroup)
    {
        try
        {
            state.Host.State["_sg"] = skillGroup;
            state.Host.State.DoString(@"
                build.skillsTab:PasteSocketGroup(_sg)
                runCallback('OnFrame')
            ");
            state.Host.State["_sg"] = null;
            return FormatStats(state.Host.GetAllStats(), CoreStatKeys);
        }
        catch (Exception ex) { return Error(ex); }
    }

    [McpServerTool]
    [Description(
        "Equip an item from raw item text (as shown in-game). " +
        "Example: 'New Item\\nHeavy Bow\\n25% increased Critical Damage Bonus'. " +
        "Returns updated core stats.")]
    public string EquipItem(
        [Description("Raw item text, lines separated by \\n.")] string itemText)
    {
        try
        {
            state.Host.State["_itemText"] = itemText;
            state.Host.State.DoString(@"
                build.itemsTab:CreateDisplayItemFromRaw(_itemText)
                build.itemsTab:AddDisplayItem()
                runCallback('OnFrame')
            ");
            state.Host.State["_itemText"] = null;
            return FormatStats(state.Host.GetAllStats(), CoreStatKeys);
        }
        catch (Exception ex) { return Error(ex); }
    }

    // ── Build list ─────────────────────────────────────────────────────────

    [McpServerTool]
    [Description(
        "List all saved builds from the PoB builds directory. " +
        "Returns JSON array with 'name' and 'path' for each build.")]
    public string ListBuilds()
    {
        try
        {
            var root = GetBuildsPath();
            if (!Directory.Exists(root))
                return $"Builds directory not found: {root}";

            var entries = new List<object>();
            CollectBuilds(entries, root, "");
            return JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex) { return Error(ex); }
    }

    // ── Lua escape hatch ───────────────────────────────────────────────────

    [McpServerTool]
    [Description(
        "Execute a Lua snippet in the live PoB environment and return the result. " +
        "Example: 'return build.calcsTab.mainOutput.TotalDPS'. " +
        "Use for deep inspection not covered by other tools.")]
    public string RunLua(
        [Description("Lua code to execute. Use 'return' to return a value.")] string lua)
    {
        try
        {
            var result = state.Host.State.DoString(lua);
            if (result is null || result.Length == 0) return "(no return value)";
            return string.Join("\n", result.Select(r => r is null ? "nil" : r.ToString()));
        }
        catch (Exception ex) { return Error(ex); }
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static readonly string[] CoreStatKeys =
    [
        "TotalDPS", "AverageDamage", "Speed",
        "CritChance", "CritMultiplier", "HitChance",
        "ManaCost", "Life", "Mana", "EnergyShield",
        "Armour", "Evasion", "PhysicalReduction",
        "SpiritReservedPercent",
    ];

    private static string FormatStats(Dictionary<string, object?> all, string[]? filter)
    {
        var subset = filter is null
            ? all.OrderBy(kv => kv.Key)
            : filter
                .Select(k => new KeyValuePair<string, object?>(k, all.TryGetValue(k, out var v) ? v : null))
                .Where(kv => kv.Value is not null);

        return JsonSerializer.Serialize(
            subset.ToDictionary(kv => kv.Key, kv => kv.Value),
            new JsonSerializerOptions { WriteIndented = true });
    }

    private static void CollectBuilds(List<object> list, string dir, string prefix)
    {
        foreach (var sub in Directory.GetDirectories(dir).OrderBy(x => x))
        {
            CollectBuilds(list, sub, prefix + Path.GetFileName(sub) + "/");
        }
        foreach (var file in Directory.GetFiles(dir, "*.xml").OrderBy(x => x))
        {
            list.Add(new { name = prefix + Path.GetFileNameWithoutExtension(file), path = file });
        }
    }

    private static string GetBuildsPath()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(local, "PathOfBuilding", "Builds");
    }

    private static string Error(Exception ex) => $"ERROR: {ex.GetType().Name}: {ex.Message}";
}
