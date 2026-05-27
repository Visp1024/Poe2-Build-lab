#!/usr/bin/env python3
"""
convert_tree_assets.py
======================
Конвертирует ресурсы пассивного дерева Path of Building PoE2
из формата .dds.zst (BC1/BC7/RGBA sprite sheets) в PNG + manifest.json.

Вход:   src/TreeData/{version}/*.dds.zst   (sprite sheets)
        src/TreeData/{version}/*.png        (orbit lines — копируются как есть)
        src/TreeData/{version}/tree.json    (ddsCoords: sprite name → index)

Выход:  PoBApp/Assets/TreeData/{version}/*.png
        PoBApp/Assets/TreeData/{version}/manifest.json

manifest.json используется C# runtime для поиска спрайтов:
  {
    "version": "0_4",
    "sheets": {
      "skills_128_128_BC1": {
        "file": "skills_128_128_BC1.png",
        "sprite_w": 128, "sprite_h": 128,
        "sheet_w": 2048, "sheet_h": 2048,
        "cols": 16, "rows": 16
      }
    },
    "sprites": {
      "Art/2DArt/.../SupremeEgo.dds": {
        "sheet": "skills_128_128_BC1",
        "x": 0, "y": 128, "w": 128, "h": 128
      },
      "orbit_normal0": { "sheet": null, "file": "orbit_normal0.png" }
    }
  }

Использование:
    python tools/convert_tree_assets.py                  # все версии
    python tools/convert_tree_assets.py --version 0_4   # конкретная
    python tools/convert_tree_assets.py --force          # перезаписать

Зависимости:
    pip install zstandard Pillow texture2ddecoder
"""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import shutil
import struct
import sys
from pathlib import Path


# ── Dependency checks ─────────────────────────────────────────────────────────

def _require(pkg: str, import_name: str | None = None) -> object:
    import importlib
    name = import_name or pkg
    try:
        return importlib.import_module(name)
    except ImportError:
        sys.exit(f"Missing dependency: pip install {pkg}")


zstd_mod    = _require("zstandard")
Image_mod   = _require("Pillow", "PIL.Image")
t2d_mod     = _require("texture2ddecoder")

import zstandard as zstd                 # noqa: E402
from PIL import Image                   # noqa: E402
import texture2ddecoder as t2d          # noqa: E402


# ── DDS parsing ───────────────────────────────────────────────────────────────

_DDS_MAGIC    = b"DDS "
_DDPF_FOURCC  = 0x4
_DX10_FOURCC  = b"DX10"
_DXT1_FOURCC  = b"DXT1"
_DXT3_FOURCC  = b"DXT3"
_DXT5_FOURCC  = b"DXT5"

_DXGI = {
    71: "BC1",   # BC1_UNORM
    72: "BC1",   # BC1_UNORM_SRGB
    74: "BC2",   # BC2_UNORM
    75: "BC2",   # BC2_UNORM_SRGB
    77: "BC3",   # BC3_UNORM
    78: "BC3",   # BC3_UNORM_SRGB
    80: "BC4",   # BC4_UNORM
    83: "BC5",   # BC5_UNORM
    95: "BC6H",  # BC6H_UF16
    96: "BC6H",  # BC6H_SF16
    98: "BC7",   # BC7_UNORM
    99: "BC7",   # BC7_UNORM_SRGB
    28: "RGBA",  # R8G8B8A8_UNORM
    87: "BGRA",  # B8G8R8A8_UNORM
}

_DECODE = {
    "BC1":  t2d.decode_bc1,
    "BC3":  t2d.decode_bc3,
    "BC4":  t2d.decode_bc4,
    "BC5":  t2d.decode_bc5,
    "BC6H": t2d.decode_bc6,
    "BC7":  t2d.decode_bc7,
}


def _bcn_block_size(fmt: str) -> int:
    """Bytes per 4×4 block for a BCn format."""
    return 8 if fmt in ("BC1", "BC4") else 16


