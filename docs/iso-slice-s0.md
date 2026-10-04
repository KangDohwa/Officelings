# S0 integrator package — approved reduced iso slice

Frozen on 2026-10-04. Use this handoff with [iso-spec.md](iso-spec.md). It lets art, Core, WPF and tracker work start without sharing output files. It describes future production; the reference PNG is an alignment guide, not finished office art.

The slice has three characters, one front desk seating profile, 38 normal rows and 24 blink rows per character. WPF and the tracker export test consume v6. Native parity and end-profile seating belong to the next phase. Full conversion plans for 25 characters, with 53 as the upper bound.

## Frozen geometry and field names

All coordinates are zero-based native pixels before integer physical-device zoom. Screen x points right, y down. Grid positions name tile centers. Project with `P(i,j,z)=(32(i-j),16(i+j)-z)` and snap once with `floor(value+0.5)`. Simulation and navigation remain in grid space.

| Field / meaning | Frozen value |
| --- | --- |
| `version`, `projection`, `requiredCapabilities` | Asset/layout version 1; `iso2to1`; `["iso2to1","renderItemsV1"]`. These additions do not make today's loaders iso-aware. |
| `facings` | `["se","sw","ne","nw"]`; legacy right/down/up/left map to SE/SW/NE/NW. |
| `tile.width`, `tile.height`, `tile.anchor` | 64, 32, `{x:32,y:16}`. |
| `wallRise`, `wallHeight` | 16, 48. Wall horizontal span is 32. |
| Wall cell / first `edgeAnchor` | Left 32×64 / `(32,48)`; right 32×64 / `(0,48)`; rear corner 64×64 / `(32,48)`. An anchor can lie on an exclusive boundary. |
| Wall endpoints in source geometry | Left `(32,48)→(0,64)`; right `(0,48)→(32,64)`; tops are 48px above those endpoints. Corner joins both arms. Top/base trim is 2px inside that height. |
| `characterCell`, `footAnchor`, sheet `anchor` | 32×64, `(16,60)`, `(16,60)`; `scale:1`. Standing reference height 40, allowed 38–42. |
| Clip `seatAnchor` | Stable seated pelvis `(16,44)`. This is the seated pivot; source foot is retained as the sheet reference. |
| `deskHeight`, `seatHeight` | Desktop 18, cushion 12. Monitor top 36; chair back top 21. |
| `deskOrientation`, `seatProfile`, variant `seatProfiles` | Four named orientations; only `front`; variants declare only the front profile. Keep the collection/field hook for a later end profile. Reject end before playback. |
| Furniture cells | 128×128, except the 2×3 rug uses 192×128: its ground envelope is 160×80. A 1×3 sofa's envelope is 128×64. |
| Prop cells / source grip anchor | 32×32 / `(16,16)`; attach that grip to the actor's hand contact. |
| Cat cell / sheet anchor | 32×32 / `(16,28)`; 14–18px standing body. |
| Shadow cells / alpha | 128×128; alpha 0, 48 cast, 96 contact. Character contact footprint 16×6; furniture footprint follows its geometry. |
| `light.screenDirection`, highlight vector | `upper-left`, `(-2,-1)`. Screen-left vertical face lighter than screen-right. |
| `light.shadowOffset`, furniture cast offset | `(8,4)` for character cast tails; `(6,3)` for furniture, in projected pixels. |

Body art uses alpha 0/255, nearest-neighbor sampling and transparent padding. Character x=0 and y=61..63 stay transparent in every phase. Source anchors never come from alpha bounds. A foot pivot can be transparent between shoes: the checker looks for a planted sole on y=60 within x=4..28, rather than requiring ink at `(16,60)`.

State additions retain these spellings: `schemaVersion:6`, `office.view`, `office.projection`, `office.tile`, `office.wallRise`, `office.wallHeight`, `office.wallSegments`, `office.cutaway`, root `requiredCapabilities`, `renderItemsVersion:1`, `renderItems`; pose `isoFacing`, `deskOrientation`, `seatProfile`, `groundScreen`, `seatGround`, `headScreen`, `eyeState`, `seatPhase`, `seatPhaseFrame`, `seatAnchor`. Legacy `pose.facing`, x/y and semantic agent states keep their meanings. Render-item fields are `id`, `kind`, owner/target references, `sprite` (the existing SpriteFrame payload), `destination`, `pass`, `depth`, `isoZOrder`, `partId`, `opacity`, optional `seatGroupId`, `hitTargetId`. Core resolves contacts, source frames and order once; consumers draw that result. WPF and the tracker export test are the only slice consumers.

