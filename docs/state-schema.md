# Local state contract (version 3)

The tracker and WPF engine now share pluggable PNG sheets, a JSON asset manifest, and a data-driven office. Current writers emit **`schemaVersion:3`**, asset/layout format **1**, and legacy fallback **`atlas.version:2`**. A strict v1/v2 consumer must reject v3 until it implements the sheet/anchor/tile rules below. There is no implemented Windhawk viewer in this change; the C++ consumer is separate.

## Files and delivery

Default output: `%LOCALAPPDATA%\Vibedotcom`. Source layout is `layout.json`; installed source art is `assets/assets.json` or `assets/manifest.json`. Without an installed pack, both executables use the generated art embedded from `assets/pack/`. See [asset-format.md](asset-format.md) for loading precedence, pack rebuild instructions and full authoring fields.

| File | Contract |
| --- | --- |
| `state.json` | Atomic UTF-8 snapshot; authoritative office, agent poses, asset metadata and furniture frames. |
| `layout.json` | Editable source layout, version 1. Tracker creates it only when missing and never overwrites an existing source. |
| `runtime-layout.json` | Expanded, assigned map, version 1; replaced on membership changes. `state.json.office` is authoritative when these two reads span an update. |
| `manifest.json` | Exported asset metadata, version 1. Use the manifest embedded in the same snapshot to avoid a startup replacement race. |
| `sheets/<sha256>.png` | Immutable content-addressed PNGs used by that snapshot. All paths relative to output; no URLs/absolute paths. |
| `atlas.png` | Fixed legacy fallback/bubble atlas v2, also published as a hashed sheet with ID `fallback`. |

PNG files and exported manifest are written before the first snapshot. A crash may leave unused PNGs; there is no automatic cleanup. Individual JSON writes flush and rename a unique sibling `.tmp`; a set of files is **not** one transaction. Retain the last valid snapshot and its image cache until all sheets referenced by a new snapshot are valid. Close Windows reads with `FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE`. Unknown additional fields are tolerated; unsupported major versions rejected. Bounds and finite coordinates must be checked before rendering.

Snapshot sequence is positive signed int64, increasing within a tracker run and reset on restart. `generatedAt` is ISO 8601 with UTC offset. Nominal snapshots 200ms, source discovery 3s; sequence can skip. A snapshot older than 10s is disconnected. Readable stale state should remain available. WPF tracks logs itself on a background task rather than consuming this file.

## Version 3 root

All existing root/agent fields remain (see the historical tables below). Required runtime additions:

| Field | Meaning |
| --- | --- |
| `schemaVersion` | 3. |
| `assets` | Complete asset format 1 manifest: `version,sheets,furniture,characters,tileTypes`. Exported sheet files include derived `width,height,sha256`. This object is authoritative over sibling manifest.json. |
| `office` | Layout format 1: dimensions, door, full row-major `tiles`, furniture instances with stable IDs/asset IDs/rotation/state, named `zones`. `autoDesks:null` in resolved runtime maps. |
| `furnitureSprites` | Array of `{id,sprite}` for instances using a PNG clip. Selected frame is authoritative; an empty array means all furniture uses fallback primitives. The id refers to `office.furniture[].id`. |
| `agents[].pose.characterId` | Assigned roster design ID. Repeats only when eligible roster designs are exhausted; stable while the agent is tracked. |
| `agents[].pose.sprite` | The exact selected `SpriteFrame` below. |
| `pose.spriteRow`, `spriteColumn` | Convenience aliases for the selected sheet row/column. **They do not necessarily index atlas.png in v3.** Use `pose.sprite.sheetId`. |

Agent semantic metadata, provider enum, states, timestamps, opacity, carryingPaper, reportOnDesk, reaction and chatPartnerId retain v2 meanings. The agent/task fields in the historical table are unchanged.

Example selected frame from a 32x64 art sheet displayed at half size:

