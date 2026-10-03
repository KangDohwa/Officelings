# Deterministic face lock

`tools/facelock.py` uses Python, Pillow and NumPy from the existing sprite-gen environment. It reads production assets and writes sibling `locked/` directories. Source art and team runs stay unchanged. It neither regenerates art nor changes the renderer, builds a pack or commits files.

Run these from the repository root after the art inputs are ready. Incomplete characters are skipped, so they can remain in the tree.

```powershell
& C:\Users\myori\.codex\skills\sprite-gen\.venv\Scripts\python.exe tools/facelock.py --all --blink
& C:\Users\myori\.codex\skills\sprite-gen\.venv\Scripts\python.exe tools/facelock_qa.py --all --self-check
```

The first command handles all character manifests directly under `assets/art/sheets/` and all roster directories. Non-character sheets are ignored. A roster publication needs all 13 states and 42 nonempty frames with declared 32×64 geometry. If final `run/curated` exports exist, every export must match the source sheet. Missing rows/frames, mismatched exports, or source bytes changing during processing cause a skip. Inputs are read into memory and checked again before publication. The original implementation replaced individual files atomically; the current shared publisher stages an immutable generation and replaces one root manifest under a writer lock. Identical reruns keep unchanged file timestamps. A team can publish after the source check; QA detects stale hashes and requests a rerun. The `.sg-rwlock` filename alone is not a completion marker. Finished runs also retain it.

The original output layout placed sheets at `assets/art/sheets/locked/<sheet>.png` with a copied manifest and added metadata. Roster output used `assets/art/roster/<id>/locked/`, retaining the original sheet/manifest filenames, stable `<id>.png` / `<id>.manifest.json` aliases and `curated/<state>-frame-N.png`. The current publisher retains those logical names inside `.generations/<hash>/`; the root manifest points to the committed generation. Follow its paths when reading output. Animation geometry, FPS, loops and unused transparent cells stay intact. `--blink` adds three-facing blink PNGs and `blink` metadata. Each `<name>.facelock.json` records source hashes, reference choices, pixel boxes, integer placements and actual edited pixels. The pack builder selects inputs separately; rerun it to adopt the output, as described in the [art pipeline](art-pipeline.md).

For one sheet and its production manifest:

```powershell
& C:\Users\myori\.codex\skills\sprite-gen\.venv\Scripts\python.exe tools/facelock.py --manifest assets/art/sheets/codex-worker-01.manifest.json --sheet assets/art/sheets/codex-worker-01.png --blink
```

`--frames PATH` instead accepts a complete flat directory of final `<state>-frame-N.png` files plus `--manifest`. Do not pass raw extractions when curation changed the final geometry. `--profile PATH` supplies reviewed coordinates for an unusual character: JSON `{"rows": [[eyeBoxes, mouthBox], ...]}` in `walk_down`, `walk_left`, `walk_right`, `typing_side`, `report`, `sleep`, `coffee`, `phone`, `talk`, `surprised` order. An eye box is `[x0,y0,x1,y1]` in native cell pixels, with exclusive right/bottom edges; each row may have multiple boxes. `mouthBox` can be `null`. Profile files are read only.

## Pixel operation

Frame zero of each row with a visible face is the reference. The reviewed p5 family and nine roster designs have small manual calibrations in source pixels; later additions are recorded below. Glasses, white eyebrows and the lavender cat hood are ambiguous to a skin-color classifier. Unlisted designs use conservative warm-skin spans, enclosed dark-feature detection and the eye-highlight band. Their reports say `automatic-unreviewed`; inspect them visually after generation. Detection failure exits with an error and requests a reviewed profile. It does not mark the character complete.

For animation frames, search translations of ±7 native pixels by cross-correlating the head alpha/outline and hair colors above the eye boxes, so regenerated eyes do not drive alignment. Ties use a deterministic smallest-movement order. Reviewed placements handle differently pitched idle heads, the p5 sleep tilt and cap-teen's differently sized report heads, where hair geometry and facial geometry were generated inconsistently. Reviewed placements may exceed the automatic search range. No pixel scaling, rotation, interpolation or alpha blending occurs.

Tight reference eye/brow patches (including highlights and glasses) replace RGB pixels in the common opaque interior of each aligned head. Reference skin within these small patches supplies the background needed to erase older eye shapes; skin/shading outside them remains original. This deliberate small skin overwrite prevents residual eye pixels and patch holes. Outline/transparent corners are excluded and their count is reported. Original alpha and every pixel outside the recorded face patch remain byte-identical. A few hair pixels touching an eyebrow/eyelid may belong to the patch; the rest of the hair and all body motion are retained.

