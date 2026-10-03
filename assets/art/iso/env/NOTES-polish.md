# ENV polish handoff

This handoff supersedes the pixel-quality limitations in NOTES-A1.md / NOTES-A2.md;
those files remain the historical first-pass record. Only this ENV lane was written.
Furniture, characters, pack, tools, shared specifications and git history were untouched.

## Delivered pixels and fixed contract

- Existing `floors/floors.png`, `walls/walls.png` and `walls/walls-corner.png` retain
  their filenames, cell sizes, atlas positions and source anchors. Both original
  `.iso.json` files are byte-for-byte unchanged; their SHA256s are in `review-geometry.txt`.
- Navy walls have inset wainscot panels, a beveled wood dado at local v=28..32,
  a warm 2px crown and 2px baseboard inside the 48px height. Upper panels darken
  vertically; left panels/bevels are brighter than independently authored right ones.
- Window clear width is 26 face pixels (formerly 20), with a 30px wooden frame,
  cross mullion, sparse stars, skyline silhouettes and a small distant warm glow.
  Glass remains its separate opaque backdrop layer. Curtains are optional overlays.
- Closed door clear width is 21 face pixels (formerly 16), with inset wood panels,
  carved grain, handle/backplate, frame and separate threshold. The widening is
  on the latch side: the frozen t=8 hinge, open-leaf geometry and manifest hinge
  fields still agree. The open leaf remains a contained narrow projected plane.
- Wood uses six distinct deterministic cells: warm body/light/darker worn clusters,
  broad irregular grain, staggered butt joints, occasional small knots and scuffs.
  All seam-contact texels are identical across the set. Base transitions were repainted;
  optional checker trim adds a narrow wood/brass-colored border inside pantry cells.
- Separate wall AO cells use only navy #111924 at alpha 96 contact / 48 falloff.
  Floor and wall body/decor art retains alpha 0/255; no filtered blur or smoothing.

## Optional cells in `polish.iso.json`

These are additive source exports, **not** runtime integration. The original 11 floor
cells / 15 logical wall modules remain present. The room review demonstrates optional
variants, checker trim, AO and wall decor. An integrator must select/place these cells
explicitly; the existing runtime does not acquire them merely by discovering this file.

| Sheet / file | Cells | Source anchor | Selection |
| --- | --- | --- | --- |
| `iso-env-wood-variants` / `floors/wood-variants.png` | 6 rows of 64×32 | (32,16) | `wood-i-v0`..`v5`; row 0 also equals original `wood-i` |
| `iso-env-checker-border` / `floors/checker-border.png` | 10 rows of 64×32 | (32,16) | Rows 0/1 are dark/light checker bases; rows 2..9 are 8 trim overlays |
| `iso-env-wall-decor` / `walls/wall-decor.png` | 8 rows of 32×64 | L (32,48), R (0,48), per clip | L curtain/picture/clock/string-lights rows 0..3; R rows 4..7 |
| `iso-env-wall-contact` / `walls/wall-contact-shadows.png` | 2 columns of 128×128 | (64,64) | Left column 0, right column 1; row 0 |

Wood hash is unsigned 32-bit throughout (negative coordinates first become unsigned):

```text
h = ((uint32(i) * 0x1f123bb5) XOR (uint32(j) * 0x5f356495) XOR 0x6a09e667)
h = uint32((h XOR (h >> 16)) * 0x45d9f3b)
h = h XOR (h >> 16)
variant = h % 6
```

Use unsigned shifts; mask multiplications to 32 bits in languages with wider integers.
The exact executable recipe is `variant_index` in `polish_env.py`. Do not rotate cells
or randomly choose plank axes. Checker bases still use `(i+j)%2`, phase 0 dark / 1 light.

On a checker tile, apply border overlay for every cardinal edge facing wood. Border
rows 2..5 correspond to `+i,+j,-i,-j`; corner rows 6..9 correspond to adjacent edge
pairs `(0,1),(1,2),(2,3),(3,0)`. Corners follow edges. On the wood side apply the
original four edge / four corner overlays. No outline is added between checker tiles.

AO is anchored at the host wall's **first ground endpoint**: draw destination
`snapped P(edge.i,edge.j,0) - (64,64)`. Travel is one segment along +j for L / +i
for R; the falloff extends into the room. Draw after floors/material transitions and
before rear walls; clip to the room floor. Exterior wall height does not enter this
ground-layer placement. Two sides may overlap at the corner for contact occlusion.

