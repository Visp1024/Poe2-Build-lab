using PBLEngine;
using Xunit;

namespace PBLEngine.Tests;

[Collection("LuaHost")]
public class TraderWeightsTests : IClassFixture<LuaHostFixture>
{
    private readonly LuaHost _host;

    public TraderWeightsTests(LuaHostFixture fixture)
    {
        _host = fixture.Host;
        _host.NewBuild();
    }

    [Fact]
    public void GetWeightStats_ContainsFullDpsAndEhp_ExcludesIgnored()
    {
        var json = _host.GetTraderWeightStatsJson();
        Assert.Contains("\"FullDPS\"", json);
        Assert.Contains("\"TotalEHP\"", json);
        // ignoreForItems-статы не предлагаются (Offence/Defence — Data.lua:128)
        Assert.DoesNotContain("Offence/Defence", json);
    }

    [Fact]
    public void GetWeights_EmptyBuild_ReturnsDefaultPair()
    {
        var json = _host.GetTraderWeightsJson();
        Assert.Contains("\"FullDPS\"", json);
        Assert.Contains("\"TotalEHP\"", json);
    }

    [Fact]
    public void SetWeights_RoundTripsThroughBuildXml()
    {
        _host.SetTraderWeights(
            """[{"stat":"FullDPS","weightMult":0.7},{"stat":"Life","weightMult":1.0}]""");
        var xml = _host.SaveBuildToXml();
        Assert.Contains("TradeSearchWeights", xml);
        Assert.Contains("Life", xml);

        _host.LoadBuildFromXml(xml, "roundtrip");
        var json = _host.GetTraderWeightsJson();
        Assert.Contains("\"Life\"", json);
        Assert.Contains("0.7", json);
    }

    [Fact]
    public void SetWeights_DropsZeroAndUnknownStats()
    {
        _host.SetTraderWeights(
            """[{"stat":"FullDPS","weightMult":0},{"stat":"NoSuchStat","weightMult":1.0},{"stat":"TotalEHP","weightMult":0.5}]""");
        var json = _host.GetTraderWeightsJson();
        Assert.DoesNotContain("\"FullDPS\"", json);
        Assert.DoesNotContain("NoSuchStat", json);
        Assert.Contains("\"TotalEHP\"", json);
    }

    [Fact(Timeout = 300_000)]
    [Trait("Category", "Slow")]
    public async Task Generate_WithCustomWeights_ProducesQuery()
    {
        // Life — стат с прямым output-ключом; проверяем что кастомный список работает end-to-end
        var result = await _host.GenerateTradeQueryAsync("Helmet",
            """{"statWeights":[{"stat":"Life","weightMult":1.0}],"includeCorrupted":false,"includeMirrored":false}""",
            null, System.Threading.CancellationToken.None);
        Assert.Null(result.Error);
        Assert.Contains("\"type\":\"weight\"", result.QueryJson);
    }
}
