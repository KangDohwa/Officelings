"""Fetch optional, local-only official icons; never build or modify the asset pack."""

from __future__ import annotations

import argparse
import hashlib
from io import BytesIO
import os
from pathlib import Path
import shutil
import sys
import time
import warnings
from urllib.parse import urlsplit
from urllib.request import Request, HTTPRedirectHandler, build_opener
from safe_files import safe_path, read_bytes, atomic_write, make_directory

try:
    from PIL import Image, ImageChops, ImageDraw, ImageOps
except ImportError:
    raise SystemExit("Use an existing Python environment with Pillow (also used by the art tools). "
                     "This tool does not install packages.")


ROOT = Path(__file__).resolve().parents[1]
DEFAULT_ORIGINALS = ROOT / "assets/brand/originals"
DEFAULT_OUTPUT = ROOT / "assets/brand-overlay"
# Official sources listed in assets/brand/README.md. Keep the downloaded bytes intact.
SOURCES = {
    "codex": ("https://cdn.oaistatic.com/assets/favicon-eex17e9e.ico",),
    "claude": ("https://claude.ai/apple-touch-icon.png",),
    "antigravity": (
        "https://www.gstatic.com/lamda/images/gemini_sparkle_v002_d4735304ff6292a690345.svg",
        "https://www.gstatic.com/lamda/images/gemini_favicon_f069958c85030456e93de685481c559f160ea06b.png",
    ),
}
SIZES = ((12, ""), (24, "@2x"), (36, "@3x"))
VECTOR_SIZE = 36 * 8
LIGHT_GLYPH = (245, 245, 245)
MAX_DOWNLOAD_BYTES = 4 * 1024 * 1024
MAX_PIXELS = 4 * 1024 * 1024
APPROVED_HOSTS = {"cdn.oaistatic.com", "claude.ai", "www.gstatic.com"}


