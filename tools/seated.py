"""Derive seated prototypes from locked art; never writes the runtime pack.

Run with the existing sprite-gen Python environment. All writes stay in
assets/art/seated. The appended rows preserve the original 13 rows verbatim.
"""
from __future__ import annotations

import argparse
from collections import Counter
import copy
import hashlib
import io
import json
from pathlib import Path
import re
import sys

import numpy as np
from PIL import Image, ImageDraw
sys.dont_write_bytecode = True
from facelock import blink_frame
from publication import atomic_write, publish_batch

ROOT = Path(__file__).resolve().parents[1]
ART = ROOT / "assets/art"
OUT = ART / "seated"
STATES = ["idle", "walk_down", "walk_left", "walk_right", "walk_up",
          "typing_back", "typing_side", "report", "sleep", "coffee", "phone",
          "talk", "surprised"]
COUNTS = [4, 4, 4, 4, 4, 3, 3, 3, 2, 3, 3, 3, 2]
NEW = ["seated_back", "seated_left", "seated_right"]
# Native idle calibrations: neck, hip, torso x bounds, sleeve sampling rectangle.
# ponytail: explicit roster calibration; new silhouettes need a reviewed entry.
PROFILES = {
    "apron-baker": (39, 48, (9, 23), (4, 34, 10, 40)),
    "beanie-scarf": (36, 51, (9, 24), (6, 40, 10, 47)),
    "cap-teen": (30, 44, (10, 23), (10, 34, 15, 40)),
    "cat-hood": (37, 49, (10, 23), (12, 39, 17, 45)),
    "dev-hoodie": (33, 44, (8, 25), (10, 36, 17, 43)),
    "glasses-bun": (34, 44, (9, 23), (12, 35, 18, 40)),
    "labcoat-tall": (34, 48, (10, 23), (12, 37, 18, 46)),
    "manager-suit": (36, 51, (11, 22), (12, 40, 17, 47)),
    "professor": (33, 50, (9, 23), (12, 35, 18, 42)),
    "punk-girl": (33, 44, (9, 24), (12, 35, 18, 40)),
    "red-braid": (31, 45, (10, 23), (12, 32, 18, 38)),
    "p5": (42, 51, (10, 22), (12, 42, 18, 47)),
}
# Rectangles for the visible hanging arm; torso column fills its vacated pixels.
ARMS = {
    "apron-baker": {"left": ((18, 39, 24, 50), 12), "right": ((8, 38, 14, 50), 21)},
    "beanie-scarf": {"left": ((19, 39, 25, 51), 14), "right": ((10, 39, 16, 51), 21)},
    "cap-teen": {"left": ((18, 32, 23, 44), 13), "right": ((11, 32, 16, 44), 20)},
    "cat-hood": {"left": ((16, 39, 21, 49), 13), "right": ((12, 39, 17, 49), 21)},
    "dev-hoodie": {"left": ((10, 34, 17, 44), 20), "right": ((17, 34, 25, 44), 13)},
    "glasses-bun": {"left": ((14, 34, 19, 44), 21), "right": ((12, 34, 17, 44), 22)},
    "professor": {"left": ((16, 35, 22, 49), 13), "right": ((10, 35, 16, 49), 20)},
    "red-braid": {"left": ((17, 30, 22, 45), 14), "right": ((11, 30, 15, 44), 20)},
    "labcoat-tall": {"left": ((16, 34, 21, 44), 14), "right": ((11, 34, 17, 44), 19)},
    "manager-suit": {"left": ((17, 38, 22, 51), 13), "right": ((12, 38, 17, 51), 20)},
    "punk-girl": {"left": ((17, 33, 22, 44), 13), "right": ((12, 33, 17, 44), 21)},
    "p5": {"left": ((17, 42, 22, 51), 13), "right": ((12, 42, 17, 51), 20)},
}
# Broad automatic masks need reviewed idle eye interiors to preserve the face.
HEAD_CUTS = {"apron-baker": {"back": 34}}
BLINK_EYES = {"red-braid": {
    "left": [(8, 21, 10, 25), (14, 21, 18, 25)],
    "right": [(13, 22, 17, 25), (22, 22, 25, 25)]},
    "apron-baker": {"left": [(11, 28, 15, 31)],
                    "right": [(15, 27, 18, 31), (19, 27, 23, 31)]}}
