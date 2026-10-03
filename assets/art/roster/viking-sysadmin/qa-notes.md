# Visual QA — viking-sysadmin

## Pipeline and references

sprite-gen 2.20.0; provider `codex`; still-image component rows; `gen-set --concurrency 1`. Cell, margins, row order, frame counts, FPS, looping, foot-centroid alignment and 32-color pixel-unfake fit copied from worker. No video, facelock, hand-painted poses or substituted static frames.

A/B/C contacts, dev-hoodie, pixel-agents and available later-wave bases were viewed once in a small local composite. These existing roster/reference images were used only for local visual comparison and were not attached to generation. The new original base used only the user's character description. Action generation attaches this newly generated base and sprite-gen's empty layout guides.

Accepted base review: very large expressive eyes with bright white catchlights, exposed copper hairline and warm face, slate horned knit beanie, thick red braided beard, broad black T-shirt with small server icon, olive cargo pockets and heavy boots. The horned beanie / large beard / broad shoulders silhouette differs from the existing roster. Original base and its audit raw copy contain the same image and were reviewed as one downscaled image.

## Row review

Reviewed first-pass idle/down/left/right walk together in one downscaled composite. Eyes retain white catchlights; hat, copper hairline, beard, server shirt, cargo pockets and boots remain recognizable. Idle correctly supplies down/left/right/up. Three walk rows were rejected because contact frames 0 and 2 appeared to favor the same forward foot, giving a repeated two-step impression. Their original images, prompts and reports are retained under `run/qa/rejected/first-pass/`. Corrections explicitly distinguish left/right contact legs and counter-swinging arms.

Reviewed first-pass walk_up and typing_back together in a second downscaled composite. Walk_up has alternating visible sole/leg changes and a stable back-view identity. Typing_back was rejected for raised shoulder-height fists. Its correction requests low wrists by the shirt hem, hanging upper arms, bent knees, and tiny alternating finger presses. Its retry will also attach this character's generated typing_side strip as a seated/low-arm motion reference; the base remains the identity reference and the empty layout guide retains three slots.

Reviewed typing_side and report together in a third downscaled composite. Typing_side bends the knees, keeps hands at waist height and alternates fingers subtly; accepted as the low-arm reference. Report holds paper close, extends it, and offers it at fingertips; paper touches the hand in all three poses. Large highlighted eyes and the costume remain stable, with small expression differences.

Reviewed sleep and coffee together in a fourth downscaled composite. Sleep has intentionally closed eyes and two restrained seated breathing/head poses. Coffee holds a cream mug, raises it against the mouth, and lowers it; the sipping frame intentionally closes the eyes. Mug touches the hand throughout. Both retain the hat, beard, server shirt and cargo/boot identity. Coffee's first attempt timed out after 180 seconds; sprite-gen's one automatic retry succeeded, and the successful generation report is retained.

Reviewed phone, talk and surprised together in a fifth downscaled composite, completing review of all first-pass source rows. Phone remains attached to both hands while thumbs move. Talk changes an open palm/chest-level hand to a lowered hand; its mouth is small beneath the beard. Surprised widens the highlighted eyes and raises hands beside cheeks. Hat, exposed copper hairline, braided beard, shirt/server mark, cargo pockets and boots remain recognizable. No floating punctuation was generated; the `!` bubble stays a separate layer.

Walk_down and walk_left retakes completed with explicit alternating contact/arm prompts. Walk_right's retake timed out twice (the second call was sprite-gen's automatic retry); its first-pass image/report remain in the rejected archive, and no timed-out output is accepted. The next attempt changes inputs to the accepted generated idle direction strip plus the four-slot guide and uses a shorter dedicated prompt (`run/prompts/walk_right-retake.txt`) in an explicit character-local work directory. No other roster reference is sent.

Typing_back retake completed with the base + generated typing_side + its empty three-slot guide. The shorter walk_right retry completed successfully in 51.5 seconds using the accepted idle strip + guide. The four retake rows were reviewed together in a sixth source composite: walk_down now clearly alternates the low boot between contacts; back typing places wrists near the waist and keeps knees bent; walk_right has contact/passing and arm changes, though tiny pose variation remains. Walk_left still has overly similar wide contacts at 0 and 2, so it receives a second visual correction using accepted idle LEFT identity + this character's corrected walk_down as an alternating-contact motion reference + the four-slot guide. Its shorter prompt explicitly places the foreground hand on opposite sides in frames 0 and 2. First correction evidence is retained under `run/qa/rejected/second-pass/`.

