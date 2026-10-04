"""Compatibility entry point for the geometry-derived v3 chair redesign.

The former blanket NE/NW cushion/base transfer is superseded. Rebuild the pack
with build_iso_pack.py --seated-only to preserve the shipped character art.
"""
from pathlib import Path
from iso_seated_geometry import write_chair


def split():
    write_chair(Path(__file__).resolve().parents[1]/"assets/art/iso/furniture-v3/chair")


if __name__ == "__main__":
    split()
