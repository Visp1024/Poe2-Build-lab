using Avalonia;
using Avalonia.Media.Imaging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace PBLApp.Controls;

/// <summary>
/// Loads passive tree sprite sheets from manifest.json and provides
/// per-sprite (Bitmap, srcRect) lookups. Sheets are loaded lazily and cached.
///
/// Sheets are stored as WebP tiles (one image per row) because the original
/// vertical sheets (up to 49500 px tall) exceed WebP's 16383-pixel dimension
/// limit. Each tile has size (sprite_w x sprite_h) and represents a single row.
/// </summary>
public sealed class TreeAssetStore : IDisposable
{
    // ── Manifest types ─────────────────────────────────────────────────────

    private sealed record SpriteEntry(
        string? Sheet,
        int X, int Y, int W, int H,
        string? File);

    private sealed record SheetEntry(
        string[] Tiles,   // per-row WebP filenames; tile index = y / TileH
        int TileH,
        int SpriteW, int SpriteH,
        int SheetW, int SheetH,
        int Cols, int Rows);

    // ── State ──────────────────────────────────────────────────────────────

    private readonly string _assetDir;
    private readonly Dictionary<string, SpriteEntry> _sprites = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SheetEntry>  _sheets  = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Bitmap>      _bitmaps = new(StringComparer.OrdinalIgnoreCase);

    private TreeAssetStore(string assetDir) => _assetDir = assetDir;

    // ── Factory ────────────────────────────────────────────────────────────

    /// <summary>
    /// Try to load the manifest for the given tree version.
    /// Returns null if assets are not yet converted (run tools/convert_tree_to_webp.py).
    /// </summary>
    public static TreeAssetStore? TryLoad(string repoRoot, string version = "0_4")
    {
        // Preferred path: assets copied next to the exe via <Content> (Assets/TreeData/<ver>/).
        // Fallback path: dev layout where the working directory is the repo and PBLApp/Assets is in-tree.
        string[] candidates =
        {
            Path.Combine(AppContext.BaseDirectory, "Assets", "TreeData", version),
            Path.Combine(repoRoot, "PBLApp", "Assets", "TreeData", version),
        };

        foreach (var dir in candidates)
        {
            var manifest = Path.Combine(dir, "manifest.json");
            if (!File.Exists(manifest)) continue;
            var store = new TreeAssetStore(dir);
            store.ParseManifest(manifest);
            return store;
        }
        return null;
    }

    // ── Public API ─────────────────────────────────────────────────────────

    /// <summary>
    /// Get a sprite by name.
    /// Returns (bitmap, srcRect) or null if name is unknown / sheet not found.
    /// </summary>
    public (Bitmap Bmp, Rect Src)? GetSprite(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        if (!_sprites.TryGetValue(name, out var s)) return null;

        // Standalone WebP (orbit lines, etc.)
        if (s.Sheet == null)
        {
            if (s.File == null) return null;
            var bmp = LoadBitmap(s.File);
            if (bmp == null) return null;
            return (bmp, new Rect(0, 0, bmp.PixelSize.Width, bmp.PixelSize.Height));
        }

        // Sprite inside a tiled sheet — select the row tile and rebase Y to local coords.
        if (!_sheets.TryGetValue(s.Sheet, out var sheetEntry)) return null;
        if (sheetEntry.TileH <= 0 || sheetEntry.Tiles.Length == 0) return null;

        var row = s.Y / sheetEntry.TileH;
        if (row < 0 || row >= sheetEntry.Tiles.Length) return null;

        var tile = LoadBitmap(sheetEntry.Tiles[row]);
        if (tile == null) return null;

        var localY = s.Y - row * sheetEntry.TileH;
        return (tile, new Rect(s.X, localY, s.W, s.H));
    }

    /// <summary>
    /// Check whether a sprite name exists in the manifest.
    /// </summary>
    public bool HasSprite(string name) =>
        !string.IsNullOrEmpty(name) && _sprites.ContainsKey(name);

    // ── Manifest parsing ───────────────────────────────────────────────────

    private void ParseManifest(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
        var root = doc.RootElement;

        if (root.TryGetProperty("sprites", out var spritesEl))
        {
            foreach (var prop in spritesEl.EnumerateObject())
            {
                var v = prop.Value;
                var sheet = v.TryGetProperty("sheet", out var shEl) && shEl.ValueKind != JsonValueKind.Null
                    ? shEl.GetString() : null;
                var file  = v.TryGetProperty("file", out var fEl) ? fEl.GetString() : null;
                int x = 0, y = 0, w = 0, h = 0;
                if (v.TryGetProperty("x", out var xEl)) x = xEl.GetInt32();
                if (v.TryGetProperty("y", out var yEl)) y = yEl.GetInt32();
                if (v.TryGetProperty("w", out var wEl)) w = wEl.GetInt32();
                if (v.TryGetProperty("h", out var hEl)) h = hEl.GetInt32();
                _sprites[prop.Name] = new SpriteEntry(sheet, x, y, w, h, file);
            }
        }

        if (root.TryGetProperty("sheets", out var sheetsEl))
        {
            foreach (var prop in sheetsEl.EnumerateObject())
            {
                var v = prop.Value;
                var tiles = ReadTiles(v);
                var tileH = v.TryGetProperty("tile_h", out var thEl)
                    ? thEl.GetInt32()
                    : v.GetProperty("sprite_h").GetInt32();
                _sheets[prop.Name] = new SheetEntry(
                    tiles,
                    tileH,
                    v.GetProperty("sprite_w").GetInt32(),
                    v.GetProperty("sprite_h").GetInt32(),
                    v.GetProperty("sheet_w").GetInt32(),
                    v.GetProperty("sheet_h").GetInt32(),
                    v.GetProperty("cols").GetInt32(),
                    v.GetProperty("rows").GetInt32());
            }
        }
    }

    private static string[] ReadTiles(JsonElement sheetEl)
    {
        // Preferred: "tiles": ["a.webp","b.webp",...]
        if (sheetEl.TryGetProperty("tiles", out var tilesEl) && tilesEl.ValueKind == JsonValueKind.Array)
        {
            var list = new List<string>(tilesEl.GetArrayLength());
            foreach (var t in tilesEl.EnumerateArray())
            {
                var s = t.GetString();
                if (!string.IsNullOrEmpty(s)) list.Add(s);
            }
            return list.ToArray();
        }
        // Legacy: single "file" entry covering the whole (unsplit) sheet.
        if (sheetEl.TryGetProperty("file", out var fileEl))
        {
            var f = fileEl.GetString();
            if (!string.IsNullOrEmpty(f)) return [f];
        }
        return [];
    }

    // ── Bitmap cache ───────────────────────────────────────────────────────

    private Bitmap? LoadBitmap(string filename)
    {
        if (_bitmaps.TryGetValue(filename, out var cached)) return cached;
        var fullPath = Path.Combine(_assetDir, filename);
        if (!File.Exists(fullPath)) return null;
        try
        {
            var bmp = new Bitmap(fullPath);
            _bitmaps[filename] = bmp;
            return bmp;
        }
        catch { return null; }
    }

    public void Dispose()
    {
        foreach (var bmp in _bitmaps.Values) bmp.Dispose();
        _bitmaps.Clear();
    }
}
