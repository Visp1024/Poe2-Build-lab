using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using PBLEngine;

namespace PBLParity;

internal static class Program
{
    public static int Main(string[] args)
    {
        var options = ParseArgs(args);
        if (options is null) return 1;

        try
        {
            var repoRoot = FindRepoRoot();
            Console.WriteLine($"[parity] repo root: {repoRoot}");
            Console.WriteLine($"[parity] build:     {options.BuildXmlPath}");

            var origStats = RunOriginalPoB(repoRoot, options.BuildXmlPath, options.TimeoutSeconds);
            Console.WriteLine($"[parity] orig PoB:  {origStats.Count} stats");

            var pblStats = RunPBLEngine(repoRoot, options.BuildXmlPath);
            Console.WriteLine($"[parity] PBLEngine: {pblStats.Count} stats");

            var report = Diff(origStats, pblStats, options.Tolerance);
            PrintReport(report, options);

            if (options.JsonOutPath is { } jsonPath)
            {
                File.WriteAllText(jsonPath, JsonSerializer.Serialize(report,
                    new JsonSerializerOptions { WriteIndented = true }));
                Console.WriteLine($"[parity] wrote JSON report → {jsonPath}");
            }

            return report.SignificantMismatchCount > 0 ? 2 : 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"FATAL: {ex.Message}");
            Console.Error.WriteLine(ex.StackTrace);
            return 1;
        }
    }

    // ---- option parsing ----------------------------------------------------

    private sealed record Options(
        string BuildXmlPath,
        double Tolerance,
        int TimeoutSeconds,
        bool ShowAll,
        int MaxMismatches,
        string? JsonOutPath);

    private static Options? ParseArgs(string[] args)
    {
        if (args.Length == 0 || args[0] == "--help" || args[0] == "-h")
        {
            Console.WriteLine("Usage: PBLParity <build.xml> [options]");
            Console.WriteLine();
            Console.WriteLine("Compares stats between the original PoB (SimpleGraphic runtime) and");
            Console.WriteLine("PBLEngine (NLua 5.4) for the same build XML. Exit 0 = parity, 2 = drift.");
            Console.WriteLine();
            Console.WriteLine("Options:");
            Console.WriteLine("  --tolerance=N    Relative float tolerance (default 1e-6)");
            Console.WriteLine("  --timeout=N      Seconds to wait for orig PoB dump (default 60)");
            Console.WriteLine("  --max=N          Max mismatch lines to print (default 50)");
            Console.WriteLine("  --all            Print every stat, not just mismatches");
            Console.WriteLine("  --json=PATH      Also write the full report as JSON");
            return null;
        }

        var build = args[0];
        var tolerance = 1e-6;
        var timeout = 60;
        var max = 50;
        var showAll = false;
        string? jsonOut = null;

        foreach (var arg in args.Skip(1))
        {
            if (arg.StartsWith("--tolerance="))
                tolerance = double.Parse(arg["--tolerance=".Length..], CultureInfo.InvariantCulture);
            else if (arg.StartsWith("--timeout="))
                timeout = int.Parse(arg["--timeout=".Length..]);
            else if (arg.StartsWith("--max="))
                max = int.Parse(arg["--max=".Length..]);
            else if (arg == "--all")
                showAll = true;
            else if (arg.StartsWith("--json="))
                jsonOut = arg["--json=".Length..];
        }

        if (!File.Exists(build))
            throw new FileNotFoundException($"build XML not found: {build}");

        return new Options(Path.GetFullPath(build), tolerance, timeout, showAll, max, jsonOut);
    }

    // ---- repo root ---------------------------------------------------------

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "manifest.xml")))
            dir = dir.Parent;
        if (dir == null) throw new InvalidOperationException("repo root not found (manifest.xml missing)");
        return dir.FullName;
    }

    // ---- side 1: original PoB ---------------------------------------------

    private static Dictionary<string, object?> RunOriginalPoB(
        string repoRoot, string buildXml, int timeoutSeconds)
    {
        var exeReal = Path.Combine(repoRoot, "runtime", "Path of Building-PoE2.exe");
        var exeEscaped = Path.Combine(repoRoot, "runtime", "Path{space}of{space}Building-PoE2.exe");
        if (!File.Exists(exeReal))
        {
            if (File.Exists(exeEscaped))
            {
                File.Copy(exeEscaped, exeReal, overwrite: false);
            }
            else
            {
                throw new FileNotFoundException($"runtime exe not found: {exeReal}");
            }
        }

        var script = Path.Combine(repoRoot, "tools", "parity", "dump_stats.lua");
        var outPath = Path.Combine(Path.GetTempPath(),
            $"pob_parity_orig_{Guid.NewGuid():N}.json");
        if (File.Exists(outPath)) File.Delete(outPath);

        var psi = new ProcessStartInfo
        {
            FileName = exeReal,
            WorkingDirectory = Path.Combine(repoRoot, "runtime"),
            UseShellExecute = false,
            CreateNoWindow = false  // SimpleGraphic needs a window; flashes briefly
        };
        psi.EnvironmentVariables["POB_PARITY_BUILD"] = buildXml;
        psi.EnvironmentVariables["POB_PARITY_OUT"]   = outPath;
        psi.EnvironmentVariables["POB_PARITY_SCRIPT"] = script;

        Console.WriteLine("[parity] launching original PoB...");
        var proc = Process.Start(psi)!;
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTime.UtcNow < deadline)
        {
            if (proc.HasExited && File.Exists(outPath)) break;
            if (File.Exists(outPath))
            {
                // dump_stats.lua does os.exit() AFTER closing the file, so the
                // file is fully written by the time we see it. Give the OS a beat.
                Thread.Sleep(200);
                break;
            }
            Thread.Sleep(250);
        }
        if (!proc.HasExited)
        {
            try { proc.Kill(); } catch { }
        }

        if (!File.Exists(outPath))
        {
            throw new TimeoutException(
                $"original PoB didn't produce output within {timeoutSeconds}s. " +
                $"Expected at: {outPath}");
        }

        var json = File.ReadAllText(outPath);
        File.Delete(outPath);
        return ParseStatsJson(json);
    }

    // ---- side 2: PBLEngine -------------------------------------------------

    private static Dictionary<string, object?> RunPBLEngine(string repoRoot, string buildXml)
    {
        Console.WriteLine("[parity] initializing PBLEngine...");
        using var host = new LuaHost();
        host.Initialize(repoRoot);
        var xml = File.ReadAllText(buildXml);
        host.LoadBuildFromXml(xml, "ParityBuild");
        // Normalize so the dict shape matches the orig side.
        var raw = host.GetAllStats();
        var copy = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (k, v) in raw)
        {
            copy[k] = v switch
            {
                long l => (double)l,
                int i => (double)i,
                _ => v
            };
        }
        return copy;
    }

    // ---- shared helpers ----------------------------------------------------

    private static Dictionary<string, object?> ParseStatsJson(string json)
    {
        var dict = new Dictionary<string, object?>(StringComparer.Ordinal);
        using var doc = JsonDocument.Parse(json);
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            dict[prop.Name] = prop.Value.ValueKind switch
            {
                JsonValueKind.Number => prop.Value.GetDouble(),
                JsonValueKind.String => prop.Value.GetString(),
                JsonValueKind.True   => true,
                JsonValueKind.False  => false,
                _ => null
            };
        }
        return dict;
    }

    // ---- diff --------------------------------------------------------------

    private sealed record StatRow(string Name, object? Orig, object? Pbl, string Status, double? RelDelta);
    private sealed record Report(
        int OrigCount,
        int PblCount,
        int MatchCount,
        int CloseCount,
        int MismatchCount,
        int OnlyOrigCount,
        int OnlyPblCount,
        int SignificantMismatchCount,
        List<StatRow> Rows);

    private static Report Diff(
        Dictionary<string, object?> orig,
        Dictionary<string, object?> pbl,
        double tolerance)
    {
        var allKeys = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var k in orig.Keys) allKeys.Add(k);
        foreach (var k in pbl.Keys) allKeys.Add(k);

        var rows = new List<StatRow>(allKeys.Count);
        int matches = 0, close = 0, mismatches = 0, onlyOrig = 0, onlyPbl = 0;

        foreach (var key in allKeys)
        {
            var hasO = orig.TryGetValue(key, out var ov);
            var hasP = pbl.TryGetValue(key, out var pv);

            if (hasO && !hasP)
            {
                rows.Add(new StatRow(key, ov, null, "ONLY-ORIG", null));
                onlyOrig++;
                continue;
            }
            if (!hasO && hasP)
            {
                rows.Add(new StatRow(key, null, pv, "ONLY-PBL", null));
                onlyPbl++;
                continue;
            }

            var (status, relDelta) = Compare(ov, pv, tolerance);
            rows.Add(new StatRow(key, ov, pv, status, relDelta));
            switch (status)
            {
                case "MATCH":    matches++;    break;
                case "CLOSE":    close++;      break;
                case "MISMATCH": mismatches++; break;
            }
        }

        // "Significant" = any mismatch on a numerical stat or any type mismatch.
        // ONLY-* counts as significant only if the missing side had a non-default value.
        int significant = mismatches +
            rows.Count(r => r.Status == "ONLY-ORIG" && IsSignificantValue(r.Orig)) +
            rows.Count(r => r.Status == "ONLY-PBL" && IsSignificantValue(r.Pbl));

        return new Report(orig.Count, pbl.Count, matches, close, mismatches,
            onlyOrig, onlyPbl, significant, rows);
    }

    private static (string status, double? relDelta) Compare(object? o, object? p, double tol)
    {
        if (o is null && p is null) return ("MATCH", 0.0);
        if (o is null || p is null) return ("MISMATCH", null);

        // Cross-side normalization: orig PoB serializes ±Inf / NaN as quoted
        // strings (Lua math.huge can't go into JSON directly), PBLEngine
        // returns real IEEE doubles. Treat both representations as numeric.
        o = NormalizeSpecial(o);
        p = NormalizeSpecial(p);

        if (o is bool ob && p is bool pb)
            return (ob == pb ? "MATCH" : "MISMATCH", null);

        if (o is string os && p is string ps)
            return (os == ps ? "MATCH" : "MISMATCH", null);

        if (TryAsDouble(o, out var od) && TryAsDouble(p, out var pd))
        {
            if (double.IsNaN(od) && double.IsNaN(pd)) return ("MATCH", 0.0);
            if (od == pd) return ("MATCH", 0.0);
            var maxAbs = Math.Max(Math.Abs(od), Math.Abs(pd));
            var diff = Math.Abs(od - pd);
            var rel = maxAbs > 0 ? diff / maxAbs : diff;
            if (rel < tol) return ("CLOSE", rel);
            return ("MISMATCH", rel);
        }

        return (o.Equals(p) ? "MATCH" : "MISMATCH", null);
    }

    private static object NormalizeSpecial(object v) => v switch
    {
        "+Inf" => double.PositiveInfinity,
        "-Inf" => double.NegativeInfinity,
        "NaN"  => double.NaN,
        _      => v
    };

    private static bool TryAsDouble(object? v, out double d)
    {
        switch (v)
        {
            case double dd: d = dd; return true;
            case float ff:  d = ff; return true;
            case long ll:   d = ll; return true;
            case int ii:    d = ii; return true;
            case bool bb:   d = bb ? 1 : 0; return true;
            default: d = 0; return false;
        }
    }

    private static bool IsSignificantValue(object? v) =>
        v switch
        {
            null => false,
            bool b => b,
            string s => !string.IsNullOrEmpty(s),
            double d => d != 0.0,
            long l => l != 0,
            int i => i != 0,
            _ => true
        };

    // ---- report ------------------------------------------------------------

    private static void PrintReport(Report r, Options opts)
    {
        Console.WriteLine();
        Console.WriteLine("==== Parity report ====================================================");
        Console.WriteLine($"  orig stats:   {r.OrigCount}");
        Console.WriteLine($"  PBL  stats:   {r.PblCount}");
        Console.WriteLine($"  matches:      {r.MatchCount}");
        Console.WriteLine($"  close (~):    {r.CloseCount}  (within tolerance {opts.Tolerance:g})");
        Console.WriteLine($"  mismatches:   {r.MismatchCount}");
        Console.WriteLine($"  only in orig: {r.OnlyOrigCount}");
        Console.WriteLine($"  only in PBL:  {r.OnlyPblCount}");
        Console.WriteLine($"  significant:  {r.SignificantMismatchCount}");
        Console.WriteLine();

        var toShow = opts.ShowAll
            ? r.Rows
            : r.Rows.Where(x => x.Status != "MATCH" && x.Status != "CLOSE").ToList();

        if (toShow.Count == 0)
        {
            Console.WriteLine("  ✓ No drift to report.");
            return;
        }

        // Prefer significant rows first (mismatches > only-with-value > only-empty).
        toShow = [.. toShow.OrderByDescending(r =>
            r.Status switch
            {
                "MISMATCH" => 3,
                "ONLY-ORIG" => IsSignificantValue(r.Orig) ? 2 : 0,
                "ONLY-PBL"  => IsSignificantValue(r.Pbl)  ? 2 : 0,
                _ => 1
            })
            .ThenBy(r => r.Name, StringComparer.Ordinal)];

        var n = Math.Min(opts.MaxMismatches, toShow.Count);
        Console.WriteLine($"  Top {n} of {toShow.Count} drift rows:");
        Console.WriteLine();
        Console.WriteLine($"  {"status",-10} {"stat",-40} {"orig",-15} {"pbl",-15} {"Δrel",-10}");
        Console.WriteLine($"  {new string('-', 10)} {new string('-', 40)} {new string('-', 15)} {new string('-', 15)} {new string('-', 10)}");
        foreach (var row in toShow.Take(n))
        {
            Console.WriteLine($"  {row.Status,-10} {Trunc(row.Name, 40),-40} " +
                $"{Trunc(Fmt(row.Orig), 15),-15} {Trunc(Fmt(row.Pbl), 15),-15} " +
                $"{(row.RelDelta is double d ? d.ToString("g3", CultureInfo.InvariantCulture) : "—"),-10}");
        }
    }

    private static string Fmt(object? v) => v switch
    {
        null      => "—",
        bool b    => b ? "true" : "false",
        double d  => d.ToString("g6", CultureInfo.InvariantCulture),
        long l    => l.ToString(CultureInfo.InvariantCulture),
        string s  => $"\"{s}\"",
        _ => v.ToString() ?? "?"
    };
    private static string Trunc(string s, int n) => s.Length <= n ? s : s[..(n - 1)] + "…";
}
