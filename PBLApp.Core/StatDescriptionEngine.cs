using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using PBLApp.Core.Localization;

namespace PBLApp.ViewModels;

/// <summary>
/// Renders localised stat description lines from raw {stat_id → value} pairs,
/// using templates downloaded from repoe-fork (gem_stats_templates.json).
///
/// Priority: active_skill_gem > skill_stat > gem_stat (file load order).
/// Unmatched stats are ignored — callers should supply English-rendered fallbacks.
/// </summary>
public sealed class StatDescriptionEngine
{
    public static readonly StatDescriptionEngine Instance = new();

    // Handler bitmask flags (matching gen_gem_stats_ru.py)
    private const int H_NEGATE     = 1;
    private const int H_MS_TO_SEC  = 2;
    private const int H_DIV_100    = 4;
    private const int H_PER_MIN    = 8;
    private const int H_DIV_10     = 16;

    private record Variant(
        string Template,
        string[]? Formats,
        int[]? Handlers,
        CondRange[]?  Conditions);   // one per stat in the entry

    private record CondRange(double? Min, double? Max);

    private record Entry(string[] Ids, Variant[] EnVariants, Variant[] RuVariants);

    private List<Entry> _entries = new();
    // primary-key (ids[0]) → list of indices into _entries
    private Dictionary<string, List<int>> _index = new(StringComparer.Ordinal);

    private StatDescriptionEngine()
    {
        LoadTemplates();
        LocalizationService.Instance.LanguageChanged += (_, _) => { /* templates are lang-agnostic */ };
    }

    private void LoadTemplates()
    {
        var asm = typeof(StatDescriptionEngine).Assembly;
        using var stream = asm.GetManifestResourceStream(
            "PBLApp.ViewModels.Translations.gem_stats_templates.json");
        if (stream is null) return;

        var json = new StreamReader(stream, Encoding.UTF8).ReadToEnd();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        foreach (var elem in root.EnumerateArray())
        {
            var ids = ReadStringArray(elem, "i");
            if (ids.Length == 0) continue;

            var en = ReadVariants(elem, "en");
            var ru = ReadVariants(elem, "ru");
            if (en.Length == 0 && ru.Length == 0) continue;

            int idx = _entries.Count;
            _entries.Add(new Entry(ids, en, ru));

            if (!_index.TryGetValue(ids[0], out var list))
                _index[ids[0]] = list = new List<int>();
            list.Add(idx);
        }
    }

    private static string[] ReadStringArray(JsonElement parent, string prop)
    {
        if (!parent.TryGetProperty(prop, out var arr)) return Array.Empty<string>();
        var result = new List<string>();
        foreach (var e in arr.EnumerateArray())
            result.Add(e.GetString() ?? "");
        return result.ToArray();
    }

    private static Variant[] ReadVariants(JsonElement parent, string prop)
    {
        if (!parent.TryGetProperty(prop, out var arr)) return Array.Empty<Variant>();
        var list = new List<Variant>();
        foreach (var v in arr.EnumerateArray())
        {
            var tmpl = v.TryGetProperty("t", out var tp) ? tp.GetString() ?? "" : "";
            if (tmpl.Length == 0) continue;

            string[]? fmts = v.TryGetProperty("f", out var fp)
                ? ReadStringArray(v, "f") : null;

            int[]? handlers = null;
            if (v.TryGetProperty("h", out var hp))
            {
                var hs = new List<int>();
                foreach (var h in hp.EnumerateArray())
                    hs.Add(h.GetInt32());
                handlers = hs.ToArray();
            }

            CondRange[]? conds = null;
            if (v.TryGetProperty("c", out var cp))
            {
                var cs = new List<CondRange>();
                foreach (var c in cp.EnumerateArray())
                {
                    if (c.ValueKind == JsonValueKind.Null)
                    {
                        cs.Add(new CondRange(null, null));
                        continue;
                    }
                    double? mn = c.TryGetProperty("min", out var minP) ? minP.GetDouble() : null;
                    double? mx = c.TryGetProperty("max", out var maxP) ? maxP.GetDouble() : null;
                    cs.Add(new CondRange(mn, mx));
                }
                conds = cs.ToArray();
            }

            list.Add(new Variant(tmpl, fmts, handlers, conds));
        }
        return list.ToArray();
    }

