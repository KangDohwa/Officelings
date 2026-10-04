"""Measure authored sitter/chair fit and write nearest-neighbor contact sheets.

All output is scratch data. --fixture includes available extra source designs,
but preserves the production character/blink bytes from the supplied full pack.
Run before changing geometry, then use --before on the after measurement.
"""
import argparse
from copy import deepcopy
import json
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw

import build_iso_pack as pack
import iso_seated_geometry as geometry

ACTIONS = ("typing", "thinking", "sleep", "sit-down", "stand-up")
EXTRAS = ("afro-sweater", "glasses-bun", "hanbok-modern", "red-braid")


def fixture(directory, shipped_directory):
    shipped = json.loads((shipped_directory / "assets.json").read_text(encoding="utf-8"))
    production = {s["id"]: s for s in shipped["sheets"] if s["kind"] in ("character", "blink")}
    identities = tuple(c["id"].split("/")[-1] for c in shipped["characters"])
    available = tuple(i for i in EXTRAS if (pack.ART / f"characters/{i}/{i}.iso.json").is_file() and i not in identities)
    manifest, files = pack.assemble(pack.source_paths(pack.ART, identities + available))
    for identity in available:
        if not any(c["id"] == f"iso/character/{identity}" for c in manifest["characters"]):
            manifest["characters"].append(dict(id=f"iso/character/{identity}", sheetId=identity + "-normal", name=identity))
    manifest["sheets"] = [deepcopy(production.get(s["id"], s)) for s in manifest["sheets"] if s["id"] != "iso-chair-sandwich"]
    for s in production.values():
        files[s["file"]] = (shipped_directory / s["file"]).read_bytes()
    geometry.bake(manifest, files)
    pack.link_blinks({s["id"]: s for s in manifest["sheets"]})
    # Runtime loader's 4 MiB bound also applies to the expanded scratch fixture.
    compact = (json.dumps(manifest, ensure_ascii=False, separators=(",", ":")) + "\n").encode("utf-8")
    if len(compact) > pack.MAX_JSON:
        raise ValueError("fixture manifest exceeds runtime bound")
    for name, data in files.items():
        pack.atomic_write(directory / name, data)
    pack.atomic_write(directory / "assets.json", compact)
    pack.atomic_write(directory / "layout.json", pack.json_bytes(pack.make_layout(manifest)))
    print("Fixture designs:", ", ".join(identities + available))
    print("Absent source designs:", ", ".join(i for i in EXTRAS if i not in identities + available) or "none")


def unpack(value):
    import base64
    return np.unpackbits(np.frombuffer(base64.b64decode(value), np.uint8), bitorder="little").reshape(64, 32).astype(bool)


