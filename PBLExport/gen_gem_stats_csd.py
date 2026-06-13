"""
Generate gem_stats_templates.json (EN + RU stat-description templates for the
gem-tooltip StatDescriptionEngine) from the GGPK .csd files instead of repoe-fork.

Why: repoe-fork dropped its Russian stat_translations (the /Russian/ tree now
404s), so the old gen_gem_stats_ru.py can no longer fetch RU. The .csd files
exported by pathofexile-dat (PBLExport/ggpk_export/files/) are the game's own
source — each `description` block carries the stat ids plus EN + RU (and other
language) templates with the same placeholder / handler / condition model the
C# StatDescriptionEngine already understands.

Output schema (one record per stat-id tuple, first file in priority order wins):
  { "i": [ids...], "en": [variant...], "ru": [variant...] }
  variant = { "t": template-with-{N}, "f"?: [fmt...], "h"?: [bitmask...], "c"?: [cond|null...] }
  fmt: "#" | "+#"   handler bits: 1=negate 2=ms->s 4=/100 8=per-min->per-sec 16=/10
  cond: {"min":x?, "max":y?}  (omitted/null = always)

Existing entries whose id-tuple the .csd files don't cover are preserved (merge),
so coverage never regresses below the previous repoe-fork build.

Usage:  python PBLExport/gen_gem_stats_csd.py [--dry-run]
"""

import json, re, os, sys, io, glob

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")

HERE     = os.path.dirname(__file__)
CSD_DIR  = os.path.join(HERE, "ggpk_export", "files")
OUT_DIR  = os.path.join(HERE, "..", "PBLApp.Core", "Translations")
OUT_PATH = os.path.join(OUT_DIR, "gem_stats_templates.json")
DRY      = "--dry-run" in sys.argv

# Priority order: gem-specific first, generic catch-all last (first match wins
# per id-tuple) — mirrors the old repoe STAT_FILES ordering.
CSD_FILES = [
    "Data@StatDescriptions@active_skill_gem_stat_descriptions.csd",
    "Data@StatDescriptions@meta_gem_stat_descriptions.csd",
    "Data@StatDescriptions@gem_stat_descriptions.csd",
    "Data@StatDescriptions@skill_stat_descriptions.csd",
    "Data@StatDescriptions@stat_descriptions.csd",
    "Data@StatDescriptions@advanced_mod_stat_descriptions.csd",
    "Data@StatDescriptions@utility_flask_buff_stat_descriptions.csd",
]

MARKUP_RE      = re.compile(r"\[([^|\]]+)\|([^\]]*)\]|\[([^\]]*)\]")
PLACEHOLDER_RE = re.compile(r"\{(\d*)(?::([^}]*))?\}")

HANDLER_BITS = {
    "negate": 1,
    "milliseconds_to_seconds": 2,
    "milliseconds_to_seconds_2dp_if_required": 2,
    "milliseconds_to_seconds_2dp": 2,
    "milliseconds_to_seconds_0dp": 2,
    "milliseconds_to_seconds_1dp": 2,
    "divide_by_one_hundred": 4,
    "divide_by_one_hundred_2dp": 4,
    "divide_by_one_hundred_2dp_if_required": 4,
    "divide_by_one_hundred_0dp": 4,
    "per_minute_to_per_second": 8,
    "per_minute_to_per_second_2dp": 8,
    "per_minute_to_per_second_2dp_if_required": 8,
    "per_minute_to_per_second_0dp": 8,
    "divide_by_ten_0dp": 16,
    "divide_by_ten_1dp": 16,
}


def read_text(path):
    b = open(path, "rb").read()
    if b[:2] in (b"\xff\xfe", b"\xfe\xff"):
        return b.decode("utf-16", errors="replace")
    return b.decode("utf-8", errors="replace")


def strip_markup(s):
    return MARKUP_RE.sub(lambda m: m.group(2) if m.group(2) is not None else m.group(3), s)


def parse_cond(tok):
    """csd condition token -> {min,max} | None ('#' = any)."""
    if tok == "#":
        return None
    if tok.startswith("!"):
        return None  # engine has no '!= N'; rely on variant order
    if "|" in tok:
        lo, hi = tok.split("|", 1)
        c = {}
        if lo != "#":
            try: c["min"] = float(lo)
            except ValueError: pass
        if hi != "#":
            try: c["max"] = float(hi)
            except ValueError: pass
        return c or None
    try:
        v = float(tok)
        return {"min": v, "max": v}
    except ValueError:
        return None


def num(x):
    return int(x) if x == int(x) else x