## Wall mounting slots

Wall face-space t runs 0..32 from the first endpoint; v runs downward 0..48 from
the wall top. L source x=32-t; R source x=t; both source y=v+t/2. Mounting points
below are stable integer source coordinates; alpha at the semantic pivot can be
transparent, and the clip's separate `anchorChecks` identify a real attachment texel.

| Overlay | Wall mount (t,v) | Source mounting point L/R | Offset from first edge endpoint L / R |
| --- | --- | --- | --- |
| Curtain rod center | (16,6), z=42 | (16,14) / (16,14) | (-16,-34) / (16,-34) |
| Picture frame top center | (16,9), z=39 | (16,17) / (16,17) | (-16,-31) / (16,-31) |
| Clock top rim | (16,9), z=39 | (16,17) / (16,17) | (-16,-31) / (16,-31) |
| String cable center | (16,6), z=42 | (16,14) / (16,14) | (-16,-34) / (16,-34) |

String-light hook points are (t=6,v=8) and (t=25,v=8). Their snapped source points
are L `(26,11),(7,21)` and R `(6,11),(25,21)`. Small bulbs are included in this
transparent attachment cell; no room-light shader or broad glow is baked into it.
Use one face-specific overlay per slot, after its host wall. To align an external
decoration to a slot, subtract that decoration's own source mounting point from
the world slot point in the table. Hanging height changes pixels, not host depth.

## Reviews, reproduction and verification

`polish_env.py` borrows half-open masks and placement from the existing generator
`runs/iso-env/build_env.py`, preserving the original generation evidence. It repaints
pixels programmatically; do not rerun the historical generator to reproduce polish.

```powershell
$env:SPRITE_GEN_ROOT = 'C:/Users/myori/.codex/skills/sprite-gen'
$env:PYTHONIOENCODING = 'utf-8'
& "$env:SPRITE_GEN_ROOT/.venv/Scripts/python.exe" assets/art/iso/env/polish_env.py
& "$env:SPRITE_GEN_ROOT/.venv/Scripts/python.exe" tools/iso_check.py assets/art/iso/env |
  Set-Content -Encoding UTF8 assets/art/iso/env/iso-check.txt
```

- Refreshed `room-shell-review.png` / `-3x`, all existing floor reviews and transitions,
  wall room reviews, closed-door room and module review. Added `polish-detail-review.png`.
- Final checker: **38 PASS, 18 inapplicable SKIP, 0 FAIL**; see `iso-check.txt`.
  Tolerances and checker source were not changed.
- Generator checks all **72 ordered wood-neighbor pairs** (36 across each grid axis);
  measured boundary RGB difference is **0**. It checks hash-selected 10×10 ground
  coverage, original layer contacts, an independently computed closed-wall envelope,
  open-wall coverage and exact nearest-neighbor room expansion. Evidence is in
  `review-geometry.txt`. Open leaves intentionally project in front of the wall plane.
- Viewed 10 images total, including original room, hero, existing generated material,
  iterative rooms, final native room, module details and all 16 transition cases.
  Geometry checks do not prove painterly quality or runtime occlusion.
- No new still generation ran. The requested codex workflow was probed with the
  installed sprite-gen environment; it failed configuration loading with
  `Could not find home directory`. This is an environment error, not evidence
  that the account is logged out. No provider switch, login/config mutation or
  external runner was used.

## Time and remaining visual weaknesses

One ENV lane, approximately **0.21 lane-hours** including reading, drawing, review
and validation; measured from 2026-10-03 19:58:59 UTC to 2026-10-03 20:11:31 UTC.
No concurrent lane hours are counted, and this is not a generation-throughput metric.

The shell is richer, but the small one-edge window and especially the open leaf still
read smaller than the hero. Navy wall shading is restrained and the wainscot bevels
remain graphic. Long plank bands and a few reused wear motifs can still be recognized
in an empty room; six variants break the identical one-cell repeat rather than creating
100 unique tiles. The third darker wood tone is concentrated in worn grain/knots,
not entire dark planks, to keep the room warm. The hero's softer painted material
depth and accumulated furniture/ambient detail are not fully matched by this shell.
Optional cells require integrator wiring; no engine playback or actor/door-crossing
test was run in this art-only lane. No git commit was made.
