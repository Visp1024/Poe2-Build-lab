using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Documents;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PBLApp.Core;
using PBLApp.Core.Localization;
using PBLApp.ViewModels;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PBLApp.Ipc;

/// <summary>
/// HTTP-based IPC server for PBLMcp to control the running PBLApp instance.
/// Binds 127.0.0.1 on a random port, writes a discovery file with port+token.
///
/// All endpoints dispatch their work to the Avalonia UI thread so they
/// operate on the live MainWindowViewModel and trigger normal binding updates.
/// </summary>
public sealed class IpcServer
{
    private readonly HttpListener _listener = new();
    private readonly string _token;
    private readonly int _port;
    private readonly CancellationTokenSource _cts = new();

    public static IpcServer? Current { get; private set; }

    public int Port  => _port;
    public string Token => _token;

    private IpcServer(int port, string token)
    {
        _port  = port;
        _token = token;
        _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
    }

    /// <summary>Start IPC server: bind a random localhost port, write discovery file, begin accept loop.</summary>
    public static IpcServer Start()
    {
        if (Current is not null) return Current;

        // Pick a free port by binding to :0 and reading back
        var tcp = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        tcp.Start();
        var port = ((IPEndPoint)tcp.LocalEndpoint).Port;
        tcp.Stop();

        var token = GenerateToken();
        var srv = new IpcServer(port, token);
        srv._listener.Start();
        WriteDiscoveryFile(port, token);
        _ = Task.Run(srv.AcceptLoopAsync);
        Current = srv;
        return srv;
    }

    private static string GenerateToken()
    {
        var buf = new byte[16];
        RandomNumberGenerator.Fill(buf);
        return Convert.ToHexString(buf);
    }

    private static string DiscoveryPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PathOfBuilding", "mcp-ipc.json");

    private static void WriteDiscoveryFile(int port, string token)
    {
        var dir = Path.GetDirectoryName(DiscoveryPath)!;
        Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(new
        {
            port,
            token,
            pid = Environment.ProcessId,
            startedAt = DateTime.UtcNow.ToString("o"),
        });
        File.WriteAllText(DiscoveryPath, json);
    }

