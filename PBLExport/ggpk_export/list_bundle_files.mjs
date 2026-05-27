/**
 *列出 PoE2 bundle index 中所有匹配模式的文件路径
 * Lists all file paths from PoE2 bundle index matching a pattern
 */
import { readFileSync } from 'fs';
import { decompressSliceInBundle, decompressedBundleSize } from 'pathofexile-dat/dist/bundles/bundle.js';
import { readIndexBundle } from 'pathofexile-dat/dist/bundles/index-bundle.js';

const GAME_DIR = 'D:/Games/steamapps/common/Path of Exile 2/Bundles2';
const FILTER = process.argv[2] || 'StatDesc';

// Load and decompress the index bundle
console.log('Loading _.index.bin...');
const indexBin = readFileSync(`${GAME_DIR}/_.index.bin`);
const indexBinU8 = new Uint8Array(indexBin.buffer, indexBin.byteOffset, indexBin.byteLength);
const decompSize = decompressedBundleSize(indexBinU8);
const indexBundle = new Uint8Array(decompSize);
decompressSliceInBundle(indexBinU8, 0, indexBundle);

const { bundlesInfo, filesInfo, dirsInfo, pathRepsBundle } = readIndexBundle(indexBundle);
console.log(`Index loaded. Files: ${filesInfo.byteLength / 20}`);

// Decode path representations bundle
// Format: bundle of path segment operations
// Each path in the bundle index is built by: push segment / pop segment / store path
const pathRepsDecompSize = decompressedBundleSize(pathRepsBundle);
const pathRepsDecomp = new Uint8Array(pathRepsDecompSize);
decompressSliceInBundle(pathRepsBundle, 0, pathRepsDecomp);

// Parse path reps: sequence of uint32 entries
// 0 = push empty string (reset)
// 1 = pop (go up one level)
// N >= 2 = index into string table (path segment)
// The string table is stored as null-terminated strings at the start
const view = new DataView(pathRepsDecomp.buffer, pathRepsDecomp.byteOffset, pathRepsDecomp.byteLength);
const decoder = new TextDecoder('utf-8');

// Find the size of the string table (first uint32 = total path data size)
const pathDataSize = view.getUint32(0, true);
let offset = 4;

// Read all null-terminated strings from the string table
const strings = [];
const stringStart = offset;
while (offset < stringStart + pathDataSize) {
  let end = offset;
  while (end < pathRepsDecomp.byteLength && pathRepsDecomp[end] !== 0) end++;
  strings.push(decoder.decode(pathRepsDecomp.subarray(offset, end)));
  offset = end + 1;
}
console.log(`String table: ${strings.length} strings`);

// Read path operations
const allPaths = [];
const stack = [];

while (offset < pathRepsDecomp.byteLength) {
  const op = view.getUint32(offset, true);
  offset += 4;

  if (op === 0) {
    // Push: start new path context
    stack.length = 0;
  } else if (op === 1) {
    // Pop: go up
    stack.pop();
  } else {
    // String index (op >= 2 means index into strings array, offset by 2)
    const idx = op - 2;
    const seg = idx < strings.length ? strings[idx] : '';
    stack.push(seg);
    // Check if this is a leaf (file) by reading next op
    // If next is 0 or 1 or end — this is a complete path
    const nextOp = offset < pathRepsDecomp.byteLength ? view.getUint32(offset, true) : 0;
    // Heuristic: if segment contains a dot (extension), it's a file
    if (seg.includes('.') || nextOp === 0 || nextOp === 1) {
      allPaths.push(stack.join('/'));
    }
  }
}

// Filter and print
const filterLower = FILTER.toLowerCase();
const matched = allPaths.filter(p => p.toLowerCase().includes(filterLower));
console.log(`\nPaths matching '${FILTER}': ${matched.length}`);
matched.sort().forEach(p => console.log(p));
