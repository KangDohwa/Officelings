"""Small isolated runtime-pack checks; run with python -B -m unittest discover -s tools."""

from copy import deepcopy
import hashlib
import json
from pathlib import Path
import shutil
import tempfile
import unittest
from unittest.mock import patch

from PIL import Image

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
        self.assertEqual({"id": "seat", "x": 1, "y": 2, "action": "typing", "facing": "right"}, desk["points"][0])
        self.assertEqual({"x": 8, "y": 28}, sheets["cozy-desk"]["anchor"])
        with Image.open(self.output / sheets["cozy-desk"]["file"]) as image:
            box = image.getchannel("A").getbbox()
            self.assertTrue(all(abs(a - b) <= 1 for a, b in zip(box, (12, 25, 52, 60))))
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
            if asset_id == "cozy-desk":
                continue
            sheet = sheets[variant["sheetId"]]
            with Image.open(self.output / sheet["file"]) as image:
                frames = [image.crop((column * 64, 0, column * 64 + 64, 64))
                          for column in range(image.width // 64)]
            self.assertEqual(build_pack.furniture_anchor(frames, (variant["footprintW"], variant["footprintH"])),
                             sheet["anchor"])
        tiles = {t["id"]: t for t in manifest["tileTypes"]}
        self.assertEqual({0, 1, 2, 3, 5, 6, 10, 11, 12, 13, 14, 15, 16, 17, 18, 255}, set(tiles))
        self.assertEqual({"id": 5, "kind": "wall", "color": "#674638"}, tiles[5])
        with Image.open(self.output / sheets["tiles"]["file"]) as image:
            self.assertEqual((255, 255), image.getchannel("A").getextrema())
            self.assertEqual(set(image.crop((0, 0, 32, 22)).get_flattened_data()),
                             set(image.crop((0, 22, 32, 32)).get_flattened_data()))
        floor_sheet = sheets["floor-tiles"]
        self.assertEqual((16, 16, 1, {"x": 0, "y": 0}),
                         (floor_sheet["cellW"], floor_sheet["cellH"], floor_sheet["scale"], floor_sheet["anchor"]))
        floor_clips = {row["name"]: row for row in floor_sheet["rows"]}
        source_tiles = build_pack.ProductionSheet(self.sheets / "tiles.png", self.sheets / "tiles.manifest.json")
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
