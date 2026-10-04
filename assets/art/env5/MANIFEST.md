# Environment Pack 5 — Office customization

Load `sheets/*.png` with the sibling `*.manifest.json`. These manifests copy the **same schema** as `../env2/sheets/plants.manifest.json` for props and `../env2/sheets/wall-navy.manifest.json` for tiles: `frame_layout.rows[state][frame]` is authoritative. All indices and pixel coordinates are zero-based. Static rows, including `variants`, orientation rows and architecture rows, are **choices, not animations**. Catalog, collision and animation-mask notes are additive documentation here and in separate JSON files; no fields were added to the production manifest schema.

Tiles and walls are **32×32**, opaque, no padding, top-left anchor **(0,0)**. Furniture, structural props and decorations use **64×64** transparent cells, bottom anchor **(32,61)**, two-pixel horizontal and three-pixel vertical safe margins, matching env2/env3. Smaller objects intentionally occupy less of a cell. PNGs use straight RGBA, binary alpha and byte-zero transparent RGB. Empty trailing cells are byte zero. Scale with nearest-neighbor sampling. `palette.json` is a **byte-identical copy of env2's palette; no colors were added**.

## Editor catalog

`catalog.json` lists **318 selectable items**. Every item has a unique ID, name, category, tags, footprint, blocking flag, source sheet/manifest/state/frame, explicit rotation variants, recolor family (or `null`), source cell size, anchor, suggested scale, draw layer and notes. Animation entries select phase zero; their production row owns the complete loop. Static alternatives have separate entries; rotations belonging to an item are nested rather than counted as extra items. Every exported frame is covered by the catalog, including animation phases and nested rotations.

Footprints are **recommendations in 16-pixel runtime tiles at the declared `suggestedScale`**: 0.5 for 32-pixel tiles, 1 for props. They reserve the ground area, not the full height of the silhouette. `blocking:false` marks floor coverings, wall/ceiling decor and open doors; `blocking:true` recommends a rectangular physical reservation. These are adapter inputs, **not installed runtime collision, seat or interaction definitions**. The runtime format in `docs/asset-format.md` still needs an adapter into `assets.json`, with explicit blocked cells, interactions and renderer anchors as appropriate. Production bottom anchors must not be mistaken for the runtime furniture-origin anchor. No app code, pack builder, active asset pack, layout, user settings or editor integration was changed.

Only rotations listed in `rotationVariants` exist. Desks and office chairs offer front **0**, authored side **90/right** and **270/left**. Sofas offer authored **0/down** and **90/right**. Cubicle shapes have four explicit pixel drawings; low dividers have two. Symmetric panel reverse views can share identical pixels. Floor thresholds are baked orthogonal tile rotations. Most wall-mounted or front-view props have one view. Never infer missing views or rotate a perspective sprite at runtime.

| Category | Sheets / sibling manifests | Declared frames | Catalog items |
|---|---:|---:|---:|
| Floors and trims | 26 | 76 | 76 |
| Walls and openings | 9 | 135 | 135 |
| Partitions and structure | 9 | 21 | 11 |
| Furniture color families | 11 | 66 | 38 |
| Office and lounge props | 25 | 46 | 44 |
| Seasonal decor | 8 | 21 | 14 |
| **Total** | **88** | **365** | **318** |

The catalog contains **356 explicit rotation variants**. Item, rotation and frame totals count different things; they should not be added together.

## Floors

Each of the following 20 sheets has row `variants`, **two** 32×32 static choices, 1 fps, `loop:false`:

| Sheet suffix after `floor-` | Material |
|---|---|
| `wood-maple`, `wood-ash`, `wood-cherry`, `wood-bamboo`, `wood-reclaimed` | five additional wood treatments |
| `wood-herringbone` | diagonally interlocking 2:1 parquet planks, 32-pixel periodic pattern |
| `wood-light-oak`, `wood-dark-walnut` | pale oak and deeper walnut |
| `concrete`, `polished-stone` | quiet warm concrete; ivory limestone blocks with polish marks |
| `carpet-sage`, `carpet-cream`, `carpet-lilac` | three sparse-texture carpets |
| `rubber-gym`, `tatami` | charcoal rubber flecks; woven mat with dark bound edges |
| `checker-cream`, `checker-sage`, `checker-blue` | cream with walnut, jade or blue; 8-pixel and 16-pixel checks |
| `grass-garden`, `bathroom-tile` | muted jade grass tufts; ivory ceramic/grout |

