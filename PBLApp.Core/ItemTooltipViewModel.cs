using CommunityToolkit.Mvvm.ComponentModel;
using PBLApp.Core.Localization;
using PBLEngine;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using System.Text.RegularExpressions;

namespace PBLApp.ViewModels;

/// <summary>
/// One coloured run inside a tooltip line. Text is plain (no ^x escapes).
/// </summary>
public sealed record TooltipSegment(string Text, string ColorHex);

/// <summary>
/// One rendered tooltip line — pre-segmented for XAML.
/// </summary>
public sealed class TooltipLineVm
{
    public string Kind { get; init; } = "text";   // "text" | "separator"
    public int Size { get; init; } = 14;
    public bool Centered { get; init; }
    public int Block { get; init; } = 1;
    /// <summary>Plain text without colour codes — used by hover-tooltip ToolTip strings.</summary>
    public string PlainText { get; init; } = "";
    public IReadOnlyList<TooltipSegment> Segments { get; init; } = Array.Empty<TooltipSegment>();
}

/// <summary>
/// View model for the styled PoB-like item tooltip. Built from
/// <see cref="LuaHost.GetItemTooltipLines"/> output; segments are parsed
/// from inline ^x&lt;hex&gt; / ^&lt;digit&gt; colour codes.
/// </summary>
public partial class ItemTooltipViewModel : ViewModelBase
{
    public ObservableCollection<TooltipLineVm> Lines { get; } = new();

    /// <summary>
    /// True when the tooltip is empty (no item to show). Use to hide the view.
    /// </summary>
    [ObservableProperty]
    private bool _isEmpty = true;

    public void Load(IEnumerable<ItemTooltipLine> rawLines)
    {
        Lines.Clear();
        foreach (var line in rawLines)
        {
            if (line.Kind == "separator")
            {
                Lines.Add(new TooltipLineVm
                {
                    Kind = "separator",
                    Size = line.Size,
                    Block = line.Block,
                });
            }
            else
            {
                var segments = ParseSegments(line.Text);
                var plain    = StripColors(line.Text);
                var translated = GameTranslationService.Instance.TooltipLine(plain);
                // If the translator gave us a different string, replace the segments with
                // a single segment carrying the translation in the dominant colour. This
                // loses inline value-vs-label colouring (e.g. white "37" inside grey
                // "Evasion Rating: 37") but keeps a clean Russian read-out.
                if (!ReferenceEquals(translated, plain) && translated != plain)
                {
                    var color = segments.Count > 0 ? segments[0].ColorHex : "#CDD6F4";
                    segments = HighlightNumbers(translated, color);
                    plain = translated;
                }
                else if (segments.Count == 1)
                {
                    // Untranslated single-segment line — still highlight numbers
                    // (e.g. flavour, English fallbacks).
                    segments = HighlightNumbers(segments[0].Text, segments[0].ColorHex);
                }
                Lines.Add(new TooltipLineVm
                {
                    Kind = "text",
                    Size = line.Size,
                    Centered = line.Centered,
                    Block = line.Block,
                    PlainText = plain,
                    Segments = segments,
                });
            }
        }
        IsEmpty = Lines.Count == 0;
    }

    public void Clear()
    {
        Lines.Clear();
        IsEmpty = true;
    }

    // ── Number highlighting ────────────────────────────────────────────────

    // Numbers (and percent signs) are highlighted with a contrasting colour
    // so the eye picks rolled values out of the surrounding label.
    private static readonly Regex NumberRx = new(@"[+\-]?\d+(?:\.\d+)?%?", RegexOptions.Compiled);

    /// <summary>Choose a number-highlight colour given the line's base colour.</summary>
    private static string NumberColor(string baseColor) => baseColor.ToUpperInvariant() switch
    {
        "#8888FF" => "#F9E2AF",   // mod cornflowerblue → warm gold
        "#7F7F7F" => "#FFFFFF",   // grey label        → white
        "#CDD6F4" => "#F9E2AF",   // default           → warm gold
        "#DD0022" => "#DD0022",   // delta negative — keep red (already eye-catching)
        "#33FF77" => "#33FF77",   // delta positive — keep green
        _         => baseColor,
    };

