using PBLEngine;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace PBLEngine.Tests;

[Collection("LuaHost")]
public class TraderDiffTests : IClassFixture<LuaHostFixture>
{
    private readonly LuaHost _host;

    public TraderDiffTests(LuaHostFixture fixture)
    {
        _host = fixture.Host;
        _host.NewBuild();
    }

    private const string ItemText =
        "Rarity: RARE\nDoom Crown\nWarrior Greathelm\nArmour: 500\nItem Level: 81\nImplicits: 0\n+120 to maximum Life";

    private const string Weights =
        """[{"stat":"FullDPS","weightMult":1.0},{"stat":"TotalEHP","weightMult":0.5}]""";

    [Fact(Timeout = 60_000)]
    public async Task ComputeListingDiff_LifeHelmet_PositiveEhp()
    {
        var diff = await _host.ComputeListingDiffAsync(
            "Helmet", ItemText, Weights, CancellationToken.None);

        if (diff is null)
        {
            _host.EnsureTraderInit();
            _host.State["_pblSlotName"] = "Helmet";
            _host.State["_pblItemText"] = ItemText;
            _host.State["_pblWeights"] = Weights;
            var raw = (string)_host.State.DoString(
                "return PBLTrader.ComputeDiffJson(_pblSlotName, _pblItemText, _pblWeights)")[0];
            Assert.Fail($"diff is null, raw lua json: {raw}");
        }
        Assert.True(diff!.EhpDiff > 0, $"EhpDiff={diff.EhpDiff}");
    }

    [Fact(Timeout = 60_000)]
    public async Task ComputeListingDiff_GarbageText_ReturnsNullOrZero()
    {
        // Мусорный текст не должен ронять хост: либо null, либо нулевой дифф
        var diff = await _host.ComputeListingDiffAsync(
            "Helmet", "not an item at all", Weights, CancellationToken.None);

        if (diff is not null)
        {
            Assert.Equal(0, diff.DpsDiff, 3);
        }
    }
}