    private async Task AcceptLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try { ctx = await _listener.GetContextAsync(); }
            catch { break; }
            _ = Task.Run(() => HandleAsync(ctx));
        }
    }

    private async Task HandleAsync(HttpListenerContext ctx)
    {
        try
        {
            // Auth via X-PoB-Token header
            var header = ctx.Request.Headers["X-PoB-Token"];
            if (header != _token)
            {
                ctx.Response.StatusCode = 401;
                await WriteJson(ctx, new { error = "Invalid or missing X-PoB-Token." });
                return;
            }

            var path = ctx.Request.Url?.AbsolutePath ?? "";
            string body = "";
            if (ctx.Request.HasEntityBody)
                using (var sr = new StreamReader(ctx.Request.InputStream, Encoding.UTF8))
                    body = await sr.ReadToEndAsync();

            object result = path switch
            {
                "/ping"          => new { ok = true, pid = Environment.ProcessId },
                "/state"         => await OnUi(GetState),
                "/screenshot"    => await OnUi(() => TakeScreenshot(body)),
                "/set-language"  => await OnUi(() => SetLanguage(body)),
                "/select-tab"    => await OnUi(() => SelectTab(body)),
                "/list-builds"   => await OnUi(ListBuilds),
                "/open-build"    => await OnUi(() => OpenBuild(body)),
                "/go-back"       => await OnUi(GoBack),
                "/items/state"        => await OnUi(ItemsState),
                "/items/select-slot"  => await OnUi(() => ItemsSelectSlot(body)),
                "/items/select-pool"  => await OnUi(() => ItemsSelectPool(body)),
                "/items/equip"        => await OnUi(() => ItemsEquip(body)),
                "/items/unequip"      => await OnUi(() => ItemsUnequip(body)),
                "/items/get-editor"   => await OnUi(GetEditorState),
                "/items/get-tooltip"  => await OnUi(GetTooltipState),
                "/items/all-tooltips" => await OnUi(AllTooltips),
                "/items/hover-text"   => await OnUi(HoverText),
                "/items/compatible-slots" => await OnUi(() => CompatibleSlots(body)),
                "/items/tattoo-state" => await OnUi(TattooState),
                "/items/set-tattoo"   => await OnUi(() => SetTattoo(body)),
                "/items/phylactery-state" => await OnUi(PhylacteryStateInfo),
                "/items/edit-current" => await OnUi(EditCurrentItem),
                "/items/cancel-edit"  => await OnUi(CancelEdit),
                "/items/editor-save"             => await OnUi(EditorSave),
                "/items/editor-create-new"       => await OnUi(() => EditorCreateNew(body)),
                "/items/editor-set-quality"      => await OnUi(() => EditorSetQuality(body)),
                "/items/editor-set-corrupted"    => await OnUi(() => EditorSetCorrupted(body)),
                "/items/editor-set-rarity"       => await OnUi(() => EditorSetRarity(body)),
                "/items/editor-set-base"         => await OnUi(() => EditorSetBase(body)),
                "/items/editor-set-unique"       => await OnUi(() => EditorSetUnique(body)),
                "/items/editor-set-base-stat"    => await OnUi(() => EditorSetBaseStat(body)),
                "/items/editor-set-mod-slider"   => await OnUi(() => EditorSetModSlider(body)),
                "/items/editor-add-affix"        => await OnUi(() => EditorAddAffix(body)),
                "/items/editor-remove-mod"       => await OnUi(() => EditorRemoveMod(body)),
                "/items/editor-add-rune-socket"  => await OnUi(EditorAddRuneSocket),
                "/items/editor-set-rune"         => await OnUi(() => EditorSetRune(body)),
                "/items/editor-remove-rune-socket" => await OnUi(() => EditorRemoveRuneSocket(body)),
                "/tree/state"             => await OnUi(TreeState),
                "/tree/set-search"        => await OnUi(() => TreeSetSearch(body)),
                "/tree/hover-node"        => await OnUi(() => TreeHoverNode(body)),
                "/tree/node-text"         => await OnUi(() => TreeNodeText(body)),
                "/tree/toggle-node"       => await OnUiAsync(() => TreeToggleNode(body)),
                "/tree/select-class"      => await OnUi(() => TreeSelectClass(body)),
                "/tree/select-ascendancy" => await OnUi(() => TreeSelectAscendancy(body)),
                "/tree/view"              => await OnUi(TreeGetView),
                "/tree/set-view"          => await OnUi(() => TreeSetView(body)),
                "/tree/focus-node"        => await OnUi(() => TreeFocusNode(body)),
                "/tree/zoom"              => await OnUi(() => TreeZoom(body)),
                "/tree/pan"               => await OnUi(() => TreePan(body)),
                "/tree/open-jewel-picker" => await OnUi(() => TreeOpenJewelPicker(body)),
                "/tree/pick-jewel"        => await OnUi(() => TreePickJewel(body)),
                "/tree/power-build"       => await OnUiAsync(() => TreePowerBuild(body)),
                "/tree/power-report"      => await OnUi(TreePowerReport),
                "/skills/tooltips"        => await OnUi(SkillsTooltips),
                "/skills/support-picker"          => await OnUi(() => SupportPickerState(body)),
                "/skills/support-picker/set-tab"  => await OnUi(() => SupportPickerSetTab(body)),
                "/skills/support-picker/set-mode" => await OnUi(() => SupportPickerSetMode(body)),
                "/skills/support-picker/select"   => await OnUi(() => SupportPickerSelect(body)),
                "/ui/text"                => await OnUi(() => DumpUiText(body)),
                "/trader/state"      => await OnUi(TraderState),
                "/trader/open"       => await OnUi(() => TraderOpen(body)),
                "/trader/search"     => await OnUi(() => TraderSearch(body)),
                "/trader/set-league" => await OnUi(() => TraderSetLeague(body)),
                _ => new { error = $"Unknown endpoint: {path}" }
            };

            ctx.Response.StatusCode = 200;
            await WriteJson(ctx, result);
        }
        catch (Exception ex)
        {
            try
            {
                ctx.Response.StatusCode = 500;
                await WriteJson(ctx, new { error = ex.GetType().Name + ": " + ex.Message });
            }
            catch { }
        }
    }

    private static async Task WriteJson(HttpListenerContext ctx, object obj)
    {
        var json = JsonSerializer.Serialize(obj);
        var buf = Encoding.UTF8.GetBytes(json);
        ctx.Response.ContentType = "application/json; charset=utf-8";
        ctx.Response.ContentLength64 = buf.Length;
        await ctx.Response.OutputStream.WriteAsync(buf);
        ctx.Response.OutputStream.Close();
    }

    // ── UI-thread dispatch ────────────────────────────────────────────────

    private static async Task<object> OnUi(Func<object> fn)
        => await Dispatcher.UIThread.InvokeAsync(fn);

    private static async Task<object> OnUiAsync(Func<Task<object>> fn)
        => await Dispatcher.UIThread.InvokeAsync(fn);

    // ── Endpoint handlers (all run on UI thread) ──────────────────────────

    private static Window? GetMainWindow()
        => (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

    private static MainWindowViewModel? GetMainVm()
        => GetMainWindow()?.DataContext as MainWindowViewModel;

    private static object GetState()
    {
        var vm = GetMainVm();
        if (vm is null) return new { error = "MainWindowViewModel not available." };

        var lang = LocalizationService.Instance.CurrentLanguage;
        var w = GetMainWindow();
        var size = w is null ? (0, 0) : ((int)w.ClientSize.Width, (int)w.ClientSize.Height);

        return vm.CurrentPage switch
        {
            BuildListViewModel bl => new
            {
                language = lang,
                page     = "BuildList",
                buildCount = EnumerateAllBuildFiles().Count(),
                selectedBuild = (string?)null,
                window = new { width = size.Item1, height = size.Item2 },
            },
            BuildPageViewModel bp => new
            {
                language = lang,
                page     = bp.IsLoading ? "BuildPageLoading" : bp.LoadError.Length > 0 ? "BuildPageError" : "BuildPageReady",
                buildName  = bp.BuildName,
                loadError  = bp.LoadError.Length > 0 ? bp.LoadError : null,
                selectedTabIndex = bp.SelectedTabIndex,
                selectedTabKey   = bp.SelectedTabKey,
                window = new { width = size.Item1, height = size.Item2 },
            },
            _ => new { language = lang, page = vm.CurrentPage?.GetType().Name, window = new { width = size.Item1, height = size.Item2 } }
        };
    }

    private static object SetLanguage(string body)
    {
        var req = JsonSerializer.Deserialize<Dictionary<string, string>>(body) ?? new();
        if (!req.TryGetValue("lang", out var lang) || string.IsNullOrWhiteSpace(lang))
            return new { error = "Missing 'lang' field." };
        LocalizationService.Instance.SetLanguage(lang);
        // Force any open Items tab to refresh (translated item names rebuild)
        if (GetMainVm()?.CurrentPage is BuildPageViewModel bp && bp.ItemsTab is { } items)
            items.Refresh();
        return new { ok = true, language = LocalizationService.Instance.CurrentLanguage };
    }

    private static object SelectTab(string body)
    {
        var req = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(body) ?? new();
        if (GetMainVm()?.CurrentPage is not BuildPageViewModel bp)
            return new { error = "Not on BuildPage." };

        int idx;
        if (req.TryGetValue("index", out var ie) && ie.ValueKind == JsonValueKind.Number)
            idx = ie.GetInt32();
        else if (req.TryGetValue("name", out var ne) && ne.ValueKind == JsonValueKind.String)
        {
            idx = Array.FindIndex(BuildPageViewModel.TabKeys,
                k => k.Equals(ne.GetString(), StringComparison.OrdinalIgnoreCase));
            if (idx < 0) return new { error = $"Unknown tab '{ne.GetString()}'." };
        }
        else return new { error = "Provide 'name' or 'index'." };

        if (idx < 0 || idx >= BuildPageViewModel.TabKeys.Length)
            return new { error = $"Index {idx} out of range." };

        bp.SelectedTabIndex = idx;
        return new { ok = true, index = idx, key = bp.SelectedTabKey };
    }

    private static object ListBuilds()
    {
        if (GetMainVm()?.CurrentPage is not BuildListViewModel)
            return new { error = "Not on BuildList page." };
        var builds = EnumerateAllBuildFiles()
            .Select(f => new { name = System.IO.Path.GetFileNameWithoutExtension(f), isFolder = false })
            .ToArray();
        return new { builds };
    }

    /// <summary>Recursively list all *.xml build files under the Builds root.</summary>
    private static IEnumerable<string> EnumerateAllBuildFiles()
    {
        var root = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PathOfBuilding2", "Builds");
        if (!System.IO.Directory.Exists(root)) yield break;
        foreach (var f in System.IO.Directory.EnumerateFiles(root, "*.xml", System.IO.SearchOption.AllDirectories))
            yield return f;
    }

    private static object OpenBuild(string body)
    {
        var req  = JsonSerializer.Deserialize<Dictionary<string, string>>(body) ?? new();
        if (GetMainVm()?.CurrentPage is not BuildListViewModel bl)
            return new { error = "Not on BuildList page." };
        if (!req.TryGetValue("name", out var name) || string.IsNullOrWhiteSpace(name))
            return new { error = "Missing 'name'." };

        var match = EnumerateAllBuildFiles()
            .FirstOrDefault(f =>
            {
                var n = System.IO.Path.GetFileNameWithoutExtension(f);
                return n.Equals(name, StringComparison.OrdinalIgnoreCase)
                    || n.Contains(name, StringComparison.OrdinalIgnoreCase);
            });
        if (match is null) return new { error = $"Build '{name}' not found." };

        var entry = new BuildEntryViewModel(
            System.IO.Path.GetFileNameWithoutExtension(match)!, match, BuildEntryKind.Build);
        bl.OpenItemCommand.Execute(entry);
        return new { ok = true, opened = entry.Name };
    }

    private static object GoBack()
    {
        if (GetMainVm()?.CurrentPage is BuildPageViewModel bp)
        {
            bp.BackCommand.Execute(null);
            return new { ok = true };
        }
        return new { error = "Not on BuildPage." };
    }

    private static object TakeScreenshot(string body)
    {
        var req = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(body) ?? new();
        var window = GetMainWindow();
        if (window is null) return new { error = "MainWindow not available." };

        var size = window.ClientSize;
        if (size.Width <= 0 || size.Height <= 0)
            return new { error = $"Window has zero size: {size}." };

        var px = new PixelSize(Math.Max(1, (int)size.Width), Math.Max(1, (int)size.Height));
        var dpi = new Vector(96, 96);

        // Render the entire MainWindow into a bitmap.
        var rtb = new RenderTargetBitmap(px, dpi);
        rtb.Render(window);

        // Resolve output path
        string outPath;
        if (req.TryGetValue("path", out var p) && p.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(p.GetString()))
        {
            outPath = p.GetString()!;
        }
        else
        {
            var dir = Path.Combine(Path.GetTempPath(), "pob-screenshots");
            Directory.CreateDirectory(dir);
            outPath = Path.Combine(dir, $"pob_{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}.png");
        }

        rtb.Save(outPath);
        return new
        {
            ok    = true,
            path  = outPath,
            width = px.Width,
            height = px.Height,
        };
    }

    // ── Items tab handlers ─────────────────────────────────────────────────

    private static ItemsTabViewModel? GetItemsVm()
        => (GetMainVm()?.CurrentPage as BuildPageViewModel)?.ItemsTab;

    private static ItemSlotViewModel[] AllSlots(ItemsTabViewModel v) => [
        v.Helmet, v.BodyArmour, v.Weapon1, v.Weapon2, v.Weapon1Swap, v.Weapon2Swap,
        v.Ring1, v.Ring2, v.Ring3, v.Gloves, v.Boots, v.Belt, v.Amulet,
        v.Flask1, v.Flask2, v.Charm1, v.Charm2, v.Charm3,
        v.Arm1, v.Arm2, v.Leg1, v.Leg2 ];

    private static object ItemsState()
    {
        if (GetItemsVm() is not { } v) return new { error = "ItemsTab not ready." };
        return new
        {
            selectedSlot       = v.SelectedSlotName,
            selectedPoolItemId = v.SelectedPoolItemId,
            isEditorOpen       = v.IsEditorOpen,
            isTooltipMode      = v.IsTooltipMode,
            isEditMode         = v.IsEditMode,
            isRightPaneEmpty   = v.IsRightPaneEmpty,
            isWeaponSwapActive = v.IsWeaponSwapActive,
            slots = AllSlots(v).Select(s => new
            {
                slot               = s.SlotName,
                isEmpty            = s.IsEmpty,
                displayName        = s.DisplayName,
                translatedBaseName = s.TranslatedBaseName,
                rarity             = s.Rarity,
            }).ToArray(),
            jewelSlots = v.JewelSlots.Select(j => new
            {
                slot = j.SlotName, nodeId = j.NodeId, nodeName = j.NodeName,
                isEmpty = j.IsEmpty, displayName = j.DisplayName,
            }).ToArray(),
            pool = v.ItemPool.Select(p => new
            {
                id = p.ItemId, name = p.Name, baseName = p.BaseName,
                rarity = p.Rarity, equippedSlot = p.EquippedSlot,
            }).ToArray(),
        };
    }

    private static object ItemsSelectSlot(string body)
    {
        if (GetItemsVm() is not { } v) return new { error = "ItemsTab not ready." };
        var req = JsonSerializer.Deserialize<Dictionary<string, string>>(body) ?? new();
        if (!req.TryGetValue("slot", out var slot) || string.IsNullOrEmpty(slot))
            return new { error = "Missing 'slot'." };

        var all = AllSlots(v);
        var match = all.FirstOrDefault(s => s.SlotName.Equals(slot, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            var jewel = v.JewelSlots.FirstOrDefault(j => j.SlotName.Equals(slot, StringComparison.OrdinalIgnoreCase));
            if (jewel is null) return new { error = $"Slot '{slot}' not found." };
            v.SelectSlot(jewel.SlotName);
            return new { ok = true, selected = jewel.SlotName, isEmpty = jewel.IsEmpty };
        }
        v.SelectSlot(match.SlotName);
        return new { ok = true, selected = match.SlotName, isEmpty = match.IsEmpty, displayName = match.DisplayName };
    }

    private static object ItemsSelectPool(string body)
    {
        if (GetItemsVm() is not { } v) return new { error = "ItemsTab not ready." };
        var req = JsonSerializer.Deserialize<Dictionary<string, int>>(body) ?? new();
        if (!req.TryGetValue("id", out var id))
            return new { error = "Missing 'id'." };
        var entry = v.ItemPool.FirstOrDefault(e => e.ItemId == id);
        if (entry is null) return new { error = $"Pool item id={id} not found." };
        v.SelectPoolItem(id);
        return new { ok = true, name = entry.Name, baseName = entry.BaseName };
    }

    private static object ItemsEquip(string body)
    {
        if (GetItemsVm() is not { } v) return new { error = "ItemsTab not ready." };
        var req = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(body) ?? new();
        if (!req.TryGetValue("id", out var ie) || ie.ValueKind != JsonValueKind.Number)
            return new { error = "Missing 'id' (number)." };
        if (!req.TryGetValue("slot", out var se) || se.ValueKind != JsonValueKind.String)
            return new { error = "Missing 'slot' (string)." };

        v.SelectPoolItem(ie.GetInt32());
        v.SelectedEquipSlot = se.GetString() ?? "";
        if (!v.CanEquipPoolItem) return new { error = "CanEquipPoolItem=false (incompatible slot?)." };
        v.EquipPoolItemCommand.Execute(null);
        return new { ok = true };
    }

    private static object ItemsUnequip(string body)
    {
        if (GetItemsVm() is not { } v) return new { error = "ItemsTab not ready." };
        var req = JsonSerializer.Deserialize<Dictionary<string, string>>(body) ?? new();
        if (!req.TryGetValue("slot", out var slot) || string.IsNullOrEmpty(slot))
            return new { error = "Missing 'slot'." };

        var entry = v.ItemPool.FirstOrDefault(e => e.EquippedSlot.Equals(slot, StringComparison.OrdinalIgnoreCase));
        if (entry is null) return new { error = $"No item equipped in '{slot}'." };
        v.SelectPoolItem(entry.ItemId);
        v.UnequipPoolItemCommand.Execute(null);
        return new { ok = true, unequipped = entry.Name };
    }

    private static object GetEditorState()
    {
        if (GetItemsVm() is not { } v) return new { error = "ItemsTab not ready." };
        if (v.ItemEditor is not { } ed) return new { isOpen = false };
        return new
        {
            isOpen            = true,
            title             = ed.EditorTitle,
            displayName       = ed.DisplayName,
            displayBaseName   = ed.DisplayBaseName,
            isEditingExisting = ed.IsEditingExisting,
            rarity            = ed.Rarity.ToString(),
            baseName          = ed.SelectedBase?.Name,
            uniqueName        = ed.SelectedUnique?.Name,
            itemLevel         = ed.ItemLevel,
            quality           = ed.Quality,
            isCorrupted       = ed.IsCorrupted,
            canEditMods       = ed.CanEditMods,
            prefixCount       = ed.PrefixCount,
            suffixCount       = ed.SuffixCount,
            maxRunes          = ed.MaxRunes,
            runeSockets       = ed.RuneSockets.Select(r => new
            {
                index    = r.Index,
                runeName = r.SelectedRune?.Name,
                modSummary = r.ModSummary,
            }).ToArray(),
            baseStats = new
            {
                armour        = new { value = ed.BaseArmour,       @default = ed.DefaultArmour,       overridden = ed.IsArmourOverridden },
                evasion       = new { value = ed.BaseEvasion,      @default = ed.DefaultEvasion,      overridden = ed.IsEvasionOverridden },
                energyShield  = new { value = ed.BaseEnergyShield, @default = ed.DefaultEnergyShield, overridden = ed.IsEnergyShieldOverridden },
                ward          = new { value = ed.BaseWard,         @default = ed.DefaultWard,         overridden = ed.IsWardOverridden },
                spirit        = new { value = ed.BaseSpirit,       @default = ed.DefaultSpirit,       overridden = ed.IsSpiritOverridden },
                charmSlots    = new { value = ed.BaseCharmSlots,   @default = ed.DefaultCharmSlots,   overridden = ed.IsCharmSlotsOverridden },
            },
            implicits         = ed.ImplicitMods.ToArray(),
            explicits         = ed.ExplicitMods.Select((m, i) => new
            {
                index         = i,
                text          = m.Text,
                translatedText = m.TranslatedText,
                affixType     = m.AffixType,
                affixLabel    = m.AffixLabel,
                isImplicit    = m.IsImplicit,
                isRemovable   = m.IsRemovable,
                hasSlider     = m.HasSlider,
                sliderValue   = m.SliderValue,
                sliderMin     = m.SliderMin,
                sliderMax     = m.SliderMax,
            }).ToArray(),
        };
    }

    private static object GetTooltipState()
    {
        if (GetItemsVm() is not { } v) return new { error = "ItemsTab not ready." };
        if (v.ItemTooltip is not { } tt) return new { isOpen = false };
        return new
        {
            isOpen = true,
            lineCount = tt.Lines.Count,
            lines = SerializeTooltipLines(tt),
        };
    }

    private static object[] SerializeTooltipLines(PBLApp.ViewModels.ItemTooltipViewModel tt)
        => tt.Lines.Select(l => new
        {
            kind     = l.Kind,
            size     = l.Size,
            centered = l.Centered,
            block    = l.Block,
            plain    = l.PlainText,
            segments = l.Segments.Select(s => new { text = s.Text, color = s.ColorHex }).ToArray(),
        }).ToArray();

    /// <summary>Builds the display tooltip for every equipped slot and every pool item,
    /// without disturbing the current selection. Lets inspection tooling read all
    /// tooltips at once for verification.</summary>
    private static object AllTooltips()
    {
        if (GetItemsVm() is not { } v) return new { error = "ItemsTab not ready." };

        var slotTooltips = new List<object>();
        foreach (var s in AllSlots(v))
        {
            if (s.IsEmpty) continue;
            var tt = v.BuildTooltipFor(null, s.SlotName);
            if (tt is null) continue;
            slotTooltips.Add(new
            {
                slot = s.SlotName, displayName = s.DisplayName,
                lineCount = tt.Lines.Count, lines = SerializeTooltipLines(tt),
            });
        }
        foreach (var j in v.JewelSlots)
        {
            if (j.IsEmpty) continue;
            var tt = v.BuildTooltipFor(null, j.SlotName);
            if (tt is null) continue;
            slotTooltips.Add(new
            {
                slot = j.SlotName, displayName = j.DisplayName,
                lineCount = tt.Lines.Count, lines = SerializeTooltipLines(tt),
            });
        }

        var poolTooltips = new List<object>();
        foreach (var p in v.ItemPool)
        {
            var tt = v.BuildTooltipFor(p.ItemId, null);
            if (tt is null) continue;
            poolTooltips.Add(new
            {
                id = p.ItemId, name = p.Name, equippedSlot = p.EquippedSlot,
                lineCount = tt.Lines.Count, lines = SerializeTooltipLines(tt),
            });
        }

        return new { ok = true, slots = slotTooltips.ToArray(), pool = poolTooltips.ToArray() };
    }

    // Returns the plain-text hover tooltip (the StyledHoverTooltip popup shown over
    // each slot/pool cell on pointer-hover) — a path separate from the right-pane
    // ItemTooltipViewModel, so it needs its own translation audit.
    private static object HoverText()
    {
        if (GetItemsVm() is not { } v) return new { error = "ItemsTab not ready." };
        return new
        {
            ok = true,
            slots = AllSlots(v).Where(s => !s.IsEmpty)
                .Select(s => new { slot = s.SlotName, text = s.HoverTooltipText }).ToArray(),
            pool = v.ItemPool.Select(p => new { id = p.ItemId, name = p.Name, text = p.HoverTooltipText }).ToArray(),
        };
    }

    // ── Skills tooltips ────────────────────────────────────────────────────
    // Returns the EXACT text rendered in each gem's hover tooltip. The tooltip
    // is a hover-only popup, so we read the same GemTooltipEntry list the view
    // binds to (built by SkillsTabViewModel.ProcessTooltipLines) and concatenate
    // each entry's rendered segments — identical to what the View draws.

    private static SkillsTabViewModel? GetSkillsVm()
        => (GetMainVm()?.CurrentPage as BuildPageViewModel)?.SkillsTab;

    private static object SkillsTooltips()
    {
        if (GetSkillsVm() is not { } v) return new { error = "SkillsTab not ready." };

        var groups = new List<object>();
        foreach (var g in v.Groups)
        {
            var gems = new List<object>();
            void AddGem(GemViewModel? gem)
            {
                if (gem is null || gem.GemIndex <= 0) return;
                gem.RefreshTooltip();   // same code path the view uses on hover
                var lines = SerializeGemTooltip(gem.TooltipEntries);
                gems.Add(new
                {
                    name        = gem.CommittedName,
                    displayName = GameTranslationService.TGem(gem.CommittedName),
                    gemIndex    = gem.GemIndex,
                    lineCount   = lines.Length,
                    lines,
                });
            }
            AddGem(g.ActiveGem);
            foreach (var s in g.SupportSlots) AddGem(s);

            groups.Add(new
            {
                index     = g.Index,
                isMain    = g.IsMain,
                activeGem = g.ActiveGemName,
                gems      = gems.ToArray(),
            });
        }
        return new { ok = true, groups = groups.ToArray() };
    }

    // ── Support-gem picker (selector) test hooks ──────────────────────────────
    // Address a support slot by group Index + 0-based slot ordinal, so the picker's
    // attribute tabs/filter/selection can be driven and inspected without the popup.

    private static GemViewModel? ResolvePickerSlot(IDictionary<string, string> req)
    {
        if (GetSkillsVm() is not { } v) return null;
        int gi = req.TryGetValue("group", out var gs) && int.TryParse(gs, out var g) ? g : -1;
        int so = req.TryGetValue("slot",  out var ss) && int.TryParse(ss, out var s) ? s : -1;
        var grp = v.Groups.FirstOrDefault(x => x.Index == gi);
        return grp?.SupportSlots.ElementAtOrDefault(so);
    }

    private static object PickerStateOf(GemViewModel slot) => new
    {
        ok          = true,
        isTrigger   = slot.IsTriggerSlot,
        supportMode = slot.SupportMode,
        skillsMode  = slot.SkillsMode,
        tab         = slot.Tab.ToString(),
        committed   = slot.CommittedName,
        tabs = new[]
        {
            new { key = "All", have = slot.TabAllHave, max = slot.TabAllMax },
            new { key = "Str", have = slot.TabStrHave, max = slot.TabStrMax },
            new { key = "Dex", have = slot.TabDexHave, max = slot.TabDexMax },
            new { key = "Int", have = slot.TabIntHave, max = slot.TabIntMax },
            new { key = "Special", have = slot.TabSpecialHave, max = 0 },
        },
        filtered = slot.FilteredGemNames.Select(g => new
        {
            name    = g.Name,
            display = g.DisplayName,
            color   = g.Color,
            attr    = GemAttrUtil.FromColor(g.Color).ToString(),
        }).ToArray(),
    };

    private static object SupportPickerState(string body)
    {
        var req = JsonSerializer.Deserialize<Dictionary<string, string>>(body) ?? new();
        if (GetSkillsVm() is null) return new { error = "SkillsTab not ready." };
        return ResolvePickerSlot(req) is { } slot ? PickerStateOf(slot)
                                                   : new { error = "Slot not found." };
    }

    private static object SupportPickerSetTab(string body)
    {
        var req = JsonSerializer.Deserialize<Dictionary<string, string>>(body) ?? new();
        if (ResolvePickerSlot(req) is not { } slot) return new { error = "Slot not found." };
        if (req.TryGetValue("tab", out var t) && Enum.TryParse<GemTab>(t, true, out var tab))
            slot.Tab = tab;
        return PickerStateOf(slot);
    }

    private static object SupportPickerSetMode(string body)
    {
        var req = JsonSerializer.Deserialize<Dictionary<string, string>>(body) ?? new();
        if (ResolvePickerSlot(req) is not { } slot) return new { error = "Slot not found." };
        if (req.TryGetValue("support", out var s))
            slot.SupportMode = s is "true" or "1";
        return PickerStateOf(slot);
    }

    private static object SupportPickerSelect(string body)
    {
        var req = JsonSerializer.Deserialize<Dictionary<string, string>>(body) ?? new();
        if (ResolvePickerSlot(req) is not { } slot) return new { error = "Slot not found." };
        if (!req.TryGetValue("name", out var name) || string.IsNullOrWhiteSpace(name))
            return new { error = "Missing 'name'." };
        slot.SearchText = name;
        slot.CommitName();
        return new { ok = true, committed = slot.CommittedName };
    }

    private static object[] SerializeGemTooltip(System.Collections.Generic.IReadOnlyList<GemTooltipEntry>? entries)
    {
        if (entries is null) return Array.Empty<object>();
        return entries.Select(e => new
        {
            isSep = e.IsSep,
            text  = e.IsSep ? "" : RenderedTextOf(e),
        }).ToArray();
    }

    // The view renders the per-entry Segments (via InlinesHelper); concatenating
    // their text reproduces exactly what reaches the screen. Falls back to the
    // whole-line Text when no segments were built.
    private static string RenderedTextOf(GemTooltipEntry e)
        => e.Segments is { Count: > 0 } segs
            ? string.Concat(segs.Select(s => s.Text))
            : e.Text;

    // ── Generic rendered-text dump ─────────────────────────────────────────
    // Walks the live visual tree(s) and returns the actual text of every visible
    // TextBlock / TextBox — the real strings going to render (translated where the
    // app translates them), for any on-screen field. Note: tree-canvas node labels
    // are immediate-mode drawn (no TextBlock) and are NOT included here — use
    // /tree/hover-node or /skills/tooltips for those.
    private static object DumpUiText(string body)
    {
        var win = GetMainWindow();
        if (win is null) return new { error = "No main window." };

        var items = new List<object>();
        CollectText(win, items, "MainWindow");

        // Any other open windows (modal dialogs: confirm / choose-attribute, etc.).
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desk)
            foreach (var w in desk.Windows)
                if (!ReferenceEquals(w, win))
                    CollectText(w, items, w.Title ?? w.GetType().Name);

        return new { ok = true, count = items.Count, items = items.ToArray() };
    }

    private static void CollectText(Visual root, List<object> items, string scope)
    {
        foreach (var v in root.GetVisualDescendants())
        {
            if (v is not Control c || !c.IsEffectivelyVisible) continue;

            string? text = v switch
            {
                TextBlock tb => InlineOrText(tb),
                TextBox  txt => txt.Text,
                _            => null,
            };
            if (string.IsNullOrWhiteSpace(text)) continue;

            items.Add(new { scope, type = v.GetType().Name, name = c.Name, text });
        }
    }

    private static string? InlineOrText(TextBlock tb)
    {
        if (tb.Inlines is { Count: > 0 } inlines)
        {
            var sb = new StringBuilder();
            AppendInlines(sb, inlines);
            if (sb.Length > 0) return sb.ToString();
        }
        return tb.Text;
    }

    private static void AppendInlines(StringBuilder sb, InlineCollection inlines)
    {
        foreach (var inl in inlines)
        {
            switch (inl)
            {
                case Run r:        sb.Append(r.Text); break;
                case LineBreak:    sb.Append('\n');   break;
                case Span sp:      AppendInlines(sb, sp.Inlines); break;
            }
        }
    }

    /// <summary>Reports the equip-compatible (drag-highlight) target slots PoB would
    /// accept for an item — honouring keystone/ascendancy flags such as Instruments of
    /// Power (Focus into Weapon 2 with a Staff) and Giant's Blood. Body: optional
    /// {"id": poolItemId} or {"slot": equippedSlotName}; with neither, returns the map
    /// for every pool item.</summary>
    private static object TattooState()
    {
        if (GetItemsVm() is not { } v) return new { error = "ItemsTab not ready." };
        var t = v.Tattoos;
        return new
        {
            available = t.Available,
            sockets = t.Sockets.Select(s => new
            {
                index    = s.Index,
                slotType = s.SlotType,
                label    = s.SlotTypeLabel,
                rune     = s.SelectedRune?.Name ?? "",
                options  = s.AvailableRunes.Count,
            }).ToArray(),
        };
    }

    private static object SetTattoo(string body)
    {
        if (GetItemsVm() is not { } v) return new { error = "ItemsTab not ready." };
        var req = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(body) ?? new();
        if (!req.TryGetValue("index", out var ie) || ie.ValueKind != JsonValueKind.Number)
            return new { error = "Missing 'index' (1-based number)." };
        var rune = req.TryGetValue("rune", out var re) && re.ValueKind == JsonValueKind.String
            ? re.GetString() ?? "" : "";
        if (!v.Tattoos.Available) return new { error = "Tattoos not available (Runic Meridians not allocated)." };
        v.Tattoos.SetSocketRune(ie.GetInt32(), rune);
        return new { ok = true };
    }

    private static object PhylacteryStateInfo()
    {
        if (GetItemsVm() is not { } v) return new { error = "ItemsTab not ready." };
        var p = v.Phylactery;
        return new { available = p.Available, jewel = p.JewelName };
    }

    private static object CompatibleSlots(string body)
    {
        if (GetItemsVm() is not { } v) return new { error = "ItemsTab not ready." };
        var req = string.IsNullOrWhiteSpace(body)
            ? new Dictionary<string, JsonElement>()
            : JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(body) ?? new();

        if (req.TryGetValue("id", out var ie) && ie.ValueKind == JsonValueKind.Number)
        {
            int id = ie.GetInt32();
            var entry = v.ItemPool.FirstOrDefault(e => e.ItemId == id);
            if (entry is null) return new { error = $"Pool item id={id} not found." };
            return new { ok = true, id, name = entry.Name, equippedSlot = entry.EquippedSlot,
                         slots = v.CompatibleSlotsForPool(id).OrderBy(s => s).ToArray() };
        }
        if (req.TryGetValue("slot", out var se) && se.ValueKind == JsonValueKind.String)
        {
            var slot = se.GetString() ?? "";
            return new { ok = true, slot,
                         slots = v.CompatibleSlotsForSlot(slot).OrderBy(s => s).ToArray() };
        }

        var all = v.ItemPool.Select(p => new
        {
            id = p.ItemId, name = p.Name, equippedSlot = p.EquippedSlot,
            slots = v.CompatibleSlotsForPool(p.ItemId).OrderBy(s => s).ToArray(),
        }).ToArray();
        return new { ok = true, pool = all };
    }

    private static object EditCurrentItem()
    {
        if (GetItemsVm() is not { } v) return new { error = "ItemsTab not ready." };
        if (!v.EditCurrentItemCommand.CanExecute(null))
            return new { error = "EditCurrentItemCommand not executable (no item selected?)." };
        v.EditCurrentItemCommand.Execute(null);
        return new { ok = true, isEditMode = v.IsEditMode };
    }

    private static object CancelEdit()
    {
        if (GetItemsVm() is not { } v) return new { error = "ItemsTab not ready." };
        if (!v.CancelEditCommand.CanExecute(null))
            return new { error = "CancelEditCommand not executable (no editor open?)." };
        v.CancelEditCommand.Execute(null);
        return new { ok = true, isTooltipMode = v.IsTooltipMode };
    }

    // ── Editor mutation handlers (used by MCP for crafting workflows) ────

    private static ItemEditorViewModel? GetEditor()
        => GetItemsVm()?.ItemEditor;

    private static object EditorSave()
    {
        if (GetItemsVm() is not { } v) return new { error = "ItemsTab not ready." };
        if (v.ItemEditor is not { } ed) return new { error = "No editor open." };
        ed.SaveCommand.Execute(null);
        if (!string.IsNullOrEmpty(ed.SaveError)) return new { error = ed.SaveError };
        return new { ok = true };
    }

    private static object EditorCreateNew(string body)
    {
        if (GetItemsVm() is not { } v) return new { error = "ItemsTab not ready." };
        v.OpenNewItemEditorCommand.Execute(null);
        if (v.ItemEditor is null) return new { error = "Editor failed to open." };
        // Optional initial selection
        var req = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(body) ?? new();
        if (req.TryGetValue("rarity", out var rJson) && rJson.ValueKind == JsonValueKind.String)
            ApplyRarity(v.ItemEditor, rJson.GetString());
        if (req.TryGetValue("category", out var catJson) && catJson.ValueKind == JsonValueKind.String)
            v.ItemEditor.SelectedCategory = catJson.GetString() ?? "";
        if (req.TryGetValue("base", out var bJson) && bJson.ValueKind == JsonValueKind.String)
        {
            var match = v.ItemEditor.Bases.FirstOrDefault(b =>
                b.Name.Contains(bJson.GetString() ?? "", StringComparison.OrdinalIgnoreCase));
            if (match is not null) v.ItemEditor.SelectedBase = match;
        }
        if (req.TryGetValue("unique", out var uJson) && uJson.ValueKind == JsonValueKind.String)
        {
            ApplyRarity(v.ItemEditor, "Unique");
            var match = v.ItemEditor.FilteredUniques.FirstOrDefault(u =>
                u.Name.Contains(uJson.GetString() ?? "", StringComparison.OrdinalIgnoreCase));
            if (match is not null) v.ItemEditor.SelectedUnique = match;
        }
        return new { ok = true };
    }

    private static void ApplyRarity(ItemEditorViewModel ed, string? rarity)
    {
        if (string.IsNullOrEmpty(rarity)) return;
        ed.Rarity = rarity.ToLowerInvariant() switch
        {
            "normal" => ItemEditorViewModel.ItemRarity.Normal,
            "magic"  => ItemEditorViewModel.ItemRarity.Magic,
            "rare"   => ItemEditorViewModel.ItemRarity.Rare,
            "unique" => ItemEditorViewModel.ItemRarity.Unique,
            _        => ed.Rarity,
        };
    }

    private static object EditorSetQuality(string body)
    {
        if (GetEditor() is not { } ed) return new { error = "No editor open." };
        var req = JsonSerializer.Deserialize<Dictionary<string, int>>(body) ?? new();
        if (!req.TryGetValue("quality", out var q)) return new { error = "Missing 'quality'." };
        ed.Quality = Math.Clamp(q, 0, 30);
        return new { ok = true, quality = ed.Quality };
    }

    private static object EditorSetCorrupted(string body)
    {
        if (GetEditor() is not { } ed) return new { error = "No editor open." };
        var req = JsonSerializer.Deserialize<Dictionary<string, bool>>(body) ?? new();
        if (!req.TryGetValue("value", out var v2)) return new { error = "Missing 'value'." };
        ed.IsCorrupted = v2;
        return new { ok = true, isCorrupted = ed.IsCorrupted };
    }

    private static object EditorSetRarity(string body)
    {
        if (GetEditor() is not { } ed) return new { error = "No editor open." };
        var req = JsonSerializer.Deserialize<Dictionary<string, string>>(body) ?? new();
        if (!req.TryGetValue("rarity", out var r) || string.IsNullOrEmpty(r))
            return new { error = "Missing 'rarity'." };
        ApplyRarity(ed, r);
        return new { ok = true, rarity = ed.Rarity.ToString() };
    }

    private static object EditorSetBase(string body)
    {
        if (GetEditor() is not { } ed) return new { error = "No editor open." };
        var req = JsonSerializer.Deserialize<Dictionary<string, string>>(body) ?? new();
        if (!req.TryGetValue("name", out var name) || string.IsNullOrEmpty(name))
            return new { error = "Missing 'name'." };
        var match = ed.Bases.FirstOrDefault(b =>
            b.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            ?? ed.Bases.FirstOrDefault(b => b.Name.Contains(name, StringComparison.OrdinalIgnoreCase));
        if (match is null) return new { error = $"Base '{name}' not found in current category." };
        ed.SelectedBase = match;
        return new { ok = true, baseName = match.Name };
    }

    private static object EditorSetUnique(string body)
    {
        if (GetEditor() is not { } ed) return new { error = "No editor open." };
        var req = JsonSerializer.Deserialize<Dictionary<string, string>>(body) ?? new();
        if (!req.TryGetValue("name", out var name) || string.IsNullOrEmpty(name))
            return new { error = "Missing 'name'." };
        ApplyRarity(ed, "Unique");
        var match = ed.FilteredUniques.FirstOrDefault(u =>
            u.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            ?? ed.FilteredUniques.FirstOrDefault(u => u.Name.Contains(name, StringComparison.OrdinalIgnoreCase));
        if (match is null) return new { error = $"Unique '{name}' not found." };
        ed.SelectedUnique = match;
        return new { ok = true, uniqueName = match.Name };
    }

    private static object EditorSetBaseStat(string body)
    {
        if (GetEditor() is not { } ed) return new { error = "No editor open." };
        var req = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(body) ?? new();
        if (!req.TryGetValue("stat", out var sJson) || sJson.ValueKind != JsonValueKind.String)
            return new { error = "Missing 'stat'." };
        if (!req.TryGetValue("value", out var vJson) || !vJson.TryGetInt32(out int val))
            return new { error = "Missing 'value' (int)." };
        var stat = sJson.GetString()?.ToLowerInvariant() ?? "";
        switch (stat)
        {
            case "armour":       ed.BaseArmour       = val; break;
            case "evasion":      ed.BaseEvasion      = val; break;
            case "energyshield":
            case "es":           ed.BaseEnergyShield = val; break;
            case "ward":         ed.BaseWard         = val; break;
            case "spirit":       ed.BaseSpirit       = val; break;
            case "charmslots":   ed.BaseCharmSlots   = val; break;
            default: return new { error = $"Unknown stat '{stat}'." };
        }
        return new { ok = true, stat, value = val };
    }

    private static object EditorSetModSlider(string body)
    {
        if (GetEditor() is not { } ed) return new { error = "No editor open." };
        var req = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(body) ?? new();
        if (!req.TryGetValue("index", out var iJson) || !iJson.TryGetInt32(out int idx))
            return new { error = "Missing 'index' (int)." };
        if (!req.TryGetValue("value", out var vJson) || !vJson.TryGetDouble(out double val))
            return new { error = "Missing 'value' (number)." };
        if (idx < 0 || idx >= ed.ExplicitMods.Count) return new { error = "Index out of range." };
        var mod = ed.ExplicitMods[idx];
        if (!mod.HasSlider) return new { error = "Mod has no slider." };
        mod.SliderValue = Math.Clamp(val, mod.SliderMin, mod.SliderMax);
        return new { ok = true, index = idx, sliderValue = mod.SliderValue, text = mod.Text };
    }

    private static object EditorAddAffix(string body)
    {
        if (GetEditor() is not { } ed) return new { error = "No editor open." };
        var req = JsonSerializer.Deserialize<Dictionary<string, string>>(body) ?? new();
        if (!req.TryGetValue("query", out var q) || string.IsNullOrEmpty(q))
            return new { error = "Missing 'query'." };
        // Try affixName first, then stat text contains.
        var match = ed.FilteredAffixes.FirstOrDefault(a =>
            a.Entry.AffixName.Contains(q, StringComparison.OrdinalIgnoreCase))
            ?? ed.FilteredAffixes.FirstOrDefault(a =>
            a.Entry.StatText.Contains(q, StringComparison.OrdinalIgnoreCase));
        if (match is null) return new { error = $"No matching affix for '{q}'." };
        if (!ed.CanAddAffix(match.Entry)) return new { error = "Cannot add this affix (capped/duplicated)." };
        ed.AddAffix(match.Entry);
        return new { ok = true, added = match.Entry.AffixName, statText = match.Entry.StatText };
    }

    private static object EditorRemoveMod(string body)
    {
        if (GetEditor() is not { } ed) return new { error = "No editor open." };
        var req = JsonSerializer.Deserialize<Dictionary<string, int>>(body) ?? new();
        if (!req.TryGetValue("index", out var idx) || idx < 0 || idx >= ed.ExplicitMods.Count)
            return new { error = "Invalid 'index'." };
        var mod = ed.ExplicitMods[idx];
        if (!mod.IsRemovable) return new { error = "Mod is not user-removable." };
        ed.RemoveExplicitMod(mod);
        return new { ok = true };
    }

    private static object EditorAddRuneSocket()
    {
        if (GetEditor() is not { } ed) return new { error = "No editor open." };
        if (!ed.CanAddRune) return new { error = "Cannot add another rune socket (cap reached / corrupted)." };
        ed.AddRuneSocketCommand.Execute(null);
        return new { ok = true, sockets = ed.RuneSockets.Count };
    }

    private static object EditorSetRune(string body)
    {
        if (GetEditor() is not { } ed) return new { error = "No editor open." };
        var req = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(body) ?? new();
        if (!req.TryGetValue("socket", out var sJson) || !sJson.TryGetInt32(out int sock))
            return new { error = "Missing 'socket' (1-based int)." };
        if (sock < 1 || sock > ed.RuneSockets.Count) return new { error = "Socket index out of range." };
        var slot = ed.RuneSockets[sock - 1];
        if (req.TryGetValue("rune", out var rJson) && rJson.ValueKind == JsonValueKind.String)
        {
            var name = rJson.GetString() ?? "";
            if (string.IsNullOrEmpty(name) || string.Equals(name, "None", StringComparison.OrdinalIgnoreCase))
            { slot.SelectedRune = null; return new { ok = true, cleared = true }; }
            var rune = ed.CompatibleRunes.FirstOrDefault(r =>
                r.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                ?? ed.CompatibleRunes.FirstOrDefault(r => r.Name.Contains(name, StringComparison.OrdinalIgnoreCase));
            if (rune is null) return new { error = $"Rune '{name}' not compatible with this base." };
            slot.SelectedRune = rune;
            return new { ok = true, runeName = rune.Name, modSummary = slot.ModSummary };
        }
        slot.SelectedRune = null;
        return new { ok = true, cleared = true };
    }

    private static object EditorRemoveRuneSocket(string body)
    {
        if (GetEditor() is not { } ed) return new { error = "No editor open." };
        var req = JsonSerializer.Deserialize<Dictionary<string, int>>(body) ?? new();
        if (!req.TryGetValue("socket", out var sock) || sock < 1 || sock > ed.RuneSockets.Count)
            return new { error = "Invalid 'socket' (1-based)." };
        var slot = ed.RuneSockets[sock - 1];
        ed.RemoveRuneSocketCommand.Execute(slot);
        return new { ok = true, sockets = ed.RuneSockets.Count };
    }

    // ── Tree tab handlers ─────────────────────────────────────────────────

    private static TreeTabViewModel? GetTreeVm()
        => (GetMainVm()?.CurrentPage as BuildPageViewModel)?.TreeTab;

    private static object TreeState()
    {
        if (GetTreeVm() is not { } t) return new { error = "TreeTab not ready." };
        return new
        {
            selectedClass      = t.SelectedClass?.Name,
            selectedAscendancy = t.SelectedAscend?.Name,
            classes = t.Classes.Select(c => new
            {
                id   = c.Id,
                name = c.Name,
                ascendancies = c.Ascendancies.Select(a => new { id = a.Id, name = a.Name }).ToArray(),
            }).ToArray(),
            availableAscendancies = t.AvailableAscendancies.Select(a => new { id = a.Id, name = a.Name }).ToArray(),
            searchText      = t.SearchText,
            nodeCount       = t.NodeCount,
            allocatedCount  = t.AllocatedCount,
            isToggleBusy    = t.IsToggleBusy,
            heatmapEnabled     = t.HeatmapEnabled,
            powerStat          = t.SelectedPowerStat?.Option.StatKey,
            isPowerStale       = t.IsPowerStale,
            powerBuildProgress = t.PowerBuildProgress,
            workersReady       = TreePowerService.Current?.Ready ?? 0,
            workersTotal       = TreePowerService.Current?.Size ?? 0,
            powerSortIndex     = t.PowerSortIndex,
            powerFilter        = t.PowerFilter,
        };
    }

    private static object TreeSetSearch(string body)
    {
        if (GetTreeVm() is not { } t) return new { error = "TreeTab not ready." };
        var req = JsonSerializer.Deserialize<Dictionary<string, string>>(body) ?? new();
        t.SearchText = req.TryGetValue("text", out var text) ? text ?? "" : "";
        return new { ok = true, searchText = t.SearchText };
    }

    private static async Task<object> TreeToggleNode(string body)
    {
        if (GetTreeVm() is not { } t) return new { error = "TreeTab not ready." };
        var req = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(body) ?? new();
        int? id = req.TryGetValue("nodeId", out var n) && n.ValueKind == JsonValueKind.Number
            ? n.GetInt32() : null;
        var res = await t.ToggleNodeTimed(id);
        return res is { } r
            ? new { ok = true, id = r.Id, allocated = r.Allocated, ms = Math.Round(r.Ms, 2) }
            : new { error = "No toggle candidate." };
    }

    private static object TreeHoverNode(string body)
    {
        if (GetTreeVm() is not { } t) return new { error = "TreeTab not ready." };
        if (t.HoverCanvasNode is null) return new { error = "Canvas view not bound." };
        var req = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(body) ?? new();
        int? id = req.TryGetValue("nodeId", out var n) && n.ValueKind == JsonValueKind.Number
            ? n.GetInt32() : null;
        var res = t.HoverCanvasNode(id);
        return res is { } r
            ? new { ok = true, id = r.Id, name = r.Name, pathLen = r.PathLen }
            : new { error = "No hover candidate (no allocated nodes?)." };
    }

    // Returns the REAL translated text of a passive-node tooltip — the same
    // strings TreeCanvas draws (node name via TPassiveName, each stat line via
    // TPassiveStat). Canvas labels are immediate-mode drawn so /ui/text can't
    // see them; this is the faithful-text route for tree-node translation audits.
    // Body: {"nodeId": N} for one node, or omit to dump every allocated node.
    // Each line carries raw + translated so callers can spot still-English text.
    private static object TreeNodeText(string body)
    {
        if (GetTreeVm() is not { } t) return new { error = "TreeTab not ready." };
        var req = string.IsNullOrWhiteSpace(body)
            ? new Dictionary<string, JsonElement>()
            : JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(body) ?? new();

        IEnumerable<int> ids = req.TryGetValue("nodeId", out var n) && n.ValueKind == JsonValueKind.Number
            ? new[] { n.GetInt32() }
            : t.AllocatedIds.OrderBy(x => x);

        static bool LooksUntranslated(string raw, string tr)
            => string.Equals(raw, tr, StringComparison.Ordinal)
               && System.Text.RegularExpressions.Regex.IsMatch(tr, "[A-Za-z]");

        var nodes = new List<object>();
        int modTotal = 0, modUntr = 0, nameUntr = 0;
        foreach (var id in ids)
        {
            var info = t.GetNodeHoverInfo(id);
            if (info is null) continue;

            var nameTr = GameTranslationService.TPassiveName(info.Name);
            bool nameBad = LooksUntranslated(info.Name, nameTr);
            if (nameBad) nameUntr++;

            var mods = (info.Mods ?? Array.Empty<string>()).Select(raw =>
            {
                var tr = GameTranslationService.TPassiveStat(raw);
                bool bad = LooksUntranslated(raw, tr);
                modTotal++;
                if (bad) modUntr++;
                return new { raw, translated = tr, untranslated = bad };
            }).ToArray();

            nodes.Add(new
            {
                id = info.NodeId,
                nameRaw          = info.Name,
                name             = nameTr,
                nameUntranslated = nameBad,
                type             = info.Type,
                ascendancy       = info.AscendancyName,
                allocated        = info.IsAllocated,
                mods,
            });
        }

        return new
        {
            ok = true,
            count = nodes.Count,
            namesUntranslated = nameUntr,
            modsTotal = modTotal,
            modsUntranslated = modUntr,
            nodes = nodes.ToArray(),
        };
    }

    private static object TreeSelectClass(string body)
    {
        if (GetTreeVm() is not { } t) return new { error = "TreeTab not ready." };
        var req = JsonSerializer.Deserialize<Dictionary<string, string>>(body) ?? new();
        if (!req.TryGetValue("name", out var name) || string.IsNullOrEmpty(name))
            return new { error = "Missing 'name'." };

        var match = t.Classes.FirstOrDefault(c =>
            c.Name.Equals(name, StringComparison.OrdinalIgnoreCase) ||
            c.Name.Contains(name, StringComparison.OrdinalIgnoreCase));
        if (match is null)
            return new { error = $"Class '{name}' not found.", available = t.Classes.Select(c => c.Name).ToArray() };

        // Auto-confirm dialog in IPC mode
        t.ConfirmClassChange = () => Task.FromResult(true);
        t.SelectedClass = match;
        return new
        {
            ok                = true,
            selectedClass     = t.SelectedClass?.Name,
            ascendancies      = t.AvailableAscendancies.Select(a => a.Name).ToArray(),
        };
    }

    private static object TreeSelectAscendancy(string body)
    {
        if (GetTreeVm() is not { } t) return new { error = "TreeTab not ready." };
        var req = JsonSerializer.Deserialize<Dictionary<string, string>>(body) ?? new();
        if (!req.TryGetValue("name", out var name) || string.IsNullOrEmpty(name))
            return new { error = "Missing 'name'." };

        var match = t.AvailableAscendancies.FirstOrDefault(a =>
            a.Name.Equals(name, StringComparison.OrdinalIgnoreCase) ||
            a.Name.Contains(name, StringComparison.OrdinalIgnoreCase));
        if (match is null)
            return new { error = $"Ascendancy '{name}' not found.",
                         available = t.AvailableAscendancies.Select(a => a.Name).ToArray() };

        t.SelectedAscend = match;
        return new { ok = true, selectedAscendancy = match.Name };
    }

    // ── Tree view control (zoom / pan / focus) ────────────────────────────

    private static object TreeViewResult(TreeTabViewModel t)
    {
        if (t.GetCanvasView is null) return new { error = "Canvas view not bound." };
        var (scale, cx, cy) = t.GetCanvasView();
        return new { ok = true, scale, centerX = cx, centerY = cy };
    }

    private static object TreeGetView()
    {
        if (GetTreeVm() is not { } t) return new { error = "TreeTab not ready." };
        return TreeViewResult(t);
    }

    private static object TreeSetView(string body)
    {
        if (GetTreeVm() is not { } t) return new { error = "TreeTab not ready." };
        if (t.SetCanvasView is null) return new { error = "Canvas view not bound." };
        var req = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(body) ?? new();
        double? scale = req.TryGetValue("scale",   out var s)  && s.ValueKind  == JsonValueKind.Number ? s.GetDouble()  : null;
        double? cx    = req.TryGetValue("centerX", out var x)  && x.ValueKind  == JsonValueKind.Number ? x.GetDouble()  : null;
        double? cy    = req.TryGetValue("centerY", out var y)  && y.ValueKind  == JsonValueKind.Number ? y.GetDouble()  : null;
        t.SetCanvasView(scale, cx, cy);
        return TreeViewResult(t);
    }

    private static object TreeFocusNode(string body)
    {
        if (GetTreeVm() is not { } t) return new { error = "TreeTab not ready." };
        if (t.FocusCanvasNode is null) return new { error = "Canvas view not bound." };
        var req = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(body) ?? new();
        if (!req.TryGetValue("nodeId", out var n) || n.ValueKind != JsonValueKind.Number)
            return new { error = "Missing numeric 'nodeId'." };
        int nodeId = n.GetInt32();
        double? scale = req.TryGetValue("scale", out var s) && s.ValueKind == JsonValueKind.Number ? s.GetDouble() : null;
        bool found = t.FocusCanvasNode(nodeId, scale);
        if (!found) return new { error = $"Node {nodeId} is not present in the current tree." };
        return TreeViewResult(t);
    }

    private static object TreeZoom(string body)
    {
        if (GetTreeVm() is not { } t) return new { error = "TreeTab not ready." };
        if (t.ZoomCanvas is null) return new { error = "Canvas view not bound." };
        var req = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(body) ?? new();
        if (!req.TryGetValue("factor", out var f) || f.ValueKind != JsonValueKind.Number)
            return new { error = "Missing numeric 'factor'." };
        t.ZoomCanvas(f.GetDouble());
        return TreeViewResult(t);
    }

    private static object TreePan(string body)
    {
        if (GetTreeVm() is not { } t) return new { error = "TreeTab not ready." };
        if (t.PanCanvas is null) return new { error = "Canvas view not bound." };
        var req = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(body) ?? new();
        double dx = req.TryGetValue("dx", out var x) && x.ValueKind == JsonValueKind.Number ? x.GetDouble() : 0;
        double dy = req.TryGetValue("dy", out var y) && y.ValueKind == JsonValueKind.Number ? y.GetDouble() : 0;
        t.PanCanvas(dx, dy);
        return TreeViewResult(t);
    }

    private static object TreeOpenJewelPicker(string body)
    {
        if (GetTreeVm() is not { } t) return new { error = "TreeTab not ready." };
        if (t.TriggerSocketPicker is null) return new { error = "Canvas not bound." };
        var req = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(body) ?? new();
        if (!req.TryGetValue("nodeId", out var n) || n.ValueKind != JsonValueKind.Number)
            return new { error = "Missing numeric 'nodeId'." };
        t.TriggerSocketPicker(n.GetInt32());
        return new { ok = true, isOpen = t.IsJewelPickerOpen,
                     options = t.JewelPickerOptions.Select(o => new { o.ItemId, o.DisplayName, o.StatusText, o.IsCurrent }).ToArray() };
    }

    private static object TreePickJewel(string body)
    {
        if (GetTreeVm() is not { } t) return new { error = "TreeTab not ready." };
        var req = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(body) ?? new();
        if (!req.TryGetValue("itemId", out var i) || i.ValueKind != JsonValueKind.Number)
            return new { error = "Missing numeric 'itemId'." };
        t.PickJewelById(i.GetInt32());
        return new { ok = true };
    }

    private static async Task<object> TreePowerBuild(string body)
    {
        if (GetTreeVm() is not { } t) return new { error = "TreeTab not ready." };
        var req = string.IsNullOrWhiteSpace(body)
            ? new Dictionary<string, JsonElement>()
            : JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(body) ?? new();

        string? stat = req.TryGetValue("stat", out var s) && s.ValueKind == JsonValueKind.String
            ? s.GetString() : null;

        // Generation is now manual (Generate button) — toggling the heat map / changing
        // the stat no longer auto-builds. Set the stat, show the panel, then fire the
        // Generate command explicitly so automation can drive a build.
        if (stat != null)
            t.SelectedPowerStat = t.PowerStatOptions
                .FirstOrDefault(o => o.Option.StatKey == stat) ?? t.SelectedPowerStat;
        t.HeatmapEnabled = true;
        t.GeneratePowerCommand.Execute(null);

        // Wait for the background build to drain so the screenshot reflects the result.
        // Real builds can take ~85s; cap at 3600 iterations × 50ms = 180s.
        for (int i = 0; i < 3600 && (t.IsPowerBuilding || t.PowerOverlay is null); i++)
            await Task.Delay(50);

        return new { ok = true, building = t.IsPowerBuilding, rows = t.PowerReport.Count };
    }

    private static object TreePowerReport()
    {
        if (GetTreeVm() is not { } t) return new { error = "TreeTab not ready." };
        return new
        {
            enabled  = t.HeatmapEnabled,
            stat     = t.SelectedPowerStat?.Option.StatKey,
            isStale  = t.IsPowerStale,
            progress = t.PowerBuildProgress,
            rows = t.PowerReport.Take(30).Select(r => new
            {
                id        = r.NodeId,
                name      = r.Name,
                type      = r.Type,
                typeBadge = r.TypeBadge,
                power     = r.PowerStr,
                perPoint  = r.PerPointStr,
                steps     = r.StepsStr,
                alloc     = r.IsAllocated,
            }).ToArray(),
        };
    }

    // ── Trader (session + active window) ─────────────────────────────────────

    private static TraderSession? GetTraderSession()
        => (GetMainVm()?.CurrentPage as BuildPageViewModel)?.TraderSession;

    private static TraderWindowViewModel? GetTraderWindow()
        => (GetMainVm()?.CurrentPage as BuildPageViewModel)?.ActiveTraderWindow;

    private static object TraderState()
    {
        var s = GetTraderSession();
        if (s is null) return new { open = false, error = "Trader session not created." };
        var w = GetTraderWindow();
        return new
        {
            ok = true,
            windowOpen = w is not null,
            slot = w?.SlotName,
            league = s.SelectedLeague,
            leagues = s.Leagues.ToArray(),
            leagueLoadError = s.LeagueLoadError.Length > 0 ? s.LeagueLoadError : null,
            loggedIn = s.IsLoggedIn,
            account = s.AccountName,
            activeWeightCount = s.ActiveWeightCount,
            statWeightsJson = s.StatWeightsJson,
            status = w?.Status,
            busy = w?.IsBusy ?? false,
            hasQuery = w?.LastQueryJson is not null,
            results = w?.Results.Select(r => new
            {
                price = r.Listing.Amount,
                currency = r.Listing.Currency,
                seller = r.Listing.Seller,
                dps = r.DpsDiff,
                ehp = r.EhpDiff,
                value = r.StatValue,
                valuePerDiv = r.ValuePerDiv,
                triedOn = r.IsTriedOn,
            }).ToArray(),
        };
    }

    private static object TraderOpen(string body)
    {
        if (GetMainVm()?.CurrentPage is not BuildPageViewModel bp)
            return new { error = "No build page." };
        var req = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(body) ?? new();
        var slot = req.TryGetValue("slot", out var s) && s.ValueKind == JsonValueKind.String
            ? s.GetString() : null;
        if (string.IsNullOrEmpty(slot)) return new { error = "Provide 'slot'." };
        if (bp.RequestOpenTrader is null) return new { error = "Trader open route not wired." };
        bp.RequestOpenTrader(slot);
        return new { ok = true, opened = slot };
    }

    private static object TraderSearch(string body)
    {
        var w = GetTraderWindow();
        if (w is null) return new { error = "No trader window open. Call /trader/open first." };
        w.SearchCommand.Execute(null); // fire-and-forget; прогресс виден через /trader/state
        return new { ok = true, started = w.SlotName };
    }

    private static object TraderSetLeague(string body)
    {
        var s = GetTraderSession();
        if (s is null) return new { error = "Trader session not created." };
        var req = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(body) ?? new();
        if (req.TryGetValue("league", out var l) && l.ValueKind == JsonValueKind.String &&
            l.GetString() is { Length: > 0 } league)
        {
            s.SelectedLeague = league;
            return new { ok = true, league };
        }
        return new { error = "Provide 'league'." };
    }

    public void Stop()
    {
        _cts.Cancel();
        try { _listener.Stop(); } catch { }
        try { _listener.Close(); } catch { }
        try { if (File.Exists(DiscoveryPath)) File.Delete(DiscoveryPath); } catch { }
    }
}

