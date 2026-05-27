using NLua;
using System;
using System.IO;
using PBLHost;

var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../.."));
using var host = new LuaHost();
host.Initialize(repoRoot);
host.NewBuild();

// Debug: what does CreateDisplayItemFromRaw return?
host.State.DoString(@"
    local rawText = 'New Item\nHeavy Bow\n25% increased Critical Damage Bonus'
    build.itemsTab:CreateDisplayItemFromRaw(rawText)
    if build.itemsTab.displayItem then
        print('displayItem created: ' .. tostring(build.itemsTab.displayItem.name))
        print('displayItem base: ' .. tostring(build.itemsTab.displayItem.baseName))
    else
        print('displayItem is nil!')
    end
    build.itemsTab:AddDisplayItem()
    runCallback('OnFrame')
    print('CritMultiplier after AddDisplayItem: ' .. tostring(build.calcsTab.mainOutput.CritMultiplier))
    -- Check which item slot has the bow
    for slotName, slot in pairs(build.itemsTab.slots) do
        if slot.item then
            print('Slot ' .. slotName .. ': ' .. tostring(slot.item.name))
        end
    end
");
