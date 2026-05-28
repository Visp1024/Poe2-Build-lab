// Rebuild PBLApp/Assets/icon.ico from PBLApp/Assets/icon.png.
// Run: node tools/icons/build-ico.mjs
import { readFileSync, writeFileSync, mkdirSync } from 'fs';
import path from 'path';
import { fileURLToPath } from 'url';
import sharp from 'sharp';
import pngToIco from 'png-to-ico';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const repoRoot  = path.resolve(__dirname, '..', '..');
const assetsDir = path.join(repoRoot, 'PBLApp', 'Assets');
const sourcePng = path.join(assetsDir, 'icon.png');

const tmpDir = path.join(__dirname, '.tmp');
mkdirSync(tmpDir, { recursive: true });

const meta = await sharp(sourcePng).metadata();
console.log(`source: ${path.basename(sourcePng)} ${meta.width}x${meta.height} (${meta.format})`);

// Square-pad if non-square so resize stays centred
const side = Math.max(meta.width, meta.height);
const squareBuf = await sharp(sourcePng)
  .resize(side, side, { fit: 'contain', background: { r: 0, g: 0, b: 0, alpha: 0 } })
  .png()
  .toBuffer();

const sizes = [16, 32, 48, 64, 128, 256];
const tmpPngs = [];
for (const s of sizes) {
  const out = path.join(tmpDir, `ico-${s}.png`);
  await sharp(squareBuf)
    .resize(s, s, { kernel: 'lanczos3' })
    .png({ compressionLevel: 9 })
    .toFile(out);
  tmpPngs.push(out);
  console.log(`  wrote ${out}`);
}

const icoPath = path.join(assetsDir, 'icon.ico');
writeFileSync(icoPath, await pngToIco(tmpPngs));
console.log(`wrote ${icoPath} (sizes: ${sizes.join(',')})`);
