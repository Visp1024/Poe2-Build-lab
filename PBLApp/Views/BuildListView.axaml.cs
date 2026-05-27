using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using PBLApp.Core.Localization;
using PBLApp.ViewModels;
using System;

namespace PBLApp.Views;

public partial class BuildListView : UserControl
{
    private EventHandler? _langChangedHandler;

    public BuildListView()
    {
        InitializeComponent();

        BuildTree.DoubleTapped += (_, _) =>
        {
            if (DataContext is BuildListViewModel vm)
                vm.OpenBuildCommand.Execute(null);
        };

        Loaded   += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        LangCombo.DropDownClosed += LangCombo_DropDownClosed;

        // Defer sync to after Avalonia's rendering pass — setting SelectedIndex
        // synchronously in Loaded is reset by Avalonia on first render.
        Dispatcher.UIThread.Post(SyncLangCombo, DispatcherPriority.Render);

        _langChangedHandler = (_, _) => Dispatcher.UIThread.Post(SyncLangCombo, DispatcherPriority.Render);
        LocalizationService.Instance.LanguageChanged += _langChangedHandler;
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        LangCombo.DropDownClosed -= LangCombo_DropDownClosed;
        if (_langChangedHandler is not null)
        {
            LocalizationService.Instance.LanguageChanged -= _langChangedHandler;
            _langChangedHandler = null;
        }
    }

    private void SyncLangCombo()
    {
        if (LangCombo is null) return;
        var lang = LocalizationService.Instance.CurrentLanguage;
        for (int i = 0; i < LangCombo.ItemCount; i++)
        {
            if (LangCombo.Items[i] is ComboBoxItem { Tag: string tag } && tag == lang)
            {
                LangCombo.SelectedIndex = i;
                break;
            }
        }
    }

    private void LangCombo_DropDownClosed(object? sender, EventArgs e)
    {
        if (sender is ComboBox { SelectedItem: ComboBoxItem { Tag: string tag } })
            LocalizationService.Instance.SetLanguage(tag);
    }
}
