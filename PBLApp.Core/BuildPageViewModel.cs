using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PBLApp.Core.Localization;
using PBLEngine;
using System;
using System.IO;
using System.Threading.Tasks;

namespace PBLApp.ViewModels;

public partial class BuildPageViewModel : ViewModelBase
{
    [ObservableProperty]
    private bool _isLoading = true;

    [ObservableProperty]
    private string _loadError = "";

    /// <summary>
    /// Index of the active tab in BuildPageView's TabControl.
    /// 0 = Items, 1 = Tree, 2 = Skills, 3 = Calcs.
    /// Notes and Settings (Config + Import/Export) live in separate windows
    /// (buttons in the header); they are not tabs.
    /// Two-way bound to the view, also driven by MCP tools for headless testing.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsItemsTab))]
    [NotifyPropertyChangedFor(nameof(IsTreeTab))]
    [NotifyPropertyChangedFor(nameof(IsSkillsTab))]
    [NotifyPropertyChangedFor(nameof(IsCalcsTab))]
    [NotifyPropertyChangedFor(nameof(IsConfigTab))]
    [NotifyPropertyChangedFor(nameof(CurrentTabContent))]
    [NotifyPropertyChangedFor(nameof(SelectedTabKey))]
    private int _selectedTabIndex = 0;

    public static readonly string[] TabKeys =
        ["Items", "Tree", "Skills", "Calcs", "Config"];

    public string SelectedTabKey =>
        SelectedTabIndex >= 0 && SelectedTabIndex < TabKeys.Length
            ? TabKeys[SelectedTabIndex] : "";

    // Per-tab IsChecked toggles for the RadioButton-style tab strip in BuildPageView.
    // Setting any to true via RadioButton click routes back through SelectedTabIndex.
    public bool IsItemsTab  { get => SelectedTabIndex == 0; set { if (value) SelectedTabIndex = 0; } }
    public bool IsTreeTab   { get => SelectedTabIndex == 1; set { if (value) SelectedTabIndex = 1; } }
    public bool IsSkillsTab { get => SelectedTabIndex == 2; set { if (value) SelectedTabIndex = 2; } }
    public bool IsCalcsTab  { get => SelectedTabIndex == 3; set { if (value) SelectedTabIndex = 3; } }
    public bool IsConfigTab { get => SelectedTabIndex == 4; set { if (value) SelectedTabIndex = 4; } }

    /// <summary>Active tab's content VM, dispatched from <see cref="SelectedTabIndex"/>.</summary>
    public object? CurrentTabContent => SelectedTabIndex switch
    {
        0 => ItemsTab,
        1 => TreeTab,
        2 => SkillsTab,
        3 => CalcsTab,
        4 => ConfigTab,
        _ => null,
    };

    // ── Pop-out state: when a tab is shown in its own window, the corresponding
    // TabItem in the main TabControl is hidden until that window closes.
    [ObservableProperty] private bool _isItemsPoppedOut;
    [ObservableProperty] private bool _isTreePoppedOut;
    [ObservableProperty] private bool _isSkillsPoppedOut;
    [ObservableProperty] private bool _isCalcsPoppedOut;

    public bool IsTabPoppedOut(string key) => key switch
    {
        "Items"  => IsItemsPoppedOut,
        "Tree"   => IsTreePoppedOut,
        "Skills" => IsSkillsPoppedOut,
        "Calcs"  => IsCalcsPoppedOut,
        _        => false,
    };

    public void SetTabPoppedOut(string key, bool value)
    {
        switch (key)
        {
            case "Items":  IsItemsPoppedOut  = value; break;
            case "Tree":   IsTreePoppedOut   = value; break;
            case "Skills": IsSkillsPoppedOut = value; break;
            case "Calcs":  IsCalcsPoppedOut  = value; break;
        }
    }

    /// <summary>Index of the first visible (i.e. not popped-out) tab, or -1 if all are popped out.</summary>
    public int FirstVisibleTabIndex()
    {
        for (int i = 0; i < TabKeys.Length; i++)
            if (!IsTabPoppedOut(TabKeys[i])) return i;
        return -1;
    }