`trim-carpet-{sage,cream,lilac}` has eight single-frame rows in this order: `north`, `east`, `south`, `west`, `north_east`, `south_east`, `south_west`, `north_west`. These are **opaque carpet edge tiles**, not overlays. Place corner trims at the corresponding carpet perimeter corner; use plain carpet in the interior.

`threshold-oak-stone`, `threshold-walnut-carpet`, `threshold-bathroom` each have four single-frame rows `r0`, `r90`, `r180`, `r270`. `r0` has its first material above and second material below a horizontal trim; the remaining rows turn that tile clockwise. Floor material textures are reused on both sides of the trim. No automatic material-transition inference is included.

## Walls and openings

Nine sheets: `wall-brick`, `wall-white-plaster`, `wall-wallpaper-rose`, `wall-wallpaper-mint`, `wall-wallpaper-blue`, `wall-glass`, `wall-wood-dark`, `wall-chalkboard`, `wall-wainscot`.

Each is **32×480**, one column, fifteen single-frame rows in this order:

| Row | State | Meaning |
|---:|---|---|
| 0 | `panel` | straight face with honey rail and dark baseboard |
| 1–2 | `top_left`, `top_right` | outer end/corner trim |
| 3–4 | `inside_left`, `inside_right` | returning inner rail/corner |
| 5 | `vertical` | centered vertical structural trim |
| 6–9 | `tee_left`, `tee_right`, `tee_up`, `tee_down` | explicit T connections |
| 10 | `door_frame` | wood/honey surround, dark opening |
| 11–13 | `window_classic`, `window_wide`, `window_round` | classic four-pane, wide, round window |
| 14 | `archway` | curved honey/wood surround, dark opening |

These are **decorative wall elevations and explicit composition pieces**, not a complete bitmask autotile set. Repeat or stack panels to build a room. Door/arch interiors are dark opaque placeholders, not alpha holes; their catalog entries recommend walkability. Windows are fixed views. Glass wall tiles remain opaque muted blue/jade to match the tile convention; separate glass door props have transparent surroundings and open states.

## Partitions and structure

| Sheet | Rows / choices |
|---|---|
| `cubicle-straight`, `cubicle-corner`, `cubicle-tee` | `r0/r90/r180/r270`, explicit static elevations |
| `low-dividers` | `variants` columns 0 horizontal, 1 depth/vertical |
| `sliding-glass-door`, `double-door` | `closed/open`, one frame each; open state is nonblocking |
| `curtain` | brick-red pleated curtain, overlay, nonblocking |
| `stairs-decor` | five decorative stone steps, **nonfunctional** |
| `pillar` | ivory/grey column with capital and base |

The doors have no automatic state transitions. Cubicle footprint rectangles conservatively reserve their whole extent; an adapter can author more precise blocked cells for L/T shapes. Curtain and ceiling layers are placement suggestions, not new runtime layer logic.

## Furniture recolor families

All furniture colorways were made with **`sprite-gen recolor`**, not image generation. There are **13 deterministic material bakes / 66 finished views**. `recolor/*/spec.json`, masks, source cells, tool reports, palette-patch PNGs and assembled `finished-*.png` files retain the evidence. RGB changes are restricted to the selected material pixels; original alpha and all non-material RGBA bytes are preserved exactly. Wooden legs, books, cushions where excluded, metal bases, CRT housings and small accessories remain independent of the fabric/top color.

