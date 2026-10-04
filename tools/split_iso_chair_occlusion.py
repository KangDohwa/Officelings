"""Repartition the v3 chair's authored cushion/base for NE/NW seat occlusion.

Run after chair-v3-build-review.py and before build_iso_pack.py. No actor masks,
new ink, source-anchor changes or runtime rank changes are involved.
"""
from pathlib import Path
import importlib.util
import json

import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
CHAIR = ROOT / "assets/art/iso/furniture-v3/chair"


def split():
    spec = importlib.util.spec_from_file_location("chair_geometry", CHAIR / "chair-v3-build-review.py")
    geometry = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(geometry)
    path = CHAIR / "chair.iso.json"
    manifest = json.loads(path.read_text(encoding="utf-8"))
    sheet = manifest["sheets"][0]
    atlas = Image.open(CHAIR / sheet["file"]).convert("RGBA")
    for facing in ("ne", "nw"):
        geometry.chair(facing)  # Creates component masks in memory; never calls build().
        components = geometry.COMPONENTS[facing]
        rear = next(r for r in sheet["rows"] if r["direction"] == facing and r["name"] == "rear-00")
        front = next(r for r in sheet["rows"] if r["direction"] == facing and r["name"] == "front-00")

        def cell(row):
            x, y = row["column"] * 128, row["row"] * 128
            return np.array(atlas.crop((x, y, x + 128, y + 128)))

        ra, fa = cell(rear), cell(front)
        empty = np.array(Image.open(CHAIR / f"chair-empty-{facing}.png").convert("RGBA"))
        if np.any((ra[:, :, 3] > 0) & (fa[:, :, 3] > 0)) or not np.array_equal(ra + fa, empty):
            raise ValueError(f"{facing}: source parts no longer reconstruct the reviewed empty chair")
        move = (geometry.alpha(components["base"]) | geometry.alpha(components["cushion"])) & (ra[:, :, 3] > 0)
        fa[move] = ra[move]
        ra[move] = 0
        if not np.array_equal(ra + fa, empty):
            raise ValueError(f"{facing}: repartition changed empty pixels")
        for row, pixels in ((rear, ra), (front, fa)):
            atlas.paste(Image.fromarray(pixels), (row["column"] * 128, row["row"] * 128))
        # The old rear contact was on the transferred cushion. Use the far arm.
        yy, xx = np.where(ra[:, :, 3] > 0)
        nearest = np.argmin((xx - 64) ** 2 + (yy - 68) ** 2)
        rear["anchorChecks"] = [dict(x=int(xx[nearest]), y=int(yy[nearest]), radius=0)]
        rear["contactKind"] = "far-arm-junction"
        front["contactKind"] = "backrest-cushion-base"
        print(f"{facing}: moved {int(move.sum())} cushion/base pixels from rear-00 to front-00")
    provenance = manifest["provenance"]
    provenance["chairV3"] = "Rounded stepped 5px cushion; z21 mesh back, z13 arms, gas lift and five caster contacts. NE/NW cushion/base occlude the seated lower body; SE/SW cushion/base remain rear. Original part IDs/cells/anchors/ground keys/ranks preserved."
    provenance["limitations"] = "Low S0 back and arm height restrict ergonomic silhouette. Regeneration requires tools/split_iso_chair_occlusion.py before rebuilding the pack."
    provenance["seatedOcclusion"] = "NE/NW: complete authored cushion and lift/caster base pixels move from rear-00 (-20) to front-00 (20). Far arm remains rear. Disjoint empty RGBA union is unchanged; no actor-shaped cuts."
    atlas.save(CHAIR / sheet["file"])
    path.write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    split()
