# viking-sysadmin — roster wave 3

Big friendly sysadmin: broad shoulders, slate-blue horned knit beanie, visible copper hairline, thick red center-braided beard, black band T-shirt with a tiny server icon, olive cargo pants and heavy boots. Large warm eyes with white highlights; original silhouette.

Load `viking-sysadmin.png` with `viking-sysadmin.manifest.json`. Canonical pipeline files: `run/sprite-sheet-alpha.png` and `run/manifest.json`. Cell **32×64**, grid **4×13**, sheet **128×832**, **42 frames**. RGBA straight alpha, nearest-neighbor sampling, transparent unused trailing cells. Bottom anchor **(16,60)**, horizontal margin 1 px and vertical margin 4 px. Source strips use magenta **#FF00FF**, removed by sprite-gen component extraction.

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

Row names, order, counts and timing follow `assets/art/worker-request.json` and the beanie-scarf layout. Typing contains only the person; furniture, keyboard and the surprised `!` bubble belong to separate layers. Idle selects a facing column, not an animation.

Generated with sprite-gen 2.20.0, explicit provider **codex**, still-image component rows, concurrency **1**. No video or facelock. Prompts, generation reports, extraction evidence, rejected takes and review composites remain in this character's own `run/`. `viking-sysadmin-contact.png` is the labeled idle + walk review at exact 3× nearest-neighbor.

QA: all generated sources and all 42 curated cells reviewed in small composites; worker geometry/row/count/timing, binary alpha, safe margins, empty cells and absence of visible chroma validated. Final 32-color export reuses `run/palette.lock.json` so bright eye highlights survive; no face painting or facelock. Sprite-gen inspection reports no errors, with retained pitch/cap warnings. GIFs under `run/qa/` are review aids; loop playback was not checked.

Weak spots: one-pixel eye/hat differences, delicate eye highlights at native size, compressed tiny server/prop details, subtle back typing and mouth motion beneath the beard. Side steps and wide handoff slightly reduce apparent size. Walking remains **experimental / continuity unverified**. See `qa-notes.md` for per-row findings and rejected takes.
