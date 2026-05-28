using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PBLApp.Core.Localization;
using PBLEngine;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace PBLApp.ViewModels;

public record DamageTypeRow(string Type, string Min, string Max);

/// <summary>Display wrapper around <see cref="SkillGroupEntry"/> with a translatable name.
/// The Name field is typically a comma-separated list of gem names — each part is
/// translated via <see cref="GameTranslationService.TGem"/>.</summary>
public sealed class SkillGroupDisplayVm : ObservableObject
{
    public SkillGroupEntry Entry { get; }
    public int    Index => Entry.Index;
    public string Name  => Entry.Name;

    public string DisplayName => TranslateGroupName(Entry.Name);

    public SkillGroupDisplayVm(SkillGroupEntry e)
    {
        Entry = e;
        LocalizationService.Instance.LanguageChanged += (_, _) =>
            OnPropertyChanged(nameof(DisplayName));
    }

    public override string ToString() => DisplayName;

    private static string TranslateGroupName(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        // Group names look like "Cast on Minion Death, Spark" — split, translate each gem, rejoin.
        var parts = name.Split(", ");
        for (int i = 0; i < parts.Length; i++)
            parts[i] = GameTranslationService.TGem(parts[i]);
        return string.Join(", ", parts);
    }
}

/// <summary>Display wrapper around <see cref="ActiveSkillEntry"/> with a translatable name.</summary>
public sealed class ActiveSkillDisplayVm : ObservableObject
{
    public ActiveSkillEntry Entry { get; }
    public int    Index => Entry.Index;
    public string Name  => Entry.Name;

    public string DisplayName => GameTranslationService.TGem(Entry.Name);

    public ActiveSkillDisplayVm(ActiveSkillEntry e)
    {
        Entry = e;
        LocalizationService.Instance.LanguageChanged += (_, _) =>
            OnPropertyChanged(nameof(DisplayName));
    }

    public override string ToString() => DisplayName;
}

public partial class CalcsTabViewModel : ViewModelBase
{
    private readonly LuaHost _host;
    private readonly BuildModel _build;

    public ObservableCollection<StatSectionViewModel> Sections { get; } = [];
    public ObservableCollection<SkillGroupDisplayVm> SkillGroups { get; } = [];
    public ObservableCollection<DamageTypeRow> DamageRows { get; } = [];
    public ObservableCollection<ActiveSkillDisplayVm> ActiveSkills { get; } = [];

    [ObservableProperty] private SkillGroupDisplayVm? _selectedSkillGroup;
    [ObservableProperty] private ActiveSkillDisplayVm? _selectedActiveSkill;
    [ObservableProperty] private bool _hasMultipleActiveSkills;
    [ObservableProperty] private StatRowViewModel? _selectedStat;
    [ObservableProperty] private string _breakdownTitle = "";
    [ObservableProperty] private ObservableCollection<string> _breakdownLines = [];
    [ObservableProperty] private ObservableCollection<ModifierEntry> _modifierRows = [];
    [ObservableProperty] private bool _hasModifierRows;

    // Skill detail panel
    [ObservableProperty] private bool _hasDamageData;
    [ObservableProperty] private string _avgDamageLabel = "—";
    [ObservableProperty] private string _speedText = "—";
    [ObservableProperty] private string _castTimeText = "—";
    [ObservableProperty] private string _hitChanceText = "—";
    [ObservableProperty] private string _critChanceText = "—";
    [ObservableProperty] private string _critMultText = "—";
    [ObservableProperty] private string _critEffectText = "—";
    [ObservableProperty] private string _totalDpsText = "—";
    [ObservableProperty] private string _combinedDpsText = "—";

    private bool _suppressSkillChange;
    private readonly Action? _onMainGroupChanged;

