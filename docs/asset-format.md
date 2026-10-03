# Asset and layout format (version 1)

The WPF viewer and tracker use the same language-neutral JSON records. No .NET type names, enums-as-integers, paths to source logs, or serialized executable code occur in these formats. Separate art production can replace the fallback without changing the renderer or simulation. Generation-tool manifests under `assets/art/` are production records, **not** this runtime manifest; write a small adapter manifest pointing to the final PNGs.

## Loading

- Normal startup reads `%LOCALAPPDATA%\Vibedotcom\assets\assets.json` (or `manifest.json`) and `%LOCALAPPDATA%\Vibedotcom\layout.json` when present. `--asset-dir DIR` / `--layout FILE` override them in both executables. Demo/QA and normal startup without an installed pack use the generated art embedded from `assets/pack/`.
- The bundled [default layout](../src/Vibedotcom.Core/Data/layout.json) uses this art for a furnished office with twelve fixed desks. Explicit missing/invalid packs retain the procedural [fallback catalog](../src/Vibedotcom.Core/Data/assets.json) and its compatible fallback layout; invalid input produces a warning. Invalid input is never automatically repaired. Restart to reload a custom art pack; the viewer's layout editor can edit the office separately.
- JSON: UTF-8, camelCase; `version:1`; unknown additional fields tolerated. Maximum JSON 4 MiB. PNGs are relative local files within the pack directory, maximum 32 MiB each, 8-bit non-interlaced grayscale/RGB/indexed/gray-alpha/RGBA, maximum 8192 per axis and 16,777,216 pixels. Use transparent RGBA for characters and furniture. Dimensions, compressed scanline lengths, clip bounds and PNG filters are checked before use. There is no palette conversion at runtime.
- A pack is loaded as a unit. `fallback` is a reserved sheet ID; the procedural atlas always supplies fallback characters and bubbles. Custom furniture/tile IDs override matching fallback definitions; omitted furniture/tile definitions are inherited. The custom character roster replaces the default roster. If a roster has no eligible design for a role/provider, that agent uses fallback art.

## manifest.json

`assets.json` accepts the same schema and takes precedence if both filenames exist in an input directory. Exported snapshots still publish `manifest.json`.

Rebuild the bundled runtime pack before `dotnet build` with:

```powershell
& C:\Users\myori\.codex\skills\sprite-gen\.venv\Scripts\python.exe tools/build_pack.py
```

The builder reads final production sheets, skips incomplete roster characters and partial environment files, prefers available `env2/normalized/` furniture, and writes compact PNG sheets plus `assets.json` to `assets/pack/`. Every published `assets/art/seated/<id>/<id>.png` / `.manifest.json` pair is preferred, including IDs absent from the original roster. Pending directories without both canonical files use the original poses; an invalid published pair fails the build before any pack files are written. When seated art is absent, a locked sheet is preferred only when its provenance hashes still match the original inputs; stale, partial or missing locked output falls back to the original. Each design is measured from its front idle alpha bbox (feet to top) and reduced to 30 world pixels with nearest-neighbor sampling baked into integer source pixels. All poses retain the `(16,60)` foot reference and runtime scale 1; different hair/pose silhouettes remain authored. Seated loops and their blink copies use the same integer resize and placement across every phase, with a transformed clip `seatAnchor`; the pelvis is never repositioned from a phase's alpha bbox. Original `typing/up` and `typing/right` clips remain available as legacy poses. Rerun as art production completes, then rebuild the executables to refresh their embedded art. Completed roster designs and the 36 p5 variants are eligible across providers and roles. Persistent provider/name tags follow the actual character; a gold plate edge marks supervisors. Relationship hints are faint, at most 40 world pixels long, shown only for nearby (within 64 pixels) working/reporting children or hovered families. They never connect distant rooms.

Prop sheets, including the original mixed props and available normalized sheets, pass through the importable `tools/anim_normalize.py.normalize_sheet` API in memory. All multi-frame prop animations lock non-animated RGBA bytes to frame zero; `variants` remain separate static choices. The API's existing full-body exceptions remain available for deliberately animated bodies. No production art is written.

