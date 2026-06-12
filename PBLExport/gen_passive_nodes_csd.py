# PBLExport/gen_passive_nodes_csd.py
#
# Дополняет passive_nodes_ru.json переводами стат-строк дерева из GGPK .csd
# (выгружаются pathofexile-dat'ом, см. ggpk_export/config.json -> "files").
#
# Зачем: repoe-fork путь (gen_passive_ru.py) сломан — формат шаблонов
# изменился. .csd — первоисточник из самой игры: каждый блок description
# содержит EN-шаблон и RU-шаблон одного стата. Матчим уже отрендеренную
# EN-строку из tree.json по EN-шаблону (regex), переносим числа в RU-шаблон.
#
# Запуск:  python PBLExport/gen_passive_nodes_csd.py [--tree 0_5] [--dry-run]
# Существующие переводы в passive_nodes_ru.json не перезаписываются.
import json
import re
import sys
import io
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
CSD_DIR = ROOT / "PBLExport" / "ggpk_export" / "files"
TRANS = ROOT / "PBLApp.Core" / "Translations" / "passive_nodes_ru.json"
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")

TREE_VER = "0_5"
DRY = "--dry-run" in sys.argv
if "--tree" in sys.argv:
    TREE_VER = sys.argv[sys.argv.index("--tree") + 1]

CSD_FILES = [
    "Data@StatDescriptions@passive_skill_stat_descriptions.csd",
    "Data@StatDescriptions@passive_skill_aura_stat_descriptions.csd",
    "Data@StatDescriptions@stat_descriptions.csd",
    # некоторые ноды (особенно аскендства) рендерятся гемовыми статами
    "Data@StatDescriptions@gem_stat_descriptions.csd",
    "Data@StatDescriptions@meta_gem_stat_descriptions.csd",
    "Data@StatDescriptions@advanced_mod_stat_descriptions.csd",
]

BULLET_RE = re.compile(r"^[•◆●○◦‣▪–-]\s*")

# Строки, для которых в GGPK НЕТ русского текста (новые 0.5-статы без
# языковых секций в .csd, либо RU сливает строки). Ручной перевод в
# официальной терминологии; при появлении официального RU в .csd блоки
# начнут матчиться раньше и эти записи перестанут использоваться.
HAND_FIXES = {
    "Can tattoo Runes onto your body, gaining":
        "Позволяет наносить руны на ваше тело в виде татуировок,",
    "additional Rune-only sockets:":
        "добавляя гнёзда только для рун:",
    "100 Passive Skill Points become Weapon Set Skill Points":
        "100 очков пассивных навыков становятся очками навыков набора оружия",
    "5% reduced Movement Speed Penalty from using Cold Skills while moving":
        "5% снижение штрафа к скорости передвижения от использования навыков холода во время движения",
    "5% reduced Movement Speed Penalty from using Fire Skills while moving":
        "5% снижение штрафа к скорости передвижения от использования навыков огня во время движения",
    "6% increased Cast Speed per Spell Echoed Recently, up to 30%":
        "6% повышение скорости сотворения чар за каждые чары, повторённые эхом недавно, вплоть до 30%",
    "Archon Buffs also grant 30% increased Critical Hit Chance":
        "Баффы архонта также дают 30% повышение шанса критического удара",
    "Archon Buffs also grant 50% increased Critical Damage Bonus":
        "Баффы архонта также дают 50% повышение бонуса к критическому урону",
    "Gain 6% of Cold damage as Extra Lightning damage":
        "6% урона от холода становится дополнительным уроном от молнии",
    "Base Unarmed Physical damage replaced with damage based on their Skill Level":
        "Базовый физический урон без оружия заменяется уроном, зависящим от уровня навыка",
}

MARKUP_RE = re.compile(r"\[([^|\]]+)\|([^\]]*)\]|\[([^\]]*)\]")
PLACEHOLDER_RE = re.compile(r"\{(\d*)(?::[^}]*)?\}")


def strip_markup(s: str) -> str:
    return MARKUP_RE.sub(lambda m: m.group(2) if m.group(2) is not None else m.group(3), s)