### Seat and hand contacts

All offsets below are local to the desk's minimum occupied tile center. A chair's ground center is approach A. Its pelvis is `C=P(A)+(0,-12)`; seated character destination is `C-(16,44)`. Grounded seated shoes reach source y=56. The two typing hands are centered at `H=P(A+3/8 f)+(0,-18)`, where f points from the chair toward the desk. The 3/8 step puts the keyboard at the 4px desktop overhang.

| Desk orientation / rotation | A / worker facing | C relative to desk origin | H relative to desk origin | Source hands, left/right screen points |
| --- | --- | --- | --- | --- |
| SE / 0 | `(1,0)` / NW | `(32,4)` | `(20,-8)` | `(3,32)`, `(5,32)` |
| SW / 90 | `(1,1)` / NE | `(0,20)` | `(12,8)` | `(27,32)`, `(29,32)` |
| NE / 270 | `(0,-1)` / SW | `(32,-28)` | `(20,-28)` | `(3,44)`, `(5,44)` |
| NW / 180 | `(-1,1)` / SE | `(-64,-12)` | `(-52,-12)` | `(27,44)`, `(29,44)` |

`handAnchors` is a two-element array of `{x,y}` source points in screen-left then screen-right order. All four typing phases use those points; actual hand pixels may differ by at most 1px. Furniture `handContacts` names the same two points as projected offsets from its origin. Ordinary standing carried-prop grips are SE `(24,44)`, SW `(8,44)`, NE `(24,40)`, NW `(8,40)` in the character cell. Pickup/coffee/phone/handover may publish `handAnchorsByPhase` for their authored motion; a still fallback uses the standing grip. Do not bake cups/paper/device pixels into the body and also draw an overlay.

Freeze transition metadata as clip `phaseContacts`, four objects with `seatAnchor`, `contactOffset` and `footY`. Offsets are from the snapped chair ground. For lowering:

| Phase | Source pelvis `seatAnchor` | Projected `contactOffset` | Local planted sole y |
| --- | --- | --- | --- |
| 0 | `(16,40)` | `(0,-20)` | 60 |
| 1 | `(16,42)` | `(0,-18)` | 60 |
| 2 | `(16,43)` | `(0,-15)` | 58 |
| 3 | `(16,44)` | `(0,-12)` | 56 |

The seated destination moves from ground y−60 to y−56 while shoe source coordinates change, keeping the sole on the same world floor. Stand-up uses this schedule in reverse. Bake reversed pixels only after contact/clothing review. Arrival turns for 120ms; lower/raise lasts 400ms each, 10fps four-phase clips. Seat ownership survives the transition; walking starts after raising.

Sofa contacts remain those in iso-spec section 4, using the approach vector into the sofa rather than the final seated facing. Apply the same pelvis anchor at the sofa contact. Do not transplant the desk approach onto the sofa.

Variant `seatProfiles` is an array keyed by each record's `id`; the slice has exactly one record, `id:"front"`. Its fields are `approach:{x,y}` (local navigation cell), `isoFacing`, `seatGround:{i,j}` (local ground grid point), `contactOffset:{x,y}` (projected pelvis from the variant origin), and `handContacts:[{x,y},{x,y}]` (projected keyboard contacts from that origin). The variant's existing `chairSeatAnchor` equals that contactOffset. The `points` work interaction keeps `id:"seat"`, `action:"typing"`, legacy `facing`, x/y and adds `isoFacing`, `seatProfile:"front"`. The source clip's seatAnchor is a different, source-cell point. Transition phaseContacts use their explicitly stated chair-ground-relative contactOffset; do not substitute the variant-origin offset into a transition.

For rotation0/SE, the frozen profile record is:

```json
{
  "id": "front", "approach": {"x": 1, "y": 0}, "isoFacing": "nw",
  "seatGround": {"i": 1, "j": 0}, "contactOffset": {"x": 32, "y": 4},
  "handContacts": [{"x": 19, "y": -8}, {"x": 21, "y": -8}]
}
```

