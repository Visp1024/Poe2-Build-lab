using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace PBLApp.Converters;

public class TypeMatchConverter : IValueConverter
{
    public static readonly TypeMatchConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var types = (parameter as string)?.Split('|') ?? [];
        return Array.IndexOf(types, value as string) >= 0;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