```json
{
  "characterId":"braid",
  "spriteRow":1, "spriteColumn":2,
  "sprite": {
    "sheetId":"people", "row":1, "column":2,
    "x":64, "y":64, "width":32, "height":64,
    "anchorX":16, "anchorY":60, "scale":0.5,
    "offsetX":0, "offsetY":0
  }
}
```

`x/y/width/height` inside a SpriteFrame are **source pixels**, unlike `pose.x/y` in continuous world tiles. All source coordinates/dimensions are integers and stay inside the referenced sheet. Anchor is source pixels; offsets are world pixels after scaling. Scale and anchors must be finite. No implicit mirroring, tint, pixel rotation or provider inference is required in C++.

## Geometry and rendering

1. World extent is `office.widthTiles*tileSize` by `heightTiles*tileSize`; tileSize=16. Origin top-left, +X right/+Y down. Width/height 8-128. Tile IDs refer to `assets.tileTypes`; only `kind:floor` is walkable. Interior walls and void are explicit. Zone labels do not create walls. Rotation selects an explicitly authored furniture variant; do not rotate its points again.
2. Character feet: `footX=(pose.x+0.5)*16`, `footY=(pose.y+1)*16`. Destination rectangle: `(round(footX-anchorX*scale+offsetX), round(footY-anchorY*scale+offsetY), width*scale, height*scale)`. Pose remains at its collision tile while seated/sleeping offsets change only drawing.
3. Furniture PNG destination: `(instance.x*16-anchorX*scale+offsetX, instance.y*16-anchorY*scale+offsetY, width*scale, height*scale)`. Use `furnitureSprites[id].sprite`. Tile clip first frames are stretched into their 16x16 tile; fallback tiles use the supplied colors. Transparent background skips floor/wall, retains furniture and agents.
4. Draw base floor/wall, relationship lines, then a mixed furniture/character scene sorted by layer, world Y, ordinal stable ID. Furniture layer and zOrder come from its selected `(rotation,state)` variant; sortY=`(y+height)*16+zOrder`. Characters use layer 1 and footY. Bubbles and desk/name markers follow. Do not add visual seated/sleep offsets to sortY.
5. Apply pose.opacity to sprite, walking paper and bubble. The paper overlay uses world footX-10 when facing left, otherwise footX+4, and footY-12; size 6x8. Desk paper belongs to the desk with matching agentId. Shared bubbles still use the fallback atlas row 6 columns 0-8; put them centered at footX, 17 world pixels above the character destination's top.
6. Use nearest-neighbor and integer display zoom (standalone 1-6). Zoom/pan are camera changes, not simulation coordinates. Windhawk should render supplied poses/frames, not duplicate A*, seat selection, random activities or the roster hash.

Unknown furniture without art may use a neutral rectangle. The fallback furniture remains drawing primitives; a pixel-identical WPF/GDI furniture comparison requires a PNG furniture pack. Core-selected PNG rectangles/positions are shared by both renderers.

## Lifecycle and timing

Door → A* to own seat → typing/thinking/waiting. Four-neighbor Manhattan A* avoids nonfloor tiles and each variant's blockedCells. Reporting → own seat → pickup .9s → carry beside parent seat → handover 1.4s → leisure. Missing parent uses own seat vicinity. Paper lasts 12s, parent reaction 3s. Pending delivery survives semantic transition to idle; resumed working/thinking/waiting or gone cancels it. Gone → door → 1.2s fade → hidden; tracker retention remains 60s.

Idle chooses coffee/water 10–18s, TV 20–33s, nap 28–42s, phone/chat 14–24s, avoiding the immediately previous activity. **Duration starts on arrival**, not during travel. Interaction points are reserved where available. Reciprocal chat starts its shared duration when both partners arrive. Busy/missing stations fall back to phone at free floor near the lounge. Characters can cross/share corridor tiles; moving-body collision avoidance is not implemented.

