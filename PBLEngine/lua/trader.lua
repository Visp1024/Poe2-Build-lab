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
