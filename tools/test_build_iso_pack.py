"""Synthetic adapter cases and an end-to-end build from approved lane sources."""
from copy import deepcopy
import hashlib
import json
from pathlib import Path
import tempfile
import unittest

from PIL import Image

import build_iso_pack as pack


def clip(name="idle", direction="se", row=0, frames=4):
    return dict(name=name, direction=direction, row=row, frames=frames, fps=6, loop=True)


class AliasTests(unittest.TestCase):
    def setUp(self):
        self.sheet = dict(id="mini", rows=[dict(clip(), seatAnchor=dict(x=16, y=44))], aliases=[])

    def test_chain_preserves_seat_phase_and_bindings(self):
        self.sheet["aliases"] = [dict(name="rest", direction="se", target=dict(name="idle", direction="se"), frame=2),
                                 dict(name="waiting", direction="se", target=dict(name="rest", direction="se"), effect="waiting-bubble")]
        resolved = pack.expand_aliases(self.sheet)[-1]
        self.assertEqual((resolved["row"], resolved["column"], resolved["frames"], resolved["loop"]), (0, 2, 1, False))
        self.assertEqual(resolved["seatAnchor"], dict(x=16, y=44))
        self.assertEqual(resolved["effect"], "waiting-bubble")
        self.assertEqual(resolved["sourceClip"], dict(name="idle", direction="se"))

    def test_transfer_timing_survives_idle_fallback(self):
        self.sheet["aliases"] = [dict(name="pickup", direction="se", target=dict(name="idle", direction="se"), frame=0, durationMs=667, prop="report-paper")]
        resolved = pack.expand_aliases(self.sheet)[-1]
        self.assertEqual((resolved["durationMs"], resolved["prop"], resolved["frames"]), (667, "report-paper", 1))

    def test_cycle_missing_target_and_facing_change_fail(self):
        for target in (dict(name="a", direction="se"), dict(name="missing", direction="se"), dict(name="idle", direction="sw")):
            with self.subTest(target=target):
                self.sheet["aliases"] = [dict(name="a", direction="se", target=target)]
                with self.assertRaises(ValueError):
                    pack.expand_aliases(self.sheet)

    def test_duplicate_alias_and_invalid_phase_fail(self):
        for alias in (dict(name="idle", direction="se", target=dict(name="idle", direction="se")),
                      dict(name="held", direction="se", target=dict(name="idle", direction="se"), frame=4)):
            self.sheet["aliases"] = [alias]
            with self.assertRaises(ValueError):
                pack.expand_aliases(self.sheet)


class MiniSourceTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        image = Image.new("RGBA", (128, 32))
        for phase in range(4):
            image.putpixel((phase * 32 + 16, 16), (17, 25, 36, 255))
        image.save(self.root / "mini.png")
        self.source = dict(version=1, projection="iso2to1", sheets=[dict(id="mini-prop", kind="prop", file="mini.png",
            cellW=32, cellH=32, anchor=dict(x=16, y=16), scale=1, projection="iso2to1",
            rows=[dict(clip(), anchorChecks=[dict(x=16, y=16, radius=0)])])])

    def write(self, name="mini.iso.json"):
        path = self.root / name
        path.write_text(json.dumps(self.source), encoding="utf-8")
        return path

    def test_synthetic_source_hashes_and_pixels_are_unchanged(self):
        path = self.write()
        checks = pack.check_sources([path])
        self.assertEqual(checks["fail"], 0)
        manifest, files = pack.assemble([path])
        sheet = manifest["sheets"][0]
        original = (self.root / "mini.png").read_bytes()
        self.assertEqual(files[sheet["file"]], original)
        self.assertEqual(sheet["sha256"], hashlib.sha256(original).hexdigest())
        self.assertEqual(sheet["anchor"], dict(x=16, y=16))
        self.assertEqual(sheet["file"], f"sheets/pack-{sheet['sha256']}.png")

    def test_path_escape_absolute_path_and_url_fail(self):
        for path in ("../outside.png", "C:/outside.png", "https://example.com/art.png", "//host/art.png"):
            with self.subTest(path=path), self.assertRaises(ValueError):
                pack.local_file(self.root, path)

    def test_duplicate_sheet_and_out_of_bounds_clip_fail(self):
        a, b = self.write(), self.write("second.iso.json")
        with self.assertRaisesRegex(ValueError, "duplicate sheet"):
            pack.assemble([a, b])
        self.source["sheets"][0]["rows"][0]["row"] = 1
        with self.assertRaisesRegex(ValueError, "out of bounds"):
            pack.assemble([self.write()])

    def test_missing_reference_and_nonunit_scale_fail(self):
        self.source["characters"] = [dict(id="missing", sheetId="absent")]
        with self.assertRaisesRegex(ValueError, "missing sheet"):
            pack.assemble([self.write()])
        self.source.pop("characters")
        self.source["sheets"][0]["scale"] = 0.5
        with self.assertRaisesRegex(ValueError, "projection/scale"):
            pack.assemble([self.write()])


class RealPackTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.temp = tempfile.TemporaryDirectory()
        cls.addClassCleanup(cls.temp.cleanup)
        cls.output = Path(cls.temp.name)
        cls.summary = pack.build_iso_pack(output=cls.output, pilot=True)
        cls.manifest = json.loads((cls.output / "assets.json").read_text(encoding="utf-8"))
        cls.layout = json.loads((cls.output / "layout.json").read_text(encoding="utf-8"))
        cls.sheets = {s["id"]: s for s in cls.manifest["sheets"]}

    def test_all_files_hashes_and_clips_resolve(self):
        pack.verify_references(self.manifest)
        for sheet in self.sheets.values():
            data = (self.output / sheet["file"]).read_bytes()
            self.assertEqual(hashlib.sha256(data).hexdigest(), sheet["sha256"])
            pack.validate_clips(sheet)
            for mask in sheet.get("faceLock", {}).values():
                data = (self.output / mask["eyeMask"]).read_bytes()
                self.assertEqual(hashlib.sha256(data).hexdigest(), mask["sha256"])

    def test_character_rows_and_resolved_waiting_blinks(self):
        for character in self.manifest["characters"]:
            normal = self.sheets[character["sheetId"]]
            blink = self.sheets[normal["blink"]["sheetId"]]
            self.assertEqual((len(normal["rows"]), len(blink["rows"])), (38, 24))
            self.assertEqual((normal["width"], normal["height"], normal["anchor"], normal["scale"]), (128, 2432, dict(x=16, y=60), 1))
            for facing in pack.FACINGS:
                waiting = next(c for c in normal["clips"] if (c["name"], c["direction"]) == ("waiting", facing))
                self.assertEqual((waiting["frames"], waiting["loop"], waiting["seatAnchor"]), (1, False, dict(x=16, y=44)))
                self.assertEqual("blink" in waiting, facing in ("se", "sw"))

    def test_all_furniture_orientations_and_spec_footprints(self):
        footprints = {"desk": (1, 2), "chair": (1, 1), "sofa": (1, 3), "plant": (1, 1), "shelf": (1, 2),
                      "tv-cabinet": (1, 2), "coffee-machine": (1, 1), "fridge": (1, 1), "water-cooler": (1, 1),
                      "rug": (2, 3), "side-table": (1, 1), "floor-lamp": (1, 1)}
        self.assertEqual(len(self.manifest["furniture"]), 12)
        for family in self.manifest["furniture"]:
            self.assertEqual({v["orientation"] for v in family["variants"]}, set(pack.FACINGS))
            base = footprints[family["id"].removeprefix("iso/")]
            for v in family["variants"]:
                expected = base[::-1] if v["rotation"] in (90, 270) else base
                self.assertEqual((v["footprintW"], v["footprintH"]), expected)
                for part in v["parts"]:
                    sheet = self.sheets[part["sheetId"]]
                    clip_data = next(c for c in sheet["rows"] if (c["name"], c["direction"]) == (part["animation"], part["direction"]))
                    self.assertEqual(part["anchor"], clip_data.get("anchor", sheet["anchor"]))
                    self.assertEqual(part["anchorChecks"], clip_data["anchorChecks"])

    def test_exact_layout_and_reachable_seats(self):
        self.assertEqual((self.layout["widthTiles"], self.layout["heightTiles"], len(self.layout["tiles"])), (10, 10, 100))
        self.assertEqual(self.layout["door"], dict(x=0, y=4))
        self.assertEqual(len(self.layout["furniture"]), 16)
        for actual, expected in zip(self.layout["furniture"], pack.PLACEMENTS):
            self.assertEqual((actual["id"], actual["assetId"].removeprefix("iso/"), actual["x"], actual["y"], actual["orientation"]), expected)
        for j in range(10):
            for i in range(10):
                expected = 2 + (i + j) % 2 if i in (8, 9) and j <= 5 else 1
                self.assertEqual(self.layout["tiles"][j * 10 + i], expected)
        self.assertEqual(pack.validate_layout(self.layout, self.manifest)["reachableApproaches"], 7)
        self.assertEqual(len(self.layout["wallSegments"]), 20)


if __name__ == "__main__":
    unittest.main()
