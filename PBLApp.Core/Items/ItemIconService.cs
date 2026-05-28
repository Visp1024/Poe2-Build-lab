using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PBLApp.Core.Items;

/// <summary>
/// Resolves item display names to absolute paths of pre-downloaded item icons.
///
/// Two install layouts are supported:
///
///   1. Published build:  &lt;AppDir&gt;/ItemIcons/icon_map.json + ItemIcons/Art/2DItems/...
///   2. Dev/repo build:   &lt;repo&gt;/PBLExport/icons/icon_map.json + cache/Art/2DItems/...
///
/// If neither map is present, the service silently returns null for every
/// lookup — that's the "light" build mode where the UI falls back to its
/// text-only slot rendering.
/// </summary>
public sealed class ItemIconService
{
    public static readonly ItemIconService Instance = new();

    private readonly Dictionary<string, string> _bases   = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _uniques = new(StringComparer.OrdinalIgnoreCase);
    private readonly string? _iconRoot;
    private readonly bool _loaded;

    public bool IsAvailable => _loaded;

    private ItemIconService()
    {
        foreach (var (mapPath, iconRoot) in CandidatePaths())
        {
            if (!File.Exists(mapPath)) continue;
            try
            {
                var json = File.ReadAllText(mapPath);
                var doc = JsonSerializer.Deserialize<IconMapDto>(json, JsonOpts);
                if (doc is null) continue;
                if (doc.Bases is not null)
                    foreach (var (k, v) in doc.Bases)
                        if (!string.IsNullOrEmpty(v.File)) _bases[k] = v.File;
                if (doc.Uniques is not null)
                    foreach (var (k, v) in doc.Uniques)
                        if (!string.IsNullOrEmpty(v.File)) _uniques[k] = v.File;
                _iconRoot = iconRoot;
                _loaded = true;
                return;
            }
            catch
            {
                // bad map — try the next candidate
            }
        }
    }

    /// <summary>
    /// Resolve an item to an icon path. Lookup order:
    ///   1. Unique name (split off the comma if the caller passed "Bramblejack, Plate Vest")
    ///   2. Base name
    /// Returns null when the map is empty or no match is found.
    /// </summary>
    public string? Resolve(string? baseName, string? uniqueName = null)
    {
        if (!_loaded || _iconRoot is null) return null;
        string? rel = null;
        if (!string.IsNullOrEmpty(uniqueName))
        {
            var stripped = StripAfterComma(uniqueName);
            if (_uniques.TryGetValue(stripped, out var u)) rel = u;
        }
        if (rel is null && !string.IsNullOrEmpty(baseName))
            _bases.TryGetValue(baseName, out rel);
        if (rel is null) return null;
        // map.file uses forward slashes — normalise for Windows
        var full = Path.Combine(_iconRoot, rel.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(full) ? full : null;
    }

    private static string StripAfterComma(string s)
    {
        var i = s.IndexOf(',');
        return i > 0 ? s[..i].Trim() : s;
    }

    private static IEnumerable<(string MapPath, string IconRoot)> CandidatePaths()
    {
        // 1) Next to the running exe (published layout)
        var appDir = AppContext.BaseDirectory;
        yield return (
            Path.Combine(appDir, "ItemIcons", "icon_map.json"),
            Path.Combine(appDir, "ItemIcons"));

        // 2) Dev fallback — walk up from BaseDirectory to find a sibling repo root
        var dir = new DirectoryInfo(appDir);
        while (dir is not null)
        {
            var dev = Path.Combine(dir.FullName, "PBLExport", "icons");
            if (Directory.Exists(dev))
            {
                yield return (
                    Path.Combine(dev, "icon_map.json"),
                    Path.Combine(dev, "cache"));
                yield break;
            }
            dir = dir.Parent;
        }
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private sealed class IconMapDto
    {
        [JsonPropertyName("bases")]   public Dictionary<string, IconEntryDto>? Bases   { get; set; }
        [JsonPropertyName("uniques")] public Dictionary<string, IconEntryDto>? Uniques { get; set; }
    }

    private sealed class IconEntryDto
    {
        [JsonPropertyName("file")] public string File { get; set; } = "";
    }
}
