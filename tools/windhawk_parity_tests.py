"""Synthetic checks for the same-state parity tool; no renderers are launched."""
import hashlib
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

import windhawk_parity as parity


class ParityTests(unittest.TestCase):
    def setUp(self):
        parity.DEFAULT_OUT.mkdir(parents=True, exist_ok=True)
        self.temp = tempfile.TemporaryDirectory(dir=parity.DEFAULT_OUT)
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name).resolve()
        self.wpf = self.root / "wpf"
        self.wpf.mkdir()
        parity.write_png(self.wpf / "sheet.png", 1, 1, bytes((10, 20, 30, 0)))
        self.sheet = {"id": "test", "file": "sheet.png", "sha256": parity.sha256(self.wpf / "sheet.png")}
        (self.wpf / "manifest.json").write_text(json.dumps({"sheets": [self.sheet]}), encoding="utf-8")
        self.state = {"schemaVersion": 5, "office": {"widthTiles": 8, "heightTiles": 8},
                      "assets": {"sheets": [dict(self.sheet, file="source.png")]}, "agents": []}
        self.write_states()

    def write_states(self):
        for filename in parity.SCENARIOS.values():
            (self.wpf / filename).write_text(json.dumps(self.state), encoding="utf-8")

    def test_supported_versions_and_original_bytes(self):
        for version in (3, 4, 5):
            with self.subTest(version=version):
                self.state["schemaVersion"] = version
                self.state["future"] = {"state": "extra"}
                self.write_states()
                original = (self.wpf / "state.json").read_bytes()
                states = parity.prepare_states(self.wpf, self.root / str(version))
                self.assertEqual(states["main"]["schemaVersion"], version)
                copied = json.loads(Path(states["main"]["nativeInput"]).read_text(encoding="utf-8"))
                self.assertEqual(copied["future"], self.state["future"])
                self.assertEqual(copied["assets"]["sheets"][0]["file"], "sheet.png")
                self.assertEqual((self.wpf / "state.json").read_bytes(), original)

    def test_unknown_versions_rejected(self):
        for version in (2, 6, 2147483647, "5", 5.0, None):
            with self.subTest(version=version):
                self.state["schemaVersion"] = version
                self.write_states()
                with self.assertRaises(ValueError):
                    parity.prepare_states(self.wpf, self.root / "rejected")

    def test_metadata_mismatch_rejected(self):
        self.state["assets"]["sheets"][0]["sha256"] = "0" * 64
        self.write_states()
        with self.assertRaises(ValueError):
            parity.prepare_states(self.wpf, self.root / "mismatch")

    def test_export_hash_mismatch_rejected(self):
        (self.wpf / "sheet.png").write_bytes(b"damaged")
        with self.assertRaises(ValueError):
            parity.prepare_states(self.wpf, self.root / "damaged")

    def test_v5_ambient_pets_and_seats_preserved(self):
        ambient = self.root / "ambient"
        ambient.mkdir()
        state = dict(self.state, ambient={"windowView": "snow", "localTime": "2026-12-12T12:34:56+09:00"},
                     pets=[{"id": "synthetic-pet"}], agents=[{"pose": {"seatPhase": "raise"}}])
        for name in ("day", "night", "rain", "snow"):
            (ambient / f"{name}-state.json").write_text(json.dumps(state), encoding="utf-8")
        states = parity.prepare_states(self.wpf, self.root / "ambient-out", ambient)
        self.assertEqual(len(states), 8)
        self.assertEqual(states["ambient-snow"]["pets"], 1)
        self.assertEqual(states["ambient-snow"]["seatPhases"], ["raise"])
        copied = json.loads(Path(states["ambient-snow"]["nativeInput"]).read_text(encoding="utf-8"))
        self.assertEqual(copied["ambient"], state["ambient"])
        self.assertEqual(copied["pets"], state["pets"])

    def test_rgba_includes_rgb_beneath_zero_alpha(self):
        reference, candidate = self.root / "a.png", self.root / "b.png"
        parity.write_png(reference, 1, 1, bytes((10, 20, 30, 0)))
        parity.write_png(candidate, 1, 1, bytes((11, 20, 30, 0)))
        result = parity.compare("zero-alpha", reference, candidate, self.root, True)
        self.assertEqual(result["exactRgbaPercent"], 0)
        self.assertEqual(result["differingPixels"], 1)

    def test_no_resizing_on_dimension_mismatch(self):
        reference, candidate = self.root / "a.png", self.root / "b.png"
        parity.write_png(reference, 1, 1, bytes((10, 20, 30, 255)))
        parity.write_png(candidate, 2, 1, bytes((10, 20, 30, 255)) * 2)
        self.assertEqual(parity.compare("size", reference, candidate, self.root, False)["status"], "dimension-mismatch")

    def test_reviewed_hash_pair_and_tag_mode(self):
        app = self.root / "Vibedotcom.App.exe"
        app.with_suffix(".dll").write_bytes(b"synthetic-app")
        (self.root / "Vibedotcom.Core.dll").write_bytes(b"synthetic-core")
        app_hash = hashlib.sha256(b"synthetic-app").hexdigest()
        core_hash = hashlib.sha256(b"synthetic-core").hexdigest()
        with patch.object(parity, "run_hidden") as run:
            with self.assertRaises(ValueError):
                parity.run_wpf(app, self.root / "run", self.root / "run.log", "icon", app_hash, "0" * 64)
            run.assert_not_called()
            parity.run_wpf(app, self.root / "run", self.root / "run.log", "icon", app_hash, core_hash)
            self.assertEqual(run.call_args.args[0][-2:], ["--tags", "icon"])


if __name__ == "__main__":
    unittest.main()
