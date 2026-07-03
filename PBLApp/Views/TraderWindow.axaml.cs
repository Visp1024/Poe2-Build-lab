using Avalonia.Controls;
using Avalonia.Input.Platform;
using PBLApp.ViewModels;
using System.Threading.Tasks;

namespace PBLApp.Views;

public partial class TraderWindow : Window
{
    public TraderWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is TraderWindowViewModel vm)
                vm.Session.CopyToClipboardAsync = CopyAsync;
        };
    }

    private async Task CopyAsync(string text)
    {
        if (Clipboard is { } cb) await cb.SetTextAsync(text);
    }
}
