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
    public void ViewModel_PopulatesSlots_AndPresetsChangeWeights()
    {
        var vm = new TraderTabViewModel(_host, new BuildModel(_host),
            webApi: new PBLApp.Core.Trader.TraderWebApi(new FakeHttpHandler()));

        Assert.NotEmpty(vm.Slots);
        Assert.Contains(vm.Slots, s => s.SlotName == "Helmet");
        Assert.Contains(vm.Slots, s => s.SlotName == "Body Armour");

        vm.ApplyPresetCommand.Execute("ehp");
        Assert.Equal(0.1, vm.DpsWeight, 6);
        Assert.Equal(1.0, vm.EhpWeight, 6);

        Assert.Contains("\"statWeights\"", vm.OptionsJson);
        Assert.Contains("FullDPS", vm.StatWeightsJson);
    }
}
