using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using PBLApp.ViewModels;

namespace PBLApp.Views;

public partial class ImportTabView : UserControl
{
    public ImportTabView()
    {
        InitializeComponent();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        if (DataContext is ImportTabViewModel vm)
            vm.CopyToClipboardRequested += CopyToClipboard;
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);
        if (DataContext is ImportTabViewModel vm)
            vm.CopyToClipboardRequested -= CopyToClipboard;
    }

    private async void CopyToClipboard(string text)
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard != null)
            await clipboard.SetTextAsync(text);
    }
}
