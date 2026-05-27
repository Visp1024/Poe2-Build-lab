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

    [ObservableProperty] private string _value = "—";
    [ObservableProperty] private bool _isSelected;

    public StatRowViewModel(string labelKey, string statKey, string suffix = "")
    {
        _labelKey = labelKey;
        StatKey   = statKey;
        _suffix   = suffix;
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
