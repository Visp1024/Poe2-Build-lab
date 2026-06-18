using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using PBLApp.Core.Localization;
using PBLEngine;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Input;
using RadiusEmitter = (int NodeId, double RadiusWorld);
using JewelRadius = (int NodeId, double Outer, double Inner, bool Variable);
using JewelIcon = (int NodeId, string BaseName, string Title, bool Unique);

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

    /// <summary>True while a background alloc/dealloc + recalc is running, so the
    /// hover tooltip shows a loading spinner where the (Lua-computed) stat diff
    /// will appear once it settles.</summary>
    public static readonly StyledProperty<bool> IsBusyProperty =
        AvaloniaProperty.Register<TreeCanvas, bool>(nameof(IsBusy));

    public static readonly StyledProperty<ICommand?> AllocNodeCommandProperty =
        AvaloniaProperty.Register<TreeCanvas, ICommand?>(nameof(AllocNodeCommand));

    public static readonly StyledProperty<ICommand?> DeallocNodeCommandProperty =
        AvaloniaProperty.Register<TreeCanvas, ICommand?>(nameof(DeallocNodeCommand));

    public static readonly StyledProperty<TreeAssetStore?> AssetStoreProperty =
        AvaloniaProperty.Register<TreeCanvas, TreeAssetStore?>(nameof(AssetStore));

    public static readonly StyledProperty<IReadOnlyList<RadiusEmitter>?> RadiusEmittersProperty =
        AvaloniaProperty.Register<TreeCanvas, IReadOnlyList<RadiusEmitter>?>(nameof(RadiusEmitters));

    public static readonly StyledProperty<IReadOnlyList<JewelRadius>?> JewelRadiiProperty =
        AvaloniaProperty.Register<TreeCanvas, IReadOnlyList<JewelRadius>?>(nameof(JewelRadii));

    public static readonly StyledProperty<IReadOnlyList<JewelIcon>?> JewelIconsProperty =
        AvaloniaProperty.Register<TreeCanvas, IReadOnlyList<JewelIcon>?>(nameof(JewelIcons));

    public static readonly StyledProperty<IReadOnlyDictionary<string, AscendancyBgDto>?> AscendancyBackgroundsProperty =
        AvaloniaProperty.Register<TreeCanvas, IReadOnlyDictionary<string, AscendancyBgDto>?>(nameof(AscendancyBackgrounds));

    public static readonly StyledProperty<string> ClassBackgroundImageProperty =
        AvaloniaProperty.Register<TreeCanvas, string>(nameof(ClassBackgroundImage), "");

    /// <summary>Callback supplied by the view layer that fetches the modern
    /// hover-info packet (mod lines + stat diff + path distance) for a node
    /// via <c>LuaHost.GetNodeHoverInfo</c>. Hooked into Render via a small
    /// per-node cache that invalidates on alloc-state change.</summary>
    public static readonly StyledProperty<Func<int, NodeHoverInfo?>?> HoverInfoProviderProperty =
        AvaloniaProperty.Register<TreeCanvas, Func<int, NodeHoverInfo?>?>(nameof(HoverInfoProvider));

    public static readonly StyledProperty<NodePowerResult?> NodePowerOverlayProperty =
        AvaloniaProperty.Register<TreeCanvas, NodePowerResult?>(nameof(NodePowerOverlay));

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
    public bool IsBusy
    {
        get => GetValue(IsBusyProperty);
        set => SetValue(IsBusyProperty, value);
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
    public IReadOnlyList<JewelRadius>? JewelRadii
    {
        get => GetValue(JewelRadiiProperty);
        set => SetValue(JewelRadiiProperty, value);
    }
    public IReadOnlyList<JewelIcon>? JewelIcons
    {
        get => GetValue(JewelIconsProperty);
        set => SetValue(JewelIconsProperty, value);
    }
    public IReadOnlyDictionary<string, AscendancyBgDto>? AscendancyBackgrounds
    {
        get => GetValue(AscendancyBackgroundsProperty);
        set => SetValue(AscendancyBackgroundsProperty, value);
    }
    public string ClassBackgroundImage
    {
        get => GetValue(ClassBackgroundImageProperty);
        set => SetValue(ClassBackgroundImageProperty, value);
    }
    public Func<int, NodeHoverInfo?>? HoverInfoProvider
    {
        get => GetValue(HoverInfoProviderProperty);
        set => SetValue(HoverInfoProviderProperty, value);
    }

    public NodePowerResult? NodePowerOverlay
    {
        get => GetValue(NodePowerOverlayProperty);
        set => SetValue(NodePowerOverlayProperty, value);
    }

    /// <summary>Raised on a left-click of an allocated jewel socket node, with the
    /// node id and its screen-space centre. The view layer opens the jewel picker
    /// there. Set by <c>TreeTabView</c>.</summary>
    public Action<int, Point>? SocketClicked { get; set; }

    // Cache the resolved hover packet for the currently hovered node so that
    // every Render pass (pan/zoom/repaint while the cursor stays on a node)
    // doesn't re-hit Lua. Invalidated by hovered-node change or alloc-state
    // change (handled in OnPropertyChanged).
    private NodeHoverInfo? _hoverCache;
    private int            _hoverCacheNodeId   = -1;
    private object?        _hoverCacheAllocRef;

    // Cache the shortest path (ordered node ids: target → … → allocated anchor)
    // from the allocated tree to the hovered unallocated node. Recomputed only
    // when the hovered node or the allocated set changes — not on pan/zoom.
    private List<int>? _hoverPath;
    private int        _hoverPathTargetId = -1;
    private object?    _hoverPathAllocRef;

    // ── Pan / zoom state ───────────────────────────────────────────────────

    private double _offsetX, _offsetY;
    private double _scale = 0.12;
    private bool   _isPanning;
    private Point  _panStart;
    private Point  _pressStart;          // to distinguish click from drag
    private bool   _isRightPress;        // true when press was RMB (no panning)
    private bool   _fitNeeded = true;

    // ── View persistence (zoom + centre across tab switch / app restart) ────
    // Restored once on the first fit pass instead of FitToView; saved on user
    // pan/zoom (debounced) and when the canvas leaves the visual tree.
    private TreeViewPersistence.View? _pendingView;
    private readonly DispatcherTimer _saveViewTimer;

    // ── Loading spinner (shown in the tooltip while a toggle's Lua is running) ─
    private readonly DispatcherTimer _spinnerTimer;
    private double _spinnerAngle;

    public TreeCanvas()
    {
        _pendingView = TreeViewPersistence.Load();

        _saveViewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _saveViewTimer.Tick += (_, _) => { _saveViewTimer.Stop(); SaveCurrentView(); };

        _spinnerTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
        _spinnerTimer.Tick += (_, _) => { _spinnerAngle += 0.5; InvalidateVisual(); };

        // Re-read from disk when re-attached only if we haven't already restored
        // (a reused instance keeps its in-memory view); save on leave.
        AttachedToVisualTree += (_, _) => _pendingView ??= TreeViewPersistence.Load();
        DetachedFromVisualTree += (_, _) => { SaveCurrentView(); _spinnerTimer.Stop(); };
    }

    // Spin only while busy AND hovering a node (the tooltip is what shows it).
    private void UpdateSpinnerTimer()
    {
        bool want = IsBusy && _hoveredNode != null;
        if (want && !_spinnerTimer.IsEnabled) _spinnerTimer.Start();
        else if (!want && _spinnerTimer.IsEnabled) _spinnerTimer.Stop();
    }

    private void SaveCurrentView()
    {
        if (Bounds.Width <= 0 || Bounds.Height <= 0) return;
        var (s, cx, cy) = GetViewState();
        TreeViewPersistence.Save(s, cx, cy);
    }

    private void QueueViewSave()
    {
        _saveViewTimer.Stop();
        _saveViewTimer.Start();
    }

    // ── Hover / lookup ─────────────────────────────────────────────────────

    private TreeNodeDto? _hoveredNode;
    private Dictionary<int, TreeNodeDto>             _nodeById      = new();
    // Per-ascendancy transform: subtree centroid (cx,cy) maps to the background
    // circle center (tx,ty); k shrinks node positions around the center so the
    // subtree fits inside the plate.
    private Dictionary<string, (double cx, double cy, double tx, double ty, double k)> _ascendTransforms = new();
    private Dictionary<string, double> _ascendRadii = new();

    // Radius (world units) of the main tree's innermost ring — the class-start
    // nodes (~1443 in PoE2 0.5). The selected ascendancy plate is sized so its
    // edge meets this ring, and its subtree is scaled to sit inside the plate.
    private double _mainInnerRadius = 1450;

    // Cache for the "can allocate" set (nodes adjacent to any allocated node).
    // Recomputing this iterates every node × its neighbours — ~10-30 ms on the
    // full tree. We only need a refresh when AllocatedIds or Nodes change, not
    // on every pan/zoom/hover.
    private HashSet<int>? _canAllocCache;
    private object?       _canAllocAllocRef;
    private object?       _canAllocNodesRef;

    // Cache for node-power heat-map colour lookup.
    // Rebuilt when the NodePowerOverlay reference changes; cleared when null.
    private System.Collections.Generic.Dictionary<int, Color>? _powerColorCache;
    private object? _powerOverlayRef;

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

    // Hover path preview: dashed route + rings for the shortest path from the
    // allocated tree to the hovered unallocated node (the points you'd spend).
    private static readonly IPen PathPreviewPen = new Pen(new SolidColorBrush(Color.Parse("#FAB387")), 2.6)
        { DashStyle = new DashStyle(new double[] { 3, 2 }, 0) };
    private static readonly IPen PathPreviewNodePen = MkPen("#FAB387", 2.2);

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

    // Persistent ring drawn for an allocated socket that holds a radius jewel.
    // Teal — distinct from the white node outlines, the blue node field and the
    // tan ascendancy backdrops, so the radius stays legible even at full-tree
    // zoom (our client always fits the whole tree). The neutral-white tint PoB
    // uses works there only because the tree is viewed zoomed-in.
    private static readonly IBrush JewelRingFill = new SolidColorBrush(Color.FromArgb(30, 70, 210, 200));
    private static readonly IPen   JewelRingPen  = new Pen(new SolidColorBrush(Color.FromArgb(230, 70, 210, 200)), 2.4);

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
            // Alloc changed → hover diff numbers are stale.
            _hoverCache = null;
            _hoverCacheNodeId = -1;
            InvalidateVisual();
        }
        else if (change.Property == SearchTextProperty)
        {
            InvalidateVisual();
        }
        else if (change.Property == IsBusyProperty)
        {
            UpdateSpinnerTimer();
            InvalidateVisual();
        }
        else if (change.Property == AscendancyFilterProperty)
        {
            // The selected ascendancy's subtree is re-centered to world (0,0),
            // so its per-ascendancy transform depends on the active filter.
            ComputeAscendOffsets();
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
        else if (change.Property == JewelRadiiProperty)
        {
            InvalidateVisual();
        }
        else if (change.Property == JewelIconsProperty)
        {
            InvalidateVisual();
        }
        else if (change.Property == AscendancyBackgroundsProperty)
        {
            // Subtree offsets target the background circles, so they depend on this dict.
            ComputeAscendOffsets();
            InvalidateStaticLayer();
            InvalidateVisual();
        }
        else if (change.Property == ClassBackgroundImageProperty)
        {
            InvalidateVisual();
        }
        else if (change.Property == NodePowerOverlayProperty)
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
            // Restore the persisted framing (tab switch / restart) once; fall
            // back to fit-to-view when there's no saved state.
            if (_pendingView is { } v)
            {
                // Restore the framing in-place; must NOT InvalidateVisual here (we're
                // mid-Render) — the new transform takes effect in this same pass.
                SetViewTransform(v.Scale, v.CenterX, v.CenterY);
                _pendingView = null;
            }
            else
            {
                FitToView();
            }
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

        // ── Ascendancy background plates ────────────────────────────────────
        DrawAscendancyBackgrounds(dc, filter);

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

        // ── Hover path preview ─────────────────────────────────────────────
        // Shortest route from the allocated tree to the hovered unallocated
        // node: dashed edges here, rings on the path nodes after the node draw.
        List<int>? hoverPath = null;
        if (_hoveredNode != null && alloc is { Count: > 0 } && !alloc.Contains(_hoveredNode.Id))
        {
            hoverPath = GetHoverPath(_hoveredNode, alloc, filter);
            if (hoverPath is { Count: > 1 })
            {
                for (int i = 0; i + 1 < hoverPath.Count; i++)
                {
                    if (!_nodeById.TryGetValue(hoverPath[i], out var a)) continue;
                    if (!_nodeById.TryGetValue(hoverPath[i + 1], out var b)) continue;
                    var (ax, ay) = W2S(a);
                    var (bx, by) = W2S(b);
                    dc.DrawLine(PathPreviewPen, new Point(ax, ay), new Point(bx, by));
                }
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

        // ── Socketed jewel radius rings (persistent, allocated sockets) ─────
        var jewelRadii = JewelRadii;
        if (jewelRadii != null && jewelRadii.Count > 0)
        {
            foreach (var (nodeId, outer, inner, variable) in jewelRadii)
            {
                if (!_nodeById.TryGetValue(nodeId, out var sockNode)) continue;
                var (jx, jy) = W2S(sockNode);
                double rOut = outer * _scale;
                if (variable && inner > 0)
                {
                    // Thread-of-Hope-like: annulus — fill the ring band, outline both edges.
                    double rIn = inner * _scale;
                    var ringGeo = new CombinedGeometry(
                        GeometryCombineMode.Exclude,
                        new EllipseGeometry(new Rect(jx - rOut, jy - rOut, rOut * 2, rOut * 2)),
                        new EllipseGeometry(new Rect(jx - rIn,  jy - rIn,  rIn  * 2, rIn  * 2)));
                    dc.DrawGeometry(JewelRingFill, null, ringGeo);
                    dc.DrawEllipse(null, JewelRingPen, new Point(jx, jy), rOut, rOut);
                    dc.DrawEllipse(null, JewelRingPen, new Point(jx, jy), rIn,  rIn);
                }
                else
                {
                    // Standard jewel: full disc.
                    dc.DrawEllipse(JewelRingFill, JewelRingPen, new Point(jx, jy), rOut, rOut);
                }
            }
        }

        // ── Draw nodes ─────────────────────────────────────────────────────
        // All nodes drawn per-frame. We tried baking unallocated nodes into
        // the static bitmap (commit d119426) but small icons lost too much
        // detail when the bake was downscaled at typical zoom levels. Live
        // rendering is fast enough — DrawNode is a few primitives per node
        // and only visible nodes (post-cull) get drawn.
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
            bool isSearch = search.Length > 0 && NodeMatchesSearch(node, search);

            DrawNode(dc, node, sx, sy, r, isAlloc, isCan, isSearch, node == _hoveredNode);
        }

        // ── Hover path-preview node rings (over node art) ──────────────────
        // Ring each node on the route except the allocated anchor and the
        // hovered node itself (the latter already gets the orange hover ring).
        if (hoverPath is { Count: > 1 })
        {
            foreach (var pid in hoverPath)
            {
                if (alloc!.Contains(pid) || pid == _hoveredNode!.Id) continue;
                if (!_nodeById.TryGetValue(pid, out var pn)) continue;
                var (px, py) = W2S(pn);
                double iconHalfPx = GetIconHalfWorld(pn.Type) * _scale;
                bool useSprites   = AssetStore != null && iconHalfPx >= MinIconScreenPx;
                double orPx       = useSprites ? iconHalfPx : GetRadius(pn.Type);
                dc.DrawEllipse(null, PathPreviewNodePen, new Point(px, py), orPx + 3.0, orPx + 3.0);
            }
        }

        // ── Socketed jewel art (over the socket base, allocated sockets) ───
        // The jewel base/unique art sprites (Diamond, Timeless Jewel, unique
        // titles, ...) already include their own circular frame, so we draw them
        // unclipped and unframed, slightly larger than the empty-socket art so
        // they read as "filled". Mirrors PoB painting the jewel as the socket
        // overlay.
        var jewelIcons = JewelIcons;
        if (jewelIcons != null && jewelIcons.Count > 0 && AssetStore is { } jstore)
        {
            double half = GetIconHalfWorld("Socket") * _scale * 1.3;
            if (half >= MinIconScreenPx)
            {
                foreach (var (nodeId, baseName, title, unique) in jewelIcons)
                {
                    if (!_nodeById.TryGetValue(nodeId, out var sock)) continue;
                    var (jx, jy) = W2S(sock);
                    if (jx + half < 0 || jx - half > Bounds.Width ||
                        jy + half < 0 || jy - half > Bounds.Height)
                        continue;

                    // Prefer the unique's own art when present, else the base art.
                    string? key = unique && jstore.HasSprite(title) ? title
                                : jstore.HasSprite(baseName)        ? baseName
                                : null;
                    if (key == null) continue;
                    if (jstore.GetSprite(key) is not { } sp) continue;

                    dc.DrawImage(sp.Bmp, sp.Src, new Rect(jx - half, jy - half, half * 2, half * 2));
                }
            }
        }

        // ── Jewel radius rings (Socket hover) ─────────────────────────────
        if (_hoveredNode is { Type: "Socket" })
            DrawJewelRadius(dc, _hoveredNode);

        // ── Hover info panel ───────────────────────────────────────────────
        if (_hoveredNode != null)
        {
            // Points-to-node from the C# path (instant — shown even while the
            // Lua stat diff is still loading). 0 when allocated / unreachable.
            int csPathDist = hoverPath is { Count: > 1 } ? hoverPath.Count - 1 : 0;
            DrawHoverInfo(dc, _hoveredNode, alloc?.Contains(_hoveredNode.Id) == true, csPathDist, IsBusy);
        }
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

    private Color? PowerColorFor(int nodeId)
    {
        var overlay = NodePowerOverlay;
        if (overlay is null) { _powerColorCache = null; _powerOverlayRef = null; return null; }
        if (!ReferenceEquals(overlay, _powerOverlayRef) || _powerColorCache is null)
        {
            _powerColorCache = new();
            foreach (var e in overlay.Entries)
            {
                var c = NodePowerColorizer.ColorFor(e, overlay.Max, overlay.OffDefMode);
                if (c is { } col) _powerColorCache[e.Id] = col;
            }
            _powerOverlayRef = overlay;
        }
        return _powerColorCache.TryGetValue(nodeId, out var v) ? v : (Color?)null;
    }

    private void DrawNode(DrawingContext dc, TreeNodeDto node,
                          double sx, double sy, double r,
                          bool alloc, bool canAlloc, bool search, bool hover) =>
        DrawNodeAt(dc, node, sx, sy, r, alloc, canAlloc, search, hover, _scale, AssetStore,
                   alloc ? null : PowerColorFor(node.Id));

    /// <summary>Scale-parametric variant of <see cref="DrawNode"/>. Used both
    /// for per-frame screen draws and when baking the unallocated node layer
    /// into the static bitmap (where the screen-space scale isn't yet known).</summary>
    private static void DrawNodeAt(DrawingContext dc, TreeNodeDto node,
                                   double sx, double sy, double r,
                                   bool alloc, bool canAlloc, bool search, bool hover,
                                   double scale, TreeAssetStore? assets,
                                   Color? powerTint = null)
    {
        var center = new Point(sx, sy);

        // ── Try sprite rendering ───────────────────────────────────────────
        double iconHalfWorld = GetIconHalfWorld(node.Type);
        double iconHalfPx    = iconHalfWorld * scale;

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

        // ── Heat-map power tint (unallocated nodes only) ──────────────────
        if (powerTint is { } pt)
        {
            double glowR = (useSprites ? iconHalfPx : r) + 3;
            dc.DrawEllipse(new SolidColorBrush(pt, 0.55), null, center, glowR, glowR);
            dc.DrawEllipse(null, new Pen(new SolidColorBrush(pt), 2), center, glowR, glowR);
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

    // Design-token brushes for the modern tooltip. Colour values come from
    // PBLApp/Themes/Tokens.Colors.axaml (Dark theme) — kept inline here so
    // TreeCanvas remains a self-contained Avalonia Control rather than
    // having to resolve resources on every paint.
    private static readonly Color BgMantleC      = Color.Parse("#15171D");
    private static readonly Color BorderStrongC  = Color.Parse("#3E4456");
    private static readonly Color TextPrimaryC   = Color.Parse("#E4E7EE");
    private static readonly Color TextSecondaryC = Color.Parse("#A8B0BD");
    private static readonly Color TextMutedC     = Color.Parse("#6E7689");
    private static readonly Color Brand400C      = Color.Parse("#D9B670"); // notable / keystone gold
    private static readonly Color SuccessC       = Color.Parse("#7FC78A");
    private static readonly Color DangerC        = Color.Parse("#D87171");
    private static readonly Color StatLifeC      = Color.Parse("#7FC78A");
    private static readonly Color StatEsC        = Color.Parse("#6FA8DC");
    private static readonly Color StatManaC      = Color.Parse("#8AA6F5");
    private static readonly Color WarningC       = Color.Parse("#E8B763");
    private static readonly Color ModExplicitC   = Color.Parse("#CDD6F4");
    private static readonly Color InfoC          = Color.Parse("#6FA8DC");
    private static readonly Color AttrIntC       = Color.Parse("#89B4FA");
    private static readonly Color AttrDexC       = Color.Parse("#A6E3A1");

    private static readonly IBrush HoverBgBrush     = new SolidColorBrush(Color.FromArgb(245, BgMantleC.R, BgMantleC.G, BgMantleC.B));
    private static readonly IPen   HoverBorderPen   = MkPen("#3E4456", 1.0);
    private static readonly IBrush HoverSepBrush    = new SolidColorBrush(BorderStrongC);

    private static readonly System.Text.RegularExpressions.Regex NumberRegex =
        new(@"[+-]?\d+\.?\d*", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static Color NodeTitleColor(string type, bool alloc)
    {
        return type switch
        {
            "Notable" or "AscendClassStart" => Brand400C,
            "Keystone"                       => WarningC,
            "Socket"                         => InfoC,
            "Mastery"                        => AttrIntC,
            _                                => alloc ? SuccessC : TextPrimaryC,
        };
    }

    /// <summary>Renders a single mod line with numbers highlighted in a
    /// different colour from the surrounding text. Returns the text height.</summary>
    private static double DrawColoredModLine(DrawingContext dc, string text, double x, double y,
                                             double maxWidth, double fontSize,
                                             Typeface typeface,
                                             Color textColor, Color numberColor)
    {
        var matches = NumberRegex.Matches(text);
        if (matches.Count == 0)
        {
            var ft = new FormattedText(text, CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, typeface, fontSize, new SolidColorBrush(textColor))
            { MaxTextWidth = maxWidth };
            dc.DrawText(ft, new Point(x, y));
            return ft.Height;
        }

        // Split into alternating runs; emit each with the right colour.
        // Render in one FormattedText with spans so wrapping behaves
        // naturally — Avalonia's TextSpan covers ranges within the text.
        var ft2 = new FormattedText(text, CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, typeface, fontSize, new SolidColorBrush(textColor))
        { MaxTextWidth = maxWidth };
        var numberBrush = new SolidColorBrush(numberColor);
        foreach (System.Text.RegularExpressions.Match m in matches)
            ft2.SetForegroundBrush(numberBrush, m.Index, m.Length);
        dc.DrawText(ft2, new Point(x, y));
        return ft2.Height;
    }

    private void DrawHoverInfo(DrawingContext dc, TreeNodeDto node, bool alloc, int csPathDist, bool busy)
    {
        // Try fetching the modern hover packet (mod text + stat diff + path).
        // Cache the result while the cursor stays on the same node and the
        // alloc set doesn't change. While a toggle's Lua is in flight we don't
        // query it (it would be stale) — show the mods + a loading spinner.
        NodeHoverInfo? info = null;
        if (HoverInfoProvider != null && !busy)
        {
            if (_hoverCacheNodeId == node.Id
                && ReferenceEquals(_hoverCacheAllocRef, AllocatedIds))
            {
                info = _hoverCache;
            }
            else
            {
                try { info = HoverInfoProvider(node.Id); }
                catch { info = null; }
                _hoverCache         = info;
                _hoverCacheNodeId   = node.Id;
                _hoverCacheAllocRef = AllocatedIds;
            }
        }

        const double Pad      = 14;
        const double MaxW     = 440;
        const double LineGap  = 4;
        const double SecGap   = 10;
        double contentW = MaxW - Pad * 2;

        // Match the rest of the app's UI font — Inter falls back to Segoe UI
        // on systems without it, same chain as Tokens.Typography.axaml.
        var titleFace = new Typeface("Inter, Segoe UI, Arial", FontStyle.Normal, FontWeight.SemiBold);
        var bodyFace  = new Typeface("Inter, Segoe UI, Arial");
        var smallFace = new Typeface("Inter, Segoe UI, Arial", FontStyle.Normal, FontWeight.Medium);

        var blocks = new List<(FormattedText ft, Color col, double topGap)>();

        // ── Title block ────────────────────────────────────────────────────
        var displayName = !string.IsNullOrEmpty(info?.Name)
            ? GameTranslationService.TPassiveName(info!.Name)
            : GameTranslationService.TPassiveName(node.Name);
        var titleColor  = NodeTitleColor(info?.Type ?? node.Type, alloc);
        var titleFt = new FormattedText(displayName, CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, titleFace, 17, new SolidColorBrush(titleColor))
        { MaxTextWidth = contentW };
        blocks.Add((titleFt, titleColor, 0));

        // Type / ascendancy caption
        var typeText = info?.Type ?? node.Type;
        var ascText  = info?.AscendancyName ?? node.AscendancyName;
        var subtitle = string.IsNullOrEmpty(ascText) ? typeText : $"{typeText} · {ascText}";
        var subFt = new FormattedText(subtitle.ToUpperInvariant(), CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, smallFace, 11, new SolidColorBrush(TextMutedC))
        { MaxTextWidth = contentW };
        blocks.Add((subFt, TextMutedC, 1));

        // ── Mod lines ──────────────────────────────────────────────────────
        IEnumerable<string> modSource = info?.Mods is { Length: > 0 }
            ? info.Mods.Select(GameTranslationService.TPassiveStat)
            : node.Stats.Select(GameTranslationService.TPassiveStat);

        bool firstMod = true;
        foreach (var raw in modSource)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            // Pre-measure so we can choose mod-block typography uniformly.
            var modFt = new FormattedText(raw, CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, bodyFace, 13, new SolidColorBrush(ModExplicitC))
            { MaxTextWidth = contentW };
            // Highlight numbers in brand gold.
            foreach (System.Text.RegularExpressions.Match m in NumberRegex.Matches(raw))
                modFt.SetForegroundBrush(new SolidColorBrush(Brand400C), m.Index, m.Length);
            blocks.Add((modFt, ModExplicitC, firstMod ? SecGap : 0));
            firstMod = false;
        }

        // ── Stat diff blocks ───────────────────────────────────────────────
        static string LocalizeHeader(string key) => key switch
        {
            "alloc"        => LocalizationService.Get("Tree_Tip_AllocGives"),
            "unalloc"      => LocalizationService.Get("Tree_Tip_UnallocGives"),
            "pathAlloc"    => LocalizationService.Get("Tree_Tip_PathAllocGives"),
            "pathUnalloc"  => LocalizationService.Get("Tree_Tip_PathUnallocGives"),
            _              => key,
        };

        void EmitDiffs(string headerKey, NodeStatDiff[] diffs)
        {
            if (diffs.Length == 0) return;
            var header = LocalizeHeader(headerKey);
            var hdrFt = new FormattedText(header, CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, bodyFace, 12, new SolidColorBrush(TextSecondaryC))
            { MaxTextWidth = contentW };
            blocks.Add((hdrFt, TextSecondaryC, SecGap));

            var perPointSuffix = " " + LocalizationService.Get("Tree_Tip_PerPointFmt");

            foreach (var d in diffs)
            {
                var col = d.IsPositive ? SuccessC : DangerC;
                var label = GameTranslationService.TCalcLabel(d.Label);
                var line = $"{d.ValueText}  {label}";
                if (!string.IsNullOrEmpty(d.PercentText)) line += "  " + d.PercentText;
                // Per-point delta comes from Lua as just the raw value
                // ("+5%"); wrap with "[value per pt]" using the current
                // language. Empty for single-node diffs.
                string perPointBracket = "";
                if (!string.IsNullOrEmpty(d.PerPointText))
                {
                    perPointBracket = $"[{d.PerPointText}{perPointSuffix}]";
                    line += "  " + perPointBracket;
                }
                var ft = new FormattedText(line, CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, bodyFace, 13, new SolidColorBrush(TextPrimaryC))
                { MaxTextWidth = contentW };
                int valLen = d.ValueText.Length;
                ft.SetForegroundBrush(new SolidColorBrush(col), 0, valLen);
                if (perPointBracket.Length > 0)
                {
                    int idx = line.IndexOf(perPointBracket, StringComparison.Ordinal);
                    if (idx > 0)
                        ft.SetForegroundBrush(new SolidColorBrush(TextMutedC), idx, perPointBracket.Length);
                }
                if (!string.IsNullOrEmpty(d.PercentText))
                {
                    int idx = line.IndexOf(d.PercentText, StringComparison.Ordinal);
                    if (idx > 0)
                        ft.SetForegroundBrush(new SolidColorBrush(TextSecondaryC), idx, d.PercentText.Length);
                }
                blocks.Add((ft, TextPrimaryC, 0));
            }
        }

        int spinnerBlockIndex = -1;
        if (info != null)
        {
            // If the node has mods but no measurable diff (e.g. specialty
            // nodes that affect a skill the player isn't using), show a
            // small footnote so the empty space doesn't look like a bug.
            bool emittedAny = info.StatDiffs.Length > 0 || info.PathStatDiffs.Length > 0;
            EmitDiffs(info.DiffHeader, info.StatDiffs);
            EmitDiffs(info.PathDiffHeader, info.PathStatDiffs);
            if (!emittedAny && info.Mods.Length > 0 && !info.IsAllocated)
            {
                var noteFt = new FormattedText(LocalizationService.Get("Tree_Tip_NoChange"),
                    CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    bodyFace, 12, new SolidColorBrush(TextMutedC))
                { MaxTextWidth = contentW };
                blocks.Add((noteFt, TextMutedC, SecGap));
            }
        }
        else if (busy)
        {
            // Stat diff is still computing on the background Lua thread — show a
            // spinner (drawn in the paint loop over the leading indent).
            var calcFt = new FormattedText("      " + LocalizationService.Get("Tree_Tip_Calculating"),
                CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                smallFace, 12, new SolidColorBrush(TextSecondaryC))
            { MaxTextWidth = contentW };
            blocks.Add((calcFt, TextSecondaryC, SecGap));
            spinnerBlockIndex = blocks.Count - 1;
        }

        // Points-to-node footer. Use the Lua value when present, else the
        // instant C# path distance so it shows even while the diff is loading.
        int pathDist = info?.PathDist ?? csPathDist;
        if (pathDist > 0)
        {
            var pathStr = pathDist == 1
                ? LocalizationService.Get("Tree_Tip_OnePoint")
                : string.Format(LocalizationService.Get("Tree_Tip_PointsFmt"), pathDist);
            var pathFt = new FormattedText(pathStr, CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, smallFace, 12, new SolidColorBrush(TextMutedC))
            { MaxTextWidth = contentW };
            blocks.Add((pathFt, TextMutedC, SecGap));
        }

        // ── Layout & paint ─────────────────────────────────────────────────
        double panelW = Math.Max(160, blocks.Max(b => b.ft.Width)) + Pad * 2;
        double panelH = Pad * 2 + blocks.Sum(b => b.ft.Height + LineGap + b.topGap) - LineGap;

        // Position the panel near the hovered node. Try right of the node
        // first; if it would overflow off-canvas, place it on the left.
        // Then clamp vertically so the entire panel stays visible.
        var (nx, ny) = W2S(node);
        double nodeR = GetRadius(node.Type);
        double gap   = 18;
        double px    = nx + nodeR + gap;
        if (px + panelW + 4 > Bounds.Width)
            px = nx - nodeR - gap - panelW;
        double py = ny - panelH / 2;
        if (py + panelH + 4 > Bounds.Height) py = Bounds.Height - panelH - 4;
        if (py < 4) py = 4;
        // Final fallback — keep within left edge if both right and left were rejected.
        if (px < 4) px = 4;

        dc.DrawRectangle(HoverBgBrush, HoverBorderPen, new Rect(px, py, panelW, panelH), 6, 6);

        double ty = py + Pad;
        for (int i = 0; i < blocks.Count; i++)
        {
            var (ft, _, topGap) = blocks[i];
            ty += topGap;
            dc.DrawText(ft, new Point(px + Pad, ty));
            if (i == spinnerBlockIndex)
                DrawSpinner(dc, px + Pad + 7, ty + ft.Height / 2, 6, _spinnerAngle);
            ty += ft.Height + LineGap;
        }
    }

    // Small rotating spinner: a fading trail of dots around a circle.
    private static void DrawSpinner(DrawingContext dc, double cx, double cy, double r, double angle)
    {
        const int dots = 8;
        for (int i = 0; i < dots; i++)
        {
            double a = angle + i * (2 * Math.PI / dots);
            double x = cx + Math.Cos(a) * r;
            double y = cy + Math.Sin(a) * r;
            byte alpha = (byte)(40 + 215 * i / (dots - 1));
            var brush = new SolidColorBrush(Color.FromArgb(alpha, Brand400C.R, Brand400C.G, Brand400C.B));
            dc.DrawEllipse(brush, null, new Point(x, y), 1.6, 1.6);
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
            // LMB click (not drag)
            if (d < 5.0 && _hoveredNode != null)
            {
                // An already-allocated jewel socket opens the in-tree jewel picker
                // instead of re-allocating; everything else allocs / changes attribute.
                if (_hoveredNode.Type == "Socket" && AllocatedIds?.Contains(_hoveredNode.Id) == true
                    && SocketClicked != null)
                {
                    var (scx, scy) = W2S(_hoveredNode);
                    SocketClicked(_hoveredNode.Id, new Point(scx, scy));
                }
                else
                {
                    AllocNodeCommand?.Execute(_hoveredNode.Id);
                }
            }
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
            QueueViewSave();
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
            UpdateSpinnerTimer();
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
        QueueViewSave();
        e.Handled = true;
    }

    // ── Programmatic view control (testing / automation hooks) ──────────────
    // Used by the IPC bridge (visual_tree_* MCP tools) to drive zoom / pan /
    // focus without a real pointer, so a screenshot can frame a specific node.

    private const double MinViewScale = 0.04;
    private const double MaxViewScale = 2.5;

    /// <summary>Current view as (scale, worldCenterX, worldCenterY) — the world
    /// point currently under the viewport centre.</summary>
    public (double Scale, double CenterX, double CenterY) GetViewState()
    {
        double w = Bounds.Width  > 0 ? Bounds.Width  : 1;
        double h = Bounds.Height > 0 ? Bounds.Height : 1;
        double cx = (w / 2 - _offsetX) / _scale;
        double cy = (h / 2 - _offsetY) / _scale;
        return (_scale, cx, cy);
    }

    /// <summary>Set zoom and/or the world point under the viewport centre. Any
    /// argument left null keeps its current value.</summary>
    public void SetViewState(double? scale, double? centerX, double? centerY)
    {
        var (curScale, curCx, curCy) = GetViewState();
        double s  = scale.HasValue ? Math.Clamp(scale.Value, MinViewScale, MaxViewScale) : curScale;
        double cx = centerX ?? curCx;
        double cy = centerY ?? curCy;
        ApplyView(s, cx, cy);
    }

    /// <summary>Centre the view on a node (by id) and optionally zoom. Returns
    /// false when the node id is not present in the current tree.</summary>
    public bool FocusNode(int nodeId, double? scale = null)
    {
        if (!_nodeById.TryGetValue(nodeId, out var node)) return false;
        var (wx, wy) = EffectiveWorld(node);
        double s = scale.HasValue ? Math.Clamp(scale.Value, MinViewScale, MaxViewScale) : _scale;
        ApplyView(s, wx, wy);
        return true;
    }

    /// <summary>Programmatically fire the socket-click (open the jewel picker) for a
    /// node id, as if the user clicked it. Used by the IPC test tools. No-op if the
    /// node is not a socket present in the current tree.</summary>
    public void TriggerSocketClick(int nodeId)
    {
        if (SocketClicked == null) return;
        if (!_nodeById.TryGetValue(nodeId, out var node) || node.Type != "Socket") return;
        var (sx, sy) = W2S(node);
        SocketClicked(nodeId, new Point(sx, sy));
    }

    /// <summary>Multiply zoom around the viewport centre (e.g. 1.15 = one wheel notch in).</summary>
    public void ZoomBy(double factor)
    {
        if (factor <= 0) return;
        var (curScale, curCx, curCy) = GetViewState();
        ApplyView(Math.Clamp(curScale * factor, MinViewScale, MaxViewScale), curCx, curCy);
    }

    /// <summary>Scroll the view by a screen-pixel delta (positive dx moves content right).</summary>
    public void PanByPixels(double dx, double dy)
    {
        _offsetX += dx;
        _offsetY += dy;
        _fitNeeded = false;
        InvalidateVisual();
    }

    private void ApplyView(double scale, double centerWorldX, double centerWorldY)
    {
        SetViewTransform(scale, centerWorldX, centerWorldY);
        InvalidateVisual();
    }

    // Mutates the view transform only, WITHOUT requesting a repaint. Safe to call
    // from inside Render() (restoring the persisted framing on first paint) — there,
    // InvalidateVisual() throws "Visual was invalidated during the render pass", which
    // crashes the render loop and leaves the tab stuck on its previous content.
    private void SetViewTransform(double scale, double centerWorldX, double centerWorldY)
    {
        double w = Bounds.Width  > 0 ? Bounds.Width  : 1;
        double h = Bounds.Height > 0 ? Bounds.Height : 1;
        _scale   = scale;
        _offsetX = w / 2 - centerWorldX * scale;
        _offsetY = h / 2 - centerWorldY * scale;
        _fitNeeded = false;
        if (_staticLayer != null && _scale > _staticLayerScale * 1.5)
            InvalidateStaticLayer();
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    // Search matches a node when the typed text is contained in either its
    // English name or its name translated into the current UI language, so the
    // search box works bilingually (EN + the language set in settings) at once.
    private static bool NodeMatchesSearch(TreeNodeDto node, string search)
    {
        if (node.Name.Contains(search, StringComparison.OrdinalIgnoreCase))
            return true;
        var localized = GameTranslationService.TPassiveName(node.Name);
        return !string.IsNullOrEmpty(localized)
            && !localized.Equals(node.Name, StringComparison.Ordinal)
            && localized.Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Test/IPC hook: hover a node by id, or auto-pick an unallocated
    /// node a few hops from the allocated tree, so the path preview can be
    /// captured in a screenshot. Returns the chosen node id, name, path length.</summary>
    public (int Id, string Name, int PathLen)? HoverNodeForTest(int? nodeId)
    {
        var alloc = AllocatedIds;
        TreeNodeDto? target = nodeId is { } id && _nodeById.TryGetValue(id, out var n)
            ? n
            : PickPathHoverCandidate(alloc);
        if (target is null) return null;

        _hoveredNode = target;
        InvalidateVisual();

        int len = 0;
        if (alloc is { Count: > 0 } && !alloc.Contains(target.Id))
            len = (GetHoverPath(target, alloc, AscendancyFilter)?.Count ?? 1) - 1;
        return (target.Id, target.Name, len);
    }

    // BFS from the allocated set; return a visible main-tree node ~3 hops out
    // (a route long enough to show several segments), else the farthest reached.
    private TreeNodeDto? PickPathHoverCandidate(IReadOnlySet<int>? alloc)
    {
        if (alloc == null || alloc.Count == 0) return null;
        var visited = new HashSet<int>(alloc);
        var queue   = new Queue<(int id, int dist)>();
        foreach (var a in alloc) queue.Enqueue((a, 0));
        TreeNodeDto? best = null;
        while (queue.Count > 0)
        {
            var (cur, dist) = queue.Dequeue();
            if (!_nodeById.TryGetValue(cur, out var curNode)) continue;
            bool curAsc = !string.IsNullOrEmpty(curNode.AscendancyName);
            foreach (var lid in curNode.LinkedIds)
            {
                if (!visited.Add(lid)) continue;
                if (!_nodeById.TryGetValue(lid, out var lnode)) continue;
                if (!IsNodeVisible(lnode, AscendancyFilter)) continue;
                if (curAsc != !string.IsNullOrEmpty(lnode.AscendancyName)) continue;
                if (lnode.Type is not ("Mastery" or "ClassStart" or "AscendClassStart"))
                {
                    best = lnode;
                    if (dist + 1 >= 3) return lnode;
                }
                queue.Enqueue((lid, dist + 1));
            }
        }
        return best;
    }

    /// <summary>Shortest path from the allocated tree to <paramref name="target"/>
    /// via node links (multi-source BFS from the allocated set). Returns the
    /// ordered ids target → … → first allocated anchor, or null when no path
    /// exists. Cached per (target, alloc-set) so pan/zoom doesn't recompute.</summary>
    private List<int>? GetHoverPath(TreeNodeDto target, IReadOnlySet<int> alloc, string filter)
    {
        if (_hoverPathTargetId == target.Id && ReferenceEquals(_hoverPathAllocRef, alloc))
            return _hoverPath;
        _hoverPathTargetId = target.Id;
        _hoverPathAllocRef = alloc;

        var visited = new HashSet<int>(alloc);
        var parent  = new Dictionary<int, int>();
        var queue   = new Queue<int>(alloc);
        bool found  = false;
        while (queue.Count > 0)
        {
            int cur = queue.Dequeue();
            if (cur == target.Id) { found = true; break; }
            if (!_nodeById.TryGetValue(cur, out var curNode)) continue;
            bool curAsc = !string.IsNullOrEmpty(curNode.AscendancyName);
            foreach (var lid in curNode.LinkedIds)
            {
                if (!visited.Add(lid)) continue;
                if (!_nodeById.TryGetValue(lid, out var lnode)) continue;
                if (!IsNodeVisible(lnode, filter)) continue;
                // Don't route across the main-tree ↔ ascendancy boundary.
                if (curAsc != !string.IsNullOrEmpty(lnode.AscendancyName)) continue;
                parent[lid] = cur;
                queue.Enqueue(lid);
            }
        }

        if (!found) return _hoverPath = null;
        var path = new List<int> { target.Id };
        int n = target.Id;
        while (parent.TryGetValue(n, out var p))
        {
            path.Add(p);
            n = p;
            if (alloc.Contains(p)) break;
        }
        return _hoverPath = path;
    }

    // Returns true for main-tree nodes always; for ascendancy nodes only when their name matches filter
    private bool IsNodeVisible(TreeNodeDto node, string filter) =>
        string.IsNullOrEmpty(node.AscendancyName) ||
        (!string.IsNullOrEmpty(filter) && node.AscendancyName == filter);

    // Effective world-space position after the ascendancy transform (recenter
    // into the background circle + shrink to fit inside the plate).
    private (double wx, double wy) EffectiveWorld(TreeNodeDto node)
    {
        double wx = node.X, wy = node.Y;
        if (!string.IsNullOrEmpty(node.AscendancyName) &&
            _ascendTransforms.TryGetValue(node.AscendancyName, out var t))
        {
            wx = t.tx + (node.X - t.cx) * t.k;
            wy = t.ty + (node.Y - t.cy) * t.k;
        }
        return (wx, wy);
    }

    private (double sx, double sy) W2S(TreeNodeDto node)
    {
        var (wx, wy) = EffectiveWorld(node);
        return (wx * _scale + _offsetX, wy * _scale + _offsetY);
    }

    // Compute per-ascendancy transform that brings each sub-tree centroid to the
    // center of its background circle (tree-data position next to the class start)
    // and shrinks the subtree, if needed, so it stays inside the plate.
    // Ascendancies without a known background fall back to world (0,0), unscaled.
    private void ComputeAscendOffsets()
    {
        _ascendTransforms.Clear();
        _ascendRadii.Clear();
        var nodes = Nodes;
        if (nodes == null) return;
        var bgs = AscendancyBackgrounds;
        var filter = AscendancyFilter;

        // Radius of the main tree's innermost ring (class-start nodes). The
        // selected ascendancy plate is drawn out to this radius and its nodes
        // are scaled to fit inside it.
        double inner = double.MaxValue;
        foreach (var n in nodes)
        {
            if (!string.IsNullOrEmpty(n.AscendancyName)) continue;
            double r = Math.Sqrt(n.X * n.X + n.Y * n.Y);
            if (r > 1 && r < inner) inner = r;
        }
        if (inner < double.MaxValue) _mainInnerRadius = inner;

        // Bounding box per ascendancy — its center keeps the cluster visually
        // centered in the circle (a centroid drifts toward dense node areas).
        var groups = new Dictionary<string, (double minX, double minY, double maxX, double maxY)>();
        foreach (var n in nodes)
        {
            if (string.IsNullOrEmpty(n.AscendancyName)) continue;
            if (!groups.TryGetValue(n.AscendancyName, out var g))
                g = (double.MaxValue, double.MaxValue, double.MinValue, double.MinValue);
            groups[n.AscendancyName] = (
                Math.Min(g.minX, n.X), Math.Min(g.minY, n.Y),
                Math.Max(g.maxX, n.X), Math.Max(g.maxY, n.Y));
        }
        foreach (var (name, g) in groups)
        {
            double cx = (g.minX + g.maxX) * 0.5;
            double cy = (g.minY + g.maxY) * 0.5;

            // Max node distance from the box center (world units).
            double maxR = 0;
            foreach (var n in nodes!)
            {
                if (n.AscendancyName != name) continue;
                double d = Math.Sqrt((n.X - cx) * (n.X - cx) + (n.Y - cy) * (n.Y - cy));
                if (d > maxR) maxR = d;
            }
            _ascendRadii[name] = maxR;

            double tx = 0, ty = 0, k = 1.0;
            if (bgs != null && bgs.TryGetValue(name, out var bg))
            {
                // The selected ascendancy's branch is drawn at the tree center
                // (matching the center plate at world (0,0)); the others keep
                // their class-circle position (they're hidden anyway).
                bool isSelected = !string.IsNullOrEmpty(filter) && name == filter;
                tx = isSelected ? 0 : bg.X;
                ty = isSelected ? 0 : bg.Y;
                // Fit inside the plate. Node positions shrink but icons keep
                // their world size, so reserve a margin of one large notable
                // frame past the outermost node center. The selected branch fits
                // the big centered plate (radius = main inner ring); the others
                // fit their own class-circle plate.
                double half   = isSelected ? _mainInnerRadius : Math.Min(bg.Width, bg.Height) * 0.5;
                double margin = isSelected ? 120 : 180;
                if (maxR > 0 && half > margin)
                    k = Math.Min(1.0, (half - margin) / maxR);
            }
            _ascendTransforms[name] = (cx, cy, tx, ty, k);
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

        // Reference scale: bake at a moderate density with headroom for zoom,
        // but cap so the larger world axis fits within RenderTargetBitmap's
        // 4096 px limit — otherwise the bake would clip nodes near the far
        // edges of the tree (PoE2 main tree is ~32k × 32k world units).
        double maxWorld   = Math.Max(maxX - minX, maxY - minY);
        const int MaxPx   = 4000; // a touch under 4096 to leave room for pad
        double fitScale   = MaxPx / maxWorld;
        double refScale   = Math.Min(Math.Max(_scale * 1.2, 0.15), fitScale);

        int pxW = Math.Max(64, (int)((maxX - minX) * refScale));
        int pxH = Math.Max(64, (int)((maxY - minY) * refScale));

        var bmp = new Avalonia.Media.Imaging.RenderTargetBitmap(
            new PixelSize(pxW, pxH), new Vector(96, 96));
        using (var ctx = bmp.CreateDrawingContext())
        {
            // World → bitmap: (wx - minX) * refScale
            // First pass: connections (neutral pen) — node icons drawn on top
            // will fully cover the line endpoints near each node.
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

            // Node bake removed — small unallocated nodes rendered poorly when
            // the bitmap was upscaled at the user's typical zoom (PoE2 tree is
            // ~32k world units, so refScale gets clamped low to fit 4096 px;
            // tiny per-node features lose detail through bilinear filtering).
            // Connections bake cleanly because they're sub-pixel lines that
            // antialias well at any scale; node icons / frames / dim overlays
            // do not. Per-frame node draw loop kept intact.
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

    /// <summary>Draw every ascendancy background plate at its tree-data position
    /// (the circle adjacent to each class start), all at the uniform data size
    /// (1500×1500 world units). The selected ascendancy is bright, the rest are
    /// dimmed — mirrors original PoB's PassiveTreeView behaviour. Plates that
    /// share one circle (replacement ascendancies like Lich / Abyssal Lich)
    /// collapse to a single image, preferring the selected one.</summary>
    private void DrawAscendancyBackgrounds(DrawingContext dc, string selected)
    {
        var assets = AssetStore;
        var bgs = AscendancyBackgrounds;
        if (assets == null || bgs == null || bgs.Count == 0) return;

        // Center plate: the chosen ascendancy's artwork (or the class plate when
        // no ascendancy is picked) at world (0,0) — mirrors original PoB, which
        // draws class.background at the tree center.
        {
            // Both the class plate (no ascendancy picked) and the chosen
            // ascendancy plate are blown up so their circular art meets the main
            // tree's innermost (class-start) ring — the plate edge sits right
            // against the surrounding start nodes.
            string centerImage = ClassBackgroundImage;
            double w = _mainInnerRadius * 2, h = _mainInnerRadius * 2;
            if (!string.IsNullOrEmpty(selected) && bgs.TryGetValue(selected, out var selBg))
                centerImage = selBg.Image;
            var centerSprite = assets.GetSprite(centerImage);
            if (centerSprite.HasValue)
            {
                var (cBmp, cSrc) = centerSprite.Value;
                double halfW = w * 0.5 * _scale;
                double halfH = h * 0.5 * _scale;
                var rect = new Rect(_offsetX - halfW, _offsetY - halfH, halfW * 2, halfH * 2);
                if (!(rect.Right < 0 || rect.Left > Bounds.Width ||
                      rect.Bottom < 0 || rect.Top > Bounds.Height))
                    dc.DrawImage(cBmp, cSrc, rect);
            }
        }

        // When an ascendancy is selected, only its branch (re-centered to world
        // (0,0)) and the center plate above are shown; the surrounding class
        // circles are hidden. With nothing selected, fall through and draw all
        // class plates dimmed so the available ascendancy circles stay visible.
        if (!string.IsNullOrEmpty(selected)) return;

        // Collapse plates sharing the same circle (replacement ascendancies).
        var byPos = new Dictionary<(long, long), AscendancyBgDto>();
        foreach (var bg in bgs.Values)
        {
            var key = ((long)Math.Round(bg.X), (long)Math.Round(bg.Y));
            if (!byPos.TryGetValue(key, out var cur) ||
                bg.Id.Equals(selected, StringComparison.OrdinalIgnoreCase))
                byPos[key] = bg;
            else if (cur.Id.Equals(selected, StringComparison.OrdinalIgnoreCase))
                { /* keep the selected one */ }
        }

        foreach (var bg in byPos.Values)
        {
            var sprite = assets.GetSprite(bg.Image);
            if (!sprite.HasValue) continue;
            var (bmp, src) = sprite.Value;

            double halfW = bg.Width  * 0.5 * _scale;
            double halfH = bg.Height * 0.5 * _scale;
            double cx = bg.X * _scale + _offsetX;
            double cy = bg.Y * _scale + _offsetY;

            var destRect = new Rect(cx - halfW, cy - halfH, halfW * 2, halfH * 2);
            if (destRect.Right < 0 || destRect.Left > Bounds.Width ||
                destRect.Bottom < 0 || destRect.Top > Bounds.Height) continue;

            bool isSelected = bg.Id.Equals(selected, StringComparison.OrdinalIgnoreCase);
            using (dc.PushOpacity(isSelected ? 1.0 : 0.5))
                dc.DrawImage(bmp, src, destRect);
        }
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
