// Trim dark padding from icon.png and rescale so the emblem fills the canvas,
// then rebuild icon.ico from the result.
// Backup original to icon.original.png on first run.
import { existsSync, copyFileSync, writeFileSync, mkdirSync, readFileSync } from 'fs';
import path from 'path';
import { fileURLToPath } from 'url';
import sharp from 'sharp';
import pngToIco from 'png-to-ico';

const __dirname  = path.dirname(fileURLToPath(import.meta.url));
const repoRoot   = path.resolve(__dirname, '..', '..');
const assetsDir  = path.join(repoRoot, 'PBLApp', 'Assets');
const pngPath    = path.join(assetsDir, 'icon.png');
const backupPath = path.join(assetsDir, 'icon.original.png');
const icoPath    = path.join(assetsDir, 'icon.ico');

if (!existsSync(backupPath)) {
  copyFileSync(pngPath, backupPath);
  console.log(`backed up original → ${backupPath}`);
}

// Always trim from the ORIGINAL, not previously-trimmed output, so this is idempotent.
const srcBuf = readFileSync(backupPath);
const srcMeta = await sharp(srcBuf).metadata();
console.log(`source: ${srcMeta.width}x${srcMeta.height}`);

// Trim near-black borders. threshold controls aggressiveness.
const trimmedBuf = await sharp(srcBuf)
  .trim({ threshold: 25 })
  .toBuffer();
const tMeta = await sharp(trimmedBuf).metadata();
console.log(`trimmed: ${tMeta.width}x${tMeta.height}`);

// Square it (pad shorter side with transparency).
const side = Math.max(tMeta.width, tMeta.height);
const squareBuf = await sharp(trimmedBuf)
  .resize(side, side, { fit: 'contain', background: { r: 0, g: 0, b: 0, alpha: 0 } })
  .png()
  .toBuffer();

// Additional zoom: scale > 1024 then centre-crop. Outer decorative spikes
// will exit the frame; main emblem becomes ZOOM times larger.
const ZOOM = 1.85;
const big = Math.round(1024 * ZOOM);
const zoomedBuf = await sharp(squareBuf)
  .resize(big, big, { kernel: 'lanczos3' })
  .png()
  .toBuffer();
const offset = Math.round((big - 1024) / 2);
await sharp(zoomedBuf)
  .extract({ left: offset, top: offset, width: 1024, height: 1024 })
  .png({ compressionLevel: 9 })
  .toFile(pngPath);
console.log(`wrote ${pngPath} (1024x1024, zoom ${ZOOM}x)`);

// Update squareBuf reference so the ICO sources match what we wrote.
const finalBuf = await sharp(pngPath).toBuffer();

// Rebuild ico.
const tmpDir = path.join(__dirname, '.tmp');
mkdirSync(tmpDir, { recursive: true });
const sizes = [16, 32, 48, 64, 128, 256];
const tmpPngs = [];
for (const s of sizes) {
  const p = path.join(tmpDir, `ico-${s}.png`);
  await sharp(finalBuf)
    .resize(s, s, { kernel: 'lanczos3' })
    .png({ compressionLevel: 9 })
    .toFile(p);
  tmpPngs.push(p);
}
writeFileSync(icoPath, await pngToIco(tmpPngs));
console.log(`wrote ${icoPath} (sizes: ${sizes.join(',')})`);
