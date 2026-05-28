// Render icon.svg + logo.svg into PNG/ICO assets used by PBLApp.
//
// Output:
//   PBLApp/Assets/icon.png   (512x512)
//   PBLApp/Assets/icon.ico   (16,32,48,64,128,256)
//   PBLApp/Assets/logo.png   (1024x1280)
//
// Run: node tools/icons/render-icons.mjs
import { readFileSync, writeFileSync, mkdirSync } from 'fs';
import path from 'path';
import { fileURLToPath } from 'url';
import sharp from 'sharp';
import pngToIco from 'png-to-ico';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const repoRoot = path.resolve(__dirname, '..', '..');
const assetsDir = path.join(repoRoot, 'PBLApp', 'Assets');
mkdirSync(assetsDir, { recursive: true });

const iconSvg = readFileSync(path.join(__dirname, 'icon.svg'));

// Logo embeds the icon via <use href="#__icon__"/>; inline the icon's <svg> body
// as a <symbol id="__icon__"> so a single render call resolves the reference.
const rawLogo = readFileSync(path.join(__dirname, 'logo.svg'), 'utf8');
const iconBody = iconSvg.toString()
  .replace(/^[\s\S]*?<svg[^>]*>/, '')
  .replace(/<\/svg>\s*$/, '');
const logoSvg = rawLogo.replace(
  '<defs>',
  `<defs><symbol id="__icon__" viewBox="0 0 1024 1024">${iconBody}</symbol>`
);

async function renderPng(svgBuf, size, outPath) {
  await sharp(svgBuf, { density: 384 })
    .resize(size, size, { fit: 'contain', background: { r: 0, g: 0, b: 0, alpha: 0 } })
    .png({ compressionLevel: 9 })
    .toFile(outPath);
  console.log(`wrote ${outPath} (${size}px)`);
}

async function main() {
  // icon.png — main 512x512
  await renderPng(iconSvg, 512, path.join(assetsDir, 'icon.png'));

  // ico: bundle multiple sizes
  const icoSizes = [16, 32, 48, 64, 128, 256];
  const tmpDir = path.join(__dirname, '.tmp');
  mkdirSync(tmpDir, { recursive: true });
  const tmpPngs = [];
  for (const s of icoSizes) {
    const p = path.join(tmpDir, `icon-${s}.png`);
    await renderPng(iconSvg, s, p);
    tmpPngs.push(p);
  }
  const icoBuf = await pngToIco(tmpPngs);
  const icoPath = path.join(assetsDir, 'icon.ico');
  writeFileSync(icoPath, icoBuf);
  console.log(`wrote ${icoPath} (${icoSizes.join(',')})`);

  // logo.png — 1024x1280
  await sharp(Buffer.from(logoSvg), { density: 192 })
    .resize(1024, 1280, { fit: 'contain', background: { r: 6, g: 20, b: 26, alpha: 1 } })
    .png({ compressionLevel: 9 })
    .toFile(path.join(assetsDir, 'logo.png'));
  console.log(`wrote ${path.join(assetsDir, 'logo.png')} (1024x1280)`);
}

main().catch(e => { console.error(e); process.exit(1); });
