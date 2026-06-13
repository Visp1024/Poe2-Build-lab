using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace PBLApp.Converters;

/// <summary>Returns true when the bound enum value's name equals the converter
/// parameter (e.g. ConverterParameter=Str). Used to drive an "active" class on
/// the support-picker attribute tabs from a single enum property.</summary>
public class EnumEqualsConverter : IValueConverter
{
    public static readonly EnumEqualsConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => string.Equals(value?.ToString(), parameter as string, StringComparison.Ordinal);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
