using ModelContextProtocol.Server;
using PBLApp.Core.Localization;
using PBLApp.ViewModels;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PBLMcp;

/// <summary>
/// MCP tools for headless localization testing of PBLApp.
/// Lets an agent change language, switch between BuildPage tabs and dump the actual
/// translated strings visible in each tab — including item slots, tree class/asc
/// selectors, calc stat labels, config sections, skill names, etc.
/// </summary>
[McpServerToolType]
public class LocTools(AppDriver driver)
{
    private static readonly JsonSerializerOptions Indent = new() { WriteIndented = true };

    // ── Language ───────────────────────────────────────────────────────────

    [McpServerTool]
    [Description("List all UI languages supported by the app. Returns codes (e.g. 'en','ru').")]
    public string LocListLanguages()
        => JsonSerializer.Serialize(new[] { "en", "ru" }, Indent);

    [McpServerTool]
    [Description("Return the currently active UI language code (e.g. 'en' or 'ru').")]
    public string LocGetCurrentLanguage()
        => LocalizationService.Instance.CurrentLanguage;

    [McpServerTool]
    [Description(
        "Switch the UI language. Triggers LanguageChanged event so all bound strings, " +
        "translated gem/item/passive names, config section names refresh in place. " +
        "Pass 'en' or 'ru' (or any other code with a matching Strings.<code>.resx).")]
    public string LocSetLanguage(
        [Description("Two-letter language code: 'en' or 'ru'.")] string lang)
    {
        try
        {
            LocalizationService.Instance.SetLanguage(lang);
            // Force ItemsTab to re-translate item names by triggering a refresh
            if (driver.App.CurrentPage is BuildPageViewModel bp && bp.ItemsTab is { } items)
                items.Refresh();
            return $"Language switched to '{LocalizationService.Instance.CurrentLanguage}'.";
        }
        catch (Exception ex) { return Error(ex); }
    }

    [McpServerTool]
    [Description(
        "Look up a UI string by its resource key in the currently active language. " +
        "Returns '[Key]' marker if the key is missing. Useful for verifying that a key " +
        "exists in the chosen language's .resx file.")]
    public string LocGetString(
        [Description("Resource key, e.g. 'Tab_Items', 'Btn_Save', 'Tree_Class'.")] string key)
    {
        try { return LocalizationService.Get(key); }
        catch (Exception ex) { return Error(ex); }
    }

