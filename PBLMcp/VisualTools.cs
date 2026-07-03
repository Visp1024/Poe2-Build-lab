using ModelContextProtocol.Server;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace PBLMcp;

/// <summary>
/// MCP tools that drive a real PBLApp.exe process via HTTP IPC.
/// Unlike AppTools/LocTools (which operate on headless ViewModels inside the MCP
/// process), these tools control the actual rendered Avalonia client and can
/// produce real screenshots for visual analysis.
/// </summary>
[McpServerToolType]
public class VisualTools
{
    // ── Client lifecycle ──────────────────────────────────────────────────

    [McpServerTool]
    [Description(
        "Launch PBLApp.exe (the visual client) with IPC enabled and wait for it to be ready. " +
        "Returns 'Ready' or an error. If a client is already running, returns its PID. " +
        "Typical startup time: 30-45 seconds (LuaHost initialization).")]
    public async Task<string> VisualLaunchClient(
        [Description("Maximum seconds to wait for the client to respond on IPC (default 90).")] int timeoutSeconds = 90)
    {
        try
        {
            if (await IpcClient.IsAliveAsync())
            {
                var d = IpcClient.TryReadDiscovery();
                return $"Already running. PID={d?.Pid}, port={d?.Port}.";
            }

            // Find the PBLApp executable. Prefer freshly-built Debug binary.
            var repoRoot = FindRepoRoot();
            var exe = Path.Combine(repoRoot, "PBLApp", "bin", "Debug", "net9.0", "PBLApp.exe");
            if (!File.Exists(exe))
                return Error($"PBLApp.exe not found at {exe}. Build the project first (dotnet build PBLApp/PBLApp.csproj).");

            var psi = new ProcessStartInfo(exe, "--enable-ipc")
            {
                UseShellExecute  = false,
                WorkingDirectory = repoRoot,
                CreateNoWindow   = false,
            };
            var proc = Process.Start(psi)
                       ?? throw new InvalidOperationException("Process.Start returned null.");

            if (!await IpcClient.WaitForReadyAsync(timeoutSeconds))
                return Error($"Client started (PID={proc.Id}) but IPC did not respond within {timeoutSeconds}s.");

            var disc = IpcClient.TryReadDiscovery();
            return $"Client ready. PID={disc?.Pid ?? proc.Id}, port={disc?.Port}.";
        }
        catch (Exception ex) { return Error(ex); }
    }

    [McpServerTool]
    [Description("Check if a PBLApp visual client is running and responsive over IPC.")]
    public async Task<string> VisualIsClientRunning()
    {
        try
        {
            var alive = await IpcClient.IsAliveAsync();
            if (!alive) return "{\"running\": false}";
            var d = IpcClient.TryReadDiscovery();
            return $"{{\"running\": true, \"pid\": {d?.Pid ?? 0}, \"port\": {d?.Port ?? 0}}}";
        }
        catch (Exception ex) { return Error(ex); }
    }

    [McpServerTool]
    [Description("Close the running PBLApp visual client (kills the process). Safe to call when no client is running.")]
    public string VisualCloseClient()
    {
        try
        {
            var d = IpcClient.TryReadDiscovery();
            if (d is null || d.Pid <= 0) return "No client running.";
            try
            {
                var p = Process.GetProcessById(d.Pid);
                p.Kill(entireProcessTree: true);
                p.WaitForExit(5000);
                return $"Killed PID={d.Pid}.";
            }
            catch (ArgumentException) { return $"Process {d.Pid} already exited."; }
        }
        catch (Exception ex) { return Error(ex); }
    }

    // ── Screenshot ────────────────────────────────────────────────────────

    [McpServerTool]
    [Description(
        "Capture a screenshot of the running PBLApp visual client. Returns the absolute path " +
        "to the saved PNG. If `path` is empty, saves to %TEMP%\\pob-screenshots\\pob_<timestamp>.png. " +
        "Client must be running (use visual_launch_client first).")]
    public async Task<string> VisualScreenshot(
        [Description("Absolute output path for the PNG. If empty, auto-generates a temp path.")] string path = "")
    {
        try
        {
            var body = string.IsNullOrWhiteSpace(path) ? (object)new { } : new { path };
            return await IpcClient.CallAsync("POST", "/screenshot", body);
        }
        catch (Exception ex) { return Error(ex); }
    }

