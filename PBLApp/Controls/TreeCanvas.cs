using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using PBLApp.Core.Localization;
using PBLEngine;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Input;
using RadiusEmitter = (int NodeId, double RadiusWorld);

namespace PBLApp.Controls;

/// <summary>
/// Pan/zoom canvas that renders passive skill tree nodes and connections.
/// Left-drag to pan, scroll wheel to zoom, click to toggle node allocation.
/// </summary>
public sealed class TreeCanvas : Control
{
    // ── Styled properties ──────────────────────────────────────────────────

    public static readonly StyledProperty<IReadOnlyList<TreeNodeDto>?> NodesProperty =
        AvaloniaProperty.Register<TreeCanvas, IReadOnlyList<TreeNodeDto>?>(nameof(Nodes));

    public static readonly StyledProperty<IReadOnlySet<int>?> AllocatedIdsProperty =
        AvaloniaProperty.Register<TreeCanvas, IReadOnlySet<int>?>(nameof(AllocatedIds));

    public static readonly StyledProperty<string> SearchTextProperty =
        AvaloniaProperty.Register<TreeCanvas, string>(nameof(SearchText), "");

    public static readonly StyledProperty<string> AscendancyFilterProperty =
        AvaloniaProperty.Register<TreeCanvas, string>(nameof(AscendancyFilter), "");

    public static readonly StyledProperty<ICommand?> AllocNodeCommandProperty =
        AvaloniaProperty.Register<TreeCanvas, ICommand?>(nameof(AllocNodeCommand));

    public static readonly StyledProperty<ICommand?> DeallocNodeCommandProperty =
        AvaloniaProperty.Register<TreeCanvas, ICommand?>(nameof(DeallocNodeCommand));

    public static readonly StyledProperty<TreeAssetStore?> AssetStoreProperty =
        AvaloniaProperty.Register<TreeCanvas, TreeAssetStore?>(nameof(AssetStore));

    public static readonly StyledProperty<IReadOnlyList<RadiusEmitter>?> RadiusEmittersProperty =
        AvaloniaProperty.Register<TreeCanvas, IReadOnlyList<RadiusEmitter>?>(nameof(RadiusEmitters));

    public static readonly StyledProperty<IReadOnlyDictionary<string, (double X, double Y)>?> AscendancyBackgroundsProperty =
        AvaloniaProperty.Register<TreeCanvas, IReadOnlyDictionary<string, (double X, double Y)>?>(nameof(AscendancyBackgrounds));

    public IReadOnlyList<TreeNodeDto>? Nodes
    {
        get => GetValue(NodesProperty);
        set => SetValue(NodesProperty, value);
    }
    public IReadOnlySet<int>? AllocatedIds
    {
        get => GetValue(AllocatedIdsProperty);
        set => SetValue(AllocatedIdsProperty, value);
    }
    public string SearchText
    {
        get => GetValue(SearchTextProperty);
        set => SetValue(SearchTextProperty, value);
    }
    public string AscendancyFilter
    {
        get => GetValue(AscendancyFilterProperty);
        set => SetValue(AscendancyFilterProperty, value);
    }
    public ICommand? AllocNodeCommand
    {
        get => GetValue(AllocNodeCommandProperty);
        set => SetValue(AllocNodeCommandProperty, value);
    }

    public ICommand? DeallocNodeCommand
    {
        get => GetValue(DeallocNodeCommandProperty);
        set => SetValue(DeallocNodeCommandProperty, value);
    }
    public TreeAssetStore? AssetStore
    {
        get => GetValue(AssetStoreProperty);
        set => SetValue(AssetStoreProperty, value);
    }
    public IReadOnlyList<RadiusEmitter>? RadiusEmitters
    {
        get => GetValue(RadiusEmittersProperty);
        set => SetValue(RadiusEmittersProperty, value);
    }
    public IReadOnlyDictionary<string, (double X, double Y)>? AscendancyBackgrounds
    {
        get => GetValue(AscendancyBackgroundsProperty);
        set => SetValue(AscendancyBackgroundsProperty, value);
    }

    // ── Pan / zoom state ───────────────────────────────────────────────────

    private double _offsetX, _offsetY;
    private double _scale = 0.12;
    private bool   _isPanning;
    private Point  _panStart;
    private Point  _pressStart;          // to distinguish click from drag
    private bool   _isRightPress;        // true when press was RMB (no panning)
    private bool   _fitNeeded = true;

    // ── Hover / lookup ─────────────────────────────────────────────────────

    private TreeNodeDto? _hoveredNode;
    private Dictionary<int, TreeNodeDto>             _nodeById      = new();
    private Dictionary<string, (double dx, double dy)> _ascendOffsets = new();
    private Dictionary<string, double>              _ascendRadii   = new();

    // Cache for the "can allocate" set (nodes adjacent to any allocated node).
    // Recomputing this iterates every node × its neighbours — ~10-30 ms on the
    // full tree. We only need a refresh when AllocatedIds or Nodes change, not
    // on every pan/zoom/hover.
    private HashSet<int>? _canAllocCache;
    private object?       _canAllocAllocRef;
    private object?       _canAllocNodesRef;

