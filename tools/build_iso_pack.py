"""Build the isolated iso runtime pack without altering any source pixels.

rows contains authored storage; clips contains fully resolved playback metadata.
All paths in the published manifest refer to content-addressed files in the pack.
"""
from __future__ import annotations

import argparse
from collections import Counter, deque
from copy import deepcopy
import hashlib
from io import BytesIO
import json
import math
import os
from pathlib import Path, PureWindowsPath
import sys
import tempfile

from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[1]
ART = ROOT / "assets/art/iso"
FACINGS = ("se", "sw", "ne", "nw")
ROTATIONS = {"se": 0, "sw": 90, "nw": 180, "ne": 270}
CHARACTERS = ("dev-hoodie-v2", "punk-girl", "professor")
FURNITURE_KINDS = {"desk": "desk", "chair": "chair", "sofa": "sofa", "plant": "plant", "shelf": "bookshelf",
                   "tv-cabinet": "tv", "coffee-machine": "pantry", "fridge": "cabinet", "water-cooler": "water",
                   "rug": "rug", "side-table": "table", "floor-lamp": "lamp"}
COLLECTIONS = ("characters", "furniture", "pets", "carriedProps", "wallModules")
METADATA = ("actionBindings", "attachmentRule", "transitionRule", "materialPriority")
MAX_JSON = 4 * 1024 * 1024
MAX_PNG = 32 * 1024 * 1024


def json_bytes(data):
    result = (json.dumps(data, ensure_ascii=False, indent=2, allow_nan=False) + "\n").encode("utf-8")
    if len(result) > MAX_JSON:
        raise ValueError("JSON exceeds the 4 MiB limit")
    return result


def local_file(directory, name):
    if not isinstance(name, str) or not name or ":" in name or PureWindowsPath(name).drive or Path(name).is_absolute():
        raise ValueError(f"invalid local PNG path: {name!r}")
    path = (directory / name).resolve()
    if not path.is_relative_to(directory.resolve()):
        raise ValueError(f"path escapes source directory: {name}")
    return path


def png_info(data):
    if len(data) > MAX_PNG:
        raise ValueError("PNG exceeds the 32 MiB limit")
    if len(data) < 33 or data[:8] != b"\x89PNG\r\n\x1a\n" or data[12:16] != b"IHDR" or data[24:29] != bytes((8, 6, 0, 0, 0)):
        raise ValueError("expected an 8-bit non-interlaced RGBA PNG")
    with Image.open(BytesIO(data)) as image:
        w, h = image.size
        if image.format != "PNG" or image.mode != "RGBA":
            raise ValueError("expected an RGBA PNG")
        if max(w, h) > 8192 or w * h > 16_777_216:
            raise ValueError("PNG dimensions exceed asset limits")
        image.load()
    return dict(width=w, height=h, sha256=hashlib.sha256(data).hexdigest())


def validate_clips(sheet):
    if sheet.get("projection") != "iso2to1" or sheet.get("scale") != 1:
        raise ValueError(f"{sheet['id']}: projection/scale must be iso2to1/1")
    w, h = sheet["cellW"], sheet["cellH"]
    if any(type(n) is not int or not 1 <= n <= 512 for n in (w, h)):
        raise ValueError(f"{sheet['id']}: invalid cell dimensions")
    if sheet["width"] % w or sheet["height"] % h:
        raise ValueError(f"{sheet['id']}: partial cell")
    keys = set()
    for c in sheet["clips"]:
        key = c["name"], c["direction"]
        if key in keys or c["direction"] not in (*FACINGS, "any"):
            raise ValueError(f"{sheet['id']}: duplicate/invalid clip {key}")
        keys.add(key)
        values = c["row"], c.get("column", 0), c["frames"]
        if any(type(n) is not int for n in values) or min(values[:2]) < 0 or not 1 <= values[2] <= 256:
            raise ValueError(f"{sheet['id']}/{key}: invalid clip range")
        if (c["row"] + 1) * h > sheet["height"] or (c.get("column", 0) + c["frames"]) * w > sheet["width"]:
            raise ValueError(f"{sheet['id']}/{key}: clip out of bounds")
        if type(c["fps"]) not in (int, float) or not math.isfinite(c["fps"]) or not 0 < c["fps"] <= 60 or type(c["loop"]) is not bool:
            raise ValueError(f"{sheet['id']}/{key}: invalid playback")
        a = c.get("anchor", sheet["anchor"])
        if any(type(a[k]) is not int or not 0 <= a[k] <= limit for k, limit in (("x", w), ("y", h))):
            raise ValueError(f"{sheet['id']}/{key}: invalid source anchor")
        if c.get("seatProfile") == "end":
            raise ValueError(f"{sheet['id']}/{key}: end seating is outside the slice")