    // Layout: (sectionKey, [(rowKey, statKey, suffix)])
    private static readonly (string SectionKey, (string RowKey, string StatKey, string Suffix)[] Rows)[] Layout =
    [
        ("Stat_SkillDPS", [
            ("Row_TotalDPS",        "TotalDPS",             ""),
            ("Row_CombinedDPS",     "CombinedDPS",          ""),
            ("Row_AverageDamage",   "AverageDamage",        ""),
            ("Row_WithDotDPS",      "WithDotDPS",           ""),
            ("Row_TotalDotDPS",     "TotalDot",             ""),
            ("Row_ImpaleDPS",       "ImpaleDPS",            ""),
        ]),
        ("Stat_HitRanges", [
            ("Row_PhysMin",         "PhysicalMin",          ""),
            ("Row_PhysMax",         "PhysicalMax",          ""),
            ("Row_LightMin",        "LightningMin",         ""),
            ("Row_LightMax",        "LightningMax",         ""),
            ("Row_ColdMin",         "ColdMin",              ""),
            ("Row_ColdMax",         "ColdMax",              ""),
            ("Row_FireMin",         "FireMin",              ""),
            ("Row_FireMax",         "FireMax",              ""),
            ("Row_ChaosMin",        "ChaosMin",             ""),
            ("Row_ChaosMax",        "ChaosMax",             ""),
        ]),
        ("Stat_Hit", [
            ("Row_HitChance",       "HitChance",            "%"),
            ("Row_Accuracy",        "Accuracy",             ""),
        ]),
        ("Stat_AttackRate", [
            ("Row_SpeedPerSec",     "Speed",                "/s"),
            ("Row_TimeSec",         "Time",                 "s"),
            ("Row_HitSpeed",        "HitSpeed",             "/s"),
        ]),
        ("Stat_Crit", [
            ("Row_CritChance",      "CritChance",           "%"),
            ("Row_CritMult",        "CritMultiplier",       "x"),
            ("Row_CritEffectMod",   "CritEffect",           "x"),
            ("Row_PreEffCrit",      "PreEffectiveCritChance", "%"),
        ]),
        ("Stat_SkillInfo", [
            ("Row_Duration",        "Duration",                     "s"),
            ("Row_Radius",          "AreaOfEffectRadiusMetres",     "m"),
        ]),
        ("Stat_Ailments", [
            ("Row_IgniteChance",    "IgniteChance",         "%"),
            ("Row_IgniteOnHit",     "IgniteChanceOnHit",    "%"),
            ("Row_IgniteOnCrit",    "IgniteChanceOnCrit",   "%"),
            ("Row_IgniteDPS",       "IgniteDPS",            ""),
            ("Row_IgniteDuration",  "IgniteDuration",       "s"),
            ("Row_ShockChance",     "ShockChance",          "%"),
            ("Row_ShockOnHit",      "ShockChanceOnHit",     "%"),
            ("Row_ShockOnCrit",     "ShockChanceOnCrit",    "%"),
            ("Row_ShockEffect",     "ShockEffectMod",       "%"),
            ("Row_ChillChance",     "ChillChance",          "%"),
            ("Row_ChillOnHit",      "ChillChanceOnHit",     "%"),
            ("Row_FreezeOnHit",     "FreezeChanceOnHit",    "%"),
            ("Row_FreezeOnCrit",    "FreezeChanceOnCrit",   "%"),
            ("Row_BleedOnHit",      "BleedChanceOnHit",     "%"),
            ("Row_PoisonOnHit",     "PoisonChanceOnHit",    "%"),
            ("Row_StunBuildup",     "StunBuildup",          ""),
        ]),
        ("Stat_Attributes", [
            ("Row_Strength",        "Str",                  ""),
            ("Row_Dexterity",       "Dex",                  ""),
            ("Row_Intelligence",    "Int",                  ""),
        ]),
        ("Stat_Life", [
            ("Row_Life",            "Life",                 ""),
            ("Row_LifeUnreserved",  "LifeUnreserved",       ""),
            ("Row_LifeRegen",       "LifeRegenRecovery",    "/s"),
            ("Row_LifeRegenPct",    "LifeRegenPercent",     "%"),
            ("Row_LifeLeech",       "LifeLeechRate",        "/s"),
        ]),
        ("Stat_Mana", [
            ("Row_Mana",            "Mana",                 ""),
            ("Row_ManaCost",        "ManaCost",             ""),
            ("Row_ManaRegen",       "ManaRegenRecovery",    "/s"),
            ("Row_ManaLeech",       "ManaLeechRate",        "/s"),
        ]),
        ("Stat_EnergyShield", [
            ("Row_ES",              "EnergyShield",         ""),
            ("Row_ESRegen",         "EnergyShieldRegenRecovery", "/s"),
            ("Row_ESRegenPct",      "EnergyShieldRegenPercent",  "%"),
        ]),
        ("Stat_Armour", [
            ("Row_Armour",          "Armour",               ""),
            ("Row_PhysReduction",   "PhysicalReduction",    "%"),
            ("Row_PhysDmgRedHit",   "PhysicalDamageReductionWhenHit", "%"),
        ]),
        ("Stat_Evasion", [
            ("Row_Evasion",         "Evasion",              ""),
            ("Row_EvadeChance",     "MeleeEvadeChance",     "%"),
            ("Row_ProjEvade",       "ProjectileEvadeChance","%"),
        ]),
        ("Stat_BlockDeflect", [
            ("Row_BlockChance",     "BlockChance",          "%"),
            ("Row_SpellBlock",      "SpellBlockChance",     "%"),
            ("Row_DeflectChance",   "DeflectChance",        "%"),
            ("Row_SpellDeflect",    "SpellDeflectChance",   "%"),
        ]),
        ("Stat_MaxHitTaken", [
            ("Row_Physical",        "PhysicalMaximumHitTaken",      ""),
            ("Row_Fire",            "FireMaximumHitTaken",           ""),
            ("Row_Cold",            "ColdMaximumHitTaken",           ""),
            ("Row_Lightning",       "LightningMaximumHitTaken",      ""),
            ("Row_Chaos",           "ChaosMaximumHitTaken",          ""),
        ]),
        ("Stat_Resistances", [
            ("Row_FireResist",      "FireResist",           "%"),
            ("Row_FireMaxResist",   "FireResistMax",        "%"),
            ("Row_ColdResist",      "ColdResist",           "%"),
            ("Row_ColdMaxResist",   "ColdResistMax",        "%"),
            ("Row_LightResist",     "LightningResist",      "%"),
            ("Row_LightMaxResist",  "LightningResistMax",   "%"),
            ("Row_ChaosResist",     "ChaosResist",          "%"),
            ("Row_ChaosMaxResist",  "ChaosResistMax",       "%"),
        ]),
        ("Stat_Charges", [
            ("Row_EnduranceCharges","EnduranceCharges",     ""),
            ("Row_FrenzyCharges",   "FrenzyCharges",        ""),
            ("Row_PowerCharges",    "PowerCharges",         ""),
            ("Row_MaxEndurance",    "EnduranceChargesMax",  ""),
            ("Row_MaxFrenzy",       "FrenzyChargesMax",     ""),
            ("Row_MaxPower",        "PowerChargesMax",      ""),
        ]),
    ];

