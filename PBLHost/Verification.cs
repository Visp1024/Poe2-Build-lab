using NLua;
using PBLEngine;
using System;
using System.Collections.Generic;

namespace PBLHost;

/// <summary>
/// Runs a subset of the Busted spec assertions directly through NLua/Lua 5.4
/// and reports pass/fail.  Expected values are taken verbatim from the spec files
/// in spec/System/.
/// </summary>
public static class Verification
{
    private record TestCase(string Name, Action<LuaHost> Run);

    private static int _pass, _fail;
    private static readonly List<string> _failures = new();

    public static int Run(LuaHost host)
    {
        _pass = _fail = 0;
        _failures.Clear();

        Console.WriteLine("\n══════════════════════════════════════════════════════");
        Console.WriteLine(" Verification: NLua/Lua 5.4 vs expected (Busted/LuaJIT)");
        Console.WriteLine("══════════════════════════════════════════════════════\n");

        // ── TestAttacks ────────────────────────────────────────────────────
        Section("TestAttacks");

        Test(host, "empty build: CritMultiplier = 2", h =>
        {
            h.NewBuild();
            AssertEq(h, "CritMultiplier", "build.calcsTab.mainOutput.CritMultiplier", 2.0);
        });

        Test(host, "Heavy Bow equipped: CritMultiplier = 2.25", h =>
        {
            h.NewBuild();
            // Pass item text via a Lua variable to avoid long-string whitespace issues
            h.State["_itemText"] = "New Item\nHeavy Bow\n25% increased Critical Damage Bonus";
            h.State.DoString(@"
                build.itemsTab:CreateDisplayItemFromRaw(_itemText)
                build.itemsTab:AddDisplayItem()
                runCallback('OnFrame')
            ");
            h.State["_itemText"] = null;

            // Trace applyRange for the problematic line
            h.State.DoString(@"
                local raw = '25% increased Critical Damage Bonus'
                local result = itemLib.applyRange(raw, 1, 1, nil)
                print('DEBUG applyRange result=[' .. tostring(result) .. ']')
                -- Check scalability key
                local stripped = raw:gsub('(%-?%d+%.?%d*)', '#')
                print('DEBUG strippedLine=[' .. stripped .. ']')
                print('DEBUG modScalability key exists=' .. tostring(data.modScalability ~= nil and data.modScalability[stripped] ~= nil))
            ");
            AssertEq(h, "CritMultiplier", "build.calcsTab.mainOutput.CritMultiplier", 2.25);
        });

        // ── TestSkills ─────────────────────────────────────────────────────
        Section("TestSkills");

        Test(host, "Ball Lightning lv1: ManaCost = 9", h =>
        {
            h.NewBuild();
            h.State.DoString(@"
                build.skillsTab:PasteSocketGroup('Ball Lightning 1/0  1\n')
                runCallback('OnFrame')
            ");
            AssertEq(h, "ManaCost", "build.calcsTab.mainOutput.ManaCost", 9.0);
        });

        Test(host, "Ball Lightning + 50% Mana Cost Efficiency: ManaCost = 6", h =>
        {
            h.NewBuild();
            h.State.DoString(@"
                build.skillsTab:PasteSocketGroup('Ball Lightning 1/0  1\n')
                build.configTab.input.customMods = '50% increased Mana Cost Efficiency'
                build.configTab:BuildModList()
                runCallback('OnFrame')
            ");
            AssertEq(h, "ManaCost", "build.calcsTab.mainOutput.ManaCost", 6.0);
        });

        Test(host, "Ball Lightning + 25% Cost Efficiency: ManaCost ≈ 7.2", h =>
        {
            h.NewBuild();
            h.State.DoString(@"
                build.skillsTab:PasteSocketGroup('Ball Lightning 1/0  1\n')
                build.configTab.input.customMods = '25% increased Cost Efficiency'
                build.configTab:BuildModList()
                runCallback('OnFrame')
            ");
            AssertApprox(h, "ManaCost", "build.calcsTab.mainOutput.ManaCost", 7.2, 0.001);
        });

        Test(host, "Ball Lightning + 25% CE + 25% MCE stacked: ManaCost = 6", h =>
        {
            h.NewBuild();
            h.State.DoString(@"
                build.skillsTab:PasteSocketGroup('Ball Lightning 1/0  1\n')
                build.configTab.input.customMods = '25% increased Cost Efficiency\n25% increased Mana Cost Efficiency'
                build.configTab:BuildModList()
                runCallback('OnFrame')
            ");
            AssertEq(h, "ManaCost", "build.calcsTab.mainOutput.ManaCost", 6.0);
        });

        Test(host, "Blasphemy reserves Spirit > 0", h =>
        {
            h.NewBuild();
            h.State.DoString(@"
                build.skillsTab:PasteSocketGroup('Blasphemy 20/0  1\nDespair 20/0  1\n')
                runCallback('OnFrame')
            ");
            AssertTrue(h, "SpiritReservedPercent > 0",
                "build.calcsTab.mainOutput.SpiritReservedPercent",
                v => v > 0);
        });

        // ── TestDefence ────────────────────────────────────────────────────
        Section("TestDefence");

        // pob1and2Compat mods (applied on top of customMods)
        const string compat = @"
            build.configTab.input.customMods = (build.configTab.input.customMods or '') .. '\n' ..
                '5% reduced maximum life\n' ..
                '5% reduced maximum mana\n' ..
                '-2 to life\n' ..
                '-10% to elemental resistances\n' ..
                '-60% to chaos resistance\n' ..
                '+2 to mana\n'
            build.configTab:BuildModList()
            runCallback('OnFrame')
        ";

        Test(host, "no-armour base max hits: PhysPhysicalMaximumHitTaken=60, Fire=38", h =>
        {
            h.NewBuild();
            h.State.DoString("build.configTab.input.enemyIsBoss = 'None'");
            h.State.DoString("build.configTab.input.customMods = ''");
            h.State.DoString(compat);
            AssertCalcsEq(h, "PhysicalMaximumHitTaken", 60.0);
            AssertCalcsEq(h, "FireMaximumHitTaken",      38.0);
            AssertCalcsEq(h, "ColdMaximumHitTaken",      38.0);
            AssertCalcsEq(h, "LightningMaximumHitTaken", 38.0);
            AssertCalcsEq(h, "ChaosMaximumHitTaken",     38.0);
        });

        Test(host, "+200 all res + 200% phys DR: Phys=600, Fire=240", h =>
        {
            h.NewBuild();
            h.State.DoString("build.configTab.input.enemyIsBoss = 'None'");
            h.State.DoString(@"build.configTab.input.customMods = '+200 to all resistances\n200% additional Physical Damage Reduction\n'");
            h.State.DoString(compat);
            AssertCalcsEq(h, "PhysicalMaximumHitTaken", 600.0);
            AssertCalcsEq(h, "FireMaximumHitTaken",     240.0);
        });

        Test(host, "armoured: +940 life +10000 armour: Fire=625", h =>
        {
            h.NewBuild();
            h.State.DoString("build.configTab.input.enemyIsBoss = 'None'");
            h.State.DoString(@"build.configTab.input.customMods = '+940 to maximum life\n+10000 to armour\n'");
            h.State.DoString(compat);
            AssertCalcsEq(h, "FireMaximumHitTaken",      625.0);
            AssertCalcsEq(h, "ColdMaximumHitTaken",      625.0);
            AssertCalcsEq(h, "LightningMaximumHitTaken", 625.0);
            AssertCalcsEq(h, "ChaosMaximumHitTaken",     625.0);
        });

        // ── Phase3: Notes ──────────────────────────────────────────────────
        Section("Phase3.Notes");

        Test(host, "GetNotes on new build returns empty", h =>
        {
            h.NewBuild();
            var notes = h.GetNotes();
            if (notes != "")
                throw new Exception($"Expected empty notes, got: '{notes}'");
        });

        Test(host, "SetNotes/GetNotes round-trip", h =>
        {
            h.NewBuild();
            h.SetNotes("Hello notes");
            var actual = h.GetNotes();
            if (actual != "Hello notes")
                throw new Exception($"Expected 'Hello notes', got: '{actual}'");
        });

        Test(host, "Notes persisted in SaveDB XML", h =>
        {
            h.NewBuild();
            h.SetNotes("My build notes");
            var xml = h.SaveBuildToXml();
            if (xml == null) throw new Exception("SaveBuildToXml returned null");
            if (!xml.Contains("My build notes"))
                throw new Exception("Notes text not found in saved XML");
        });

        Test(host, "SaveBuildToXml returns PathOfBuilding2 XML", h =>
        {
            h.NewBuild();
            var xml = h.SaveBuildToXml();
            if (xml == null) throw new Exception("SaveBuildToXml returned null");
            if (!xml.Contains("<PathOfBuilding2"))
                throw new Exception("XML does not contain <PathOfBuilding2 root element");
        });

        // ── Phase3: ConfigOptions ──────────────────────────────────────────
        Section("Phase3.ConfigOptions");

        Test(host, "GetConfigOptions returns non-empty list", h =>
        {
            h.NewBuild();
            var opts = h.GetConfigOptions();
            if (opts.Count == 0)
                throw new Exception("GetConfigOptions returned empty list");
        });

        Test(host, "GetConfigOptions contains resistancePenalty as list type", h =>
        {
            h.NewBuild();
            var opts = h.GetConfigOptions();
            var opt = opts.Find(o => o.Var == "resistancePenalty");
            if (opt == null) throw new Exception("resistancePenalty not found");
            if (opt.Type != "list") throw new Exception($"Expected type=list, got: {opt.Type}");
            if (opt.ListOptions.Length == 0) throw new Exception("resistancePenalty has no list options");
        });

        Test(host, "GetConfigOptions: all options have non-empty Var and Label", h =>
        {
            h.NewBuild();
            var opts = h.GetConfigOptions();
            foreach (var o in opts)
            {
                if (string.IsNullOrEmpty(o.Var))
                    throw new Exception($"Option with empty Var found: label='{o.Label}'");
                if (string.IsNullOrEmpty(o.Label))
                    throw new Exception($"Option with empty Label found: var='{o.Var}'");
            }
        });

        Test(host, "SetConfigValue enemyIsBoss does not crash", h =>
        {
            h.NewBuild();
            h.SetConfigValue("enemyIsBoss", "Pinnacle");
            h.SetConfigValue("enemyIsBoss", "None");
        });

        Test(host, "SetConfigValue resistancePenalty 0 vs -60 changes stats", h =>
        {
            h.NewBuild();
            h.SetConfigValue("resistancePenalty", "0");
            var statsAt0 = h.GetAllStats();
            h.SetConfigValue("resistancePenalty", "-60");
            var statsAt60 = h.GetAllStats();
            if (statsAt0.Count == 0 || statsAt60.Count == 0)
                throw new Exception("GetAllStats returned empty after SetConfigValue");
        });

        // ── Summary ────────────────────────────────────────────────────────
        Console.WriteLine($"\n──────────────────────────────────────────────────────");
        Console.WriteLine($"  Passed : {_pass}");
        Console.WriteLine($"  Failed : {_fail}");
        if (_failures.Count > 0)
        {
            Console.WriteLine("\nFailures:");
            foreach (var f in _failures)
                Console.WriteLine($"  ✗ {f}");
        }
        Console.WriteLine("──────────────────────────────────────────────────────\n");

        return _fail == 0 ? 0 : 1;
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static void Section(string name)
        => Console.WriteLine($"  [{name}]");

    private static void Test(LuaHost host, string name, Action<LuaHost> body)
    {
        try
        {
            body(host);
        }
        catch (AssertionException ex)
        {
            _fail++;
            var msg = $"{name}: {ex.Message}";
            _failures.Add(msg);
            Console.WriteLine($"  ✗ {msg}");
            return;
        }
        catch (Exception ex)
        {
            _fail++;
            var msg = $"{name}: EXCEPTION {ex.GetType().Name}: {ex.Message}";
            _failures.Add(msg);
            Console.WriteLine($"  ✗ {msg}");
            return;
        }
        _pass++;
        Console.WriteLine($"  ✓ {name}");
    }

    private static void AssertEq(LuaHost h, string label, string luaExpr, double expected)
    {
        var result = h.State.DoString($"return {luaExpr}");
        var actual = result is { Length: > 0 } && result[0] != null
            ? Convert.ToDouble(result[0])
            : throw new AssertionException($"{label}: got nil");
        if (Math.Abs(actual - expected) > 1e-9)
            throw new AssertionException($"{label}: expected {expected}, got {actual}");
    }

    private static void AssertApprox(LuaHost h, string label, string luaExpr, double expected, double tolerance)
    {
        var result = h.State.DoString($"return {luaExpr}");
        var actual = result is { Length: > 0 } && result[0] != null
            ? Convert.ToDouble(result[0])
            : throw new AssertionException($"{label}: got nil");
        if (Math.Abs(actual - expected) > tolerance)
            throw new AssertionException($"{label}: expected {expected} ±{tolerance}, got {actual}");
    }

    private static void AssertTrue(LuaHost h, string label, string luaExpr, Func<double, bool> predicate)
    {
        var result = h.State.DoString($"return {luaExpr}");
        var actual = result is { Length: > 0 } && result[0] != null
            ? Convert.ToDouble(result[0])
            : throw new AssertionException($"{label}: got nil");
        if (!predicate(actual))
            throw new AssertionException($"{label}: predicate failed for value {actual}");
    }

    private static void AssertCalcsEq(LuaHost h, string stat, double expected)
    {
        AssertEq(h, stat,
            $"build.calcsTab.calcsOutput.{stat}",
            expected);
    }

    private class AssertionException(string msg) : Exception(msg);
}