## Frozen material palette

The actual [env2 palette](../assets/art/env2/palette.json) and [env5 palette](../assets/art/env5/palette.json) each contain 118 RGB colors and are identical at this revision. Select cluster colors from those files, then use the hero to keep the wood/navy/teal/red relationships. The four stops called out below were selected from exact hero samples, not approximations. Wood #B96537 already occurs in both JSON palettes; the two navy samples and the red sample add three colors. Every other stop occurs in both JSON palettes.

| Ramp | Darkest → shadow → body → light → highlight |
| --- | --- |
| Wood | `#4C2A23 #804A2F #B96537 #C78251 #ECA662` |
| Navy | `#111924 #122031 #325571 #48728F #6492A3` |
| Teal | `#10282C #235E5C #20746C #3C9F96 #5DB1A5` |
| Red cloth | `#3F1F18 #61282B #9F2937 #BF6554 #ECA662` |
| Metal | `#191E1E #262D38 #565958 #808985 #89918F` |
| Cream | `#605E5C #898D7B #D0B999 #E7D1AC #F2EBD3` |
| Glass/screen | `#10282C #186D7A #338F88 #4BAFA3 #5DB1A5` |
| Leaves | `#0E1E1C #1D3A39 #264F4B #235E5C #537B78` |

Sample coordinates refer to the hero's 1920×1080 original, with crop boxes `(left,top,right,bottom)`, right/bottom exclusive:

| Hero region | Sample / provenance |
| --- | --- |
| Navy wall `(730,190,815,280)` | `#111924` occurs 940 times; `#122031` 2,070 times. |
| Wood floor `(820,860,1080,910)` | `#B96537` occurs 9,192 times. |
| Red sofa `(552,462,612,493)` | `#9F2937` occurs 1,524 times. |
| Teal chair `(1238,610,1295,659)` | `#235E5C` occurs 696 times and is already in both palettes. |
| Plant `(798,171,870,273)` | Leaf stops also occur in both palettes; this crop confirms the muted teal-green relationship. |

Blue rug uses navy with cloth clusters. Checker uses metal darkest `#191E1E` and cream light `#E7D1AC`. Shadows use navy darkest `#111924`, with the fixed alpha values above. Screen/lamp glow stays on a small separate overlay. Character/pet `identityPalette` is an explicit list of `#RRGGBB` from that identity reference, shared unchanged by its normal/blink sheets. Environment/prop sheets cannot expand the palette with arbitrary identity colors. Checker membership tolerance is RGB max-channel difference ≤3, not a license to add new ramps. The reference PNG includes the swatches; a second palette PNG is unnecessary.

Relative to the former CRT center (A+3/4 f), desk SE/SW/NE/NW projected deltas are (-12,-6), (+12,-6), (-12,+6), (+12,+6), with corresponding ground deltas (-3/8,0), (0,-3/8), (0,+3/8), (+3/8,0). Apply these deltas to all CRT housing/base and screen/LED ordinary depthAnchor values. Preserve monitor isoZOrder 0 and existing overlay rank 1; do not attach CRT parts to seat-ground depth. Keep A/C/H, keyboard contact pairs, source anchors, desk height 18 and monitor top 36. Rebuild exposed desktop and cut CRT supports as physical art. Relocate desktop props on legal surface contacts consistently across monitor states when needed; publish their actual ground keys.

## Exact contact reference

Reference: [iso-contact-v1.png](../assets/art/iso/ref/iso-contact-v1.png), **768×320 RGBA**, native 1×. Generate deterministically with `tools/iso_check.py --reference`. No smoothing, resizing, external image generation or production-sheet cutting is involved.

There are four 192×160 panels per row. Top row desk orientations are SE, SW, NE, NW. Each panel has local desk-origin D=`(96,80)`, the exact floor diamonds under the desk and chair, a tabletop at z18, cushion at z12, upright monitor from z18 to z36, and a simple seated rig. CRT ground center is `A+9/8 f`; its projected base is `P(A+9/8 f,18)`, with an 18px screen height. This is a contact fixture, not an approved silhouette or depth golden. Cross centers are the authoritative contacts: cream `#F2EBD3` = ground foot reference, teal `#5DB1A5` = pelvis, red `#BF6554` = hands. Each cross extends two pixels in x/y; paired hand crosses overlap.

