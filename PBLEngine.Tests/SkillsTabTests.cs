using PBLEngine;
using System;
using System.Linq;
using Xunit;

namespace PBLEngine.Tests;

/// <summary>
/// Tests for GetGemsInGroup and GetMainSkillGroupIndex.
/// </summary>
[Collection("LuaHost")]
public class SkillsTabTests : IClassFixture<LuaHostFixture>
{
    private readonly LuaHost _host;

    public SkillsTabTests(LuaHostFixture fixture)
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

    // ── GetGemsInGroup ─────────────────────────────────────────────────────

    [Fact]
    public void GetGemsInGroup_InvalidGroup_ReturnsEmptyList()
    {
        var gems = _host.GetGemsInGroup(9999);

        Assert.Empty(gems);
    }

    [Fact]
    public void GetGemsInGroup_GroupWithSingleGem_ReturnsOneEntry()
    {
        AddSkill("Lightning Warp 20/20  1");
        var groups = _host.GetSkillGroups();
        var grp = groups.First(g => g.Name.Contains("Lightning Warp"));

        var gems = _host.GetGemsInGroup(grp.Index);

        Assert.Single(gems);
    }

    [Fact]
    public void GetGemsInGroup_AllGemsHaveNonEmptyName()
    {
        AddSkill("Lightning Warp 20/20  1");
        var groups = _host.GetSkillGroups();
        var grp = groups.First(g => g.Name.Contains("Lightning Warp"));

        var gems = _host.GetGemsInGroup(grp.Index);

        Assert.All(gems, g => Assert.False(string.IsNullOrWhiteSpace(g.Name)));
    }

    [Fact]
    public void GetGemsInGroup_LightningWarpLevel20_LevelIs20()
    {
        AddSkill("Lightning Warp 20/20  1");
        var groups = _host.GetSkillGroups();
        var grp = groups.First(g => g.Name.Contains("Lightning Warp"));

        var gems = _host.GetGemsInGroup(grp.Index);
        var lw = gems.First(g => g.Name.Contains("Lightning Warp"));

        Assert.Equal(20, lw.Level);
    }

    [Fact]
    public void GetGemsInGroup_LightningWarpQuality20_QualityIs20()
    {
        AddSkill("Lightning Warp 20/20  1");
        var groups = _host.GetSkillGroups();
        var grp = groups.First(g => g.Name.Contains("Lightning Warp"));

        var gems = _host.GetGemsInGroup(grp.Index);
        var lw = gems.First(g => g.Name.Contains("Lightning Warp"));

        Assert.Equal(20, lw.Quality);
    }

    [Fact]
    public void GetGemsInGroup_ActiveGem_IsNotMarkedSupport()
    {
        AddSkill("Lightning Warp 20/20  1");
        var groups = _host.GetSkillGroups();
        var grp = groups.First(g => g.Name.Contains("Lightning Warp"));

        var gems = _host.GetGemsInGroup(grp.Index);
        var lw = gems.First(g => g.Name.Contains("Lightning Warp"));

        Assert.False(lw.IsSupport);
    }

    [Fact]
    public void GetGemsInGroup_EnabledGem_IsEnabledTrue()
    {
        AddSkill("Lightning Warp 20/20  1");
        var groups = _host.GetSkillGroups();
        var grp = groups.First(g => g.Name.Contains("Lightning Warp"));

        var gems = _host.GetGemsInGroup(grp.Index);

        Assert.All(gems, g => Assert.True(g.IsEnabled));
    }

    [Fact]
    public void GetGemsInGroup_SupportGem_IsMarkedSupport()
    {
        AddSkill("Lightning Warp 20/20  1\nFire Attunement 20/20  1");
        var groups = _host.GetSkillGroups();
        var grp = groups.First(g => g.Name.Contains("Lightning Warp"));

        var gems = _host.GetGemsInGroup(grp.Index);

        Assert.Contains(gems, g => g.IsSupport);
    }

    [Fact]
    public void GetGemsInGroup_SupportGem_ActiveGemStillNotSupport()
    {
        AddSkill("Lightning Warp 20/20  1\nFire Attunement 20/20  1");
        var groups = _host.GetSkillGroups();
        var grp = groups.First(g => g.Name.Contains("Lightning Warp"));

        var gems = _host.GetGemsInGroup(grp.Index);
        var lw = gems.First(g => g.Name.Contains("Lightning Warp"));

        Assert.False(lw.IsSupport);
    }

    [Fact]
    public void GetGemsInGroup_TwoGems_ReturnsTwoEntries()
    {
        AddSkill("Lightning Warp 20/20  1\nFire Attunement 20/20  1");
        var groups = _host.GetSkillGroups();
        var grp = groups.First(g => g.Name.Contains("Lightning Warp"));

        var gems = _host.GetGemsInGroup(grp.Index);

        Assert.Equal(2, gems.Count);
    }

    // ── GetMainSkillGroupIndex ─────────────────────────────────────────────

    [Fact]
    public void GetMainSkillGroupIndex_NewBuild_ReturnsNonNegative()
    {
        var idx = _host.GetMainSkillGroupIndex();

        Assert.True(idx >= 0);
    }

    [Fact]
    public void GetMainSkillGroupIndex_AfterSetActiveSkillGroup_ReturnsSetIndex()
    {
        AddSkill("Lightning Warp 20/20  1");
        var groups = _host.GetSkillGroups();
        var grp = groups.First(g => g.Name.Contains("Lightning Warp"));

        _host.SetActiveSkillGroup(grp.Index);
        var idx = _host.GetMainSkillGroupIndex();

        Assert.Equal(grp.Index, idx);
    }

    [Fact]
    public void GetMainSkillGroupIndex_AfterSwitchGroups_ReturnsLastSetIndex()
    {
        AddSkill("Lightning Warp 20/20  1");
        AddSkill("Spark 20/20  1");
        var groups = _host.GetSkillGroups();
        var grp1 = groups.First(g => g.Name.Contains("Lightning Warp"));
        var grp2 = groups.First(g => g.Name.Contains("Spark"));

        _host.SetActiveSkillGroup(grp1.Index);
        _host.SetActiveSkillGroup(grp2.Index);
        var idx = _host.GetMainSkillGroupIndex();

        Assert.Equal(grp2.Index, idx);
    }
}
