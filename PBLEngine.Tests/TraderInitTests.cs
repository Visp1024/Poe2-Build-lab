using PBLEngine;
using Xunit;

namespace PBLEngine.Tests;

[Collection("LuaHost")]
public class TraderInitTests : IClassFixture<LuaHostFixture>
{
    private readonly LuaHost _host;

    public TraderInitTests(LuaHostFixture fixture)
    {
        _host = fixture.Host;
        _host.NewBuild();
    }

    [Fact]
    public void EnsureTraderInit_CreatesGeneratorAndRequests()
    {
        _host.EnsureTraderInit();

        Assert.Equal("table", (string)_host.State.DoString("return type(PBLTrader.generator)")[0]);
        Assert.Equal("table", (string)_host.State.DoString("return type(PBLTrader.requests)")[0]);
        Assert.Equal("table", (string)_host.State.DoString("return type(main.api)")[0]);
        // генератор привязан к текущему itemsTab
        Assert.True((bool)_host.State.DoString(
            "return PBLTrader.generator.itemsTab == build.itemsTab")[0]);
    }

    [Fact]
    public void GetSlotsJson_ReturnsBaseSlots()
    {
        _host.EnsureTraderInit();
        var json = (string)_host.State.DoString("return PBLTrader.GetSlotsJson()")[0];

        Assert.Contains("\"Helmet\"", json);
        Assert.Contains("\"Body Armour\"", json);
        Assert.Contains("\"Ring 1\"", json);
    }
}