BLINK_SKIN_POINTS = {"red-braid": {"left": (12, 24), "right": (20, 24)},
                     "apron-baker": {"left": (16, 28), "right": (14, 29)}}


def digest(data):
    return hashlib.sha256(data).hexdigest()


def publish(path, data):
    path.parent.mkdir(parents=True, exist_ok=True)
    if path.exists() and path.read_bytes() == data:
        return
    atomic_write(path, data)


def png(im):
    buf = io.BytesIO()
    im.save(buf, format="PNG")
    return buf.getvalue()


def frame(im, manifest, state, index=0):
    r = manifest["frame_layout"]["rows"][state][index]
    return im.crop((r["x"], r["y"], r["x"] + r["w"], r["y"] + r["h"]))


def palette(im, box):
    colors = Counter(tuple(map(int, p)) for p in np.array(im.crop(box)).reshape(-1, 4) if p[3])
    if not colors:
        raise ValueError(f"empty palette sample {box}")
    common = colors.most_common(10)
    # Sample only existing RGBA pixels: no interpolation or new palette colors.
    outline = min((c for c, _ in common), key=lambda c: sum(c[:3]))
    candidates = [(c, n) for c, n in common if sum(c[:3]) > sum(outline[:3]) + 70]
    fill = max(candidates or common, key=lambda cn: cn[1])[0]
    light = max((c for c, _ in candidates or common), key=lambda c: sum(c[:3]))
    return outline, fill, light


def shift(im, x=0, y=0):
    result = Image.new("RGBA", (32, 64))
    result.alpha_composite(im, (x, y))
    return result


