"""
Generate gem_stats_templates.json: EN + RU stat description templates for gem tooltip rendering.
Downloads templates from repoe-fork (stat IDs + parameterized templates) and saves a compact
JSON used by the C# StatDescriptionEngine to render localized stat lines at runtime.

Usage: python gen_gem_stats_ru.py
Output: ../PBLApp.Core/Translations/gem_stats_templates.json
"""

import json, re, urllib.request, os

BASE    = "https://repoe-fork.github.io/poe2"
OUT_DIR = os.path.join(os.path.dirname(__file__), "../PBLApp.Core/Translations")

# Files in priority order (first match wins when rendering).
# skill_stat_descriptions covers most skill-level scaling stats;
# gem_stat_descriptions covers support gem modifiers;
# stat_descriptions is the generic catch-all (largest file).
STAT_FILES = [
    # Gem-specific (highest priority)
    "active_skill_gem_stat_descriptions",
    "meta_gem_stat_descriptions",
    "gem_stat_descriptions",
    "skill_stat_descriptions",
    # Generic catch-all (large but needed for uncommon gem stats)
    "stat_descriptions",
    # Other
    "advanced_mod_stat_descriptions",
    "utility_flask_buff_stat_descriptions",
    "chest_stat_descriptions",
]


def fetch(url):
    print(f"  {url}...", end=" ", flush=True)
    try:
        with urllib.request.urlopen(url, timeout=30) as r:
            data = json.loads(r.read().decode("utf-8"))
            print(f"OK ({len(data)} entries)")
            return data
    except Exception as e:
        print(f"FAILED: {e}")
        return []


def strip_markup(s: str) -> str:
    """[Tag|text] → text; [Tag] → Tag."""
    s = re.sub(r'\[([^\]|]+)\|([^\]]+)\]', r'\2', s)
    s = re.sub(r'\[([^\]|]+)\]', r'\1', s)
    return s


def encode_handlers(handler_list: list) -> int:
    """Encode handlers as a bitmask: 1=negate, 2=ms_to_sec, 4=div100, 8=per_min_to_per_sec."""
    bits = 0
    for h in handler_list:
        if h == "negate":
            bits |= 1
        elif h in ("milliseconds_to_seconds", "milliseconds_to_seconds_2dp_if_required",
                    "milliseconds_to_seconds_0dp"):
            bits |= 2
        elif h in ("divide_by_one_hundred", "divide_by_one_hundred_2dp"):
            bits |= 4
        elif h in ("per_minute_to_per_second", "per_minute_to_per_second_2dp_if_required"):
            bits |= 8
        elif h == "divide_by_ten_0dp":
            bits |= 16
    return bits


def encode_variant(var: dict, lang: str) -> dict | None:
    raw = var.get("string", "")
    if not raw:
        return None
    tmpl = strip_markup(raw)
    fmt  = var.get("format", ["#"])
    hdls = var.get("index_handlers", [[] for _ in fmt])
    cond = var.get("condition", [{}] * len(fmt))

    obj: dict = {"t": tmpl}

    # Format: omit if all "#" (most common case)
    if fmt != ["#"] * len(fmt):
        obj["f"] = fmt

    # Handlers: encode as bitmask array; omit if all zero
    h = [encode_handlers(h) for h in hdls]
    if any(h):
        obj["h"] = h

    # Conditions per-value; omit if all empty
    c = [c if c else None for c in cond]
    if any(c):
        obj["c"] = c

    return obj


# ── Download templates ──────────────────────────────────────────────────────────
print("Fetching EN templates...")
en_data: dict[str, list] = {}   # fname → list of entries
for fname in STAT_FILES:
    entries = fetch(f"{BASE}/stat_translations/{fname}.min.json")
    en_data[fname] = entries

print("\nFetching RU templates...")
ru_data: dict[str, list] = {}
for fname in STAT_FILES:
    entries = fetch(f"{BASE}/Russian/stat_translations/{fname}.min.json")
    ru_data[fname] = entries

# ── Build lookup: (ids_tuple) → {en: [...], ru: [...]} ──────────────────────────
# Use the first ID as primary key for fast lookup; full IDs stored in entry.
# Process files in priority order — first file wins for a given IDs tuple.
print("\nBuilding entry map...")
seen_ids: set[tuple] = set()
out: list[dict] = []

for fname in STAT_FILES:
    en_list = en_data.get(fname, [])
    # Build RU lookup for this file: ids_tuple → list of RU variants
    ru_by_ids: dict[tuple, list] = {}
    for entry in ru_data.get(fname, []):
        ids   = tuple(entry.get("ids", []))
        ru_by_ids[ids] = entry.get("Russian", [])

    for entry in en_list:
        ids   = tuple(entry.get("ids", []))
        if not ids or ids in seen_ids:
            continue
        seen_ids.add(ids)

        en_variants = entry.get("English", [])
        ru_variants = ru_by_ids.get(ids, [])

        en_enc = [v for v in (encode_variant(v, "en") for v in en_variants) if v]
        ru_enc = [v for v in (encode_variant(v, "ru") for v in ru_variants) if v]

        if not en_enc and not ru_enc:
            continue

        rec: dict = {"i": list(ids)}
        if en_enc:
            rec["en"] = en_enc
        if ru_enc:
            rec["ru"] = ru_enc
        out.append(rec)

print(f"Total unique entries: {len(out)}")

# ── Write output ────────────────────────────────────────────────────────────────
os.makedirs(OUT_DIR, exist_ok=True)
out_path = os.path.join(OUT_DIR, "gem_stats_templates.json")
with open(out_path, "w", encoding="utf-8") as f:
    json.dump(out, f, ensure_ascii=False, separators=(",", ":"))

size_kb = os.path.getsize(out_path) // 1024
print(f"Wrote {len(out)} entries to gem_stats_templates.json ({size_kb} KB)")
