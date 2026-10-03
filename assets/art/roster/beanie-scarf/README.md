# beanie-scarf — team B

Chubby cheerful man: mustard knit beanie, brown hair tufts, thick orange scarf, padded chocolate vest, cream sleeves, navy trousers and tan boots. Original silhouette; not a worker recolor.

Load `beanie-scarf.png` with `beanie-scarf.manifest.json`. Canonical pipeline files are `run/sprite-sheet-alpha.png` and `run/manifest.json`. Cell **32×64**, grid **4×13**, sheet **128×832**, **42 frames**. RGBA straight alpha; unused trailing cells are transparent. Nearest-neighbor sampling. Bottom anchor (16,60), four-pixel bottom margin. Raw strips use pure magenta **#FF00FF**, removed by sprite-gen component extraction.

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

Exact row naming/order/counts match `assets/art/worker-request.json` and `assets/art/MANIFEST.md`. Typing contains only the person; furniture and the surprised `!` bubble remain separate layers, as in worker. The sprite-gen manifest assigns a nominal idle timing; select its column instead of playing it as an animation.

Made with sprite-gen 2.20.0, explicit provider **codex**, still-image component rows; no video. Reused ten interrupted-run rows and filled talk/surprised. Replaced back typing after two rejected shoulder-height-hand versions; extra side-typing reference is recorded in `qa-notes.md`. Canonical extraction plus documented curation scale/bottom placement corrects extraction size variation without altering source poses. Generation prompts, reports, rejected strips, final frame review pages and GIFs are retained in `run/`.

QA: all source rows and all 42 final cells viewed. Geometry, row counts/order, binary alpha, empty cells and absence of visible chroma checked. Weak spots: tiny facial/hat changes remain, back typing is very subtle, and wide side steps reduce the apparent side-view scale. Walk loops are **experimental**: no connected browser was available to watch playback, so seamless motion is not certified. GIFs under `run/qa/` are review evidence. `../B-contact.png` shows idle and all walk frames at exact 3× nearest-neighbor.