The second left-walk visual correction improved the foreground arm alternation but drifted the outfit: hat became nearly black, beard/braid was shortened, and server icon became a green block. It was rejected after a seventh source composite against idle. Its image, short prompt and report are retained in `run/qa/rejected/third-pass/`. The final left correction attaches the original base + the second-pass LEFT strip (identity only, its repeated motion is explicitly rejected) + guide. It explicitly restores the slate-blue knit hat, voluminous long braided beard, tiny three-slot pale-teal server icon, warm face and original pixel density while retaining opposite foreground arm/leg contacts.

Final left source was reviewed against idle in an eighth source composite. Slate-blue hat, long copper-red braided beard, warm highlighted eye and tiny teal server slots are restored. Foreground arm lies behind the torso at contact 0 and in front of the belly at contact 2; contacts/passing poses are now distinguishable. Small passing-pose differences remain, so no seamless-locomotion claim is made.

All available generated base/row images were viewed in downscaled composites; audit raw copies duplicate their corresponding published generations. Reviewed roster glance, base, eight source composites and both final locomotion/action pages (all 42 curated cells). One extra small palette audit was needed after discovering export color loss: thirteen displayed review images total, no individual large/raw-image viewing.

## Extraction, palette and final checks

Canonical component extraction succeeded for all 13 rows, with no errors. Its reports retain pitch disagreement/fallback, native physical-cap and source-frame edge warnings, including surprised's likely pitch-divisor warning. Standard sprite-gen curation transforms (`run/curation.json`) fit a common within-row height to the safe cell and anchor baseline y=60, using nearest-neighbor pixel transforms. No poses were painted, mirrored, substituted, reordered or retimed. Runtime idle FPS is 0/static selection; sprite-gen's nominal 6-FPS idle GIF is only an orientation-review aid.

The first additional export median-cut discarded rare bright colors, weakening catchlights. That sheet is preserved under `run/qa/rejected/palette-reduction/`. Fixed export to reuse sprite-gen's existing 32-color `palette.lock.json`, including warm white `(250,237,203)`, through canonical `apply_palette`. This is a global color mapping, not facelock or face editing. Shape and alpha bytes are unchanged. The extra compact final palette audit confirms retained bright face marks, slate-blue hat, copper-red beard and teal shirt mark; final contact/action files were refreshed afterward.

`run/qa/validation.json`: 128×832 RGBA; 32×64 cells; 4 columns; exact worker row order/counts/FPS/loop flags; 42 nonempty declared frames; undeclared trailing cells transparent; binary alpha; x≥1, right≤31, y≥4, bottom≤60; no visible magenta; at most 32 opaque colors; bright highlight color present. Palette mapping leaves alpha unchanged. `sprite-gen inspect` returned `ok: true`, no errors; extraction warnings remain available. Per-state GIF/contact exports succeeded.

| State | Static visual verdict / remaining limit |
|---|---|
| idle | Correct down/left/right/up anchors; select facing, do not animate. Tiny eye/hat variation remains. |
| walk_down | Opposite low-boot contacts and passing/arm changes readable after retake. **Experimental; loop continuity unverified.** |
| walk_left | Foreground arm reverses between contacts; opposite leg/contact and passing changes readable. Passing poses remain similar. **Experimental; loop continuity unverified.** |
| walk_right | Right-facing identity, contact/passing and arm changes readable. Small half-cycle differences. **Experimental; loop continuity unverified.** |
| walk_up | Stable back identity, alternating soles/legs. **Experimental; loop continuity unverified.** |
| typing_back | Corrected low wrists and seated knees; finger motion is very subtle/partly occluded. Loop unverified. |
| typing_side | Seated bent knees, low alternating hands; small finger motion. Loop unverified. |
| report | Held-close / extended / offered paper stays in hand; small paper detail and reduced gesture scale. Once/hold final. |
| sleep | Two restrained seated breathing poses; intentionally closed eyes. Loop unverified. |
| coffee | Mug at chest / mouth / chest, attached throughout; sipping eyes intentionally closed. Loop unverified. |
| phone | Phone attached to both hands; tiny thumb changes. Loop unverified. |
| talk | Open palm / chest gesture / lowered hand; mouth detail is small beneath beard. Loop unverified. |
| surprised | Wide eye marks and raised hands beside cheeks; tiny eye/mouth details compress. Separate `!` overlay. Once/hold final. |

Motion was assessed from poses/sequences; GIF creation does not certify watched playback or seamless loops. Real-time loop playback was not checked. At native 32×64, catchlights and server/paper details are delicate and some one-pixel facial/hat changes remain. No facelock, video generation, runtime source edits, app builds/tests or git commit were performed. Saved defaults/authentication settings were not changed.
