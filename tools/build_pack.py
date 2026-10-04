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
import time

from PIL import Image, ImageDraw

from anim_normalize import normalize_frames, normalize_sheet
from publication import writer_lock
from safe_files import safe_path, relative_file, read_bytes, read_json, open_png, write_boundary, bounded_glob, make_directory
from safe_files import atomic_write as write_file


ROOT = Path(__file__).resolve().parents[1]
GENERATED_BY = "tools/build_pack.py"
OWNED_PNG = re.compile(r"pack-[0-9a-f]{64}\.png\Z")
PNG_GRACE_SECONDS = 3600
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
        manifest_path = safe_path(manifest_path)
        self.data = read_json(manifest_path)
        self.directory = manifest_path.parent
        if "publication" in self.data:
            image_path = relative_file(self.directory, self.data["sprite_sheet_alpha"])
        with open_png(image_path) as source:
            if source.format != "PNG":
                raise ValueError("final sheet is not a PNG")
            self.image = source.convert("RGBA")
        self.layout = self.data["frame_layout"]
        self.rows = self.layout["rows"]
        self.animation = self.data["animation"]["rows"]
        if len(self.rows) > 128 or any(len(row) > 128 for row in self.rows.values()):
            raise ValueError("sheet row/frame count exceeds budget")
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
        result = new_image((32, 64))
        result.paste(frame.resize(size, Image.Resampling.NEAREST), seated_position)
        return result

    def normalized(frame):
        bottom = frame.getchannel("A").getbbox()[3]
        result = new_image((32, 64))
        resized = frame.resize(size, Image.Resampling.NEAREST)
        result.paste(resized, (16 - size[0] // 2, 60 - round(bottom * size[1] / 64)))
        return result

    # Optional locked blink pixels are transferred to the existing pose using
    # its published eye boxes/offsets, never substituting a walking body.
    blink_rows = []
    lock = source.data.get("facelock", {})
    report_path = relative_file(source.directory, lock.get("report", "missing"))
    try:
        report = read_json(report_path)
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
                with open_png(relative_file(source.directory, entry["file"])) as image:
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

    image = new_image((128, 13 * 64))
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
        extended = new_image((128, image.height + 64))
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
        with open_png(relative_file(source.directory, blink["file"])) as opened:
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
                extended = new_image((128, image.height + 64))
                extended.paste(image, (0, 0))
                image = extended
                for column, frame in enumerate(group):
                    image.paste(seated_normalized(frame), (column * 32, row * 64))
                clips.append(dict(clip(runtime_name, row, 4, fps, loop, direction), seatAnchor=transformed))
    return image, clips


def clip(name, row, frames=1, fps=1, loop=False, direction="any", column=0):
    return dict(name=name, direction=direction, row=row, column=column,
                frames=frames, fps=fps, loop=loop)


def new_image(size, color=0):
    width, height = size
    if max(size) > 8192 or min(size) <= 0 or width * height > 16_777_216:
        raise ValueError("generated PNG dimension/pixel budget exceeded")
    return Image.new("RGBA", size, color)

def atlas(frames: list[Image.Image]) -> Image.Image:
    width, height = frames[0].size
    if len(frames) > 128 or width * len(frames) > 8192 or height > 8192 or width * height * len(frames) > 16_777_216:
        raise ValueError("atlas dimensions/frame count exceed budget")
    image = new_image((width * len(frames), height))
    for column, frame in enumerate(frames):
        if frame.size != (width, height):
            raise ValueError("atlas cell sizes differ")
        image.paste(frame, (column * width, 0))
    return image


def provider_icons(art_dir: Path, size: int) -> dict[str, Image.Image]:
    """Copy independently authored native cells, including published role art."""
    directory = art_dir / "provider-icons"
    if not (directory / "manifest.json").is_file():
        return {}
    data = read_json(directory / "manifest.json")
    overlays = {icon["id"] for icon in data["icons"] if icon["type"] == "overlay"}
    result = {}
    for asset in data["assets"]:
        if asset["size"] != size:
            continue
        name = asset["id"]
        path = relative_file(directory, asset["files"]["1"])
        if name in overlays and not path.is_file():
            continue
        if name in result or asset["anchor"] != {"x": 0, "y": 0}:
            raise ValueError(f"{name}: invalid provider icon metadata")
        with open_png(path) as image:
            if image.format != "PNG" or image.size != (size, size):
                raise ValueError(f"{name}: provider icon must be a native {size}x{size} PNG")
            result[name] = image.convert("RGBA")
        if result[name].getchannel("A").getbbox() is None:
            raise ValueError(f"{name}: empty provider icon")
    if not {"codex", "claude", "antigravity"} <= result.keys():
        raise ValueError(f"missing native {size}px provider icons")
    return result


def laptop_sprite() -> Image.Image:
    """A tiny original open laptop for a single walkable world tile."""
    image = new_image((16, 16))
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
    image = new_image((32, 32))
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
                rotated = new_image((original.width, original.height * 4))
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
    path = safe_path(path)
    if path.is_file() and read_bytes(path) == data:
        return
    write_file(path, data)


def build_pack(art_dir: Path, output: Path) -> dict:
    art_dir, output = safe_path(art_dir), safe_path(output)
    safe_path(output / "sheets")
    if output == art_dir or art_dir in output.parents:
        raise ValueError("output must be outside the production art directory")
    manifest = {"version": 1, "generatedBy": GENERATED_BY,
                "sheets": [], "characters": [], "furniture": [], "tileTypes": []}
    pngs = {}
    images = {}
    skipped = []
    fallbacks = []
    cache = {}
    source_pixels = 0
    output_pixels = 0

    def production(path, metadata=None, prop=None):
        nonlocal source_pixels
        key = (path, metadata, prop)
        if key not in cache:
            if len(cache) >= 256:
                raise ValueError("source sheet count exceeds budget")
            candidate = ProductionSheet(path, metadata or path.with_suffix(".manifest.json"))
            pixels = candidate.image.width * candidate.image.height
            if source_pixels + pixels > 32 * 1024 * 1024:
                raise ValueError("source sheet pixel budget exceeded")
            cache[key] = candidate
            source_pixels += pixels
            if prop is not None:
                cache[key].image, _ = normalize_sheet(cache[key].image, cache[key].data, prop=prop)
        return cache[key]

    def add_sheet(sheet_id, image, cell, anchor, rows):
        nonlocal output_pixels
        previous = images.get(sheet_id)
        output_pixels += image.width * image.height - (previous.width * previous.height if previous is not None else 0)
        if max(image.size) > 8192 or image.width * image.height > 16_777_216 or output_pixels > 32 * 1024 * 1024:
            raise ValueError("output sheet pixel budget exceeded")
        images[sheet_id] = image
        entry = dict(id=sheet_id, file="", cellW=cell[0], cellH=cell[1],
                     anchor=anchor, scale=1, rows=rows)
        existing = next((s for s in manifest["sheets"] if s["id"] == sheet_id), None)
        if existing is None:
            manifest["sheets"].append(entry)
        else:
            existing.update(entry)

    def add_character(character_id, path, metadata=None):
        metadata = metadata or path.with_suffix(".manifest.json")
        image, rows = seated_characters.get(character_id, (None, None))
        locked = path.parent / "locked" / f"{character_id}.png"
        if not locked.with_suffix(".manifest.json").is_file():
            locked = art_dir / "roster" / character_id / "locked" / f"{character_id}.png"
        locked_metadata = locked.with_suffix(".manifest.json")
        if image is None:
            try:
                data = read_json(locked_metadata)
                report = read_json(relative_file(locked.parent, data["facelock"]["report"]))
                if all(hashlib.sha256(read_bytes(relative_file(ROOT, name.replace("\\", "/")))).hexdigest() == digest
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
    for directory in bounded_glob(art_dir / "seated"):
        if not directory.is_dir() or directory.name.startswith("."):
            continue
        path = directory / f"{directory.name}.png"
        if not path.with_suffix(".manifest.json").is_file():
            continue
        if not path.is_file():
            try:
                if "publication" not in read_json(path.with_suffix(".manifest.json")):
                    continue  # Legacy pending directory, not a committed batch.
            except (OSError, ValueError):
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
        for directory in bounded_glob(roster_dir):
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
        desk = new_image((desk_cell, desk_cell))
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
            chair = new_image((64, 64))
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
    rug_cell = new_image((64, 64))
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

    for size, sheet_id in ((12, "provider-icons"), (10, "provider-icons-10")):
        providers = provider_icons(art_dir, size)
        if not providers:
            continue
        add_sheet(sheet_id, atlas(list(providers.values())), (size, size), {"x": 0, "y": 0},
                  [clip(name, 0, column=column) for column, name in enumerate(providers)])
    laptop = laptop_sprite()
    add_sheet("office-extras", laptop, (16, 16), {"x": 0, "y": 0}, [clip("laptop", 0)])
    furniture("cozy-laptop", "laptop", (1, 1), [laptop], sheet_id="office-extras")
    manifest["furniture"][-1]["variants"][0]["blockedCells"] = []
    furniture("cozy-counter-stool", "chair", (1, 1), [counter_stool_sprite()])
    manifest["furniture"][-1]["variants"][0]["chairSeatAnchor"] = {"x": 8, "y": -5}
    manifest["furniture"][-1]["variants"][0]["blockedCells"] = []

    # Replace reused bodies in place; all states share one anchor and geometry.
    # Published masks preserve the deliberately tiny motion without guessing it.
    ambient = (
        ("window-views", "window", "window", (3, 1), 0, [point("view", 1, 1, "window", "up")]),
        ("coffee-machine", "pantry", "pantry", (3, 2), 1, []),
        ("mug-steam", "mug-steam", "decoration", (1, 1), 2, []),
        *((f"plant-{name}", "plant" if name == "monstera" else f"plant-{name}",
           "plant", (1, 1), 1, []) for name in ("monstera", "snake", "fern", "succulent", "ficus")),
        ("wall-clock", "clock", "clock", (1, 1), 2, []),
        ("ceiling-light", "ceiling-light", "lamp", (1, 1), 2, []),
        ("vending-machine", "vending-machine", "cabinet", (2, 3), 1,
         [point("snack", 1, 3, "vending", "up")]),
        ("fridge", "fridge", "cabinet", (2, 3), 1, [point("snack", 1, 3, "fridge", "up")]),
        ("water-cooler", "water", "water", (1, 2), 1, []),
        ("dartboard", "dartboard", "dartboard", (2, 1), 2,
         [point("darts", 1, 2, "darts", "up")]),
        ("ping-pong", "ping-pong", "ping-pong", (3, 2), 1,
         [point("near", 1, 2, "pingpong", "up"), point("far", 1, -1, "pingpong", "down")]),
        ("bean-bag", "bean-bag", "chair", (2, 1), 1,
         [point("rest", 1, 1, "beanbag", "up")]),
        ("arcade", "arcade", "arcade", (2, 3), 1,
         [point("play", 1, 3, "arcade", "up")]),
        ("reading-nook", "reading-nook", "reading-nook", (3, 2), 1,
         [point("read", 1, 2, "reading", "up")]),
        ("standing-desk", "standing-desk", "standing-desk", (3, 2), 1,
         [point("work", 1, 2, "standing-work", "up")]),
        ("fish-tank", "fish-tank", "fish-tank", (3, 2), 1,
         [point("watch", 1, 2, "aquarium", "up")]),
        ("lava-lamp", "lava-lamp", "lamp", (1, 1), 1, []),
        ("desk-fan", "desk-fan", "decoration", (1, 1), 1, []),
        ("string-lights", "string-lights", "lamp", (3, 1), 2, []),
    )
    shared_ambient = set()
    for name, suffix, kind, footprint, layer, points in ambient:
        path = art_dir / "env4" / "sheets" / f"{name}.png"
        if not path.is_file() or not path.with_suffix(".manifest.json").is_file():
            continue
        source = production(path)
        with open_png(art_dir / "env4" / "qa" / "masks" / f"{name}.png") as opened:
            mask = opened.convert("L")
        if mask.size != (64, 64):
            raise ValueError(f"{name}: invalid ambient animation mask")
        groups, rows = [], []
        for state in source.rows:
            frames = source.frames(state, (64, 64))
            if len(frames) != source.animation[state]["frames"]:
                raise ValueError(f"{name}/{state}: frame count disagrees with animation")
            frames, _, _ = normalize_frames(frames, name, animated_mask=mask, max_shift=0,
                                           mode="static-body-with-animated-mask")
            fps, loop = source.playback(state)
            rows.append(clip(state, len(groups), len(frames), fps, loop))
            groups.append(frames)
        if name == "string-lights":
            original = groups[0][0]
            dark = min((p for p in original.get_flattened_data() if p[3] == 255),
                       key=lambda p: sum(p[:3]))
            unlit = new_image((64, 64), dark)
            unlit.putalpha(original.getchannel("A"))
            groups.append([Image.composite(unlit, original, mask)])
            rows.append(clip("off", len(groups) - 1))
        if name == "vending-machine":
            rows.extend((dict(rows[0], name="closed", frames=1, fps=1, loop=False),
                         dict(rows[0], name="open")))
        image = new_image((64 * max(map(len, groups)), 64 * len(groups)))
        for row, frames in enumerate(groups):
            image.paste(atlas(frames), (0, row * 64))
        asset_id = "cozy-" + suffix
        asset = next((f for f in manifest["furniture"] if f["id"] == asset_id), None)
        approach = points or (asset["variants"][0]["points"] if asset else [])
        anchor = furniture_anchor(sum(groups, []), footprint)
        actor_feet = {"window-views": 70, "coffee-machine": 72, "water-cooler": 74,
                      "vending-machine": 72, "fridge": 72, "dartboard": 82,
                      "ping-pong": 72, "bean-bag": 70, "arcade": 72,
                      "reading-nook": 70, "standing-desk": 72, "fish-tank": 74}
        if approach and name in actor_feet:
            p = approach[0]
            anchor = {"x": 32 - (p["x"] * 16 + 8), "y": actor_feet[name] - (p["y"] + 1) * 16}
        elif name == "string-lights":
            anchor = {"x": 8, "y": 38}
        add_sheet(asset_id, image, (64, 64), anchor, rows)
        if asset_id not in ("cozy-window", "cozy-clock", "cozy-string-lights"):
            shared_ambient.add(asset_id)
        if asset is None:
            furniture(asset_id, kind, footprint, groups[0], points=points, layer=layer)
            asset = manifest["furniture"][-1]
        base = dict(asset["variants"][0], layer=layer)
        if points:
            base = dict(base, points=points)
        states = [("default", rows[0]["name"])]
        states.extend((row["name"], row["name"]) for row in rows if row["name"] != "variants")
        asset["variants"] = [dict(base, state=state, animation=animation) for state, animation in states]
        if name == "plant-monstera":
            alias = next(f for f in manifest["furniture"] if f["id"] == "cozy-plant-monstera")
            alias["variants"] = [dict(v) for v in asset["variants"]]
        if name in ("bean-bag", "reading-nook"):
            contact_y = 50 if name == "bean-bag" else 55
            anchor = next(s for s in manifest["sheets"] if s["id"] == asset_id)["anchor"]
            for variant in asset["variants"]:
                variant["chairSeatAnchor"] = {"x": 32 - anchor["x"], "y": contact_y - anchor["y"]}

    bubble_dir = art_dir / "env4" / "bubbles"
    glyphs = {}
    for metadata in bounded_glob(bubble_dir, "*.manifest.json"):
        name = metadata.name.removesuffix(".manifest.json")
        if name == "icons" or not (bubble_dir / f"{name}.png").is_file():
            continue
        source = production(bubble_dir / f"{name}.png")
        glyphs[name] = source.frames("variants", (12, 12))[0]
    if glyphs:
        add_sheet("bubble-icons", atlas(list(glyphs.values())), (12, 12), {"x": 0, "y": 0},
                  [clip(name, 0, column=column) for column, name in enumerate(glyphs)])

    rotate_furniture(manifest, images)

    # Ambient furniture resolves its exact clip through its variant. Share the
    # native cells to leave room for every published human and optional pet.
    if shared_ambient:
        sheets = [s for s in manifest["sheets"] if s["id"] in shared_ambient]
        image = new_image((max(images[s["id"]].width for s in sheets),
                                    sum(images[s["id"]].height for s in sheets)))
        rows, animations, y = [], {}, 0
        for sheet in sheets:
            image.paste(images[sheet["id"]], (0, y))
            for row in sheet["rows"]:
                name = sheet["id"] + "-" + row["name"]
                animations[sheet["id"], row["name"]] = name
                offset = row.get("footOffset", {"x": 0, "y": 0})
                rows.append(dict(row, name=name, row=row["row"] + y // 64,
                                 footOffset={axis: offset[axis] - sheet["anchor"][axis] for axis in ("x", "y")}))
            y += images[sheet["id"]].height
        manifest["sheets"] = [s for s in manifest["sheets"] if s["id"] not in shared_ambient]
        for sheet_id in shared_ambient:
            del images[sheet_id]
        add_sheet("env4-props", image, (64, 64), {"x": 0, "y": 0}, rows)
        for asset in manifest["furniture"]:
            for variant in asset["variants"]:
                if variant["sheetId"] in shared_ambient:
                    variant.update(animation=animations[variant["sheetId"], variant["animation"]], sheetId="env4-props")

    # Pets use the normalizer's existing full-body exception at fixed placement.
    # Their facing selectors are single-frame clips, never a four-facing loop.
    pet_paths = []
    for directory in bounded_glob(art_dir / "pets"):
        if not directory.is_dir() or directory.name.startswith("."):
            continue
        pet_paths.append((directory.name, directory / f"{directory.name}.png"))
        pet_paths.extend((f"{directory.name}-{p.stem}", p)
                         for p in bounded_glob(directory / "variants", "*.png"))
    for pet_id, path in pet_paths:
        if not path.is_file() or not path.with_suffix(".manifest.json").is_file():
            continue
        source = production(path)
        cell = (source.layout["cellWidth"], source.layout["cellHeight"])
        source.image, _ = normalize_sheet(source.image, source.data, prop="pet", max_shift=0)
        groups, rows = [], []
        for state in source.rows:
            frames = source.frames(state, cell)
            fps, loop = source.playback(state)
            row = len(groups)
            if state in ("idle", "blink"):
                if len(frames) != 4:
                    raise ValueError(f"{pet_id}/{state}: expected four facing choices")
                for column, direction in enumerate(("down", "left", "right", "up")):
                    rows.append(clip("idle" if state == "idle" else "blink-idle", row,
                                     direction=direction, column=column))
            elif state.startswith(("walk_", "roll_")):
                action, direction = state.split("_", 1)
                rows.append(clip(action, row, len(frames), fps, loop, direction))
            else:
                direction = source.data.get("pet", {}).get("actionFacings", {}).get(state, "any")
                entry = clip(state, row, len(frames), fps, loop, direction)
                if state == "sleep" and "pet" in source.data:
                    entry["seatAnchor"] = dict(source.data["pet"]["sleepContact"])
                rows.append(entry)
            groups.append(frames)
        image = new_image((cell[0] * max(map(len, groups)), cell[1] * len(groups)))
        for row, frames in enumerate(groups):
            image.paste(atlas(frames), (0, row * cell[1]))
        add_sheet("pet-" + pet_id, image, cell, source.data["anchor"], rows)
        if pet_id == "props" and "dog_bed" in source.rows:
            bed_frames = source.frames("dog_bed", cell)
            bed_anchor = furniture_anchor(bed_frames, (2, 1))
            bed_row = next(row for row in rows if row["name"] == "dog_bed")
            bed_row["footOffset"] = {axis: source.data["anchor"][axis] - bed_anchor[axis] for axis in ("x", "y")}
            furniture("cozy-pet-bed", "bed", (2, 1), bed_frames,
                      points=[point("nap", 1, 1, "pet-napping", "up")], sheet_id="pet-props")
            manifest["furniture"][-1]["variants"][0]["animation"] = "dog_bed"
            target = source.data["interactions"]["points"]["dog_bed"][0]["target"]
            manifest["furniture"][-1]["variants"][0]["chairSeatAnchor"] = {
                axis: target[axis] - bed_anchor[axis] for axis in ("x", "y")}

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
                frame = new_image((128, 64))
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
                        frame = new_image((128, 64))
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
            image = new_image((128 * max(map(len, groups)), 64 * len(groups)))
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
    checker = new_image((32, 32), (*light, 255))
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
        opaque = new_image((32, 32), (*light, 255))
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
        new_image((32, 32), (*dark, 255)),
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

    if len(manifest["sheets"]) > 127:
        raise ValueError("runtime pack exceeds 127 sheets plus the runtime fallback")
    compressed_bytes = 0
    for sheet in manifest["sheets"]:
        image = images[sheet["id"]]
        if image.width > 8192 or image.height > 8192 or image.width * image.height > 16_777_216:
            raise ValueError(f"{sheet['id']}: runtime PNG is too large")
        stream = BytesIO()
        image.save(stream, format="PNG", compress_level=9)
        png = stream.getvalue()
        compressed_bytes += len(png)
        if len(png) > 32 * 1024 * 1024 or compressed_bytes > 64 * 1024 * 1024:
            raise ValueError("runtime PNG compressed byte budget exceeded")
        sheet["file"] = f"sheets/pack-{hashlib.sha256(png).hexdigest()}.png"
        pngs[sheet["file"]] = png
    # Content-addressed sheets leave the previously published manifest usable
    # until the final replacement succeeds. Never clean before publishing it.
    data = (json.dumps(manifest, ensure_ascii=False, indent=2) + "\n").encode("utf-8")
    stale = 0
    make_directory(output)
    with write_boundary(output / ".boundary"), tempfile.TemporaryDirectory(dir=output, prefix=".build-pack-") as temporary:
        stage = Path(temporary)
        for filename, png in sorted(pngs.items()):
            atomic_write(stage / filename, png)
        atomic_write(stage / "assets.json", data)
        with writer_lock(output):
            for filename in sorted(pngs):
                destination = relative_file(output, filename)
                make_directory(destination.parent)
                with write_boundary(destination):
                    if not destination.exists():
                        os.replace(stage / filename, destination)
            atomic_write(output / "assets.json", read_bytes(stage / "assets.json", 4 * 1024 * 1024))
            # Use the newly committed manifest; a grace period also protects
            # readers that still hold the previous manifest.
            referenced = {s["file"] for s in read_json(output / "assets.json")["sheets"]}
            cutoff = time.time() - PNG_GRACE_SECONDS
            for path in sorted((output / "sheets").glob("pack-*.png")):
                safe_path(path)
                if OWNED_PNG.fullmatch(path.name) and f"sheets/{path.name}" not in referenced and path.stat().st_mtime < cutoff:
                    with write_boundary(path):
                        path.unlink()
                    stale += 1
    return dict(manifest=str(output / "assets.json"), characters=len(manifest["characters"]),
                seatedCharacters=len(seated_characters),
                blinkCharacters=sum(any(row["name"].startswith("blink-") for row in sheet["rows"])
                                    for sheet in manifest["sheets"] if sheet["id"].startswith("char-")),
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
