using Avalonia.Controls;
using Avalonia.Input;
using PBLApp.ViewModels;

namespace PBLApp.Views;

public partial class CalcsTabView : UserControl
{
    public CalcsTabView()
    {
        InitializeComponent();
        // Global handler: a press anywhere inside a stat row selects it and opens the
        // breakdown drawer. handledEventsToo:false so header/scrim handlers (which mark
        // the event Handled) opt out.
        AddHandler(PointerPressedEvent, OnAnyPointerPressed, handledEventsToo: false);
    }

    private void OnAnyPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not CalcsTabViewModel vm) return;

        var element = e.Source as Control;
        while (element is not null)
        {
            if (element.DataContext is StatRowViewModel row)
            {
                vm.SelectStatCommand.Execute(row);
                return;
            }
            element = element.Parent as Control;
        }
    }

    // Click a card header → toggle its collapsed state. Marked handled so the global
    // row-select handler doesn't also fire.
    private void SectionHeader_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border { DataContext: StatSectionViewModel section })
            section.IsCollapsed = !section.IsCollapsed;
        e.Handled = true;
    }

    // Click the scrim → dismiss the breakdown drawer.
    private void Scrim_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is CalcsTabViewModel vm)
            vm.CloseBreakdownCommand.Execute(null);
        e.Handled = true;
    }
}
