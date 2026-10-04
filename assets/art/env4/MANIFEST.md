# Environment Pack 4 — Ambient life

Load `sheets/*.png` or `bubbles/*.png` with the sibling `*.manifest.json`. These manifests copy the **same schema** as `../env2/sheets/plants.manifest.json`: `frame_layout.rows[state][frame]` is authoritative. All row/frame indices and source-pixel coordinates are zero-based. Static `variants` rows are choices, not animations. Interaction and mask notes live here and in `qa/regions.json`; no fields were added to the production schema.

Props use **64×64** transparent cells, no padding between cells, bottom anchor **(32,61)**, matching env2. Smaller objects deliberately occupy less of a cell. The bubble glyphs use **12×12** transparent cells, one-pixel safe margins, top-left anchor **(0,0)**. PNGs have straight RGBA alpha and byte-zero RGB wherever alpha is zero. Only the ceiling halo has intermediate alpha; every other production sprite uses binary alpha. Empty trailing cells are transparent. Always scale with nearest-neighbor sampling.

`palette.json` is a **byte-identical copy of env2's palette**; no colors were added. New bodies use a small warm walnut/honey, ivory, teal, navy, dusty-blue and brick-red subset. Existing appliance/plant body colors are retained.

## Window views

`sheets/window-views.png` is **256×320**, four columns and five rows, all **4 frames, 2 fps, loop:true**:

| Row | State | Appearance |
|---|---|---|
| 0 | `day` | muted blue sky, small moving ivory cloud, distant block skyline |
| 1 | `sunset` | brick-red sky, amber sun/window lights, moving cloud |
| 2 | `night` | navy sky, fixed stars/crescent moon, moving dark cloud, warm city windows |
| 3 | `rain` | overcast blue sky/cloud and falling diagonal streaks |
| 4 | `snow` | dusty-blue sky/cloud and small drifting ivory flakes |

The walnut frame, honey mullions and sill are identical **across every frame and every state**. Only the four pane interiors can change: inclusive rectangles **(9,30)–(29,41)**, **(34,30)–(54,41)**, **(9,45)–(29,55)**, **(34,45)–(54,55)**. Night stars are static, while the cloud moves. Cloud and weather travel are deliberately short; they reset at the loop boundary rather than traversing an entire window.

## Appliances, plants and light

Unless noted otherwise, the clips below loop. Rows are listed in exported order; trailing cells in shorter rows are empty.

| Sheet | Rows / frames / timing | Intended animated region |
|---|---|---|
| `coffee-machine.png` | `steam`, `drip`, `brew`; 3 frames each, 3 fps | pale steam above reservoir; brown drops inside brew opening; `brew` combines both |
| `mug-steam.png` | `steam`; 3 frames, 2 fps | steam above fixed mug rim/handle |
| `plant-monstera.png` | `sway`; 3 frames, 1 fps | upper leaf tips only |
| `plant-snake.png` | `sway`; 3 frames, 1 fps | upper leaf tips only |
| `plant-fern.png` | `sway`; 3 frames, 1 fps | upper fronds only |
| `plant-succulent.png` | `sway`; 3 frames, 1 fps | upper fleshy leaves only |
| `plant-ficus.png` | `sway`; 3 frames, 1 fps | top canopy only |
| `wall-clock.png` | `tick`; 4 frames, 1 fps | red second-hand paths; ivory face/rim and 10:10 hands fixed outside those paths |
| `ceiling-light.png` | `warm`; 3 frames, 1 fps; `flicker`; 3 frames, nominal 3 fps | translucent halo alpha; flicker also changes warm bulb fill, leaving fixture body fixed |
| `vending-machine.png` | `glow`; 3 frames, 1 fps | three narrow display light strips and coin indicator |
| `fridge.png` | `closed`, `open`; 1 frame each, 1 fps, **loop:false** | lower door/interior and rightward swung door leaf; static upper freezer |
| `water-cooler.png` | `bubble`; 3 frames, 2 fps | a small bubble rises within the fixed bottle |

`flicker` uses **900 / 90 / 150 ms** frame durations, giving a long bright phase and brief dim phases. Production `durations_ms` should be respected by a player that supports variable timing. The `warm` row softly varies the existing halo without repainting the fixture. These are selectable clip variants, not a random flicker scheduler.

All five plants are the **five env2 kinds only**, using env2 `plants/variants` columns 0–4 respectively. Frame zero is byte-identical to that source cell. Each sway shifts upper leaf rows by one pixel right/left while tapering to a fixed connection; lower foliage, stems/trunk and pots never move. The swaying regions are inclusive:

