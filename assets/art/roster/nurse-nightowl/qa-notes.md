# Visual QA — nurse-nightowl

Reviewed the existing A/B/C roster contacts once at low resolution, dev-hoodie's highlighted-eye benchmark and the pixel-agents quality reference locally. The existing style reference was not uploaded: automatic approval review rejected that external transfer, so the base was generated from the user's textual art direction. Animation generation references are this nurse's own base plus sprite-gen layout guides. Reviewed the accepted base, all 13 source rows in four reduced composites, the rejected/corrected back-typing comparison, extraction comparisons, the complete final 42-frame composite and the final report-size correction. No large raw strips were opened individually.

Identity: messy navy/indigo high ponytail and visible forehead, large brown eyes with light catchlights, short rounded scrub silhouette, charcoal/silver neck stethoscope, cream socks and broad warm slippers. The large cream/amber mug appears only in coffee. Hair and outfit stay recognizable across directions and actions; sleep has intentionally closed eyes and back views intentionally hide the face. This is a new silhouette rather than a worker recolor.

## Corrections and processing

- Rejected initial `typing_back`: hands were raised beside the shoulders. Retained it and its generation report in `run/qa/rejected/`. The retry used `base.png` for identity, **this nurse's `run/raw/typing_side.png` for seated posture and low hand height**, and the three-slot guide for geometry. Extra motion reference and correction prompt are recorded in `typing-back-correction.txt` and `run/reports/gen-set/typing_back-correction.json`. Accepted retry has relaxed shoulders and mostly occluded forward hands at hip height.
- Initial pixel-unfake extraction plus a 46 px curation scale weakened eye highlights and created stripe-like facial pixels. Retained the rejected atlas/request/curation and comparative review. Final `run/sprite-request.json` uses canonical components with `fit.pixel_unfake=false` and `fit.resample=nearest`; row geometry, margins, names, counts, fps and chroma are unchanged.
- Standard `run/curation.json` transforms retain more face detail, ground all poses at y=60 and keep horizontal padding. Report frames receive individual whole-body scale adjustments to a common 46 px height so the extended-paper pose does not shrink. All pixels outside the report row stayed byte-identical during that final adjustment.
- Final publication invokes the existing **`sprite_gen.frames.extract.apply_palette`** on the composed atlas, using the character's shared 32-color palette with its brightest cream changed to white. This is global palette/binary-alpha cleanup; no face-specific pixel editing or facelock. The optional RGB-only intermediate palette bake in `run/variants/` is provenance, not the final runtime input.

## Static motion verdicts

| State | Observed / limitation |
|---|---|
| idle | Down, left, right, up anchors in exact column order; select a facing, do not animate. |
| walk_down | Alternating visible foot positions and arm changes; small facial pixels vary. |
| walk_left | Step/arm changes read leftward; alternating gait depth is subtle. |
| walk_right | Step/arm changes read rightward; some contact phases look similar. |
| walk_up | Alternating slippers and arms; ponytail visible, no face. |
| typing_back | Correct low seated arms; fingertip changes are intentionally extremely small. |
| typing_side | Seated pose with low extended forearms; hand changes are subtle. |
| report | Hold, extend, offer paper; attached to hand. Corrected final body-scale consistency. |
| sleep | Two seated drooping-head/breathing poses; closed eyes are intentional. |
| coffee | Hold, raise to mouth, lower the big mug; connected to hands. |
| phone | Attached phone and distinct thumb/hand changes; face tilts down. |
| talk | Hand at chest, open palm, relaxed hand; tiny mouth variation. |
| surprised | Wide highlighted eyes/O mouth, then hands beside cheeks; `!` remains an external bubble. |

These are frame-by-frame verdicts. **Walk continuity and loop seams remain experimental/unverified.** Sprite-gen animation previews were exported under `run/qa/`, but playback was not watched. Preview GIFs use the canonical pre-publication frames; the final PNG additionally has global palette/alpha cleanup. The preview's nominal idle timing is not the runtime idle playback contract.

## Verification and remaining weaknesses

`sprite-gen inspect` exited 0. Final `run/qa/validation.json` confirms 128×832 RGBA, 32×64 cells, exactly the beanie-scarf `frame_layout`, 13 rows/42 nonempty frames, exact worker row order/counts/fps/loops, all requested frames unique within each row, transparent unused cells, safe margins, 32 colors, binary alpha, no chroma-adjacent pixels and clean RGB under transparent alpha. Report alignment was checked separately after the complete frame review.

Catchlights are readable but sometimes only one native pixel; small face/eyelid changes persist between frames and rows. Back typing is barely visible from behind, and side-walk gait phases are less distinct than their source-scale poses. No facelock was run; the supervisor's pass remains pending. No changes were made to source, tests, Windhawk, shared docs or other character directories; no commit was made.
