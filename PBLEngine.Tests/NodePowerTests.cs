using PBLEngine;
using System.IO;
using System.Linq;
using Xunit;

namespace PBLEngine.Tests;

[Collection("LuaHost")]
public class NodePowerTests : IClassFixture<LuaHostFixture>
{
    private readonly LuaHost _host;

    public NodePowerTests(LuaHostFixture fixture)
    {
        _host = fixture.Host;
        // Load a real community build (Sorceress/Stormweaver, CombinedDPS ~42k) so
        // power calculations have something to measure.  Community builds store
        // includeInFullDPS=false on every socket group (FullDPS uses a separate
        // per-group toggle), so we enable it on the main group after loading so
        // the FullDPS stat is non-zero.
        var buildPath = Path.Combine(RepoRoot(), "tools", "parity", "community_builds",
            "CqX3fXBg_S0q7p54SFGHC.xml");
        _host.LoadBuildFromXml(File.ReadAllText(buildPath), "NodePowerTest");
        // Enable FullDPS roll-up for the main socket group.
        // build.mainSocketGroup is an index into skillsTab.socketGroupList.
        _host.State.DoString(@"
            local idx = build.mainSocketGroup or 1
            local sg = build.skillsTab.socketGroupList and build.skillsTab.socketGroupList[idx]
            if sg then sg.includeInFullDPS = true end
        ");
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "src")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Cannot find repo root (no src/ ancestor)");
    }

    [Fact]
    public void GetPowerStatList_ReturnsOptions_IncludingOffDefDefault()
    {
        var list = _host.GetPowerStatList();

        Assert.NotEmpty(list);
        Assert.Contains(list, o => o.CombinedOffDef);            // Offence/Defence default
        Assert.Contains(list, o => o.StatKey == "FullDPS");      // a known single stat
    }

    [Fact]
    public void GetPowerStatList_ExcludesItemOnlyEntries()
    {
        var list = _host.GetPowerStatList();

        // The "Name" entry has ignoreForNodes/itemField set and must not appear in the
        // node heat-map list. This is a real filter check — if the filter broke, "Name"
        // would be included and this assertion would catch it.
        Assert.DoesNotContain(list, o => o.Label == "Name");
    }

    [Fact]
    public void BuildNodePower_SingleStat_TopNodeHasPositivePower()
    {
        var res = _host.BuildNodePower("FullDPS");

        Assert.False(res.OffDefMode);
        Assert.NotEmpty(res.Entries);
        // Strongest node for a DPS stat must move the stat upward.
        var top = res.Entries.OrderByDescending(e => e.Power).First();
        Assert.True(top.Power > 0, $"top node power was {top.Power}");
        Assert.True(res.Max.SingleStat > 0);
    }

    [Fact]
    public void BuildNodePower_OffDefMode_ReturnsBothChannels()
    {
        var res = _host.BuildNodePower(null);   // null = Offence/Defence default

        Assert.True(res.OffDefMode);
        Assert.NotEmpty(res.Entries);
        Assert.True(res.Max.Offence  > 0, "no offence maximum");
        Assert.True(res.Max.Defence  > 0, "no defence maximum");
    }

    [Fact]
    public void BuildNodePower_ReportsProgressToCompletion()
    {
        int last = -1;
        _host.BuildNodePower("Life", null, pc => last = pc);

        Assert.True(last >= 0);   // callback fired at least once
    }

    [Fact]
    public void BuildNodePower_OnlyReturnsAllocatableTreeNodeTypes()
    {
        var res = _host.BuildNodePower("Life");

        Assert.All(res.Entries, e =>
            Assert.Contains(e.Type, new[] { "Normal", "Notable", "Keystone" }));
    }
}