| Sheets | Choices / order |
|---|---|
| `desks-front`, `desks-side-right`, `desks-side-left` | wood, white, black, green, blue |
| `chairs-front`, `chairs-side-right`, `chairs-side-left` | green, blue, red, white, black, lilac |
| `sofas` | rows `down/right`; green, blue, red, white, black, lilac in each row |
| `armchairs`, `rugs` | green, blue, red, white, black, lilac |
| `lamps` | white, green, red |
| `bookshelves` | rows `open/cabinet`; walnut, oak, birch in each row |

Front desks are empty ENV2 desks; side desks retain ENV3's static on-screen CRT, keyboard and mug. Chairs are complete sprites, not split seating layers. The five desk colors do not include animated screen states in this pack. Original wood desk colorways are byte-identical to their source cells. Bookshelf wood colorways retain book/cabinet details. Rugs are nonblocking floor overlays.

## Office and lounge props

Unless explicitly listed as a loop below, sheets have static `variants`, 1 fps, `loop:false`. Numeric columns are zero-based.

| Sheet | Appearance / choices |
|---|---|
| `kitchen-island` | ivory counter, walnut cabinets, kettle/mug, three attached stools |
| `conference-table-eight` | joined walnut conference table with **eight chairs**, paper/pitcher |
| `whiteboard-wheels` | sketch dashes, marker tray, posts and casters |
| `notice-board` | cork, paper notes and red pins |
| `projector-screen` | blank ivory pull-down screen, bottom pull cord |
| `printer-station` | cream printer, paper and walnut supply cabinet |
| `plant-wall` | framed dense green living wall, wall overlay |
| `hanging-plants` | 0 fern, 1 monstera, 2 trailing vine; visible hanging cords |
| `tall-plants` | 0 ENV2 ficus, 1 taller rubber plant |
| `coat-rack`, `shoe-rack` | hanging cream coat / blue scarf; two-tier shoes bench |
| `waste-bins` | 0 ENV2 trash bin, 1 recycling bin |
| `water-jug` | ribbed blue bottle with cap and label |
| `coffee-table-sets` | 0 cup, 1 fern, 2 magazines on ENV2 coffee-table body |
| `floor-cushions` | red, blue, green, lilac |
| `standing-lamp` | cream shade on arched floor stand |
| `ceiling-fan` | row `spin`, three frames, 3 fps, loop |
| `wall-shelves` | 0 empty, 1 books, 2 plants |
| `framed-art` | mountain, botanical, abstract, seascape, city, cat — six new pixel compositions |
| `wall-posters` | event circle, botanical, music, coffee — four static designs |
| `neon-sign`, `neon-open` | blank dark housing / separate hand-pixel **OPEN** version |
| `speaker`, `retro-tv`, `game-console-corner` | speaker cones; walnut CRT/antenna; TV, console and controllers on cabinet |

The conference table, island and game corner are decorative groupings. Their chairs/controllers are part of the sprite; there are no new simulated seats or interactions. Separate furniture chairs remain available.

## Seasonal sets and fixed-pixel loops

| Set | Sheets / choices |
|---|---|
| Winter | `winter-tree/twinkle` (3 frames, 2 fps); `winter-garland/twinkle` (3, 2 fps); `winter-snow-window/snow` (4, 2 fps, ENV4 loop reused) |
| Spring | `spring-cherry-branch`; `spring-flowers` pink bouquet, red tulips, white flowers |
| Halloween | `halloween-pumpkins` plain, jack-o-lantern, pair; `halloween-cobwebs` left/right |
| Autumn | `autumn-leaves` two static floor scatter choices |

Only four rows loop: fan, tree, garland, snow window. `qa/animation-regions.json` and `qa/masks/*.png` publish exact pixel intent. Fan mount, rod and hub are fixed while its blade silhouettes change. The tree changes only **seven one-pixel ornament centers**; its star, needles, trunk and pot never change. The garland changes four bulb interiors; branch, cords and sockets are fixed. The snow window changes only ENV4's four pane interiors: inclusive **(9,30)–(29,41)**, **(34,30)–(54,41)**, **(9,45)–(29,55)**, **(34,45)–(54,55)**. Frame, mullions and sill are fixed.

