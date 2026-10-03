# Brand icons (local use only)

Official provider icons are not redistributed with this project. See [TRADEMARKS.md](../../TRADEMARKS.md) for owner guidelines and the [release checklist](../../docs/release-checklist.md) for public-release work.

The optional fetch tool downloads the originals below onto your own machine and writes a local PNG overlay. It does not run a build, rebuild the pack or enable embedding. Default builds use generic icons with `EmbedBrandOverlay=false`; keep that default for builds you share. Downloading an overlay does not change this setting.

| Provider | Icon | Official source (checked 2026-10-04) |
| --- | --- | --- |
| Claude (Anthropic) | apple-touch icon | https://claude.ai/apple-touch-icon.png |
| Gemini (Google), used for Antigravity | sparkle SVG / favicon PNG | https://www.gstatic.com/lamda/images/gemini_sparkle_v002_d4735304ff6292a690345.svg , https://www.gstatic.com/lamda/images/gemini_favicon_f069958c85030456e93de685481c559f160ea06b.png |
| ChatGPT (OpenAI), used for Codex | favicon | https://cdn.oaistatic.com/assets/favicon-eex17e9e.ico (the www.openai.com / chatgpt.com hosts block scripted downloads) |

These are provider-hosted UI assets, not a grant of permission to use or redistribute the marks. Local-only fetching is not a legal determination that personal use is permitted. Review the current [OpenAI guidelines](https://openai.com/brand/), [Anthropic trademark guidelines](https://www.anthropic.com/legal/trademark-guidelines) and [Google guidance](https://about.google/brand-resource-center/guidance/) before displaying or sharing them. Resizing, corner masking, recoloring and small display sizes may conflict with brand requirements even when original colors are retained. The Gemini-to-Antigravity mapping is the project's choice, not Google's designation of an Antigravity logo.

## Optional local usage

Use an existing Python environment with Pillow, which the project's art tools also use. The tool never installs dependencies. If a working CairoSVG renderer is already installed, the Gemini SVG is rasterized at 288px first. Otherwise it uses the official 256px PNG, so an SVG renderer is not required.

From the repository root:

```powershell
python -B tools/fetch_brand_icons.py --no-recolor
# Equivalent Windows wrapper; select your existing environment if needed:
./tools/fetch_brand_icons.ps1 -Python 'C:\path\to\venv\Scripts\python.exe' -NoRecolor
```

Both commands default to original colors (`--no-recolor` / `-NoRecolor` can be omitted). This conservative default avoids changing the glyph colors, but does not guarantee brand-guideline compliance. Each invocation downloads the sources again; network access is needed. A fetch or processing failure returns a nonzero exit code. If the SVG download or renderer is unavailable, the official PNG is tried instead.

- Raw downloads keep their original bytes and URL filenames in git-ignored `assets/brand/originals/`. The fallback PNG is downloaded when needed.
- The nine outputs go to git-ignored `assets/brand-overlay/`: `codex.png`, `claude.png`, `antigravity.png` on 12x12 canvases, plus `codex@2x.png`, `claude@2x.png`, `antigravity@2x.png` on 24x24, and matching `@3x.png` files on 36x36.
- Every size is fitted proportionally and downsampled directly from the largest original ICO frame, original PNG or supersampled SVG using premultiplied-alpha Lanczos. The 2x/3x files are never enlarged from the 12px output. Fully transparent pixels have zero RGB; partial alpha and glyph holes are retained.
- Claude retains the orange square and white glyph. Only its corners are masked, at the original resolution, with a radius of 22% of the tile width to make a rounded app icon. Gemini retains its gradient. Neither logo is redrawn or reduced to a monochrome silhouette.

For a light ChatGPT glyph on a dark background, explicitly opt in:

```powershell
python -B tools/fetch_brand_icons.py --recolor
# Or: ./tools/fetch_brand_icons.ps1 -Recolor
```

`--recolor` uses the official ChatGPT glyph's existing alpha mask with light RGB `(245, 245, 245)`; it does not redraw the glyph or change Claude's orange tile or Gemini's gradient. `--recolor` and `--no-recolor` are mutually exclusive. The default black ChatGPT glyph may be hard to see on dark backgrounds; choosing the light variant is an explicit local choice that may raise additional brand-guideline concerns.

Override the directories when needed:

```powershell
python -B tools/fetch_brand_icons.py --originals 'C:\local\brand originals' --output 'C:\local\brand overlay' --no-recolor
./tools/fetch_brand_icons.ps1 -Originals 'C:\local\brand originals' -Output 'C:\local\brand overlay' -NoRecolor
# Optional existing per-user overlay location:
python -B tools/fetch_brand_icons.py --output "$env:LOCALAPPDATA\Vibedotcom\brand"
```

The default directories are resolved relative to the repository, independent of your working directory. Relative overrides are resolved from your working directory. Only the named downloads and nine PNGs are written; unrelated files are left in place. Override directories are not automatically git-ignored, so keep any official originals and outputs out of commits and shared builds.

## Using the overlay in the app

The runtime does not scan the repository's `assets/brand-overlay/` directory. Its precedence is valid per-provider icons in `%LOCALAPPDATA%\Vibedotcom\brand\`, then optional embedded icons, then the generic pack. It uses the nearest valid density when a density is missing or corrupt.

To copy the nine generated PNGs into the per-user runtime folder, explicitly use `--install`. This replaces those nine filenames there, preserves unrelated files and does not copy the originals:

```powershell
python -B tools/fetch_brand_icons.py --recolor --install
./tools/fetch_brand_icons.ps1 -Recolor -Install
# The wrapper also accepts --install as an alias for -Install.
```

Alternatively, manually copy the PNGs there or use `--output "$env:LOCALAPPDATA\Vibedotcom\brand"`. Existing icons in that folder take precedence over embedded icons. Embedding the repository overlay requires an explicit local App build with `-p:EmbedBrandOverlay=true`; the fetch tool leaves the default `EmbedBrandOverlay=false` unchanged. Do not share builds containing official icons without reviewing the owners' requirements and obtaining any needed permission.

Run the isolated tests (all downloads are mocked):

```powershell
python -B -m unittest discover -s tools -p test_fetch_brand_icons.py
```
