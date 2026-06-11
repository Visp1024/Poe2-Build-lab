"""
Generate passive_nodes_ru.json: mapping of English passive tree stat strings
to Russian translations, using repoe-fork stat_translations templates.

Usage: python gen_passive_ru.py
Output: ../PBLApp.Core/Translations/passive_nodes_ru.json
"""

import json, re, urllib.request, os, sys

BASE = "https://repoe-fork.github.io/poe2"
TREE_JSON = os.path.join(os.path.dirname(__file__), "../src/TreeData/0_4/tree.json")
OUT_DIR   = os.path.join(os.path.dirname(__file__), "../PBLApp.Core/Translations")

STAT_FILES = [
    # Passive-specific (highest priority)
    "passive_skill_stat_descriptions",
    "passive_skill_aura_stat_descriptions",
    # Skill/gem stats (many passives reference skill stats)
    "active_skill_gem_stat_descriptions",
    "skill_stat_descriptions",
    "gem_stat_descriptions",
    "meta_gem_stat_descriptions",
    # Generic stats (largest file — covers most remaining)
    "stat_descriptions",
    # Other modifiers
    "advanced_mod_stat_descriptions",
    "character_panel_stat_descriptions",
    "utility_flask_buff_stat_descriptions",
    "chest_stat_descriptions",
    "endgame_map_stat_descriptions",
    "atlas_stat_descriptions",
    "map_stat_descriptions",
    "tablet_stat_descriptions",
    "monster_stat_descriptions",
    "leaguestone_stat_descriptions",
    "expedition_relic_stat_descriptions",
    "primordial_altar_stat_descriptions",
    "sanctum_relic_stat_descriptions",
    "sentinel_stat_descriptions",
    "heist_equipment_stat_descriptions",
    "character_panel_gamepad_stat_descriptions",
]


def fetch(url):
    print(f"  Fetching {url}...", end=" ", flush=True)
    try:
        with urllib.request.urlopen(url, timeout=30) as r:
            data = json.loads(r.read().decode("utf-8"))
            print(f"OK ({len(data)} entries)")
            return data
    except Exception as e:
        print(f"FAILED: {e}")
        return []


def strip_markup(s: str) -> str:
    """[Tag|text] → text, also strip remaining [Tag] references."""
    s = re.sub(r'\[([^\]|]+)\|([^\]]+)\]', r'\2', s)
    s = re.sub(r'\[([^\]|]+)\]', r'\1', s)
    return s


# Bullet/diamond characters used as line-item prefixes in PoE stat templates.
# tree.json strips these prefixes when storing individual stat strings.
_BULLET_PREFIX = re.compile(
    r'^[◆●•◦◈·‹☆★]\s*'
)

def strip_bullet(s: str) -> str:
    """Remove leading bullet/diamond character used in multi-line stat templates."""
    return _BULLET_PREFIX.sub('', s)


def remap_placeholders(s: str, orig_indices: list[int]) -> str:
    """Remap {orig_N} → {new_N} in s (new_N = position of orig_N in orig_indices)."""
    tmp = s
    for new_i, orig_i in enumerate(orig_indices):
        tmp = tmp.replace(f'{{{orig_i}}}', f'\x00PH{new_i}\x00')
    for new_i in range(len(orig_indices)):
        tmp = tmp.replace(f'\x00PH{new_i}\x00', f'{{{new_i}}}')
    return tmp


def normalise_format(fmt) -> list[str]:
    """Ensure format is always a list of strings.

    repoe-fork returns format as either:
      - a list  e.g. ["#"]  or  ["+#", "#"]
      - a string e.g. "ignore" or "#"   (single-value shortcut)
    """
    if isinstance(fmt, list):
        return fmt
    if isinstance(fmt, str):
        return [fmt]
    return []


