using Avalonia.Controls;
using Avalonia.Input.Platform;
using PBLApp.ViewModels;

namespace PBLApp.Views;

public partial class TraderTabView : UserControl
{
    public TraderTabView()
    {
        InitializeComponent();
        // Буфер обмена доступен только из визуального дерева — VM получает
        // делегат (паттерн PromptRenameAsync в BuildPageView).
        DataContextChanged += (_, _) =>
        {
            if (DataContext is TraderTabViewModel vm)
                vm.CopyToClipboardAsync = text =>
                    TopLevel.GetTopLevel(this)?.Clipboard?.SetTextAsync(text)
                        ?? System.Threading.Tasks.Task.CompletedTask;
        };
    }
}
