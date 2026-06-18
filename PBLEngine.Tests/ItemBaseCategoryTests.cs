using PBLEngine;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace PBLEngine.Tests;

/// <summary>
/// Guards the GGPK data-export output: every item-base category file under
/// src/Data/Bases must define at least one base.
///
/// Regression: the data-export pipeline (commit 08736ea83) once wrote
/// header-only bow.lua / crossbow.lua, so data.itemBases had no Bow/Crossbow
/// entries and bows could not be created or equipped. Run after any GGPK
/// re-export — the export does NOT currently emit Bow/Crossbow and will
/// re-empty those files.
/// </summary>
[Collection("LuaHost")]
public class ItemBaseCategoryTests : IClassFixture<LuaHostFixture>
{
    private readonly LuaHost _host;

    /// <summary>
    /// PoE1 weapon types that legitimately have no bases in PoE2 0.5 — their
    /// generated files are intentionally header-only. Keep in sync with the
    /// itemTypes list in src/Export/Scripts/bases.lua.
    /// </summary>
    private static readonly HashSet<string> LegacyEmpty = new(StringComparer.OrdinalIgnoreCase)
    {
        "axe", "claw", "dagger", "fishing",
    };

    public ItemBaseCategoryTests(LuaHostFixture fixture) => _host = fixture.Host;

    public static IEnumerable<object[]> BaseFiles()
    {
        var basesDir = Path.Combine(RepoRoot(), "src", "Data", "Bases");
        return Directory.EnumerateFiles(basesDir, "*.lua")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(name => name is not null && !LegacyEmpty.Contains(name))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .Select(name => new object[] { name! });
    }

    [Theory]
    [MemberData(nameof(BaseFiles))]
    public void BaseCategory_DefinesAtLeastOneBase(string fileName)
    {
        var path = Path.Combine(RepoRoot(), "src", "Data", "Bases", fileName + ".lua")
            .Replace('\\', '/');

        // Load the generated base file (header: `local itemBases = ...`) into a
        // fresh table and count the entries it defines.
        _host.State["_baseFilePath"] = path;
        var result = _host.State.DoString(@"
            local t = {}
            local chunk, err = loadfile(_baseFilePath)
            if not chunk then return -1, err end
            local ok, e2 = pcall(chunk, t)
            if not ok then return -2, e2 end
            local n = 0
            for _ in pairs(t) do n = n + 1 end
            return n
        ");
        _host.State["_baseFilePath"] = null;

        var count = result is { Length: > 0 } && result[0] is long n ? (int)n : -99;
        var detail = result is { Length: > 1 } ? $" ({result[1]})" : "";

        Assert.True(count > 0,
            $"Base category '{fileName}.lua' defines {count} item bases{detail} — " +
            "the GGPK export likely produced an empty/broken file. " +
            "If this category truly has no bases in the current game version, add it to LegacyEmpty.");
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "src", "Data", "Bases")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Cannot find repo root (no src/Data/Bases ancestor)");
    }
}
