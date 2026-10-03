# Vibedotcom generated office art

Load PNGs under `sheets/` with their sibling `.manifest.json`. All runtime files are RGBA with straight alpha; use nearest-neighbor sampling. Coordinates and row numbers are zero-based. Empty trailing cells stay transparent. `manifest.frame_layout.rows[state][frame]` is authoritative; do not infer cells from alpha.

This is a separate art bundle, not the existing `atlas.version == 1` contract in `docs/state-schema.md`. That contract uses 16×24 cells and a different row/column order. No renderer or state-schema code was changed. A developer must map action/facing to the rows below and scale world geometry to 32 px tiles.

## Runtime sheet inventory

| Sheet | Cell | Grid / pixels | Meaning |
|---|---|---|---|
| `sheets/accessories.png` | 32×64 | 1×2 / 32×128 | accessories rows below |
| `sheets/antigravity-subagent-01.png` | 32×64 | 4×13 / 128×832 | Character rows below |
| `sheets/antigravity-subagent-02.png` | 32×64 | 4×13 / 128×832 | Character rows below |
| `sheets/antigravity-subagent-03.png` | 32×64 | 4×13 / 128×832 | Character rows below |
| `sheets/antigravity-subagent-04.png` | 32×64 | 4×13 / 128×832 | Character rows below |
| `sheets/antigravity-supervisor-01.png` | 32×64 | 4×13 / 128×832 | Character rows below |
| `sheets/antigravity-supervisor-02.png` | 32×64 | 4×13 / 128×832 | Character rows below |
| `sheets/antigravity-supervisor-03.png` | 32×64 | 4×13 / 128×832 | Character rows below |
| `sheets/antigravity-supervisor-04.png` | 32×64 | 4×13 / 128×832 | Character rows below |
| `sheets/antigravity-worker-01.png` | 32×64 | 4×13 / 128×832 | Character rows below |
| `sheets/antigravity-worker-02.png` | 32×64 | 4×13 / 128×832 | Character rows below |
| `sheets/antigravity-worker-03.png` | 32×64 | 4×13 / 128×832 | Character rows below |
| `sheets/antigravity-worker-04.png` | 32×64 | 4×13 / 128×832 | Character rows below |
| `sheets/claude-subagent-01.png` | 32×64 | 4×13 / 128×832 | Character rows below |
| `sheets/claude-subagent-02.png` | 32×64 | 4×13 / 128×832 | Character rows below |
| `sheets/claude-subagent-03.png` | 32×64 | 4×13 / 128×832 | Character rows below |
| `sheets/claude-subagent-04.png` | 32×64 | 4×13 / 128×832 | Character rows below |
| `sheets/claude-supervisor-01.png` | 32×64 | 4×13 / 128×832 | Character rows below |
| `sheets/claude-supervisor-02.png` | 32×64 | 4×13 / 128×832 | Character rows below |
| `sheets/claude-supervisor-03.png` | 32×64 | 4×13 / 128×832 | Character rows below |
| `sheets/claude-supervisor-04.png` | 32×64 | 4×13 / 128×832 | Character rows below |
| `sheets/claude-worker-01.png` | 32×64 | 4×13 / 128×832 | Character rows below |
| `sheets/claude-worker-02.png` | 32×64 | 4×13 / 128×832 | Character rows below |
| `sheets/claude-worker-03.png` | 32×64 | 4×13 / 128×832 | Character rows below |
| `sheets/claude-worker-04.png` | 32×64 | 4×13 / 128×832 | Character rows below |
| `sheets/codex-subagent-01.png` | 32×64 | 4×13 / 128×832 | Character rows below |
| `sheets/codex-subagent-02.png` | 32×64 | 4×13 / 128×832 | Character rows below |
| `sheets/codex-subagent-03.png` | 32×64 | 4×13 / 128×832 | Character rows below |
| `sheets/codex-subagent-04.png` | 32×64 | 4×13 / 128×832 | Character rows below |
| `sheets/codex-supervisor-01.png` | 32×64 | 4×13 / 128×832 | Character rows below |
| `sheets/codex-supervisor-02.png` | 32×64 | 4×13 / 128×832 | Character rows below |
| `sheets/codex-supervisor-03.png` | 32×64 | 4×13 / 128×832 | Character rows below |
| `sheets/codex-supervisor-04.png` | 32×64 | 4×13 / 128×832 | Character rows below |
| `sheets/codex-worker-01.png` | 32×64 | 4×13 / 128×832 | Character rows below |
| `sheets/codex-worker-02.png` | 32×64 | 4×13 / 128×832 | Character rows below |
| `sheets/codex-worker-03.png` | 32×64 | 4×13 / 128×832 | Character rows below |
| `sheets/codex-worker-04.png` | 32×64 | 4×13 / 128×832 | Character rows below |
| `sheets/icons.png` | 32×32 | 6×3 / 192×96 | icons rows below |
| `sheets/props.png` | 64×64 | 3×10 / 192×640 | props rows below |
| `sheets/tiles.png` | 32×32 | 2×4 / 64×128 | tiles rows below |
| `sheets/worker-subagent.png` | 32×64 | 4×13 / 128×832 | Character rows below |
| `sheets/worker-supervisor.png` | 32×64 | 4×13 / 128×832 | Character rows below |
| `sheets/worker.png` | 32×64 | 4×13 / 128×832 | Character rows below |

