# mohawk-skater — roster wave 2

Young slim skater with a tall electric-blue mohawk and shaved sides, large highlighted blue eyes, a patched black vest over a cream lightning band T-shirt, ripped slate-blue jeans, chunky ivory sneakers and a small silver earring. Original silhouette; not a recolor.

Load `mohawk-skater.png` with `mohawk-skater.manifest.json`. Canonical pipeline files are `run/sprite-sheet-alpha.png` and `run/manifest.json`. Cell **32×64**, grid **4×13**, sheet **128×832**, **42 frames**. RGBA straight alpha; unused trailing cells are transparent. Nearest-neighbor sampling. Bottom anchor **(16,60)**, four-pixel bottom margin. Raw strips use pure magenta **#FF00FF**, removed by sprite-gen component extraction.

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

Exact cell, row naming/order/counts and timing follow `../beanie-scarf/` and `../../worker-request.json`. Typing contains only the person; furniture and the surprised **!** bubble remain separate layers, as in worker. Select the idle column rather than playing idle as an animation.

Made with sprite-gen 2.20.0, explicit provider **codex**, still-image component rows with concurrency **1**; no video and no facelock. `base.png` is the uncropped identity reference. Requests, prompts, layout guides, raw strips and reports remain in this character's own `run/`. The external pixel-agents reference was used only for pixel craft at base generation.

`wave2-contact.png` is a labeled idle + four-way walking contact sheet at exact **3× nearest-neighbor**. It is stored here to respect this worker's exclusive directory boundary. See `qa-notes.md` for visual review, corrections and remaining limitations.

Final extraction uses sprite-gen's documented **kcentroid component fit** after a pixel-pitch/shrink attempt lost eye highlights. No facelock or pose painting was used. The tall crest uses up to **56 px** silhouette height inside the exact same cell. Runtime validation passed: 42 nonempty cells, identical beanie layout/timing, safe margins, binary alpha, transparent unused cells, no visible chroma, 32 colors and no repeated identical frame pairs. All final cells were viewed in `final-review.png`. Small face/accessory changes and subtle typing/phone motion remain; walking loops are **experimental**, since no connected browser was available for playback inspection.
