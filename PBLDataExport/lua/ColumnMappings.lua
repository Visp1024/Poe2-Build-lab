-- ColumnMappings.lua
-- Bridges pathofexile-dat-schema (current Bundles2) to PoB src/Export/spec.lua
-- (snapshot from a past Dat View pass). Each entry:
--
--   <TableName> = {
--       rename = { <pathofexile-dat-name> = <pob-name>, ... },
--       computed = { <pob-name> = function(row) return <value> end, ... },
--   }
--
-- Renames run first. Computed funcs run with foreign-row refs already
-- resolved (so row.Stat.Id works). Workflow for adding a table:
--
--   1. python scripts/inspect-schema.py <TableName>
--   2. grep -A30 '<tablename>=' src/Export/spec.lua
--   3. Match columns: identical → no entry; renamed → rename map;
--      removed-from-schema → computed func reconstructing the value.

return {
    -- CostTypes: Resource enum was removed entirely (PoB spec.lua had it);
    -- ResourceString renamed to FormatText.
    -- UniqueStashLayout: flavourText.lua accesses row.ItemVisualIdentity,
    -- schema names the column ItemVisualIdentityKey.
    UniqueStashLayout = {
        rename = { ItemVisualIdentityKey = "ItemVisualIdentity" },
    },

    -- ModType: schema only has Name; mods.lua reads it as .Id.
    ModType = {
        rename = { Name = "Id" },
    },

    -- Mods: pathofexile-dat-schema uses suffix-style names; mods.lua uses
    -- legacy PoB names (Type, Family, SpawnTags, GenerationWeightTags, etc.).
    Mods = {
        rename = {
            ModType                  = "Type",
            Families                 = "Family",
            SpawnWeight_Tags         = "SpawnTags",
            GenerationWeight_Tags    = "GenerationWeightTags",
            GenerationWeight_Values  = "GenerationWeightValues",
        },
        computed = {
            -- SpawnWeight values are unnamed in pathofexile-dat-schema and not
            -- exported. mods.lua reads them via mod.SpawnWeight; default to
            -- empty so the script doesn't blow up. (Output diff vs production
            -- will show empty weightVal lists for this run; an upstream
            -- schema fix or a separate computation pass is needed for parity.)
            SpawnWeight = function(row) return {} end,
            -- NodeType: PoE1-only jewel-mod node category. Default to 3 so
            -- mods.lua's `if mod.NodeType ~= 3` check skips the radius-jewel
            -- branch (which is what we want when the column is absent).
            NodeType = function(row) return 3 end,
        },
    },

    CostTypes = {
        rename = { FormatText = "ResourceString" },
        computed = {
            Resource = function(row)
                local stat = row.Stat and rawget(row.Stat, "Id") or ""
                -- Match production src/Data/Costs.lua mapping exactly.
                local resourceByStat = {
                    ["base_mana_cost"]                  = "Mana",
                    ["base_life_cost"]                  = "Life",
                    ["base_es_cost"]                    = "ES",
                    ["base_rage_cost"]                  = "Rage",
                    ["base_ward_cost"]                  = "Ward",
                    ["base_mana_cost_%"]                = "ManaPercent",
                    ["base_life_cost_%"]                = "LifePercent",
                    ["base_es_cost_%"]                  = "ESPercent",
                    ["base_ward_cost_%"]                = "WardPercent",
                    ["base_unreserved_mana_cost_%"]     = "UnreservedManaPercent",
                    ["base_mana_cost_per_minute"]       = "ManaPerMinute",
                    ["base_life_cost_per_minute"]       = "LifePerMinute",
                    ["base_es_cost_per_minute"]         = "ESPerMinute",
                    ["base_rage_cost_per_minute"]       = "RagePerMinute",
                    ["base_ward_cost_per_minute"]       = "WardPerMinute",
                    ["base_mana_cost_%_per_minute"]     = "ManaPercentPerMinute",
                    ["base_life_cost_%_per_minute"]     = "LifePercentPerMinute",
                    ["base_es_cost_%_per_minute"]       = "ESPercentPerMinute",
                }
                return resourceByStat[stat]
            end,
        },
    },
}