def expand_aliases(sheet):
    """Materialize chains, including held-phase offsets, without new pixel rows."""
    stored = {(c["name"], c["direction"]): deepcopy(c) for c in sheet["rows"]}
    if len(stored) != len(sheet["rows"]):
        raise ValueError(f"{sheet['id']}: duplicate stored clips")
    aliases = {}
    for a in sheet.get("aliases", []):
        key = a["name"], a["direction"]
        if key in stored or key in aliases:
            raise ValueError(f"{sheet['id']}: duplicate alias {key}")
        aliases[key] = a
    resolved = {}

    def resolve(key, chain=()):
        if key in chain:
            raise ValueError(f"{sheet['id']}: alias cycle {chain + (key,)}")
        if key in resolved:
            return deepcopy(resolved[key])
        if key in stored:
            c = deepcopy(stored[key])
            c.setdefault("column", 0)
            c.setdefault("loop", True)
            c["sourceClip"] = dict(name=key[0], direction=key[1])
        else:
            if key not in aliases:
                raise ValueError(f"{sheet['id']}: missing alias target {key}")
            a = aliases[key]
            target = a["target"]["name"], a["target"]["direction"]
            if target[1] != key[1]:
                raise ValueError(f"{sheet['id']}: alias changes facing {key}")
            c = resolve(target, chain + (key,))
            if "frame" in a:
                frame = a["frame"]
                if type(frame) is not int or not 0 <= frame < c["frames"]:
                    raise ValueError(f"{sheet['id']}: invalid held phase {key}")
                c["column"] += frame
                c["frames"], c["loop"] = 1, False
                if "phaseContacts" in c:
                    c["phaseContacts"] = [c["phaseContacts"][frame]]
                if "handAnchorsByPhase" in c:
                    c["handAnchorsByPhase"] = [c["handAnchorsByPhase"][frame]]
            c.update({k: deepcopy(v) for k, v in a.items() if k not in ("target", "frame")})
            c["aliasOf"] = dict(name=target[0], direction=target[1])
        resolved[key] = c
        return deepcopy(c)

    return [resolve(key) for key in (*stored, *aliases)]


def link_blinks(sheets):
    for normal in sheets.values():
        if normal["kind"] != "character":
            continue
        blink = sheets[normal["blink"]["sheetId"]]
        closed = {(c["name"], c["direction"]): c for c in blink["rows"]}
        blink["normalSheetId"] = normal["id"]
        blink["clips"] = []
        for c in normal["clips"]:
            if c["direction"] not in FACINGS[:2]:
                continue
            src = c["sourceClip"]
            b = closed[("blink-" + src["name"], src["direction"])]
            copy = deepcopy(c)
            copy.update(name="blink-" + c["name"], row=b["row"])
            copy["normalRow"] = c["row"]
            copy["sourceClip"] = dict(name=b["name"], direction=b["direction"])
            blink["clips"].append(copy)
            c["blink"] = dict(sheetId=blink["id"], name=copy["name"], direction=c["direction"], durationMs=100)