def make_pose(source, direction, profile, seat_y, blink_delta=None, skin_colors=None, arm=None):
    neck, hip, core, sleeve_box = profile
    delta = seat_y - hip
    if direction != "back" and arm:
        x0, y0, x1, y1 = arm[0]
        sleeve_box = (x0, y0, x1, min(y1, y0 + 6))
    sleeve = palette(source, sleeve_box)
    lower = palette(source, (11, hip, 23, min(58, hip + 9)))
    shoe = palette(source, (8, 56, 25, 60))
    # Skin is sampled from the original hanging hands, never guessed by RGB.
    skin_box = (11, max(neck, hip - 5), 21, hip)
    skin = skin_colors or palette(source, skin_box)
    upper = source.copy()
    a = np.array(upper)
    a[hip:] = 0
    if direction == "back":
        # Keep hair/hood/upper shoulders, tuck the lower arms inside the torso.
        elbow = min(hip - 4, neck + 7)
        a[elbow:hip, :core[0]] = 0
        a[elbow:hip, core[1]:] = 0
    else:
        # Drop the original vertical forearm/hand; replacement covers the sleeve.
        # Retain side hair and accessories above the elbow.
        elbow = min(hip - 3, neck + 7)
        if direction == "right":
            a[elbow:hip, core[1]:] = 0
        else:
            a[elbow:hip, :core[0]] = 0
        if arm:
            (x0, y0, x1, y1), sample_x = arm
            for y in range(max(y0, neck), min(y1, hip)):
                for x in range(x0, x1):
                    if a[y, x, 3]:
                        a[y, x] = np.array(source)[y, sample_x]
    upper = Image.fromarray(a)
    head = upper.crop((0, 0, 32, neck))
    body = upper.copy()
    body.paste((0, 0, 0, 0), (0, 0, 32, neck))
    keyboard_y = seat_y - 9
    poses, closed, nods = [], [], [0, 0, 1, 0]
    for f in range(4):
        out = shift(body, y=delta)
        draw = ImageDraw.Draw(out)
        if direction == "back":
            # Upper arms point down/forward; both hands are beyond the body.
            for side, x in enumerate((core[0], core[1] - 3)):
                y = min(keyboard_y - 1, neck + delta + 2) + int(f == (1 if side == 0 else 3))
                draw.rounded_rectangle((x, y, x + 3, keyboard_y + 1), radius=1,
                                       fill=sleeve[1], outline=sleeve[0])
                draw.point((x + 1, y + 1), fill=sleeve[2])
        else:
            sign = 1 if direction == "right" else -1
            x = 17 if sign == 1 else 15
            # Static thigh, bent knee/calf and shoe: no inherited walking pixels.
            knee = x + sign * 8
            draw.rectangle((min(x, knee), seat_y - 2, max(x, knee), seat_y + 3),
                           fill=lower[1], outline=lower[0])
            draw.rectangle((knee - 2, seat_y + 2, knee + 2, 58),
                           fill=lower[1], outline=lower[0])
            draw.rectangle((min(knee - 2, knee + sign * 4), 58,
                            max(knee + 2, knee + sign * 4), 59),
                           fill=shoe[1], outline=shoe[0])
            # Far and near hands alternate a one-pixel key tap, not an arm swing.
            for far in (True, False):
                y = keyboard_y - int((f == 1 and far) or (f == 3 and not far))
                root_x = x - sign * int(far)
                end = x + sign * (9 if far else 11)
                shoulder_y = min(keyboard_y - 2, neck + delta + 2)
                draw.line([(root_x, shoulder_y), (root_x, y - 1)],
                          fill=sleeve[0], width=5)
                draw.line([(root_x, shoulder_y + 1), (root_x, y - 1)],
                          fill=sleeve[1], width=3)
                draw.line([(root_x, y), (end - sign * 3, y)],
                          fill=sleeve[0], width=4)
                draw.line([(root_x, y - 1), (end - sign * 3, y - 1)],
                          fill=sleeve[1], width=2)
                draw.point((root_x + sign * 2, y - 2), fill=sleeve[2])
                # Hand colors come from exposed face skin (provided by caller).
                draw.line([(end - sign * 3, y), (end, y)],
                          fill=skin[0], width=3)
                draw.line([(end - sign * 2, y - 1), (end, y - 1)],
                          fill=skin[1], width=1)
        head_position = (0, delta + nods[f])
        out.alpha_composite(head, head_position)
        poses.append(out)
        if blink_delta is not None:
            c = np.array(out)
            for y, x, color in blink_delta:
                target_y = y + delta + nods[f]
                if y < neck and c[target_y, x, 3]:
                    c[target_y, x] = color
            closed.append(Image.fromarray(c))
    return poses, closed, {"sourceHipY": hip, "sourceNeckY": neck,
                           "translation": {"x": 0, "y": delta},
                           "headOffsets": [{"x": 0, "y": n} for n in nods],
                           "keyboardAnchor": {"x": 16 if direction == "back" else
                                              (28 if direction == "right" else 4),
                                              "y": keyboard_y}}


def load(mp):
    blobs = {mp: mp.read_bytes()}
    m = json.loads(blobs[mp])
    sp = mp.parent / m["sprite_sheet_alpha"]
    blobs[sp] = sp.read_bytes()
    im = Image.open(io.BytesIO(blobs[sp])).convert("RGBA")
    rows = m["frame_layout"]["rows"]
    if im.size != (128, 832) or set(rows) != set(STATES):
        raise ValueError("incomplete 13-row sheet")
    for s, count in zip(STATES, COUNTS):
        if (len(rows[s]) != count or m["animation"]["rows"][s]["frames"] != count
                or any(r["w"] != 32 or r["h"] != 64 or r["x"] < 0 or r["y"] < 0
                       or r["x"] + 32 > 128 or r["y"] + 64 > 832 for r in rows[s])
                or any(not frame(im, m, s, f).getbbox() for f in range(count))):
            raise ValueError(f"incomplete {s}")
    rp = mp.parent / m["facelock"]["report"]
    blobs[rp] = rp.read_bytes()
    r = json.loads(blobs[rp])
    for path, sha in r["sourceHashes"].items():
        p = ROOT / path
        blobs[p] = p.read_bytes()
        if digest(blobs[p]) != sha:
            raise ValueError(f"stale face lock: {path}")
    for b in m.get("blink", {}).values():
        p = mp.parent / b["file"]
        blobs[p] = p.read_bytes()
    if not m.get("blink"):
        p = ROOT / "tools/facelock.py"
        blobs[p] = p.read_bytes()
    return m, im, r, blobs


