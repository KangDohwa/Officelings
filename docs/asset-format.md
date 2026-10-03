# Asset and layout format (version 1)

The WPF viewer and tracker use the same language-neutral JSON records. No .NET type names, enums-as-integers, paths to source logs, or serialized executable code occur in these formats. Separate art production can replace the fallback without changing the renderer or simulation. Generation-tool manifests under `assets/art/` are production records, **not** this runtime manifest; write a small adapter manifest pointing to the final PNGs.

## Loading

- Normal startup reads `%LOCALAPPDATA%\Vibedotcom\assets\manifest.json` and `%LOCALAPPDATA%\Vibedotcom\layout.json`. `--asset-dir DIR` / `--layout FILE` override them in both executables. Demo/QA use the bundled fallback unless explicitly overridden.
- Missing files use the embedded [fallback catalog](../src/Vibedotcom.Core/Data/assets.json) and [default layout](../src/Vibedotcom.Core/Data/layout.json). Invalid input produces a warning and falls back; input files are never repaired or overwritten. Restart to reload. There is no live editor/hot reload.
- JSON: UTF-8, camelCase; `version:1`; unknown additional fields tolerated. Maximum JSON 4 MiB. PNGs are relative local files within the pack directory, maximum 32 MiB each, 8-bit non-interlaced grayscale/RGB/indexed/gray-alpha/RGBA, maximum 8192 per axis and 16,777,216 pixels. Use transparent RGBA for characters and furniture. Dimensions, compressed scanline lengths, clip bounds and PNG filters are checked before use. There is no palette conversion at runtime.
- A pack is loaded as a unit. `fallback` is a reserved sheet ID; the procedural atlas always supplies fallback characters and bubbles. Custom furniture/tile IDs override matching fallback definitions; omitted furniture/tile definitions are inherited. The custom character roster replaces the default roster. If a roster has no eligible design for a role/provider, that agent uses fallback art.

## manifest.json

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
| `width`, `height`, `sha256` | Derived by the loader/exporter. Do not supply guessed image dimensions. |

Character animation selection: exact action/direction → action/`any` → first `idle` clip → first clip. `entering/walking/exiting` select `walk`. Other action names are `typing`, `thinking`, `waiting`, `pickup`, `reporting`, `coffee`, `water`, `tv`, `phone`, `napping`, `chatting`, `gone`. Left frames are authored, never automatically mirrored. Convert production names `walk_down` etc. into `name:"walk",direction:"down"`; `typing_back→typing/up`, `typing_side→typing/right`, `report→reporting`, `sleep→napping`, `talk→chatting`, `surprised→waiting`. Missing specialized poses can reuse `idle`; supply more rows when the art is ready.

Roster entries: unique `id`, `sheetId`, optional `provider` (`codex/claude/antigravity`) and optional `role` (`supervisor/worker`). Omission means eligible for all. Agent IDs use uint32 FNV-1a over UTF-16 code units (offset 2166136261, multiply 16777619 with wrapping). Hash chooses a start in eligible roster order; linear probing picks an unused design when available. Newcomers in one update are assigned in ordinal ID order. Assignment remains fixed while the agent is tracked; designs repeat when the eligible roster is exhausted. A changed active roster after a restart may resolve a hash collision differently. Provider markers are drawn separately on desks; author role/provider clothing in filtered sheets when desired.

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

The referenced sheet needs an `any` clip named `desk-side`. Each `(rotation,state)` is explicit and unique; rotation is 0/90/180/270 clockwise, with its own geometry and authored frames. The engine never rotates coordinates or pixels implicitly. States are selected by the layout instance; animations start at simulation startup. No runtime furniture switch/editor is exposed. Custom definitions should use distinct IDs when their geometry differs from the default desk/pantry/etc.; the bundled layout requires its original footprints.

