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

    static InlinesHelper()
    {
        SegmentsProperty.Changed.AddClassHandler<TextBlock>(
            (tb, e) => Apply(tb, e.NewValue as IReadOnlyList<TooltipTextSegment>));
    }

    public static void SetSegments(TextBlock tb, IReadOnlyList<TooltipTextSegment>? value)
        => tb.SetValue(SegmentsProperty, value);

    public static IReadOnlyList<TooltipTextSegment>? GetSegments(TextBlock tb)
        => tb.GetValue(SegmentsProperty);

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
}