def parse_csd(path: Path):
    """Возвращает список блоков: {'en': [(conds, template)], 'ru': [...]}"""
    text = path.read_text(encoding="utf-16", errors="replace") \
        if path.read_bytes()[:2] in (b"\xff\xfe", b"\xfe\xff") \
        else path.read_text(encoding="utf-8", errors="replace")
    lines = text.splitlines()
    blocks = []
    i = 0
    n = len(lines)
    while i < n:
        if lines[i].strip() != "description" and not lines[i].strip().startswith("description "):
            i += 1
            continue
        i += 1
        if i >= n:
            break
        # строка статов: "<count> <id1> <id2>..."
        stat_line = lines[i].strip()
        m = re.match(r"^(\d+)\s+(.*)$", stat_line)
        if not m:
            continue
        i += 1
        block = {"en": [], "ru": []}
        cur_lang = "en"
        while i < n:
            s = lines[i].strip()
            if s == "description" or s.startswith("description ") or not lines[i].startswith(("\t", " ")) and s:
                break
            lm = re.match(r'^lang "(.+)"$', s)
            if lm:
                cur_lang = "ru" if lm.group(1) == "Russian" else lm.group(1)
                i += 1
                continue
            if re.match(r"^\d+$", s):
                i += 1
                continue
            tm = re.match(r'^(.*?)"(.*)"(.*)$', s)
            if tm and cur_lang in ("en", "ru"):
                conds = tm.group(1).split()
                template = tm.group(2)
                block[cur_lang].append((conds, template))
            i += 1
        if block["en"]:
            blocks.append(block)
    return blocks


def template_to_regex(template: str):
    """EN-шаблон -> (compiled regex, [индексы плейсхолдеров в порядке групп])."""
    template = strip_markup(template)
    order = []
    pos = 0
    out = []
    auto_idx = 0
    for m in PLACEHOLDER_RE.finditer(template):
        out.append(re.escape(template[pos:m.start()]))
        idx = int(m.group(1)) if m.group(1) else auto_idx
        auto_idx = idx + 1
        order.append(idx)
        out.append(r"([+-]?[\d]+(?:[.,]\d+)?)")
        pos = m.end()
    out.append(re.escape(template[pos:]))
    try:
        return re.compile("^" + "".join(out) + "$"), order
    except re.error:
        return None, order


def cond_ok(cond: str, value: float) -> bool:
    if cond == "#":
        return True
    if cond.startswith("!"):
        try:
            return value != float(cond[1:])
        except ValueError:
            return True
    if "|" in cond:
        lo, hi = cond.split("|", 1)
        if lo != "#":
            try:
                if value < float(lo):
                    return False
            except ValueError:
                pass
        if hi != "#":
            try:
                if value > float(hi):
                    return False
            except ValueError:
                pass
        return True
    try:
        return value == float(cond)
    except ValueError:
        return True


def render_ru(ru_template: str, values: dict) -> str | None:
    """Подставляет захваченные display-строки в RU-шаблон."""
    out = strip_markup(ru_template)
    auto_idx = 0
    def sub(m):
        nonlocal auto_idx
        idx = int(m.group(1)) if m.group(1) else auto_idx
        auto_idx = idx + 1
        return values.get(idx, m.group(0))
    res = PLACEHOLDER_RE.sub(sub, out)
    return None if PLACEHOLDER_RE.search(res) else res


def expand_pairs(block):
    """EN/RU-варианты блока -> список (en_subline, ru_subline) c учётом \\n."""
    pairs = []
    ru_list = block["ru"]
    for vi, (en_conds, en_t) in enumerate(block["en"]):
        # RU-вариант: тот же индекс, иначе первый
        ru_candidates = ru_list if ru_list else []
        en_lines = en_t.split("\\n")
        for ru_conds, ru_t in (ru_candidates or [(en_conds, en_t)]):
            ru_lines = ru_t.split("\\n")
            if len(ru_lines) != len(en_lines):
                # RU иногда сливает заголовочные строки (например, keystone
                # с татуировками) — выравниваем буллет-хвосты по концу
                n_tail = min(len(en_lines), len(ru_lines))
                en_lines_p = en_lines[-n_tail:]
                ru_lines_p = ru_lines[-n_tail:]
            else:
                en_lines_p, ru_lines_p = en_lines, ru_lines
            for el, rl in zip(en_lines_p, ru_lines_p):
                # tree.json срезает буллеты у подстрок многострочных статов
                el = BULLET_RE.sub("", el.strip()).strip()
                rl = BULLET_RE.sub("", rl.strip()).strip()
                pairs.append((el, rl, ru_conds, vi))
    return pairs


