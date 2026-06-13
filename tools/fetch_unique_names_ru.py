#!/usr/bin/env python3
"""Fetch Russian unique-item names from poe2db.tw and merge them into
PBLApp.Core/Translations/unique_names_ru.json.

Why this exists: repoe-fork dropped its Russian dumps (404) and the GGPK
`Words` table exports English-only for unique titles (GGG localises unique
names through client strings that our pathofexile-dat export doesn't capture).
poe2db.tw renders those same GGG client strings, so its /ru/ pages are the
practical source for Russian unique names until a proper RU Words export
exists.

Input : a newline-delimited list of English unique TITLES (item.title), passed
        as argv[1]. Generate it from the running engine:
            for _, it in pairs(main.uniqueDB.list) do print(it.title or it.name) end
Output: updates unique_names_ru.json in place (sorted, BOM-less UTF-8),
        keeping existing entries and only adding/refreshing where poe2db
        returns a Cyrillic name that differs from the English title.

Extraction: the item page's first <span class="lc"> is the item name, the
second is the base type. We take the first.
"""
import json, re, sys, time, urllib.request, urllib.error
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
OUT  = REPO / "PBLApp.Core" / "Translations" / "unique_names_ru.json"
UA   = "Mozilla/5.0 (PathBuildLab unique-name fetcher)"
LC   = re.compile(r'<span class="lc">([^<]+)</span>')
CYR  = re.compile(r'[А-Яа-яЁё]')

def slug(name: str) -> str:
    # poe2db keeps hyphens ("The_Knight-errant") but drops apostrophes and
    # collapses other punctuation/space runs to a single underscore.
    return re.sub(r'_+', '_', re.sub(r"[^A-Za-z0-9-]+", "_", name.replace("'", ""))).strip("_")

def _get(sl: str):
    url = f"https://poe2db.tw/ru/{sl}"
    req = urllib.request.Request(url, headers={"User-Agent": UA, "Accept": "text/html"})
    for attempt in range(2):                       # one retry — many failures are transient timeouts
        try:
            with urllib.request.urlopen(req, timeout=25) as r:
                html = r.read().decode("utf-8", "replace")
            m = LC.search(html)
            if not m:
                return None
            ru = m.group(1).strip()
            return ru if ru and CYR.search(ru) else None
        except urllib.error.HTTPError as e:
            if e.code == 404:
                return None                        # genuine miss — no point retrying
        except (urllib.error.URLError, TimeoutError):
            time.sleep(0.5)
    return None

def fetch_ru(title: str):
    # Try the full title slug, then progressively drop trailing words: some
    # titles carry the base ("Waistgate Heavy Belt" — the page is /ru/Waistgate).
    words = title.split()
    for cut in range(len(words), 0, -1):
        ru = _get(slug(" ".join(words[:cut])))
        if ru and ru != title:
            return title, ru
    return title, None

def main():
    if len(sys.argv) < 2:
        sys.exit("usage: fetch_unique_names_ru.py <english_titles.txt>")
    titles = [l.strip() for l in Path(sys.argv[1]).read_text(encoding="utf-8").splitlines() if l.strip()]
    existing = json.loads(OUT.read_text(encoding="utf-8-sig")) if OUT.exists() else {}

    todo = [t for t in titles if t not in existing]
    print(f"{len(titles)} titles, {len(existing)} already translated, fetching {len(todo)}")

    added, failed = {}, []
    done = 0
    with ThreadPoolExecutor(max_workers=8) as ex:
        for en, ru in ex.map(fetch_ru, todo):
            done += 1
            if ru:
                added[en] = ru
            else:
                failed.append(en)
            if done % 25 == 0:
                print(f"  [{done}/{len(todo)}] added={len(added)} failed={len(failed)}")

    merged = dict(existing)
    merged.update(added)
    merged = dict(sorted(merged.items(), key=lambda kv: kv[0]))
    OUT.write_text(json.dumps(merged, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"done: +{len(added)} new, {len(failed)} unresolved, total {len(merged)}")
    if failed:
        print("unresolved:", ", ".join(sorted(failed)[:40]), "..." if len(failed) > 40 else "")

if __name__ == "__main__":
    main()
