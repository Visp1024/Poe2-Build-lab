using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace PBLApp.Views;

public partial class ConfirmDialog : Window
{
    private bool _result;

    public ConfirmDialog()
    {
        InitializeComponent();
    }

    /// <summary>Show as modal over <paramref name="owner"/>; resolves to true if the
    /// user clicked the confirm (destructive) button.</summary>
    public static async Task<bool> ShowAsync(Window owner, string title, string message)
    {
        var dlg = new ConfirmDialog();
        dlg.TitleText.Text   = title;
        dlg.MessageText.Text = message;
        dlg.Title            = title;
        await dlg.ShowDialog(owner);
        return dlg._result;
    }

    private void Confirm_Click(object? sender, RoutedEventArgs e)
    {
        _result = true;
        Close();
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        _result = false;
        Close();
    }
}
