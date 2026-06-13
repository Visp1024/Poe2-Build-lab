using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PBLEngine;

/// <summary>
/// C# view of the currently loaded PoB build.
/// Reads from LuaHost after each recalc and raises PropertyChanged
/// so Avalonia bindings can react without polling.
/// </summary>
public sealed class BuildModel : INotifyPropertyChanged
{
    private readonly LuaHost _host;

    public event PropertyChangedEventHandler? PropertyChanged;

    private double _totalDps;
    public double TotalDps { get => _totalDps; private set => Set(ref _totalDps, value); }

    private double _averageDamage;
    public double AverageDamage { get => _averageDamage; private set => Set(ref _averageDamage, value); }

    private double _speed;
    public double Speed { get => _speed; private set => Set(ref _speed, value); }

    private double _critChance;
    public double CritChance { get => _critChance; private set => Set(ref _critChance, value); }

    private double _critMultiplier;
    public double CritMultiplier { get => _critMultiplier; private set => Set(ref _critMultiplier, value); }

    private double _hitChance;
    public double HitChance { get => _hitChance; private set => Set(ref _hitChance, value); }

    private double _manaCost;
    public double ManaCost { get => _manaCost; private set => Set(ref _manaCost, value); }

    private double _life;
    public double Life { get => _life; private set => Set(ref _life, value); }

    private double _mana;
    public double Mana { get => _mana; private set => Set(ref _mana, value); }

    private double _energyShield;
    public double EnergyShield { get => _energyShield; private set => Set(ref _energyShield, value); }

    // Runic Ward (PoB internal name "Ward") — a separate defensive pool, present
    // only on builds that stack it. Surfaced conditionally (HasWard) so the
    // sidebar can hide it for the vast majority of builds that have none.
    private double _ward;
    public double Ward
    {
        get => _ward;
        private set
        {
            Set(ref _ward, value);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasWard)));
        }
    }

    /// <summary>True when the build has any Runic Ward to show.</summary>
    public bool HasWard => _ward > 0;

    private double _armour;
    public double Armour { get => _armour; private set => Set(ref _armour, value); }

    private double _evasion;
    public double Evasion { get => _evasion; private set => Set(ref _evasion, value); }

    private double _spirit;
    public double Spirit { get => _spirit; private set => Set(ref _spirit, value); }

    // Darkness is the Acolyte of Chayula resource that replaces Spirit when
    // "Embrace the Darkness" is allocated (Spirit then reads 0). Surfaced so the
    // sidebar can swap the Spirit readout for a Darkness one.
    private double _darkness;
    public double Darkness
    {
        get => _darkness;
        private set
        {
            Set(ref _darkness, value);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasDarkness)));
        }
    }

    private double _reservedDarkness;
    public double ReservedDarkness { get => _reservedDarkness; private set => Set(ref _reservedDarkness, value); }

    /// <summary>True for Acolyte of Chayula builds that converted Spirit into Darkness.</summary>
    public bool HasDarkness => _darkness > 0;

    private double _physicalReduction;
    public double PhysicalReduction { get => _physicalReduction; private set => Set(ref _physicalReduction, value); }

    private Dictionary<string, object?> _allStats = new();
    public IReadOnlyDictionary<string, object?> AllStats => _allStats;

    private string _notes = "";
    public string Notes
    {
        get => _notes;
        set
        {
            if (_notes == value) return;
            _notes = value;
            _host.SetNotes(value);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Notes)));
        }
    }

    public BuildModel(LuaHost host)
    {
        _host = host;
    }

    public void NewBuild()
    {
        _host.NewBuild();
        Refresh();
    }

    public void LoadBuildFromXml(string xml, string name = "Loaded Build")
    {
        _host.LoadBuildFromXml(xml, name);
        Refresh();
    }

    public string? SaveBuildToXml() => _host.SaveBuildToXml();

    public void Refresh()
    {
        _allStats = _host.GetAllStats();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AllStats)));

        _notes = _host.GetNotes();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Notes)));

        TotalDps          = ReadDouble("TotalDPS");
        AverageDamage     = ReadDouble("AverageDamage");
        Speed             = ReadDouble("Speed");
        CritChance        = ReadDouble("CritChance");
        CritMultiplier    = ReadDouble("CritMultiplier");
        HitChance         = ReadDouble("HitChance");
        ManaCost          = ReadDouble("ManaCost");
        Life              = ReadDouble("Life");
        Mana              = ReadDouble("Mana");
        EnergyShield      = ReadDouble("EnergyShield");
        Ward              = ReadDouble("Ward");
        Armour            = ReadDouble("Armour");
        Evasion           = ReadDouble("Evasion");
        Spirit            = ReadDouble("Spirit");
        Darkness          = ReadDouble("Darkness");
        ReservedDarkness  = ReadDouble("ReservedDarkness");
        PhysicalReduction = ReadDouble("PhysicalReduction");
    }

    private double ReadDouble(string key)
    {
        if (_allStats.TryGetValue(key, out var raw) && raw != null)
            return Convert.ToDouble(raw);
        return 0.0;
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
