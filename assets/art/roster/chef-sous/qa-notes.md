## Current cleanup delivery ? 2026-10-04

**PASS: facelock, blink, seated pixel checks, and cyclic frame/seam review.** This does not certify real-time playback or polished anatomy in every cell.

Generated one corrected face reference with the built-in image_gen tool, extracted 17 small eye patches into nine frame-zero action references, retained full native catchlight coverage when reducing an eye to three columns, and applied reviewed eye/brow/moustache lock boxes and idle placements. Bodies, hair/hat silhouettes, raw strips, frame order and timing remain authored. No whole generated face or pose replaces a frame.

Load `chef-sous.png` + `chef-sous.manifest.json` for the flat 13-row delivery. The root PNG equals the currently committed locked PNG. Follow `locked/chef-sous.manifest.json` for generation paths; older root files in `locked/` are historical. The immutable corrected source is `cleanup/source/`, its local reviewed calibration is `cleanup/profile.json`, and the pre-cleanup delivery is `cleanup/before.*`. The original `run/` is preserved.

New seated frames are committed through **`seated/chef-sous.manifest.json`** inside this character folder: 12 normal frames in three appended rows, plus eight phase-matched side blink frames. Follow that manifest to its `.generations/` sheet and blink files. The top 42 cells are byte-identical to the current locked sheet; this local output has not been copied to shared `assets/art/seated/` or adopted by the pack.

QA: `cleanup/facelock-qa/report.json` has 13 groups, after=0, failures=0. `cleanup/validation.json` records independent reload checks: 42 nonempty flat cells, 12 seated cells, 21 opaque colors, binary alpha, unchanged silhouette/layout/timing, zero transparent RGB, empty trailing cells, safe margins, baseline y=60, source freshness and eye-only blink changes. Seated heads are preserved through the recorded one-pixel nod and lower bodies are identical across phases. Shared facelock/QA/seated code and QA thresholds were not edited.

Walks: all four full cycles and the explicit **2?3?0?1** seam strips were viewed in `cleanup/walk-seams.png`. No extra wrap jump or baseline reset was identified; native changed-pixel counts at 3?0 are down: 520, left: 575, right: 406, up: 546. These counts supplement visual inspection and do not score gait quality. `cleanup/walk-metrics.json` binds the results to frame hashes, while the four `cleanup/walk_*.gif` files preserve order and timing. **Real-time/browser/runtime playback remains UNVERIFIED:** browser inventory returned apps=[], browsers=[], and the in-app browser was unavailable. A self-contained replay page for all 16 loops is retained at `../chef-sous/cleanup/loops.html`.

Remaining weak: Thin moustache readability still varies by angle. Left-walk opposing-leg contacts are subtle; hat/torso pixels vary slightly between authored phases. Seated knees/forearms are deliberately simple and lose trouser detail. Native eye pixels being consistent does not certify the full original face-quality bar in every pose.

Reproduce with the existing sprite-gen Python environment and the character-local driver `assets/art/roster/chef-sous/cleanup/cleanup.py chef-sous <stage>`; stages are `source`, `lock`, `qa`, `seated`, `review`, `deliver`. It invokes the existing tools' processing/check functions with local calibrations in memory and redirects seated publication here. QA invokes `facelock_qa.check` for this committed report because the stock CLI scanned an unrelated locked report before filtering and hit PermissionError; the original checker and its assertions are unchanged. Corruption-detecting self-check also passed.

No build_pack, pack mutation, runtime change, commit or push. Image generation was serial (chef face, then gardener face); generated references and prompts are local to those two cleanup folders.

---

**Historical generation/extraction notes below describe the pre-cleanup snapshot and are superseded for current face-lock, blink, seated and seam status.**

# Visual QA — chef-sous

## Pipeline and references

sprite-gen 2.20.0; explicit provider `codex`; still-image component-row pipeline, `gen-set --concurrency 1`. Numeric recipe copies worker's 32×64 cells, 13-row order, 42 frame counts, timing, margins, foot alignment and 32-color pixel-unfake fit. No video generation, facelock, whole-sheet mirroring, pose painting or substituted static poses.

Existing A/B/C contacts were glanced at once in a low-resolution composite. Available wave-3 sprites/bases, dev-hoodie and the pixel-agents reference were compared in two small composites. The chef's narrow tall paper hat, chestnut temple tufts, thin mustache, double-breasted rolled-sleeve jacket, shoulder towel, checkered trousers and clogs provide an original silhouette.

An initial invocation attaching existing `dev-hoodie/base.png` was rejected by automatic approval review because explicit external transfer of that existing reference was not established. It did not generate an image. The successful base used only the user's new chef-sous description, with no reference files. Row generation uses this newly generated chef-sous base and sprite-gen's generated empty layout guides. No existing roster image was sent. No saved defaults or authentication settings were changed.

Accepted base: large warm eyes with visible white highlights, visible brown hairline, thin mustache and a cheerful face; high crooked paper hat and shoulder towel remain readable. Idle's four orientations and walk_down's four phases were viewed together in a downscaled composite: highlighted eyes, clothing and towel identity stay stable, back-facing hair/towel are readable, and walk_down alternates foot contacts.

