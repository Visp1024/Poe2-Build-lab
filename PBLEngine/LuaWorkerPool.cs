using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PBLEngine;

/// <summary>Пул фоновых LuaHost-воркеров для параллельного расчёта power.
/// Создание дешёвое; реальные хосты поднимаются лениво из EnsureStarted()
/// со стаггером 3 с между стартами (инициализация одного хоста ~40 с —
/// стартовать их одновременно означало бы CPU-конкуренцию за одно и то же время).</summary>
public sealed class LuaWorkerPool : IDisposable
{
    private const int StaggerMs = 3000;

    /// <summary>Один слот пула: Task инициализации + материализованный воркер
    /// (заполняется самой Task после успешного создания, используется для
    /// поиска мёртвых воркеров при следующем EnsureStarted()).</summary>
    private sealed class WorkerSlot
    {
        public Task<IPowerWorker> Task = null!;
        public LuaWorker? Worker;
    }

    private readonly string _repoRoot;
    private readonly object _gate = new();
    private readonly CancellationTokenSource _disposeCts = new();
    private List<WorkerSlot>? _slots;
    private int _ready;
    private bool _disposed;

    public LuaWorkerPool(string repoRoot, int size)
    {
        _repoRoot = repoRoot;
        Size = size;
    }

    public int Size { get; }

    /// <summary>Число воркеров, завершивших Initialize.</summary>
    public int Ready => Volatile.Read(ref _ready);

    public event Action? ReadyChanged;

    /// <summary>Идемпотентно стартует инициализацию воркеров (стаггер 3 с между
    /// стартами) и возвращает их Task — вход для NodePowerOrchestrator.
    /// Мёртвые воркеры заменяются новыми Task.</summary>
    public IReadOnlyList<Task<IPowerWorker>> EnsureStarted()
    {
        lock (_gate)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(LuaWorkerPool));

            if (_slots == null)
            {
                _slots = new List<WorkerSlot>(Size);
                for (int i = 0; i < Size; i++)
                    _slots.Add(Spawn(i * StaggerMs));
            }
            else
            {
                // Замена мёртвых воркеров — стаггер не нужен, это редкая штучная замена.
                for (int i = 0; i < _slots.Count; i++)
                {
                    var dead = Volatile.Read(ref _slots[i].Worker);
                    if (dead is { IsDead: true })
                    {
                        Interlocked.Decrement(ref _ready);
                        _slots[i] = Spawn(0);
                        try { dead.Dispose(); } catch { /* best-effort */ }
                    }
                }
            }
            return _slots.Select(s => s.Task).ToList();
        }
    }

    private WorkerSlot Spawn(int delayMs)
    {
        var slot = new WorkerSlot();
        var ct = _disposeCts.Token;
        slot.Task = Task.Run(async () =>
        {
            if (delayMs > 0) await Task.Delay(delayMs, ct);
            ct.ThrowIfCancellationRequested();

            var host = new LuaHost();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            host.Initialize(_repoRoot);
            sw.Stop();
            Console.Error.WriteLine($"[LuaWorkerPool] worker init {sw.ElapsedMilliseconds} ms");
            var worker = new LuaWorker(host);
            Volatile.Write(ref slot.Worker, worker);
            Interlocked.Increment(ref _ready);
            ReadyChanged?.Invoke();
            return (IPowerWorker)worker;
        }, ct);
        return slot;
    }

    /// <summary>Dispose всех хостов, отмена не начатых инициализаций.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _disposeCts.Cancel();

            if (_slots != null)
            {
                foreach (var slot in _slots)
                {
                    var worker = Volatile.Read(ref slot.Worker);
                    if (worker != null)
                    {
                        worker.Dispose();
                    }
                    else
                    {
                        // Init still in flight (or already cancelled) — dispose the
                        // host once/if it actually finishes materialising.
                        slot.Task.ContinueWith(t =>
                        {
                            if (t.Status == TaskStatus.RanToCompletion)
                                ((LuaWorker)t.Result).Dispose();
                        }, TaskScheduler.Default);
                    }
                }
                _slots = null;
            }
        }
        _disposeCts.Dispose();
    }
}

/// <summary>IPowerWorker поверх собственного LuaHost: один state — один вызов
/// за раз (семафор); первое исключение NLua помечает воркер мёртвым.</summary>
internal sealed class LuaWorker : IPowerWorker, IDisposable
{
    private readonly LuaHost _host;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private volatile bool _disposed;
    public bool IsDead { get; private set; }

    public LuaWorker(LuaHost host) => _host = host;

    private async Task<T> Run<T>(Func<T> f, CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (_disposed) throw new ObjectDisposedException(nameof(LuaWorker));
            return await Task.Run(f, ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (ObjectDisposedException) { throw; }
        catch { IsDead = true; throw; }
        finally { _lock.Release(); }
    }

    public Task PrepareAsync(string buildXml, string? statKey, CancellationToken ct)
        => Run<object?>(() =>
        {
            _host.LoadBuildFromXml(buildXml, "PowerWorker");
            _host.BeginPowerSession(statKey);
            return null;
        }, ct);

    public Task<List<PowerBatchRow>> ComputeBatchAsync(int[] ids, CancellationToken ct)
        => Run(() => _host.ComputePowerBatch(ids), ct);

    public Task<List<PathPowerRow>> ComputePathBatchAsync(int[] ids, CancellationToken ct)
        => Run(() => _host.ComputePathPowerBatch(ids), ct);

    public async Task FinishAsync()
    {
        if (IsDead) return;
        try { await Run<object?>(() => { _host.EndPowerSession(); return null; }, CancellationToken.None); }
        catch { /* best-effort */ }
    }

    public void Dispose()
    {
        _disposed = true;
        try { _host.Dispose(); } catch { /* best-effort */ }
        // Intentionally not disposing _lock: a concurrent in-flight ComputeBatchAsync
        // may still be holding it (finally { _lock.Release(); } would throw
        // ObjectDisposedException on a disposed SemaphoreSlim). SemaphoreSlim without
        // AvailableWaitHandle allocated holds no OS resources, so leaving it undisposed
        // here is a documented-safe pattern — no leak.
    }
}
