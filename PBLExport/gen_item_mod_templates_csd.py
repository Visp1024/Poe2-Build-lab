r"""
Generate item_mod_templates_ru.json — number-redacted EN -> RU templates for the
item-mod / stat-line translator (GameTranslationService.TooltipLine fallback),
from the GGPK .csd stat-description files.

Why: item mods that PoB's Lua layer doesn't translate fall back to the curated
`_itemModTemplates` dict in C#, which only covers ~180 lines by hand. The .csd
files are the game's own source (EN + RU templates with the same placeholder
model), so we can auto-generate the redacted-template dict the runtime already
understands and cover thousands of mods.

How the runtime matches (must mirror exactly):
  template = NumberRx.Replace(line, "#")            # redact the displayed EN line
  if dict.TryGetValue(template, out ru):
      numbers = NumberRx.Matches(line)              # captured, in order (leading '-'
                                                    #   is INSIDE the match, '+' is NOT)
      return Regex.Replace(ru, "#", -> numbers[i++])# inject into RU '#'s, in order
NumberRx = -?\d+(?:[.,]\d+)?

So each entry is built by SIMULATION: substitute every placeholder with a
distinct sample number (signed per its format), run the exact redaction to get
the EN key + ordered numbers, redact the RU side too, and keep the pair only if
injecting the EN numbers back into the RU key reproduces the rendered RU string
(guards against placeholder reordering / EN-only or RU-only constants).

Output: flat { "redacted EN": "redacted RU" }. First file in priority order
wins per EN key. Existing entries are not consulted (hand-curated
_itemModTemplates always wins at runtime — it's checked first there).

Usage:  python PBLExport/gen_item_mod_templates_csd.py [--dry-run]
"""

import json, re, os, sys, io

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")

HERE     = os.path.dirname(__file__)
CSD_DIR  = os.path.join(HERE, "ggpk_export", "files")
OUT_DIR  = os.path.join(HERE, "..", "PBLApp.Core", "Translations")
OUT_PATH = os.path.join(OUT_DIR, "item_mod_templates_ru.json")
DRY      = "--dry-run" in sys.argv

# Generic first so it wins per redacted-EN key; gem/meta/passive add the rest.
CSD_FILES = [
    "Data@StatDescriptions@stat_descriptions.csd",
    "Data@StatDescriptions@advanced_mod_stat_descriptions.csd",
    "Data@StatDescriptions@gem_stat_descriptions.csd",
    "Data@StatDescriptions@meta_gem_stat_descriptions.csd",
    "Data@StatDescriptions@passive_skill_stat_descriptions.csd",
    "Data@StatDescriptions@passive_skill_aura_stat_descriptions.csd",
]

MARKUP_RE      = re.compile(r"\[([^|\]]+)\|([^\]]*)\]|\[([^\]]*)\]")
PLACEHOLDER_RE = re.compile(r"\{(\d*)(?::([^}]*))?\}")
NUM_RE         = re.compile(r"-?\d+(?:[.,]\d+)?")   # mirrors C# NumberRx
BULLET_RE      = re.compile(r"^[•◆●○◦‣▪–-]\s*")


def read_text(path):
    b = open(path, "rb").read()
    if b[:2] in (b"\xff\xfe", b"\xfe\xff"):
        return b.decode("utf-16", errors="replace")
    return b.decode("utf-8", errors="replace")


def strip_markup(s):
    return MARKUP_RE.sub(lambda m: m.group(2) if m.group(2) is not None else m.group(3), s)


def sample(idx):
    # Distinct, unlikely to collide with literal template constants (75, 100, ...).
    return str(7001 + idx * 137)


def render(sub):
    """Substitute placeholders with signed sample numbers (mirrors a positive-valued
    display). Returns the rendered string, or None on a malformed placeholder."""
    auto = [0]

    def repl(m):
        idx = int(m.group(1)) if m.group(1) else auto[0]
        auto[0] = idx + 1
        fmt = m.group(2) or ""
        s = sample(idx)
        return ("+" + s) if fmt.startswith("+") else s

    return PLACEHOLDER_RE.sub(repl, sub)


def make_pair(en_sub, ru_sub):
    """(en_sub, ru_sub) raw templates -> (en_key, ru_key) redacted, or None."""
    en_sub = BULLET_RE.sub("", strip_markup(en_sub).strip()).strip()
    ru_sub = BULLET_RE.sub("", strip_markup(ru_sub).strip()).strip()
    if not en_sub or not ru_sub:
        return None

    en_disp, ru_disp = render(en_sub), render(ru_sub)
    en_key  = NUM_RE.sub("#", en_disp)
    en_nums = NUM_RE.findall(en_disp)
    ru_key  = NUM_RE.sub("#", ru_disp)

    # Keep only if sequentially injecting the EN numbers into the RU key
    # reproduces the rendered RU exactly (rejects reordering / lone constants).
    if ru_key.count("#") != len(en_nums):
        return None
    it = iter(en_nums)
    recon = re.sub("#", lambda _: next(it), ru_key)
    if recon != ru_disp:
        return None

    if en_key == ru_key or not re.search(r"[A-Za-z]", en_key):
        return None
    return en_key, ru_key


VAR_RE = re.compile(r'^(.*?)"(.*)"(.*)$')


def parse_csd(path):
    """-> list of blocks {en:[template...], ru:[template...]}"""
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
        i += 1
        block = {"en": [], "ru": []}
        cur = "en"
        while i < n:
            s = lines[i].strip()
            if s == "description":
                break
            lm = re.match(r'^lang "(.+)"$', s)
            if lm:
                cur = "ru" if lm.group(1) == "Russian" else "other"
                i += 1
                continue
            if re.match(r"^\d+$", s):
                i += 1
                continue
            vm = VAR_RE.match(s)
            if vm and cur in ("en", "ru"):
                block[cur].append(vm.group(2))
            i += 1
        if block["en"] and block["ru"]:
            out.append(block)
    return out


def variant_pairs(block):
    """Yield aligned (en_template, ru_template) variant pairs, conservatively."""
    en, ru = block["en"], block["ru"]
    if len(en) == len(ru):
        yield from zip(en, ru)
    elif len(ru) == 1:
        for e in en:
            yield e, ru[0]
    # otherwise ambiguous -> skip the block


def main():
    out = {}
    for f in CSD_FILES:
        path = os.path.join(CSD_DIR, f)
        if not os.path.exists(path):
            print(f"  [skip] {f} — not exported")
            continue
        added = 0
        for b in parse_csd(path):
            for en_t, ru_t in variant_pairs(b):
                en_lines = en_t.split("\\n")
                ru_lines = ru_t.split("\\n")
                if len(en_lines) != len(ru_lines):
                    continue  # multiline misalignment -> skip
                for el, rl in zip(en_lines, ru_lines):
                    pair = make_pair(el, rl)
                    if pair and pair[0] not in out:
                        out[pair[0]] = pair[1]
                        added += 1
        print(f"  {f}: +{added} templates")

    print(f"total: {len(out)} item-mod templates")

    if DRY:
        for k in list(out)[:25]:
            print(f"    {k[:55]}  ->  {out[k][:55]}")
        return

    os.makedirs(OUT_DIR, exist_ok=True)
    with open(OUT_PATH, "w", encoding="utf-8") as fh:
        json.dump(dict(sorted(out.items())), fh, ensure_ascii=False, indent=1)
    kb = os.path.getsize(OUT_PATH) // 1024
    print(f"Wrote {len(out)} entries to item_mod_templates_ru.json ({kb} KB)")


if __name__ == "__main__":
    main()
