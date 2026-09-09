-- modtiers.lua — вычисление «грейда» (тира) модификаторов предмета.
--
-- Тир считается по пулу модов базы предмета: моды одного типа (Prefix/Suffix) с
-- одинаковым statOrder образуют серию (ровно та группировка, которой пользуется
-- крафт-панель оригинального PoB, ItemsTab:UpdateAffixControl). Внутри серии
-- сортировка по mod.level по убыванию, T1 = самый высокий уровень = лучший тир —
-- как показывает игра. Внутренняя нумерация PoB обратная (там T1 — худший),
-- поэтому её здесь намеренно не переиспользуем.
--
-- Публичный API:
--   PBLModTiers.BuildPool(baseName)          -> pool (кэшируется)
--   PBLModTiers.Resolve(baseName, lineText)  -> { type, tier, count, affix, level } | nil
--   PBLModTiers.TierOfModId(baseName, modId) -> { type, tier, count, affix, level } | nil
--   PBLModTiers.BuildLineMeta(item)          -> modLine -> { kind }  (identity + по .line)
--   PBLModTiers.BadgeFor(baseName, kind, text) -> строка "P|3|13" / "I" / nil

PBLModTiers = PBLModTiers or {}
local M = PBLModTiers

M._cache = M._cache or {}

--- Сбрасывает кэш пулов (нужно после перезагрузки данных).
function M.ResetCache()
	M._cache = {}
end

-- ── Вспомогательное ─────────────────────────────────────────────────────────

--- Убирает inline-коды цвета (^xRRGGBB / ^N) из строки.
local function stripColors(s)
	s = s:gsub("%^[xX]%x%x%x%x%x%x", "")
	s = s:gsub("%^%d", "")
	return s
end

--- «Скелет» строки мода: убираем всё числовое и знаки диапазона, чтобы
--- «+87 к макс. запасу жизни» и «+(85-99) к макс. запасу жизни» совпали.
local function skeleton(s)
	s = s:gsub("%b{}", "")            -- {tags:...} / {range:...} / {crafted}
	s = stripColors(s)
	s = s:gsub("[%d%.%(%)%+%-]", "")
	s = s:gsub("%s+", " ")
	return (s:gsub("^%s+", ""):gsub("%s+$", ""))
end
M.Skeleton = skeleton

