# Visual QA — cyber-courier

Read SPEC.md, worker-request.json, assets/art/MANIFEST.md, beanie-scarf's layout/QA and roster descriptions. Glanced once at A/B/C contact references at low resolution; inspected pixel-agents and dev-hoodie eye references. Reviewed the base, every generated original row, every reroll and both targeted left-walk edits through downscaled composites. All 42 native output cells were viewed at 3× nearest-neighbor. Generation always used **codex**, one image at a time. No video, pose painting, cloned game frames, frame reordering, timing changes or facelock.

## Generation and correction evidence

- Initial 13 rows were complete, but native extraction reduced white eye highlights, side contacts repeated, and back typing raised hands.
- Regenerated idle, walk_down/left/right, typing_back/side, report, coffee, phone, talk and surprised with stronger runtime eye instructions. Sleep and walk_up kept their original source rows. Back typing now keeps fingers low beside the waist rather than beside the head.
- Left walking received two further attempts. `walk_left-targeted-correction.txt` used the previous left strip as edit target plus `raw/walk_right.png` as a timing/leg-depth reference. `walk_left-visual-gait.txt` used `references/left-leg-phases-from-right.png`: a mirrored **lower-body-only** derivation of generated right-walk cells. That derived image is a motion guide; no mirrored sprite was shipped. Extra references are recorded in the generation reports. Previous left edit inputs remain in `qa/rejected/`.
- The left ear device is hidden to match accepted left idle; front/right/back retain the device. The left contact-depth problem improved less than requested: poses 0 and 2 still favor similar foreground-leg contacts. This row is not certified as a clean four-phase gait.
- Canonical sprite-gen chroma/component extraction and pixel-unfake were retained. An idle-only kcentroid comparison did not solve face readability and was not shipped. `curation.json` applies row-uniform scale and per-frame bottom alignment, preserving **y=60** without modifying source poses. A rounding-related one-pixel coffee baseline offset was corrected through the same standard transform.
- The 32-color `palette.lock.json` remained pinned throughout. The final 60 visible colors include additional selective-outline tones; they are not two independent base palettes. Pitch/harmonic/physical-cap warnings remain in extraction logs and are not claimed resolved.

## Static verdicts and limits

| State | Reviewed result / remaining limit |
|---|---|
| idle | Four correct direction columns; angular teal bob and coat preserved. Native highlights are small, especially in profiles. Back view intentionally hides the face. |
| walk_down | Alternating raised/planted shoes and arm positions; small face/pixel variation remains. Experimental, playback unverified. |
| walk_left | Correct left facing and hidden ear device; stride/passing differences exist, but 0/2 foreground contacts remain too similar after four total takes. Experimental, playback unverified. |
| walk_right | More distinct contact/depth and passing positions than the first take. Experimental, playback unverified. |
| walk_up | Back hair, hood down and alternating visible shoes; only modest phase variety. Experimental, playback unverified. |
| typing_back | Seated back view with low hands, no furniture. Fingers mostly occluded and changes are very small. |
| typing_side | Bent knees and forward forearms; no furniture. Finger variation is subtle; profile highlight is tiny. |
| report | Paper held close, extended and offered; connected to the hand. The extended paper makes this row smaller than ordinary standing rows. |
| sleep | Seated, shut eyes and slight head/breathing variation. No detached sleep marks. Closed eyes are intentional. |
| coffee | Mug at chest, raised to mouth, lowered; attached to hands. Foot baseline corrected to 60. |
| phone | Navy handset in both hands; tiny thumb changes may disappear at native size. |
| talk | Relaxed, raised-hand and lowered/open-hand gestures; small expression differences. |
| surprised | Wide eye shapes and raised hands beside cheeks; `!` belongs to the separate overlay. |

Native face quality remains weaker than the raw references in some frames: one-pixel catchlights and slightly changing profile shapes. Do not interpret the metadata checks as a face-quality or motion pass. **Facelock was not run; the supervisor owns that step.** GIFs were exported, but playback and loop seams were not watched, so seamless runtime motion is unverified.

## Structure verification

[validation.json](validation.json) passes: 32×64 cell / 128×832 sheet; exact beanie-scarf animation and frame-layout metadata; exact worker state order/counts/FPS/loops; 42 nonempty declared cells and 10 empty trailing cells; binary alpha; zero visible chroma pixels; horizontal/vertical margins and all foot baselines at 60; canonical-sheet and individual curated-cell pixel equality; all 20 idle/walk contact cells at exact 3× nearest-neighbor. No app build/test was needed or run for this asset-only task.