Roster hash + collision probing is specified in asset-format.md. Newcomers walk in at the door even when a desk expansion changes the map. Vacated middle slots retain an unassigned desk until reused; existing design assignments remain stable. One work point per desk instance is used; add separate desks for more seats. Without an automatic grid the authored seats must accommodate the active set. PNG clips use action-relative elapsed time and supplied FPS; missing action/direction falls back to idle. Procedural characters preserve v2 pixel rows/columns, but their phase is now action-relative. **Always trust supplied rectangles and indexes.**

WPF runs at up to 30fps during movement, 8fps otherwise, caches static floor/furniture and decoded crops, and polls logs off the UI thread. `--demo` is fabricated and exercises entry, work/thinking/waiting, report transfer, varied breaks, exit and repeated sessions. `--smoke-test` creates offscreen zoom/phase/tooltip/transparency PNGs without opening any window. `--qa --quit-after N` is a separately guarded nonactivating normal-mode instance; desktop/topmost are disabled for QA. Native desktop/click-through behavior is covered with fake topology/policy tests; actual shell display is best-effort and was not exercised on the live laptop.

## Version history

- v3: arbitrary PNG sheet cells, explicit anchors/scale, format 1 assets/layout, tile collision, rotated interactions, roster assignment, A*, authoritative furniture frames. v2 consumers must be updated.
- v2: fixed 16x24 procedural atlas with direction/identity rows and paper/chat/fade fields.
- v1: 208×168 fixed atlas, no direction/identity variants. Historical reference follows; its fixed BFS geometry and Unix-time frame formula do **not** describe v3.

---

# Historical version 2 contract

The tracker writes sibling `state.json` and `atlas.png` (default `%LOCALAPPDATA%\Vibedotcom`) for the separately developed Windhawk consumer.

Version 2 preserves version 1's JSON field names/types, 16×24 cell size, first six character rows, tool-bubble row 6, and animation columns 0–12. It appends identity variants, directional frames and optional pose fields. **Atlas dimensions and facing/action values changed.** Strict version 1 readers must be updated to accept version 2 and use supplied dimensions/indexes. This preserves the old contract's structure/index meanings, rather than guaranteeing that an unchanged version 1 binary renders version 2.

## Delivery

- `atlas.png` is generated and atomically replaced at tracker startup, before the first snapshot. Version 2 dimensions/pixels are fixed.
- `state.json` is UTF-8 without BOM, camelCase, lowercase string enums, explicit nulls. A unique sibling `.tmp` is flushed and moved atomically over the destination. Ignore temporary files.
- Nominal snapshots: 200ms; discovery: 3s (configurable). `sequence` starts at 1 and resets on restart. `generatedAt` distinguishes restarts; sequence numbers need not be contiguous.
- Windows readers: `FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE`; close after reading. Retain last valid snapshot after transient sharing/missing-file failures.
- Validate `schemaVersion`, `atlas.version`, dimensions, finite coordinates and sprite indexes. This contract uses `schemaVersion:2`, `atlas.version:2`. Flag snapshots older than 10s as disconnected. Missing files mean no snapshot yet.
- Timestamps are ISO 8601 `DateTimeOffset` strings with a UTC offset. Sequence is positive signed int64. Coordinates/counts are JSON numbers.
- Unknown additional JSON fields must be tolerated. Unknown required major versions must be rejected.

## JSON example

Invented metadata. Furniture list shortened; render the complete supplied list.

