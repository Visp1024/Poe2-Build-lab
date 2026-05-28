using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;

namespace PBLApp.ViewModels;

/// <summary>
/// Converts an absolute filesystem path to a cached <see cref="Bitmap"/> for
/// binding to <c>Image.Source</c>. Returns null if the value is null/empty or
/// the file is missing — callers are expected to hide the Image via IsVisible.
///
/// A single process-wide cache keeps decoded bitmaps alive so repeated slot
/// repaints (rebinds, scrolling the pool) don't re-decode the same .webp.
/// </summary>
public sealed class PathToBitmapConverter : IValueConverter
{
    public static readonly PathToBitmapConverter Instance = new();

    private static readonly ConcurrentDictionary<string, Bitmap?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrEmpty(path)) return null;
        return Cache.GetOrAdd(path, static p =>
        {
            try { return File.Exists(p) ? new Bitmap(p) : null; }
            catch { return null; }
        });
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
