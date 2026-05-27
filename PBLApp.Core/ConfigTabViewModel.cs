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
    public IReadOnlyList<ConfigSectionViewModel> Sections      { get; }
    public IReadOnlyList<ConfigSectionViewModel> LeftSections  { get; }
    public IReadOnlyList<ConfigSectionViewModel> MiddleSections { get; }
    public IReadOnlyList<ConfigSectionViewModel> RightSections { get; }

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

        // Greedy balance: place each section (in original order) into whichever
        // column is currently shortest, weighting `text` rows as roughly 8 lines
        // (the multi-line Custom Modifiers textarea is ~160 px tall vs. ~22 px
        // for a typical option row).
        var columns = new[] { new List<ConfigSectionViewModel>(), new List<ConfigSectionViewModel>(), new List<ConfigSectionViewModel>() };
        var heights = new int[3];
        foreach (var section in Sections)
        {
            int idx = 0;
            for (int i = 1; i < 3; i++)
                if (heights[i] < heights[idx]) idx = i;
            columns[idx].Add(section);
            heights[idx] += Weight(section) + 2; // +2 for section header + bottom margin
        }
        LeftSections   = columns[0];
        MiddleSections = columns[1];
        RightSections  = columns[2];
    }

    private static int Weight(ConfigSectionViewModel s) =>
        s.Options.Sum(o => o.Type == "text" ? 8 : 1);
}
