# Environment Pack 3 — Side-view workstations

Load `sheets/*.png` with the sibling `*.manifest.json`. These manifests copy the **same schema** as `../env2/sheets/desks.manifest.json`: `frame_layout.rows[state][frame]` is authoritative. All row, frame and pixel coordinates are zero-based. Static `variants` rows are choices, not animations.

All furniture uses **64×64** transparent cells, no padding between cells, bottom anchor **(32,61)**, matching env2. PNGs use straight RGBA with binary alpha, including byte-zero RGB in transparent pixels. Scale with nearest-neighbor sampling. The preview uses env2's **32×32** floor/wall grid. `palette.json` is a byte-identical copy of env2's palette; **no colors were added**. The delivered props use a compact subset of that palette.

## Facing and sheets

`left` / `right` name the **seated worker's viewing direction**, not the direction of the monitor's glass. A right-facing worker sits to the desk's left; its CRT screen faces the worker toward the left. The cream CRT's rear housing extends right. Left-facing props reverse this arrangement. These are side-profile elevations with a little tabletop visible, not overhead pixel rotations.

| Sheet | Appearance / rows |
|---|---|
| `desk-side-wood-left.png`, `desk-side-wood-right.png` | honey/walnut desk, cream side-profile CRT, keyboard, ivory mug |
| `desk-side-white-left.png`, `desk-side-white-right.png` | same silhouette, ivory tabletop/apron, walnut legs and drawer |
| `desk-side-double-wood-left.png`, `desk-side-double-wood-right.png` | wooden desk with two cream CRTs staggered in depth; both screens animate |
| `desk-side-double-white-left.png`, `desk-side-double-white-right.png` | same double-CRT desk with ivory tabletop/apron |
| `office-chair-side-left.png`, `office-chair-side-right.png` | complete teal side-profile office chair; row `variants`, 1 frame |
| `office-chair-side-left-back.png`, `office-chair-side-right-back.png` | chair backrest, seat body and wheeled base behind the character; row `variants`, 1 frame |
| `office-chair-side-left-front.png`, `office-chair-side-right-front.png` | near armrest/support and seat front lip in front of the character; row `variants`, 1 frame |
| `laptop-table-side-left.png`, `laptop-table-side-right.png` | matching wooden overflow table with open side-profile laptop and static idle screen; row `variants`, 1 frame |

Every desk sheet is **192×192** (3 columns × 3 rows):

| Row | State | Frames / timing |
|---|---|---|
| 0 | `off` | 1, dark screen, 1 fps, `loop:false` |
| 1 | `on` | 1, idle terminal code dashes, 1 fps, `loop:false` |
| 2 | `typing` | 3, terminal/glow cycle, 3 fps, 333 ms per frame, `loop:true` |

The two trailing cells in rows 0 and 1 are transparent. Chair/laptop sheets are **64×64**, 1 fps, `loop:false`. Code is represented by tiny horizontal dashes seen edge-on, not legible writing. No people are included in the production sheets.

## Seating and layers

These are source-pixel **pelvis/seat contact anchors**, measured from each chair cell's top-left. They are not foot anchors. Align the seated character's pelvis/contact point to this coordinate; adapt the offset to that character's own seated pose.

| Worker facing | Chair seat anchor (x,y) | Chair-cell offset from desk-cell top-left (x,y) | Seat in desk-cell coordinates (x,y) |
|---|---|---|---|
| `right` | **(31,44)** | **(-26,0)** | **(5,44)** |
| `left` | **(32,44)** | **(26,0)** | **(58,44)** |

These offsets work for the single CRT, double CRT and laptop table. Desk and chair use the same bottom anchor (32,61). Keep the full 64×64 canvas of each layer; do not independently crop or center the pieces. The front layer deliberately contains separated pieces and is not a standalone complete chair.

