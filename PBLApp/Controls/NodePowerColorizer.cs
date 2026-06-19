using System;
using Avalonia.Media;
using PBLEngine;

namespace PBLApp.Controls;

/// <summary>Pure colour math for the tree heat map, ported from PoB's
/// <c>PassiveTreeView</c>: brightness = (max(power,0)/powerMax * 1.5) ^ 0.5,
/// mapped to the RED/BLUE theme (blue = strong contribution, red = weak/negative).
/// Off/Def mode blends an offence (blue) and defence (green) channel.</summary>
public static class NodePowerColorizer
{
    private static double Curve(double power, double max) =>
        max <= 0 ? 0 : Math.Min(1.0, Math.Pow(Math.Max(power, 0) / max * 1.5, 0.5));

    private static double Ratio(double power, double max) =>
        max <= 0 ? 0 : Math.Min(1.0, Math.Max(power, 0) / max);

    /// <summary>Raw power as a fraction [0,1] of the strongest node's power (the
    /// stronger channel in off/def mode). Used to declutter the zoomed-out view: a
    /// node survives only if it has at least a threshold fraction of the max power,
    /// so the heat map's meaningful nodes stand out instead of a field of dim circles.
    /// Uses the RAW ratio (not the ^0.5 colour curve, which inflates small values).</summary>
    public static double Brightness(NodePowerEntry e, NodePowerMax max, bool offDef) =>
        offDef ? Math.Max(Ratio(e.Offence, max.Offence), Ratio(e.Defence, max.Defence))
               : Ratio(e.Power, max.SingleStat);

    /// <summary>Tint for a node, or null when it contributes nothing (leave default).</summary>
    public static Color? ColorFor(NodePowerEntry e, NodePowerMax max, bool offDef)
    {
        if (offDef)
        {
            double off = Curve(e.Offence, max.Offence);
            double def = Curve(e.Defence, max.Defence);
            if (off <= 0 && def <= 0) return null;
            // offence → blue, defence → green (RED/BLUE-style, with green for defence)
            byte b = (byte)(60 + off * 195);
            byte g = (byte)(40 + def * 180);
            return Color.FromArgb(220, 30, g, b);
        }

        double t = Curve(e.Power, max.SingleStat);
        if (t <= 0) return null;
        // RED/BLUE: low = dim red, high = bright blue.
        byte rr = (byte)(180 * (1 - t) + 30);
        byte bb = (byte)(80 + t * 175);
        return Color.FromArgb(220, rr, 40, bb);
    }
}
