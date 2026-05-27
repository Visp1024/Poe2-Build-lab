using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
    private int _selectedTabIndex = 0;

    public static readonly string[] TabKeys =
        ["Items", "Tree", "Skills", "Calcs"];

    public string SelectedTabKey =>
        SelectedTabIndex >= 0 && SelectedTabIndex < TabKeys.Length
            ? TabKeys[SelectedTabIndex] : "";

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

    public string BuildName { get; }
    public BuildModel? Build { get; private set; }
    public CalcsTabViewModel? CalcsTab { get; private set; }
    public SkillsTabViewModel? SkillsTab { get; private set; }
    public ItemsTabViewModel? ItemsTab { get; private set; }
    public TreeTabViewModel? TreeTab { get; private set; }
    public NotesTabViewModel? NotesTab { get; private set; }
    public ConfigTabViewModel? ConfigTab { get; private set; }
    public ImportTabViewModel? ImportTab { get; private set; }

    public IRelayCommand BackCommand { get; }

    private readonly string _xmlPath;

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
            var xml = await File.ReadAllTextAsync(xmlPath);
            var model = new BuildModel(host);
            await Task.Run(() => model.LoadBuildFromXml(xml, BuildName));
            Build = model;
            CalcsTab  = new CalcsTabViewModel(host, model);
            SkillsTab = new SkillsTabViewModel(host, model,
                onStatsChanged:  () => CalcsTab.Refresh(),
                onGroupsChanged: () => { CalcsTab.RefreshSkillGroups(); CalcsTab.Refresh(); });
            ItemsTab  = new ItemsTabViewModel(host, model,
                onStatsChanged: () => CalcsTab.Refresh());
            TreeTab   = new TreeTabViewModel(host,
                onStatsChanged: () => { model.Refresh(); CalcsTab.Refresh(); });
            NotesTab  = new NotesTabViewModel(model, xmlPath);
            ConfigTab = new ConfigTabViewModel(host, model);
            ImportTab = new ImportTabViewModel(host, model, xmlPath);
            OnPropertyChanged(nameof(Build));
            OnPropertyChanged(nameof(CalcsTab));
            OnPropertyChanged(nameof(SkillsTab));
            OnPropertyChanged(nameof(ItemsTab));
            OnPropertyChanged(nameof(TreeTab));
            OnPropertyChanged(nameof(NotesTab));
            OnPropertyChanged(nameof(ConfigTab));
            OnPropertyChanged(nameof(ImportTab));
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

    [RelayCommand]
    private async Task SaveBuildAsync()
    {
        if (Build == null) return;
        var xml = Build.SaveBuildToXml();
        if (xml != null && !string.IsNullOrEmpty(_xmlPath))
            await File.WriteAllTextAsync(_xmlPath, xml);
    }
}
