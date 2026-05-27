using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
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
            PickerList?.Focus();
            if (PickerList is { ItemCount: > 0 } && PickerList.SelectedIndex < 0)
                PickerList.SelectedIndex = 0;
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            if (PickerList is { ItemCount: > 0 })
            {
                if (PickerList.SelectedIndex < 0) PickerList.SelectedIndex = 0;
                CommitPickedSkill();
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            ClosePickerFlyout();
            e.Handled = true;
        }
    }

    private void PickerList_DoubleTapped(object? sender, TappedEventArgs e) => CommitPickedSkill();

    private void PickerList_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)       { CommitPickedSkill(); e.Handled = true; }
        else if (e.Key == Key.Escape) { ClosePickerFlyout(); e.Handled = true; }
    }

    private void CommitPickedSkill()
    {
        if (PickerList?.SelectedItem is GemNameItem item
            && DataContext is SkillsTabViewModel vm)
        {
            vm.AddGroupFromPicker(item.Name);
            ClosePickerFlyout();
        }
    }

    private void ClosePickerFlyout()
    {
        if (AddSkillButton?.Flyout is FlyoutBase fb) fb.Hide();
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

        // Reset the SearchText so the user starts with the full list, and focus
        // the search box. The DataContext propagates from the host Button.
        if (content.DataContext is GemViewModel gem)
            gem.SearchText = "";

        var search = content.FindControl<TextBox>("SupportPickerSearch");
        Dispatcher.UIThread.Post(() => search?.Focus(), DispatcherPriority.Background);
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

    private void SupportPickerList_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is ListBox list) CommitPickedSupport(list);
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
}
