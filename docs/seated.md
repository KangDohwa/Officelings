# Seated typing prototype

`tools/seated.py` derives seated poses from locked PNGs and writes `assets/art/seated/`. The production sheets, env2/env3, `src/`, `tools/build_pack.py` and `assets/pack/` stay unchanged. The art delivery described here used no image generation or git commit. The later pack/runtime integration is covered in the [art pipeline](art-pipeline.md) and [state contract](state-schema.md).

## Run and outputs

```powershell
& C:\Users\<user>\.codex\skills\sprite-gen\.venv\Scripts\python.exe -B tools/seated.py
# Optional native-pixel hip/seat height; default 52, accepted range 48..54.
& C:\Users\<user>\.codex\skills\sprite-gen\.venv\Scripts\python.exe -B tools/seated.py --seat-y 52 --character professor
```

Use the existing Pillow/NumPy environment. The script calls the read-only `blink_frame` helper in `tools/facelock.py`. Inputs need all 13 states, 42 nonempty 32×64 frames and a current face-lock provenance report. Missing, incomplete or stale sources are skipped; source bytes are checked again before publication. New silhouettes need a reviewed calibration in `PROFILES`/`ARMS`. They are not treated as p5 recolors. Identical output leaves timestamps unchanged.

The 2026-10-04 snapshot contains 50 publications / 600 new normal frames: eleven roster designs, all 36 p5 provider/role/appearance variants, and the three worker aliases. `apron-baker` acquired a complete locked sheet during verification and was added before delivery. `afro-sweater` and the newly present `hijab-designer` had no locked sheet and were skipped. This describes the snapshot, not future art-team completion.

At the recorded delivery, each `assets/art/seated/<character-id>/` contained the files below. The current shared publisher stores the logical file set in `.generations/<hash>/` and commits its paths through the root manifest. Follow that manifest rather than an older root PNG. Preview images remain separate review output. This publication change was read during the documentation update; the pixel-review results below belong to the recorded delivery.

- `<character-id>.png` and `.manifest.json`: RGBA 128×1024, four columns, sixteen rows. The original 128×832 top section is pixel-identical to the locked sheet.
- `blink-seated_left.png`, `blink-seated_right.png`: normal-sized 32×64 frame-zero closures.
- `seated-blink.png`: 128×128, four phase-matched closure frames per side; row 0 left, row 1 right. These preserve the seated body, hands and animation phase.
- Original blink files, when available, and `source.facelock.json`: retained source provenance. The copied report describes the original face lock; it is not a seated anatomy approval.
- `qa.json`: checks for the three new rows. The production manifest also records the exact input hashes and per-direction source/translation/head offsets.

## Additive production rows and runtime mapping

This extends the production `animation.rows` / `frame_layout.rows` convention from [MANIFEST.md](../assets/art/MANIFEST.md), not the runtime version-1 manifest. Original row names, frame rectangles, counts, timing, and pixels remain intact. Cell size stays 32×64, straight alpha, binary coverage, nearest-neighbor sampling, with the existing foot reference (16,60).

| New production row | Index | Facing | Frames | FPS / playback | Suggested adapter selection |
| --- | ---: | --- | ---: | --- | --- |
| `seated_back` | 13 | up / behind | 4 | 4, loop | `typing/up` |
| `seated_left` | 14 | left | 4 | 4, loop | `typing/left` |
| `seated_right` | 15 | right | 4 | 4, loop | `typing/right` |

Both side heads use their independently authored idle direction; no whole-character mirroring occurs. The back head/body comes from up idle, with lowered, tucked upper arms. Old typing/walking leg positions are never played in these loops. Below the hip, back-view legs are omitted for chair/desk occlusion. Side thighs, bent knees/calves and shoes are static pixel geometry using sampled source colors. Hanging side arms are removed and their vacated areas use adjacent torso pixels; two shortened bent arms extend toward the keyboard. This is a calibrated pixel prototype, with visibly simpler arm/leg shading than the source art.

Frames 1 and 3 alternate a one-pixel far/near hand tap. Back upper-arm/shoulder edges shift one pixel on alternating phases. Frame 2 moves the copied head down one pixel, then returns to the original position in frame 3. The pelvis and lower body never bob or animate. Eyes, glasses, facial pixels and the opaque head patch stay identical after undoing that integer head nod. No scaling, rotation, interpolation, or new generated face is involved.

The additive production fields are:

