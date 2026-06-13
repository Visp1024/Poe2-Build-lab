using System;
using System.IO;
using System.Text.Json;

namespace PBLApp.Controls;

/// <summary>
/// Persists the passive-tree view (zoom + world-space centre) so it survives
/// tab switches and app restarts.
///
/// Storage: <c>%LOCALAPPDATA%\PathOfBuilding\tree_view.json</c> — a single
/// {scale, cx, cy} record. Global (not per-build): returning to the Tree tab
/// or relaunching restores the last framing the user left.
/// </summary>
public static class TreeViewPersistence
{
    private static readonly string StateFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PathOfBuilding", "tree_view.json");

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public sealed record View
    {
        public double Scale   { get; init; }
        public double CenterX { get; init; }
        public double CenterY { get; init; }
    }

    public static View? Load()
    {
        try
        {
            if (!File.Exists(StateFile)) return null;
            var v = JsonSerializer.Deserialize<View>(File.ReadAllText(StateFile));
            // Guard against a zero/garbage scale that would render an empty view.
            return v is { Scale: > 0 } ? v : null;
        }
        catch { return null; }
    }

    public static void Save(double scale, double cx, double cy)
    {
        if (!(scale > 0)) return;
        try
        {
            var dir = Path.GetDirectoryName(StateFile);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(StateFile,
                JsonSerializer.Serialize(new View { Scale = scale, CenterX = cx, CenterY = cy }, JsonOpts));
        }
        catch { /* best-effort */ }
    }
}
