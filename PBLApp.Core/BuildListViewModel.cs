using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PBLEngine;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace PBLApp.ViewModels;

/// <summary>Single segment of the breadcrumb trail at the top of the grid view.</summary>
public sealed record BreadcrumbSegment(string Name, string Path, bool IsLast);

public partial class BuildListViewModel : ViewModelBase
{
    private readonly Task<LuaHost> _hostTask;
    private readonly Action<BuildEntryViewModel> _openBuild;

    /// <summary>Asks the view to show a Yes/No confirmation modal before deleting.
    /// Returns true if the user confirmed. View wires this on construction.</summary>
    public Func<string, Task<bool>>? ConfirmDeleteAsync { get; set; }

    /// <summary>Items in the currently-visible folder. Folders come first, then builds;
    /// the final entry is always the virtual "+ Create" placeholder.</summary>
    [ObservableProperty]
    private ObservableCollection<BuildEntryViewModel> _currentItems = new();

    /// <summary>Breadcrumb trail for the current folder, root → current.</summary>
    [ObservableProperty]
    private ObservableCollection<BreadcrumbSegment> _breadcrumb = new();

    [ObservableProperty] private bool   _isEmpty;
    [ObservableProperty] private string _statusMessage = "";

    /// <summary>Absolute path of the folder whose contents are currently displayed.</summary>
    private string _currentFolder;

    public BuildListViewModel(Task<LuaHost> hostTask, Action<BuildEntryViewModel> openBuild)
    {
        _hostTask = hostTask;
        _openBuild = openBuild;
        _currentFolder = GetBuildsPath();
        // Rebuild the cards on language switch so the class/ascendancy subtitles
        // re-translate (the list also offers the language selector).
        PBLApp.Core.Localization.LocalizationService.Instance.LanguageChanged += (_, _) => Refresh();
        Refresh();
    }

    // ── Loading / navigation ──────────────────────────────────────────────

    [RelayCommand]
    private void Refresh()
    {
        var root = GetBuildsPath();
        Directory.CreateDirectory(root);
        _currentFolder = root;

        // Flat list: every build under the root, including ones nested in
        // sub-folders, shown together (no folder navigation).
        CurrentItems.Clear();
        if (Directory.Exists(root))
        {
            foreach (var file in Directory.GetFiles(root, "*.xml", SearchOption.AllDirectories)
                                          .OrderBy(Path.GetFileNameWithoutExtension, StringComparer.OrdinalIgnoreCase))
                CurrentItems.Add(new BuildEntryViewModel(Path.GetFileNameWithoutExtension(file)!, file, BuildEntryKind.Build));
        }
        CurrentItems.Add(new BuildEntryViewModel("", root, BuildEntryKind.Create));

        IsEmpty = CurrentItems.Count <= 1; // only the Create placeholder
        Breadcrumb.Clear();
    }

    /// <summary>Navigate to <paramref name="folder"/>. Clamps to root.</summary>
    public void NavigateTo(string folder)
    {
        var root = GetBuildsPath();
        _currentFolder = IsInsideRoot(folder, root) ? folder : root;
        Refresh();
    }

    [RelayCommand]
    private void OpenItem(BuildEntryViewModel? entry)
    {
        if (entry is null) return;
        StatusMessage = "";
        try
        {
            switch (entry.Kind)
            {
                case BuildEntryKind.Folder:
                    NavigateTo(entry.Path);
                    break;
                case BuildEntryKind.Build:
                    _openBuild(entry);
                    break;
                case BuildEntryKind.Create:
                    _ = NewBuildAsync();
                    break;
            }
        }
        catch (Exception ex)
        {
            StatusMessage = "Error: " + ex.Message;
            WriteErrorLog("OpenItem", ex);
        }
    }

    [RelayCommand]
    private void NavigateBreadcrumb(BreadcrumbSegment? segment)
    {
        if (segment is null) return;
        NavigateTo(segment.Path);
    }

    // ── Create / delete ───────────────────────────────────────────────────

    private async Task NewBuildAsync()
    {
        try
        {
            StatusMessage = "";
            var host = await _hostTask;
            host.NewBuild();

            Directory.CreateDirectory(_currentFolder);
            var name = UniqueName(_currentFolder, "New Build");
            var filePath = Path.Combine(_currentFolder, name + ".xml");
            var xml = host.SaveBuildToXml();
            if (xml == null)
            {
                StatusMessage = "Failed to create new build (SaveBuildToXml returned null).";
                return;
            }
            await File.WriteAllTextAsync(filePath, xml);

            var entry = new BuildEntryViewModel(name, filePath, BuildEntryKind.Build);
            _openBuild(entry);
        }
        catch (Exception ex)
        {
            StatusMessage = "Error creating build: " + ex.Message;
            WriteErrorLog("NewBuildAsync", ex);
        }
    }

    [RelayCommand]
    private async Task DeleteEntryAsync(BuildEntryViewModel? entry)
    {
        if (entry is null) return;
        if (entry.Kind == BuildEntryKind.Create) return;

        var confirmer = ConfirmDeleteAsync;
        var confirmed = confirmer == null || await confirmer(entry.Name);
        if (!confirmed) return;

        try
        {
            if (entry.IsFolder) Directory.Delete(entry.Path, recursive: true);
            else                File.Delete(entry.Path);
            Refresh();
        }
        catch (Exception ex)
        {
            StatusMessage = "Error deleting: " + ex.Message;
            WriteErrorLog("DeleteEntryAsync", ex);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static bool IsInsideRoot(string path, string root)
    {
        var full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
        var rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        return full.Equals(rootFull, StringComparison.OrdinalIgnoreCase)
            || full.StartsWith(rootFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static string UniqueName(string dir, string baseName)
    {
        if (!File.Exists(Path.Combine(dir, baseName + ".xml"))) return baseName;
        for (int i = 2; ; i++)
        {
            var candidate = $"{baseName} {i}";
            if (!File.Exists(Path.Combine(dir, candidate + ".xml"))) return candidate;
        }
    }

    private static string GetBuildsPath()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(local, "PathOfBuilding2", "Builds");
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
}