    // ── State / control ───────────────────────────────────────────────────

    [McpServerTool]
    [Description(
        "Get current state of the running PBLApp client: language, current page, " +
        "selected tab/build, window size. JSON response.")]
    public async Task<string> VisualGetState()
    {
        try { return await IpcClient.CallAsync("GET", "/state"); }
        catch (Exception ex) { return Error(ex); }
    }

    [McpServerTool]
    [Description(
        "Change UI language in the running visual client. Triggers full UI re-translation " +
        "(tabs, labels, item names). Pass 'en' or 'ru'.")]
    public async Task<string> VisualSetLanguage(
        [Description("Language code: 'en' or 'ru'.")] string lang)
    {
        try { return await IpcClient.CallAsync("POST", "/set-language", new { lang }); }
        catch (Exception ex) { return Error(ex); }
    }

    [McpServerTool]
    [Description(
        "Switch active tab on the BuildPage of the running client. " +
        "Tab names: Items/Tree/Skills/Calcs/Notes/Config/ImportExport (case-insensitive), or 0-based index.")]
    public async Task<string> VisualSelectTab(
        [Description("Tab name or 0-based index.")] string nameOrIndex)
    {
        try
        {
            object body = int.TryParse(nameOrIndex, out var idx)
                ? new { index = idx }
                : new { name = nameOrIndex };
            return await IpcClient.CallAsync("POST", "/select-tab", body);
        }
        catch (Exception ex) { return Error(ex); }
    }

    [McpServerTool]
    [Description("List builds shown in the BuildList page of the running client.")]
    public async Task<string> VisualListBuilds()
    {
        try { return await IpcClient.CallAsync("GET", "/list-builds"); }
        catch (Exception ex) { return Error(ex); }
    }

    [McpServerTool]
    [Description(
        "Open a build by name (partial match) in the running client. " +
        "Client navigates from BuildList to BuildPage. Allow a few seconds for the build to load — " +
        "check visual_get_state until page=BuildPageReady.")]
    public async Task<string> VisualOpenBuild(
        [Description("Full or partial build name.")] string name)
    {
        try { return await IpcClient.CallAsync("POST", "/open-build", new { name }); }
        catch (Exception ex) { return Error(ex); }
    }

    [McpServerTool]
    [Description("Navigate back from the BuildPage to the BuildList in the running client.")]
    public async Task<string> VisualGoBack()
    {
        try { return await IpcClient.CallAsync("POST", "/go-back"); }
        catch (Exception ex) { return Error(ex); }
    }

    // ── Items tab (detailed control) ──────────────────────────────────────

    [McpServerTool]
    [Description(
        "Get full state of the Items tab in the running client: all 22 slots " +
        "(with translatedBaseName in current language), jewel sockets, pool, " +
        "selected slot, weapon-swap state.")]
    public async Task<string> VisualItemsState()
    {
        try { return await IpcClient.CallAsync("GET", "/items/state"); }
        catch (Exception ex) { return Error(ex); }
    }

    [McpServerTool]
    [Description(
        "Select an equipment slot in the running client. Slot examples: Helmet, Body Armour, " +
        "Weapon 1, Ring 1, Gloves, Boots, Belt, Amulet, Flask 1, Charm 1, or 'Jewel 12345'. " +
        "Auto-opens the item editor on the right if the slot is occupied.")]
    public async Task<string> VisualItemsSelectSlot(
        [Description("Slot name (case-insensitive).")] string slot)
    {
        try { return await IpcClient.CallAsync("POST", "/items/select-slot", new { slot }); }
        catch (Exception ex) { return Error(ex); }
    }

