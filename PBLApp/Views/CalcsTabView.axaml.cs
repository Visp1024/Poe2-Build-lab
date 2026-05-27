using Avalonia.Controls;
using Avalonia.Input;
using PBLApp.ViewModels;

namespace PBLApp.Views;

public partial class CalcsTabView : UserControl
{
    public CalcsTabView()
    {
        InitializeComponent();
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
}