def process(cid, mp, seat_y):
    m, original, lock, blobs = load(mp)
    if cid not in PROFILES and not re.fullmatch(
            r"(?:codex|claude|antigravity)-(?:worker|supervisor|subagent)-0[1-4]|worker(?:-(?:subagent|supervisor))?", cid):
        raise ValueError("new silhouette needs a reviewed seated calibration")
    if cid in BLINK_EYES:
        p = ROOT / "tools/facelock.py"
        blobs[p] = p.read_bytes()
    profile = PROFILES.get(cid, PROFILES["p5"])
    sheet = Image.new("RGBA", (128, 1024))
    sheet.paste(original)
    result = copy.deepcopy(m)
    result.pop("publication", None)
    result["characterId"] = cid
    result["game_input"] = result["sprite_sheet_alpha"] = f"{cid}.png"
    result["anchor"] = {"x": 16, "y": 60}
    result["seatAnchor"] = {"x": 16, "y": seat_y}
    result.pop("sprite_sheet_alpha_report", None)
    result.pop("base_image", None)
    result["facelock"]["report"] = "source.facelock.json"
    result["seated"] = {"version": 1, "method": "locked-pixel-crop-and-repose",
                         "blink": {}, "rows": {}, "sourceHashes":
                         {p.relative_to(ROOT).as_posix(): digest(b) for p, b in blobs.items()}}
    blink_sheet = Image.new("RGBA", (128, 128))
    files, all_poses, checks = {}, [], []
    for row, direction, idle_col in zip(range(13, 16), ("back", "left", "right"), (3, 1, 2)):
        base_profile = PROFILES.get(cid, PROFILES["p5"])
        profile = (HEAD_CUTS.get(cid, {}).get(direction, base_profile[0]), *base_profile[1:])
        state = NEW[row - 13]
        source = frame(original, m, "idle", idle_col)
        blink_delta = None
        if direction != "back":
            b = m.get("blink", {}).get(direction)
            walk = np.array(frame(original, m, f"walk_{direction}", 0))
            if b:
                closed = np.array(Image.open(io.BytesIO(blobs[mp.parent / b["file"]])).convert("RGBA"))
            else:
                eye_boxes = lock["rows"][f"walk_{direction}"]["eyeBoxes"]
                closed = blink_frame(walk, [(x0, min(y1 - 1, y0 + 2), x1, y1)
                                            for x0, y0, x1, y1 in eye_boxes])
            dx, dy = lock["rows"][f"idle_{direction}"]["offsets"][0]
            boxes = lock["rows"][f"idle_{direction}"]["eyeBoxes"]
            blink_delta = [(int(y + dy), int(x + dx), closed[y, x])
                           for y, x in zip(*np.where(np.any(walk != closed, axis=2)))
                           if 0 <= x + dx < 32 and 0 <= y + dy < profile[0]
                           and any(x0 <= x < x1 and y0 <= y < y1 for x0, y0, x1, y1 in boxes)]
            if not blink_delta:
                raise ValueError(f"no transferable blink pixels: {direction}")
            if cid in BLINK_EYES:
                idle = np.array(source)
                idle_closed = blink_frame(idle, BLINK_EYES[cid][direction],
                                          BLINK_SKIN_POINTS[cid][direction])
                blink_delta = [(int(y), int(x), idle_closed[y, x])
                               for y, x in zip(*np.where(np.any(idle != idle_closed, axis=2)))]
        skin_colors = None
        if direction != "back":
            record = lock["rows"][f"idle_{direction}"]
            dx, dy = record["offsets"][0]
            boxes = record["eyeBoxes"]
            face = np.array(source)[min(b[3] + dy for b in boxes):profile[0]]
            warm = Counter(tuple(map(int, p)) for p in face.reshape(-1, 4)
                           if p[3] and 20 <= int(p[0]) - int(p[1]) <= 100
                           and 10 <= int(p[1]) - int(p[2]) <= 90 and int(p[0]) > 70)
            if warm:
                fill = warm.most_common(1)[0][0]
                skin_colors = (min(warm, key=lambda p: sum(p[:3])), fill, fill)
        poses, blink_poses, metadata = make_pose(source, direction, profile, seat_y, blink_delta,
                                               skin_colors, ARMS.get(cid, ARMS["p5"]).get(direction))
        all_poses.append(poses)
        result["animation"]["rows"][state] = {"row": row, "frames": 4, "fps": 4,
                                               "durations_ms": [250] * 4, "loop": True}
        result["frame_layout"]["rows"][state] = [
            {"x": f * 32, "y": row * 64, "w": 32, "h": 64} for f in range(4)]
        result["seated"]["rows"][state] = {"source": {"state": "idle", "frame": idle_col},
                                            "seatAnchor": result["seatAnchor"], **metadata}
        for f, pose in enumerate(poses):
            sheet.paste(pose, (f * 32, row * 64))
        # Compare actual output heads after undoing the documented integer nod.
        a0 = np.array(poses[0])
        head_y = metadata["translation"]["y"]
        lo = max(0, head_y)
        hi = head_y + profile[0]
        head_mask = np.array(source)[lo - head_y:profile[0], :, 3] > 0
        head_error = max(int(np.max(np.abs(a0[lo:hi][head_mask].astype(int) -
                         np.array(p)[lo + metadata["headOffsets"][f]["y"]:
                                     hi + metadata["headOffsets"][f]["y"]][head_mask].astype(int))))
                         for f, p in enumerate(poses))
        leg_error = max(int(np.max(np.abs(a0[seat_y:].astype(int) -
                                             np.array(p)[seat_y:].astype(int)))) for p in poses)
        assert head_error == 0 and leg_error == 0, (cid, state, head_error, leg_error)
        assert all(p.getbbox() and set(np.unique(np.array(p)[:, :, 3])) <= {0, 255} for p in poses)
        assert all(np.any(np.array(p) != a0) for p in poses[1:]), "static typing loop"
        checks.append({"row": state, "alignedHeadMaxRgbaDifference": head_error,
                       "lowerBodyMaxRgbaDifference": leg_error})
        if blink_poses:
            filename = f"blink-{state}.png"
            files[filename] = png(blink_poses[0])
            result["seated"]["blink"][direction] = {"file": filename,
                "source": {"state": state, "frame": 0}, "durationMs": 100,
                "sheetRow": row - 14, "method": "reviewed-seated-eye-interiors" if cid in BLINK_EYES else
                ("transferred-locked-blink" if b else "facelock-blink-from-calibrated-walking-eyes")}
            for f, pose in enumerate(blink_poses):
                blink_sheet.paste(pose, (f * 32, (row - 14) * 64))
                diff = np.any(np.array(pose) != np.array(poses[f]), axis=2)
                assert diff.any() and not diff[hi + 1:].any(), "blink changed body"
    assert sheet.crop((0, 0, 128, 832)).tobytes() == original.tobytes()
    result["frame_layout"]["sheetHeight"] = 1024
    result["seated"]["blinkSheet"] = {"file": "seated-blink.png", "cellW": 32,
                                      "cellH": 64, "frames": 4, "fps": 4}
    # Preserve existing blink metadata and its relative files in the additive sheet.
    for direction, b in m.get("blink", {}).items():
        filename = Path(b["file"]).name
        files[filename] = blobs[mp.parent / b["file"]]
        result["blink"][direction]["file"] = filename
    report_path = mp.parent / m["facelock"]["report"]
    source_report = json.loads(blobs[report_path])
    for key in ("sourceManifest", "sourceSheet", "sourceFrames", "outputSheet", "outputManifest"):
        if source_report.get(key):
            source_report[key] = str((report_path.parent / source_report[key]).resolve())
    files["source.facelock.json"] = (json.dumps(source_report, ensure_ascii=False, indent=2) + "\n").encode()
    files[f"{cid}.png"] = png(sheet)
    files["seated-blink.png"] = png(blink_sheet)
    files[f"{cid}.manifest.json"] = (json.dumps(result, ensure_ascii=False, indent=2) + "\n").encode()
    files["qa.json"] = (json.dumps({"character": cid, "checks": checks,
        "original13RowsUnchanged": True, "frameCount": 12,
        "sourceManifest": mp.relative_to(ROOT).as_posix()}, indent=2) + "\n").encode()
    if any(p.read_bytes() != b for p, b in blobs.items()):
        raise ValueError("input changed during processing; rerun")
    publish_batch(OUT / cid, files, f"{cid}.manifest.json")
    return all_poses