| Top panel | Global desk origin | Chair ground / pelvis | Character cell top-left | Global two hands |
| --- | --- | --- | --- | --- |
| SE / worker NW | `(96,80)` | `(128,96)` / `(128,84)` | `(112,40)` | `(115,72)`, `(117,72)` |
| SW / worker NE | `(288,80)` | `(288,112)` / `(288,100)` | `(272,56)` | `(299,88)`, `(301,88)` |
| NE / worker SW | `(480,80)` | `(512,64)` / `(512,52)` | `(496,8)` | `(499,52)`, `(501,52)` |
| NW / worker SE | `(672,80)` | `(608,80)` / `(608,68)` | `(592,24)` | `(619,68)`, `(621,68)` |

Bottom row, y=160..319:

| Panel | Exact contents |
| --- | --- |
| Tile | Source tile top-left `(64,224)`, anchor `(96,240)`. Geometric vertices `(96,224),(128,240),(96,256),(64,240)`. |
| Standing | Character source cell top-left `(272,180)`, foot `(288,240)`; reference body y=201..240 inclusive, 40px. The guide cross fills the transparent pivot gap intentionally. |
| Walls | Left first/base endpoint `(440,264)`, second `(408,280)`, tops `(440,216),(408,232)`; right first `(520,264)`, second `(552,280)`, tops `(520,216),(552,232)`. |
| Palette | Eight rows in the table order. Swatch top-left `(628+24c,184+16r)`, size 20×12, c=0..4, r=0..7. Labels begin x=580. |

Tile rasterization samples `(x+0.5,y+0.5)`: `dx=x+0.5-32`, `dy=y+0.5-16`, `i=dx/64+dy/32`, `j=dy/32-dx/64`. Fill only `-.5≤i<.5` and `-.5≤j<.5`; that is exactly 1,024 opaque texels. Geometric vertices at x=64/y=32 are exclusive boundaries. Debug crosses/text may extend beyond a source mask in this reference only; no crosses/text enter production art.

Check the contacts by overlaying candidate production cells at the listed top-lefts, or assemble them from the D/A/C/H formulas. Do not recenter a candidate from its visible pixels. The pilot checks all four views, even though the hero's usual desk view is behind-diagonal.

## Naming and manifest conventions

These are additive to [asset-format.md](asset-format.md); that file is not edited by this handoff. Asset/layout stay version 1. Production manifests end in **`.iso.json`** so the checker can discover only lane exports. Runtime adapters live exclusively in pack; private production metadata is not a promise of current runtime support.

| Output | Convention |
| --- | --- |
| Character | `<id>-normal.png`, `<id>-blink.png`, `<id>-eyes-se.png`, `<id>-eyes-sw.png`, `<id>.iso.json`. Normal 128×2432; blink 128×1536; each eye-mask atlas 128×2432. |
| Environment | `floors.png` / `floors.iso.json`; `walls.png` / `walls.iso.json`. Atlas rows/cells have no gutters. Split by cell size when needed. |
| Furniture | `<family>-parts.png`, `<family>.iso.json`; separate `<family>-screen.png` allowed for monitor states. Every split part is a clip, not a new catalog family. |
| Pet / props | `cat-normal.png`, `cat.iso.json`; `carried-props.png`, `carried-props.iso.json`. Shadows use a separate 128×128-cell sheet. |
| IDs / paths | Runtime IDs `iso/<family>` or `iso/character/<id>`; sheet IDs unique in the assembled pack. Files are relative to their `.iso.json` directory; no escape, absolute path or network URL. |
| Part IDs | `<family>__<role>__<facing>__<nn>`, e.g. `desk__front__se__00`. Role is ground/rear/front/monitor/screen/side/edge/shadow; nn is two decimal digits. State goes in clip `state`, not in `partId`, so it remains stable. Clip names include a state suffix when needed to keep `(name,direction)` unique. |