| Plant | Region |
|---|---|
| monstera | (17,30)–(46,40) |
| snake plant | (19,20)–(44,34) |
| fern | (16,32)–(47,44) |
| succulent | (21,40)–(41,49) |
| ficus tree | (15,10)–(47,24) |

Exact masks for all props are published under `qa/masks/`. The coffee mask is **(27,7)–(37,15)** plus **(32,31)–(32,36)**; the mug mask is **(25,41)–(35,47)**. Vending light strips are **(24,21)–(33,21)**, **(24,34)–(33,34)**, **(24,46)–(33,46)** plus pixel **(41,36)**. The fridge state mask is **(21,39)–(55,58)**. Bubble, clock, light and fan masks are nonrectangular or disconnected; use their mask PNGs rather than a broad enclosing box.

## Idle-activity props

| Sheet | Rows / frames / timing | Appearance and fixed-body rule |
|---|---|---|
| `dartboard.png` | `variants`; 1, 1 fps, loop:false | segmented ivory/navy dartboard with three attached darts |
| `ping-pong.png` | `ball`; 2, 2 fps, loop:true | blue/teal court, cream markings/net and walnut legs; only two tiny ball positions change |
| `bean-bag.png` | `variants`; 1, 1 fps, loop:false | soft brick-red cushion with seam |
| `arcade.png` | `screen`; 3, 2 fps, loop:true | navy cabinet, cream marquee, joystick/buttons; tiny invaders/paddle animate only in **(24,26)–(38,36)** |
| `reading-nook.png` | `variants`; 1, 1 fps, loop:false | low walnut bookshelf, dusty-blue floor cushion and open ivory book |
| `standing-desk.png` | `variants`; 1, 1 fps, loop:false | honey top, tall ivory adjustable legs, compact screen/keyboard |
| `fish-tank.png` | `swim`; 4, 2 fps, loop:true | two fish and rising bubbles in the water; glass rim/reflections, gravel/plant and walnut stand fixed |
| `lava-lamp.png` | `flow`; 3, 1 fps, loop:true | two colored blobs move within amber glass; cap and pedestal fixed |
| `desk-fan.png` | `spin`; 3, 4 fps, loop:true | dark/blue rotor pixels change behind a fixed ivory grille/hub; neck/base fixed |
| `string-lights.png` | `twinkle`; 3, 1 fps, loop:true | five red/gold/teal seasonal bulbs; only bulb interiors change, cable/holders/outlines fixed |

### Interaction points

These are **art-placement recommendations**, not installed runtime furniture definitions. Target/contact and actor-foot points are in each **64×64 source cell's coordinate system**, measured from its top-left. Actor-foot points can lie outside the sprite cell; that is intentional. For an instance positioned by its env2 bottom anchor, source point `(x,y)` has unscaled offset **(x−32,y−61)** from that anchor. Apply the chosen runtime scale to this offset. Pelvis contacts are separate from actor-foot points. Reachability, collision footprints, character poses and actions still need a runtime adapter.

| Prop | Target / contact point | Suggested actor-foot point / facing | Interaction |
|---|---|---|---|
| dartboard | aim at **(32,48)** | **(32,82)** / up | throw/retrieve darts |
| ping-pong | court center **(32,39)** | far **(32,18)** / down; near **(32,72)** / up | two-player table activity |
| bean bag | pelvis **(32,50)** | approach **(32,70)** / up | sit/rest; align seated pelvis to contact |
| arcade | console **(32,42)** | **(32,72)** / up | play arcade |
| reading nook | book **(32,51)**, cushion pelvis **(32,55)** | approach **(32,70)** / up | sit/read; align pelvis separately |
| standing desk | keyboard **(32,34)** | **(32,72)** / up | standing work; no chair/seat contact |
| fish tank | glass **(32,38)** | **(32,74)** / up | observe/feed fish |
| lava lamp | base **(32,56)** | **(32,67)** / up | lamp toggle/observe |
| desk fan | base control **(32,54)** | **(32,67)** / up | fan toggle |
| string lights | cable center **(31,43)** | **(32,70)** / up | decorate/toggle seasonal lights |

## Bubble-icon expansion

`bubbles/icons.png` is a **156×12** static selector strip. Row `variants`, 13 columns, 1 fps, loop:false; these columns are independent glyphs. Each glyph is also delivered as a **12×12 PNG plus a sibling manifest** for direct use. The existing `../sheets/icons.png` was the sole original bubble sheet viewed. Its production cells are 32×32; this pack explicitly authors the requested native 12×12 glyphs rather than claiming the old cell size was 12×12.

