using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PBLApp.Core.Localization;
using PBLEngine;
using System.Collections.Generic;
using System.Linq;

namespace PBLApp.ViewModels;

public partial class ConfigSectionViewModel : ObservableObject
{
    /// <summary>Raw English section name from PoB (used for column routing).</summary>
    public string NameKey { get; }
    /// <summary>Localised display name (shown as the group header).</summary>
    public string Name { get; }
    public IReadOnlyList<ConfigOptionViewModel> Options { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ChevronGlyph))]
    private bool _isExpanded = true;

    public string ChevronGlyph => IsExpanded ? "▾" : "▸"; // ▾ / ▸

    [RelayCommand]
    private void Toggle() => IsExpanded = !IsExpanded;

    public ConfigSectionViewModel(string nameKey, string name, IEnumerable<ConfigOptionViewModel> options)
    {
        NameKey = nameKey;
        Name    = name;
        Options = [.. options];
    }
}

public class ConfigTabViewModel : ViewModelBase
{
    /// <summary>All config sections in PoB order. The view flows them into a
    /// space-filling masonry (same compact look as the Calcs tab).</summary>
    public IReadOnlyList<ConfigSectionViewModel> Sections { get; }

    public ConfigTabViewModel(LuaHost host, BuildModel build)
    {
        var options = host.GetConfigOptions();
        Sections = options
            .GroupBy(o => o.Section)
            .Select(g => new ConfigSectionViewModel(
                g.Key,
                GameTranslationService.TConfigSection(g.Key),
                g.Select(o => new ConfigOptionViewModel(o, host, build))))
            .Where(s => s.Options.Count > 0)
            .ToList();
    }
}