A production manifest contains `version:1`, `projection:"iso2to1"`, `sheets:[...]`, and the relevant furniture/tile/character definitions. Each sheet supplies `id`, `kind`, `file`, `cellW`, `cellH`, `anchor`, `scale:1`, `projection`, `rows`. `kind` is character/blink/floor/wall/furniture/pet/prop/shadow. Each clip supplies `name`, `direction`, `row`, optional `column` default 0, `frames`, `fps`, `loop`; the exact 38 stored character names/order/timing are in iso-spec section 3 and the checker's `CONTRACT`. Do not put materialized alias clips in the normal sheet's 38 stored `rows` list.

| Additional production field | Use |
| --- | --- |
| Sheet `footAnchor` | Required on character/blink, `(16,60)`. |
| Sheet `identityPalette` | Required on character/blink/pet; explicit source identity colors. |
| Clip `anchor` | Optional per-clip source origin, otherwise sheet anchor. Required if a variant differs. Wall `edgeAnchor` documents the same point. |
| Clip `anchorChecks` | For wall/furniture/pet/prop/shadow, at least one `{x,y,radius:0 or 1}` physical contact with nonzero alpha in every phase. Use a grounded sole, contact surface, wall/base texel or prop grip; the geometric origin may legitimately be transparent. The lane cannot pick a decorative pixel and call it a contact. |
| Clip seated/transition fields | `deskOrientation`, `seatProfile`, `seatAnchor`, `handAnchors`, `phaseContacts` as frozen above; optional `handAnchorsByPhase` for non-typing prop motion. |
| Furniture clip depth fields | `partId`, `depthAnchor:{i,j}`, integer `isoZOrder`; optional `seatGroupId`, `hitTargetId`. Depth is ground i+j, not image bottom or z. |
| Normal sheet `aliases` | Array of `{name,direction,target:{name,direction},frame?,effect?,prop?,seatProfile?}`. Targets stay in the same facing; `frame:0` means held source phase, not new storage. Aliases may chain without cycles. Every runtime action/facing in iso-spec's fallback table must resolve. `effect`/`prop` are declarative bindings for existing bubbles or local prop clips; no code strings. |
| Normal sheet `blink` | `{sheetId:"<id>-blink",normalRows:[0,1,4,5,8,9,12,13,16,17,20,21,24,25,26,27,28,29,30,31,34,35,36,37],durationMs:100}`. |
| Normal sheet `faceLock` | Exactly `se:{eyeMask:"<id>-eyes-se.png"}`, `sw:{eyeMask:"<id>-eyes-sw.png"}`. Masks are RGBA atlases, alpha 0/255, phase-aligned to the normal atlas; only that facing's eye texels are marked. No NE/NW masks. |
| Floor clip fields | `family`, `axis` for wood, `phase` for checker; `transition:true` for partial overlays. Base diamonds are fully opaque; overlays stay within the same diamond. |
| Floor sheet `patches` | One 6×6 checker definition per base family/axis: `{name,family,axis?,tiles:[{row,column?}],phaseRule?}`. Wood chooses repeated variants deterministically; checker supplies two distinct dark/light references and `phaseRule:"parity"`, using `(i+j)%2`. |

Example excerpts, to be filled out with all required rows and assets:

```json
{
  "name": "seated-typing-front", "direction": "se", "row": 8,
  "frames": 4, "fps": 6, "loop": true,
  "deskOrientation": "nw", "seatProfile": "front",
  "seatAnchor": {"x": 16, "y": 44},
  "handAnchors": [{"x": 27, "y": 44}, {"x": 29, "y": 44}]
}
```

```json
{
  "name": "seated-waiting", "direction": "se",
  "target": {"name": "seated-thinking", "direction": "se"},
  "frame": 0, "effect": "waiting-bubble", "seatProfile": "front"
}
```

Runtime packaging resolves aliases into ordinary clips with explicit frame counts and offsets; it does not require an alias-aware renderer. It publishes `typing`, seated `thinking`/`waiting`, `napping`, `chatting`, `reporting`, water/TV/darts and their documented fallbacks, plus explicit `standing-thinking`/`standing-waiting` context aliases to idle. Optional wave/celebrate dispatch also resolves. A seated alias retains its seatAnchor. An idle fallback used for pickup/handover retains the action's transfer timing. Blink aliases point to the same normal row/phase's closed-eye copy. `surprised` is never aliased to waiting. End-profile requests fail rather than dispatching an absent row.

