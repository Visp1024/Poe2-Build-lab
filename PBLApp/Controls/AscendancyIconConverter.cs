using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;

namespace PBLApp.Controls;

/// <summary>
/// Multi-binding converter that resolves a (className, ascendClassName) pair into
/// an Avalonia bitmap cropped from the tree sprite-sheet.
///
/// Strategy:
///   1. Prefer the ascendancy sprite "Classes{ascendClassName}".
///   2. Fall back to the base class sprite "Classes{className}".
///   3. Return null if neither is available — XAML shows a placeholder in that case.
///
/// Each unique sprite name is cropped once into a fresh <see cref="RenderTargetBitmap"/>
/// and cached for the lifetime of the process.
/// </summary>
public sealed class AscendancyIconConverter : IMultiValueConverter
{
    public static readonly AscendancyIconConverter Instance = new();

    private const int IconSize = 256;
    private static readonly ConcurrentDictionary<string, Bitmap?> _cache = new();

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count < 2) return null;
        var className       = values[0] as string ?? "";
        var ascendClassName = values[1] as string ?? "";

        // Prefer ascendancy → fall back to class.
        return Resolve(ascendClassName) ?? Resolve(className);
    }

    private static Bitmap? Resolve(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        return _cache.GetOrAdd("Classes" + name, key =>
        {
            var store = TreeAssetStore.Default;
            if (store?.GetSprite(key) is not { } sprite) return null;

            try
            {
                var rtb = new RenderTargetBitmap(new PixelSize(IconSize, IconSize), new Vector(96, 96));
                using var ctx = rtb.CreateDrawingContext();
                ctx.DrawImage(
                    sprite.Bmp,
                    sourceRect: sprite.Src,
                    destRect:   new Rect(0, 0, IconSize, IconSize));
                return rtb;
            }
            catch
            {
                return null;
            }
        });
    }
}
