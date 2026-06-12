# tools/loc_coverage.py
# Измеряет покрытие RU-локализации против актуальных данных:
#   1. имена нод дерева 0_5      vs passive_names_ru.json
#   2. стат-строки нод дерева 0_5 vs passive_nodes_ru.json
#   3. описания гемов из Gems.lua vs skill_descriptions_ru.json
# Запуск: python tools/loc_coverage.py [--dump-missing PREFIX]
import json, re, sys, io
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
TR = ROOT / "PBLApp.Core" / "Translations"
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")

def load(name):
    return json.loads((TR / name).read_text(encoding="utf-8-sig"))

def report(label, total, missing, dump_prefix=None, dump_name=None):
    pct = 100.0 * (total - len(missing)) / total if total else 100.0
    print(f"{label}: {total - len(missing)}/{total} ({pct:.1f}%), missing {len(missing)}")
    for s in sorted(missing)[:10]:
        print(f"    MISS: {s[:110]}")
    if dump_prefix and missing:
        out = ROOT / "tools" / f"{dump_prefix}_{dump_name}.txt"
        out.write_text("\n".join(sorted(missing)), encoding="utf-8")
        print(f"    -> full list: {out}")

dump = sys.argv[2] if len(sys.argv) > 2 and sys.argv[1] == "--dump-missing" else None

# ---- 1+2. Tree 0_5 ----------------------------------------------------------
tree = json.loads((ROOT / "src" / "TreeData" / "0_5" / "tree.json").read_text(encoding="utf-8"))
names_ru = load("passive_names_ru.json")
nodes_ru = load("passive_nodes_ru.json")

node_names, node_stats = set(), set()
for node in tree["nodes"].values():
    if not isinstance(node, dict) or "name" not in node:
        continue
    # служебные: прокси, джевел-сокеты без имени и т.п.
    if node.get("isProxy") or node.get("isJewelSocket"):
        continue
    node_names.add(node["name"])
    for s in node.get("stats", []):
        for line in s.split("\n"):
            line = line.strip()
            if line:
                node_stats.add(line)

# рантайм: PassiveName -> fallback ClassOrAscendancyName (стартовые ноды восхождений)
class_ru = load("class_names_ru.json")
missing_names = {n for n in node_names if n not in names_ru and n not in class_ru}
report("Tree 0_5 node names ", len(node_names), missing_names, dump, "missing_node_names")
report("Tree 0_5 node stats ", len(node_stats), {s for s in node_stats if s not in nodes_ru},
       dump, "missing_node_stats")

# ---- 3. Gem descriptions ----------------------------------------------------
descs = set()
for f in (ROOT / "src" / "Data" / "Skills").glob("*.lua"):
    text = f.read_text(encoding="utf-8")
    for d in re.findall(r'description\s*=\s*"((?:[^"\\]|\\.)*)"', text):
        if d:
            descs.add(d.replace('\\"', '"').replace("\\n", "\n"))
skill_ru = load("skill_descriptions_ru.json")
# рантайм (GameTranslationService.SkillDescription): exact -> Trim() ->
# longest-common-prefix среди ключей с общими первыми 50 символами
def lcp_hit(t):
    if len(t) < 50:
        return False
    tl = t.lower()
    best, hit, tie = 0, None, False
    for k in skill_ru:
        if len(k) < 50 or k[:50].lower() != tl[:50]:
            continue
        kl = k.lower()
        n = min(len(kl), len(tl))
        i = 50
        while i < n and kl[i] == tl[i]:
            i += 1
        if i > best:
            best, hit, tie = i, k, False
        elif i == best:
            tie = True
    return hit is not None and not tie

missing_descs = set()
for d in descs:
    t = d.strip()
    if d in skill_ru or t in skill_ru or lcp_hit(t):
        continue
    missing_descs.add(d)
report("Gem descriptions    ", len(descs), missing_descs, dump, "missing_gem_descs")