| Column | File / meaning |
|---|---|
| 0 | `coffee.png` — mug/steam |
| 1 | `music-note.png` — paired music note |
| 2 | `light-bulb.png` — idea |
| 3 | `sweat-drop.png` — blue sweat drop |
| 4 | `heart.png` — brick-red heart |
| 5 | `exclamation-error.png` — red exclamation/error |
| 6 | `question.png` — amber question mark |
| 7 | `sparkles.png` — warm sparkles |
| 8 | `zzz.png` — sleep |
| 9 | `check.png` — teal check |
| 10 | `pizza.png` — cheese/pepperoni slice |
| 11 | `headphones.png` — dusty-blue headphones |
| 12 | `magnifier.png` — blue lens/dark handle |

These are **standalone glyphs with transparent surroundings**, suitable inside a speech/thought bubble. They do not include an enclosing bubble shape. They use hard pixels, dark outlines, simple muted accents and no antialiasing.

## Provenance and assembly

The nine new static activity bodies were generated by **sprite-gen, provider=codex, still-image component-row pipeline**, with **`--concurrency 1`**. Two generation calls ran sequentially, one per row: `leisure_a` (5 independent objects) and `leisure_b` (4). No video generation, generated motion frames, alternate providers or API-billing fallback were used. `request-bodies.json`, `references/style.png`, prompts, raw strips, extraction records, provider reports and canonical atlas/GIF outputs are retained under this directory. Curation was skipped; defaults were not saved.

Canonical sprite-gen connected-component extraction and `kcentroid` fitting succeeded without extraction warnings. The inspector's low silhouette-similarity warnings compare **different static objects**, not animation poses; they are retained in `runs/bodies/sprite-inspect.report.json`. No equal-width slicing of generated strips was used. `build_env4.py` fits/quantizes those extracted silhouettes, widens the table/tank/reading nook/standing desk at native resolution, and composes all motion with deterministic Pillow/numpy operations. These final assembly edits are authoritative in `sheets/`, not the retained pipeline atlas.

Env2 vending machine, fridge, clock, mug, ceiling light and five plants are **read-only reused bodies**. Coffee machine and water cooler reuse the existing original mixed prop sheet. Window architecture/views, string lights and the 13 glyphs are programmatic pixel art. This is an additive ambient pack; reused art is not presented as newly generated artwork.

## Verification and preview

```powershell
$env:SPRITE_GEN_ROOT='C:\Users\<user>\.codex\skills\sprite-gen'
$env:PYTHONDONTWRITEBYTECODE='1'
& "$env:SPRITE_GEN_ROOT\.venv\Scripts\python.exe" assets/art/env4/build_env4.py check
```

Use `build` instead of `check` to deterministically rebuild the final sheets, masks, contacts and scene from the retained extracted frames. `prepare` creates the reference/recipe only. All builder writes stay within env4. The builder imports **`tools.anim_normalize.normalize_frames` without modifying it**.

The check **reloads final exported PNGs**, compares all four RGBA bytes outside each published animation mask, and also checks identity **across states** for window, coffee, light and fridge. It invokes the normalization API with an explicit mask and `max_shift=0`, requiring **zero pixels corrected and byte-identical API output**. It does not normalize away defects to pass. A deliberately corrupted vending-body pixel is rejected before normalization as a negative control.

Other checks cover exact production schema keys, row/frame rectangles and counts, cell/sheet dimensions, nonempty declared frames, distinct looping poses, safe margins, transparent trailing cells, zero hidden RGB, binary alpha except the ceiling halo, palette membership and byte-identical plant phase-zero sources. `qa/export-report.json` records the results and fixed-region byte hashes. Masks/region intent are authored and visually reviewed; byte equality certifies fixed pixels, not motion naturalness or runtime interaction behavior.

Final inventory: **23 prop sheets/manifests, 87 declared prop frames; 14 bubble sheets/manifests, 26 declared glyph frames** (13 individual glyphs plus their duplicated selector strip). Total **37 sheets + 37 manifests, 113 declared frames, 25 animated rows**. All final frame contacts were viewed at 3×/4×, with 12 image views total including references and the final scene review.

`preview-env4.png` is the **2× nearest-neighbor** export of a native **448×288** office corner: **896×576**. It shows a night window, vending machine, arcade, dartboard, fish tank and ping-pong table, plus several quieter props. Floor, wall, rug and two chairs are read-only env2 art; all ambient props are env4. `scene.json` records placement evidence. This is a static art composition, not runtime integration.

Weak items: the clock uses four stylized quarter-turn second-hand poses rather than a real-time 60-position clock; the two-position ping-pong ball is schematic; coffee drops, cooler bubbles and fan motion are deliberately tiny at 1×; weather/fish/lava loops are short and have discrete resets. The small fern retains env2's existing silhouette. No application build/test, runtime-pack installation or git commit was performed.