~~~json
{
  "schemaVersion": 2,
  "generatedAt": "2026-10-03T12:00:10+00:00",
  "sequence": 1,
  "atlas": {
    "file": "atlas.png", "width": 720, "height": 1176,
    "cellWidth": 16, "cellHeight": 24, "columns": 45, "rows": 49, "version": 2
  },
  "office": {
    "widthTiles": 17, "heightTiles": 21, "tileSize": 16,
    "door": { "x": 1, "y": 18 },
    "furniture": [
      { "kind": "pantry", "x": 8, "y": 5, "width": 6, "height": 2, "agentId": null },
      { "kind": "desk", "x": 2, "y": 5, "width": 3, "height": 2, "agentId": "codex:example" }
    ]
  },
  "agents": [
    {
      "agent": {
        "id": "codex:example", "provider": "codex", "parentId": null,
        "isSubagent": false, "name": "Example", "role": null,
        "model": "example-model", "cwd": "C:/Example",
        "task": "Inspect the example file.", "tool": "exec_command", "toolKind": "shell",
        "state": "working", "lastSeen": "2026-10-03T12:00:09+00:00",
        "stateSince": "2026-10-03T12:00:08+00:00", "bestEffort": false
      },
      "pose": {
        "x": 3.0, "y": 7.0, "desk": { "x": 3, "y": 7 },
        "action": "typing", "facing": "up", "spriteRow": 0, "spriteColumn": 3,
        "visible": true, "opacity": 1.0, "carryingPaper": false,
        "reportOnDesk": false, "reaction": null, "chatPartnerId": null
      }
    }
  ],
  "warnings": []
}
~~~

Sprite indexes illustrate valid indexes, rather than the example ID's hash/time result.

## Root and geometry

| Field | Type | Meaning |
| --- | --- | --- |
| `schemaVersion` | integer | 2 for this contract, 1 for legacy. |
| `generatedAt` | timestamp | Generation time independent of log events. |
| `sequence` | int64 | Increasing within one tracker process. |
| `atlas` | object | Eight required fields above. `file` is sibling `atlas.png`, not a URL. |
| `office` | object | Shared simulation geometry. |
| `agents` | array | Required `agent` and `pose` objects. Empty valid. Unique IDs; order is not a permanent desk assignment. |
| `warnings` | string array | Nonfatal warnings; may contain local paths. Display as text. |
| `office.widthTiles`, `heightTiles` | positive integers | Variable bounds. Pixel extent = tiles × tileSize. Never assume 48×28. |
| `office.tileSize` | integer | 16 in both versions, independent of display zoom. |
| `office.door` | `{x:int,y:int}` | Walkable entrance/exit cell. |
| `office.furniture` | array | Required `kind:string,x:int,y:int,width:int,height:int,agentId:string|null`. Tile rectangles, top-left inclusive/bottom-right exclusive. |

Furniture kinds: `desk`, `pantry`, `water` (new), `table`, `tv`, `sofa`, `plant`. Complete rectangles block BFS movement. Unknown kinds may use a neutral rectangle. `agentId` is the desk owner, otherwise null; retained gone agents may still own desks. `pose.desk` is the walkable approach below its desktop.

Walkable bounds: `1 ≤ x < widthTiles−1`, `3 ≤ y < heightTiles−1`, excluding furniture. Top three rows are wall. Geometry is rebuilt when membership changes. Capacity = highest occupied slot +1 (zero if empty). Columns: 1 if capacity≤2, 2 if≤6, 3 if≤15, otherwise 4. Lounge X=columns*5+3; width=loungeX+9; height=max(21,7+ceil(capacity/columns)*4). Consumers must use supplied geometry instead of duplicating this calculation.

## Agent fields (unchanged, all required)

| Field | Type | Meaning |
| --- | --- | --- |
| `id` | nonempty string | Opaque stable ID: Codex `codex:<session-id>`; Claude supervisor `claude:<session-id>`, worker `claude:<parent-session-id>/agent-<agent-id>`; Antigravity `antigravity:<conversation-id>`. Do not split to infer parents. |
| `provider` | enum string | `codex`, `claude`, `antigravity`. |
| `parentId` | string/null | Full parent ID; may be absent/stale. Null does not imply supervisor. |
| `isSubagent` | boolean | Worker/sidechain classification; determines clothing even without a known parent. |
| `name` | string | Nickname or provider/worker label. |
| `role`, `model`, `cwd` | string/null | Observed metadata, null=unknown. |
| `task` | string | Last user prompt, whitespace collapsed, truncated to 240 UTF-16 code units. Empty=unknown. Tool results never overwrite it. |
| `tool` | string/null | Tool name, never raw arguments. Codex exec wrappers use the first detected nested tool name where possible. |
| `toolKind` | string/null | `shell`, `file`, `search`, `agent`, `other`, or null. |
| `state` | enum string | `working`, `thinking`, `waiting`, `reporting`, `idle`, `gone`. Semantic state independent of pose. |
| `lastSeen` | timestamp | Latest source write; not process enumeration. |
| `stateSince` | timestamp | Semantic state start; elapsed=max(0,generatedAt−stateSince). |
| `bestEffort` | boolean | Always true for Antigravity; false does not guarantee live process/approval detection. |