The builder copies the original artwork listed in `assets/art/provider-icons/manifest.json`, using each native size's density-1 PNG without resizing or repainting it. `provider-icons` has 12×12 cells; `provider-icons-10` has independently authored 10×10 cells. Both use scale 1, anchor `(0,0)`, and single-frame non-looping `any` clips named exactly after the artwork IDs: `codex`, `claude`, `antigravity`, `other-agent`, their `-supervisor` and `-sub-agent` composites, and the standalone `supervisor` / `sub-agent` overlays when present. Select a glyph with `Assets.Frame("provider-icons", provider, "any", 0)` or the corresponding 10px sheet and role ID. Production art remains read-only; local brand overrides are unchanged.

The builder still draws the tiny laptop directly with Pillow pixel operations. `office-extras` has a 16×16 `laptop`/`any` frame, scale 1 and anchor `(0,0)`, selected with `Assets.Frame("office-extras", "laptop", "any", 0)` for overflow agents. Its furniture palette entry is `cozy-laptop`, a nonblocking 1×1 prop without a work seat. Furniture rotation clips occupy extra rows in the existing sheets. The builder permits at most 127 sheets, reserving the 128th slot for the runtime's procedural `fallback` sheet.

Available env4 prop publications replace their reused env2 bodies or add furniture using 64×64 cells and scale 1. `cozy-window`, `cozy-clock` and `cozy-string-lights` retain independent sheets with unprefixed production row names. All other env4 furniture shares `env4-props`, whose clips are named `<furniture-id>-<production-row>` (for example `cozy-pantry-brew`, `cozy-bean-bag-variants`); `cozy-plant-monstera` shares the `cozy-plant-*` clips. `default` selects the first listed clip. Each state has explicit 0/90/180/270 variants and clips suffixed `-r90/-r180/-r270`. Read the variant's `sheetId` and `animation` to select the exact frame. Shared-sheet `footOffset` preserves each original cell's placement and actor-foot alignment despite its common `(0,0)` anchor. State changes never move the body. Existing pantry/water footprints and interaction points are retained.

| Furniture ID | Additional states / clips |
| --- | --- |
| `cozy-window` | `day`, `sunset`, `night`, `rain`, `snow` (4 frames, 2 fps each) |
| `cozy-pantry` | `steam`, `drip`, `brew` |
| `cozy-mug-steam` | `steam` |
| `cozy-plant`, `cozy-plant-monstera`, `cozy-plant-snake`, `cozy-plant-fern`, `cozy-plant-succulent`, `cozy-plant-ficus` | `sway` |
| `cozy-clock` | `tick` (4 frames, 1 fps) |
| `cozy-ceiling-light` | `warm`, `flicker` |
| `cozy-vending-machine` | `glow`, `closed`, `open` |
| `cozy-fridge` | `closed`, `open` (static, non-looping) |
| `cozy-water` | `bubble` |
| `cozy-dartboard`, `cozy-bean-bag`, `cozy-reading-nook`, `cozy-standing-desk` | Static `variants` clip, `default` state |
| `cozy-ping-pong` | `ball` |
| `cozy-arcade` | `screen` |
| `cozy-fish-tank` | `swim` |
| `cozy-lava-lamp` | `flow` |
| `cozy-desk-fan` | `spin` |
| `cozy-string-lights` | `twinkle`, `off` |

Env4 rows pass through `normalize_frames` with their published `env4/qa/masks/<sheet>.png`, `max_shift=0` and `static-body-with-animated-mask`. This preserves authored motion while locking every RGBA byte outside the mask. The generated string-light `off` clip darkens only masked bulb interiors using the artwork's darkest opaque colour, preserving alpha, cable, holders and outlines; it never rewrites production art. Vending `closed` selects the first authored `glow` frame without looping; `open` aliases the authored three-frame `glow` loop. WPF adds a small open dispensing flap and cup overlay during the drink activity. The source has no open-door artwork, so these runtime states preserve the original cabinet pixels. Version 1 keeps constant FPS, so ceiling-light `flicker` uses the published nominal 3 fps rather than its variable production `durations_ms`.