    public CalcsTabViewModel(LuaHost host, BuildModel build, Action? onMainGroupChanged = null)
    {
        _host = host;
        _build = build;
        _onMainGroupChanged = onMainGroupChanged;

        foreach (var (sectionKey, rows) in Layout)
        {
            var section = new StatSectionViewModel(sectionKey);
            foreach (var (rowKey, key, suffix) in rows)
                section.Rows.Add(new StatRowViewModel(rowKey, key, suffix));
            Sections.Add(section);
        }

        LocalizationService.Instance.LanguageChanged += (_, _) => RebuildDamageRows();

        RefreshSkillGroups();
        Refresh();
    }

    public void RefreshSkillGroups()
    {
        _suppressSkillChange = true;
        SkillGroups.Clear();
        foreach (var g in _host.GetSkillGroups())
            SkillGroups.Add(new SkillGroupDisplayVm(g));
        var mainIdx = _host.GetMainSkillGroupIndex();
        SelectedSkillGroup = SkillGroups.FirstOrDefault(g => g.Index == mainIdx)
                          ?? SkillGroups.FirstOrDefault();
        _suppressSkillChange = false;
        RefreshActiveSkills();
    }

    private void RefreshActiveSkills()
    {
        _suppressSkillChange = true;
        ActiveSkills.Clear();
        if (SelectedSkillGroup is { } grp)
        {
            foreach (var s in _host.GetActiveSkillsInGroup(grp.Index))
                ActiveSkills.Add(new ActiveSkillDisplayVm(s));
        }
        HasMultipleActiveSkills = ActiveSkills.Count > 1;
        var savedIdx = 0;
        if (SelectedSkillGroup is { } g && ActiveSkills.Count > 0)
        {
            savedIdx = _host.GetMainActiveSkillIndex(g.Index);
            var saved = ActiveSkills.FirstOrDefault(s => s.Index == savedIdx);
            // Default override for CastOn / trigger groups: when nothing is explicitly
            // saved (or the saved choice points at the trigger gem itself) and there's
            // a non-trigger active skill in the group, prefer that. Falls through to
            // the trigger gem when the group has no other active skills.
            if (saved is null || saved.Entry.IsTrigger)
            {
                var firstNonTrigger = ActiveSkills.FirstOrDefault(s => !s.Entry.IsTrigger);
                SelectedActiveSkill = firstNonTrigger ?? saved ?? ActiveSkills.FirstOrDefault();
            }
            else
            {
                SelectedActiveSkill = saved;
            }
        }
        else
        {
            SelectedActiveSkill = ActiveSkills.FirstOrDefault();
        }
        _suppressSkillChange = false;

        // If the default override picked a different index than what's currently set
        // in Lua, push it down so the engine actually recomputes DPS for that skill.
        if (SelectedSkillGroup is { } applyGrp && SelectedActiveSkill is { } sel && sel.Index != savedIdx)
        {
            _host.SetActiveSkillGroup(applyGrp.Index, sel.Index);
            _build.Refresh();
            Refresh();
        }
    }

