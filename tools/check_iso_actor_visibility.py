"""Measure final seated WPF queue ownership using fixed, authored head ROIs.

The two new rigs require 90% head visibility in every facing/action/phase.
No source or pack pixels are changed. Run after IsoChecks --render-seated.
"""
import argparse
from collections import Counter
import json
from pathlib import Path

import numpy as np
from PIL import Image

import build_iso_pack as pack
import iso_seated_geometry as geometry

SKIN = {
    "cap-teen": ("BD6C3D", "DC7C3D", "E8944B", "EE9E51", "ECA661"),
    "labcoat-tall": ("EB9D5C", "E28E4D", "F0A463", "E9B07F"),
}


def measure(directory, pack_directory):
    manifest = json.loads((pack_directory / "assets.json").read_text(encoding="utf-8"))
    sheets = {s["id"]: s for s in manifest["sheets"]}
    designs = {c["sheetId"]: c["id"].split("/")[-1] for c in manifest["characters"]}
    images = {s["id"]: Image.open(pack_directory / s["file"]).convert("RGBA") for s in manifest["sheets"]}
    records = []; failures = []
    for record in json.loads((directory / "queue.json").read_text(encoding="utf-8")):
        items = record["scene"]["Items"]
        body = next(i for i in items if i["InstanceId"] == "actor/body")
        sheet = sheets[body["SheetId"]]; design = designs[sheet["id"]]
        action, facing = body["Clip"].split("/"); frame = body["Frame"]
        clip = next(c for c in sheet["clips"] if c["name"] == action and c["direction"] == facing)
        sprite = geometry.source_cell(sheet, images[sheet["id"]], clip, frame)
        opaque = sprite[:, :, 3] > 0
        if design in SKIN:
            idle = next(c for c in sheet["clips"] if c["name"] == "idle" and c["direction"] == facing)
            if design == "labcoat-tall":
                head = Image.open(pack.ART / f"characters/{design}/head-{facing}.png").convert("RGBA")
            else:
                # Cap's cranial alpha is the immutable idle prefix y<38:
                # 249 SE/SW and 250 NE/NW samples, matching its authored notes.
                canonical = geometry.source_cell(sheet, images[sheet["id"]], idle, 0)
                canonical[38:] = 0; head = Image.fromarray(canonical)
            offset = sheet["headOffsetsByClip"][clip["sourceClip"]["name"] + "/" + facing][frame]
            shifted = Image.new("RGBA", (32, 64)); shifted.alpha_composite(head, (offset["x"], offset["y"]))
            cranial = np.array(shifted); head_mask = cranial[:, :, 3] > 0
            if not np.array_equal(sprite[head_mask], cranial[head_mask]):
                raise AssertionError(record["name"] + ": authored head pixels changed")
        else:
            top = np.flatnonzero(opaque.any(axis=1)).min()
            head_mask = opaque.copy(); head_mask[:top] = False; head_mask[top + 18:] = False
        yy, xx = np.indices(opaque.shape); hands = np.zeros_like(opaque)
        skin = np.zeros_like(opaque)
        for color in SKIN.get(design, ()):
            skin |= np.all(sprite[:, :, :3] == tuple(bytes.fromhex(color)), axis=2)
        for h in clip.get("handAnchors", []):
            radius = np.maximum(abs(xx - h["x"]), abs(yy - h["y"]))
            hands |= (radius <= 2) & opaque & ~head_mask & (skin if design in SKIN else True)
        torso = opaque & ~head_mask & ~hands & (xx >= 11) & (xx <= 22) & (yy < 44)
        owners = np.full(opaque.shape, body["InstanceId"], object)
        frames = {f["InstanceId"]: f for f in record["frames"]}
        for item in items[items.index(body) + 1:]:
            if item["Alpha"] != 255: continue
            info = frames[item["InstanceId"]]; r = info["frame"]
            if item["SheetId"] not in images:
                images[item["SheetId"]] = Image.open(directory / info["runtimeFile"]).convert("RGBA")
            image = images[item["SheetId"]]
            cell = np.array(image.crop((r["X"], r["Y"], r["X"] + r["Width"], r["Y"] + r["Height"])))
            sx = xx + body["X"] - item["X"]; sy = yy + body["Y"] - item["Y"]
            inside = (sx >= 0) & (sx < cell.shape[1]) & (sy >= 0) & (sy < cell.shape[0])
            hit = np.zeros_like(opaque); hit[inside] = cell[sy[inside], sx[inside], 3] == 255
            owners[hit] = item["InstanceId"]
        rois = {}
        for name, mask in (("head", head_mask), ("torso", torso), ("hands", hands)):
            total = int(mask.sum()); visible = int((mask & (owners == body["InstanceId"])).sum())
            rois[name] = dict(visible=visible, total=total, fraction=visible / total if total else None,
                              occluders=dict(Counter(owners[mask & (owners != body["InstanceId"])].tolist())))
        threshold = .90 if design in SKIN or facing in ("se", "sw") else .85
        if rois["head"]["fraction"] < threshold or action == "typing" and design in SKIN and rois["hands"]["fraction"] != 1:
            failures.append(record["name"])
        records.append(dict(name=record["name"], design=design, facing=facing, action=action, frame=frame, **rois))
    output = directory / "visibility.json"
    output.write_text(json.dumps(dict(records=records, failures=failures), indent=2) + "\n", encoding="utf-8")
    for design in SKIN:
        for facing in pack.FACINGS:
            rows = [r for r in records if r["design"] == design and r["facing"] == facing]
            typing = [r for r in rows if r["action"] == "typing"]
            print(f"{design}/{facing}: head all={min(r['head']['fraction'] for r in rows):.2%}; "
                  f"typing={min(r['head']['fraction'] for r in typing):.2%}; "
                  f"torso={min(r['torso']['fraction'] for r in typing):.2%}; "
                  f"hands={min(r['hands']['fraction'] for r in typing):.2%}")
    print(f"Visibility: {len(records)} frames, {len(failures)} failures; {output}")
    if failures: raise AssertionError("Failed frames: " + ", ".join(failures))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory", type=Path); parser.add_argument("pack", type=Path)
    args = parser.parse_args(); measure(args.directory, args.pack)
