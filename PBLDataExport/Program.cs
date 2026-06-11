using System.Diagnostics;
using System.Text;
using System.Text.Json;
using NLua;

namespace PBLDataExport;

internal static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            var options = ParseArgs(args);
            var repoRoot = FindRepoRoot();
            Console.WriteLine($"[pbl-data-export] repo root: {repoRoot}");

            var ggpkExport = Path.Combine(repoRoot, "PBLExport", "ggpk_export");
            var tablesDir = Path.Combine(ggpkExport, "tables", "English");

            if (options.RunDump)
            {
                RunPathOfExileDat(ggpkExport);
            }
            else
            {
                Console.WriteLine("[pbl-data-export] --no-dump: reusing existing JSON dumps");
            }

            if (!Directory.Exists(tablesDir))
            {
                Console.Error.WriteLine($"No table dumps in {tablesDir}; run with --dump or run pathofexile-dat manually.");
                return 1;
            }

            // Discover available tables from JSON files on disk so the runner
            // can register everything present, not just the script-driven subset.
            var availableTables = Directory.EnumerateFiles(tablesDir, "*.json")
                .Select(p => new
                {
                    Name = Path.GetFileNameWithoutExtension(p),
                    Path = p
                })
                .ToList();
            Console.WriteLine($"[pbl-data-export] {availableTables.Count} JSON dumps available");

            // Reference declarations: which columns are foreign-row references.
            // Sourced from config.json schema metadata when present; for now,
            // hard-code the cases the proof-of-concept scripts need.
            // CostTypes.Stat -> Stats (foreign row).
            var refs = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["CostTypes"] = new(StringComparer.OrdinalIgnoreCase) { ["Stat"] = "Stats" },
                ["UniqueStashLayout"] = new(StringComparer.OrdinalIgnoreCase)
                {
                    ["WordsKey"] = "Words",
                    ["ItemVisualIdentityKey"] = "ItemVisualIdentity"
                },
                ["UniqueOrigins"] = new(StringComparer.OrdinalIgnoreCase)
                {
                    ["Unique"] = "Words",
                    ["Origin"] = "Origin"
                },
                ["Essences"] = new(StringComparer.OrdinalIgnoreCase)
                {
                    ["BaseItemType"] = "BaseItemTypes",
                },
                ["EssenceMods"] = new(StringComparer.OrdinalIgnoreCase)
                {
                    ["Essence"]            = "Essences",
                    ["TargetItemCategory"] = "EssenceTargetItemCategories",
                    ["Mod"]                = "Mods",
                    ["DisplayMod"]         = "Mods",
                },
                ["EssenceTargetItemCategories"] = new(StringComparer.OrdinalIgnoreCase)
                {
                    ["ItemClasses"] = "ItemClasses",
                },
                ["SkillGems"] = new(StringComparer.OrdinalIgnoreCase)
                {
                    ["BaseItemType"] = "BaseItemTypes",
                    ["GemEffects"]   = "GemEffects",
                },
                ["GemEffects"] = new(StringComparer.OrdinalIgnoreCase)
                {
                    ["GrantedEffect"] = "GrantedEffects",
                    ["AdditionalGrantedEffects"] = "GrantedEffects",
                },
                ["GrantedEffects"] = new(StringComparer.OrdinalIgnoreCase)
                {
                    ["ActiveSkill"]          = "ActiveSkills",
                    ["StatSet"]              = "GrantedEffectStatSets",
                    ["AdditionalStatSets"]   = "GrantedEffectStatSets",
                    ["CostTypes"]            = "CostTypes",
                },
                ["GrantedEffectStatSets"] = new(StringComparer.OrdinalIgnoreCase)
                {
                    ["Label"] = "GrantedEffectLabels",
                },
                ["SupportGems"] = new(StringComparer.OrdinalIgnoreCase)
                {
                    ["SkillGem"] = "SkillGems",
                },
                ["SkillGemSupports"] = new(StringComparer.OrdinalIgnoreCase)
                {
                    ["SkillGem"] = "SkillGems",
                    ["Supports"] = "SkillGems",
                },
                ["Mods"] = new(StringComparer.OrdinalIgnoreCase)
                {
                    ["ModType"] = "ModType",
                    ["Stat1"] = "Stats",
                    ["Stat2"] = "Stats",
                    ["Stat3"] = "Stats",
                    ["Stat4"] = "Stats",
                    ["Stat5"] = "Stats",
                    ["Stat6"] = "Stats",
                    ["Tags"] = "Tags",
                    ["SpawnWeight_Tags"] = "Tags",
                    ["ImplicitTags"] = "Tags",
                    ["GenerationWeight_Tags"] = "Tags",
                    ["Families"] = "ModFamily"
                }
            };

            var lua = SetupLua(repoRoot);

            // Inject configuration via a temp JSON file rather than a complex
            // C# structure — NLua's auto-marshalling of nested List<Dictionary>
            // throws SEHException in current NLua, and JSON is trivial for the
            // Lua side to decode via dkjson.
            var configEntries = availableTables.Select(t => new
            {
                name = t.Name,
                path = t.Path.Replace('\\', '/'),
                refs = refs.TryGetValue(t.Name, out var r) ? r : null
            });
            var filesRoot = Path.Combine(ggpkExport, "files").Replace('\\', '/');
            var configJson = JsonSerializer.Serialize(new
            {
                tables = configEntries,
                scripts = options.Scripts,
                filesRoot = filesRoot
            });
            var configPath = Path.Combine(Path.GetTempPath(), "pbl-data-export-config.json")
                .Replace('\\', '/');
            File.WriteAllText(configPath, configJson);
            Console.WriteLine($"[pbl-data-export] scripts: {string.Join(", ", options.Scripts)}");
            Console.WriteLine($"[pbl-data-export] config: {configPath}");

            lua.DoString($@"
                local dkjson = require('dkjson')
                local f = io.open([[{configPath}]], 'r')
                local raw = f:read('*all'); f:close()
                _pblExport = dkjson.decode(raw)
            ");

            // src/Export/Scripts/*.lua writes to "../Data/Foo.lua" — CWD must be src/Export.
            var exportDir = Path.Combine(repoRoot, "src", "Export");
            Directory.SetCurrentDirectory(exportDir);

            // Invoke the runner.
            var runnerPath = Path.Combine(
                AppContext.BaseDirectory, "lua", "HeadlessRunner.lua")
                .Replace('\\', '/');
            var result = lua.DoFile(runnerPath);
            var failCount = result is { Length: > 0 } && result[0] is long n ? (int)n : 0;

            Console.WriteLine($"[pbl-data-export] done, fail count = {failCount}");
            return failCount > 0 ? 2 : 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"FATAL: {ex}");
            return 1;
        }
    }

    private record Options(bool RunDump, List<string> Scripts);

    private static Options ParseArgs(string[] args)
    {
        var scripts = new List<string>();
        var runDump = true;
        foreach (var arg in args)
        {
            if (arg == "--no-dump") runDump = false;
            else if (arg.StartsWith("--script=")) scripts.Add(arg.Substring("--script=".Length));
            else if (arg == "--help" || arg == "-h")
            {
                Console.WriteLine("Usage: PBLDataExport [--no-dump] [--script=NAME ...]");
                Console.WriteLine("  --no-dump        skip pathofexile-dat run, reuse existing JSON dumps");
                Console.WriteLine("  --script=NAME    add NAME to the list of Scripts/*.lua to run (default: costs)");
                Environment.Exit(0);
            }
        }
        if (scripts.Count == 0)
        {
            scripts.Add("costs");
        }
        return new Options(runDump, scripts);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "manifest.xml")))
            dir = dir.Parent;
        if (dir == null)
            throw new InvalidOperationException("Repo root not found (manifest.xml missing upward).");
        return dir.FullName;
    }

    private static void RunPathOfExileDat(string ggpkExport)
    {
        Console.WriteLine("[pbl-data-export] running pathofexile-dat...");
        // Windows: invoking npx.cmd directly via Process.Start makes Node
        // resolve npm from the CWD's node_modules instead of the global
        // install (MODULE_NOT_FOUND on npx-cli.js). Route through cmd.exe
        // so the shell does the PATH lookup correctly.
        var psi = OperatingSystem.IsWindows()
            ? new ProcessStartInfo("cmd.exe", "/c npx pathofexile-dat@latest")
            : new ProcessStartInfo("npx", "pathofexile-dat@latest");
        psi.WorkingDirectory = ggpkExport;
        psi.UseShellExecute = false;
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        using var proc = Process.Start(psi)!;
        proc.OutputDataReceived += (_, e) => { if (e.Data != null) Console.WriteLine("  " + e.Data); };
        proc.ErrorDataReceived  += (_, e) => { if (e.Data != null) Console.Error.WriteLine("  " + e.Data); };
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();
        proc.WaitForExit();
        if (proc.ExitCode != 0)
            throw new InvalidOperationException($"pathofexile-dat exited {proc.ExitCode}");
    }

    private static Lua SetupLua(string repoRoot)
    {
        var lua = new Lua();

        // package.path: dkjson + JsonDatFile + HeadlessRunner all in lua/ next
        // to the exe, plus PBLEngine's compat.lua for Lua-5.4 shims.
        var localLua = Path.Combine(AppContext.BaseDirectory, "lua").Replace('\\', '/');
        var engineLua = Path.Combine(repoRoot, "PBLEngine", "lua").Replace('\\', '/');
        lua.DoString($@"
            package.path = '{localLua}/?.lua;{engineLua}/?.lua;' .. (package.path or '')
        ");

        // Apply the same 5.4-compat shims PBLEngine uses (string.format, unpack,
        // math.log10, etc.) — Scripts were authored against LuaJIT 5.1.
        var compatPath = Path.Combine(engineLua, "compat.lua");
        if (File.Exists(compatPath))
        {
            lua.DoFile(compatPath);
        }

        // Lua print should go to our stdout, not get swallowed.
        lua.DoString(@"
            local _print = print
            function print(...)
                local parts = {...}
                for i = 1, select('#', ...) do
                    parts[i] = tostring(parts[i])
                end
                io.write(table.concat(parts, '\t'), '\n')
                io.flush()
            end
        ");

        return lua;
    }
}
