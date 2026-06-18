using PBLEngine;
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
        _host.NewBuild();
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

        // The "Name" entry (ignoreForNodes/itemField) must not appear in the node heat-map list.
        Assert.DoesNotContain(list, o => o.IgnoreForNodes);
    }
}
