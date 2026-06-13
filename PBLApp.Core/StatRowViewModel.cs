using CommunityToolkit.Mvvm.ComponentModel;
using PBLApp.Core.Localization;
using System;
using System.Collections.Generic;

namespace PBLApp.ViewModels;

public partial class StatRowViewModel : ViewModelBase
{
    private readonly string _labelKey;
    private readonly string _suffix;

    public string Label => LocalizationService.Get(_labelKey);
    public string StatKey { get; }

    /// <summary>Theme resource key for the value colour (e.g. "StatDpsBrush", "ElFireBrush").
    /// Resolved to an IBrush in XAML via TooltipKindConverter.BrushKey.</summary>
    public string ColorKey { get; }

    [ObservableProperty] private string _value = "—";
    [ObservableProperty] private bool _isSelected;

    /// <summary>True when an active search filter doesn't match this row — the row is
    /// dimmed (not hidden) so the layout stays stable while searching.</summary>
    [ObservableProperty] private bool _isDimmed;

    public StatRowViewModel(string labelKey, string statKey, string suffix = "", string colorKey = "TextPrimaryBrush")
    {
        _labelKey = labelKey;
        StatKey   = statKey;
        _suffix   = suffix;
        ColorKey  = colorKey;
        LocalizationService.Instance.LanguageChanged += (_, _) => OnPropertyChanged(nameof(Label));
    }

    public void UpdateValue(IReadOnlyDictionary<string, object?> stats)
    {
        if (stats.TryGetValue(StatKey, out var raw) && raw is not null)
            Value = Format(Convert.ToDouble(raw));
        else
            Value = "—";
    }

    private string Format(double d)
    {
        if (_suffix == "%")  return d.ToString("N1") + "%";
        if (_suffix == "x")  return d.ToString("N2") + "x";
        if (d >= 1_000_000)  return (d / 1_000_000.0).ToString("N2") + "M";
        if (d >= 10_000)     return (d / 1_000.0).ToString("N1") + "k";
        if (d == Math.Floor(d)) return ((long)d).ToString("N0");
        return d.ToString("N2");
    }
}
