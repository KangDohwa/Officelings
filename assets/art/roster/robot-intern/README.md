# robot-intern — roster wave 2

Friendly squat robot intern: oversized rounded orange box head, cream-tipped antenna, one dark horizontal visor with expressive highlighted cyan eye lights, orange body panels, cream shoulder caps/grippers, short charcoal joints, orange block feet and a tiny teal scarf. Original mechanical silhouette; not a human roster recolor. Profile views show one visible highlighted eye; back views hide the visor. Sleep closes the visor eyes deliberately.

Load `robot-intern.png` with `robot-intern.manifest.json`. Final pipeline copies are `run/sprite-sheet-alpha.png` and `run/manifest.json`. Cell **32×64**, grid **4×13**, sheet **128×832**, **42 frames**. RGBA straight alpha, binary transparency, unused trailing cells transparent. Nearest-neighbor sampling. Bottom anchor (16,60), four-pixel bottom margin. Raw strips use pure magenta **#FF00FF**, removed by sprite-gen component extraction.

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

Naming, row order, frame counts, timing, margins and chroma conventions match `assets/art/worker-request.json`, `assets/art/MANIFEST.md` and beanie-scarf. Typing contains only the robot; furniture and the surprised `!` bubble are separate layers. Select idle columns instead of animating them; nominal idle durations remain in the manifest.

Made with sprite-gen 2.20.0, explicit provider **codex**, still-image component rows. No video generation or facelock. Source prompts, generation reports, correction versions and rejected strips are retained in `run/`. Final extraction uses canonical **kcentroid** fitting after rejecting a pixel-grid extraction that flattened heads. Final cells use sprite-gen's standard binary-alpha, nearest transform and shared-palette functions; `run/qa/final-fit.json` records the post-compose recipe. Rebuilding the final fitted output requires that recipe after `compose-atlas`. Raw poses are unchanged; no pose painting, clones, mirroring or frame retiming.

`wave2-contact.png` contains every idle and walk frame at exact **3× nearest-neighbor**, labeled for review. It stays inside this character directory to honor the write restriction. `run/exports/pngs/` contains exact final cells; `run/previews/` contains GIF review loops. Preview looping of once-only gestures is for inspection; runtime playback follows the manifest.

QA: 13 canonical rows, all 42 cells nonempty and distinct within each state, binary alpha, safe margins, y=60 bottoms, transparent unused cells and no visible magenta verified. Final sheet uses **31 opaque colors**. Most silhouettes are 46 px tall; **report is uniformly 39 px tall** to accommodate the offered paper, so switching into that action changes apparent scale. Other weak spots: side/back panel details vary slightly, left and right walking use different camera angles, and leg depth remains subtle. Walks are **experimental**: GIFs were exported but real-time playback and loop seams were not watched. See `qa-notes.md` for the review and correction record.
