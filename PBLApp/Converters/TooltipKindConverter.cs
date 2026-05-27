using Avalonia.Data.Converters;
using Avalonia.Layout;
using Avalonia.Media;
using PBLApp.Core.Localization;
using System;
using System.Globalization;

namespace PBLApp.ViewModels;

/// <summary>
/// Static converters used by ItemTooltipView. Lives next to the VM so the
/// XAML can reach them via x:Static.
/// </summary>
public static class TooltipKindConverter
{
    public static readonly IValueConverter IsSeparator = new KindEqualsConverter("separator");
    public static readonly IValueConverter IsText      = new KindEqualsConverter("text");
    public static readonly IValueConverter CenterOrLeft = new BoolToAlignmentConverter();
    public static readonly IValueConverter HexToBrush  = new HexBrushConverter();
    /// <summary>Maps PoB pixel sizes (14/18/22) to Avalonia DIPs at 0.85× (matches in-game look).</summary>
    public static readonly IValueConverter ScaleSize   = new ScaleSizeConverter(0.85);
    /// <summary>Bold for header sizes (≥ 20), Normal otherwise.</summary>
    public static readonly IValueConverter WeightForSize = new WeightForSizeConverter();
    /// <summary>Run an arbitrary English mod/stat line through GameTranslationService.TooltipLine.</summary>
    public static readonly IValueConverter TranslateLine = new TranslateLineConverter();

    /// <summary>Translate a rune / soul core / idol name via runes_ru.json.</summary>
    public static readonly IValueConverter TranslateRune = new TranslateRuneConverter();

    private sealed class TranslateLineConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
            => value is string s ? GameTranslationService.Instance.TooltipLine(s) : value;
        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    private sealed class TranslateRuneConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
            => value is string s ? GameTranslationService.Instance.Rune(s) : value;
        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    private sealed class KindEqualsConverter(string expected) : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
            => value is string s && string.Equals(s, expected, StringComparison.Ordinal);
        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    private sealed class BoolToAlignmentConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
            => value is bool b && b ? HorizontalAlignment.Center : HorizontalAlignment.Left;
        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    private sealed class ScaleSizeConverter(double factor) : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            double size = value switch
            {
                int i    => i,
                long l   => l,
                double d => d,
                _        => 14.0,
            };
            return Math.Max(8.0, size * factor);
        }
        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    private sealed class WeightForSizeConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            double size = value switch
            {
                int i    => i,
                long l   => l,
                double d => d,
                _        => 14.0,
            };
            return size >= 20 ? FontWeight.Bold : FontWeight.Normal;
        }
        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    private sealed class HexBrushConverter : IValueConverter
    {
        private static readonly IBrush Default = new SolidColorBrush(Color.Parse("#CDD6F4"));

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is not string hex || string.IsNullOrEmpty(hex)) return Default;
            try { return new SolidColorBrush(Color.Parse(hex)); }
            catch { return Default; }
        }
        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
