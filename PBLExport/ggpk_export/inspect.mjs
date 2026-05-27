import { readFileSync } from 'fs';
import path from 'path';

// Попробуем импортировать модуль pathofexile-dat напрямую
const modPath = 'C:/Users/Gleb.Gleb-PC/AppData/Roaming/npm/node_modules/pathofexile-dat/dist/cli/bundle-loaders.js';
try {
  const mod = await import(modPath);
  console.log('Module exports:', Object.keys(mod));
} catch(e) {
  console.log('Error:', e.message);
}
