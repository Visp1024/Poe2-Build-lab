using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using PBLApp.ViewModels;

namespace PBLApp.Controls;

public static class InlinesHelper
{
    public static readonly AttachedProperty<IReadOnlyList<TooltipTextSegment>?> SegmentsProperty =
        AvaloniaProperty.RegisterAttached<TextBlock, IReadOnlyList<TooltipTextSegment>?>(
            "Segments", typeof(InlinesHelper));

    /// <summary>Variant that takes ItemTooltip's TooltipSegment record (Text + ColorHex).
    /// Same rendering path — splits the run list into colored Run inlines so TextWrapping
    /// works on the host TextBlock (a WrapPanel of TextBlocks can't word-wrap a long line).</summary>
    public static readonly AttachedProperty<IReadOnlyList<TooltipSegment>?> ItemSegmentsProperty =
        AvaloniaProperty.RegisterAttached<TextBlock, IReadOnlyList<TooltipSegment>?>(
            "ItemSegments", typeof(InlinesHelper));

    static InlinesHelper()
    {
        SegmentsProperty.Changed.AddClassHandler<TextBlock>(
            (tb, e) => Apply(tb, e.NewValue as IReadOnlyList<TooltipTextSegment>));
        ItemSegmentsProperty.Changed.AddClassHandler<TextBlock>(
            (tb, e) => ApplyItem(tb, e.NewValue as IReadOnlyList<TooltipSegment>));
    }

    public static void SetSegments(TextBlock tb, IReadOnlyList<TooltipTextSegment>? value)
        => tb.SetValue(SegmentsProperty, value);

    public static IReadOnlyList<TooltipTextSegment>? GetSegments(TextBlock tb)
        => tb.GetValue(SegmentsProperty);

    public static void SetItemSegments(TextBlock tb, IReadOnlyList<TooltipSegment>? value)
        => tb.SetValue(ItemSegmentsProperty, value);

    public static IReadOnlyList<TooltipSegment>? GetItemSegments(TextBlock tb)
        => tb.GetValue(ItemSegmentsProperty);

    private static void Apply(TextBlock tb, IReadOnlyList<TooltipTextSegment>? segs)
    {
        var inlines = tb.Inlines ??= new InlineCollection();
        inlines.Clear();
        if (segs == null) return;
        foreach (var s in segs)
        {
            var run = new Run(s.Text);
            if (!string.IsNullOrEmpty(s.Color) && Color.TryParse(s.Color, out var c))
                run.Foreground = new SolidColorBrush(c);
            inlines.Add(run);
        }
    }

    private static void ApplyItem(TextBlock tb, IReadOnlyList<TooltipSegment>? segs)
    {
        var inlines = tb.Inlines ??= new InlineCollection();
        inlines.Clear();
        if (segs == null) return;
        foreach (var s in segs)
        {
            var run = new Run(s.Text);
            if (!string.IsNullOrEmpty(s.ColorHex) && Color.TryParse(s.ColorHex, out var c))
                run.Foreground = new SolidColorBrush(c);
            inlines.Add(run);
        }
    }
}
