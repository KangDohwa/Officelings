# labcoat-tall — team B

Tall slim woman with long straight black hair and bangs, amber eyes with ivory highlights, white lab coat over a teal dress, and brown flats. Her long narrow silhouette differs from the other team B characters.

Load `labcoat-tall.png` with `labcoat-tall.manifest.json`. **32×64 cells**, **4×13 grid**, **128×832 sheet**, **42 frames**. RGBA straight alpha with binary 0/255 coverage. Empty trailing cells stay transparent; use nearest-neighbor. Foot baseline **y=60** with four-pixel bottom padding. Raw rows use pure magenta **#FF00FF**. Names, row order and counts match worker exactly.

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


Idle columns select down/left/right/up; they are static anchors. Typing contains only the seated person. Furniture and the surprised `!` bubble use separate game layers, exactly as worker. The pipeline manifest supplies a nominal idle timing; do not animate between facings.

Sprite-gen 2.20.0, explicit **codex** provider, still-image component rows; no video. Reused nine interrupted-run rows, generated missing talk/surprised, regenerated back typing and report. The wide original report shrank its last pose; the replacement uses a bent elbow and compact paper offer with stable body size. Back typing attaches the accepted side-typing strip as a low-arm-height motion reference.

The native-grid extraction candidate lost eye highlights and is retained under `run/qa/rejected/`. Final extraction uses sprite-gen's canonical **kCentroid component** path, hard chroma edges (`--fringe-unmix-reach 0 --decontam off`), and only integer placement shifts. No secondary body shrink or pose painting. Final PNG uses sprite-gen's `build_shared_palette/apply_palette` to keep **32 opaque colors** across all rows, with unchanged alpha. `run/sprite-sheet-alpha.png` is the pre-palette pipeline intermediate; load the PNG beside this README.

QA: every source image and all final cells viewed; frame-layout coordinates, counts/order, margins, binary alpha, empty cells, chroma residue and palette/alpha preservation checked in `run/qa/validation.json`. Review pages and GIFs live in `run/qa/`. Weak spots: long hair partly hides typing arms, finger motion is subtle, paper handoff is compact, and some head/hair pixels vary slightly. Walk continuity is **experimental/unverified** because no connected browser was available for playback. `../B-contact.png` shows idle and walks at exact 3× nearest-neighbor.