def build_variant(conds, template, handlers, nstats):
    """csd variant line -> engine variant dict, or None."""
    t = strip_markup(template)

    fmt = ["#"] * nstats
    auto = 0

    def repl(m):
        nonlocal auto
        idx = int(m.group(1)) if m.group(1) else auto
        auto = idx + 1
        spec = m.group(2) or ""
        if 0 <= idx < nstats and "+" in spec:
            fmt[idx] = "+#"
        return "{%d}" % idx

    t = PLACEHOLDER_RE.sub(repl, t).strip()
    if not t:
        return None

    # conditions per stat
    cs = [parse_cond(conds[i]) if i < len(conds) else None for i in range(nstats)]

    # handlers: trailing "name idx" pairs (idx 1-based)
    hbits = [0] * nstats
    toks = handlers.split()
    i = 0
    while i < len(toks):
        name = toks[i]
        bit = HANDLER_BITS.get(name)
        if bit and i + 1 < len(toks) and toks[i + 1].lstrip("-").isdigit():
            idx = int(toks[i + 1]) - 1
            if 0 <= idx < nstats:
                hbits[idx] |= bit
            i += 2
        else:
            i += 1

    obj = {"t": t}
    if any(f != "#" for f in fmt):
        obj["f"] = fmt
    if any(hbits):
        obj["h"] = hbits
    if any(c is not None for c in cs):
        def cond_obj(c):
            if not c:
                return None
            o = {}
            if "min" in c: o["min"] = num(c["min"])
            if "max" in c: o["max"] = num(c["max"])
            return o or None
        obj["c"] = [cond_obj(c) for c in cs]
    return obj


VAR_RE = re.compile(r'^(.*?)"(.*)"(.*)$')


def parse_csd(path):
    """-> list of {ids:[...], en:[variant...], ru:[variant...]}"""
    lines = read_text(path).splitlines()
    n = len(lines)
    out = []
    i = 0
    while i < n:
        if lines[i].strip() != "description":
            i += 1
            continue
        i += 1
        if i >= n:
            break
        m = re.match(r"^(\d+)\s+(.*)$", lines[i].strip())
        if not m:
            continue
        nstats = int(m.group(1))
        ids = m.group(2).split()
        i += 1
        block = {"ids": ids, "en": [], "ru": []}
        cur = "en"  # default (no lang line) section is English
        while i < n:
            raw = lines[i]
            s = raw.strip()
            if s == "description":
                break
            lm = re.match(r'^lang "(.+)"$', s)
            if lm:
                cur = "ru" if lm.group(1) == "Russian" else "other"
                i += 1
                continue
            if re.match(r"^\d+$", s):  # variant-count line
                i += 1
                continue
            vm = VAR_RE.match(s)
            if vm and cur in ("en", "ru"):
                v = build_variant(vm.group(1).split(), vm.group(2), vm.group(3).strip(), nstats)
                if v:
                    block[cur].append(v)
            i += 1
        if block["en"] or block["ru"]:
            out.append(block)
    return out


def main():
    seen = set()
    out = []
    # Per-skill overrides (e.g. "... while in Demon Form") live in
    # specific_skill_stat_descriptions files; they win over the generic ones, so
    # parse them first. Auto-discovered — add them to the GGPK export to close the
    # last residual skill-specific gaps (see LOCALIZATION_PLAN).
    specific = sorted(os.path.basename(p) for p in
                      glob.glob(os.path.join(CSD_DIR, "*specific_skill_stat_descriptions*.csd")))
    for f in specific + CSD_FILES:
        path = os.path.join(CSD_DIR, f)
        if not os.path.exists(path):
            print(f"  [skip] {f} — not exported")
            continue
        blocks = parse_csd(path)
        added = 0
        for b in blocks:
            key = tuple(b["ids"])
            if not key or key in seen:
                continue
            seen.add(key)
            rec = {"i": b["ids"]}
            if b["en"]:
                rec["en"] = b["en"]
            if b["ru"]:
                rec["ru"] = b["ru"]
            out.append(rec)
            added += 1
        print(f"  {f}: {len(blocks)} blocks, +{added} new id-tuples")

    csd_count = len(out)
    ru_count = sum(1 for r in out if "ru" in r)
    print(f".csd: {csd_count} id-tuples ({ru_count} with RU)")

    # Merge: keep prior repoe entries for id-tuples the .csd files didn't cover.
    kept = 0
    if os.path.exists(OUT_PATH):
        prev = json.loads(open(OUT_PATH, encoding="utf-8").read())
        for rec in prev:
            key = tuple(rec.get("i", []))
            if key and key not in seen:
                out.append(rec)
                seen.add(key)
                kept += 1
    print(f"merged {kept} prior entries for uncovered id-tuples; total {len(out)}")

    if DRY:
        print("(dry-run, not writing)")
        return

    os.makedirs(OUT_DIR, exist_ok=True)
    with open(OUT_PATH, "w", encoding="utf-8") as fh:
        json.dump(out, fh, ensure_ascii=False, separators=(",", ":"))
    kb = os.path.getsize(OUT_PATH) // 1024
    print(f"Wrote {len(out)} entries to gem_stats_templates.json ({kb} KB)")


if __name__ == "__main__":
    main()
