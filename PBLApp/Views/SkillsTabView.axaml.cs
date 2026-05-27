using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using PBLApp.ViewModels;

namespace PBLApp.Views;

public partial class SkillsTabView : UserControl
{
    public SkillsTabView()
    {
        InitializeComponent();
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

    // ── Support gem slots ─────────────────────────────────────────────────

    private void SupportGemNameBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox cb && cb.DataContext is GemViewModel gem
            && cb.SelectedItem is GemNameItem)
            gem.CommitName();
    }

    private void SupportGemNameBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is ComboBox cb && cb.DataContext is GemViewModel gem)
            gem.CommitName();
    }

    private void GemNameBox_LostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is ComboBox cb && cb.DataContext is GemViewModel gem)
            gem.CommitName();
    }
}
