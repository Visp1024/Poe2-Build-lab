// Build a preliminary icon map from extracted PBLExport/ggpk_export/tables/*.json.
//
// Output: PBLExport/icons/icon_map.preliminary.json
//   {
//     bases:   { [enName]: { dds, url, file, w, h, class, ru } },
//     uniques: { [enName]: { dds, url, file, w, h, alt, ru } }
//   }
//
// `url` points at cdn.poe2db.tw (which mirrors GGPK item art as .webp without
// signed-hash URLs that GGG's own CDN requires). Items not present there will
// be dropped by the downloader pass.

import { readFileSync, writeFileSync, mkdirSync } from 'fs';
import { fileURLToPath } from 'url';
import path from 'path';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const REPO = path.resolve(__dirname, '..');
const TABLES = path.join(REPO, 'PBLExport', 'ggpk_export', 'tables');
const OUT_DIR = path.join(REPO, 'PBLExport', 'icons');
mkdirSync(OUT_DIR, { recursive: true });

function load(lang, name) {
    return JSON.parse(readFileSync(path.join(TABLES, lang, name + '.json'), 'utf8'));
}

const en = {
    base:    load('English', 'BaseItemTypes'),
    ivi:     load('English', 'ItemVisualIdentity'),
    usl:     load('English', 'UniqueStashLayout'),
    words:   load('English', 'Words'),
    classes: load('English', 'ItemClasses'),
};
const ru = {
    base:  load('Russian', 'BaseItemTypes'),
    words: load('Russian', 'Words'),
};

function ddsToUrl(dds) {
    if (!dds || !dds.toLowerCase().endsWith('.dds')) return null;
    const webp = dds.replace(/\.dds$/i, '.webp');
    return {
        url: 'https://cdn.poe2db.tw/image/' + webp,
        file: webp,
    };
}

const bases = {};
let baseSkipNoIvi = 0, baseSkipDup = 0;
for (const r of en.base) {
    const ivi = en.ivi[r.ItemVisualIdentity];
    if (!ivi || !ivi.DDSFile) { baseSkipNoIvi++; continue; }
    const link = ddsToUrl(ivi.DDSFile);
    if (!link) continue;
    const name = r.Name;
    if (!name) continue;
    const cls = en.classes[r.ItemClass];
    const ruName = ru.base[r._index]?.Name;
    if (bases[name]) { baseSkipDup++; continue; }
    bases[name] = {
        dds: ivi.DDSFile,
        url: link.url,
        file: link.file,
        w: r.Width,
        h: r.Height,
        class: cls?.Id ?? null,
        ru: ruName && ruName !== name ? ruName : undefined,
    };
}

const uniques = {};
let uSkipNoIvi = 0, uSkipDup = 0;
for (const r of en.usl) {
    const ivi = en.ivi[r.ItemVisualIdentityKey];
    const w = en.words[r.WordsKey];
    if (!ivi || !ivi.DDSFile || !w?.Text) { uSkipNoIvi++; continue; }
    const link = ddsToUrl(ivi.DDSFile);
    if (!link) continue;
    const name = w.Text;
    const ruName = ru.words[r.WordsKey]?.Text;
    // Multiple stash layouts can share a name (renamed/alt-art); prefer non-alt + earliest.
    const existing = uniques[name];
    if (existing) {
        if (!existing.alt && r.IsAlternateArt) { uSkipDup++; continue; }
        if (existing.alt && !r.IsAlternateArt) {
            // replace alt-art entry with primary
        } else {
            uSkipDup++; continue;
        }
    }
    uniques[name] = {
        dds: ivi.DDSFile,
        url: link.url,
        file: link.file,
        alt: !!r.IsAlternateArt,
        ru: ruName && ruName !== name ? ruName : undefined,
    };
}

const out = { bases, uniques };
const outPath = path.join(OUT_DIR, 'icon_map.preliminary.json');
writeFileSync(outPath, JSON.stringify(out, null, 2));

console.log('bases:    %d  (skipped no-IVI=%d dup=%d)', Object.keys(bases).length, baseSkipNoIvi, baseSkipDup);
console.log('uniques:  %d  (skipped no-IVI=%d dup=%d)', Object.keys(uniques).length, uSkipNoIvi, uSkipDup);
console.log('wrote ->', path.relative(REPO, outPath));