    [McpServerTool]
    [Description("Select an item from the item pool by its id. Opens the editor on that item.")]
    public async Task<string> VisualItemsSelectPool(
        [Description("Pool item id (from items/state response).")] int id)
    {
        try { return await IpcClient.CallAsync("POST", "/items/select-pool", new { id }); }
        catch (Exception ex) { return Error(ex); }
    }

    [McpServerTool]
    [Description(
        "Equip a pool item into a specific slot in the running client. " +
        "The item type must be compatible with the slot (e.g. a Ring item only fits Ring 1/2/3).")]
    public async Task<string> VisualItemsEquip(
        [Description("Pool item id.")] int id,
        [Description("Target slot name.")] string slot)
    {
        try { return await IpcClient.CallAsync("POST", "/items/equip", new { id, slot }); }
        catch (Exception ex) { return Error(ex); }
    }

    [McpServerTool]
    [Description("Unequip the item currently in the given slot (returns it to the pool).")]
    public async Task<string> VisualItemsUnequip(
        [Description("Slot name (must currently hold an item).")] string slot)
    {
        try { return await IpcClient.CallAsync("POST", "/items/unequip", new { slot }); }
        catch (Exception ex) { return Error(ex); }
    }

    [McpServerTool]
    [Description(
        "Get the state of the currently-open item editor: rarity, base, unique, " +
        "all explicit/implicit mods with their slider values. Returns {isOpen:false} if no editor open.")]
    public async Task<string> VisualItemsGetEditor()
    {
        try { return await IpcClient.CallAsync("GET", "/items/get-editor"); }
        catch (Exception ex) { return Error(ex); }
    }

    [McpServerTool]
    [Description(
        "Get the structured contents of the right-panel item tooltip (PoB-styled display). " +
        "Returns {isOpen:false} when no item is selected. Otherwise returns the list of lines " +
        "with kind ('text'|'separator'), size, centered flag, block index, plain text, and " +
        "coloured segments. Useful for verifying tooltip correctness without taking a screenshot.")]
    public async Task<string> VisualItemsGetTooltip()
    {
        try { return await IpcClient.CallAsync("GET", "/items/get-tooltip"); }
        catch (Exception ex) { return Error(ex); }
    }

    [McpServerTool]
    [Description(
        "Read the PoB-styled display tooltip of EVERY equipped item (incl. jewel sockets) " +
        "and every pool item at once, without changing the current selection. Returns " +
        "{slots:[{slot,displayName,lines:[...]}], pool:[{id,name,equippedSlot,lines:[...]}]}. " +
        "Each line has kind/size/centered/block/plain text and coloured segments. Use to " +
        "audit all tooltips in one call instead of selecting items one by one.")]
    public async Task<string> VisualItemsGetAllTooltips()
    {
        try { return await IpcClient.CallAsync("GET", "/items/all-tooltips"); }
        catch (Exception ex) { return Error(ex); }
    }

    [McpServerTool]
    [Description(
        "Read the Runic Meridians (Martial Artist) body-tattoo Rune sockets: whether the panel " +
        "is available (node allocated) and the rune in each fixed socket (helmet / body armour ×2 " +
        "/ gloves / boots). Returns {available, sockets:[{index,slotType,label,rune,options}]}.")]
    public async Task<string> VisualItemsTattooState()
    {
        try { return await IpcClient.CallAsync("GET", "/items/tattoo-state"); }
        catch (Exception ex) { return Error(ex); }
    }

    [McpServerTool]
    [Description(
        "Set (or clear) the Rune in a Runic Meridians tattoo socket by 1-based index. The rune " +
        "name is matched against that socket's compatible runes; pass empty to clear. The rune's " +
        "mods are injected into calc via the hidden tattoo custom-mods path.")]
    public async Task<string> VisualItemsSetTattoo(
        [Description("1-based socket index (1=helmet, 2-3=body armour, 4=gloves, 5=boots).")] int index,
        [Description("Rune name (exact), or empty to clear.")] string rune = "")
    {
        try { return await IpcClient.CallAsync("POST", "/items/set-tattoo", new { index, rune }); }
        catch (Exception ex) { return Error(ex); }
    }