At rotation 0 the new activity points are `cozy-window/view → window (1,1)/up`, `cozy-vending-machine/snack → vending (1,3)/up`, `cozy-fridge/snack → fridge (1,3)/up`, `cozy-dartboard/darts → darts (1,2)/up`, `cozy-ping-pong/near → pingpong (1,2)/up`, `cozy-ping-pong/far → pingpong (1,-1)/down`, `cozy-bean-bag/rest → beanbag (1,1)/up`, `cozy-arcade/play → arcade (1,3)/up`, `cozy-reading-nook/read → reading (1,2)/up`, `cozy-standing-desk/work → standing-work (1,2)/up`, and `cozy-fish-tank/watch → aquarium (1,2)/up`. Coordinates are local tiles; the normal rotation pass transforms points and facings. Primary activity points align the source actor-foot X=32 to their tile centre and their recommended source Y to the tile's bottom edge. Ping-pong's far point is an approximate floor approach, six pixels below its recommended source contact. Bean bag and reading nook also expose source-derived pelvis contacts through `chairSeatAnchor`.

`bubble-icons` packs the 13 env4 native 12×12 glyphs into one selector strip, anchor `(0,0)`, scale 1. Its static `any` clips are `coffee`, `music-note`, `light-bulb`, `sweat-drop`, `heart`, `exclamation-error`, `question`, `sparkles`, `zzz`, `check`, `pizza`, `headphones`, `magnifier`. Individual production glyphs and the duplicate `bubbles/icons.png` strip represent the same cells; no enclosing bubble shape is added, and the existing 32×32 `icons` sheet remains available.

Optional published pets have sheets `pet-shiba`, `pet-tuxedo-cat`, `pet-orange-tabby`, `pet-corgi`, `pet-hamster-ball`, `pet-robot-buddy`, `pet-parrot`, `pet-capybara`. Coat sheets are `pet-shiba-cream`, `pet-shiba-sesame`, `pet-tuxedo-cat-blue-grey`, `pet-tuxedo-cat-chocolate`, `pet-orange-tabby-silver`, `pet-orange-tabby-brown`, `pet-corgi-sable`, `pet-corgi-cream`. These retain native 32×32 cells, `(16,29)` anchor and scale 1, and stay outside the human character roster. They use the normalizer's existing `pet` full-body exception with `max_shift=0`, preserving authored locomotion and action poses. `idle` and `blink-idle` are four one-frame direction selectors (`down/left/right/up`); `walk` or `roll` clips select the corresponding direction. `sit/down`, `sleep/right`, `eat_drink/right`, `play/right`, `react/down` retain their source timing and loop flags. Each `sleep` clip has a `seatAnchor` copied from that pet's published `pet.sleepContact`. Parrot additionally has `head_bob/any` and `wing_flap/any`, with no locomotion clips. `pet-props` retains seven static 64×64 clips and anchor `(32,61)`: `dog_bed`, `cat_tree`, `water_bowl`, `food_bowl`, `ball`, `yarn`, `scratching_post`. `cozy-pet-bed` uses `pet-props/dog_bed`, kind `bed`, a 2×1 blocked footprint, rotation 0/default and `nap (1,1,pet-napping,up)`. Its `dog_bed` clip has `footOffset=(16,16)`, centring the body in the footprint and placing its bottom at world Y=16. The corresponding `chairSeatAnchor=(16,5)` is the published bed target `(32,50)` minus the effective cell anchor `(16,45)`; pet sleep alignment uses the pet's own clip contact. Missing optional sheet/manifest pairs are skipped; no pet behaviour is installed by the builder.

Optional WPF-only brand overrides are independent of the manifest and state: valid `%LOCALAPPDATA%\Vibedotcom\brand\{codex,claude,antigravity}.png` files (12x12) and optional `@2x` (24x24) / `@3x` (36x36) companions take precedence over an explicitly embedded overlay, then the generic pack. Missing or corrupt files fall through to another valid density/provider default; restart to reload. `tools/fetch_brand_icons.ps1` writes ignored `assets/brand-overlay/`; only `-p:EmbedBrandOverlay=true` embeds that folder into the App (default false). `build_pack.py` never reads it or logo originals. See [local usage and source URLs](../assets/brand/README.md). No official logo enters the Core pack or exported snapshots.

Minimal example (a real `people.png` is required):