Blink clips have names `blink-<normal-name>`, the matching direction/fps/loop, four phases, and rows 0..23 in the normalRows order. All non-eye RGBA bytes must match the corresponding normal phase exactly; each marked eye region is at most 32 texels, within 12×4 pixels, and lies on the opaque head. Keep the head silhouette/landmarks stable, allowing only documented integer translation of at most 1px per phase; masks follow that translation. The checker proves mask presence and normal/blink equality outside it. Human review still proves identity and cross-phase face stability.

At a seat, chair rear is −20, actor 0, desk front 10, chair front 20. For all four desk orientations, split the keyboard-bearing seat strip into the horizontal desktop/keyboard surface and the physical front fascia. The horizontal surface belongs to a seat-group rear part at −20; the fascia stays at 10. Preserve the exact source anchors, ordinary ground-cell depthAnchor values and frozen C/H contacts. Use disjoint physical masks whose RGBA union equals the complete source strip.

The backrest covering a back-facing torso belongs to the front occluding part, regardless of its physical name. Rebuild the physical backrest in all four orientations at top height 21, retaining cushion height 12 and the frozen pelvis/hand contacts. For SE/SW sitters the backrest is rear; for NE/NW sitters it is front. Do not move the entire backrest behind a back-facing torso or globally raise actor/head depth.

Split long front faces into per-edge strips as needed. Parts reconstruct the complete empty object without duplicated opaque pixels; seat-specific anchors/ranks affect only the attached assembly. Walkers use their own ground depth. Furniture definitions use the existing `parts` hook; runtime IDs and group ownership are supplied by packaging/Core.

For each of the four phases of seated-typing-front and each pilot, use the documented canonical head alpha at its clip offset and the actual furniture queue. Require at least 85% head visibility for workers NW/NE and at least 90% for workers SE/SW. Preserve head denominators, and publish visible/total head, precisely defined torso ROI and hand-contact skin ROI for all four directions. A checker pass alone is not visual acceptance. Review empty reconstruction, C/H, walkers, transitions and WPF ordering separately. With A3 rev2 furniture, all 48 pilot typing phases expose the documented hand-contact skin ROI fully. Forearm/sleeve corridor visibility for SE/SW workers is 86.5–95.9%. These are measured final-art results with fixed actor snapshots, not a new anatomical hand gate or runtime acceptance.

## Exclusive output ownership

Do not touch flat art, another lane's sources, shared manifests or runtime packs. A lane publishes only within its assigned directory. Only I1 packaging writes **anything** under `assets/art/iso/pack`, including adapters, assembled PNGs, layout fixtures, deterministic QA rasters and goldens. S0 integrator alone owns `assets/art/iso/ref`, this document, iso-spec and the checker; a lane reports a contract correction to the integrator instead of editing them.

| Task owner | Exclusive directory |
| --- | --- |
| A1 | `assets/art/iso/env/floors/` |
| A2 | `assets/art/iso/env/walls/` |
| A3 | `assets/art/iso/furniture/workstations/` |
| A4 | `assets/art/iso/furniture/lounge/` |
| A5 | `assets/art/iso/furniture/utility/` |
| C1→C2→C3, one active writer per ID | `assets/art/iso/characters/<id>/` |
| P1 | `assets/art/iso/pets/` and `assets/art/iso/props/` |
| I1 packaging only | `assets/art/iso/pack/` |

C1/C2/C3 are sequential stages for the same ID, not simultaneous writers in a character folder. Different IDs may run in parallel after the pilot gate. Engine G1/G2/G3 likewise use one shared Core owner and sequential contract handoffs. W1/W2 share one WPF owner; H1 owns tracker/export tests. Assign actual source-file ownership before code work starts. Workers do not create nested agents. At most four lanes run at once.

## Checker and completion evidence

[tools/iso_check.py](../tools/iso_check.py) uses Python 3.10+, Pillow and numpy only. It prints one pass/fail table and exits 1 on failure, including missing input. Directory input recursively discovers `.iso.json` only. The script writes nothing unless `--reference` is explicitly selected; that switch writes only `assets/art/iso/ref/iso-contact-v1.png`.

On this Windows workspace, the existing Python environment is:

```powershell
$isoPython = 'C:/Users/<user>/.codex/skills/sprite-gen/.venv/Scripts/python.exe'
& $isoPython tools/iso_check.py --check-reference
& $isoPython tools/iso_check.py assets/art/iso/env/floors
& $isoPython tools/iso_check.py assets/art/iso/characters/dev-hoodie --stage c1
& $isoPython tools/iso_check.py assets/art/iso/characters/dev-hoodie --stage c2
& $isoPython tools/iso_check.py assets/art/iso/characters/dev-hoodie
```

Each lane runs the checker on its own exports before finishing. C1 publishes only the 8-row idle/walk atlas, 128×512, plus its seated rig review; `--stage c1` checks that prefix and explicitly skips C2 aliases/C3 blink. C2 expands the normal atlas to 38 rows and runs `--stage c2`; blink is explicitly skipped until C3. C3 and I1 must use the default **final** stage. Environment/furniture/pet/prop checks are the same at all stages. A SKIP means a check is inapplicable or belongs to a later stage, never that it passed.

The checker covers cell sizes and bounds, scale/projection, binary transparency and character guards, physical anchor pixels, palette membership at max-channel tolerance 3, normal/blink/cat row contracts, action alias resolution, depth-part naming/ground keys/tie ranks, and SE/SW eye masks with all 96 blink comparisons. Floor bases are assembled into 6×6 patches: every owned texel must be covered once, without overlaps/holes, and adjacent same-material edge colors differ by at most 8 per RGB channel. Checker contrast is intentional; its two whole-tile sources use the declared parity. Transition masks cannot leave the diamond. Inspect transitions visually as well. Tolerances may be lowered, never raised above 3/8 through CLI flags.

A successful checker does not prove pose quality, hand anatomy, identity, monitor-only animation masks, every furniture reconstruction, reachability, occlusion, DPI rendering or v6 behavior. Those need the task reviews below and G/W/H/Q fixtures. At 1×, inspect feet/contacts and 6×6 floor patches; 2×/3× must be exact nearest-neighbor expansions. Do not rebuild unrelated app/test suites for an art handoff.

## Lane tasks and exact acceptance