    public void Refresh()
    {
        var stats = _build.AllStats;
        foreach (var section in Sections)
            foreach (var row in section.Rows)
                row.UpdateValue(stats);

        RefreshSkillDetailPanel();
    }

    private void RefreshSkillDetailPanel()
    {
        // Quick-access summary labels from already-formatted stat rows
        TotalDpsText    = GetStatValue("TotalDPS");
        CombinedDpsText = GetStatValue("CombinedDPS");
        AvgDamageLabel  = GetStatValue("AverageDamage");
        SpeedText       = GetStatValue("Speed");
        CastTimeText    = GetStatValue("Time");
        HitChanceText   = GetStatValue("HitChance");
        CritChanceText  = GetStatValue("CritChance");
        CritMultText    = GetStatValue("CritMultiplier");
        CritEffectText  = GetStatValue("CritEffect");

        RebuildDamageRows();
    }

    private void RebuildDamageRows()
    {
        DamageRows.Clear();
        bool any = false;
        foreach (var (typeKey, minKey, maxKey) in new[] {
            ("DmgType_Physical",  "PhysicalMin",  "PhysicalMax"),
            ("DmgType_Lightning", "LightningMin", "LightningMax"),
            ("DmgType_Cold",      "ColdMin",      "ColdMax"),
            ("DmgType_Fire",      "FireMin",      "FireMax"),
            ("DmgType_Chaos",     "ChaosMin",     "ChaosMax"),
        })
        {
            var min = GetStatValue(minKey);
            var max = GetStatValue(maxKey);
            if (min != "—" || max != "—")
            {
                DamageRows.Add(new DamageTypeRow(
                    LocalizationService.Get(typeKey),
                    min == "—" ? "0" : min,
                    max == "—" ? "0" : max));
                any = true;
            }
        }
        HasDamageData = any;
    }

    private string GetStatValue(string key)
    {
        foreach (var section in Sections)
            foreach (var row in section.Rows)
                if (row.StatKey == key) return row.Value;
        return "—";
    }

    partial void OnSelectedSkillGroupChanged(SkillGroupDisplayVm? value)
    {
        if (value is null || _suppressSkillChange) return;
        _host.SetActiveSkillGroup(value.Index);
        _build.Refresh();
        RefreshActiveSkills();
        Refresh();
        _onMainGroupChanged?.Invoke();
    }

    partial void OnSelectedActiveSkillChanged(ActiveSkillDisplayVm? value)
    {
        if (value is null || _suppressSkillChange || SelectedSkillGroup is null) return;
        _host.SetActiveSkillGroup(SelectedSkillGroup.Index, value.Index);
        _build.Refresh();
        Refresh();
        _onMainGroupChanged?.Invoke();
    }

    [RelayCommand]
    private void SelectStat(StatRowViewModel? row)
    {
        if (SelectedStat is not null)
            SelectedStat.IsSelected = false;

        SelectedStat = row;
        BreakdownLines.Clear();
        ModifierRows.Clear();
        HasModifierRows = false;
        BreakdownTitle = "";

        if (row is null) return;

        row.IsSelected = true;
        BreakdownTitle = row.Label;

        var lines = _host.GetStatBreakdown(row.StatKey);
        foreach (var line in lines)
            BreakdownLines.Add(line);

        if (BreakdownLines.Count == 0)
            BreakdownLines.Add(LocalizationService.Get("Calc_NoBreakdown"));

        var mods = _host.GetModifierTable(row.StatKey);
        foreach (var m in mods)
            ModifierRows.Add(m);
        HasModifierRows = ModifierRows.Count > 0;
    }
}
