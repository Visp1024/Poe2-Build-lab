-- compat.lua — LuaJIT 5.1 → Lua 5.4 compatibility shims for PBLHost.
-- Must be loaded before any PoB Lua file.

-- 5.1: unpack was table.unpack in 5.2+
unpack = table.unpack

-- LuaJIT provides global argv as 'arg'; NLua does not
arg = arg or {}

-- 5.1: loadstring renamed to load in 5.2
loadstring = loadstring or load

-- 5.1/5.2: math.atan2(y,x) merged into math.atan(y,x) in 5.3
math.atan2 = math.atan2 or function(y, x) return math.atan(y, x) end

-- 5.1/5.2: math.log10 removed in 5.2 (use math.log(x, base))
math.log10 = math.log10 or function(x) return math.log(x, 10) end

-- 5.1: math.pow removed in 5.2 (use x^y operator instead)
math.pow = math.pow or function(x, y) return x ^ y end

-- 5.4: string.format('%d', float) is a hard error; LuaJIT silently truncated.
-- Wrap with a pcall-retry that floors floats before integer format specifiers.
local _raw_format = string.format
string.format = function(fmt, ...)
    local ok, result = pcall(_raw_format, fmt, ...)
    if ok then return result end
    local args = {...}
    for i = 1, #args do
        if type(args[i]) == 'number' and math.type(args[i]) ~= 'integer' then
            local n = args[i]
            -- NaN or ±inf cannot be floor'd to an integer
            if n ~= n or n == 1/0 or n == -1/0 then
                args[i] = 0
            else
                args[i] = math.floor(n)
            end
        end
    end
    local ok2, result2 = pcall(_raw_format, fmt, table.unpack(args))
    if ok2 then return result2 end
    return '?'
end

-- LuaJIT 'bit' library — bitwise ops used widely in PoB calc files.
-- Lua 5.4 has native &, |, ~, >>, << so we emulate the full API.
bit = {
    band   = function(a, b, ...)
        local r = a & b
        local args = {...}
        for i = 1, #args do r = r & args[i] end
        return r
    end,
    bor    = function(a, b, ...)
        local r = a | b
        local args = {...}
        for i = 1, #args do r = r | args[i] end
        return r
    end,
    bxor   = function(a, b, ...)
        local r = a ~ b
        local args = {...}
        for i = 1, #args do r = r ~ args[i] end
        return r
    end,
    bnot   = function(a) return ~a & 0xFFFFFFFF end,
    lshift = function(a, b) return (a << b) & 0xFFFFFFFF end,
    rshift = function(a, b) return (a & 0xFFFFFFFF) >> b end,
    arshift = function(a, b)
        a = a & 0xFFFFFFFF
        if a >= 0x80000000 then a = a - 0x100000000 end
        return a >> b
    end,
    tobit  = function(a)
        a = a & 0xFFFFFFFF
        if a >= 0x80000000 then return a - 0x100000000 end
        return a
    end,
    tohex  = function(a, n)
        n = n or 8
        return string.format('%0' .. n .. 'x', a & 0xFFFFFFFF)
    end,
    rol    = function(a, b) b = b & 31; return ((a << b) | (a >> (32 - b))) & 0xFFFFFFFF end,
    ror    = function(a, b) b = b & 31; return ((a >> b) | (a << (32 - b))) & 0xFFFFFFFF end,
    bswap  = function(a)
        a = a & 0xFFFFFFFF
        return ((a & 0xFF) << 24) | (((a >> 8) & 0xFF) << 16)
             | (((a >> 16) & 0xFF) << 8) | ((a >> 24) & 0xFF)
    end,
}
package.loaded['bit'] = bit

-- LuaJIT 'jit' module — stubs for jit.opt.start(), require('jit').off(), etc.
jit = {
    version = 'LuaJIT 2.1 (C# stub)',
    os      = 'Windows',
    arch    = 'x64',
    opt     = { start = function() end, flush = function() end },
    off     = function() end,
    on      = function() end,
    flush   = function() end,
    attach  = function() end,
    status  = function() return false, 'off', 'off' end,
}
package.loaded['jit']     = jit
package.loaded['jit.opt'] = jit.opt

-- lua-utf8 stub — Common.lua uses it only for thousands-separator formatting;
-- ASCII-only string ops are sufficient for calc output.
package.loaded['lua-utf8'] = {
    len     = function(s) return #s end,
    sub     = string.sub,
    reverse = string.reverse,
    find    = string.find,
    gsub    = string.gsub,
    char    = string.char,
    byte    = string.byte,
    gmatch  = string.gmatch,
    match   = string.match,
    upper   = string.upper,
    lower   = string.lower,
    codes   = function(s)
        local i = 0
        return function()
            i = i + 1
            if i <= #s then return i, string.byte(s, i) end
        end
    end,
}

-- io.read stub — HeadlessWrapper.lua calls io.read('*l') when startup fails,
-- which blocks forever in headless mode.  Return '' so execution continues.
local _io_read = io.read
io.read = function(fmt, ...)
    if fmt == '*l' or fmt == 'l' or fmt == '*L' then return '' end
    return _io_read(fmt, ...)
end