    [ObservableProperty] private string _buildName = "";

    /// <summary>Asks the view for a new build name (prompt dialog), seeded with the
    /// current name. Returns the entered name, or null if cancelled. View wires this.</summary>
    public Func<string, Task<string?>>? PromptRenameAsync { get; set; }

    [ObservableProperty] private bool _isHeatmapBuilding;
    [ObservableProperty] private int  _heatmapProgress;
    public string HeatmapBusyText => LocalizationService.Get("BuildPage_HeatmapBuilding");

    public BuildModel? Build { get; private set; }
    public CalcsTabViewModel? CalcsTab { get; private set; }
    public SkillsTabViewModel? SkillsTab { get; private set; }
    public ItemsTabViewModel? ItemsTab { get; private set; }
    public TreeTabViewModel? TreeTab { get; private set; }
    public NotesTabViewModel? NotesTab { get; private set; }
    public ConfigTabViewModel? ConfigTab { get; private set; }
    public ImportTabViewModel? ImportTab { get; private set; }

    /// <summary>Активное окно подбора (владелец — BuildPageView). Задаётся при открытии/
    /// перенацеливании окна, обнуляется при закрытии. Читается IPC для /trader/*.</summary>
    public TraderWindowViewModel? ActiveTraderWindow { get; set; }

    /// <summary>Просит View открыть/перенацелить окно подбора на слот. Ставит BuildPageView.</summary>
    public Action<string>? RequestOpenTrader { get; set; }

    /// <summary>Просит View открыть окно «Импорт/Экспорт». Ставит BuildPageView;
    /// нужно и кнопке, и IPC-проверке окна экспорта.</summary>
    public Action? RequestOpenImportExport { get; set; }

    /// <summary>Просит View показать окно «Обновить из игры» (то же окно импорта персонажа,
    /// в режиме перезаписи текущего билда). Ставит BuildPageView.</summary>
    public Func<CharacterImportViewModel, Task>? ShowCharacterImportWindow { get; set; }

    public TraderSession? TraderSession { get; private set; }

    /// <summary>Лениво создаёт сессию трейдера (её конструктор гоняет заметную Lua-работу:
    /// QueryMods + скан статов слотов), незачем платить при каждом открытии билда.</summary>
    public TraderSession EnsureTraderSession()
    {
        if (TraderSession is null && _host is not null && Build is not null)
            TraderSession = new TraderSession(_host, Build,
                // Build.Refresh() — примерка экипирует предмет и рекалькулирует движок,
                // но статы в шапке (BuildModel) без этого не обновятся.
                onStatsChanged: () => { Build?.Refresh(); CalcsTab?.Refresh(); ItemsTab?.Refresh(); SkillsTab?.Refresh(); });
        return TraderSession!;
    }

    public IRelayCommand BackCommand { get; }

    /// <summary>Character level (1-100). Bound to the level editor in the header; setting it
    /// pushes to the engine, recalcs, and silently persists (manual level edits should stick).</summary>
    [ObservableProperty]
    private int _characterLevel = 1;

    partial void OnCharacterLevelChanged(int value)
    {
        if (_host is null || Build is null) return;
        _host.SetCharacterLevel(value);
        Build.Refresh();
        CalcsTab?.Refresh();
        SkillsTab?.Refresh();
        _ = AutoSaveAsync();
    }

    private LuaHost? _host;

    private string _xmlPath;

    public BuildPageViewModel(Task<LuaHost> hostTask, BuildEntryViewModel entry, Action goBack)
    {
        BuildName = entry.Name;
        _xmlPath = entry.Path;
        BackCommand = new RelayCommand(goBack);
        _ = LoadAsync(hostTask, entry.Path);
    }