Tracker policy unchanged: explicit input request / parent turn completion → waiting; worker completion → reporting for 12s → idle; working/thinking without writes for working timeout → idle; gone timeout or session end → gone. Waiting does not time into idle. Gone retention=60s; old sessions omitted at cold start.

## Pose fields and lifecycle

| Field | Type | Meaning |
| --- | --- | --- |
| `x`, `y` | finite numbers | Continuous occupied tile, X right/Y down. |
| `desk` | `{x:int,y:int}` | Assigned approach tile, independent of break destination. |
| `action` | string | `entering`, `walking`, `typing`, `thinking`, `waiting`, `pickup` (new), `reporting`, `coffee`, `tv`, `phone`, `napping`, `chatting`, `water` (new), `exiting`, `gone`. |
| `facing` | string | `down`, `up` (new), `right`, `left`. |
| `spriteRow` | integer | 0–48 excluding row 6. Authoritative identity/provider/role row. |
| `spriteColumn` | integer | 0–44, authoritative frame. |
| `visible` | boolean | False after exit fade. Suppress sprite and line when false. |
| `opacity` | optional number | 0–1, default 1. Door fade. |
| `carryingPaper` | optional boolean | Default false. Draw paper beside walking sprite. |
| `reportOnDesk` | optional boolean | Default false. Paper on this agent's assigned desktop. |
| `reaction` | optional string/null | Default null. `!` selects bubble 5; unknown values may be ignored. |
| `chatPartnerId` | optional string/null | Default null. Reciprocal partner; it may still be approaching its station. |

Entry: door → own desk. Completion: own desk → pickup 0.9s → carry to walkable cell beside parent (desk.x+1,desk.y, nearest free fallback) → handover 1.4s → leisure. Parent paper lasts 12s, `!` 3s. Missing parent uses own desk vicinity. Pending delivery survives semantic transition to idle; new working/thinking/waiting/gone cancels it. Still-reporting semantic state does not repeatedly deliver the same paper.

Gone: door → 1.2s opacity fade → invisible until tracker forgets it. Leisure avoids immediate repetition and reserves stations where feasible: coffee/water 10–18s, TV 20–33s, nap 28–42s, phone/chat 14–24s (travel included). Chat partners share expiry. Saturated stations fall back to phone on a free lounge cell. No video/network/audio.

### Rendering

- Foot point: `footX=(x+0.5)*16`, `footY=(y+1)*16`; sprite destination `(round(footX−8),round(footY−24),16,24)`.
- **Version 2 walking frames are already oriented; do not mirror left.** Version 1 mirrors left.
- Furniture first, then parent lines, then sprites in ascending Y. Lines connect child center to parent's desk; app shows them during entering/walking/pickup/reporting. Suppress missing/invisible parents.
- Sleeping pose is horizontal. App shifts napping sprite upward 10 world pixels onto the sofa behind its approach cell.
- Walking paper: 6×8 rectangle at (spriteX+12,spriteY+12), or spriteX−2 when facing left. Desk paper belongs to furniture associated with pose.desk.
- Bubbles: row 6 at (spriteX,spriteY−17,16,24). Tool only while typing; reaction takes priority. Waiting may use exclamation, nap sleeping z, paired chat dots, coffee occasional steam.
- Always nearest-neighbor. Standalone fits at integer zoom 1–6; explicit zoom and drag pan supported. World exceeding viewport at 1× requires pan.
- Furniture/walls/floor are drawing primitives, not atlas tiles. App gently animates monitors, plants, TV and clock; consumer styling may differ while preserving geometry/poses.
- Trust pose over semantic state. Core owns BFS, lifecycle and randomness; consumer interpolation is optional.