def _mip_bytes(fmt: str, w: int, h: int) -> int:
    """Byte size of one mip level for a BCn or uncompressed format."""
    if fmt in ("RGBA", "BGRA"):
        return w * h * 4
    blocks_w = max(1, (w + 3) // 4)
    blocks_h = max(1, (h + 3) // 4)
    return blocks_w * blocks_h * _bcn_block_size(fmt)


def _slice_stride(fmt: str, w: int, h: int, mip_count: int) -> int:
    """Total bytes for all mip levels of one texture array slice."""
    total = 0
    for m in range(mip_count):
        mw = max(1, w >> m)
        mh = max(1, h >> m)
        total += _mip_bytes(fmt, mw, mh)
    return total


def _parse_dds_header(data: bytes) -> tuple[int, int, str, int, int, int]:
    """
    Returns (width, height, fmt_name, data_offset, array_size, mip_count).
    fmt_name: 'BC1' | 'BC3' | 'BC7' | 'RGBA' | 'BGRA' | ...
    array_size: 1 for plain textures, >1 for texture arrays.
    """
    if data[:4] != _DDS_MAGIC:
        raise ValueError("Not a DDS file (bad magic)")

    # DDS_HEADER at offset 4, size 124
    _, _, height, width = struct.unpack_from("<IIII", data, 4)
    mip_count = max(1, struct.unpack_from("<I", data, 28)[0])
    # DDSPixelFormat at offset 76 (4 + 72)
    pf_flags, four_cc = struct.unpack_from("<I4s", data, 80)

    data_offset = 4 + 124  # after magic + DDS_HEADER
    array_size  = 1

    if pf_flags & _DDPF_FOURCC:
        if four_cc == _DX10_FOURCC:
            dxgi, _res_dim, _misc, arr, _misc2 = struct.unpack_from("<IIIII", data, data_offset)
            data_offset += 20
            array_size   = max(1, arr)
            fmt = _DXGI.get(dxgi)
            if fmt is None:
                raise ValueError(f"Unsupported DXGI format: {dxgi}")
        elif four_cc == _DXT1_FOURCC:
            fmt = "BC1"
        elif four_cc == _DXT3_FOURCC:
            fmt = "BC3"
        elif four_cc == _DXT5_FOURCC:
            fmt = "BC5"
        else:
            raise ValueError(f"Unknown DDS FourCC: {four_cc}")
    else:
        raise ValueError("Uncompressed DDS without DDPF_FOURCC not supported")

    return width, height, fmt, data_offset, array_size, mip_count


def _decode_slice(fmt: str, pixel_data: bytes, w: int, h: int) -> Image.Image:
    """Decode one slice (mip level 0 data) to RGBA PIL Image."""
    if fmt == "RGBA":
        return Image.frombuffer("RGBA", (w, h), pixel_data[:w * h * 4], "raw", "RGBA", 0, 1)
    if fmt == "BGRA":
        return Image.frombuffer("RGBA", (w, h), pixel_data[:w * h * 4], "raw", "BGRA", 0, 1)
    decoder = _DECODE.get(fmt)
    if decoder is None:
        raise ValueError(f"No decoder for DDS format: {fmt}")
    raw_bgra = decoder(pixel_data[:_mip_bytes(fmt, w, h)], w, h)
    return Image.frombuffer("RGBA", (w, h), raw_bgra, "raw", "BGRA")


def decode_dds(data: bytes) -> Image.Image:
    """
    Decode DDS data → PIL RGBA Image.
    For texture arrays (arraySize > 1) stitches all slices into a
    single-column sprite sheet: width = sprite_w, height = sprite_h * array_size.
    """
    width, height, fmt, offset, array_size, mip_count = _parse_dds_header(data)
    pixel_data = data[offset:]

    if array_size == 1:
        return _decode_slice(fmt, pixel_data, width, height)

    # Texture array — extract slice 0 data size (full mip chain per slice)
    stride = _slice_stride(fmt, width, height, mip_count)
    mip0_size = _mip_bytes(fmt, width, height)

    sheet = Image.new("RGBA", (width, height * array_size))
    for i in range(array_size):
        slice_data = pixel_data[i * stride : i * stride + mip0_size]
        img = _decode_slice(fmt, slice_data, width, height)
        sheet.paste(img, (0, i * height))
    return sheet


# ── Filename parsing ──────────────────────────────────────────────────────────

_SHEET_RE = re.compile(r"^(.+)_(\d+)_(\d+)_([A-Z0-9]+)\.dds\.zst$")


def parse_sheet_filename(filename: str) -> tuple[str, int, int, str] | None:
    """
    'skills_128_128_BC1.dds.zst' → ('skills', 128, 128, 'BC1')
    Returns None if filename doesn't match the pattern.
    """
    m = _SHEET_RE.match(filename)
    if not m:
        return None
    return m.group(1), int(m.group(2)), int(m.group(3)), m.group(4)


def sheet_key(filename: str) -> str:
    """'skills_128_128_BC1.dds.zst' → 'skills_128_128_BC1'"""
    return filename[: -len(".dds.zst")]


# ── Sprite coordinate calculation ─────────────────────────────────────────────

def sprite_rect(index_1based: int, sprite_w: int, sprite_h: int,
                sheet_w: int) -> tuple[int, int, int, int]:
    """
    Convert 1-based sprite index to (x, y, w, h) in sheet pixel coords.
    sprites are laid out left-to-right, top-to-bottom.
    """
    cols = max(1, sheet_w // sprite_w) if sprite_w > 0 else 1
    i = index_1based - 1          # → 0-based
    col = i % cols
    row = i // cols
    return col * sprite_w, row * sprite_h, sprite_w, sprite_h


# ── File hashing (for incremental updates) ────────────────────────────────────

def file_hash(path: Path) -> str:
    h = hashlib.sha256(path.read_bytes())
    return h.hexdigest()[:16]


# ── Per-version conversion ────────────────────────────────────────────────────

def convert_version(version: str, repo_root: Path, force: bool) -> None:
    src_dir = repo_root / "src" / "TreeData" / version
    out_dir = repo_root / "PoBApp" / "Assets" / "TreeData" / version

    if not src_dir.exists():
        print(f"  [SKIP] {src_dir} not found")
        return

    tree_json = src_dir / "tree.json"
    if not tree_json.exists():
        print(f"  [WARN] tree.json missing — cannot build sprite manifest")
        dds_coords: dict[str, dict[str, int]] = {}
    else:
        dds_coords = json.loads(tree_json.read_bytes()).get("ddsCoords", {})

    out_dir.mkdir(parents=True, exist_ok=True)

    # Load incremental cache (src filename → sha256[:16])
    cache_path = out_dir / ".cache.json"
    cache: dict[str, str] = json.loads(cache_path.read_text("utf-8")) if cache_path.exists() else {}
    new_cache: dict[str, str] = {}

    manifest: dict = {
        "version": version,
        "sheets": {},
        "sprites": {},
    }

    # ── Convert DDS.ZST sprite sheets ─────────────────────────────────────

    for zst_file in sorted(src_dir.glob("*.dds.zst")):
        parsed = parse_sheet_filename(zst_file.name)
        if parsed is None:
            print(f"  [WARN] Cannot parse sheet filename: {zst_file.name} — skipped")
            continue

        category, sprite_w, sprite_h, enc = parsed
        key = sheet_key(zst_file.name)
        png_name = key + ".png"
        png_out = out_dir / png_name

        src_hash = file_hash(zst_file)
        new_cache[zst_file.name] = src_hash

        if not force and cache.get(zst_file.name) == src_hash and png_out.exists():
            print(f"  [skip] {zst_file.name}")
            img_w, img_h = Image.open(png_out).size
        else:
            print(f"  [conv] {zst_file.name} … ", end="", flush=True)
            try:
                raw_dds = zstd.ZstdDecompressor().decompress(zst_file.read_bytes())
                img = decode_dds(raw_dds)
                img.save(png_out, "PNG", compress_level=1)
                img_w, img_h = img.size
                print(f"{img_w}x{img_h}  OK")
            except Exception as exc:
                print(f"ERROR: {exc}")
                continue

        cols = max(1, img_w // sprite_w) if sprite_w > 0 else 1
        rows = max(1, img_h // sprite_h) if sprite_h > 0 else 1

        manifest["sheets"][key] = {
            "file":     png_name,
            "sprite_w": sprite_w,
            "sprite_h": sprite_h,
            "sheet_w":  img_w,
            "sheet_h":  img_h,
            "cols":     cols,
            "rows":     rows,
        }

        # Fill sprite entries from ddsCoords
        for sprite_name, idx in dds_coords.get(zst_file.name, {}).items():
            x, y, w, h = sprite_rect(idx, sprite_w, sprite_h, img_w)
            manifest["sprites"][sprite_name] = {
                "sheet": key,
                "x": x, "y": y, "w": w, "h": h,
            }

    # ── Copy orbit PNG files as-is ─────────────────────────────────────────

    for png_src in sorted(src_dir.glob("*.png")):
        dst = out_dir / png_src.name
        src_hash = file_hash(png_src)
        new_cache[png_src.name] = src_hash

        if not force and cache.get(png_src.name) == src_hash and dst.exists():
            print(f"  [skip] {png_src.name}")
        else:
            shutil.copy2(png_src, dst)
            print(f"  [copy] {png_src.name}")

        # orbit_normal0  →  sprite with sheet=null, file=filename
        manifest["sprites"][png_src.stem] = {
            "sheet": None,
            "file":  png_src.name,
        }

    # ── Write manifest and cache ───────────────────────────────────────────

    manifest_path = out_dir / "manifest.json"
    manifest_path.write_text(json.dumps(manifest, indent=2, ensure_ascii=False), "utf-8")

    cache_path.write_text(json.dumps(new_cache, indent=2), "utf-8")

    n_sprites = len(manifest["sprites"])
    n_sheets  = len(manifest["sheets"])
    print(f"  -> manifest.json: {n_sheets} sheets, {n_sprites} sprites")


# ── Entry point ───────────────────────────────────────────────────────────────

def main() -> None:
    ap = argparse.ArgumentParser(
        description="Convert PoB passive tree assets: .dds.zst → PNG + manifest.json"
    )
    ap.add_argument(
        "--version", "-v",
        metavar="VER",
        help="Tree version to convert (e.g. 0_4). Default: all versions found.",
    )
    ap.add_argument(
        "--repo-root", "-r",
        default=".",
        metavar="DIR",
        help="Repository root directory (default: current directory).",
    )
    ap.add_argument(
        "--force", "-f",
        action="store_true",
        help="Re-convert files even if the source is unchanged.",
    )
    args = ap.parse_args()

    repo_root = Path(args.repo_root).resolve()
    tree_data_root = repo_root / "src" / "TreeData"

    if not tree_data_root.exists():
        sys.exit(f"ERROR: TreeData not found at {tree_data_root}")

    if args.version:
        versions = [args.version]
    else:
        versions = sorted(
            d.name for d in tree_data_root.iterdir()
            if d.is_dir() and any(d.glob("*.dds.zst"))
        )

    if not versions:
        sys.exit("No tree versions with .dds.zst files found.")

    print(f"Repo root : {repo_root}")
    print(f"Versions  : {', '.join(versions)}")

    for ver in versions:
        print(f"\n--- {ver} ---")
        convert_version(ver, repo_root, force=args.force)

    print("\nDone.")


if __name__ == "__main__":
    main()
