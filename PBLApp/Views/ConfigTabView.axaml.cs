using Avalonia.Controls;
using Avalonia.Input;
using PBLApp.ViewModels;

namespace PBLApp.Views;

public partial class ConfigTabView : UserControl
{
    public ConfigTabView()
    {
        InitializeComponent();
    }

    // Click a card header → collapse/expand the section's options.
    private void SectionHeader_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border { DataContext: ConfigSectionViewModel section })
            section.IsExpanded = !section.IsExpanded;
        e.Handled = true;
    }
}
