using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace PBLApp.Core;

/// <summary>
/// Tiny key→value preference store for small UI settings that should survive
/// across sessions (e.g. the Calcs density mode). Backed by a single JSON file
/// at <c>%LOCALAPPDATA%\PathOfBuilding\prefs.json</c>.
///
/// Deliberately minimal — for anything structured (grid widths, window bounds)
/// use the dedicated persistence helpers instead.
/// </summary>
public static class AppPreferences
{
    private static readonly string StateFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PathOfBuilding", "prefs.json");

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private static Dictionary<string, string>? _cache;

    public static string? Get(string key) => Load().GetValueOrDefault(key);

    public static bool GetBool(string key, bool fallback = false)
        => Load().TryGetValue(key, out var v) ? v == "1" || string.Equals(v, "true", StringComparison.OrdinalIgnoreCase) : fallback;

    public static void Set(string key, string value)
    {
        var dict = Load();
        dict[key] = value;
        Save(dict);
    }

    public static void SetBool(string key, bool value) => Set(key, value ? "1" : "0");

    private static Dictionary<string, string> Load()
    {
        if (_cache is not null) return _cache;
        try
        {
            if (!File.Exists(StateFile)) return _cache = new();
            return _cache = JsonSerializer.Deserialize<Dictionary<string, string>>(
                File.ReadAllText(StateFile)) ?? new();
        }
        catch { return _cache = new(); }
    }

    private static void Save(Dictionary<string, string> all)
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
