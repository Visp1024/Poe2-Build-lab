using PBLApp.ViewModels;
using PBLEngine;
using System.Threading.Tasks;

namespace PBLMcp;

/// <summary>
/// Drives a headless instance of the PBLApp MVVM stack.
/// Shares the same LuaHost as PBLState so state (loaded build, mods, etc.) is consistent.
/// </summary>
public sealed class AppDriver
{
    internal readonly PBLState _state;
    private MainWindowViewModel? _mainVm;

    /// <summary>Build-list selection remembered between app_select_build and app_open_build.
    /// The refactored BuildListViewModel no longer tracks a selected entry itself
    /// (navigation is per-folder via OpenItemCommand), so the test-tool layer holds it.</summary>
    public BuildEntryViewModel? SelectedBuildEntry { get; set; }

    public AppDriver(PBLState state)
    {
        _state = state;
    }

    /// <summary>Root ViewModel of the app, lazily constructed on first access.</summary>
    public MainWindowViewModel App
    {
        get
        {
            _mainVm ??= new MainWindowViewModel(_state.HostTask);
            return _mainVm;
        }
    }

    /// <summary>Resets the app to BuildList page, sharing the same already-initialised LuaHost.</summary>
    public void Reset()
    {
        _mainVm = new MainWindowViewModel(_state.HostTask);
        SelectedBuildEntry = null;
    }
}
