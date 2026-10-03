# astro-hoodie — roster wave 3

Cozy developer with short angular silver-blue hair, warm peach face, large highlighted navy eyes, a cropped ivory spacesuit-style jacket with mission patches, and a round helmet pushed behind the head onto the shoulders as an open hood. Navy joggers and cream/teal sneakers with orange details. The angular forelock and broad rounded shoulder hood distinguish this character from the existing roster.

Load `astro-hoodie.png` with `astro-hoodie.manifest.json`. Canonical pipeline files: `run/sprite-sheet-alpha.png` and `run/manifest.json`. Cell **32×64**, grid **4×13**, sheet **128×832**, **42 frames**. RGBA straight alpha; unused trailing cells are transparent. Use nearest-neighbor sampling. Bottom anchor **(16,60)** with four-pixel bottom margin. Raw strips use flat magenta **#FF00FF**, removed by sprite-gen component extraction.

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

Row naming, ordering, timing and counts match `../../worker-request.json`, `../../MANIFEST.md`, and `../beanie-scarf/`. Idle columns select orientation and are not a playback cycle. Typing/sleep contain only the person; furniture is a separate layer. The surprised **!** is a separate runtime bubble, as in the worker convention.

Made with sprite-gen **2.20.0**, explicit provider **codex**, still-image component rows. Generation concurrency is **1**. No video and no facelock. Base, prompts, generation reports, raw rows and canonical extracted frames remain inside this character directory. `qa-notes.md` records visual review, corrections and remaining limits. `astro-hoodie-contact.png` is the labeled idle/walk contact sheet at exact **3× nearest-neighbor**.

QA: every source generation and all 42 final cells reviewed using 12 compact images/composites. The first finer design was rejected because extraction weakened the eye highlights; the base and rows were rebuilt with larger pixel blocks and highlights. Back typing and down/up contact poses were regenerated again. Geometry, exact worker rows/timing, binary alpha, transparent trailing cells, cell margins and absent visible chroma passed; sprite-gen inspect passed. Curated GIFs live in `run/previews/` and are review aids; runtime uses the manifest and static idle direction selection.

Weak spots: report is smaller because the extended paper must fit; some side/seated eye highlights are tiny; typing/phone finger movement is subtle; coffee faces mostly down. Small hair/patch/face pixel changes remain. Walk loops are **experimental**: actual playback and seamless continuity were not verified. See `qa-notes.md` for per-row verdicts and extraction warnings. The pixel-agents quality comparison is subjective, not an objective benchmark score.
