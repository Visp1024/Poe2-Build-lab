using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PBLApp.ViewModels;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace PBLApp.Views;

public partial class ItemsTabView : UserControl
{
    private static readonly DataFormat<string> DragFmt =
        DataFormat.CreateStringApplicationFormat("pob-items-drag");

    public ItemsTabView()
    {
        InitializeComponent();

        AddHandler(PointerPressedEvent,    OnPointerPressed, RoutingStrategies.Tunnel);
        AddHandler(PointerMovedEvent,      OnPointerMoved,   RoutingStrategies.Tunnel);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent,     OnDrop);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
    }

    // ── Drag source ───────────────────────────────────────────────────────

    private Point   _pressOrigin;
    private string? _pendingPayload;
    private PointerPressedEventArgs? _pressEvent;
    private Control? _hoverTarget;        // current slot/pool border under the pointer

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var tag = (e.Source as Control)?.FindAncestorTag();
        if (string.IsNullOrEmpty(tag) ||
            (!tag.StartsWith("slot:") && !tag.StartsWith("pool:")))
        {
            _pendingPayload = null;
            _pressEvent     = null;
            return;
        }
        _pressOrigin    = e.GetPosition(this);
        _pendingPayload = tag;
        _pressEvent     = e;
    }

    private async void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_pendingPayload is null || _pressEvent is null) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _pendingPayload = null;
            _pressEvent     = null;
            return;
        }

        var pos = e.GetPosition(this);
        if (System.Math.Abs(pos.X - _pressOrigin.X) < 6 &&
            System.Math.Abs(pos.Y - _pressOrigin.Y) < 6) return;

        var payload = _pendingPayload;
        var press   = _pressEvent;
        _pendingPayload = null;
        _pressEvent     = null;

        var transfer = new DataTransfer();
        transfer.Add(DataTransferItem.Create(DragFmt, payload));

        // Light up every valid drop target for this drag's source kind.
        var lit = HighlightCompatibleTargets(payload);
        // Force a frame so the highlight is painted before DoDragDropAsync
        // takes the UI thread into the platform's modal drag loop.
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
        try
        {
            await DragDrop.DoDragDropAsync(press, transfer, DragDropEffects.Move);
        }
        finally
        {
            ClearHighlights(lit);
            SetHoverTarget(null);
        }
    }

    // ── Drop target tracking ──────────────────────────────────────────────

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        var src = e.DataTransfer?.TryGetValue(DragFmt);
        var dstControl = FindTargetControl(e.Source as Control);
        var dstTag = dstControl?.Tag as string;

        // Hover/drop is allowed only when the destination is in the highlighted
        // compatible set built when the drag started (or is the unequip border).
        bool valid = !string.IsNullOrEmpty(src) && !string.IsNullOrEmpty(dstTag) && src != dstTag
                     && IsCompatible(src!, dstTag!)
                     && (dstTag == "unequip" || (dstControl is not null && _compatibleSlots.Contains(dstControl)));

        if (valid)
        {
            SetHoverTarget(dstControl);
            e.DragEffects = DragDropEffects.Move;
            e.Handled = true;
        }
        else
        {
            SetHoverTarget(null);
            e.DragEffects = DragDropEffects.None;
        }
    }

    private void OnDragLeave(object? sender, DragEventArgs e)
    {
        // The leave fires for individual elements as the pointer moves between them;
        // only clear when the pointer leaves the whole UserControl.
        var pos = e.GetPosition(this);
        if (pos.X < 0 || pos.Y < 0 || pos.X > Bounds.Width || pos.Y > Bounds.Height)
            SetHoverTarget(null);
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not ItemsTabViewModel vm) return;
        var src = e.DataTransfer?.TryGetValue(DragFmt);
        var dstTag = FindTargetControl(e.Source as Control)?.Tag as string;
        if (string.IsNullOrEmpty(src) || string.IsNullOrEmpty(dstTag) || src == dstTag) return;

        bool ok = (src, dstTag) switch
        {
            var (s, d) when s.StartsWith("pool:") && d.StartsWith("slot:") =>
                int.TryParse(s[5..], out int id) && vm.DragEquipPoolToSlot(id, d[5..]),
            var (s, d) when s.StartsWith("slot:") && d == "unequip" =>
                vm.DragUnequipSlot(s[5..]),
            var (s, d) when s.StartsWith("slot:") && d.StartsWith("slot:") =>
                vm.DragSwapSlots(s[5..], d[5..]),
            _ => false
        };
        if (ok) e.Handled = true;
    }

    // ── Highlight helpers ─────────────────────────────────────────────────

    private static bool IsCompatible(string src, string dst) => (src, dst) switch
    {
        // Pool item → any equipment slot
        var (s, d) when s.StartsWith("pool:") && d.StartsWith("slot:") => true,
        // Slot → pool list (unequip)
        var (s, d) when s.StartsWith("slot:") && d == "unequip"        => true,
        // Slot → another slot
        var (s, d) when s.StartsWith("slot:") && d.StartsWith("slot:") => true,
        _ => false
    };

    // Per-control snapshot of just the properties we'll mutate during a drag.
    // BorderBrush is intentionally NOT touched — slot buttons bind it to
    // {Binding *.NameColor}, and ClearValue would erase that binding.
    private readonly Dictionary<Control, double> _origOpacity = new();
    private readonly HashSet<Control> _compatibleSlots = new();
    private static readonly IBrush DropReadyBrush  = new SolidColorBrush(Color.Parse("#2D5A3D"));
    private static readonly IBrush DropHoverBrush  = new SolidColorBrush(Color.Parse("#4ABE7A"));

    private List<Control> HighlightCompatibleTargets(string sourceTag)
    {
        var touched = new List<Control>();
        _origOpacity.Clear();
        _compatibleSlots.Clear();

        if (DataContext is not ItemsTabViewModel vm) return touched;

        var allowedSlots = sourceTag switch
        {
            var s when s.StartsWith("pool:") && int.TryParse(s[5..], out int id) =>
                vm.CompatibleSlotsForPool(id),
            var s when s.StartsWith("slot:") =>
                vm.CompatibleSlotsForSlot(s[5..]),
            _ => new HashSet<string>()
        };

        foreach (var v in this.GetVisualDescendants())
        {
            if (v is not Control c || c.Tag is not string t || t.Length == 0) continue;
            if (t == sourceTag) continue;

            if (c is Button btn && btn.Classes.Contains("slot-cell") && t.StartsWith("slot:"))
            {
                _origOpacity[btn] = btn.Opacity;
                bool isCompatible = allowedSlots.Contains(t[5..]);
                if (isCompatible)
                {
                    btn.Background = DropReadyBrush;
                    _compatibleSlots.Add(btn);
                }
                else
                {
                    btn.Opacity = 0.35;
                }
                touched.Add(btn);
            }
            else if (c is Border br && t == "unequip" && sourceTag.StartsWith("slot:"))
            {
                _origOpacity[br] = br.Opacity;
                br.Background = DropReadyBrush;
                touched.Add(br);
            }
        }
        return touched;
    }

    private void ClearHighlights(List<Control> lit)
    {
        foreach (var c in lit)
        {
            // Background was set as a local value — clearing it lets the Style /
            // base value take over again. BorderBrush is left untouched (binding
            // remains intact). Opacity is restored from the snapshot.
            c.ClearValue(Avalonia.Controls.Primitives.TemplatedControl.BackgroundProperty);
            if (_origOpacity.TryGetValue(c, out var op)) c.Opacity = op;
        }
        _origOpacity.Clear();
        _compatibleSlots.Clear();
    }

    private void SetHoverTarget(Control? c)
    {
        if (ReferenceEquals(_hoverTarget, c)) return;
        if (_hoverTarget is Button oldBtn && _compatibleSlots.Contains(oldBtn))
            oldBtn.Background = DropReadyBrush;
        _hoverTarget = c;
        if (_hoverTarget is Button newBtn && _compatibleSlots.Contains(newBtn))
            newBtn.Background = DropHoverBrush;
    }

    /// <summary>Walks up from the e.Source until we find the slot Button or unequip Border.</summary>
    private static Control? FindTargetControl(Control? c)
    {
        while (c is not null)
        {
            if (c.Tag is string s && s.Length > 0
                && (s.StartsWith("slot:") || s == "unequip"))
                return c;
            c = c.Parent as Control;
        }
        return null;
    }
}

internal static class DragDropHelpers
{
    /// <summary>Walks up the visual tree to find the nearest Control whose Tag is a non-empty string.
    /// Used to identify slot / pool / unequip targets without per-element handlers.</summary>
    public static string? FindAncestorTag(this Control? c)
    {
        while (c is not null)
        {
            if (c.Tag is string s && s.Length > 0) return s;
            c = c.Parent as Control;
        }
        return null;
    }
}
