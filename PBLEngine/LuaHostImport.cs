using System;

namespace PBLEngine;

/// <summary>Что и как импортировать из персонажа — соответствует чекбоксам
/// ImportTab (src/Classes/ImportTab.lua:118-129).</summary>
public sealed record CharacterImportOptions
{
    /// <summary>Дерево пассивных умений, специализации связок оружия и самоцветы.</summary>
    public bool PassiveTree { get; init; } = true;
    /// <summary>Снаряжение и умения (гемы в сокетах).</summary>
    public bool ItemsAndSkills { get; init; } = true;

    /// <summary>Удалить существующие самоцветы перед импортом дерева.</summary>
    public bool ClearJewels { get; init; } = true;
    /// <summary>Удалить надетое снаряжение перед импортом предметов.</summary>
    public bool ClearItems { get; init; } = true;
    /// <summary>Удалить существующие группы умений перед импортом.</summary>
    public bool ClearSkills { get; init; } = true;
    /// <summary>Не импортировать предметы и умения второй связки оружия.</summary>
    public bool IgnoreWeaponSwap { get; init; }
}

/// <summary>Итог импорта: <c>Ok</c> — всё прошло, иначе <c>Error</c> — текст ошибки Lua.</summary>
public sealed record CharacterImportResult(bool Ok, string? Error = null);

// Импорт персонажа: разбор JSON и всю логику делает штатный Lua-ImportTab
// (тот же код, что в оригинальном PoB) — здесь только мост и флаги-чекбоксы.
public sealed partial class LuaHost
{
    /// <summary>Читаемое имя класса по тому, что отдаёт API персонажей: там лежит
    /// ВНУТРЕННИЙ id восхождения ("Witch2"), который дерево маппит в имя ("Инфернальная")
    /// — тот же перевод делает ImportTab:DownloadCharacterList (ImportTab.lua:499).
    /// Неизвестное значение возвращается как есть.</summary>
    public string ResolveCharacterClassName(string apiClass)
    {
        if (string.IsNullOrEmpty(apiClass)) return apiClass;
        State["_clsIn"] = apiClass;
        try
        {
            var result = State.DoString(@"
                local tree = build and build.latestTree
                local entry = tree and tree.internalAscendNameMap and tree.internalAscendNameMap[_clsIn]
                return entry and entry.ascendClass and entry.ascendClass.name or _clsIn
            ");
            return result is { Length: > 0 } && result[0] is string s && s.Length > 0 ? s : apiClass;
        }
        catch
        {
            return apiClass;
        }
        finally { State["_clsIn"] = null; }
    }

    /// <summary>Импортирует персонажа в текущий билд из ответа
    /// <c>GET api.pathofexile.com/character/poe2/&lt;имя&gt;</c> (сырой JSON,
    /// с обёрткой <c>{"character": …}</c> или без неё).</summary>
    public CharacterImportResult ImportCharacter(string characterJson, CharacterImportOptions? options = null)
    {
        var opts = options ?? new CharacterImportOptions();
        if (!opts.PassiveTree && !opts.ItemsAndSkills)
            return new CharacterImportResult(true);

        State["_impJson"] = characterJson;
        State["_impTree"] = opts.PassiveTree;
        State["_impItems"] = opts.ItemsAndSkills;
        State["_impClearJewels"] = opts.ClearJewels;
        State["_impClearItems"] = opts.ClearItems;
        State["_impClearSkills"] = opts.ClearSkills;
        State["_impIgnoreSwap"] = opts.IgnoreWeaponSwap;
        try
        {
            var result = State.DoString(@"
                if not (build and build.importTab) then return 'билд не загружен' end
                local dkjson = require 'dkjson'
                local data, _pos, errDecode = dkjson.decode(_impJson)
                if errDecode or type(data) ~= 'table' then
                    return 'не удалось разобрать ответ API: ' .. tostring(errDecode)
                end
                -- Ответ API обёрнут в { character = {...} }; принимаем и голого персонажа.
                local charData = data.character or data
                if type(charData) ~= 'table' or not charData.name then
                    return 'в ответе API нет персонажа'
                end

                local importTab = build.importTab
                importTab.controls.charImportTreeClearJewels.state = _impClearJewels
                importTab.controls.charImportItemsClearItems.state = _impClearItems
                importTab.controls.charImportItemsClearSkills.state = _impClearSkills
                importTab.controls.charImportItemsIgnoreWeaponSwap.state = _impIgnoreSwap

                if _impTree then
                    if type(charData.passives) ~= 'table' then
                        return 'в ответе API нет дерева пассивных умений'
                    end
                    -- ImportPassiveTreeAndJewels ходит по этим таблицам через pairs/ipairs:
                    -- отсутствующая (у совсем свежего персонажа) уронила бы импорт.
                    charData.jewels = charData.jewels or {}
                    charData.passives.hashes = charData.passives.hashes or {}
                    charData.passives.specialisations = charData.passives.specialisations or {}
                    charData.passives.skill_overrides = charData.passives.skill_overrides or {}
                    charData.passives.quest_stats = charData.passives.quest_stats or {}
                    charData.passives.jewel_data = charData.passives.jewel_data or {}
                    local ok, err = pcall(function() importTab:ImportPassiveTreeAndJewels(charData) end)
                    if not ok then return 'дерево: ' .. tostring(err) end
                    build.calcsTab:BuildOutput()
                end

                if _impItems then
                    charData.equipment = charData.equipment or {}
                    charData.skills = charData.skills or {}
                    local ok, err = pcall(function() importTab:ImportItemsAndSkills(charData) end)
                    if not ok then return 'предметы и умения: ' .. tostring(err) end
                end

                build.buildFlag = true
                return nil
            ");
            var error = result is { Length: > 0 } ? result[0] as string : null;
            if (error is { Length: > 0 })
                return new CharacterImportResult(false, error);
        }
        catch (Exception ex)
        {
            return new CharacterImportResult(false, ex.Message);
        }
        finally
        {
            State["_impJson"] = null;
            State["_impTree"] = null;
            State["_impItems"] = null;
            State["_impClearJewels"] = null;
            State["_impClearItems"] = null;
            State["_impClearSkills"] = null;
            State["_impIgnoreSwap"] = null;
        }

        TriggerRecalc();
        SyncCalcsSkill();
        return new CharacterImportResult(true);
    }
}