def character_shadow():
    """A pack-owned ground sprite: an exact 16x6 contact and optional cast tail."""
    image = Image.new("RGBA", (128, 128))
    draw = ImageDraw.Draw(image)
    draw.ellipse((64, 65, 79, 70), fill=(17, 25, 36, 48))
    draw.ellipse((56, 61, 71, 66), fill=(17, 25, 36, 96))
    stream = BytesIO()
    image.save(stream, format="PNG", compress_level=9)
    data = stream.getvalue()
    info = png_info(data)
    row = dict(name="standing", direction="any", row=0, column=0, frames=1, fps=1, loop=False,
               anchorChecks=[dict(x=64, y=64, radius=0)], contactFootprint=dict(width=16, height=6),
               shadowOffset=dict(x=8, y=4))
    sheet = dict(id="iso-character-shadow", kind="shadow", file=f"sheets/pack-{info['sha256']}.png",
                 cellW=128, cellH=128, anchor=dict(x=64, y=64), scale=1, projection="iso2to1",
                 rows=[row], clips=[deepcopy(row)], **info)
    return sheet, data


def bind_character_actions(manifest):
    available = {p["id"].rsplit("/", 1)[-1]: p for p in manifest["carriedProps"]}
    effects = set()
    manifest["optionalPropFallbacks"] = {"held-dart": dict(available=False, fallback="idle-without-prop",
        reason="Held dart is optional in the slice; the approved prop source exports no dart.")}
    for sheet in manifest["sheets"]:
        if sheet["kind"] != "character":
            continue
        for c in sheet["clips"]:
            prop = c.get("prop", manifest.get("actionBindings", {}).get(c["name"]))
            if prop:
                c["prop"] = prop
                if prop in available:
                    p = available[prop]
                    c["propBinding"] = dict(id=p["id"], sheetId=p["sheetId"], animation=p["animation"], direction=c["direction"])
                elif prop == "held-dart":
                    c["propOptional"] = True
                elif not manifest["carriedProps"]:
                    # Pilot is explicitly a partial pack; the full pack never omits required props.
                    c["propDeferred"] = True
                else:
                    raise ValueError(f"{sheet['id']}/{c['name']}: missing required prop {prop}")
            if "effect" in c:
                effects.add(c["effect"])
        for facing in FACINGS:
            walk = deepcopy(next(c for c in sheet["clips"] if (c["name"], c["direction"]) == ("walk", facing)))
            walk.update(name="gone", effect="exit-fade", hiddenAfterExit=True)
            sheet["clips"].append(walk)
        effects.add("exit-fade")
    for pet in manifest["pets"]:
        for binding in pet.get("bubbleBindings", {}).values():
            if isinstance(binding, dict):
                binding.pop("sharedArt", None)
                binding["external"] = True
                effects.add(binding["binding"])
    manifest["sharedEffectBindings"] = sorted(effects)


def source_paths(art_dir, characters=CHARACTERS, extras=True):
    paths = [art_dir / "env/floors/floors.iso.json", art_dir / "env/walls/walls.iso.json"]
    paths.extend(sorted((art_dir / "furniture-v2").rglob("*.iso.json")))
    if not any("furniture-v2" in p.parts for p in paths[2:]):
        raise ValueError("approved furniture-v2 sources are missing")
    for identity in characters:
        filename = "dev-hoodie" if identity == "dev-hoodie-v2" else identity
        paths.append(art_dir / f"characters/{identity}/{filename}.iso.json")
    if extras:
        paths.extend([art_dir / "pets/cat.iso.json", art_dir / "props/carried-props.iso.json"])
        paths.extend(sorted((art_dir / "env").glob("*.iso.json")))
    return paths


def check_sources(paths):
    import iso_check
    checks = iso_check.Checks()
    for path in paths:
        iso_check.validate(path, checks, 3, 8, "final")
    counts = Counter(row[2] for row in checks.results)
    failed = [row for row in checks.results if row[2] == "FAIL"]
    if failed:
        raise ValueError("source checker failed:\n" + "\n".join(f"{owner}: {name}: {detail}" for owner, name, _, detail in failed))
    return dict(manifests=len(paths), **{key.lower(): counts[key] for key in ("PASS", "SKIP", "FAIL")})


