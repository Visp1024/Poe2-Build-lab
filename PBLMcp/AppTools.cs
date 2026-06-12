using ModelContextProtocol.Server;
using PBLApp.ViewModels;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PBLMcp;

/// <summary>
/// MCP tools for testing PBLApp as an agent would:
/// navigate pages, interact with ViewModels, assert state.
/// All tools operate on a headless MVVM instance — no real window needed.
/// </summary>
[McpServerToolType]
public class AppTools(AppDriver driver)
{
    // ── Ready check ───────────────────────────────────────────────────────

    [McpServerTool]
    [Description(
        "Wait until the PoB engine (LuaHost + passive tree + data) finishes loading. " +
        "Always call this first after connecting. Typically takes 30-45 seconds on first start. " +
        "Returns 'Ready' or an error if timeout is exceeded.")]
    public async Task<string> AppWaitForReady(
        [Description("Maximum seconds to wait (default 90).")] int timeoutSeconds = 90)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
            await driver._state.HostTask.WaitAsync(cts.Token);
            return "Ready. PoB engine loaded.";
        }
        catch (OperationCanceledException)
        {
            return Error($"Engine not ready after {timeoutSeconds}s.");
        }
        catch (Exception ex) { return Error(ex); }
    }

    // ── Navigation & state ─────────────────────────────────────────────────

    [McpServerTool]
    [Description(
        "Return the current page of the app and its key properties. " +
        "Page types: BuildListPage, BuildPageLoading, BuildPageReady, BuildPageError.")]
    public string AppGetCurrentPage()
    {
        try
        {
            return driver.App.CurrentPage switch
            {
                BuildListViewModel bl => JsonSerializer.Serialize(new
                {
                    page             = "BuildListPage",
                    buildCount       = bl.CurrentItems.Count,
                    selectedBuild    = driver.SelectedBuildEntry?.Name,
                    canOpenBuild     = driver.SelectedBuildEntry is { IsBuild: true },
                },
                Indent),

                BuildPageViewModel bp => JsonSerializer.Serialize(new
                {
                    page        = bp.IsLoading ? "BuildPageLoading" : bp.LoadError.Length > 0 ? "BuildPageError" : "BuildPageReady",
                    buildName   = bp.BuildName,
                    isLoading   = bp.IsLoading,
                    loadError   = bp.LoadError.Length > 0 ? bp.LoadError : null,
                    totalDps    = bp.Build?.TotalDps,
                    life        = bp.Build?.Life,
                    mana        = bp.Build?.Mana,
                },
                Indent),

                var other => $"{{ \"page\": \"{other.GetType().Name}\" }}"
            };
        }
        catch (Exception ex) { return Error(ex); }
    }

    [McpServerTool]
    [Description("Reset the app to its initial state: BuildList page, no build loaded.")]
    public string AppReset()
    {
        try
        {
            driver.Reset();
            return "App reset to BuildList page.";
        }
        catch (Exception ex) { return Error(ex); }
    }

    // ── Build list interactions ────────────────────────────────────────────

    [McpServerTool]
    [Description(
        "List all builds shown in the BuildList page. " +
        "Returns a JSON array with name, path, isFolder for each entry.")]
    public string AppListBuilds()
    {
        try
        {
            if (driver.App.CurrentPage is not BuildListViewModel bl)
                return Error("Not on BuildList page. Call app_reset first.");

            var entries = Flatten(bl.CurrentItems);
            return JsonSerializer.Serialize(entries, Indent);
        }
        catch (Exception ex) { return Error(ex); }
    }

    [McpServerTool]
    [Description(
        "Select a build by name in the BuildList page. " +
        "Partial name match is supported. Returns the matched build name or an error.")]
    public string AppSelectBuild(
        [Description("Full or partial name of the build to select.")] string name)
    {
        try
        {
            if (driver.App.CurrentPage is not BuildListViewModel bl)
                return Error("Not on BuildList page.");

            var all = Flatten(bl.CurrentItems).Where(e => !e.IsFolder).ToList();
            var match = all.FirstOrDefault(e =>
                e.Name.Equals(name, StringComparison.OrdinalIgnoreCase) ||
                e.Name.Contains(name, StringComparison.OrdinalIgnoreCase));

            if (match is null)
                return Error($"No build matching '{name}'. Available: {string.Join(", ", all.Select(e => e.Name))}");

            driver.SelectedBuildEntry = match;
            return $"Selected: {match.Name}";
        }
        catch (Exception ex) { return Error(ex); }
    }

    [McpServerTool]
    [Description(
        "Open the currently selected build (equivalent to double-clicking it). " +
        "Navigates to BuildPage. Then call app_wait_for_build_load to wait for the calc to finish.")]
    public string AppOpenBuild()
    {
        try
        {
            if (driver.App.CurrentPage is not BuildListViewModel bl)
                return Error("Not on BuildList page.");
            if (driver.SelectedBuildEntry is not { IsBuild: true } entry)
                return Error("No build selected or selected item is a folder. Call app_select_build first.");

            bl.OpenItemCommand.Execute(entry);
            return "Navigated to BuildPage. Call app_wait_for_build_load to wait for calc results.";
        }
        catch (Exception ex) { return Error(ex); }
    }

    [McpServerTool]
    [Description(
        "Wait for the currently open build to finish loading (up to timeoutSeconds). " +
        "Returns final page state with stats, or timeout/error info.")]
    public async Task<string> AppWaitForBuildLoad(
        [Description("Maximum seconds to wait (default 30).")] int timeoutSeconds = 30)
    {
        try
        {
            if (driver.App.CurrentPage is not BuildPageViewModel bp)
                return Error("Not on BuildPage. Call app_open_build first.");

            var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
            while (bp.IsLoading && DateTime.UtcNow < deadline)
                await Task.Delay(200);

            if (bp.IsLoading)
                return Error($"Build still loading after {timeoutSeconds}s.");

            return JsonSerializer.Serialize(new
            {
                buildName   = bp.BuildName,
                loadError   = bp.LoadError.Length > 0 ? bp.LoadError : null,
                totalDps    = bp.Build?.TotalDps,
                life        = bp.Build?.Life,
                mana        = bp.Build?.Mana,
                energyShield = bp.Build?.EnergyShield,
                armour      = bp.Build?.Armour,
                evasion     = bp.Build?.Evasion,
                critChance  = bp.Build?.CritChance,
                critMult    = bp.Build?.CritMultiplier,
                hitChance   = bp.Build?.HitChance,
            }, Indent);
        }
        catch (Exception ex) { return Error(ex); }
    }

    [McpServerTool]
    [Description("Navigate back from BuildPage to BuildList.")]
    public string AppGoBack()
    {
        try
        {
            if (driver.App.CurrentPage is not BuildPageViewModel bp)
                return Error("Not on BuildPage.");

            bp.BackCommand.Execute(null);
            return "Navigated back to BuildList.";
        }
        catch (Exception ex) { return Error(ex); }
    }

    // ── Build verification ────────────────────────────────────────────────

    [McpServerTool]
    [Description(
        "Open a named build, wait for it to load, and assert that a single stat matches an expected value. " +
        "For multiple stats, call this tool multiple times (the build stays loaded). " +
        "Returns PASS or FAIL with actual vs expected values.")]
    public async Task<string> AppAssertStat(
        [Description("Full or partial build name. Pass empty string to use the already-open build.")] string buildName,
        [Description("Stat key to check, e.g. Life, Mana, TotalDPS, CritChance.")] string statKey,
        [Description("Expected numeric value.")] double expectedValue,
        [Description("Tolerance for floating-point comparison (default 0.01).")] double tolerance = 0.01)
    {
        try
        {
            // If a build name was provided, navigate to it
            if (!string.IsNullOrWhiteSpace(buildName))
            {
                await driver._state.HostTask;
                driver.Reset();
                var selResult = AppSelectBuild(buildName);
                if (selResult.StartsWith("ERROR")) return selResult;
                var openResult = AppOpenBuild();
                if (openResult.StartsWith("ERROR")) return openResult;
                var loadResult = await AppWaitForBuildLoad(60);
                if (loadResult.StartsWith("ERROR")) return loadResult;
            }

            if (driver.App.CurrentPage is not BuildPageViewModel bp)
                return Error("No build open. Pass a build name or call app_open_build first.");
            if (bp.LoadError.Length > 0)
                return Error($"Build load error: {bp.LoadError}");
            if (bp.Build is null)
                return Error("Build has no stats yet.");

            if (!bp.Build.AllStats.TryGetValue(statKey, out var rawActual) || rawActual is null)
                return $"FAIL  {statKey}: expected {expectedValue}, got nil";

            double actualVal = Convert.ToDouble(rawActual);
            return Math.Abs(actualVal - expectedValue) <= tolerance
                ? $"PASS  {statKey}: {actualVal}"
                : $"FAIL  {statKey}: expected {expectedValue}, got {actualVal}";
        }
        catch (Exception ex) { return Error(ex); }
    }

    // ── Skill group selection ─────────────────────────────────────────────

    [McpServerTool]
    [Description(
        "List all skill groups available in the currently loaded build. " +
        "Returns a JSON array with index and name for each group.")]
    public string AppListSkills()
    {
        try
        {
            if (driver.App.CurrentPage is not BuildPageViewModel bp)
                return Error("Not on BuildPage. Open a build first.");
            if (bp.CalcsTab is not { } calcs)
                return Error("CalcsTab not ready yet. Wait for build to load.");

            var groups = calcs.SkillGroups.Select(g => new { g.Index, g.Name }).ToList();
            return JsonSerializer.Serialize(groups, Indent);
        }
        catch (Exception ex) { return Error(ex); }
    }

    [McpServerTool]
    [Description(
        "Select the active skill group by name (partial match) or by 1-based index. " +
        "This triggers a full recalculation and updates all stats. " +
        "After calling this, use app_get_stats or app_dump_stats to read results.")]
    public string AppSelectSkill(
        [Description("Skill group name (partial match) or 1-based index as string, e.g. '3' or 'Lightning Warp'.")] string nameOrIndex)
    {
        try
        {
            if (driver.App.CurrentPage is not BuildPageViewModel bp)
                return Error("Not on BuildPage. Open a build first.");
            if (bp.CalcsTab is not { } calcs)
                return Error("CalcsTab not ready yet. Wait for build to load.");

            SkillGroupDisplayVm? target = null;

            if (int.TryParse(nameOrIndex, out var idx))
            {
                target = calcs.SkillGroups.FirstOrDefault(g => g.Index == idx)
                      ?? calcs.SkillGroups.ElementAtOrDefault(idx - 1);
            }
            else
            {
                target = calcs.SkillGroups.FirstOrDefault(g =>
                    g.Name.Equals(nameOrIndex, StringComparison.OrdinalIgnoreCase) ||
                    g.Name.Contains(nameOrIndex, StringComparison.OrdinalIgnoreCase));
            }

            if (target is null)
            {
                var names = string.Join(", ", calcs.SkillGroups.Select(g => $"[{g.Index}] {g.Name}"));
                return Error($"Skill group '{nameOrIndex}' not found. Available: {names}");
            }

            calcs.SelectedSkillGroup = target;
            return $"Selected skill: [{target.Index}] {target.Name}. Stats recalculated.";
        }
        catch (Exception ex) { return Error(ex); }
    }

    [McpServerTool]
    [Description(
        "Get stats for the currently open build. " +
        "Pass a comma-separated list of keys to filter (e.g. 'TotalDPS,Life,LightningMin'), " +
        "or leave empty to get all stats as a sorted JSON object.")]
    public string AppGetStats(
        [Description("Comma-separated stat keys to retrieve, or empty for all stats.")] string keys = "")
    {
        try
        {
            if (driver.App.CurrentPage is not BuildPageViewModel bp)
                return Error("Not on BuildPage.");
            if (bp.Build is not { } build)
                return Error("Build not loaded yet.");

            var all = build.AllStats;

            if (string.IsNullOrWhiteSpace(keys))
            {
                var sorted = all
                    .Where(kv => kv.Value is not null)
                    .OrderBy(kv => kv.Key)
                    .ToDictionary(kv => kv.Key, kv => kv.Value);
                return JsonSerializer.Serialize(sorted, Indent);
            }

            var requested = keys.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var result = new Dictionary<string, object?>();
            foreach (var key in requested)
            {
                all.TryGetValue(key, out var val);
                result[key] = val;
            }
            return JsonSerializer.Serialize(result, Indent);
        }
        catch (Exception ex) { return Error(ex); }
    }

    [McpServerTool]
    [Description(
        "Dump all stats to a text file on the Desktop (format: KEY<tab>VALUE, sorted). " +
        "Returns the file path. Useful for debugging and comparing with expected values.")]
    public string AppDumpStats(
        [Description("Optional filename (without path). Defaults to 'pob_mcp_dump.txt'.")] string filename = "pob_mcp_dump.txt")
    {
        try
        {
            if (driver.App.CurrentPage is not BuildPageViewModel bp)
                return Error("Not on BuildPage.");
            if (bp.Build is not { } build)
                return Error("Build not loaded yet.");

            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            var path = Path.Combine(desktop, filename);

            var lines = build.AllStats
                .Where(kv => kv.Value is not null)
                .OrderBy(kv => kv.Key)
                .Select(kv => $"{kv.Key}\t{kv.Value}");
            File.WriteAllLines(path, lines);
            return $"Dumped {build.AllStats.Count} stats to: {path}";
        }
        catch (Exception ex) { return Error(ex); }
    }

    // ── Skills tab ────────────────────────────────────────────────────────

    [McpServerTool]
    [Description(
        "Get gems for a skill group in the currently loaded build. " +
        "Specify a 1-based group index or partial name; leave empty for the main group. " +
        "Returns a JSON array with name, level, quality, type (Active/Support), and enabled.")]
    public string AppGetGems(
        [Description("1-based group index or group name (partial match). Leave empty for the main group.")] string groupNameOrIndex = "")
    {
        try
        {
            if (driver.App.CurrentPage is not BuildPageViewModel bp)
                return Error("Not on BuildPage. Open a build first.");
            if (bp.SkillsTab is not { } skills)
                return Error("SkillsTab not ready. Wait for build to load.");

            SkillGroupViewModel? group;
            if (string.IsNullOrWhiteSpace(groupNameOrIndex))
            {
                group = skills.Groups.FirstOrDefault(g => g.IsMain)
                     ?? skills.Groups.FirstOrDefault();
            }
            else if (int.TryParse(groupNameOrIndex, out var idx))
            {
                group = skills.Groups.ElementAtOrDefault(idx - 1);
            }
            else
            {
                group = skills.Groups.FirstOrDefault(g =>
                    g.ActiveGemName.Equals(groupNameOrIndex, StringComparison.OrdinalIgnoreCase) ||
                    g.ActiveGemName.Contains(groupNameOrIndex, StringComparison.OrdinalIgnoreCase));
            }

            if (group is null)
            {
                var avail = string.Join(", ", skills.Groups.Select(g => $"[{g.Index}] {g.ActiveGemName}"));
                return Error($"Group not found. Available: {avail}");
            }

            var result = group.SupportSlots.Select(g => new
            {
                name    = g.SearchText,
                displayName = g.DisplayText,
                level   = (int)g.Level,
                quality = (int)g.Quality,
                isSupport = g.IsSupport,
                enabled = g.IsEnabled,
            }).ToList();
            return JsonSerializer.Serialize(result, Indent);
        }
        catch (Exception ex) { return Error(ex); }
    }

    // ── Compile check ──────────────────────────────────────────────────────

    [McpServerTool]
    [Description(
        "Build the PBLApp project and return compilation output. " +
        "Use this to verify that your code changes compile before testing.")]
    public async Task<string> BuildApp(
        [Description("Build configuration: Debug or Release (default Debug).")] string configuration = "Debug")
    {
        try
        {
            var repoRoot = FindRepoRoot();
            var csproj   = Path.Combine(repoRoot, "PBLApp", "PBLApp.csproj");

            var psi = new ProcessStartInfo("dotnet",
                $"build \"{csproj}\" -c {configuration} --nologo -v minimal")
            {
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
                WorkingDirectory       = repoRoot,
            };

            using var proc = Process.Start(psi)!;
            var stdout = await proc.StandardOutput.ReadToEndAsync();
            var stderr = await proc.StandardError.ReadToEndAsync();
            await proc.WaitForExitAsync();

            var output = (stdout + stderr).Trim();
            return proc.ExitCode == 0
                ? $"BUILD SUCCEEDED\n{output}"
                : $"BUILD FAILED (exit {proc.ExitCode})\n{output}";
        }
        catch (Exception ex) { return Error(ex); }
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static readonly JsonSerializerOptions Indent = new() { WriteIndented = true };

    private static List<BuildEntryViewModel> Flatten(
        IEnumerable<BuildEntryViewModel> items)
    {
        var result = new List<BuildEntryViewModel>();
        foreach (var item in items)
        {
            result.Add(item);
            if (item.IsFolder)
                result.AddRange(Flatten(item.Children));
        }
        return result;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "src")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Cannot find repo root");
    }

    private static string Error(string msg) => $"ERROR: {msg}";
    private static string Error(Exception ex) => $"ERROR: {ex.GetType().Name}: {ex.Message}";
}
