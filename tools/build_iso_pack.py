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
from iso_depth_metadata import refine_depth

ROOT = Path(__file__).resolve().parents[1]
ART = ROOT / "assets/art/iso"
FACINGS = ("se", "sw", "ne", "nw")
ROTATIONS = {"se": 0, "sw": 90, "nw": 180, "ne": 270}
CHARACTERS = ("dev-hoodie-v2", "punk-girl", "professor", "glasses-bun", "hanbok-modern", "red-braid")
OPTIONAL_CHARACTERS = ("cap-teen", "labcoat-tall")
FURNITURE_KINDS = {"desk": "desk", "chair": "chair", "sofa": "sofa", "plant": "plant", "shelf": "bookshelf",
                   "tv-cabinet": "tv", "coffee-machine": "pantry", "fridge": "cabinet", "water-cooler": "water",
                   "rug": "rug", "side-table": "table", "floor-lamp": "lamp"}
COLLECTIONS = ("characters", "furniture", "pets", "carriedProps", "wallModules")
METADATA = ("actionBindings", "attachmentRule", "transitionRule", "materialPriority")
MAX_JSON = 4 * 1024 * 1024
MAX_PNG = 32 * 1024 * 1024
# Select a complete family (including separate shadow manifests) from v3.
# Missing v3 exports fall back to the unchanged v2 family. Plant/lamp/rug
# deliberately remain v2; copied v3 plant/lamp exports are not pack inputs.
FURNITURE_V3 = {
    "workstations/desk": "workstations/desk",
    "workstations/chair": "chair/chair",
    "lounge/tv-cabinet": "lounge-tv/tv-cabinet",
    "lounge/sofa": "utility-lounge/sofa",
    "lounge/side-table": "utility-lounge/side-table",
    "utility/shelf": "utility-shelf/shelf",
    "utility/water-cooler": "utility-lounge/water-cooler",
    "utility/fridge": "utility-lounge/fridge",
    "utility/coffee-machine": "utility-lounge/coffee-machine",
}


def json_bytes(data, *, compact=False):
    result = (json.dumps(data, ensure_ascii=False, indent=None if compact else 2,
                         separators=(",", ":") if compact else None, allow_nan=False) + "\n").encode("utf-8")
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
    fallback = sorted((art_dir / "furniture-v2").rglob("*.iso.json"))
    if not fallback:
        raise ValueError("approved furniture-v2 sources are missing")
    replacements = {}
    for old, new in FURNITURE_V3.items():
        family = [p for p in fallback if p.relative_to(art_dir / "furniture-v2").as_posix()
                  in (old + ".iso.json", old + "-shadow.iso.json")]
        candidates = [art_dir / "furniture-v3" / (new + ("-shadow" if p.name.endswith("-shadow.iso.json") else "") + ".iso.json")
                      for p in family]
        if candidates and all(p.is_file() for p in candidates):
            replacements.update(zip(family, candidates))
    paths.extend(replacements.get(p, p) for p in fallback)
    for identity in characters:
        filename = "dev-hoodie" if identity == "dev-hoodie-v2" else identity
        paths.append(art_dir / f"characters/{identity}/{filename}.iso.json")
    if extras:
        paths.extend([art_dir / "pets/cat.iso.json", art_dir / "props/carried-props.iso.json"])
        paths.extend(sorted((art_dir / "env").glob("*.iso.json")))
        for identity in OPTIONAL_CHARACTERS:
            path = art_dir / f"characters/{identity}/{identity}.iso.json"
            if path.is_file() and identity not in characters:
                paths.append(path)
        # Only final exports are candidates; a present failing export stops publication.
        paths.extend(sorted((art_dir / "pets-v2").rglob("*.iso.json")))
        paths.extend(sorted((art_dir / "env-v3").glob("*.iso.json")))
    return paths


