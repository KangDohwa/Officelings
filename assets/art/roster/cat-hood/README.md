# cat-hood — team C

Girl in a lavender cat-ear hoodie, plum twin tails, dark shorts, cream knee socks and plum shoes. Large chibi head, highlighted eyes and a hood-and-twin-tail silhouette distinguish her from the other workers.

Load `cat-hood.png` with `cat-hood.manifest.json`. Canonical pipeline files are `run/sprite-sheet-alpha.png` and `run/manifest.json`. Cell **32×64**, grid **4×13**, sheet **128×832**, **42 frames**. RGBA straight alpha; unused trailing cells are transparent. Nearest-neighbor sampling. Bottom anchor **(16,60)**, four-pixel bottom margin. Raw strips use pure magenta **#FF00FF**, removed by sprite-gen component extraction.

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

Exact row naming, order and counts match `assets/art/worker-request.json`, `assets/art/MANIFEST.md` and beanie-scarf. Typing contains only the seated person; furniture and the surprised `!` bubble are separate layers. Select the idle column instead of animating the four orientations.

Made with sprite-gen 2.20.0, explicit provider **codex**, still-image component rows; no video. Reused six interrupted-run rows (**20 frames**): idle, walk_down, walk_left, walk_right, sleep and surprised. Regenerated seven rows (**22 frames**) to correct rear-hood identity, typing posture/facing, action facing and report readability. Talk required one further retry using the accepted coffee strip as facing evidence. Canonical extraction and sprite-gen curation scale/bottom placement preserve the cell geometry. Original strips, rejected candidates, prompts, provider reports, final review pages and GIF previews remain in `run/`.

QA: all source rows, eight new generation results and all 42 final cells viewed in bounded composites. Geometry, row order/counts, margins, binary alpha, transparent trailing cells and absence of visible chroma checked. See `qa-notes.md` and `run/qa/validation.json`. Small face/eye and hair-detail variations remain at native resolution; back typing and phone movement are subtle. Walk loops are **experimental**: contact poses were reviewed, but real-time playback/seam continuity was not observed. GIFs are under `run/previews/`.

`C-contact.png` contains this character's idle and all walk frames at exact **3× nearest-neighbor**, with row and frame labels. It is kept inside this character's folder to respect the assigned write boundary. `tools/facelock.py` was not run.
