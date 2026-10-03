# Visual QA — astro-hoodie

## Reference and scope

Read `SPEC.md`, `assets/art/worker-request.json`, `assets/art/MANIFEST.md`, and the beanie-scarf README, QA notes, request and manifest. Reviewed A/B contact sheets once at low resolution in `references/roster-overview.png`; C-contact.png was absent at that check. The same composite contains the local pixel-agents reference and dev-hoodie idle eyes. The strict user art direction supersedes the original worker prompt's dot eyes. Only this character directory is written. No commit, video or facelock.

Distinctive silhouette: silver-blue angular short hair and a single lifted forelock, completely exposed warm face, cropped broad white mission jacket and a rounded helmet shell resting behind the head like an open shoulder hood. The hood is not a visor or backpack. This is an original design rather than a roster recolor.

## Rejected first fit

The first base and idle/walk_down/walk_left/walk_right source rows looked consistent at source resolution, but their finer pixel density lost obvious white highlights during canonical 32px extraction. Rejected them for the face requirement. `rejected-first-fit/` preserves those rows, extraction reports and two composites. The original base remains at `references/rejected-base-original.png` with `base-v1.report.json`.

Generated `base-v2.png` with explicit 26×46 logical-pixel budget, large solid hair clumps, uncluttered face, and prominent rectangular white highlights inside both navy eyes. It used the first base for identity and the pixel-agents sheet for density/style. The base revision comparison was viewed. Adopted this base as `base.png`, then regenerated idle. In the canonical idle cells, both front eyes contain vertically stacked bright pixels inside the dark iris, and the face/hairline remain visible. Pixel pitch warnings remain diagnostic evidence; they are not silently called a clean native-grid pass.

## Pipeline

Explicit provider codex; still-image component rows; generation concurrency 1. Canonical sprite-gen extraction performs chroma removal, component separation, pixel processing and placement. Source/output audit twins contain the same generated content and do not count as separate unseen generations. All review images are compact composites; raw large images are not viewed one by one. Source strips use #FF00FF; final cells use straight RGBA.

## Targeted row corrections

Viewed all six initial v2 walk/typing strips in `run/qa/raw-review-2.png`. Outfit/face/hood identity is consistent, but back typing still raised hands near the shoulders, and down/up walks repeated the same low/leading shoe in their contact poses. Preserved these three source rows in `run/qa/rejected/` and regenerated them.

Back typing correction attaches `run/base-source.png` (identity), `run/raw/typing_side.png` (seated posture and LOW arm height only), and `run/references/layout-guides/typing_back.png` (three-slot geometry). The prompt and `typing_back.correction.report.json` record these extra motion references. In `run/qa/raw-corrections-review.png`, the hands now sit near the lower jacket/waist instead of beside the head; finger changes remain deliberately tiny, and the view is slightly turned toward the right.

Down/up walk correction explicitly requires opposite screen-left/screen-right low shoes in contact frames 0 and 2, with passing poses between. The correction composite shows the leading shoe swapping sides. Left/right walk arms/legs shift but their passing/contact distinctions are modest. No source pose was painted, mirrored, reordered, cloned or replaced with a still; timings remain the worker timings.

## Final review and checks

Viewed all six report/sleep/coffee/phone/talk/surprised source rows in `run/qa/raw-review-3.png`. Paper, mug and phone touch the hands; no furniture, detached symbols, punctuation or chroma-colored costume pieces were found. The mug-to-mouth pose closes its eyes intentionally, as do the sleep frames. All source generations, including rejected attempts, were viewed in compact composites. Audit twins were byte-checked identical. Total visual review: **12 images/composites**, without opening raw large images individually.

Canonical extraction succeeded for every row. The extractor records pitch disagreements, consensus fallbacks and native-size caps; those warnings remain in the frame manifest. Extraction can give different apparent row scales, so `fit-curation.py` records sprite-gen's standard uniform within-row scale and bottom placement in `run/curation.json`. Target standing height is 46px, seated height 38px; width is capped at 30px. Wide report poses constrain the entire report row to a smaller body scale. `write_curation_atomic` stamps the per-state frame revisions; compose applies the normal NEAREST pixel transform. No local pose or eye painting and no facelock.

Viewed all **42 final cells** in `run/qa/final-review-1.png` and `final-review-2.png`, then the labeled `astro-hoodie-contact.png` at exact **3× NEAREST**. Hair, exposed face, mission jacket and pushed-back hood remain recognizable. The bright front-eye highlights survived the second design/extraction; some side/seated highlights are only a tiny pixel cluster and less prominent than the source. Small hair, patch and facial pixel changes remain between poses. The report row is smaller, back typing is slightly turned right, and typing/phone finger changes are very subtle. Coffee faces mostly down rather than strongly down-right.

`finish-assets.py` passed assertions for **128×832** RGBA, **32×64** cells, exact worker row order/counts/FPS/loops, **13 rows / 42 nonempty frames**, in-bounds rectangles, binary alpha 0/255, zero RGB in transparent pixels, all **10 unused cells empty**, x padding, four-pixel bottom margin, and no visible magenta-family key pixels. Evidence: `run/qa/validation.json`. Sprite-gen inspect exited **0**, `ok: true`, with no errors; its warnings concern the uncurated extraction and remain in `run/sprite-inspect.report.json`. Final exported sheet geometry was checked after curation.

| State | Static motion review / remaining limit |
|---|---|
| idle | Down, left, right, up anchors; select direction, do not animate. |
| walk_down | Corrected opposite low shoes in contact frames 0 and 2; small pose/face pixel variation. |
| walk_left | Visible stride/arm changes; contact/passing differences remain modest. |
| walk_right | Visible stride/arm changes; contact/passing differences remain modest. |
| walk_up | Corrected opposite low shoes; back hair/hood preserved. |
| typing_back | Hands lowered to waist; tiny motion, slightly turned toward right. |
| typing_side | Bent knees, low extended forearms; tiny finger changes. |
| report | Held, extended, offered paper; smaller apparent body scale. |
| sleep | Seated closed-eye nap, subtle head/breathing change. |
| coffee | Held, lifted to mouth, lowered mug; mostly front view. |
| phone | Phone touches hands; thumb movement is subtle. |
| talk | Hand opens/lowers with small head/mouth change. |
| surprised | Wide highlighted eyes, then hands raised; external ! bubble. |

Exported curated GIFs for all states under `run/previews/`. Playback was **not watched** in this task, so all four walking loops remain **experimental**, and seamless loop continuity is **unverified**. The inspect metrics and static contact sheets are not independent proof of motion quality. GIF defaults are review behavior, not the runtime idle-selection/FPS/loop contract. The comparison to pixel-agents is a subjective visual review; no objective score establishing “at least as good” is claimed.

Ran the workflow finish stage against the delivered sheet with curation=skip. Its only remaining question was saving global defaults; selected **no / this request only**, consistent with the user's instruction to answer the workflow questions autonomously and the character-folder-only write scope. No preferences were saved.
