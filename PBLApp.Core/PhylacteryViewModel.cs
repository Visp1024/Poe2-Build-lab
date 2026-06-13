using CommunityToolkit.Mvvm.ComponentModel;
using PBLApp.Core.Localization;
using PBLEngine;
using System;

namespace PBLApp.ViewModels;

/// <summary>
/// Read-only status for the Crystalline Phylactery (Lich). The node is a normal tree jewel
/// socket, so the user sockets a non-Unique jewel through the usual jewel-socket UI; the
/// engine then applies that jewel's bonuses a second time (the node's "100% increased
/// Effect", via ConfigTab:ApplyPhylacteryMods). This VM only surfaces a hint so the user
/// knows the Phylactery socket is doubled, and which jewel is in it.
/// </summary>
public sealed partial class PhylacteryViewModel : ObservableObject
{
    private readonly LuaHost _host;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private bool _available;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private string _jewelName = "";

    public string StatusText => string.IsNullOrEmpty(JewelName)
        ? LocalizationService.Get("Phylactery_Empty")
        : string.Format(LocalizationService.Get("Phylactery_JewelFmt"), JewelName);

    public PhylacteryViewModel(LuaHost host)
    {
        _host = host;
        Refresh();
    }

    public void Refresh()
    {
        try
        {
            var st = _host.GetPhylacteryState();
            Available = st.Available;
            JewelName = st.JewelName;
        }
        catch
        {
            Available = false;
            JewelName = "";
        }
    }
}
