using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PBLApp.Core;
using PBLApp.Core.Localization;
using PBLEngine;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace PBLApp.ViewModels;

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

/// <summary>One of the three domain columns (Offence / Resources / Defence) shown
/// in the redesigned Calcs board. Each owns the sections pinned to it.</summary>
public sealed partial class StatColumnViewModel(string titleKey) : ViewModelBase
{
    private readonly string _titleKey = titleKey;
    public string Title => LocalizationService.Get(_titleKey);
    public ObservableCollection<StatSectionViewModel> Sections { get; } = [];

    public void RaiseTitle() => OnPropertyChanged(nameof(Title));
}

public partial class CalcsTabViewModel : ViewModelBase
{
    private readonly LuaHost _host;
    private readonly BuildModel _build;

    /// <summary>Flat list of every section — used for the recompute + filter loops.</summary>
    public List<StatSectionViewModel> Sections { get; } = [];

    /// <summary>Three domain columns the board renders side by side.</summary>
    public ObservableCollection<StatColumnViewModel> Columns { get; } = [];

    public ObservableCollection<SkillGroupDisplayVm> SkillGroups { get; } = [];
    public ObservableCollection<ActiveSkillDisplayVm> ActiveSkills { get; } = [];

    [ObservableProperty] private SkillGroupDisplayVm? _selectedSkillGroup;
    [ObservableProperty] private ActiveSkillDisplayVm? _selectedActiveSkill;
    [ObservableProperty] private bool _hasMultipleActiveSkills;
    [ObservableProperty] private StatRowViewModel? _selectedStat;
    [ObservableProperty] private string _breakdownTitle = "";
    [ObservableProperty] private ObservableCollection<string> _breakdownLines = [];
    [ObservableProperty] private ObservableCollection<ModifierEntry> _modifierRows = [];
    [ObservableProperty] private bool _hasModifierRows;

    [ObservableProperty] private string _searchText = "";

    /// <summary>Drives the collapse-all / expand-all toggle label + behaviour.</summary>
    [ObservableProperty] private bool _allCollapsed;

    /// <summary>Compact density: tighter padding + 4-column masonry that fills space,
    /// instead of the spacious fixed 3 domain columns. Defaults to compact; persisted.</summary>
    [ObservableProperty] private bool _isCompact = AppPreferences.GetBool("CalcsCompact", true);

    public bool HasBreakdown => SelectedStat is not null;

    private bool _suppressSkillChange;
    private readonly Action? _onMainGroupChanged;

    // Column index: 0 = Offence, 1 = Resources, 2 = Defence.
    // Per-row tuple: (rowKey, statKey, suffix, colourKey). colourKey "" → inherit section row colour.
    private sealed record RowDef(string Key, string Stat, string Suffix = "", string Color = "");
    private sealed record SectionDef(string Key, int Column, string Accent, string RowColor, string? Gate, RowDef[] Rows);

    private const string Dps  = "StatDpsBrush";
    private const string Life = "StatLifeBrush";
    private const string Mana = "StatManaBrush";
    private const string Es   = "StatEsBrush";
    private const string Ward = "StatWardBrush";
    private const string Arm  = "StatArmourBrush";
    private const string Eva  = "StatEvasionBrush";
    private const string Def  = "InfoBrush";
    private const string Gold = "Brand400Brush";
    private const string Neut = "TextPrimaryBrush";
    private const string Phys = "ElPhysicalBrush";
    private const string Fire = "ElFireBrush";
    private const string Cold = "ElColdBrush";
    private const string Ltng = "ElLightningBrush";
    private const string Chao = "ElChaosBrush";

