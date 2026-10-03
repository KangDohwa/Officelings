# chef-sous — roster wave 4

**Current cleanup (2026-10-04): PASS** for facelock/blink/seated pixel checks and cyclic 3?0 frame review. Flat delivery remains `chef-sous.png` + `chef-sous.manifest.json` (32?64 cells, 42 frames). New seated delivery is `seated/chef-sous.manifest.json` (12 added normal + eight side-blink frames). Latest evidence: `cleanup/validation.json`, `cleanup/facelock-qa/`, `cleanup/final-cells.png`, `cleanup/seated-review.png`, `cleanup/walk-seams.png` and four walk GIFs. Real-time playback is unverified because no browser was available. See the current section of `qa-notes.md` for changes, reproduction and remaining weak details. Original source notes below are historical; their deferred/no-facelock statements predate this cleanup.


Cheerful sous-chef developer: tall crooked paper toque, chestnut forelock and temple tufts, large highlighted brown eyes, thin pencil mustache, white double-breasted jacket with rolled sleeves, striped shoulder tea towel, checkered trousers and walnut clogs. The tall paper silhouette and shoulder towel distinguish him from the existing roster.

Load `chef-sous.png` with `chef-sous.manifest.json`. Canonical pipeline files are `run/sprite-sheet-alpha.png` and `run/manifest.json`. Cell **32×64**, grid **4×13**, sheet **128×832**, **42 frames**. RGBA straight alpha; unused trailing cells are transparent. Nearest-neighbor sampling. Bottom anchor **(16,60)**, four-pixel bottom margin. Raw strips use pure magenta **#FF00FF**, removed by sprite-gen component extraction.

| Row | State | Frames | FPS / playback |
|---|---|---|---|
| 0 | idle | 4: down, left, right, up | static facing selection |
| 1 | walk_down | 4 | 7, loop |
| 2 | walk_left | 4 | 7, loop |
| 3 | walk_right | 4 | 7, loop |
| 4 | walk_up | 4 | 7, loop |
| 5 | typing_back | 3 | 5, loop |
| 6 | typing_side | 3 | 5, loop |
| 7 | report | 3 | 5, once / hold final |
| 8 | sleep | 2 | 2, loop |
| 9 | coffee | 3 | 3, loop |
| 10 | phone | 3 | 3, loop |
| 11 | talk | 3 | 4, loop |
| 12 | surprised | 2 | 5, once / hold final |

Exact row names/order/counts match worker and beanie-scarf. Typing contains only the seated person. Furniture and the surprised **!** bubble are separate layers, following worker conventions. Idle columns select direction; idle is not a four-frame animation.

Made with sprite-gen 2.20.0, explicit provider **codex**, still-image component rows, one generation at a time. No video generation or facelock. Prompts, generation reports and source strips remain in `run/`; rejected rows, when present, remain in `run/qa/rejected/`. See `qa-notes.md` for the final review and limitations. `chef-sous-contact.png` labels idle and all walk frames at exact **3× nearest-neighbor**. All output stays inside this character folder.

Five rows were regenerated: walk_left, typing_back, sleep, surprised and report. Canonical component extraction was retained; standard curation scale/translation places every final cell on y=60 with top/side clearance. A final shared palette pass, matching beanie-scarf's export approach, reduces the composed 57 opaque colors to **21** without changing alpha. The root PNG/manifest are the delivered runtime pair; the run sheet is pre-export palette evidence.

QA: base, every generated row including rejects, and all 42 final cells viewed in **12 composite review images**. Row geometry/counts/order, binary alpha, empty trailing cells, no magenta remnants and margins checked; sprite-gen inspect reports no errors. Weak spots: native-size eye highlights and the thin mustache are uneven/small after extraction, especially in some angled faces; the strict face-quality bar is only partly met. Left-walk contact alternation and back typing are subtle, and seated rows have a different apparent head/body scale. Walk loops remain **experimental / playback unverified**. No facelock was run; supervisor review is still needed for facial consistency.
