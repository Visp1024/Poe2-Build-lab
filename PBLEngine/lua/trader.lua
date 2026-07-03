-- PBL trader glue: headless-обвязка над TradeQueryGenerator/TradeQueryRequests.
-- НЕ трогает src/Classes/* — только использует их. Загружается лениво
-- через LuaHost.EnsureTraderInit() после HeadlessWrapper.
local dkjson = require("dkjson")

PBLTrader = PBLTrader or {}

-- Слоты как в оригинальном PoB Trader (TradeQuery.lua:19)
PBLTrader.baseSlots = { "Weapon 1", "Weapon 2", "Weapon 1 Swap", "Weapon 2 Swap",
	"Helmet", "Body Armour", "Gloves", "Boots", "Amulet", "Ring 1", "Ring 2", "Ring 3",
	"Belt", "Charm 1", "Charm 2", "Charm 3", "Flask 1", "Flask 2" }

function PBLTrader.Init()
	if not main.api then
		main.api = new("PoEAPI", main.lastToken, main.lastRefreshToken, main.tokenExpiry)
	end
	PBLTrader.requests = PBLTrader.requests or new("TradeQueryRequests")
	-- Генератор держит ссылку на itemsTab — пересоздаём при смене билда
	if not PBLTrader.generator or PBLTrader.generator.itemsTab ~= build.itemsTab then
		PBLTrader.generator = new("TradeQueryGenerator", { itemsTab = build.itemsTab })
	end
	-- tradeTypeIndex выставляет только UI-попап оригинала (RequestQuery); без него
	-- status.option в query получается nil → API: "Invalid status type".
	-- 1 = "securable" — дефолт оригинала (TradeQuery.lua:310).
	PBLTrader.generator.tradeTypeIndex = PBLTrader.generator.tradeTypeIndex or 1
end

-- ── Генерация взвешенного запроса ────────────────────────────────────────────
-- StartQuery создаёт корутину; C# гонит её через StepGenerate() до genDone.
-- RequestQuery (UI-попап оригинала) не используется.

function PBLTrader.StartGenerate(slotName, optionsJson)
	PBLTrader.Init()
	local options, _, jsonErr = dkjson.decode(optionsJson)
	if not options then return "error: bad options json: " .. tostring(jsonErr) end
	options.statWeights = PBLTrader._enrichWeights(options.statWeights or {})
	if #options.statWeights == 0 then return "error: no stat weights selected" end
	local slot = build.itemsTab.slots[slotName]
	if not slot then return "error: unknown slot " .. tostring(slotName) end

	PBLTrader.genDone, PBLTrader.genQuery, PBLTrader.genErr = false, nil, nil
	-- HeadlessWrapper GetTime() = const 0, а yield-гейт корутины генератора —
	-- GetTime()-start > 50 мс (TradeQueryGenerator.lua:710). Без реального времени
	-- корутина не yield'ится и одна StepGenerate прогоняет всё разом (нет отмены).
	-- Ставим real-time GetTime на время генерации; снимаем на всех выходах.
	if not PBLTrader._origGetTime then
		PBLTrader._origGetTime = GetTime
		local t0 = os.clock()
		GetTime = function() return (os.clock() - t0) * 1000 end
	end
	PBLTrader.generator.requesterContext = nil
	PBLTrader.generator.requesterCallback = function(_, queryJson, errMsg)
		PBLTrader.genQuery, PBLTrader.genErr, PBLTrader.genDone = queryJson, errMsg, true
	end
	PBLTrader.generator:StartQuery(slot, options)
	-- StartQuery молча выходит для неподдерживаемых категорий (TradeQueryGenerator.lua:840-843)
	if not (PBLTrader.generator.calcContext and PBLTrader.generator.calcContext.co) then
		PBLTrader.genDone = true
		PBLTrader.genErr = PBLTrader.genErr or "unsupported item category"
		PBLTrader._restoreGetTime()
	end
	return "started"
end

function PBLTrader._restoreGetTime()
	if PBLTrader._origGetTime then
		GetTime = PBLTrader._origGetTime
		PBLTrader._origGetTime = nil
	end
end

function PBLTrader.StepGenerate()
	if not PBLTrader.genDone then
		PBLTrader.generator:OnFrame() -- resume корутины; по смерти сам зовёт FinishQuery → callback
	end
	if PBLTrader.genDone then
		PBLTrader._restoreGetTime()
	end
	return PBLTrader.genDone
end

function PBLTrader.CancelGenerate()
	local g = PBLTrader.generator
	if g and g.calcContext and g.calcContext.co then
		g.calcContext.co = nil
		main:ClosePopup()
	end
	PBLTrader.genDone, PBLTrader.genQuery, PBLTrader.genErr = true, nil, "cancelled"
	PBLTrader._restoreGetTime()
end

function PBLTrader.GetGenerateResult()
	return PBLTrader.genQuery, PBLTrader.genErr
end

-- ── Поиск и fetch результатов ────────────────────────────────────────────────
-- SearchWithQueryWeightAdjusted сам повторяет поиск при >10k результатов и
-- фетчит блоки по 10; нам остаётся качать ProcessQueue и ждать callback.

function PBLTrader.StartSearch(realm, league, queryJson)
	PBLTrader.Init()
	PBLTrader.searchDone, PBLTrader.searchErr = false, nil
	PBLTrader.searchItems, PBLTrader.searchQueryId = nil, nil
	PBLTrader.rateLimitWait = 0
	PBLTrader.requests:SearchWithQueryWeightAdjusted(realm, league, queryJson,
		function(items, errMsg)
			PBLTrader.searchItems, PBLTrader.searchErr = items, errMsg
			PBLTrader.searchDone = true
		end,
		{ callbackQueryId = function(id) PBLTrader.searchQueryId = id end })
end

function PBLTrader.Pump()
	PBLTrader.requests:ProcessQueue(function(waitTime)
		PBLTrader.rateLimitWait = waitTime or 0
	end)
end

function PBLTrader.GetSearchStateJson()
	return dkjson.encode({
		done = PBLTrader.searchDone or false,
		err = PBLTrader.searchErr,
		queryId = PBLTrader.searchQueryId,
		rateLimitWait = PBLTrader.rateLimitWait or 0,
		items = PBLTrader.searchItems,
	})
end

-- ── Дифф результата ─────────────────────────────────────────────────────────

function PBLTrader.ComputeDiffJson(slotName, itemString, statWeightsJson)
	PBLTrader.Init()
	local weights = dkjson.decode(statWeightsJson) or {}
	weights = PBLTrader._enrichWeights(weights)
	local calcFunc, baseOutput = build.calcsTab:GetMiscCalculator()
	if not calcFunc then return dkjson.encode({ err = "no calculator" }) end
	local ok, item = pcall(function()
		local it = new("Item", itemString)
		-- конструктор только парсит текст; modList/slotModList строит BuildAndParseRaw
		-- (так же делает генератор — TradeQueryGenerator.lua:697)
		it:BuildAndParseRaw()
		return it
	end)
	if not ok or not item or not item.baseName then
		return dkjson.encode({ err = "bad item text" })
	end
	local okCalc, output = pcall(function()
		return calcFunc({ repSlotName = slotName, repItem = item })
	end)
	if not okCalc or not output then
		return dkjson.encode({ err = "calc failed: " .. tostring(output) })
	end
	local function dps(o) return o.FullDPS or o.CombinedDPS or o.TotalDPS or 0 end
	local statValue = PBLTrader.generator.WeightedRatioOutputs(baseOutput, output, weights) * 1000
	return dkjson.encode({
		dpsDiff = dps(output) - dps(baseOutput),
		ehpDiff = (output.TotalEHP or 0) - (baseOutput.TotalEHP or 0),
		statValue = statValue,
	})
end

function PBLTrader.GetSlotsJson()
	local out = {}
	for _, slotName in ipairs(PBLTrader.baseSlots) do
		local slot = build.itemsTab.slots[slotName]
		if slot then
			local item = slot.selItemId and build.itemsTab.items[slot.selItemId]
			table.insert(out, {
				slotName = slotName,
				itemName = item and (item.name or item.baseName) or "",
			})
		end
	end
	return dkjson.encode(out)
end

-- ── Веса статов ──────────────────────────────────────────────────────────────
-- Источник истины — build.itemsTab.tradeQuery.statSortSelectionList: его уже
-- сериализует ItemsTab (узел TradeSearchWeights, ItemsTab.lua:1142/1238),
-- т.е. персист в XML билда и совместимость с оригинальным PoB бесплатны.

local function findPowerStat(statKey)
	for _, entry in ipairs(data.powerStatList) do
		if entry.stat == statKey then return entry end
	end
end

function PBLTrader._enrichWeights(list)
	local out = {}
	for _, w in ipairs(list or {}) do
		local ps = w.stat and findPowerStat(w.stat)
		if ps and (tonumber(w.weightMult) or 0) > 0 then
			table.insert(out, {
				stat = ps.stat, label = ps.label, transform = ps.transform,
				weightMult = tonumber(w.weightMult),
			})
		end
	end
	return out
end

function PBLTrader.GetWeightStatsJson()
	local out = {}
	for _, stat in ipairs(data.powerStatList) do
		-- тот же фильтр, что попап оригинала (TradeQuery.lua:635-647)
		if not stat.ignoreForItems and stat.label ~= "Name" and stat.stat then
			table.insert(out, { stat = stat.stat, label = stat.label })
		end
	end
	return dkjson.encode(out)
end

function PBLTrader._weightsList()
	PBLTrader.Init()
	local tq = build.itemsTab.tradeQuery
	tq.statSortSelectionList = tq.statSortSelectionList or {}
	if #tq.statSortSelectionList == 0 then
		tq.statSortSelectionList = PBLTrader._enrichWeights({
			{ stat = "FullDPS", weightMult = 1.0 },
			{ stat = "TotalEHP", weightMult = 0.5 },
		})
	end
	return tq.statSortSelectionList
end

function PBLTrader.GetWeightsJson()
	local out = {}
	for _, w in ipairs(PBLTrader._weightsList()) do
		table.insert(out, { stat = w.stat, label = w.label, weightMult = w.weightMult })
	end
	return dkjson.encode(out)
end

function PBLTrader.SetWeightsJson(weightsJson)
	PBLTrader.Init()
	local list = dkjson.decode(weightsJson) or {}
	build.itemsTab.tradeQuery.statSortSelectionList = PBLTrader._enrichWeights(list)
end
