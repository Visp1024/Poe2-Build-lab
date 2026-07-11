using System;
using PBLEngine;

namespace PBLApp.Core;

/// <summary>Process-wide holder of the power-calc worker pool. The pool is
/// created lazily on the first heat-map calculation and survives build
/// re-opens (worker init is ~40 s — never throw it away).</summary>
public static class TreePowerService
{
    private static LuaWorkerPool? _pool;
    private static readonly object Gate = new();

    /// <summary>Configured worker count: prefs key "tree.powerWorkers"
    /// ("auto" | "0".."8"), default auto = clamp(cores-2, 1, 4). 0 → пул выключен.</summary>
    public static int ConfiguredSize()
    {
        var raw = AppPreferences.Get("tree.powerWorkers");
        if (int.TryParse(raw, out var n)) return Math.Clamp(n, 0, 8);
        return Math.Clamp(Environment.ProcessorCount - 2, 1, 4);
    }

    /// <summary>Возвращает пул (создавая при первом обращении) или null при size=0.
    /// Если сохранённый размер изменился — старый пул утилизируется.</summary>
    public static LuaWorkerPool? GetPool(string repoRoot)
    {
        var size = ConfiguredSize();
        lock (Gate)
        {
            if (size == 0) { _pool?.Dispose(); _pool = null; return null; }
            if (_pool != null && _pool.Size != size) { _pool.Dispose(); _pool = null; }
            return _pool ??= new LuaWorkerPool(repoRoot, size);
        }
    }
}
