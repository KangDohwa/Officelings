# cyber-courier — roster wave 4

Young courier-developer: asymmetric teal bob, exposed forehead, silver/cyan cyber ear piece, open reflective yellow raincoat over a charcoal tee, utility belt and teal/cream high-top sneakers. The angular bob and long flared coat distinguish the silhouette from the existing roster.

Load [cyber-courier.png](cyber-courier.png) with [cyber-courier.manifest.json](cyber-courier.manifest.json). Canonical pipeline files are `run/sprite-sheet-alpha.png` and `run/manifest.json`. Cell **32×64**, grid **4×13**, sheet **128×832**, **42 frames**. RGBA straight alpha; ten unused trailing cells are transparent. Use nearest-neighbor sampling. Foot anchor **(16,60)**; margins 1 px horizontally and 4 px vertically. Raw strips use pure magenta **#FF00FF**, removed by sprite-gen component extraction.

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

Row naming, order, counts, timing, loop flags and rectangles match beanie-scarf exactly; counts/FPS/loop flags also match `assets/art/worker-request.json`. Typing contains only the person. Furniture and the surprised `!` bubble are separate layers. Idle selects a column rather than playing its nominal manifest durations.

Made with the installed sprite-gen CLI, explicit provider **codex**, still-image component rows. **No video and no facelock.** Original rows and rejected rerolls, prompts, generation reports, extraction logs, curation transforms, individual curated PNGs and GIF previews are retained under `run/`. The base palette is pinned to 32 colors; selective outlines add dark edge colors, giving 60 visible RGB colors in the final sheet.

[cyber-courier-contact.png](cyber-courier-contact.png) is the labeled idle + four-direction walk review at exact **3× nearest-neighbor**. [final-review-all.png](final-review-all.png) shows all 42 cells. [validation.json](validation.json) records passing geometry, binary alpha, empty-cell, chroma, baseline and contact-sheet pixel checks. [qa-notes.md](qa-notes.md) records visual limits.

Weak spots: some native eye highlights remain only one pixel, side-view faces vary slightly, and left-walk contact phases 0/2 remain too similar despite regeneration. Phone/back-typing finger changes are subtle. Standing frames are approximately 53–54 px tall, larger than the worker's nominal 46 px target to preserve facial details; report is smaller because the paper must fit within the cell. Walk continuity is **experimental / playback unverified**. Supervisor facelock remains pending.
