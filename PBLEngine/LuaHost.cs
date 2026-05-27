using NLua;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace PBLEngine;

/// <summary>
/// Owns the NLua state and the PoB Lua environment lifecycle.
/// Call Initialize() once, then use NewBuild() / LoadBuildFromXml().
/// </summary>
public sealed class LuaHost : IDisposable
{
    public Lua State { get; } = new Lua();

    public string RepoRoot { get; private set; } = "";
    private string _srcDir = "";
    private string _compatLuaPath = "";

    public void Initialize(string repoRoot)
    {
        RepoRoot = repoRoot;
        _srcDir = Path.Combine(repoRoot, "src");
        if (!Directory.Exists(_srcDir))
            throw new DirectoryNotFoundException($"src/ not found at: {_srcDir}");

        var runtimeLuaDir = Path.Combine(repoRoot, "runtime", "lua");
        // Use AppContext.BaseDirectory (not Assembly.Location): under PublishSingleFile=true the
        // entry assembly is extracted to a temp dir and Location is empty / wrong path.
        _compatLuaPath = Path.Combine(AppContext.BaseDirectory, "lua", "compat.lua");

        State.State.Encoding = System.Text.Encoding.UTF8;

        Directory.SetCurrentDirectory(_srcDir);

        var runtimeLuaFwd = runtimeLuaDir.Replace('\\', '/');
        State.DoString($"package.path = '{runtimeLuaFwd}/?.lua;{runtimeLuaFwd}/?/init.lua;' .. package.path");

        if (File.Exists(_compatLuaPath))
            State.DoFile(_compatLuaPath);
        else
            throw new FileNotFoundException($"compat.lua not found at: {_compatLuaPath}");

        State.DoFile("HeadlessWrapper.lua");

        State.DoString(@"
            if not newBuild and launch and launch.main then
                build = launch.main.modes and launch.main.modes['BUILD']
                function newBuild()
                    launch.promptMsg = nil
                    launch.main:SetMode('BUILD', false, 'New Build')
                    runCallback('OnFrame')
                    build = launch.main.modes['BUILD']
                end
                function loadBuildFromXML(xmlText, name)
                    launch.promptMsg = nil
                    launch.main:SetMode('BUILD', false, name or '', xmlText)
                    runCallback('OnFrame')
                    build = launch.main.modes['BUILD']
                end
            end
        ");
    }

    public void NewBuild()
    {
        State.DoString("newBuild()");
        State.DoString("runCallback('OnFrame')");
        SyncCalcsSkill();
    }

    public void LoadBuildFromXml(string xml, string name = "Loaded Build")
    {
        State["_loadXml"]  = xml;
        State["_loadName"] = name;
        State.DoString("loadBuildFromXML(_loadXml, _loadName); runCallback('OnFrame')");
        State["_loadXml"]  = null;
        State["_loadName"] = null;
        SyncCalcsSkill();
    }

    // Sync calcsTab.input.skill_number to build.mainSocketGroup so that
    // calcsOutput (which has per-type Min/Max) matches mainOutput.
    // CalcOffence only writes PhysicalMin/LightningMin/etc. in "CALCS" mode.
    private void SyncCalcsSkill()
    {
        State.DoString(@"
            if build and build.calcsTab and build.mainSocketGroup then
                build.calcsTab.input.skill_number = build.mainSocketGroup
                build.calcsTab:BuildOutput()
            end
        ");
    }

    public string? SaveBuildToXml()
    {
        var result = State.DoString(@"
            if build and build.calcsTab then
                if not build.calcsTab.mainOutput then
                    build.calcsTab:BuildOutput()
                end
            end
            if build and build.SaveDB then
                return build:SaveDB('')
            end
            return nil
        ");
        return result is { Length: > 0 } ? result[0] as string : null;
    }

    public List<string> GetStatBreakdown(string statName)
    {
        var lines = new List<string>();
        State["_bdStat"] = statName;
        var result = State.DoString(@"
            local env = build and build.calcsTab and build.calcsTab.calcsEnv
            if not env then return nil end
            local bd = env.player and env.player.breakdown and env.player.breakdown[_bdStat]
            if not bd then return nil end
            local out = {}
            local function strip(s)
                if type(s) ~= 'string' then return tostring(s or '') end
                return (s:gsub('%^x%x%x%x%x%x%x?',''):gsub('%^%d',''):gsub('%^_?x%x+',''))
            end
            for _, line in ipairs(bd) do
                table.insert(out, strip(line))
            end
            return out
        ");
        State["_bdStat"] = null;
        if (result is { Length: > 0 } && result[0] is NLua.LuaTable tbl)
        {
            foreach (var k in tbl.Keys)
                if (tbl[k] is string s) lines.Add(s);
        }
        return lines;
    }

    public void DumpStatsToFile(string filePath)
    {
        var result = State.DoString(@"
            local main  = build and build.calcsTab and build.calcsTab.mainOutput
            local calcs = build and build.calcsTab and build.calcsTab.calcsOutput
            local rows = {}
            local seen = {}
            local function add(src, o)
                if not o then return end
                for k, v in pairs(o) do
                    if not seen[k] and (type(v)=='number' or type(v)=='string' or type(v)=='boolean') then
                        seen[k] = true
                        table.insert(rows, src..'\t'..tostring(k)..'\t'..tostring(v))
                    end
                end
            end
            add('CALCS', calcs)
            add('MAIN',  main)
            table.sort(rows)
            return table.concat(rows, '\n')
        ");
        var text = result is { Length: > 0 } && result[0] is string s ? s : "(no data)";
        System.IO.File.WriteAllText(filePath, text, System.Text.Encoding.UTF8);
    }

    public string GetActiveSkillDiag()
    {
        var result = State.DoString(@"
            local sg  = build and build.mainSocketGroup or -1
            local mo  = build and build.calcsTab and build.calcsTab.mainOutput
            local dps = mo and mo.TotalDPS or 'nil'
            local spd = mo and mo.Speed    or 'nil'
            local grp = build and build.skillsTab and build.skillsTab.socketGroupList
            local grpCount = grp and #grp or 0
            local activeGrp = grp and grp[sg]
            local dsl = activeGrp and activeGrp.displaySkillList
            local skillName = dsl and dsl[1] and dsl[1].activeEffect and dsl[1].activeEffect.grantedEffect and dsl[1].activeEffect.grantedEffect.name or 'unknown'
            return string.format('mainSocketGroup=%s grpCount=%s skill=%s TotalDPS=%s Speed=%s', tostring(sg), tostring(grpCount), tostring(skillName), tostring(dps), tostring(spd))
        ");
        return result is { Length: > 0 } && result[0] is string s ? s : "(no result)";
    }

    public List<SkillGroupEntry> GetSkillGroups()
    {
        var groups = new List<SkillGroupEntry>();
        var result = State.DoString(@"
            if not (build and build.skillsTab) then return nil end
            local out = {}
            local function strip(s)
                return s and s:gsub('%^x%x%x%x%x%x%x?',''):gsub('%^%d','') or ''
            end
            for i, group in ipairs(build.skillsTab.socketGroupList) do
                local name = strip(group.displayLabel)
                if name == '' then name = strip(group.label) end
                if name == '' then name = string.format('Group %d', i) end
                local enabled = (group.enabled ~= false) and 1 or 0
                table.insert(out, { i, name, strip(group.label or ''), enabled })
            end
            return out
        ");
        if (result is { Length: > 0 } && result[0] is NLua.LuaTable tbl)
        {
            foreach (var k in tbl.Keys)
            {
                if (tbl[k] is not NLua.LuaTable row) continue;
                var idx      = row[1L] is long l  ? (int)l  : 0;
                var name     = row[2L] as string   ?? $"Group {idx}";
                var label    = row[3L] as string   ?? "";
                var enabled  = row[4L] is long en  && en == 1L;
                groups.Add(new SkillGroupEntry(idx, name, label, enabled));
            }
        }
        return groups;
    }

    // ── Skill group / gem editing ──────────────────────────────────────────

    private void TriggerRecalc()
    {
        State.DoString(@"
            build.buildFlag = true
            runCallback('OnFrame')
            if build.calcsTab then build.calcsTab:BuildOutput() end
        ");
    }

    public void SetGemName(int groupIdx, int gemIdx, string name)
    {
        State["_grpIdx"] = (long)groupIdx;
        State["_gemIdx"] = (long)gemIdx;
        State["_gemVal"] = name;
        var result = State.DoString(@"
            local g = build.skillsTab.socketGroupList[_grpIdx]
            if not g then return 0 end
            local gem = g.gemList[_gemIdx]
            if not gem then return 0 end
            gem.nameSpec = _gemVal
            gem.gemId = nil; gem.gemData = nil; gem.skillId = nil; gem.grantedEffect = nil
            build.skillsTab:ProcessSocketGroup(g)
            return (gem.gemData ~= nil) and 1 or 0
        ");
        State["_grpIdx"] = null; State["_gemIdx"] = null; State["_gemVal"] = null;
        if (result is { Length: > 0 } && result[0] is long resolved && resolved == 1)
            TriggerRecalc();
    }

    public void SetGemLevel(int groupIdx, int gemIdx, int level)
    {
        State["_grpIdx"] = (long)groupIdx;
        State["_gemIdx"] = (long)gemIdx;
        State["_gemVal"] = (long)level;
        State.DoString("local g=build.skillsTab.socketGroupList[_grpIdx]; if g then local gem=g.gemList[_gemIdx]; if gem then gem.level=_gemVal end end");
        State["_grpIdx"] = null; State["_gemIdx"] = null; State["_gemVal"] = null;
        TriggerRecalc();
    }

    public void SetGemQuality(int groupIdx, int gemIdx, int quality)
    {
        State["_grpIdx"] = (long)groupIdx;
        State["_gemIdx"] = (long)gemIdx;
        State["_gemVal"] = (long)quality;
        State.DoString("local g=build.skillsTab.socketGroupList[_grpIdx]; if g then local gem=g.gemList[_gemIdx]; if gem then gem.quality=_gemVal end end");
        State["_grpIdx"] = null; State["_gemIdx"] = null; State["_gemVal"] = null;
        TriggerRecalc();
    }

    public void SetGemEnabled(int groupIdx, int gemIdx, bool enabled)
    {
        State["_grpIdx"] = (long)groupIdx;
        State["_gemIdx"] = (long)gemIdx;
        State["_gemVal"] = enabled;
        State.DoString("local g=build.skillsTab.socketGroupList[_grpIdx]; if g then local gem=g.gemList[_gemIdx]; if gem then gem.enabled=_gemVal end end");
        State["_grpIdx"] = null; State["_gemIdx"] = null; State["_gemVal"] = null;
        TriggerRecalc();
    }

    public void AddGemToGroup(int groupIdx)
    {
        State["_grpIdx"] = (long)groupIdx;
        State.DoString(@"
            local g = build.skillsTab.socketGroupList[_grpIdx]
            if g then
                table.insert(g.gemList, {
                    nameSpec='', level=19, quality=20, enabled=true,
                    count=1, enableGlobal1=true, enableGlobal2=true
                })
            end
        ");
        State["_grpIdx"] = null;
        TriggerRecalc();
    }

    public void RemoveGemFromGroup(int groupIdx, int gemIdx)
    {
        State["_grpIdx"] = (long)groupIdx;
        State["_gemIdx"] = (long)gemIdx;
        State.DoString("local g=build.skillsTab.socketGroupList[_grpIdx]; if g then table.remove(g.gemList,_gemIdx) end");
        State["_grpIdx"] = null; State["_gemIdx"] = null;
        TriggerRecalc();
    }

    public void AddSkillGroup()
    {
        State.DoString(@"
            table.insert(build.skillsTab.socketGroupList, {
                label='New Group', enabled=true, gemList={}
            })
        ");
        TriggerRecalc();
    }

    public void RemoveSkillGroup(int groupIdx)
    {
        State["_grpIdx"] = (long)groupIdx;
        State.DoString(@"
            table.remove(build.skillsTab.socketGroupList, _grpIdx)
            local n = #build.skillsTab.socketGroupList
            if build.mainSocketGroup > n then build.mainSocketGroup = math.max(1, n) end
            if build.calcsTab then
                build.calcsTab.input.skill_number = build.mainSocketGroup
            end
        ");
        State["_grpIdx"] = null;
        TriggerRecalc();
    }

    public void SetGroupLabel(int groupIdx, string label)
    {
        State["_grpIdx"] = (long)groupIdx;
        State["_gemVal"] = label;
        State.DoString("local g=build.skillsTab.socketGroupList[_grpIdx]; if g then g.label=_gemVal end");
        State["_grpIdx"] = null; State["_gemVal"] = null;
    }

    public void SetGroupEnabled(int groupIdx, bool enabled)
    {
        State["_grpIdx"] = (long)groupIdx;
        State["_gemVal"] = enabled;
        State.DoString("local g=build.skillsTab.socketGroupList[_grpIdx]; if g then g.enabled=_gemVal end");
        State["_grpIdx"] = null; State["_gemVal"] = null;
        TriggerRecalc();
    }

    public List<ActiveSkillEntry> GetActiveSkillsInGroup(int groupIndex)
    {
        var skills = new List<ActiveSkillEntry>();
        State["_grpIdx"] = (long)groupIndex;
        var result = State.DoString(@"
            if not (build and build.skillsTab) then return nil end
            local group = build.skillsTab.socketGroupList[_grpIdx]
            if not group then return nil end
            local dsl = group.displaySkillList
            if not (dsl and #dsl > 0) then return nil end
            local function strip(s)
                return s and s:gsub('%^x%x%x%x%x%x%x?',''):gsub('%^%d','') or ''
            end
            local out = {}
            for i, skill in ipairs(dsl) do
                local name = ''
                if skill.activeEffect and skill.activeEffect.grantedEffect then
                    name = strip(skill.activeEffect.grantedEffect.name or '')
                end
                if name == '' then name = string.format('Skill %d', i) end
                table.insert(out, { i, name })
            end
            return out
        ");
        State["_grpIdx"] = null;
        if (result is { Length: > 0 } && result[0] is NLua.LuaTable tbl)
        {
            foreach (var k in tbl.Keys)
            {
                if (tbl[k] is not NLua.LuaTable row) continue;
                var idx  = row[1L] is long l ? (int)l : 0;
                var name = row[2L] as string ?? $"Skill {idx}";
                skills.Add(new ActiveSkillEntry(idx, name));
            }
        }
        return skills;
    }

    public void SetActiveSkillGroup(int groupIndex, int activeSkillIndex = 1)
    {
        State["_skillIdx"]       = (long)groupIndex;
        State["_activeSkillIdx"] = (long)activeSkillIndex;
        State.DoString(@"
            if build and build.skillsTab then
                local group = build.skillsTab.socketGroupList[_skillIdx]
                if group then
                    group.mainActiveSkill = _activeSkillIdx
                    group.mainActiveSkillCalcs = _activeSkillIdx
                end
                -- 'MAIN' mode (mainOutput/sidebar) uses build.mainSocketGroup
                build.mainSocketGroup = _skillIdx
                -- 'CALCS' mode (calcsEnv/breakdown) uses calcsTab.input.skill_number
                if build.calcsTab then
                    build.calcsTab.input.skill_number = _skillIdx
                end
                build.buildFlag = true
                runCallback('OnFrame')
                if build.calcsTab then
                    build.calcsTab:BuildOutput()
                end
            end
        ");
        State["_skillIdx"]       = null;
        State["_activeSkillIdx"] = null;
    }

    public int GetMainSkillGroupIndex()
    {
        var result = State.DoString("return (build and build.mainSocketGroup) or 0");
        return result is { Length: > 0 } && result[0] is long l ? (int)l : 0;
    }

    public List<GemEntry> GetGemsInGroup(int groupIndex)
    {
        var gems = new List<GemEntry>();
        State["_gemGrpIdx"] = (long)groupIndex;
        var result = State.DoString(@"
            if not (build and build.skillsTab) then return nil end
            local group = build.skillsTab.socketGroupList[_gemGrpIdx]
            if not group then return nil end
            local function strip(s)
                return s and tostring(s):gsub('%^x%x%x%x%x%x%x?',''):gsub('%^%d','') or ''
            end
            local function gemColor(gd)
                if not gd then return '#CDD6F4' end
                local s = gd.reqStr or 0
                local d = gd.reqDex or 0
                local i = gd.reqInt or 0
                if s >= d and s >= i and s > 0 then return '#F38BA8'
                elseif d >= i and d > 0 then return '#A6E3A1'
                elseif i > 0 then return '#89B4FA'
                end
                return '#CDD6F4'
            end
            local out = {}
            for i, gem in ipairs(group.gemList) do
                local ge = gem.grantedEffect or (gem.gemData and gem.gemData.grantedEffect)
                local isSupport = (ge and ge.support) and 1 or 0
                local name = strip(gem.nameSpec)
                if name == '' and ge then name = strip(ge.name) end
                if name == '' then name = string.format('Gem %d', i) end
                local level = gem.level or 1
                local quality = gem.quality or 0
                local enabled = (gem.enabled ~= false) and 1 or 0
                local color = gemColor(gem.gemData)
                table.insert(out, {name, level, quality, enabled, isSupport, color})
            end
            return out
        ");
        State["_gemGrpIdx"] = null;
        if (result is { Length: > 0 } && result[0] is LuaTable tbl)
        {
            foreach (var k in tbl.Keys)
            {
                if (tbl[k] is not LuaTable row) continue;
                var name      = row[1L] as string ?? "";
                var level     = row[2L] is long lv ? (int)lv : 1;
                var quality   = row[3L] is long q  ? (int)q  : 0;
                var enabled   = row[4L] is long en && en == 1L;
                var isSupport = row[5L] is long su && su == 1L;
                var color     = row[6L] as string ?? "#CDD6F4";
                gems.Add(new GemEntry(name, level, quality, enabled, isSupport, color));
            }
        }
        return gems;
    }

    public List<GemTooltipLine> GetGemTooltip(int groupIdx, int gemIdx)
    {
        var lines = new List<GemTooltipLine>();
        State["_grpIdx"] = (long)groupIdx;
        State["_gemIdx"] = (long)gemIdx;
        var result = State.DoString(@"
            if not (build and build.skillsTab) then return nil end
            local group = build.skillsTab.socketGroupList[_grpIdx]
            if not group then return nil end
            local gem = group.gemList[_gemIdx]
            if not gem or not gem.gemData then return nil end
            local ge = gem.gemData.grantedEffect
            if not ge then return nil end

            local function strip(s)
                return s and tostring(s):gsub('%^x%x%x%x%x%x%x?',''):gsub('%^%d','') or ''
            end

            local displayInstance = gem.displayEffect or gem
            local level = displayInstance.level or gem.level or 1
            local gel = ge.levels and ge.levels[level] or {}

            local out = {}
            local function add(text, kind) table.insert(out, {strip(text), kind}) end

            -- Name
            local name = strip(ge.name ~= '' and ge.name or gem.nameSpec or '')
            add(name, 'name')

            -- Tags
            if gem.gemData.tagString and gem.gemData.tagString ~= '' then
                add(gem.gemData.tagString, 'tag')
            end

            -- Level / support info
            if not ge.support then
                local lvlStr = string.format('Level: %d%s', level,
                    (level >= (gem.gemData.naturalMaxLevel or 20)) and ' (Max)' or '')
                add(lvlStr, 'meta')
            else
                if gel.manaMultiplier then
                    add(string.format('Cost Multiplier: %d%%', gel.manaMultiplier + 100), 'meta')
                end
                if gel.reservationMultiplier then
                    add(string.format('Reservation Multiplier: %d%%', gel.reservationMultiplier + 100), 'meta')
                end
            end

            -- Reservation
            if gel.spiritReservationFlat then
                add(string.format('Reservation: %d Spirit', gel.spiritReservationFlat), 'meta')
            elseif gel.spiritReservationPercent then
                add(string.format('Reservation: %.1f%% Spirit', gel.spiritReservationPercent), 'meta')
            end

            -- Cost
            if gel.cost then
                local costParts = {}
                local costRes = {
                    {res='Mana',    div=1, label='Mana'},
                    {res='Life',    div=1, label='Life'},
                    {res='ES',      div=1, label='Energy Shield'},
                    {res='Spirit',  div=1, label='Spirit'},
                }
                for _, r in ipairs(costRes) do
                    if gel.cost[r.res] then
                        table.insert(costParts, string.format('%g %s', gel.cost[r.res], r.label))
                    end
                end
                if #costParts > 0 then
                    add('Cost: ' .. table.concat(costParts, ', '), 'meta')
                end
            end

            -- Cooldown
            if gel.cooldown then
                add(string.format('Cooldown Time: %.2f sec', gel.cooldown), 'meta')
            end

            -- Attack / cast
            if not ge.support then
                if gem.gemData.tags and gem.gemData.tags.attack then
                    if gel.attackSpeedMultiplier then
                        add(string.format('Attack Speed: %d%% of base', gel.attackSpeedMultiplier + 100), 'meta')
                    end
                    if gel.baseMultiplier then
                        add(string.format('Attack Damage: %g%% of base', gel.baseMultiplier * 100), 'meta')
                    end
                else
                    if ge.castTime and ge.castTime > 0 then
                        add(string.format('Cast Time: %.2f sec', ge.castTime), 'meta')
                    elseif ge.castTime and ge.castTime == 0 then
                        add('Cast Time: Instant', 'meta')
                    end
                end
                if gel.critChance then
                    add(string.format('Critical Hit Chance: %.2f%%', gel.critChance), 'meta')
                end
            end

            -- Stat descriptions
            if build.data and build.data.describeStats and ge.statSets and ge.statSets[1] then
                local statSet = ge.statSets[1]
                local ok, stats = pcall(calcLib.buildSkillInstanceStats, displayInstance, ge, statSet)
                if ok and stats then
                    -- Raw stats for C# localised rendering (stat_id=value pairs)
                    local raw_parts = {tostring(statSet.statDescriptionScope or 'gem_stat_descriptions')}
                    for stat_id, value in pairs(stats) do
                        if type(value) == 'number' and value ~= 0 then
                            table.insert(raw_parts, tostring(stat_id) .. '=' .. tostring(value))
                        end
                    end
                    -- English rendered fallback (shown when no RU template found)
                    local ok2, descs = pcall(build.data.describeStats, stats, statSet.statDescriptionScope)
                    if ok2 and descs and #descs > 0 then
                        add('---', 'sep')
                        add(table.concat(raw_parts, '|'), 'raw_stats')
                        for _, line in ipairs(descs) do
                            add(line, 'stat')
                        end
                    end
                end
            end

            -- Flavour description
            if ge.description and ge.description ~= '' then
                add('---', 'sep')
                add(ge.description, 'desc')
            end

            return out
        ");
        State["_grpIdx"] = null; State["_gemIdx"] = null;

        if (result is { Length: > 0 } && result[0] is NLua.LuaTable tbl)
        {
            foreach (var k in tbl.Keys)
            {
                if (tbl[k] is not NLua.LuaTable row) continue;
                var text = row[1L] as string ?? "";
                var kind = row[2L] as string ?? "stat";
                if (!string.IsNullOrEmpty(text))
                    lines.Add(new GemTooltipLine(text, kind));
            }
        }
        return lines;
    }

    /// <summary>Returns tooltip lines for a gem looked up by name (no socket-group context).
    /// Skips stat descriptions since those require a live calcLib instance.</summary>
    public List<GemTooltipLine> GetGemTooltipByName(string gemName, int level = 20)
    {
        var lines = new List<GemTooltipLine>();
        State["_gemNameKey"]   = gemName;
        State["_gemLevelHint"] = (long)level;
        var result = State.DoString(@"
            if not (data and data.gems) then return nil end
            -- gems are keyed by internal ID; search by gem.name
            local gemData = nil
            for _, gem in pairs(data.gems) do
                if gem.name == _gemNameKey then gemData = gem; break end
            end
            if not gemData then return nil end
            local ge = gemData.grantedEffect
            if not ge then return nil end

            local maxLevel = ge.levels and #ge.levels or 20
            local lv = math.min(_gemLevelHint, maxLevel)
            if lv == 0 then lv = 1 end
            local gel = ge.levels and ge.levels[lv] or {}

            local function strip(s)
                return s and tostring(s):gsub('%^x%x%x%x%x%x%x?',''):gsub('%^%d','') or ''
            end

            local out = {}
            local function add(text, kind) table.insert(out, {strip(text), kind}) end

            -- Name
            local name = strip(ge.name ~= '' and ge.name or _gemNameKey or '')
            add(name, 'name')

            -- Tags
            if gemData.tagString and gemData.tagString ~= '' then
                add(gemData.tagString, 'tag')
            end

            -- Level / support info
            if not ge.support then
                local lvlStr = string.format('Level: %d%s', lv,
                    (lv >= (gemData.naturalMaxLevel or 20)) and ' (Max)' or '')
                add(lvlStr, 'meta')
            else
                if gel.manaMultiplier then
                    add(string.format('Cost Multiplier: %d%%', gel.manaMultiplier + 100), 'meta')
                end
                if gel.reservationMultiplier then
                    add(string.format('Reservation Multiplier: %d%%', gel.reservationMultiplier + 100), 'meta')
                end
            end

            -- Reservation
            if gel.spiritReservationFlat then
                add(string.format('Reservation: %d Spirit', gel.spiritReservationFlat), 'meta')
            elseif gel.spiritReservationPercent then
                add(string.format('Reservation: %.1f%% Spirit', gel.spiritReservationPercent), 'meta')
            end

            -- Cost
            if gel.cost then
                local costParts = {}
                local costRes = {
                    {res='Mana',   label='Mana'},
                    {res='Life',   label='Life'},
                    {res='ES',     label='Energy Shield'},
                    {res='Spirit', label='Spirit'},
                }
                for _, r in ipairs(costRes) do
                    if gel.cost[r.res] then
                        table.insert(costParts, string.format('%g %s', gel.cost[r.res], r.label))
                    end
                end
                if #costParts > 0 then
                    add('Cost: ' .. table.concat(costParts, ', '), 'meta')
                end
            end

            -- Cooldown
            if gel.cooldown then
                add(string.format('Cooldown Time: %.2f sec', gel.cooldown), 'meta')
            end

            -- Attack / cast
            if not ge.support then
                if gemData.tags and gemData.tags.attack then
                    if gel.attackSpeedMultiplier then
                        add(string.format('Attack Speed: %d%% of base', gel.attackSpeedMultiplier + 100), 'meta')
                    end
                    if gel.baseMultiplier then
                        add(string.format('Attack Damage: %g%% of base', gel.baseMultiplier * 100), 'meta')
                    end
                else
                    if ge.castTime and ge.castTime > 0 then
                        add(string.format('Cast Time: %.2f sec', ge.castTime), 'meta')
                    elseif ge.castTime and ge.castTime == 0 then
                        add('Cast Time: Instant', 'meta')
                    end
                end
                if gel.critChance then
                    add(string.format('Critical Hit Chance: %.2f%%', gel.critChance), 'meta')
                end
            end

            -- Flavour description
            if ge.description and ge.description ~= '' then
                add('---', 'sep')
                add(ge.description, 'desc')
            end

            return out
        ");
        State["_gemNameKey"] = null; State["_gemLevelHint"] = null;

        if (result is { Length: > 0 } && result[0] is NLua.LuaTable tbl)
        {
            foreach (var k in tbl.Keys)
            {
                if (tbl[k] is not NLua.LuaTable row) continue;
                var text = row[1L] as string ?? "";
                var kind = row[2L] as string ?? "stat";
                if (!string.IsNullOrEmpty(text))
                    lines.Add(new GemTooltipLine(text, kind));
            }
        }
        return lines;
    }

    public Dictionary<string, string> GetGemColors()
    {
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        var result = State.DoString(@"
            local function gemColor(gem)
                local s = gem.reqStr or 0
                local d = gem.reqDex or 0
                local i = gem.reqInt or 0
                if s >= d and s >= i and s > 0 then return '#F38BA8'
                elseif d >= i and d > 0 then return '#A6E3A1'
                elseif i > 0 then return '#89B4FA'
                end
                return '#CDD6F4'
            end
            local colors = {}
            for id, gem in pairs(data.gems) do
                if gem.name then
                    colors[gem.name] = gemColor(gem)
                end
            end
            return colors
        ");
        if (result is { Length: > 0 } && result[0] is LuaTable tbl)
            foreach (var k in tbl.Keys)
                if (k is string name && tbl[k] is string color)
                    dict[name] = color;
        return dict;
    }

    public void AddSkillGroupWithGem(string gemName)
    {
        State["_newGemName"] = gemName;
        State.DoString(@"
            local newGroup = {label='', enabled=true, gemList={
                {nameSpec=_newGemName, level=19, quality=20, enabled=true,
                 count=1, enableGlobal1=true, enableGlobal2=true}
            }}
            table.insert(build.skillsTab.socketGroupList, newGroup)
            build.skillsTab:ProcessSocketGroup(newGroup)
        ");
        State["_newGemName"] = null;
        TriggerRecalc();
    }

    public (List<string> Active, List<string> Support) GetAvailableGemNames()
    {
        var result = State.DoString(@"
            local active = {}
            local support = {}
            for id, gem in pairs(data.gems) do
                if gem.name and gem.grantedEffect then
                    if gem.grantedEffect.support then
                        table.insert(support, gem.name)
                    else
                        table.insert(active, gem.name)
                    end
                end
            end
            table.sort(active)
            table.sort(support)
            return active, support
        ");
        static List<string> toList(object? tbl) {
            var list = new List<string>();
            if (tbl is LuaTable t)
                foreach (var k in t.Keys)
                    if (t[k] is string s) list.Add(s);
            return list;
        }
        return (
            result is { Length: > 0 } ? toList(result[0]) : [],
            result is { Length: > 1 } ? toList(result[1]) : []
        );
    }

    public List<ModifierEntry> GetModifierTable(string statName)
    {
        var rows = new List<ModifierEntry>();
        State["_modStat"] = statName;
        var result = State.DoString(@"
            local env = build and build.calcsTab and build.calcsTab.calcsEnv
            if not env then return nil end
            local modDB = env.player and env.player.modDB
            if not modDB then return nil end
            local out = {}
            local function strip(s)
                if type(s) ~= 'string' then return tostring(s or '') end
                return (s:gsub('%^x%x%x%x%x%x%x?',''):gsub('%^%d',''):gsub('%^_?x%x+',''))
            end
            local function walkDB(db)
                if not db then return end
                local modList = db.mods and db.mods[_modStat]
                if modList then
                    for _, mod in ipairs(modList) do
                        local val = type(mod.value)=='number' and mod.value or 0
                        if val ~= 0 or mod.type == 'OVERRIDE' then
                            local src = mod.source or ''
                            local srcType, srcName = src:match('^([^:]+):(.*)$')
                            table.insert(out, {
                                tostring(val),
                                mod.type or '',
                                srcType or src,
                                strip(srcName or '')
                            })
                        end
                    end
                end
                walkDB(db.parent)
            end
            walkDB(modDB)
            return out
        ");
        State["_modStat"] = null;
        if (result is { Length: > 0 } && result[0] is NLua.LuaTable tbl)
        {
            foreach (var k in tbl.Keys)
            {
                if (tbl[k] is not NLua.LuaTable row) continue;
                var val      = row[1L] as string ?? "0";
                var modType  = row[2L] as string ?? "";
                var source   = row[3L] as string ?? "";
                var srcName  = row[4L] as string ?? "";
                rows.Add(new ModifierEntry(val, modType, source, srcName));
            }
        }
        return rows;
    }

    public object? GetStat(string statName)
    {
        var result = State.DoString(
            $"local o = build and build.calcsTab and build.calcsTab.mainOutput" +
            $"; return o and o.{statName} or nil");
        return result is { Length: > 0 } ? result[0] : null;
    }

    public Dictionary<string, object?> GetAllStats()
    {
        var dict = new Dictionary<string, object?>(StringComparer.Ordinal);
        var result = State.DoString(@"
            local main  = build and build.calcsTab and build.calcsTab.mainOutput
            local calcs = build and build.calcsTab and build.calcsTab.calcsOutput
            if not main then return nil end
            local t = {}
            -- calcs first: provides per-type Min/Max (only written in CALCS mode)
            if calcs then
                for k, v in pairs(calcs) do
                    if type(v) == 'number' or type(v) == 'string' or type(v) == 'boolean' then
                        t[k] = v
                    end
                end
            end
            -- main overrides: authoritative for DPS, Speed, Life, etc.
            for k, v in pairs(main) do
                if type(v) == 'number' or type(v) == 'string' or type(v) == 'boolean' then
                    t[k] = v
                end
            end
            return t
        ");
        if (result is { Length: > 0 } && result[0] is LuaTable tbl)
        {
            foreach (var key in tbl.Keys)
                dict[key.ToString()!] = tbl[key];
        }
        return dict;
    }

    public List<ConfigOption> GetConfigOptions()
    {
        var result = new List<ConfigOption>();
        try
        {
            var raw = State.DoString(@"
                local function strip(s)
                    if not s then return '' end
                    return tostring(s):gsub('%^x%x%x%x%x%x',''):gsub('%^%d',''):gsub('%^_x%x+',''):gsub('%^_?%d','')
                end
                local varList = LoadModule('Modules/ConfigOptions')
                local input = (build and build.configTab
                    and build.configTab.configSets
                    and build.configTab.configSets[build.configTab.activeConfigSetId]
                    and build.configTab.configSets[build.configTab.activeConfigSetId].input) or {}
                local rows = {}
                local section = 'General'
                for _, v in ipairs(varList) do
                    if v.section then
                        section = v.section
                    elseif v.var and v.label and not v.legacy
                        and not v.ifSkillData and not v.ifCond and not v.ifOption
                        and not v.ifItem and not v.ifEnemyCond and not v.ifMod then
                        local t = v.type or 'check'
                        if t=='check' or t=='list' or t=='count' or t=='integer' or t=='float' or t=='countAllowZero' or t=='text' then
                            local row = {v.var, strip(v.label), t, section, ''}
                            local val = input[v.var]
                            if val ~= nil then row[5] = tostring(val) end
                            if t == 'list' and v.list then
                                local opts = {}
                                for _, item in ipairs(v.list) do
                                    table.insert(opts, tostring(item.val)..'\t'..strip(item.label or tostring(item.val)))
                                end
                                row[6] = opts
                            end
                            table.insert(rows, row)
                        end
                    end
                end
                return rows
            ");

            if (raw is not { Length: > 0 } || raw[0] is not NLua.LuaTable rows)
                return result;

            foreach (var rowKey in rows.Keys)
            {
                if (rows[rowKey] is not NLua.LuaTable row) continue;
                var var_   = row[1L] as string ?? "";
                var label  = row[2L] as string ?? "";
                var type_  = row[3L] as string ?? "check";
                var sect   = row[4L] as string ?? "";
                var curVal = row[5L] as string ?? "";

                var listOpts = Array.Empty<ConfigListItem>();
                if (row[6L] is NLua.LuaTable listTbl)
                {
                    var opts = new List<ConfigListItem>();
                    foreach (var k in listTbl.Keys)
                    {
                        if (listTbl[k] is not string s) continue;
                        var parts = s.Split('\t', 2);
                        if (parts.Length == 2)
                            opts.Add(new ConfigListItem(parts[0], parts[1]));
                    }
                    listOpts = [.. opts];
                }
                result.Add(new ConfigOption(var_, label, type_, sect, curVal, listOpts));
            }
        }
        catch { /* return whatever we have so far */ }
        return result;
    }

    public void SetConfigValue(string var, string value)
    {
        State["_cfgVar"] = var;
        State["_cfgVal"] = value;
        State.DoString(@"
            if build and build.configTab and build.configTab.configSets then
                local input = build.configTab.configSets[build.configTab.activeConfigSetId].input
                if _cfgVal == '' or _cfgVal == nil then
                    input[_cfgVar] = nil
                else
                    local n = tonumber(_cfgVal)
                    if n ~= nil then
                        input[_cfgVar] = n
                    elseif _cfgVal == 'true' then
                        input[_cfgVar] = true
                    elseif _cfgVal == 'false' then
                        input[_cfgVar] = false
                    else
                        input[_cfgVar] = _cfgVal
                    end
                end
            end
            runCallback('OnFrame')
        ");
        State["_cfgVar"] = null;
        State["_cfgVal"] = null;
    }

    public string GetNotes()
    {
        var result = State.DoString(@"
            if build and build.notesTab and build.notesTab.controls and build.notesTab.controls.edit then
                return build.notesTab.controls.edit.buf or ''
            end
            return ''
        ");
        return result is { Length: > 0 } && result[0] is string s ? s : "";
    }

    public void SetNotes(string text)
    {
        State["_notesText"] = text;
        State.DoString(@"
            if build and build.notesTab and build.notesTab.controls and build.notesTab.controls.edit then
                build.notesTab.controls.edit:SetText(_notesText or '')
            end
        ");
        State["_notesText"] = null;
    }

    // ── Items tab ──────────────────────────────────────────────────────────────

    public Dictionary<string, ItemEntry> GetEquippedItems()
    {
        var dict = new Dictionary<string, ItemEntry>(StringComparer.Ordinal);
        var result = State.DoString(@"
            if not (build and build.itemsTab) then return {} end
            local function strip(s)
                if not s then return '' end
                return tostring(s):gsub('%^x%x%x%x%x%x%x?',''):gsub('%^%d',''):gsub('%^_?x%x+','')
            end
            local function mods(lines)
                local out = {}
                for _, ml in ipairs(lines or {}) do
                    if ml.line then
                        local t = strip(ml.line)
                        if t ~= '' then table.insert(out, t) end
                    end
                end
                return out
            end
            -- Source of truth is slots[name].selItemId. For jewel sockets the data is
            -- stored only on the slot control (activeItemSet[nodeId] has no selItemId).
            local result = {}
            for slotName, slot in pairs(build.itemsTab.slots) do
                local id = slot and slot.selItemId
                if type(id) == 'number' and id > 0 then
                    local item = build.itemsTab.items[id]
                    if item then
                        table.insert(result, {
                            tostring(slotName),
                            strip(item.name or ''),
                            strip(item.baseName or ''),
                            item.rarity or 'NORMAL',
                            item.itemLevel or 0,
                            mods(item.enchantModLines),
                            mods(item.implicitModLines),
                            mods(item.explicitModLines)
                        })
                    end
                end
            end
            return result
        ");
        if (result is { Length: > 0 } && result[0] is LuaTable tbl)
        {
            foreach (var k in tbl.Keys)
            {
                if (tbl[k] is not LuaTable row) continue;
                var slot     = row[1L] as string ?? "";
                var name     = row[2L] as string ?? "";
                var baseName = row[3L] as string ?? "";
                var rarity   = row[4L] as string ?? "NORMAL";
                var ilvl     = row[5L] is long li ? (int)li : 0;
                var enchants  = ToStringList(row[6L]);
                var implicits = ToStringList(row[7L]);
                var explicits = ToStringList(row[8L]);
                if (!string.IsNullOrEmpty(slot))
                    dict[slot] = new ItemEntry(name, baseName, rarity, ilvl, enchants, implicits, explicits);
            }
        }
        return dict;
    }

    public void UnequipItem(string slotName)
    {
        State["_slotName"] = slotName;
        State.DoString(@"
            if build and build.itemsTab then
                local slot = build.itemsTab.slots[_slotName]
                if slot then
                    slot:SetSelItemId(0)
                    build.itemsTab:PopulateSlots()
                end
            end
        ");
        State["_slotName"] = null;
        TriggerRecalc();
    }

    /// <summary>Returns every item in the build's item pool with equipped-slot info.</summary>
    public List<ItemPoolEntry> GetItemPool()
    {
        var pool = new List<ItemPoolEntry>();
        var result = State.DoString(@"
            if not (build and build.itemsTab) then return {} end
            local itemsTab = build.itemsTab
            -- Map itemId -> slotName using slots[].selItemId (works for ALL slot types
            -- including jewel sockets, whose data is NOT mirrored to activeItemSet).
            local equipped = {}
            for slotName, slot in pairs(itemsTab.slots) do
                local id = slot and slot.selItemId
                if type(id) == 'number' and id > 0 then
                    equipped[id] = tostring(slotName)
                end
            end
            local function strip(s)
                if not s then return '' end
                return tostring(s):gsub('%^x%x%x%x%x%x%x?',''):gsub('%^%d',''):gsub('%^_?x%x+','')
            end
            local result = {}
            for _, itemId in ipairs(itemsTab.itemOrderList) do
                local item = itemsTab.items[itemId]
                if item then
                    local primary = ''
                    if item.GetPrimarySlot then
                        primary = item:GetPrimarySlot() or ''
                    end
                    table.insert(result, {
                        itemId,
                        strip(item.name or ''),
                        strip(item.baseName or ''),
                        item.rarity or 'NORMAL',
                        item.itemLevel or 0,
                        primary,
                        equipped[itemId] or ''
                    })
                end
            end
            return result
        ");
        if (result is { Length: > 0 } && result[0] is LuaTable tbl)
        {
            foreach (var k in tbl.Keys)
            {
                if (tbl[k] is not LuaTable row) continue;
                var id          = row[1L] is long li  ? (int)li  : 0;
                var name        = row[2L] as string   ?? "";
                var baseName    = row[3L] as string   ?? "";
                var rarity      = row[4L] as string   ?? "NORMAL";
                var ilvl        = row[5L] is long il  ? (int)il  : 0;
                var primarySlot = row[6L] as string   ?? "";
                var equSlot     = row[7L] as string   ?? "";
                if (id > 0)
                    pool.Add(new ItemPoolEntry(id, name, baseName, rarity, ilvl, primarySlot, equSlot));
            }
        }
        return pool;
    }

    /// <summary>
    /// Parses raw item text (e.g. copied from PoE2 via Ctrl+C) and adds it to the item pool.
    /// Returns true on success.
    /// </summary>
    public bool ImportItemFromText(string rawText)
    {
        State["_importRaw"] = rawText;
        var result = State.DoString(@"
            if not (build and build.itemsTab) then return false end
            build.itemsTab:CreateDisplayItemFromRaw(_importRaw)
            local d = build.itemsTab.displayItem
            if d then
                -- Recompute rune-derived mods so newly-emitted Rune: lines actually
                -- contribute to the item's stats (PoB doesn't auto-call this on parse).
                if d.itemSocketCount and d.itemSocketCount > 0 and d.UpdateRunes
                    and d.base and d.base.tags then
                    pcall(function()
                        d:UpdateRunes()
                        if d.BuildAndParseRaw then d:BuildAndParseRaw() end
                    end)
                end
                build.itemsTab:AddDisplayItem(true)  -- noAutoEquip = true
                return true
            end
            return false
        ");
        State["_importRaw"] = null;
        TriggerRecalc();
        return result is { Length: > 0 } && result[0] is bool b && b;
    }

    /// <summary>Equips an item from the pool into the specified slot.</summary>
    public void EquipItemToSlot(int itemId, string slotName)
    {
        State["_equipItemId"]   = (long)itemId;
        State["_equipSlotName"] = slotName;
        State.DoString(@"
            if not (build and build.itemsTab) then return end
            local slot = build.itemsTab.slots[_equipSlotName]
            if slot and build.itemsTab.items[_equipItemId] then
                slot:SetSelItemId(_equipItemId)
                build.itemsTab:PopulateSlots()
                build.itemsTab:AddUndoState()
                build.buildFlag = true
            end
        ");
        State["_equipItemId"]   = null;
        State["_equipSlotName"] = null;
        TriggerRecalc();
    }

    /// <summary>
    /// Renders a full PoB-style item tooltip into structured lines.
    /// When <paramref name="slotName"/> is provided the item is taken from
    /// that slot (and PoB appends the "Removing this item from X will give you:"
    /// delta block). Otherwise <paramref name="itemId"/> is used and no delta is shown.
    /// </summary>
    public List<ItemTooltipLine> GetItemTooltipLines(int itemId, string? slotName = null)
    {
        var lines = new List<ItemTooltipLine>();
        State["_ttItemId"]   = (long)itemId;
        State["_ttSlotName"] = slotName ?? "";
        var result = State.DoString(@"
            if not (build and build.itemsTab) then return nil end
            local slot = nil
            local item = nil
            if _ttSlotName ~= '' then
                slot = build.itemsTab.slots[_ttSlotName]
                if slot and type(slot.selItemId) == 'number' and slot.selItemId > 0 then
                    item = build.itemsTab.items[slot.selItemId]
                end
            end
            if not item and _ttItemId > 0 then
                item = build.itemsTab.items[_ttItemId]
            end
            if not item then return nil end

            -- Force per-slot delta block (PoB has main.slotOnlyTooltips for the same purpose).
            local prevSlotOnly = main.slotOnlyTooltips
            main.slotOnlyTooltips = (slot ~= nil)

            -- Stub mimicking Classes/Tooltip.lua just enough for AddItemTooltip.
            local stub = {
                lines = {}, blocks = {{ height = 0 }},
                tooltipHeader = false, center = false, color = nil, maxWidth = nil,
            }
            function stub:Clear() self.lines = {}; self.blocks = {{ height = 0 }} end
            function stub:SetRecipe(_) end
            function stub:AddLine(size, text, font, background)
                if not text then return end
                -- Skip unique flavour text (orange italic-ish, FONTIN SC font, not the title).
                -- Title is also FONTIN SC but has size 22; flavour has size 18 (fontSizeBig).
                if font and font:find('FONTIN SC ITALIC') then return end
                if font == 'FONTIN SC' and (size or 0) <= 18
                   and text:find('^%^xAF6025') then return end
                -- Skip the Press Ctrl+D footer.
                if text:find('Press Ctrl%+D') then return end
                -- For PoB-crafted rares the title and baseName lines are identical;
                -- skip the duplicate header. Compare plain text against the most
                -- recent text line, ignoring any leading inline color codes.
                local function plainOf(t)
                    if not t then return '' end
                    t = t:gsub('%^[xX]?%x*', '')
                    t = t:gsub('%^%d', '')
                    return t
                end
                if #self.lines > 0 then
                    local prev = self.lines[#self.lines]
                    if prev.kind == 'text' then
                        local pp = plainOf(prev.text)
                        local cp = plainOf(text)
                        if pp == cp and pp ~= '' then return end
                    end
                end
                for line in (text .. '\n'):gmatch('([^\n]*)\n') do
                    -- AddItemTooltip's compare block emits ^7-prefixed headers; strip the prefix for matching.
                    local plain = (line:gsub('^%^[xX]?%x*', '')):gsub('^%^%d', '')
                    if plain:find('^Equipping') or plain:find('^Removing') then
                        table.insert(self.blocks, { height = (size or 0) + 2 })
                    else
                        self.blocks[#self.blocks].height = self.blocks[#self.blocks].height + (size or 0) + 2
                    end
                    table.insert(self.lines, {
                        kind = 'text',
                        size = size or 14,
                        text = line,
                        center = self.center and 1 or 0,
                        block = #self.blocks,
                        font = font or '',
                    })
                end
            end
            function stub:AddSeparator(size)
                local last = self.lines[#self.lines]
                if last and last.kind == 'separator' then return end
                table.insert(self.lines, {
                    kind = 'separator',
                    size = size or 10,
                    text = '',
                    center = 0,
                    block = last and last.block or 1,
                    font = '',
                })
            end

            local ok, err = pcall(function()
                build.itemsTab:AddItemTooltip(stub, item, slot)
            end)
            main.slotOnlyTooltips = prevSlotOnly
            if not ok then
                table.insert(stub.lines, { kind='text', size=14, text='^xFF5555Tooltip error: '..tostring(err),
                    center=0, block=1, font='' })
            end
            return stub.lines
        ");
        State["_ttItemId"]   = null;
        State["_ttSlotName"] = null;

        if (result is { Length: > 0 } && result[0] is LuaTable tbl)
        {
            foreach (var k in tbl.Keys)
            {
                if (tbl[k] is not LuaTable row) continue;
                var kind     = row["kind"]   as string ?? "text";
                var size     = row["size"]   is long sz ? (int)sz : 14;
                var text     = row["text"]   as string ?? "";
                var centered = row["center"] is long c  && c == 1L;
                var block    = row["block"]  is long bl ? (int)bl : 1;
                var font     = row["font"]   as string;
                lines.Add(new ItemTooltipLine(kind, size, text, centered, block,
                    string.IsNullOrEmpty(font) ? null : font));
            }
        }
        return lines;
    }

    /// <summary>Removes an item entirely from the build's item pool.</summary>
    public void DeleteItemFromPool(int itemId)
    {
        State["_delItemId"] = (long)itemId;
        State.DoString(@"
            if not (build and build.itemsTab) then return end
            local item = build.itemsTab.items[_delItemId]
            if item then
                build.itemsTab:DeleteItem(item)
                build.itemsTab:PopulateSlots()
            end
        ");
        State["_delItemId"] = null;
        TriggerRecalc();
    }

    // ── Weapon swap (within active item set) ──────────────────────────────────

    /// <summary>Returns true if the secondary weapon pair (Weapon 1/2 Swap) is the active one.</summary>
    public bool IsWeaponSwapActive()
    {
        var result = State.DoString(@"
            if not (build and build.itemsTab and build.itemsTab.activeItemSet) then return false end
            return build.itemsTab.activeItemSet.useSecondWeaponSet and true or false
        ");
        return result is { Length: > 0 } && result[0] is bool b && b;
    }

    /// <summary>Switches between primary (false) and secondary (true) weapon pairs.</summary>
    public void SetWeaponSwapActive(bool useSecondary)
    {
        State["_useSecondSet"] = useSecondary;
        State.DoString(@"
            if not (build and build.itemsTab and build.itemsTab.activeItemSet) then return end
            local prev = build.itemsTab.activeItemSet.useSecondWeaponSet and true or false
            local want = _useSecondSet and true or false
            if prev ~= want then
                build.itemsTab.activeItemSet.useSecondWeaponSet = want
                build.itemsTab:AddUndoState()
                build.buildFlag = true
                build.itemsTab:PopulateSlots()
            end
        ");
        State["_useSecondSet"] = null;
        TriggerRecalc();
    }

    // ── Jewel sockets (passive tree socket nodes) ─────────────────────────────

    /// <summary>Returns all jewel socket slots present on the current passive tree (allocated and not).</summary>
    public List<JewelSocketEntry> GetJewelSockets()
    {
        var list = new List<JewelSocketEntry>();
        var result = State.DoString(@"
            if not (build and build.itemsTab and build.spec) then return {} end
            local itemsTab = build.itemsTab
            local function strip(s)
                if not s then return '' end
                return tostring(s):gsub('%^x%x%x%x%x%x%x?',''):gsub('%^%d',''):gsub('%^_?x%x+','')
            end
            local function mods(lines)
                local out = {}
                for _, ml in ipairs(lines or {}) do
                    if ml.line then
                        local t = strip(ml.line)
                        if t ~= '' then table.insert(out, t) end
                    end
                end
                return out
            end
            local nodes = build.spec.nodes or {}
            local socketIds = {}
            for nid, n in pairs(nodes) do
                if n and (n.type == 'Socket' or n.containJewelSocket) then
                    table.insert(socketIds, nid)
                end
            end
            table.sort(socketIds, function(a,b) return a < b end)
            local allocNodes = build.spec.allocNodes or {}
            local result = {}
            for _, nid in ipairs(socketIds) do
                local node = nodes[nid]
                local slotName = 'Jewel ' .. nid
                -- Source of truth is the slot control (activeItemSet[nid] has no selItemId)
                local slot = itemsTab.slots[slotName]
                local row = {
                    nid,
                    strip((node and node.name) or ''),
                    slotName,
                    allocNodes[nid] ~= nil,
                    '', '', 'NORMAL', 0, {}, {}, {}
                }
                local id = slot and slot.selItemId
                if type(id) == 'number' and id > 0 then
                    local item = itemsTab.items[id]
                    if item then
                        row[5]  = strip(item.name or '')
                        row[6]  = strip(item.baseName or '')
                        row[7]  = item.rarity or 'NORMAL'
                        row[8]  = item.itemLevel or 0
                        row[9]  = mods(item.enchantModLines)
                        row[10] = mods(item.implicitModLines)
                        row[11] = mods(item.explicitModLines)
                    end
                end
                table.insert(result, row)
            end
            return result
        ");
        if (result is { Length: > 0 } && result[0] is LuaTable tbl)
        {
            foreach (var k in tbl.Keys)
            {
                if (tbl[k] is not LuaTable row) continue;
                var nid       = row[1L] is long nL ? (int)nL : (row[1L] is double nD ? (int)nD : 0);
                var nodeName  = row[2L] as string ?? "";
                var slotName  = row[3L] as string ?? "";
                var alloc     = row[4L] is bool ab && ab;
                var itemName  = row[5L] as string ?? "";
                var baseName  = row[6L] as string ?? "";
                var rarity    = row[7L] as string ?? "NORMAL";
                var ilvl      = row[8L] is long li ? (int)li : 0;
                var enchants  = ToStringList(row[9L]);
                var implicits = ToStringList(row[10L]);
                var explicits = ToStringList(row[11L]);

                ItemEntry? item = string.IsNullOrEmpty(itemName)
                    ? null
                    : new ItemEntry(itemName, baseName, rarity, ilvl, enchants, implicits, explicits);
                list.Add(new JewelSocketEntry(nid, nodeName, slotName, alloc, item));
            }
        }
        return list;
    }

    // ── Item editor ───────────────────────────────────────────────────────────

    /// <summary>Returns sorted list of all item base type categories.</summary>
    public List<string> GetItemBaseCategories()
    {
        var list = new List<string>();
        var result = State.DoString(@"
            if not (build and build.data and build.data.itemBaseTypeList) then return {} end
            local out = {}
            for _, name in ipairs(build.data.itemBaseTypeList) do
                table.insert(out, name)
            end
            return out
        ");
        if (result is { Length: > 0 } && result[0] is LuaTable t)
            foreach (var k in t.Keys)
                if (t[k] is string s) list.Add(s);
        return list;
    }

    /// <summary>Returns all item bases for a given category, sorted by level requirement.</summary>
    public List<BaseItemEntry> GetItemBasesForCategory(string category)
    {
        State["_itemCat"] = category;
        var list = new List<BaseItemEntry>();
        var result = State.DoString(@"
            if not (build and build.data and build.data.itemBaseLists) then return {} end
            local lst = build.data.itemBaseLists[_itemCat]
            if not lst then return {} end
            local out = {}
            for _, entry in ipairs(lst) do
                local impl   = (entry.base and entry.base.implicit) or ''
                local lvlReq = (entry.base and entry.base.req and entry.base.req.level) or 0
                local btype  = (entry.base and entry.base.type) or ''
                local socks  = (entry.base and entry.base.socketLimit) or 0
                table.insert(out, { entry.name, impl, lvlReq, btype, socks })
            end
            return out
        ");
        State["_itemCat"] = null;
        if (result is { Length: > 0 } && result[0] is LuaTable t)
            foreach (var k in t.Keys)
                if (t[k] is LuaTable row)
                {
                    var name    = row[1L] as string ?? "";
                    var impl    = row[2L] as string ?? "";
                    var lvl     = row[3L] is long li ? (int)li : 0;
                    var btype   = row[4L] as string ?? "";
                    var socks   = row[5L] is long si ? (int)si : 0;
                    list.Add(new BaseItemEntry(name, category, impl, lvl, btype, socks));
                }
        return list;
    }

    /// <summary>
    /// Returns all item affixes (from data.itemMods.Item) that can apply to the
    /// given base item name, filtered by the base's tags.
    /// </summary>
    public List<AffixEntry> GetItemAffixes(string baseName)
    {
        State["_affixBase"] = baseName;
        var list = new List<AffixEntry>();
        var result = State.DoString(@"
            if not (data and data.itemBases and data.itemMods and data.itemMods.Item) then return {} end
            local base = data.itemBases[_affixBase]
            if not base then return {} end
            -- collect all matching tags for this base
            local tags = {}
            if base.tags then
                for tag, _ in pairs(base.tags) do tags[tag] = true end
            end
            -- also add type-derived tags (e.g. 'helmet', 'body_armour')
            if base.type then
                local t = base.type:lower():gsub(' ','_'):gsub(':.*','')
                tags[t] = true
            end
            local out = {}
            for modId, mod in pairs(data.itemMods.Item) do
                if mod.type and mod[1] then
                    local ok = false
                    for i, wk in ipairs(mod.weightKey or {}) do
                        local wv = mod.weightVal and mod.weightVal[i] or 0
                        if wv > 0 and tags[wk] then ok = true; break end
                    end
                    if ok then
                        table.insert(out, {
                            modId,
                            mod.affix or '',
                            mod[1],
                            mod.type,
                            mod.level or 0,
                            mod.group or ''
                        })
                    end
                end
            end
            -- sort by group then level
            table.sort(out, function(a,b)
                if a[6] ~= b[6] then return a[6] < b[6] end
                return a[5] < b[5]
            end)
            return out
        ");
        State["_affixBase"] = null;
        if (result is { Length: > 0 } && result[0] is LuaTable t)
            foreach (var k in t.Keys)
                if (t[k] is LuaTable row)
                {
                    var id   = row[1L] as string ?? "";
                    var afx  = row[2L] as string ?? "";
                    var stat = row[3L] as string ?? "";
                    var typ  = row[4L] as string ?? "";
                    var lvl  = row[5L] is long li ? (int)li : 0;
                    var grp  = row[6L] as string ?? "";
                    list.Add(new AffixEntry(id, afx, stat, typ, lvl, grp));
                }
        return list;
    }

    /// <summary>Returns all unique items from the loaded unique database.</summary>
    public List<UniqueItemEntry> GetUniqueItems()
    {
        var list = new List<UniqueItemEntry>();
        var result = State.DoString(@"
            if not (main and main.uniqueDB and main.uniqueDB.list) then return {} end
            local out = {}
            for _, item in pairs(main.uniqueDB.list) do
                if item and item.name and item.base then
                    local cat   = (item.base and item.base.type) or ''
                    -- Display title (no base appended) + the full DB key for lookup
                    local title = item.title or item.name
                    local key   = item.name
                    table.insert(out, { title, item.baseName or '', cat, key })
                end
            end
            table.sort(out, function(a,b) return a[1] < b[1] end)
            return out
        ");
        if (result is { Length: > 0 } && result[0] is LuaTable t)
            foreach (var k in t.Keys)
                if (t[k] is LuaTable row)
                {
                    var name  = row[1L] as string ?? "";
                    var base_ = row[2L] as string ?? "";
                    var cat   = row[3L] as string ?? "";
                    var key   = row[4L] as string ?? name;
                    list.Add(new UniqueItemEntry(name, base_, cat, key));
                }
        return list;
    }

    /// <summary>
    /// Returns the raw text of a unique item template from main.uniqueDB
    /// (includes {range:X} prefixes for editable mod rolls).
    /// </summary>
    public string GetUniqueItemRaw(string uniqueName)
    {
        State["_uniqueName"] = uniqueName;
        var result = State.DoString(@"
            if not (main and main.uniqueDB and main.uniqueDB.list) then return '' end
            local item = main.uniqueDB.list[_uniqueName]
            if not item or not item.BuildRaw then return '' end
            return item:BuildRaw() or ''
        ");
        State["_uniqueName"] = null;
        return result is { Length: > 0 } && result[0] is string s ? s : "";
    }

    /// <summary>Returns the PoB raw text representation of an item in the pool.</summary>
    /// <summary>Returns intrinsic base defence/utility values for the given base item.</summary>
    public BaseDefaults GetBaseDefaults(string baseName)
    {
        State["_bdName"] = baseName;
        var result = State.DoString(@"
            if not (data and data.itemBases) then return nil end
            local b = data.itemBases[_bdName]
            if not b then return nil end
            local a = (b.armour and b.armour.Armour)       or 0
            local e = (b.armour and b.armour.Evasion)      or 0
            local s = (b.armour and b.armour.EnergyShield) or 0
            local w = (b.armour and b.armour.Ward)         or 0
            local sp = b.spirit     or 0
            local ch = b.charmLimit or 0
            local q  = b.quality    or 0
            return { a, e, s, w, sp, ch, q }
        ");
        State["_bdName"] = null;
        if (result is { Length: > 0 } && result[0] is LuaTable t)
        {
            int read(int i) => t[(long)i] is long li ? (int)li : t[(long)i] is double d ? (int)d : 0;
            return new BaseDefaults(read(1), read(2), read(3), read(4), read(5), read(6), read(7));
        }
        return new BaseDefaults(0, 0, 0, 0, 0, 0, 0);
    }

    /// <summary>Returns all runes from data.itemMods.Runes with their per-slot-type mod lines.</summary>
    public List<RuneEntry> GetAllRunes()
    {
        var list = new List<RuneEntry>();
        var result = State.DoString(@"
            if not (data and data.itemMods and data.itemMods.Runes) then return nil end
            local out = {}
            for runeName, slots in pairs(data.itemMods.Runes) do
                local entry = { runeName, {}, {} }   -- {name, slotTypes[], modsByType{slot,mods[]}}
                local types = {}
                for slotType, slotData in pairs(slots) do
                    table.insert(types, slotType)
                    local mods = {}
                    -- numeric array entries on slotData are mod lines
                    for i, mod in ipairs(slotData) do
                        table.insert(mods, mod)
                    end
                    entry[3][slotType] = mods
                end
                entry[2] = types
                table.insert(out, entry)
            end
            return out
        ");
        if (result is null || result.Length == 0 || result[0] is not LuaTable tbl)
            return list;
        foreach (var k in tbl.Keys)
        {
            if (tbl[k] is not LuaTable e) continue;
            var name = e[1] as string ?? "";
            if (string.IsNullOrEmpty(name)) continue;
            var slotTypes = new List<string>();
            if (e[2] is LuaTable t2)
                foreach (var sk in t2.Keys) if (t2[sk] is string s) slotTypes.Add(s);
            var modsByType = new Dictionary<string, IReadOnlyList<string>>();
            if (e[3] is LuaTable t3)
                foreach (var sk in t3.Keys)
                {
                    if (sk is not string slot) continue;
                    var mods = new List<string>();
                    if (t3[sk] is LuaTable mt)
                        foreach (var mk in mt.Keys) if (mt[mk] is string ms) mods.Add(ms);
                    modsByType[slot] = mods;
                }
            list.Add(new RuneEntry(name, slotTypes, modsByType));
        }
        list.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
        return list;
    }

    public string GetItemRawText(int itemId)
    {
        State["_rawItemId"] = (long)itemId;
        var result = State.DoString(@"
            if not (build and build.itemsTab) then return '' end
            local item = build.itemsTab.items[_rawItemId]
            if not item then return '' end
            return item:BuildRaw() or ''
        ");
        State["_rawItemId"] = null;
        return result is { Length: > 0 } ? (result[0] as string ?? "") : "";
    }

    /// <summary>
    /// Replaces an existing pool item's data with the parsed result of the given raw text.
    /// The item keeps its ID and slot assignment.
    /// </summary>
    public bool UpdateItemFromText(int itemId, string rawText)
    {
        State["_updId"]  = (long)itemId;
        State["_updRaw"] = rawText;
        var result = State.DoString(@"
            if not (build and build.itemsTab) then return false end
            local existing = build.itemsTab.items[_updId]
            if not existing then return false end
            local function stage(name, fn)
                local _ok, _err = pcall(fn)
                if not _ok then error('['..name..'] '..tostring(_err)) end
            end
            local ok, err = pcall(function()
                local newItem
                stage('parse', function() newItem = new('Item', sanitiseText(_updRaw)) end)
                if not newItem then error('new returned nil') end
                if not newItem.base then error('no .base on parsed item ('..tostring(newItem.baseName)..')') end
                -- Build the new item's own mod list first (validates the parsed state).
                stage('newItem.BuildModList', function() newItem:BuildModList() end)
                -- Replace the existing item in the items table outright, preserving id.
                stage('install', function()
                    newItem.id = existing.id
                    build.itemsTab.items[existing.id] = newItem
                end)
                stage('PopulateSlots', function() build.itemsTab:PopulateSlots() end)
                stage('AddUndoState', function() build.itemsTab:AddUndoState() end)
                build.buildFlag = true
            end)
            if not ok then
                if ConPrintf then ConPrintf('UpdateItemFromText error: ' .. tostring(err)) end
                return false
            end
            return true
        ");
        State["_updId"]  = null;
        State["_updRaw"] = null;
        TriggerRecalc();
        return result is { Length: > 0 } && result[0] is bool b && b;
    }

    private static IReadOnlyList<string> ToStringList(object? obj)
    {
        var list = new List<string>();
        if (obj is LuaTable t)
            foreach (var k in t.Keys)
                if (t[k] is string s) list.Add(s);
        return list;
    }

    // ── Tree: classes ─────────────────────────────────────────────────────────

    public List<ClassEntry> GetClassData()
    {
        var classes = new List<ClassEntry>();
        var result = State.DoString(@"
            if not (build and build.spec and build.spec.tree) then return nil end
            local out = {}
            for classId, cls in ipairs(build.spec.tree.classes) do
                local ascends = {{ 0, 'None' }}
                for ascId, asc in ipairs(cls.ascendancies or {}) do
                    table.insert(ascends, { ascId, asc.name or ('Ascend '..ascId) })
                end
                table.insert(out, { classId, cls.name or ('Class '..classId), ascends })
            end
            return out
        ");
        if (result is not { Length: > 0 } || result[0] is not LuaTable tbl) return classes;

        foreach (var k in tbl.Keys)
        {
            if (tbl[k] is not LuaTable row) continue;
            var classId   = row[1L] is long ci ? (int)ci : 0;
            var className = row[2L] as string ?? "";
            var ascends   = new List<AscendEntry>();
            if (row[3L] is LuaTable ascTbl)
                foreach (var ak in ascTbl.Keys)
                    if (ascTbl[ak] is LuaTable aRow)
                    {
                        var aid  = aRow[1L] is long al ? (int)al : 0;
                        var name = aRow[2L] as string ?? "";
                        ascends.Add(new AscendEntry(aid, name));
                    }
            classes.Add(new ClassEntry(classId, className, ascends));
        }
        return classes;
    }

    public (int ClassId, int AscendClassId, string ClassName, string AscendName) GetCurrentClassInfo()
    {
        var result = State.DoString(@"
            if not (build and build.spec) then return 1, 0, '', '' end
            local s = build.spec
            return s.curClassId or 1,
                   s.curAscendClassId or 0,
                   s.curClassName or '',
                   s.curAscendClassName or 'None'
        ");
        if (result is not { Length: >= 4 }) return (1, 0, "", "None");
        var classId  = result[0] is long ci ? (int)ci : 1;
        var ascId    = result[1] is long ai ? (int)ai : 0;
        var cls      = result[2] as string ?? "";
        var asc      = result[3] as string ?? "None";
        return (classId, ascId, cls, asc);
    }

    public void SelectClass(int classId, int ascendClassId = 0)
    {
        State["_selClassId"]  = (long)classId;
        State["_selAscendId"] = (long)ascendClassId;
        State.DoString(@"
            if build and build.spec then
                build.spec:SelectClass(_selClassId)
                if _selAscendId > 0 then
                    build.spec:SelectAscendClass(_selAscendId)
                end
                build.buildFlag = true
                runCallback('OnFrame')
                if build.calcsTab then build.calcsTab:BuildOutput() end
            end
        ");
        State["_selClassId"]  = null;
        State["_selAscendId"] = null;
    }

    /// <summary>Clears all user-allocated nodes from the spec (start nodes will be re-allocated by SelectClass).</summary>
    public void ResetAllocatedNodes()
    {
        State.DoString(@"
            if build and build.spec then
                local allocNodes = build.spec.allocNodes
                for id, node in pairs(allocNodes) do
                    node.alloc = false
                end
                for k in pairs(allocNodes) do allocNodes[k] = nil end
            end
        ");
    }

    /// <summary>Returns background center world-positions keyed by ascendancy id (e.g. "Oracle").</summary>
    public Dictionary<string, (double X, double Y)> GetAscendancyBackgrounds()
    {
        var result = new Dictionary<string, (double, double)>(StringComparer.OrdinalIgnoreCase);
        var raw = State.DoString(@"
            if not (build and build.spec and build.spec.tree) then return nil end
            local tree = build.spec.tree
            local out = {}
            for _, cls in pairs(tree.classes or {}) do
                for _, ascend in ipairs(cls.classes or {}) do
                    if ascend.id and ascend.background then
                        out[ascend.id] = { ascend.background.x or 0, ascend.background.y or 0 }
                    end
                end
            end
            return out
        ");
        if (raw is not { Length: >= 1 } || raw[0] is not LuaTable tbl) return result;
        foreach (var k in tbl.Keys)
        {
            if (k is not string name || tbl[k] is not LuaTable row) continue;
            var x = row[1L] is double dx ? dx : row[1L] is long lx ? (double)lx : 0.0;
            var y = row[2L] is double dy ? dy : row[2L] is long ly ? (double)ly : 0.0;
            result[name] = (x, y);
        }
        return result;
    }

    // ── Tree: node allocation ──────────────────────────────────────────────────

    public int ToggleNode(int nodeId)
    {
        State["_nodeId"] = (long)nodeId;
        var result = State.DoString(@"
            if not (build and build.spec) then return 0 end
            local node = build.spec.nodes[_nodeId]
            if not node then return 0 end
            if node.alloc then
                -- Type ClassStart cannot be deallocated
                if node.type == 'ClassStart' or node.type == 'AscendClassStart' then return 0 end
                build.spec:DeallocNode(node)
                build.buildFlag = true
                runCallback('OnFrame')
                if build.calcsTab then build.calcsTab:BuildOutput() end
                return -1
            else
                if not node.path or #node.path == 0 then return 0 end
                build.spec:AllocNode(node)
                build.buildFlag = true
                runCallback('OnFrame')
                if build.calcsTab then build.calcsTab:BuildOutput() end
                return 1
            end
        ");
        State["_nodeId"] = null;
        return result is { Length: > 0 } && result[0] is long r ? (int)r : 0;
    }

    /// <summary>Allocate a non-attribute node. Returns 1 on success, 0 on failure.</summary>
    public int AllocNode(int nodeId)
    {
        State["_nodeId"] = (long)nodeId;
        var result = State.DoString(@"
            if not (build and build.spec) then return 0 end
            local node = build.spec.nodes[_nodeId]
            if not node then return 0 end
            if node.alloc then return 0 end
            if not node.path or #node.path == 0 then return 0 end
            build.spec:AllocNode(node)
            build.buildFlag = true
            runCallback('OnFrame')
            if build.calcsTab then build.calcsTab:BuildOutput() end
            return 1
        ");
        State["_nodeId"] = null;
        return result is { Length: > 0 } && result[0] is long r ? (int)r : 0;
    }

    /// <summary>Deallocate a node. Returns -1 on success, 0 on failure.</summary>
    public int DeallocNode(int nodeId)
    {
        State["_nodeId"] = (long)nodeId;
        var result = State.DoString(@"
            if not (build and build.spec) then return 0 end
            local node = build.spec.nodes[_nodeId]
            if not node then return 0 end
            if not node.alloc then return 0 end
            if node.type == 'ClassStart' or node.type == 'AscendClassStart' then return 0 end
            build.spec:DeallocNode(node)
            build.buildFlag = true
            runCallback('OnFrame')
            if build.calcsTab then build.calcsTab:BuildOutput() end
            return -1
        ");
        State["_nodeId"] = null;
        return result is { Length: > 0 } && result[0] is long r ? (int)r : 0;
    }

    /// <summary>
    /// Change the attribute of an already-allocated attribute node without deallocating.
    /// Returns 1 on success, 0 on failure.
    /// </summary>
    public int ChangeAttributeNode(int nodeId, int attributeIndex)
    {
        State["_nodeId"]  = (long)nodeId;
        State["_attrIdx"] = (long)attributeIndex;
        var result = State.DoString(@"
            if not (build and build.spec) then return 0 end
            local node = build.spec.nodes[_nodeId]
            if not node then return 0 end
            if not node.alloc then return 0 end
            -- Update hashOverrides with chosen attribute
            build.spec:SwitchAttributeNode(_nodeId, _attrIdx)
            -- Propagate override into the live spec node
            local hashNode = build.spec.hashOverrides[_nodeId]
            if hashNode then
                build.spec:ReplaceNode(node, hashNode)
                build.spec.tree:ProcessStats(node)
            end
            build.spec.attributeIndex = _attrIdx
            build.buildFlag = true
            runCallback('OnFrame')
            if build.calcsTab then build.calcsTab:BuildOutput() end
            return 1
        ");
        State["_nodeId"]  = null;
        State["_attrIdx"] = null;
        return result is { Length: > 0 } && result[0] is long r ? (int)r : 0;
    }

    /// <summary>
    /// Switch an attribute node to the chosen stat (1=Strength, 2=Dexterity, 3=Intelligence)
    /// and allocate it. Returns 1 on success, 0 on failure.
    /// </summary>
    public int AllocAttributeNode(int nodeId, int attributeIndex)
    {
        State["_nodeId"]   = (long)nodeId;
        State["_attrIdx"]  = (long)attributeIndex;
        var result = State.DoString(@"
            if not (build and build.spec) then return 0 end
            local node = build.spec.nodes[_nodeId]
            if not node then return 0 end
            if node.alloc then return 0 end
            if not node.path or #node.path == 0 then return 0 end
            -- AllocNode calls SwitchAttributeNode internally for each isAttribute path node,
            -- using spec.attributeIndex. Set it BEFORE AllocNode so our choice is used.
            build.spec.attributeIndex = _attrIdx
            build.spec:AllocNode(node)
            build.buildFlag = true
            runCallback('OnFrame')
            if build.calcsTab then build.calcsTab:BuildOutput() end
            return 1
        ");
        State["_nodeId"]  = null;
        State["_attrIdx"] = null;
        return result is { Length: > 0 } && result[0] is long r ? (int)r : 0;
    }

    public HashSet<int> GetAllocatedNodeIds()
    {
        var set = new HashSet<int>();
        var result = State.DoString(@"
            if not (build and build.spec) then return nil end
            local alloc = {}
            for id, _ in pairs(build.spec.allocNodes or {}) do
                alloc[#alloc+1] = id
            end
            return alloc
        ");
        if (result is { Length: > 0 } && result[0] is LuaTable tbl)
            foreach (var k in tbl.Keys)
                if (tbl[k] is long al) set.Add((int)al);
        return set;
    }

    // ── Tree tab ───────────────────────────────────────────────────────────────

    public (List<TreeNodeDto> Nodes, HashSet<int> AllocatedIds) GetTreeData()
    {
        var nodes     = new List<TreeNodeDto>();
        var allocated = new HashSet<int>();

        var result = State.DoString(@"
            if not (build and build.spec and build.spec.tree) then return nil, nil end
            local tree       = build.spec.tree
            local specNodes  = build.spec.nodes   -- class-specific (ReplaceNode applied)
            local allocNodes = build.spec.allocNodes or {}

            -- Collect nodes
            local out = {}
            for nodeId, node in pairs(specNodes) do
                local t = node.type
                if t and t ~= 'OnlyImage' then
                    local id = node.id or nodeId

                    local stats = {}
                    for _, s in ipairs(node.sd or {}) do
                        if type(s) == 'string' then stats[#stats+1] = s end
                    end

                    local linked = {}
                    for _, lid in ipairs(node.linkedId or {}) do
                        linked[#linked+1] = tostring(lid)
                    end

                    -- Per-node overlay overrides global; fallback to global by type
                    local globalOv = tree.nodeOverlay and tree.nodeOverlay[t] or {}
                    local ov = node.nodeOverlay or globalOv
                    -- Socket nodes: world-space jewel radius (outer * 1.2 multiplier)
                    local jewelRadius = 0
                    if t == 'Socket' then jewelRadius = 1560 end  -- Large radius (1300*1.2)
                    table.insert(out, {
                        id,
                        node.x or 0,
                        node.y or 0,
                        t,
                        node.dn or '',
                        table.concat(stats, '\n'),
                        table.concat(linked, ','),
                        node.ascendancyName or '',
                        node.icon or '',
                        ov.unalloc or '',
                        ov.alloc or '',
                        ov.path or '',
                        jewelRadius,
                        node.isAttribute and 1 or 0
                    })
                end
            end

            -- Collect allocated IDs
            local alloc = {}
            for id, _ in pairs(allocNodes) do
                alloc[#alloc+1] = id
            end

            return out, alloc
        ");

        if (result is not { Length: >= 2 }) return (nodes, allocated);

        // Parse nodes
        if (result[0] is LuaTable nodeTbl)
        {
            foreach (var k in nodeTbl.Keys)
            {
                if (nodeTbl[k] is not LuaTable row) continue;

                var id   = row[1L] is long li  ? (int)li  : 0;
                var x    = row[2L] is double dx ? dx : row[2L] is long lx ? (double)lx : 0.0;
                var y    = row[3L] is double dy ? dy : row[3L] is long ly ? (double)ly : 0.0;
                var type = row[4L] as string ?? "Normal";
                var name = row[5L] as string ?? "";
                var statsRaw  = row[6L] as string ?? "";
                var linkedRaw = row[7L] as string ?? "";
                var asc            = row[8L]  as string ?? "";
                var icon           = row[9L]  as string ?? "";
                var overlayUnalloc = row[10L] as string ?? "";
                var overlayAlloc   = row[11L] as string ?? "";
                var overlayPath    = row[12L] as string ?? "";
                var jewelRadius    = row[13L] is double jr ? jr : row[13L] is long jl ? (double)jl : 0.0;
                var isAttribute    = row[14L] is long ia && ia != 0;

                var stats     = statsRaw  == "" ? [] : statsRaw.Split('\n');
                var linkedIds = linkedRaw == "" ? []
                    : linkedRaw.Split(',')
                                .Select(s => int.TryParse(s, out var n) ? n : 0)
                                .Where(n => n != 0)
                                .ToArray();

                nodes.Add(new TreeNodeDto(id, x, y, type, name, stats, linkedIds, asc,
                                          icon, overlayUnalloc, overlayAlloc, overlayPath, jewelRadius, isAttribute));
            }
        }

        // Parse allocated IDs
        if (result[1] is LuaTable allocTbl)
            foreach (var k in allocTbl.Keys)
                if (allocTbl[k] is long al) allocated.Add((int)al);

        return (nodes, allocated);
    }

    /// <summary>
    /// Returns (nodeId, worldRadius) pairs for nodes that currently emit a
    /// "can allocate without connection" radius (e.g. Keystones when Entwined
    /// Realities is allocated via the Oracle ascendancy).
    /// </summary>
    public List<(int NodeId, double RadiusWorld)> GetRadiusEmitters()
    {
        var result = new List<(int, double)>();

        var raw = State.DoString(@"
            if not (build and build.spec) then return nil end
            local spec = build.spec
            local leaps = spec.intuitiveLeapLikeNodes
            if not leaps or #leaps == 0 then return nil end

            local emitters = {}
            for _, leap in ipairs(leaps) do
                if leap.from == 'Keystone' and leap.radiusIndex then
                    local radData = data and data.jewelRadius and data.jewelRadius[leap.radiusIndex]
                    if radData then
                        -- PassiveTreeJewelDistanceMultiplier = 1.2
                        local outerWorld = radData.outer * 1.2
                        for _, keyNode in pairs(spec.tree.keystoneMap or {}) do
                            if spec.allocNodes[keyNode.id] then
                                table.insert(emitters, { keyNode.id, outerWorld })
                            end
                        end
                    end
                end
            end
            return emitters
        ");

        if (raw is not { Length: >= 1 } || raw[0] is not LuaTable tbl)
            return result;

        foreach (var k in tbl.Keys)
        {
            if (tbl[k] is not LuaTable row) continue;
            var nodeId = row[1L] is long li ? (int)li : 0;
            var radius = row[2L] is double dr ? dr : row[2L] is long lr ? (double)lr : 0.0;
            if (nodeId != 0 && radius > 0) result.Add((nodeId, radius));
        }

        return result;
    }

    public void Dispose() => State.Dispose();
}
