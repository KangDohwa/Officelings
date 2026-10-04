# Local state contract (version 5)

The tracker and WPF engine share PNG sheets, an asset manifest, and an authored office. Writers emit **`schemaVersion:5`**, asset/layout format **1**, and legacy fallback **`atlas.version:2`**. Version 5 appends optional seated fields to v4; no v4 fields are removed, renamed or given new types. Strict older readers must explicitly accept v5. The current Windhawk consumer accepts v3, v4 and v5; unsupported versions are rejected with the last valid scene retained. See the [native guide](../windhawk/README.md).

## Scope: flat state, iso scenes and hosted presence

This file describes the local **flat** interchange. App `view:"iso"` uses the embedded iso pack and in-process `IIsoOffice`/`IsoScene`; current Tracker emits v5 only. S0's `schemaVersion:6`/`renderItems` contract is a future plan, not current exported state. Windhawk rejects schema 6. See [iso integration](iso-integration-contract.md), [S0 reference](iso-slice-s0.md) and [view differences](app-usage.md#flat-and-isometric-views).

Hosted sharing is a separate allowlisted `v:1` presence body (`officeId`, reduced agents, `ts`), never this local snapshot, assets or layout. See [sharing](app-usage.md#presence-sharing). Its server/public viewer is developed separately.

## Version 5 additions

Privacy adds an optional root `privacy` string: `off`, `tasks` or `full`. Current writers always emit it; missing input means `off`. This is additive and keeps schema version 5. Consumers can show a small privacy badge and should refresh tooltip/name caches when the level changes. Redaction already happens in Core: use the supplied metadata and `pose.displayName` without reconstructing text from older snapshots.

`tasks` removes task, tool, tool-category and role text, reduces `agent.cwd` to a folder leaf, and replaces prompt-derived Antigravity titles with generic names. `full` additionally removes `agent.cwd` and `agent.model` and replaces all agent/display names with stable provider labels. Removed optional fields are null and task is empty. In enabled modes, session IDs and parent/desk ownership references are consistent opaque `private:<sha256>` identifiers; renderers must treat IDs as opaque. Once used, those opaque IDs remain for the run even after switching off. Provider, state, timestamps, relationships and poses retain their existing types. Generic labels are stable during one tracker run. See [privacy](privacy.md) for controls and limits.

All v4 fields and existing `action`/semantic state names remain. Seating is an additional visual phase, independent of assignment, collision coordinates and `Working/Thinking/Waiting` state.

| Optional field | Meaning / default for older input |
| --- | --- |
| `pose.seatedVariant` | Selected `seated_back` (facing `up`), `seated_left` or `seated_right` clip; null outside seating or when the character has no seated sheet. |
| `pose.seatAnchor` | Absolute world-pixel `{x,y}` chair/pelvis contact; null outside seating. |
| `pose.chairBackId`, `pose.chairFrontId` | Chair layer **asset IDs**, related to the host variant metadata; null when absent. Linked layer instances remain in `office.furniture` and have their own supplied furniture frames. |
| `pose.seatPhase` | Null while standing/walking; `turn` for 120ms on arrival, `lower` for two 120ms frames, `seated` during work, `raise` for two 120ms frames before departure. Interrupted work still finishes raising before path movement. |
| `pose.seatPhaseFrame` | Integer 0/1 during `lower`/`raise`; default 0. |
| `assets.sheets[].rows[].seatAnchor` | Optional source-pixel pelvis contact, transformed with the baked integer character placement; distinct from the sheet foot anchor. |
| `assets.furniture[].variants[].chairBackId`, `chairFrontId` | Optional asset IDs for linked nonblocking chair layers. |
| variant `chairOffset` | Optional world-pixel offset from the host origin to linked chair instance origins; default zero. |
| variant `chairSeatAnchor` | Optional **world-pixel** contact relative to host origin. This differs from a clip's source-pixel `seatAnchor`. |

Core already resolves seated alignment and the lower/raise offset into `pose.sprite.offsetX/offsetY`; consumers use the existing SpriteFrame destination equation and must not apply seating alignment twice. `turn` selects a stationary facing idle pose. `lower`/`raise` use seated frame zero and offsets −2/−1 or −1/−3 world pixels. Working plays the four authored phases; Thinking uses slower taps and a short frame-2 head nod once per eight-second cycle; Waiting holds phase zero, retaining matching closed-eye pixels during a blink, and shows a `?` bubble. Missing seated sheets retain the legacy typing selection. No walking loop is used for sitting or raising.

Layer order at profile seats is chair back → character → desk/table edge → chair front/armrest. Back-facing desks retain the keyboard behind the worker and the visible chair backrest/armrests in front of the torso. Unsplit overflow chairs, stools and sofas draw their foreground over their seated occupant; their laptop indicator remains above it. Seated character drawing is clipped below `seatAnchor.y + 1` so authored calves/shoes never protrude. Source cells are not cropped/recentered independently. Keep the published tile position for navigation; WPF uses a common seat depth for these linked pieces, with small layer offsets, and normal world-Y depth for passing walkers.

V5 overhead plates use a 14-world-pixel body, a crisp 6px font, the project's original provider icons (10px at 1×, 12px from 2×) and a 2px head gap. Crown and badge clips identify supervisors and sub-agents. An optional local brand override still takes precedence and retains its 12px size. Labels contain at most **14 Unicode text elements plus an ellipsis**; full names stay in tooltips. Only intersecting plates move upward in 2px increments. Reserved desk labels keep v4 visibility semantics: only away owners receive them. Sit/stand transitions still occupy the chair and never receive a second reserved label. The default office remains 35×20 tiles with twelve fixed desks: back-facing rows and a four-seat authored profile pod; profile laptop tables add designated overflow seats. Membership never changes office dimensions or furniture.

All multi-frame runtime furniture is passed through the prop normalization API; only authored screen/LED/steam/water/glow regions animate. Static bodies are byte-identical.

## Files and delivery

Ambient life adds optional fields to version 5 without removing or changing existing fields. `pose.activitySpotId` identifies the furniture instance reserved for a break, including the walk there; `pose.activityPartnerId` identifies a reciprocal partner for ping-pong or chatting. Both default to null. New visual actions are `darts`, `pingpong`, `arcade`, `reading`, `beanbag`, `aquarium`, `vending`, `fridge` and `window`. Semantic agent state still means the same thing. Use supplied sprite offsets for cushion contacts; navigation remains on the approach tile. For `beanbag`, WPF rotates the body 90 degrees around its opaque centre and places that centre four world pixels above the supplied foot/contact position; the hit bounds rotate with it. Reading adds a small book above the seated lap. Single interaction points admit one agent; two-player activities reserve two distinct points.

WPF offscreen exports may include `ambient:{windowView,weather,stringLightsOn,winterDecor,localTime}` and a renderer-only `pets` array. Each pet has `id,sheetId,x,y,action,facing,sprite,followingId?,furnitureId?`; coordinates and SpriteFrame use the existing tile/foot destination equation. A missing array means no supplied pets. The tracker omits `pets` entirely. Pets do not become agents, occupy desks or affect assignment. Ordinary tracker snapshots omit `ambient`; a consumer can sample its own local clock and settings.

The current Windhawk v5 renderer accepts these optional fields/actions, uses supplied ambient or local time/weather, renders clock hands/lights/December decoration, supplied pet frames, dart/ping-pong overlays, bean-bag poses, dispensing flaps and reading props. It also uses the original 10/12px provider and crown/badge clips. Ordinary Tracker output still omits ambient/pets; native pets are supplied frames, not a second simulation. These are renderer implementations and offscreen test paths, not verification of Explorer injection.

Default output: `%LOCALAPPDATA%\Vibedotcom`. Editable source is `layout.json`; installed art is `assets/assets.json` or `assets/manifest.json`. Without an installed pack, both executables use the art embedded from `assets/pack/`. See [asset-format.md](asset-format.md) for asset loading/authoring. The v4 fixed-capacity rules below supersede that document's historical automatic expansion description.

| File | Contract |
| --- | --- |
| `state.json` | Atomic UTF-8 snapshot; authoritative office, agent poses, assets and furniture frames. |
| `layout.json` | Editable source layout, version 1. Tracker creates it only when missing; loading never overwrites an existing source. |
| `runtime-layout.json` | Fixed map with current ownership/monitor state, version 1; replaced when the resolved map changes. `state.json.office` is authoritative when separate reads span an update. |
| `manifest.json` | Exported asset metadata, version 1. Use the manifest in the same snapshot to avoid replacement races. |
| `sheets/<sha256>.png` | Immutable content-addressed PNGs. Paths relative to output; no URLs/absolute paths. |
| `atlas.png` | Legacy fallback/bubble atlas v2, also published as a hashed sheet with ID `fallback`. |

PNGs/manifest are written before the first snapshot. A crash can leave unused PNGs; there is no cleanup. Individual JSON writes flush and rename a unique sibling `.tmp`; the file set is not one transaction. Retain the last valid snapshot/image cache until every referenced sheet is valid. Windows reads use `FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE`. Tolerate unknown additional fields; reject unsupported versions. Validate bounds and finite coordinates before rendering.

Sequence is positive signed int64, increasing after each successful publication within one tracker run and reset on restart. `generatedAt` is ISO 8601 with UTC offset, refreshed for every publication attempt, including retries. The tracker updates moving characters at nominal 5 Hz and settled animation at 1–2 Hz, with no-live rooms at 1 Hz. Due blink/leisure/transition changes are sampled within those cadence bounds. It serializes changed agent, furniture or warning content and reuses compact office/asset JSON and the publication buffer. Changed snapshots publish immediately when observed; unchanged content gets a heartbeat every 3s. Source discovery defaults to 3s; `--interval-seconds` sets the moving interval and minimum periodic interval. Sequences can skip. Snapshots older than 10s are disconnected; readable stale state stays available. Atomic replacement and the schema are unchanged. WPF tracks logs itself on a background task.

## Version 4 fields

All root/agent fields from v3 remain, including semantic metadata, timestamps, opacity, carryingPaper, chatPartnerId, characterId and sprite. New parameters are appended with defaults in the Core records.

| Field | Meaning |
| --- | --- |
| `schemaVersion` | 4 in historical v4 input; current writers emit 5. |
| `assets` | Complete format 1 manifest: `version,sheets,furniture,characters,tileTypes`. Exported sheets include `width,height,sha256`. Authoritative over sibling manifest.json. |
| `office` | Format 1: fixed dimensions, door, full row-major `tiles`, furniture with stable IDs/assets/rotation/state, zones and `overflowSeats`. Resolved source/runtime maps have `autoDesks:null`. |
| `office.furniture[].deskId` | Optional stable work-station ID, unique among desks, independent of agent/order/coordinates. Legacy work furniture without it uses instance `id`. Instance IDs stay stable too. Foreground chair IDs are `desk.id + "-chair"`. |
| `office.furniture[].seat` | Optional `InteractionPoint` override `{id,x,y,action:"typing",facing}`. Coordinates relative to furniture origin, already authored for its rotation. Replaces typing points in `AssetPack.Variant`; other points/blocked cells stay unchanged. |
| `office.overflowSeats` | Array of `{id,x,y,facing,furnitureId?}` designated laptop seats. Absolute tile coordinates; unique IDs; facing `up/down/left/right`. Optional furnitureId references an instance, not an asset. Seats must be distinct from desk work points, walkable and reachable from the door. |
| `office.furniture[].monitorState` | Optional `off/working/screensaver`, supplied for every desk, including primitives. Unassigned = off; working only when its Working owner is at the work tile and typing; all other assigned activity = screensaver. Ownership persists during entry, breaks, thinking and waiting. |
| `furnitureSprites` | Array of `{id,sprite,monitorState?}` for PNG furniture. Selected frame is authoritative; id refers to furniture instance id. Desk status matches furniture metadata. Empty means all furniture uses primitives. Desk `state` selects a matching monitor variant only if supported with the same collision/work geometry; otherwise its authored state remains. |
| `agents[].pose.characterId` | Assigned roster design ID, stable while tracked; repeats when eligible designs are exhausted. |
| `agents[].pose.sprite` | Exact selected SpriteFrame below. |
| `pose.spriteRow/spriteColumn` | Aliases for selected sheet indexes; they need not index atlas.png. Use `sprite.sheetId`. |
| `agents[].pose.deskId` | Optional stable desk ID, relating to furniture.deskId. Null for overflow/queue. |
| `agents[].pose.overflowSeatId` | Optional reserved seat ID, relating to office.overflowSeats[].id. Null for desk/queue. |
| `agents[].pose.workLocation` | `desk/overflow/queue`. Overflow uses existing typing/thinking/waiting frames plus a renderer laptop indicator. No runtime desk is created. |
| `agents[].pose.queued` | True for a live agent awaiting capacity. It walks to reachable floor within four Manhattan tiles of the door, then waits with `reaction:"…"`. Nearby queue cells can be shared after they fill. Gone agents release seats, have null seat IDs, `workLocation:"queue"`, `queued:false` while exiting. |
| `agents[].pose.desk` | Existing `{x,y}` field remains as the work destination for all three locations. It does not imply desk furniture exists. An invalid preview can supply a reachable substitute for a blocked authored point. |
| `agents[].pose.displayName` | Optional compact label: sensible trimmed Name → Role → short Model (last path segment) → provider/worker fallback. Plan titles such as Google AI Pro/Ultra, Claude Max and ChatGPT Pro are excluded. At most 16 Unicode text elements, including an ellipsis when truncated. Full agent.name remains for tooltips. |
| `agents[].pose.providerIcon` | Optional string ID `codex/claude/antigravity`, independent of character design. Use matching icon art when available, otherwise a provider/text marker. |

`reaction` additionally permits the queue ellipsis. `reportOnDesk` is false for overflow/queue; place desktop paper only on a matching deskId. Missing v4 optional fields in older input default to null/false (`workLocation:"desk"`); v4 writers always supply current assignments.

Example SpriteFrame from a 32×64 sheet displayed at half size:

```json
{
  "characterId":"braid", "spriteRow":1, "spriteColumn":2,
  "sprite":{
    "sheetId":"people", "row":1, "column":2,
    "x":64, "y":64, "width":32, "height":64,
    "anchorX":16, "anchorY":60, "scale":0.5, "offsetX":0, "offsetY":0
  }
}
```

SpriteFrame x/y/width/height are integer source pixels within the referenced sheet, unlike continuous tile pose.x/y. Anchor is source pixels; offsets are world pixels after scaling. Scale/anchors are finite. No implicit mirroring, tint, pixel rotation or provider inference is required.

## Geometry and rendering

1. World extent = office.widthTiles×tileSize by heightTiles×tileSize; tileSize=16, origin top-left, +X right/+Y down. Bounds 8–128 tiles. Only tile type kind:floor is walkable. Walls/void are explicit; zone labels do not create walls. Rotation selects an authored variant; do not rotate its points again.
2. Character feet: footX=(pose.x+0.5)×16, footY=(pose.y+1)×16. Destination: (round(footX-anchorX×scale+offsetX), round(footY-anchorY×scale+offsetY), width×scale, height×scale). Pose stays at its collision tile while seating/sleep offsets affect only drawing.
3. Furniture destination: (instance.x×16-anchorX×scale+offsetX, instance.y×16-anchorY×scale+offsetY, width×scale, height×scale). Use furnitureSprites[id].sprite. Tile first frames stretch into 16×16; fallback tiles use supplied colors. Transparent background clears only the window background/margins/letterbox and void tiles. Nonvoid floor/wall tiles always draw on an opaque base colour, including when their PNG has translucent edges. All office pixels retain their normal opacity; the window opacity slider then uniformly fades the composed content. WPF uses per-pixel alpha, without a colour key, for transparent/desktop surfaces. Opaque tiles provide normal hit testing; transparent margins have no agent hit and native per-pixel layered windows may pass fully transparent pixels through. The renderer keeps a transparent pan surface, so WPF-only hit testing can still include margins; whole-window click-through is an explicit separate option.
4. Draw base floor/wall, relationship lines, then mixed furniture/characters sorted by layer, world Y and ordinal stable ID. Furniture layer/zOrder come from the selected variant, sortY=(y+height)×16+zOrder. Characters use layer 1 and footY; visual offsets do not change sortY. Draw an overflow laptop above furniture overlays while the owner is typing/thinking/waiting, then bubbles/names.
5. Apply pose.opacity to character, paper and bubble. Walking paper: footX−10 when facing left, otherwise footX+4; footY−12; 6×8. Desktop paper matches deskId/agentId. Shared bubbles use fallback atlas row 6 columns 0–8, centered at footX, 17 world pixels above character destination top. Queue ellipsis can be text.
6. Use nearest-neighbor. Standalone manual zoom is integer 1–6; automatic zoom may be fractional below 1 to fit a small window. Center/letterbox the fixed office. Zoom/pan and window resize change the camera, not office dimensions or simulation coordinates. Render supplied poses/frames/status rather than duplicating assignment, A*, random activity or roster hashing.
7. Name tag visibility is a renderer setting, `tagMode`, outside this state schema: `hover` (default), `icon`, or `always`; unknown values fall back to `hover`. Full plates contain `[provider-icon] displayName` above the actual head. `hover` reveals only the hovered character or away owner's hovered desk; `icon` shows a 12×12 world-pixel provider icon at those anchors and reveals the full plate on hover; `always` keeps full plates visible. Scale with zoom and stack visible plates to avoid collisions, including concurrent hover fades. A desk reservation exists only while its owner is visible and away: hide it during seat phases or for a Working/Thinking/Waiting owner within 1.5 Manhattan tiles of their work point. Unassigned desks have no reservation. Hover hit tests include bodies, desk footprints, plates and the body-to-plate gap; WPF uses 120ms fade in, 60ms leave grace, then 200ms fade out. Preserve full `agent.name` in detail tooltips after 420ms, placing them clear of visible plates. Queued agents also show a small `…` bubble. No JSON fields or schema version change is required. Windhawk must implement this renderer setting separately.

Unknown furniture without art may use a neutral rectangle; fallback furniture uses primitives. PNG furniture supplies shared frame rectangles/positions. Monitor metadata is available even when a pack lacks monitor-state art.

## Assignment, lifecycle and timing

The bundled office has 12 explicit desks with linked chairs/screens, a fixed authored floor and designated lounge/armchair/pantry laptop seats. Pantry overflow is linked to nonblocking counter stools. Membership never resizes, adds, removes or repositions furniture. Live desk/overflow reservations survive state/order changes and breaks. A returning session reuses its historical desk if free; history never evicts a live owner. Otherwise children choose free desks closest to their parent, then overflow, then queue. New parents are assigned before their children. Queued agents take released capacity before later newcomers; existing overflow owners keep their seats. History lives in the animator across reloads and resets on restart. One typing point per desk instance is used.

Legacy `autoDesks` migrates once per load into explicit desks/chairs with fixed `capacity` (default 12), then clears autoDesks in the resolved source. Legacy expand may accommodate that capacity during this migration only; membership cannot grow it. Existing source files stay untouched. `LayoutLoader.Adapt` only assigns authored desks and ignores excess slots.

Door → A* to desk/overflow → typing/thinking/waiting, or door queue → waiting. Four-neighbor Manhattan A* avoids nonfloor and variant blockedCells. Reporting → own work seat → pickup .9s → carry beside parent's work seat → handover 1.4s → leisure, including overflow parents/children. Missing/gone parent uses own seat vicinity. Queued reports wait for capacity. Paper lasts 12s, reaction 3s. Delivery survives semantic transition to idle; resumed working/thinking/waiting or gone cancels it. Gone releases capacity, walks to door, fades 1.2s and hides; tracker retains it for 60s.

Idle chooses coffee/water 10–18s, TV 20–33s, nap 28–42s, phone/chat 14–24s, avoiding its last activity. Duration starts on arrival. Reserved overflow work seats are excluded from every leisure action and fallback phone selection, including during owners' breaks. Reciprocal chat starts duration after both arrive. Busy/missing stations fall back to phone at reachable free floor near the lounge. Characters can cross/share corridors and enter at the same door tile; moving-body collision avoidance is not implemented.

Roster hash/probing is specified in asset-format.md. Character clips use action-relative elapsed time and supplied FPS; missing action/direction falls back to idle. Procedural characters preserve v2 rows/columns with action-relative phase. Always trust supplied frames/indexes.

Public editor API: `OfficeAnimator.ReloadLayout(OfficeLayout layout, bool validate = false)` preserves live characters, design IDs, positions where still walkable and desk history. Surviving seat IDs retain owners after reorder/moves; removed seats release owners for reassignment. Default preview permits temporary blocked overlaps and unreachable work points, clears paths and substitutes nearby reachable destinations. Structural errors (IDs, variants, dimensions, bounds) reject before changing the animator. Save calls `LayoutLoader.Validate(layout, assets)` or reload with validate:true for blocked-cell overlap, door and A* reachability checks. Core has no WPF dependency.

Flat WPF uses retained scene layers, shared/lazy decoded sheets, cached room/crops and deadline-based animation scheduling, with a slower transparent walking cadence. Source polling runs off the UI thread; a static scene need not redraw continuously. Iso has its own scene revisions and change deadlines. Recorded CPU is percent of one core; see the [performance results](perf-report.md#after-optimization). Demo metadata is fabricated; offscreen rendering does not validate native shell hosting.

## Version history

- v5: additive seated variants/contact anchors, chair-layer references and sit/stand phases; authored profile workstations, clipped seated legs, compact head-adjacent plates and normalized prop bodies. Asset/layout formats remain 1; fallback atlas remains 2. The current Windhawk consumer supports v5.
- v4: fixed authored capacity, stable desk/overflow/queue reservations and reloadable previews; additive seat IDs/overrides, compact names, provider icon IDs, queue ellipsis and monitor status. Existing v3 fields/types remain.
- v3: PNG sheets, anchors/scale, format 1 assets/layout, tile collision, rotated interactions, roster assignment, A*, furniture frames.
- v2: fixed 16×24 procedural atlas with direction/identity rows and paper/chat/fade fields.
- v1: 208×168 fixed atlas. Historical BFS geometry and Unix-time frame formula do not describe current writers.

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