    // ── Layered rendering: static connection bitmap ───────────────────────
    // Connections form ~5000 anti-aliased lines that don't change unless the
    // node set or ascendancy filter does. Bake them once into an off-screen
    // bitmap at a fixed reference scale; on each Render, draw that bitmap
    // transformed by the current pan/zoom. Pan becomes a translate (≈ 0 cost),
    // zoom is a fast bitmap rescale, and only the alloc/can-alloc connection
    // overlays + nodes redraw per frame. Re-bake when scale drifts past the
    // resolution headroom (current bake gets blurry beyond ~1.5×).
    private Avalonia.Media.Imaging.RenderTargetBitmap? _staticLayer;
    private double _staticLayerScale;   // reference scale used during bake
    private double _staticMinX, _staticMinY; // world-space top-left of the bitmap
    private object? _staticNodesRef;
    private string  _staticFilter = "<unset>";

    // ── Brushes & pens (static) ────────────────────────────────────────────

    private static readonly IBrush BgBrush = new SolidColorBrush(Color.Parse("#11111B"));

    // Node fill: unalloc / alloc
    private static readonly IBrush NrmFill   = new SolidColorBrush(Color.Parse("#252537"));
    private static readonly IBrush NrmAlloc  = new SolidColorBrush(Color.Parse("#2A4A3A"));
    private static readonly IBrush NotFill   = new SolidColorBrush(Color.Parse("#1E2040"));
    private static readonly IBrush NotAlloc  = new SolidColorBrush(Color.Parse("#1B3060"));
    private static readonly IBrush KsFill    = new SolidColorBrush(Color.Parse("#2B2500"));
    private static readonly IBrush KsAlloc   = new SolidColorBrush(Color.Parse("#4A3800"));
    private static readonly IBrush SockFill  = new SolidColorBrush(Color.Parse("#1A1A2E"));
    private static readonly IBrush AscFill   = new SolidColorBrush(Color.Parse("#1C1A40"));
    private static readonly IBrush AscAlloc  = new SolidColorBrush(Color.Parse("#30205A"));
    private static readonly IBrush StartFill = new SolidColorBrush(Color.Parse("#0D1A2A"));

    // Node border: unalloc / alloc
    private static readonly IPen NrmRingU   = MkPen("#45475A", 1.0);
    private static readonly IPen NrmRingA   = MkPen("#A6E3A1", 1.5);
    private static readonly IPen NotRingU   = MkPen("#585B70", 1.5);
    private static readonly IPen NotRingA   = MkPen("#89B4FA", 2.0);
    private static readonly IPen KsRingU    = MkPen("#6C7086", 1.5);
    private static readonly IPen KsRingA    = MkPen("#F9E2AF", 2.0);
    private static readonly IPen SockRingU  = MkPen("#585B70", 1.5);
    private static readonly IPen SockRingA  = MkPen("#F38BA8", 2.0);
    private static readonly IPen AscRingU   = MkPen("#7287FD", 1.0);
    private static readonly IPen AscRingA   = MkPen("#B4BEFE", 2.0);
    private static readonly IPen StartRing  = MkPen("#CDD6F4", 2.0);
    private static readonly IPen MastRing   = MkPen("#CBA6F7", 1.0);

    // Connections
    private static readonly IPen ConnPen        = MkPen("#27273E", 1.5);
    private static readonly IPen ConnAllocPen   = MkPen("#4A6FA0", 2.5);
    private static readonly IPen CanAllocPen    = MkPen("#444460", 1.8);

    // Overlays
    private static readonly IPen SearchPen  = MkPen("#F9E2AF", 2.0);
    private static readonly IPen HoverPen   = MkPen("#FAB387", 2.0);
    private static readonly IPen CanAllocNodePen = MkPen("#5A5A80", 1.2);

    // Unallocated node dimming overlay (60 % black)
    private static readonly IBrush DimBrush = new SolidColorBrush(Color.FromArgb(153, 0, 0, 0));

    // Glow (semi-transparent)
    private static readonly IBrush GlowA    = new SolidColorBrush(Color.FromArgb(35, 166, 227, 161));
    private static readonly IBrush GlowNotA = new SolidColorBrush(Color.FromArgb(35, 137, 180, 250));
    private static readonly IBrush GlowKsA  = new SolidColorBrush(Color.FromArgb(60, 249, 226, 175));
    // Keystone extra glow layers
    private static readonly IBrush KsGlow1  = new SolidColorBrush(Color.FromArgb(45, 249, 226, 175));
    private static readonly IBrush KsGlow2  = new SolidColorBrush(Color.FromArgb(25, 249, 226, 175));

    // Jewel socket hover radius rings: Small / Medium / Large / Very Large
    private static readonly IPen RadSmall  = MkPen("#BB6600", 1.2);
    private static readonly IPen RadMedium = MkPen("#66FFCC", 1.2);
    private static readonly IPen RadLarge  = MkPen("#2222CC", 1.2);
    private static readonly IPen RadVLarge = MkPen("#C100FF", 1.2);