--- Числовые диапазоны «(N-M)» в тексте мода → список {min, max}.
local function parseRanges(text)
	local out = {}
	for lo, hi in text:gmatch("%((%-?%d+%.?%d*)%-(%-?%d+%.?%d*)%)") do
		out[#out + 1] = { min = tonumber(lo), max = tonumber(hi) }
	end
	return out
end

--- Все числа строки (роллы).
local function parseValues(text)
	local out = {}
	for v in text:gmatch("%-?%d+%.?%d*") do
		out[#out + 1] = tonumber(v)
	end
	return out
end

--- Ключ серии: тип + подпись statOrder.
local function seriesKey(mod)
	local parts = {}
	for i = 1, #(mod.statOrder or {}) do
		parts[#parts + 1] = tostring(mod.statOrder[i])
	end
	return (mod.type or "") .. "|" .. table.concat(parts, ",")
end

--- Теги базы предмета (для отбора модов, способных на неё выпасть).
local function baseTags(base)
	local tags = {}
	if base.tags then
		for tag in pairs(base.tags) do tags[tag] = true end
	end
	if base.type then
		tags[(base.type:lower():gsub(" ", "_"):gsub(":.*", ""))] = true
	end
	return tags
end

--- Источник модов для базы — та же цепочка, что в Item.lua при разборе предмета.
local function affixSourceFor(base)
	if not (data and data.itemMods) then return nil end
	return (base.subType and data.itemMods[base.type .. base.subType])
		or data.itemMods[base.type]
		or data.itemMods.Item
end

-- ── Построение пула ─────────────────────────────────────────────────────────

--- Пул модов базы с посчитанными тирами.
--- pool.bySkel[skeleton] = { entry, ... }   — по «скелету» каждой строки мода
--- pool.byModId[modId]   = entry
function M.BuildPool(baseName)
	if not baseName or baseName == "" then return nil end
	local cached = M._cache[baseName]
	if cached ~= nil then
		if cached == false then return nil end
		return cached
	end

	local base = data and data.itemBases and data.itemBases[baseName]
	local source = base and affixSourceFor(base)
	if not (base and source) then
		M._cache[baseName] = false
		return nil
	end

	local tags = baseTags(base)

	-- 1. Отбираем моды, способные выпасть на эту базу.
	local applicable = {}
	for modId, mod in pairs(source) do
		if mod.type and mod[1] then
			local ok = false
			for i, wk in ipairs(mod.weightKey or {}) do
				local wv = mod.weightVal and mod.weightVal[i] or 0
				if wv > 0 and tags[wk] then ok = true break end
			end
			if ok then
				applicable[#applicable + 1] = { modId = modId, mod = mod }
			end
		end
	end

	-- 2. Раскладываем по сериям (тип + statOrder).
	local series = {}
	for _, rec in ipairs(applicable) do
		local key = seriesKey(rec.mod)
		local s = series[key]
		if not s then s = {} series[key] = s end
		s[#s + 1] = rec
	end

	-- 3. Внутри серии — по убыванию уровня: T1 = лучший.
	local pool = { bySkel = {}, byModId = {} }
	for _, s in pairs(series) do
		table.sort(s, function(a, b)
			if a.mod.level ~= b.mod.level then
				return (a.mod.level or 0) > (b.mod.level or 0)
			end
			return a.modId < b.modId
		end)
		local count = #s
		for tier, rec in ipairs(s) do
			local entry = {
				modId = rec.modId,
				type  = rec.mod.type,
				affix = rec.mod.affix or "",
				level = rec.mod.level or 0,
				tier  = tier,
				count = count,
				mod   = rec.mod,
			}
			pool.byModId[rec.modId] = entry
			-- Гибридные моды дают несколько строк — индексируем каждую.
			for i = 1, #rec.mod do
				local skel = skeleton(rec.mod[i])
				if skel ~= "" then
					local bucket = pool.bySkel[skel]
					if not bucket then bucket = {} pool.bySkel[skel] = bucket end
					bucket[#bucket + 1] = {
						entry  = entry,
						text   = rec.mod[i],
						ranges = parseRanges(rec.mod[i]),
					}
				end
			end
		end
	end

	M._cache[baseName] = pool
	return pool
end

-- ── Разрешение строки мода в тир ────────────────────────────────────────────

--- По тексту роллнутого мода находит запись пула: точный тир той серии,
--- чьи диапазоны накрывают выпавшие значения.
function M.Resolve(baseName, lineText)
	local cand = M.ResolveCandidate(baseName, lineText)
	return cand and cand.entry or nil
end

--- То же, но возвращает саму запись пула вместе с исходным текстом строки мода
--- (нужен для диапазона ползунка в редакторе).
function M.ResolveCandidate(baseName, lineText)
	if not lineText or lineText == "" then return nil end
	local pool = M.BuildPool(baseName)
	if not pool then return nil end

	local bucket = pool.bySkel[skeleton(lineText)]
	if not bucket or #bucket == 0 then return nil end
	if #bucket == 1 then return bucket[1] end

	local values = parseValues((lineText:gsub("%b{}", "")))
	if #values == 0 then return bucket[1] end

	local best, bestScore = nil, -1
	for _, cand in ipairs(bucket) do
		local score = 0
		local n = math.min(#cand.ranges, #values)
		for i = 1, n do
			local r = cand.ranges[i]
			if values[i] >= r.min and values[i] <= r.max then score = score + 1 end
		end
		if score > bestScore then bestScore = score best = cand end
	end
	return best or bucket[1]
end

--- Плоская сводка по строке мода для C#: type, affix, statText, tier, count, group.
--- Возвращает nil, если строка не сопоставилась с пулом базы.
function M.ResolveInfo(baseName, lineText)
	local cand = M.ResolveCandidate(baseName, lineText)
	if not cand then return nil end
	local e = cand.entry
	return {
		type     = e.type or "",
		affix    = e.affix or "",
		statText = cand.text or "",
		tier     = e.tier or 0,
		count    = e.count or 0,
		group    = (e.mod and e.mod.group) or "",
	}
end

--- Прямой поиск по modId (для уже скрафченных предметов, где modId известен).
function M.TierOfModId(baseName, modId)
	local pool = M.BuildPool(baseName)
	return pool and pool.byModId[modId] or nil
end

-- ── Метаданные строк предмета ───────────────────────────────────────────────

--- Раскладывает строки мода предмета по видам. Ключ — сама таблица modLine;
--- дополнительно кладём по modLine.line, потому что тултип при масштабировании
--- (эффект самоцвета) форматирует КОПИЮ строки, и identity теряется.
function M.BuildLineMeta(item)
	local meta = { byLine = {} }
	local function add(list, kind)
		for _, ml in ipairs(list or {}) do
			local rec = { kind = kind }
			meta[ml] = rec
			if ml.line then meta.byLine[ml.line] = rec end
		end
	end
	add(item.enchantModLines, "enchant")
	add(item.runeModLines,    "rune")
	add(item.implicitModLines, "implicit")
	add(item.explicitModLines, "explicit")
	return meta
end

--- Бейдж для строки тултипа: "P|3|13" / "S|5|7" / "P" / "I" / nil.
--- kind — из BuildLineMeta, text — уже отформатированная строка мода.
function M.BadgeFor(baseName, kind, text)
	if kind == "implicit" then return "I" end
	if kind ~= "explicit" then return nil end
	local e = M.Resolve(baseName, stripColors(text or ""))
	if not e then return nil end
	local letter = e.type == "Prefix" and "P" or (e.type == "Suffix" and "S" or nil)
	if not letter then return nil end
	if not e.tier or e.count <= 1 then return letter end
	return letter .. "|" .. tostring(e.tier) .. "|" .. tostring(e.count)
end