def build_regex(template: str, formats: list) -> str | None:
    """Convert a template string with {0},{1}... into a regex pattern."""
    formats = normalise_format(formats)
    escaped = re.escape(strip_markup(template))

    # If the template has no {N} placeholders at all, it's a literal
    # match (e.g. "Culling Strike" with format "ignore").
    if not re.search(r'\\\{', escaped):
        return escaped

    for i, fmt in enumerate(formats):
        placeholder = re.escape(f"{{{i}}}")
        if fmt == "ignore":
            # Value exists but is not displayed; still consume placeholder if present
            if placeholder in escaped:
                escaped = escaped.replace(placeholder, r'(?:-?\d+(?:\.\d+)?)', 1)
            continue
        if "+" in fmt:
            capture = r'([+-]?\d+(?:\.\d+)?)'
        else:
            capture = r'(-?\d+(?:\.\d+)?)'
        if placeholder not in escaped:
            return None  # template has more placeholders than formats
        escaped = escaped.replace(placeholder, capture, 1)

    # Remaining unresolved placeholders → bail
    if re.search(r'\\\{', escaped):
        return None
    return escaped


def render(template: str, values: list, formats: list, handlers: list) -> str:
    """Apply values into a RU template string."""
    formats = normalise_format(formats)
    result = strip_markup(template)
    # If no placeholders, return as-is (literal template like "Culling Strike")
    if not re.search(r'\{\d+\}', result):
        return result
    for i, (raw_val, fmt, hlist) in enumerate(zip(values, formats, handlers)):
        if fmt == "ignore":
            continue
        v = float(raw_val)
        if "negate" in hlist:
            v = -v
        if "milliseconds_to_seconds_2dp_if_required" in hlist:
            v = v / 1000.0
            if v == int(v):
                v_str = str(int(v))
            else:
                v_str = f"{v:.2f}".rstrip("0").rstrip(".")
        elif v == int(v):
            v_str = str(int(v))
        else:
            v_str = str(v)

        if "+" in fmt and v > 0:
            v_str = "+" + v_str

        result = result.replace(f"{{{i}}}", v_str, 1)
    return result


# ── Load tree.json ─────────────────────────────────────────────────────────────
print("Loading tree.json...")
with open(TREE_JSON, encoding="utf-8") as f:
    tree = json.load(f)

all_stats = set()
for node in tree.get("nodes", {}).values():
    for s in node.get("stats", []):
        if s and s.strip():
            all_stats.add(s.strip())

print(f"  Unique stat strings: {len(all_stats)}")

# ── Fetch templates ─────────────────────────────────────────────────────────────
print("\nFetching stat_translations templates...")
en_entries = []
ru_entries = []
for fname in STAT_FILES:
    en_entries.extend(fetch(f"{BASE}/stat_translations/{fname}.min.json"))
    ru_entries.extend(fetch(f"{BASE}/Russian/stat_translations/{fname}.min.json"))

# ── Build RU lookup: id → list of RU variant objects ──────────────────────────
# Priority: FIRST occurrence wins (STAT_FILES order: passive-specific files first,
# generic stat_descriptions last). Later files must NOT overwrite earlier ones,
# because stat_descriptions.min.json maps keystone IDs to their *display names*
# (e.g. keystone_eldritch_battery → "Eldritch Battery") while
# passive_skill_stat_descriptions maps the same ID to the actual description lines.
ru_by_id: dict = {}
for entry in ru_entries:
    variants = entry.get("Russian", [])
    for id_ in entry.get("ids", []):
        if id_ not in ru_by_id:          # first occurrence wins
            ru_by_id[id_] = variants

