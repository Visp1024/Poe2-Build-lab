using CommunityToolkit.Mvvm.ComponentModel;
using PBLApp.Core.Localization;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace PBLApp.ViewModels;

public partial class StatSectionViewModel : ViewModelBase
{
    private readonly string _key;

    // When set, the whole section is hidden unless this stat is present and > 0.
    // Used for niche sections (e.g. Runic Ward) that only matter for builds that
    // stack them — otherwise they'd show a row of zeroes on every build.
    private readonly string? _gateStatKey;

    public string Label => LocalizationService.Get(_key);
    public ObservableCollection<StatRowViewModel> Rows { get; } = [];

    [ObservableProperty] private bool _isVisible = true;

    public StatSectionViewModel(string key, string? gateStatKey = null)
    {
        _key = key;
        _gateStatKey = gateStatKey;
        LocalizationService.Instance.LanguageChanged += (_, _) => OnPropertyChanged(nameof(Label));
    }

    /// <summary>Recompute section visibility from the latest stat dictionary.</summary>
    public void UpdateVisibility(IReadOnlyDictionary<string, object?> stats)
    {
        if (_gateStatKey is null) { IsVisible = true; return; }
        IsVisible = stats.TryGetValue(_gateStatKey, out var raw)
                    && raw is not null
                    && Convert.ToDouble(raw) > 0;
    }
}
