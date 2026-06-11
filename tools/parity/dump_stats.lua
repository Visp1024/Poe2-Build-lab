-- tools/parity/dump_stats.lua
-- Runs inside the original PoB (SimpleGraphic runtime) AFTER main:Init has
-- finished. Loads the build XML pointed to by POB_PARITY_BUILD, triggers the
-- calc pipeline directly via buildMode:Init (skipping the async SetMode loop),
-- and writes the merged mainOutput + calcsOutput as JSON to POB_PARITY_OUT.
--
-- Activated by src/Launch.lua when POB_PARITY_BUILD is set. The PoB window
-- still opens briefly — there's no headless mode in the GUI runtime.

local function fail(msg)
    ConPrintf("[parity] FAIL: %s", tostring(msg))
    -- non-zero exit so the C# orchestrator can detect it
    os.exit(2)
end

local buildPath = os.getenv("POB_PARITY_BUILD")
local outPath   = os.getenv("POB_PARITY_OUT")
if not buildPath or not outPath then
    fail("POB_PARITY_BUILD or POB_PARITY_OUT not set")
end

ConPrintf("[parity] reading %s", buildPath)
local f, err = io.open(buildPath, "r")
if not f then fail("open build XML: " .. tostring(err)) end
local xml = f:read("*all")
f:close()
if not xml or #xml == 0 then fail("empty build XML") end

-- Load build directly. main.modes.BUILD is the Build module table (PoB's class
-- pattern: the module IS the metatable). Calling :Init(nil, name, xml) runs
-- the whole load -> spec -> items -> skills -> calc pipeline synchronously.
local build = main.modes and main.modes.BUILD
if not build then fail("main.modes.BUILD missing — main:Init did not complete?") end

ConPrintf("[parity] loading build via buildMode:Init")
local okInit, errInit = pcall(build.Init, build, nil, "ParityBuild", xml)
if not okInit then fail("buildMode:Init: " .. tostring(errInit)) end

-- Mirror PBLEngine.LuaHost.SyncCalcsSkill: nudge calcs to write CALCS-mode
-- output (per-type Min/Max only show up in CALCS mode).
if build.calcsTab and build.mainSocketGroup then
    build.calcsTab.input.skill_number = build.mainSocketGroup
    local okCalc, errCalc = pcall(build.calcsTab.BuildOutput, build.calcsTab)
    if not okCalc then ConPrintf("[parity] BuildOutput warning: %s", tostring(errCalc)) end
end

-- Collect scalars only (numbers / strings / booleans). Same shape as
-- PBLEngine.LuaHost.GetAllStats — main overrides calcs for shared keys.
local out = {}
local function collect(src)
    if type(src) ~= "table" then return end
    for k, v in pairs(src) do
        local t = type(v)
        if t == "number" or t == "string" or t == "boolean" then
            out[k] = v
        end
    end
end
collect(build.calcsTab and build.calcsTab.calcsOutput)
collect(build.calcsTab and build.calcsTab.mainOutput)

-- Minimal JSON encoder. dkjson sits in runtime/lua/ and may not be on package.path
-- under SimpleGraphic, so we serialize by hand — payload is a flat dict of scalars.
local function encode(v)
    local t = type(v)
    if t == "string" then
        return '"' .. v:gsub('\\', '\\\\'):gsub('"', '\\"'):gsub('\n', '\\n'):gsub('\r', '\\r'):gsub('\t', '\\t') .. '"'
    elseif t == "number" then
        -- Match dotnet's invariant double formatting where possible.
        if v ~= v then return '"NaN"'                       -- NaN
        elseif v ==  math.huge then return '"+Inf"'
        elseif v == -math.huge then return '"-Inf"'
        elseif v == math.floor(v) and math.abs(v) < 1e15 then
            return tostring(math.floor(v))
        else
            return string.format("%.17g", v)
        end
    elseif t == "boolean" then
        return v and "true" or "false"
    end
    return "null"
end

-- Sort keys for stable output.
local keys = {}
for k in pairs(out) do keys[#keys + 1] = k end
table.sort(keys)

local parts = { "{" }
for i, k in ipairs(keys) do
    parts[#parts + 1] = (i > 1 and "," or "") .. '\n  ' .. encode(k) .. ': ' .. encode(out[k])
end
parts[#parts + 1] = '\n}\n'
local json = table.concat(parts)

local fw, errW = io.open(outPath, "w")
if not fw then fail("open output: " .. tostring(errW)) end
fw:write(json)
fw:close()

ConPrintf("[parity] wrote %d stats to %s", #keys, outPath)
-- Exit cleanly. SimpleGraphic doesn't tear down gracefully via os.exit but it works.
os.exit(0)
