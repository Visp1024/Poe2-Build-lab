using Avalonia.Data.Converters;
using PBLApp.Core.Localization;
using PBLApp.ViewModels;
using System;
using System.Globalization;

namespace PBLApp.Converters;

/// <summary>Подпись лиги в фильтре импорта персонажа: служебное значение
/// <see cref="CharacterImportViewModel.AllLeagues"/> показывается как «Все лиги»,
/// остальные — как есть (имена лиг GGG не переводятся).</summary>
public sealed class LeagueLabelConverter : IValueConverter
{
    public static readonly LeagueLabelConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value as string == CharacterImportViewModel.AllLeagues
            ? LocalizationService.Get("CharImport_AllLeagues")
            : value?.ToString() ?? "";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
