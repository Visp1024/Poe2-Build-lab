using System;
using Avalonia;
using Avalonia.Controls;

namespace PBLApp.Controls;

/// <summary>
/// Column-based masonry layout: lays children out into a fixed number of equal-width
/// columns, always appending the next child to the currently-shortest column so cards
/// of varying height pack tightly and fill the available space. Used by the Calcs tab's
/// compact density mode (where cards are intentionally detached from strict domain
/// columns and just flow to fill space).
/// </summary>
public class MasonryPanel : Panel
{
    public static readonly StyledProperty<int> ColumnCountProperty =
        AvaloniaProperty.Register<MasonryPanel, int>(nameof(ColumnCount), 3);

    public static readonly StyledProperty<double> ColumnSpacingProperty =
        AvaloniaProperty.Register<MasonryPanel, double>(nameof(ColumnSpacing), 8);

    public static readonly StyledProperty<double> RowSpacingProperty =
        AvaloniaProperty.Register<MasonryPanel, double>(nameof(RowSpacing), 8);

    static MasonryPanel()
    {
        AffectsMeasure<MasonryPanel>(ColumnCountProperty, ColumnSpacingProperty, RowSpacingProperty);
        AffectsArrange<MasonryPanel>(ColumnCountProperty, ColumnSpacingProperty, RowSpacingProperty);
    }

    public int ColumnCount
    {
        get => GetValue(ColumnCountProperty);
        set => SetValue(ColumnCountProperty, value);
    }

    public double ColumnSpacing
    {
        get => GetValue(ColumnSpacingProperty);
        set => SetValue(ColumnSpacingProperty, value);
    }

    public double RowSpacing
    {
        get => GetValue(RowSpacingProperty);
        set => SetValue(RowSpacingProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        int cols = Math.Max(1, ColumnCount);
        double colWidth = ColumnWidth(availableSize.Width, cols);
        var heights = new double[cols];

        foreach (var child in Children)
        {
            if (!child.IsVisible) continue;
            child.Measure(new Size(colWidth, double.PositiveInfinity));
            int c = ShortestColumn(heights);
            heights[c] += child.DesiredSize.Height + RowSpacing;
        }

        double maxH = 0;
        foreach (var h in heights) maxH = Math.Max(maxH, h);
        if (maxH > 0) maxH -= RowSpacing; // no trailing gap

        double width = double.IsInfinity(availableSize.Width) ? colWidth * cols + ColumnSpacing * (cols - 1) : availableSize.Width;
        return new Size(width, maxH);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        int cols = Math.Max(1, ColumnCount);
        double colWidth = ColumnWidth(finalSize.Width, cols);
        var heights = new double[cols];

        foreach (var child in Children)
        {
            if (!child.IsVisible) continue;
            int c = ShortestColumn(heights);
            double x = c * (colWidth + ColumnSpacing);
            double y = heights[c];
            child.Arrange(new Rect(x, y, colWidth, child.DesiredSize.Height));
            heights[c] += child.DesiredSize.Height + RowSpacing;
        }

        return finalSize;
    }

    private double ColumnWidth(double available, int cols)
    {
        if (double.IsInfinity(available) || available <= 0) return 240; // sane fallback when unconstrained
        return Math.Max(1, (available - ColumnSpacing * (cols - 1)) / cols);
    }

    private static int ShortestColumn(double[] heights)
    {
        int best = 0;
        for (int i = 1; i < heights.Length; i++)
            if (heights[i] < heights[best]) best = i;
        return best;
    }
}
