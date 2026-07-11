using CommunityToolkit.Mvvm.ComponentModel;
using PBLApp.Core;
using PBLApp.Core.Localization;
using System.Collections.Generic;

namespace PBLApp.ViewModels;

/// <summary>
/// ViewModel for the application settings window. Currently a single
/// "Appearance" section with the theme selector; future sections (color
/// customization) stack below it. Theme application goes through
/// <see cref="IThemeSwitcher"/> so this assembly stays Avalonia-free.
/// </summary>
public partial class AppSettingsViewModel : ViewModelBase
{
    private readonly IThemeSwitcher _themeSwitcher;

    /// <summary>Localized theme option labels; order must match Index/Mode mapping below.</summary>
    public IReadOnlyList<string> ThemeOptions { get; } =
    [
        LocalizationService.Get("Theme_System"),
        LocalizationService.Get("Theme_Dark"),
        LocalizationService.Get("Theme_Light"),
    ];

    /// <summary>0 = System, 1 = Dark, 2 = Light.</summary>
    [ObservableProperty]
    private int _selectedThemeIndex;

    public AppSettingsViewModel(IThemeSwitcher themeSwitcher)
    {
        _themeSwitcher = themeSwitcher;
        _selectedThemeIndex = IndexOf(themeSwitcher.Current);
    }

    partial void OnSelectedThemeIndexChanged(int value) => _themeSwitcher.Apply(ModeAt(value));

    public static int IndexOf(AppThemeMode mode) => mode switch
    {
        AppThemeMode.Dark  => 1,
        AppThemeMode.Light => 2,
        _                  => 0,
    };

    public static AppThemeMode ModeAt(int index) => index switch
    {
        1 => AppThemeMode.Dark,
        2 => AppThemeMode.Light,
        _ => AppThemeMode.System,
    };
}
