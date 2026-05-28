// Download item icons from cdn.poe2db.tw based on the preliminary icon map.
//
// - Reads PBLExport/icons/icon_map.preliminary.json
// - Deduplicates by `file` path (many name aliases share one image)
// - Downloads only what's missing on disk; idempotent
// - HTTP 200 -> save + keep entry; 403/404/other -> drop entry
// - Writes:
//     PBLExport/icons/cache/<DDS-mirrored path as .webp>
//     PBLExport/icons/icon_map.json    (filtered to entries that exist)
//     PBLExport/icons/missing.json     (entries dropped, for diagnostics)

import { readFileSync, writeFileSync, existsSync, mkdirSync, statSync } from 'fs';
import { fileURLToPath } from 'url';
import path from 'path';
import { pipeline } from 'stream/promises';
import { createWriteStream } from 'fs';
import { Readable } from 'stream';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const REPO = path.resolve(__dirname, '..');
const ICON_DIR = path.join(REPO, 'PBLExport', 'icons');
const CACHE = path.join(ICON_DIR, 'cache');
mkdirSync(CACHE, { recursive: true });

const CONCURRENCY = parseInt(process.env.CONC || '16', 10);
const USER_AGENT = 'PathBuildLab/0.1 (icon mirror; +https://github.com/g-hodikov/PathBuildLab)';

const prelim = JSON.parse(readFileSync(path.join(ICON_DIR, 'icon_map.preliminary.json'), 'utf8'));

// Collect unique files to fetch
const fileToEntries = new Map(); // file -> { url, ownersBase: [name], ownersUnique: [name] }
for (const [name, e] of Object.entries(prelim.bases)) {
    const rec = fileToEntries.get(e.file) ?? { url: e.url, ownersBase: [], ownersUnique: [] };
    rec.ownersBase.push(name);
    fileToEntries.set(e.file, rec);
}
for (const [name, e] of Object.entries(prelim.uniques)) {
    const rec = fileToEntries.get(e.file) ?? { url: e.url, ownersBase: [], ownersUnique: [] };
    rec.ownersUnique.push(name);
    fileToEntries.set(e.file, rec);
}

const allFiles = [...fileToEntries.keys()];
console.log('unique files to verify: %d  (concurrency=%d)', allFiles.length, CONCURRENCY);

const ok = new Set();
const failed = new Map(); // file -> status

let inFlight = 0;
let cursor = 0;
let doneCount = 0;
let cachedCount = 0;
let downloadedCount = 0;
let lastLog = Date.now();

async function fetchOne(file) {
    const dest = path.join(CACHE, file);
    if (existsSync(dest)) {
        try {
            const sz = statSync(dest).size;
            if (sz > 0) { ok.add(file); cachedCount++; return; }
        } catch {}
    }
    const url = fileToEntries.get(file).url;
    try {
        const r = await fetch(url, { headers: { 'User-Agent': USER_AGENT } });
        if (!r.ok) { failed.set(file, r.status); return; }
        const ct = r.headers.get('content-type') || '';
        if (ct.startsWith('text/')) { failed.set(file, 'html-' + r.status); return; }
        mkdirSync(path.dirname(dest), { recursive: true });
        await pipeline(Readable.fromWeb(r.body), createWriteStream(dest));
        if (statSync(dest).size < 64) { failed.set(file, 'too-small'); return; }
        ok.add(file);
        downloadedCount++;
    } catch (e) {
        failed.set(file, 'err: ' + (e.code || e.message));
    }
}

async function worker() {
    while (true) {
        const i = cursor++;
        if (i >= allFiles.length) return;
        inFlight++;
        await fetchOne(allFiles[i]);
        inFlight--;
        doneCount++;
        if (Date.now() - lastLog > 2000) {
            lastLog = Date.now();
            console.log('  [%d/%d] cached=%d dl=%d fail=%d', doneCount, allFiles.length, cachedCount, downloadedCount, failed.size);
        }
    }
}

await Promise.all(Array.from({ length: CONCURRENCY }, () => worker()));
console.log('done: total=%d ok=%d cached=%d dl=%d fail=%d', allFiles.length, ok.size, cachedCount, downloadedCount, failed.size);

// Build final map filtered to successful entries
const finalMap = { bases: {}, uniques: {} };
for (const [name, e] of Object.entries(prelim.bases)) {
    if (ok.has(e.file)) finalMap.bases[name] = e;
}
for (const [name, e] of Object.entries(prelim.uniques)) {
    if (ok.has(e.file)) finalMap.uniques[name] = e;
}

writeFileSync(path.join(ICON_DIR, 'icon_map.json'), JSON.stringify(finalMap, null, 2));

const missing = {
    bases: Object.fromEntries(Object.entries(prelim.bases).filter(([_, e]) => !ok.has(e.file)).map(([n, e]) => [n, { file: e.file, status: failed.get(e.file) ?? 'unknown' }])),
    uniques: Object.fromEntries(Object.entries(prelim.uniques).filter(([_, e]) => !ok.has(e.file)).map(([n, e]) => [n, { file: e.file, status: failed.get(e.file) ?? 'unknown' }])),
};
writeFileSync(path.join(ICON_DIR, 'missing.json'), JSON.stringify(missing, null, 2));

console.log('final bases:   %d', Object.keys(finalMap.bases).length);
console.log('final uniques: %d', Object.keys(finalMap.uniques).length);
console.log('missing bases: %d, missing uniques: %d', Object.keys(missing.bases).length, Object.keys(missing.uniques).length);
console.log('wrote PBLExport/icons/icon_map.json and missing.json');