```json
{
  "version": 1,
  "sheets": [{
    "id": "people", "file": "people.png", "cellW": 32, "cellH": 64,
    "anchor": { "x": 16, "y": 60 }, "scale": 0.5,
    "rows": [
      {"name":"idle", "direction":"any", "row":0, "frames":4, "fps":1},
      {"name":"walk", "direction":"down", "row":1, "frames":4, "fps":7},
      {"name":"walk", "direction":"left", "row":2, "frames":4, "fps":7},
      {"name":"walk", "direction":"right", "row":3, "frames":4, "fps":7},
      {"name":"walk", "direction":"up", "row":4, "frames":4, "fps":7},
      {"name":"typing", "direction":"up", "row":5, "frames":3, "fps":5},
      {"name":"typing", "direction":"right", "row":6, "frames":3, "fps":5},
      {"name":"reporting", "direction":"any", "row":7, "frames":3, "fps":5, "loop":false},
      {"name":"napping", "direction":"any", "row":8, "frames":2, "fps":2, "footOffset":{"x":0,"y":-10}},
      {"name":"coffee", "direction":"any", "row":9, "frames":3, "fps":3},
      {"name":"phone", "direction":"any", "row":10, "frames":3, "fps":3},
      {"name":"chatting", "direction":"any", "row":11, "frames":3, "fps":4},
      {"name":"waiting", "direction":"any", "row":12, "frames":2, "fps":5}
    ]
  }],
  "characters": [{"id":"braid", "sheetId":"people"}],
  "furniture": [],
  "tileTypes": []
}
```

Sheet fields:

| Field | Meaning |
| --- | --- |
| `id`, `file` | Unique sheet ID and relative PNG filename. |
| `cellW`, `cellH` | Positive source cell pixels, 1–512, no gutters/margins. PNG dimensions must be cell multiples. Cell size is independent of the 16px world tile. |
| `anchor` | Source pixel point attached to the character's feet; furniture attaches it to its instance's top-left world point. |
| `scale` | Source-to-world scale, default 1, .125–4; e.g. 32×64 at .5 becomes 16×32 world pixels. Prefer integer world dimensions for crisp rendering. |
| `rows` | Clips identified by unique `(name,direction)`, direction `up/down/left/right/any`. |
| clip `row`, `column`, `frames` | Zero-based row, first column (default 0), contiguous frame count 1–256. |
| clip `fps`, `loop` | FPS in (0,60], loop default true. Character time starts when visual action changes; non-loop clips clamp at their last frame and receive no random phase. Moving rendering is capped at 30 fps. |
| clip `footOffset` | Optional `{x,y}` in **world pixels**, applied after scale; default zero. Changes drawing only, never collision/depth. |
| clip `seatAnchor` | Optional `{x,y}` in **source pixels**, the pelvis/seat contact point after pack resizing and placement. Present on `seated_back/up`, `seated_left/left`, `seated_right/right` and their phase-compatible side blink clips. Core resolves the world offset in `SpriteFrame`; renderers use that frame directly. |
| `width`, `height`, `sha256` | Derived by the loader/exporter. Do not supply guessed image dimensions. |

Character animation selection: exact action/direction → action/`any` → first `idle` clip → first clip. `entering/walking/exiting` select `walk`. Other action names are `typing`, `thinking`, `waiting`, `pickup`, `reporting`, `coffee`, `water`, `tv`, `phone`, `napping`, `chatting`, `gone`. Left frames are authored, never automatically mirrored. Convert production names `walk_down` etc. into `name:"walk",direction:"down"`; `typing_back→typing/up`, `typing_side→typing/right`, `report→reporting`, `sleep→napping`, `talk→chatting`, `surprised→waiting`. Missing specialized poses can reuse `idle`; supply more rows when the art is ready.

Roster entries: unique `id`, `sheetId`, optional `provider` (`codex/claude/antigravity`) and optional `role` (`supervisor/worker`). Omission means eligible for all. Agent IDs use uint32 FNV-1a over UTF-16 code units (offset 2166136261, multiply 16777619 with wrapping). Hash chooses a start in eligible roster order; linear probing picks an unused design when available. Newcomers in one update are assigned in ordinal ID order. Assignment remains fixed while the agent is tracked; designs repeat when the eligible roster is exhausted. A changed active roster after a restart may resolve a hash collision differently. Provider glyphs are independent of character clothing; author role/provider clothing in filtered sheets when desired.

### Furniture

Add entries to `furniture`:

```json
{
  "id":"side-desk", "kind":"desk",
  "variants":[{
    "rotation":90, "state":"default", "footprintW":2, "footprintH":3,
    "blockedCells":[{"x":0,"y":0},{"x":1,"y":0},{"x":0,"y":1},{"x":1,"y":1}],
    "points":[{"id":"seat","x":2,"y":1,"action":"typing","facing":"left"}],
    "sheetId":"props", "animation":"desk-side", "layer":1, "zOrder":0
  }]
}
```

