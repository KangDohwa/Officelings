# apron-baker ? team C

Bald ginger-bearded friendly man with a broad build, large highlighted olive eyes, red-and-cream plaid shirt, cream apron with front pocket, charcoal trousers and brown boots. The scalp/beard, wide torso and apron give him his own silhouette.

Load `apron-baker.png` with `apron-baker.manifest.json`. Canonical pipeline files are `run/sprite-sheet-alpha.png` and `run/manifest.json`. Cell **32?64**, grid **4?13**, sheet **128?832**, **42 frames**. RGBA straight alpha; unused trailing cells are transparent. Nearest-neighbor sampling. Bottom anchor (16,60), four-pixel bottom margin. Raw strips request pure magenta **#FF00FF**, removed by sprite-gen component extraction.

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

Exact row naming/order/counts match `assets/art/worker-request.json`, `assets/art/MANIFEST.md` and beanie-scarf. Typing contains only the person; furniture and the surprised `!` bubble remain separate layers. Select an idle column rather than playing the nominal idle durations in the sprite-gen manifest.

Made with sprite-gen 2.20.0, explicit provider **codex**, still-image component rows; no video. Reused **26 original poses**, including the first two poses of each down/left/right walk. Corrected **16 poses**: opposite walking phases, back apron straps/waist, back typing and wrong-character talking. Additional walk takes are extracted as candidates and selected through `run/curation.json`; runtime rows still contain exactly four walk frames. Accepted source choices and rejected candidates are recorded in `qa-notes.md`. Standard curation scale/bottom placement fits the character into the same cell contract as beanie-scarf. No local pose painting, mirroring or timing changes. No facelock pass was run.

`C-contact.png` is the labeled idle + all walk frames at exact **3? nearest-neighbor**, kept inside this character folder. Final frame review pages, validation and GIFs are in `run/qa/`. Weak spots: small beard/eye-shading changes and simplified plaid remain; side-walk limb alternation and typing fingers are subtle. Walk loops are **experimental**: no browser was connected, so real-time continuity and seams are unverified.