    [McpServerTool]
    [Description(
        "Bulk lookup: comma-separated list of UI resource keys, returns a JSON object " +
        "mapping key → translated string in the current language.")]
    public string LocGetStrings(
        [Description("Comma-separated resource keys, e.g. 'Tab_Items,Tab_Tree,Btn_Save'.")] string keys)
    {
        try
        {
            var result = new Dictionary<string, string>();
            foreach (var k in keys.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                result[k] = LocalizationService.Get(k);
            return JsonSerializer.Serialize(result, Indent);
        }
        catch (Exception ex) { return Error(ex); }
    }

    // ── Tab navigation ─────────────────────────────────────────────────────

    [McpServerTool]
    [Description(
        "Switch the active tab on the BuildPage. " +
        "Accepts a tab name (case-insensitive: Items/Tree/Skills/Calcs/Notes/Config/ImportExport) " +
        "or a 0-based index. Build must be loaded.")]
    public string AppSelectTab(
        [Description("Tab name or 0-based index.")] string nameOrIndex)
    {
        try
        {
            if (driver.App.CurrentPage is not BuildPageViewModel bp)
                return Error("Not on BuildPage. Open a build first.");

            int idx;
            if (int.TryParse(nameOrIndex, out var parsed))
            {
                idx = parsed;
            }
            else
            {
                idx = Array.FindIndex(BuildPageViewModel.TabKeys,
                    k => k.Equals(nameOrIndex, StringComparison.OrdinalIgnoreCase));
                if (idx < 0)
                    return Error($"Unknown tab '{nameOrIndex}'. Available: {string.Join(", ", BuildPageViewModel.TabKeys)}.");
            }
            if (idx < 0 || idx >= BuildPageViewModel.TabKeys.Length)
                return Error($"Index {idx} out of range (0..{BuildPageViewModel.TabKeys.Length - 1}).");

            bp.SelectedTabIndex = idx;
            return JsonSerializer.Serialize(new
            {
                index = idx,
                key   = BuildPageViewModel.TabKeys[idx],
                header = LocalizationService.Get("Tab_" + BuildPageViewModel.TabKeys[idx]),
            }, Indent);
        }
        catch (Exception ex) { return Error(ex); }
    }

    [McpServerTool]
    [Description(
        "Return the currently active tab on the BuildPage: index, key, and translated header.")]
    public string AppGetCurrentTab()
    {
        try
        {
            if (driver.App.CurrentPage is not BuildPageViewModel bp)
                return Error("Not on BuildPage.");
            var idx = bp.SelectedTabIndex;
            return JsonSerializer.Serialize(new
            {
                index = idx,
                key   = bp.SelectedTabKey,
                header = string.IsNullOrEmpty(bp.SelectedTabKey)
                    ? "" : LocalizationService.Get("Tab_" + bp.SelectedTabKey),
                allTabHeaders = BuildPageViewModel.TabKeys
                    .Select(k => new { key = k, header = LocalizationService.Get("Tab_" + k) })
                    .ToArray(),
            }, Indent);
        }
        catch (Exception ex) { return Error(ex); }
    }

    // ── Generic text dump ──────────────────────────────────────────────────

    [McpServerTool]
    [Description(
        "Dump all visible/translated text on a BuildPage tab, in the current language. " +
        "Pass tab name (Items/Tree/Skills/Calcs/Notes/Config/ImportExport) or leave empty " +
        "to dump the currently active tab. Use this to verify a translation visually.")]
    public string AppDumpTabText(
        [Description("Tab name. Empty = current tab.")] string tab = "")
    {
        try
        {
            if (driver.App.CurrentPage is not BuildPageViewModel bp)
                return Error("Not on BuildPage.");

            var key = string.IsNullOrWhiteSpace(tab) ? bp.SelectedTabKey : tab;
            return key.ToLowerInvariant() switch
            {
                "items"       => DumpItemsTab(bp.ItemsTab),
                "tree"        => DumpTreeTab(bp.TreeTab),
                "skills"      => DumpSkillsTab(bp.SkillsTab),
                "calcs"       => DumpCalcsTab(bp.CalcsTab),
                "notes"       => DumpNotesTab(bp.NotesTab),
                "config"      => DumpConfigTab(bp.ConfigTab),
                "importexport" => DumpImportTab(bp.ImportTab),
                _ => Error($"Unknown tab key '{key}'.")
            };
        }
        catch (Exception ex) { return Error(ex); }
    }

    [McpServerTool]
    [Description(
        "Dump translated text of the BuildList page (page title, build names, language label, button captions).")]
    public string AppDumpBuildListText()
    {
        try
        {
            if (driver.App.CurrentPage is not BuildListViewModel bl)
                return Error("Not on BuildList page. Call app_reset first.");

            return JsonSerializer.Serialize(new
            {
                title        = LocalizationService.Get("List_Title"),
                language     = LocalizationService.Get("List_Language"),
                btnNew       = LocalizationService.Get("Btn_NewBuild"),
                btnImport    = LocalizationService.Get("Btn_Import"),
                btnRefresh   = LocalizationService.Get("Btn_Refresh"),
                btnOpen      = LocalizationService.Get("Btn_OpenBuild"),
                btnDelete    = LocalizationService.Get("Btn_Delete"),
                emptyHint    = LocalizationService.Get("List_Empty"),
                builds       = bl.Builds.Select(b => new
                {
                    name     = b.Name,
                    isFolder = b.IsFolder,
                }).ToArray(),
            }, Indent);
        }
        catch (Exception ex) { return Error(ex); }
    }

    // ── Items tab ──────────────────────────────────────────────────────────

    private static string DumpItemsTab(ItemsTabViewModel? vm)
    {
        if (vm is null) return Error("ItemsTab not ready.");

        ItemSlotViewModel[] slots = [
            vm.Helmet, vm.BodyArmour, vm.Weapon1, vm.Weapon2, vm.Weapon1Swap, vm.Weapon2Swap,
            vm.Ring1, vm.Ring2, vm.Ring3, vm.Gloves, vm.Boots, vm.Belt, vm.Amulet,
            vm.Flask1, vm.Flask2, vm.Charm1, vm.Charm2, vm.Charm3,
            vm.Arm1, vm.Arm2, vm.Leg1, vm.Leg2 ];

        return JsonSerializer.Serialize(new
        {
            selectedSlot = vm.SelectedSlotName,
            slots = slots.Select(s => new
            {
                slot               = s.SlotName,
                isEmpty            = s.IsEmpty,
                displayName        = s.DisplayName,
                translatedDisplayName = s.TranslatedDisplayName,
                translatedBaseName  = s.TranslatedBaseName,
                rarity             = s.Rarity,
                itemLevel          = s.ItemLevel,
                implicits          = s.Implicits,
                explicits          = s.Explicits,
                enchants           = s.Enchants,
            }).ToArray(),
            jewelSlots = vm.JewelSlots.Select(j => new
            {
                slot        = j.SlotName,
                nodeId      = j.NodeId,
                nodeName    = j.NodeName,
                isEmpty     = j.IsEmpty,
                displayName = j.DisplayName,
            }).ToArray(),
            pool = vm.ItemPool.Select(p => new
            {
                id           = p.ItemId,
                name         = p.Name,
                baseName     = p.BaseName,
                rarity       = p.Rarity,
                equippedSlot = p.EquippedSlot,
            }).ToArray(),
            editor = vm.ItemEditor is { } ed ? new
            {
                title          = ed.EditorTitle,
                isEditingExisting = ed.IsEditingExisting,
                rarity         = ed.Rarity.ToString(),
                baseName       = ed.SelectedBase?.Name,
                uniqueName     = ed.SelectedUnique?.Name,
                itemLevel      = ed.ItemLevel,
                prefixCount    = ed.PrefixCount,
                suffixCount    = ed.SuffixCount,
                implicits      = ed.ImplicitMods.ToArray(),
                explicits      = ed.ExplicitMods.Select(m => new
                {
                    text       = m.Text,
                    affixType  = m.AffixType,
                    isImplicit = m.IsImplicit,
                    hasSlider  = m.HasSlider,
                    sliderMin  = m.SliderMin,
                    sliderMax  = m.SliderMax,
                    sliderValue = m.SliderValue,
                }).ToArray(),
            } : null,
        }, Indent);
    }

    [McpServerTool]
    [Description(
        "Select an equipment slot in the Items tab. Slot name examples: Helmet, Body Armour, " +
        "Weapon 1, Weapon 2, Ring 1, Ring 2, Gloves, Boots, Belt, Amulet, Flask 1, Flask 2, " +
        "Charm 1, or a jewel-socket slot name like 'Jewel 12345'. Auto-opens the item editor " +
        "if the slot is occupied.")]
    public string AppItemsSelectSlot(
        [Description("Slot name (case-insensitive, must match exactly except for case).")] string slotName)
    {
        try
        {
            if (driver.App.CurrentPage is not BuildPageViewModel bp || bp.ItemsTab is not { } items)
                return Error("Not on BuildPage with ItemsTab ready.");

            ItemSlotViewModel[] all = [
                items.Helmet, items.BodyArmour, items.Weapon1, items.Weapon2,
                items.Weapon1Swap, items.Weapon2Swap,
                items.Ring1, items.Ring2, items.Ring3, items.Gloves, items.Boots,
                items.Belt, items.Amulet, items.Flask1, items.Flask2,
                items.Charm1, items.Charm2, items.Charm3,
                items.Arm1, items.Arm2, items.Leg1, items.Leg2 ];

            var match = all.FirstOrDefault(s => s.SlotName.Equals(slotName, StringComparison.OrdinalIgnoreCase));
            if (match is null)
            {
                // Try jewel sockets
                var jewel = items.JewelSlots.FirstOrDefault(j => j.SlotName.Equals(slotName, StringComparison.OrdinalIgnoreCase));
                if (jewel is null)
                    return Error($"Slot '{slotName}' not found. Use app_dump_tab_text Items to list available slots.");
                items.SelectSlot(jewel.SlotName);
                return $"Selected jewel socket: {jewel.SlotName} ({(jewel.IsEmpty ? "empty" : jewel.DisplayName)})";
            }
            items.SelectSlot(match.SlotName);
            return $"Selected slot: {match.SlotName} ({(match.IsEmpty ? "empty" : match.DisplayName)})";
        }
        catch (Exception ex) { return Error(ex); }
    }

    [McpServerTool]
    [Description("Toggle weapon set on the Items tab. Pass 1 for primary, 2 for swap.")]
    public string AppItemsSelectWeaponSet(
        [Description("Weapon set: 1 (primary) or 2 (swap).")] int setNumber)
    {
        try
        {
            if (driver.App.CurrentPage is not BuildPageViewModel bp || bp.ItemsTab is not { } items)
                return Error("Not on BuildPage with ItemsTab ready.");
            if (setNumber == 1) items.SelectWeaponSet1Command.Execute(null);
            else if (setNumber == 2) items.SelectWeaponSet2Command.Execute(null);
            else return Error("setNumber must be 1 or 2.");
            return $"Weapon set {(items.IsWeaponSwapActive ? 2 : 1)} active.";
        }
        catch (Exception ex) { return Error(ex); }
    }

    // ── Tree tab ───────────────────────────────────────────────────────────

    private static string DumpTreeTab(TreeTabViewModel? vm)
    {
        if (vm is null) return Error("TreeTab not ready.");

        return JsonSerializer.Serialize(new
        {
            labels = new
            {
                @class = LocalizationService.Get("Tree_Class"),
                asc    = LocalizationService.Get("Tree_Asc"),
                search = LocalizationService.Get("Tree_Search"),
                searchPh = LocalizationService.Get("Tree_PhNodeName"),
                hint     = LocalizationService.Get("Tree_Hint"),
            },
            selectedClass = vm.SelectedClass?.Name,
            selectedAscendancy = vm.SelectedAscend?.Name,
            classes = vm.Classes.Select(c => new
            {
                id   = c.Id,
                name = c.Name,
                ascendancies = c.Ascendancies.Select(a => new { a.Id, a.Name }).ToArray(),
            }).ToArray(),
            availableAscendancies = vm.AvailableAscendancies.Select(a => new { a.Id, a.Name }).ToArray(),
            searchText = vm.SearchText,
            nodeCount  = vm.NodeCount,
            allocatedCount = vm.AllocatedCount,
        }, Indent);
    }

    [McpServerTool]
    [Description(
        "Select a character class on the Tree tab by name (partial, case-insensitive match). " +
        "Triggers ascendancy refresh. Note: changing class on a build with allocated nodes " +
        "normally prompts a confirm dialog; in headless mode we bypass the dialog.")]
    public string AppTreeSelectClass(
        [Description("Class name (partial match), e.g. 'Warrior', 'Witch'.")] string className)
    {
        try
        {
            if (driver.App.CurrentPage is not BuildPageViewModel bp || bp.TreeTab is not { } tree)
                return Error("Not on BuildPage with TreeTab ready.");

            var match = tree.Classes.FirstOrDefault(c =>
                c.Name.Equals(className, StringComparison.OrdinalIgnoreCase) ||
                c.Name.Contains(className, StringComparison.OrdinalIgnoreCase));
            if (match is null)
                return Error($"Class '{className}' not found. Available: {string.Join(", ", tree.Classes.Select(c => c.Name))}.");

            // Bypass confirm dialog: ConfirmClassChange returns true immediately
            tree.ConfirmClassChange = () => Task.FromResult(true);
            tree.SelectedClass = match;
            return $"Selected class: {match.Name}.";
        }
        catch (Exception ex) { return Error(ex); }
    }

    [McpServerTool]
    [Description(
        "Select an ascendancy on the Tree tab by name (partial, case-insensitive). " +
        "Class must already be selected.")]
    public string AppTreeSelectAscendancy(
        [Description("Ascendancy name (partial match), e.g. 'Titan', 'Warbringer'.")] string ascName)
    {
        try
        {
            if (driver.App.CurrentPage is not BuildPageViewModel bp || bp.TreeTab is not { } tree)
                return Error("Not on BuildPage with TreeTab ready.");

            var match = tree.AvailableAscendancies.FirstOrDefault(a =>
                a.Name.Equals(ascName, StringComparison.OrdinalIgnoreCase) ||
                a.Name.Contains(ascName, StringComparison.OrdinalIgnoreCase));
            if (match is null)
                return Error($"Ascendancy '{ascName}' not found. Available: {string.Join(", ", tree.AvailableAscendancies.Select(a => a.Name))}.");

            tree.SelectedAscend = match;
            return $"Selected ascendancy: {match.Name}.";
        }
        catch (Exception ex) { return Error(ex); }
    }

    [McpServerTool]
    [Description("Set the search text on the Tree tab (used to filter passive tree nodes by name).")]
    public string AppTreeSetSearch(
        [Description("Substring to search for in node names. Empty string clears the search.")] string text)
    {
        try
        {
            if (driver.App.CurrentPage is not BuildPageViewModel bp || bp.TreeTab is not { } tree)
                return Error("Not on BuildPage with TreeTab ready.");
            tree.SearchText = text ?? "";
            return $"Tree search text set to: '{tree.SearchText}'.";
        }
        catch (Exception ex) { return Error(ex); }
    }

    [McpServerTool]
    [Description(
        "Get translated names of all allocated passive tree nodes (in current language). " +
        "Useful for verifying passive node translations.")]
    public string AppTreeGetAllocated()
    {
        try
        {
            if (driver.App.CurrentPage is not BuildPageViewModel bp || bp.TreeTab is not { } tree)
                return Error("Not on BuildPage with TreeTab ready.");
            var allocated = tree.AllocatedIds;
            var rows = tree.Nodes
                .Where(n => allocated.Contains(n.Id))
                .Select(n => new
                {
                    id      = n.Id,
                    name    = n.Name,
                    translatedName = GameTranslationService.TPassiveName(n.Name),
                    type    = n.Type,
                    isAttribute = n.IsAttribute,
                })
                .ToArray();
            return JsonSerializer.Serialize(rows, Indent);
        }
        catch (Exception ex) { return Error(ex); }
    }

    // ── Skills tab ─────────────────────────────────────────────────────────

    private static string DumpSkillsTab(SkillsTabViewModel? vm)
    {
        if (vm is null) return Error("SkillsTab not ready.");
        return JsonSerializer.Serialize(new
        {
            labels = new
            {
                header       = LocalizationService.Get("Skill_Header"),
                activeSkill  = LocalizationService.Get("Skill_ActiveSkill"),
                noGroups     = LocalizationService.Get("Skill_NoGroups"),
                noSelection  = LocalizationService.Get("Skill_NoSelection"),
                main         = LocalizationService.Get("Skill_Main"),
                colSupport   = LocalizationService.Get("Skill_ColSupport"),
                colLevel     = LocalizationService.Get("Skill_ColLevel"),
                colQuality   = LocalizationService.Get("Skill_ColQuality"),
                colOn        = LocalizationService.Get("Skill_ColOn"),
            },
            groups = vm.Groups.Select(g => new
            {
                index = g.Index,
                label = g.ActiveGemName,
                isMain = g.IsMain,
                isEnabled = g.IsEnabled,
                supports = g.SupportSlots.Select(gem => new
                {
                    searchText  = gem.SearchText,
                    displayText = gem.DisplayText,
                    level       = (int)gem.Level,
                    quality     = (int)gem.Quality,
                    isSupport   = gem.IsSupport,
                    isEnabled   = gem.IsEnabled,
                }).ToArray(),
            }).ToArray(),
        }, Indent);
    }

    // ── Calcs tab ──────────────────────────────────────────────────────────

    private static string DumpCalcsTab(CalcsTabViewModel? vm)
    {
        if (vm is null) return Error("CalcsTab not ready.");
        return JsonSerializer.Serialize(new
        {
            selectedSkillGroup = vm.SelectedSkillGroup?.Name,
            sections = vm.Sections.Select(sec => new
            {
                label = sec.Label,
                rows  = sec.Rows.Select(r => new
                {
                    label   = r.Label,
                    statKey = r.StatKey,
                    value   = r.Value,
                }).ToArray(),
            }).ToArray(),
        }, Indent);
    }

    // ── Notes tab ──────────────────────────────────────────────────────────

    private static string DumpNotesTab(NotesTabViewModel? vm)
    {
        if (vm is null) return Error("NotesTab not ready.");
        return JsonSerializer.Serialize(new
        {
            saveButton = LocalizationService.Get("Btn_SaveNotes"),
            notesLength = vm.Notes?.Length ?? 0,
            notesPreview = (vm.Notes ?? "").Length > 200
                ? (vm.Notes ?? "")[..200] + "…"
                : vm.Notes ?? "",
        }, Indent);
    }

    // ── Config tab ─────────────────────────────────────────────────────────

    private static string DumpConfigTab(ConfigTabViewModel? vm)
    {
        if (vm is null) return Error("ConfigTab not ready.");
        return JsonSerializer.Serialize(new
        {
            sections = vm.Sections.Select(s => new
            {
                name = s.Name,
                options = s.Options.Select(o => new
                {
                    var   = o.Var,
                    label = o.Label,
                    type  = o.Type,
                    value = o.Type switch
                    {
                        "check" => o.IsChecked.ToString().ToLowerInvariant(),
                        "list"  => o.SelectedVal,
                        _        => o.TextValue,
                    },
                }).ToArray(),
            }).ToArray(),
        }, Indent);
    }

    // ── Import tab ─────────────────────────────────────────────────────────

    private static string DumpImportTab(ImportTabViewModel? vm)
    {
        if (vm is null) return Error("ImportTab not ready.");
        return JsonSerializer.Serialize(new
        {
            btnGenerate = LocalizationService.Get("Btn_GenerateCode"),
            btnCopy     = LocalizationService.Get("Btn_CopyClipboard"),
            btnImport   = LocalizationService.Get("Btn_Import"),
        }, Indent);
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static string Error(string msg) => $"ERROR: {msg}";
    private static string Error(Exception ex) => $"ERROR: {ex.GetType().Name}: {ex.Message}";
}
