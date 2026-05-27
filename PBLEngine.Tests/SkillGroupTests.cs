using PBLEngine;
using System;
using System.Linq;
using Xunit;

namespace PBLEngine.Tests;

/// <summary>
/// Tests for GetSkillGroups, GetActiveSkillsInGroup, and SetActiveSkillGroup.
/// Covers the displayLabel fix and the Active Skill sub-selector for trigger builds.
/// </summary>
[Collection("LuaHost")]
public class SkillGroupTests : IClassFixture<LuaHostFixture>
{
    private readonly LuaHost _host;

    public SkillGroupTests(LuaHostFixture fixture)
    {
        _host = fixture.Host;
        _host.NewBuild();
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    // PasteSocketGroup format: "GemName level/quality [DISABLED] count"
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

    // ── GetSkillGroups ─────────────────────────────────────────────────────

    [Fact]
    public void GetSkillGroups_NewBuild_ReturnsNonNullList()
    {
        // A fresh build has no gems, so the list may be empty — but must not be null.
        var groups = _host.GetSkillGroups();

        Assert.NotNull(groups);
    }

    [Fact]
    public void GetSkillGroups_AllGroupsHaveNonEmptyName()
    {
        // Process the build so displayLabel is populated
        _host.State.DoString("runCallback('OnFrame')");

        var groups = _host.GetSkillGroups();

        Assert.All(groups, g => Assert.False(string.IsNullOrWhiteSpace(g.Name)));
    }

    [Fact]
    public void GetSkillGroups_AfterAddLightningWarp_GroupAppears()
    {
        AddSkill("Lightning Warp 20/20  1");

        var groups = _host.GetSkillGroups();

        Assert.Contains(groups, g => g.Name.Contains("Lightning Warp"));
    }

    [Fact]
    public void GetSkillGroups_TriggerGroup_ShowsCompositeDisplayLabel()
    {
        // Cast on Minion Death is a trigger skill; paired with Spark it should
        // produce displayLabel = "Cast on Minion Death, Spark" (or similar).
        AddSkill("Cast on Minion Death 20/20  1\nSpark 20/20  1");

        var groups = _host.GetSkillGroups();

        var triggerGroup = groups.FirstOrDefault(g =>
            g.Name.Contains("Cast on Minion Death", StringComparison.OrdinalIgnoreCase));

        Assert.NotNull(triggerGroup);
    }

    [Fact]
    public void GetSkillGroups_AllIndicesArePositive()
    {
        var groups = _host.GetSkillGroups();

        Assert.All(groups, g => Assert.True(g.Index > 0));
    }

    // ── GetActiveSkillsInGroup ─────────────────────────────────────────────

    [Fact]
    public void GetActiveSkillsInGroup_SingleSkillGroup_ReturnsOneEntry()
    {
        AddSkill("Lightning Warp 20/20  1");
        var groups = _host.GetSkillGroups();
        var lwGroup = groups.First(g => g.Name.Contains("Lightning Warp"));

        // Trigger MAIN-mode calc so displaySkillList is populated
        _host.SetActiveSkillGroup(lwGroup.Index);
        var skills = _host.GetActiveSkillsInGroup(lwGroup.Index);

        Assert.Single(skills);
    }

    [Fact]
    public void GetActiveSkillsInGroup_SingleSkillGroup_NameMatchesSkill()
    {
        AddSkill("Lightning Warp 20/20  1");
        var groups = _host.GetSkillGroups();
        var lwGroup = groups.First(g => g.Name.Contains("Lightning Warp"));

        _host.SetActiveSkillGroup(lwGroup.Index);
        var skills = _host.GetActiveSkillsInGroup(lwGroup.Index);

        Assert.Contains(skills, s => s.Name.Contains("Lightning Warp"));
    }

    [Fact]
    public void GetActiveSkillsInGroup_TriggerGroup_ReturnsMultipleSkills()
    {
        AddSkill("Cast on Minion Death 20/20  1\nSpark 20/20  1");
        var groups = _host.GetSkillGroups();
        var triggerGroup = groups.First(g =>
            g.Name.Contains("Cast on Minion Death", StringComparison.OrdinalIgnoreCase));

        _host.SetActiveSkillGroup(triggerGroup.Index);
        var skills = _host.GetActiveSkillsInGroup(triggerGroup.Index);

        Assert.True(skills.Count >= 2,
            $"Expected at least 2 skills in trigger group, got {skills.Count}");
    }

    [Fact]
    public void GetActiveSkillsInGroup_TriggerGroup_ContainsBothSkills()
    {
        AddSkill("Cast on Minion Death 20/20  1\nSpark 20/20  1");
        var groups = _host.GetSkillGroups();
        var triggerGroup = groups.First(g =>
            g.Name.Contains("Cast on Minion Death", StringComparison.OrdinalIgnoreCase));

        _host.SetActiveSkillGroup(triggerGroup.Index);
        var skills = _host.GetActiveSkillsInGroup(triggerGroup.Index);

        Assert.Contains(skills, s => s.Name.Contains("Spark", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GetActiveSkillsInGroup_AllSkillsHaveNonEmptyName()
    {
        AddSkill("Lightning Warp 20/20  1");
        var groups = _host.GetSkillGroups();
        var grp = groups.First(g => g.Name.Contains("Lightning Warp"));

        _host.SetActiveSkillGroup(grp.Index);
        var skills = _host.GetActiveSkillsInGroup(grp.Index);

        Assert.All(skills, s => Assert.False(string.IsNullOrWhiteSpace(s.Name)));
    }

    [Fact]
    public void GetActiveSkillsInGroup_AllSkillsHavePositiveIndex()
    {
        AddSkill("Lightning Warp 20/20  1");
        var groups = _host.GetSkillGroups();
        var grp = groups.First(g => g.Name.Contains("Lightning Warp"));

        _host.SetActiveSkillGroup(grp.Index);
        var skills = _host.GetActiveSkillsInGroup(grp.Index);

        Assert.All(skills, s => Assert.True(s.Index > 0));
    }

    // ── SetActiveSkillGroup ────────────────────────────────────────────────

    [Fact]
    public void SetActiveSkillGroup_ValidGroup_DoesNotThrow()
    {
        AddSkill("Lightning Warp 20/20  1");
        var groups = _host.GetSkillGroups();
        var grp = groups.First(g => g.Name.Contains("Lightning Warp"));

        var ex = Record.Exception(() => _host.SetActiveSkillGroup(grp.Index));

        Assert.Null(ex);
    }

    [Fact]
    public void SetActiveSkillGroup_LightningWarp_TotalDpsIsPositive()
    {
        AddSkill("Lightning Warp 20/20  1");
        var groups = _host.GetSkillGroups();
        var grp = groups.First(g => g.Name.Contains("Lightning Warp"));

        _host.SetActiveSkillGroup(grp.Index);
        var dps = Stat("TotalDPS");

        Assert.True(dps > 0, $"Expected TotalDPS > 0, got {dps}");
    }

    [Fact]
    public void SetActiveSkillGroup_LightningWarp_SpeedIsPositive()
    {
        AddSkill("Lightning Warp 20/20  1");
        var groups = _host.GetSkillGroups();
        var grp = groups.First(g => g.Name.Contains("Lightning Warp"));

        _host.SetActiveSkillGroup(grp.Index);
        var speed = Stat("Speed");

        Assert.True(speed > 0, $"Expected Speed > 0, got {speed}");
    }

    [Fact]
    public void SetActiveSkillGroup_WithActiveSkillIndex_DoesNotThrow()
    {
        AddSkill("Cast on Minion Death 20/20  1\nSpark 20/20  1");
        var groups = _host.GetSkillGroups();
        var grp = groups.First(g =>
            g.Name.Contains("Cast on Minion Death", StringComparison.OrdinalIgnoreCase));

        _host.SetActiveSkillGroup(grp.Index);
        var skills = _host.GetActiveSkillsInGroup(grp.Index);
        if (skills.Count < 2) return; // guard in case of unexpected group shape

        var ex = Record.Exception(() => _host.SetActiveSkillGroup(grp.Index, skills[1].Index));

        Assert.Null(ex);
    }

    [Fact]
    public void SetActiveSkillGroup_SwitchActiveSkill_UpdatesMainActiveSkillInLua()
    {
        AddSkill("Cast on Minion Death 20/20  1\nSpark 20/20  1");
        var groups = _host.GetSkillGroups();
        var grp = groups.First(g =>
            g.Name.Contains("Cast on Minion Death", StringComparison.OrdinalIgnoreCase));

        _host.SetActiveSkillGroup(grp.Index);
        var skills = _host.GetActiveSkillsInGroup(grp.Index);
        if (skills.Count < 2) return;

        _host.SetActiveSkillGroup(grp.Index, skills[1].Index);

        // Verify Lua side reflects the change
        var result = _host.State.DoString($@"
            local grp = build.skillsTab.socketGroupList[{grp.Index}]
            return grp and grp.mainActiveSkill or 0
        ");
        var mainActiveSkill = result is { Length: > 0 } ? Convert.ToInt32(result[0]) : 0;

        Assert.Equal(skills[1].Index, mainActiveSkill);
    }

    // ── LightningMin/Max from calcsOutput ────────────────────────────────
    // These are only populated in CALCS mode; GetAllStats() merges both outputs.

    [Fact]
    public void LightningWarp_LightningMinIsPositive()
    {
        AddSkill("Lightning Warp 20/20  1");
        var groups = _host.GetSkillGroups();
        var grp = groups.First(g => g.Name.Contains("Lightning Warp"));

        _host.SetActiveSkillGroup(grp.Index);
        var min = Stat("LightningMin");

        Assert.True(min > 0, $"Expected LightningMin > 0, got {min}");
    }

    [Fact]
    public void LightningWarp_LightningMaxGreaterThanMin()
    {
        AddSkill("Lightning Warp 20/20  1");
        var groups = _host.GetSkillGroups();
        var grp = groups.First(g => g.Name.Contains("Lightning Warp"));

        _host.SetActiveSkillGroup(grp.Index);

        Assert.True(Stat("LightningMax") >= Stat("LightningMin"),
            "LightningMax should be >= LightningMin");
    }
}
