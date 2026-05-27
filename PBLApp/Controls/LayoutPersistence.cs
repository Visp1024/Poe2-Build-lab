using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace PBLApp.Controls;

/// <summary>
/// Persists Grid column widths across sessions.
///
/// Storage: <c>%LOCALAPPDATA%\PathOfBuilding\layout_state.json</c> — a single
/// dictionary keyed by a caller-supplied string (e.g. "SkillsTab.Columns")
/// holding the tracked column widths in order.
///
/// Apply by calling <see cref="Bind"/> from a view constructor with the Grid
/// instance, a stable key, and the indices of the columns whose widths should
/// be remembered. The bind is one-shot per Grid instance; re-attaching the
/// view re-applies saved widths but does not double-subscribe.
/// </summary>
public static class LayoutPersistence
{
    private static readonly string StateFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PathOfBuilding", "layout_state.json");

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private static Dictionary<string, double[]>? _cache;

    public static void Bind(Grid grid, string key, params int[] trackedColumns)
    {
        bool wired = false;

        // Per-bind debounce so dragging a splitter only writes the file once
        // after the user releases the mouse (rather than every pixel).
        var saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        saveTimer.Tick += (_, _) =>
        {
            saveTimer.Stop();
            var dict = Load();
            dict[key] = trackedColumns.Select(c =>
                c >= 0 && c < grid.ColumnDefinitions.Count
                    ? grid.ColumnDefinitions[c].Width.Value
                    : 0.0).ToArray();
            Save(dict);
        };

        grid.AttachedToVisualTree += (_, _) =>
        {
            var saved = Load().GetValueOrDefault(key);
            if (saved is not null && saved.Length == trackedColumns.Length)
            {
                for (int i = 0; i < trackedColumns.Length; i++)
                {
                    int col = trackedColumns[i];
                    if (col < 0 || col >= grid.ColumnDefinitions.Count) continue;
                    var w = saved[i];
                    if (w > 0)
                        grid.ColumnDefinitions[col].Width = new GridLength(w);
                }
            }

            if (wired) return;
            wired = true;

            foreach (var col in trackedColumns)
            {
                if (col < 0 || col >= grid.ColumnDefinitions.Count) continue;
                grid.ColumnDefinitions[col].PropertyChanged += (_, e) =>
                {
                    if (e.Property == ColumnDefinition.WidthProperty)
                    {
                        saveTimer.Stop();
                        saveTimer.Start();
                    }
                };
            }
        };
    }

    private static Dictionary<string, double[]> Load()
    {
        if (_cache is not null) return _cache;
        try
        {
            if (!File.Exists(StateFile)) return _cache = new();
            return _cache = JsonSerializer.Deserialize<Dictionary<string, double[]>>(
                File.ReadAllText(StateFile)) ?? new();
        }
        catch { return _cache = new(); }
    }

    private static void Save(Dictionary<string, double[]> all)
    {
        _cache = all;
        try
        {
            var dir = Path.GetDirectoryName(StateFile);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(StateFile, JsonSerializer.Serialize(all, JsonOpts));
        }
        catch { /* best-effort */ }
    }
}
