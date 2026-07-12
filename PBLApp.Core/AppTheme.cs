namespace PBLApp.Core;

/// <summary>
/// Application theme selection. <see cref="System"/> follows the OS (Windows)
/// light/dark setting; the other two force a variant.
/// </summary>
public enum AppThemeMode
{
    System,
    Dark,
    Light,
}

/// <summary>
/// Abstraction over the theme engine so ViewModels in this assembly (which has
/// no Avalonia reference) can apply a theme. Implemented by ThemeService in PBLApp.
/// </summary>
public interface IThemeSwitcher
{
    AppThemeMode Current { get; }
    void Apply(AppThemeMode mode);
}

public static class AppThemeModes
{
    /// <summary>Parse the value stored in prefs (mode.ToString()). Unknown,
    /// missing or corrupt values fall back to Dark (the app default) — never throws.</summary>
    public static AppThemeMode Parse(string? stored) => stored switch
    {
        nameof(AppThemeMode.System) => AppThemeMode.System,
        nameof(AppThemeMode.Light)  => AppThemeMode.Light,
        _                           => AppThemeMode.Dark,
    };
}
