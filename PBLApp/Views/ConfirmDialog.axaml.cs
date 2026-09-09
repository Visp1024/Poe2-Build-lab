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
    /// user clicked the confirm (destructive) button. <paramref name="confirmText"/>
    /// подписывает эту кнопку — по умолчанию «Удалить», но диалог зовут и не только
    /// на удаление (например, «Обновить из игры»).</summary>
    public static async Task<bool> ShowAsync(Window owner, string title, string message,
        string? confirmText = null)
    {
        var dlg = new ConfirmDialog();
        dlg.TitleText.Text   = title;
        dlg.MessageText.Text = message;
        dlg.Title            = title;
        if (confirmText is { Length: > 0 }) dlg.ConfirmBtn.Content = confirmText;
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
