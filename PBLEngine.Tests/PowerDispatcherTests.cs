using PBLEngine;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace PBLEngine.Tests;

public class PowerDispatcherTests
{
    private sealed class FakeWorker : IPowerWorker
    {
        public int Prepared, Batches;
        public bool FailOnBatch;
        public int DelayMs;
        public Task PrepareAsync(string xml, string? stat, CancellationToken ct)
        { Prepared++; return Task.CompletedTask; }
        public async Task<List<PowerBatchRow>> ComputeBatchAsync(int[] ids, CancellationToken ct)
        {
            if (FailOnBatch) throw new InvalidOperationException("boom");
            if (DelayMs > 0) await Task.Delay(DelayMs, ct);
            Batches++;
            return ids.Select(i => new PowerBatchRow(i, i * 0.5, 0, 0, (i * 0.5).ToString())).ToList();
        }
        public Task<List<PathPowerRow>> ComputePathBatchAsync(int[] ids, CancellationToken ct)
            => Task.FromResult(ids.Select(i => new PathPowerRow(i, i * 0.4, "pp" + i)).ToList());
        public Task FinishAsync() => Task.CompletedTask;
    }

    private static List<PowerNodeInfo> Nodes(int n) =>
        Enumerable.Range(1, n).Select(i =>
            new PowerNodeInfo(i, "mk" + (i % 7), "N" + i, "Normal", false, false, 1 + i % 5)).ToList();

    [Fact]
    public async Task RunAsync_AllNodesComputed_AcrossWorkers()
    {
        var w1 = new FakeWorker { DelayMs = 10 }; var w2 = new FakeWorker { DelayMs = 10 };
        var result = await NodePowerOrchestrator.RunAsync(
            "<xml/>", "FullDPS", false, Nodes(120),
            new[] { Task.FromResult<IPowerWorker>(w1), Task.FromResult<IPowerWorker>(w2) },
            null, CancellationToken.None);
        Assert.Equal(120, result.Entries.Count);
        Assert.Equal(1, w1.Prepared); Assert.Equal(1, w2.Prepared);
        Assert.True(w1.Batches > 0 && w2.Batches > 0, "оба воркера получили батчи");
    }

    [Fact]
    public async Task RunAsync_LateWorkerJoins()
    {
        var w1 = new FakeWorker { DelayMs = 30 };
        var late = new FakeWorker();
        var lateTask = Task.Delay(100).ContinueWith(_ => (IPowerWorker)late);
        var result = await NodePowerOrchestrator.RunAsync(
            "<xml/>", null, true, Nodes(200),
            new[] { Task.FromResult<IPowerWorker>(w1), lateTask },
            null, CancellationToken.None);
        Assert.Equal(200, result.Entries.Count);
        Assert.True(late.Batches > 0, "поздний воркер подключился к очереди");
    }

    [Fact]
    public async Task RunAsync_FailedWorker_BatchRequeued_OthersFinish()
    {
        var bad = new FakeWorker { FailOnBatch = true };
        var good = new FakeWorker();
        var result = await NodePowerOrchestrator.RunAsync(
            "<xml/>", "FullDPS", false, Nodes(100),
            new[] { Task.FromResult<IPowerWorker>(bad), Task.FromResult<IPowerWorker>(good) },
            null, CancellationToken.None);
        Assert.Equal(100, result.Entries.Count);   // ничего не потеряно
    }

    [Fact]
    public async Task RunAsync_AllWorkersFail_Throws()
    {
        var bad = new FakeWorker { FailOnBatch = true };
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            NodePowerOrchestrator.RunAsync("<xml/>", "FullDPS", false, Nodes(10),
                new[] { Task.FromResult<IPowerWorker>(bad) }, null, CancellationToken.None));
    }

    [Fact]
    public async Task RunAsync_Cancellation_ReturnsEmpty()
    {
        using var cts = new CancellationTokenSource();
        var slow = new FakeWorker { DelayMs = 50 };
        var run = NodePowerOrchestrator.RunAsync("<xml/>", "FullDPS", false, Nodes(500),
            new[] { Task.FromResult<IPowerWorker>(slow) }, null, cts.Token);
        cts.CancelAfter(60);
        var result = await run;
        Assert.Empty(result.Entries);
    }

    [Fact]
    public async Task RunAsync_TopK_GetsPathPower_StepsOneReusesPower()
    {
        var w = new FakeWorker();
        var result = await NodePowerOrchestrator.RunAsync(
            "<xml/>", "FullDPS", false, Nodes(150),
            new[] { Task.FromResult<IPowerWorker>(w) }, null, CancellationToken.None);
        var steps1 = result.Entries.Where(e => e.Steps == 1).ToList();
        Assert.All(steps1, e => Assert.Equal(e.Power, e.PathPower));
        var topWithPath = result.Entries.Where(e => e.PathPower != null).Count();
        Assert.True(topWithPath >= Math.Min(100, 150), $"топ-K досчитан (got {topWithPath})");
    }
}
