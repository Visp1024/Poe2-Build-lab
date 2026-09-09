using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using PBLApp.Core.Localization;
using PBLApp.ViewModels;
using System;
using System.Collections.Generic;

namespace PBLApp.Views;

public partial class BuildPageView : UserControl
{
    private EventHandler? _langChangedHandler;
    private NotesWindow? _notesWindow;
    private TraderWindow? _traderWindow;
    private SettingsWindow? _settingsWindow;
    private ImportExportWindow? _importExportWindow;
    private readonly Dictionary<string, TabWindow> _tabWindows = new();

    public BuildPageView()
    {
        InitializeComponent();

        Loaded   += OnLoaded;
        Unloaded += OnUnloaded;
        DataContextChanged += (_, _) =>
        {
            if (DataContext is BuildPageViewModel vm)
            {
                vm.PromptRenameAsync = PromptRenameAsync;
                vm.RequestOpenTrader = OpenTrader;
                vm.ShowCharacterImportWindow = ShowCharacterImportWindowAsync;
                vm.RequestOpenImportExport = () => OpenImportExport_Click(this, new RoutedEventArgs());
                vm.ConfirmAsync = ConfirmAsync;
            }
        };
    }

    private async System.Threading.Tasks.Task<string?> PromptRenameAsync(string currentName)
    {
        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner is null) return null;

        var title = LocalizationService.Get("Dlg_RenameTitle");
        var msg   = LocalizationService.Get("Dlg_RenameMsg");
        return await PromptDialog.ShowAsync(owner, title, msg, currentName);
    }

    private async System.Threading.Tasks.Task<bool> ConfirmAsync(string title, string message)
    {
        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner is null) return true;
        return await ConfirmDialog.ShowAsync(owner, title, message,
            LocalizationService.Get("CharImport_UpdateButton"));
    }

    /// <summary>Окно «Обновить из игры» — то же окно импорта персонажа, но в режиме
    /// перезаписи открытого билда.</summary>
    private System.Threading.Tasks.Task ShowCharacterImportWindowAsync(CharacterImportViewModel importVm)
    {
        var owner  = TopLevel.GetTopLevel(this) as Window;
        var window = new CharacterImportWindow { DataContext = importVm };
        importVm.CloseRequested += () => Dispatcher.UIThread.Post(window.Close);
        if (owner is not null) window.Show(owner);
        else                   window.Show();
        return System.Threading.Tasks.Task.CompletedTask;
    }

    private void RenameBuild_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is BuildPageViewModel vm)
            vm.RenameBuildCommand.Execute(null);
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

    private void PopOutTab_Pressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { Tag: string key }) return;
        if (DataContext is not BuildPageViewModel vm) return;
        e.Handled = true;   // don't let the press bubble to the host RadioButton tab

        object? content = key switch
        {
            "Items"  => vm.ItemsTab,
            "Tree"   => vm.TreeTab,
            "Skills" => vm.SkillsTab,
            "Calcs"  => vm.CalcsTab,
            _        => null,
        };
        if (content is null) return;

        string title = LocalizationService.Get("Tab_" + key);

        if (_tabWindows.TryGetValue(key, out var existing))
        {
            try { existing.Activate(); return; }
            catch { _tabWindows.Remove(key); }
        }

        var owner = TopLevel.GetTopLevel(this) as Window;
        var win = new TabWindow { DataContext = content, Title = title };
        _tabWindows[key] = win;

        // Hide the tab in the main TabControl while it lives in its own window.
        vm.SetTabPoppedOut(key, true);

        // If the popped-out tab was the active one, switch to the next visible tab.
        int hiddenIdx = Array.IndexOf(BuildPageViewModel.TabKeys, key);
        if (hiddenIdx == vm.SelectedTabIndex)
        {
            int next = vm.FirstVisibleTabIndex();
            if (next >= 0) vm.SelectedTabIndex = next;
        }

        win.Closed += (_, _) =>
        {
            _tabWindows.Remove(key);
            vm.SetTabPoppedOut(key, false);
        };

        if (owner is not null) win.Show(owner);
        else win.Show();
    }

    private void OpenSettings_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not BuildPageViewModel vm || vm.ConfigTab is null) return;

        if (_settingsWindow is not null)
        {
            try { _settingsWindow.Activate(); return; }
            catch { _settingsWindow = null; }
        }

        var owner = TopLevel.GetTopLevel(this) as Window;
        _settingsWindow = new SettingsWindow { DataContext = vm.ConfigTab };
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        if (owner is not null) _settingsWindow.Show(owner);
        else _settingsWindow.Show();
    }

    private void OpenImportExport_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not BuildPageViewModel vm || vm.ImportTab is null) return;

        if (_importExportWindow is not null)
        {
            try { _importExportWindow.Activate(); return; }
            catch { _importExportWindow = null; }
        }

        var owner = TopLevel.GetTopLevel(this) as Window;
        _importExportWindow = new ImportExportWindow { DataContext = vm.ImportTab };
        _importExportWindow.Closed += (_, _) => _importExportWindow = null;
        if (owner is not null) _importExportWindow.Show(owner);
        else _importExportWindow.Show();
    }

    private void OpenTrader(string slot)
    {
        if (DataContext is not BuildPageViewModel vm) return;
        var session = vm.EnsureTraderSession();

        if (_traderWindow is not null)
        {
            try
            {
                if (_traderWindow.DataContext is TraderWindowViewModel wvm) wvm.Retarget(slot);
                _traderWindow.Activate();
                return;
            }
            catch { _traderWindow = null; }
        }

        var owner = TopLevel.GetTopLevel(this) as Window;
        var winVm = new TraderWindowViewModel(session, slot);
        _traderWindow = new TraderWindow { DataContext = winVm };
        vm.ActiveTraderWindow = winVm;
        _traderWindow.Closed += (_, _) =>
        {
            _traderWindow = null;
            vm.ActiveTraderWindow = null;
        };
        if (owner is not null) _traderWindow.Show(owner);
        else _traderWindow.Show();
    }

    private void OpenNotes_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not BuildPageViewModel vm || vm.NotesTab is null) return;

        // Focus existing window if already open
        if (_notesWindow is not null)
        {
            try { _notesWindow.Activate(); return; }
            catch { _notesWindow = null; }
        }

        var owner = TopLevel.GetTopLevel(this) as Window;
        _notesWindow = new NotesWindow { DataContext = vm.NotesTab };
        _notesWindow.Closed += (_, _) => _notesWindow = null;
        if (owner is not null)
            _notesWindow.Show(owner);
        else
            _notesWindow.Show();
    }
}