class NoRedirects(HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        raise ValueError("brand downloads do not allow redirects")


urlopen = build_opener(NoRedirects()).open


def validate_url(url):
    parts = urlsplit(url)
    if (parts.scheme != "https" or parts.hostname not in APPROVED_HOSTS or
            parts.username or parts.password or parts.port not in (None, 443) or
            Path(parts.path).suffix.lower() not in (".png", ".ico")):
        raise ValueError("expected an approved HTTPS PNG/ICO URL")
    return parts


def download(url: str, originals: Path, expected_sha256: str | None = None) -> Path:
    parts = validate_url(url)
    path = safe_path(originals / Path(parts.path).name)
    deadline = time.monotonic() + 20
    request = Request(url, headers={
        "User-Agent": "Mozilla/5.0 (compatible; Officelings-local-brand-fetch/1.0)",
    })
    with urlopen(request, timeout=5) as response:
        if response.geturl() != url:
            raise ValueError("unexpected redirected download")
        expected_mimes = {"image/png"} if parts.path.endswith(".png") else {"image/x-icon", "image/vnd.microsoft.icon"}
        if response.headers.get_content_type() not in expected_mimes:
            raise ValueError("unexpected download Content-Type")
        length = response.headers.get("Content-Length")
        if length is not None and (int(length) < 0 or int(length) > MAX_DOWNLOAD_BYTES):
            raise ValueError("download byte budget exceeded")
        data = bytearray()
        while True:
            if time.monotonic() >= deadline:
                raise ValueError("download deadline exceeded")
            block = response.read(min(65536, MAX_DOWNLOAD_BYTES + 1 - len(data)))
            if not block:
                break
            data.extend(block)
            if len(data) > MAX_DOWNLOAD_BYTES:
                raise ValueError("download byte budget exceeded")
        if time.monotonic() >= deadline:
            raise ValueError("download deadline exceeded")
    data = bytes(data)
    if expected_sha256 is not None and hashlib.sha256(data).hexdigest() != expected_sha256.lower():
        raise ValueError("download SHA-256 mismatch")
    image = raster_bytes(data, "PNG" if parts.path.lower().endswith(".png") else "ICO")
    image.close()
    atomic_write(path, data)
    print(f"Original: {path}")
    return path


def load_raster(path: Path) -> Image.Image:
    return raster_bytes(read_bytes(path, MAX_DOWNLOAD_BYTES))


def raster_bytes(data: bytes, expected_format: str | None = None) -> Image.Image:
    with warnings.catch_warnings():
        warnings.simplefilter("error", Image.DecompressionBombWarning)
        with Image.open(BytesIO(data), formats=("PNG", "ICO")) as source:
            if expected_format is not None and source.format != expected_format:
                raise ValueError("download magic does not match its URL/MIME")
            if max(source.size) > 4096 or source.width * source.height > MAX_PIXELS:
                raise ValueError("raster dimension/pixel budget exceeded")
            if source.format == "ICO":
                sizes = source.ico.sizes()
                if any(max(size) > 4096 or size[0] * size[1] > MAX_PIXELS for size in sizes):
                    raise ValueError("ICO frame pixel budget exceeded")
                source = source.ico.getimage(max(sizes, key=lambda size: size[0] * size[1]))
            return source.convert("RGBA")


def render_svg(path: Path) -> Image.Image:
    raise ValueError("SVG inputs are disabled; use the official PNG fallback")


def clean_alpha(source: Image.Image) -> Image.Image:
    """Clear hidden RGB only; retain all nonzero alpha, including antialiased edges."""
    image = source.convert("RGBA")
    transparent = image.getchannel("A").point(lambda alpha: 255 if alpha == 0 else 0)
    image.paste((0, 0, 0, 0), (0, 0), transparent)
    return image


def prepare_source(provider: str, source: Image.Image, recolor: bool = False) -> Image.Image:
    image = source.convert("RGBA")
    if min(image.size) < 36 or image.getchannel("A").getbbox() is None:
        raise ValueError(f"{provider}: need a nonempty source of at least 36px per side")
    if provider == "claude":
        # The apple-touch PNG is opaque; keep its orange tile and white glyph,
        # applying only the app-icon corner mask, at the original resolution.
        mask = Image.new("L", image.size, 0)
        ImageDraw.Draw(mask).rounded_rectangle(
            (0, 0, image.width - 1, image.height - 1), radius=min(image.size) * 0.22, fill=255)
        image.putalpha(ImageChops.multiply(image.getchannel("A"), mask))
    elif provider == "codex" and recolor:
        # Derive the light variant from the official glyph's exact alpha; no redraw.
        light = Image.new("RGBA", image.size, (*LIGHT_GLYPH, 0))
        light.putalpha(image.getchannel("A"))
        image = light
    return clean_alpha(image)


def render_icon(source: Image.Image, size: int) -> Image.Image:
    # Premultiplication prevents invisible RGB from contaminating translucent edges.
    fitted = ImageOps.contain(source.convert("RGBa"), (size, size),
                              method=Image.Resampling.LANCZOS).convert("RGBA")
    canvas = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    canvas.paste(fitted, ((size - fitted.width) // 2, (size - fitted.height) // 2))
    return clean_alpha(canvas)


def fetch_icons(originals: Path = DEFAULT_ORIGINALS, output: Path = DEFAULT_OUTPUT,
                recolor: bool = False) -> list[Path]:
    originals, output = safe_path(originals), safe_path(output)
    make_directory(originals)
    images = {provider: load_raster(download(SOURCES[provider][0], originals))
              for provider in ("codex", "claude")}
    images["antigravity"] = load_raster(download(SOURCES["antigravity"][1], originals))

    prepared = {provider: prepare_source(provider, image, recolor)
                for provider, image in images.items()}
    make_directory(output)
    files = []
    for provider, image in prepared.items():
        for size, suffix in SIZES:
            path = output / f"{provider}{suffix}.png"
            # Every size comes from the original, never from the 12px output.
            stream = BytesIO()
            render_icon(image, size).save(stream, format="PNG")
            atomic_write(path, stream.getvalue())
            files.append(path)
    return files


def argument_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--originals", type=Path, default=DEFAULT_ORIGINALS,
                        metavar="DIR", help="raw downloads (default: assets/brand/originals)")
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT,
                        metavar="DIR", help="PNG overlay (default: assets/brand-overlay)")
    parser.add_argument("--install", action="store_true",
                        help="also copy the nine PNGs to %%LOCALAPPDATA%%/Officelings/brand")
    colors = parser.add_mutually_exclusive_group()
    colors.add_argument("--no-recolor", dest="recolor", action="store_false",
                        help="keep original colors (default); still clean alpha and round Claude corners")
    colors.add_argument("--recolor", dest="recolor", action="store_true",
                        help="opt in to a light ChatGPT glyph; Claude and Gemini colors stay original")
    parser.set_defaults(recolor=False)
    return parser


def install_icons(files: list[Path]) -> Path:
    local_app_data = os.environ.get("LOCALAPPDATA")
    if not local_app_data:
        raise ValueError("--install needs LOCALAPPDATA on Windows; otherwise use --output DIR")
    directory = safe_path(Path(local_app_data) / "Officelings/brand")
    make_directory(directory)
    for path in files:
        target = directory / path.name
        if safe_path(path) != safe_path(target):
            atomic_write(target, read_bytes(path, MAX_DOWNLOAD_BYTES))
    return directory


def main(argv: list[str] | None = None) -> int:
    args = argument_parser().parse_args(argv)
    try:
        files = fetch_icons(args.originals, args.output, args.recolor)
        installed = install_icons(files) if args.install else None
    except (OSError, RuntimeError, ValueError) as error:
        print(f"Brand fetch failed: {error}", file=sys.stderr)
        return 1
    mode = "light ChatGPT glyph" if args.recolor else "original colors"
    print(f"Wrote {len(files)} local PNGs (12/24/36px, {mode}) to {safe_path(args.output)}")
    if installed is not None:
        print(f"Installed the local runtime overlay in {installed}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
