using PBLApp.ViewModels;
using PBLEngine;
using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace PBLEngine.Tests;

[Collection("LuaHost")]
public class TraderTabViewModelTests : IClassFixture<LuaHostFixture>
{
    private readonly LuaHost _host;

    public TraderTabViewModelTests(LuaHostFixture fixture)
    {
        _host = fixture.Host;
        _host.NewBuild();
    }

    [Fact(Timeout = 60_000)]
    public async Task TryOnListing_EquipsItemAndChangesLife()
    {
        var before = Convert.ToDouble(_host.GetStat("Life"));

        var ok = await _host.TryOnListingAsync("Helmet",
            "Rarity: RARE\nDoom Crown\nWarrior Greathelm\nItem Level: 81\nImplicits: 0\n+120 to maximum Life",
            CancellationToken.None);

        Assert.True(ok);
        var after = Convert.ToDouble(_host.GetStat("Life"));
        Assert.True(after > before, $"Life before={before}, after={after}");
    }

    [Fact(Timeout = 60_000)]
    public async Task TryOnListing_GarbageText_ReturnsFalse()
    {
        var ok = await _host.TryOnListingAsync("Helmet", "garbage", CancellationToken.None);
        Assert.False(ok);
    }

    [Fact]
    public void Session_PopulatesWeights_AndPresetsChangeWeights()
    {
        var session = new TraderSession(_host, new BuildModel(_host),
            webApi: new PBLApp.Core.Trader.TraderWebApi(new FakeHttpHandler()));

        Assert.NotEmpty(session.WeightStats);
        Assert.Contains(session.KnownSlots, s => s == "Helmet");
        Assert.Contains(session.KnownSlots, s => s == "Body Armour");

        session.ApplyPresetCommand.Execute("ehp");
        var fullDps = session.WeightStats.First(w => w.Stat == "FullDPS");
        var totalEhp = session.WeightStats.First(w => w.Stat == "TotalEHP");
        Assert.Equal(0.1, fullDps.WeightMult, 6);
        Assert.Equal(1.0, totalEhp.WeightMult, 6);
        Assert.True(session.HasActiveWeights);
        Assert.Contains("FullDPS", session.StatWeightsJson);
    }

    [Fact]
    public void Window_BuildsOptionsJson_FromSessionWeightsAndMaxPrice()
    {
        var session = new TraderSession(_host, new BuildModel(_host),
            webApi: new PBLApp.Core.Trader.TraderWebApi(new FakeHttpHandler()));
        session.ApplyPresetCommand.Execute("dps");
        var win = new TraderWindowViewModel(session, "Helmet");

        Assert.Contains("\"statWeights\"", win.OptionsJson);
        Assert.Contains("FullDPS", win.OptionsJson);
    }

    [Fact]
    public void Window_RequiredFilters_LoadAddAndBuildJson_PersistPerSlotInSession()
    {
        var session = new TraderSession(_host, new BuildModel(_host),
            webApi: new PBLApp.Core.Trader.TraderWebApi(new FakeHttpHandler()));
        var win = new TraderWindowViewModel(session, "Helmet");
        Assert.True(win.HasStatCategory);

        win.LoadAvailableStatsCommand.Execute(null);
        Assert.NotEmpty(win.AvailableStats);

        var stat = win.AvailableStats[0];
        win.AddRequiredCommand.Execute(stat);
        win.RequiredFilters[0].Min = "75";

        Assert.Contains(stat.Id, win.RequiredJson);
        Assert.Contains("\"min\":75", win.RequiredJson);

        // персист в сессии: перенацелить на другой слот и обратно — фильтр на месте
        win.Retarget("Gloves");
        Assert.Empty(win.RequiredFilters);
        win.Retarget("Helmet");
        Assert.Single(win.RequiredFilters);
        Assert.Equal(stat.Id, win.RequiredFilters[0].Id);
    }
}