```json
"anchor": {"x":16,"y":60},
"seatAnchor": {"x":16,"y":52},
"seated": {
  "version": 1,
  "rows": {
    "seated_right": {
      "seatAnchor": {"x":16,"y":52},
      "keyboardAnchor": {"x":28,"y":43},
      "source": {"state":"idle","frame":2},
      "headOffsets": [{"x":0,"y":0},{"x":0,"y":0},{"x":0,"y":1},{"x":0,"y":0}]
    }
  }
}
```

`seatAnchor` is the native-pixel hip/seat contact reference at the bottom center of the cropped torso; like the foot reference, it can lie on the silhouette boundary. It is constant across all four frames. `--seat-y` translates the retained body so this reference meets the requested seat height. `keyboardAnchor` is the hand contact reference: back `(16,seatY-9)`, left `(4,seatY-9)`, right `(28,seatY-9)`.

For an adapter using the [asset-format.md](asset-format.md) runtime schema, select the three new rows instead of the old typing clips; do not emit two `typing/up` entries in the same sheet. For example:

```json
{"name":"typing","direction":"up","row":13,"frames":4,"fps":4,"loop":true,"seatAnchor":{"x":16,"y":52}}
{"name":"typing","direction":"left","row":14,"frames":4,"fps":4,"loop":true,"seatAnchor":{"x":16,"y":52}}
{"name":"typing","direction":"right","row":15,"frames":4,"fps":4,"loop":true,"seatAnchor":{"x":16,"y":52}}
```

These are separate illustrative clip objects. At this art delivery, `seatAnchor` was a proposed optional field. Version-1 consumers tolerate unknown fields but do not automatically use them. The delivery supplied art and an integration convention; runtime adoption was a separate step. The current builder and Core now carry the seated/seat-anchor choices through the [v5 contract](state-schema.md).

Alignment in a renderer that supports the proposed field:

```text
spriteTopLeft = chairSeatWorld - seatAnchor * scale
```

For an adapter using the existing world-pixel `footOffset` instead:

```text
footOffset = chairSeatWorld - characterFootWorld + (anchor - seatAnchor) * scale
```

Keep simulation/collision and depth at the existing foot/world point. Chair backs/fronts and desk fronts still need the appropriate furniture layers. If normalization is baked into source pixels, transform both anchors and the keyboard reference with the same integer placement/nearest-neighbor transform; do not reuse raw `(16,52)` after resizing. Check chair seat height and keyboard height together.

## Blink compatibility

