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
function round(val, dec)
    if dec then
        return math.floor(val * 10 ^ dec + 0.5) / 10 ^ dec
    else
        return math.floor(val + 0.5)
    end
end

-- copyTable: src/Modules/Common.lua. mods.lua uses it to dup statEntry.stats.
function copyTable(tbl, noRecurse)
    local out = {}
    for k, v in pairs(tbl) do
        if not noRecurse and type(v) == "table" then out[k] = copyTable(v) else out[k] = v end
    end
    return out
end

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

-- isValueInArray / isValueInTable: src/Modules/Common.lua.
function isValueInTable(tbl, val)
    for k, v in pairs(tbl) do if val == v then return k end end
end
function isValueInArray(tbl, val)
    for i, v in ipairs(tbl) do if val == v then return i end end
end

-- processTemplateFile: src/Export/Main.lua. Reads a template .txt file,
-- dispatches #-directives via directiveTable, writes non-directive lines
-- to outDir/name.lua. bases.lua uses this to expand 30 base templates.
function processTemplateFile(name, inDir, outDir, directiveTable)
    local state = {}
    local out = io.open(outDir .. name .. ".lua", "w")
    if not out then
        error("processTemplateFile: cannot open " .. outDir .. name .. ".lua for writing")
    end
    out:write("-- This file is automatically generated, do not edit!\n")
    for line in io.lines(inDir .. name .. ".txt") do
        local spec, args = line:match("#(%a+) ?(.*)")
        if spec then
            if directiveTable[spec] then
                directiveTable[spec](state, args, out)
            else
                printf("Unknown directive '%s'", spec)
            end
        else
            if line:match("^%-%-") or line:match("^local") or line == "" then
                out:write(line, "\n")
            elseif line:match("^legacy") then
                out:write("\t" .. line, ",\n")
            else
                out:write("\t\t\t" .. line, "\n")
            end
        end
    end
    out:close()
end

-- LoadModule(name, ...) — PoB's module loader. Just dofile-equivalent that
-- runs the file with the given args and returns its result.
function LoadModule(fileName, ...)
    if not fileName:match("%.lua$") then fileName = fileName .. ".lua" end
    local func, err = loadfile(fileName)
    if not func then
        error("LoadModule() error loading '" .. fileName .. "': " .. tostring(err))
    end
    return func(...)
end

-- Bit-op aliases + byte helpers mods.lua needs through murmurHash2 / intToBytes.
local b_and    = bit.band
local b_xor    = bit.bxor
local b_rshift = bit.rshift

function bytesToInt(b, o)
    o = o or 1
    local n = (b:byte(o + 0) or 0)
           + (b:byte(o + 1) or 0) * 256
           + (b:byte(o + 2) or 0) * 65536
           + (b:byte(o + 3) or 0) * 16777216
    return bit.tobit(n)
end

function intToBytes(int)
    return string.char(
        b_and(int, 0xFF),
        b_and(b_rshift(int, 8), 0xFF),
        b_and(b_rshift(int, 16), 0xFF),
        b_and(b_rshift(int, 24), 0xFF)
    )
end

do
    local function toUnsigned(val) return val < 0 and val + 0x100000000 or val end
    local function murmurMix(val)
        val = toUnsigned(val)
        return bit.tobit(val * 0xE995 + b_and(val * 0x5BD1, 0xFFFF) * 0x10000)
    end
    function murmurHash2(key, seed)
        local len = #key
        local h = b_xor(seed or 0, len)
        local o = 1
        while len >= 4 do
            local k = bytesToInt(key, o)
            k = murmurMix(k)
            k = b_xor(k, b_rshift(k, 24))
            k = murmurMix(k)
            h = murmurMix(h)
            h = b_xor(h, k)
            o = o + 4
            len = len - 4
        end
        if len > 0 then
            h = b_xor(h, bytesToInt(key, o))
            h = murmurMix(h)
        end
        h = b_xor(h, b_rshift(h, 13))
        h = murmurMix(h)
        h = b_xor(h, b_rshift(h, 15))
        return toUnsigned(h)
    end
end

-- HashStats: src/Modules/Common.lua. Since upstream 0.23.x mods.lua computes the
-- trade hash through it, the headless runner has to provide it alongside the
-- murmur helpers above.
do
    local GGG_STAT_HASH32_SEED = 0xC58F1A7B
    local GGG_TRADE_SEED = 0x02312233
    function HashStats(stats, extraStat)
        if extraStat then
            stats = copyTable(stats)
            table.insert(stats, extraStat)
        end
        local statHashes = ""
        for _, statName in ipairs(stats) do
            statHashes = statHashes .. intToBytes(murmurHash2(statName, GGG_STAT_HASH32_SEED))
        end
        return murmurHash2(statHashes, GGG_TRADE_SEED)
    end
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

-- Defensive wrappers around statdesc functions. Some PoB scripts (e.g.
-- mods.lua) assume describeMod returns out.modTags as a string, but under
-- our shim it can be nil for mods with empty/unresolved ImplicitTags.
do
    local _describeMod = describeMod
    function describeMod(mod)
        local out, orders, missing = _describeMod(mod)
        if out then out.modTags = out.modTags or "" end
        return out, orders, missing
    end
end

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
