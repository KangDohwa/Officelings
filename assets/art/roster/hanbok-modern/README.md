# hanbok-modern — roster wave 3

Young office designer in a soft coral modern hanbok-inspired cropped jacket with an attached tied ribbon, white shirt, taupe wide trousers and cream sneakers. Chestnut-black shoulder-length half-up hair, a small crown knot and gold hairpin; large highlighted eyes and visible forehead. Distinct hair and layered sleeve/trouser silhouette, not a recolor.

Load `hanbok-modern.png` with `hanbok-modern.manifest.json`. Canonical pipeline files are `run/sprite-sheet-alpha.png` and `run/manifest.json`. Cell **32×64**, grid **4×13**, sheet **128×832**, **42 frames**. RGBA straight alpha; unused trailing cells are transparent. Nearest-neighbor sampling. Bottom anchor **(16,60)**, four-pixel bottom margin. Raw strips use pure magenta **#FF00FF**, removed by sprite-gen component extraction.

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

Exact row naming/order/counts match `assets/art/worker-request.json`, `assets/art/MANIFEST.md` and `../beanie-scarf/`. Typing contains only the person; furniture and the surprised `!` bubble remain separate layers, as in worker. Select the idle column instead of playing idle as an animation.

Made with sprite-gen 2.20.0, explicit provider **codex**, still-image component rows, one generation at a time. No video generation and no facelock. `run/curation.json` records standard sprite-gen scale/bottom-placement transforms; source poses remain intact. Prompts, source strips, extraction evidence and animation previews are retained in `run/`. The generation wrapper confines temporary provider state to this character folder and deletes its temporary authentication copy after execution.

QA: all generated source rows and all 42 final cells viewed through **12 composite images total**, including the existing-roster/reference overview. Rejected the first reduced-face result, regenerated `walk_down`, disabled the extractor's dark-minority bias, and reserved pure white in the shared 32-color palette to preserve eye highlights. Standing silhouettes reach about **54 px**, slightly taller than worker's typical body, while retaining identical cells and foot anchors. Tiny side-eye/hairpin differences and subtle back typing remain. Walk loops are **experimental**: frame contacts were inspected, but live playback and loop seams were not certified.

`hanbok-modern-contact.png` is a labeled idle + four-direction walk contact sheet at exact **3× nearest-neighbor**. See `qa-notes.md` for the visual verdict and limitations, and `validation.json` for geometry checks.
