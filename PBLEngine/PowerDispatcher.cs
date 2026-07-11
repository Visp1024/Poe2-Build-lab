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
        // Guard against duplicate/invalid ids before the dictionary build below —
        // a duplicate Id throws from ToDictionary, and Id<=0 never legitimately
        // appears in a real node list.
        nodes = nodes.Where(n => n.Id > 0).DistinctBy(n => n.Id).ToList();
        var byId = nodes.ToDictionary(n => n.Id);
        // Группы одинаковых modKey — в один батч, чтобы работал кэш воркера.
        var ordered = nodes.OrderBy(n => n.ModKey, StringComparer.Ordinal).Select(n => n.Id).ToList();
        var queue = new ConcurrentQueue<int[]>(Chunk(ordered, BatchSize));

        int total = nodes.Count, done = 0;
        var rows = new ConcurrentDictionary<int, PowerBatchRow>();
        var prepared = new ConcurrentBag<IPowerWorker>();
        var dead = new HashSet<IPowerWorker>();
        var deadLock = new object();
        // Last exception that killed a worker — surfaced as InnerException on
        // "all workers failed" so the UI/logs show *why* every worker died instead
        // of just the fact that they did.
        Exception? lastWorkerError = null;

        void MarkDead(IPowerWorker w, Exception? ex = null)
        {
            lock (deadLock) { dead.Add(w); if (ex != null) lastWorkerError = ex; }
        }
        bool IsDead(IPowerWorker w) { lock (deadLock) return dead.Contains(w); }

        async Task Consume(Task<IPowerWorker> wt, ConcurrentQueue<int[]> q,
                           Func<IPowerWorker, int[], Task<int>> work, int lo, int hi)
        {
            IPowerWorker w;
            try { w = await wt.WaitAsync(ct); } catch { return; }
            // A late worker (still warming up while faster ones drained the queue)
            // must not pay PrepareAsync (LoadBuildFromXml + BuildOutput) just to find
            // nothing left to do — skip straight out before Prepare if so.
            if (q.IsEmpty) return;
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
                        int mapped = lo + (int)(d * (hi - lo) / (double)Math.Max(1, total));
                        onProgress?.Invoke(Math.Min(hi, mapped));
                    }
                    catch (OperationCanceledException ex) { q.Enqueue(batch); MarkDead(w, ex); return; }
                    catch (Exception ex) { q.Enqueue(batch); MarkDead(w, ex); return; }   // воркер мёртв — батч назад, выходим
                }
            }
            catch (Exception ex) { MarkDead(w, ex); /* Prepare умер — воркер выбывает */ }
        }

        // Раунды: пока в очереди что-то есть, гоняем консюмеров живых воркеров.
        // Батч, вернувшийся в очередь после смерти своего воркера (Fix 1), подхватят
        // выжившие воркеры в следующем раунде — без этого он мог осиротеть, если
        // остальные консюмеры к тому моменту уже опустошили очередь и вышли.
        async Task RunRounds(ConcurrentQueue<int[]> q, Func<IPowerWorker, int[], Task<int>> work, int lo, int hi)
        {
            while (!q.IsEmpty)
            {
                if (ct.IsCancellationRequested) return;

                var usable = new List<Task<IPowerWorker>>();
                foreach (var wt in workerTasks)
                {
                    if (wt.IsFaulted || wt.IsCanceled) continue;
                    if (wt.IsCompletedSuccessfully && IsDead(wt.Result)) continue;
                    usable.Add(wt);
                }
                if (usable.Count == 0)
                    throw new InvalidOperationException("all workers failed", lastWorkerError);

                await Task.WhenAll(usable.Select(wt => Consume(wt, q, work, lo, hi)).ToArray());

                if (ct.IsCancellationRequested) return;
            }
        }

        try
        {
            // Фаза 1: power. Прогресс 0-80.
            await RunRounds(queue, async (w, batch) =>
            {
                foreach (var r in await w.ComputeBatchAsync(batch, ct)) rows[r.Id] = r;
                return batch.Length;
            }, 0, 80);
            if (ct.IsCancellationRequested)
                return new NodePowerResult(offDefMode, new NodePowerMax(0, 0, 0), []);

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
                // Фаза 2: pathPower. Прогресс 80-100.
                await RunRounds(pathQueue, async (w, batch) =>
                {
                    foreach (var r in await w.ComputePathBatchAsync(batch, ct)) pathRows[r.Id] = r;
                    return batch.Length;
                }, 80, 100);
                if (ct.IsCancellationRequested)
                    return new NodePowerResult(offDefMode, new NodePowerMax(0, 0, 0), []);
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
