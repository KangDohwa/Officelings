# Visual QA ? manager-suit

Delivered: `manager-suit.png` / `manager-suit.manifest.json`, 32?64 cells, 4?13 / 128?832, all 42 frames. `README.md` lists the exact rows. `C-contact.png` is a labeled 3? nearest-neighbor review of all 20 idle/walk cells, kept here to respect the exclusive manager-suit write scope.

Viewed accepted base and pixel-agents quality reference together, all 13 existing raw rows in two small composites, initial extracted frames, each corrective generation in batched comparison composites, all 42 final runtime cells, and the final contact sheet. Eleven image views total, including one old contact image accidentally read after an export check failed; the definitive review is the newly composed `run/qa/all-review.png`. No large raw image was viewed individually. Duplicate `.png.raw.png` files preserve the same generation content. No video; one codex generation at a time; no facelock tool.

Identity: swept-back brown quiff, oversized chibi head, large brown eyes with small cream catchlights, fitted navy suit, white shirt, red tie, brown shoes, warm skin and dark selective outlines. Hair/outfit silhouette stays identifiable across rows. Back views intentionally hide the face; sleep and the sip pose intentionally close the eyes. Native downscaling simplifies highlights and fingers; catchlights are small and some eyelid/hair pixels still vary.

Reused nine interrupted-run rows / 28 canonical frames. Their pre/post subset-extraction SHA-256 hashes are identical (`run/qa/reused-frame-hashes.json`). The previously corrected coffee row was retained; the earlier coffee drift noted by the previous agent was already fixed in its surviving source.

Corrections: back typing originally held fists at shoulder height. Final generation used base-source.png for identity, raw/typing_side.png for the seated LOW-arm posture, and the three-slot layout guide. Fingers now peek around the lower waist/lap; the motion is deliberately tiny. Report originally had poorly legible paper: the replacement holds a cream document with writing marks at chest, then extends it, then offers it at fingertips. It remains attached in all three poses. First full left/right walk retries still had weak opposite contacts and were rejected. Targeted edits changed third-pose leg depth/diagonal and counter-swing; accepted worker/raw/walk_left.png and walk_right.png were extra MOTION references only. Manager strip/base retain identity. Rejected originals and first retries are in run/qa/rejected/resume/. Prompts and provider reports are retained. No pose painting, mirroring, cloned frames or timing repair.

Extraction/fit: source grids are finer than the runtime cell in several rows. Canonical sprite-gen extraction was retained; standard per-frame curation scale/dx/dy normalizes within-row height and snaps the foot boundary to y=60 with nearest-neighbor sampling. Standing targets 46px, seated targets 42px; width bounds can reduce these slightly. Partial extraction mixed old/new source palettes (47 composed colors); runtime output uses the same sprite-gen build_shared_palette/apply_palette method as beanie-scarf to fit the 32-color limit (21 actually used), with byte-identical alpha. Canonical run sheet remains provenance; root PNG is the runtime delivery.

Metadata: worker frame_layout is exactly equal. Animation metadata is exactly equal to beanie-scarf, including its static idle fps=0 / nominal 167ms. Original worker manifest instead assigns idle fps=1 / 1000ms; idle is facing selection in both. All animated row frame counts/fps/loop settings match worker. No action retiming was used.

| Row | Static visual verdict |
|---|---|
| idle | Four orientation anchors: down, left, right, up; outfit retained. |
| walk_down | Alternating foot contacts; small eyelid/head-pixel variation. |
| walk_left | Opposite contact depth and arm swing visible after third-pose correction; passing poses subtle. |
| walk_right | Third pose has changed leg depth and opposite arm swing; depth is still stylized. |
| walk_up | Alternating feet, no front face. |
| typing_back | Seated, hands at lower waist; alternating fingertips very subtle. |
| typing_side | Seated right/down-right, low hands; tiny finger movement. |
| report | Chest / extend / offer, attached cream paper visible in all three. |
| sleep | Two seated closed-eye head/breath poses. |
| coffee | Attached mug at chest / mouth / lowered; sip eyes close. |
| phone | Attached phone and small thumb changes; movement is subtle. |
| talk | Palm/arm gesture and mouth changes. |
| surprised | Wide-eyed reaction then hands beside cheeks; separate `!` overlay per worker. |

Motion continuity: **experimental / unverified**. GIFs exported under run/qa/gifs at the declared timing, but playback was not directly watched. Static contacts do not certify a seamless wrap. Back typing and phone are weak as motion and should be reviewed in the supervisor pass.

Checks: all 42 cells nonempty/in bounds, exact worker cell rectangles/order/counts, exact beanie animation metadata, safe margins, every foot boundary y=60, transparent unused cells, binary straight alpha, 21 opaque palette colors within the 32-color limit, no visible magenta residue. Canonical extract, compose-atlas, compose-gif and inspect completed successfully. Inspect returned no errors and retained source-grid/capped-size/intermediate-edge warnings for corrected rows; these refer to uncurated source frames, while final runtime margins were checked independently. Back typing motion presence was flagged 0.0080 < 0.0100; this warning remains, and no threshold was weakened. Full warning details: run/qa/inspection.json.

Workflow finish called with curation=skip. Answer to save_defaults is NO; no preferences saved. No app build/test or runtime playback performed. No commit or writes outside this character folder.
