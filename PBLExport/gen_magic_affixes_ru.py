# PBLExport/gen_magic_affixes_ru.py
#
# Дополняет magic_affixes_ru.json (prefixes/suffixes) переводами магических
# аффиксов флаконов и оберегов из GGPK-таблицы Mods (EN+RU, выгружается
# pathofexile-dat'ом, см. ggpk_export/config.json -> "tables": "Mods").
#
# Почему именно флаконы/обереги: Domain == 2 в Mods — это пул аффиксов
# флаконов и оберегов. И флакон ("Флакон"), и оберег ("Оберег") мужского
# рода, поэтому из родовой разметки префикса берём форму MS (masculine
# singular) — она корректна для обоих.
#
# Формат RU-имени префикса в Mods:
#   <if:MS>{Солнечный}<elif:FS>{Солнечная}<elif:NS>{Солнечное}...<elif:NP>{...}
# Суффиксы ("of the X") хранятся простой строкой ("непрерывности").
#
# GenerationType: 1 = префикс, 2 = суффикс.
# При коллизии (тот же EN-аффикс уже есть с другим переводом) побеждает GGPK:
# это официальный игровой текст именно для флаконов/оберегов (Domain 2), а
# существующие значения часто неофициальные (напр. "Кипящий" вместо "Бурлящий").
# Записи, которых нет в Domain 2, не трогаются.
#
# Запуск: python PBLExport/gen_magic_affixes_ru.py [--dry-run]
import json
import re
import sys
import io
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
TABLES = ROOT / "PBLExport" / "ggpk_export" / "tables"
OUT = ROOT / "PBLApp.Core" / "Translations" / "magic_affixes_ru.json"
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")

DRY = "--dry-run" in sys.argv
FLASK_CHARM_DOMAIN = 2
MS_RE = re.compile(r"<if:MS>\{([^}]*)\}")
BRACE_RE = re.compile(r"^\{([^}]*)\}$")


def load(p):
    return json.loads(p.read_text(encoding="utf-8-sig"))


def ru_name(raw: str) -> str | None:
    """Extract the masculine-singular RU form (flasks/charms are masculine)."""
    raw = (raw or "").strip()
    if not raw:
        return None
    m = MS_RE.search(raw)
    if m:
        return m.group(1).strip() or None
    b = BRACE_RE.match(raw)
    if b:
        return b.group(1).strip() or None
    # plain string (typical for suffixes)
    return raw if "<" not in raw and "{" not in raw else None


def main():
    en = load(TABLES / "English" / "Mods.json")
    ru = load(TABLES / "Russian" / "Mods.json")
    ru_by_idx = {r["_index"]: r for r in ru}

    cur = load(OUT) if OUT.exists() else {"prefixes": {}, "suffixes": {}}
    pre = dict(cur.get("prefixes", {}))
    suf = dict(cur.get("suffixes", {}))
    before = (len(pre), len(suf))

    add_p = add_s = overwritten = 0
    collisions = {}
    seen_intra = {}
    for r in en:
        if r.get("Domain") != FLASK_CHARM_DOMAIN:
            continue
        en_name = (r.get("Name") or "").strip()
        if not en_name:
            continue
        rr = ru_by_idx.get(r["_index"])
        if not rr:
            continue
        rn = ru_name(rr.get("Name"))
        if not rn or rn == en_name:
            continue
        is_suffix = en_name.startswith("of ") or r.get("GenerationType") == 2
        bucket = suf if is_suffix else pre
        # intra-domain duplicates of the same EN name: keep the first, note conflicts
        if en_name in seen_intra and seen_intra[en_name] != rn:
            collisions.setdefault(en_name, set()).update({seen_intra[en_name], rn})
            continue
        seen_intra[en_name] = rn
        if en_name in bucket:
            if bucket[en_name] != rn:
                overwritten += 1
        elif is_suffix:
            add_s += 1
        else:
            add_p += 1
        bucket[en_name] = rn   # GGPK (official flask/charm text) wins

    pre = dict(sorted(pre.items()))
    suf = dict(sorted(suf.items()))
    out = {"prefixes": pre, "suffixes": suf}
    print(f"prefixes {before[0]} -> {len(pre)} (+{add_p}), suffixes {before[1]} -> {len(suf)} (+{add_s}), "
          f"{overwritten} existing overwritten with official GGPK text")
    if collisions:
        print(f"{len(collisions)} intra-domain ambiguous names (kept first):")
        for k, v in list(collisions.items())[:20]:
            print("   ", repr(k), "->", v)
    if DRY:
        print("(dry-run, not written)")
        return
    OUT.write_text(json.dumps(out, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"wrote {OUT}")


if __name__ == "__main__":
    main()