The three walking rows lock per facing, and visible idle eyes reuse their walking reference at reviewed placements. A true right profile can hide the far eye present in a three-quarter walk: punk-girl's right idle locks the visible eye only. Activities use their own row references because their heads differ in pitch, size and view. `talk` locks the eyes/brows while preserving original mouth articulation. `surprised` retains its separately authored wide-eye reference; `sleep` retains closed eyes. The p5 sleep lids are drawn once as thin deterministic lines over the union of the old lid footprints to remove lower duplicate lids in the tilted frame. `coffee`, `phone` and `typing_side` lock the visible eyes per row. Coffee keeps the mouth/cup occlusion original so the mug animation survives. Non-talking mouth patches are locked where a distinct mouth exists and the calibration covers it; idle mouth placement and cap-teen's report mouth remain authored because the head pitches/sizes do not share a compatible mouth footprint. `walk_up`, `typing_back`, and the up-facing idle slot are copied unchanged.

Blink exports replace eye interiors with nearby skin and draw one native-pixel closed lid, preserving eyebrows/glasses as calibrated. These are optional authoring assets; runtime playback is not changed. See the additive field definition in [asset-format.md](asset-format.md#optional-production-blink-metadata).

## QA and limits

`tools/facelock_qa.py` reloads original and output PNGs. For each row it prints the largest pairwise RGB-channel difference (0–255) after integer head alignment, before and after, plus differing-pixel counts. It measures the entire common-opaque eye boxes, recomputed from the source PNGs, rather than trusting the processor's recorded eye-pixel mask. It also checks idle+walk facing agreement, unchanged alpha, unchanged pixels outside the face patch, unchanged back rows, preserved manifest layout/timing, matching curated exports and non-stale source hashes. The asymmetric synthetic self-check verifies translation, preserved body pixels, and that an intentionally corrupted output yields a nonzero score.

Before/after strips, head strips, blink strips and `report.json` are written to `%TEMP%\facelock-qa\`, at exact 6× nearest-neighbor. Limit inspection with repeatable `--character ID`; use `--out PATH` to change the QA directory. Numeric zero proves equality in the stated region, not that a classifier found every facial feature or that motion looks natural. Silhouette corners, hair outside the patch, nose/blush/skin shading, mouth articulation and prop occlusion are not part of the eye score. View the strips to catch missed eye pixels and incorrect placement, especially for newly completed, automatically detected designs.

Face lock fixes regenerated eyes. It leaves inconsistent head sizes, head pitch, anatomy, gait, hand contacts, clothing and row-to-row scale for separate work. Large shape changes need a reviewed calibration or better source art. These checks do not establish runtime animation or loop continuity.

## Reviewed output snapshot (2026-10-04)

The first review processed 36 p5 variants, their three worker aliases and seven complete roster characters, totaling 46 publications. Five incomplete runs were skipped. QA passed 460 animated eye rows and 138 idle/walk facing groups (598 groups), all with an after difference of 0. Preservation checks passed for alpha, back rows, timing/layout and pixels outside the patches. The review exercised the frames-only CLI, corruption-detecting synthetic check and identical-input rerun. The all-input rerun preserved bytes and timestamps of 578 output files. Later blink corrections were checked on the affected character and rerun identically.

| Visually reviewed character | Maximum animated-row eye RGB difference before | After |
| --- | ---: | ---: |
| dev-hoodie | 236 | 0 |
| glasses-bun | 233 | 0 |
| professor | 232 | 0 |
| punk-girl | 239 | 0 |
| codex-worker-01 | 199 | 0 |
| claude-worker-02 | 204 | 0 |
| antigravity-worker-03 | 112 | 0 |

Viewed before/after 6× nearest strips for those seven designs, including walking, idle and blink, plus all face-bearing activity rows for the four named roster characters. Reviewed cap-teen after it completed during this pass; added native boxes/placements and corrected blink remnants. Iterations corrected misplaced idle eyes, p5 sleep double lids and leftover blink highlights. Remaining limits include original head-size/pitch changes, authored idle mouths, cap-teen's report mouth and coffee mouth/cup occlusion; the score excludes those. Newly completed unlisted characters need visual review of their automatic masks. The report/strips under `%TEMP%\facelock-qa\` reflect the last QA snapshot, not future art-team changes.

## Cat-hood and manager-suit follow-up (2026-10-04)

Both characters now select built-in reviewed profiles by roster directory name. Cat-hood's lavender hood defeated the warm-skin detector; its profile supplies native eye/brow and mouth boxes for all ten face-bearing rows. Manager-suit's automatic boxes included side hair/ears and misplaced idle eyes, so it also uses a reviewed profile. The additions include per-facing idle placements and sleep placements. Cat-hood's right idle uses only the visible eye; the far eye in its three-quarter right walk stays hidden in the true-profile idle. Cat-hood's phone mouth is occluded and remains authored, as do talk articulation and coffee mouth/cup pixels.

Run only these two publications (manager-suit retains its existing blink exports):

```powershell
& C:\Users\myori\.codex\skills\sprite-gen\.venv\Scripts\python.exe tools/facelock.py --manifest assets/art/roster/cat-hood/cat-hood.manifest.json --sheet assets/art/roster/cat-hood/cat-hood.png
& C:\Users\myori\.codex\skills\sprite-gen\.venv\Scripts\python.exe tools/facelock.py --manifest assets/art/roster/manager-suit/manager-suit.manifest.json --sheet assets/art/roster/manager-suit/manager-suit.png --blink
& C:\Users\myori\.codex\skills\sprite-gen\.venv\Scripts\python.exe tools/facelock_qa.py --all --self-check
```

No external `--profile` file is needed for these named calibrations. An external profile replaces the eye/mouth rows and is labelled `custom`; named idle/sleep placement calibrations are not applied to that override.

| Reviewed character | walk_down | walk_left | walk_right | talk | Maximum animated row | Maximum idle/walk group |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| cat-hood | 192 → 0 | 199 → 0 | 191 → 0 | 209 → 0 | 240 → 0 | 243 → 0 |
| manager-suit | 232 → 0 | 232 → 0 | 231 → 0 | 232 → 0 | 232 → 0 | 232 → 0 |

Values are the QA's maximum aligned RGB-channel difference over common-opaque eye boxes, measured from the original source to the final reviewed output. Viewed eight images total, including both characters' four-frame walking strips, idle and talk, calibration references, and before/after activity strips. Iteration enlarged incomplete eye/lid footprints and corrected idle placements without touching source art. The custom final strips are exact 5× nearest-neighbor at `%TEMP%\facelock-qa\cat-hood-final-5x.png`, `manager-suit-final-5x.png` and `activities-final-5x.png`; the existing QA CLI still emits its standard 6× strips. Original ear/hood motion and head geometry remain authored, with alpha and all pixels outside the declared patches preserved.

The full follow-up QA and corruption-detecting self-check passed: 49 publications, 490 animated eye rows + 147 idle/walk groups = 637 groups, all after differences 0, with no failures. Apron-baker and afro-sweater were not processed or edited. This snapshot does not mark other automatically detected designs visually reviewed.

## Afro-sweater redo (2026-10-04)

The redone orange-afro character now has an additive built-in `afro-sweater` profile. Its glasses defeat skin-enclosed-hole detection. Ten native eye/brow/glasses boxes and optional mouth boxes replace the automatic detector for this name. These coordinates apply to the new kcentroid source, not the archived old face or rejected pixel-unfake output. Existing characters and the processing algorithm are unchanged.

Reviewed idle placements are down `[0,3]`, left `[-4,-3]`, right `[8,-3]`. The true-profile right idle exposes one lens, so its eye box is restricted to the visible near lens of the three-quarter right walk. The manual right placement exceeds the automatic ±7 search range. Talk mouth articulation, coffee mouth/cup occlusion and phone mouth pixels remain authored; sleep uses its closed-eye reference.

Run against the stable unlocked production run, then check all available locked outputs while writing QA artifacts inside this character's boundary:

```powershell
& C:\Users\myori\.codex\skills\sprite-gen\.venv\Scripts\python.exe tools/facelock.py --manifest assets/art/roster/afro-sweater/run/manifest.json --sheet assets/art/roster/afro-sweater/run/sprite-sheet-alpha.png
& C:\Users\myori\.codex\skills\sprite-gen\.venv\Scripts\python.exe tools/facelock_qa.py --all --self-check --out assets/art/roster/afro-sweater/redo/facelock-qa-all
```

The delivered `afro-sweater.png` copies the locked pixels; `run/` remains the unlocked source so hashes and before/after QA stay valid. The delivery manifest retains the exact original layout/timing and points its additive face-lock report to `locked/afro-sweater.facelock.json`. No pack or runtime adoption was performed.

Final full QA/self-check: 52 publications, 520 animated eye rows + 156 idle/walk groups = 676 groups, no failures and all after differences zero. Afro-sweater's maximum animated-row difference was 224 → 0; walk_down 218 → 0, walk_left 185 → 0, walk_right 207 → 0. Viewed its walk_down/walk_left/idle before/after strips and every final cell in compact boards, plus the final 3× nearest-neighbor contact sheet. The report row was regenerated again to reduce per-frame scale changes, then face-lock and full QA were rerun.

Small side lenses and the report's far-eye sparkle remain weaker than dev-hoodie's, and finger gestures are subtle. Integer face locking does not improve those details or certify gait continuity. The final walk GIFs were exported, but playback was not observed. The numeric pass does not establish that every direction meets the requested art quality.
