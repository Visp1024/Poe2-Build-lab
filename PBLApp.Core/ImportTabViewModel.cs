using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PBLApp.Core.Localization;
using PBLEngine;
using System;
using System.IO;
using System.Threading.Tasks;

namespace PBLApp.ViewModels;

public enum ImportExportMode
{
    /// <summary>Opened from an open build — export the current build (import hidden).</summary>
    BuildExport,
    /// <summary>Opened from the build list — import a code into a brand-new build file
    /// (export hidden).</summary>
    ListImport,
}

public partial class ImportTabViewModel : ViewModelBase
{
    private readonly BuildModel? _build;
    private string? _xmlPath;

    // ListImport-mode collaborators.
    private readonly string? _buildsFolder;
    private readonly Action<string>? _onImported;

    public ImportExportMode Mode { get; }

    [ObservableProperty] private string _importCode = "";
    [ObservableProperty] private string _exportCode = "";
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private bool _isWorking;

    public bool ShowImport => Mode == ImportExportMode.ListImport;
    public bool ShowExport => Mode == ImportExportMode.BuildExport;

    public string WindowTitle => ShowImport
        ? LocalizationService.Get("Import_Title")
        : LocalizationService.Get("Export_Title");

    public event Action<string>? CopyToClipboardRequested;

    /// <summary>Raised after a successful ListImport so the host window can close itself.</summary>
    public event Action? CloseRequested;

    /// <summary>BuildExport mode: export the currently-loaded build.</summary>
    public ImportTabViewModel(LuaHost host, BuildModel build, string xmlPath)
    {
        Mode = ImportExportMode.BuildExport;
        _build = build;
        _xmlPath = xmlPath;
    }

    /// <summary>ListImport mode: decode a share code into a new build file under
    /// <paramref name="buildsFolder"/>, then hand the new file path to
    /// <paramref name="onImported"/> (which opens it). Needs no LuaHost — decoding is
    /// pure C# and the build loads fresh when opened.</summary>
    public ImportTabViewModel(string buildsFolder, Action<string> onImported)
    {
        Mode = ImportExportMode.ListImport;
        _buildsFolder = buildsFolder;
        _onImported = onImported;
    }

    /// <summary>Repoint at a new file path after the build was renamed (BuildExport mode).</summary>
    public void UpdateXmlPath(string xmlPath) => _xmlPath = xmlPath;

    [RelayCommand]
    private void GenerateCode()
    {
        var xml = _build?.SaveBuildToXml();
        if (xml == null)
        {
            StatusMessage = LocalizationService.Get("Msg_ErrorSave");
            return;
        }
        ExportCode = BuildCodec.Encode(xml);
        StatusMessage = string.Format(LocalizationService.Get("Msg_CodeGenerated"), ExportCode.Length);
    }

    [RelayCommand]
    private void CopyCode()
    {
        if (string.IsNullOrEmpty(ExportCode)) return;
        CopyToClipboardRequested?.Invoke(ExportCode);
        StatusMessage = LocalizationService.Get("Msg_Copied");
    }

    [RelayCommand]
    private async Task ImportAsync()
    {
        var code = ImportCode.Trim();
        if (string.IsNullOrEmpty(code))
        {
            StatusMessage = LocalizationService.Get("Msg_PasteCode");
            return;
        }

        IsWorking = true;
        StatusMessage = LocalizationService.Get("Msg_Importing");
        try
        {
            var xml = BuildCodec.Decode(code);
            if (xml == null || !xml.Contains("<PathOfBuilding"))
            {
                StatusMessage = LocalizationService.Get("Msg_InvalidCode");
                return;
            }

            if (Mode == ImportExportMode.ListImport)
                await ImportAsNewBuildAsync(xml);
            else
                await ImportIntoCurrentBuildAsync(xml);

            ImportCode = "";
        }
        catch (Exception ex)
        {
            StatusMessage = string.Format(LocalizationService.Get("Msg_Error"), ex.Message);
        }
        finally
        {
            IsWorking = false;
        }
    }

    // BuildExport mode (legacy): load the code into the current build, overwrite its file.
    private async Task ImportIntoCurrentBuildAsync(string xml)
    {
        await Task.Run(() => _build!.LoadBuildFromXml(xml, "Imported Build"));
        if (!string.IsNullOrEmpty(_xmlPath))
            await File.WriteAllTextAsync(_xmlPath, xml);
        StatusMessage = LocalizationService.Get("Msg_Imported");
    }

    // ListImport mode: write a new file (named after the build), then open it.
    private async Task ImportAsNewBuildAsync(string xml)
    {
        var folder = _buildsFolder!;
        Directory.CreateDirectory(folder);
        var name = UniqueName(folder, BuildNaming.FromXml(xml));
        var filePath = Path.Combine(folder, name + ".xml");
        await File.WriteAllTextAsync(filePath, xml);

        StatusMessage = LocalizationService.Get("Msg_Imported");
        _onImported?.Invoke(filePath);
        CloseRequested?.Invoke();
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
}
