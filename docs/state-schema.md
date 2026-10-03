# Phase 1 → Phase 2 local state contract (version 1)

The tracker produces two files in the same output directory (default `%LOCALAPPDATA%\Vibedotcom`). Phase 2 is not implemented yet. The contract below describes the actual C# records and generated atlas, not a proposed future format.

## Delivery and reading

- `atlas.png`: generated procedurally and atomically replaced once at tracker startup. Its pixels and dimensions are fixed for atlas version 1. It is written before the first snapshot.
- `state.json`: UTF-8 JSON, no BOM, camelCase keys, lowercase string enum values, explicit `null` values. Written to a unique sibling `.tmp`, flushed, then moved over the destination. Do not read temporary files.
- Nominal snapshot interval is 200 ms; source discovery is every 3 seconds. Both are configurable. `sequence` increases from 1 within one tracker run and resets after restart. `generatedAt` distinguishes a restarted/new snapshot. Do not assume contiguous sequence numbers.
- On Windows, readers should open with read access and sharing for **read, write and delete** (C++: `FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE`); close after reading. Otherwise replacement can fail. Retry a sharing/missing-file failure on the next poll and keep the last valid snapshot. Neither file is a write-back channel.
- Validate `schemaVersion == 1` and `atlas.version == 1`, dimensions and indexes. Reject unsupported major versions; tolerate unknown additional fields. Read the JSON as data, never execute strings. A consumer should flag a snapshot older than 10 seconds as disconnected, rather than inventing fresh activity. A missing file at startup means the tracker has not produced state yet.
- Timestamps are ISO 8601 strings with a UTC offset (normally `+00:00`), backed by .NET `DateTimeOffset`. `sequence` is a positive signed 64-bit integer. All positions and counts are JSON numbers, not numeric strings. Sample clock times and IDs below are invented.

## JSON shape

```json
{
  "schemaVersion": 1,
  "generatedAt": "2026-10-03T12:00:10+00:00",
  "sequence": 1,
  "atlas": {
    "file": "atlas.png", "width": 208, "height": 168,
    "cellWidth": 16, "cellHeight": 24, "columns": 13, "rows": 7, "version": 1
  },
  "office": {
    "widthTiles": 48, "heightTiles": 28, "tileSize": 16,
    "door": { "x": 1, "y": 25 },
    "furniture": [
      { "kind": "pantry", "x": 39, "y": 6, "width": 7, "height": 2, "agentId": null },
      { "kind": "desk", "x": 3, "y": 6, "width": 3, "height": 2, "agentId": "codex:example" }
    ]
  },
  "agents": [
    {
      "agent": {
        "id": "codex:example", "provider": "codex", "parentId": null,
        "isSubagent": false, "name": "Codex", "role": null,
        "model": "example-model", "cwd": "C:/Example",
        "task": "Inspect the example file.", "tool": "exec_command", "toolKind": "shell",
        "state": "working", "lastSeen": "2026-10-03T12:00:09+00:00",
        "stateSince": "2026-10-03T12:00:08+00:00", "bestEffort": false
      },
      "pose": {
        "x": 4.0, "y": 8.0, "desk": { "x": 4, "y": 8 },
        "action": "typing", "facing": "right", "spriteRow": 0, "spriteColumn": 3, "visible": true
      }
    }
  ],
  "warnings": []
}
```

The example furniture array is shortened. Consumers must render the complete supplied list, not infer that those are its only entries.

## Root and office fields

