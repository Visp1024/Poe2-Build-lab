using PBLEngine;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace PBLEngine.Tests;

[Collection("LuaHost")]
public class TraderRequiredTests : IClassFixture<LuaHostFixture>
{
    private readonly LuaHost _host;

    public TraderRequiredTests(LuaHostFixture fixture)
    {
        _host = fixture.Host;
        _host.NewBuild();
    }

    [Fact]
    public void TradeStatsForSlot_Helmet_NonEmptyExplicitIds()
    {
        var json = _host.GetTradeStatsForSlotJson("Helmet");
        Assert.Contains("explicit.stat_", json);
        Assert.Contains("\"text\"", json);
    }

    [Fact]
    public void TradeStatsForSlot_UnknownSlot_EmptyArray()
    {
        var json = _host.GetTradeStatsForSlotJson("No Such Slot");
        Assert.Equal("[]", json.Trim());
    }

    [Fact(Timeout = 30_000)]
    public async Task ApplyRequiredStats_AddsAndGroup_KeepsWeightGroup()
    {
        const string query =
            """{"query":{"status":{"option":"securable"},"stats":[{"type":"weight","value":{"min":100},"filters":[]}]},"sort":{"statgroup.0":"desc"}}""";
        var result = await _host.ApplyRequiredStatsAsync(query,
            """[{"id":"explicit.stat_111","min":75},{"id":"explicit.stat_222"}]""",
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.Contains("\"type\":\"weight\"", result);
        Assert.Contains("\"type\":\"and\"", result);
        Assert.Contains("explicit.stat_111", result);
        Assert.Contains("\"min\":75", result);
        Assert.Contains("explicit.stat_222", result);
    }
}
