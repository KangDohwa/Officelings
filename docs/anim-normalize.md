# Prop animation normalization

`tools/anim_normalize.py` aligns frames to frame zero and copies that frame's RGBA bytes into every non-animated pixel. It corrects whole-frame integer shifts and regenerated body/cabinet details without resampling. Use the existing sprite-gen environment with Pillow and NumPy.

```powershell
& 'C:\Users\<user>\.codex\skills\sprite-gen\.venv\Scripts\python.exe' -B tools/anim_normalize.py
& 'C:\Users\<user>\.codex\skills\sprite-gen\.venv\Scripts\python.exe' -B tools/test_anim_normalize.py
```

The CLI reads 64×64 props in `assets/art/env2/sheets`, optional `assets/art/env3/sheets`, and recognized prop manifests in `assets/art/sheets/locked`. Locked human-character sheets and 32×32 environment tiles/walls are excluded. It also reads `assets/art/sheets/props.png` with its manifest. The TV, coffee machine and water cooler in this source set live there.

Outputs go to `assets/art/env2/normalized/`:

| Source | Output, retaining PNG and manifest filenames |
| --- | --- |
| env2 props | Directory root |
| env3 props | `env3/` |
| locked props, when present | `locked/` |
| Original mixed `props` sheet | `base/props.png`, `base/props.manifest.json` |

The source files stay unchanged. The CLI hashes PNGs and manifests before and after processing. It preserves `animation`, `frame_layout`, cell dimensions, timing, anchors, unrelated states and unlisted trailing cells. Source provenance paths are rebased in the copied manifests. It does not build or install a runtime pack.

`--output DIR`, `--qa-dir DIR`, and `--max-shift N` override the defaults. The default integer search radius is ±6 pixels on each axis. Output and QA directories must be separate from all source sheet directories. Reruns replace generated files with the same names; they do not delete other files.

## Regions and alignment

Each animation row has its own frame-zero reference. `frame_layout.rows` supplies the exact crop rectangles. `variants` rows, and the original mixed sheet's plants, door and clock choices, are preserved individually. The desk's off/on/typing choices retain their screen changes while their common body is stabilized.

| Prop rule | Animated pixels retained |
| --- | --- |
| TV / monitor / original desk | Screen interiors detected from connected cool-colour regions; an off screen uses aligned on-frame colours |
| Server rack | Compact substantial interior change clusters, retaining LEDs |
| Coffee machine | Compact interior clusters and changing pale/transparent steam above the body |
| Water cooler | Compact interior clusters and changing cool-colour water vessel pixels |
| Ceiling light | Pixels with intermediate alpha in any frame, retaining glow |
| Cat / dog / pet / robot vacuum | Union of visible sprite pixels: full body, paws, tail and brush animation |
| Other props | Automatic compact change clusters, excluding silhouette edges and small colour noise |

Alignment uses integer cross-correlation of opaque alpha and silhouette outlines, with deterministic preference for the smallest displacement on ties. Steam uses the lower opaque body; translucent glow is excluded from alignment. Transparent translation fill never wraps. A correction that clips visible pixels is rejected. There is no stretching or palette/alpha conversion.

Automatic clustering is a heuristic for static bodies. It can suppress subtle intended changes or retain compact unintended changes. Inspect new prop types and add a small `PROP_RULES` entry or pass an explicit mask. Unknown screen colours that cannot be detected raise an error instead of silently freezing the screen. For full animation, alignment corrects cell drift but can also remove intentional whole-sprite translation; intended world movement should remain in the runtime.

## Report and QA

`report.json` records source hashes and per-row results. The CLI prints each prop's shift, percentage fixed, animation bbox and static RGBA check. `PASS` means all RGBA channels outside the animation mask exactly match frame zero. Single-frame and unrelated design choices are labelled separately.

- `max_shift_fixed`: maximum absolute applied translation on either axis, in pixels.
- `pixels_made_identical_pct`: differing non-animated frame-pixels actually replaced, divided by `(frames − 1) × cell width × cell height`, including transparent cells. Already equal pixels are not counted. `static_region_pct` separately measures how much of the cell is locked.
- `animated_region_bbox`: `[x0, y0, x1, y1]` in reference cell coordinates, with exclusive upper bounds; `null` means no animated region. The bbox encloses a potentially nonrectangular mask.
- `shifts_applied`, `varying_pixels_before/after`, and `warnings` support inspection.

Four labelled before/after strips are written to `%TEMP%\anim-qa` as PNGs at 4× nearest-neighbor scale. They cover TV, server rack, coffee machine and water cooler. The coffee machine and water cooler in this source set each have one frame; their strips show that static input unchanged. The env2 monitors are different static designs, and the ceiling glow also has one frame. The tool does not invent animation for them.

## Importable API

```python
from tools.anim_normalize import normalize_frames, normalize_sheet

# Pillow input; no filesystem writes or input mutation.
new_sheet, report = normalize_sheet(image, production_manifest, max_shift=6)

# A clip: the optional bool mask has shape (height, width), in frame-zero coords.
new_frames, animated_mask, report = normalize_frames(
    frames, prop="tv", max_shift=6,
    # animated_mask=reviewed_mask,
    # mode="full-animation",  # preserve intended whole-body motion
)
```

A script running directly from `tools/` can use `from anim_normalize import ...`. `normalize_sheet` returns a new sheet and per-row report, preserving the supplied manifest. `normalize_frames` also returns the detected mask for a caller's QA. The runtime pack builder calls this API for base/env2/env3 furniture and prefers available env2 normalized sheets. Runtime pack tests independently check fixed body pixels; rerun `tools/build_pack.py` before rebuilding the app.
