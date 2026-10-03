# nurse-nightowl — roster wave 3

Tired-but-cheerful night-shift nurse: messy dark-blue high ponytail with amber tie, large highlighted brown eyes, teal scrubs, neck stethoscope, cream socks and coral/ochre slippers. Short, softly pear-shaped silhouette. A large cream-and-amber mug appears in the coffee action.

Load `nurse-nightowl.png` with `nurse-nightowl.manifest.json`. Canonical pipeline sources are in `run/`. Cell **32×64**, grid **4×13**, sheet **128×832**, **42 frames**. RGBA straight alpha; unused trailing cells are transparent. Use nearest-neighbor sampling and the manifest's `frame_layout` rectangles. Bottom anchor **(16,60)**, four-pixel bottom margin. Raw strips use solid magenta **#FF00FF**, removed by sprite-gen component extraction.

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

Names, ordering, counts and playback settings match `assets/art/worker-request.json` and `beanie-scarf`. Typing contains only the seated person; furniture is a separate layer. The surprised reaction uses the existing separate `!` bubble convention. Idle selects a direction rather than playing as a loop.

Generated with sprite-gen 2.20.0, explicit provider **codex**, still-image component rows, **concurrency 1**, no video. Back typing was regenerated to lower the hands. Lattice snapping weakened the face, so the final sheet uses canonical component extraction with nearest resampling, standard curation placement, and sprite-gen's existing `apply_palette` cleanup for **32 colors and binary alpha**. The brightest palette color is white. Standing figures are about 46–52 px tall; seated poses are shorter. No facelock, manual eye painting, mirrored walk substitutions or static fallback was used.

Prompts, generation reports, source strips, extracted frames, rejected versions and QA previews remain under `run/`. `nurse-nightowl.png` is the final publication after palette cleanup; the intermediate `run/sprite-sheet-alpha.png` precedes that cleanup. `nurse-nightowl-contact.png` is the labeled idle/walk review at exact **3× nearest-neighbor**. All 42 final frames were visually reviewed. Geometry, exact beanie-scarf layout, playback settings, unique frames, margins, binary alpha and absence of visible chroma passed `run/qa/validation.json`.

Small facial changes remain; eye highlights can be only one native pixel, and back typing is very subtle. Walk loops remain **experimental**: animation previews were exported, but their playback/seams were not inspected. See `qa-notes.md` for the per-state verdict. No app build/test or git commit was performed.
