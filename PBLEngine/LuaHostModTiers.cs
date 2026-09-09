using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PBLEngine;

/// <summary>Грейд одной строки мода: тип аффикса, тир и шаблон стата из пула базы.</summary>
public record ModTierInfo(
    string AffixType,   // "Prefix" / "Suffix"
    string AffixName,   // "of the Bear"
    string StatText,    // "+(13-16) to Strength" — шаблон с диапазоном
    int    Tier,        // 1 = лучший тир серии
    int    TierCount,   // всего тиров в серии для этой базы
    string Group);

// Грейд (тир) модификаторов — загрузка lua/modtiers.lua по требованию.
// Модуль считает тир по пулу модов базы предмета; T1 = лучший (как в игре).
public sealed partial class LuaHost
{
    private bool _modTiersLoaded;

    /// <summary>Идемпотентно подгружает PBLModTiers в Lua-состояние.</summary>
    public void EnsureModTiers()
    {
        if (_modTiersLoaded) return;
        State.DoString(File.ReadAllText(Path.Combine(EngineLuaDir, "modtiers.lua")), "@modtiers.lua");
        _modTiersLoaded = true;
    }

    /// <summary>
    /// Сопоставляет строки модов с пулом базы и возвращает их грейд — по одному
    /// элементу на входную строку; null там, где сопоставить не удалось
    /// (крафт, руны, моды не из пула базы — лучше без бейджа, чем с выдуманным).
    /// </summary>
    public List<ModTierInfo?> ResolveModTiers(string baseName, IReadOnlyList<string> modLines)
    {
        var result = new List<ModTierInfo?>();
        if (modLines.Count == 0) return result;

        EnsureModTiers();
        State["_mtBase"]  = baseName;
        State["_mtLines"] = string.Join("\x1F", modLines.Select(l => l.Replace('\t', ' ')));
        var raw = State.DoString(@"
            if not PBLModTiers then return '' end
            local out = {}
            for line in (_mtLines .. '\x1F'):gmatch('([^\x1F]*)\x1F') do
                local ok, info = pcall(PBLModTiers.ResolveInfo, _mtBase, line)
                if ok and info then
                    out[#out + 1] = table.concat({
                        info.type, info.affix, info.statText,
                        tostring(info.tier), tostring(info.count), info.group
                    }, '\t')
                else
                    out[#out + 1] = ''
                end
            end
            return table.concat(out, '\x1F')
        ");
        State["_mtBase"]  = null;
        State["_mtLines"] = null;

        var serialized = raw is { Length: > 0 } && raw[0] is string str ? str : "";
        foreach (var row in serialized.Split('\x1F'))
        {
            if (string.IsNullOrEmpty(row)) { result.Add(null); continue; }
            var p = row.Split('\t', 6);
            if (p.Length < 6) { result.Add(null); continue; }
            int.TryParse(p[3], out var tier);
            int.TryParse(p[4], out var count);
            result.Add(new ModTierInfo(p[0], p[1], p[2], tier, count, p[5]));
        }
        // Защита от рассинхрона длины (не должно случаться, но UI не должен падать).
        while (result.Count < modLines.Count) result.Add(null);
        return result;
    }
}
