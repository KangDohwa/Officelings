# Visual QA — hijab-designer

Reviewed the existing A/B/C contacts once at low resolution, remaining available C-character sheets/bases, and the pixel-agents reference. Some C sheets were unfinished; existing files were used without changing them. Reviewed the accepted base, all 13 generated strips in three downscaled composite pages, the rejected/accepted back-typing comparison, and all 42 final cells. Twelve review images total. Duplicate raw/output PNGs carry identical generation content and were not opened separately. No facelock, video generation, pose painting, mirroring, frame replacement, or timing changes.

Identity: rounded pastel mint hijab covers the hair; warm brown face; cream cardigan with pockets; long dusty-blue skirt; brown flats; single round tan tote. Idle carries the stylus tablet. Other actions stow it. The hijab and skirt make the silhouette different from the existing roster. The large eye shapes survive better in the final extraction, but **catchlights remain weak in some idle/side/action frames**; the final size does not uniformly preserve the expressive clarity of the raw references. The supervisor's planned facelock has not been run. Eyes intentionally close in sleep and during the coffee sip; back views intentionally hide the face.

## Corrections and fitting

- Rejected `typing_back` v1: hands were raised beside the shoulders. Saved the strip, prompt, and generation report under `run/qa/rejected/`. Regenerated with three references: `run/base-source.png` for identity, **own `run/raw/typing_side.png` for seated posture and low hand height**, and the generated three-slot back-typing layout guide. Accepted v2 places hands at waist/lap height. `run/qa/corrections/typing_back-v2.report.json` records the extra motion reference. Finger motion remains very subtle; the back pose is stylized.
- Rejected the first pixel-unfake atlas after viewing every cell: grid snapping/capping followed by scale normalization lost eye/face detail and produced broken-looking contours. Original extraction warnings and rejected sheet/curation are retained under `run/qa/rejected/pixel-unfake-*`.
- Re-extracted all raw strips using the **canonical component extractor**, `fit.pixel_unfake=false`, `fit.resample=kcentroid`, as also used by the existing labcoat-tall asset. Final extraction returned **zero errors and zero warnings**. This is a fitting change, not equal-slot cutting or a substitute generation pipeline.
- Final export uses sprite-gen's `binarize_alpha` (128 threshold), `apply_transform` with nearest sampling, and `build_shared_palette`/`apply_palette` for a shared 32-color palette. Integer placement pins every frame bottom to y=60. Report frames are normalized to a common 46 px height to remove within-row scale changes; other rows are 56 px high. **The report transition therefore changes apparent body scale, and this character uses more of the cell height than beanie-scarf.** Cell geometry, margins, row order, frame counts, and timing remain identical.

## Static per-state verdicts

| State | Viewed result / limitation |
|---|---|
| idle | Four orientations in down/left/right/up order; attached tablet/stylus and tote. Some face highlights are weak. Static selection only. |
| walk_down | Alternating visible shoes and arm positions; long skirt retained. Minor contour/face variation. |
| walk_left | Leftward stride and counter-swing visible; skirt and tote retained. |
| walk_right | Rightward stride and counter-swing visible; independently generated. |
| walk_up | Alternating shoes beneath the skirt, face hidden; small skirt-hem variation. |
| typing_back | Corrected low hands; three unique cells. Fingers and seated posture are stylized/subtle. |
| typing_side | Seated knees beneath long skirt, hands at waist; small finger changes. |
| report | Held/extended/offered cream paper remains attached. All three bodies have equal 46 px height; smaller than other rows. |
| sleep | Closed eyes, seated nap, small breathing variation. Tote contour varies slightly between frames. |
| coffee | Mug held/raised/lowered; sip closes eyes deliberately. |
| phone | Phone attached to hands; thumb positions differ. |
| talk | Hand raises/opens/lowers; mouth changes slightly. |
| surprised | Open mouth and hands raised beside cheeks; no baked punctuation. Separate `!` overlay follows worker convention. |

Walks remain **experimental**. Final-cell GIFs were exported at nominal state timing, but real-time playback and loop seams were **not reviewed**. Static frame differences do not certify continuous motion. `run/previews/` uses the final exported cells, including normalized report scale; `run/exports/pngs/` retains those exact cells. Idle has no GIF because its columns are facing selectors. GIF centisecond rounding is preview encoding only; manifest FPS/durations are unchanged.

## Machine verification

`run/qa/validation.json`: 128×832 RGBA sheet, 32×64 cells, 13 canonical rows, 42 nonempty frames, exact row counts/order/rectangles, binary alpha, safe margins, every bottom at y=60, fully transparent unused cells, no visible magenta, 32 opaque colors, palette application preserves the binary alpha, and every state has the requested number of distinct final cells. `wave2-contact.png` contains all idle and walk cells at exact 3× nearest-neighbor; those cells were also reviewed in `run/qa/review/final-all.png`.

Project artifact writes were restricted to this character directory. No application build/test, commit, upload, or facelock was performed.
