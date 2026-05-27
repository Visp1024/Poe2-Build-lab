using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PBLEngine;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace PBLApp.ViewModels;

public partial class BuildListViewModel : ViewModelBase
{
    private readonly Task<LuaHost> _hostTask;
    private readonly Action<BuildEntryViewModel> _openBuild;

    [ObservableProperty]
    private ObservableCollection<BuildEntryViewModel> _builds = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenBuildCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteBuildCommand))]
    private BuildEntryViewModel? _selectedBuild;

    [ObservableProperty]
    private bool _isEmpty;

    [ObservableProperty]
    private string _statusMessage = "";

    public BuildListViewModel(Task<LuaHost> hostTask, Action<BuildEntryViewModel> openBuild)
    {
        _hostTask = hostTask;
        _openBuild = openBuild;
        LoadBuilds();
    }

    private void LoadBuilds()
    {
        var buildsPath = GetBuildsPath();
        Builds.Clear();
        if (Directory.Exists(buildsPath))
            LoadDirectory(Builds, buildsPath);
        IsEmpty = Builds.Count == 0;
    }

    private static void LoadDirectory(ObservableCollection<BuildEntryViewModel> list, string dir)
    {
        foreach (var sub in Directory.GetDirectories(dir).OrderBy(Path.GetFileName))
        {
            var folder = new BuildEntryViewModel(Path.GetFileName(sub)!, sub, isFolder: true);
            LoadDirectory(folder.Children, sub);
            list.Add(folder);
        }
        foreach (var file in Directory.GetFiles(dir, "*.xml").OrderBy(Path.GetFileNameWithoutExtension))
            list.Add(new BuildEntryViewModel(Path.GetFileNameWithoutExtension(file)!, file, isFolder: false));
    }

    [RelayCommand(CanExecute = nameof(CanOpenBuild))]
    private void OpenBuild()
    {
        try
        {
            StatusMessage = "";
            if (SelectedBuild is { IsFolder: false } entry)
                _openBuild(entry);
        }
        catch (Exception ex)
        {
            StatusMessage = "Error opening build: " + ex.Message;
            WriteErrorLog("OpenBuild", ex);
        }
    }

    private bool CanOpenBuild() => SelectedBuild is { IsFolder: false };

    [RelayCommand]
    private async Task NewBuildAsync()
    {
        try
        {
            StatusMessage = "";
            var host = await _hostTask;
            host.NewBuild();

            var buildsPath = GetBuildsPath();
            Directory.CreateDirectory(buildsPath);

            var name = UniqueName(buildsPath, "New Build");
            var filePath = Path.Combine(buildsPath, name + ".xml");
            var xml = host.SaveBuildToXml();
            if (xml == null)
            {
                StatusMessage = "Failed to create new build (SaveBuildToXml returned null).";
                return;
            }

            await File.WriteAllTextAsync(filePath, xml);

            var entry = new BuildEntryViewModel(name, filePath, isFolder: false);
            _openBuild(entry);
        }
        catch (Exception ex)
        {
            StatusMessage = "Error creating build: " + ex.Message;
            WriteErrorLog("NewBuildAsync", ex);
        }
    }

    private static void WriteErrorLog(string source, Exception ex)
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PathOfBuilding2");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "crash.log");
            File.AppendAllText(path,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {source}\n" +
                $"BaseDir: {AppContext.BaseDirectory}\n" +
                $"CWD:     {Directory.GetCurrentDirectory()}\n" +
                ex + "\n----------------------------------------\n");
        }
        catch { }
    }

    [RelayCommand(CanExecute = nameof(CanDeleteBuild))]
    private void DeleteBuild()
    {
        if (SelectedBuild is null) return;

        if (SelectedBuild.IsFolder)
            Directory.Delete(SelectedBuild.Path, recursive: true);
        else
            File.Delete(SelectedBuild.Path);

        LoadBuilds();
    }

    private bool CanDeleteBuild() => SelectedBuild is not null;

    [RelayCommand]
    private void Refresh() => LoadBuilds();

    private static string UniqueName(string dir, string baseName)
    {
        if (!File.Exists(Path.Combine(dir, baseName + ".xml")))
            return baseName;
        for (int i = 2; ; i++)
        {
            var candidate = $"{baseName} {i}";
            if (!File.Exists(Path.Combine(dir, candidate + ".xml")))
                return candidate;
        }
    }

    private static string GetBuildsPath()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(local, "PathOfBuilding2", "Builds");
    }
}
