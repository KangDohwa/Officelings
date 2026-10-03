# manager-suit — team C

Man with sculpted slicked-back brown hair, expressive highlighted eyes, fitted navy suit, white shirt, red tie and brown shoes. Upright supervisor posture and a cream report document; original identity, not a worker recolor.

Load `manager-suit.png` with `manager-suit.manifest.json`. Canonical pipeline files are `run/sprite-sheet-alpha.png` and `run/manifest.json`. RGBA straight alpha; nearest-neighbor sampling. Cell **32×64**, grid **4×13**, sheet **128×832**, **42 frames**. Row layout, names and counts match `assets/art/sheets/worker.manifest.json` exactly; animation metadata matches beanie-scarf exactly. Empty trailing cells are transparent. Bottom anchor **(16,60)**; four-pixel bottom margin. Raw strips use pure magenta **#FF00FF**, removed by sprite-gen component extraction.

| Row | State | Frames | FPS | Playback |
|---|---|---|---|---|
| 0 | `idle` | 4 | 0 | static facing |
| 1 | `walk_down` | 4 | 7 | loop |
| 2 | `walk_left` | 4 | 7 | loop |
| 3 | `walk_right` | 4 | 7 | loop |
| 4 | `walk_up` | 4 | 7 | loop |
| 5 | `typing_back` | 3 | 5 | loop |
| 6 | `typing_side` | 3 | 5 | loop |
| 7 | `report` | 3 | 5 | once |
| 8 | `sleep` | 2 | 2 | loop |
| 9 | `coffee` | 3 | 3 | loop |
| 10 | `phone` | 3 | 3 | loop |
| 11 | `talk` | 3 | 4 | loop |
| 12 | `surprised` | 2 | 5 | once |

Idle columns: down, left, right, up. The manifest assigns nominal idle timing; select its facing column instead of animating it. Typing contains the seated person only; furniture is a separate layer. Surprise is the body reaction; the `!` bubble comes from the existing icons sheet.

Made with sprite-gen **2.20.0**, explicit provider **codex**, component-row still-image pipeline; no video. Reused nine interrupted-run rows (28 frames); corrected back typing, left/right walks and report paper. `run/` retains prompts, sources, frames, pinned 32-color palette, rejected strips, extraction reports, curation and previews. Standard curation transforms normalize within-row body height and foot placement without painting or cloning poses.

QA and limitations: see `qa-notes.md` and `run/qa/validation.json`. Tiny facial and finger details simplify at native scale; back typing and phone are subtle. Walk continuity remains **experimental / unverified**: exported GIFs do not certify observed seamless playback. `C-contact.png` contains this character's idle and all walk frames at exact **3× nearest-neighbor**; it stays in this folder to respect the assigned write scope.