    [McpServerTool]
    [Description(
        "Read the Crystalline Phylactery (Lich) state: whether the node is allocated and the " +
        "name of the jewel socketed into its tree jewel socket (whose bonuses the engine then " +
        "applies at 2× effect). Socket the jewel via the normal jewel-socket UI. " +
        "Returns {available, jewel}.")]
    public async Task<string> VisualItemsPhylacteryState()
    {
        try { return await IpcClient.CallAsync("GET", "/items/phylactery-state"); }
        catch (Exception ex) { return Error(ex); }
    }

    [McpServerTool]
    [Description(
        "Report the equip-compatible (drag-highlight) target slots PoB accepts for an item. " +
        "Honours keystone/ascendancy rules: e.g. a Focus is only valid in Weapon 2 while a " +
        "Staff is equipped if Instruments of Power is allocated; Giant's Blood enables dual " +
        "one-handers, etc. Pass a pool item id, or an equipped slot name, or neither to get " +
        "the map for every pool item ({pool:[{id,name,equippedSlot,slots:[...]}]}).")]
    public async Task<string> VisualItemsCompatibleSlots(
        [Description("Pool item id. Optional.")] int? id = null,
        [Description("Equipped slot name (returns OTHER slots the item could move to). Optional.")] string? slot = null)
    {
        try
        {
            object body = id.HasValue ? new { id = id.Value }
                        : !string.IsNullOrEmpty(slot) ? new { slot }
                        : new { };
            return await IpcClient.CallAsync("POST", "/items/compatible-slots", body);
        }
        catch (Exception ex) { return Error(ex); }
    }

    [McpServerTool]
    [Description(
        "Switch the right pane from the styled display tooltip to the inline editor for the " +
        "currently selected item. Requires an item to be selected (slot with an item, or pool item).")]
    public async Task<string> VisualItemsEditCurrent()
    {
        try { return await IpcClient.CallAsync("POST", "/items/edit-current"); }
        catch (Exception ex) { return Error(ex); }
    }

    [McpServerTool]
    [Description(
        "Cancel inline editing and return to the styled display tooltip for the same item.")]
    public async Task<string> VisualItemsCancelEdit()
    {
        try { return await IpcClient.CallAsync("POST", "/items/cancel-edit"); }
        catch (Exception ex) { return Error(ex); }
    }

    // ── Editor mutation tools (full crafting pipeline) ───────────────────

    [McpServerTool]
    [Description("Commit the current editor state (creates or updates the pool item). Returns {ok:true} or {error:'...'}. Closes the editor on success.")]
    public async Task<string> VisualItemsEditorSave()
    {
        try { return await IpcClient.CallAsync("POST", "/items/editor-save"); }
        catch (Exception ex) { return Error(ex); }
    }

    [McpServerTool]
    [Description("Open a new-item editor with optional initial selection. Body fields (all optional): rarity ('Normal'|'Magic'|'Rare'|'Unique'), category, base (partial match), unique (partial match).")]
    public async Task<string> VisualItemsEditorCreateNew(
        [Description("Rarity: Normal/Magic/Rare/Unique. Optional.")] string? rarity = null,
        [Description("Item category (e.g. 'Body Armour', 'Boots'). Optional.")] string? category = null,
        [Description("Base item name (partial match). Optional.")] string? baseItem = null,
        [Description("Unique item name (partial match). Sets rarity=Unique. Optional.")] string? unique = null)
    {
        var body = new Dictionary<string, string>();
        if (rarity   is not null) body["rarity"]   = rarity;
        if (category is not null) body["category"] = category;
        if (baseItem is not null) body["base"]     = baseItem;
        if (unique   is not null) body["unique"]   = unique;
        try { return await IpcClient.CallAsync("POST", "/items/editor-create-new", body); }
        catch (Exception ex) { return Error(ex); }
    }

    [McpServerTool]
    [Description("Set the rarity of the item being edited. Values: Normal, Magic, Rare, Unique.")]
    public async Task<string> VisualItemsEditorSetRarity(
        [Description("New rarity.")] string rarity)
    {
        try { return await IpcClient.CallAsync("POST", "/items/editor-set-rarity", new { rarity }); }
        catch (Exception ex) { return Error(ex); }
    }

