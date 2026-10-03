# Visual QA — hanbok-modern

Viewed the available A/B/C contact references and pixel-agents quality reference once in a low-resolution overview. Reviewed the base, every generated row, the regenerated walk-down row and every final cell through 12 composites total. `run/qa/final-all.png` pairs all latest raw strips with the final 42 cells. Duplicate `.raw.png` audit twins contain the same generation content.

Identity: large chibi head, visible forehead/hairline, chestnut-black half-up hair with loose lower hair and small crown knot, gold hairpin, coral hanbok-inspired diagonal jacket and tied ribbon, white shirt, wide taupe trousers, cream sneakers. Outfit and silhouette remain consistent. Side-view facial details and the very small hairpin are less clear than the front view. Closed eyes are intentional in sleep and the middle sipping frame; back views intentionally hide the face.

Rejected the first 48-pixel placement because eye highlights collapsed into dark eye blobs. Regenerated **walk_down** using three references: the old walk-down strip for pose, `base.png` for identity and its generated layout guide for spacing. `run/reports/walk_down-eye-redraw.json` records this call; `run/qa/rejected-small-eyes/` preserves the old strip. The redraw alone did not fix the extraction loss. Final extraction uses `fit.detail_bias: false`, a shared 32-color palette with **#FFFFFF explicitly reserved**, and standard curation placement at up to 54 pixels high (seated target 52, subject to width). `palette-notes.json` records the replaced near-duplicate shade. These are extraction/palette/placement changes; facelock was not run.

| State | Static visual verdict |
|---|---|
| idle | Four anchors in down / left / right / up order; small view-dependent face/scale changes. |
| walk_down | Alternating foot and arm phases; broad highlighted eyes are the clearest facial row. |
| walk_left | Clear leftward gait phases; tiny side-eye and shoe differences. |
| walk_right | Clear rightward gait phases; slight head/face pixel variation. |
| walk_up | Back of half-up hair and alternating shoe contacts; face hidden. |
| typing_back | Seated crouch, low forward arms mostly occluded; hand motion very subtle. |
| typing_side | Bent knees and low forward hands; tiny finger motion, mild head variation. |
| report | Paper held close, extended, then offered; paper touches hand. Native face details are small. |
| sleep | Two seated head/breathing poses with closed eyes. |
| coffee | Mug held / raised to mouth / lowered; middle eyes closed intentionally. |
| phone | Attached phone and changed thumb pose; native finger/screen changes are subtle. |
| talk | Small hand and mouth changes; minor face/hair changes remain. |
| surprised | Body reaction followed by hands beside cheeks; `!` stays a separate overlay. |

Geometry checks passed: 32×64 cells, 128×832 sheet, canonical 13-row ordering, 42 nonempty frames, expected FPS/loops, binary straight alpha with zero transparent RGB, clear trailing cells, no visible magenta, safe margins and y=60 baseline. All rows have the declared number of distinct frame hashes. Hash differences are not proof of motion quality.

The extractor reports pitch-detection disagreements and native-size caps; these are retained in `run/frames/frames-manifest.json`. Final placement removes edge contact but does not eliminate all sampling variation. Standing bodies are taller than worker's typical 41–47 pixels to retain eye detail. GIFs are exported for review, but live playback has not been watched: walk continuity and loop seams remain **experimental/unverified**. No video generation, facelock, source-code changes or git commit.