def published_file(cid, name):
    manifest = json.loads((OUT / cid / f"{cid}.manifest.json").read_bytes())
    return OUT / cid / manifest.get("publication", {}).get("generation", "") / name


def scene(pose, direction, seat_y):
    """Diagnostic composition using unchanged env2 props (not env3 side art)."""
    canvas = Image.new("RGBA", (104, 90), "#e7dfcd")
    desk = Image.open(ART / "env2/sheets/desks.png").convert("RGBA").crop((0, 0, 64, 64))
    chair = Image.open(ART / "env2/sheets/office-chairs.png").convert("RGBA").crop((0, 0, 64, 64))
    monitor = Image.open(ART / "env2/sheets/monitors.png").convert("RGBA").crop((0, 0, 64, 64))
    hip_x, hip_y = 52, 64
    canvas.alpha_composite(chair, (hip_x - 32, hip_y - 47))
    if direction == "back":
        canvas.alpha_composite(desk, (hip_x - 32, 1))
        canvas.alpha_composite(monitor, (hip_x - 32, -16))
    else:
        desk_x = 59 if direction == "right" else -18
        canvas.alpha_composite(desk, (desk_x, hip_y - 9 - 43))
        canvas.alpha_composite(monitor, (41 if direction == "right" else 0, hip_y - 9 - 61))
    canvas.alpha_composite(pose, (hip_x - 16, hip_y - seat_y))
    if direction == "back":
        # Backrest is in front of the pelvis, keyboard/desk is beyond the hands.
        backrest = chair.copy()
        backrest.paste((0, 0, 0, 0), (0, 0, 64, 43))
        canvas.alpha_composite(backrest, (hip_x - 32, hip_y - 47))
    return canvas