    [McpServerTool]
    [Description("Set the base item by name within the currently selected category (partial match).")]
    public async Task<string> VisualItemsEditorSetBase(
        [Description("Base item name (partial OK).")] string name)
    {
        try { return await IpcClient.CallAsync("POST", "/items/editor-set-base", new { name }); }
        catch (Exception ex) { return Error(ex); }
    }

    [McpServerTool]
    [Description("Set the unique item to edit (partial name match). Switches rarity to Unique automatically.")]
    public async Task<string> VisualItemsEditorSetUnique(
        [Description("Unique name (partial OK).")] string name)
    {
        try { return await IpcClient.CallAsync("POST", "/items/editor-set-unique", new { name }); }
        catch (Exception ex) { return Error(ex); }
    }

    [McpServerTool]
    [Description("Set item Quality (0-30%).")]
    public async Task<string> VisualItemsEditorSetQuality(
        [Description("Quality percentage 0-30.")] int quality)
    {
        try { return await IpcClient.CallAsync("POST", "/items/editor-set-quality", new { quality }); }
        catch (Exception ex) { return Error(ex); }
    }

    [McpServerTool]
    [Description("Toggle the Corrupted flag on the item. When true, mod editing is locked.")]
    public async Task<string> VisualItemsEditorSetCorrupted(
        [Description("true to mark corrupted, false to clear.")] bool value)
    {
        try { return await IpcClient.CallAsync("POST", "/items/editor-set-corrupted", new { value }); }
        catch (Exception ex) { return Error(ex); }
    }

    [McpServerTool]
    [Description("Override a base defence stat. Stat names: armour, evasion, energyShield (or 'es'), ward, spirit, charmSlots. Setting to the intrinsic default removes the override.")]
    public async Task<string> VisualItemsEditorSetBaseStat(
        [Description("Stat name (see description).")] string stat,
        [Description("New override value.")] int value)
    {
        try { return await IpcClient.CallAsync("POST", "/items/editor-set-base-stat", new { stat, value }); }
        catch (Exception ex) { return Error(ex); }
    }

    [McpServerTool]
    [Description("Set the slider value for an explicit mod by its index (use editor state to discover index).")]
    public async Task<string> VisualItemsEditorSetModSlider(
        [Description("Zero-based index into the explicits list.")] int index,
        [Description("New slider value (will be clamped to [sliderMin, sliderMax]).")] double value)
    {
        try { return await IpcClient.CallAsync("POST", "/items/editor-set-mod-slider", new { index, value }); }
        catch (Exception ex) { return Error(ex); }
    }

    [McpServerTool]
    [Description("Add an affix to the item by partial query (matches affixName or statText). Non-unique only — respects prefix/suffix caps and group dedup.")]
    public async Task<string> VisualItemsEditorAddAffix(
        [Description("Search query against affix name or stat text.")] string query)
    {
        try { return await IpcClient.CallAsync("POST", "/items/editor-add-affix", new { query }); }
        catch (Exception ex) { return Error(ex); }
    }

    [McpServerTool]
    [Description("Remove a user-removable mod row by index. Implicits and unique-range mods are locked.")]
    public async Task<string> VisualItemsEditorRemoveMod(
        [Description("Zero-based index into the explicits list.")] int index)
    {
        try { return await IpcClient.CallAsync("POST", "/items/editor-remove-mod", new { index }); }
        catch (Exception ex) { return Error(ex); }
    }

    [McpServerTool]
    [Description("Allocate one more rune/idol socket on the item (up to base's socket limit; disabled when corrupted).")]
    public async Task<string> VisualItemsEditorAddRuneSocket()
    {
        try { return await IpcClient.CallAsync("POST", "/items/editor-add-rune-socket"); }
        catch (Exception ex) { return Error(ex); }
    }