`worker.png`, `worker-supervisor.png`, `worker-subagent.png` are convenient aliases of the Codex / combo 01 sheets. All 36 provider/role/appearance variants share the same cell geometry and animation ordering.

## Character rows (all character sheets)

Cell **32×64**, four columns, thirteen rows, **128×832**. Body silhouettes are mostly 21–28×41–47 px. Draw at native scale using foot anchor **(16,60)** in the cell. The four-pixel bottom margin is intentional; occupied walking tile remains 32×32. Idle row columns select facing and are not an animation.

| Row | State | Frames / columns | Suggested fps | Playback |
|---|---|---|---|---|
| 0 | `idle` | 0 down, 1 left, 2 right, 3 up | static | select facing |
| 1 | `walk_down` | 0–3 | 7 | loop |
| 2 | `walk_left` | 0–3 | 7 | loop |
| 3 | `walk_right` | 0–3 | 7 | loop |
| 4 | `walk_up` | 0–3 | 7 | loop |
| 5 | `typing_back` | 0–2 | 5 | loop |
| 6 | `typing_side` | 0–2 | 5 | loop |
| 7 | `report` | 0–2 | 5 | play once, hold final |
| 8 | `sleep` | 0–1 | 2 | loop |
| 9 | `coffee` | 0–2 | 3 | loop |
| 10 | `phone` | 0–2 | 3 | loop |
| 11 | `talk` | 0–2 | 4 | loop |
| 12 | `surprised` | 0–1 | 5 | play once, hold final |

`typing_back` faces up; `typing_side`, `report`, `coffee`, `phone`, and `talk` face right / down-right. Typing rows contain only the seated person: furniture is a separate layer. `sleep` is a seated, slumped nap suited to a sofa. `surprised` is the body reaction; put bubble frame 5 above it for `!`. Do not mirror whole sheets: choose the generated left/right walk rows. A provider color identifies the agent provider, not its model.

| Appearance suffix | Skin | Hair |
|---|---|---|
| 01 | warm tan | chestnut |
| 02 | fair | sandy blond |
| 03 | deep brown | soft black |
| 04 | olive / medium brown | auburn |

Provider names: `codex` = teal, `claude` = terracotta orange, `antigravity` = violet / blue. Role names: `worker`, `supervisor` (gold crown), `subagent` (navy lanyard / cream ID). Example: `sheets/antigravity-subagent-03.png`. Recolor reports under `sheets/` record exact RGB substitutions and confirm unchanged alpha. Hair and shoes share some warm brown source colors, so footwear also follows the hair palette.

## Props rows

| Row | State | Frames | Suggested fps / use |
|---|---|---|---|
| 0 | `desk` | 3 | 0 monitor off; 1 on; 2 typing glow (select state; no geometry motion); static |
| 1 | `chair` | 1 | office chair; static |
| 2 | `coffee_machine` | 1 | coffee machine on walnut cabinet; static |
| 3 | `water_cooler` | 1 | water cooler; static |
| 4 | `sofa` | 1 | two-seat orange sofa; static |
| 5 | `tv` | 3 | 3 screen frames with play icon; cycle; 3 fps |
| 6 | `plants` | 3 | 0 broad-leaf; 1 fern; 2 succulent (static choices); static |
| 7 | `window` | 1 | wood-framed four-pane window; static |
| 8 | `door` | 2 | 0 closed; 1 open (static choices); static |
| 9 | `clock` | 3 | 0 9:00; 1 12:00; 2 3:00 (static choices); static |

## Icons rows

| Row | State | Frames | Suggested fps / use |
|---|---|---|---|
| 0 | `report_item` | 1 | paper report; static |
| 1 | `bubbles` | 6 | 0 working gear; 1 thinking dots; 2 waiting ?; 3 done check; 4 zzz; 5 surprised ! (static choices); static |
| 2 | `role_badges` | 2 | 0 crown pin; 1 lanyard (optional standalone icons); static |

