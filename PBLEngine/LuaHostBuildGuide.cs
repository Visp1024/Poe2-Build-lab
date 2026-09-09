using NLua;
using System.Collections.Generic;

namespace PBLEngine;

/// <summary>Гем в группе умений так, как его ждёт внутриигровой Build Planner:
/// <paramref name="GameId"/> — идентификатор базы предмета
/// (<c>Metadata/Items/Gems/SkillGemSpark</c>), он же ключ <c>src/Data/Gems.lua</c>.</summary>
public sealed record GuideGem(string GameId, string Name, bool IsSupport, bool IsEnabled);

/// <summary>Группа умений билда: активные гемы и всё, что с ними в одном сокете.</summary>
public sealed record GuideSkillGroup(string Label, bool IsEnabled, IReadOnlyList<GuideGem> Gems);

// Сбор данных билда для экспорта гайда в игру (файл .build). Здесь только выемка
// из Lua; сборка самого JSON — в PBLApp.Core/Export/BuildGuideExporter.cs.
public sealed partial class LuaHost
{
    /// <summary>Внутренний id восхождения — то, что игра пишет в поле <c>ascendancy</c>
    /// файла .build («Sorceress1»). Пусто, если восхождение не выбрано.</summary>
    public string GetAscendancyInternalId()
    {
        try
        {
            var result = State.DoString(@"
                local spec = build and build.spec
                local tree = spec and spec.tree
                if not (tree and tree.classes) then return '' end
                -- Тот же путь, которым PassiveSpec отдаёт восхождение наружу
                -- (PassiveSpec.lua:250): class.classes, а не class.ascendancies.
                local class = tree.classes[spec.curClassId]
                local ascend = class and class.classes and class.classes[spec.curAscendClassId]
                return ascend and (ascend.internalId or '') or ''
            ");
            return result is { Length: > 0 } ? result[0] as string ?? "" : "";
        }
        catch
        {
            return "";
        }
    }

    /// <summary>Группы умений билда с идентификаторами баз гемов. Пустые группы
    /// и гемы без <c>gameId</c> (кастомные/битые записи) отбрасываются.</summary>
    public List<GuideSkillGroup> GetGuideSkillGroups()
    {
        var groups = new List<GuideSkillGroup>();
        var result = State.DoString(@"
            if not (build and build.skillsTab) then return {} end
            local function strip(s)
                return s and tostring(s):gsub('%^x%x%x%x%x%x%x?',''):gsub('%^%d','') or ''
            end
            local out = {}
            for _, group in ipairs(build.skillsTab.socketGroupList) do
                local gems = {}
                for _, gem in ipairs(group.gemList or {}) do
                    local data = gem.gemData
                    local gameId = data and data.gameId
                    if gameId then
                        local ge = gem.grantedEffect or (data and data.grantedEffect)
                        local name = strip(gem.nameSpec)
                        if name == '' then name = strip(data.name) end
                        table.insert(gems, {
                            tostring(gameId),
                            name,
                            (ge and ge.support) and 1 or 0,
                            (gem.enabled ~= false) and 1 or 0,
                        })
                    end
                end
                if #gems > 0 then
                    table.insert(out, {
                        strip(group.label or ''),
                        (group.enabled ~= false) and 1 or 0,
                        gems,
                    })
                end
            end
            return out
        ");

        if (result is { Length: > 0 } && result[0] is LuaTable tbl)
        {
            foreach (var key in tbl.Keys)
            {
                if (tbl[key] is not LuaTable row) continue;
                var label = row[1L] as string ?? "";
                var enabled = row[2L] is long en && en == 1L;
                var gems = new List<GuideGem>();
                if (row[3L] is LuaTable gemTbl)
                {
                    foreach (var gemKey in gemTbl.Keys)
                    {
                        if (gemTbl[gemKey] is not LuaTable g) continue;
                        var gameId = g[1L] as string ?? "";
                        if (gameId.Length == 0) continue;
                        gems.Add(new GuideGem(
                            gameId,
                            g[2L] as string ?? "",
                            g[3L] is long su && su == 1L,
                            g[4L] is long ge && ge == 1L));
                    }
                }
                if (gems.Count > 0) groups.Add(new GuideSkillGroup(label, enabled, gems));
            }
        }
        return groups;
    }
}
