using CommunityToolkit.Mvvm.ComponentModel;
using PBLApp.Core.Localization;
using PBLEngine;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace PBLApp.ViewModels;

/// <summary>
/// One Runic Meridians body-tattoo Rune socket: a fixed slot type plus a chosen rune.
/// Selecting a rune routes back to the engine (configTab.input.tattooRunes), whose mods
/// are injected during calc just like Custom Modifiers — but invisibly.
/// </summary>
public sealed partial class TattooSocketViewModel : ObservableObject
{
    private readonly TattoosViewModel _parent;
    private readonly bool _suppress;

    public int    Index         { get; }   // 1-based, matches engine socket index
    public string SlotType      { get; }   // "helmet" | "body armour" | "gloves" | "boots"
    public string SlotTypeLabel { get; }

    /// <summary>Runes valid for this socket's slot type, plus a leading "none" sentinel.</summary>
    public IReadOnlyList<RuneEntry> AvailableRunes { get; }

    [ObservableProperty]
    private RuneEntry? _selectedRune;

    public TattooSocketViewModel(TattoosViewModel parent, int index, string slotType,
                                 IReadOnlyList<RuneEntry> available, RuneEntry? initial)
    {
        _parent       = parent;
        Index         = index;
        SlotType      = slotType;
        SlotTypeLabel = LabelFor(slotType);
        AvailableRunes = available;
        _suppress     = true;
        _selectedRune = initial;
        _suppress     = false;
    }

    partial void OnSelectedRuneChanged(RuneEntry? value)
    {
        if (_suppress) return;
        _parent.OnSocketRuneChanged(Index, value?.Name ?? "");
    }

    private static string LabelFor(string slotType) => slotType switch
    {
        "helmet"       => LocalizationService.Get("Tattoo_Helmet"),
        "body armour"  => LocalizationService.Get("Tattoo_BodyArmour"),
        "gloves"       => LocalizationService.Get("Tattoo_Gloves"),
        "boots"        => LocalizationService.Get("Tattoo_Boots"),
        _              => slotType,
    };
}

/// <summary>
/// The Runic Meridians (Martial Artist) tattoo panel. <see cref="Available"/> is true only
/// while the ascendancy node is allocated; the panel is hidden otherwise.
/// </summary>
public sealed partial class TattoosViewModel : ObservableObject
{
    private readonly LuaHost _host;
    private readonly Action? _onStatsChanged;
    private List<RuneEntry> _allRunes = [];

    [ObservableProperty]
    private bool _available;

    public ObservableCollection<TattooSocketViewModel> Sockets { get; } = [];

    /// <summary>Empty-selection sentinel so the picker can clear a socket back to no rune.</summary>
    private static readonly RuneEntry NoneRune =
        new("", Array.Empty<string>(), new Dictionary<string, IReadOnlyList<string>>(), "");

    public TattoosViewModel(LuaHost host, Action? onStatsChanged)
    {
        _host           = host;
        _onStatsChanged = onStatsChanged;
        try { _allRunes = _host.GetAllRunes(); } catch { _allRunes = []; }
        Refresh();
    }

    public void Refresh()
    {
        TattooState state;
        try { state = _host.GetTattooState(); }
        catch { Available = false; Sockets.Clear(); return; }

        Available = state.Available;
        Sockets.Clear();
        if (!state.Available) return;

        foreach (var s in state.Sockets)
        {
            var compatible = new List<RuneEntry> { NoneRune };
            compatible.AddRange(_allRunes.Where(r => ResolvesFor(r, s.SlotType)));
            var initial = string.IsNullOrEmpty(s.RuneName)
                ? NoneRune
                : compatible.FirstOrDefault(r => r.Name == s.RuneName) ?? NoneRune;
            Sockets.Add(new TattooSocketViewModel(this, s.Index, s.SlotType, compatible, initial));
        }
    }

    /// <summary>A rune fits the socket if it has mods for the exact slot type or for one of
    /// the generic categories the item rune picker also falls back through.</summary>
    private static bool ResolvesFor(RuneEntry r, string slotType)
    {
        if (r.ModsByType.ContainsKey(slotType)) return true;
        return r.ModsByType.ContainsKey("armour")
            || r.ModsByType.ContainsKey("caster")
            || r.ModsByType.ContainsKey("weapon")
            || r.ModsByType.ContainsKey("focus");
    }

    internal void OnSocketRuneChanged(int index, string runeName)
    {
        _host.SetTattooRune(index, runeName);
        _onStatsChanged?.Invoke();
    }

    /// <summary>Programmatic socket set (used by IPC tooling): applies the rune and
    /// rebuilds the socket VMs so the picker reflects it.</summary>
    public void SetSocketRune(int index, string runeName)
    {
        OnSocketRuneChanged(index, runeName);
        Refresh();
    }
}