    [McpServerTool]
    [Description("Insert a rune into a socket by 1-based index. Rune name is matched against CompatibleRunes (partial OK). Pass empty/None to clear.")]
    public async Task<string> VisualItemsEditorSetRune(
        [Description("1-based socket index.")] int socket,
        [Description("Rune name (partial OK) or 'None' to clear.")] string rune)
    {
        try { return await IpcClient.CallAsync("POST", "/items/editor-set-rune", new { socket, rune }); }
        catch (Exception ex) { return Error(ex); }
    }

    [McpServerTool]
    [Description("Remove a rune socket entirely by 1-based index.")]
    public async Task<string> VisualItemsEditorRemoveRuneSocket(
        [Description("1-based socket index.")] int socket)
    {
        try { return await IpcClient.CallAsync("POST", "/items/editor-remove-rune-socket", new { socket }); }
        catch (Exception ex) { return Error(ex); }
    }

    // ── Tree tab (class / ascendancy switching) ──────────────────────────

    [McpServerTool]
    [Description(
        "Get full state of the Tree tab: selected class, selected ascendancy, " +
        "list of all available classes with their ascendancies, allocated node count, search text.")]
    public async Task<string> VisualTreeState()
    {
        try { return await IpcClient.CallAsync("GET", "/tree/state"); }
        catch (Exception ex) { return Error(ex); }
    }

    [McpServerTool]
    [Description(
        "Switch the character class on the Tree tab (partial name match). " +
        "In IPC mode the confirm-dialog is auto-bypassed even if nodes are allocated. " +
        "Available classes: Witch, Ranger, Warrior, Sorceress, Huntress, Mercenary, Monk, Druid.")]
    public async Task<string> VisualTreeSelectClass(
        [Description("Class name (partial match).")] string name)
    {
        try { return await IpcClient.CallAsync("POST", "/tree/select-class", new { name }); }
        catch (Exception ex) { return Error(ex); }
    }

    [McpServerTool]
    [Description(
        "Switch the ascendancy on the Tree tab (partial name match). " +
        "Class must already be selected. Examples: Titan, Warbringer (Warrior); Stormweaver, Chronomancer (Sorceress).")]
    public async Task<string> VisualTreeSelectAscendancy(
        [Description("Ascendancy name (partial match).")] string name)
    {
        try { return await IpcClient.CallAsync("POST", "/tree/select-ascendancy", new { name }); }
        catch (Exception ex) { return Error(ex); }
    }

    // ── Tree view control (zoom / pan / focus a node for screenshots) ────────

    [McpServerTool]
    [Description(
        "Read the passive tree viewport: current zoom 'scale' and the world coords " +
        "(centerX, centerY) under the viewport centre. Use before set-view to capture, " +
        "tweak, then restore a framing.")]
    public async Task<string> VisualTreeGetView()
    {
        try { return await IpcClient.CallAsync("GET", "/tree/view"); }
        catch (Exception ex) { return Error(ex); }
    }

    [McpServerTool]
    [Description(
        "Set the passive tree viewport directly. Any argument left null keeps its current " +
        "value. 'scale' is pixels-per-world-unit (~0.12 fits the whole tree; ~0.6-1.0 is a " +
        "close-up); centerX/centerY are world coords to place under the viewport centre. " +
        "Returns the resulting view.")]
    public async Task<string> VisualTreeSetView(
        [Description("Zoom (pixels per world unit), e.g. 0.6 for a close-up. Null = keep.")] double? scale = null,
        [Description("World X to centre on. Null = keep.")] double? centerX = null,
        [Description("World Y to centre on. Null = keep.")] double? centerY = null)
    {
        try { return await IpcClient.CallAsync("POST", "/tree/set-view", new { scale, centerX, centerY }); }
        catch (Exception ex) { return Error(ex); }
    }

