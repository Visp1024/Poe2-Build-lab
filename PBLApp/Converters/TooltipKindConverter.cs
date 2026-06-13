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
    public static readonly IValueConverter CenterOrLeftTextAlignment = new BoolToTextAlignmentConverter();
    public static readonly IValueConverter HexToBrush  = new HexBrushConverter();
    /// <summary>Maps PoB pixel sizes (14/18/22) to Avalonia DIPs at 1.0× (denser scale was hard to read).</summary>
    public static readonly IValueConverter ScaleSize   = new ScaleSizeConverter(1.0);
    /// <summary>Bold for header sizes (≥ 20), Normal otherwise.</summary>
    public static readonly IValueConverter WeightForSize = new WeightForSizeConverter();
    /// <summary>LineHeight = size × 1.4 — without this, larger title glyphs get clipped
    /// vertically by a too-tight default line box.</summary>
    public static readonly IValueConverter LineHeightForSize = new LineHeightForSizeConverter();
    /// <summary>Run an arbitrary English mod/stat line through GameTranslationService.TooltipLine.</summary>
    public static readonly IValueConverter TranslateLine = new TranslateLineConverter();

    /// <summary>Translate a rune / soul core / idol name via runes_ru.json.</summary>
    public static readonly IValueConverter TranslateRune = new TranslateRuneConverter();

    /// <summary>Maps a socketable-augment category (Rune/SoulCore/Idol/AbyssalEye/CongealedMist)
    /// to a short localized badge label.</summary>
    public static readonly IValueConverter RuneAugTypeLabel = new RuneAugTypeLabelConverter();

    /// <summary>Maps a socketable-augment category to a distinct badge colour.</summary>
    public static readonly IValueConverter RuneAugTypeBrush = new RuneAugTypeBrushConverter();

    private sealed class RuneAugTypeLabelConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            var key = (value as string) ?? "";
            return key switch
            {
                "Rune"          => LocalizationService.Get("Rune_Aug_Rune"),
                "SoulCore"      => LocalizationService.Get("Rune_Aug_SoulCore"),
                "Idol"          => LocalizationService.Get("Rune_Aug_Idol"),
                "AbyssalEye"    => LocalizationService.Get("Rune_Aug_AbyssalEye"),
                "CongealedMist" => LocalizationService.Get("Rune_Aug_CongealedMist"),
                _               => "",
            };
        }
        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    private sealed class RuneAugTypeBrushConverter : IValueConverter
    {
        // PoB-flavoured: idols violet, soul cores teal, abyssal eyes crimson, mist blue, runes neutral.
        private static readonly IBrush Idol    = new SolidColorBrush(Color.Parse("#C792EA"));
        private static readonly IBrush SoulCore = new SolidColorBrush(Color.Parse("#56C2C0"));
        private static readonly IBrush Abyss   = new SolidColorBrush(Color.Parse("#E06C9F"));
        private static readonly IBrush Mist    = new SolidColorBrush(Color.Parse("#7AA2F7"));
        private static readonly IBrush Rune    = new SolidColorBrush(Color.Parse("#9AA4B2"));

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
            => (value as string) switch
            {
                "Idol"          => Idol,
                "SoulCore"      => SoulCore,
                "AbyssalEye"    => Abyss,
                "CongealedMist" => Mist,
                _               => Rune,
            };
        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

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

    /// <summary>Translate a rune name, or show an em-dash for the empty "none" sentinel.</summary>
    public static readonly IValueConverter RuneNameOrDash = new RuneNameOrDashConverter();

    private sealed class RuneNameOrDashConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
            => value is string s && s.Length > 0 ? GameTranslationService.Instance.Rune(s) : "—";
        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>Show a string as-is, or an em-dash when empty (e.g. a "none" sentinel).</summary>
    public static readonly IValueConverter StringOrDash = new StringOrDashConverter();

    private sealed class StringOrDashConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
            => value is string s && s.Length > 0 ? s : "—";
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

    private sealed class BoolToTextAlignmentConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
            => value is bool b && b ? TextAlignment.Center : TextAlignment.Left;
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

    private sealed class LineHeightForSizeConverter : IValueConverter
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
            return Math.Max(16.0, size * 1.4);
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

        private static readonly IBrush Transparent = Brushes.Transparent;

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is not string hex || string.IsNullOrEmpty(hex)) return Default;
            if (string.Equals(hex, "Transparent", StringComparison.OrdinalIgnoreCase)) return Transparent;
            try { return new SolidColorBrush(Color.Parse(hex)); }
            catch { return Default; }
        }
        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