def check_sources(paths):
    import iso_check
    checks = iso_check.Checks()
    for path in paths:
        iso_check.validate(path, checks, 0, 8, "final")
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
                    seatAnchor=dict(x=16, y=44), deskHeight=18, seatHeight=12, chairBackTop=21,
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
        if not source.get("characters"):
            normals = [s for s in source["sheets"] if s["kind"] == "character"]
            if len(normals) == 1:
                # glasses-bun exports the complete checker-approved rig without a design entry.
                manifest["characters"].append(dict(id=f"iso/character/{path.parent.name}", sheetId=normals[0]["id"]))
        for key in METADATA:
            if key in source:
                if key in manifest and manifest[key] != source[key]:
                    raise ValueError(f"conflicting source metadata: {key}")
                manifest[key] = deepcopy(source[key])
        for assembly in source.get("decorAssemblies", []):
            manifest.setdefault("decorAssemblies", []).append(deepcopy(assembly))
            for edge in range(assembly["spanEdges"]):
                parts = [dict(sheetId=p["sheetId"], animation=p["clip"], direction="any")
                         for p in assembly["parts"] + assembly.get("overlays", []) if p["edgeIndex"] == edge]
                manifest["wallModules"].append(dict(id=f"{assembly['id']}-edge-{edge}", face=assembly["face"],
                    edgeAnchor=dict(x=32 if assembly["face"] == "left" else 0, y=48), parts=parts,
                    slot=assembly.get("slot", "window"), **({"hostModule": assembly["hostModule"]} if "hostModule" in assembly else {})))
    # Nameplate belongs to the closed leaf; the runtime door stays open for agents.
    for assembly in manifest.get("decorAssemblies", []):
        if assembly.get("visibleWhen") == "closed":
            host = next(m for m in manifest["wallModules"] if m["id"] == assembly["hostModule"])
            host["parts"].extend(dict(sheetId=p["sheetId"], animation=p["clip"], direction="any") for p in assembly["parts"])
    shadow, data = character_shadow()
    if shadow["id"] in sheets:
        raise ValueError("reserved iso-character-shadow sheet ID")
    sheets[shadow["id"]] = shadow
    manifest["sheets"].append(shadow)
    files[shadow["file"]] = data
    for character in manifest["characters"]:
        character["shadow"] = dict(sheetId=shadow["id"], animation="standing", direction="any", renderPass="shadows", seated=False)
    bind_character_actions(manifest)
    from iso_seated_geometry import bake
    bake(manifest, files)
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
        if family["id"] == "iso/tv-cabinet":
            # Authored TV states select the existing screen/glow clips; no art
            # or part IDs change. Explicit variants keep the runtime state API.
            for original in list(family["variants"]):
                for state, animation in original.get("screenStates", {}).items():
                    if state == original["state"]:
                        continue
                    variant = deepcopy(original)
                    variant["state"] = state
                    variant["screen"]["animation"] = animation
                    glow = variant.get("glow")
                    selection = original.get("glowStates", {}).get(state)
                    if glow and selection:
                        glow.update(dict(animation=selection) if isinstance(selection, str) else selection)
                    family["variants"].append(variant)
        for variant in family["variants"]:
            variant["heightPx"] = variant.get("height", 0)
            for part in variant["parts"]:
                sheet = sheets[part["sheetId"]]
                clip = next(c for c in sheet["clips"] if (c["name"], c["direction"]) == (part["animation"], part["direction"]))
                part["anchor"] = deepcopy(clip.get("anchor", sheet["anchor"]))
                part["anchorChecks"] = deepcopy(clip["anchorChecks"])
                part.setdefault("hitTargetId", "owner")
    for family_id, field, fallback in (("iso/desk", "monitorTop", 36), ("iso/tv-cabinet", "tvTop", 38)):
        family = next((f for f in manifest["furniture"] if f["id"] == family_id), None)
        if family:
            manifest[field] = max(v.get(field, fallback) for v in family["variants"])
    if len(sheets) > 128 or len(manifest["furniture"]) > 512:
        raise ValueError("pack exceeds the 128-sheet/512-furniture limits")
    refine_depth(manifest)
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
    # Six intentional back-to-back pairs; fronts/monitors face the surrounding aisles.
    # The original three contact examples remain in IsoLayoutLoader.Default, not this production room.
    ("desk-1", "desk", 2, 1, "ne"), ("desk-2", "desk", 2, 2, "sw"),
    ("desk-3", "desk", 7, 1, "nw"), ("desk-4", "desk", 8, 1, "se"),
    ("desk-5", "desk", 2, 4, "ne"), ("desk-6", "desk", 2, 5, "sw"),
    ("desk-7", "desk", 6, 4, "nw"), ("desk-8", "desk", 7, 4, "se"),
    ("desk-9", "desk", 5, 7, "nw"), ("desk-10", "desk", 6, 7, "se"),
    ("desk-11", "desk", 8, 7, "ne"), ("desk-12", "desk", 8, 8, "sw"),
    ("sofa-1", "sofa", 0, 9, "ne"), ("plant-1", "plant", 0, 2, "se"),
    ("bookshelf-1", "shelf", 0, 0, "se"), ("tv-1", "tv-cabinet", 0, 7, "se"),
    ("coffee-1", "coffee-machine", 1, 0, "sw"), ("fridge-1", "fridge", 0, 5, "se"),
    ("water-1", "water-cooler", 5, 0, "sw"), ("rug-1", "rug", 0, 7, "sw"),
    ("side-table-1", "side-table", 2, 7, "se"), ("lamp-1", "floor-lamp", 0, 3, "se"),
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
        if family == "desk":
            seat = next(p for p in v["seatProfiles"] if p["id"] == "front")
            facing = seat["isoFacing"]
            chair = next(c for c in families["iso/chair"]["variants"] if c["orientation"] == facing and c["state"] == "default")
            chair_id = f"chair-{number}"
            instances.append(dict(id=chair_id, kind="chair", assetId="iso/chair",
                x=i + seat["approach"]["x"], y=j + seat["approach"]["y"],
                width=chair["footprintW"], height=chair["footprintH"], rotation=ROTATIONS[facing],
                orientation=facing, state="default", hitTargetId=chair_id,
                seatGroupId=f"desk-{number}/seat", seatOwnerId=ident, deskId=ident))
    floor_sheet = next(s for s in manifest["sheets"] if s["id"] == "iso-env-floors")
    manifest["tileTypes"] = [dict(id=n, kind="floor", projection="iso2to1", family=c["family"],
                                  sheetId=floor_sheet["id"], animation=c["name"], direction="any",
                                  **{k: c[k] for k in ("axis", "phase") if k in c}) for n, c in enumerate(floor_sheet["rows"][:3], 1)]
    tiles = [2 + (i + j) % 2 if j == 0 or i == 0 and j <= 2 else 1 for j in range(10) for i in range(10)]
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
    v3 = any(m["id"] == "iso/wall-plain-l-v3" for m in manifest["wallModules"])
    wall_suffix = "-v3" if v3 else ""
    for face, suffix in (("left", "l"), ("right", "r")):
        for n in range(10):
            module = "open-door" if face == "left" and n == 4 else "window" if not v3 and face == "right" and n == 4 else "plain"
            a = dict(i=-0.5, j=n - 0.5) if face == "left" else dict(i=n - 0.5, j=-0.5)
            b = dict(i=-0.5, j=n + 0.5) if face == "left" else dict(i=n + 0.5, j=-0.5)
            walls.append(dict(id=f"wall-{suffix}-{n:02d}", face=face, moduleId=f"iso/wall-{module}-{suffix}{wall_suffix}", start=a, end=b, heightPx=48, rise=16,
                              **(dict(closedModuleId="iso/wall-closed-door-l" + wall_suffix) if module == "open-door" else {})))
    if manifest.get("decorAssemblies"):
        # Keep the door clear and reserve both window hosts. Sparse upper wall accents.
        choices = (("left", 2, "whiteboard"), ("left", 3, "clock"), ("left", 6, "corkboard"),
                   ("left", 8, "city-picture"), ("right", 1, "abstract-art"), ("right", 3, "string-lights"),
                   ("right", 4, "large-window-day"), ("right", 7, "plant-shelf"), ("right", 9, "wall-lamp"))
        for face, n, name in choices:
            suffix = "l" if face == "left" else "r"
            assembly = next(a for a in manifest["decorAssemblies"] if a["id"] == f"iso/env-v3/{name}-{suffix}")
            for edge in range(assembly["spanEdges"]):
                host = next(w for w in walls if w["id"] == f"wall-{suffix}-{n+edge:02d}")
                walls.append(dict(id=f"decor-{suffix}-{n:02d}-{edge}", face=face, hostId=host["id"],
                    moduleId=f"{assembly['id']}-edge-{edge}", start=deepcopy(host["start"]), end=deepcopy(host["end"])))
    layout = dict(version=1, view="iso", projection="iso2to1", requiredCapabilities=manifest["requiredCapabilities"],
                  widthTiles=10, heightTiles=10, tileSize=16, tile=manifest["tile"], wallRise=16, wallHeight=48,
                  tiles=tiles, floorTransitions=transitions, door=dict(x=0, y=4), furniture=instances,
                  zones=[dict(name="workspace", x=1, y=1, width=9, height=9), dict(name="pantry", x=0, y=0, width=10, height=1),
                         dict(name="lounge", x=0, y=6, width=3, height=4), dict(name="door", x=0, y=4, width=1, height=1)],
                  wallSegments=walls, wallJoins=[dict(id="rear-corner", moduleId="iso/wall-inner-rear-corner", ground=dict(i=-0.5, j=-0.5)),
                      dict(id="end-left", moduleId="iso/wall-end-cap-l", ground=dict(i=-0.5, j=8.5)),
                      dict(id="end-right", moduleId="iso/wall-end-cap-r", ground=dict(i=8.5, j=-0.5))],
                  cutaway=dict(front="hidden", rear="visible"))
    if manifest["pets"]:
        layout["pets"] = [dict(id="cat-1", assetId="iso/cat", x=2, y=6,
            demonstrationPath=[dict(x=i, y=j) for i, j in ((2, 6), (1, 6), (1, 5), (1, 4), (1, 5), (1, 6), (2, 6))])]
    if len(manifest["characters"]) >= 3:
        # Frozen synthetic IDs follow the flat UTF-16 FNV-1a + ordinal/probing rule.
        layout["acceptanceFixture"] = dict(seed=20261004, agents=[
            dict(id="iso-demo-child", provider="codex", role="worker", parentId="iso-demo-supervisor",
                 characterId="iso/character/punk-girl", deskId="desk-1", action="reporting"),
            dict(id="iso-demo-supervisor", provider="claude", role="supervisor",
                 characterId="iso/character/professor", deskId="desk-10", action="typing"),
            dict(id="iso-demo-worker", provider="antigravity", role="worker",
                 characterId="iso/character/dev-hoodie-v2", deskId="desk-11", action="typing")])
        used = set()
        roster = [c["id"] for c in manifest["characters"]]
        for agent in sorted(layout["acceptanceFixture"]["agents"], key=lambda a: a["id"]):
            value = 2166136261
            units = agent["id"].encode("utf-16-le")
            for n in range(0, len(units), 2):
                value = ((value ^ (units[n] + (units[n+1] << 8))) * 16777619) & 0xffffffff
            start = value % len(roster)
            agent["characterId"] = next((roster[(start+k) % len(roster)] for k in range(len(roster))
                                          if roster[(start+k) % len(roster)] not in used), roster[start])
            used.add(agent["characterId"])
    validate_layout(layout, manifest)
    return layout