    [McpServerTool]
    [Description(
        "Centre the passive tree on a node (by numeric node id) and optionally zoom in, " +
        "so a screenshot frames that node and its surroundings. Pass scale ~0.6-1.0 for a " +
        "readable close-up. Ideal for inspecting a jewel socket and its radius ring. " +
        "Returns the resulting view, or an error if the node id is absent.")]
    public async Task<string> VisualTreeFocusNode(
        [Description("Numeric passive-tree node id to centre on.")] int nodeId,
        [Description("Zoom (pixels per world unit) to apply, e.g. 0.6. Null = keep current zoom.")] double? scale = null)
    {
        try { return await IpcClient.CallAsync("POST", "/tree/focus-node", new { nodeId, scale }); }
        catch (Exception ex) { return Error(ex); }
    }

    [McpServerTool]
    [Description(
        "Zoom the passive tree by a multiplicative factor around the viewport centre " +
        "(1.15 = one wheel notch in, 0.87 = one notch out). Returns the resulting view.")]
    public async Task<string> VisualTreeZoom(
        [Description("Zoom factor: >1 zooms in, <1 zooms out.")] double factor)
    {
        try { return await IpcClient.CallAsync("POST", "/tree/zoom", new { factor }); }
        catch (Exception ex) { return Error(ex); }
    }

    [McpServerTool]
    [Description(
        "Scroll (pan) the passive tree by a screen-pixel delta. Positive dx moves the tree " +
        "content right, positive dy moves it down. Returns the resulting view.")]
    public async Task<string> VisualTreePan(
        [Description("Horizontal scroll in pixels.")] double dx,
        [Description("Vertical scroll in pixels.")] double dy)
    {
        try { return await IpcClient.CallAsync("POST", "/tree/pan", new { dx, dy }); }
        catch (Exception ex) { return Error(ex); }
    }

    [McpServerTool]
    [Description(
        "Open the in-tree jewel picker for an allocated jewel-socket node (as if the user " +
        "clicked it), so a screenshot shows the dropdown of socketable jewels (pool + already " +
        "socketed). Returns the option list. The node must be an allocated Socket.")]
    public async Task<string> VisualTreeOpenJewelPicker(
        [Description("Numeric jewel-socket node id.")] int nodeId)
    {
        try { return await IpcClient.CallAsync("POST", "/tree/open-jewel-picker", new { nodeId }); }
        catch (Exception ex) { return Error(ex); }
    }

    [McpServerTool]
    [Description(
        "Choose a jewel in the currently-open in-tree jewel picker by item id (0 = empty the " +
        "socket). If that jewel is in another socket the two swap. Call " +
        "visual_tree_open_jewel_picker first. Mirrors the user clicking a row in the picker.")]
    public async Task<string> VisualTreePickJewel(
        [Description("Pool/socketed jewel item id, or 0 to empty the socket.")] int itemId)
    {
        try { return await IpcClient.CallAsync("POST", "/tree/pick-jewel", new { itemId }); }
        catch (Exception ex) { return Error(ex); }
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "src"))
                && File.Exists(Path.Combine(dir.FullName, "PBLApp", "PBLApp.csproj")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Cannot find repo root");
    }

    [McpServerTool]
    [Description(
        "Get trader window state: target slot, league, login status, search status and " +
        "results (price, seller, stat diffs). Poll this after visual_trader_search.")]
    public async Task<string> VisualTraderState()
    {
        try { return await IpcClient.CallAsync("GET", "/trader/state"); }
        catch (Exception ex) { return Error(ex); }
    }

    [McpServerTool]
    [Description(
        "Start an upgrade search in the currently open trader window. Open a window first " +
        "with visual_trader_open. Runs async — poll visual_trader_state for stage/results.")]
    public async Task<string> VisualTraderSearch(string slot)
    {
        try { return await IpcClient.CallAsync("POST", "/trader/search", new { slot }); }
        catch (Exception ex) { return Error(ex); }
    }

    [McpServerTool]
    [Description("Select the trade league (shared by the trader window).")]
    public async Task<string> VisualTraderSetLeague(string league)
    {
        try { return await IpcClient.CallAsync("POST", "/trader/set-league", new { league }); }
        catch (Exception ex) { return Error(ex); }
    }

    private static string Error(string msg)      => $"ERROR: {msg}";
    private static string Error(Exception ex)    => $"ERROR: {ex.GetType().Name}: {ex.Message}";
}
