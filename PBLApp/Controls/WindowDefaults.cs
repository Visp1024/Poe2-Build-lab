using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;

namespace PBLApp.Controls;

/// <summary>
/// Shared window-size defaults + per-window persistence.
///
/// First launch: window opens at Full-HD (1920x1080, DIPs) clamped to the screen
/// working area. Position is left to the XAML <c>WindowStartupLocation</c>
/// (CenterScreen for MainWindow, CenterOwner for child windows).
///
/// Subsequent launches: position, size, and maximised state are restored from
/// <c>%LOCALAPPDATA%\PathOfBuilding\window_state.json</c> (one entry per window
/// type by simple class name).
/// </summary>
public static class WindowDefaults
{
    public const double DefaultWidth  = 1920;
    public const double DefaultHeight = 1080;

    private static readonly string StateFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PathOfBuilding", "window_state.json");

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    /// <summary>Call from a Window constructor after <c>InitializeComponent</c>.</summary>
    public static void Apply(Window w)
    {
        w.Opened  += (_, _) => OnOpened(w);
        w.Closing += (_, _) => OnClosing(w);
    }

    // ── Open ─────────────────────────────────────────────────────────────

    private static void OnOpened(Window w)
    {
        var saved = LoadFor(w.GetType().Name);

        // Pick the screen the window should restore onto: the monitor that
        // contained its saved position (so a window closed on a secondary
        // display reopens there), falling back to the current/primary screen.
        Screen? screen = null;
        if (saved is not null)
            screen = w.Screens.ScreenFromPoint(new PixelPoint(saved.X, saved.Y));
        screen ??= w.Screens.ScreenFromWindow(w) ?? w.Screens.Primary;
        if (screen is null) return;

        var scale = screen.Scaling > 0 ? screen.Scaling : 1.0;
        var maxW  = screen.WorkingArea.Width  / scale;
        var maxH  = screen.WorkingArea.Height / scale;

        if (saved is not null)
        {
            // Restore size, clamped to current screen.
            w.Width  = Math.Max(200, Math.Min(saved.Width,  maxW));
            w.Height = Math.Max(150, Math.Min(saved.Height, maxH));

            // Restore position, clamped so the window stays on-screen.
            int physW = (int)(w.Width  * scale);
            int physH = (int)(w.Height * scale);
            int x = Math.Clamp(saved.X, screen.WorkingArea.X,
                               Math.Max(screen.WorkingArea.X, screen.WorkingArea.Right  - physW));
            int y = Math.Clamp(saved.Y, screen.WorkingArea.Y,
                               Math.Max(screen.WorkingArea.Y, screen.WorkingArea.Bottom - physH));
            w.Position = new PixelPoint(x, y);

            if (saved.Maximized)
                w.WindowState = WindowState.Maximized;
        }
        else
        {
            // No saved state: apply Full-HD default, clamped to screen.
            w.Width  = Math.Min(DefaultWidth,  maxW);
            w.Height = Math.Min(DefaultHeight, maxH);
            // Position left to XAML WindowStartupLocation.
        }
    }

    // ── Save ─────────────────────────────────────────────────────────────

    private static void OnClosing(Window w)
    {
        try
        {
            // Capture the un-maximised bounds where possible.
            bool maximised = w.WindowState == WindowState.Maximized;
            var dict = LoadAll();
            dict[w.GetType().Name] = new SavedState
            {
                X         = w.Position.X,
                Y         = w.Position.Y,
                Width     = w.Width,
                Height    = w.Height,
                Maximized = maximised,
            };
            SaveAll(dict);
        }
        catch { /* persistence is best-effort */ }
    }

    // ── JSON I/O ─────────────────────────────────────────────────────────

    private static SavedState? LoadFor(string key)
    {
        var all = LoadAll();
        return all.TryGetValue(key, out var s) ? s : null;
    }

    private static Dictionary<string, SavedState> LoadAll()
    {
        try
        {
            if (!File.Exists(StateFile))
                return new Dictionary<string, SavedState>();
            var json = File.ReadAllText(StateFile);
            return JsonSerializer.Deserialize<Dictionary<string, SavedState>>(json)
                   ?? new Dictionary<string, SavedState>();
        }
        catch
        {
            return new Dictionary<string, SavedState>();
        }
    }

    private static void SaveAll(Dictionary<string, SavedState> all)
    {
        try
        {
            var dir = Path.GetDirectoryName(StateFile);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(StateFile, JsonSerializer.Serialize(all, JsonOpts));
        }
        catch { /* best-effort */ }
    }

    public sealed record SavedState
    {
        public int    X         { get; init; }
        public int    Y         { get; init; }
        public double Width     { get; init; }
        public double Height    { get; init; }
        public bool   Maximized { get; init; }
    }
}
