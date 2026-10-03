# Environment Pack 2

Load `sheets/*.png` with the sibling `*.manifest.json`. These manifests use the **same schema** as `../sheets/props.manifest.json` and `../sheets/tiles.manifest.json`: `frame_layout.rows[state][frame]` is authoritative. All row and frame indices are zero-based. Static variant rows are choices, not animations.

Tiles are **32×32**, opaque, no padding, top-left anchor **(0,0)**. Furniture, wall decorations, small objects and pets use **64×64** transparent cells, bottom anchor **(32,61)**, matching the existing prop convention. Smaller objects deliberately occupy less of the cell. PNGs have straight RGBA alpha; always scale with nearest-neighbor sampling. Only the ceiling glow uses intermediate alpha. Empty trailing cells are transparent.

## Floors

Each floor has one `tile` frame in a separate 32×32 sheet.

| Sheet | Appearance |
|---|---|
| `floor-wood-oak.png` | warm honey oak, four horizontal plank bands |
| `floor-wood-walnut.png` | deeper warm walnut |
| `floor-wood-staggered.png` | lighter honey, staggered plank ends |
| `floor-carpet-blue.png` | quiet dusty blue weave |
| `floor-carpet-red.png` | quiet brick red weave |
| `floor-checker-pantry.png` | warm ivory / charcoal checker, exactly 8×8 px squares |
| `floor-bathroom.png` | pale ceramic squares and muted grout |
| `floor-stone.png` | warm grey block stone |

## Walls

`wall-navy.png` and `wall-wood.png` each have seven one-frame rows in this order, **32×224** sheets:

| Row | State | Use |
|---|---|---|
| 0 | `panel` | straight horizontal navy or walnut panel, honey rail and dark baseboard |
| 1 | `top_left` | top edge with left outer trim |
| 2 | `top_right` | top edge with right outer trim |
| 3 | `inside_left` | left inner trim and returning rail |
| 4 | `inside_right` | right inner trim and returning rail |
| 5 | `door_frame` | honey doorway surround, dark open interior |
| 6 | `window_wall` | small four-pane teal window, walnut surround and honey sill |

These are explicit wall pieces for simple room compositions, **not a complete bitmask autotile set**. The doorway opening is 16 px wide within its tile; stack or repeat panels for a larger architectural composition. A transparent door leaf is not included.

## Furniture, decorations and ambient sprites

Unless specified below, each sheet has one row named `variants`, 1 fps, `loop:false`. Its columns select the following items.

| Sheet | Frames / columns |
|---|---|
| `bookshelf.png` | 0 three open shelves; 1 books over cream cabinet doors, small pot on top |
| `framed-pictures.png` | 0 mountain/sun; 1 botanical; 2 sleeping cat portrait; 3 geometric abstract |
| `wall-clock.png` | 0 ivory clock, 10:10 |
| `whiteboard.png` | 0 block diagrams, marker tray |
| `filing-cabinet.png` | 0 three cream drawers |
| `printer.png` | 0 desktop printer with paper |
| `meeting-table-chairs.png` | 0 compact walnut table and four teal chairs, joined grouping |
| `round-table.png` | 0 round honey top, central pedestal |
| `coffee-table.png` | 0 low walnut table with two magazines |
| `desks.png` | 0 walnut; 1 ivory top / walnut legs; monitors separate |
| `office-chairs.png` | 0 teal; 1 blue; 2 brick red; front view, matching rounded backrest / seat geometry |
| `monitors.png` | 0 old cream CRT; 1 modern charcoal; 2 dual monitors; keyboard included |
| `sofa-red.png` | 0 down/front, horizontal; 1 right-facing, vertical with back along left edge |
| `armchair.png` | 0 cream / teal with walnut feet |
| `rugs.png` | 0 dusty blue geometric; 1 brick red geometric; overhead view |
| `plants.png` | 0 monstera; 1 snake plant; 2 fern; 3 succulent; 4 ficus tree |
| `trash-bin.png` | 0 charcoal bin, cream rim / paper |
| `mug.png` | 0 ivory coffee mug, right handle |
| `vending-machine.png` | 0 navy drink machine |
| `fridge.png` | 0 ivory two-door fridge, small magnets |
| `kitchen-counter-sink.png` | 0 walnut cabinet, ivory top, sink / faucet |
| `microwave.png` | 0 ivory microwave, dark door |
| `lamp.png` | 0 standing cream-shade lamp |
| `ceiling-light-glow.png` | 0 warm fixture with hard-edged translucent halo bands |
| `server-rack.png` | row `blink`, 3 frames, 3 fps, loop; fixed cabinet, changing LED blocks |
| `cat.png` | row 0 `sleep`, 1 static curled tabby; row 1 `walk_right`, 2 contact poses, 4 fps, loop; trailing sleep cell empty |
| `robot-vacuum.png` | row `clean`, 2 frames, 3 fps, loop; compact round vacuum, brush / status variation |

