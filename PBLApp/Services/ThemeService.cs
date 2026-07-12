using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using PBLApp.Core;

namespace PBLApp.Services;

/// <summary>
/// Single source of truth for the application theme. Reads/writes the
/// "AppTheme" key in <see cref="AppPreferences"/> and applies the variant to
/// <see cref="Application.Current"/>. Unknown/missing stored values fall back
/// to <see cref="AppThemeMode.Dark"/> (the app default).
/// </summary>
public sealed class ThemeService : IThemeSwitcher
{
    public const string PrefKey = "AppTheme";

    public static readonly ThemeService Instance = new();

    private ThemeService() { }

    public AppThemeMode Current { get; private set; } = AppThemeMode.Dark;

    /// <summary>Read the saved mode and apply it. Called before MainWindow is
    /// created so the window comes up in the right variant (no flash).</summary>
    public void InitializeAtStartup()
    {
        Current = AppThemeModes.Parse(AppPreferences.Get(PrefKey));
        ApplyVariant(Current);
    }

    public void Apply(AppThemeMode mode)
    {
        Current = mode;
        ApplyVariant(mode);
        // Persist best-effort; the runtime theme is already applied even if the
        // write fails (AppPreferences swallows I/O errors internally).
        AppPreferences.Set(PrefKey, mode.ToString());
    }

    public static ThemeVariant ToVariant(AppThemeMode mode) => mode switch
    {
        AppThemeMode.Dark  => ThemeVariant.Dark,
        AppThemeMode.Light => ThemeVariant.Light,
        _                  => ThemeVariant.Default,
    };

    private static void ApplyVariant(AppThemeMode mode)
    {
        if (Application.Current is { } app)
            app.RequestedThemeVariant = ToVariant(mode);
    }

    /// <summary>Resolve a token brush (e.g. "DangerBrush") for the active theme
    /// variant, with a hex fallback so rendering never fails on a missing key.
    /// For C#-built visuals (dialogs, tooltips) that can't use DynamicResource;
    /// they resolve at creation time and don't live-update on theme switch.</summary>
    public static IBrush Brush(string key, string fallbackHex)
    {
        var app = Application.Current;
        if (app is not null && app.TryGetResource(key, app.ActualThemeVariant, out var res) && res is IBrush b)
            return b;
        return new SolidColorBrush(Color.Parse(fallbackHex));
    }
}
