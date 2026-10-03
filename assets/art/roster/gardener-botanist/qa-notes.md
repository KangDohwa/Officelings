## Current cleanup delivery ? 2026-10-04

**PASS: facelock, blink, seated pixel checks, and cyclic frame/seam review.** This does not certify real-time playback or polished anatomy in every cell.

Generated one corrected eye reference with the built-in image_gen tool and extracted 15 small visible-eye patches into nine frame-zero action references. Retained a full native catchlight during three-column extraction. Report has only one visible eye: the far eye is occluded by hair, so no invented far-eye patch is added. Applied reviewed native eye/brow boxes, idle placements, blink interiors and seated calibration.

Load `gardener-botanist.png` + `gardener-botanist.manifest.json` for the flat 13-row delivery. The root PNG equals the currently committed locked PNG. Follow `locked/gardener-botanist.manifest.json` for generation paths; older root files in `locked/` are historical. The immutable corrected source is `cleanup/source/`, its local reviewed calibration is `cleanup/profile.json`, and the pre-cleanup delivery is `cleanup/before.*`. The original `run/` is preserved.

New seated frames are committed through **`seated/gardener-botanist.manifest.json`** inside this character folder: 12 normal frames in three appended rows, plus eight phase-matched side blink frames. Follow that manifest to its `.generations/` sheet and blink files. The top 42 cells are byte-identical to the current locked sheet; this local output has not been copied to shared `assets/art/seated/` or adopted by the pack.

QA: `cleanup/facelock-qa/report.json` has 13 groups, after=0, failures=0. `cleanup/validation.json` records independent reload checks: 42 nonempty flat cells, 12 seated cells, 23 opaque colors, binary alpha, unchanged silhouette/layout/timing, zero transparent RGB, empty trailing cells, safe margins, baseline y=60, source freshness and eye-only blink changes. Seated heads are preserved through the recorded one-pixel nod and lower bodies are identical across phases. Shared facelock/QA/seated code and QA thresholds were not edited.

Walks: all four full cycles and the explicit **2?3?0?1** seam strips were viewed in `cleanup/walk-seams.png`. No extra wrap jump or baseline reset was identified; native changed-pixel counts at 3?0 are down: 562, left: 547, right: 433, up: 303. These counts supplement visual inspection and do not score gait quality. `cleanup/walk-metrics.json` binds the results to frame hashes, while the four `cleanup/walk_*.gif` files preserve order and timing. **Real-time/browser/runtime playback remains UNVERIFIED:** browser inventory returned apps=[], browsers=[], and the in-app browser was unavailable. A self-contained replay page for all 16 loops is retained at `../chef-sous/cleanup/loops.html`.

Remaining weak: Side catchlights are still only one native pixel. Down/left contacts are subtle, small hat/apron shading changes remain, and report appears smaller. Seated apron/forearm shading is simplified. Native eye pixels being consistent does not certify the full original face-quality bar in every pose.

Reproduce with the existing sprite-gen Python environment and the character-local driver `assets/art/roster/chef-sous/cleanup/cleanup.py gardener-botanist <stage>`; stages are `source`, `lock`, `qa`, `seated`, `review`, `deliver`. It invokes the existing tools' processing/check functions with local calibrations in memory and redirects seated publication here. QA invokes `facelock_qa.check` for this committed report because the stock CLI scanned an unrelated locked report before filtering and hit PermissionError; the original checker and its assertions are unchanged. Corruption-detecting self-check also passed.

No build_pack, pack mutation, runtime change, commit or push. Image generation was serial (chef face, then gardener face); generated references and prompts are local to those two cleanup folders.

---

**Historical generation/extraction notes below describe the pre-cleanup snapshot and are superseded for current face-lock, blink, seated and seam status.**

# Visual QA ? gardener-botanist (wave 4)