def assemble(paths):
    manifest = dict(version=1, projection="iso2to1", requiredCapabilities=["iso2to1", "renderItemsV1"],
                    facings=list(FACINGS), sheets=[], characters=[], furniture=[], tileTypes=[], pets=[],
                    carriedProps=[], wallModules=[], sourceManifests=[], unsupportedSeatProfiles=["end"],
                    tile=dict(width=64, height=32, anchor=dict(x=32, y=16)), wallRise=16, wallHeight=48,
                    characterCell=dict(width=32, height=64), footAnchor=dict(x=16, y=60), standingHeight=40,
                    seatAnchor=dict(x=16, y=44), deskHeight=18, seatHeight=12, monitorTop=36, chairBackTop=21,
                    light=dict(screenDirection="upper-left", shadowOffset=dict(x=8, y=4), furnitureShadowOffset=dict(x=6, y=3)))
    files, sheets = {}, {}

    def add_png(path):
        data = path.read_bytes()
        info = png_info(data)
        file = f"sheets/pack-{info['sha256']}.png"
        files[file] = data
        return dict(file=file, **info)

    for path in paths:
        raw = path.read_bytes()
        if len(raw) > MAX_JSON:
            raise ValueError(f"{path}: source manifest exceeds 4 MiB")
        source = json.loads(raw.decode("utf-8-sig"))
        if source["version"] != 1 or source["projection"] != "iso2to1" or not source["sheets"]:
            raise ValueError(f"{path}: invalid source manifest")
        # Provenance paths are relative to the approved iso source root, never user paths.
        try:
            name = path.relative_to(ART).as_posix()
        except ValueError:
            name = path.name
        manifest["sourceManifests"].append(dict(file=name, sha256=hashlib.sha256(raw).hexdigest()))
        for original in source["sheets"]:
            s = deepcopy(original)
            if s["id"] in sheets:
                raise ValueError(f"duplicate sheet ID: {s['id']}")
            s.update(add_png(local_file(path.parent, s["file"])))
            for mask in s.get("faceLock", {}).values():
                info = add_png(local_file(path.parent, mask["eyeMask"]))
                mask.update(eyeMask=info.pop("file"), **info)
            s["clips"] = expand_aliases(s)
            sheets[s["id"]] = s
            manifest["sheets"].append(s)
        for key in COLLECTIONS:
            for original in source.get(key, []):
                item = deepcopy(original)
                if key == "characters":
                    identity = path.parent.name
                    item["id"] = f"iso/character/{identity}"
                if any(item["id"] == existing["id"] for existing in manifest[key]):
                    raise ValueError(f"duplicate {key} ID: {item['id']}")
                manifest[key].append(item)
        for key in METADATA:
            if key in source:
                if key in manifest and manifest[key] != source[key]:
                    raise ValueError(f"conflicting source metadata: {key}")
                manifest[key] = deepcopy(source[key])
    shadow, data = character_shadow()
    if shadow["id"] in sheets:
        raise ValueError("reserved iso-character-shadow sheet ID")
    sheets[shadow["id"]] = shadow
    manifest["sheets"].append(shadow)
    files[shadow["file"]] = data
    for character in manifest["characters"]:
        character["shadow"] = dict(sheetId=shadow["id"], animation="standing", direction="any", renderPass="shadows", seated=False)
    bind_character_actions(manifest)
    link_blinks(sheets)
    for s in sheets.values():
        validate_clips(s)
        if s["kind"] == "furniture":
            s["parts"] = [dict(partId=c["partId"], name=c["name"], direction=c["direction"],
                               depthAnchor=deepcopy(c["depthAnchor"]), isoZOrder=c["isoZOrder"],
                               anchor=deepcopy(c.get("anchor", s["anchor"])), anchorChecks=deepcopy(c["anchorChecks"]),
                               hitTargetId=c.get("hitTargetId", "owner"),
                               **{key: c[key] for key in ("seatGroupId", "state") if key in c}) for c in s["rows"]]
    for family in manifest["furniture"]:
        family.setdefault("kind", FURNITURE_KINDS[family["id"].removeprefix("iso/")])
        for variant in family["variants"]:
            variant["heightPx"] = variant.get("height", 0)
            for part in variant["parts"]:
                sheet = sheets[part["sheetId"]]
                clip = next(c for c in sheet["clips"] if (c["name"], c["direction"]) == (part["animation"], part["direction"]))
                part["anchor"] = deepcopy(clip.get("anchor", sheet["anchor"]))
                part["anchorChecks"] = deepcopy(clip["anchorChecks"])
                part.setdefault("hitTargetId", "owner")
    if len(sheets) > 128 or len(manifest["furniture"]) > 512:
        raise ValueError("pack exceeds the 128-sheet/512-furniture limits")
    verify_references(manifest)
    return manifest, files


