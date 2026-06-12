"""
Repack PBLApp/Assets/TreeData/<ver>/*.png as WebP and rewrite manifest.json.

Sheets that exceed WebP's 16383-pixel dimension limit are tiled vertically into
N images of (sprite_w x sprite_h) each — one per row in the original sheet —
saved as "<name>_<row>.webp". A "tiles" array is written into the manifest so
TreeAssetStore can pick the right row file by y/sprite_h and rebase coords.

- Tile images > 1 MB → lossy WebP quality=85 (backgrounds, ascendancy plates).
- Smaller tiles → lossless WebP (crisp node-icon edges).
- Standalone (non-sheet) PNGs are also re-encoded as WebP.

Run once after assets are extracted/regenerated. The .png originals are deleted.
"""

import json
import os
import sys
from pathlib import Path
from PIL import Image

LARGE_THRESHOLD_BYTES = 1 * 1024 * 1024  # 1 MB
LOSSY_QUALITY = 85
LOSSY_METHOD = 6


def save_webp(im: Image.Image, path: Path) -> int:
    """Save image as WebP; choose lossy/lossless by raw RGBA size heuristic."""
    raw_bytes = im.width * im.height * 4
    if raw_bytes > LARGE_THRESHOLD_BYTES:
        im.save(path, "WEBP", quality=LOSSY_QUALITY, method=LOSSY_METHOD)
    else:
        im.save(path, "WEBP", lossless=True, method=LOSSY_METHOD)
    return path.stat().st_size


def convert_sheet(png: Path, sheet_entry: dict) -> list[str]:
    """Split a sheet PNG into per-row WebP tiles. Returns list of tile filenames."""
    sprite_w = sheet_entry["sprite_w"]
    sprite_h = sheet_entry["sprite_h"]
    rows = sheet_entry["rows"]
    stem = png.stem  # without .png
    out_dir = png.parent
    tile_names: list[str] = []

    with Image.open(png) as im:
        im.load()
        for i in range(rows):
            tile = im.crop((0, i * sprite_h, sprite_w, (i + 1) * sprite_h))
            name = f"{stem}_{i}.webp"
            save_webp(tile, out_dir / name)
            tile_names.append(name)

    png.unlink()
    return tile_names


def convert_standalone(png: Path) -> str:
    """Convert a single sprite PNG to WebP. Returns the new filename."""
    webp = png.with_suffix(".webp")
    with Image.open(png) as im:
        im.load()
        save_webp(im, webp)
    png.unlink()
    return webp.name


def convert_dir(version_dir: Path) -> tuple[int, int]:
    manifest_path = version_dir / "manifest.json"
    if not manifest_path.exists():
        print(f"  skip {version_dir.name}: no manifest.json")
        return 0, 0

    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))

    # 1) Sheets: tile each into <name>_<row>.webp
    sheets = manifest.get("sheets", {})
    for key, entry in sheets.items():
        file_name = entry.get("file")
        if not file_name:
            continue
        png_path = version_dir / file_name
        if not png_path.exists():
            print(f"    WARN: sheet file missing: {png_path.name}")
            continue
        orig_size = png_path.stat().st_size
        tiles = convert_sheet(png_path, entry)
        entry["tiles"] = tiles
        entry["tile_h"] = entry["sprite_h"]
        entry.pop("file", None)  # tiles supersede file
        new_size = sum((version_dir / t).stat().st_size for t in tiles)
        print(f"    {file_name:55s}  {orig_size/1024/1024:7.1f} MB -> {new_size/1024/1024:6.2f} MB  [{len(tiles)} tiles]")

    # 2) Standalone sprites
    sprites = manifest.get("sprites", {})
    standalone_count = 0
    for entry in sprites.values():
        f = entry.get("file")
        if not f:
            continue
        png_path = version_dir / f
        if not png_path.exists():
            continue
        new_name = convert_standalone(png_path)
        entry["file"] = new_name
        standalone_count += 1

    manifest_path.write_text(json.dumps(manifest, indent=2), encoding="utf-8")
    print(f"  manifest updated: {standalone_count} standalone sprites, {len(sheets)} sheets")

    # Count remaining PNGs (should be 0 for converted versions)
    leftover = list(version_dir.glob("*.png"))
    if leftover:
        print(f"    NOTE: {len(leftover)} png file(s) untouched (not in manifest)")
    return 0, standalone_count + len(sheets)


def main() -> int:
    repo_root = Path(__file__).resolve().parent.parent
    base = repo_root / "PBLApp" / "Assets" / "TreeData"
    if not base.is_dir():
        print(f"ERROR: {base} not found", file=sys.stderr)
        return 1

    for ver_dir in sorted(p for p in base.iterdir() if p.is_dir()):
        print(f"\n[{ver_dir.name}]")
        convert_dir(ver_dir)

    total_before = 0
    total_after = 0
    for png in base.rglob("*.webp"):
        total_after += png.stat().st_size
    print(f"\nFinal Assets/TreeData size: {total_after/1024/1024:.1f} MB")
    return 0


if __name__ == "__main__":
    sys.exit(main())