    private static readonly SectionDef[] Layout =
    [
        // ── Column 0 — Offence ──────────────────────────────────────────────
        new("Stat_SkillDPS", 0, Dps, Dps, null, [
            new("Row_TotalDPS",      "TotalDPS"),
            new("Row_CombinedDPS",   "CombinedDPS"),
            new("Row_AverageDamage", "AverageDamage"),
            new("Row_WithDotDPS",    "WithDotDPS"),
            new("Row_TotalDotDPS",   "TotalDot"),
            new("Row_ImpaleDPS",     "ImpaleDPS"),
        ]),
        new("Stat_HitRanges", 0, Dps, Dps, null, [
            new("Row_PhysMin",  "PhysicalMin",  "", Phys),
            new("Row_PhysMax",  "PhysicalMax",  "", Phys),
            new("Row_LightMin", "LightningMin", "", Ltng),
            new("Row_LightMax", "LightningMax", "", Ltng),
            new("Row_ColdMin",  "ColdMin",      "", Cold),
            new("Row_ColdMax",  "ColdMax",      "", Cold),
            new("Row_FireMin",  "FireMin",      "", Fire),
            new("Row_FireMax",  "FireMax",      "", Fire),
            new("Row_ChaosMin", "ChaosMin",     "", Chao),
            new("Row_ChaosMax", "ChaosMax",     "", Chao),
        ]),
        new("Stat_Hit", 0, Dps, Dps, null, [
            new("Row_HitChance", "HitChance", "%"),
            new("Row_Accuracy",  "Accuracy"),
        ]),
        new("Stat_AttackRate", 0, Dps, Dps, null, [
            new("Row_SpeedPerSec", "Speed",    "/s"),
            new("Row_TimeSec",     "Time",     "s"),
            new("Row_HitSpeed",    "HitSpeed", "/s"),
        ]),
        new("Stat_Crit", 0, Dps, Dps, null, [
            new("Row_CritChance",    "CritChance",              "%"),
            new("Row_CritMult",      "CritMultiplier",          "x"),
            new("Row_CritEffectMod", "CritEffect",              "x"),
            new("Row_PreEffCrit",    "PreEffectiveCritChance",  "%"),
        ]),
        new("Stat_SkillInfo", 0, Dps, Dps, null, [
            new("Row_Duration", "Duration",                  "s"),
            new("Row_Radius",   "AreaOfEffectRadiusMetres",  "m"),
        ]),
        new("Stat_Ailments", 0, Dps, Dps, null, [
            new("Row_IgniteChance",   "IgniteChance",        "%"),
            new("Row_IgniteOnHit",    "IgniteChanceOnHit",   "%"),
            new("Row_IgniteOnCrit",   "IgniteChanceOnCrit",  "%"),
            new("Row_IgniteDPS",      "IgniteDPS",           "",  Fire),
            new("Row_IgniteDuration", "IgniteDuration",      "s"),
            new("Row_ShockChance",    "ShockChance",         "%"),
            new("Row_ShockOnHit",     "ShockChanceOnHit",    "%"),
            new("Row_ShockOnCrit",    "ShockChanceOnCrit",   "%"),
            new("Row_ShockEffect",    "ShockEffectMod",      "%"),
            new("Row_ChillChance",    "ChillChance",         "%"),
            new("Row_ChillOnHit",     "ChillChanceOnHit",    "%"),
            new("Row_FreezeOnHit",    "FreezeChanceOnHit",   "%"),
            new("Row_FreezeOnCrit",   "FreezeChanceOnCrit",  "%"),
            new("Row_BleedOnHit",     "BleedChanceOnHit",    "%"),
            new("Row_PoisonOnHit",    "PoisonChanceOnHit",   "%"),
            new("Row_StunBuildup",    "StunBuildup"),
        ]),

        // ── Column 1 — Resources ────────────────────────────────────────────
        new("Stat_Attributes", 1, Gold, Neut, null, [
            new("Row_Strength",     "Str", "", "AttrStrBrush"),
            new("Row_Dexterity",    "Dex", "", "AttrDexBrush"),
            new("Row_Intelligence", "Int", "", "AttrIntBrush"),
        ]),
        new("Stat_Life", 1, Life, Life, null, [
            new("Row_Life",           "Life"),
            new("Row_LifeUnreserved", "LifeUnreserved"),
            new("Row_LifeRegen",      "LifeRegenRecovery", "/s"),
            new("Row_LifeRegenPct",   "LifeRegenPercent",  "%"),
            new("Row_LifeLeech",      "LifeLeechRate",     "/s"),
        ]),
        new("Stat_Mana", 1, Mana, Mana, null, [
            new("Row_Mana",      "Mana"),
            new("Row_ManaCost",  "ManaCost"),
            new("Row_ManaRegen", "ManaRegenRecovery", "/s"),
            new("Row_ManaLeech", "ManaLeechRate",     "/s"),
        ]),
        new("Stat_EnergyShield", 1, Es, Es, null, [
            new("Row_ES",         "EnergyShield"),
            new("Row_ESRegen",    "EnergyShieldRegenRecovery", "/s"),
            new("Row_ESRegenPct", "EnergyShieldRegenPercent",  "%"),
        ]),
        new("Stat_Ward", 1, Ward, Ward, "Ward", [
            new("Row_Ward",            "Ward"),
            new("Row_WardRechargeDelay", "WardRechargeDelay", "s"),
        ]),

        // ── Column 2 — Defence ──────────────────────────────────────────────
        new("Stat_Resistances", 2, Def, Def, null, [
            new("Row_FireResist",     "FireResist",         "%", Fire),
            new("Row_FireMaxResist",  "FireResistMax",      "%", Fire),
            new("Row_ColdResist",     "ColdResist",         "%", Cold),
            new("Row_ColdMaxResist",  "ColdResistMax",      "%", Cold),
            new("Row_LightResist",    "LightningResist",    "%", Ltng),
            new("Row_LightMaxResist", "LightningResistMax", "%", Ltng),
            new("Row_ChaosResist",    "ChaosResist",        "%", Chao),
            new("Row_ChaosMaxResist", "ChaosResistMax",     "%", Chao),
        ]),
        new("Stat_Armour", 2, Arm, Arm, null, [
            new("Row_Armour",        "Armour"),
            new("Row_PhysReduction", "PhysicalReduction",                "%"),
            new("Row_PhysDmgRedHit", "PhysicalDamageReductionWhenHit",   "%"),
        ]),
        new("Stat_Evasion", 2, Eva, Eva, null, [
            new("Row_Evasion",     "Evasion"),
            new("Row_EvadeChance", "MeleeEvadeChance",      "%"),
            new("Row_ProjEvade",   "ProjectileEvadeChance", "%"),
        ]),
        new("Stat_BlockDeflect", 2, Def, Def, null, [
            new("Row_BlockChance",   "BlockChance",        "%"),
            new("Row_SpellBlock",    "SpellBlockChance",   "%"),
            new("Row_DeflectChance", "DeflectChance",      "%"),
            new("Row_SpellDeflect",  "SpellDeflectChance", "%"),
        ]),
        new("Stat_MaxHitTaken", 2, Def, Def, null, [
            new("Row_Physical",  "PhysicalMaximumHitTaken",  "", Phys),
            new("Row_Fire",      "FireMaximumHitTaken",      "", Fire),
            new("Row_Cold",      "ColdMaximumHitTaken",      "", Cold),
            new("Row_Lightning", "LightningMaximumHitTaken", "", Ltng),
            new("Row_Chaos",     "ChaosMaximumHitTaken",     "", Chao),
        ]),
        new("Stat_Charges", 2, Gold, Neut, null, [
            new("Row_EnduranceCharges", "EnduranceCharges"),
            new("Row_FrenzyCharges",    "FrenzyCharges"),
            new("Row_PowerCharges",     "PowerCharges"),
            new("Row_MaxEndurance",     "EnduranceChargesMax"),
            new("Row_MaxFrenzy",        "FrenzyChargesMax"),
            new("Row_MaxPower",         "PowerChargesMax"),
        ]),
    ];