def measure(directory, output):
    manifest = json.loads((directory / "assets.json").read_text(encoding="utf-8"))
    sheets = {s["id"]: s for s in manifest["sheets"]}
    images = {s["id"]: Image.open(directory / s["file"]).convert("RGBA") for s in manifest["sheets"]}
    sandwich = sheets["iso-chair-sandwich"]
    parts = {c["name"] + "/" + c["direction"]: c for c in sandwich["clips"]}
    records = []; calibrated = {}
    output.mkdir(parents=True, exist_ok=True)
    for design in manifest["characters"]:
        sheet = sheets[design["sheetId"]]
        for facing in geometry.FACINGS:
            chair, _, labels = geometry.chair(facing)
            # Verify these material labels describe the measured pack's static art.
            static = sheets["iso-chair-parts"]
            union = sum((geometry.source_cell(static, images[static["id"]], c, 0).astype(np.uint16)
                         for c in static["clips"] if c["direction"] == facing), np.zeros((128,128,4), np.uint16))
            if not np.array_equal(union, chair):
                raise AssertionError("pack does not match current chair geometry")
            board = Image.new("RGBA", (4 * 192, len(ACTIONS) * 274), "#E7D1AC")
            draw = ImageDraw.Draw(board)
            for row, action in enumerate(ACTIONS):
                clip = next(c for c in sheet["clips"] if c["name"] == action and c["direction"] == facing)
                draw.text((4, row * 274 + 4), f"{design['id']} / {facing} / {action}", fill="#191E1E")
                for frame, binding in enumerate(clip["chairFrames"]):
                    sprite = geometry.source_cell(sheet, images[sheet["id"]], clip, frame)
                    dx, dy, height = geometry.contact(clip, frame)
                    pose = geometry.POSES[clip["sourceClip"]["name"]]
                    cache_key = (facing, pose, height, dx, dy, sprite.tobytes())
                    if cache_key not in calibrated:
                        # Audit nearest-proxy extrapolation at actual authored
                        # coordinates, also for old packs without the new helper.
                        ad = geometry.actor_depth(facing, pose, height)
                        ay, ax = np.where(sprite[:,:,3] > 0); sy, sx = np.where(np.isfinite(ad))
                        distances = (ax[:,None]+dx-sx)**2 + (ay[:,None]+dy-sy)**2
                        nearest = distances.argmin(axis=1)
                        depth = np.full((128,128), -np.inf)
                        depth[ay+dy,ax+dx] = ad[sy[nearest],sx[nearest]]
                        extrapolation = np.zeros((128,128))
                        extrapolation[ay+dy,ax+dx] = np.sqrt(distances.min(axis=1))
                        calibrated[cache_key] = depth, extrapolation
                    depth, extrapolation = calibrated[cache_key]
                    body = np.zeros((128,128), bool)
                    body[dy:dy+64, dx:dx+32] = sprite[:,:,3] > 0
                    cells = [geometry.source_cell(sandwich, images[sandwich["id"]], parts[key], 0) for key in binding["clips"]]
                    alpha_count = sum((c[:,:,3] > 0).astype(int) for c in cells)
                    if np.any(alpha_count > 1) or not np.array_equal(sum(c.astype(np.uint16) for c in cells), chair):
                        raise AssertionError("part union has gaps, duplicate opaque samples or changed colors")
                    front = (cells[1][:,:,3] > 0) | (cells[2][:,:,3] > 0)
                    visible_cushion = (labels == 2) & (~body | front)
                    # Authored pelvis contact +/- lap extent, fixed independently
                    # of chair size/proxy. Exclude shins and feet below +6.
                    anchor = clip.get("phaseContacts", [{}] * clip["frames"])[frame].get("seatAnchor", clip.get("seatAnchor", {"y":44}))
                    lap = np.zeros_like(body)
                    seat_y = dy + int(anchor["y"])
                    lap[max(0,seat_y-2):seat_y+7] = body[max(0,seat_y-2):seat_y+7]
                    rows = []
                    for y in range(max(0,seat_y-2), seat_y+7):
                        bx = np.flatnonzero(body[y]); cx = np.flatnonzero(visible_cushion[y])
                        if not len(bx) or not len(cx):
                            continue
                        left = int(max(0, bx.min() - cx.min()))
                        right = int(max(0, cx.max() - bx.max()))
                        rows.append(dict(y=y, left=left, right=right,
                                         pixels=int(((cx < bx.min()) | (cx > bx.max())).sum())))
                    head = np.zeros((64,32), bool)
                    ys = np.flatnonzero((sprite[:,:,3] > 0).any(axis=1))
                    head[ys.min():ys.min()+18] = sprite[ys.min():ys.min()+18,:,3] > 0
                    hands = np.zeros_like(head)
                    for h in clip.get("handAnchors", []):
                        x,y = int(h["x"]),int(h["y"])
                        hands[y-1:y+2,x-1:x+2] = sprite[y-1:y+2,x-1:x+2,3] > 0
                    hidden = unpack(binding["hidden"])
                    cushion_lap = lap & (labels == 2)
                    ys, xs = np.where(cushion_lap)
                    # Invert P_y=80+s-z, D=s+2z on the actual sprite ray.
                    inferred_z = (depth[ys,xs] - ys + 80) / 3
                    if np.any(body & front & (geometry.chair(facing)[1] <= depth+1e-7)):
                        raise AssertionError("front chair sample is not nearer than body proxy")
                    r = dict(key=f"{design['id']}/{facing}/{action}/{frame}", facing=facing, action=action,
                             frame=frame, height=height, left=max((r["left"] for r in rows),default=0),
                             right=max((r["right"] for r in rows),default=0), overhangPixels=sum(r["pixels"] for r in rows),
                             lipCoveredLap=int((lap & (cells[1][:,:,3]>0)).sum()),
                             cushionCoveredLap=int((lap & body & front & (labels==2)).sum()),
                             hiddenHead=int((head & hidden).sum()), hiddenHands=int((hands & hidden).sum()),
                             hiddenHeadMask=geometry.bits(head & hidden), hiddenHandsMask=geometry.bits(hands & hidden),
                             lapProxyBelowTop=int((inferred_z < 12-1e-7).sum()),
                             minLapProxyZ=round(float(inferred_z.min()),4) if len(ys) else None,
                             lapMaxExtrapolation=round(float(extrapolation[cushion_lap].max()),4) if len(ys) else 0,
                             rows=rows)
                    records.append(r)
                    result = Image.new("RGBA", (128,128))
                    result.alpha_composite(Image.fromarray(cells[0]))
                    result.alpha_composite(Image.fromarray(sprite), (dx,dy))
                    result.alpha_composite(Image.fromarray(cells[1]))
                    result.alpha_composite(Image.fromarray(cells[2]))
                    crop = result.crop((40,28,88,90)).resize((192,248), Image.Resampling.NEAREST)
                    board.alpha_composite(crop, (frame * 192,row * 274 + 22))
            board.save(output / f"{design['id'].split('/')[-1]}-{facing}-all-phases-4x.png")
    (output / "metrics.json").write_text(json.dumps(records, indent=2)+"\n", encoding="utf-8")
    return records