def verify_references(manifest):
    """Check structured runtime references; never substitute an absent clip."""
    sheets = {s["id"]: s for s in manifest["sheets"]}
    def visit(value):
        if isinstance(value, list):
            for child in value:
                visit(child)
        elif isinstance(value, dict):
            if "sheetId" in value:
                if value["sheetId"] not in sheets:
                    raise ValueError(f"missing sheet reference: {value['sheetId']}")
                sheet = sheets[value["sheetId"]]
                name = value.get("animation", value.get("clip", value.get("name") if "direction" in value else None))
                if name is not None:
                    facings = value.get("directions", [value.get("direction", "any")])
                    for facing in facings:
                        if not any(c["name"] == name and c["direction"] == facing for c in sheet["clips"]):
                            raise ValueError(f"missing clip reference: {sheet['id']}/{name}/{facing}")
            for child in value.values():
                visit(child)
    visit(manifest)


PLACEMENTS = (
    ("desk-1", "desk", 2, 2, "nw"), ("chair-1", "chair", 1, 3, "se"),
    ("desk-2", "desk", 5, 2, "ne"), ("chair-2", "chair", 5, 1, "sw"),
    ("desk-3", "desk", 4, 5, "sw"), ("chair-3", "chair", 5, 6, "ne"),
    ("sofa-1", "sofa", 6, 8, "ne"), ("plant-1", "plant", 0, 6, "se"),
    ("bookshelf-1", "shelf", 0, 0, "se"), ("tv-1", "tv-cabinet", 0, 8, "se"),
    ("coffee-1", "coffee-machine", 9, 1, "nw"), ("fridge-1", "fridge", 9, 3, "nw"),
    ("water-1", "water-cooler", 9, 5, "nw"), ("rug-1", "rug", 6, 7, "sw"),
    ("side-table-1", "side-table", 3, 8, "se"), ("lamp-1", "floor-lamp", 4, 8, "se"),
)


