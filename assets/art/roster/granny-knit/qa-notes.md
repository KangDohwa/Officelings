## Current cleanup delivery ? 2026-10-04

**PASS: facelock, blink, seated pixel checks, and cyclic frame/seam review.** This does not certify real-time playback or polished anatomy in every cell.

Corrected automatic masks that selected hair instead of the right-facing eye and coffee eyes. Reviewed native eye boxes and tight blink interiors now include actual warm cheek/lens-adjacent skin; left and right closure extents remove the full iris and retain a single lid. This fixes blink has no nearby skin without changing skin detection or QA thresholds. Added a seated calibration and cream cloth sampling for the folded skirt instead of the orange flower accent.

Load `granny-knit.png` + `granny-knit.manifest.json` for the flat 13-row delivery. The root PNG equals the currently committed locked PNG. Follow `locked/granny-knit.manifest.json` for generation paths; older root files in `locked/` are historical. The immutable corrected source is `cleanup/source/`, its local reviewed calibration is `cleanup/profile.json`, and the pre-cleanup delivery is `cleanup/before.*`. The original `run/` is preserved.

New seated frames are committed through **`seated/granny-knit.manifest.json`** inside this character folder: 12 normal frames in three appended rows, plus eight phase-matched side blink frames. Follow that manifest to its `.generations/` sheet and blink files. The top 42 cells are byte-identical to the current locked sheet; this local output has not been copied to shared `assets/art/seated/` or adopted by the pack.

QA: `cleanup/facelock-qa/report.json` has 13 groups, after=0, failures=0. `cleanup/validation.json` records independent reload checks: 42 nonempty flat cells, 12 seated cells, 32 opaque colors, binary alpha, unchanged silhouette/layout/timing, zero transparent RGB, empty trailing cells, safe margins, baseline y=60, source freshness and eye-only blink changes. Seated heads are preserved through the recorded one-pixel nod and lower bodies are identical across phases. Shared facelock/QA/seated code and QA thresholds were not edited.

Walks: all four full cycles and the explicit **2?3?0?1** seam strips were viewed in `cleanup/walk-seams.png`. No extra wrap jump or baseline reset was identified; native changed-pixel counts at 3?0 are down: 387, left: 758, right: 757, up: 477. These counts supplement visual inspection and do not score gait quality. `cleanup/walk-metrics.json` binds the results to frame hashes, while the four `cleanup/walk_*.gif` files preserve order and timing. **Real-time/browser/runtime playback remains UNVERIFIED:** browser inventory returned apps=[], browsers=[], and the in-app browser was unavailable. A self-contained replay page for all 16 loops is retained at `../chef-sous/cleanup/loops.html`.

Remaining weak: Glasses rims/chain and native highlights remain less clear than the raw art. Skirt patterns flicker between authored phases and conceal leg identity. Seated skirt folds lose the floral pattern and use angular bent-leg geometry. Native eye pixels being consistent does not certify the full original face-quality bar in every pose.

Reproduce with the existing sprite-gen Python environment and the character-local driver `assets/art/roster/chef-sous/cleanup/cleanup.py granny-knit <stage>`; stages are `source`, `lock`, `qa`, `seated`, `review`, `deliver`. It invokes the existing tools' processing/check functions with local calibrations in memory and redirects seated publication here. QA invokes `facelock_qa.check` for this committed report because the stock CLI scanned an unrelated locked report before filtering and hit PermissionError; the original checker and its assertions are unchanged. Corruption-detecting self-check also passed.

No build_pack, pack mutation, runtime change, commit or push. Image generation was serial (chef face, then gardener face); generated references and prompts are local to those two cleanup folders.

---

**Historical generation/extraction notes below describe the pre-cleanup snapshot and are superseded for current face-lock, blink, seated and seam status.**

# Visual QA — granny-knit

One character only, roster wave 2. Reviewed A-contact and B-contact at low resolution, the four existing C character contacts, and the pixel-agents character reference. The gray bun with pencil, round glasses/chain, broad lilac shawl and flared floral dress distinguish this character from the 12 original silhouettes. The accepted source has a large head and expressive brown eyes with ivory highlights.

Every generated source image was viewed through downscaled composites: the base, all 13 initial rows, the three corrected rows, and the final right-walk pencil edit. Raw/published generation PNG pairs contain the same generation. Also reviewed all 42 extracted/fitted cells through composite pages and the final combined review. No raw large images were opened individually. The last report-only scale normalization was checked geometrically; it changes no source poses or colors.

## Rejected and regenerated

