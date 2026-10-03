"""Small isolated runtime-pack checks; run with python -B -m unittest discover -s tools."""

from copy import deepcopy
from collections import deque
import hashlib
import json
from pathlib import Path
import shutil
import tempfile
import unittest
from unittest.mock import patch

from PIL import Image, ImageChops

import build_pack


class BuildPackTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="vibedotcom-pack-")
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.art = self.root / "art"
        self.output = self.root / "pack"
        self.sheets = self.art / "sheets"
        self.sheets.mkdir(parents=True)
        originals = build_pack.ROOT / "assets" / "art" / "sheets"
        for name in [*build_pack.P5_IDS, "props", "icons", "tiles"]:
            for suffix in (".png", ".manifest.json"):
                shutil.copyfile(originals / f"{name}{suffix}", self.sheets / f"{name}{suffix}")
        self.base = build_pack.ProductionSheet(self.sheets / "codex-worker-01.png",
                                              self.sheets / "codex-worker-01.manifest.json")

    def roster(self, name, image=None, metadata=None, final_alias=False):
        directory = self.art / "roster" / name
        run = directory / "run"
        run.mkdir(parents=True)
        image = image if image is not None else self.base.image
        image.save(run / "sprite-sheet-alpha.png")
        data = metadata if metadata is not None else self.base.data
        (run / "manifest.json").write_text(json.dumps(data), encoding="utf-8")
        if final_alias:
            image.save(directory / f"{name}.png")
            (directory / f"{name}.manifest.json").write_text(json.dumps(data), encoding="utf-8")
        return run

    def manifest(self):
        return json.loads((self.output / "assets.json").read_text(encoding="utf-8"))

    def files(self):
        return {str(p.relative_to(self.output)): (hashlib.sha256(p.read_bytes()).hexdigest(), p.stat().st_mtime_ns)
                for p in self.output.rglob("*") if p.is_file()}

    def copy_seated(self, name):
        source = build_pack.ROOT / "assets/art/seated" / name
        target = self.art / "seated" / name
        target.mkdir(parents=True)
        data = json.loads((source / f"{name}.manifest.json").read_text(encoding="utf-8-sig"))
        files = {f"{name}.png", f"{name}.manifest.json", data["facelock"]["report"],
                 data["seated"]["blinkSheet"]["file"]}
        files.update(entry["file"] for entry in data.get("blink", {}).values())
        files.update(entry["file"] for entry in data["seated"]["blink"].values())
        for filename in files:
            shutil.copyfile(source / filename, target / filename)
        return target

    def runtime_frames(self, manifest, sheet_id, name, direction="any"):
        sheet = next(s for s in manifest["sheets"] if s["id"] == sheet_id)
        row = next(r for r in sheet["rows"] if (r["name"], r["direction"]) == (name, direction))
        cw, ch = sheet["cellW"], sheet["cellH"]
        with Image.open(self.output / sheet["file"]) as image:
            return [image.crop(((row["column"] + f) * cw, row["row"] * ch,
                                (row["column"] + f + 1) * cw, (row["row"] + 1) * ch)).convert("RGBA")
                    for f in range(row["frames"])]

    def assert_static_body(self, reference, actual, authored_mask):
        # The allowed region comes from production QA, never runtime differences.
        static = Image.composite(reference, actual, authored_mask)
        self.assertEqual(hashlib.sha256(reference.tobytes()).digest(), hashlib.sha256(static.tobytes()).digest())

    def test_every_published_seated_design_and_phase_matched_blinks(self):
        published = [p for p in sorted((build_pack.ROOT / "assets/art/seated").iterdir())
                     if p.is_dir() and (p / f"{p.name}.png").is_file() and (p / f"{p.name}.manifest.json").is_file()]
        for directory in published:
            self.copy_seated(directory.name)
        env = self.art / "env2/sheets"
        env.mkdir(parents=True)
        for name in ("desks", "monitors", "office-chairs"):
            for suffix in (".png", ".manifest.json"):
                shutil.copyfile(build_pack.ROOT / "assets/art/env2/sheets" / f"{name}{suffix}", env / f"{name}{suffix}")
        summary = build_pack.build_pack(self.art, self.output)
        self.assertEqual(len(published), summary["seatedCharacters"])
        manifest = self.manifest()
        self.assertTrue({p.name for p in published} <= {c["id"] for c in manifest["characters"]})
        sheets = {s["id"]: s for s in manifest["sheets"]}
        for directory in published:
            with self.subTest(character=directory.name):
                source = build_pack.ProductionSheet(directory / f"{directory.name}.png", directory / f"{directory.name}.manifest.json")
                sheet = sheets["char-" + directory.name]
                rows = {(r["name"], r["direction"]): r for r in sheet["rows"]}
                self.assertEqual(len(rows), len(sheet["rows"]))
                # Original typing remains available as legacy, with its original timing/count.
                for source_name, direction in (("typing_back", "up"), ("typing_side", "right")):
                    legacy = rows[("typing", direction)]
                    self.assertEqual((3, *source.playback(source_name)), (legacy["frames"], legacy["fps"], legacy["loop"]))
                box = source.frames("idle", (32, 64))[0].getchannel("A").getbbox()
                size = (round(32 * 30 / (box[3] - box[1])), round(64 * 30 / (box[3] - box[1])))
                position = (16 - size[0] // 2, 60 - round(60 * size[1] / 64))
                for name, direction in build_pack.SEATED_ACTIONS:
                    row = rows[(name, direction)]
                    self.assertEqual((4, 4, True), (row["frames"], row["fps"], row["loop"]))
                    contact = source.data["seated"]["rows"][name]["seatAnchor"]
                    self.assertEqual({"x": position[0] + round(contact["x"] * size[0] / 32),
                                      "y": position[1] + round(contact["y"] * size[1] / 64)}, row["seatAnchor"])
                    frames = self.runtime_frames(manifest, sheet["id"], name, direction)
                    for phase, original in enumerate(source.frames(name, (32, 64))):
                        expected = Image.new("RGBA", (32, 64))
                        expected.paste(original.resize(size, Image.Resampling.NEAREST), position)
                        self.assertEqual(expected.tobytes(), frames[phase].tobytes())
                        lower = (0, row["seatAnchor"]["y"], 32, 64)
                        self.assertEqual(frames[0].crop(lower).tobytes(), frames[phase].crop(lower).tobytes())
                    if direction == "up":
                        # Independent monitor bounds: source y=4..25, anchor y=22.
                        # Outer plate top is two pixels above the nine-pixel interior.
                        for frame in frames:
                            body = frame.getchannel("A").getbbox()
                            head_top = 41 - row["seatAnchor"]["y"] + body[1]
                            self.assertLessEqual(25 - 22, head_top - 13)
                            # DrawTag's independent 14-character maximum plate:
                            # width <=94, height9, one-pixel outer border, y=head-12.
                            label = "WWWWWWWWWWWWWW"
                            self.assertEqual(14, len(label))
                            center = 24 - row["seatAnchor"]["x"] + (body[0] + body[2]) / 2
                            plate = (round(center - 94 / 2) - 1, head_top - 13,
                                     round(center - 94 / 2) + 95, head_top - 2)
                            monitor = (20 - 8, 4 - 22, 44 - 8, 25 - 22)
                            def overlaps(a, b):
                                return a[0] < b[2] and a[2] > b[0] and a[1] < b[3] and a[3] > b[1]
                            self.assertFalse(overlaps(monitor, plate), directory.name)
                            # Negative control: the unchanged original anchor causes
                            # the same authored monitor to collide with that plate.
                            self.assertTrue(overlaps((12, -4, 36, 17), plate), directory.name)
                        continue
                    closed_row = rows[("blink-" + name, direction)]
                    self.assertEqual(row["seatAnchor"], closed_row["seatAnchor"])
                    self.assertEqual((row["frames"], row["fps"], row["loop"]),
                                     (closed_row["frames"], closed_row["fps"], closed_row["loop"]))
                    closed = self.runtime_frames(manifest, sheet["id"], "blink-" + name, direction)
                    metadata = source.data["seated"]["rows"][name]
                    head_end = position[1] + round((metadata["sourceNeckY"] + metadata["translation"]["y"] + 1) * size[1] / 64)
                    for normal, shut in zip(frames, closed):
                        self.assertEqual(normal.getchannel("A").tobytes(), shut.getchannel("A").tobytes())
                        self.assertEqual(normal.crop((0, head_end, 32, 64)).tobytes(), shut.crop((0, head_end, 32, 64)).tobytes())
        before = self.files()
        build_pack.build_pack(self.art, self.output)
        self.assertEqual(before, self.files())

    def test_pending_seated_directories_fall_back_but_invalid_published_pairs_fail(self):
        pending = self.art / "seated" / "codex-worker-01"
        pending.mkdir(parents=True)
        self.base.image.save(pending / "codex-worker-01.png")
        metadata_only = self.art / "seated" / "codex-worker-02"
        metadata_only.mkdir()
        (metadata_only / "codex-worker-02.manifest.json").write_text("{}")
        summary = build_pack.build_pack(self.art, self.output)
        self.assertEqual(0, summary["seatedCharacters"])
        self.assertFalse(any(r["name"].startswith("seated_") for s in self.manifest()["sheets"] for r in s["rows"]))
        before = self.files()
        # A complete pair with an invalid seated row must not quietly become old typing.
        published = self.copy_seated("codex-worker-03")
        path = published / "codex-worker-03.manifest.json"
        data = json.loads(path.read_text())
        del data["frame_layout"]["rows"]["seated_left"]
        path.write_text(json.dumps(data))
        with self.assertRaisesRegex(ValueError, "codex-worker-03: invalid seated publication"):
            build_pack.build_pack(self.art, self.output)
        self.assertEqual(before, self.files())

    def test_published_blink_body_corruption_is_rejected(self):
        directory = self.copy_seated("codex-worker-01")
        path = directory / "seated-blink.png"
        with Image.open(path) as image:
            corrupted = image.convert("RGBA")
        corrupted.putpixel((16, 55), (255, 0, 0, 255))
        corrupted.save(path)
        with self.assertRaisesRegex(ValueError, "blink phase 0 changes the body/alpha"):
            build_pack.build_pack(self.art, self.output)
        self.assertFalse((self.output / "assets.json").exists())

    def test_normalized_prop_preference_and_independent_byte_identity(self):
        env = self.art / "env2"
        (env / "sheets").mkdir(parents=True)
        (env / "normalized").mkdir()
        for name in ("bookshelf", "server-rack"):
            for suffix in (".png", ".manifest.json"):
                shutil.copyfile(build_pack.ROOT / "assets/art/env2/sheets" / f"{name}{suffix}", env / "sheets" / f"{name}{suffix}")
                shutil.copyfile(build_pack.ROOT / "assets/art/env2/normalized" / f"{name}{suffix}", env / "normalized" / f"{name}{suffix}")
        # A static choice marker makes normalized/raw source preference observable.
        path = env / "normalized/bookshelf.png"
        with Image.open(path) as image:
            expected_book = image.convert("RGBA").crop((0, 0, 64, 64))
            changed = image.convert("RGBA")
        pixel = next((x, y) for y in range(64) for x in range(64) if expected_book.getpixel((x, y))[3] == 255)
        changed.putpixel(pixel, (1, 2, 3, 255))
        expected_book.putpixel(pixel, (1, 2, 3, 255))
        changed.save(path)
        # Reintroduce an RGBA body defect into the original TV input, away from its screen.
        props = build_pack.ProductionSheet(self.sheets / "props.png", self.sheets / "props.manifest.json")
        rect = props.rows["tv"][1]
        props.image.putpixel((rect["x"], rect["y"]), (99, 88, 77, 0))
        props.image.save(self.sheets / "props.png")
        with patch.object(build_pack, "normalize_sheet", wraps=build_pack.normalize_sheet) as normalize:
            build_pack.build_pack(self.art, self.output)
        seen = {call.kwargs.get("prop", call.args[1]["characterId"]) for call in normalize.call_args_list}
        self.assertTrue({"props", "desk", "server-rack"} <= seen)
        manifest = self.manifest()
        self.assertFalse({"cozy-cat", "cozy-vacuum"} & {a["id"] for a in manifest["furniture"]})
        self.assertEqual(expected_book.tobytes(), self.runtime_frames(manifest, "cozy-bookshelf", "cozy-bookshelf")[0].tobytes())
        # These published normalized files are independent expected bytes; no mask
        # is inferred from runtime changes (nor generated by the test's normalizer).
        for asset_id, relative, source_row in (("cozy-tv", "base/props", "tv"), ("cozy-server", "server-rack", "blink"),
                                               ("cozy-pantry", "base/props", "coffee_machine"), ("cozy-water", "base/props", "water_cooler")):
            source_path = build_pack.ROOT / "assets/art/env2/normalized" / f"{relative}.png"
            reference = build_pack.ProductionSheet(source_path, source_path.with_suffix(".manifest.json"))
            expected = reference.frames(source_row, (64, 64))
            actual = self.runtime_frames(manifest, asset_id, asset_id)
            self.assertEqual(len(expected), len(actual))
            for normal, frame in zip(expected, actual):
                self.assertEqual(hashlib.sha256(normal.tobytes()).digest(), hashlib.sha256(frame.tobytes()).digest())
            corrupted = actual[-1].copy()
            x, y = next((x, y) for y in range(46, 64) for x in range(64) if expected[0].getpixel((x, y))[3] == 255)
            r, g, b, a = corrupted.getpixel((x, y))
            corrupted.putpixel((x, y), ((r + 1) % 256, g, b, a))
            self.assertNotEqual(hashlib.sha256(expected[-1].tobytes()).digest(), hashlib.sha256(corrupted.tobytes()).digest())
    def test_authored_side_furniture_seats_layers_and_static_body_masks(self):
        source_dir = build_pack.ROOT / "assets/art/env3/sheets"
        target = self.art / "env3/sheets"
        target.mkdir(parents=True)
        for path in source_dir.iterdir():
            if path.suffix in (".png", ".json"):
                shutil.copyfile(path, target / path.name)
        build_pack.build_pack(self.art, self.output)
        manifest = self.manifest()
        assets = {f["id"]: f for f in manifest["furniture"]}
        for name in ("desk-side-wood", "desk-side-white", "desk-side-double-wood", "desk-side-double-white", "laptop-table-side"):
            asset = assets["cozy-" + name]
            is_desk = name.startswith("desk-")
            self.assertEqual("desk" if is_desk else "table", asset["kind"])
            self.assertEqual({90, 270}, {v["rotation"] for v in asset["variants"]})
            for direction, rotation, offset, seat_x, contact_x in (("right", 90, -26, -1, -3), ("left", 270, 26, 3, 50)):
                source = build_pack.ProductionSheet(source_dir / f"{name}-{direction}.png", source_dir / f"{name}-{direction}.manifest.json")
                for variant in (v for v in asset["variants"] if v["rotation"] == rotation):
                    self.assertEqual((3, 2, 6, 2), (variant["footprintW"], variant["footprintH"], len(variant["blockedCells"]), variant["zOrder"]))
                    self.assertEqual(("cozy-chair-side-back", "cozy-chair-side-front", {"x": 0, "y": 0}, {"x": contact_x, "y": 15}),
                                     tuple(variant[k] for k in ("chairBackId", "chairFrontId", "chairOffset", "chairSeatAnchor")))
                    self.assertEqual([{"id": "seat", "x": seat_x, "y": 1, "action": "typing", "facing": direction}] if is_desk else [], variant["points"])
                    source_state = {"default": "on", "off": "off", "working": "typing", "screensaver": "on"}[variant["state"]] if is_desk else "variants"
                    frames = self.runtime_frames(manifest, variant["sheetId"], variant["animation"])
                    expected = source.frames(source_state, (64, 64))
                    for reference, frame in zip(expected, frames):
                        self.assertEqual(reference.tobytes(), frame.crop((32, 0, 96, 64)).tobytes(), "authored profile must not be pixel-rotated")
                    if is_desk:
                        mask = Image.new("L", (64, 64))
                        screen_x = 38 if direction == "right" else 24
                        mask.paste(255, (screen_x, 26, screen_x + 2, 35))
                        if "double" in name:
                            screen_x = 32 if direction == "right" else 30
                            mask.paste(255, (screen_x, 20, screen_x + 2, 29))
                        with Image.open(build_pack.ROOT / "assets/art/env3/qa/masks" / f"{name}-{direction}.png") as published_mask:
                            self.assertEqual(mask.tobytes(), published_mask.convert("L").tobytes())
                        off = source.frames("off", (64, 64))[0]
                        for frame in frames:
                            self.assert_static_body(off, frame.crop((32, 0, 96, 64)), mask)
                        corrupted = frames[0].crop((32, 0, 96, 64))
                        x, y = next((x, y) for y in range(45, 61) for x in range(64) if off.getpixel((x, y))[3] == 255)
                        r, g, b, a = corrupted.getpixel((x, y))
                        corrupted.putpixel((x, y), ((r + 1) % 256, g, b, a))
                        with self.assertRaises(AssertionError):
                            self.assert_static_body(off, corrupted, mask)
                layers = Image.new("RGBA", (128, 64))
                for part, z_order in (("back", -2), ("front", 4)):
                    chair = next(v for v in assets[f"cozy-chair-side-{part}"]["variants"] if v["rotation"] == rotation)
                    self.assertEqual((3, 2, [], [], z_order), tuple(chair[k] for k in ("footprintW", "footprintH", "blockedCells", "points", "zOrder")))
                    layers.alpha_composite(self.runtime_frames(manifest, chair["sheetId"], chair["animation"])[0])
                with Image.open(source_dir / f"office-chair-side-{direction}.png") as original:
                    expected_chair = Image.new("RGBA", (128, 64))
                    expected_chair.paste(original.convert("RGBA"), (32 + offset, 0))
                self.assertEqual(expected_chair.tobytes(), layers.tobytes())
                # Seat floor point is beside the desk, close to chair base, facing its blocked edge.
                self.assertLessEqual(abs(seat_x * 16 + 8 - contact_x), 8)
                self.assertEqual(32, 15 + (61 - 44))
                blocked = {(3 + x, 3 + y) for y in range(2) for x in range(3)}
                walkable, queue = {(0, 0)}, deque([(0, 0)])
                while queue:
                    x, y = queue.popleft()
                    for step in ((x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1)):
                        if 0 <= step[0] < 10 and 0 <= step[1] < 8 and step not in blocked and step not in walkable:
                            walkable.add(step)
                            queue.append(step)
                self.assertIn((3 + seat_x, 4), walkable)
                self.assertIn((3 + seat_x + (1 if direction == "right" else -1), 4), blocked)

    def assert_desk_states(self, manifest, screen_bounds):
        sheet = next(s for s in manifest["sheets"] if s["id"] == "cozy-desk")
        asset = next(f for f in manifest["furniture"] if f["id"] == "cozy-desk")
        variants = {v["state"]: v for v in asset["variants"] if v["rotation"] == 0}
        self.assertEqual(16, len(asset["variants"]))
        self.assertEqual({"default", "off", "working", "screensaver"}, set(variants))
        geometry = {key: value for key, value in variants["default"].items() if key not in ("state", "animation")}
        self.assertEqual((3, 2, 6), (geometry["footprintW"], geometry["footprintH"], len(geometry["blockedCells"])))
        self.assertEqual([{"id": "seat", "x": 1, "y": 2, "action": "typing", "facing": "up"}], geometry["points"])
        cell, anchor = 64, {"x": 8, "y": 22} if "chairBackId" in geometry else {"x": 8, "y": 8}
        self.assertEqual((cell, cell, 1, anchor),
                         (sheet["cellW"], sheet["cellH"], sheet["scale"], sheet["anchor"]))
        rows = {r["name"]: r for r in sheet["rows"]}
        with Image.open(self.output / sheet["file"]) as image:
            off = image.crop((0, 0, cell, cell))
            working = []
            for state, variant in variants.items():
                self.assertEqual(geometry, {k: v for k, v in variant.items() if k not in ("state", "animation")})
                row = rows[variant["animation"]]
                self.assertEqual("any", row["direction"])
                self.assertEqual((3, 3, True) if state == "working" else (1, 1, False),
                                 (row["frames"], row["fps"], row["loop"]))
                for column in range(row["column"], row["column"] + row["frames"]):
                    frame = image.crop((column * cell, row["row"] * cell, (column + 1) * cell, (row["row"] + 1) * cell))
                    self.assertEqual(off.getchannel("A").tobytes(), frame.getchannel("A").tobytes())
                    changed = ImageChops.difference(off.convert("RGB"), frame.convert("RGB")).getbbox()
                    if state in ("default", "off"):
                        self.assertIsNone(changed)
                    else:
                        self.assertIsNotNone(changed)
                        x, y, right, bottom = screen_bounds
                        self.assertTrue(changed[0] >= x and changed[1] >= y and changed[2] <= right + 1 and changed[3] <= bottom + 1)
                    if state == "working":
                        working.append(frame.tobytes())
            self.assertEqual(3, len(set(working)))

    def assert_furniture_rotations(self, manifest):
        sheets = {s["id"]: s for s in manifest["sheets"]}
        facings = ("up", "right", "down", "left")
        for asset in manifest["furniture"]:
            if asset["id"].startswith(("cozy-desk-side-", "cozy-chair-side-", "cozy-laptop-table-side")):
                continue  # These have only authored profile variants, checked separately.
            variants = {(v["rotation"], v["state"]): v for v in asset["variants"]}
            bases = [v for v in asset["variants"] if v["rotation"] == 0]
            self.assertEqual(len(variants), len(asset["variants"]))
            self.assertEqual(4 * len(bases), len(variants), asset["id"])
            for base in bases:
                width, height = base["footprintW"], base["footprintH"]
                transforms = {90: lambda x, y: (height - 1 - y, x),
                              180: lambda x, y: (width - 1 - x, height - 1 - y),
                              270: lambda x, y: (y, width - 1 - x)}
                sheet = sheets[base["sheetId"]]
                rows = {r["name"]: r for r in sheet["rows"]}
                original = rows[base["animation"]]
                cell = sheet["cellW"]
                ax, ay = sheet["anchor"]["x"], sheet["anchor"]["y"]
                anchors = {90: (cell - ay - height * 16, ax),
                           180: (cell - ax - width * 16, cell - ay - height * 16),
                           270: (ay, cell - ax - width * 16)}
                with Image.open(self.output / sheet["file"]) as image:
                    for rotation, transform in transforms.items():
                        variant = variants[(rotation, base["state"])]
                        self.assertEqual((height, width) if rotation in (90, 270) else (width, height),
                                         (variant["footprintW"], variant["footprintH"]))
                        self.assertEqual({transform(t["x"], t["y"]) for t in base["blockedCells"]},
                                         {(t["x"], t["y"]) for t in variant["blockedCells"]})
                        self.assertEqual([dict(p, x=transform(p["x"], p["y"])[0], y=transform(p["x"], p["y"])[1],
                                               facing=facings[(facings.index(p["facing"]) + rotation // 90) % 4]) for p in base["points"]],
                                         variant["points"])
                        z_order = base["zOrder"]
                        if "chairSeatAnchor" in base:
                            z_order = {90: -32, 180: -48, 270: -32}[rotation]
                            self.assertEqual({90: {"x": -9, "y": 24}, 180: {"x": 24, "y": -9},
                                              270: {"x": 41, "y": 24}}[rotation], variant["chairSeatAnchor"])
                        elif asset["id"] == "cozy-chair-back":
                            z_order = {90: -32, 180: -48, 270: -32}[rotation]
                        elif asset["id"] == "cozy-chair-front":
                            z_order = {90: -14, 180: -30, 270: -14}[rotation]
                        self.assertEqual((base["sheetId"], base["layer"], z_order),
                                         (variant["sheetId"], variant["layer"], variant["zOrder"]))
                        row = rows[variant["animation"]]
                        self.assertEqual({"x": ax - anchors[rotation][0], "y": ay - anchors[rotation][1]}, row["footOffset"])
                        self.assertEqual((original["frames"], original["fps"], original["loop"], "any"),
                                         (row["frames"], row["fps"], row["loop"], row["direction"]))
                        for frame in range(original["frames"]):
                            x, y = (original["column"] + frame) * cell, original["row"] * cell
                            source = image.crop((x, y, x + cell, y + cell))
                            x, y = (row["column"] + frame) * cell, row["row"] * cell
                            actual = image.crop((x, y, x + cell, y + cell))
                            transpose = {90: Image.Transpose.ROTATE_270, 180: Image.Transpose.ROTATE_180,
                                         270: Image.Transpose.ROTATE_90}[rotation]
                            self.assertEqual(source.transpose(transpose).tobytes(), actual.tobytes(),
                                             f"{asset['id']}/{base['state']}/{rotation}/{frame}")

    def test_contract_and_partial_run_exclusion(self):
        self.roster("complete")
        partial = self.base.image.copy()
        partial.paste((0, 0, 0, 0), (32, 768, 64, 832))
        self.roster("empty-last-frame", image=partial)
        missing = deepcopy(self.base.data)
        del missing["frame_layout"]["rows"]["talk"]
        self.roster("missing-action", metadata=missing)
        # A completed top-level final remains eligible even if its pipeline run
        # is partial; only the validated final atlas enters the runtime pack.
        run = self.roster("final-alias", final_alias=True)
        partial.save(run / "sprite-sheet-alpha.png")

        env = self.art / "env2" / "sheets"
        env.mkdir(parents=True)
        for name in ("desks", "monitors", "office-chairs"):
            for suffix in (".png", ".manifest.json"):
                shutil.copyfile(build_pack.ROOT / "assets" / "art" / "env2" / "sheets" / f"{name}{suffix}",
                                env / f"{name}{suffix}")
        summary = build_pack.build_pack(self.art, self.output)
        self.assertEqual(38, summary["characters"])
        self.assertEqual({"empty-last-frame", "missing-action"}, {s["id"] for s in summary["skipped"]})
        manifest = self.manifest()
        sheets = {s["id"]: s for s in manifest["sheets"]}
        self.assertLessEqual(len(sheets), 128)
        for character in manifest["characters"]:
            self.assertEqual({"id", "sheetId"}, set(character))
            sheet = sheets[character["sheetId"]]
            self.assertEqual((32, 64, 1, {"x": 16, "y": 60}),
                             (sheet["cellW"], sheet["cellH"], sheet["scale"], sheet["anchor"]))
            idle = [r for r in sheet["rows"] if r["name"] == "idle"]
            self.assertEqual(["down", "left", "right", "up"], [r["direction"] for r in idle])
            self.assertTrue(all(r["frames"] == 1 and not r["loop"] for r in idle))
            for source_name, _, action, direction in build_pack.ACTIONS[1:]:
                row = next(r for r in sheet["rows"] if (r["name"], r["direction"]) == (action, direction))
                source = self.base.animation[source_name]
                self.assertEqual((source["fps"], source["loop"]), (row["fps"], row["loop"]))
        furniture = {f["id"]: f["variants"][0] for f in manifest["furniture"]}
        required = {"desk", "pantry", "water", "tv", "sofa", "table", "plant", "bookshelf", "cabinet",
                    "server", "meeting", "armchair", "rug", "picture", "whiteboard", "clock", "window", "door"}
        self.assertTrue({f"cozy-{name}" for name in required} <= furniture.keys())
        desk = furniture["cozy-desk"]
        self.assertEqual((3, 2, 6), (desk["footprintW"], desk["footprintH"], len(desk["blockedCells"])))
        self.assertEqual({"id": "seat", "x": 1, "y": 2, "action": "typing", "facing": "up"}, desk["points"][0])
        self.assert_desk_states(manifest, (23, 9, 40, 16))
        self.assert_furniture_rotations(manifest)
        composite = Image.new("RGBA", (64, 80))
        for sheet_id in ("cozy-chair-back", "cozy-desk", "cozy-chair-front"):
            with Image.open(self.output / sheets[sheet_id]["file"]) as image:
                cell = sheets[sheet_id]["cellW"]
                composite.alpha_composite(image.crop((0, 0, cell, cell)), (0, 0) if sheet_id == "cozy-desk" else (0, 14))
        box = composite.getchannel("A").getbbox()
        self.assertTrue(all(abs(a - b) <= 1 for a, b in zip(box, (8, 4, 56, 72))))
        with Image.open(self.output / sheets["cozy-desk"]["file"]) as image:
            frame = image.crop((0, 0, 64, 64))
            self.assertIsNone(frame.crop((0, 54, 64, 64)).getchannel("A").getbbox())
            # The tabletop is byte-identical to the original env2 crop/resize,
            # only padded: world y=(30-22)=8, previously (16-8)=8.
            source = build_pack.ProductionSheet(env / "desks.png", env / "desks.manifest.json").frames("variants", (64, 64))[0]
            expected = source.crop(source.getchannel("A").getbbox()).resize((48, 24), Image.Resampling.NEAREST)
            self.assertEqual(expected.tobytes(), frame.crop((8, 30, 56, 54)).tobytes())
            source = build_pack.ProductionSheet(env / "monitors.png", env / "monitors.manifest.json").frames("variants", (64, 64))[0]
            expected_monitor = source.crop(source.getchannel("A").getbbox()).resize((24, 21), Image.Resampling.NEAREST)
            self.assertEqual(expected_monitor.getchannel("A").tobytes(), frame.crop((20, 4, 44, 25)).getchannel("A").tobytes())
            screen_mask = Image.new("L", (24, 21))
            screen_mask.paste(255, (3, 5, 21, 13))
            stem_color = expected_monitor.getpixel((12, 20))
            self.assertEqual(255, stem_color[3])
            for state in ("cozy-desk-off", "cozy-desk-working", "cozy-desk-screensaver"):
                for runtime in self.runtime_frames(manifest, "cozy-desk", state):
                    self.assert_static_body(expected_monitor, runtime.crop((20, 4, 44, 25)), screen_mask)
                    for y in range(25, 30):
                        for x in (31, 32):
                            self.assertEqual(stem_color, runtime.getpixel((x, y)))
        self.assertEqual(("cozy-chair-back", "cozy-chair-front", {"x": 0, "y": 0}, {"x": 24, "y": 41}, 0),
                         tuple(desk[k] for k in ("chairBackId", "chairFrontId", "chairOffset", "chairSeatAnchor", "zOrder")))
        self.assertEqual(([], [], 0), tuple(furniture["cozy-chair-back"][k] for k in ("blockedCells", "points", "zOrder")))
        chair = furniture["cozy-chair-front"]
        self.assertEqual(([], 18, {"x": 8, "y": 8}),
                         (chair["blockedCells"], chair["zOrder"], sheets["cozy-chair-front"]["anchor"]))
        expected_points = {
            "cozy-pantry": [(1, 2, "coffee", "up")],
            "cozy-water": [(0, 2, "water", "up")],
            "cozy-tv": [(1, 3, "tv", "up")],
            "cozy-sofa": [(1, 2, "napping", "down")],
            "cozy-table": [(0, 2, "chatting", "right"), (2, 2, "chatting", "left")],
        }
        for asset_id, expected in expected_points.items():
            self.assertEqual(expected, [(p["x"], p["y"], p["action"], p["facing"]) for p in furniture[asset_id]["points"]])
        for name in ("rug", "picture", "whiteboard", "clock", "window", "door"):
            self.assertEqual([], furniture[f"cozy-{name}"]["blockedCells"])
            self.assertEqual(0 if name == "rug" else 2, furniture[f"cozy-{name}"]["layer"])
        for asset_id, variant in furniture.items():
            if asset_id in ("cozy-desk", "cozy-chair-back", "cozy-chair-front", "cozy-laptop"):
                continue
            sheet = sheets[variant["sheetId"]]
            with Image.open(self.output / sheet["file"]) as image:
                cw, ch = sheet["cellW"], sheet["cellH"]
                frames = [image.crop((column * cw, 0, (column + 1) * cw, ch))
                          for column in range(image.width // cw)]
            self.assertEqual(build_pack.furniture_anchor(frames, (variant["footprintW"], variant["footprintH"])),
                             sheet["anchor"])
        tiles = {t["id"]: t for t in manifest["tileTypes"]}
        self.assertEqual({0, 1, 2, 3, 5, 6, 7, 8, 10, 11, 12, 13, 14, 15, 16, 17, 18, 255}, set(tiles))
        for tile_id, animation in ((5, "partition"), (7, "wood-wall"), (8, "wood-baseboard")):
            self.assertEqual({"id": tile_id, "kind": "wall", "color": "#252b48",
                              "sheetId": "room-walls", "animation": animation}, tiles[tile_id])
        source_tiles = build_pack.ProductionSheet(self.sheets / "tiles.png", self.sheets / "tiles.manifest.json")
        with Image.open(self.output / sheets["tiles"]["file"]) as image:
            self.assertEqual((255, 255), image.getchannel("A").getextrema())
            plaster = source_tiles.frames("wall_office", (32, 32))[0].crop((0, 0, 32, 26))
            self.assertEqual(plaster.resize((32, 32), Image.Resampling.NEAREST).tobytes(),
                             image.crop((0, 0, 32, 32)).tobytes())
        floor_sheet = sheets["floor-tiles"]
        self.assertEqual((16, 16, 1, {"x": 0, "y": 0}),
                         (floor_sheet["cellW"], floor_sheet["cellH"], floor_sheet["scale"], floor_sheet["anchor"]))
        floor_clips = {row["name"]: row for row in floor_sheet["rows"]}
        with Image.open(self.output / floor_sheet["file"]) as image:
            self.assertEqual((255, 255), image.getchannel("A").getextrema())
            for name, tile_ids, source_name in (("wood", (1, 10, 11, 12), "floor_pantry"),
                                                ("checker", (2, 13, 14, 15), None),
                                                ("carpet", (3, 16, 17, 18), "floor_office")):
                rebuilt = Image.new("RGBA", (32, 32))
                for quarter, tile_id in enumerate(tile_ids):
                    tile = tiles[tile_id]
                    self.assertEqual(("floor", "floor-tiles", f"{name}-{quarter}"),
                                     (tile["kind"], tile["sheetId"], tile["animation"]))
                    row = floor_clips[tile["animation"]]
                    self.assertEqual((1, 1, False), (row["frames"], row["fps"], row["loop"]))
                    x, y = row["column"] * 16, row["row"] * 16
                    rebuilt.paste(image.crop((x, y, x + 16, y + 16)),
                                  ((quarter % 2) * 16, (quarter // 2) * 16))
                if source_name:
                    # The p5 source has near-opaque alpha; retain the existing
                    # plaster-backed floor normalization, but never resample.
                    plaster = source_tiles.frames("wall_office", (32, 32))[0].getpixel((16, 0))
                    expected = Image.new("RGBA", (32, 32), plaster)
                    expected.alpha_composite(source_tiles.frames(source_name, (32, 32))[0])
                    self.assertEqual(expected.tobytes(), rebuilt.tobytes())
                else:
                    self.assertNotEqual(rebuilt.getpixel((0, 0)), rebuilt.getpixel((8, 0)))
                    self.assertEqual(rebuilt.getpixel((0, 0)), rebuilt.getpixel((8, 8)))
                    for y in range(32):
                        for x in range(32):
                            self.assertEqual(rebuilt.getpixel(((x // 8) * 8, (y // 8) * 8)), rebuilt.getpixel((x, y)))
        self.assertEqual({"bubble-working", "bubble-thinking", "bubble-waiting", "bubble-done", "bubble-napping",
                          "bubble-reaction", "role-supervisor", "role-worker", "report-item"},
                         {r["name"] for r in sheets["icons"]["rows"]})
        self.assertEqual((32, 32, 1, {"x": 0, "y": 0}),
                         (sheets["icons"]["cellW"], sheets["icons"]["cellH"], sheets["icons"]["scale"], sheets["icons"]["anchor"]))

    def test_original_provider_icons_laptop_and_fallback_desk_states(self):
        build_pack.build_pack(self.art, self.output)
        manifest = self.manifest()
        sheets = {s["id"]: s for s in manifest["sheets"]}
        self.assert_desk_states(manifest, (23, 27, 39, 33))
        self.assert_furniture_rotations(manifest)
        providers = sheets["provider-icons"]
        self.assertEqual((12, 12, 1, {"x": 0, "y": 0}),
                         (providers["cellW"], providers["cellH"], providers["scale"], providers["anchor"]))
        self.assertEqual({"codex", "claude", "antigravity"}, {r["name"] for r in providers["rows"]})
        glyphs = []
        with Image.open(self.output / providers["file"]) as image:
            self.assertEqual((36, 12), image.size)
            for row in providers["rows"]:
                self.assertEqual(("any", 1, 1, False), (row["direction"], row["frames"], row["fps"], row["loop"]))
                x, y = row["column"] * 12, row["row"] * 12
                glyph = image.crop((x, y, x + 12, y + 12))
                pixels = set(glyph.get_flattened_data())
                colours = {p[:3] for p in pixels if p[3]}
                self.assertEqual({0, 255}, {p[3] for p in pixels})
                self.assertEqual(2, len(colours))
                self.assertIn((32, 37, 43), colours)
                glyphs.append(glyph.getchannel("A").tobytes())
        self.assertEqual(3, len(set(glyphs)))
        extras = sheets["office-extras"]
        self.assertEqual((16, 16, 1, {"x": 0, "y": 0}),
                         (extras["cellW"], extras["cellH"], extras["scale"], extras["anchor"]))
        self.assertEqual(("laptop", "any", 0, 0, 1, 1, False),
                         tuple(extras["rows"][0][k] for k in ("name", "direction", "row", "column", "frames", "fps", "loop")))
        self.assertEqual({"laptop", "laptop-r90", "laptop-r180", "laptop-r270"}, {r["name"] for r in extras["rows"]})
        with Image.open(self.output / extras["file"]) as image:
            self.assertEqual((16, 64), image.size)
            self.assertEqual((1, 3, 15, 13), image.crop((0, 0, 16, 16)).getchannel("A").getbbox())
            self.assertEqual({0, 255}, {p[3] for p in image.get_flattened_data()})
        laptop = next(f for f in manifest["furniture"] if f["id"] == "cozy-laptop")["variants"][0]
        self.assertEqual(("office-extras", "laptop", 1, 1, [], [], 1),
                         (laptop["sheetId"], laptop["animation"], laptop["footprintW"], laptop["footprintH"],
                          laptop["blockedCells"], laptop["points"], laptop["layer"]))
        stool_sheet = sheets["cozy-counter-stool"]
        self.assertEqual((32, 32, 1, {"x": 8, "y": 14}),
                         (stool_sheet["cellW"], stool_sheet["cellH"], stool_sheet["scale"], stool_sheet["anchor"]))
        stool = next(f for f in manifest["furniture"] if f["id"] == "cozy-counter-stool")
        self.assertEqual("chair", stool["kind"])
        self.assertEqual({0, 90, 180, 270}, {v["rotation"] for v in stool["variants"]})
        for variant in stool["variants"]:
            self.assertEqual((1, 1, [], [], 1),
                             (variant["footprintW"], variant["footprintH"], variant["blockedCells"],
                              variant["points"], variant["layer"]))
        with Image.open(self.output / stool_sheet["file"]) as image:
            self.assertEqual((32, 128), image.size)
            box = image.crop((0, 0, 32, 32)).getchannel("A").getbbox()
            self.assertEqual((8, 7, 24, 30), box)
            # Bottom-centre feet land at (8,16), the owning tile's floor point.
            self.assertEqual((8, 16), ((box[0] + box[2]) // 2 - stool_sheet["anchor"]["x"],
                                      box[3] - stool_sheet["anchor"]["y"]))

    def test_deterministic_rerun_and_owned_stale_cleanup(self):
        unique = self.base.image.copy()
        unique.putpixel((16, 20), (120, 23, 101, 255))
        run = self.roster("temporary-design", image=unique)
        build_pack.build_pack(self.art, self.output)
        manifest = self.manifest()
        unique_file = next(s["file"] for s in manifest["sheets"] if s["id"] == "char-temporary-design")
        foreign = self.output / "sheets" / "foreign.png"
        foreign.write_bytes(b"preserve another author's file")
        before = self.files()
        build_pack.build_pack(self.art, self.output)
        self.assertEqual(before, self.files())
        (run / "sprite-sheet-alpha.png").unlink()
        summary = build_pack.build_pack(self.art, self.output)
        self.assertEqual(1, summary["removedStale"])
        self.assertFalse((self.output / unique_file).exists())
        self.assertEqual(b"preserve another author's file", foreign.read_bytes())
        self.assertEqual({"assets.json", "sheets"}, {p.name for p in self.output.iterdir()})
        self.assertFalse(list(self.output.rglob("*.tmp")))

    def test_failed_manifest_replace_preserves_published_pack(self):
        build_pack.build_pack(self.art, self.output)
        manifest_path = self.output / "assets.json"
        before = manifest_path.read_bytes()
        old_files = [self.output / s["file"] for s in self.manifest()["sheets"]]
        unique = self.base.image.copy()
        unique.putpixel((16, 20), (91, 47, 111, 255))
        self.roster("new-design", image=unique)
        replace = build_pack.os.replace

        def fail_manifest(source, target):
            if Path(target).name == "assets.json":
                raise OSError("isolated manifest replacement failure")
            return replace(source, target)

        with patch.object(build_pack.os, "replace", side_effect=fail_manifest):
            with self.assertRaisesRegex(OSError, "isolated manifest"):
                build_pack.build_pack(self.art, self.output)
        self.assertEqual(before, manifest_path.read_bytes())
        self.assertTrue(all(p.is_file() for p in old_files))
        self.assertFalse(list(self.output.rglob("*.tmp")))
        build_pack.build_pack(self.art, self.output)
        self.assertEqual(37, len(self.manifest()["characters"]))


if __name__ == "__main__":
    unittest.main()
