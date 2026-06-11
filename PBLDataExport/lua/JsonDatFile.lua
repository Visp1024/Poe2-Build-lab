-- JsonDatFile.lua
-- Drop-in replacement for src/Export/Classes/Dat64File.lua that reads
-- pathofexile-dat JSON dumps instead of raw .datc64 bytes.
--
-- Mirrors the public interface used by src/Export/Scripts/*.lua:
--   :Rows()         iterator
--   :GetRow(key, value)
--   :GetRowByIndex(i)
--   :GetRowList(key, value, match)
--   :ReadCell(rowIndex, colIndex)
--   :ReadValueText(spec, offset) — only called for leaguenames in Main, safe stub
--
-- Row objects: plain Lua tables augmented with a metatable that auto-resolves
-- Key columns to row objects from the referenced table on first access.

local JsonDat = {}
JsonDat.__index = JsonDat

-- Registry of loaded tables (name lowercase → JsonDat instance).
JsonDat._registry = {}

-- Public: dat("Foo") in Scripts. Lookup is case-insensitive.
function dat(name)
    local key = name:lower()
    local d = JsonDat._registry[key]
    if not d then
        error(name .. ".json not loaded (add to PBLExport/ggpk_export/config.json)")
    end
    return d
end

-- Internal: load a table from disk and register it.
function JsonDat.load(tableName, jsonPath)
    local dkjson = require("dkjson")
    local f = io.open(jsonPath, "r")
    if not f then
        error("Cannot open " .. jsonPath)
    end
    local raw = f:read("*all")
    f:close()
    local data, _, err = dkjson.decode(raw, 1, nil)
    if err then
        error("JSON parse error in " .. jsonPath .. ": " .. err)
    end

    local self = setmetatable({}, JsonDat)
    self.name = tableName:lower()
    self.rowCount = #data
    self.rowsRaw = data  -- list of plain row dicts as parsed by dkjson

    -- rowCache: stable row objects with key auto-resolution.
    local refMeta
    refMeta = {
        __index = function(t, key)
            -- Lazy: if column not present in raw data, return nil.
            -- Already-set keys are answered directly from the table (rawget).
            return rawget(t, key)
        end
    }
    self.rowCache = {}
    for i = 1, self.rowCount do
        local row = data[i]
        row._rowIndex = i
        setmetatable(row, refMeta)
        self.rowCache[i] = row
    end

    JsonDat._registry[self.name] = self
    return self
end

-- After all tables are loaded, walk each row and resolve Key columns.
-- pathofexile-dat emits foreign-row references as plain integers (the row
-- _index in the target table), and lists of references as arrays of integers.
-- We replace them with the actual row objects so `row.Stat.Id` style access
-- in Scripts works unchanged.
--
-- Heuristic: any column whose name appears as the lowercase name of another
-- loaded table is treated as a reference. The schema in config.json drives
-- which columns are keys, but we don't currently parse the schema — instead
-- we mark refs explicitly via JsonDat.declareRef(tableName, columnName, refTable).
JsonDat._refMap = {}  -- [tableName][columnName] = refTableName

function JsonDat.declareRef(tableName, columnName, refTableName)
    local t = JsonDat._refMap[tableName:lower()]
    if not t then
        t = {}
        JsonDat._refMap[tableName:lower()] = t
    end
    t[columnName] = refTableName:lower()
end

function JsonDat.resolveRefs()
    for tableName, refs in pairs(JsonDat._refMap) do
        local d = JsonDat._registry[tableName]
        if d then
            for i = 1, d.rowCount do
                local row = d.rowCache[i]
                for col, refTable in pairs(refs) do
                    local target = JsonDat._registry[refTable]
                    if target then
                        local v = rawget(row, col)
                        if type(v) == "number" then
                            -- pathofexile-dat indices are 0-based; Lua arrays are 1-based.
                            row[col] = target.rowCache[v + 1]
                        elseif type(v) == "table" then
                            -- List of refs
                            local list = {}
                            for j = 1, #v do
                                local idx = v[j]
                                if type(idx) == "number" then
                                    list[j] = target.rowCache[idx + 1]
                                end
                            end
                            row[col] = list
                        end
                    end
                end
            end
        end
    end
end

-- ---- public methods (mirror Dat64FileClass) --------------------------------

function JsonDat:Rows()
    local i = 0
    return function()
        i = i + 1
        if i <= self.rowCount then
            return self.rowCache[i]
        end
    end
end

function JsonDat:GetRow(key, value)
    for i = 1, self.rowCount do
        local row = self.rowCache[i]
        if rawget(row, key) == value then
            return row
        end
    end
end

function JsonDat:GetRowByIndex(i)
    return self.rowCache[i]
end

function JsonDat:GetRowList(key, value, match)
    local out = {}
    for i = 1, self.rowCount do
        local row = self.rowCache[i]
        local v = rawget(row, key)
        if type(v) == "table" then
            for _, item in ipairs(v) do
                if (match and type(item) == "string" and item:match(value)) or item == value then
                    table.insert(out, row)
                    break
                end
            end
        else
            if (match and type(v) == "string" and v:match(value)) or v == value then
                table.insert(out, row)
            end
        end
    end
    return out
end

function JsonDat:ReadCell(rowIndex, colIndex)
    -- Scripts that index by position rather than name are rare; not implemented.
    error("ReadCell(rowIndex, colIndex) is unsupported in JsonDatFile — Scripts should index by column name.")
end

function JsonDat:ReadValueText(spec, offset)
    -- Only Main.lua's leaguenames lookup uses this; we stub league label.
    return "Headless"
end

return JsonDat
