"""Adapt final sprite-gen art to the runtime pack with the existing prop normalizer."""

from __future__ import annotations

import argparse
from collections import Counter
import hashlib
from io import BytesIO
import json
import math
import os
from pathlib import Path
import re
import sys
import tempfile

from PIL import Image, ImageDraw

from anim_normalize import normalize_sheet


ROOT = Path(__file__).resolve().parents[1]
GENERATED_BY = "tools/build_pack.py"
OWNED_PNG = re.compile(r"pack-[0-9a-f]{64}\.png\Z")
P5_IDS = sorted(
    f"{provider}-{role}-{number:02d}"
    for provider in ("codex", "claude", "antigravity")
    for role in ("worker", "supervisor", "subagent")
    for number in range(1, 5)
)
# Source names, exact required frame counts, runtime action and facing.
ACTIONS = (
    ("idle", 4, "idle", "any"),
    ("walk_down", 4, "walk", "down"),
    ("walk_left", 4, "walk", "left"),
    ("walk_right", 4, "walk", "right"),
    ("walk_up", 4, "walk", "up"),
    ("typing_back", 3, "typing", "up"),
    ("typing_side", 3, "typing", "right"),
    ("report", 3, "reporting", "any"),
    ("sleep", 2, "napping", "any"),
    ("coffee", 3, "coffee", "any"),
    ("phone", 3, "phone", "any"),
    ("talk", 3, "chatting", "any"),
    ("surprised", 2, "waiting", "any"),
)
SEATED_ACTIONS = (("seated_back", "up"), ("seated_left", "left"), ("seated_right", "right"))


class ProductionSheet:
    def __init__(self, image_path: Path, manifest_path: Path):
        self.data = json.loads(manifest_path.read_text(encoding="utf-8-sig"))
        self.directory = manifest_path.parent
        with Image.open(image_path) as source:
            if source.format != "PNG":
                raise ValueError("final sheet is not a PNG")
            self.image = source.convert("RGBA")
        self.layout = self.data["frame_layout"]
        self.rows = self.layout["rows"]
        self.animation = self.data["animation"]["rows"]
        if self.image.size != (self.layout["sheetWidth"], self.layout["sheetHeight"]):
            raise ValueError("sheet dimensions disagree with frame_layout")

    def frames(self, name: str, cell: tuple[int, int]) -> list[Image.Image]:
        result = []
        for rect in self.rows[name]:
            x, y, width, height = (rect[k] for k in ("x", "y", "w", "h"))
            if any(type(n) is not int for n in (x, y, width, height)):
                raise ValueError(f"{name}: frame coordinates must be integers")
            if (width, height) != cell or min(x, y) < 0:
                raise ValueError(f"{name}: invalid frame cell")
            if x + width > self.image.width or y + height > self.image.height:
                raise ValueError(f"{name}: frame outside sheet")
            frame = self.image.crop((x, y, x + width, y + height))
            if frame.getchannel("A").getbbox() is None:
                raise ValueError(f"{name}: empty frame")
            result.append(frame)
        if not result:
            raise ValueError(f"{name}: no frames")
        return result

    def playback(self, name: str) -> tuple[float, bool]:
        row = self.animation[name]
        fps, loop = row["fps"], row["loop"]
        if type(fps) not in (int, float) or not math.isfinite(fps) or not 0 < fps <= 60:
            raise ValueError(f"{name}: invalid fps")
        if type(loop) is not bool:
            raise ValueError(f"{name}: invalid loop")
        return fps, loop


