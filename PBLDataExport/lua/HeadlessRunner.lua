-- HeadlessRunner.lua
-- Headless Dat View replacement. Loads pathofexile-dat JSON dumps via
-- JsonDatFile.lua, stubs the SimpleGraphic / GUI globals that
-- src/Export/Scripts/*.lua expect from Dat View's Main.lua, then runs
-- a list of scripts.
--
-- Invoked from PBLDataExport (C# Program.cs) after package.path is set
-- and CWD has been changed to src/Export/. Global `_pblExport` is injected
-- from C# with config (dump root, table list, scripts to run, output paths).

local _pblExport = _pblExport
assert(_pblExport, "_pblExport must be set by host")

-- ---- 1. Compat: many Scripts call print/printf -----------------------------

local _print = print
function print(...)
    _print(...)
end
function printf(...)
    print(string.format(...))
end
function ConPrintf(...)
    print(string.format(...))
end

-- ---- 2. Common.lua helpers used by some Scripts ----------------------------

-- newClass: PoB's lightweight class system (src/Modules/Common.lua).
-- Not all scripts use it, but cheap to provide.
function newClass(name, base, init)
    if not init then
        init = base
        base = nil
    end
    local cls = setmetatable({}, { __index = base })
    cls.__index = cls
    return cls, init
end

-- Some Scripts call wipeTable.
function wipeTable(t)
    for k in pairs(t) do t[k] = nil end
end

-- sanitiseText: src/Modules/Common.lua strips color codes + trims whitespace.
-- flavourText.lua uses it on Words.Text2.
function sanitiseText(s)
    if type(s) ~= "string" then return tostring(s) end
    s = s:gsub("%^[xX][%da-fA-F]+", "")  -- ^xAABBCC
    s = s:gsub("%^%d", "")               -- ^7 etc
    s = s:match("^%s*(.-)%s*$") or s
    return s
end

t_insert = table.insert

-- pairsSortByKey: src/Modules/Common.lua. Stable iteration order for table dumps.
function pairsSortByKey(t, f)
    local sortedKeys = {}
    for key in pairs(t) do t_insert(sortedKeys, key) end
    table.sort(sortedKeys, f)
    local i = 0
    return function()
        i = i + 1
        if sortedKeys[i] == nil then return nil end
        return sortedKeys[i], t[sortedKeys[i]]
    end
end

-- escapeGGGString: strips PoE rich-text markup (used by statdesc).
function escapeGGGString(text)
    return text
        :gsub("<[^>]+>{([^}]+)}", "%1")
        :gsub("%[([^|%]]+)%]", "%1")
        :gsub("%[[^|]+|([^|]+)%]", "%1")
end

-- codePointToUTF8 / convertUTF16to8: src/Modules/Common.lua. The PoE .csd files
-- are UTF-16LE; PoB reads them as raw bytes and converts here.
function codePointToUTF8(cp)
    if cp < 0x80 then
        return string.char(cp)
    elseif cp < 0x800 then
        return string.char(0xC0 + math.floor(cp / 0x40), 0x80 + cp % 0x40)
    elseif cp < 0x10000 then
        return string.char(0xE0 + math.floor(cp / 0x1000),
                           0x80 + math.floor(cp / 0x40) % 0x40,
                           0x80 + cp % 0x40)
    else
        return string.char(0xF0 + math.floor(cp / 0x40000),
                           0x80 + math.floor(cp / 0x1000) % 0x40,
                           0x80 + math.floor(cp / 0x40) % 0x40,
                           0x80 + cp % 0x40)
    end
end

function convertUTF16to8(text, offset)
    if not text then return "" end
    offset = offset or 1
    -- Strip UTF-16 BOM if present (FF FE) — pathofexile-dat preserves it.
    if #text >= 2 and text:byte(1) == 0xFF and text:byte(2) == 0xFE then
        offset = 3
    end
    local out = {}
    local highSurr
    for i = offset, #text - 1, 2 do
        local codeUnit = text:byte(i) + text:byte(i + 1) * 256
        if codeUnit == 0 then break
        elseif codeUnit >= 0xD800 and codeUnit <= 0xDBFF then
            highSurr = codeUnit - 0xD800
        elseif codeUnit >= 0xDC00 and codeUnit <= 0xDFFF then
            if highSurr then
                t_insert(out, codePointToUTF8(highSurr * 1024 + codeUnit - 0xDC00 + 0x010000))
                highSurr = nil
            end
        else
            t_insert(out, codePointToUTF8(codeUnit))
        end
    end
    return table.concat(out)
end

-- getFile(path) -> raw binary string of a Bundles2 asset extracted by
-- pathofexile-dat into PBLExport/ggpk_export/files/. pathofexile-dat encodes
-- the file path by replacing '/' with '@', so "Data/StatDescriptions/foo.csd"
-- lives at "files/Data@StatDescriptions@foo.csd".
local _fileCache = {}
function getFile(path)
    if _fileCache[path] then return _fileCache[path] end
    local diskName = path:gsub("/", "@")
    local fullPath = _pblExport.filesRoot .. "/" .. diskName
    local f = io.open(fullPath, "rb")
    if not f then
        print("getFile MISS: " .. path .. "  (expected at " .. fullPath .. ")")
        return nil
    end
    local content = f:read("*all")
    f:close()
    _fileCache[path] = content
    return content
end

-- ---- 3. Dat shim -----------------------------------------------------------

local JsonDat = require("JsonDatFile")

-- Load every table listed in _pblExport.tables.
-- Each entry: { name = "CostTypes", path = "<root>/English/CostTypes.json", refs = { col = "Stats", ... } }
for _, tableInfo in ipairs(_pblExport.tables) do
    JsonDat.load(tableInfo.name, tableInfo.path)
    if tableInfo.refs then
        for col, refTo in pairs(tableInfo.refs) do
            JsonDat.declareRef(tableInfo.name, col, refTo)
        end
    end
end
JsonDat.resolveRefs()

-- Column-mapping layer: bridges pathofexile-dat-schema to PoB spec.lua.
local mappings = require("ColumnMappings")
JsonDat.applyMappings(mappings)

-- statdesc library: src/Export/statdesc.lua expects globals getFile/
-- convertUTF16to8/dat which we just provided. Loading it makes
-- loadStatFile / describeStats / describeScalability available to Scripts.
-- CWD is src/Export/, so the file is right next door.
dofile("statdesc.lua")

-- ---- 4. Drive scripts ------------------------------------------------------

local successCount, failCount = 0, 0
for _, scriptName in ipairs(_pblExport.scripts) do
    print("=== Running " .. scriptName .. " ===")
    local path = "Scripts/" .. scriptName .. ".lua"
    local chunk, loadErr = loadfile(path)
    if not chunk then
        print("LOAD ERROR: " .. tostring(loadErr))
        failCount = failCount + 1
    else
        local ok, runErr = pcall(chunk)
        if ok then
            print("OK: " .. scriptName)
            successCount = successCount + 1
        else
            print("RUN ERROR in " .. scriptName .. ": " .. tostring(runErr))
            failCount = failCount + 1
        end
    end
end

print(string.format("\n=== %d ok, %d failed ===", successCount, failCount))
return failCount
