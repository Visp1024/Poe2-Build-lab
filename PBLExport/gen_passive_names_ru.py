"""
Generate passive_names_ru.json: mapping of English passive node names
to Russian translations, using repoe-fork skills data.

Usage: python gen_passive_names_ru.py
Output: ../PBLApp.Core/Translations/passive_names_ru.json
"""

import json, urllib.request, os

BASE    = "https://repoe-fork.github.io/poe2"
TREE_JSON = os.path.join(os.path.dirname(__file__), "../src/TreeData/0_4/tree.json")
OUT_DIR   = os.path.join(os.path.dirname(__file__), "../PBLApp.Core/Translations")


def fetch_json(url):
    print(f"  Fetching {url}...", flush=True)
    try:
        with urllib.request.urlopen(url, timeout=60) as r:
            data = json.loads(r.read().decode("utf-8"))
            print(f"  OK ({len(data)} entries)")
            return data
    except Exception as e:
        print(f"  FAILED: {e}")
        return {}


# ── Load passive node names from tree.json ─────────────────────────────────────
print("Loading tree.json...")
with open(TREE_JSON, encoding="utf-8") as f:
    tree = json.load(f)

node_names: set[str] = set()
for node in tree.get("nodes", {}).values():
    name = node.get("name", "").strip()
    if name:
        node_names.add(name)

print(f"  Unique passive node names: {len(node_names)}")

# ── Fetch English and Russian skills data ───────────────────────────────────────
print("\nFetching skills data...")
en_skills = fetch_json(f"{BASE}/skills.min.json")
ru_skills = fetch_json(f"{BASE}/Russian/skills.min.json")

if not en_skills or not ru_skills:
    print("Failed to fetch skills data — aborting.")
    raise SystemExit(1)

# ── Build name mapping: EN display_name → RU display_name ─────────────────────
print("\nBuilding name mapping...")
name_map: dict[str, str] = {}
no_name = 0

for skill_id, en_entry in en_skills.items():
    en_name = ""
    # Try active_skill.display_name first
    active = en_entry.get("active_skill") or {}
    en_name = active.get("display_name", "").strip()

    if not en_name or en_name not in node_names:
        continue

    # Look up same skill in Russian
    ru_entry = ru_skills.get(skill_id)
    if not ru_entry:
        no_name += 1
        continue

    ru_active = ru_entry.get("active_skill") or {}
    ru_name = ru_active.get("display_name", "").strip()

    if ru_name and ru_name != en_name:
        name_map[en_name] = ru_name

print(f"  Translated: {len(name_map)}/{len(node_names)}")
print(f"  Not found in RU: {no_name}")
unmatched = node_names - set(name_map.keys())
print(f"  Unmatched: {len(unmatched)}")
if unmatched:
    print("  First 10 unmatched:")
    for n in sorted(unmatched)[:10]:
        print(f"    {n!r}")

# ── Write output ───────────────────────────────────────────────────────────────
os.makedirs(OUT_DIR, exist_ok=True)
out_path = os.path.join(OUT_DIR, "passive_names_ru.json")
with open(out_path, "w", encoding="utf-8") as f:
    json.dump(name_map, f, ensure_ascii=False, separators=(",", ":"))

size_kb = os.path.getsize(out_path) // 1024
print(f"\nWrote {len(name_map)} entries to passive_names_ru.json ({size_kb} KB)")