def validate_layout(layout, manifest):
    modules = {m["id"]: m for m in manifest["wallModules"]}
    walls = {w["id"]: w for w in layout["wallSegments"]}
    decorated = set()
    if len(walls) != len(layout["wallSegments"]):
        raise ValueError("duplicate wall ID")
    for wall in walls.values():
        module = modules[wall["moduleId"]]
        if module["face"] != wall["face"]:
            raise ValueError("wall face mismatch")
        if "hostId" in wall:
            host = walls.get(wall["hostId"])
            if not host or "hostId" in host or host["face"] != wall["face"] or host["start"] != wall["start"]:
                raise ValueError("invalid decor host/anchor")
            if host["id"] in decorated:
                raise ValueError("overlapping wall decor slots")
            decorated.add(host["id"])
            if module.get("hostModule", host["moduleId"]) != host["moduleId"] or not module.get("hostModule") and "plain" not in host["moduleId"]:
                raise ValueError("decor overlaps a door/window")
    families = {f["id"]: f for f in manifest["furniture"]}
    blocked = set()
    physical = set()
    seats = set()
    approaches = []
    variants = {}
    for item in layout["furniture"]:
        v = next(v for v in families[item["assetId"]]["variants"] if v["orientation"] == item["orientation"] and v["state"] == item["state"])
        variants[item["id"]] = v
        if (item["width"], item["height"]) != (v["footprintW"], v["footprintH"]):
            raise ValueError(f"{item['id']}: footprint mismatch")
        cells = {(item["x"] + x, item["y"] + y) for x in range(item["width"]) for y in range(item["height"])}
        if any(not all(0 <= n < 10 for n in p) for p in cells):
            raise ValueError(f"{item['id']}: outside footprint")
        if item["assetId"] != "iso/rug":
            if physical & cells:
                raise ValueError(f"{item['id']}: overlapping physical footprint")
            physical.update(cells)
        for seat in v.get("seatProfiles", []):
            point = item["x"] + seat["approach"]["x"], item["y"] + seat["approach"]["y"]
            if item["assetId"] == "iso/desk":
                if point in seats:
                    raise ValueError(f"{item['id']}: duplicate work seat")
                seats.add(point)
            approaches.append((item["id"], point))
        for cell in v["blockedCells"]:
            point = item["x"] + cell["x"], item["y"] + cell["y"]
            if point in blocked or not all(0 <= n < 10 for n in point):
                raise ValueError(f"{item['id']}: overlapping/outside blocked cell {point}")
            blocked.add(point)
        approaches.extend((item["id"], (item["x"] + p["x"], item["y"] + p["y"])) for p in v["points"])
    for item in layout["furniture"]:
        if item["assetId"] == "iso/chair" and item.get("deskId"):
            desk = next((d for d in layout["furniture"] if d["id"] == item["deskId"] and d["assetId"] == "iso/desk"), None)
            if desk is None:
                raise ValueError(f"{item['id']}: unknown chair owner")
            seat = next(s for s in variants[desk["id"]]["seatProfiles"] if s["id"] == "front")
            if (item["x"], item["y"], item["orientation"]) != (desk["x"] + seat["approach"]["x"], desk["y"] + seat["approach"]["y"], seat["isoFacing"]):
                raise ValueError(f"{item['id']}: misaligned chair")
        if item["assetId"] == "iso/rug":
            rug = {(item["x"] + x, item["y"] + y) for x in range(item["width"]) for y in range(item["height"])}
            for desk in (d for d in layout["furniture"] if d["assetId"] == "iso/desk"):
                cells = {(desk["x"] + x, desk["y"] + y) for x in range(desk["width"]) for y in range(desk["height"])}
                if rug & (cells | seats):
                    raise ValueError(f"{desk['id']}: workstation overlaps lounge rug")
    approaches = list(dict.fromkeys(approaches))
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
    manifest["deferredContent"] = [*CHARACTERS[1:], "pets", "carriedProps", "environment-polish", "env-v3"] if pilot else []
    layout_data = json_bytes(layout)
    manifest["files"] = [dict(file=name, bytes=len(data), sha256=hashlib.sha256(data).hexdigest()) for name, data in sorted(files.items())]
    manifest["files"].append(dict(file="layout.json", bytes=len(layout_data), sha256=hashlib.sha256(layout_data).hexdigest()))
    manifest_data = json_bytes(manifest, compact=True)
    # Immutable PNGs first, assets.json last. Older referenced hashes are retained.
    for filename, data in sorted(files.items()):
        path = output / filename
        if path.exists() and path.read_bytes() == data:
            continue
        atomic_write(path, data)
    atomic_write(output / "layout.json", layout_data)
    atomic_write(output / "assets.json", manifest_data)
    return dict(manifest=str(output / "assets.json"), characters=len(manifest["characters"]), sheets=len(manifest["sheets"]),
                pngs=len(files), furniture=len(manifest["furniture"]), pets=len(manifest["pets"]), sourceChecks=checks,
                pngBytes=sum(map(len, files.values())), manifestBytes=len(manifest_data), layoutBytes=len(layout_data),
                layoutChecks=validate_layout(layout, manifest), bytes=sum(map(len, files.values())) + len(manifest_data) + len(layout_data))