| Task / planning hours | Dependency | Required output and acceptance |
| --- | --- | --- |
| A1 floors / 4–7 | S0 | Exactly 11 logical 64×32 cells: i-axis wood, dark/light checker, four edge transitions (±i,±j), four corner patches. Base coverage follows the half-open mask. Publish wood and checker 6×6 patch definitions; review all 16 edge-neighbor combinations and mixed corners at 1×/2×. No individual dark diamond outline. |
| A2 walls / 6–10 | S0 | 15 logical modules: plain L/R, window L/R, closed door L/R, open door L/R, rear corner, end caps L/R, top trims L/R, base trims L/R. Separate opening/leaf/threshold layers where needed. Use the exact anchors/rise/height, independently shaded L/R. Review 20 joined rear edges, door at (0,4), window beside (4,0), corner and ends without gaps/double strokes. State/part separation can add physical atlas cells without adding logical modules. |
| A3 desk+monitor+chair / 5–8 | S0 | Desk and chair in SE/SW/NE/NW, front profile only. Desk default aliases off; screensaver static; working four screen phases at 3fps. Same contacts/footprints in all states; only screen/LED pixels vary. Publish screen mask, disjoint desk/chair parts and physical contact checks. All four C/H pairs match the reference within 1px. Reconstruct empty/occupied chair; inspect near/far/end walker overlap. Preserve end hook without end art. |
| A4 sofa/rug/TV/side table / 4–7 | A3 part convention | Four named variants for each family. Sofa 1×3 at rotation 0, cushion12/back24, explicit rotated nap approach/contact; split rear/front strips. Rug 2×3, nonblocking, 192×128 cells, 180° pairs reusable if nondirectional. TV cabinet 1×2, cabinet16/top38; side table 1×1,height16. Check empty/occupied sofa reconstruction and walkers at both ends. |
| A5 plant/shelf/coffee/fridge/water/lamp / 5–9 | S0 | Four named variants each, with spec footprints/heights: plant1×1/top34/pot12; shelf1×2/top44; coffee1×1/top28; fridge1×1/top44; water1×1/top40; lamp1×1/top40. Front/rear controls, spines, hinges/spigots and fixed upper-left light remain correct; safe radial reuse is allowed. Publish reachable authored interaction points where used and explicit parts/anchors. |
| C1 per character / 3–5 each | S0; A3 for seated contacts; pilot gate for IDs after dev-hoodie | Four-facing identity references; rows0–3 idle, rows4–7 walk, four cells each. Grounded 38–42px idle silhouette, no clipped guard/padding, four distinct gait phases and approved asymmetry/light. Review a neutral seated rig in all four desk orientations against C/H; C2 alone publishes its final seated rows. Run `--stage c1`. |
| C2 seated/actions / 4–7 each | Same ID's C1 | Complete rows8–37 in the exact contract: four front typing, four thinking with neutral phase0, four sit, four stand; SE/SW pickup/handover/coffee/phone/talk/surprised; NE/NW sleep. Publish all explicit aliases, per-phase transition/prop contacts and bubble bindings. Waiting holds neutral seated phase0; raises finish before walking; report timing stays intact. Review all stored rows and all alias directions, including water/TV/darts and optional wave/celebrate. Run `--stage c2`. |
| C3 blink/face-lock / 1–2 each | Same ID's C2 | 24×4 blink cells and exactly SE/SW phase-aligned eye-mask atlases. Every non-eye byte equals the corresponding normal phase. Head/identity stable; anatomical accessories and directional light restored after any mirror. No NE/NW eye masks. Run final checker on the complete character manifest. |
| P1 cat+props / 7–13 total | S0; pilot hand contacts | Cat 9 rows/36 cells, 128×288: idle SE/SW/NE/NW, walk SE/SW/NE/NW, curled sleep any, four phases each. Ground foot(16,28), 14–18px silhouette, nonblocking independent depth and separate shadow. Shared 32×32 grip-anchored cup/water cup/phone/report paper and optional held dart; attach to all three rigs without clipping or duplicate body pixels. Export explicit normal/reaction bubble bindings using existing shared UI art. Run checker on pets and props. |
| I1 packaging / 5–8 | A1–A5, C3×3, P1; G1 | Adapt lane manifests, expand aliases, preserve flat pack, compile complete iso pack and exact 10×10 placement from iso-spec. Final checker on all source manifests, asset limits, reachable approaches and stable IDs. Only this lane writes pack. No character rescale, anchor recenter or implicit missing-clip fallback. |

Art subtotals match iso-spec: floors4–7, walls6–10, furniture14–24, characters24–42, pets4–7, props3–6. P1 combines the pet and prop lines. G1/G2/G3, W1/W2, H1 and Q1 retain the bounded dependencies in iso-spec; acceptance is WPF motion/occlusion/alpha picking, tracker v6/export publication and flat regression. N1/N2 start only after H1/W2/Q1 freeze the fixtures, pack and WPF rasters; native parity is not a reduced-slice gate.

## Pilot before the other characters

Dev-hoodie is the first and only character production job until its full C1→C2→C3 package passes. A3's front desk can proceed in parallel; C1 idle/walk begins from S0, then its seated rig waits for A3 contacts. A1/A2/A5 and G1 may proceed within the four-lane cap. Professor, punk-girl and later roster IDs remain queued.

The pilot deliverable is 152 normal + 96 blink = **248 animation cells**, the two eye-mask atlases, all action aliases, and a contact/identity review of all four desk orientations. A3 plus this character is estimated **13–22 lane-hours**, included in the slice's **102–175** total. After all checks/review pass, the integrator records the measured generation/wait, repair, validation and review lane-hours in the handoff, replaces estimates using that evidence, and releases the next character jobs. Keep lane-hours distinct from elapsed time: overlapping lane work is added, not counted once.

No throughput is measured yet. The planned 25-character conversion is **6,200 character cells and 600–1,066 WPF-first lane-hours**; the 53-character upper bound is **13,144 cells and 824–1,458 lane-hours**. Native adds 30–52 full-conversion lane-hours later. The three-character slice is **744 character cells**, or **780 with the 36-cell cat**, before floors/furniture/props. Review alias motion limitations before scaling the roster; extra action rows or end seating require an updated contract and estimate.
