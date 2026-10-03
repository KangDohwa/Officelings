# granny-knit — roster wave 2

**Current cleanup (2026-10-04): PASS** for facelock/blink/seated pixel checks and cyclic 3?0 frame review. Flat delivery remains `granny-knit.png` + `granny-knit.manifest.json` (32?64 cells, 42 frames). New seated delivery is `seated/granny-knit.manifest.json` (12 added normal + eight side-blink frames). Latest evidence: `cleanup/validation.json`, `cleanup/facelock-qa/`, `cleanup/final-cells.png`, `cleanup/seated-review.png`, `cleanup/walk-seams.png` and four walk GIFs. Real-time playback is unverified because no browser was available. See the current section of `qa-notes.md` for changes, reproduction and remaining weak details. Original source notes below are historical; their deferred/no-facelock statements predate this cleanup.


Kind elderly woman: silver-gray bun pierced by a golden pencil, thin round glasses on a gold chain, dusty lilac knit shawl, cream floral dress, and comfortable brown shoes. Large highlighted eyes and a round head above a broad shawl/flared-skirt silhouette distinguish her from the original roster. This is an original character, not a worker recolor.

Load `granny-knit.png` with `granny-knit.manifest.json`. Canonical pipeline files are `run/sprite-sheet-alpha.png` and `run/manifest.json`. Cell **32×64**, grid **4×13**, sheet **128×832**, **42 frames**. RGBA straight alpha; unused trailing cells are transparent. Use nearest-neighbor sampling. Bottom anchor **(16,60)**, with the same four-pixel bottom margin as beanie-scarf. Raw strips use **magenta #FF00FF**, removed by canonical sprite-gen component extraction.

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

Exact state naming, order, counts, and playback settings match `assets/art/worker-request.json` and `assets/art/roster/beanie-scarf`. Idle columns select facing; do not play them as an animation. Typing rows contain only the seated person. Furniture, sleep symbols, and the surprised **!** bubble belong to separate game layers, following the worker conventions.

Made with **sprite-gen 2.20.0**, explicit provider **codex**, still-image component rows, one generation at a time. No video and no facelock. Source images, prompts, reports, extraction evidence, rejected rows, and previews are retained in `run/`; final review findings and limitations are in `qa-notes.md`.

`wave2-contact.png` labels idle plus every walk frame at exact **3× nearest-neighbor**. It is inside this character folder to respect the task's final write restriction. No shared roster contact sheet was changed.

Final processing uses canonical component extraction with **kcentroid**, then sprite-gen's nearest-neighbor transforms, binary alpha, and one shared **32-color** palette. The initial pixel-unfake result was rejected because it damaged facial detail. Final footprints are 55–56 pixels high for most states; report is uniformly 46 pixels high to fit the extended paper without changing size between its frames. All frames retain the specified cell margins and y=60 baseline.

Validation passed for all 42 declared frames, exact row order/counts/fps, binary alpha, clean transparent RGB, safe margins, and empty trailing cells. **Weak spots:** eye highlights, glasses rims and chain are still less clear than the generated sources; flower/hair pixels vary slightly, and back typing/phone gestures are very subtle. The strict highlighted-eye quality bar is not certified in every final cell. Walking continuity remains **experimental/unverified** because no connected browser was available. No facelock was run.