def refresh_seated_pack(art_dir=ART, output=ART / "pack"):
    """Rebuild the chair sandwich against the shipped actor pixels.

    Source character exports currently differ from the shipped pack. A chair
    repair must not also replace their six animation sheets. Other lanes can
    use the normal full source build when intentionally integrating that art.
    """
    if output.resolve().is_relative_to((ROOT/"assets/pack").resolve()):
        raise ValueError("the flat runtime pack is not an iso output directory")
    manifest=json.loads((output/"assets.json").read_text(encoding="utf-8"))
    files={s["file"]:(output/s["file"]).read_bytes() for s in manifest["sheets"] if s["id"]!="iso-chair-sandwich"}
    for s in manifest["sheets"]:
        for mask in s.get("faceLock",{}).values(): files[mask["eyeMask"]]=(output/mask["eyeMask"]).read_bytes()
    path=art_dir/"furniture-v3/chair/chair.iso.json"
    source=json.loads(path.read_text(encoding="utf-8")); sheet=deepcopy(source["sheets"][0])
    data=local_file(path.parent,sheet["file"]).read_bytes();info=png_info(data)
    sheet.update(file=f"sheets/pack-{info['sha256']}.png",**info);sheet["clips"]=expand_aliases(sheet)
    files[sheet["file"]]=data
    manifest["sheets"]=[sheet if s["id"]==sheet["id"] else s for s in manifest["sheets"] if s["id"]!="iso-chair-sandwich"]
    manifest["furniture"]=[deepcopy(source["furniture"][0]) if f["id"]=="iso/chair" else f for f in manifest["furniture"]]
    for p in manifest.get("sourceManifests",[]):
        if p["file"]=="furniture-v3/chair/chair.iso.json":p["sha256"]=hashlib.sha256(path.read_bytes()).hexdigest()
    from iso_seated_geometry import bake
    bake(manifest,files)
    link_blinks({s["id"]:s for s in manifest["sheets"]})
    refine_depth(manifest)
    for s in manifest["sheets"]:validate_clips(s)
    verify_references(manifest)
    referenced={s["file"] for s in manifest["sheets"]}
    referenced.update(m["eyeMask"] for s in manifest["sheets"] for m in s.get("faceLock",{}).values())
    files={name:data for name,data in files.items() if name in referenced}
    manifest["files"]=[dict(file=n,bytes=len(d),sha256=hashlib.sha256(d).hexdigest()) for n,d in sorted(files.items())]
    layout=(output/"layout.json").read_bytes()
    manifest["files"].append(dict(file="layout.json",bytes=len(layout),sha256=hashlib.sha256(layout).hexdigest()))
    for name,data in files.items():atomic_write(output/name,data)
    atomic_write(output/"assets.json",json_bytes(manifest,compact=True))
    return manifest["seatedOracle"]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--art-dir", type=Path, default=ART)
    parser.add_argument("--output", type=Path, default=ART / "pack")
    parser.add_argument("--pilot", action="store_true", help="floors, walls, furniture-v2 and dev-hoodie-v2 only")
    parser.add_argument("--layout-only", action="store_true", help="rewrite layout.json from the existing pack; leave assets.json and all art unchanged")
    parser.add_argument("--seated-only", action="store_true", help="rebuild chair masks against shipped actor pixels, preserving all other art")
    args = parser.parse_args()
    try:
        if args.seated_only:
            print(json.dumps(refresh_seated_pack(args.art_dir,args.output),indent=2))
        elif args.layout_only:
            manifest = json.loads((args.output / "assets.json").read_text(encoding="utf-8"))
            layout = make_layout(manifest)
            data = json_bytes(layout)
            atomic_write(args.output / "layout.json", data)
            print(json.dumps(dict(layout=str(args.output / "layout.json"), sha256=hashlib.sha256(data).hexdigest(),
                layoutChecks=validate_layout(layout, manifest), manifestFileIndexUnchanged=True), indent=2))
        else:
            print(json.dumps(build_iso_pack(args.art_dir, args.output, pilot=args.pilot), indent=2))
    except (OSError, ValueError, KeyError, TypeError, StopIteration) as error:
        print(f"build_iso_pack: {error}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