    private static readonly string[] ColumnTitleKeys = ["Calc_ColOffence", "Calc_ColResources", "Calc_ColDefence"];

    public CalcsTabViewModel(LuaHost host, BuildModel build, Action? onMainGroupChanged = null)
    {
        _host = host;
        _build = build;
        _onMainGroupChanged = onMainGroupChanged;

        foreach (var titleKey in ColumnTitleKeys)
            Columns.Add(new StatColumnViewModel(titleKey));

        foreach (var def in Layout)
        {
            var section = new StatSectionViewModel(def.Key, def.Accent, def.Gate);
            foreach (var r in def.Rows)
                section.Rows.Add(new StatRowViewModel(
                    r.Key, r.Stat, r.Suffix,
                    string.IsNullOrEmpty(r.Color) ? def.RowColor : r.Color));
            Sections.Add(section);
            Columns[def.Column].Sections.Add(section);
        }

        LocalizationService.Instance.LanguageChanged += (_, _) =>
        {
            foreach (var c in Columns) c.RaiseTitle();
        };

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
        {
            foreach (var row in section.Rows)
                row.UpdateValue(stats);
            section.UpdateVisibility(stats);
        }
    }

    // ── Search filter: dim rows/sections that don't match (never hide → stable layout) ──
    partial void OnSearchTextChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        var q = (SearchText ?? "").Trim();
        bool empty = q.Length == 0;

        foreach (var section in Sections)
        {
            bool sectionLabelMatch = !empty && section.Label.Contains(q, StringComparison.OrdinalIgnoreCase);
            bool anyRow = false;
            foreach (var row in section.Rows)
            {
                bool hit = empty || sectionLabelMatch
                           || row.Label.Contains(q, StringComparison.OrdinalIgnoreCase);
                row.IsDimmed = !empty && !hit;
                if (hit) anyRow = true;
            }
            section.MatchesFilter = empty || sectionLabelMatch || anyRow;
        }
    }

    [RelayCommand]
    private void ToggleAll()
    {
        AllCollapsed = !AllCollapsed;
        foreach (var section in Sections)
            section.IsCollapsed = AllCollapsed;
    }

    partial void OnIsCompactChanged(bool value) => AppPreferences.SetBool("CalcsCompact", value);

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

    partial void OnSelectedStatChanged(StatRowViewModel? value) => OnPropertyChanged(nameof(HasBreakdown));

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

    [RelayCommand]
    private void CloseBreakdown() => SelectStat(null);
}