- **walk_left v1:** repeated contact silhouette/arm swing. Regenerated with accepted `base-source.png` for identity, own `raw/idle.png` as the additional direction reference, and the generated four-slot layout guide. The nearer arm now swaps front/back between the contact frames; passing shoes close under the skirt.
- **walk_right v1:** weak contact alternation. Same additional idle-strip reference and opposite-arm requirements. The correction introduced two pencil tips forming an unwanted V; rejected that version and regenerated only the pencil error. The last edit references the rejected corrected row for its accepted motion, the accepted base for the single pencil, and the layout guide. Final source has one pencil while retaining the corrected arm swing.
- **typing_back v1:** hands were at shoulder height. Regenerated with accepted base identity, own `raw/typing_side.png` as an additional seated/LOW-arm-height posture reference, and the three-slot layout guide. Hands now peek beside the lower shawl/dress seam and vary subtly.

Rejected strips and the earlier extraction/atlas attempts are retained under `run/qa/rejected/`. Extra reference paths and exact prompts are recorded in the corresponding correction reports and prompts. Generation was always serial, explicit provider **codex**, still-image rows. Two calls required sprite-gen's built-in timeout retry; both eventually generated files. No provider switch, video, mirrored directions, reordered frames, painted face edits, or facelock.

## Extraction and placement

Canonical pixel-unfake extraction produced unstable pitch observations and smeared small facial details. Rejected that atlas. Re-extracted the same accepted raw strips through canonical component extraction with `fit.pixel_unfake=false`, `fit.resample=kcentroid`. Applied existing sprite-gen `binarize_alpha`, `apply_transform(snap_scale=1)`, `build_shared_palette`, and `apply_palette` for hard pixels, safe placement, and one 32-color palette. No fixed-slot extraction or hand-painted sprite replacement was used.

An additional 46-pixel reduction harmed facial readability, so final fitting preserves the native extracted footprint up to 56 pixels inside the 32×64 cell. Most states are 56 pixels high, back typing is 55. Report initially fitted to 56/50/46 pixels as the arm/paper extended; normalized all three to **46** using nearest transforms, matching the existing roster's report normalization convention. Other 39 cells and the palette remained unchanged. Details are in `run/qa/final-fit.json` and `run/qa/validation.json`.

The phone source's last frame had tiny tap sparkles; canonical component extraction removed the detached marks. Furniture and the surprised **!** remain separate layers. Sleep/sipping deliberately close the eyes; up-facing rows deliberately hide the face.

## Static action verdicts

| State | Review |
|---|---|
| idle | Four static orientations: down, left, right, up. |
| walk_down | Alternating shoe contacts and arm differences read as short steps. |
| walk_left | Regenerated contact/pass phases and opposite near-arm positions; long skirt partly obscures leg identity. |
| walk_right | Corrected near-arm alternation and one pencil; skirt still limits foot separation. |
| walk_up | Alternating visible shoes below the dress; back shawl/bun stay recognizable. |
| typing_back | Seated back silhouette, hands low; finger motion is extremely subtle. |
| typing_side | Bent-knee seated pose, low forearms; small hand changes. |
| report | Held, extended, offered paper stays attached to the hand; normalized size is smaller than the other states. |
| sleep | Eyes closed, seated slumped posture; subtle breathing change. |
| coffee | Attached mug at chest, mouth, chest; sip pose closes the eyes. |
| phone | Phone stays in hands; tap/scroll difference is very small at native resolution. |
| talk | Relaxed, open palm, lowered hand; mouth changes are tiny. |
| surprised | O mouth, then hands at cheeks; no baked punctuation. |

These are **static visual judgements**, not proof of seamless animation. Browser inventory returned `apps: [], browsers: []`; real playback was unavailable. All four walk loops remain **experimental/unverified**, including their last-to-first seams. `run/qa/gifs/` contains 12 motion GIFs made from the actual final exported cells at the requested timings. Idle is facing selection, so it has no animation GIF.

## Final validation and remaining weaknesses

Passed: 128×832 RGBA sheet; 32×64 cells; exact 13-row order, names, frame counts, FPS and loop flags; 42 nonempty declared cells; every trailing cell empty; binary alpha; RGB zero under alpha zero; 32 opaque colors; no visible pure magenta pixels; x margins >=1 and y margins >=4; every frame bottom y=60. Every state's declared frame count is also distinct by full-cell hash; this does not certify intentional motion.

Final eye highlights, round glasses and chain are weaker than the raw generation, and vary between rows. I cannot certify the user's strict highlighted-eye quality threshold in **every** final cell. Tiny hair/flower pattern changes remain. Main states are larger than the original worker's typical 41–47-pixel footprint because native detail was retained; cell geometry and anchors remain interchangeable. Back typing and phone motion are subtle. Report has a smaller body footprint. Supervisor facelock remains pending as requested; none was run here.

Only `assets/art/roster/granny-knit/` was written. No shared contacts, source code, tests, application docs, other character folders, or git state were modified. Workflow finish was called with codex / gpt-rows / curation skip; the defaults question was answered **no** to honor the character-folder-only write scope, so no global preferences were saved.
