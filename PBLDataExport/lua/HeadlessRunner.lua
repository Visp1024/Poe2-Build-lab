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