Existing top-level `blink` metadata/files are preserved for the original rows. New closures live under `seated.blink.left/right`, using the same relative `file`, `source.state/frame`, and `durationMs:100` convention described in [asset-format.md](asset-format.md#optional-production-blink-metadata). `sheetRow` selects the matching row in `seated.blinkSheet` / `seated-blink.png`. An adapter can expose those four-phase rows as `blink-typing/left` and `blink-typing/right` at the same 4 fps. Choose the current typing phase during the closure; a single frame-zero file alone cannot follow a head nod in another phase. Back views have no blink.

Only changed eye pixels from locked walking blink exports are transferred through the recorded walking-to-idle integer offset, then through the seated translation/nod. The walking body is never substituted. `cat-hood` has no original blink exports, so the existing face-lock helper closes its calibrated walking eyes before transfer. `red-braid`'s original automatic mask produced a conspicuous full-face band; its new seated closures use reviewed tight idle eye interiors and source skin points. `apron-baker` also uses reviewed idle eye interiors to avoid its broad automatic mask. These corrections affect only the new seated closures. Original face-lock files are read-only.

## Visual review

The supplied `%TEMP%/typing-back.png` was inspected first. Then ten composite contact-sheet views were inspected, all at exact 5× nearest-neighbor, with read-only env2 chair/desk/monitor props (eleven image views including the supplied bug image). Iterations corrected standing remnants, hanging duplicate arms, sleeve palette samples, desk contact spacing, chair-front occlusion and the red-braid blink band. The last contact sheet covers the newly completed apron-baker. There was no isolated-sprite viewing or image generation.

Current review composites:

- [Roster seated preview](../assets/art/seated/preview-seated.png): all eleven roster designs, back/left/right and a second back phase.
- [Four-phase poses and blink pairs](../assets/art/seated/apron-baker/preview-motion-blink.png): all phases for dev-hoodie, professor and red-braid; left-eye closure pairs for all eleven roster designs plus a p5 sample.
- [Apron-baker source/pose/blink contact sheet](../assets/art/seated/apron-baker/preview-apron.png): source idle/typing poses, all new loop phases and both side closures.
- [p5 page 1](../assets/art/seated/antigravity-subagent-01/preview-variants-1.png), [page 2](../assets/art/seated/antigravity-subagent-01/preview-variants-2.png), [page 3](../assets/art/seated/antigravity-subagent-01/preview-variants-3.png): all 36 variants and three aliases. Page 1 also shows corrected red-braid left/right closures.
- Initial source composites remain under `assets/art/seated/dev-hoodie/source-contact-*.png` as review evidence.

This review used env2's front-view chairs/desks; authored env3 side furniture was still pending at that point. The side previews reposition unchanged props to judge sitting and hand contact. They do not approve perspective, depth or alignment against env3. Contact sheets show pose readability and frame differences. The art review did not include real-time playback or runtime integration.

| Character / class | Judgment from the composite sheets | Remaining limitation |
| --- | --- | --- |
| `dev-hoodie` | Convincing compact seated back and side silhouette; legs no longer suggest a run. | Large hair hides much of the back motion; side arm shading is flat. |
| `professor` | Convincing seated vest/white-sleeve silhouette and keyboard reach. | Forearms and bent knees are more angular than the original shading. |
| `beanie-scarf` | Sitting and typing read clearly; hat/scarf identity survives. | Adjacent-column torso repair leaves obvious horizontal seams in the side vest. |
| `apron-baker` | Broad seated plaid/apron silhouette and keyboard reach read clearly in all three directions. | Side apron folds and the plaid near the elbow become coarse horizontal bands. |
| `cap-teen` | Seated head/tee silhouette is readable with fixed knees. | Short-sleeve/hand separation and leg shading are coarse. |
| `cat-hood` | Hood/twin-tail seated silhouette works; side hands reach the keyboard. | Back hands are concealed by hair/hood; pale bent legs are blocky. |
| `manager-suit` | Suit and low horizontal arms read as seated office work. | Sleeve/body repair is visibly simplified, with limited wrist detail. |
| `punk-girl` | Seated jacket silhouette works; no boot gait is retained. | Bare thigh/boot geometry is especially angular; jacket detail is simplified near the elbow. |
| `red-braid` | Seated pose and corrected blink are usable; authored head facings and back braid handedness survive. | Side stripes/braid ends are partly simplified or covered by the reposed arms. |
| `glasses-bun` | Sitting reads, with preserved eyes/glasses. Weak costume result. | The long skirt becomes a crude rectangular lap/knee shape; folded skirt fabric needs authored art for a polished result. |
| `labcoat-tall` | Side posture reads as seated; back typing readability is weak. | Long hair hides almost all arm motion, and coat/lap/bare-leg transitions remain coarse. A tailored seated base would improve it. |
| p5 workers, all providers / 01–04 | Consistent usable seated prototypes across recolors. | Small torso and flat dark forearms simplify sleeve shading; native finger taps are subtle. |
| p5 supervisors, all providers / 01–04 | Same seated quality; source crown remains in each authored head. | Same arm/leg simplification as workers. |
| p5 subagents, all providers / 01–04 | Same seated quality; original badge/lanyard stays on the retained torso. | Horizontal arms partly cover the role marker in side views. |

These are seated prototypes. None is certified as a polished final animation. The two long-garment classes are the weakest. Their bases could have been redrawn, but were not; the limitations above remain.

## Pixel checks

The generator's assertions compare new heads against actual output frames after undoing the integer nod, verify exact lower-body equality across phases, binary alpha, nonempty cells and nonstatic loops, and verify all original 13 rows against the locked source. Every publication passed its three-row checks with aligned head RGBA difference 0 and lower-body difference 0. Eye-only blink edits are checked separately from normal frames. Input freshness is verified against the existing source hashes and a second byte read before writing.

An independent reload also compared output head pixels directly against the locked idle source, checked lower-body equality and absence of back feet, checked phase-matched blink bodies/alpha and the original top section, and recomputed input hashes. The single-p5 CLI path and seat heights 48/54 on four silhouettes were exercised. An identical-input all-source rerun preserved file bytes and timestamps. The final additional apron-baker publication received the same reload checks.

The checks establish pixel preservation and absence of animated legs. Anatomy, typing realism, fabric folds and live-loop smoothness still need visual judgment. The character assessments above came from composite art inspection, not the equality scores. This art review performed no app or pack build, renderer change or git commit.
