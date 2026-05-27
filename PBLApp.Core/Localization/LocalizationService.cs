using System;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Resources;

namespace PBLApp.Core.Localization;

public sealed class LocalizationService : INotifyPropertyChanged
{
    // SettingsFile MUST be declared before Instance so it is initialized before
    // the LocalizationService() constructor runs. Static fields are initialized
    // in declaration order: if Instance = new() appeared first, LoadSaved() would
    // read SettingsFile while it was still "" (its default), always falling back
    // to "en" and ignoring the saved language file.
    private static readonly string SettingsFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PathOfBuilding", "language.txt");

    private static readonly ResourceManager Rm =
        new("PBLApp.ViewModels.Localization.Strings", typeof(LocalizationService).Assembly);

    public static readonly LocalizationService Instance = new();

    private CultureInfo _culture;

    private LocalizationService()
    {
        var saved = LoadSaved();
        _culture = CultureInfo.GetCultureInfo(saved);
    }

    private static string LoadSaved()
    {
        try { return File.Exists(SettingsFile) ? File.ReadAllText(SettingsFile).Trim() : "en"; }
        catch { return "en"; }
    }

    public string this[string key] => Rm.GetString(key, _culture) ?? $"[{key}]";

    public static string Get(string key) => Instance[key];

    public string CurrentLanguage => _culture.TwoLetterISOLanguageName;

    public void SetLanguage(string cultureName)
    {
        var next = CultureInfo.GetCultureInfo(cultureName);
        if (next.Name == _culture.Name) return;
        _culture = next;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile)!);
            File.WriteAllText(SettingsFile, cultureName);
        }
        catch { }
        LanguageChanged?.Invoke(this, EventArgs.Empty);
        // Fire CurrentLanguage explicitly — TrExtension binds {loc:Tr KEY} to this
        // property with a converter that resolves the key. Each XAML-bound label
        // re-evaluates its converter when CurrentLanguage changes.
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentLanguage)));
        // Also fire empty-string for any other listeners depending on root-level refresh.
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }

    public event EventHandler? LanguageChanged;
    public event PropertyChangedEventHandler? PropertyChanged;
}
