using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using PBLApp.Core.Localization;
using PBLApp.ViewModels;

namespace PBLApp.Views;

public partial class BuildListView : UserControl
{
    private EventHandler? _langChangedHandler;

    public BuildListView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded   += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        // Wire the VM's delete-confirmation hook to a real Avalonia modal.
        if (DataContext is BuildListViewModel vm)
        {
            vm.ConfirmDeleteAsync = ConfirmDeleteAsync;
            vm.ShowImportWindow   = ShowImportWindowAsync;
            vm.ShowCharacterImportWindow = ShowCharacterImportWindowAsync;
            vm.PromptRenameAsync  = PromptRenameAsync;
        }
    }

    private async Task<string?> PromptRenameAsync(string currentName)
    {
        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner is null) return null;

        var title = LocalizationService.Get("Dlg_RenameTitle");
        var msg   = LocalizationService.Get("Dlg_RenameMsg");
        return await PromptDialog.ShowAsync(owner, title, msg, currentName);
    }

    private Task ShowImportWindowAsync(ImportTabViewModel importVm)
    {
        var owner  = TopLevel.GetTopLevel(this) as Window;
        var window = new ImportExportWindow { DataContext = importVm };
        importVm.CloseRequested += () => Dispatcher.UIThread.Post(window.Close);
        if (owner is not null) window.Show(owner);
        else                   window.Show();
        return Task.CompletedTask;
    }

    private Task ShowCharacterImportWindowAsync(CharacterImportViewModel importVm)
    {
        var owner  = TopLevel.GetTopLevel(this) as Window;
        var window = new CharacterImportWindow { DataContext = importVm };
        importVm.CloseRequested += () => Dispatcher.UIThread.Post(window.Close);
        if (owner is not null) window.Show(owner);
        else                   window.Show();
        return Task.CompletedTask;
    }

    private async Task<bool> ConfirmDeleteAsync(string name)
    {
        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner is null) return true;

        var title = LocalizationService.Get("Dlg_DeleteTitle");
        var fmt   = LocalizationService.Get("Dlg_DeleteBuildMsg");
        var msg   = string.Format(fmt, name);
        return await ConfirmDialog.ShowAsync(owner, title, msg);
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        LangCombo.DropDownClosed += LangCombo_DropDownClosed;
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

    private void SettingsBtn_Click(object? sender, RoutedEventArgs e)
    {
        var owner = TopLevel.GetTopLevel(this) as Window;
        var window = new AppSettingsWindow();
        if (owner is not null) _ = window.ShowDialog(owner);
        else window.Show();
    }

    // ── Card click routing ────────────────────────────────────────────────

    private void Card_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // Only react to a primary-button release within the card itself.
        if (!e.GetCurrentPoint(null).Properties.IsLeftButtonPressed) return;
        if (sender is not Border { Tag: BuildEntryViewModel entry }) return;
        if (DataContext is not BuildListViewModel vm) return;

        vm.OpenItemCommand.Execute(entry);
        e.Handled = true;
    }

    private void RenameCard_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(null).Properties.IsLeftButtonPressed) return;
        if (sender is not Border { Tag: BuildEntryViewModel entry }) return;
        if (DataContext is not BuildListViewModel vm) return;

        // Stop the press from bubbling up to Card_PointerPressed and opening the build.
        e.Handled = true;
        _ = vm.RenameEntryCommand.ExecuteAsync(entry);
    }

    private void ImportCard_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(null).Properties.IsLeftButtonPressed) return;
        if (DataContext is not BuildListViewModel vm) return;

        // Stop the press from bubbling up to Card_PointerPressed and creating an empty build.
        e.Handled = true;
        _ = vm.OpenImportCommand.ExecuteAsync(null);
    }

    private void CharacterImportCard_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(null).Properties.IsLeftButtonPressed) return;
        if (DataContext is not BuildListViewModel vm) return;

        // Как и у импорта из кода: не даём нажатию всплыть до Card_PointerPressed,
        // иначе поверх окна импорта создастся пустой билд.
        e.Handled = true;
        _ = vm.OpenCharacterImportCommand.ExecuteAsync(null);
    }

    private void DeleteCard_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(null).Properties.IsLeftButtonPressed) return;
        if (sender is not Border { Tag: BuildEntryViewModel entry }) return;
        if (DataContext is not BuildListViewModel vm) return;

        // Stop the press from bubbling up to Card_PointerPressed and re-opening the build.
        e.Handled = true;
        _ = vm.DeleteEntryCommand.ExecuteAsync(entry);
    }
}