def report(records, before=None, enforce=False, details=False):
    failed = []
    for facing in geometry.FACINGS:
        seated = [r for r in records if r["facing"]==facing and r["action"] in ("typing","thinking","sleep")]
        nums = (max(r["left"] for r in seated), max(r["right"] for r in seated),
                sum(r["overhangPixels"] for r in seated), sum(r["lipCoveredLap"] for r in seated))
        previous_seated = [r for r in before or [] if r["facing"]==facing and r["action"] in ("typing","thinking","sleep")]
        prefix = ""
        if previous_seated:
            old = (max(r["left"] for r in previous_seated), max(r["right"] for r in previous_seated),
                   sum(r["overhangPixels"] for r in previous_seated), sum(r["lipCoveredLap"] for r in previous_seated))
            prefix = f"before L/R={old[:2]}, outside={old[2]}, lip/lap={old[3]} -> "
        print(f"{facing}: {len(seated)} seated frames; {prefix}after L/R={nums[:2]} px, outside={nums[2]}, lip/lap={nums[3]}")
        print(f"  lap proxy: below z12={sum(r['lapProxyBelowTop'] for r in seated)} pixels; "
              f"max extrapolation={max(r['lapMaxExtrapolation'] for r in seated):.4f}px")
        if facing in ("se","sw") and max(nums[:2]) > 2:
            failed.append(f"{facing}: overhang exceeds 2px per side")
        if nums[3] != 0:
            failed.append(f"{facing}: lip covers lap")
        if any(r["lapProxyBelowTop"] for r in seated):
            failed.append(f"{facing}: lap proxy extrapolates below cushion top")
    if before:
        previous = {r["key"]:r for r in before}
        for r in records:
            b = previous[r["key"]]
            if details:
                names = ("left", "right", "overhangPixels", "lipCoveredLap", "cushionCoveredLap", "lapProxyBelowTop", "hiddenHead", "hiddenHands")
                print(r["key"], "before", [b[n] for n in names], "after", [r[n] for n in names])
            if any(np.any(unpack(r[name]) & ~unpack(b[name])) for name in ("hiddenHeadMask", "hiddenHandsMask")):
                failed.append(r["key"]+": new hidden head/hand pixels")
        print("Before -> after lip/lap:", sum(r["lipCoveredLap"] for r in before if r["action"] in ("typing","thinking","sleep")),
              "->", sum(r["lipCoveredLap"] for r in records if r["action"] in ("typing","thinking","sleep")))
    print(f"Partition check: {len(records)} frames; no gaps or duplicate opaque pixels")
    if enforce and failed:
        raise AssertionError("\n".join(failed))


if __name__ == "__main__":
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("pack", type=Path)
    p.add_argument("output", type=Path)
    p.add_argument("--fixture", type=Path)
    p.add_argument("--before", type=Path)
    p.add_argument("--enforce", action="store_true")
    p.add_argument("--details", action="store_true", help="print each design/facing/phase before and after")
    args = p.parse_args()
    if args.fixture:
        fixture(args.fixture, args.pack)
    records = measure(args.fixture or args.pack, args.output)
    before = json.loads(args.before.read_text(encoding="utf-8")) if args.before else None
    report(records, before, args.enforce, args.details)