    private static IReadOnlyList<TooltipSegment> HighlightNumbers(string text, string baseColor)
    {
        var hl = NumberColor(baseColor);
        if (hl == baseColor || string.IsNullOrEmpty(text))
            return new[] { new TooltipSegment(text, baseColor) };

        var result = new List<TooltipSegment>();
        int last = 0;
        foreach (Match m in NumberRx.Matches(text))
        {
            if (m.Index > last)
                result.Add(new TooltipSegment(text[last..m.Index], baseColor));
            result.Add(new TooltipSegment(m.Value, hl));
            last = m.Index + m.Length;
        }
        if (last < text.Length)
            result.Add(new TooltipSegment(text[last..], baseColor));
        if (result.Count == 0)
            result.Add(new TooltipSegment(text, baseColor));
        return result;
    }

    // ── Colour-code parsing ────────────────────────────────────────────────

    // Default tooltip text colour (PoB uses { 0.5, 0.3, 0 } brown; we render
    // "uncoloured" runs as a warm beige that reads well on dark backgrounds).
    private const string DefaultColor = "#CDD6F4";

    private static readonly Dictionary<char, string> SingleDigitColors = new()
    {
        ['0'] = "#000000",
        ['1'] = "#FF0000",
        ['2'] = "#00FF00",
        ['3'] = "#5050FF",  // blue tweaked: pure blue is too dark
        ['4'] = "#FF00FF",
        ['5'] = "#00FFFF",
        ['6'] = "#FFFF00",
        ['7'] = "#FFFFFF",
        ['8'] = "#808080",
        ['9'] = "#C0C0C0",
    };

    /// <summary>Splits a raw tooltip line into (color, text) segments.</summary>
    public static IReadOnlyList<TooltipSegment> ParseSegments(string raw)
    {
        if (string.IsNullOrEmpty(raw))
            return Array.Empty<TooltipSegment>();

        var result = new List<TooltipSegment>();
        var color = DefaultColor;
        var buf = new StringBuilder();
        int i = 0;
        while (i < raw.Length)
        {
            if (raw[i] == '^' && i + 1 < raw.Length)
            {
                var next = raw[i + 1];
                // ^xRRGGBB — explicit hex
                if ((next == 'x' || next == 'X') && i + 7 < raw.Length + 1
                    && i + 8 <= raw.Length && IsHex(raw, i + 2, 6))
                {
                    Flush(result, buf, color);
                    color = "#" + raw.Substring(i + 2, 6).ToUpperInvariant();
                    i += 8;
                    continue;
                }
                // ^<digit> — palette index
                if (SingleDigitColors.TryGetValue(next, out var palette))
                {
                    Flush(result, buf, color);
                    color = palette;
                    i += 2;
                    continue;
                }
            }
            buf.Append(raw[i]);
            i++;
        }
        Flush(result, buf, color);
        return result;
    }

    private static void Flush(List<TooltipSegment> result, StringBuilder buf, string color)
    {
        if (buf.Length == 0) return;
        result.Add(new TooltipSegment(buf.ToString(), color));
        buf.Clear();
    }

    private static bool IsHex(string s, int start, int len)
    {
        if (start + len > s.Length) return false;
        for (int k = 0; k < len; k++)
        {
            var c = s[start + k];
            if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')))
                return false;
        }
        return true;
    }

    public static string StripColors(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return "";
        var sb = new StringBuilder(raw.Length);
        int i = 0;
        while (i < raw.Length)
        {
            if (raw[i] == '^' && i + 1 < raw.Length)
            {
                var next = raw[i + 1];
                if ((next == 'x' || next == 'X') && i + 8 <= raw.Length && IsHex(raw, i + 2, 6))
                {
                    i += 8;
                    continue;
                }
                if (SingleDigitColors.ContainsKey(next))
                {
                    i += 2;
                    continue;
                }
            }
            sb.Append(raw[i]);
            i++;
        }
        return sb.ToString();
    }
}