## atlas.png version 2

**720×1176**, 8-bit/channel RGBA, straight alpha, non-interlaced, transparent. **45 columns ×49 rows**, **16×24** cells, no margins/gutters. Source rectangle=(column*16,row*24,16,24).

### Rows

| Rows | Meaning |
| --- | --- |
| 0,1 | Codex supervisor/worker, variant 0, teal. |
| 2,3 | Claude supervisor/worker, variant 0, orange. |
| 4,5 | Antigravity supervisor/worker, variant 0, blue/purple. Worker row exists even without detected workers. |
| 6 | Shared bubbles. |
| 7–12 | Variant 1, six provider/role rows in the order above. |
| 13–18 | Variant 2, same order. |
| 19–24 | Variant 3, same order. |
| 25–30 | Variant 4, same order. |
| 31–36 | Variant 5, same order. |
| 37–42 | Variant 6, same order. |
| 43–48 | Variant 7, same order. |

Variant is unsigned 32-bit FNV-1a over **UTF-16 code units**: initial 2166136261; each code unit applies (hash XOR codeUnit)*16777619, wrapping uint32; variant=hash%8. Role index=providerIndex*2+(isSubagent?1:0), provider indexes Codex=0, Claude=1, Antigravity=2. Row=role index for variant 0; otherwise 7+(variant−1)*6+role index. Supplied row is authoritative. Variants change skin, hair/style, glasses and shirt tone. Supervisors have tie/gold badge (back stripe while typing); workers small white lanyard/badge.

### Character columns

| Column | Animation/frame |
| --- | --- |
| 0 | Standing front. |
| 1,2 | Legacy right walking A/B. |
| 3,4 | Typing up A/B, animated hands. |
| 5 | Thinking. |
| 6 | Waiting. |
| 7 | Legacy report with paper. |
| 8 | Legacy coffee. |
| 9 | Legacy TV (back). |
| 10 | Legacy phone. |
| 11 | Legacy horizontal nap. |
| 12 | Legacy chat. |
| 13–16 | Walking down, four steps. |
| 17–20 | Walking up, four steps. |
| 21–24 | Walking right, four steps. |
| 25–28 | Walking left, four steps. |
| 29,30 | Pickup A/B. |
| 31,32 | Handover A/B. |
| 33,34 | Coffee A/B, cup lifted. |
| 35,36 | TV A/B, back. |
| 37,38 | Phone A/B, scrolling. |
| 39,40 | Nap A/B, horizontal breathing. |
| 41,42 | Chat A/B, gesture. |
| 43,44 | Water A/B, cup lifted. |

Phase tick=floor(unixMilliseconds/160)+variant. Walking base+tick%4; typing/pickup/handover base+tick%2. Coffee/water base+floor(tick/4)%2; TV /5; phone/chat /3; nap /6. Other poses static. Four walk steps include neutral recovery between alternating strides. 200ms snapshot sampling can skip frames; supplied index is authoritative.

Bubble row 6: columns 0 shell, 1 file, 2 search, 3 agent, 4 other (legacy), 5 exclamation, 6 sleeping z, 7 chat dots, 8 steam. Columns 9–44 transparent. Select bubbles from action/tool/reaction, never character spriteColumn.

## Version 1 reference

Legacy atlas **208×168**, 13 columns ×7 rows, same cells. Rows 0–5 provider/role without variety; row 6 bubble columns 0–4, transparent 5–12. Character columns 0–12 retain the legacy meanings above. Facing only right/left (mirror left). Version 2 optional pose fields absent: use defaults. Office width was 48, height started at 28; render supplied bounds nevertheless. Required root/agent field names and types are unchanged.
