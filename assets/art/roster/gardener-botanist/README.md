# gardener-botanist ? roster wave 4

**Current cleanup (2026-10-04): PASS** for facelock/blink/seated pixel checks and cyclic 3?0 frame review. Flat delivery remains `gardener-botanist.png` + `gardener-botanist.manifest.json` (32?64 cells, 42 frames). New seated delivery is `seated/gardener-botanist.manifest.json` (12 added normal + eight side-blink frames). Latest evidence: `cleanup/validation.json`, `cleanup/facelock-qa/`, `cleanup/final-cells.png`, `cleanup/seated-review.png`, `cleanup/walk-seams.png` and four walk GIFs. Real-time playback is unverified because no browser was available. See the current section of `qa-notes.md` for changes, reproduction and remaining weak details. Original source notes below are historical; their deferred/no-facelock statements predate this cleanup.


Calm botanist with a broad flower-trimmed straw hat, a single long umber braid, moss-green tool-pocket apron, rolled work trousers and dark rubber boots. Idle holds a tiny terracotta potted plant. An original silhouette, not a worker recolor.

Load `gardener-botanist.png` with `gardener-botanist.manifest.json`. Retained canonical pipeline outputs (pre-export palette): `run/sprite-sheet-alpha.png` and `run/manifest.json`. **32?64 cells; 4?13 grid; 128?832 sheet; 42 frames.** RGBA straight alpha; unused cells transparent. Nearest-neighbor sampling. Foot anchor **(16,60)** with four-pixel bottom margin. Source strips use pure magenta **#FF00FF**, removed by sprite-gen component extraction.

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

Row names, order, frame counts and timing match `../../worker-request.json` and `../../MANIFEST.md`, using the `beanie-scarf` layout. Typing contains only the seated person; desk, keyboard, monitor and chair are separate layers. The surprised `!` is a separate overlay, matching worker conventions. Idle columns are orientation anchors, not a played animation.

Made with sprite-gen 2.20.0, explicit provider **codex**, still-image component-row pipeline, concurrency **1**. No video and no facelock. Prompts, source strips, extraction reports and review evidence are retained in `run/`.

Visual QA and limitations: see `qa-notes.md`. The labeled idle/walk review is `gardener-botanist-contact.png`, with frames at exact **3? nearest-neighbor**.

Final export uses the sprite-gen shared-palette helpers without changing alpha. Geometry/row counts/empty cells/margins/chroma were checked. **Weak spots:** final eye catchlights are not consistently strong enough to meet the requested face bar; supervisor facelock is deferred. Walk contacts remain subtle and loop continuity is experimental/unverified; tiny hat/face changes and small report scale remain. See `qa-notes.md` for the exact limitations.
