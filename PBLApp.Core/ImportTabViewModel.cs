using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PBLApp.Core.Export;
using PBLApp.Core.Localization;
using PBLEngine;
using System;
using System.Diagnostics;
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
    private readonly LuaHost? _host;
    private string? _xmlPath;
    private string _buildName = "";

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
        _host = host;
        _build = build;
        _xmlPath = xmlPath;
        _buildName = Path.GetFileNameWithoutExtension(xmlPath);
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
    public void UpdateXmlPath(string xmlPath)
    {
        _xmlPath = xmlPath;
        _buildName = Path.GetFileNameWithoutExtension(xmlPath);
    }

    // ── Экспорт гайда в игру (.build для внутриигрового планировщика) ────────

    /// <summary>Папка, куда игра смотрит за файлами гайдов; показывается в окне
    /// и остаётся редактируемой — путь к Documents можно перенести.</summary>
    [ObservableProperty] private string _guideFolder = BuildGuideExporter.DefaultFolder;

    /// <summary>Путь последнего записанного .build — по нему открывается папка.</summary>
    [ObservableProperty] private string _lastGuideFile = "";

    public bool ShowGuideExport => Mode == ImportExportMode.BuildExport;

    /// <summary>Пишет .build в <see cref="GuideFolder"/>. Формат — схема GGG
    /// (pathofexile.com/developer/docs/game): пассивки, гемы с саппортами и
    /// подсказки по слотам; уровневых интервалов гайд не содержит.</summary>
    [RelayCommand]
    private async Task ExportGuideAsync()
    {
        if (_host is null)
        {
            StatusMessage = LocalizationService.Get("Guide_NoEngine");
            return;
        }

        IsWorking = true;
        try
        {
            var folder = GuideFolder;
            var name = _buildName;
            var result = await Task.Run(() => BuildGuideExporter.Export(_host, name, folder));
            if (!result.Ok)
            {
                StatusMessage = string.Format(LocalizationService.Get("Msg_Error"), result.Error ?? "");
                return;
            }

            LastGuideFile = result.FilePath ?? "";
            StatusMessage = string.Format(LocalizationService.Get("Guide_Done"),
                result.PassiveCount, result.SkillCount, result.ItemCount, result.FilePath);
            if (result.SkippedPassives > 0)
                StatusMessage += " " + string.Format(
                    LocalizationService.Get("Guide_SkippedPassives"), result.SkippedPassives);
        }
        finally
        {
            IsWorking = false;
        }
    }

    /// <summary>Открывает папку гайдов в проводнике — файл ещё нужно увидеть глазами.</summary>
    [RelayCommand]
    private void OpenGuideFolder()
    {
        try
        {
            var folder = Directory.Exists(GuideFolder) ? GuideFolder : BuildGuideExporter.DefaultFolder;
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            StatusMessage = string.Format(LocalizationService.Get("Msg_Error"), ex.Message);
        }
    }

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
