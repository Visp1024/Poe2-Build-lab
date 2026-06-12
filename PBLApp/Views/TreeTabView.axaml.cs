using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using PBLApp.Controls;
using PBLApp.Core.Localization;
using PBLApp.ViewModels;
using System;
using System.Threading.Tasks;

namespace PBLApp.Views;

public partial class TreeTabView : UserControl
{
    private TreeAssetStore? _store;

    public TreeTabView() => InitializeComponent();

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        _store?.Dispose();
        _store = null;

        if (DataContext is TreeTabViewModel vm && !string.IsNullOrEmpty(vm.RepoRoot))
        {
            _store = TreeAssetStore.TryLoad(vm.RepoRoot, vm.TreeVersion);
            var canvas = this.FindControl<TreeCanvas>("TreeCanvasControl");
            if (canvas != null)
            {
                canvas.AssetStore        = _store;
                canvas.HoverInfoProvider = vm.GetNodeHoverInfo;
            }

            vm.ConfirmClassChange = ShowClassChangeConfirmAsync;
            vm.SelectAttribute    = ShowAttributeSelectAsync;
        }
    }

    private async Task<int> ShowAttributeSelectAsync()
    {
        var parentWindow = TopLevel.GetTopLevel(this) as Window;
        if (parentWindow == null) return 0;

        var dialog = BuildAttributeSelectDialog();
        return await dialog.ShowDialog<int>(parentWindow);
    }

    private static Window BuildAttributeSelectDialog()
    {
        var dialog = new Window
        {
            Title          = LocalizationService.Get("Dlg_ChooseAttr_Title"),
            Width          = 320,
            SizeToContent  = SizeToContent.Height,
            CanResize      = false,
            ShowInTaskbar  = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background     = new SolidColorBrush(Color.Parse("#1E1E2E")),
            Foreground     = new SolidColorBrush(Color.Parse("#CDD6F4")),
        };

        var message = new TextBlock
        {
            Text         = LocalizationService.Get("Dlg_ChooseAttr_Msg"),
            TextWrapping = TextWrapping.Wrap,
            Foreground   = new SolidColorBrush(Color.Parse("#CDD6F4")),
            FontSize     = 13,
            Margin       = new Thickness(20, 20, 20, 16),
        };

        // Strength — red
        var strBtn = new Button
        {
            Content             = LocalizationService.Get("Attr_Strength"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Padding             = new Thickness(0, 10),
            Margin              = new Thickness(0, 0, 0, 8),
            Background          = new SolidColorBrush(Color.Parse("#C0392B")),
            Foreground          = new SolidColorBrush(Color.Parse("#FFFFFF")),
            FontWeight          = Avalonia.Media.FontWeight.Bold,
        };

        // Dexterity — green
        var dexBtn = new Button
        {
            Content             = LocalizationService.Get("Attr_Dexterity"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Padding             = new Thickness(0, 10),
            Margin              = new Thickness(0, 0, 0, 8),
            Background          = new SolidColorBrush(Color.Parse("#27AE60")),
            Foreground          = new SolidColorBrush(Color.Parse("#FFFFFF")),
            FontWeight          = Avalonia.Media.FontWeight.Bold,
        };

        // Intelligence — blue
        var intBtn = new Button
        {
            Content             = LocalizationService.Get("Attr_Intelligence"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Padding             = new Thickness(0, 10),
            Margin              = new Thickness(0, 0, 0, 8),
            Background          = new SolidColorBrush(Color.Parse("#2980B9")),
            Foreground          = new SolidColorBrush(Color.Parse("#FFFFFF")),
            FontWeight          = Avalonia.Media.FontWeight.Bold,
        };

        var cancelBtn = new Button
        {
            Content             = LocalizationService.Get("Dlg_ChangeClass_Cancel"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Padding             = new Thickness(0, 8),
            Background          = new SolidColorBrush(Color.Parse("#313244")),
            Foreground          = new SolidColorBrush(Color.Parse("#CDD6F4")),
        };

        strBtn.Click    += (_, _) => dialog.Close(1);
        dexBtn.Click    += (_, _) => dialog.Close(2);
        intBtn.Click    += (_, _) => dialog.Close(3);
        cancelBtn.Click += (_, _) => dialog.Close(0);

        var panel = new StackPanel { Margin = new Thickness(20, 0, 20, 20) };
        panel.Children.Add(strBtn);
        panel.Children.Add(dexBtn);
        panel.Children.Add(intBtn);
        panel.Children.Add(cancelBtn);

        var root = new StackPanel();
        root.Children.Add(message);
        root.Children.Add(panel);

        dialog.Content = root;
        return dialog;
    }

    private async Task<bool> ShowClassChangeConfirmAsync()
    {
        var parentWindow = TopLevel.GetTopLevel(this) as Window;
        if (parentWindow == null) return true;

        var dialog = BuildConfirmDialog();
        return await dialog.ShowDialog<bool>(parentWindow);
    }

    private static Window BuildConfirmDialog()
    {
        var dialog = new Window
        {
            Title          = LocalizationService.Get("Dlg_ChangeClass_Title"),
            Width          = 400,
            SizeToContent  = SizeToContent.Height,
            CanResize      = false,
            ShowInTaskbar  = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background     = new SolidColorBrush(Color.Parse("#1E1E2E")),
            Foreground     = new SolidColorBrush(Color.Parse("#CDD6F4")),
        };

        var message = new TextBlock
        {
            Text         = LocalizationService.Get("Dlg_ChangeClass_Msg"),
            TextWrapping = TextWrapping.Wrap,
            Foreground   = new SolidColorBrush(Color.Parse("#CDD6F4")),
            FontSize     = 13,
            Margin       = new Thickness(20, 20, 20, 16),
        };

        var confirmBtn = new Button
        {
            Content             = LocalizationService.Get("Dlg_ChangeClass_Confirm"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin              = new Thickness(0, 0, 8, 0),
            Padding             = new Thickness(0, 8),
            Background          = new SolidColorBrush(Color.Parse("#F38BA8")),
            Foreground          = new SolidColorBrush(Color.Parse("#1E1E2E")),
            FontWeight          = Avalonia.Media.FontWeight.Bold,
        };

        var cancelBtn = new Button
        {
            Content             = LocalizationService.Get("Dlg_ChangeClass_Cancel"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Padding             = new Thickness(0, 8),
            Background          = new SolidColorBrush(Color.Parse("#313244")),
            Foreground          = new SolidColorBrush(Color.Parse("#CDD6F4")),
        };

        confirmBtn.Click += (_, _) => dialog.Close(true);
        cancelBtn.Click  += (_, _) => dialog.Close(false);

        var buttons = new Grid { Margin = new Thickness(20, 0, 20, 20) };
        buttons.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        buttons.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        Grid.SetColumn(confirmBtn, 0);
        Grid.SetColumn(cancelBtn,  1);
        buttons.Children.Add(confirmBtn);
        buttons.Children.Add(cancelBtn);

        var panel = new StackPanel();
        panel.Children.Add(message);
        panel.Children.Add(buttons);

        dialog.Content = panel;
        return dialog;
    }
}
