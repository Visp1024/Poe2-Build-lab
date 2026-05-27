using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PBLApp.Core.Localization;
using PBLEngine;
using System;
using System.IO;
using System.Threading.Tasks;

namespace PBLApp.ViewModels;

public partial class ImportTabViewModel : ViewModelBase
{
    private readonly LuaHost _host;
    private readonly BuildModel _build;
    private readonly string _xmlPath;

    [ObservableProperty] private string _importCode = "";
    [ObservableProperty] private string _exportCode = "";
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private bool _isWorking;

    public event Action<string>? CopyToClipboardRequested;

    public ImportTabViewModel(LuaHost host, BuildModel build, string xmlPath)
    {
        _host = host;
        _build = build;
        _xmlPath = xmlPath;
    }

    [RelayCommand]
    private void GenerateCode()
    {
        var xml = _build.SaveBuildToXml();
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

            await Task.Run(() => _build.LoadBuildFromXml(xml, "Imported Build"));

            if (!string.IsNullOrEmpty(_xmlPath))
                await File.WriteAllTextAsync(_xmlPath, xml);

            StatusMessage = LocalizationService.Get("Msg_Imported");
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
}
