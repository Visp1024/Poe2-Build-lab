using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PBLEngine;

public interface IPowerWorker
{
    Task PrepareAsync(string buildXml, string? statKey, CancellationToken ct);
    Task<List<PowerBatchRow>> ComputeBatchAsync(int[] ids, CancellationToken ct);
    Task<List<PathPowerRow>> ComputePathBatchAsync(int[] ids, CancellationToken ct);
    Task FinishAsync();
}

public static class NodePowerOrchestrator
{
    public const int BatchSize     = 25;
    public const int PathBatchSize = 10;
    public const int PathTopK      = 100;

    public static async Task<NodePowerResult> RunAsync(
        string buildXml, string? statKey, bool offDefMode,
        IReadOnlyList<PowerNodeInfo> nodes,
        IReadOnlyList<Task<IPowerWorker>> workerTasks,
        Action<int>? onProgress, CancellationToken ct)
    {
        var byId = nodes.ToDictionary(n => n.Id);
        // Группы одинаковых modKey — в один батч, чтобы работал кэш воркера.
        var ordered = nodes.OrderBy(n => n.ModKey, StringComparer.Ordinal).Select(n => n.Id).ToList();
        var queue = new ConcurrentQueue<int[]>(Chunk(ordered, BatchSize));

        int total = nodes.Count, done = 0;
        var rows = new ConcurrentDictionary<int, PowerBatchRow>();
        var prepared = new ConcurrentBag<IPowerWorker>();

        async Task Consume(Task<IPowerWorker> wt, ConcurrentQueue<int[]> q,
                           Func<IPowerWorker, int[], Task<int>> work)
        {
            IPowerWorker w;
            try { w = await wt.WaitAsync(ct); } catch { return; }
            try
            {
                if (!prepared.Contains(w))
                {
                    await w.PrepareAsync(buildXml, statKey, ct);
                    prepared.Add(w);
                }
                while (!ct.IsCancellationRequested && q.TryDequeue(out var batch))
                {
                    try
                    {
                        int n = await work(w, batch);
                        int d = Interlocked.Add(ref done, n);
                        onProgress?.Invoke(Math.Min(80, (int)(d * 80.0 / Math.Max(1, total))));
                    }
                    catch (OperationCanceledException) { q.Enqueue(batch); return; }
                    catch { q.Enqueue(batch); return; }   // воркер мёртв — батч назад, выходим
                }
            }
            catch { /* Prepare умер — воркер выбывает */ }
        }

        try
        {
            // Фаза 1: power.
            await Task.WhenAll(workerTasks.Select(wt => Consume(wt, queue, async (w, batch) =>
            {
                foreach (var r in await w.ComputeBatchAsync(batch, ct)) rows[r.Id] = r;
                return batch.Length;
            })).ToArray());
            if (ct.IsCancellationRequested)
                return new NodePowerResult(offDefMode, new NodePowerMax(0, 0, 0), []);
            if (!queue.IsEmpty)
                throw new InvalidOperationException("all workers failed");

            // Фаза 2: pathPower для топ-K достижимых невзятых нод c Steps > 1.
            var top = rows.Values
                .Where(r => byId[r.Id] is { Alloc: false, IsCluster: false, Steps: > 1 })
                .OrderByDescending(r => Math.Abs(r.Power))
                .Take(PathTopK).Select(r => r.Id).ToList();
            var pathRows = new ConcurrentDictionary<int, PathPowerRow>();
            if (top.Count > 0)
            {
                var pathQueue = new ConcurrentQueue<int[]>(Chunk(top, PathBatchSize));
                done = 0; total = top.Count;
                await Task.WhenAll(workerTasks.Select(wt => Consume(wt, pathQueue, async (w, batch) =>
                {
                    foreach (var r in await w.ComputePathBatchAsync(batch, ct)) pathRows[r.Id] = r;
                    return batch.Length;
                })).ToArray());
                if (ct.IsCancellationRequested)
                    return new NodePowerResult(offDefMode, new NodePowerMax(0, 0, 0), []);
                if (!pathQueue.IsEmpty)
                    throw new InvalidOperationException("all workers failed");
            }
            onProgress?.Invoke(100);

            // Слияние.
            double maxS = 0, maxO = 0, maxD = 0;
            var entries = new List<NodePowerEntry>(rows.Count);
            foreach (var n in nodes)
            {
                if (!rows.TryGetValue(n.Id, out var r)) continue;
                if (n is { Alloc: false, IsCluster: false, Steps: not null })
                {
                    maxS = Math.Max(maxS, r.Power);
                    maxO = Math.Max(maxO, r.Offence);
                    maxD = Math.Max(maxD, r.Defence);
                }
                double? pathPower = null; string? perPointStr = null;
                if (n is { Alloc: false, IsCluster: false, Steps: 1 })
                { pathPower = r.Power; perPointStr = r.PowerStr; }
                else if (pathRows.TryGetValue(n.Id, out var pr))
                { pathPower = pr.PathPower; perPointStr = pr.PerPointStr; }
                entries.Add(new NodePowerEntry(n.Id, n.Name, n.Type, n.Alloc, n.Steps,
                    r.Power, pathPower, r.Offence, r.Defence, r.PowerStr, perPointStr));
            }
            return new NodePowerResult(offDefMode, new NodePowerMax(maxS, maxO, maxD), entries);
        }
        finally
        {
            foreach (var w in prepared)
                try { await w.FinishAsync(); } catch { }
        }
    }

    private static IEnumerable<int[]> Chunk(List<int> ids, int size)
    {
        for (int i = 0; i < ids.Count; i += size)
            yield return ids.Skip(i).Take(size).ToArray();
    }
}