def preview(published, seat_y):
    roster_group = [(cid, poses) for cid, poses in published.items() if cid in PROFILES]
    groups = [roster_group] if roster_group else []
    variants = [(cid, poses) for cid, poses in published.items() if cid not in PROFILES]
    groups += [variants[start:start + 13] for start in range(0, len(variants), 13)]
    for page, group in enumerate(groups):
        blink_row = page == 1 and "red-braid" in published
        contact = Image.new("RGBA", (4 * 104, (len(group) + int(blink_row)) * 102), "#35313e")
        draw = ImageDraw.Draw(contact)
        for row, (cid, poses) in enumerate(group):
            draw.text((2, row * 102), cid + " | back 0 / left 1 / right 3 / back 2", fill="white")
            for col, (view, f) in enumerate(((0, 0), (1, 1), (2, 3), (0, 2))):
                contact.alpha_composite(scene(poses[view][f], ("back", "left", "right")[view], seat_y),
                                        (col * 104, row * 102 + 12))
        if blink_row:
            y = len(group) * 102
            draw.text((2, y), "red-braid corrected blink | left open/shut right open/shut", fill="white")
            for col, (direction, view, shut) in enumerate((("left", 1, False), ("left", 1, True),
                                                         ("right", 2, False), ("right", 2, True))):
                p = Image.open(published_file("red-braid", f"blink-seated_{direction}.png")).convert("RGBA") if shut else published["red-braid"][view][0]
                contact.alpha_composite(scene(p, direction, seat_y), (col * 104, y + 12))
        path = OUT / "preview-seated.png" if page == 0 else OUT / variants[0][0] / f"preview-variants-{page}.png"
        publish(path, png(contact.resize((contact.width * 5, contact.height * 5), Image.Resampling.NEAREST)))
    # All four loop phases for the reported designs, plus normal/blink pairs.
    rows = []
    for cid in ("dev-hoodie", "professor", "red-braid"):
        if cid in published:
            for view, direction in enumerate(("back", "left", "right")):
                rows.append((f"{cid} {direction} | frames 0 1 2 3",
                             [scene(p, direction, seat_y) for p in published[cid][view]]))
    roster = [cid for cid in published if cid in PROFILES]
    if variants:
        roster.append(variants[0][0])
    for start in range(0, len(roster), 2):
        panels, names = [], []
        for cid in roster[start:start + 2]:
            names.append(cid)
            closed = Image.open(published_file(cid, "blink-seated_left.png")).convert("RGBA")
            panels.extend([scene(published[cid][1][0], "left", seat_y), scene(closed, "left", seat_y)])
        rows.append((" / ".join(names) + " | open / closed", panels))
    contact = Image.new("RGBA", (416, len(rows) * 102), "#35313e")
    d = ImageDraw.Draw(contact)
    for row, (label, panels) in enumerate(rows):
        d.text((2, row * 102), label, fill="white")
        for col, panel in enumerate(panels):
            contact.alpha_composite(panel, (104 * col, row * 102 + 12))
    target = next(iter(published))
    publish(OUT / target / "preview-motion-blink.png", png(contact.resize(
        (contact.width * 5, contact.height * 5), Image.Resampling.NEAREST)))
    if "apron-baker" in published:
        cid = "apron-baker"
        mp = ART / "roster" / cid / "locked" / f"{cid}.manifest.json"
        m, original, _, _ = load(mp)
        panels = []
        panels.append(("apron-baker original idle back/left/right + typing back",
            [scene(frame(original, m, state, f), direction, 52)
             for state, f, direction in (("idle", 3, "back"), ("idle", 1, "left"),
                                         ("idle", 2, "right"), ("typing_back", 0, "back"))]))
        for view, direction in enumerate(("back", "left", "right")):
            panels.append((f"apron-baker seated {direction} | 0 1 2 3",
                           [scene(p, direction, seat_y) for p in published[cid][view]]))
        panels.append(("apron-baker blink | left open/shut right open/shut", [
            scene(Image.open(published_file(cid, f"blink-seated_{direction}.png")).convert("RGBA") if shut else
                  published[cid][view][0], direction, seat_y)
            for direction, view, shut in (("left", 1, False), ("left", 1, True),
                                         ("right", 2, False), ("right", 2, True))]))
        contact = Image.new("RGBA", (416, len(panels) * 102), "#35313e")
        d = ImageDraw.Draw(contact)
        for row, (label, row_panels) in enumerate(panels):
            d.text((2, row * 102), label, fill="white")
            for col, panel in enumerate(row_panels):
                contact.alpha_composite(panel, (104 * col, row * 102 + 12))
        publish(OUT / cid / "preview-apron.png", png(contact.resize(
            (contact.width * 5, contact.height * 5), Image.Resampling.NEAREST)))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--seat-y", type=int, default=52, help="hip/seat anchor in native cell pixels (48..54)")
    parser.add_argument("--character", action="append", help="limit prototype to one or more IDs")
    args = parser.parse_args()
    if not 48 <= args.seat_y <= 54:
        parser.error("--seat-y must be 48..54 to retain the (16,60) foot convention")
    candidates = [(p.name, p / "locked" / f"{p.name}.manifest.json")
                  for p in sorted((ART / "roster").iterdir()) if p.is_dir()]
    candidates += [(mp.name.removesuffix(".manifest.json"), mp)
                   for mp in sorted((ART / "sheets/locked").glob("*.manifest.json"))]
    published = {}
    for cid, mp in candidates:
        if args.character and cid not in args.character:
            continue
        try:
            published[cid] = process(cid, mp, args.seat_y)
            print("SEATED", cid, flush=True)
        except (ValueError, KeyError, FileNotFoundError) as error:
            print("SKIP", cid, str(error), flush=True)
    if not published:
        raise SystemExit("no complete locked sources")
    preview(published, args.seat_y)
    print(f"Published {len(published)} characters / {len(published) * 12} seated frames", flush=True)


if __name__ == "__main__":
    main()
