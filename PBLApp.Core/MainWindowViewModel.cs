using CommunityToolkit.Mvvm.ComponentModel;
using PBLEngine;
using System;
using System.IO;
using System.Threading.Tasks;

namespace PBLApp.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly Task<LuaHost> _hostTask;

    [ObservableProperty]
    private ViewModelBase _currentPage = null!;

    /// <summary>Parameterless constructor for Avalonia app: creates and initialises its own LuaHost.</summary>
    public MainWindowViewModel()
    {
        _hostTask = Task.Run(CreateHost);
        CurrentPage = new BuildListViewModel(_hostTask, OpenBuild);
    }

    /// <summary>Test/MCP constructor: accepts a pre-initialised LuaHost so startup is instant.</summary>
    public MainWindowViewModel(Task<LuaHost> hostTask)
    {
        _hostTask = hostTask;
        CurrentPage = new BuildListViewModel(_hostTask, OpenBuild);
    }

    private void OpenBuild(BuildEntryViewModel entry)
    {
        CurrentPage = new BuildPageViewModel(_hostTask, entry, GoBack);
    }

    private void GoBack()
    {
        CurrentPage = new BuildListViewModel(_hostTask, OpenBuild);
    }

    private static LuaHost CreateHost()
    {
        var host = new LuaHost();
        host.Initialize(FindRepoRoot());
        return host;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "src")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException(
            "Cannot find repo root (no src/ in ancestors of " + AppContext.BaseDirectory + ")");
    }
}
