# Visual QA — punk-grandpa

Reviewed A/B/C roster contacts once at low resolution, then the available roster idle silhouettes, pixel-agents character reference and dev-hoodie eye reference in compact composites. The accepted base has a large head, exposed forehead/face, visible white eye highlights through lightly tinted round glasses, a swept gray crest, short white beard, broad leather shoulders, checked sleeves, bolo tie and heavy boots. It is an original silhouette rather than a recolor. The pixel-agents comparison is a subjective visual review, not an objective quality score.

Every generated source was viewed in downscaled composites, including the rejected versions. Duplicate `.raw.png`/published generation files represent the same generation. The source reviews are `references/base-review.png`, `run/qa/raw-review-0.png`, `run/qa/walk-left-correction-review.png`, `run/qa/raw-actions-pre-sleep.png`, `run/qa/typing-back-and-sleep-review.png`, `run/qa/raw-review-2.png`, and `run/qa/report-correction-source-and-final.png`.

Corrections:

- `walk_left`: initial contact poses kept a similar leading boot. Preserved the first strip/report/prompt under `run/qa/rejected/`, regenerated the whole row with explicit near/far leg contact and passing phases. Passing knees are more readable; the two contact poses remain similar.
- `typing_back`: initial strip raised fists beside the shoulders. Preserved it under `run/qa/rejected/`, then regenerated with **base-source.png (identity)**, **raw/typing_side.png (seated pose and LOW waist/lap forearm reference)**, and **references/layout-guides/typing_back.png (three-slot geometry)**. The correction prompt and generation report record all three inputs. Final hands remain low and mostly occluded by the torso; finger motion is very subtle.
- `report`: final cell review exposed a shrinking figure as the wide arm/paper expanded. Preserved the wide strip/report/prompt under `run/qa/rejected/`. Regenerated a small postcard and compact bent forearm. All three final figure bounding boxes are 56 pixels high, the paper stays attached, and the eyes retain visible white catchlights. The offer is deliberately compact rather than a long outstretched arm.

| State | Source-frame observation |
|---|---|
| idle | Down, left, right, up in correct order; same hair, glasses, vest and boots. |
| walk_down | Boots alternate forward/back and arms counter-swing; small facial pixel changes. |
| walk_left | Correct facing; clearer passing knees after regeneration; contacts still resemble each other. |
| walk_right | Correct facing with contact/passing leg differences; small crest/eye variation. |
| walk_up | Back of swept hair and leather vest consistent; alternating visible boots. |
| typing_back | Seated back view; corrected low hands are mostly hidden; nearly static finger motion. |
| typing_side | Seated right-facing pose, low forearms and bent knees; small finger changes. |
| report | Paper held close, extended, then offered; paper touches the hand in all frames. |
| sleep | Seated, closed eyes intentionally; second frame tilts the head to the shoulder. |
| coffee | Mug at chest, lifted to mouth, lowered; attached to hand, no steam. |
| phone | Navy phone held in both hands with pale screen pixels; thumb changes are subtle. |
| talk | Hand near chest, gesture variation, then lower hand/open mouth; no bubble. |
| surprised | Wide highlighted eyes/O mouth, then raised hands near cheeks; no baked `!`. |

No image painting, face replacement, mirroring, pose duplication or timing changes were used to conceal weak motion. No video or facelock was run.

Playback limitation: browser inventory returned `apps: [], browsers: []`. GIFs are review artifacts; actual playback and loop seams have not been watched. All four walk rows remain **experimental / continuity unverified**. Source sequences were assessed for pose readability, not certified as seamless animation.

Processing correction: the first `pixel_unfake` extraction emitted pitch-family, physical-cap and edge warnings, and its grid/palette processing erased the white eye catchlights. Fractional placement also risked losing small details. This extraction and its transforms are preserved in `run/qa/pixel-extract-rejected/`. The accepted output instead uses canonical component separation and **NEAREST fitting**, followed by the engine's own `sprite_gen.frames.extract.binarize_alpha` function (threshold 128). RGB values are not painted or replaced. The alpha-cleanup hashes are in `run/qa/nearest-alpha-cleanup.json`. Curation selects these canonical frame PNGs with only integer dx/dy shifts. `fit.pixel_unfake` is false in the final recipe; the manifest's pixel stream is the selected canonical PNG stream. This keeps the highlighted eyes and hard edges while preserving the sheet geometry.

The final NEAREST extraction returned `ok: true`, no errors and no extraction warnings. The full-cell composite and enlarged face details were viewed, then the corrected report's three source/final frames were viewed in `run/qa/report-correction-source-and-final.png`. The other 39 final frames were verified byte-for-byte unchanged. The full review artifact `run/qa/final-nearest-all-frames.png` was refreshed with the final report. White highlights are visible through the tinted lenses in the exposed-eye views; sleep closes the eyes and back views hide them intentionally. Component fitting still gives small height/position differences between some walking poses.

Final validation: **128×832 RGBA**, **32×64 cells**, **4×13 grid**, **42 nonempty declared frames**, exact worker row order/counts/FPS/loop flags, binary alpha **0/255**, all ten unused cells transparent, horizontal/vertical safe margins honored, baseline **y=60**, and no visible chroma-adjacent pixels. `run/qa/validation.json` records every final bounding box. Atlas composition and GIF export succeeded. Idle FPS is explicitly 0/static facing selection; nominal duration entries are not an idle animation.

Remaining weaknesses: the two left-walk contacts are similar; back typing is nearly static because hands are occluded; side typing/phone finger motion is small; tiny crest/glasses pixels and figure registration vary between gait poses. Actual animation playback/loop seams remain unverified, and all four walking rows remain experimental. No facelock was run. No application build/test was needed for this isolated art deliverable.
