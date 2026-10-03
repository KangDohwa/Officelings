# punk-grandpa — roster wave 4

Stylish older office worker with a tall swept silver-gray crest, clipped sides and visible forehead, round lightly amber-tinted copper glasses, large teal eyes with white catchlights, a tidy short white beard, broad espresso leather vest over rust-and-cream flannel, turquoise bolo tie, slate jeans and sturdy brown boots. His crest, broad shoulders, checked sleeves and short angular beard distinguish him from the professor and other roster silhouettes.

Load `punk-grandpa.png` with `punk-grandpa.manifest.json`. Canonical pipeline files are `run/sprite-sheet-alpha.png` and `run/manifest.json`. Cell **32×64**, grid **4×13**, sheet **128×832**, **42 frames**. RGBA straight alpha; unused trailing cells are transparent. Use nearest-neighbor sampling. Foot anchor **(16,60)**, four-pixel bottom margin. Source strips use pure magenta **#FF00FF**, removed by sprite-gen component extraction.

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

Row names, order, counts, FPS and loop flags follow `../../worker-request.json` and `../../MANIFEST.md`, as in beanie-scarf. Idle columns select facing; do not animate between them. Typing and sleep contain only the person. Furniture and the surprised `!` bubble are separate game layers.

Made using sprite-gen 2.20.0, explicit provider **codex**, still-image component rows, one generation at a time. No video or facelock. `request.json` owns the recipe; `base-source.png` is the accepted identity. Source prompts, reports, rejected rows and compact review composites are retained in `run/`. Left walking, back typing and report were regenerated after visual review. Back typing additionally references the accepted side-typing strip for seated posture and low arm height.

Final processing uses sprite-gen's canonical **component extraction + NEAREST fit**, then its own `binarize_alpha` cleanup function at threshold 128. Source RGB detail is retained, especially the white eye highlights. The first grid/palette extraction erased those highlights and is preserved under `run/qa/pixel-extract-rejected/`. `run/curation.json` selects the canonical fitted PNGs and applies only integer pixel translations; no fractional scaling or smooth resampling is used in the final bake. The manifest's `pixel` variant names that selected PNG stream; the recipe explicitly records `fit.pixel_unfake: false` and `fit.resample: nearest`. Figure heights depend on pose and reach up to 56 pixels; the cell and margins remain the worker contract.

`punk-grandpa-contact.png` contains idle and all walking frames at exact **3× NEAREST** with labels. It is inside this character's directory to respect the requested write boundary. Other final action review pages and animation GIFs are under `run/qa/` and `run/previews/`. See `qa-notes.md` for actual checks and remaining limitations.