def make_layout(manifest):
    families = {f["id"]: f for f in manifest["furniture"]}
    instances = []
    for ident, family, i, j, orientation in PLACEMENTS:
        asset = families[f"iso/{family}"]
        v = next(v for v in asset["variants"] if v["orientation"] == orientation and v["state"] == "default")
        item = dict(id=ident, kind=asset["kind"], assetId=asset["id"], x=i, y=j,
                    width=v["footprintW"], height=v["footprintH"], rotation=ROTATIONS[orientation],
                    orientation=orientation, state="default", hitTargetId=ident)
        if family in ("desk", "chair"):
            number = ident.rsplit("-", 1)[1]
            item.update(seatGroupId=f"desk-{number}/seat", seatOwnerId=f"desk-{number}")
            item["chairId" if family == "desk" else "deskId"] = f"{'chair' if family == 'desk' else 'desk'}-{number}"
        instances.append(item)
    floor_sheet = next(s for s in manifest["sheets"] if s["id"] == "iso-env-floors")
    manifest["tileTypes"] = [dict(id=n, kind="floor", projection="iso2to1", family=c["family"],
                                  sheetId=floor_sheet["id"], animation=c["name"], direction="any",
                                  **{k: c[k] for k in ("axis", "phase") if k in c}) for n, c in enumerate(floor_sheet["rows"][:3], 1)]
    tiles = [2 + (i + j) % 2 if i >= 8 and j <= 5 else 1 for j in range(10) for i in range(10)]
    transitions = []
    steps = {"+i": (1, 0), "+j": (0, 1), "-i": (-1, 0), "-j": (0, -1)}
    for j in range(10):
        for i in range(10):
            if tiles[j * 10 + i] != 1:
                continue
            active = {edge for edge, (di, dj) in steps.items() if 0 <= i + di < 10 and 0 <= j + dj < 10 and tiles[(j + dj) * 10 + i + di] in (2, 3)}
            for clip in floor_sheet["rows"]:
                if (clip.get("edge") in active or (clip.get("edges") and set(clip["edges"]) <= active)):
                    transitions.append(dict(x=i, y=j, sheetId=floor_sheet["id"], animation=clip["name"], direction="any"))
    walls = []
    for face, suffix in (("left", "l"), ("right", "r")):
        for n in range(10):
            module = "open-door" if face == "left" and n == 4 else "window" if face == "right" and n == 4 else "plain"
            a = dict(i=-0.5, j=n - 0.5) if face == "left" else dict(i=n - 0.5, j=-0.5)
            b = dict(i=-0.5, j=n + 0.5) if face == "left" else dict(i=n + 0.5, j=-0.5)
            walls.append(dict(id=f"wall-{suffix}-{n:02d}", face=face, moduleId=f"iso/wall-{module}-{suffix}", start=a, end=b, heightPx=48, rise=16,
                              **(dict(closedModuleId="iso/wall-closed-door-l") if module == "open-door" else {})))
    layout = dict(version=1, view="iso", projection="iso2to1", requiredCapabilities=manifest["requiredCapabilities"],
                  widthTiles=10, heightTiles=10, tileSize=16, tile=manifest["tile"], wallRise=16, wallHeight=48,
                  tiles=tiles, floorTransitions=transitions, door=dict(x=0, y=4), furniture=instances,
                  zones=[dict(name="workspace", x=0, y=0, width=8, height=7), dict(name="pantry", x=8, y=0, width=2, height=6),
                         dict(name="lounge", x=0, y=7, width=10, height=3), dict(name="door", x=0, y=4, width=1, height=1)],
                  wallSegments=walls, wallJoins=[dict(id="rear-corner", moduleId="iso/wall-inner-rear-corner", ground=dict(i=-0.5, j=-0.5)),
                      dict(id="end-left", moduleId="iso/wall-end-cap-l", ground=dict(i=-0.5, j=8.5)),
                      dict(id="end-right", moduleId="iso/wall-end-cap-r", ground=dict(i=8.5, j=-0.5))],
                  cutaway=dict(front="hidden", rear="visible"))
    if manifest["pets"]:
        layout["pets"] = [dict(id="cat-1", assetId="iso/cat", x=2, y=4,
            demonstrationPath=[dict(x=i, y=j) for i, j in ((2, 4), (3, 4), (3, 5), (3, 6), (2, 6), (2, 5), (2, 4))])]
    if len(manifest["characters"]) == 3:
        # Frozen synthetic IDs follow the flat UTF-16 FNV-1a + ordinal/probing rule.
        layout["acceptanceFixture"] = dict(seed=20261004, agents=[
            dict(id="iso-demo-child", provider="codex", role="worker", parentId="iso-demo-supervisor",
                 characterId="iso/character/punk-girl", deskId="desk-2", action="reporting"),
            dict(id="iso-demo-supervisor", provider="claude", role="supervisor",
                 characterId="iso/character/professor", deskId="desk-3", action="typing"),
            dict(id="iso-demo-worker", provider="antigravity", role="worker",
                 characterId="iso/character/dev-hoodie-v2", deskId="desk-1", action="typing")])
    validate_layout(layout, manifest)
    return layout


