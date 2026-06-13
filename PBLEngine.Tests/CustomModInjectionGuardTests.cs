using PBLEngine;
using Xunit;

namespace PBLEngine.Tests;

/// <summary>
/// Duplication guards for ascendancy mechanics this fork implements by injecting parsed
/// modifiers through the custom-mod path (see <c>CUSTOM_MOD_INJECTIONS.md</c>).
///
/// Each guard asserts the *upstream non-implementation signal* for a feature. If upstream
/// PoB later implements one natively the signal changes and the matching test fails —
/// that is the cue to REMOVE our injection (not to bump the number), otherwise the effect
/// would be applied twice. Run after every <c>src/</c> sync via <c>dotnet test</c>.
/// </summary>
[Collection("LuaHost")]
public class CustomModInjectionGuardTests : IClassFixture<LuaHostFixture>
{
    private readonly LuaHost _host;

    public CustomModInjectionGuardTests(LuaHostFixture fixture)
    {
        _host = fixture.Host;
        _host.NewBuild();
    }

    /// <summary>Returns (found, modCount, sd) for the latest tree's node with this display name.</summary>
    private (bool Found, int ModCount, string Sd) NodeInfo(string nodeName)
    {
        _host.State["_guardNode"] = nodeName;
        var result = _host.State.DoString(@"
            local tree = build and build.spec and build.spec.tree
            if not tree then return 'missing||' end
            for nid, n in pairs(tree.nodes) do
                if n.dn == _guardNode then
                    local c = 0
                    if n.modList then for _ in ipairs(n.modList) do c = c + 1 end end
                    return 'found|' .. c .. '|' .. (n.sd and table.concat(n.sd, ' / ') or '')
                end
            end
            return 'notfound||'
        ");
        _host.State["_guardNode"] = null;

        var raw = result is { Length: > 0 } ? result[0] as string ?? "" : "";
        var parts = raw.Split('|', 3);
        var found = parts.Length > 0 && parts[0] == "found";
        var count = parts.Length > 1 && int.TryParse(parts[1], out var c) ? c : -1;
        var sd    = parts.Length > 2 ? parts[2] : "";
        return (found, count, sd);
    }

    // ── 1. Runic Meridians (body-tattoo Rune sockets) ──────────────────────
    // We inject the socketed runes' mods (ConfigTab:ApplyTattooMods). The node itself is
    // purely descriptive — it parses NO mods. If that changes, upstream implemented it.

    [Fact]
    public void Guard_RunicMeridians_NodeStillExists()
    {
        var info = NodeInfo("Runic Meridians");
        Assert.True(info.Found,
            "Runic Meridians node not found in the latest tree. The tattoo injection " +
            "(ConfigTab:ApplyTattooMods) keys off this node — review after this tree change.");
    }

    [Fact]
    public void Guard_RunicMeridians_StillParsesNoMods()
    {
        var info = NodeInfo("Runic Meridians");
        if (!info.Found) return; // covered by the existence test
        Assert.Equal(0, info.ModCount);
        // ^ If this fails, upstream gave Runic Meridians real mods (native implementation).
        //   Remove ConfigTab:ApplyTattooMods + the Tattoos UI to avoid double-counting.
    }

    // ── 2. Crystalline Phylactery (socketed-jewel doubling) ────────────────
    // Upstream parses only the mana-cost penalty (1 mod); the jewel socket and the
    // "100% increased Effect of bonuses gained from Socketed Jewel" line are NOT
    // implemented. We reproduce them via injection.

    [Fact]
    public void Guard_Phylactery_NodeStillExists()
    {
        var info = NodeInfo("Crystalline Phylactery");
        Assert.True(info.Found,
            "Crystalline Phylactery node not found in the latest tree — review the Phylactery injection.");
    }

    [Fact]
    public void Guard_Phylactery_StillParsesOnlyManaPenalty()
    {
        var info = NodeInfo("Crystalline Phylactery");
        if (!info.Found) return;
        Assert.Equal(1, info.ModCount);
        // ^ If this fails, upstream parsed more than the lone mana-cost penalty — likely the
        //   jewel socket / "increased Effect" lines. Remove our Phylactery injection.
    }
}