The builder imports **`tools.anim_normalize.normalize_frames` without modifying it**. Verification reloads exported PNGs, compares all four bytes outside the mask, then invokes the API with `max_shift=0` and the explicit mask. **Zero corrected pixels and byte-identical API output are required**; normalization is not used to conceal a defect. A deliberately corrupted tree-pot pixel must fail before normalization. Static choices are never compared as animation phases.

## Provenance and deterministic assembly

New silhouettes were generated by **sprite-gen, provider=codex, still-image component-row pipeline**, two sequential row calls with **`--concurrency 1`**, yielding nine accepted bodies: island, eight-chair conference table, wheeled board, printer station, living wall, coat rack, shoe rack, retro TV, console corner. **No video generation**, alternate provider, API-billing fallback or generated recolor frames. `request-bodies.json`, `references/style.png`, generated prompts/layout guides, provider reports, raw strips and canonical connected-component extraction/atlas records remain under this directory. The style reference was composed from read-only ENV2 cells. Curation was skipped; defaults were not saved.

Extraction uses canonical `kcentroid` fitting, not equal-width slicing of raw rows. It completed without extraction warnings. The generic inspector's silhouette-similarity warnings compare **different static objects**; they are not animation defects. Pipeline GIFs cycle static alternatives for provenance only. Authoritative production sheets are in `sheets/`.

`build_env5.py` fits and quantizes extracted bodies to ENV2 colors, assembles tool-baked furniture materials onto original bodies, and draws floors, architecture, trims, boards, plants, small props, framed art, posters and seasonal pieces with deterministic hard pixels. These are documented assembly edits, not untouched model output. The build/check commands make no network/model calls. `recolor` invokes the installed deterministic CLI. All builder writes stay under env5. `qa/source-hashes.json` records the source art, palette and normalization API byte hashes for unchanged-source checks.

## Preview and verification

`preview-env5.png` contains a labeled category contact overview and **two 2× example player-made offices**, a cozy studio and a bright coworking space. Every scene sprite comes from **env2..env5**. Separate **960×704** nearest-neighbor examples are in `examples/`; their native scenes are **480×352**. `example-scenes.json` records every floor, wall and prop placement with production sheet, row and frame evidence. These are static art compositions, not executable editor layouts.

Six `qa/contact-*-2x.png` pages show **every declared frame**. The main preview shows material/family representatives; use those full pages and the catalog for all topology, color and orientation choices. Visual review used 12 image views total including the required ENV2 preview, reference and extraction composites, six full category contacts, example scenes, correction composite and final preview.

```powershell
$env:SPRITE_GEN_ROOT='C:\Users\<user>\.codex\skills\sprite-gen'
$env:PYTHONDONTWRITEBYTECODE='1'
$env:PYTHONIOENCODING='utf-8'
& "$env:SPRITE_GEN_ROOT\.venv\Scripts\python.exe" assets/art/env5/build_env5.py check
```

`build` deterministically rebuilds final sheets, manifests, catalog, masks, contacts, examples, preview and report from retained frames/colorways. `recolor` reruns the 13 local palette bakes first if color specifications change. `prepare` creates the reference/request only. Production checks cover copied schema keys, cell/sheet dimensions, row/frame rectangles, timing, nonempty cells, margins, opaque tiles, binary alpha, hidden RGB, palette membership, empty trailing cells, catalog coverage/IDs/rotation references, material alpha/non-material identity, fixed animation bytes, distinct loop phases, negative control and source hashes. `qa/export-report.json` contains final counts, checks and PNG/fixed-region hashes.

Weak items: the eight-chair table is intentionally compact, so its seats are tiny at 1×; cubicle T/corner connections and decorative stairs are schematic elevations; ferns and small flowers are stylized hard-pixel clusters; fan/weather/bulb loops are short and discrete. Collision footprints remain conservative recommendations, and missing perspective rotations are not supplied. No application build/test, runtime-pack installation, upload or git commit was performed.