## Tiles rows

| Row | State | Frames | Suggested fps / use |
|---|---|---|---|
| 0 | `floor_office` | 2 | two carpet texture choices; static |
| 1 | `floor_pantry` | 2 | two horizontal wood-plank texture choices; static |
| 2 | `wall_office` | 1 | ivory plaster, wood dado, dark baseboard; static |
| 3 | `wall_pantry` | 1 | honey wood panels, dado, dark baseboard; static |

## Accessories rows

| Row | State | Frames | Suggested fps / use |
|---|---|---|---|
| 0 | `crown` | 1 | small generated wearable crown; already baked into supervisor sheets; static |
| 1 | `lanyard` | 1 | small generated wearable lanyard; already baked into subagent sheets; static |

Props use **64×64** cells, bottom anchor **(32,61)**; icons use **32×32**, centered unless used as a status bubble. A bubble's tail is near (9,27), so place it above the hair with a 2–4 px gap. Tiles use **32×32**, no padding and top-left anchor. Only horizontal joins are meaningful for wall tiles; the baseboard is at the bottom. Office floor tiles are intentionally quiet; pantry grain is more visible.

## Preview, QA and limits

`preview.png` is a **512×352** composition made from the delivered sprites with `sprite-gen scene-render`. `scene.json` and `qa/scene-render/scene.report.json` preserve placement and source evidence. Enlarge it with nearest-neighbor sampling.

Directly reviewed generated rows, extracted frames, role sheets, palette variants and the composed scene. Bad left/right contact poses and raised-fist back typing were regenerated through GPT image_gen; final component extraction was rerun. Side-walk scale and furniture scale were corrected through sprite-gen curation. `qa/rejected/` preserves earlier rejected strips. Final walk contact poses alternate; small one-pixel head/foot changes remain. Back typing is deliberately subtle, and the report handoff is stylized rather than anatomically precise. The lanyard is an orientation-neutral role marker: it is also visible on back views. Walls/floors are basic repeating tiles, not a full terrain autotile set. Crown and lanyard placement were checked across every action; no overlay pixels were clipped.

Runtime loading checks: 43 sheets match their manifests, all 1,674 declared frame references are nonempty and in bounds, alpha is unchanged in all 36 palette variants, and all 26 role/action composites have zero clipped overlay pixels (`qa/validation.json`). Animation GIFs and contact sheets live in the run QA/export directories; these are review files, not runtime sheets. Real-time playback inspection was unavailable because no browser was connected. Locomotion continuity remains experimental pending that playback check; the frame-by-frame visual review does not certify seamless runtime motion. No app build/test was run for this asset-only task.

## Retained pipeline sheets

These are provenance/intermediate composed sheets; load `sheets/` for the final art. Rows, frames and fps for each are in the linked sibling manifest; layer strip names identify the role and the character state listed above.

