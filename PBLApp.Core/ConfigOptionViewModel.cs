using CommunityToolkit.Mvvm.ComponentModel;
using PBLApp.Core.Localization;
using PBLEngine;
using System.Linq;

namespace PBLApp.ViewModels;

public partial class ConfigOptionViewModel : ObservableObject
{
    private readonly LuaHost _host;
    private readonly BuildModel _build;
    private bool _suppressUpdate;

    public string Var { get; }
    public string Label { get; }
    public string Type { get; }
    public ConfigListItem[] ListOptions { get; }

    [ObservableProperty] private bool _isChecked;
    [ObservableProperty] private string _selectedVal = "";
    [ObservableProperty] private ConfigListItem? _selectedItem;
    [ObservableProperty] private string _textValue = "";

    public ConfigOptionViewModel(ConfigOption opt, LuaHost host, BuildModel build)
    {
        _host = host;
        _build = build;
        Var = opt.Var;
        Label = GameTranslationService.TConfigLabel(opt.Var, opt.Label);
        Type = opt.Type;
        ListOptions = opt.ListOptions;

        _suppressUpdate = true;
        if (opt.Type == "check")
            _isChecked = opt.CurrentValue == "true";
        else if (opt.Type == "list")
        {
            _selectedVal = opt.CurrentValue;
            _selectedItem = opt.ListOptions.FirstOrDefault(o => o.Val == opt.CurrentValue)
                         ?? opt.ListOptions.FirstOrDefault();
        }
        else
            _textValue = opt.CurrentValue;
        _suppressUpdate = false;
    }

    partial void OnIsCheckedChanged(bool value)
    {
        if (_suppressUpdate) return;
        _host.SetConfigValue(Var, value ? "true" : "false");
        _build.Refresh();
    }

    partial void OnSelectedValChanged(string value)
    {
        if (_suppressUpdate || string.IsNullOrEmpty(value)) return;
        _host.SetConfigValue(Var, value);
        _build.Refresh();
    }

    partial void OnSelectedItemChanged(ConfigListItem? value)
    {
        if (_suppressUpdate || value == null) return;
        _suppressUpdate = true;
        SelectedVal = value.Val;
        _suppressUpdate = false;
        _host.SetConfigValue(Var, value.Val);
        _build.Refresh();
    }

    partial void OnTextValueChanged(string value)
    {
        if (_suppressUpdate) return;
        if (Type == "text")
        {
            // Free-form text (Custom Modifiers): write as-is, never validate as a number.
            _host.SetConfigValue(Var, value ?? "");
            _build.Refresh();
            return;
        }
        if (string.IsNullOrWhiteSpace(value)) return;
        if (!double.TryParse(value, out _)) return;
        _host.SetConfigValue(Var, value);
        _build.Refresh();
    }

    public void ApplyText()
    {
        if (_suppressUpdate) return;
        _host.SetConfigValue(Var, TextValue);
        _build.Refresh();
    }
}
