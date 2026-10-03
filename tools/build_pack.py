"""Adapt final sprite-gen art to the version-1 runtime pack using Pillow only."""

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
    skipped = []
    fallbacks = []
    cache = {}

    def production(path, metadata=None):
        key = (path, metadata)
        if key not in cache:
            cache[key] = ProductionSheet(path, metadata or path.with_suffix(".manifest.json"))
        return cache[key]

    def add_sheet(sheet_id, image, cell, anchor, rows):
        if image.width > 8192 or image.height > 8192 or image.width * image.height > 16_777_216:
            raise ValueError(f"{sheet_id}: runtime PNG is too large")
        stream = BytesIO()
        image.save(stream, format="PNG", compress_level=9)
        data = stream.getvalue()
        filename = f"sheets/pack-{hashlib.sha256(data).hexdigest()}.png"
        pngs[filename] = data
        manifest["sheets"].append(dict(id=sheet_id, file=filename, cellW=cell[0],
                                      cellH=cell[1], anchor=anchor, scale=1, rows=rows))

    def add_character(character_id, path, metadata=None):
        metadata = metadata or path.with_suffix(".manifest.json")
        image, rows = None, None
        locked = path.parent / "locked" / f"{character_id}.png"
        if not locked.is_file():
            locked = art_dir / "roster" / character_id / "locked" / f"{character_id}.png"
        locked_metadata = locked.with_suffix(".manifest.json")
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

    props = production(art_dir / "sheets" / "props.png")
    tiles = production(art_dir / "sheets" / "tiles.png")
    icons = production(art_dir / "sheets" / "icons.png")

    def p5_prop(name, index=0):
        return props.frames(name, (64, 64))[index]

    def env_frames(name, row="variants", cell=(64, 64)):
        path = art_dir / "env2" / "sheets" / f"{name}.png"
        if not path.is_file() or not path.with_suffix(".manifest.json").is_file():
            return None
        try:
            source = production(path)
            return source.frames(row, cell), source.playback(row)
        except (OSError, ValueError, KeyError, TypeError):
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
    if all(desk_parts):
        desk = Image.new("RGBA", (64, 64))
        # Back-facing seat below the tabletop, screen above it. All authored
        # components and the agent share the same centreline (world x=24).
        for part, size, position in ((desk_parts[2][0][0], (18, 24), (23, 34)),
                                     (desk_parts[0][0][0], (48, 24), (8, 16)),
                                     (desk_parts[1][0][0], (24, 21), (20, 4))):
            cropped = part.crop(part.getchannel("A").getbbox())
            desk.alpha_composite(cropped.resize(size, Image.Resampling.NEAREST), position)
    else:
        fallbacks.append("desk-composite")
        desk = p5_prop("desk", 1)
    furniture("cozy-desk", "desk", (3, 2), [desk],
              points=[point("seat", 1, 2, "typing", "up")], anchor={"x": 8, "y": 8})
    if all(desk_parts):
        chair = Image.new("RGBA", (64, 64))
        part = desk_parts[2][0][0]
        chair.alpha_composite(part.crop(part.getchannel("A").getbbox()).resize((18, 24), Image.Resampling.NEAREST), (23, 34))
        front = Image.new("RGBA", (64, 64))
        front.paste(chair.crop((0, 49, 64, 64)), (0, 49))
        furniture("cozy-chair-front", "chair", (3, 2), [front], anchor={"x": 8, "y": 8})
        manifest["furniture"][-1]["variants"][0].update(blockedCells=[], zOrder=18)
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
