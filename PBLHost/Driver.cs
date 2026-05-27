using PBLEngine;
using System;
using System.IO;
using System.Linq;

namespace PBLHost;

/// <summary>
/// Quick console driver: load a build, select a skill, dump stats.
/// Usage: dotnet run --project PBLHost -- driver [buildName] [skillName]
/// </summary>
public static class Driver
{
    public static int Run(string[] args)
    {
        var buildName = args.ElementAtOrDefault(0) ?? "New Build 2";
        var skillMatch = args.ElementAtOrDefault(1) ?? "Lightning Warp";

        var buildsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PathOfBuilding2", "Builds");

        var xmlFile = Directory.GetFiles(buildsDir, "*.xml")
            .FirstOrDefault(f => Path.GetFileNameWithoutExtension(f)
                .Equals(buildName, StringComparison.OrdinalIgnoreCase)
                || Path.GetFileNameWithoutExtension(f)
                .Contains(buildName, StringComparison.OrdinalIgnoreCase));

        if (xmlFile is null)
        {
            Console.Error.WriteLine($"Build '{buildName}' not found in {buildsDir}");
            Console.Error.WriteLine("Available: " + string.Join(", ",
                Directory.GetFiles(buildsDir, "*.xml").Select(Path.GetFileNameWithoutExtension)));
            return 1;
        }

        Console.Error.WriteLine($"Build file : {xmlFile}");

        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../.."));
        Console.Error.WriteLine($"Repo root  : {repoRoot}");
        Console.Error.WriteLine("Initializing LuaHost…");

        using var host = new LuaHost();
        host.Initialize(repoRoot);
        Console.Error.WriteLine("Engine ready.");

        var xml = File.ReadAllText(xmlFile);
        var model = new BuildModel(host);
        model.LoadBuildFromXml(xml, buildName);
        Console.Error.WriteLine("Build loaded.");

        // List skill groups
        var groups = host.GetSkillGroups().ToList();
        Console.WriteLine($"\nSkill groups ({groups.Count}):");
        foreach (var g in groups)
            Console.WriteLine($"  [{g.Index}] {g.Name}");

        // Select skill
        var target = groups.FirstOrDefault(g =>
            g.Name.Equals(skillMatch, StringComparison.OrdinalIgnoreCase) ||
            g.Name.Contains(skillMatch, StringComparison.OrdinalIgnoreCase));

        if (target is null)
        {
            Console.Error.WriteLine($"\nSkill '{skillMatch}' not found. Using first group.");
            target = groups.FirstOrDefault();
        }

        if (target is not null)
        {
            Console.WriteLine($"\nSelecting: [{target.Index}] {target.Name}");
            host.SetActiveSkillGroup(target.Index);
            model.Refresh();
        }

        // Dump stats
        var stats = model.AllStats;
        Console.WriteLine($"\n=== Stats after skill selection ({stats.Count} total) ===");

        var keys = new[]
        {
            "TotalDPS", "CombinedDPS", "AverageDamage", "AverageHit",
            "Speed", "Time", "HitChance",
            "CritChance", "CritMultiplier", "CritEffect",
            "ManaCost",
            "LightningMin", "LightningMax",
            "ColdMin",      "ColdMax",
            "FireMin",      "FireMax",
            "PhysicalMin",  "PhysicalMax",
            "ChaosMin",     "ChaosMax",
            "IgniteChance", "IgniteDPS", "IgniteDuration",
            "ShockChance",  "ShockEffectMod", "ShockDuration",
            "Life", "Mana", "EnergyShield",
            "GemLevel",
        };

        foreach (var k in keys)
        {
            stats.TryGetValue(k, out var v);
            Console.WriteLine($"  {k,-32} = {v ?? "(null)"}");
        }

        // Full dump to file
        var dumpPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            "pob_driver_dump.txt");
        host.DumpStatsToFile(dumpPath);
        Console.Error.WriteLine($"\nFull dump → {dumpPath}");

        return 0;
    }
}
