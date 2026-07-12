using PBLEngine;
using System;
using System.Linq;
using Xunit;

namespace PBLEngine.Tests;

/// <summary>
/// Tests for the Full DPS aggregation surface: the per-group includeInFullDPS
/// flag round-trip, the FullDPS stat, and the per-skill breakdown.
/// </summary>
[Collection("LuaHost")]
public class FullDpsTests : IClassFixture<LuaHostFixture>
{
    private readonly LuaHost _host;

    public FullDpsTests(LuaHostFixture fixture)
    {
        _host = fixture.Host;
        _host.NewBuild();
    }

    private void AddSkill(string gemLine)
    {
        _host.State["_testSkillText"] = gemLine.Replace("\n", "\r\n");
        _host.State.DoString(@"
            build.skillsTab:PasteSocketGroup(_testSkillText)
            runCallback('OnFrame')
            if build.calcsTab then build.calcsTab:BuildOutput() end
        ");
        _host.State["_testSkillText"] = null;
    }

    private double Stat(string key)
    {
        var stats = _host.GetAllStats();
        return stats.TryGetValue(key, out var v) ? Convert.ToDouble(v) : 0;
    }

    private int AddLightningWarpAsMain()
    {
        AddSkill("Lightning Warp 20/20  1");
        var grp = _host.GetSkillGroups().First(g => g.Name.Contains("Lightning Warp"));
        _host.SetActiveSkillGroup(grp.Index);
        return grp.Index;
    }

    [Fact]
    public void FullDps_NoGroupsMarked_IsZero()
    {
        AddLightningWarpAsMain();

        Assert.Equal(0, Stat("FullDPS"));
    }

    [Fact]
    public void SetGroupIncludeInFullDPS_MarksGroup_FullDpsBecomesPositive()
    {
        var idx = AddLightningWarpAsMain();

        _host.SetGroupIncludeInFullDPS(idx, true);

        Assert.True(Stat("FullDPS") > 0, $"Expected FullDPS > 0, got {Stat("FullDPS")}");
    }

    [Fact]
    public void GetSkillGroups_AfterMarkInclude_ReflectsFlag()
    {
        var idx = AddLightningWarpAsMain();

        _host.SetGroupIncludeInFullDPS(idx, true);
        var grp = _host.GetSkillGroups().First(g => g.Index == idx);

        Assert.True(grp.IncludeInFullDPS);
    }

    [Fact]
    public void SetGroupIncludeInFullDPS_Unmark_FullDpsReturnsToZero()
    {
        var idx = AddLightningWarpAsMain();
        _host.SetGroupIncludeInFullDPS(idx, true);

        _host.SetGroupIncludeInFullDPS(idx, false);

        Assert.Equal(0, Stat("FullDPS"));
    }

    [Fact]
    public void GetFullDpsBreakdown_AfterMark_ReturnsEntryWithPositiveDps()
    {
        var idx = AddLightningWarpAsMain();
        _host.SetGroupIncludeInFullDPS(idx, true);

        var breakdown = _host.GetFullDpsBreakdown();

        Assert.NotEmpty(breakdown);
        Assert.All(breakdown, e => Assert.False(string.IsNullOrWhiteSpace(e.Name)));
        Assert.Contains(breakdown, e => e.Dps > 0);
    }
}
