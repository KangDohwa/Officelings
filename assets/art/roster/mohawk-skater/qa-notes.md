# Visual QA — mohawk-skater

Provider **codex**, sprite-gen **2.20.0**, still-image component-row pipeline, concurrency **1** throughout. No video, no facelock, no saved-default changes. Artifacts are confined to this character directory. Worker/beanie conventions take precedence over the older procedural-sprite text in SPEC.

Reviewed the available A/B/C roster contact images at low resolution and the local pixel-agents character reference in `reference-review.png`. The C contact available during this run was under `cat-hood/`. The new character's tall narrow blue crest, shaved sides, slim patched vest silhouette and oversized sneakers distinguish it from those references. The pixel-agents reference supplied pixel craft only, not an identity to recolor.

Viewed the accepted uncropped base and EVERY published generated strip using downscaled composites, rather than opening large raw images individually. Duplicate `.raw.png`/published files and rejected copies contain the same generation content. Review evidence: `base-review.png`, `early-raw-review.png`, `walk-raw-review.png`, `actions-raw-review-0.png`, `actions-raw-review-1.png`, `correction-raw-review.png`, `worker-motion-review.png`, and `contact-correction-review.png`.

## Corrections and reference provenance

- Both initial side walks repeated the same foreground-leg contact. First retries improved passing poses but retained the repeated third contact; both versions remain in `run/qa/rejected/`. Second retries targeted only pose 2 using existing worker motion as an additional reference. Static inspection now distinguishes the brighter near knee/leg going backward from the darker far leg going forward, with opposite arm swing. This is a frame-level judgement, not a certified seamless loop.
- Initial back typing had raised open palms near the upper vest. Its rejected strip is preserved. The retry uses the original back strip, accepted `typing_side` strip as a **LOW lap-height posture reference**, base identity and three-slot geometry. Hands now peek beside the lower vest/hips with mostly hidden forearms. Changes are intentionally small.
- `references/walk_left-worker-motion.png` and `references/walk_right-worker-motion.png` are exact declared row crops from `../../sheets/worker.png`, enlarged **4× nearest-neighbor**. They were viewed before use and supplied **leg phase only**. Mohawk heads, highlighted eyes, vest, jeans and shoes continue to come from the mohawk strips. These refs are not runtime character art.
- Correction prompts live at the character root; the final generation reports in `run/reports/gen-set/` record their refs. No motion was painted, mirrored, cloned, reordered or hidden by changing timing.

## Per-row static review

| State | Review |
|---|---|
| idle | Four separate down/left/right/up anchors; large highlighted eyes in visible face views. |
| walk_down | Opposite feet advance; eyes are slightly narrower than idle. |
| walk_left | Corrected contact/passing phases; bright near knee switches side at opposite contact. |
| walk_right | Corrected contact/passing phases; opposite arm and near/far leg depth. |
| walk_up | Back-facing crest/shaved sides; alternating shoe contacts, face hidden. |
| typing_back | Corrected low hands at lower vest, bent seated legs; tiny hand changes. |
| typing_side | Seated down-right three-quarter typing with low extended forearms; no furniture. |
| report | Paper held, extended and offered; it touches the hand. |
| sleep | Seated closed-eye nap with two head/breathing poses. |
| coffee | Attached cream mug at chest, mouth, then lowered; sip closes eyes intentionally. |
| phone | Navy phone held at chest; small thumb changes, highlighted visible eyes. |
| talk | Open-hand talking gesture lowers with mouth change. |
| surprised | Wide highlighted eyes/O mouth, then hands beside cheeks; **!** is a separate runtime overlay. |

All cyclic locomotion remains **experimental / runtime continuity unverified**. The computer-use inventory returned `apps: []`, `browsers: []`; no connected browser was available to watch playback. GIF export alone is not evidence of an observed seamless loop. Non-loop report/surprised were reviewed as start/middle/end reactions.

## Extraction and final review

The first canonical pixel-unfake extraction emitted pitch/harmonic/cap warnings. Its subsequent shrink visibly erased eye highlights; it was **rejected**, even though geometry checks passed. `cells-review-0.png` / `cells-review-1.png` show that rejected processing result, and `run/qa/rejected/pixel-unfake-*` preserve the sheet, recipe and reports.

The final run re-extracts the SAME accepted strips with sprite-gen's documented **kcentroid component fit** (`fit.pixel_unfake: false`, `fit.resample: kcentroid`). No raw strip is cut by fixed slots. This avoids the failed pitch inference and a second shrink. Canonical extraction reports **no errors or warnings**. Standard curation applies only integer horizontal/bottom placement, with no scale, rotation, mirroring or face editing. The tall crest uses up to **56 px** total silhouette height; seated/wide poses may be shorter. Cell and foot baseline remain exact.

Final palette normalization uses sprite-gen's `build_shared_palette` / `apply_palette`: **32 colors**, binary alpha. It snaps **5,913** partially opaque source pixels to full opacity; the zero/nonzero silhouette mask is checked identical, so no covered pixel is added or removed. Alpha byte values are deliberately normalized, rather than claiming they are unchanged.

Viewed ALL **42 final cells** in `final-review.png`, including a thumbnail of the actual labeled **3× nearest-neighbor** `wave2-contact.png`. This final review replaces the rejected cell review pages. The blue eye color and bright highlights are visible in open-eye face views; sleep and the coffee sip close eyes intentionally. Back views hide the face.

`run/qa/validation.json` confirms exact beanie cell/frame-layout/order/counts/FPS/loop/duration conventions, 128×832 RGBA output, 42 nonempty declared cells, fully transparent trailing cells, safe margins, all soles at y=60, no visible magenta, 32 opaque palette colors, unchanged silhouette mask, and **no identical frame pairs within any row**. Final sprite-gen `inspect` reports **ok, no errors or warnings**. GIFs under `run/previews/` are review aids with centisecond timing; idle GIF is an orientation review at a nominal preview speed. The runtime manifest controls exact action timing and once/loop flags. No app build/tests were run for this asset-only task.

Finish workflow was called with the final PNG, codex/GPT-row choices and curation **skip**. Its optional saved-default question was resolved **no**; no preferences were written.

Remaining weak spots: small facial/eyebrow/earring and vest-patch pixel variations remain across independently generated rows; knee-rip details are tiny. The taller crest occupies more vertical space than beanie-scarf. Back typing and phone gestures are subtle. Side-walk contact depth is stylized, and all locomotion continuity/seams remain **experimental / unverified** without observed playback. **Facelock was not run**; the supervisor owns it.
