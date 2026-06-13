using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using System.Linq;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PBLApp.Controls;
using PBLApp.ViewModels;

namespace PBLApp.Views;

public partial class SkillsTabView : UserControl
{
    public SkillsTabView()
    {
        InitializeComponent();
        LayoutPersistence.Bind(RootGrid, "SkillsTab.Columns", 0);
    }

    // ── "+ Add Skill" picker flyout ───────────────────────────────────────

    private void AddSkillFlyout_Opened(object? sender, System.EventArgs e)
    {
        if (DataContext is SkillsTabViewModel vm)
            vm.NewGroupSearch = "";
        Dispatcher.UIThread.Post(() => PickerSearch?.Focus(), DispatcherPriority.Background);
    }

    private void PickerSearch_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Down)
        {
            FocusFirstRow(PickerList);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            // Enter from the search box commits the top-most filtered result.
            if (DataContext is SkillsTabViewModel vm
                && vm.FilteredActiveGemNameItems.FirstOrDefault() is { } item)
                CommitSkill(item);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            ClosePickerFlyout();
            e.Handled = true;
        }
    }

    // Each row is its own button — a single click commits directly.
    private void PickerRow_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: GemNameItem item }) CommitSkill(item);
    }

    private void CommitSkill(GemNameItem item)
    {
        if (DataContext is SkillsTabViewModel vm)
        {
            vm.AddGroupFromPicker(item.Name);
            ClosePickerFlyout();
        }
    }

    private void ClosePickerFlyout()
    {
        if (AddSkillButton?.Flyout is FlyoutBase fb) fb.Hide();
    }

    // ── Group list (left) ─────────────────────────────────────────────────

    // Each group row is a Border (not Button — see group-row style comment);
    // pressing it selects the group. Selection highlight is rendered via the
    // row's .selected class and fills the full row width.
    private void GroupRow_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border { DataContext: SkillGroupViewModel group }
            && DataContext is SkillsTabViewModel vm)
            vm.SelectedGroup = group;
    }

    // ── Active gem (header) ───────────────────────────────────────────────

    private void ActiveGemNameBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox cb && cb.DataContext is SkillGroupViewModel group
            && cb.SelectedItem is GemNameItem && group.ActiveGem is { } gem)
            gem.CommitName();
    }

    private void ActiveGemNameBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is ComboBox cb
            && cb.DataContext is SkillGroupViewModel group && group.ActiveGem is { } gem)
            gem.CommitName();
    }

    private void ActiveGemNameBox_LostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is ComboBox cb && cb.DataContext is SkillGroupViewModel group
            && group.ActiveGem is { } gem)
            gem.CommitName();
    }

    // ── Support gem slots: Button + Flyout picker ─────────────────────────

    private void SupportPickerFlyout_Opened(object? sender, System.EventArgs e)
    {
        if (sender is not Flyout flyout || flyout.Content is not Control content) return;

        // Reset search + attribute tab and recompute the installed/max counters
        // (they depend on every group's current contents). Then focus the search.
        if (content.DataContext is GemViewModel gem)
        {
            gem.SearchText = "";
            gem.Tab = GemTab.All;
            gem.RefreshPickerCounts();
        }

        var search = content.FindControl<TextBox>("SupportPickerSearch");
        Dispatcher.UIThread.Post(() => search?.Focus(), DispatcherPriority.Background);
    }

    // Bottom attribute-tab click: set the active filter on the host GemViewModel.
    private void AttrTab_Pressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border { DataContext: GemViewModel gem, Tag: string tag }
            && System.Enum.TryParse<GemTab>(tag, out var tab))
            gem.Tab = tab;
    }

    private void SupportPickerSearch_KeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox tb) return;
        var list = (tb.Parent as Control)?.FindControl<ListBox>("SupportPickerList");
        if (list is null) return;

        if (e.Key == Key.Down)
        {
            list.Focus();
            if (list.ItemCount > 0 && list.SelectedIndex < 0) list.SelectedIndex = 0;
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            if (list.ItemCount > 0)
            {
                if (list.SelectedIndex < 0) list.SelectedIndex = 0;
                CommitPickedSupport(list);
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CloseSupportFlyout(tb);
            e.Handled = true;
        }
    }

    // Single-click commit: a tap sets the ListBox SelectedItem, then we commit it.
    // Guard against taps on the scrollbar / empty area below the rows.
    private void SupportPickerList_Tapped(object? sender, TappedEventArgs e)
    {
        if (sender is ListBox list && TappedOnListItem(e)) CommitPickedSupport(list);
    }

    private static bool TappedOnListItem(TappedEventArgs e)
    {
        var c = e.Source as Avalonia.Visual;
        while (c is not null)
        {
            if (c is ListBoxItem) return true;
            c = c.GetVisualParent();
        }
        return false;
    }

    private void SupportPickerList_KeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not ListBox list) return;
        if (e.Key == Key.Enter)       { CommitPickedSupport(list); e.Handled = true; }
        else if (e.Key == Key.Escape) { CloseSupportFlyout(list);  e.Handled = true; }
    }

    /// <summary>Commit the currently-selected GemNameItem on the support picker
    /// list into the host GemViewModel and close the flyout.</summary>
    private void CommitPickedSupport(ListBox list)
    {
        if (list.SelectedItem is not GemNameItem item) return;
        if (list.DataContext is not GemViewModel gem) return;

        // Use the existing CommitName plumbing: stuff the canonical English name
        // into SearchText and let the VM validate + persist via the Lua engine.
        gem.SearchText = item.Name;
        gem.CommitName();
        CloseSupportFlyout(list);
    }

    /// <summary>Walk up the visual tree to the nearest Button (the picker host)
    /// and hide its attached flyout.</summary>
    private static void CloseSupportFlyout(Control any)
    {
        Control? c = any;
        while (c is not null)
        {
            if (c is Button btn && btn.Flyout is FlyoutBase fb) { fb.Hide(); return; }
            c = c.Parent as Control;
        }
    }

    /// <summary>Move keyboard focus to the first row button of a picker
    /// ItemsControl, so the user can arrow through results after typing.</summary>
    private static void FocusFirstRow(ItemsControl? list)
    {
        var btn = list?.ContainerFromIndex(0)?.GetVisualDescendants()
                      .OfType<Button>().FirstOrDefault();
        btn?.Focus();
    }
}