`meeting-table-chairs` is a decorative grouping. Separate office chairs remain available for placement; no seating, collision or interaction fields have been added to the existing manifest schema. The sofa orientations are authored separately and are not rotated copies. The cat has only rightward walk poses; a runtime may mirror for leftward motion if appropriate, but no additional directions are delivered.

## Style, palette and provenance

Generated with **sprite-gen, provider=codex, still image component-row pipeline**. Each generation uses `--concurrency 1`; no video generation. Existing assets were references only. Original requests, generated strips, extraction records and provider reports are retained under `request-*.json`, `references/` and `runs/`. No reference-project art is copied into this pack.

Extraction uses sprite-gen's connected-component separator and canonical `kcentroid` fit. Mixed-size object rows use explicit outlier limits because a clock, board, mug and floor lamp intentionally have different occupied areas. No equal-width raw slicing is used. The initial pixel-unfake extraction of mixed-size rows hit a width cap; canonical kcentroid extraction preserves complete silhouettes before native-size fitting. `build_env2.py` then assembles individual sheets and locks colors, geometry and manifests. It writes only within this directory.

`palette.json` records colors from the existing props, tiles, worker, Claude/Antigravity worker and icon sheets. Six muted brick-red / dusty-blue accent shades are added for the requested red and blue furnishings; wood, ivory, teal, outlines and most other colors reuse existing RGB values. This is the existing warm palette family with these explicit additions, not a byte-identical palette.

Generated floor panels are normalized to full opaque cells; unwanted perimeter outlines are removed, plank joins / edge continuity are adjusted, and the pantry checker period is fixed at 8 px. Bathroom/stone use the existing warm-grey palette subset and restored 1 px grout, avoiding accidental mapping into the character's violet ramp. Wall faces are normalized above one rail at y=24; corners, doorway, window and navy panel inset are deterministic pixel edits of the two generated materials. Server LEDs are animated over a single accepted cabinet silhouette to avoid cabinet wobble. Vacuum body scale is similarly fixed while its brush and LED change. The ceiling halo uses discrete alpha bands. These assembly edits are documented rather than presented as untouched model output.

## Preview and checks

`preview-env2.png` is the **2× nearest-neighbor** export of the native **704×448** two-room composition: **1408×896**. `scene.json` and `qa/scene-render/scene.report.json` retain placement/source evidence. All environment furnishings in the composition are from env2; read-only existing character sprites are included to show scale and occupancy.

`qa/contact-props.png` and `qa/contact-tiles.png` show every delivered frame at enlarged native pixels. `qa/export-report.json` checks manifest schema keys, sheet/cell dimensions, frame counts and bounds, nonempty declared frames, opaque floor/wall tiles, and magenta residue. Pipeline manifests and extraction reports retain any generation/fit observations. Asset-only work: no app build or test is required or run.

Final inventory: **37 sheet PNGs + 37 sibling manifests, 69 declared frames**. All delivered frames and the final 2× preview were directly viewed. Plants, wood, navy wall and carpets were regenerated after visual critique; earlier versions are retained in `qa/rejected/`. `qa/contact-props-1.png` and `qa/contact-props-2.png` split the prop review into readable pages. Pipeline row GIFs under `runs/*/previews/` are provenance previews: static variant rows are selector alternatives, and the final server/vacuum assembly edits are authoritative in `sheets/`.

The preview is an art composition, not a runtime map or renderer integration. The wall pieces do not cover every topology. Cat walking and vacuum brushing are short stylized loops; frame review does not certify all gameplay movement or collision behavior. The compact meeting set has deliberately small chairs. See `qa/review.md` for the final visual review and regeneration notes.