    private async Task LoadAsync(Task<LuaHost> hostTask, string xmlPath)
    {
        try
        {
            var host = await hostTask;
            _host = host;
            var xml = await File.ReadAllTextAsync(xmlPath);
            var model = new BuildModel(host);
            await Task.Run(() => model.LoadBuildFromXml(xml, BuildName));
            Build = model;
            CalcsTab  = new CalcsTabViewModel(host, model,
                onMainGroupChanged: () =>
                {
                    SkillsTab?.RefreshMainFlag();
                    _ = AutoSaveAsync();
                });
            SkillsTab = new SkillsTabViewModel(host, model,
                onStatsChanged:  () => CalcsTab.Refresh(),
                onGroupsChanged: () => { CalcsTab.RefreshSkillGroups(); CalcsTab.Refresh(); });
            // SkillsTab?.Refresh() re-queries the granted-skill groups: allocating
            // a granting node, changing class/ascendancy, or equipping an item that
            // grants a skill adds/removes source groups in the engine, and the
            // Skills tab must rebuild its list to reflect that (otherwise old
            // class's granted skills linger and new ones never appear).
            ItemsTab  = new ItemsTabViewModel(host, model,
                onStatsChanged: () => { CalcsTab.RefreshSkillGroups(); CalcsTab.Refresh(); SkillsTab?.Refresh(); TreeTab?.RefreshJewelRadii(); });
            ItemsTab.OpenTraderForSlot = slot => RequestOpenTrader?.Invoke(slot);
            TreeTab   = new TreeTabViewModel(host,
                onStatsChanged: () => { model.Refresh(); CalcsTab.RefreshSkillGroups(); CalcsTab.Refresh(); SkillsTab?.Refresh(); ItemsTab?.Tattoos.Refresh(); ItemsTab?.Phylactery.Refresh(); },
                onItemsChanged: () => ItemsTab?.Refresh());
            if (TreeTab is not null)
            {
                TreeTab.PropertyChanged += (_, e) =>
                {
                    // Modal overlay only for calc that actually runs on the main host
                    // (pool disabled, or fallback after "all workers failed") — the
                    // pool path leaves the host free, so the UI must stay interactive
                    // and only show the non-blocking "warming up workers" status.
                    if (e.PropertyName == nameof(TreeTabViewModel.IsPowerBuildingModal))
                        IsHeatmapBuilding = TreeTab.IsPowerBuildingModal;
                    else if (e.PropertyName == nameof(TreeTabViewModel.PowerBuildProgress))
                        HeatmapProgress = TreeTab.PowerBuildProgress;
                };
            }
            NotesTab  = new NotesTabViewModel(model, xmlPath);
            ConfigTab = new ConfigTabViewModel(host, model);
            // TraderSession создаётся лениво в EnsureTraderSession при первом открытии
            // окна подбора — её конструктор гоняет заметную Lua-работу (QueryMods + скан
            // статов слотов), незачем платить при каждом открытии билда.
            ImportTab = new ImportTabViewModel(host, model, xmlPath);
            // Seed the level from the loaded build without triggering OnCharacterLevelChanged
            // (direct field write) — otherwise we'd re-apply + flip auto-mode on every load.
            _characterLevel = host.GetCharacterLevel();
            OnPropertyChanged(nameof(CharacterLevel));
            OnPropertyChanged(nameof(Build));
            OnPropertyChanged(nameof(CalcsTab));
            OnPropertyChanged(nameof(SkillsTab));
            OnPropertyChanged(nameof(ItemsTab));
            OnPropertyChanged(nameof(TreeTab));
            OnPropertyChanged(nameof(NotesTab));
            OnPropertyChanged(nameof(ConfigTab));
            OnPropertyChanged(nameof(ImportTab));
            OnPropertyChanged(nameof(CurrentTabContent));
        }
        catch (Exception ex)
        {
            LoadError = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>Спрашивает подтверждение (заголовок, текст) — вид подставляет модалку.</summary>
    public Func<string, string, Task<bool>>? ConfirmAsync { get; set; }

    /// <summary>Идёт обновление из игры — кнопка на время выключается.</summary>
    [ObservableProperty] private bool _isUpdatingFromGame;

    /// <summary>Короткая строка об исходе обновления, рядом с кнопкой.</summary>
    [ObservableProperty] private string _updateFromGameStatus = "";

    /// <summary>«Обновить из игры»: билд знает, из какого персонажа он импортирован, поэтому
    /// обновление идёт СРАЗУ — только с предупреждением, что содержимое будет заменено.
    /// Окно импорта открывается лишь когда без разговора не обойтись: билд ни к кому не
    /// привязан, нет входа в аккаунт или API ответил ошибкой.</summary>
    [RelayCommand]
    private async Task UpdateFromGameAsync()
    {
        if (Build is null || _host is null || ShowCharacterImportWindow is null) return;

        var vm = new CharacterImportViewModel(Task.FromResult(_host), Build, _xmlPath,
            afterReimport: () => { RefreshAfterImport(); return Task.CompletedTask; });

        if (ConfirmAsync is not null)
        {
            var confirmed = await ConfirmAsync(
                LocalizationService.Get("Dlg_UpdateFromGameTitle"),
                LocalizationService.Get("Dlg_UpdateFromGameMsg"));
            if (!confirmed) return;
        }

        UpdateFromGameStatus = "";
        IsUpdatingFromGame = true;
        try
        {
            var result = await vm.TryQuickUpdateAsync();
            if (result.Ok)
            {
                UpdateFromGameStatus = string.Format(
                    LocalizationService.Get("BuildPage_UpdatedFromGame"), result.CharacterName);
                return;
            }
        }
        finally { IsUpdatingFromGame = false; }

        // Не вышло молча — показываем окно: там и причина, и выбор персонажа.
        await ShowCharacterImportWindow(vm);
    }

    /// <summary>Открыть окно импорта персонажа напрямую, минуя быстрый путь — нужно
    /// агентской проверке UI (IPC <c>/character-import/open</c>).</summary>
    public async Task OpenCharacterImportWindowAsync()
    {
        if (Build is null || _host is null || ShowCharacterImportWindow is null) return;
        await ShowCharacterImportWindow(new CharacterImportViewModel(
            Task.FromResult(_host), Build, _xmlPath,
            afterReimport: () => { RefreshAfterImport(); return Task.CompletedTask; }));
    }

    /// <summary>Пересобирает вкладки после того, как в движок влили нового персонажа:
    /// поменяться могло всё — дерево, снаряжение, группы умений и сами статы.</summary>
    private void RefreshAfterImport()
    {
        Build?.Refresh();
        TreeTab?.RefreshFromEngine();
        ItemsTab?.Refresh();
        CalcsTab?.RefreshSkillGroups();
        CalcsTab?.Refresh();
        SkillsTab?.Refresh();
        _characterLevel = _host?.GetCharacterLevel() ?? _characterLevel;
        OnPropertyChanged(nameof(CharacterLevel));
    }

    [RelayCommand]
    private async Task RenameBuild()
    {
        if (PromptRenameAsync is null || Build is null) return;

        var newName = await PromptRenameAsync(BuildName);
        if (newName is null) return; // cancelled

        var result = BuildNaming.TryResolveRename(_xmlPath, newName, out var newPath);
        // No status surface in the header — silently ignore unchanged/invalid/clashing
        // names here (the build list screen gives full feedback for those cases).
        if (result != BuildNaming.RenameResult.Ok) return;

        try
        {
            File.Move(_xmlPath, newPath);
            _xmlPath  = newPath;
            BuildName = Path.GetFileNameWithoutExtension(newPath);
            // Keep child VMs pointed at the new file so saves don't recreate the old one.
            NotesTab?.UpdateXmlPath(newPath);
            ImportTab?.UpdateXmlPath(newPath);
        }
        catch { /* best-effort; file may be locked */ }
    }

    [RelayCommand]
    private async Task SaveBuildAsync()
    {
        if (Build == null) return;
        var xml = Build.SaveBuildToXml();
        if (xml != null && !string.IsNullOrEmpty(_xmlPath))
            await File.WriteAllTextAsync(_xmlPath, xml);
    }

    /// <summary>Persists the build silently — used for small UX choices like main-skill
    /// selection that should survive between sessions without forcing the user to click Save.</summary>
    private async Task AutoSaveAsync()
    {
        try { await SaveBuildAsync(); } catch { /* best-effort */ }
    }
}