    // "Can allocate without connection" radius (Entwined Realities / similar)
    private static readonly IBrush RadiusAllocFill = new SolidColorBrush(Color.FromArgb(18, 180, 130, 255));
    private static readonly IPen   RadiusAllocPen  = MkPen("#B482FF", 1.8);

    // Info panel
    private static readonly IBrush InfoBg   = new SolidColorBrush(Color.FromArgb(230, 17, 17, 27));
    private static readonly IPen   InfoBrd  = MkPen("#313244", 1.0);

    private static IPen MkPen(string hex, double thick) =>
        new Pen(new SolidColorBrush(Color.Parse(hex)), thick);

    // ── Overrides ─────────────────────────────────────────────────────────

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == NodesProperty)
        {
            var oldCount = change.GetOldValue<IReadOnlyList<TreeNodeDto>?>()?.Count ?? 0;
            var newNodes = change.GetNewValue<IReadOnlyList<TreeNodeDto>?>();
            _nodeById = newNodes?.ToDictionary(n => n.Id) ?? [];
            ComputeAscendOffsets();
            // Reset view only when loading a genuinely different tree (node count changes).
            // Data-only refreshes (attribute switch, same tree) keep count identical → no zoom reset.
            if ((newNodes?.Count ?? 0) != oldCount)
                _fitNeeded = true;
            _hoveredNode = null;
            InvalidateVisual();
        }
        else if (change.Property == AllocatedIdsProperty)
        {
            InvalidateVisual();
        }
        else if (change.Property == SearchTextProperty)
        {
            InvalidateVisual();
        }
        else if (change.Property == AscendancyFilterProperty)
        {
            InvalidateVisual();
        }
        else if (change.Property == AssetStoreProperty)
        {
            InvalidateVisual();
        }
        else if (change.Property == RadiusEmittersProperty)
        {
            InvalidateVisual();
        }
        else if (change.Property == AscendancyBackgroundsProperty)
        {
            InvalidateVisual();
        }

        // Static connection bitmap depends on Nodes + AscendancyFilter only
        if (change.Property == NodesProperty || change.Property == AscendancyFilterProperty)
            InvalidateStaticLayer();
    }

    private void InvalidateStaticLayer()
    {
        _staticLayer?.Dispose();
        _staticLayer = null;
    }

    public override void Render(DrawingContext dc)
    {
        dc.FillRectangle(BgBrush, new Rect(Bounds.Size));

        var nodes = Nodes;
        if (nodes == null || nodes.Count == 0) return;

        if (_fitNeeded && Bounds.Width > 0 && Bounds.Height > 0)
        {
            FitToView();
            _fitNeeded = false;
        }

        var alloc  = AllocatedIds;
        var search = (SearchText ?? "").Trim();
        var filter = AscendancyFilter;

        // Precompute "can allocate" set, but only when alloc/nodes ref changes.
        // Pan/zoom/hover repaints reuse the cached HashSet (saves 10-30 ms).
        HashSet<int>? canAlloc = null;
        if (alloc != null && alloc.Count > 0)
        {
            if (!ReferenceEquals(alloc, _canAllocAllocRef) ||
                !ReferenceEquals(nodes, _canAllocNodesRef) ||
                _canAllocCache is null)
            {
                _canAllocCache = new HashSet<int>();
                foreach (var node in nodes)
                {
                    if (alloc.Contains(node.Id)) continue;
                    foreach (var lid in node.LinkedIds)
                        if (alloc.Contains(lid)) { _canAllocCache.Add(node.Id); break; }
                }
                _canAllocAllocRef = alloc;
                _canAllocNodesRef = nodes;
            }
            canAlloc = _canAllocCache;
        }
        else
        {
            _canAllocCache = null;
            _canAllocAllocRef = null;
        }

        // ── Ascendancy background image ────────────────────────────────────
        if (!string.IsNullOrEmpty(filter))
            DrawAscendancyBackground(dc, filter);

        // ── Static connection layer (cached bitmap) ────────────────────────
        // Draw the ~5000 baseline (ConnPen) lines as a single textured rect.
        // Pan is free, zoom is one bilinear filter. Alloc / can-alloc edges
        // get drawn on top with heavier pens that fully cover the cached line
        // so colour transitions look right.
        EnsureStaticLayer(filter);
        if (_staticLayer != null)
        {
            var ratio = _scale / _staticLayerScale;
            double dx = _staticMinX * _scale + _offsetX;
            double dy = _staticMinY * _scale + _offsetY;
            double dw = _staticLayer.PixelSize.Width  * ratio;
            double dh = _staticLayer.PixelSize.Height * ratio;
            dc.DrawImage(_staticLayer, new Rect(dx, dy, dw, dh));
        }

        // ── Alloc / can-alloc connection overlay ──────────────────────────
        // Only the edges whose state differs from the cached neutral version
        // need redrawing. Cheap loop — typically <300 edges out of ~5000.
        foreach (var node in nodes)
        {
            if (!IsNodeVisible(node, filter)) continue;
            bool nAlloc = alloc?.Contains(node.Id) == true;
            bool nCan   = canAlloc?.Contains(node.Id) == true;
            if (!nAlloc && !nCan) continue;

            var (fromX, fromY) = W2S(node);
            foreach (var lid in node.LinkedIds)
            {
                if (lid >= node.Id) continue;
                if (!_nodeById.TryGetValue(lid, out var tgt)) continue;
                if (!IsNodeVisible(tgt, filter)) continue;
                bool srcAsc = !string.IsNullOrEmpty(node.AscendancyName);
                bool tgtAsc = !string.IsNullOrEmpty(tgt.AscendancyName);
                if (srcAsc != tgtAsc) continue;

                bool bothAlloc = nAlloc && alloc!.Contains(lid);
                bool eitherCan = nCan   || (canAlloc?.Contains(lid) == true);
                if (!bothAlloc && !eitherCan) continue;

                var (toX, toY) = W2S(tgt);
                double mx = Math.Min(fromX, toX), Mx = Math.Max(fromX, toX);
                double my = Math.Min(fromY, toY), My = Math.Max(fromY, toY);
                if (Mx < 0 || mx > Bounds.Width || My < 0 || my > Bounds.Height) continue;

                var pen = bothAlloc ? ConnAllocPen : CanAllocPen;
                dc.DrawLine(pen, new Point(fromX, fromY), new Point(toX, toY));
            }
        }

        // ── Unconnected-allocation radius halos (e.g. Entwined Realities) ────
        var emitters = RadiusEmitters;
        if (emitters != null && emitters.Count > 0)
        {
            foreach (var (emitId, radiusWorld) in emitters)
            {
                if (!_nodeById.TryGetValue(emitId, out var emitNode)) continue;
                var (ex, ey) = W2S(emitNode);
                double rPx = radiusWorld * _scale;
                dc.DrawEllipse(RadiusAllocFill, RadiusAllocPen, new Point(ex, ey), rPx, rPx);
            }
        }

        // ── Draw nodes ─────────────────────────────────────────────────────
        foreach (var node in nodes)
        {
            if (!IsNodeVisible(node, filter)) continue;

            var (sx, sy) = W2S(node);
            var r  = GetRadius(node.Type);

            if (sx + r * 3 < 0 || sx - r * 3 > Bounds.Width ||
                sy + r * 3 < 0 || sy - r * 3 > Bounds.Height)
                continue;

            bool isAlloc  = alloc?.Contains(node.Id) == true;
            bool isCan    = canAlloc?.Contains(node.Id) == true;
            bool isSearch = search.Length > 0 &&
                            node.Name.Contains(search, StringComparison.OrdinalIgnoreCase);

            DrawNode(dc, node, sx, sy, r, isAlloc, isCan, isSearch, node == _hoveredNode);
        }

        // ── Jewel radius rings (Socket hover) ─────────────────────────────
        if (_hoveredNode is { Type: "Socket" })
            DrawJewelRadius(dc, _hoveredNode);

        // ── Hover info panel ───────────────────────────────────────────────
        if (_hoveredNode != null)
            DrawHoverInfo(dc, _hoveredNode, alloc?.Contains(_hoveredNode.Id) == true);
    }

    // ── Node drawing ───────────────────────────────────────────────────────

    // Icon world half-size constants (derived from PoE2 orbitRadii spacing)
    private static double GetIconHalfWorld(string type) => type switch
    {
        "Keystone"         => 90,
        "Notable"          => 52,
        "AscendClassStart" => 52,
        "Socket"           => 48,
        "ClassStart"       => 72,
        _                  => 37,
    };

    // Minimum screen half-size (px) to bother rendering icons
    private const double MinIconScreenPx = 6.0;

    private void DrawNode(DrawingContext dc, TreeNodeDto node,
                          double sx, double sy, double r,
                          bool alloc, bool canAlloc, bool search, bool hover)
    {
        var center = new Point(sx, sy);
        var assets = AssetStore;

        // ── Try sprite rendering ───────────────────────────────────────────
        double iconHalfWorld = GetIconHalfWorld(node.Type);
        double iconHalfPx    = iconHalfWorld * _scale;

        bool useSprites = assets != null && iconHalfPx >= MinIconScreenPx;

        if (useSprites)
        {
            // Extra glow layers for Keystone before the sprite
            if (node.Type == "Keystone")
            {
                dc.DrawEllipse(KsGlow2, null, center, iconHalfPx * 2.5, iconHalfPx * 2.5);
                dc.DrawEllipse(KsGlow1, null, center, iconHalfPx * 1.8, iconHalfPx * 1.8);
                if (alloc) dc.DrawEllipse(GlowKsA, null, center, iconHalfPx * 1.4, iconHalfPx * 1.4);
            }
            DrawNodeSprite(dc, node, sx, sy, iconHalfPx, alloc, canAlloc, assets!);
        }
        else
        {
            // ── Procedural fallback ────────────────────────────────────────
            if (node.Type == "Keystone")
            {
                // Multi-layer glow for Keystones (always visible, stronger when allocated)
                dc.DrawEllipse(KsGlow2, null, center, r * 4.5, r * 4.5);
                dc.DrawEllipse(KsGlow1, null, center, r * 3.0, r * 3.0);
                if (alloc) dc.DrawEllipse(GlowKsA, null, center, r * 2.0, r * 2.0);
                DrawKeystone(dc, sx, sy, r, KsFill, alloc ? KsRingA : KsRingU, alloc);
            }
            else
            {
                if (alloc)
                {
                    IBrush glow = node.Type == "Notable" ? GlowNotA : GlowA;
                    dc.DrawEllipse(glow, null, center, r * 2.5, r * 2.5);
                }
                var (fill, ring) = GetStyle(node.Type, alloc, node.AscendancyName);
                dc.DrawEllipse(fill, ring, center, r, r);
                if (node.Type == "Notable")
                    dc.DrawEllipse(null, alloc ? NotRingA : NotRingU, center, r * 1.35, r * 1.35);
                if (node.Type == "Socket")
                    dc.DrawEllipse(alloc ? SockRingA.Brush : NrmFill, null, center, r * 0.4, r * 0.4);
            }
        }

        // ── Overlays (always drawn) ────────────────────────────────────────
        double orPx = useSprites ? iconHalfPx : r;

        if (canAlloc && !alloc)
            dc.DrawEllipse(null, CanAllocNodePen, center, orPx + 2.5, orPx + 2.5);
        if (search)
            dc.DrawEllipse(null, SearchPen, center, orPx + 4, orPx + 4);
        if (hover)
            dc.DrawEllipse(null, HoverPen, center, orPx + 3.5, orPx + 3.5);
    }

    private static void DrawNodeSprite(DrawingContext dc, TreeNodeDto node,
                                       double sx, double sy, double halfPx,
                                       bool alloc, bool canAlloc, TreeAssetStore assets)
    {
        var destRect = new Rect(sx - halfPx, sy - halfPx, halfPx * 2, halfPx * 2);
        var center   = new Point(sx, sy);

        // 1. Icon clipped to circle (RoundedRect with radius=halfPx → circle)
        var iconResult = assets.GetSprite(node.Icon);
        if (iconResult.HasValue)
        {
            var (bmp, src) = iconResult.Value;
            var clipRect = new RoundedRect(destRect, halfPx);
            using (dc.PushClip(clipRect))
            {
                dc.DrawImage(bmp, src, destRect);
                if (!alloc)
                    dc.DrawEllipse(DimBrush, null, center, halfPx, halfPx);
            }
        }

        // 2. Frame overlay: sprite preferred, procedural ring fallback
        double framePx  = halfPx * 1.35;
        var frameRect   = new Rect(sx - framePx, sy - framePx, framePx * 2, framePx * 2);
        string frameKey = alloc    ? node.OverlayAlloc
                        : canAlloc ? node.OverlayPath
                        : node.OverlayUnalloc;

        bool frameDrawn = false;
        if (!string.IsNullOrEmpty(frameKey))
        {
            var frameResult = assets.GetSprite(frameKey);
            if (frameResult.HasValue)
            {
                var (bmp, src) = frameResult.Value;
                dc.DrawImage(bmp, src, frameRect);
                frameDrawn = true;
            }
        }

        if (!frameDrawn)
        {
            var (_, ring) = GetStyle(node.Type, alloc, node.AscendancyName);
            dc.DrawEllipse(null, ring, center, framePx, framePx);
            if (node.Type is "Notable" or "AscendClassStart")
                dc.DrawEllipse(null, ring, center, framePx * 1.2, framePx * 1.2);
        }
    }

    private static void DrawKeystone(DrawingContext dc, double cx, double cy, double r,
                                     IBrush fill, IPen ring, bool alloc)
    {
        // 8-sided polygon
        const int Sides = 8;
        var pts = new Point[Sides];
        for (int i = 0; i < Sides; i++)
        {
            double a = 2 * Math.PI * i / Sides - Math.PI / Sides;
            pts[i] = new Point(cx + r * Math.Cos(a), cy + r * Math.Sin(a));
        }

        var sg = new StreamGeometry();
        using (var ctx = sg.Open())
        {
            ctx.BeginFigure(pts[0], true);
            for (int i = 1; i < Sides; i++) ctx.LineTo(pts[i]);
            ctx.EndFigure(true);
        }
        dc.DrawGeometry(fill, ring, sg);

        // inner diamond dot
        if (alloc)
        {
            var inner = new StreamGeometry();
            using (var ctx = inner.Open())
            {
                double ir = r * 0.3;
                ctx.BeginFigure(new Point(cx, cy - ir), true);
                ctx.LineTo(new Point(cx + ir, cy));
                ctx.LineTo(new Point(cx, cy + ir));
                ctx.LineTo(new Point(cx - ir, cy));
                ctx.EndFigure(true);
            }
            dc.DrawGeometry(KsRingA.Brush, null, inner);
        }
    }

    private static (IBrush fill, IPen ring) GetStyle(string type, bool alloc, string asc)
    {
        bool isAsc = !string.IsNullOrEmpty(asc);
        return type switch
        {
            "Notable"          => isAsc ? (alloc ? AscAlloc : AscFill, alloc ? AscRingA : AscRingU)
                                         : (alloc ? NotAlloc : NotFill, alloc ? NotRingA : NotRingU),
            "Socket"           => (SockFill, alloc ? SockRingA : SockRingU),
            "ClassStart"       => (StartFill, StartRing),
            "AscendClassStart" => (AscFill, alloc ? AscRingA : AscRingU),
            "Mastery"          => (NrmFill, MastRing),
            _                  => isAsc ? (alloc ? AscAlloc : AscFill, alloc ? AscRingA : AscRingU)
                                         : (alloc ? NrmAlloc : NrmFill, alloc ? NrmRingA : NrmRingU),
        };
    }

    // ── Hover info panel ───────────────────────────────────────────────────

    private void DrawHoverInfo(DrawingContext dc, TreeNodeDto node, bool alloc)
    {
        const double Pad    = 8;
        const double MaxW   = 340;
        const double LineGap = 2;
        double contentW = MaxW - Pad * 2;

        var translatedName = GameTranslationService.TPassiveName(node.Name);
        var typeStr = $"[{node.Type}{(string.IsNullOrEmpty(node.AscendancyName) ? "" : " · " + node.AscendancyName)}]";

        var rawLines = new List<(string text, Color color, double fontSize)>();
        rawLines.Add((translatedName, alloc ? Color.Parse("#A6E3A1") : Colors.White, 12));
        rawLines.Add((typeStr, Color.Parse("#585B70"), 10));
        foreach (var s in node.Stats)
            if (!string.IsNullOrEmpty(s))
                rawLines.Add((GameTranslationService.TPassiveStat(s), Color.Parse("#CDD6F4"), 10));

        var typeface = new Typeface("Cascadia Code,Consolas,monospace");

        // Build FormattedText list with wrapping; measure actual sizes
        var fts = rawLines.Select(l =>
        {
            var ft = new FormattedText(l.text, CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, typeface, l.fontSize, Brushes.White)
            {
                MaxTextWidth = contentW
            };
            return (ft, l.color);
        }).ToList();

        double panelW = Math.Max(100, fts.Max(x => x.ft.Width)) + Pad * 2;
        double panelH = Pad * 2 + fts.Sum(x => x.ft.Height + LineGap) - LineGap;

        // Keep panel inside canvas
        double px = Math.Min(8.0, Bounds.Width  - panelW - 4);
        double py = Math.Min(8.0, Bounds.Height - panelH - 4);
        px = Math.Max(4, px);
        py = Math.Max(4, py);

        dc.DrawRectangle(InfoBg, InfoBrd, new Rect(px, py, panelW, panelH), 4, 4);

        double ty = py + Pad;
        for (int i = 0; i < fts.Count; i++)
        {
            var (ft, color) = fts[i];
            ft.SetForegroundBrush(new SolidColorBrush(color));
            dc.DrawText(ft, new Point(px + Pad, ty));
            ty += ft.Height + LineGap;
        }
    }

    // ── Pointer events ─────────────────────────────────────────────────────

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var props = e.GetCurrentPoint(this).Properties;
        if (props.IsLeftButtonPressed)
        {
            _isPanning    = true;
            _isRightPress = false;
            _panStart     = e.GetPosition(this);
            _pressStart   = _panStart;
            e.Pointer.Capture(this);
            e.Handled = true;
        }
        else if (props.IsRightButtonPressed)
        {
            _isRightPress = true;
            _pressStart   = e.GetPosition(this);
            e.Handled = true;
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        var pos = e.GetPosition(this);
        double d = Math.Sqrt(Math.Pow(pos.X - _pressStart.X, 2) + Math.Pow(pos.Y - _pressStart.Y, 2));

        if (_isRightPress)
        {
            // RMB click — dealloc
            if (d < 5.0 && _hoveredNode != null)
                DeallocNodeCommand?.Execute(_hoveredNode.Id);
            _isRightPress = false;
        }
        else if (_isPanning)
        {
            // LMB click (not drag) — alloc / change attribute
            if (d < 5.0 && _hoveredNode != null)
                AllocNodeCommand?.Execute(_hoveredNode.Id);
        }

        if (_isPanning)
        {
            _isPanning = false;
            e.Pointer.Capture(null);
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var pos = e.GetPosition(this);

        if (_isPanning)
        {
            _offsetX += pos.X - _panStart.X;
            _offsetY += pos.Y - _panStart.Y;
            _panStart = pos;
            InvalidateVisual();
            e.Handled = true;
        }

        // Hover
        var wx = (pos.X - _offsetX) / _scale;
        var wy = (pos.Y - _offsetY) / _scale;
        double thresh = Math.Max(25.0, 10.0) / _scale;
        var closest = FindClosestVisible(wx, wy, thresh);
        if (closest != _hoveredNode)
        {
            _hoveredNode = closest;
            InvalidateVisual();
        }
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        var pos    = e.GetPosition(this);
        double fac = e.Delta.Y > 0 ? 1.15 : 1.0 / 1.15;
        _offsetX = pos.X + (_offsetX - pos.X) * fac;
        _offsetY = pos.Y + (_offsetY - pos.Y) * fac;
        _scale  *= fac;
        // If the user zoomed in well past the baked resolution, force a rebake
        // so the static connections stay sharp. Out by 50 % triggers a refresh.
        if (_staticLayer != null && _scale > _staticLayerScale * 1.5)
            InvalidateStaticLayer();
        InvalidateVisual();
        e.Handled = true;
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    // Returns true for main-tree nodes always; for ascendancy nodes only when their name matches filter
    private bool IsNodeVisible(TreeNodeDto node, string filter) =>
        string.IsNullOrEmpty(node.AscendancyName) ||
        (!string.IsNullOrEmpty(filter) && node.AscendancyName == filter);

    // Effective world-space position after ascendancy offset
    private (double wx, double wy) EffectiveWorld(TreeNodeDto node)
    {
        double wx = node.X, wy = node.Y;
        if (!string.IsNullOrEmpty(node.AscendancyName) &&
            _ascendOffsets.TryGetValue(node.AscendancyName, out var off))
        {
            wx += off.dx;
            wy += off.dy;
        }
        return (wx, wy);
    }

    private (double sx, double sy) W2S(TreeNodeDto node)
    {
        var (wx, wy) = EffectiveWorld(node);
        return (wx * _scale + _offsetX, wy * _scale + _offsetY);
    }

    // Compute per-ascendancy translation to bring each sub-tree centroid to (0,0),
    // and the max distance from centroid (used as background half-size).
    private void ComputeAscendOffsets()
    {
        _ascendOffsets.Clear();
        _ascendRadii.Clear();
        var nodes = Nodes;
        if (nodes == null) return;

        var groups = new Dictionary<string, (double sumX, double sumY, int cnt)>();
        foreach (var n in nodes)
        {
            if (string.IsNullOrEmpty(n.AscendancyName)) continue;
            if (!groups.TryGetValue(n.AscendancyName, out var g))
                g = (0, 0, 0);
            groups[n.AscendancyName] = (g.sumX + n.X, g.sumY + n.Y, g.cnt + 1);
        }
        foreach (var (name, g) in groups)
        {
            double cx = g.sumX / g.cnt;
            double cy = g.sumY / g.cnt;
            _ascendOffsets[name] = (-cx, -cy);

            // Compute max node distance from centroid for background sizing
            double maxR = 0;
            foreach (var n in nodes!)
            {
                if (n.AscendancyName != name) continue;
                double d = Math.Sqrt((n.X - cx) * (n.X - cx) + (n.Y - cy) * (n.Y - cy));
                if (d > maxR) maxR = d;
            }
            _ascendRadii[name] = maxR;
        }
    }

    /// <summary>(Re)bake the static connection layer if it's missing or stale.
    /// The layer is drawn in bitmap coordinates with the world origin shifted
    /// to (0,0), scaled by <see cref="_staticLayerScale"/>. On render we draw
    /// it back with a destRect computed from the current pan/zoom.</summary>
    private void EnsureStaticLayer(string filter)
    {
        var nodes = Nodes;
        if (nodes == null || nodes.Count == 0) return;

        bool stale = _staticLayer == null
                  || !ReferenceEquals(nodes, _staticNodesRef)
                  || _staticFilter != filter;
        if (!stale) return;

        // Reference scale: bake at a moderate density, with headroom for some
        // zooming-in. Tied to the current scale so first-paint quality is good.
        double refScale = Math.Max(_scale, 0.18);

        // Compute world-space bounds covering all visible-or-potentially-visible
        // nodes (including ascendancies, with their per-ascendancy offsets applied).
        // We deliberately include ALL connection endpoints — the per-node draw
        // loop filters by ascendancy at paint time, so any stale lines from
        // hidden ascendancies stay hidden because they're drawn off-screen.
        double minX = double.MaxValue, maxX = double.MinValue;
        double minY = double.MaxValue, maxY = double.MinValue;
        foreach (var n in nodes)
        {
            if (!IsNodeVisible(n, filter)) continue;
            var (wx, wy) = EffectiveWorld(n);
            if (wx < minX) minX = wx;
            if (wx > maxX) maxX = wx;
            if (wy < minY) minY = wy;
            if (wy > maxY) maxY = wy;
        }
        if (minX > maxX || minY > maxY) return;
        const double pad = 50;
        minX -= pad; minY -= pad; maxX += pad; maxY += pad;

        int pxW = Math.Min(4096, Math.Max(64, (int)((maxX - minX) * refScale)));
        int pxH = Math.Min(4096, Math.Max(64, (int)((maxY - minY) * refScale)));

        var bmp = new Avalonia.Media.Imaging.RenderTargetBitmap(
            new PixelSize(pxW, pxH), new Vector(96, 96));
        using (var ctx = bmp.CreateDrawingContext())
        {
            // World → bitmap: (wx - minX) * refScale
            foreach (var node in nodes)
            {
                if (!IsNodeVisible(node, filter)) continue;
                var (wx1, wy1) = EffectiveWorld(node);
                foreach (var lid in node.LinkedIds)
                {
                    if (lid >= node.Id) continue;
                    if (!_nodeById.TryGetValue(lid, out var tgt)) continue;
                    if (!IsNodeVisible(tgt, filter)) continue;
                    bool srcAsc = !string.IsNullOrEmpty(node.AscendancyName);
                    bool tgtAsc = !string.IsNullOrEmpty(tgt.AscendancyName);
                    if (srcAsc != tgtAsc) continue;
                    var (wx2, wy2) = EffectiveWorld(tgt);
                    ctx.DrawLine(ConnPen,
                        new Point((wx1 - minX) * refScale, (wy1 - minY) * refScale),
                        new Point((wx2 - minX) * refScale, (wy2 - minY) * refScale));
                }
            }
        }

        _staticLayer?.Dispose();
        _staticLayer       = bmp;
        _staticLayerScale  = refScale;
        _staticMinX        = minX;
        _staticMinY        = minY;
        _staticNodesRef    = nodes;
        _staticFilter      = filter;
    }

    private void FitToView()
    {
        var nodes = Nodes;
        if (nodes == null || nodes.Count == 0) return;

        // Always fit to main tree extent only
        var subset = nodes.Where(n => string.IsNullOrEmpty(n.AscendancyName)).ToList();
        if (subset.Count == 0) subset = nodes.ToList();

        double minX = double.MaxValue, maxX = double.MinValue;
        double minY = double.MaxValue, maxY = double.MinValue;
        foreach (var n in subset)
        {
            if (n.X < minX) minX = n.X;
            if (n.X > maxX) maxX = n.X;
            if (n.Y < minY) minY = n.Y;
            if (n.Y > maxY) maxY = n.Y;
        }

        double treeW = maxX - minX, treeH = maxY - minY;
        if (treeW == 0 || treeH == 0) return;

        _scale   = Math.Min(Bounds.Width / treeW, Bounds.Height / treeH) * 0.88;
        _offsetX = (Bounds.Width  - treeW * _scale) / 2.0 - minX * _scale;
        _offsetY = (Bounds.Height - treeH * _scale) / 2.0 - minY * _scale;
    }

    private TreeNodeDto? FindClosestVisible(double ewx, double ewy, double thr)
    {
        var filter = AscendancyFilter;
        TreeNodeDto? best  = null;
        double       best2 = thr * thr;
        foreach (var n in _nodeById.Values)
        {
            if (!IsNodeVisible(n, filter)) continue;
            var (nx, ny) = EffectiveWorld(n);
            double d2 = (nx - ewx) * (nx - ewx) + (ny - ewy) * (ny - ewy);
            if (d2 < best2) { best2 = d2; best = n; }
        }
        return best;
    }

    private static double GetRadius(string type) => type switch
    {
        "Keystone"         => 10.0,
        "Notable"          => 7.0,
        "ClassStart"       => 12.0,
        "AscendClassStart" => 10.0,
        "Socket"           => 8.0,
        "Mastery"          => 5.0,
        _                  => 4.5,
    };

    // ── Ascendancy background ──────────────────────────────────────────────

    private void DrawAscendancyBackground(DrawingContext dc, string ascendancyName)
    {
        var assets = AssetStore;
        if (assets == null) return;

        var spriteKey = "Classes" + ascendancyName;
        var sprite = assets.GetSprite(spriteKey);
        if (!sprite.HasValue) return;

        var (bmp, src) = sprite.Value;

        // After ComputeAscendOffsets, each ascendancy centroid maps to world (0,0).
        // World (0,0) → screen = (_offsetX, _offsetY).
        double cx = _offsetX;
        double cy = _offsetY;

        // Half-size = max node distance from centroid × padding factor
        double nodeRadius = _ascendRadii.TryGetValue(ascendancyName, out var r) ? r : 800.0;
        double half = nodeRadius * 1.4 * _scale;

        var destRect = new Rect(cx - half, cy - half, half * 2, half * 2);

        using (dc.PushOpacity(0.85))
            dc.DrawImage(bmp, src, destRect);
    }

    // ── Jewel radius visualization ─────────────────────────────────────────

    // Outer world-space radii × 1.2 multiplier
    private static readonly (double Outer, IPen Pen, string Label)[] JewelRadiiBands =
    [
        (1200, RadSmall,  "Small"),
        (1380, RadMedium, "Medium"),
        (1560, RadLarge,  "Large"),
        (1800, RadVLarge, "Very Large"),
    ];

    private void DrawJewelRadius(DrawingContext dc, TreeNodeDto socket)
    {
        var (cx, cy) = W2S(socket);
        foreach (var (outer, pen, _) in JewelRadiiBands)
        {
            double r = outer * _scale;
            dc.DrawEllipse(null, pen, new Point(cx, cy), r, r);
        }
    }
}