| Sheet | Cell | Row/frame/fps source |
|---|---|---|
| `runs/accessories/sprite-sheet-alpha.png` | 32×64 | `runs/accessories/manifest.json` |
| `runs/icons/sprite-sheet-alpha.png` | 32×32 | `runs/icons/manifest.json` |
| `runs/props/sprite-sheet-alpha.png` | 64×64 | `runs/props/manifest.json` |
| `runs/subagent-final/sprite-sheet-alpha.png` | 32×64 | `runs/subagent-final/manifest.json` |
| `runs/subagent-final/variants/normalized.png` | 32×64 | `runs/subagent-final/variants/normalized.manifest.json` |
| `runs/supervisor-final/sprite-sheet-alpha.png` | 32×64 | `runs/supervisor-final/manifest.json` |
| `runs/supervisor-final/variants/normalized.png` | 32×64 | `runs/supervisor-final/variants/normalized.manifest.json` |
| `runs/tiles/sprite-sheet-alpha.png` | 32×32 | `runs/tiles/manifest.json` |
| `runs/typing-back-fit/sprite-sheet-alpha.png` | 32×64 | `runs/typing-back-fit/manifest.json` |
| `runs/worker/sprite-sheet-alpha.png` | 32×64 | `runs/worker/manifest.json` |
| `runs/worker-final/sprite-sheet-alpha.png` | 32×64 | `runs/worker-final/manifest.json` |
| `runs/worker-final/variants/normalized.png` | 32×64 | `runs/worker-final/variants/normalized.manifest.json` |
| `runs/worker-rig/layers/subagent_coffee.png` | 32×64 | `runs/worker-rig/layers/subagent_coffee.manifest.json` |
| `runs/worker-rig/layers/subagent_idle.png` | 32×64 | `runs/worker-rig/layers/subagent_idle.manifest.json` |
| `runs/worker-rig/layers/subagent_phone.png` | 32×64 | `runs/worker-rig/layers/subagent_phone.manifest.json` |
| `runs/worker-rig/layers/subagent_report.png` | 32×64 | `runs/worker-rig/layers/subagent_report.manifest.json` |
| `runs/worker-rig/layers/subagent_sleep.png` | 32×64 | `runs/worker-rig/layers/subagent_sleep.manifest.json` |
| `runs/worker-rig/layers/subagent_surprised.png` | 32×64 | `runs/worker-rig/layers/subagent_surprised.manifest.json` |
| `runs/worker-rig/layers/subagent_talk.png` | 32×64 | `runs/worker-rig/layers/subagent_talk.manifest.json` |
| `runs/worker-rig/layers/subagent_typing_back.png` | 32×64 | `runs/worker-rig/layers/subagent_typing_back.manifest.json` |
| `runs/worker-rig/layers/subagent_typing_side.png` | 32×64 | `runs/worker-rig/layers/subagent_typing_side.manifest.json` |
| `runs/worker-rig/layers/subagent_walk_down.png` | 32×64 | `runs/worker-rig/layers/subagent_walk_down.manifest.json` |
| `runs/worker-rig/layers/subagent_walk_left.png` | 32×64 | `runs/worker-rig/layers/subagent_walk_left.manifest.json` |
| `runs/worker-rig/layers/subagent_walk_right.png` | 32×64 | `runs/worker-rig/layers/subagent_walk_right.manifest.json` |
| `runs/worker-rig/layers/subagent_walk_up.png` | 32×64 | `runs/worker-rig/layers/subagent_walk_up.manifest.json` |
| `runs/worker-rig/layers/supervisor_coffee.png` | 32×64 | `runs/worker-rig/layers/supervisor_coffee.manifest.json` |
| `runs/worker-rig/layers/supervisor_idle.png` | 32×64 | `runs/worker-rig/layers/supervisor_idle.manifest.json` |
| `runs/worker-rig/layers/supervisor_phone.png` | 32×64 | `runs/worker-rig/layers/supervisor_phone.manifest.json` |
| `runs/worker-rig/layers/supervisor_report.png` | 32×64 | `runs/worker-rig/layers/supervisor_report.manifest.json` |
| `runs/worker-rig/layers/supervisor_sleep.png` | 32×64 | `runs/worker-rig/layers/supervisor_sleep.manifest.json` |
| `runs/worker-rig/layers/supervisor_surprised.png` | 32×64 | `runs/worker-rig/layers/supervisor_surprised.manifest.json` |
| `runs/worker-rig/layers/supervisor_talk.png` | 32×64 | `runs/worker-rig/layers/supervisor_talk.manifest.json` |
| `runs/worker-rig/layers/supervisor_typing_back.png` | 32×64 | `runs/worker-rig/layers/supervisor_typing_back.manifest.json` |
| `runs/worker-rig/layers/supervisor_typing_side.png` | 32×64 | `runs/worker-rig/layers/supervisor_typing_side.manifest.json` |
| `runs/worker-rig/layers/supervisor_walk_down.png` | 32×64 | `runs/worker-rig/layers/supervisor_walk_down.manifest.json` |
| `runs/worker-rig/layers/supervisor_walk_left.png` | 32×64 | `runs/worker-rig/layers/supervisor_walk_left.manifest.json` |
| `runs/worker-rig/layers/supervisor_walk_right.png` | 32×64 | `runs/worker-rig/layers/supervisor_walk_right.manifest.json` |
| `runs/worker-rig/layers/supervisor_walk_up.png` | 32×64 | `runs/worker-rig/layers/supervisor_walk_up.manifest.json` |

## Generation and licensing

Generated for Vibedotcom with GPT image_gen / provider `codex`, using sprite-gen **2.20.0**, **component-row still-image pipeline**. No video pipeline or third-party sprite pack was used. Initial generation prompts are in `runs/*/prompts/`; request JSON files record actions, frame counts, fit and style. Targeted native-tool correction prompts and reasons are in `qa/generation-notes.md`. sprite-gen itself is **Apache-2.0**; that software license does not assign a separate license to generated images. Generated-image usage remains governed by the provider's applicable terms. No claim of exclusive copyright or third-party clearance is made.