One original character. All delivered files and retained run evidence are inside this character folder. Reviewed A/B contacts and apron-baker/C-contact once at low resolution, plus dev-hoodie and the pixel-agents reference. The wide flower straw hat, long single braid, pear-shaped green tool apron, rolled cuffs, rubber boots and idle plant form a distinct silhouette.

## Generation and review

sprite-gen 2.20.0, explicit provider codex, still-image component rows, concurrency 1. No video; no facelock; no local pose or face painting. Viewed the base, every generated source row and every retry through downscaled composites, then all 42 final cells through two 3x nearest-neighbor review pages. Duplicate .raw.png files contain the same generated content. Total image views were kept to 12, including the final contact sheet.

Initial rows: idle, four walks, typing_back/side, report, sleep, coffee, phone, talk, surprised. Rejected the first four walks for repetitive contacts / weak passing poses, the rightward walk for frontward orientation, and the back walk for missing crossed apron straps. Rejected first typing_back for shoulder-height hands. First correction attached base-source.png (identity), raw/idle.png (four-direction orientation reference), and the relevant layout guide. Back typing used raw/typing_side.png as LOW waist/lap-height seated-posture reference instead of idle. These extra references are recorded in reports/corrections/.

The first corrected walk_right faced LEFT and was rejected. Final retry changed the reference strategy: frames/idle/orig/frame-2.png is the sole RIGHT-facing character anchor, with only the walk_right layout guide added. Final nose/gaze/feet face right, with the braid at the rear of the torso. Rejected originals and reports remain under run/qa/rejected/.

## Extraction and export

Canonical component extraction only; no fixed-slot cutting used to generate frames. Pure magenta #FF00FF removed; safe margins 1 horizontal / 4 vertical. Pixel-unfake stays enabled. Default dark detail bias erased fine face information, so detail_bias=false was selected. A later canonical nearest-only idle test did not restore both eye highlights and introduced soft alpha; it was rejected. No face patching was used to hide this failure.

Extractor reported pitch disagreements and physical-size caps because several generated strips have more fine detail than the runtime cell can preserve. The warnings remain in frames/frames-manifest.json. Standard non-destructive curation transforms use one scale per row, nearest-neighbor sampling, small horizontal padding corrections and bottom y=60. Frame order, poses, timings and raw art are unchanged. Final runtime export uses sprite-gen build_shared_palette/apply_palette after the two extraction batches: 63 source colors -> 23 used final colors within the requested 32-color limit. Alpha is byte-identical through palette normalization. Canonical run atlas retains its pre-export palette; load the root gardener-botanist.png with the root manifest.

## Verified geometry

128x832 RGBA sheet, 32x64 cells, four columns, 13 rows, 42 nonempty frames. Row names/order/counts/FPS match worker-request.json and beanie-scarf. Idle is down/left/right/up, selected statically. All declared rectangles match their manifest; unused cells are empty. Binary alpha; all content within x=1..30 and y=4..59, bottom boundary y=60; zero visible magenta pixels. Details: run/qa/validation.json. Contact frames are exact 3x nearest-neighbor. No app build/test was run for this asset-only task.

## Honest limitations

**Face bar is not fully met at runtime size:** the source has large expressive eyes and white catchlights, but reduction to 32x64 weakens or loses a highlight in some front/side frames. Do not claim every final eye has a clearly visible white highlight. Hairline and face remain visible below the hat. Supervisor facelock was explicitly deferred and was not run.

Walking is **experimental / continuity unverified**. Contacts and narrow passing poses now differ; opposite-leg contacts are still subtle, especially down/left. Small one-pixel hat/face/body changes remain. GIFs were exported but runtime playback and loop seams were not watched. Back typing has low hands now, with very subtle finger motion. Report handoff reads held -> extended -> offered, but appears slightly smaller than other standing rows because the extended arm must fit the same cell. Sleep is seated with intentionally closed eyes. Coffee lifts/lowers an attached mug; phone scrolling and talk gestures read; surprised raises hands to cheeks. No floating ! is baked in: use the worker convention's separate overlay.
