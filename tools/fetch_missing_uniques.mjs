// Fetch icons for uniques missing from cdn.poe2db.tw by scraping each
// item's poe2db page and downloading the signed GGG CDN URL embedded in it.
//
// Reads PBLExport/icons/missing.json (uniques map only),
// updates PBLExport/icons/icon_map.json (uniques entry → relative .png path),
// writes files into PBLExport/icons/cache/.

import { readFileSync, writeFileSync, existsSync, mkdirSync } from 'fs';
import { fileURLToPath } from 'url';
import path from 'path';
import { pipeline } from 'stream/promises';
import { createWriteStream } from 'fs';
import { Readable } from 'stream';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const REPO = path.resolve(__dirname, '..');
const ICON_DIR = path.join(REPO, 'PBLExport', 'icons');
const CACHE = path.join(ICON_DIR, 'cache');
const UA = 'Mozilla/5.0 (PathBuildLab icon fetcher)';

const missing = JSON.parse(readFileSync(path.join(ICON_DIR, 'missing.json'), 'utf8'));
const iconMap = JSON.parse(readFileSync(path.join(ICON_DIR, 'icon_map.json'), 'utf8'));

const uniqueNames = Object.keys(missing.uniques || {});
console.log(`probing ${uniqueNames.length} missing uniques on poe2db.tw`);

function slugify(name) {
    // poe2db uses underscore-separated slugs that mostly preserve the original name,
    // with special chars stripped. Examples:
    //   "Queen of the Forest" -> "Queen_of_the_Forest"
    //   "Doedre's Tenure"     -> "Doedres_Tenure"
    //   "Painter's Servant"   -> "Painters_Servant"
    return name.replace(/'/g, '').replace(/[^A-Za-z0-9]+/g, '_').replace(/^_+|_+$/g, '');
}

async function fetchPage(url) {
    const r = await fetch(url, { headers: { 'User-Agent': UA, 'Accept': 'text/html' } });
    if (!r.ok) return null;
    return await r.text();
}

function extractSignedUrl(html, ownerName) {
    // Match: https://web.poecdn.com/gen/image/<base64>/<hash>/<file>.png
    const m = html.match(/https:\/\/web\.poecdn\.com\/gen\/image\/[A-Za-z0-9+/=_]+\/[a-f0-9]+\/[^"'\s]+\.(?:png|webp)/);
    return m ? m[0] : null;
}

async function downloadTo(url, dest) {
    const r = await fetch(url, { headers: { 'User-Agent': UA } });
    if (!r.ok) return false;
    mkdirSync(path.dirname(dest), { recursive: true });
    await pipeline(Readable.fromWeb(r.body), createWriteStream(dest));
    return true;
}

async function processName(name) {
    const entry = missing.uniques[name];
    const slug = slugify(name);
    const pageUrl = `https://poe2db.tw/us/${slug}`;
    const html = await fetchPage(pageUrl);
    if (!html) return { name, status: 'page-404' };
    const signed = extractSignedUrl(html, name);
    if (!signed) return { name, status: 'no-url' };
    // Strip the original .webp from entry.file → swap to .png to match the GGG asset
    const ext = signed.endsWith('.png') ? '.png' : path.extname(signed) || '.png';
    const rel = entry.file.replace(/\.webp$/i, ext);
    const dest = path.join(CACHE, rel);
    if (existsSync(dest)) return { name, status: 'already', rel };
    const ok = await downloadTo(signed, dest);
    if (!ok) return { name, status: 'dl-fail', signed };
    return { name, status: 'ok', rel, signed };
}

const POOL = 8;
const queue = [...uniqueNames];
const results = [];
async function worker() {
    while (queue.length) {
        const name = queue.shift();
        try {
            const r = await processName(name);
            results.push(r);
            if (r.status === 'ok' || r.status === 'already') {
                iconMap.uniques[name] = { file: r.rel };
            }
            process.stdout.write(`\r[${results.length}/${uniqueNames.length}] last=${r.status} ${name.slice(0, 30)}            `);
        } catch (e) {
            results.push({ name, status: 'err', err: String(e) });
        }
    }
}
await Promise.all(Array.from({ length: POOL }, worker));
console.log();

const ok = results.filter(r => r.status === 'ok' || r.status === 'already').length;
const failed = results.filter(r => r.status !== 'ok' && r.status !== 'already');
console.log(`done: ok=${ok} failed=${failed.length}`);
for (const f of failed) console.log(`  ${f.status}: ${f.name}${f.signed ? ' ' + f.signed : ''}`);

writeFileSync(path.join(ICON_DIR, 'icon_map.json'), JSON.stringify(iconMap, null, 2));
console.log('updated icon_map.json');
