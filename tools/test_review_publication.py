"""Review F10/F15/F16: isolated process races and interrupted batch commits."""
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import threading
import time
import unittest
from unittest.mock import patch

import build_pack
import facelock
import publication
import seated


class ReviewPublicationTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="officelings-review-publish-")
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)

    def source(self):
        folder = self.root / "source"
        folder.mkdir()
        name = "codex-worker-01"
        for suffix in (".png", ".manifest.json"):
            shutil.copyfile(build_pack.ROOT / "assets/art/sheets" / f"{name}{suffix}", folder / f"{name}{suffix}")
        return name, folder / f"{name}.manifest.json", folder / f"{name}.png"

    def snapshot(self, directory):
        return {p: p.read_bytes() for p in directory.rglob("*") if p.is_file() and p.name != ".publish.lock"}

    def assert_preserved(self, snapshot):
        for path, data in snapshot.items():
            self.assertEqual(data, path.read_bytes(), str(path))

    def assert_manifest(self, path):
        m = json.loads(path.read_bytes())
        for key in ("sprite_sheet_alpha", "game_input"):
            self.assertTrue((path.parent / m[key]).is_file())
        self.assertTrue((path.parent / m["facelock"]["report"]).is_file())
        for entry in m.get("blink", {}).values():
            self.assertTrue((path.parent / entry["file"]).is_file())
        return m

    def test_F10_parallel_builds_publish_complete_packs_and_preserve_grace(self):
        art = self.root / "art-a"
        sheets = art / "sheets"
        sheets.mkdir(parents=True)
        for name in [*build_pack.P5_IDS, "props", "icons", "tiles"]:
            for suffix in (".png", ".manifest.json"):
                shutil.copyfile(build_pack.ROOT / "assets/art/sheets" / f"{name}{suffix}", sheets / f"{name}{suffix}")
        other = self.root / "art-b"
        shutil.copytree(art, other)
        for directory, name, colour in ((art, "writer-a", (1, 2, 3, 255)), (other, "writer-b", (3, 2, 1, 255))):
            roster = directory / "roster" / name / "run"
            roster.mkdir(parents=True)
            with build_pack.Image.open(sheets / "codex-worker-01.png") as original:
                image = original.convert("RGBA")
            image.putpixel((16, 20), colour)
            image.save(roster / "sprite-sheet-alpha.png")
            shutil.copyfile(sheets / "codex-worker-01.manifest.json", roster / "manifest.json")
        output = self.root / "pack"
        processes = []
        # Announce the actual staging checkpoint and wait for the parent. Pack
        # construction must not consume the publication lock's contention timeout.
        script = """
from contextlib import contextmanager
from pathlib import Path
import build_pack, sys
original_lock = build_pack.writer_lock
@contextmanager
def ready_lock(output):
    print("staged", flush=True)
    if sys.stdin.readline().strip() != "publish":
        raise RuntimeError("publication rendezvous closed")
    with original_lock(output):
        yield
build_pack.writer_lock = ready_lock
build_pack.build_pack(Path(sys.argv[1]), Path(sys.argv[2]))
"""
        try:
            with publication.writer_lock(output):
                for directory in (art, other):
                    processes.append(subprocess.Popen([sys.executable, "-B", "-c", script, str(directory), str(output)],
                        cwd=Path(build_pack.__file__).parent, stdin=subprocess.PIPE,
                        stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True))
                for p in processes:
                    self.assertEqual("staged\n", p.stdout.readline(), "writer failed before the publication checkpoint")
                    self.assertIsNone(p.poll())
                self.assertEqual(2, len(list(output.glob(".build-pack-*/assets.json"))))
                self.assertFalse((output / "assets.json").exists())
            for p in processes:
                p.stdin.write("publish\n")
                p.stdin.flush()
            for p in processes:
                out, err = p.communicate(timeout=30)
                self.assertEqual(0, p.returncode, out + err)
        finally:
            for p in processes:
                if p.poll() is None:
                    p.kill()
                    p.communicate()
        m = json.loads((output / "assets.json").read_bytes())
        referenced = {s["file"] for s in m["sheets"]}
        self.assertTrue(all((output / name).is_file() for name in referenced))
        stale = [p for p in (output / "sheets").glob("pack-*.png") if p.relative_to(output).as_posix() not in referenced]
        self.assertTrue(stale, "previous writer's PNG should survive the grace period")
        fresh = self.snapshot(output)
        current_art = art if any(c["id"] == "writer-a" for c in m["characters"]) else other
        build_pack.build_pack(current_art, output)
        self.assert_preserved(fresh)
        old = time.time() - build_pack.PNG_GRACE_SECONDS - 10
        for p in [*stale, output / next(iter(referenced))]:
            os.utime(p, (old, old))
        summary = build_pack.build_pack(current_art, output)
        self.assertEqual(len(stale), summary["removedStale"])
        self.assertTrue(all((output / name).is_file() for name in referenced))
        self.assertFalse(list(output.glob(".build-pack-*")))

    def test_F15_facelock_failed_report_or_commit_preserves_previous_batch(self):
        name, mp, sp = self.source()
        _, output = facelock.process(name, mp, sp)
        commit = output / f"{name}.manifest.json"
        old = self.snapshot(output)
        write = publication.atomic_write
        replace = publication.os.replace
        for phase in ("report", "manifest"):
            def fail_write(path, data):
                if phase == "report" and Path(path).name.endswith(".facelock.json"):
                    raise OSError("injected report failure")
                return write(path, data)

            def fail_replace(source, target):
                if phase == "manifest" and Path(target) == commit:
                    raise OSError("injected manifest failure")
                return replace(source, target)

            with patch.object(publication, "atomic_write", side_effect=fail_write), patch.object(publication.os, "replace", side_effect=fail_replace):
                with self.assertRaisesRegex(OSError, "injected"):
                    facelock.process(name, mp, sp, make_blink=True)
            self.assert_preserved(old)
            self.assert_manifest(commit)
            self.assertFalse(list(output.glob(".stage-*")))
        facelock.process(name, mp, sp, make_blink=True)
        m = self.assert_manifest(commit)
        self.assertEqual(3, len(m["blink"]))
        # Consumers load the committed generation, including its report/blinks.
        source = build_pack.ProductionSheet(output / f"{name}.png", commit)
        image, rows = build_pack.character_sheet(source)
        self.assertGreater(image.height, 0)
        self.assertTrue(any(r["name"].startswith("blink-") for r in rows))
        report_path = output / m["facelock"]["report"]
        report = json.loads(report_path.read_bytes())
        self.assertEqual(mp.resolve(), (report_path.parent / report["sourceManifest"]).resolve())
        self.assert_manifest(report_path.parent / report["outputManifest"])

    def test_F16_seated_atomic_files_use_unique_temps(self):
        target = self.root / "pose.png"
        entered = threading.Event()
        release = threading.Event()
        replace = publication.os.replace
        temps, errors = [], []

        def interleave(source, destination):
            temps.append(Path(source))
            if threading.current_thread().name == "writer-a":
                entered.set()
                if not release.wait(5):
                    raise TimeoutError("writer-b did not complete")
            return replace(source, destination)

        def writer(data):
            try:
                seated.publish(target, data)
            except Exception as e:
                errors.append(e)

        with patch.object(publication.os, "replace", side_effect=interleave), patch.object(seated, "OUT", self.root):
            a = threading.Thread(target=writer, args=(b"a",), name="writer-a")
            a.start()
            try:
                self.assertTrue(entered.wait(5))
                writer(b"b")
            finally:
                release.set()
                a.join(5)
        self.assertFalse(errors, errors)
        self.assertEqual(2, len(set(temps)))
        self.assertEqual(b"a", target.read_bytes())
        self.assertFalse(list(self.root.glob("*.tmp")))

    def test_F16_seated_parallel_batches_and_failed_commit_keep_one_generation(self):
        name, mp, sp = self.source()
        with patch.object(facelock, "ROOT", self.root):
            _, locked = facelock.process(name, mp, sp, make_blink=True)
        source = locked / f"{name}.manifest.json"
        output = self.root / "seated"
        script = "from pathlib import Path; import seated,sys; seated.ROOT=Path(sys.argv[1]); seated.OUT=Path(sys.argv[2]); seated.process(sys.argv[3],Path(sys.argv[4]),int(sys.argv[5]))"
        processes = [subprocess.Popen([sys.executable, "-B", "-c", script, str(self.root), str(output), name, str(source), str(y)], cwd=Path(seated.__file__).parent, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True) for y in (52, 53)]
        try:
            for p in processes:
                out, err = p.communicate(timeout=30)
                self.assertEqual(0, p.returncode, out + err)
        finally:
            for p in processes:
                if p.poll() is None:
                    p.kill()
                    p.communicate()
        commit = output / name / f"{name}.manifest.json"
        m = self.assert_manifest(commit)
        self.assertTrue((commit.parent / m["seated"]["blinkSheet"]["file"]).is_file())
        old = self.snapshot(output / name)
        replace = publication.os.replace

        def fail(source, target):
            if Path(target) == commit:
                raise OSError("injected seated commit failure")
            return replace(source, target)

        with patch.object(seated, "ROOT", self.root), patch.object(seated, "OUT", output), patch.object(publication.os, "replace", side_effect=fail):
            with self.assertRaisesRegex(OSError, "injected seated"):
                seated.process(name, source, 54)
        self.assert_preserved(old)
        self.assert_manifest(commit)
        self.assertFalse(list(output.rglob("*.tmp")))


if __name__ == "__main__":
    unittest.main()