def validate_layout(layout, manifest):
    families = {f["id"]: f for f in manifest["furniture"]}
    blocked = set()
    approaches = []
    for item in layout["furniture"]:
        v = next(v for v in families[item["assetId"]]["variants"] if v["orientation"] == item["orientation"] and v["state"] == item["state"])
        if (item["width"], item["height"]) != (v["footprintW"], v["footprintH"]):
            raise ValueError(f"{item['id']}: footprint mismatch")
        for cell in v["blockedCells"]:
            point = item["x"] + cell["x"], item["y"] + cell["y"]
            if point in blocked or not all(0 <= n < 10 for n in point):
                raise ValueError(f"{item['id']}: overlapping/outside blocked cell {point}")
            blocked.add(point)
        approaches.extend((item["id"], (item["x"] + p["x"], item["y"] + p["y"])) for p in v["points"])
    start = layout["door"]["x"], layout["door"]["y"]
    reached, queue = {start}, deque([start])
    if start in blocked:
        raise ValueError("door is blocked")
    while queue:
        i, j = queue.popleft()
        for p in ((i+1, j), (i-1, j), (i, j+1), (i, j-1)):
            if all(0 <= n < 10 for n in p) and p not in blocked and p not in reached:
                reached.add(p)
                queue.append(p)
    for ident, p in approaches:
        if p not in reached:
            raise ValueError(f"{ident}: unreachable approach {p}")
    reserved = {p for _, p in approaches}
    for pet in layout.get("pets", []):
        path = [(p["x"], p["y"]) for p in pet["demonstrationPath"]]
        if any(p not in reached or p in reserved for p in path):
            raise ValueError(f"{pet['id']}: demonstration path crosses furniture or an approach")
        if any(abs(a[0]-b[0]) + abs(a[1]-b[1]) != 1 for a, b in zip(path, path[1:])):
            raise ValueError(f"{pet['id']}: demonstration path is not four-way")
    return dict(blockedCells=len(blocked), reachableCells=len(reached), reachableApproaches=len(approaches))


def atomic_write(path, data):
    path.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.NamedTemporaryFile(dir=path.parent, prefix=".iso-", suffix=".tmp", delete=False) as file:
        temporary = Path(file.name)
        file.write(data)
    try:
        os.replace(temporary, path)
    finally:
        temporary.unlink(missing_ok=True)


def build_iso_pack(art_dir=ART, output=ART / "pack", *, pilot=False):
    if output.resolve().is_relative_to((ROOT / "assets/pack").resolve()):
        raise ValueError("the flat runtime pack is not an iso output directory")
    if output.resolve().is_relative_to(art_dir.resolve()) and not output.resolve().is_relative_to((art_dir / "pack").resolve()):
        raise ValueError("source art directories are read-only; publish under iso/pack")
    paths = source_paths(art_dir, CHARACTERS[:1] if pilot else CHARACTERS, extras=not pilot)
    checks = check_sources(paths)
    manifest, files = assemble(paths)
    layout = make_layout(manifest)
    manifest["layout"] = "layout.json"
    manifest["sourceChecks"] = checks
    manifest["generatedBy"] = "tools/build_iso_pack.py"
    manifest["complete"] = not pilot
    manifest["deferredContent"] = ["punk-girl", "professor", "pets", "carriedProps", "environment-polish"] if pilot else []
    layout_data = json_bytes(layout)
    manifest["files"] = [dict(file=name, bytes=len(data), sha256=hashlib.sha256(data).hexdigest()) for name, data in sorted(files.items())]
    manifest["files"].append(dict(file="layout.json", bytes=len(layout_data), sha256=hashlib.sha256(layout_data).hexdigest()))
    manifest_data = json_bytes(manifest)
    # Immutable PNGs first, assets.json last. Older referenced hashes are retained.
    for filename, data in sorted(files.items()):
        path = output / filename
        if path.exists() and path.read_bytes() == data:
            continue
        atomic_write(path, data)
    atomic_write(output / "layout.json", layout_data)
    atomic_write(output / "assets.json", manifest_data)
    return dict(manifest=str(output / "assets.json"), characters=len(manifest["characters"]), sheets=len(manifest["sheets"]),
                pngs=len(files), furniture=len(manifest["furniture"]), sourceChecks=checks,
                layoutChecks=validate_layout(layout, manifest), bytes=sum(map(len, files.values())) + len(manifest_data) + len(layout_data))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--art-dir", type=Path, default=ART)
    parser.add_argument("--output", type=Path, default=ART / "pack")
    parser.add_argument("--pilot", action="store_true", help="floors, walls, furniture-v2 and dev-hoodie-v2 only")
    args = parser.parse_args()
    try:
        print(json.dumps(build_iso_pack(args.art_dir, args.output, pilot=args.pilot), indent=2))
    except (OSError, ValueError, KeyError, TypeError, StopIteration) as error:
        print(f"build_iso_pack: {error}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