    // ── Public API ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Renders stat lines from a flat {stat_id → value} dict.
    /// Returns Russian lines if the current language is "ru" and a template exists;
    /// otherwise English. Stats with no matching template are silently skipped.
    /// </summary>
    public List<string> Describe(Dictionary<string, double> stats)
    {
        if (_entries.Count == 0) return new();
        bool useRu = LocalizationService.Instance.CurrentLanguage == "ru";

        var result  = new List<string>();
        var remaining = new Dictionary<string, double>(stats, StringComparer.Ordinal);

        bool progress = true;
        while (progress && remaining.Count > 0)
        {
            progress = false;
            foreach (var (primaryId, indices) in _index)
            {
                if (!remaining.ContainsKey(primaryId)) continue;
                foreach (var idx in indices)
                {
                    var entry = _entries[idx];
                    // Check all IDs present
                    if (!AllPresent(entry.Ids, remaining)) continue;

                    double[] vals = GetValues(entry.Ids, remaining);

                    // Try to find a matching variant
                    var variants = useRu && entry.RuVariants.Length > 0
                        ? entry.RuVariants
                        : entry.EnVariants;
                    var variant  = PickVariant(variants, vals);
                    if (variant is null) continue;

                    var line = RenderVariant(variant, vals);
                    if (line.Length > 0)
                        result.Add(line);

                    // Consume used stats
                    foreach (var id in entry.Ids)
                        remaining.Remove(id);
                    progress = true;
                    break;
                }
                if (progress) break;  // restart outer loop
            }
        }
        return result;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────

    private static bool AllPresent(string[] ids, Dictionary<string, double> stats)
    {
        foreach (var id in ids)
            if (!stats.ContainsKey(id)) return false;
        return true;
    }

    private static double[] GetValues(string[] ids, Dictionary<string, double> stats)
    {
        var vals = new double[ids.Length];
        for (int i = 0; i < ids.Length; i++)
            vals[i] = stats[ids[i]];
        return vals;
    }

    private static Variant? PickVariant(Variant[] variants, double[] vals)
    {
        foreach (var v in variants)
        {
            if (v.Conditions is null) return v;
            bool ok = true;
            for (int i = 0; i < Math.Min(v.Conditions.Length, vals.Length); i++)
            {
                var c = v.Conditions[i];
                if (c.Min.HasValue && vals[i] < c.Min.Value) { ok = false; break; }
                if (c.Max.HasValue && vals[i] > c.Max.Value) { ok = false; break; }
            }
            if (ok) return v;
        }
        return null;
    }

    private static string RenderVariant(Variant v, double[] vals)
    {
        var sb = new StringBuilder(v.Template);
        for (int i = 0; i < vals.Length; i++)
        {
            string fmt     = v.Formats?[i] ?? "#";
            int    handler = v.Handlers?[i] ?? 0;

            if (fmt == "ignore") continue;

            double val = vals[i];
            if ((handler & H_NEGATE)    != 0) val = -val;
            if ((handler & H_MS_TO_SEC) != 0) val /= 1000.0;
            if ((handler & H_DIV_100)   != 0) val /= 100.0;
            if ((handler & H_PER_MIN)   != 0) val /= 60.0;
            if ((handler & H_DIV_10)    != 0) val /= 10.0;

            string numStr = val == Math.Floor(val)
                ? ((long)val).ToString()
                : val.ToString("0.##");

            if (fmt.Contains('+') && val > 0)
                numStr = "+" + numStr;

            sb.Replace($"{{{i}}}", numStr);
        }
        return sb.ToString().Trim();
    }
}