# ── Build pattern list ─────────────────────────────────────────────────────────
# Each record: (compiled_regex, en_values_count, ru_template, ru_formats, ru_handlers)
patterns = []
skip = 0
for entry in en_entries:
    ids = entry.get("ids", [])
    en_variants = entry.get("English", [])
    if not ids:
        continue
    ru_variants = ru_by_id.get(ids[0], [])
    if not ru_variants:
        continue

    for en_var, ru_var in zip(en_variants, ru_variants):
        en_str  = strip_markup(en_var.get("string", ""))
        ru_str  = strip_markup(ru_var.get("string", ""))
        en_fmts = normalise_format(en_var.get("format", []))
        ru_fmts = normalise_format(ru_var.get("format", en_fmts))
        ru_hdls = ru_var.get("index_handlers", [[] for _ in en_fmts])
        if not isinstance(ru_hdls, list):
            ru_hdls = [[] for _ in en_fmts]

        # ── Multi-line templates ─────────────────────────────────────────────────
        # repoe-fork stores multi-line stat descriptions (e.g. keystones) as a
        # single string with \n separators. tree.json splits them into individual
        # stat strings. We generate one pattern per line so each line matches.
        if '\n' in en_str:
            en_lines = en_str.split('\n')
            ru_lines = ru_str.split('\n')

            def _line_placeholders(s: str) -> list[int]:
                return sorted(int(m.group(1)) for m in re.finditer(r'\{(\d+)\}', s))

            for li, en_line in enumerate(en_lines):
                en_line = strip_bullet(en_line).strip()
                if not en_line:
                    continue
                ru_raw = ru_lines[li] if li < len(ru_lines) else ''
                ru_line = strip_bullet(ru_raw).strip()

                phs = _line_placeholders(en_line)
                if not phs:
                    # Literal line — no values needed
                    line_fmts: list = []
                    line_hdls: list = []
                    en_build = en_line
                    ru_build = ru_line
                elif phs[0] == 0 and phs == list(range(len(phs))):
                    # Consecutive from {0}: use leading formats/handlers
                    line_fmts = en_fmts[:len(phs)]
                    line_hdls = ru_hdls[:len(phs)]
                    en_build = en_line
                    ru_build = ru_line
                else:
                    # Non-sequential {N} (e.g. {1}, {2}): remap to {0}, {1}, ...
                    line_fmts = [en_fmts[i] for i in phs if i < len(en_fmts)]
                    line_hdls = [ru_hdls[i] for i in phs if i < len(ru_hdls)]
                    en_build = remap_placeholders(en_line, phs)
                    ru_build = remap_placeholders(ru_line, phs)

                regex_str = build_regex(en_build, line_fmts)
                if regex_str is None:
                    skip += 1
                    continue
                try:
                    compiled_full   = re.compile(f"^{regex_str}$")
                    compiled_prefix = re.compile(f"^{regex_str}")
                    ru_fmts_line = normalise_format(ru_var.get("format", line_fmts))
                    patterns.append((compiled_full, compiled_prefix,
                                     len(line_fmts), ru_build,
                                     ru_fmts_line[:len(line_fmts)],
                                     line_hdls, en_build))
                except re.error:
                    skip += 1
            continue  # done with this multi-line variant; skip the single-line path

        # ── Single-line template (existing behaviour) ────────────────────────────
        regex_str = build_regex(en_str, en_fmts)
        if regex_str is None:
            skip += 1
            continue
        try:
            compiled_full   = re.compile(f"^{regex_str}$")
            compiled_prefix = re.compile(f"^{regex_str}")
            patterns.append((compiled_full, compiled_prefix, len(en_fmts), ru_str, ru_fmts, ru_hdls, en_str))
        except re.error:
            skip += 1

print(f"\nBuilt {len(patterns)} patterns (skipped {skip})")

MIN_TRUNCATED_LEN = 30  # строки короче не считаем усечёнными

# ── Match each stat string ─────────────────────────────────────────────────────
print("\nMatching stat strings...")
stat_map: dict[str, str] = {}
unmatched = []