def character_sheet(source: ProductionSheet) -> tuple[Image.Image, list[dict]]:
    """A partial production atlas may exist; validate all 42 cells before using it."""
    if (source.layout["cellWidth"], source.layout["cellHeight"]) != (32, 64):
        raise ValueError("character cells must be 32x64")
    frames_by_action = {}
    rectangles = set()
    for name, count, _, _ in ACTIONS:
        frames = source.frames(name, (32, 64))
        if len(frames) != count or source.animation[name]["frames"] != count:
            raise ValueError(f"{name}: expected {count} frames")
        for rect in source.rows[name]:
            rectangles.add(tuple(rect[k] for k in ("x", "y", "w", "h")))
        frames_by_action[name] = frames
        if name != "idle":
            source.playback(name)
    if len(rectangles) != 42:
        raise ValueError("character must have 42 distinct valid frames")

    # One scale per design, measured from the front idle silhouette. Bake the
    # nearest-neighbour reduction into integer pixels; keep feet at (16, 60).
    box = frames_by_action["idle"][0].getchannel("A").getbbox()
    factor = 30 / (box[3] - box[1])
    size = (round(32 * factor), round(64 * factor))

    # Seated rows share one integer placement, including their blink copies.
    # Measuring each phase's alpha bottom would move the pelvis on back poses.
    seated_position = (16 - size[0] // 2, 60 - round(60 * size[1] / 64))

    def seated_normalized(frame):
        result = Image.new("RGBA", (32, 64))
        result.paste(frame.resize(size, Image.Resampling.NEAREST), seated_position)
        return result

    def normalized(frame):
        bottom = frame.getchannel("A").getbbox()[3]
        result = Image.new("RGBA", (32, 64))
        resized = frame.resize(size, Image.Resampling.NEAREST)
        result.paste(resized, (16 - size[0] // 2, 60 - round(bottom * size[1] / 64)))
        return result

    # Optional locked blink pixels are transferred to the existing pose using
    # its published eye boxes/offsets, never substituting a walking body.
    blink_rows = []
    lock = source.data.get("facelock", {})
    report_path = source.directory / lock.get("report", "missing")
    try:
        report = json.loads(report_path.read_text(encoding="utf-8-sig"))
        blink = source.data.get("blink", {})
        for name, _, action, direction in ACTIONS:
            if name.startswith("walk") or name in ("sleep", "report"):
                continue
            for column, frame in enumerate(frames_by_action[name]):
                facing = ("down", "left", "right", "up")[column] if name == "idle" else direction
                if facing == "up":
                    continue
                row_key = f"idle_{facing}" if name == "idle" else name
                target = report["rows"].get(row_key)
                if not target or not target.get("eyeBoxes"):
                    continue
                visible = facing if facing in blink else "down"
                entry = blink.get(visible)
                if not entry:
                    continue
                with Image.open(source.directory / entry["file"]) as image:
                    closed = image.convert("RGBA")
                origin = report["rows"][entry["source"]["state"]]
                opened = source.frames(entry["source"]["state"], (32, 64))[entry["source"]["frame"]]
                result = frame.copy()
                dx, dy = target["offsets"][0 if name == "idle" else column]
                for index, eye in enumerate(target["eyeBoxes"]):
                    reference = origin["eyeBoxes"][min(index, len(origin["eyeBoxes"]) - 1)]
                    patch = closed.crop(reference)
                    normal = opened.crop(reference)
                    mask = Image.new("L", patch.size)
                    mask.putdata([255 if a != b else 0 for a, b in zip(patch.get_flattened_data(), normal.get_flattened_data())])
                    eye_size = (eye[2] - eye[0], eye[3] - eye[1])
                    patch = patch.resize(eye_size, Image.Resampling.NEAREST)
                    mask = mask.resize(eye_size, Image.Resampling.NEAREST)
                    # Preserve alpha/silhouette even on unusual authored eyes.
                    for py in range(eye_size[1]):
                        for px in range(eye_size[0]):
                            x, y = eye[0] + dx + px, eye[1] + dy + py
                            if 0 <= x < 32 and 0 <= y < 64 and mask.getpixel((px, py)) and result.getpixel((x, y))[3] == 255:
                                result.putpixel((x, y), (*patch.getpixel((px, py))[:3], 255))
                blink_rows.append((action, facing, column, result))
    except (OSError, ValueError, KeyError, TypeError):
        blink_rows = []

    image = Image.new("RGBA", (128, 13 * 64))
    clips = []
    for row, (name, _, action, direction) in enumerate(ACTIONS):
        frames = frames_by_action[name]
        for column, frame in enumerate(frames):
            image.paste(normalized(frame), (column * 32, row * 64))
        if name == "idle":
            for column, facing in enumerate(("down", "left", "right", "up")):
                clips.append(clip("idle", row, 1, 1, False, facing, column))
        else:
            fps, loop = source.playback(name)
            clips.append(clip(action, row, len(frames), fps, loop, direction))
    # Group blink copies so their frame index and playback match the normal clip.
    for action, facing in dict.fromkeys((a, f) for a, f, _, _ in blink_rows):
        group = [item for item in blink_rows if item[:2] == (action, facing)]
        row = image.height // 64
        extended = Image.new("RGBA", (128, image.height + 64))
        extended.paste(image, (0, 0)); image = extended
        for column, (_, _, _, frame) in enumerate(group):
            image.paste(normalized(frame), (column * 32, row * 64))
        base = next(c for c in clips if c["name"] == action and c["direction"] == facing)
        clips.append(clip("blink-" + action, row, len(group), base["fps"], base["loop"], facing))

    if "seated" in source.data:
        seated = source.data["seated"]
        if seated["version"] != 1:
            raise ValueError("unsupported seated version")
        blink = seated["blinkSheet"]
        if (blink["cellW"], blink["cellH"], blink["frames"]) != (32, 64, 4):
            raise ValueError("seated blink cells/count must match the four normal phases")
        with Image.open(source.directory / blink["file"]) as opened:
            if opened.format != "PNG" or opened.size != (128, 128):
                raise ValueError("seated blink sheet must be a 128x128 PNG")
            blink_image = opened.convert("RGBA")
        for name, direction in SEATED_ACTIONS:
            frames = source.frames(name, (32, 64))
            if len(frames) != 4 or source.animation[name]["frames"] != 4:
                raise ValueError(f"{name}: expected four seated phases")
            fps, loop = source.playback(name)
            metadata = seated["rows"][name]
            anchor = metadata["seatAnchor"]
            if any(type(anchor[k]) is not int or not 0 <= anchor[k] <= limit
                   for k, limit in (("x", 32), ("y", 64))):
                raise ValueError(f"{name}: invalid seatAnchor")
            if any(frame.crop((0, anchor["y"], 32, 64)).tobytes()
                   != frames[0].crop((0, anchor["y"], 32, 64)).tobytes() for frame in frames[1:]):
                raise ValueError(f"{name}: lower body moves between seated phases")
            for rect in source.rows[name]:
                rectangle = tuple(rect[k] for k in ("x", "y", "w", "h"))
                if rectangle in rectangles:
                    raise ValueError(f"{name}: overlapping character frame")
                rectangles.add(rectangle)
            transformed = {"x": seated_position[0] + round(anchor["x"] * size[0] / 32),
                           "y": seated_position[1] + round(anchor["y"] * size[1] / 64)}
            groups = [(name, frames)]
            if direction != "up":
                entry = seated["blink"][direction]
                if entry["source"] != {"state": name, "frame": 0} or blink["fps"] != fps:
                    raise ValueError(f"{name}: blink playback/source disagrees with seated clip")
                blink_row = entry["sheetRow"]
                if type(blink_row) is not int or blink_row not in (0, 1):
                    raise ValueError(f"{name}: invalid blink row")
                closed = [blink_image.crop((f * 32, blink_row * 64, (f + 1) * 32, (blink_row + 1) * 64))
                          for f in range(4)]
                for f, (normal, shut) in enumerate(zip(frames, closed)):
                    head_end = (metadata["sourceNeckY"] + metadata["translation"]["y"]
                                + metadata["headOffsets"][f]["y"])
                    if (normal.getchannel("A").tobytes() != shut.getchannel("A").tobytes()
                            or normal.crop((0, head_end, 32, 64)).tobytes()
                            != shut.crop((0, head_end, 32, 64)).tobytes()):
                        raise ValueError(f"{name}: blink phase {f} changes the body/alpha")
                groups.append(("blink-" + name, closed))
            for runtime_name, group in groups:
                row = image.height // 64
                extended = Image.new("RGBA", (128, image.height + 64))
                extended.paste(image, (0, 0))
                image = extended
                for column, frame in enumerate(group):
                    image.paste(seated_normalized(frame), (column * 32, row * 64))
                clips.append(dict(clip(runtime_name, row, 4, fps, loop, direction), seatAnchor=transformed))
    return image, clips


def clip(name, row, frames=1, fps=1, loop=False, direction="any", column=0):
    return dict(name=name, direction=direction, row=row, column=column,
                frames=frames, fps=fps, loop=loop)


def atlas(frames: list[Image.Image]) -> Image.Image:
    width, height = frames[0].size
    image = Image.new("RGBA", (width * len(frames), height))
    for column, frame in enumerate(frames):
        if frame.size != (width, height):
            raise ValueError("atlas cell sizes differ")
        image.paste(frame, (column * width, 0))
    return image


def neutral_provider_icons() -> dict[str, Image.Image]:
    """Original neutral circle, five-point star and diamond; no brand inputs."""
    result = {}
    for name, accent in (("codex", "#58b8a8"), ("claude", "#e79a68"),
                         ("antigravity", "#b1a2e5")):
        image = Image.new("RGBA", (12, 12))
        draw = ImageDraw.Draw(image)
        if name == "codex":
            draw.ellipse((1, 1, 10, 10), fill=accent)
        elif name == "claude":
            draw.polygon([(5, 1), (7, 4), (10, 4), (8, 7), (9, 10),
                          (5, 8), (2, 10), (3, 7), (1, 4), (4, 4)], fill=accent)
        else:
            draw.polygon([(5, 1), (10, 5), (6, 10), (1, 6)], fill=accent)
        result[name] = image
    return result


def laptop_sprite() -> Image.Image:
    """A tiny original open laptop for a single walkable world tile."""
    image = Image.new("RGBA", (16, 16))
    draw = ImageDraw.Draw(image)
    draw.rectangle((3, 3, 12, 9), fill="#20252b")
    draw.rectangle((4, 4, 11, 8), fill="#d7c9ab")
    draw.rectangle((4, 4, 11, 7), fill="#245154")
    draw.line((5, 5, 8, 5), fill="#86c8bb")
    draw.line((5, 6, 6, 6), fill="#86c8bb")
    draw.polygon([(2, 10), (13, 10), (14, 12), (1, 12)], fill="#20252b")
    draw.line((3, 10, 12, 10), fill="#d7c9ab")
    draw.line((2, 11, 13, 11), fill="#9a9689")
    draw.line((5, 10, 10, 10), fill="#20252b")
    draw.line((7, 11, 8, 11), fill="#d7c9ab")
    return image


def counter_stool_sprite() -> Image.Image:
    """Original backless pantry stool, with its feet on source y=30."""
    image = Image.new("RGBA", (32, 32))
    draw = ImageDraw.Draw(image)
    outline = "#20252b"
    draw.polygon([(11, 12), (14, 12), (12, 29), (8, 29)], fill=outline)
    draw.polygon([(18, 12), (21, 12), (23, 29), (19, 29)], fill=outline)
    draw.line((12, 14, 10, 27), fill="#8c9184", width=2)
    draw.line((19, 14, 21, 27), fill="#8c9184", width=2)
    draw.rectangle((11, 21, 20, 23), fill=outline)
    draw.line((12, 21, 19, 21), fill="#b8b6a3")
    draw.ellipse((8, 7, 23, 14), fill=outline)
    draw.ellipse((9, 8, 22, 12), fill="#bb8350")
    draw.line((12, 8, 19, 8), fill="#ddb88d")
    draw.line((11, 12, 20, 12), fill="#80543a")
    return image


def desk_screen(desk: Image.Image, bounds: tuple[int, int, int, int],
                state: str, frame: int = 0) -> Image.Image:
    """Keep the computer, chair and desk fixed; change only screen pixels."""
    image = desk.copy()
    draw = ImageDraw.Draw(image)
    x, y, right, bottom = bounds
    draw.rectangle(bounds, fill="#172125" if state == "off" else "#10282c")
    if state == "working":
        for offset, length in ((1, 4), (3, 10), (5, 7)):
            draw.line((x + 2, y + offset, min(x + length, right - 1), y + offset),
                      fill="#86c8bb")
        draw.point((right - 2, bottom - 1), fill="#d7c9ab" if frame % 2 == 0 else "#10282c")
        draw.point((x + 6 + frame, y + 1), fill="#d7c9ab")
    elif state == "screensaver":
        cx, cy = (x + right) // 2, (y + bottom) // 2
        draw.polygon([(cx, cy - 2), (cx + 3, cy), (cx, cy + 2), (cx - 3, cy)],
                     fill="#b1a2e5")
    return image


def rotate_furniture(manifest: dict, images: dict[str, Image.Image]):
    """Bake clockwise cells and matching geometry into each existing sheet."""
    sheets = {sheet["id"]: sheet for sheet in manifest["sheets"]}
    originals = {}
    facings = {"up": "right", "right": "down", "down": "left", "left": "up"}
    linked_seats = {base[key]: next(p for p in base["points"] if p["action"] == "typing")
                    for asset in manifest["furniture"] for base in asset["variants"][:1]
                    for key in ("chairBackId", "chairFrontId") if key in base}
    for asset in manifest["furniture"]:
        variants = list(asset["variants"])
        for base in variants:
            sheet = sheets[base["sheetId"]]
            sheet_id = sheet["id"]
            cell = sheet["cellW"]
            if cell != sheet["cellH"]:
                raise ValueError(f"{sheet_id}: furniture rotation needs square cells")
            if sheet_id not in originals:
                original = images[sheet_id]
                originals[sheet_id] = (original.height // cell,
                                       {row["name"]: row.copy() for row in sheet["rows"]})
                rotated = Image.new("RGBA", (original.width, original.height * 4))
                rotated.paste(original, (0, 0))
                for y in range(0, original.height, cell):
                    for x in range(0, original.width, cell):
                        frame = original.crop((x, y, x + cell, y + cell))
                        for turn in range(1, 4):
                            frame = frame.transpose(Image.Transpose.ROTATE_270)
                            rotated.paste(frame, (x, y + turn * original.height))
                images[sheet_id] = rotated
            row_count, clips = originals[sheet_id]
            original_clip = clips[base["animation"]]
            offset = original_clip.get("footOffset", {"x": 0, "y": 0})
            anchor_x, anchor_y = sheet["anchor"]["x"] - offset["x"], sheet["anchor"]["y"] - offset["y"]
            previous = base
            seat = linked_seats.get(asset["id"])
            if "chairBackId" in base:
                seat = next(p for p in base["points"] if p["action"] == "typing")
            depth_delta = base["footprintH"] * 16 + base["zOrder"] - (seat["y"] + 1) * 16 if seat else None
            for turn in range(1, 4):
                width, height = previous["footprintW"], previous["footprintH"]
                anchor_x, anchor_y = cell - anchor_y - height * 16, anchor_x
                animation = f"{base['animation']}-r{turn * 90}"
                if not any(row["name"] == animation for row in sheet["rows"]):
                    sheet["rows"].append(dict(original_clip, name=animation,
                                              row=original_clip["row"] + turn * row_count,
                                              footOffset={"x": sheet["anchor"]["x"] - anchor_x,
                                                          "y": sheet["anchor"]["y"] - anchor_y}))
                variant = dict(previous, rotation=turn * 90, footprintW=height, footprintH=width,
                               blockedCells=[{"x": height - 1 - tile["y"], "y": tile["x"]}
                                             for tile in previous["blockedCells"]],
                               points=[dict(point, x=height - 1 - point["y"], y=point["x"],
                                            facing=facings[point["facing"]]) for point in previous["points"]],
                               animation=animation)
                if "chairSeatAnchor" in previous:
                    contact = previous["chairSeatAnchor"]
                    variant["chairSeatAnchor"] = {"x": height * 16 - contact["y"], "y": contact["x"]}
                if seat is not None:
                    seat = dict(seat, x=height - 1 - seat["y"], y=seat["x"])
                    variant["zOrder"] = (seat["y"] + 1 - variant["footprintH"]) * 16 + depth_delta
                asset["variants"].append(variant)
                previous = variant


def furniture_anchor(frames, footprint):
    boxes = [frame.getchannel("A").getbbox() for frame in frames]
    left = min(box[0] for box in boxes)
    right = max(box[2] for box in boxes)
    bottom = max(box[3] for box in boxes)
    width, height = footprint
    return {"x": round(left - (width * 16 - (right - left)) / 2),
            "y": round(bottom - height * 16)}


def atomic_write(path: Path, data: bytes):
    if path.is_file() and path.read_bytes() == data:
        return
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = None
    try:
        with tempfile.NamedTemporaryFile(dir=path.parent, prefix=".build-pack-",
                                         suffix=".tmp", delete=False) as stream:
            temporary = Path(stream.name)
            stream.write(data)
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(temporary, path)
    finally:
        if temporary is not None:
            temporary.unlink(missing_ok=True)


def build_pack(art_dir: Path, output: Path) -> dict:
    art_dir, output = art_dir.resolve(), output.resolve()
    if output == art_dir or art_dir in output.parents:
        raise ValueError("output must be outside the production art directory")
    manifest = {"version": 1, "generatedBy": GENERATED_BY,
                "sheets": [], "characters": [], "furniture": [], "tileTypes": []}
    pngs = {}
    images = {}
    skipped = []
    fallbacks = []
    cache = {}

    def production(path, metadata=None, prop=None):
        key = (path, metadata, prop)
        if key not in cache:
            cache[key] = ProductionSheet(path, metadata or path.with_suffix(".manifest.json"))
            if prop is not None:
                cache[key].image, _ = normalize_sheet(cache[key].image, cache[key].data, prop=prop)
        return cache[key]

    def add_sheet(sheet_id, image, cell, anchor, rows):
        images[sheet_id] = image
        manifest["sheets"].append(dict(id=sheet_id, file="", cellW=cell[0],
                                      cellH=cell[1], anchor=anchor, scale=1, rows=rows))

    def add_character(character_id, path, metadata=None):
        metadata = metadata or path.with_suffix(".manifest.json")
        image, rows = seated_characters.get(character_id, (None, None))
        locked = path.parent / "locked" / f"{character_id}.png"
        if not locked.is_file():
            locked = art_dir / "roster" / character_id / "locked" / f"{character_id}.png"
        locked_metadata = locked.with_suffix(".manifest.json")
        if image is None:
            try:
                data = json.loads(locked_metadata.read_text(encoding="utf-8-sig"))
                report = json.loads((locked.parent / data["facelock"]["report"]).read_text(encoding="utf-8-sig"))
                if all(hashlib.sha256((ROOT / name).read_bytes()).hexdigest() == digest
                       for name, digest in report["sourceHashes"].items()):
                    image, rows = character_sheet(production(locked, locked_metadata))
            except (OSError, ValueError, KeyError, TypeError):
                pass
        if image is None:
            image, rows = character_sheet(production(path, metadata))
        sheet_id = f"char-{character_id}"
        add_sheet(sheet_id, image, (32, 64), {"x": 16, "y": 60}, rows)
        manifest["characters"].append({"id": character_id, "sheetId": sheet_id})

    # Published seated pairs are authoritative; art authors can have pending
    # directories. A broken published pair fails before roster's skip path.
    seated_characters = {}
    for directory in sorted((art_dir / "seated").glob("*")):
        if not directory.is_dir() or directory.name.startswith("."):
            continue
        path = directory / f"{directory.name}.png"
        if not path.is_file() or not path.with_suffix(".manifest.json").is_file():
            continue
        try:
            source = production(path)
            if "seated" not in source.data:
                raise ValueError("missing seated metadata")
            seated_characters[directory.name] = character_sheet(source)
        except (OSError, ValueError, KeyError, TypeError) as error:
            raise ValueError(f"{directory.name}: invalid seated publication: {error}") from error

    for character_id in P5_IDS:
        add_character(character_id, art_dir / "sheets" / f"{character_id}.png")
    roster_dir = art_dir / "roster"
    if roster_dir.is_dir():
        for directory in sorted(roster_dir.iterdir()):
            if not directory.is_dir() or directory.name.startswith("."):
                continue
            try:
                if directory.name in P5_IDS:
                    raise ValueError("duplicate character ID")
                final = directory / f"{directory.name}.png"
                metadata = final.with_suffix(".manifest.json")
                if not final.is_file() or not metadata.is_file():
                    final = directory / "run" / "sprite-sheet-alpha.png"
                    metadata = directory / "run" / "manifest.json"
                add_character(directory.name, final, metadata)
            except (OSError, ValueError, KeyError, TypeError) as error:
                skipped.append({"id": directory.name, "reason": str(error)})

    for character_id in sorted(seated_characters.keys() - {c["id"] for c in manifest["characters"]}):
        add_character(character_id, art_dir / "seated" / character_id / f"{character_id}.png")

    props_path = art_dir / "env2" / "normalized" / "base" / "props.png"
    if not props_path.is_file() or not props_path.with_suffix(".manifest.json").is_file():
        props_path = art_dir / "sheets" / "props.png"
    props = production(props_path, prop="props")
    tiles = production(art_dir / "sheets" / "tiles.png")
    icons = production(art_dir / "sheets" / "icons.png")

    def p5_prop(name, index=0):
        return props.frames(name, (64, 64))[index]

    def env_frames(name, row="variants", cell=(64, 64)):
        for directory in ("normalized", "sheets"):
            path = art_dir / "env2" / directory / f"{name}.png"
            if not path.is_file() or not path.with_suffix(".manifest.json").is_file():
                continue
            try:
                source = production(path, prop=name if cell == (64, 64) else None)
                return source.frames(row, cell), source.playback(row)
            except (OSError, ValueError, KeyError, TypeError):
                continue
        return None

    def preferred(name, fallback, index=0, row="variants", animated=False):
        final = env_frames(name, row)
        if final is not None and (animated or index < len(final[0])):
            frames, playback = final
            return (frames, *playback) if animated else ([frames[index]], 1, False)
        fallbacks.append(name)
        return fallback

    def point(point_id, x, y, action, facing="any"):
        return dict(id=point_id, x=x, y=y, action=action, facing=facing)

    def furniture(asset_id, kind, footprint, frames, fps=1, loop=False,
                  points=(), layer=1, anchor=None, sheet_id=None):
        sheet_id = sheet_id or asset_id
        if not any(sheet["id"] == sheet_id for sheet in manifest["sheets"]):
            add_sheet(sheet_id, atlas(frames), frames[0].size,
                      anchor or furniture_anchor(frames, footprint),
                      [clip(asset_id, 0, len(frames), fps, loop)])
        animation = next(s for s in manifest["sheets"] if s["id"] == sheet_id)["rows"][0]["name"]
        width, height = footprint
        blocked = ([{"x": x, "y": y} for y in range(height) for x in range(width)]
                   if layer == 1 else [])
        variant = dict(rotation=0, state="default", footprintW=width, footprintH=height,
                       blockedCells=blocked, points=list(points), sheetId=sheet_id,
                       animation=animation, layer=layer, zOrder=0)
        manifest["furniture"].append(dict(id=asset_id, kind=kind, variants=[variant]))

    desk_parts = [env_frames(name) for name in ("desks", "monitors", "office-chairs")]
    desk_cell = 64
    desk_padding = 14 if all(desk_parts) else 0
    desk_anchor = {"x": 8, "y": 8 + desk_padding}
    if all(desk_parts):
        desk = Image.new("RGBA", (desk_cell, desk_cell))
        # Back-facing seat below the tabletop, screen above it. All authored
        # components share world x=24. Raise only the monitor 14 world pixels:
        # its bottom at y=3 leaves the head-anchored outer nameplate clear.
        # Padding keeps the tabletop/chair world positions fixed, without clipping.
        for part, size, position in ((desk_parts[0][0][0], (48, 24), (8, 16 + desk_padding)),
                                     (desk_parts[1][0][0], (24, 21), (20, 4 + desk_padding - 14))):
            cropped = part.crop(part.getchannel("A").getbbox())
            desk.alpha_composite(cropped.resize(size, Image.Resampling.NEAREST), position)
        # A narrow, static continuation of the authored stand closes the five
        # source-pixel gap to the unchanged tabletop; use its actual base colour.
        ImageDraw.Draw(desk).rectangle((31, 25, 32, 29), fill=desk.getpixel((32, 24)))
    else:
        fallbacks.append("desk-composite")
        desk = p5_prop("desk", 1)
    # Authored screen-interior bounds, shifted with the monitor independently of
    # the static desk, not inferred from animated runtime pixels.
    screen_bounds = (23, 9 + desk_padding - 14, 40, 16 + desk_padding - 14) if all(desk_parts) else (23, 27, 39, 33)
    off = desk_screen(desk, screen_bounds, "off")
    desk_frames = [off, *(desk_screen(desk, screen_bounds, "working", frame) for frame in range(3)),
                   desk_screen(desk, screen_bounds, "screensaver")]
    desk_rows = [clip("cozy-desk", 0), clip("cozy-desk-off", 0),
                 clip("cozy-desk-working", 0, 3, 3, True, column=1),
                 clip("cozy-desk-screensaver", 0, column=4)]
    desk_image = atlas(desk_frames)
    desk_image, _ = normalize_sheet(desk_image, {
        "characterId": "desk", "animation": {"rows": {r["name"]: r for r in desk_rows}},
        "frame_layout": {"sheetWidth": desk_image.width, "sheetHeight": desk_image.height,
                         "rows": {r["name"]: [{"x": (r["column"] + f) * desk_cell, "y": 0, "w": desk_cell, "h": desk_cell}
                                                for f in range(r["frames"])] for r in desk_rows}}})
    add_sheet("cozy-desk", desk_image, (desk_cell, desk_cell), desk_anchor, desk_rows)
    furniture("cozy-desk", "desk", (3, 2), [off],
              points=[point("seat", 1, 2, "typing", "up")], anchor=desk_anchor)
    variants = manifest["furniture"][-1]["variants"]
    if all(desk_parts):
        variants[0].update(chairBackId="cozy-chair-back", chairFrontId="cozy-chair-front",
                           chairOffset={"x": 0, "y": 0}, chairSeatAnchor={"x": 24, "y": 41})
    variants.extend(dict(variants[0], state=state, animation=f"cozy-desk-{state}")
                    for state in ("off", "working", "screensaver"))
    if all(desk_parts):
        # All authored chair colours remain selectable, with the same contact.
        chairs = []
        for part in desk_parts[2][0]:
            chair = Image.new("RGBA", (64, 64))
            chair.alpha_composite(part.crop(part.getchannel("A").getbbox()).resize((18, 24), Image.Resampling.NEAREST), (23, 34))
            chairs.append(chair)
        for part_name, z_order in (("back", 0), ("front", 18)):
            asset_id = f"cozy-chair-{part_name}"
            frames, rows, variants = [], [], []
            for index, chair in enumerate(chairs):
                layer = chair.copy()
                # Viewed from behind: the visible backrest/arms hide the torso.
                # Only the base below the pelvis belongs behind the character.
                layer.paste((0, 0, 0, 0), (0, 0, 64, 50) if part_name == "back" else (0, 50, 64, 64))
                state = "default" if index == 0 else f"color-{index + 1}"
                animation = asset_id if index == 0 else f"{asset_id}-{state}"
                rows.append(clip(animation, 0, column=index))
                frames.append(layer)
                variants.append(dict(rotation=0, state=state, footprintW=3, footprintH=2,
                    blockedCells=[], points=[], sheetId=asset_id, animation=animation, layer=1, zOrder=z_order))
            add_sheet(asset_id, atlas(frames), (64, 64), {"x": 8, "y": 8}, rows)
            manifest["furniture"].append(dict(id=asset_id, kind="chair", variants=variants))
    furniture("cozy-pantry", "pantry", (3, 2), [p5_prop("coffee_machine")],
              points=[point("coffee", 1, 2, "coffee", "up")])
    furniture("cozy-water", "water", (1, 2), [p5_prop("water_cooler")],
              points=[point("water", 0, 2, "water", "up")])
    tv_fps, tv_loop = props.playback("tv")
    furniture("cozy-tv", "tv", (3, 2), props.frames("tv", (64, 64)), tv_fps, tv_loop,
              points=[point("tv", 1, 3, "tv", "up")])
    sofa = preferred("sofa-red", ([p5_prop("sofa")], 1, False))
    furniture("cozy-sofa", "sofa", (3, 2), *sofa,
              points=[point("nap", 1, 2, "napping", "down")])
    # Pelvis contacts on the authored cushion, rather than the approach tile's feet.
    # The anchor is for the first (leftmost) overflow work point on this host.
    manifest["furniture"][-1]["variants"][0]["chairSeatAnchor"] = {"x": 16, "y": 18}
    table = preferred("round-table", ([p5_prop("desk", 0)], 1, False))
    furniture("cozy-table", "table", (3, 2), *table,
              points=[point("chat-left", 0, 2, "chatting", "right"),
                      point("chat-right", 2, 2, "chatting", "left")])
    plant = preferred("plants", ([p5_prop("plants")], 1, False))
    furniture("cozy-plant", "plant", (1, 1), *plant)
    furniture("cozy-plant-monstera", "plant", (1, 1), *plant, sheet_id="cozy-plant")
    for index, name in enumerate(("snake", "fern", "succulent", "ficus"), 1):
        p5_index = {"fern": 1, "succulent": 2}.get(name, 0)
        variant = preferred("plants", ([p5_prop("plants", p5_index)], 1, False), index)
        furniture(f"cozy-plant-{name}", "plant", (1, 1), *variant)

    rug = tiles.frames("floor_office", (32, 32))[0].resize((64, 48), Image.Resampling.NEAREST)
    rug_cell = Image.new("RGBA", (64, 64))
    rug_cell.paste(rug, (0, 16))
    aliases = (
        ("bookshelf", "bookshelf", (3, 2), "bookshelf", ([p5_prop("coffee_machine")], 1, False), 1),
        ("cabinet", "cabinet", (2, 2), "filing-cabinet", ([p5_prop("coffee_machine")], 1, False), 1),
        ("server", "server", (2, 3), "server-rack", ([p5_prop("water_cooler")], 1, False), 1),
        ("meeting", "table", (4, 3), "meeting-table-chairs", ([p5_prop("desk", 0)], 1, False), 1),
        ("armchair", "chair", (2, 2), "armchair", ([p5_prop("chair")], 1, False), 1),
        ("rug", "rug", (4, 3), "rugs", ([rug_cell], 1, False), 0),
        ("picture", "picture", (2, 1), "framed-pictures", ([p5_prop("window")], 1, False), 2),
        ("whiteboard", "whiteboard", (3, 1), "whiteboard", ([p5_prop("window")], 1, False), 2),
        ("clock", "clock", (1, 1), "wall-clock", ([p5_prop("clock")], 1, False), 2),
        ("window", "window", (3, 1), "window", ([p5_prop("window")], 1, False), 2),
        ("door", "door", (2, 1), "door", ([p5_prop("door", 1)], 1, False), 2),
    )
    for asset_id, kind, footprint, source_name, fallback, layer in aliases:
        animated = asset_id == "server"
        if asset_id in ("door", "window"):
            wall_part = env_frames("wall-navy", "door_frame" if asset_id == "door" else "window_wall", (32, 32))
            frames = ([wall_part[0][0]], 1, False) if wall_part else fallback
        else:
            frames = preferred(source_name, fallback, row="blink" if animated else "variants",
                               animated=animated)
        furniture(f"cozy-{asset_id}", kind, footprint, *frames, layer=layer)
        if asset_id == "armchair":
            manifest["furniture"][-1]["variants"][0]["chairSeatAnchor"] = {"x": 16, "y": 19}

    for asset_id, kind, footprint, source_name in (("lamp", "lamp", (1, 1), "lamp"),
            ("fridge", "cabinet", (2, 3), "fridge"), ("trash", "plant", (1, 1), "trash-bin"),
            ("coffee-table", "table", (3, 2), "coffee-table")):
        final = env_frames(source_name)
        if final:
            furniture(f"cozy-{asset_id}", kind, footprint, [final[0][0]])

    icon_frames = [icons.frames("bubbles", (32, 32))[i] for i in range(6)]
    icon_frames.extend(icons.frames("role_badges", (32, 32))[:2])
    icon_frames.append(icons.frames("report_item", (32, 32))[0])
    icon_names = [f"bubble-{name}" for name in ("working", "thinking", "waiting", "done", "napping", "reaction")]
    icon_names.extend(("role-supervisor", "role-worker", "report-item"))
    add_sheet("icons", atlas(icon_frames), (32, 32), {"x": 0, "y": 0},
              [clip(name, 0, column=column) for column, name in enumerate(icon_names)])

    providers = neutral_provider_icons()
    add_sheet("provider-icons", atlas(list(providers.values())), (12, 12), {"x": 0, "y": 0},
              [clip(name, 0, column=column) for column, name in enumerate(providers)])
    laptop = laptop_sprite()
    add_sheet("office-extras", laptop, (16, 16), {"x": 0, "y": 0}, [clip("laptop", 0)])
    furniture("cozy-laptop", "laptop", (1, 1), [laptop], sheet_id="office-extras")
    manifest["furniture"][-1]["variants"][0]["blockedCells"] = []
    furniture("cozy-counter-stool", "chair", (1, 1), [counter_stool_sprite()])
    manifest["furniture"][-1]["variants"][0]["chairSeatAnchor"] = {"x": 8, "y": -5}
    manifest["furniture"][-1]["variants"][0]["blockedCells"] = []
    rotate_furniture(manifest, images)

    # Authored profile art is added AFTER the legacy square-cell rotation pass.
    # A padded canvas bakes the +/-26px chair offset without clipping its base.
    env3 = art_dir / "env3" / "sheets"
    if env3.is_dir():
        side_anchor = {"x": 40, "y": 29}
        side_geometry = (("right", 90, -26, 31, -1), ("left", 270, 26, 32, 3))

        def side_source(name, screen=False):
            path = art_dir / "env2" / "normalized" / "env3" / f"{name}.png"
            if not path.is_file() or not path.with_suffix(".manifest.json").is_file():
                path = env3 / f"{name}.png"
            return production(path, prop="monitor" if screen else name)

        for part, z_order in (("back", -2), ("front", 4)):
            asset_id = f"cozy-chair-side-{part}"
            frames, rows, variants = [], [], []
            for direction, rotation, offset, _, _ in side_geometry:
                source = side_source(f"office-chair-side-{direction}-{part}")
                frame = Image.new("RGBA", (128, 64))
                frame.paste(source.frames("variants", (64, 64))[0], (32 + offset, 0))
                name = f"{asset_id}-{direction}"
                rows.append(clip(name, 0, column=len(frames)))
                frames.append(frame)
                variants.append(dict(rotation=rotation, state="default", footprintW=3, footprintH=2,
                                     blockedCells=[], points=[], sheetId=asset_id, animation=name,
                                     layer=1, zOrder=z_order))
            add_sheet(asset_id, atlas(frames), (128, 64), side_anchor, rows)
            manifest["furniture"].append(dict(id=asset_id, kind="chair", variants=variants))

        for source_name in ("desk-side-wood", "desk-side-white", "desk-side-double-wood",
                            "desk-side-double-white", "laptop-table-side"):
            asset_id = "cozy-" + source_name
            is_desk = source_name.startswith("desk-")
            rows, groups, variants = [], [], []
            for direction, rotation, offset, seat_x, tile_x in side_geometry:
                source = side_source(f"{source_name}-{direction}", screen=is_desk)
                states = ("off", "on", "typing") if is_desk else ("variants",)
                animations = {}
                for state in states:
                    originals = source.frames(state, (64, 64))
                    fps, loop = source.playback(state)
                    frames = []
                    for original in originals:
                        frame = Image.new("RGBA", (128, 64))
                        frame.paste(original, (32, 0))
                        frames.append(frame)
                    animation = f"{asset_id}-{direction}-{state}"
                    rows.append(clip(animation, len(groups), len(frames), fps, loop))
                    groups.append(frames)
                    animations[state] = animation
                for state, source_state in (("default", "on"), ("off", "off"),
                                            ("working", "typing"), ("screensaver", "on")) if is_desk else (("default", "variants"),):
                    variants.append(dict(rotation=rotation, state=state, footprintW=3, footprintH=2,
                        blockedCells=[{"x": x, "y": y} for y in range(2) for x in range(3)],
                        points=[point("seat", tile_x, 1, "typing", direction)] if is_desk else [],
                        sheetId=asset_id, animation=animations[source_state], layer=1, zOrder=2,
                        chairBackId="cozy-chair-side-back", chairFrontId="cozy-chair-side-front",
                        chairOffset={"x": 0, "y": 0},
                        chairSeatAnchor={"x": 32 + offset + seat_x - side_anchor["x"], "y": 44 - side_anchor["y"]}))
            image = Image.new("RGBA", (128 * max(map(len, groups)), 64 * len(groups)))
            for row, frames in enumerate(groups):
                image.paste(atlas(frames), (0, row * 64))
            add_sheet(asset_id, image, (128, 64), side_anchor, rows)
            manifest["furniture"].append(dict(id=asset_id, kind="desk" if is_desk else "table", variants=variants))

    palette = Counter()
    for name in ("wall_office", "floor_pantry", "floor_office"):
        palette.update(pixel[:3] for pixel in tiles.frames(name, (32, 32))[0].get_flattened_data() if pixel[3])
    def shade(bright):
        choices = [rgb for rgb in palette if (sum(rgb) / 3 >= 160 if bright else sum(rgb) / 3 < 90)]
        return max(choices or palette, key=lambda rgb: (palette[rgb], rgb))
    light, dark = shade(True), shade(False)
    checker = Image.new("RGBA", (32, 32), (*light, 255))
    draw = ImageDraw.Draw(checker)
    for y in range(0, 32, 8):
        for x in range(0, 32, 8):
            if (x // 8 + y // 8) % 2:
                draw.rectangle((x, y, x + 7, y + 7), fill=(*dark, 255))

    def tile(name, fallback, row="tile"):
        final = env_frames(name, row, (32, 32))
        if final is None:
            fallbacks.append(name)
            image = fallback
        else:
            image = final[0][0]
        opaque = Image.new("RGBA", (32, 32), (*light, 255))
        opaque.alpha_composite(image)
        return opaque

    wall = tiles.frames("wall_office", (32, 32))[0]
    # The p5 wall's first 22 rows are plaster; its dado/baseboard is only
    # appropriate for the bottom wall strip, never each upper wall tile.
    navy = tile("wall-navy", wall, "panel")
    wood = tile("wall-wood", wall, "panel")
    plaster = navy.crop((0, 0, 32, 26)).resize((32, 32), Image.Resampling.NEAREST)
    baseboard = navy.copy()
    baseboard.paste(navy.crop((0, 30, 32, 31)), (0, 31))
    tile_frames = [
        plaster,
        tile("floor-wood-oak", tiles.frames("floor_pantry", (32, 32))[0]),
        tile("floor-checker-pantry", checker),
        tile("floor-carpet-blue", tiles.frames("floor_office", (32, 32))[0]),
        Image.new("RGBA", (32, 32), (*dark, 255)),
        baseboard,
    ]
    tile_names = ("wall", "wood", "checker", "carpet", "void", "wall-baseboard")
    add_sheet("tiles", atlas(tile_frames), (32, 32), {"x": 0, "y": 0},
              [clip(name, 0, column=column) for column, name in enumerate(tile_names)])
    for tile_id, name, frame in zip((0, 1, 2, 3, 255, 6), tile_names, tile_frames):
        if tile_id in (1, 2, 3):
            continue
        color = Counter(frame.get_flattened_data()).most_common(1)[0][0]
        manifest["tileTypes"].append(dict(id=tile_id, kind="wall" if tile_id in (0, 6) else "void" if tile_id == 255 else "floor",
                                         color="#{:02X}{:02X}{:02X}".format(*color[:3]),
                                         sheetId="tiles", animation=name))
    # Render 16px world tiles at native source scale. In row-major order the
    # four quadrants reconstruct the original 32px floor without resampling.
    floor_frames, floor_clips = [], []
    for source_index, (name, tile_ids) in enumerate((
            ("wood", (1, 10, 11, 12)),
            ("checker", (2, 13, 14, 15)),
            ("carpet", (3, 16, 17, 18))), 1):
        for quarter, tile_id in enumerate(tile_ids):
            x, y = (quarter % 2) * 16, (quarter // 2) * 16
            frame = tile_frames[source_index].crop((x, y, x + 16, y + 16))
            animation = f"{name}-{quarter}"
            floor_clips.append(clip(animation, 0, column=len(floor_frames)))
            floor_frames.append(frame)
            color = Counter(frame.get_flattened_data()).most_common(1)[0][0]
            manifest["tileTypes"].append(dict(id=tile_id, kind="floor",
                                             color="#{:02X}{:02X}{:02X}".format(*color[:3]),
                                             sheetId="floor-tiles", animation=animation))
    add_sheet("floor-tiles", atlas(floor_frames), (16, 16), {"x": 0, "y": 0}, floor_clips)
    add_sheet("room-walls", atlas([plaster, wood, wood]), (32, 32), {"x": 0, "y": 0},
              [clip("partition", 0), clip("wood-wall", 0, column=1), clip("wood-baseboard", 0, column=2)])
    for tile_id, animation in ((5, "partition"), (7, "wood-wall"), (8, "wood-baseboard")):
        manifest["tileTypes"].append(dict(id=tile_id, kind="wall", color="#252b48",
                                         sheetId="room-walls", animation=animation))
    manifest["tileTypes"].sort(key=lambda entry: entry["id"])

    if len(manifest["sheets"]) > 128:
        raise ValueError("runtime pack exceeds the 128-sheet limit")
    for sheet in manifest["sheets"]:
        image = images[sheet["id"]]
        if image.width > 8192 or image.height > 8192 or image.width * image.height > 16_777_216:
            raise ValueError(f"{sheet['id']}: runtime PNG is too large")
        stream = BytesIO()
        image.save(stream, format="PNG", compress_level=9)
        png = stream.getvalue()
        sheet["file"] = f"sheets/pack-{hashlib.sha256(png).hexdigest()}.png"
        pngs[sheet["file"]] = png
    # Content-addressed sheets leave the previously published manifest usable
    # until the final replacement succeeds. Never clean before publishing it.
    data = (json.dumps(manifest, ensure_ascii=False, indent=2) + "\n").encode("utf-8")
    for filename, png in sorted(pngs.items()):
        atomic_write(output / filename, png)
    atomic_write(output / "assets.json", data)
    stale = 0
    for path in sorted((output / "sheets").glob("pack-*.png")):
        if OWNED_PNG.fullmatch(path.name) and f"sheets/{path.name}" not in pngs:
            path.unlink()
            stale += 1
    return dict(manifest=str(output / "assets.json"), characters=len(manifest["characters"]),
                seatedCharacters=len(seated_characters),
                blinkCharacters=sum(any(row["name"].startswith("blink-") for row in sheet["rows"])
                                    for sheet in manifest["sheets"]),
                sheets=len(manifest["sheets"]), pngs=len(pngs), furniture=len(manifest["furniture"]),
                tileTypes=len(manifest["tileTypes"]), skipped=skipped,
                p5Fallbacks=sorted(set(fallbacks)), removedStale=stale)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--art-dir", type=Path, default=ROOT / "assets" / "art")
    parser.add_argument("--output", type=Path, default=ROOT / "assets" / "pack")
    args = parser.parse_args()
    try:
        summary = build_pack(args.art_dir, args.output)
    except (OSError, ValueError, KeyError, TypeError) as error:
        print(f"build_pack: {error}", file=sys.stderr)
        return 1
    print(json.dumps(summary, ensure_ascii=False, indent=2))
    return 0


if __name__ == "__main__":
    sys.exit(main())
