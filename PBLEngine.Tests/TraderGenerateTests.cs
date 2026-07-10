using PBLEngine;
using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace PBLEngine.Tests;

[Collection("LuaHost")]
public class TraderGenerateTests : IClassFixture<LuaHostFixture>
{
    private readonly LuaHost _host;

    public TraderGenerateTests(LuaHostFixture fixture)
    {
        _host = fixture.Host;
        _host.NewBuild();
    }

    private const string Options =
        """{"statWeights":[{"stat":"FullDPS","weightMult":1.0},{"stat":"TotalEHP","weightMult":0.5}],"includeCorrupted":false,"includeMirrored":false}""";

    [Fact(Timeout = 300_000)]
    [Trait("Category", "Slow")] // сотни calcFunc-прогонов; в быстрых прогонах исключать фильтром
    public async Task GenerateTradeQuery_ForHelmet_ProducesWeightQuery()
    {
        var result = await _host.GenerateTradeQueryAsync(
            "Helmet", Options, null, CancellationToken.None);

        Assert.Null(result.Error);
        Assert.NotNull(result.QueryJson);
        Assert.Contains("\"type\":\"weight\"", result.QueryJson);
        Assert.Contains("armour.helmet", result.QueryJson);
        // status.option обязателен: без tradeTypeIndex генератор кладёт {} и API
        // отвечает "[2: Invalid status type]"; дефолт оригинала — securable
        Assert.Contains("\"status\":{\"option\":\"securable\"}", result.QueryJson);
    }

    /// <summary>IProgress с синхронным Report — Progress&lt;T&gt; постит в пул и гонится с завершением.</summary>
    private sealed class SyncProgress(Action action) : IProgress<int>
    {
        public void Report(int value) => action();
    }

    [Fact(Timeout = 60_000)]
    public async Task GenerateTradeQuery_Cancellation_LeavesCleanState()
    {
        using var cts = new CancellationTokenSource();
        // отменяем сразу после первого resume — детерминированно, без таймингов
        var cancelOnFirstResume = new SyncProgress(cts.Cancel);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _host.GenerateTradeQueryAsync("Helmet", Options, cancelOnFirstResume, cts.Token));

        // корутина снята — повторный запуск не заблокирован
        Assert.Equal("nil", (string)_host.State.DoString(
            "return type(PBLTrader.generator.calcContext and PBLTrader.generator.calcContext.co)")[0]);
    }

    [Fact(Timeout = 60_000)]
    public async Task GenerateTradeQuery_UnknownSlot_ReturnsError()
    {
        var result = await _host.GenerateTradeQueryAsync(
            "No Such Slot", Options, null, CancellationToken.None);

        Assert.NotNull(result.Error);
        Assert.Null(result.QueryJson);
    }
}