| Field | Type | Meaning |
| --- | --- | --- |
| `schemaVersion` | integer | Exactly `1`. |
| `generatedAt` | timestamp string | Snapshot generation time, independent of log event times. |
| `sequence` | int64 | Monotonically increasing within a tracker process. |
| `atlas` | object | All eight fields shown above are required. `file` is the relative sibling filename `atlas.png`, not an arbitrary URL. |
| `office` | object | Shared simulation geometry. |
| `agents` | array | Each item has exactly the required `agent` and `pose` objects below. Empty is valid. IDs are unique in a snapshot; array order is not a permanent desk assignment. |
| `warnings` | string array | Nonfatal read/parse warnings. May contain local paths; display as text. Empty is valid. |
| `office.widthTiles`, `heightTiles` | positive integers | Current world bounds; width is 48, height starts at 28 and expands with desk rows. Pixel extent = tile extent × `tileSize`. |
| `office.tileSize` | integer | Exactly 16 in version 1. This is world geometry, not display zoom. |
| `office.door` | `{x:int,y:int}` | Walkable entrance/exit tile. |
| `office.furniture` | object array | Each entry has required `kind:string`, `x:int`, `y:int`, `width:int`, `height:int`, `agentId:string|null`. Rectangles use tile units, top-left inclusive and bottom-right exclusive. |

Furniture kinds are `desk`, `pantry`, `table`, `tv`, `sofa`, `plant`. The complete occupied rectangle blocks walking. `agentId` identifies the owner of a desk, otherwise is `null`; it may refer to a gone agent during its retention period. Desk approach tiles are supplied in `pose.desk` and lie outside the furniture rectangle. The walkable bounds are `1 ≤ x < widthTiles−1`, `3 ≤ y < heightTiles−1`, minus furniture. The top three tile rows are the wall. An unknown additional furniture kind can be drawn as a neutral rectangle.

## Agent fields (all required, with nullable values where shown)

| Field | Type | Meaning |
| --- | --- | --- |
| `id` | nonempty string | Opaque stable provider-prefixed identity. Codex: `codex:<session-id>`. Claude parent: `claude:<session-id>`; worker: `claude:<parent-session-id>/agent-<agent-id>`. Antigravity: `antigravity:<conversation-id>`. Do not split IDs to infer parents; use `parentId`. |
| `provider` | string enum | `codex`, `claude`, `antigravity`. |
| `parentId` | string or null | Full provider-prefixed parent ID. Can point to an absent/stale parent. `null` means unknown/no parent; it does not by itself imply supervisor. |
| `isSubagent` | boolean | True for detected worker/sidechain/subagent sources. Distinguishes clothing even if the parent is unavailable. |
| `name` | string | Nickname when available, otherwise provider/worker label. |
| `role`, `model`, `cwd` | string or null | Observed metadata. `null` is unknown. Do not infer a model from provider color. |
| `task` | string | Last observed user prompt, whitespace collapsed and truncated to 240 UTF-16 code units. Empty means unavailable. Tool results never overwrite it. |
| `tool` | string or null | Current tool name. Codex's `exec` wrapper uses its first detectable nested `tools.<name>(...)` call where possible; otherwise `exec`. No raw command arguments are exported. |
| `toolKind` | string enum or null | `shell`, `file`, `search`, `agent`, `other` for a tool bubble. Name-based categorization. Null when no current tool is observed. |
| `state` | string enum | `working`, `thinking`, `waiting`, `reporting`, `idle`, `gone`. Semantic activity, independent of walking. |
| `lastSeen` | timestamp string | Most recent observed source write (DB/nonempty WAL or transcript for Antigravity). Indicates file activity, not confirmed process existence. |
| `stateSince` | timestamp string | Start of the current displayed semantic state; age = max(0, `generatedAt − stateSince`). |
| `bestEffort` | boolean | Always true for Antigravity; false for the JSONL providers. False does not promise perfect live process/approval detection. |

State rules: explicit input requests or parent turn completion → `waiting`; worker completion → `reporting` for 12 seconds → `idle`; `working`/`thinking` without writes for the configured working window → `idle`; no writes for the gone timeout, or a recognized session-end event → `gone`. Waiting is not timed into idle. Gone agents are retained for 60 seconds, then removed. Old agents are excluded at cold startup. Reporting/gone retention and timeouts are tracker policy; consumers should use supplied states rather than recalculate them.

## Pose fields and rendering