All thirteen first-pass raw rows were viewed in four downscaled composite pages; identical `.raw.png` audit copies share the same generation content. Additional static findings: right/up walks change foot contact and arm positions; side typing has bent knees and low alternating hands; report progresses from held paper to offered paper with physical hand contact; coffee raises and lowers the attached mug; phone has an attached dark handset and changing thumbs; talk changes hand/mouth poses. Sleep intentionally closes its eyes. Back views intentionally hide eyes. Coffee's sipping frame closes its eyes intentionally.

Rejected first-pass rows: `walk_left` had weak contact-leg alternation and an overly frontal view; `typing_back` raised hands toward shoulders and reversed the crooked hat; `sleep` swung the head too much between breathing poses; `surprised` narrowed the paper hat into a pleated silhouette. Originals and their original generation reports are preserved in `run/qa/rejected/`.

Retake reference plan: walk_left attaches chef-sous base + generated idle strip (left-facing orientation) + its generated empty layout guide. typing_back attaches base + generated walk_up (back-view hat/hair/towel identity) + generated typing_side (seated posture and low arm height) + its guide. Sleep/surprised attach base + generated idle (front identity) + their guides. These extra row references and purposes are explicit in the correction prompts and retake reports. All are this character's newly generated images.

Correction review: viewed all four retaken rows together. Left walk now follows idle's near-profile orientation; its contact-leg change remains subtle. Back typing has low hands and the matching back-view hat lean. Sleep preserves its head angle with a much smaller breathing change. Surprised keeps the accepted broad paper hat and cheek gesture.

The first composed report row placed feet near x=10 rather than the shared x=16 axis and used a wide reach. It was also rejected, preserved and regenerated with base + idle + guide: tiny 4×5 logical-pixel paper and a compact bent-elbow handoff. Viewed its replacement strip before re-extracting only report. This retake keeps body/feet steady and paper attached; final handover reach is intentionally small.

Canonical extraction passed. Its pixel-pitch warnings are retained in `run/reports/extract.log` and `extract-report-retake.log`: some rows had harmonic/axis disagreements and native-size caps. These are processing warnings, not a visual pass. Initial composition filled the cell top and produced a y=61 walk foot; typing rows were too large. Standard `run/curation.json` uses row-wide scales and per-frame foot-centroid/bottom translations, with no repainting, mirroring or pose replacement. Standing rows target at most 54 px including the tall hat; seated rows at most 46 px. Side clearance accounts for the foot axis and asymmetric silhouette. Canonical frame caches are unchanged.

Final export uses sprite-gen's standard shared-palette functions, as beanie-scarf does, to reduce 57 opaque colors to 21 (within the requested 32-color limit). Alpha is byte-identical before/after this palette pass. Root manifest retains the canonical rectangles and action timing and explicitly sets static idle FPS to 0. The root runtime pair owns the final palette; the canonical run sheet and GIFs precede this final palette pass.

Viewed every final cell at 3× nearest-neighbor in three labeled pages. Combined with the references/base, first-pass and correction pages, there were **12 composite review images total**; raw audit duplicates were not separately displayed. No large raw image was viewed alone.

| State | Final static review | Motion verdict |
|---|---|---|
| idle | down/left/right/up anchors; hat, towel and checks readable; front eye highlights small/uneven | static orientation selection |
| walk_down | foot and arm changes; face scale roughly stable, small pixel/detail changes | experimental; playback unverified |
| walk_left | corrected near-profile view; contact/passing shapes visible, alternate contact leg still weak | experimental; playback unverified |
| walk_right | short steps and arm changes; tiny face/mustache detail | experimental; playback unverified |
| walk_up | rear hair/hat/towel stable, shoes alternate; small grid variation | experimental; playback unverified |
| typing_back | corrected low arms, seated knees; very subtle hand presses, broad rear head | playback unverified; subtle |
| typing_side | bent knees and low arms; wrist changes small; seated apparent scale differs | playback unverified; subtle |
| report | compact held/offered paper, foot axis restored; offer extension intentionally short | non-loop sequence statically reviewed |
| sleep | eyes closed, same head angle, tiny breathing change; extraction makes body broad | playback unverified; subtle |
| coffee | hold/lift/lower attached mug; sipping eyes intentionally closed | playback unverified |
| phone | attached phone and changing thumbs; highlights/mustache tiny at native size | playback unverified; subtle |
| talk | open hand and mouth changes; native facial detail uneven | playback unverified |
| surprised | corrected broad hat, O mouth and hands beside cheeks; native eye highlights small | non-loop sequence statically reviewed |

Automated validation: 128×832 RGBA sheet, exact 32×64 cells, worker/beanie-scarf's 13-row order and all 42 declared frames, all unused trailing cells empty, binary alpha, no magenta key remnants, nonempty frames, top margin at least 4 px, side margin at least 1 px and every foot baseline at y=60. Evidence is `run/qa/validation.json`. `sprite-gen inspect` has no errors and preserves report pitch/cap warnings. Thirteen GIF previews were exported. No connected playback was reviewed; GIF export and automatic inspect do not certify continuity or a seamless loop.

**Remaining limitations:** the strict large-eyed highlighted-face quality bar is only partly met after pixel extraction: some highlights survive as one bright pixel, while others weaken, and the thin mustache is not equally readable in every angle. No face pixels were painted and no facelock was run. Native facial consistency still needs supervisor review/facelock. Left walk's contact alternation is weak, typing/phone/sleep motion is subtle, seated scale differs and small hat/hair/face pixel changes remain. This is a complete interchangeable sheet with these explicitly recorded visual limitations, not a certified face/motion-quality pass.
