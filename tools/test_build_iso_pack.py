"""Synthetic adapter cases and an end-to-end build from approved lane sources."""
import hashlib
import json
from pathlib import Path
import tempfile
import unittest

from PIL import Image

import build_iso_pack as pack
import iso_check

TEMP_ROOT = pack.ROOT / "artifacts/iso-pack-tests"


def temporary_directory():
    TEMP_ROOT.mkdir(parents=True, exist_ok=True)
    return tempfile.TemporaryDirectory(dir=TEMP_ROOT)


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
        self.temp = temporary_directory()
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

    def test_source_and_flat_output_directories_are_rejected(self):
        for directory in (pack.ROOT / "assets/pack", pack.ART / "env"):
            with self.subTest(directory=directory), self.assertRaises(ValueError):
                pack.build_iso_pack(output=directory)

    def test_interlaced_and_non_rgba_pngs_are_rejected(self):
        original = (self.root / "mini.png").read_bytes()
        mutated = bytearray(original)
        mutated[28] = 1
        with self.assertRaisesRegex(ValueError, "non-interlaced"):
            pack.png_info(mutated)
        image = Image.new("RGB", (32, 32))
        image.save(self.root / "rgb.png")
        with self.assertRaisesRegex(ValueError, "RGBA"):
            pack.png_info((self.root / "rgb.png").read_bytes())


class RealPackTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.temp = temporary_directory()
        cls.addClassCleanup(cls.temp.cleanup)
        cls.output = Path(cls.temp.name)
        cls.flat_before = {p: hashlib.sha256(p.read_bytes()).hexdigest() for p in (pack.ROOT / "assets/pack").rglob("*") if p.is_file()}
        cls.summary = pack.build_iso_pack(output=cls.output)
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
        for info in self.manifest["files"]:
            data = (self.output / info["file"]).read_bytes()
            self.assertEqual(len(data), info["bytes"])
            self.assertEqual(hashlib.sha256(data).hexdigest(), info["sha256"])

    def test_character_rows_and_resolved_waiting_blinks(self):
        self.assertEqual([c["id"] for c in self.manifest["characters"]], [f"iso/character/{c}" for c in pack.CHARACTERS])
        for character in self.manifest["characters"]:
            normal = self.sheets[character["sheetId"]]
            blink = self.sheets[normal["blink"]["sheetId"]]
            self.assertEqual((len(normal["rows"]), len(blink["rows"])), (38, 24))
            self.assertEqual(normal["identityPalette"], blink["identityPalette"])
            self.assertEqual((normal["width"], normal["height"], normal["anchor"], normal["scale"]), (128, 2432, dict(x=16, y=60), 1))
            for facing in pack.FACINGS:
                waiting = next(c for c in normal["clips"] if (c["name"], c["direction"]) == ("waiting", facing))
                self.assertEqual((waiting["frames"], waiting["loop"], waiting["seatAnchor"]), (1, False, dict(x=16, y=44)))
                self.assertEqual("blink" in waiting, facing in ("se", "sw"))
            for name in (*iso_check.REQUIRED, "gone"):
                for facing in pack.FACINGS:
                    self.assertTrue(any(c["name"] == name and c["direction"] == facing for c in normal["clips"]), (character["id"], name, facing))
            self.assertFalse(any(c.get("seatProfile") == "end" for c in normal["clips"]))

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
        self.assertEqual(len(self.layout["furniture"]), 34)
        desks = [f for f in self.layout["furniture"] if f["assetId"] == "iso/desk"]
        self.assertEqual([(f["x"], f["y"], f["orientation"]) for f in desks], [
            (2, 1, "ne"), (2, 2, "sw"), (7, 1, "nw"), (8, 1, "se"),
            (2, 4, "ne"), (2, 5, "sw"), (6, 4, "nw"), (7, 4, "se"),
            (5, 7, "nw"), (6, 7, "se"), (8, 7, "ne"), (8, 8, "sw")])
        self.assertEqual(len({f["id"] for f in self.layout["furniture"]}), 34)
        for j in range(10):
            for i in range(10):
                expected = 2 + (i + j) % 2 if j == 0 or i == 0 and j <= 2 else 1
                self.assertEqual(self.layout["tiles"][j * 10 + i], expected)
        self.assertEqual(pack.validate_layout(self.layout, self.manifest),
                         dict(blockedCells=37, reachableCells=63, reachableApproaches=16))
        self.assertEqual(len(self.layout["wallSegments"]), 20)
        door = next(w for w in self.layout["wallSegments"] if "door" in w["moduleId"])
        window = next(w for w in self.layout["wallSegments"] if "window" in w["moduleId"])
        self.assertEqual((door["start"], window["start"]), (dict(i=-0.5, j=3.5), dict(i=3.5, j=-0.5)))

    def test_shadow_pet_props_and_explicit_optional_dart(self):
        shadow = self.sheets["iso-character-shadow"]
        with Image.open(self.output / shadow["file"]) as image:
            contact = Image.new("L", image.size)
            contact.putdata([255 if p[3] == 96 else 0 for p in image.get_flattened_data()])
            box = contact.getbbox()
            self.assertEqual((box[2] - box[0], box[3] - box[1]), (16, 6))
            self.assertEqual(set(image.getchannel("A").get_flattened_data()), {0, 48, 96})
        self.assertEqual(len(self.manifest["pets"]), 1)
        self.assertEqual(len(self.manifest["carriedProps"]), 4)
        for s in self.sheets.values():
            if s["kind"] == "character":
                for c in s["clips"]:
                    if c.get("prop") == "held-dart":
                        self.assertTrue(c["propOptional"])
                    elif c.get("prop"):
                        self.assertIn("propBinding", c)
                        self.assertNotIn("propDeferred", c)

    def test_sources_and_flat_pack_are_unchanged(self):
        for info in self.manifest["sourceManifests"]:
            source = pack.ART / info["file"]
            self.assertEqual(hashlib.sha256(source.read_bytes()).hexdigest(), info["sha256"])
        for path, digest in self.flat_before.items():
            self.assertEqual(hashlib.sha256(path.read_bytes()).hexdigest(), digest)

    def test_source_pixels_anchors_and_depth_metadata_are_preserved(self):
        for info in self.manifest["sourceManifests"]:
            source_path = pack.ART / info["file"]
            source = json.loads(source_path.read_text(encoding="utf-8-sig"))
            for original in source["sheets"]:
                published = self.sheets[original["id"]]
                self.assertEqual((self.output / published["file"]).read_bytes(), (source_path.parent / original["file"]).read_bytes())
                for key in ("cellW", "cellH", "anchor", "scale", "rows", "identityPalette", "footAnchor"):
                    if key in original:
                        self.assertEqual(published[key], original[key])
                for facing, mask in original.get("faceLock", {}).items():
                    self.assertEqual((self.output / published["faceLock"][facing]["eyeMask"]).read_bytes(), (source_path.parent / mask["eyeMask"]).read_bytes())
            for family in source.get("furniture", []):
                published = next(f for f in self.manifest["furniture"] if f["id"] == family["id"])
                for a, b in zip(family["variants"], published["variants"]):
                    for original, part in zip(a["parts"], b["parts"]):
                        for key, value in original.items():
                            self.assertEqual(part[key], value)

    def test_frozen_contacts_and_wall_anchors(self):
        desk = next(f for f in self.manifest["furniture"] if f["id"] == "iso/desk")
        expected = {"se": (1, 0, "nw", 32, 4), "sw": (1, 1, "ne", 0, 20),
                    "ne": (0, -1, "sw", 32, -28), "nw": (-1, 1, "se", -64, -12)}
        for v in desk["variants"]:
            self.assertEqual(len(v["seatProfiles"]), 1)
            p = v["seatProfiles"][0]
            self.assertEqual(p["id"], "front")
            self.assertEqual((p["approach"]["x"], p["approach"]["y"], p["isoFacing"], p["contactOffset"]["x"], p["contactOffset"]["y"]), expected[v["orientation"]])
            self.assertEqual(v["chairSeatAnchor"], p["contactOffset"])
        for module in self.manifest["wallModules"]:
            expected_anchor = dict(x=0 if module["face"] == "right" else 32, y=48)
            self.assertEqual(module["edgeAnchor"], expected_anchor)

    def test_synthetic_roster_uses_flat_hash_and_cat_path_is_clear(self):
        used = set()
        roster = [c["id"] for c in self.manifest["characters"]]
        agents = sorted(self.layout["acceptanceFixture"]["agents"], key=lambda a: a["id"])
        for agent in agents:
            value = 2166136261
            encoded = agent["id"].encode("utf-16-le")
            for n in range(0, len(encoded), 2):
                unit = encoded[n] + (encoded[n + 1] << 8)
                value = ((value ^ unit) * 16777619) & 0xffffffff
            start = value % len(roster)
            design = next(roster[(start+n) % len(roster)] for n in range(len(roster)) if roster[(start+n) % len(roster)] not in used)
            used.add(design)
            self.assertEqual(design, agent["characterId"])
        child = next(a for a in agents if "parentId" in a)
        parent = next(a for a in agents if a["id"] == child["parentId"])
        self.assertEqual((child["action"], parent["role"]), ("reporting", "supervisor"))
        self.assertEqual(self.layout["pets"][0]["demonstrationPath"][0], dict(x=2, y=6))
        pack.validate_layout(self.layout, self.manifest)

    def test_pack_final_checker(self):
        checks = iso_check.Checks()
        iso_check.validate(self.output / "assets.json", checks, 3, 8, "final")
        failed = [row for row in checks.results if row[2] == "FAIL"]
        self.assertEqual(failed, [])

    def test_layout_generation_is_deterministic_and_matches_committed_layout(self):
        first = pack.json_bytes(pack.make_layout(self.manifest))
        self.assertEqual(first, pack.json_bytes(pack.make_layout(self.manifest)))
        self.assertEqual(first, (pack.ART / "pack/layout.json").read_bytes())

    def test_invalid_chair_contacts_and_physical_overlaps_are_rejected(self):
        import copy
        for change, message in ((dict(x=2, y=1), "physical"), (dict(x=0, y=4), "misaligned"),
                                (dict(orientation="nw", rotation=180), "misaligned")):
            layout = copy.deepcopy(self.layout)
            next(f for f in layout["furniture"] if f["id"] == "chair-1").update(change)
            with self.subTest(change=change), self.assertRaisesRegex(ValueError, message):
                pack.validate_layout(layout, self.manifest)

    def test_lounge_rug_cannot_extend_under_workstations(self):
        import copy
        layout = copy.deepcopy(self.layout)
        next(f for f in layout["furniture"] if f["assetId"] == "iso/rug").update(x=5, y=7)
        with self.assertRaisesRegex(ValueError, "lounge rug"):
            pack.validate_layout(layout, self.manifest)


if __name__ == "__main__":
    unittest.main()
