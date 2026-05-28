using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using PBLApp.ViewModels;

namespace PBLApp.Controls;

/// <summary>
/// Attached property that turns a plain multi-line tooltip string into a styled
/// Avalonia ToolTip with the first line emphasised as a title and subsequent
/// mod-line numbers highlighted in a contrasting color.
///
/// Usage: <c>ctl:StyledHoverTooltip.Text="{Binding HoverTooltipText}"
///         ctl:StyledHoverTooltip.TitleColor="{Binding NameColor}"</c>
///
/// Centralises the styling so we don't repeat the same ToolTip.Tip ContentTemplate
/// for every one of the 17+ slot cells and the pool entries.
/// </summary>
public static class StyledHoverTooltip
{
    public static readonly AttachedProperty<HoverTooltipModel?> SourceProperty =
        AvaloniaProperty.RegisterAttached<Control, HoverTooltipModel?>("Source", typeof(StyledHoverTooltip));

    public static void SetSource(Control c, HoverTooltipModel? v) => c.SetValue(SourceProperty, v);
    public static HoverTooltipModel? GetSource(Control c) => c.GetValue(SourceProperty);

    static StyledHoverTooltip()
    {
        SourceProperty.Changed.AddClassHandler<Control>((c, _) => Apply(c));
    }

    private static readonly Regex NumberRx = new(@"[+\-]?\d+(?:[.,]\d+)?%?", RegexOptions.Compiled);
    private const string NumberColor = "#F9E2AF";   // warm gold
    private const string LabelColor  = "#8888FF";   // cornflower (mod blue)
    private const string MutedColor  = "#7F7F7F";   // ilvl tag
    private const string DefaultTitleColor = "#CDD6F4";

    private static void Apply(Control owner)
    {
        var src = GetSource(owner);
        if (src is null || string.IsNullOrEmpty(src.Text))
        {
            ToolTip.SetTip(owner, null);
            return;
        }

        var text = src.Text;
        var titleColor = string.IsNullOrEmpty(src.TitleColor) ? DefaultTitleColor : src.TitleColor;
        var lines = text.Split('\n');
        var panel = new StackPanel { Spacing = 3 };

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.Length == 0) continue;
            if (i == 0)
                panel.Children.Add(BuildTitle(line, titleColor));
            else
                panel.Children.Add(BuildModLine(line));
        }

        var border = new Border
        {
            Background       = (IBrush?)Avalonia.Application.Current?.Resources["BgMantleBrush"] ?? new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x2E)),
            BorderBrush      = new SolidColorBrush(Color.Parse(titleColor)),
            BorderThickness  = new Thickness(1),
            CornerRadius     = new CornerRadius(3),
            Padding          = new Thickness(12, 9),
            Child            = panel,
            MaxWidth         = 480,
        };

        ToolTip.SetTip(owner, border);
    }

    private static TextBlock BuildTitle(string line, string titleColor)
    {
        var tb = new TextBlock
        {
            FontFamily   = new FontFamily("Inter, Segoe UI, Arial"),
            FontSize     = 15,
            FontWeight   = FontWeight.SemiBold,
            LineHeight   = 21,
            TextWrapping = TextWrapping.Wrap,
        };

        // Split off the ilvl tag ("Name  (ilvl 56)") so it can be muted.
        int ilvlIdx = line.IndexOf("(ilvl ", System.StringComparison.Ordinal);
        if (ilvlIdx > 0)
        {
            tb.Inlines!.Add(new Run(line[..ilvlIdx].TrimEnd())
            {
                Foreground = new SolidColorBrush(Color.Parse(titleColor)),
            });
            tb.Inlines.Add(new Run("  " + line[ilvlIdx..])
            {
                Foreground = new SolidColorBrush(Color.Parse(MutedColor)),
                FontSize = 12,
            });
        }
        else
        {
            tb.Inlines!.Add(new Run(line) { Foreground = new SolidColorBrush(Color.Parse(titleColor)) });
        }
        return tb;
    }

    private static TextBlock BuildModLine(string line)
    {
        var tb = new TextBlock
        {
            FontFamily   = new FontFamily("Inter, Segoe UI, Arial"),
            FontSize     = 13,
            LineHeight   = 18,
            TextWrapping = TextWrapping.Wrap,
        };

        var label = new SolidColorBrush(Color.Parse(LabelColor));
        var num   = new SolidColorBrush(Color.Parse(NumberColor));

        int last = 0;
        foreach (Match m in NumberRx.Matches(line))
        {
            if (m.Index > last)
                tb.Inlines!.Add(new Run(line[last..m.Index]) { Foreground = label });
            tb.Inlines!.Add(new Run(m.Value) { Foreground = num });
            last = m.Index + m.Length;
        }
        if (last < line.Length)
            tb.Inlines!.Add(new Run(line[last..]) { Foreground = label });
        if (tb.Inlines!.Count == 0)
            tb.Inlines.Add(new Run(line) { Foreground = label });
        return tb;
    }
}