def main():
    # 1. собрать недостающие строки из tree.json
    tree = json.loads((ROOT / "src" / "TreeData" / TREE_VER / "tree.json")
                      .read_text(encoding="utf-8"))
    existing = json.loads(TRANS.read_text(encoding="utf-8-sig"))
    missing = set()
    for node in tree["nodes"].values():
        if not isinstance(node, dict):
            continue
        for s in node.get("stats", []):
            for line in s.split("\n"):
                line = line.strip()
                if line and line not in existing:
                    missing.add(line)
    print(f"tree {TREE_VER}: missing stat lines: {len(missing)}")

    # 2. распарсить csd и собрать матчеры
    matchers = []  # (regex, order, ru_template, ru_conds)
    for f in CSD_FILES:
        path = CSD_DIR / f
        if not path.exists():
            print(f"  [skip] {f} — not exported")
            continue
        blocks = parse_csd(path)
        cnt = 0
        for b in blocks:
            for en_line, ru_line, ru_conds, _ in expand_pairs(b):
                rx, order = template_to_regex(en_line)
                if rx and en_line != ru_line:
                    matchers.append((rx, order, ru_line, ru_conds))
                    cnt += 1
        print(f"  {f}: {len(blocks)} blocks, {cnt} EN/RU pairs")

    # 2b. "Grants Skill: X" — через gems_ru.json (как фаза 3 в gen_passive_ru.py),
    # с фоллбэком на GGPK ActiveSkills (DisplayedName EN -> RU) для новых навыков
    gems_ru = {}
    gems_path = ROOT / "PBLApp.Core" / "Translations" / "gems_ru.json"
    if gems_path.exists():
        gems_ru = json.loads(gems_path.read_text(encoding="utf-8-sig"))
    as_en = CSD_DIR.parent / "tables" / "English" / "ActiveSkills.json"
    as_ru = CSD_DIR.parent / "tables" / "Russian" / "ActiveSkills.json"
    if as_en.exists() and as_ru.exists():
        en_rows = json.loads(as_en.read_text(encoding="utf-8-sig"))
        ru_rows = json.loads(as_ru.read_text(encoding="utf-8-sig"))
        ru_by_id = {r["Id"]: r.get("DisplayedName") or "" for r in ru_rows}
        for r in en_rows:
            en_name = r.get("DisplayedName") or ""
            ru_name = ru_by_id.get(r["Id"], "")
            if en_name and ru_name and en_name != ru_name and en_name not in gems_ru:
                gems_ru[en_name] = ru_name

    # 3. матчим
    added, unmatched = {}, []
    for line in sorted(missing):
        if line in HAND_FIXES:
            added[line] = HAND_FIXES[line]
            continue
        gm = re.match(r"^Grants Skill: (.+)$", line)
        if gm and gm.group(1) in gems_ru:
            added[line] = f"Дарует навык: {gems_ru[gm.group(1)]}"
            continue
        best = None
        for rx, order, ru_t, ru_conds in matchers:
            m = rx.match(line)
            if not m:
                continue
            values = {idx: m.group(gi + 1) for gi, idx in enumerate(order)}
            # условия RU-варианта (по первому значению)
            ok = True
            for ci, cond in enumerate(ru_conds):
                v = values.get(ci)
                if v is not None:
                    try:
                        if not cond_ok(cond, float(v.replace(",", "."))):
                            ok = False
                            break
                    except ValueError:
                        pass
            ru = render_ru(ru_t, values)
            if ru is None:
                continue
            if ok:
                best = ru
                break
            best = best or ru  # запасной вариант при несовпавших условиях
        if best:
            added[line] = best
        else:
            unmatched.append(line)

    print(f"translated: {len(added)}/{len(missing)}, unmatched: {len(unmatched)}")
    for u in unmatched[:15]:
        print(f"    UNMATCHED: {u[:110]}")

    if DRY:
        for k in sorted(added)[:20]:
            print(f"    {k[:60]}  ->  {added[k][:60]}")
        return

    merged = dict(existing)
    merged.update(added)
    TRANS.write_text(json.dumps(dict(sorted(merged.items())), ensure_ascii=False, indent=1),
                     encoding="utf-8")
    print(f"passive_nodes_ru.json: {len(existing)} -> {len(merged)} entries")
    if unmatched:
        out = ROOT / "tools" / "loc_unmatched_node_stats.txt"
        out.write_text("\n".join(unmatched), encoding="utf-8")
        print(f"unmatched -> {out}")


if __name__ == "__main__":
    main()
