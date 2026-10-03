"""Small isolated runtime-pack checks; run with python -B -m unittest discover -s tools."""

from copy import deepcopy
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

    def assert_desk_states(self, manifest, screen_bounds):
        sheet = next(s for s in manifest["sheets"] if s["id"] == "cozy-desk")
        asset = next(f for f in manifest["furniture"] if f["id"] == "cozy-desk")
        variants = {v["state"]: v for v in asset["variants"] if v["rotation"] == 0}
        self.assertEqual(16, len(asset["variants"]))
        self.assertEqual({"default", "off", "working", "screensaver"}, set(variants))
        geometry = {key: value for key, value in variants["default"].items() if key not in ("state", "animation")}
        self.assertEqual((3, 2, 6), (geometry["footprintW"], geometry["footprintH"], len(geometry["blockedCells"])))
        self.assertEqual([{"id": "seat", "x": 1, "y": 2, "action": "typing", "facing": "up"}], geometry["points"])
        self.assertEqual((64, 64, 1, {"x": 8, "y": 8}),
                         (sheet["cellW"], sheet["cellH"], sheet["scale"], sheet["anchor"]))
        rows = {r["name"]: r for r in sheet["rows"]}
        with Image.open(self.output / sheet["file"]) as image:
            off = image.crop((0, 0, 64, 64))
            working = []
            for state, variant in variants.items():
                self.assertEqual(geometry, {k: v for k, v in variant.items() if k not in ("state", "animation")})
                row = rows[variant["animation"]]
                self.assertEqual("any", row["direction"])
                self.assertEqual((3, 3, True) if state == "working" else (1, 1, False),
                                 (row["frames"], row["fps"], row["loop"]))
                for column in range(row["column"], row["column"] + row["frames"]):
                    frame = image.crop((column * 64, row["row"] * 64, (column + 1) * 64, (row["row"] + 1) * 64))
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
                        self.assertEqual((base["sheetId"], base["layer"], base["zOrder"]),
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
        with Image.open(self.output / sheets["cozy-desk"]["file"]) as image:
            box = image.crop((0, 0, 64, 64)).getchannel("A").getbbox()
            self.assertTrue(all(abs(a - b) <= 1 for a, b in zip(box, (8, 4, 56, 58))))
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
            if asset_id in ("cozy-desk", "cozy-chair-front", "cozy-laptop"):
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
