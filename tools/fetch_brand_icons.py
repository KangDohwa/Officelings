"""Fetch optional, local-only official icons; never build or modify the asset pack."""

from __future__ import annotations

import argparse
from io import BytesIO
import os
from pathlib import Path
import shutil
import sys
from urllib.parse import urlsplit
from urllib.request import Request, urlopen

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


def download(url: str, originals: Path) -> Path:
    request = Request(url, headers={
        "User-Agent": "Mozilla/5.0 (compatible; Vibedotcom-local-brand-fetch/1.0)",
    })
    with urlopen(request, timeout=20) as response:
        data = response.read()
    path = originals / Path(urlsplit(url).path).name
    path.write_bytes(data)
    print(f"Original: {path}")
    return path


def load_raster(path: Path) -> Image.Image:
    with Image.open(path) as source:
        if source.format == "ICO":
            # Pillow's default frame must not determine the quality of the 2x/3x icons.
            source = source.ico.getimage(max(source.ico.sizes(), key=lambda size: size[0] * size[1]))
        return source.convert("RGBA")


def render_svg(path: Path) -> Image.Image:
    """Use an already installed renderer, supersampling the vector before Lanczos."""
    try:
        import cairosvg

        data = cairosvg.svg2png(bytestring=path.read_bytes(),
                               output_width=VECTOR_SIZE, output_height=VECTOR_SIZE)
        with Image.open(BytesIO(data)) as source:
            return source.convert("RGBA")
    except Exception as error:
        # CairoSVG may be installed without its native Cairo library on Windows.
        raise RuntimeError(f"SVG renderer unavailable: {error}") from error


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
    originals.mkdir(parents=True, exist_ok=True)
    images = {provider: load_raster(download(SOURCES[provider][0], originals))
              for provider in ("codex", "claude")}
    try:
        svg = download(SOURCES["antigravity"][0], originals)
        images["antigravity"] = render_svg(svg)
        print(f"Gemini: vector rendered at {VECTOR_SIZE}px")
    except (OSError, RuntimeError, ValueError) as error:
        print(f"Gemini: {error}; using the official high-resolution PNG.", file=sys.stderr)
        images["antigravity"] = load_raster(download(SOURCES["antigravity"][1], originals))

    prepared = {provider: prepare_source(provider, image, recolor)
                for provider, image in images.items()}
    output.mkdir(parents=True, exist_ok=True)
    files = []
    for provider, image in prepared.items():
        for size, suffix in SIZES:
            path = output / f"{provider}{suffix}.png"
            # Every size comes from the original, never from the 12px output.
            render_icon(image, size).save(path, format="PNG")
            files.append(path)
    return files


def argument_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--originals", type=Path, default=DEFAULT_ORIGINALS,
                        metavar="DIR", help="raw downloads (default: assets/brand/originals)")
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT,
                        metavar="DIR", help="PNG overlay (default: assets/brand-overlay)")
    parser.add_argument("--install", action="store_true",
                        help="also copy the nine PNGs to %%LOCALAPPDATA%%/Vibedotcom/brand")
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
    directory = Path(local_app_data) / "Vibedotcom/brand"
    directory.mkdir(parents=True, exist_ok=True)
    for path in files:
        target = directory / path.name
        if path.resolve() != target.resolve():
            shutil.copyfile(path, target)
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
    print(f"Wrote {len(files)} local PNGs (12/24/36px, {mode}) to {args.output.resolve()}")
    if installed is not None:
        print(f"Installed the local runtime overlay in {installed}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
