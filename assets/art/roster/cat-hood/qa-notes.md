# Visual QA — cat-hood

Read SPEC.md, worker-request.json, the art manifest/generation notes, sprite-gen skill and user/atlas workflow, and beanie-scarf's README/manifest/QA conventions. The pixel-agents character reference was viewed for the requested quality comparison. The latest art request governs this asset's appearance.

The accepted base and every surviving raw row were viewed in two small composites. The lavender hood ears, bilateral plum twin tails, cream knee socks, dark shorts, selective outline and large eyes with catchlights establish the identity. Reused **idle, walk_down, walk_left, walk_right, sleep and surprised** source strips; their 20 poses were retained. Sleep intentionally closes the eyes; rear views intentionally hide the face.

Corrections, all generated with explicit provider **codex**, one call at a time:

- **walk_up:** removed the exposed nape-hair opening and cream rear-ear interiors. Final closed lavender rear hood follows idle column 3, with side twin tails and the center seam.
- **typing_back:** matched the closed rear hood and lowered open hands to hip/keyboard height; retained bent seated legs.
- **typing_side:** replaced the nearly frontal seated pose with a right-facing profile, bent knees and horizontal forearms.
- **coffee / phone:** restored down-right facing. Mug rises to the mouth and returns; phone stays attached to both hands with thumb changes.
- **report:** the original paper became indistinct after extraction. The replacement has a cream rectangular report held at chest, extended halfway and offered outward, with planted feet.
- **talk:** the first correction still faced front and was rejected. `talk-v2` uses `run/raw/coffee.png` as additional accepted facing evidence, with `run/raw/idle.png` as identity corroboration and the generated three-slot layout guide. Mug removed; relaxed/open-palm/lowering-hand poses now face down-right. This is the only additional motion/facing reference used by this resumed pass.

All eight newly generated strips were viewed in three bounded composites. Accepted correction reports are copied to `run/reports/gen-set/`; correction candidates/reports and the rejected first talk correction remain under `run/qa/corrections/`. Original replaced strips and reports are under `run/qa/rejected/`. Earlier interrupted-run rejection notes about walk_up and typing_back are resolved by the replacements above.

Canonical sprite-gen component extraction was used throughout, never fixed-slot raw cutting. The default chroma-adjacent validation radius incorrectly included an intentional plum highlight `[188,113,207]`, whose RGB distance to magenta is 139.86. Extraction used radius **120**, below every legitimate locked palette color; the pixel-count limit was unchanged. Final visible pixels contain no pure magenta, alpha is binary, and transparent pixels have zero RGB. The locked 32-color material palette acquires additional dark fringe/outline colors in canonical extraction; the final sheet contains **52 visible RGB colors**, recorded honestly in the delivered manifest.

Extraction reported native-grid/cap warnings for the high-resolution source art. Sprite-gen's standard `curation.json` scale/dy transforms fit each row inside the same cell with baseline y=60: standing rows target 46px height, seated rows target 40px, maximum width 30px. Pixel transforms use nearest sampling and the engine's pixel snapping. No pose painting, mirroring, frame cloning, reordering, timing changes or face-lock processing was performed.

All **42 baked output cells** were reviewed in `run/qa/final-review-1.png` and `final-review-2.png`. The final geometry check verifies 128×832, 32×64 cells, exact worker row order/counts, 42 occupied cells, 10 transparent unused cells, horizontal/top safety margins, bottom y=60 and no chroma. Sprite-gen inspect returned `ok: true` with no errors; its raw pitch/cap/edge warnings are retained in its report. The inspected output margins pass after curation. `C-contact.png` is the labeled 3× idle/walk contact sheet.

| State | Static motion/identity verdict |
|---|---|
| idle | Four static orientations: down, left, right, up. |
| walk_down | Distinct foot/contact poses; small face-detail changes. Experimental continuity. |
| walk_left / walk_right | Contact and passing silhouettes, arm/leg changes; near/far-leg phase is subtle at 32px. Experimental continuity. |
| walk_up | Closed rear hood consistent with idle; distinct foot positions. Experimental continuity. |
| typing_back | Low hands and seated legs; finger motion is very subtle. |
| typing_side | Right profile and seated knees; alternating hand poses. |
| report | Held / extended / offered paper remains attached to hand. |
| sleep | Two seated, closed-eye head/breathing poses. |
| coffee | Attached mug at chest / mouth / chest; middle pose closes eyes intentionally. |
| phone | Attached phone and small thumb/head changes. |
| talk | Down-right facing, open-palm gesture, small mouth change. |
| surprised | Widened eyes / raised hands; `!` remains an external overlay. |

Remaining weak spots: native-size face/eye and hair pixels vary slightly between some rows; small source differences survive curation. Back typing and phone motion may be difficult to notice. Walking contact sheets and distinct frame hashes do **not** establish smooth locomotion or a seamless wrap: real-time playback was not observed, so all four walks remain **experimental/unverified**. GIF previews preserve the requested frame counts and playback rates for review. No video pipeline, defaults/settings writes, repository commit or writes to another character's folder were performed. The supervisor owns any later `tools/facelock.py` pass.
