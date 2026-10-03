# red-braid — team B

Woman with copper-red sweeping bangs and one long side braid, cinnamon freckles, large green eyes with ivory highlights, dark denim overalls over a cream/rust striped shirt, a cream braid ribbon and brown work shoes. The braid remains on her anatomical RIGHT: screen-left in front views and screen-right in back views.

Load `red-braid.png` with `red-braid.manifest.json`. **32×64 cells**, **4×13 grid**, **128×832 sheet**, **42 frames**. RGBA straight alpha with binary 0/255 coverage. Empty trailing cells stay transparent; use nearest-neighbor. Foot baseline **y=60**, margins 1 px horizontally / 4 px vertically. Raw rows use pure magenta **#FF00FF**. State names, row order and frame counts match worker exactly.

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


Idle columns select down/left/right/up as static facings. Typing contains only the seated character. Furniture and the surprised `!` bubble use separate game layers, as in worker. The manifest's nominal idle timing is not a facing animation.

Sprite-gen 2.20.0, explicit **codex** provider, still-image component rows; no video. Reused nine interrupted-run rows, generated missing idle/surprised and replaced back typing/report. Back typing uses the accepted UP idle frame as the directional identity reference plus the side-typing strip for low arm posture. The braid handedness and ribbon were retained. Original shoulder-height hands and wide report are preserved in `run/qa/rejected/`.

Final extraction uses sprite-gen's canonical **kCentroid component** path with hard chroma edges (`--fringe-unmix-reach 0 --decontam off`) and integer placement. Report poses use a common height and shared foot baseline through sprite-gen `apply_transform(..., snap_scale=1)`; the final red-braid report required no further scale reduction. Exact transforms are recorded in `red-braid.report.json`. No pose painting. Sprite-gen's shared palette functions limit the final PNG to **32 opaque colors** without changing alpha. `run/sprite-sheet-alpha.png` and its GIFs are pre-palette pipeline intermediates; load the PNG beside this README.

QA: every generated source image and all final cells viewed, including a 12× report/eye detail review. `run/qa/validation.json` checks row order, frame rectangles/counts, binary alpha, margins, empty cells, visible chroma and palette/alpha preservation. Review pages use the delivered PNG. Weak spots: freckles compress to small color accents at this cell size, back-typing fingers are largely hidden, the tiny report can blend into the hands, and some hair/face pixels vary slightly. Walk continuity remains **experimental/unverified** because no connected browser was available for playback. `../B-contact.png` shows idle and walks at exact 3× nearest-neighbor.