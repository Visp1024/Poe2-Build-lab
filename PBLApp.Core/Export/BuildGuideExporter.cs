using PBLEngine;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PBLApp.Core.Export;

/// <summary>Итог экспорта: путь к записанному файлу либо причина отказа.</summary>
public sealed record BuildGuideResult(
    bool Ok,
    string? FilePath = null,
    string? Error = null,
    int PassiveCount = 0,
    int SkippedPassives = 0,
    int SkillCount = 0,
    int ItemCount = 0);

/// <summary>
/// Экспорт билда в файл <c>*.build</c> внутриигрового планировщика PoE 2 (0.5+):
/// один JSON-объект <c>Build</c> — восхождение, пассивки, гемы с саппортами и подсказки
/// по слотам снаряжения. Схема: pathofexile.com/developer/docs/game.
///
/// Игра читает такие файлы из <see cref="DefaultFolder"/> и рисует по ним подсветку
/// в дереве, окне гемов и инвентаре.
/// </summary>
public static class BuildGuideExporter
{
    /// <summary>Куда игра смотрит за файлами гайдов.</summary>
    public static string DefaultFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "My Games", "Path of Exile 2", "BuildPlanner");

    /// <summary>Слот PoB → идентификатор из GGPK-таблицы <c>Inventories</c>, который
    /// ждёт поле <c>inventory_id</c>. Слоты, которых в планировщике нет (самоцветы,
    /// связка оружия II, фляги-заглушки), сюда не попадают — и не экспортируются.</summary>
    private static readonly Dictionary<string, string> SlotToInventoryId = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Weapon 1"]      = "Weapon1",
        ["Weapon 2"]      = "Offhand1",
        ["Weapon 1 Swap"] = "Weapon2",
        ["Weapon 2 Swap"] = "Offhand2",
        ["Helmet"]        = "Helm1",
        ["Body Armour"]   = "BodyArmour1",
        ["Gloves"]        = "Gloves1",
        ["Boots"]         = "Boots1",
        ["Amulet"]        = "Amulet1",
        ["Ring 1"]        = "Ring1",
        ["Ring 2"]        = "Ring2",
        ["Belt"]          = "Belt1",
    };

    private static Dictionary<string, string>? _passiveIds;

    /// <summary>Маппинг «id ноды дерева → PassiveSkills.Id» из GGPK. Генерируется
    /// скриптом <c>PBLExport/gen_build_planner_ids.py</c>, лежит ресурсом в сборке.</summary>
    private static Dictionary<string, string> PassiveIds =>
        _passiveIds ??= LoadPassiveIds();

    private static Dictionary<string, string> LoadPassiveIds()
    {
        try
        {
            var asm = typeof(BuildGuideExporter).Assembly;
            var name = asm.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("passive_planner_ids.json", StringComparison.Ordinal));
            if (name is null) return [];
            using var stream = asm.GetManifestResourceStream(name);
            if (stream is null) return [];
            return JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? [];
        }
        catch
        {
            return [];
        }
    }

    /// <summary>Собирает объект <c>Build</c> по текущему состоянию движка.</summary>
    /// <param name="buildName">Имя билда — единственное обязательное поле схемы.</param>
    public static JsonObject BuildGuide(LuaHost host, string buildName, out BuildGuideStats stats)
    {
        var guide = new JsonObject
        {
            ["name"] = string.IsNullOrWhiteSpace(buildName) ? "PathBuildLab build" : buildName.Trim(),
        };

        var ascendancy = host.GetAscendancyInternalId();
        if (!string.IsNullOrEmpty(ascendancy)) guide["ascendancy"] = ascendancy;

        var (passives, skipped) = CollectPassives(host);
        if (passives.Count > 0) guide["passives"] = ToArray(passives);

        var skills = CollectSkills(host);
        if (skills.Count > 0) guide["skills"] = ToArray(skills);

        var slots = CollectInventorySlots(host);
        if (slots.Count > 0) guide["inventory_slots"] = ToArray(slots);

        stats = new BuildGuideStats(passives.Count, skipped, skills.Count, slots.Count);
        return guide;

        static JsonArray ToArray(List<JsonObject> items)
        {
            var arr = new JsonArray();
            foreach (var item in items) arr.Add(item);
            return arr;
        }
    }

    /// <summary>Сколько чего попало в файл — для строки статуса в окне экспорта.</summary>
    public readonly record struct BuildGuideStats(int Passives, int SkippedPassives, int Skills, int Items);

    private static (List<JsonObject> Passives, int Skipped) CollectPassives(LuaHost host)
    {
        var result = new List<JsonObject>();
        var skipped = 0;
        var ids = PassiveIds;
        foreach (var nodeId in host.GetAllocatedNodeIds().OrderBy(id => id))
        {
            if (ids.TryGetValue(nodeId.ToString(), out var passiveId))
                result.Add(new JsonObject { ["id"] = passiveId });
            else
                skipped++;   // нода из дерева новее выгруженной таблицы PassiveSkills
        }
        return (result, skipped);
    }

    /// <summary>Каждая группа умений становится одним <c>skill</c>: первый активный гем —
    /// сам навык, остальные гемы группы (саппорты и мета-вложения) — его
    /// <c>support_skills</c>. Выключенные группы и гемы в гайд не идут.</summary>
    private static List<JsonObject> CollectSkills(LuaHost host)
    {
        var result = new List<JsonObject>();
        foreach (var group in host.GetGuideSkillGroups())
        {
            if (!group.IsEnabled) continue;

            var gems = group.Gems.Where(g => g.IsEnabled).ToList();
            var active = gems.FirstOrDefault(g => !g.IsSupport);
            if (active is null) continue;

            var skill = new JsonObject { ["id"] = active.GameId };

            var supports = new JsonArray();
            foreach (var gem in gems.Where(g => !ReferenceEquals(g, active)))
                supports.Add(new JsonObject { ["id"] = gem.GameId });
            if (supports.Count > 0) skill["support_skills"] = supports;

            result.Add(skill);
        }
        return result;
    }

    /// <summary>Подсказки по слотам: уникальные предметы называются по имени
    /// (<c>unique_name</c> — игра подсветит именно его), остальные уходят строкой
    /// подсказки «база + моды», как это делают внешние генераторы гайдов.</summary>
    private static List<JsonObject> CollectInventorySlots(LuaHost host)
    {
        var result = new List<JsonObject>();
        foreach (var (slot, item) in host.GetEquippedItems())
        {
            if (!SlotToInventoryId.TryGetValue(slot, out var inventoryId)) continue;

            var entry = new JsonObject { ["inventory_id"] = inventoryId };
            if (string.Equals(item.Rarity, "UNIQUE", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(item.Name))
            {
                entry["unique_name"] = UniqueTitle(item);
            }
            else
            {
                var text = DescribeItem(item);
                if (text.Length == 0) continue;
                entry["additional_text"] = text;
            }
            result.Add(entry);
        }
        // Порядок слотов в файле — как в примере GGG: оружие, броня, украшения.
        return result
            .OrderBy(e => Array.IndexOf(SlotOrder, e["inventory_id"]!.GetValue<string>()))
            .ToList();
    }

    private static readonly string[] SlotOrder =
        ["Weapon1", "Offhand1", "Weapon2", "Offhand2", "Helm1", "BodyArmour1",
         "Gloves1", "Boots1", "Amulet1", "Ring1", "Ring2", "Belt1"];

    /// <summary>Имя уника без базы: PoB держит его как «Mageblood, Utility Belt»,
    /// а игра ищет запись таблицы Words — «Mageblood».</summary>
    private static string UniqueTitle(ItemEntry item)
    {
        var name = item.Name.Trim();
        var baseName = item.BaseName.Trim();
        if (baseName.Length > 0 && name.EndsWith(", " + baseName, StringComparison.OrdinalIgnoreCase))
            return name[..^(baseName.Length + 2)].Trim();
        return name;
    }

    private static string DescribeItem(ItemEntry item)
    {
        var lines = new List<string>();
        var head = item.BaseName is { Length: > 0 } ? item.BaseName : item.Name;
        if (!string.IsNullOrWhiteSpace(head)) lines.Add(head);
        var mods = item.Implicits.Concat(item.Explicits)
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .ToList();
        for (int i = 0; i < mods.Count; i++) lines.Add($"{i + 1}. {mods[i]}");
        return string.Join("\n", lines);
    }

    /// <summary>Пишет гайд в <paramref name="folder"/> (по умолчанию — папка игры).
    /// Имя файла — имя билда, недопустимые символы заменяются.</summary>
    public static BuildGuideResult Export(LuaHost host, string buildName, string? folder = null)
    {
        try
        {
            var guide = BuildGuide(host, buildName, out var stats);
            var dir = string.IsNullOrWhiteSpace(folder) ? DefaultFolder : folder!;
            Directory.CreateDirectory(dir);

            var file = Path.Combine(dir, SanitizeFileName(buildName) + ".build");
            var json = guide.ToJsonString(new JsonSerializerOptions
            {
                WriteIndented = true,
                // Русские буквы в именах билдов и подсказках должны остаться читаемыми.
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            });
            File.WriteAllText(file, json);

            return new BuildGuideResult(true, file, null,
                stats.Passives, stats.SkippedPassives, stats.Skills, stats.Items);
        }
        catch (Exception ex)
        {
            return new BuildGuideResult(false, Error: ex.Message);
        }
    }

    private static string SanitizeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name.Trim() is { Length: > 0 } trimmed ? trimmed : "PathBuildLab build";
    }
}
