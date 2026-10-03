# hijab-designer — roster wave 2

Woman designer with a pastel mint hijab, warm brown skin, large highlighted eyes, cream pocket cardigan, long dusty-blue A-line skirt, brown flats, and a round tan tote. Idle holds a dark stylus tablet and ivory stylus. The tablet is stowed for other actions. Rounded hijab and long skirt define the original silhouette.

Load `hijab-designer.png` with `hijab-designer.manifest.json`. Canonical pipeline files are `run/sprite-sheet-alpha.png` and `run/manifest.json`. Cell **32×64**, grid **4×13**, sheet **128×832**, **42 frames**. RGBA straight alpha; unused trailing cells are transparent. Nearest-neighbor sampling. Bottom anchor **(16,60)**, four-pixel bottom margin. Raw strips use pure magenta **#FF00FF**, removed by sprite-gen component extraction.

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

Exact row naming/order/counts match `../../worker-request.json`, `../../MANIFEST.md`, and `../beanie-scarf/`. Typing contains only the seated person; furniture and the surprised `!` bubble remain separate layers. Idle is four orientation anchors, not an animation; the manifest's nominal idle durations are not playback instructions.

Made with **sprite-gen 2.20.0**, explicit provider **codex**, still-image component rows, one generation at a time. No video or facelock. The supervisor can apply facelock separately. Source prompts, reports, raw strips, component frames, and QA evidence are retained under `run/`. `wave2-contact.png` shows idle and every walk frame at exact **3× nearest-neighbor**.

Visual QA, corrections, and remaining limits are recorded in `qa-notes.md`; machine checks are in `run/qa/validation.json`.

Final fitting uses canonical **kcentroid component extraction**, binary alpha, a shared 32-color palette, and integer bottom placement. Most rows occupy 56 px of height; report uses 46 px to keep the offered paper inside the cell. This preserves the cell/anchor contract but makes the report body smaller. Some eye highlights remain weak after reduction, typing hands are subtle, and walk loop continuity is **experimental/unverified**. Final-cell GIFs are under `run/previews/`.