| Field | Type | Meaning |
| --- | --- | --- |
| `x`, `y` | finite numbers | Continuous tile coordinates of the character's occupied walking cell. X increases right, Y down. There is no camera offset. |
| `desk` | `{x:int,y:int}` | Assigned desk approach/standing tile. Does not change with break activity. |
| `action` | string enum | `entering`, `walking`, `typing`, `thinking`, `waiting`, `reporting`, `coffee`, `tv`, `phone`, `napping`, `chatting`, `exiting`, `gone`. |
| `facing` | string enum | `right` or `left`. Mirror the sprite horizontally for left; geometry/position are unchanged. |
| `spriteRow` | integer | 0–5, chosen by provider and `isSubagent`; see atlas rows. |
| `spriteColumn` | integer | 0–12, authoritative animation frame. |
| `visible` | boolean | False after reaching the exit. The agent may still exist in `agents` with state gone. Do not draw it or its parent line when false. |

To render at world pixel coordinates, use `footX=(x+0.5)*16`, `footY=(y+1)*16`. A sprite's unscaled destination rectangle is `(round(footX−8), round(footY−24), 16, 24)`. Draw furniture first, then dotted parent lines between visible character centers `((x+0.5)*16,(y+0.5)*16)`, then visible characters in ascending Y order. Suppress lines to missing parents. Draw a working tool bubble using atlas row 6 at character rectangle `(spriteX, spriteY−18,16,24)`. Bubble columns are selected by `toolKind`, not `spriteColumn`. Zoom scales world geometry uniformly, always with nearest-neighbor sampling.

The core owns BFS routing and animation. Phase 2 can render/interpolate consecutive supplied positions; it need not re-create routes, idle randomness or semantic event parsing. Respect `pose` even when `state` differs: a working agent can be entering, a reporting agent can be walking, and a gone agent can be exiting. A pending report walk can outlast the semantic reporting window; an idle agent can still be delivering its paper. Reaching the report target briefly shows the paper, followed by a waiting pose before idle. Missing parents use the worker's own desk. Leisure activities rotate about every 25–45 seconds. Animation frames nominally toggle every 180 ms; snapshot timing is independent.

## atlas.png, version 1

PNG signature and encoding: width **208**, height **168**, 8 bits/channel, RGBA (PNG color type 6), non-interlaced, straight alpha. Transparent background. Cells are **16×24** pixels, **13 columns × 7 rows**, no margins or gutters. Source rectangle = `(column*16,row*24,16,24)`. Pixel `(0,0)` is the top-left. Colors and alpha are baked in; no external palette or color key is needed. GDI/GDI+ consumers should decode the PNG and use proper alpha compositing (premultiply if required by their destination API).

| Row | Meaning |
| --- | --- |
| 0 | Codex supervisor, teal shirt and tie |
| 1 | Codex worker, teal shirt |
| 2 | Claude supervisor, terracotta shirt and tie |
| 3 | Claude worker, terracotta shirt |
| 4 | Antigravity supervisor, blue-violet shirt and tie |
| 5 | Antigravity worker, blue-violet shirt (reserved mapping; current detection has no workers) |
| 6 | Tool bubbles: column 0 shell, 1 file, 2 search, 3 agent, 4 other. Columns 5–12 are transparent. |

Character columns (rows 0–5):

| Column | Frame |
| --- | --- |
| 0 | Standing |
| 1, 2 | Walking A, B (entering/walking/exiting) |
| 3, 4 | Typing A, B |
| 5 | Thinking |
| 6 | Waiting |
| 7 | Reporting with paper |
| 8 | Coffee |
| 9 | TV |
| 10 | Phone |
| 11 | Napping |
| 12 | Chatting |

The atlas contains only characters and tool bubbles. Office furniture, walls, floor, signs and provider labels are geometric drawing primitives defined by `office` and the rendering rules; they are not atlas tiles. The standalone viewer's furniture decoration/colors are presentation details, so a Phase 2 consumer may style these while preserving geometry, provider clothing, positions and state animations.