The referenced sheet needs an `any` clip named `desk-side`. Each `(rotation,state)` is explicit and unique; rotation is 0/90/180/270 clockwise, with its own geometry and authored frames. The engine never rotates coordinates or pixels implicitly. The layout editor selects authored variants and validates their placement. Core selects runtime desk states and publishes their exact frames through the [state v4 contract](state-schema.md); renderers use those frames. Custom definitions should use distinct IDs when their geometry differs from the default desk/pantry/etc.; the bundled layout requires its original footprints.

`cozy-desk` includes the computer and exposes states `default`, `off`, `working` and `screensaver` at all four legacy rotations. At rotation 0 these share the exact 3×2 footprint, six blocked cells and `seat` point `(1,2)` with action `typing`, facing `up`. With env2 parts available its **64×64** cell has anchor `(8,22)`: the original desk pixels are padded down by 14 source pixels, while the 24×21 monitor stays at source `(20,4)`. This raises only the monitor **14 world pixels**, with exclusive world bottom `y=3`, clearing the head-anchored outer nameplate (`headTop-18`); desk/keyboard/chair world positions remain unchanged. The independently defined inclusive screen bounds remain `(23,9)–(40,16)` in source pixels; only those pixels vary between states at the same rotation. `default` and `off` use the same static dark screen (`cozy-desk` and `cozy-desk-off` clips); `screensaver` uses a static sparkle (`cozy-desk-screensaver`). Only `working` animates the monitor, with three frames at 3 fps in `cozy-desk-working`. The chair is removed from the desk pixels and linked as separate nonblocking `cozy-chair-back` and `cozy-chair-front` assets, both retaining their **64×64**, `(8,8)` source anchor, `chairOffset:(0,0)` and desk-local world `chairSeatAnchor:(24,41)`. Rotation-0 desk/back retain `zOrder:0`; the front retains `zOrder:18` (two pixels after the seat's feet). The back plus front reconstruct the full chair without double rendering an embedded chair. The visible backrest and armrests (source rows 0..49) belong to the front layer, covering a back-facing seated torso; only the base below it (rows 50..63) belongs behind. Both layers offer additive `default`, `color-2` and `color-3` states for all three authored chair colours, at each existing rotation; choose the same state on both linked instances. Other legacy rotations transform the contact coordinate and keep each linked layer's depth relative to its work point. Without env2 components the original fallback desk remains available.

The back desk's short static stand extension occupies source `x=31..32, y=25..29`, connecting the raised monitor base to the tabletop at `y=30`. It copies the authored base colour and remains byte-identical across all screen states and phases.

Optional furniture-variant fields are loadable by Core; omission preserves existing packs:

| Field | Meaning |
| --- | --- |
| `chairBackId`, `chairFrontId` | Linked **furniture asset IDs**, with compatible rotation and a `default` state; separate entries in the editor palette. |
| `chairOffset` | `{x,y}` in **world pixels**, from the workstation origin to both linked chair origins. Bundled offsets are `(0,0)` because placement is baked into the chair canvases. |
| `chairSeatAnchor` | `{x,y}` in **world pixels**, pelvis contact relative to the workstation origin; it may lie outside the blocked footprint. This is already scaled/placed, not a source-cell coordinate. |

The bundled unsplit lounge seats also publish `chairSeatAnchor`: sofa `(16,18)`, armchair `(16,19)`, counter stool `(8,-5)` at rotation 0. These contacts lie on the authored seat/cushion, while the tile approach remains unchanged. On a multi-seat sofa the contact is relative to its authored napping point; each overflow occupant adds their work-tile displacement from that reference, preserving separate positions through rotations. A host without a napping point uses its first overflow tile (ordered by X then Y) as the reference. Negative contact coordinates are allowed. Plain lounge seats retain grid-based overflow rotation in the editor; contact-based placement during rotation is reserved for workstations with linked chair layers.

Authored env3 workstations retain their profile pixels and have only rotation **90/right** and **270/left**. No pixel rotation is applied. IDs are `cozy-desk-side-wood`, `cozy-desk-side-white`, `cozy-desk-side-double-wood`, `cozy-desk-side-double-white`, and `cozy-laptop-table-side`. Each has a **3×2** footprint and six blocked cells. CRT desks have states `default` (authored `on`), `off`, `working` (three authored `typing` frames at 3 fps), and `screensaver` (static `on`). The laptop table has only `default`, **kind `table`, `points:[]`**; explicit layout overflow seats can use its contact metadata without creating another desk.

| Rotation / worker facing | CRT desk work point | `chairSeatAnchor` world pixels |
| --- | --- | --- |
| 90 / right | `seat:(-1,1)`, action `typing`, facing `right` | `(-3,15)` |
| 270 / left | `seat:(3,1)`, action `typing`, facing `left` | `(50,15)` |

These work points are outside the blocked desk and face its adjacent edge. Their floor points differ from the chair base by 5/6 pixels horizontally; pelvis contact is 17 pixels above that base. All five workstations link `cozy-chair-side-back` / `cozy-chair-side-front`, with the same two rotations, 3×2 footprints, `blockedCells:[]`, `points:[]` and `chairOffset:(0,0)`. The **128×64** runtime cells pad the authored 64×64 desk at `(32,0)` with source anchor `(40,29)`; chair cells bake the original offsets `(-26,0)` / `(26,0)` without cropping their bases. Scale remains 1. Side back/desk/front `zOrder` is `-2/2/4`, giving back → character → desk/table → front at the work point. Draw either linked layers or a complete chair, never both.

`cozy-counter-stool` is an original backless pantry stool with 32×32 cells, scale 1, a 1×1 footprint, `kind:chair`, layer 1, `blockedCells:[]` and `points:[]`. Its source anchor is `(8,14)`; the rotation-0 feet at `(16,30)` land at local world `(8,16)`, the tile's bottom centre. Its static clips and explicit variants cover all four rotations. Core places the pantry props as `pantry-stool-1` and `pantry-stool-2`; overflow seat allocation remains independent of furniture interaction points.

Legacy furniture palette entries have explicit 0/90/180/270 variants. The builder rotates each whole square source cell clockwise into extra rows of the same sheet and suffixes its clips with `-r90`, `-r180`, `-r270`. A clockwise turn swaps footprint width/height and maps each local blocked cell or interaction point `(x,y)` to `(height-1-y,x)`, including points outside the footprint. Facing advances `up → right → down → left`. For a cell of height `cellH`, the new variant anchor is `(cellH-oldAnchorY-footprintH*16, oldAnchorX)`. The sheet's default anchor stays fixed; each rotated clip supplies world-pixel `footOffset = defaultAnchor-newAnchor` (bundled scale 1). The selected `SpriteFrame` carries this offset, so the viewer draws the baked pixels directly. Env3 profile workstations and their chair layers bypass this pass and select their authored 90/270 variants. Tile art is unchanged.

Footprint is in tiles. Missing `blockedCells` blocks the whole rectangle; `[]` makes it walkable. Points are local tile offsets and may lie outside the footprint, but must resolve to reachable floor. `typing` marks the work seat (currently one work seat per desk instance). Coffee, water, TV, nap and chat/phone points supply activities. One agent reserves a point at a time; extra lounge floor is used for phone fallback. Chat requires two available points and a reciprocal idle partner.

`sheetId` + `animation` select the drawing. If omitted, known kinds use cached procedural furniture; unknown kinds use a neutral rectangle. `layer` 0=floor, 1=scene, 2=overlay; default 1. Sort Y=`(instance.y+instance.height)*16 + zOrder`, where `zOrder` is a finite world-pixel offset. Characters sort at their feet. Resolve ties by ordinal instance/agent ID. All bubbles follow the scene. Use negative/positive offsets to put chair backs or desk fronts on the intended side of seated characters. Exact frame rectangles are published in `state.json.furnitureSprites`.

### Tile types

`tileTypes:[{"id":3,"kind":"floor","color":"#AABBCC","sheetId":"tiles","animation":"floor_blue"}]`. IDs are integers; kinds `floor/wall/void`; color is `#RRGGBB`. Sheet/animation are optional and select their first frame, drawn into 16×16 world pixels. Floor permits walking; wall/void blocks it. Defaults: 0 wall, 1 workspace floor, 2 pantry/lounge floor, 255 void. Tile visuals are static and cached.

## layout.json

Start from [Data/layout.json](../src/Vibedotcom.Core/Data/layout.json), which includes the authored floor tile array. Custom maps may omit `tiles` to generate a bordered floor. A custom map can supply:

| Field | Meaning |
| --- | --- |
| `version` | 1. |
| `widthTiles`, `heightTiles`, `tileSize` | Dimensions 8–128, tileSize exactly 16. |
| `tiles` | Optional row-major integer array of exactly width×height, referring to tile type IDs. Includes explicit interior walls. |
| `door` | `{x,y}` walkable entrance/exit tile. |
| `zones` | Required unique names `workspace/pantry/lounge/door`, each `{name,x,y,width,height,points?}`. Extra named zones allowed. Zone points use the same local-offset format as furniture points. Zones do not override supplied collision tiles. |
| `furniture` | Instances `{id,kind,assetId?,x,y,width,height,rotation?,state?,agentId?}`. ID unique, assetId defaults to kind; rotation defaults 0, state `default`. Width/height must equal the chosen variant footprint. AgentId is runtime seat ownership; author with null/omitted. |
| `autoDesks` | Optional custom-layout grid `{assetId,x,y,spacingX,spacingY,maxColumns,minColumns,adaptiveColumns,expand}`. `minColumns` defaults to 1. The bundled default omits it and uses twelve fixed desks. |

The bundled office keeps its twelve authored desks and fixed bounds as sessions arrive or leave. Core assigns workers to available desks, keeps occupied seats stable and supplies laptop placement for overflow agents. Renderers consume this assignment and never generate extra desks. The optional custom-layout grid remains an authoring option; placement must leave reachable work points and clear walking space. Bounds, IDs, variants, blocked-cell overlap, door and point reachability are validated before an edited layout is applied.

The layout editor saves the editable source `layout.json` explicitly. Tracker creates it only if absent in its output directory and preserves an existing source. `runtime-layout.json` and **`state.json.office` contain Core's resolved layout**; Windhawk renders that office and the supplied furniture frames rather than assigning desks or running an independent simulation. Editor changes and runtime fields are described by the [state v4 contract](state-schema.md), while asset and layout JSON retain `version:1`.

Asset exports use content-addressed `sheets/<sha256>.png` filenames. The manifest embedded in a state snapshot is authoritative, so old snapshots retain their matching PNGs during another tracker startup. There is no automatic sheet cleanup. See [state-schema.md](state-schema.md) for the runtime contract and render equations.

## Optional production `blink` metadata

Face-locked **production** manifests under `assets/art/**/locked/` may add a top-level `blink` object without changing `animation`, `frame_layout`, row counts, timing, or the existing runtime version-1 schema:

```json
"blink": {
  "down": {"file":"blink-down.png", "source":{"state":"walk_down", "frame":0}, "durationMs":100},
  "left": {"file":"blink-left.png", "source":{"state":"walk_left", "frame":0}, "durationMs":100},
  "right": {"file":"blink-right.png", "source":{"state":"walk_right", "frame":0}, "durationMs":100}
}
```

Keys are authored visible facings (`down`, `left`, `right`); back views have no blink. `file` is a PNG path relative to that manifest, with the same native cell size, straight alpha and foot anchor as its source frame. `source.state` and zero-based `source.frame` identify the normal frame used to make the closed-eye variant. `durationMs` is a suggested closure duration, not an automatic schedule. Sheet variants prefix filenames with the sheet name to avoid collisions. The field is optional; absence preserves existing behavior. Existing consumers may ignore it. The pack builder consumes these optional exports from current locked output. Using the published face-lock eye boxes and integer offsets, it transfers only the changed closed-eye pixels onto each eligible still pose, then applies the same character normalization. Runtime clips are named `blink-idle`, `blink-typing`, `blink-coffee`, `blink-phone`, `blink-chatting`, and `blink-waiting`, with matching direction/frame timing. Back views and sleeping/walking/report poses have no blink copies. Core schedules a 150 ms closure every 3?6 seconds per agent while stationary, using a separate per-agent random stream so leisure behavior is unaffected. Missing blink clips preserve the normal frame. WPF temporarily renders at 30 fps during closures, otherwise retaining its normal idle cadence. Full-cell blink PNGs carry a walking body pose; the builder never substitutes that body into a still animation.

An additional optional `facelock` object identifies the deterministic post-process version, relative QA/provenance report and source-byte digest. It is also production-only metadata. See [facelock.md](facelock.md) for rerun commands, pixel comparison definitions and limits.
