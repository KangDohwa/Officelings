"""Isolated brand-fetch checks; all downloads/renderers are mocked, no pack or runtime tests."""

from contextlib import redirect_stderr, redirect_stdout
from io import BytesIO, StringIO
from email.message import Message
import os
from pathlib import Path
import sys
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import Mock, patch
from urllib.error import URLError
from urllib.parse import urlsplit

from PIL import Image, ImageChops, ImageDraw

import fetch_brand_icons as brand


def image_bytes(image, format="PNG", **options):
    stream = BytesIO()
    image.save(stream, format=format, **options)
    return stream.getvalue()


def rgba_pixels(image):
    data = image.tobytes()
    return [tuple(data[offset:offset + 4]) for offset in range(0, len(data), 4)]


class BrandFetchTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory(prefix="officelings-brand-")
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)
        self.originals = self.root / "brand originals"
        self.output = self.root / "brand overlay"
        self.codex = Image.new("RGBA", (64, 64), (0, 0, 255, 0))
        glyph = ImageDraw.Draw(self.codex)
        glyph.ellipse((5, 5, 58, 58), fill=(0, 0, 0, 255))
        glyph.ellipse((22, 22, 41, 41), fill=(0, 0, 255, 0))
        self.claude = Image.new("RGBA", (180, 180), (220, 107, 71, 255))
        ImageDraw.Draw(self.claude).rectangle((75, 45, 104, 134), fill=(255, 255, 255, 255))
        self.gemini = Image.new("RGBA", (256, 256), (255, 0, 0, 0))
        draw = ImageDraw.Draw(self.gemini)
        for x in range(256):
            draw.line((x, 0, x, 255), fill=(80 + x // 3, 100, 220 - x // 3, 255))
        self.svg = b'<svg xmlns="http://www.w3.org/2000/svg" width="28" height="28"/>'
        self.payloads = {
            brand.SOURCES["codex"][0]: image_bytes(self.codex, "ICO", sizes=[(16, 16), (32, 32), (64, 64)]),
            brand.SOURCES["claude"][0]: image_bytes(self.claude),
            brand.SOURCES["antigravity"][0]: self.svg,
            brand.SOURCES["antigravity"][1]: image_bytes(self.gemini),
        }

    def response(self, request, **kwargs):
        response = BytesIO(self.payloads[request.full_url])
        response.headers = Message()
        response.headers["Content-Type"] = "image/x-icon" if request.full_url.endswith(".ico") else "image/png"
        response.geturl = lambda: request.full_url
        return response

    def fetch(self, recolor=False, output=None):
        with (patch.object(brand, "urlopen", side_effect=self.response) as network,
              patch.object(brand, "render_svg", side_effect=RuntimeError("no installed renderer")),
              redirect_stdout(StringIO()), redirect_stderr(StringIO())):
            files = brand.fetch_icons(self.originals, output or self.output, recolor)
        return files, network

    def test_mocked_fetch_writes_nine_dimensions_and_intact_originals(self):
        self.output.mkdir()
        marker = self.output / "unrelated.txt"
        marker.write_text("keep", encoding="utf-8")
        files, network = self.fetch()
        expected = {f"{provider}{suffix}.png" for provider in brand.SOURCES for _, suffix in brand.SIZES}
        self.assertEqual({path.name for path in files}, expected)
        self.assertEqual(network.call_count, 3)
        for url, payload in self.payloads.items():
            if url.endswith(".svg"):
                self.assertFalse((self.originals / Path(urlsplit(url).path).name).exists())
                continue
            self.assertEqual((self.originals / Path(urlsplit(url).path).name).read_bytes(), payload)
        for path in files:
            size = 36 if "@3x" in path.stem else 24 if "@2x" in path.stem else 12
            with Image.open(path) as icon:
                self.assertEqual(icon.format, "PNG")
                self.assertEqual(icon.mode, "RGBA")
                self.assertEqual(icon.size, (size, size))
                self.assertIsNotNone(icon.getchannel("A").getbbox())
                pixels = rgba_pixels(icon)
                self.assertTrue(all(pixel == (0, 0, 0, 0) for pixel in pixels if pixel[3] == 0))
        self.assertEqual(marker.read_text(encoding="utf-8"), "keep")

    def test_default_and_explicit_no_recolor_preserve_colors(self):
        parser = brand.argument_parser()
        self.assertFalse(parser.parse_args([]).recolor)
        self.assertFalse(parser.parse_args(["--no-recolor"]).recolor)
        self.assertEqual(parser.parse_args([]).output, brand.DEFAULT_OUTPUT)
        self.assertEqual(parser.parse_args([]).originals, brand.DEFAULT_ORIGINALS)
        for provider, image in (("codex", self.codex), ("claude", self.claude), ("antigravity", self.gemini)):
            with self.subTest(provider=provider):
                original = image.getpixel((image.width // 4, image.height // 2))
                prepared = brand.prepare_source(provider, image)
                self.assertEqual(prepared.getpixel((image.width // 4, image.height // 2)), original)
        self.assertGreater(len(set(rgba_pixels(brand.prepare_source("antigravity", self.gemini)))), 50)

    def test_recolor_changes_only_codex_rgb_preserving_glyph_alpha(self):
        dark = brand.prepare_source("codex", self.codex)
        light = brand.prepare_source("codex", self.codex, recolor=True)
        self.assertEqual(dark.getchannel("A").tobytes(), light.getchannel("A").tobytes())
        self.assertEqual(light.getpixel((16, 32)), (*brand.LIGHT_GLYPH, 255))
        self.assertEqual(light.getpixel((32, 32)), (0, 0, 0, 0))
        plain, _ = self.fetch()
        recolored, _ = self.fetch(recolor=True, output=self.root / "light")
        for first, second in zip(plain, recolored):
            if first.stem.startswith("codex"):
                with Image.open(first) as black, Image.open(second) as white:
                    self.assertNotEqual(black.tobytes(), white.tobytes())
                    self.assertEqual(black.getchannel("A").tobytes(), white.getchannel("A").tobytes())
            else:
                self.assertEqual(first.read_bytes(), second.read_bytes())

    def test_alpha_cleanup_keeps_partial_coverage(self):
        image = Image.new("RGBA", (3, 1))
        image.putdata([(250, 80, 90, 0), (210, 60, 30, 1), (220, 107, 71, 254)])
        cleaned = brand.clean_alpha(image)
        self.assertEqual(cleaned.getpixel((0, 0)), (0, 0, 0, 0))
        self.assertEqual(cleaned.getpixel((1, 0)), (210, 60, 30, 1))
        self.assertEqual(cleaned.getpixel((2, 0)), (220, 107, 71, 254))

    def test_claude_keeps_orange_tile_white_glyph_and_rounded_alpha(self):
        prepared = brand.prepare_source("claude", self.claude)
        self.assertEqual(prepared.getpixel((0, 0)), (0, 0, 0, 0))
        self.assertEqual(prepared.getpixel((0, 90)), (220, 107, 71, 255))
        self.assertEqual(prepared.getpixel((90, 90)), (255, 255, 255, 255))
        for size, _ in brand.SIZES:
            with self.subTest(size=size):
                icon = brand.render_icon(prepared, size)
                # At 12px a corner pixel partly covers the rounded tile. Require
                # low coverage, not a hard cut that destroys antialiased edges.
                self.assertLess(icon.getpixel((0, 0))[3], 64)
                self.assertGreater(icon.getpixel((0, size // 2))[3], 240)
                # Lanczos mixes the thin white glyph with the orange tile at 12px.
                self.assertTrue(all(channel >= 245 for channel in icon.getpixel((size // 2, size // 2))[:3]))
                for actual, expected in zip(icon.getpixel((0, size // 2))[:3], (220, 107, 71)):
                    self.assertLessEqual(abs(actual - expected), 3)

    def test_lanczos_has_smooth_edges_without_hidden_rgb_halos(self):
        source = Image.new("RGBA", (256, 256), (0, 0, 255, 0))
        ImageDraw.Draw(source).ellipse((27, 27, 226, 226), fill=(255, 0, 0, 255))
        for size, _ in brand.SIZES:
            with self.subTest(size=size):
                icon = brand.render_icon(source, size)
                pixels = rgba_pixels(icon)
                self.assertTrue(any(0 < alpha < 255 for _, _, _, alpha in pixels))
                self.assertTrue(all((red, green, blue) == (255, 0, 0)
                                    for red, green, blue, alpha in pixels if alpha > 0))
        small = brand.render_icon(source, 12)
        large = brand.render_icon(source, 36)
        enlarged_small = small.resize((36, 36), Image.Resampling.LANCZOS)
        self.assertIsNotNone(ImageChops.difference(large.getchannel("A"), enlarged_small.getchannel("A")).getbbox())

    def test_non_square_sources_fit_without_distortion(self):
        source = Image.new("RGBA", (120, 60), (80, 100, 120, 255))
        icon = brand.render_icon(source, 12)
        self.assertEqual(icon.getchannel("A").getbbox(), (0, 3, 12, 9))

    def test_ico_uses_largest_frame(self):
        path = self.root / "source.ico"
        path.write_bytes(self.payloads[brand.SOURCES["codex"][0]])
        self.assertEqual(brand.load_raster(path).size, (64, 64))

    def test_installed_svg_renderer_is_never_used_for_downloads(self):
        vector = self.gemini.resize((brand.VECTOR_SIZE, brand.VECTOR_SIZE))
        renderer = Mock(return_value=image_bytes(vector))
        with (patch.object(brand, "urlopen", side_effect=self.response) as network,
              patch.dict(sys.modules, {"cairosvg": SimpleNamespace(svg2png=renderer)}),
              redirect_stdout(StringIO())):
            files = brand.fetch_icons(self.originals, self.output)
        self.assertEqual(len(files), 9)
        self.assertEqual(network.call_count, 3)
        renderer.assert_not_called()
        self.assertTrue((self.originals / Path(urlsplit(brand.SOURCES["antigravity"][1]).path).name).exists())

    def test_svg_network_failure_uses_official_png(self):
        def response(request, **kwargs):
            if request.full_url.endswith(".svg"):
                raise URLError("SVG blocked")
            return self.response(request, **kwargs)

        with (patch.object(brand, "urlopen", side_effect=response),
              redirect_stdout(StringIO()), redirect_stderr(StringIO())):
            self.assertEqual(len(brand.fetch_icons(self.originals, self.output)), 9)

    def test_fetch_failure_returns_nonzero_without_writing_outputs(self):
        with (patch.object(brand, "urlopen", side_effect=URLError("network blocked")),
              redirect_stderr(StringIO())):
            result = brand.main(["--originals", str(self.originals), "--output", str(self.output)])
        self.assertEqual(result, 1)
        self.assertFalse(self.output.exists())

    def test_rejects_tiny_or_empty_source(self):
        for source in (Image.new("RGBA", (32, 32), (0, 0, 0, 255)), Image.new("RGBA", (64, 64))):
            with self.assertRaises(ValueError):
                brand.prepare_source("codex", source)

    def test_cli_install_copies_only_outputs_to_local_app_data(self):
        local_app_data = self.root / "local app data"
        installed = local_app_data / "Officelings/brand"
        installed.mkdir(parents=True)
        marker = installed / "unrelated.txt"
        marker.write_text("keep", encoding="utf-8")
        with (patch.dict(os.environ, {"LOCALAPPDATA": str(local_app_data)}),
              patch.object(brand, "urlopen", side_effect=self.response),
              patch.object(brand, "render_svg", side_effect=RuntimeError("no installed renderer")),
              redirect_stdout(StringIO()), redirect_stderr(StringIO())):
            self.assertEqual(brand.main(["--originals", str(self.originals), "--output", str(self.output),
                                         "--install", "--recolor"]), 0)
            # --output may itself be the installation directory.
            brand.install_icons(list(installed.glob("*.png")))
        self.assertEqual(len(list(installed.glob("*.png"))), 9)
        for path in self.output.glob("*.png"):
            self.assertEqual(path.read_bytes(), (installed / path.name).read_bytes())
        self.assertEqual(marker.read_text(encoding="utf-8"), "keep")

    def test_install_requires_local_app_data(self):
        with patch.dict(os.environ, {}, clear=True), self.assertRaisesRegex(ValueError, "LOCALAPPDATA"):
            brand.install_icons([])

    def test_recolor_flags_are_mutually_exclusive(self):
        with redirect_stderr(StringIO()), self.assertRaises(SystemExit) as error:
            brand.argument_parser().parse_args(["--recolor", "--no-recolor"])
        self.assertEqual(error.exception.code, 2)


if __name__ == "__main__":
    unittest.main()
