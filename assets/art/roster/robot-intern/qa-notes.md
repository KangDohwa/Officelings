# Visual QA — robot-intern

Read SPEC.md, worker-request.json, shared art manifest/generation notes and beanie-scarf's request, README, QA and manifest conventions. Reviewed A/B contacts once at low resolution and the available C contacts in afro-sweater/apron-baker; the root C contact was absent. Reviewed the pixel-agents style reference in the same composite. The orange robot's square head, antenna, mechanical joints and visor distinguish it from the human roster.

All **21 generated images** were viewed: base, 13 initial strips and seven correction strips. Review used **12 downscaled composites/images total**, including references and the rejected/accepted final atlas reviews. Duplicate raw/output content was not opened separately. Source identity remains orange head/panels, cream antenna tip and grippers, teal scarf and a single dark visor containing highlighted cyan eye shapes. Profile views expose one eye; back views intentionally hide the face; sleep intentionally closes the eyes. No facelock, video generation, pose painting, mirroring, clones or timing changes.

## Corrections

- Accepted `idle-v2`: changed only the back head vents from two vertical slots to horizontal bars, matching walk_up and typing_back. Initial idle retained in `run/qa/rejected/idle-v1.png`.
- Left/right v2 edits did not make opposite foot depth sufficiently clear. Kept them as rejected evidence under `run/qa/corrections/`.
- Fresh right v3 improves contact/passing differences and preserves the rounded head/cream antenna. Accepted as raw/walk_right.png. Fresh left v3 changed the head to a tall cuboid and antenna tip to orange; rejected for identity drift.
- Left v4 used own right v3 as an extra motion reference while editing the original row; contact depth remained weak. Rejected.
- Accepted left v5 uses **own canonically extracted idle/orig/frame-1.png** as the dedicated left-facing identity anchor, **own right v3** as motion reference, and the four-slot layout guide. It preserves the rounded head/circular ear/cream antenna, uses full profile, and alternates arm and step poses. Foot-depth order is still subtle; this is an experimental gait, not a certified seamless loop. The right walk remains a three-quarter view.
- Existing worker gait cells were inspected locally as possible motion references. Automatic approval review rejected sending them to the Codex generation service because that particular export was not explicitly authorized. No worker motion image was sent. Fresh retries used only this robot's generated references; the individual reports record the actual refs. Local motion-reference files are retained as review evidence, not successful generation inputs.

## Extraction and fitting

Rejected pixel-unfake output: grid/pitch warnings, squashed walk_down head and uneven action proportions. Preserved sheet, manifest, report, curation and frame-manifest under `run/qa/rejected/pixel-unfake-*`.

Re-extracted every accepted raw strip with canonical component extraction, `fit.pixel_unfake=false`, `fit.resample=kcentroid`. It returned zero errors and four native edge warnings (walk_down frame 2; walk_up frames 0,1,3). Final fitting corrects the delivered cell margins; source edge warnings remain documented and are not presented as absent.

After canonical composition, sprite-gen library functions `binarize_alpha` (128 threshold), `apply_transform(snap_scale=1)` and `build_shared_palette`/`apply_palette` place the extracted cells on y=60 with nearest sampling and a shared 32-entry palette (31 colors used). Exact inputs, rectangles and transforms are in `run/qa/final-fit.json`. Both pixel-grid and kcentroid output were viewed across all 42 cells. After that review, report frames 0/1 were numerically normalized to the last frame's **39 px** height; this final uniform-scale adjustment was checked for geometry/alpha, without opening another image to exceed the review budget. The other 39 cells and report frame 2 are unchanged. This removes within-row size change but leaves report smaller than other actions (46 px).

## Static motion verdicts

| State | Observed result / limitation |
|---|---|
| idle | Down/left/right/up static anchors; single-eye profiles and matching horizontal back vents. |
| walk_down | Alternating forward foot contacts and grippers; big highlighted visor retained after refit. |
| walk_left | Accepted profile v5; arm/step alternation visible, leg depth subtle; experimental. |
| walk_right | Accepted three-quarter v3; contact/passing differences, minor joint/contour drift; experimental. |
| walk_up | Alternating feet, no face; small torso/foot variation; experimental. |
| typing_back | Seated pose, low hands at lap/waist; finger movement very subtle. |
| typing_side | Bent legs and waist-height forearms; tiny gripper changes; side ear grille differs from idle. |
| report | Held/extended/offered attached paper; uniform 39 px height, smaller than other rows. |
| sleep | Closed cyan eye slits, slumped seated head and two breathing poses. |
| coffee | Attached mug held/raised/lowered to mouth port beneath visor. |
| phone | Attached phone, changing gripper/thumb positions; small head-panel differences. |
| talk | Hand raises/opens/lowers, mouth changes; highlighted eyes. |
| surprised | Wider visor eyes and raised hands beside cheeks; no baked punctuation. |

GIFs in `run/previews/` use exact final exported cells at nominal FPS (GIF centisecond rounding only). They have not been watched in real-time; loop seams and continuous locomotion remain **unverified**. Exporting a GIF is not a motion pass.

`run/qa/validation.json` checks 128×832 RGBA, 32×64 cells, exact row order/counts/rectangles/FPS/loop flags, 42 nonempty cells, within-state uniqueness, binary alpha, safe margins, bottoms at y=60, transparent unused cells and absence of visible magenta. `wave2-contact.png` is the labeled 3× nearest idle/walk review sheet.

Project artifact writes are restricted to robot-intern. No application build/test, commit, upload of deliverables or facelock was performed.

Workflow choices: codex / GPT still-image rows / curation skip. Start returned ready in the real user environment. Finish was called with the delivered PNG and returned only the save-defaults question; self-selected **no** under the user's instruction to answer skill questions, so no preferences were written. All five required deliverables exist, final/runtime-copy sheet hashes match, and 12 final-cell GIFs were exported.
