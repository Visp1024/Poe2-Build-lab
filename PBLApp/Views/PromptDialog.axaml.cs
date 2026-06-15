using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace PBLApp.Views;

public partial class PromptDialog : Window
{
    private string? _result;

    public PromptDialog()
    {
        InitializeComponent();
    }

    /// <summary>Show as modal over <paramref name="owner"/>; resolves to the trimmed
    /// entered text, or null if the user cancelled.</summary>
    public static async Task<string?> ShowAsync(Window owner, string title, string message, string initialText)
    {
        var dlg = new PromptDialog();
        dlg.TitleText.Text   = title;
        dlg.MessageText.Text = message;
        dlg.Title            = title;
        dlg.InputBox.Text    = initialText;
        // Focus + select-all so the user can immediately overtype.
        dlg.Opened += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            dlg.InputBox.Focus();
            dlg.InputBox.SelectAll();
        });
        await dlg.ShowDialog(owner);
        return dlg._result;
    }

    private void Ok_Click(object? sender, RoutedEventArgs e) => Accept();

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        _result = null;
        Close();
    }

    private void InputBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { Accept(); e.Handled = true; }
        else if (e.Key == Key.Escape) { _result = null; Close(); e.Handled = true; }
    }

    private void Accept()
    {
        _result = (InputBox.Text ?? "").Trim();
        Close();
    }
}
