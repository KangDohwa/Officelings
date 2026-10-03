# cap-teen — team B

Teen with a backwards red cap, messy brown hair, large highlighted brown eyes, navy headphones, oversized mustard tee, olive cargo shorts and red/cream sneakers. The cap strap is on the front; the accepted back views show the red crown without a second strap.

Load `cap-teen.png` with `cap-teen.manifest.json`. **32×64 cells**, **4×13 grid**, **128×832 sheet**, **42 frames**. RGBA straight alpha with binary 0/255 coverage. Empty trailing cells stay transparent; use nearest-neighbor. Foot baseline **y=60**, margins 1 px horizontally / 4 px vertically. Raw rows use pure magenta **#FF00FF**. State names, row order and frame counts match worker exactly.

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

Sprite-gen 2.20.0, explicit **codex** provider, still-image component rows; no video. Reused ten interrupted-run rows, generated missing idle, replaced back typing and report. The first low-arm back correction introduced a rear cap strap; the accepted UP idle frame was then supplied as the directional identity reference. Rejected strips are preserved in `run/qa/rejected/`.

Final extraction uses sprite-gen's canonical **kCentroid component** path with hard chroma edges (`--fringe-unmix-reach 0 --decontam off`) and integer placement. The regenerated report keeps a shorter arm; its three poses are additionally aligned to a common height with sprite-gen `apply_transform(..., snap_scale=1)` using nearest-neighbor and a shared foot baseline. Exact transforms are recorded in `cap-teen.report.json`. No pose painting. Sprite-gen's shared palette functions limit the final PNG to **32 opaque colors** without changing alpha. `run/sprite-sheet-alpha.png` and its GIFs are pipeline intermediates before final report alignment / palette; load the PNG beside this README.

QA: every generated source image and all final cells viewed. `run/qa/validation.json` checks row order, frame rectangles/counts, binary alpha, margins, empty cells, visible chroma and palette/alpha preservation. Review pages use the delivered PNG. Weak spots: small head/cap pixel shifts, subtle typing/phone finger movement, and the compact report row is smaller than normal standing rows. Walk continuity remains **experimental/unverified** because no connected browser was available for playback. `../B-contact.png` shows idle and walks at exact 3× nearest-neighbor.