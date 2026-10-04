"""Synthetic malformed files only; downloads and executable launches are mocked."""
from email.message import Message
from io import BytesIO
import json
import os
from pathlib import Path
import struct
import subprocess
import tempfile
import unittest
from unittest.mock import patch
import zlib

from PIL import Image
import build_pack
import fetch_brand_icons as brand
import publication
import safe_files
import seated
import windhawk_parity as parity


class HardeningTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory(prefix="vdc-hardening-")
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)

    def junction(self, name, target):
        path = self.root / name
        if os.name == "nt":
            result = subprocess.run(["cmd.exe", "/c", "mklink", "/J", str(path), str(target)],
                                    capture_output=True, creationflags=subprocess.CREATE_NO_WINDOW)
            self.assertEqual(result.returncode, 0, result.stderr)
        else:
            path.symlink_to(target, target_is_directory=True)
        self.addCleanup(lambda: path.rmdir() if os.name == "nt" else path.unlink())
        return path

    def png(self, raw=b"\0\0\0\0\0", width=1, height=1):
        def chunk(kind, data):
            return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data) & 0xffffffff)
        return b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0)) + chunk(b"IDAT", zlib.compress(raw)) + chunk(b"IEND", b"")

    def response(self, data, mime="image/png", length=None, final=None):
        result = BytesIO(data)
        result.headers = Message()
        result.headers["Content-Type"] = mime
        if length is not None:
            result.headers["Content-Length"] = str(length)
        result.geturl = lambda: final or brand.SOURCES["claude"][0]
        return result

    def test_seated_rejects_manifest_traversal_before_image_decode(self):
        manifest = self.root / "manifest.json"
        manifest.write_text(json.dumps({"sprite_sheet_alpha": "../outside.png"}))
        with self.assertRaises(ValueError):
            seated.load(manifest)

    def test_seated_publication_and_character_id_cannot_escape_output(self):
        with patch.object(seated, "OUT", self.root / "output"):
            with self.assertRaises(ValueError):
                seated.publish(self.root / "marker", b"bad")
            with self.assertRaises(ValueError):
                seated.process("../marker", self.root / "unused.json", 48)
        self.assertFalse((self.root / "marker").exists())

    def test_builder_output_junction_never_writes_or_deletes_outside(self):
        outside = self.root / "outside"
        outside.mkdir()
        marker = outside / ("pack-" + "a" * 64 + ".png")
        marker.write_bytes(b"keep")
        output = self.root / "pack"
        output.mkdir()
        link = self.junction("pack/sheets", outside)
        with self.assertRaises(ValueError):
            build_pack.atomic_write(link / marker.name, b"bad")
        with self.assertRaises(ValueError):
            build_pack.build_pack(self.root / "art", output)
        with self.assertRaises(ValueError):
            safe_files.make_directory(link / "new" / "nested")
        self.assertFalse((outside / "new").exists())
        self.assertEqual(marker.read_bytes(), b"keep")

    def test_publication_lock_rejects_hard_link_without_changing_marker(self):
        outside = self.root / "marker"
        outside.write_bytes(b"")
        output = self.root / "pack"
        output.mkdir()
        os.link(outside, output / ".publish.lock")
        with self.assertRaises(ValueError):
            with publication.writer_lock(output):
                self.fail("linked publication lock was accepted")
        self.assertEqual(outside.read_bytes(), b"")

    def test_excessive_path_depth_rejects_before_directory_creation(self):
        path = self.root.joinpath(*(["a"] * 129))
        with self.assertRaises(ValueError):
            safe_files.make_directory(path)
        self.assertFalse((self.root / "a").exists())

    @unittest.skipUnless(os.name == "nt", "Windows directory sharing boundary")
    def test_held_ancestor_rejects_write_open_and_rename(self):
        import ctypes
        directory = self.root / "directory"
        directory.mkdir()
        with safe_files.boundary(directory / "file"):
            handle = safe_files._kernel.CreateFileW(str(directory), 0x40000000, 3, None, 3, 0x02200000, None)
            failed = handle == ctypes.c_void_p(-1).value
            if not failed:
                safe_files._kernel.CloseHandle(handle)
            self.assertTrue(failed, "ancestor handle allowed reparse mutation")
            self.assertEqual(ctypes.get_last_error(), 32)
            with self.assertRaises(OSError):
                directory.rename(self.root / "renamed")
        self.assertTrue(directory.is_dir())

    @unittest.skipUnless(os.name == "nt", "Windows reparse write boundary")
    def test_write_boundary_pins_empty_directory_and_cleans_guard(self):
        import ctypes
        from ctypes import wintypes
        directory, target = self.root / "directory", self.root / "target"
        directory.mkdir(); target.mkdir()
        kernel = safe_files._kernel
        kernel.DeviceIoControl.argtypes = (wintypes.HANDLE, wintypes.DWORD, ctypes.c_void_p, wintypes.DWORD,
                                          ctypes.c_void_p, wintypes.DWORD, ctypes.POINTER(wintypes.DWORD), ctypes.c_void_p)
        substitute = ("\\??\\" + str(target)).encode("utf-16le")
        printed = str(target).encode("utf-16le")
        payload = struct.pack("<HHHH", 0, len(substitute), len(substitute) + 2, len(printed)) + substitute + b"\0\0" + printed + b"\0\0"
        point = struct.pack("<IHH", 0xa0000003, len(payload), 0) + payload
        with safe_files.write_boundary(directory / "file"):
            handle = kernel.CreateFileW(str(directory), 0x40000000, 3, None, 3, 0x02200000, None)
            self.assertNotEqual(handle, ctypes.c_void_p(-1).value)
            count = wintypes.DWORD()
            buffer = ctypes.create_string_buffer(point)
            result = kernel.DeviceIoControl(handle, 0x900a4, buffer, len(point), None, 0, ctypes.byref(count), None)
            error = ctypes.get_last_error()
            if result:
                cleanup = ctypes.create_string_buffer(struct.pack("<IHH", 0xa0000003, 0, 0))
                kernel.DeviceIoControl(handle, 0x900ac, cleanup, 8, None, 0, ctypes.byref(count), None)
            kernel.CloseHandle(handle)
            self.assertFalse(result, "pinned publication directory acquired a junction")
            self.assertEqual(error, 145)
        self.assertEqual(list(directory.iterdir()), [])
        self.assertEqual(list(target.iterdir()), [])

    def test_builder_source_links_and_directory_fanout_are_rejected(self):
        outside = self.root / "outside"
        outside.mkdir()
        roster = self.root / "roster"
        roster.mkdir()
        self.junction("roster/linked", outside)
        with self.assertRaises(ValueError):
            safe_files.bounded_glob(roster)
        for i in range(257):
            (self.root / f"input-{i}.json").write_bytes(b"{}")
        with self.assertRaises(ValueError):
            safe_files.bounded_glob(self.root, "input-*.json")

    def test_builder_rejects_frame_fanout_and_pixel_work_before_allocation(self):
        image = self.root / "image.png"
        image.write_bytes(self.png())
        manifest = self.root / "image.manifest.json"
        manifest.write_text(json.dumps({"frame_layout": {"sheetWidth": 1, "sheetHeight": 1, "rows": {"idle": [{}] * 129}}, "animation": {"rows": {}}}))
        with self.assertRaisesRegex(ValueError, "frame count"):
            build_pack.ProductionSheet(image, manifest)
        with self.assertRaises(ValueError):
            build_pack.atlas([Image.new("RGBA", (1, 1))] * 129)
        with self.assertRaises(ValueError):
            build_pack.new_image((8192, 8192))

    def test_publication_rejects_generation_junction(self):
        outside = self.root / "outside"
        outside.mkdir()
        self.junction(".generations", outside)
        with self.assertRaises(ValueError):
            publication.publish_batch(self.root, {"manifest.json": b"{}", "image.png": b"x"}, "manifest.json")
        self.assertEqual(list(outside.iterdir()), [])

    def test_json_link_size_and_depth_budgets(self):
        outside = self.root / "outside"
        outside.mkdir()
        (outside / "input.json").write_text("{}")
        alias = self.junction("alias", outside)
        with self.assertRaises(ValueError):
            safe_files.read_json(alias / "input.json")
        path = self.root / "large.json"
        with path.open("wb") as stream:
            stream.truncate(4 * 1024 * 1024 + 1)
        with self.assertRaises(ValueError):
            safe_files.read_json(path)
        path.write_text("[" * 66 + "0" + "]" * 66)
        with self.assertRaises(ValueError):
            safe_files.read_json(path)

    def test_parity_rejects_png_bomb_trailing_stream_dimensions_and_chunk(self):
        path = self.root / "bad.png"
        for data in (self.png(b"x" * (2 * 1024 * 1024)), self.png(width=100000),
                     self.png()[:-3], self.png() + b"garbage"):
            path.write_bytes(data)
            with self.subTest(bytes=len(data)), self.assertRaises(ValueError):
                parity.read_png(path)

    def test_parity_normal_png_roundtrip(self):
        path = self.root / "valid.png"
        pixels = bytearray([1, 2, 3, 4] * 6)
        parity.write_png(path, 3, 2, pixels)
        self.assertEqual(parity.read_png(path), (3, 2, pixels))

    def test_download_rejects_http_host_redirect_mime_size_hash_without_publication(self):
        url = brand.SOURCES["claude"][0]
        for invalid in (url.replace("https:", "http:"), "https://example.invalid/icon.png", "https://claude.ai/icon.svg"):
            with self.assertRaises(ValueError):
                brand.download(invalid, self.root)
        for options in ({"final": "http://claude.ai/icon.png"}, {"mime": "text/plain"},
                        {"length": brand.MAX_DOWNLOAD_BYTES + 1}):
            with patch.object(brand, "urlopen", return_value=self.response(self.png(), **options)), self.assertRaises(ValueError):
                brand.download(url, self.root)
        with patch.object(brand, "urlopen", return_value=self.response(self.png())), self.assertRaises(ValueError):
            brand.download(url, self.root, "0" * 64)
        with patch.object(brand, "MAX_DOWNLOAD_BYTES", 20), patch.object(brand, "urlopen", return_value=self.response(b"x" * 21)), self.assertRaises(ValueError):
            brand.download(url, self.root)
        with self.assertRaises(ValueError):
            brand.NoRedirects().redirect_request(None, None, 302, "", {}, "https://claude.ai/icon.png")
        self.assertFalse((self.root / "apple-touch-icon.png").exists())

    def test_download_deadline_rejects_unpublished_body(self):
        with patch.object(brand.time, "monotonic", side_effect=[0, 21]), patch.object(brand, "urlopen", return_value=self.response(self.png())), self.assertRaises(ValueError):
            brand.download(brand.SOURCES["claude"][0], self.root)
        self.assertFalse((self.root / "apple-touch-icon.png").exists())

    def test_raster_rejects_eps_svg_and_oversized_header(self):
        path = self.root / "fake.png"
        for data in (b"%!PS-Adobe-3.0 EPSF-3.0\n%%BoundingBox: 0 0 12 12\n", b"<svg/>", self.png(width=5000)):
            path.write_bytes(data)
            with self.assertRaises((OSError, ValueError)):
                brand.load_raster(path)
        with self.assertRaises(ValueError):
            brand.render_svg(path)

    def test_audited_dlls_do_not_authorize_replaced_executable(self):
        app = self.root / "App.exe"
        app.write_bytes(b"replacement")
        with patch.object(parity, "sha256", side_effect=["app", "core", "replacement"]), patch.object(parity, "AUDITED_BUILDS", {"app": "core"}), patch.object(parity, "run_hidden") as launch:
            with self.assertRaisesRegex(ValueError, "launcher"):
                parity.run_wpf(app, self.root / "output", self.root / "log")
        launch.assert_not_called()

    def test_render_only_marker_does_not_authorize_native_executable(self):
        renderer = self.root / "renderer_tests.exe"
        renderer.write_bytes(b"replacement --render-only")
        with self.assertRaisesRegex(ValueError, "Unreviewed native"):
            parity.audited_renderer(renderer)


if __name__ == "__main__":
    unittest.main()