Footprint is in tiles. Missing `blockedCells` blocks the whole rectangle; `[]` makes it walkable. Points are local tile offsets and may lie outside the footprint, but must resolve to reachable floor. `typing` marks the work seat (currently one work seat per desk instance). Coffee, water, TV, nap and chat/phone points supply activities. One agent reserves a point at a time; extra lounge floor is used for phone fallback. Chat requires two available points and a reciprocal idle partner.

`sheetId` + `animation` select the drawing. If omitted, known kinds use cached procedural furniture; unknown kinds use a neutral rectangle. `layer` 0=floor, 1=scene, 2=overlay; default 1. Sort Y=`(instance.y+instance.height)*16 + zOrder`, where `zOrder` is a finite world-pixel offset. Characters sort at their feet. Resolve ties by ordinal instance/agent ID. All bubbles follow the scene. Use negative/positive offsets to put chair backs or desk fronts on the intended side of seated characters. Exact frame rectangles are published in `state.json.furnitureSprites`.

### Tile types

`tileTypes:[{"id":3,"kind":"floor","color":"#AABBCC","sheetId":"tiles","animation":"floor_blue"}]`. IDs are integers; kinds `floor/wall/void`; color is `#RRGGBB`. Sheet/animation are optional and select their first frame, drawn into 16×16 world pixels. Floor permits walking; wall/void blocks it. Defaults: 0 wall, 1 workspace floor, 2 pantry/lounge floor, 255 void. Tile visuals are static and cached.

## layout.json

Start from [Data/layout.json](../src/Vibedotcom.Core/Data/layout.json), which omits `tiles` to generate its bordered floor. Its expanded runtime version includes the concrete tile array. A custom map can supply:

| Field | Meaning |
| --- | --- |
| `version` | 1. |
| `widthTiles`, `heightTiles`, `tileSize` | Dimensions 8–128, tileSize exactly 16. |
| `tiles` | Optional row-major integer array of exactly width×height, referring to tile type IDs. Includes explicit interior walls. |
| `door` | `{x,y}` walkable entrance/exit tile. |
| `zones` | Required unique names `workspace/pantry/lounge/door`, each `{name,x,y,width,height,points?}`. Extra named zones allowed. Zone points use the same local-offset format as furniture points. Zones do not override supplied collision tiles. |
| `furniture` | Instances `{id,kind,assetId?,x,y,width,height,rotation?,state?,agentId?}`. ID unique, assetId defaults to kind; rotation defaults 0, state `default`. Width/height must equal the chosen variant footprint. AgentId is runtime seat ownership; author with null/omitted. |
| `autoDesks` | Optional grid `{assetId,x,y,spacingX,spacingY,maxColumns,adaptiveColumns,expand}`. Automatic desks supplement fixed desks. Defaults match the bundled map: desk at (2,5), 5×4 spacing, maxColumns 4, adaptive/expand true. |

Automatic seats keep stable occupied slots; gaps from departed sessions retain an empty desk until reused. Adaptive columns choose 1/2/3/4 for capacities ≤2/≤6/≤15/larger, capped by maxColumns. Horizontal expansion inserts columns after the workspace's right edge and shifts zones/furniture to its right; vertical expansion extends floor and moves the door down. Use `adaptiveColumns:false` for a custom fixed-width map, and place a clear grid with enough spacing. `expand:false` keeps bounds fixed. Without `autoDesks`, the authored work seats must accommodate all tracked agents. Out-of-space layouts report an error rather than placing overlapping desks. Bounds/ID/variant/blocked overlap/door and point reachability are validated, including the generated desks.

Normal WPF loading never saves layout edits. Tracker creates `layout.json` only if absent in its output directory, publishes expanded `runtime-layout.json` on membership changes, and preserves an existing editable `layout.json`. **Windhawk reads `layout.json` as the source, but renders the exact expanded `state.json.office` (or matching runtime layout)**; it must not assign desks or run an independent simulation.

Asset exports use content-addressed `sheets/<sha256>.png` filenames. The manifest embedded in a state snapshot is authoritative, so old snapshots retain their matching PNGs during another tracker startup. There is no automatic sheet cleanup. See [state-schema.md](state-schema.md) for the runtime contract and render equations.