for stat in sorted(all_stats):
    matched = False
    # Первый проход: точное совпадение
    for (regex, _prefix, n_vals, ru_tmpl, ru_fmts, ru_hdls, en_str) in patterns:
        m = regex.match(stat)
        if m:
            values = list(m.groups())
            while len(ru_hdls) < len(values): ru_hdls.append([])
            while len(ru_fmts) < len(values): ru_fmts.append("#")
            ru_rendered = render(ru_tmpl, values, ru_fmts, ru_hdls)
            if ru_rendered != stat:
                stat_map[stat] = ru_rendered
            matched = True
            break

    if not matched and len(stat) >= MIN_TRUNCATED_LEN:
        # Второй проход: prefix-матч для усечённых строк
        # (дерево обрезает длинные строки)
        for (_, prefix_re, n_vals, ru_tmpl, ru_fmts, ru_hdls, en_str) in patterns:
            m = prefix_re.match(stat)
            if m and m.end() == len(stat):
                values = list(m.groups())
                while len(ru_hdls) < len(values): ru_hdls.append([])
                while len(ru_fmts) < len(values): ru_fmts.append("#")
                ru_rendered = render(ru_tmpl, values, ru_fmts, ru_hdls)
                if ru_rendered != stat:
                    stat_map[stat] = ru_rendered
                matched = True
                break

    if not matched:
        unmatched.append(stat)

# ── Post-process: "Grants Skill: X" ───────────────────────────────────────────
# Passive nodes often have "Grants Skill: <EnglishSkillName>" which isn't covered
# by stat_translations files.  Translate the skill name part using gems_ru.json.
GEMS_RU_PATH = os.path.join(OUT_DIR, "gems_ru.json")
grants_translated = 0
if os.path.exists(GEMS_RU_PATH):
    print("\nPost-processing 'Grants Skill: ...' entries...")
    with open(GEMS_RU_PATH, encoding="utf-8-sig") as f:
        gems_ru: dict[str, str] = json.load(f)

    still_unmatched = []
    for stat in unmatched:
        if stat.startswith("Grants Skill: "):
            en_skill = stat[len("Grants Skill: "):]
            ru_skill = gems_ru.get(en_skill, en_skill)
            stat_map[stat] = f"Дарует навык: {ru_skill}"
            grants_translated += 1
        else:
            still_unmatched.append(stat)
    unmatched = still_unmatched
    print(f"  'Grants Skill' translated: {grants_translated}")
else:
    print(f"\n[SKIP] gems_ru.json not found at {GEMS_RU_PATH}, skipping 'Grants Skill' post-processing")

# ── Post-process: "Grants N Passive Skill Point(s)" ───────────────────────────
# This stat string is not present in any repoe-fork stat_translations file.
# Translate directly using a regex: "Grants {n} Passive Skill Point(s)".
_GRANTS_POINT_RE = re.compile(r'^Grants (\d+) Passive Skill Points?$')
point_translated = 0
still_unmatched2 = []
for stat in unmatched:
    m = _GRANTS_POINT_RE.match(stat)
    if m:
        n = int(m.group(1))
        # Russian pluralisation: 1 → "очко", 2-4 → "очка", 5+ → "очков"
        if n % 10 == 1 and n % 100 != 11:
            form = "очко"
        elif 2 <= n % 10 <= 4 and not (12 <= n % 100 <= 14):
            form = "очка"
        else:
            form = "очков"
        stat_map[stat] = f"Даёт {n} {form} пассивного навыка"
        point_translated += 1
    else:
        still_unmatched2.append(stat)
unmatched = still_unmatched2
if point_translated:
    print(f"  'Grants Passive Skill Point' translated: {point_translated}")

print(f"  Translated: {len(stat_map)}/{len(all_stats)}")
print(f"  Unmatched:  {len(unmatched)}")
if unmatched[:10]:
    print("  First unmatched examples:")
    for s in unmatched[:10]:
        print(f"    {s!r}")

# ── Write output ───────────────────────────────────────────────────────────────
os.makedirs(OUT_DIR, exist_ok=True)
out_path = os.path.join(OUT_DIR, "passive_nodes_ru.json")
with open(out_path, "w", encoding="utf-8") as f:
    json.dump(stat_map, f, ensure_ascii=False, separators=(",", ":"))

size_kb = os.path.getsize(out_path) // 1024
print(f"\nWrote {len(stat_map)} entries to passive_nodes_ru.json ({size_kb} KB)")