Preview draw order: **chair back → seated character → desk/table → chair front**. This lets the near desk edge cover legs beneath the tabletop while the armrest and seat lip cover the character correctly. Back plus front, without a character, reconstructs the complete chair byte-for-byte. Use either the complete chair or the layer pair, not both. `scene.json` also records numeric seat anchors, placements and draw order for all eight preview seats.

Seating coordinates live here and in `scene.json`; no additional fields were inserted into env2's sheet manifest schema. These remain production manifests, not the runtime asset format from `docs/asset-format.md`. Runtime asset/layout adaptation and seated character art are separate work.

## Animation identity guarantee and check

The desk body, legs, drawer, monitor casing/stand, keyboard and mug are **pixel-identical across all five declared frames**, including `off` versus `on` versus `typing`. Chairs are separate static pieces. Only these inclusive screen-interior rectangles may change:

| Facing | Main CRT screen | Additional CRT screen |
|---|---|---|
| `right` | (38,26)–(39,34), 18 pixels | (32,20)–(33,28), 18 pixels |
| `left` | (24,26)–(25,34), 18 pixels | (30,20)–(31,28), 18 pixels |

Glow is confined to the screen plane; no animated light is painted onto the casing, desk or props. `qa/masks/*.png` publishes the precise masks. `build_env3.py check` reloads the final exported PNGs and compares **all four RGBA bytes** outside those masks against the off frame. It also checks unchanged alpha, distinct monitor frames, exact mask geometry, schema keys, frame rectangles/counts, transparent trailing cells, palette membership, margins, chair layer reconstruction and authored left/right mirroring. A deliberately altered desk-body pixel is rejected by a negative control. `qa/export-report.json` records per-sheet results and the non-screen byte hashes.

```powershell
$env:PYTHONDONTWRITEBYTECODE='1'
& 'C:\Users\myori\.codex\skills\sprite-gen\.venv\Scripts\python.exe' assets/art/env3/build_env3.py check
```

The check uses Pillow and Python's standard library only: no generation tool, external QA command, network or model call. Replace `check` with `build` to deterministically rebuild sheets, contacts, report and preview from the retained extracted frames. The builder writes only within env3 and reads env2 as reference data.

## Preview, provenance and limits

`preview-env3.png` is a **2× nearest-neighbor** export of a **640×352** composition: **1280×704**. Its two rows show left-facing and right-facing workers, each with wooden, white, double-monitor and laptop workstations. All eight people are deliberately faceless **placeholder seated silhouettes**, drawn for arrangement/depth review. Floor and wall pixels are read-only env2 assets. `qa/contact-desks-1.png`, `qa/contact-desks-2.png` and `qa/contact-chairs-laptops.png` show every delivered frame and the seating layers.

Generated with **sprite-gen, provider=codex, still-image component-row pipeline**. Two generation calls ran sequentially: an original reference strip from `prompt-base.txt`, then one static component row via `prepare` / `gen-set --concurrency 1`. No video or generated animation frames. No existing env2 image was uploaded to the provider; its style/palette were inspected locally. The newly generated reference and layout guide are retained under `runs/`. Canonical connected-component extraction with `kcentroid` fitting succeeded without extraction warnings. The pipeline atlas is retained in `runs/props/`; authoritative final sheets are in `sheets/`.

`build_env3.py` fits the extracted props to native cells, locks materials to env2 colors, derives left views by exact horizontal mirroring, recolors the white top, staggers a duplicate CRT for the double-monitor desk, splits chair layers and draws the screen frames deterministically. These are documented assembly edits, not untouched model output. The pipeline inspector reported low silhouette similarity for `static_props` because its columns are three different objects, not poses of one object; that generic animation comparison is not a quality claim about these static alternatives.

Final inventory: **16 sheet PNGs + 16 sibling manifests, 48 declared frames**. Final contacts and the 2× preview were viewed. Screen-only identity checks passed for all eight desk sheets, and both chair layer pairs reconstruct exactly. This is an art pack and composition preview; it does not integrate runtime rotations, collisions, seats or character poses. No application build or test was run. No git commit was made.
