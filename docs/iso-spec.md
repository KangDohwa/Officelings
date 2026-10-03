# Strict 2:1 isometric specification

This is the source of truth for the planned iso art and engine conversion. The owner selected strict 2:1. All dimensions below are native pixels at 1×, before display zoom. This document specifies future work; it does not claim that the current app supports iso.

The existing [asset format](asset-format.md) and [state contract](state-schema.md) remain the flat contract. Iso additions and their required behavior are specified here. A conflicting concept-note measurement does not override this document. The concept PNGs are visual references, not sprite sheets to cut apart.

## 1. Resolve the concept measurements

Read together: [A](../assets/art/iso-concept/NOTES-A.md), [B](../assets/art/iso-concept/NOTES-B.md), [C](../assets/art/iso-concept/NOTES-C.md). Visual references: [hero](../assets/art/iso-concept/iso-office-hero.png), [selected strict view](../assets/art/iso-concept/iso-variant-1-strict-2to1.png), [asset board](../assets/art/iso-concept/iso-asset-board.png).

| Source | What it actually describes | Resolution |
| --- | --- | --- |
| A | 480×270 composition shown at 4×; guide tile 28×14, apparent larger cells about 56×28; character guide 24×40, anchor (12,39) | Keep its room, furniture, clothing and palette relationships as visual references. Its measurements are not production geometry. |
| B | Native tile 64×32; wall span/rise/height 32/16/48; character cell 32×64, foot (16,60); board shown at 3× | Adopt its tile, wall and foot geometry. Reduced previews do not measure individual sprites. |
| C | Strict scene rendered with native floor 32×16 and shown at 2× as 64×32; character guide remains native 32×64 with foot (16,63) | Adopt the camera choice. Replace its half-resolution floor and bottom-row foot convention. Do not copy its mixed floor/character scale into production. |
| Current flat pack | Builder normalizes standing bodies to 30 world pixels and retains foot (16,60) | Keep flat art and its scale unchanged. Iso keeps the reference point but uses newly authored 40px bodies. |

The canonical choices are **tile 64×32, character cell 32×64, foot (16,60), standing height 40, wall height 48, source scale 1**. The foot reference is the ground point beneath the body, and may be transparent between the two shoes; it is not the cell's last row or whichever shoe is lowest in a frame. A planted sole meets the y=60 ground reference in the standing/walking rig. Rows 61–63 are transparent padding. Never infer an anchor from an alpha bounding box or change it between frames.

## 2. Geometry and coordinates

### Grid, origin and projection

Grid coordinates are `(i,j)`, also serialized as existing layout `x,y`. A tile's integer coordinate identifies its **center**. Cell `(i,j)` occupies `[i−0.5,i+0.5) × [j−0.5,j+0.5)` in grid space. Furniture `x,y` identify the minimum occupied cell center, not the outer corner of a diamond.

The world origin is the center of tile `(0,0)` on the ground. Screen axes point right and down. Projection, before camera translation and zoom:

```text
P(i,j,z) = (32(i−j), 16(i+j)−z)
z = height above the ground in native screen pixels
```

`z` is a visual elevation, not a third navigation axis. The camera places this projected world with an integer physical-pixel translation `(ox,oy)`. It does not add a half tile to character coordinates. Existing flat feet still use their existing half-tile/bottom-edge formula.

For a ground point `(sx,sy)`, after removing camera translation and zoom:

```text
i = sx/64 + sy/32
j = sy/32 − sx/64
tileI = floor(i + 0.5)
tileJ = floor(j + 0.5)
```

Do not apply that inverse to an elevated sprite's top pixel to find its ground tile. Use the item's ground anchor. For tile `(i,j)` centered at `(cx,cy)`, a point is inside the closed diamond when `abs(sx−cx)/32 + abs(sy−cy)/16 <= 1`. Shared edges use the half-open grid intervals above, so only one tile wins; positive-side cells own an exact boundary. Reject out-of-bounds and void cells afterward.

The tile's geometric vertices relative to its center are `(0,−16),(32,0),(0,16),(−32,0)`. In its 64×32 PNG they are `(32,0),(64,16),(32,32),(0,16)`. Values 64 and 32 describe exclusive image boundaries, not valid pixel indexes. Rasterize at texel centers with the same half-open ownership rule; do not clamp all vertices inward and draw duplicate outlines. Draw tiles with source anchor `(32,16)`.

A 10×10 room uses tile centers `0..9` on each axis. Ground bounds are **x = −320..320, y = −16..304**, a 640×320 diamond envelope. The rear corner is `P(−0.5,−0.5)=(0,−16)`. With 48px walls, the top reaches y=−64. Compute actual render bounds including art overhangs, shadows and labels; do not assume the ground envelope is the whole image.

### Wall geometry

“Left” and “right” refer to the two screen halves of the rear wall, not character facings.

| Face | Grid boundary / travel along it | Ground step | Segment geometry |
| --- | --- | --- | --- |
| Left rear wall | `i=−0.5`, increasing `j` | (−32,+16) | 32px horizontal span, 16px absolute slope rise, 48px vertical height |
| Right rear wall | `j=−0.5`, increasing `i` | (+32,+16) | Same dimensions |

One edge segment is a parallelogram made from its two ground endpoints and those endpoints translated `(0,−48)`. Its geometric envelope is 32×64. “Rise 16” is the slope between adjacent endpoints; it does not make later wall modules taller. Moving away from the rear corner lowers the base and top by 16 screen pixels per edge.

Left/right base modules use 32×64 cells. Corner joins use 64×64 cells. A 2px top cap and 2px base trim lie inside the 48px height; decorative height is not added to it. Module anchors are the projected first endpoint in the direction of travel, relative to the source cell. Publish the exact anchor for each face; both faces cannot share a guessed bottom-center anchor. Place neighboring endpoints exactly 32px apart horizontally and 16px apart vertically.

### Character size, feet and shadows

An ordinary standing silhouette is **40px high**, measured inclusively from its highest opaque body pixel to its lowest grounded sole pixel in reference idle frame zero. Accept 38–42px for hair, hats and posture; shoes stay on the common ground anchor. A default 40px reference occupies y=21..60. Body width is normally 20–26px; keep x=0 transparent to permit anchor-preserving mirror baking. Props extending beyond the character cell belong on a separate prop layer.

Today's flat bodies are 30px high beside 16px navigation tiles. Iso deliberately changes that ratio: a 40px person stands beside a 64×32 floor tile, an 18px desktop and a 12px seat. A 64px-tall opaque person would dwarf those desks; 64 is canvas height, not body height. A monitor may reach 36px above the floor, just below the ordinary standing head. Validate the seated head, hands and monitor together, rather than scaling the body until a generated composition looks similar.

Standing/walking placement at 1× is `destination = snapped P(i,j,0) − (16,60)`. The anchor is a zero-based integer coordinate measured from the source cell's upper left; both coordinates are copied into runtime metadata. There is no additional half-pixel adjustment.

Shadows are separate ground-layer sprites, never baked into the body or furniture pixels. A standing person's contact shadow has a 16×6 raster footprint centered at their ground point; an optional cast tail extends toward (+8,+4). Seated shadows belong to the seat assembly; do not draw an additional standing ellipse under the seated body. Furniture shadows follow their footprint, with a 6px-right/3px-down cast displacement. Use one fixed shadow ramp, with alpha 96 for contact and 48 for the cast tail, and no filtered blur. Alpha transitions used for fading are runtime effects; normal body art uses alpha 0 or 255.

### Pixel rules

- Iso PNG pixels are world pixels: `scale:1`. No half-resolution source tile, fractional sprite scale, shear or runtime pixel rotation.
- Grid simulation positions may remain continuous. Snap the projected ground point once with `floor(value+0.5)`, including negative values; apply all integer offsets to that point. Do not independently round the body, chair contact, paper and shadow.
- Camera zoom is an integer number of **physical device pixels** per source pixel, 1..6. Convert WPF device-independent units through the actual DPI before drawing; 150% DPI must not create 1.5-device-pixel sprite pixels. Pan translation is also an integer physical pixel.
- Nearest-neighbor sampling, no smoothing, no fractional destination rectangles. At less than 1× available space, keep 1× and allow pan/clipping. This differs from flat's existing fractional auto-fit option.
- Keep an atlas cell's dimensions and anchors through normalization. No per-frame recentering, bounding-box cropping, or rescaling to fit an animation pose.

## 3. Directions, actions and character atlas

### Direction names and movement

Names describe the direction seen on screen. Manifest/state strings use lowercase `se,sw,ne,nw`; prose uses capitals. Canonical clip order is **SE, SW, NE, NW**.

| Grid step | Projected screen step | Iso facing | Existing movement/pose semantic |
| --- | --- | --- | --- |
| `+i` | (+32,+16), down-right | SE | `right` |
| `+j` | (−32,+16), down-left | SW | `down` |
| `−j` | (+32,−16), up-right | NE | `up` |
| `−i` | (−32,−16), up-left | NW | `left` |

Keep Core's four direction meanings as grid directions. Add a projection-specific lookup at animation selection: **right→SE, down→SW, up→NE, left→NW**. Do not map names by their English screen meaning; mapping flat `down` to iso SE would be wrong for `+j`. Preserve `pose.facing` with the old values and add `pose.isoFacing`; the latter selects iso art.

The first release keeps current four-neighbor Manhattan A*. Every move already projects to a diagonal screen direction. Entry, exit, report delivery, leisure travel and seat orientation use the same mapping. Semantic agent states remain `working/thinking/waiting/reporting/idle/gone`; they do not become direction-specific states.

An optional later eight-neighbor grid walk must explicitly change neighbor costs and corner checks: axial cost 1, diagonal cost √2, octile heuristic, and no diagonal step if either adjoining axial cell is blocked. Grid diagonals project to screen cardinal motion: `(1,1)` down, `(1,−1)` right, `(−1,1)` left, `(−1,−1)` up. With only four art facings, select the dominant grid component; equal components retain the previous compatible facing, otherwise choose the `i` component. Thus down chooses SE or SW, right SE or NE, left SW or NW, and up NE or NW. Resolve this once in Core and publish it; consumers must not alternate poses. Eight-way navigation is outside the slice.

### Furniture orientation and the two desk seating choices

An object's orientation is the outward normal of its usable front: bookshelf opening, sofa opening, display screen, or keyboard edge. A worker at a desk faces **opposite** the desk's front. A chair/sofa occupant instead faces **the same direction** as that seat's front after turning to sit; the initial approach may face toward the seat. Numeric rotations remain clockwise grid rotations, not image transforms:

| Rotation | Object front | Primary worker facing | Optional end worker facing | Desk footprint |
| --- | --- | --- | --- | --- |
| 0 | SE | NW | NE | 1×2 |
| 90 | SW | NE | SE | 2×1 |
| 180 | NW | SE | SW | 1×2 |
| 270 | NE | SW | NW | 2×1 |

“Two visible facings per desk orientation” means **two legal authored camera views of a seated worker**, `seatProfile:front` and `seatProfile:end`. It does not mean that eyes are visible in both, or that the desk gains two simultaneous work seats. The end profile uses the clockwise adjacent edge. The selected profile has its own keyboard/monitor placement and hand contacts; a person cannot type through the side of an unchanged monitor. Layout selects one profile, default `front`. The slice ships both profiles for contact/animation review; its room uses front profiles.

There are eight typing clips: four orientations × two profiles. Other seated actions and transitions use four facing clips, reused between profiles because their pelvis rig is identical. The desk/seat metadata, not a duplicate character row, supplies the contact position. This is a deliberate expansion from C's approximate 20-row estimate.

### Full iso row set

Every row has **four stored cells**, columns 0..3. Unless noted, groups use SE, SW, NE, NW order. No mandatory action silently falls back to standing idle. Rows may contain derived or repeated pixels, but their clips and metadata must exist.

| Rows | Clip / runtime selection | FPS / loop | Notes |
| --- | --- | --- | --- |
| 0–3 | `idle` | 1 / yes | Standing; four facings. |
| 4–7 | `walk` | 8 / yes | Four distinct gait phases per facing; entering/walking/exiting select these. |
| 8–15 | `seated-typing-front/end` | 6 / yes | Eight clips, detailed order below. |
| 16–19 | `seated-idle` | 1 / yes | Resting at the seat. |
| 20–23 | `seated-waiting` | 1 / no | Four copies of rest phase zero; waiting bubble is separate. |
| 24–27 | `seated-thinking` | 2 / yes | Small finger tap/head nod; no typing loop at full speed. |
| 28–31 | `sit-down` | 10 / no | Four lowering phases; clamp at frame 3. |
| 32–35 | `stand-up` | 10 / no | Four raising phases; reverse sit-down only when contacts and clothing remain valid. |
| 36–39 | `coffee` | 4 / yes | Cup lift and recovery. |
| 40–43 | `phone` | 3 / yes | Device in the authored hand. |
| 44–47 | `sleep` | 1 / yes | Diagonal recline on the sofa; source cell stays 32×64. |
| 48–51 | `talk` | 3 / yes | Runtime `chatting`; body gestures, separate speech bubble. |
| 52–55 | `pickup` | 6 / no | Paper pickup. |
| 56–59 | `report-handover` | 6 / no | Runtime `reporting`; carrying paper is an overlay during walk. |
| 60–63 | `surprised` | 6 / no | Triggered reaction; never replace all waiting poses with surprise. |
| 64–67 | `tv` | 2 / yes | Standing leisure view. |
| 68–71 | `water` | 4 / yes | May reuse coffee motion with a different prop. |
| 72–75 | `darts` | 6 / yes | Throw/recovery rig and separate dart. Previewable; no new leisure scheduler is required by the slice. |

Rows 8–15 are exactly:

| Row | Desk orientation | Profile | Character facing |
| --- | --- | --- | --- |
| 8 | SE | front | NW |
| 9 | SE | end | NE |
| 10 | SW | front | NE |
| 11 | SW | end | SE |
| 12 | NE | front | SW |
| 13 | NE | end | NW |
| 14 | NW | front | SE |
| 15 | NW | end | SW |

Total: **76 normal rows, 304 stored frames, 128×4864 PNG per character**. Normal source rectangles are `(column×32,row×64,32,64)`, without gutters. The row table is a production layout; runtime always follows the clip manifest and supplied frame, never hardcodes row numbers.

Runtime selection: Working at a work seat→selected seated typing clip; Thinking→seated thinking; Waiting→seated waiting; seated leisure rest→seated idle; entering/walking/exiting→walk; napping→sleep; chatting→talk; reporting→handover. Gone hides after the existing exit/fade lifecycle. Standing thinking/waiting may select idle plus the existing bubble. Pickups and handovers retain their existing overall lifecycle timing even when their four-frame clips clamp early.

On seat arrival, turn to the selected facing for 120ms, lower for 400ms, then stay seated. On departure raise for 400ms before any path movement. A transition owns the seat until it finishes. Interrupting work during lowering completes the contact transition and then raises. Publish phase frames 0..3. Do not use walking frames to simulate lowering or raising.

### Derivation, asymmetry and face lock

SW may be derived from SE, and NW from NE, when the design passes an asymmetry review. This is a production operation. A standard horizontal flip changes x=16 to x=15 in a 32px cell: translate the flipped result +1px to restore the foot/pelvis x=16, or reflect coordinates as `x′=32−x`. The transparent x=0 guard prevents loss at the far edge. Recheck hand contacts and all alpha bounds after baking.

Do not mirror a hair parting, shoulder bag, earring, badge, readable text, mug handle or phone hand onto the other anatomical side. Either author the opposite view, or mirror the body and repaint those features from an anatomical-side reference. Repaint directional lighting afterward; a mirror must not move highlights to the lower-right. `mirrorOf` records provenance and the integer correction, not a runtime flip instruction. Review the professor's glasses and punk-girl's hair/accessories explicitly.

Derived rows can include waiting from seated-idle phase zero, thinking from the seated rig with a hand/head patch, water from coffee with a cup patch, stand-up from validated sit-down reversal, and paired safe facings. Idle/rest frames may repeat. Walking, typing, throwing and report motion still need contact and loop review; derivation is not permission to label four identical walk frames as a completed gait.

Face-lock is **per iso facing**. SE/SW show eyes; NE/NW show the back and have no eye masks or blink clips. Keep each facing's hairline, skin, facial landmarks and head outline stable across phases, allowing only an explicit integer head translation of at most 1px. Do not paste today's frontal flat face onto a diagonal head. A character's identity comes from the same hair, skin, clothing and accessories, with newly authored diagonal references.

Store phase-matched closed-eye copies in a separate **128×2432** blink PNG: 38 visible-facing normal rows × four phases = **152 frames**. Their order is ascending normal row number, selecting only SE/SW clips. NE/NW need no transparent placeholder rows. All non-eye RGBA bytes must equal the corresponding normal phase, including hair, alpha, props and offsets. Metadata records the normal row/phase and eye mask; the usual closure is 100ms. Blink swaps the matching phase without resetting action time, choosing frame zero, or moving the face. These are additional stored copies, not another animation state. Per character: 304 normal + 152 blink = **456 stored cells**.

### Additions to the asset contract

Asset format stays `version:1`; iso-aware validation is selected by `projection:"iso2to1"`. These are additions to [asset-format.md](asset-format.md), not changes to existing flat field meanings. A single-view iso manifest declares the following defaults; sheet/variant values may specialize them:

```json
{
  "version": 1,
  "projection": "iso2to1",
  "requiredCapabilities": ["iso2to1", "renderItemsV1"],
  "facings": ["se", "sw", "ne", "nw"],
  "tile": {"width": 64, "height": 32, "anchor": {"x": 32, "y": 16}},
  "wallRise": 16,
  "wallHeight": 48,
  "characterCell": {"width": 32, "height": 64},
  "footAnchor": {"x": 16, "y": 60},
  "standingHeight": 40,
  "light": {"screenDirection": "upper-left", "shadowOffset": {"x": 8, "y": 4}}
}
```

| Scope / additive fields | Contract |
| --- | --- |
| Sheet `projection`, `footAnchor` | Iso characters use `cellW:32,cellH:64,scale:1,anchor:(16,60)`; `footAnchor` documents the same point. Reject mismatches. Shared icon/bubble sheets may use `projection:"any"`. |
| Clip `direction` | Accept `se/sw/ne/nw` only in iso sheets, in addition to existing flat strings and `any`. Names remain unique with direction. |
| Clip `deskOrientation`, `seatProfile`, `seatAnchor`, `handAnchors` | Typing identifies front/end and its desk orientation. Seated contact is source pixels; idle/thinking/waiting use pelvis (16,44). Two named hand points are source pixels and may vary by phase. Transitions supply per-phase contact metadata. |
| Sheet/production `faceLock`, `blink`, `mirrorOf` | Per-facing references, masks, closed-eye sheet/clip links and derivation provenance. Runtime clips may retain the current `blink-<clip>` naming convention. Never depend on a production generator's private manifest format. |
| Variant `orientation`, `heightPx`, `seatProfiles`, `parts` | Explicit orientation plus footprint/collision data; front/end workstation metadata; drawing parts with source clip, integer source anchor, ground `depthAnchor`, `isoZOrder`, seat group and optional `hitTargetId`. |
| Interaction `isoFacing`, `seatGround`, `contactOffset`, `handContacts` | Legacy action/`facing` and integer navigation point remain. Seat ground is a possibly fractional local grid point for drawing/depth; contact/hand offsets are integer projected pixels from the variant origin. |
| Tile type `projection`, `family`, `axis`, `phase` | Tile family, plank axis or checker phase; source 64×32 at scale 1. Draw the authored diamond, not a 16×16 stretch. |
| Walls `face`, `edgeAnchor`, `heightPx`, `hostId` | Left/right geometry and attachment relationship; source clip anchors are explicit. |

Existing `anchor`, source rectangles, `fps`, `loop`, hashes, local-path validation and image limits still apply. Iso source `footOffset` values must be integers. Resolve a seated frame by its pelvis/contact, then publish its resulting integer destination/offset; renderers must not solve the rig again. The old `chairSeatAnchor` remains a projected world-pixel contact, with the active projection declared; it is not the navigation tile or source-cell anchor.

Keep flat and iso as separate runtime packs, with projection-scoped IDs such as `flat/cozy-desk` and `iso/desk`. A view loads one complete pack plus shared UI art. Do not combine the present flat pack and every iso sheet into one manifest: current limits are 128 sheets, 512 furniture entries and 8192px per image axis. The 76-row normal and 38-row blink sheets fit; pack furniture in atlases grouped by cell dimensions. Keep split parts inside a furniture definition instead of multiplying catalog entries past 512. Extend validators only where the iso fields require it; do not relax PNG/path/hash limits.

## 4. Furniture, anchors, light and occlusion

### Required orientation sets and dimensions

Every listed family exports all four named variants. At rotation 0 the footprint below is `(i width × j height)`; 90/270 swap those dimensions. Front and rear views are separate drawings. Mirroring is a starting point only, followed by anchor, asymmetry and lighting corrections.

| Family | SE footprint | Height above ground | Opposite-side production rule |
| --- | --- | --- | --- |
| Desk + monitor | 1×2 | Desktop 18; monitor top 36 | SE/SW and NE/NW may share a mirrored desk structure. Author screen/back, keyboard and both seat profiles. |
| Chair | 1×1 | Cushion 12; back top 28 | SE↔SW, NE↔NW may be mirrored after fixing arms, wheels and shade. |
| Sofa | 1×3 | Cushion 12; back top 24 | Front pair and back pair may share geometry; maintain cushion joins and arm sides. |
| Plant | 1×1 | Pot 12; leaves top 34 | All variants may reuse a radial pot. Keep leaf asymmetry and upper-left highlights. |
| Bookshelf | 1×2 | 44 | Front pair may be mirrored only after restoring book order/spines; rear is authored. |
| TV cabinet | 1×2 | Cabinet 16; screen top 38 | Front pair/rear pair structure reusable; screen, cables and rear panel authored. |
| Coffee machine | 1×1 | 28 | Front/rear differ; controls, cup and spout retain their sides. |
| Fridge | 1×1 | 44 | Door hinge/handle is anatomical to the object; front/rear authored. |
| Water cooler | 1×1 | 40 | Spigots and bottle authored; cylindrical body reusable. |
| Rug | 2×3 | 0 | Two axis variants; 180° pairs identical only for a non-directional pattern. Nonblocking. |
| Side table | 1×1 | 16 | Symmetric structure may be reused; repaint shade for each face. |
| Lamp | 1×1 | 40 | Radial geometry reusable; light highlight remains upper-left. |
| Meeting table, full conversion | 2×3 | 18 | Four variants, multiple named seats; not an extra slice asset. |

Footprints describe navigation occupancy, not image bounds. Table legs, leaves, lampshades and cushion overhang may extend outside it. Blocking defaults to the complete footprint. Rugs use `blockedCells:[]`; linked work chairs are nonblocking at their reserved approach tiles. Decorative free-standing chairs block their cell unless explicitly used as an authored seat. Seat reservation excludes other activity destinations, while retaining the current engine's lack of moving-body collision avoidance.

A 1×2 object's ground envelope is 96×48. Choose a source canvas containing that envelope, its height and guard pixels. For slice export use 128×128 cells for furniture parts and their states; source anchors may differ by variant but are explicit integers. A 1×3 sofa also fits this canvas at the specified height. Larger full-conversion objects may use 256×192 or another integer cell size within current limits. Never squeeze multi-tile art into a character cell.

Desk `default` aliases `off`; `screensaver` is a static screen variant, and `working` has four monitor phases at 3fps. Every rotation/profile uses the same footprint and contacts across these states. Only the authored screen/LED mask changes; all desk, chair, monitor housing and shadow pixels are byte-identical across phases. The same static-body rule applies to later steam, water and lamp animation.

For desk rotation 0, the minimum occupied center is `(0,0)`, primary approach `(1,0)` faces NW and end approach `(0,2)` faces NE. The complete desk point table is:

| Orientation | Front approach/facing | End approach/facing |
| --- | --- | --- |
| SE | (1,0) / NW | (0,2) / NE |
| SW | (1,1) / NE | (−1,0) / SE |
| NW | (−1,1) / SE | (0,−1) / SW |
| NE | (0,−1) / SW | (2,0) / NW |

These are already-rotated local navigation cells. For authoring geometry only, a clockwise turn maps local `(i,j)` to `(h−1−j,i)`, including exterior points. Runtime selects that authored variant and does not rotate it again.

Desk chair ground center equals the selected approach `A`. Its pelvis contact is `C=P(A)+(0,−12)`. A stable seated clip has source pelvis `(16,44)`, so destination is `C−(16,44)`; its grounded shoes can reach source y=56. Source foot (16,60) remains the sheet reference but is not the active seated pivot. Do not clip every iso leg at the pelvis using the flat profile-seat rule; foreground masks hide the appropriate parts, and visible feet stay grounded.

Let `f` be the unit grid step toward the desk. The center of the typing hand-contact pair is `H=P(A+(3/8)f)+(0,−18)`. This projects to integer offsets (±12,±6) and corresponds to a 4px horizontal desktop overhang beyond its blocked edge. Relative to the source pelvis, the pair center is SE `(28,44)`, SW `(4,44)`, NE `(28,32)`, NW `(4,32)`. Author two nearby hand points on the keyboard and match them within 1px across all typing phases. Both desk profiles receive their own keyboard, monitor and contacts.

Sofa approach and cushion contact are different. The central approach for an SE 1×3 sofa is `(1,1)`, approaching NW and then turning to SE for the seated/reclining rig; its seat ground lies 3/4 tile toward the sofa from that approach. Here `f` is the approach vector into the sofa, not the final seated facing, and `C=P(A+(3/4)f)+(0,−12)`. Rotated variants supply explicit approach and seated facings and contacts. Do not change the actor's walkable navigation coordinate to a blocked sofa cell when seating or sleeping. A larger table has distinct stable seat IDs and contacts; the current one-work-seat-per-desk rule does not change automatically.

### Depth and split layers

Ground depth is **`d=i+j`**, never image top, canvas bottom, head height or projected y after subtracting object height. Draw ascending depth. Tie order is `(d, isoZOrder, ordinal instance/agent ID, part ID)`. `isoZOrder` is an integer tie rank; unlike flat's existing `zOrder`, it is not a pixel offset added to depth.

Render passes are:

1. Background/void, then opaque floor diamonds and material transitions.
2. Floor props such as rugs, then contact/cast shadows, clipped to floor.
3. The two exterior rear walls, their trim/window/door layers and wall decor.
4. A mixed depth queue for furniture parts, actors, carried props and interior walls/decor. Existing relationship hints sit above floors and below this queue.
5. Actor/desk names, bubbles and local hover/editor UI.

An object's default coarse depth anchor is the nearest ground corner `(x+w−0.5,y+h−0.5)`, depth `x+y+w+h−1`. That is valid only for a compact object whose overlap is correct as one part. A sofa, desk or table cannot rely on one maximum-depth key: it would incorrectly cover a walker beside its rear half. Split its overlapping image regions into authored parts tied to their ground edge/cell, with disjoint masks. A part's `depthAnchor` identifies its occluding ground plane. Where a long front face crosses several depth bands, export per-edge/cell strips; do not cut a tall object by screen-height bands.

At a seated worker, the portions intersecting that worker share the seat ground depth and `seatGroupId`. Use tie ranks **chair-back −20 → actor 0 → desk-front 10 → chair-front/armrest 20**. Desk rear/monitor and other panels keep their own anchors. For a back-facing sitter, the visible backrest covering the torso belongs to chair-front even though it is physically called a “back.” Sofa rear cushion is behind the actor, front apron/arms are in front. Empty parts recombine the full object without gaps or duplicate dark pixels.

This seat-specific ordering affects the attached actor and intersecting assembly parts, not the entire room. A passing walker uses its own ground depth, so it passes behind the near edge and in front of the far edge. Test walkers on both sides and at both ends of every multi-tile family. If one panel must be simultaneously before and after two actors, split that panel further; do not solve it with a larger global z offset. Use published part masks for hit testing too.

Exterior rear wall decor is attached to its wall pass and sorted after its host, then by attachment ID. Interior wall segments share scene depth with their decorations and openings; an attachment inherits host depth, with a higher tie rank. Its hanging height moves pixels up but never changes ground depth. Low thresholds/baseboards and closed/open door leaves are separate parts so a character can cross an open doorway correctly. Walls do not gain depth from their 48px height.

### Lighting and palette

The fixed key light comes from the **upper-left of the screen**, with highlight direction (−2,−1) and cast-shadow direction (+2,+1). Top planes are lightest. Screen-left-facing vertical planes use the lighter side shade; screen-right-facing planes use the darker side shade. Bottom rims, leg contacts and recesses use the darkest ramp entry. Do not rotate the world light with the furniture, and do not retain mirrored highlights.

These are the initial shared material ramps, listed darkest → shadow → body → light → highlight. They are authoring decisions, not colors sampled and proven to occur in every concept image. Keep skin/hair colors from each character's original identity reference. A material change needs one ramp change and pack-wide review, not unrelated per-object colors.

| Material | Five RGB stops |
| --- | --- |
| Warm wood | `#4B2D24 #79503A #B47A4C #D69D62 #EDC486` |
| Navy wall | `#111B29 #1C2B3B #2B4054 #426077 #688399` |
| Teal cloth/chair | `#123637 #1D5554 #2D7772 #4B9C90 #81B9A1` |
| Red sofa cloth | `#4B2027 #74303A #A4494B #CC6B5D #E79B7C` |
| Dark metal | `#141B24 #26323D #455362 #72828D #AFBEC1` |
| Cream plastic | `#625F57 #908A78 #BAB199 #DBD0B4 #F2E8CE` |
| Glass/screen | `#102F38 #165663 #258392 #56B8BC #A4DFD1` |
| Plant leaves | `#163D32 #225D45 #39855B #67A369 #A1C480` |

Wood has grain along the authored grid axis, with no checker noise used as “detail.” Cloth uses broad clusters, metal a sparse 1px edge highlight, plastic broader highlights, glass a fixed upper-left reflection plus separately animated screen pixels. Screen glow and lamp glow are small authored overlays; they do not recolor neighboring objects or change the shared key light in the slice. Night/day lighting variants are later work.

Blue carpet/rug uses the navy ramp with cloth texture; checker uses the dark-metal darkest stop and cream-plastic light stop. Stone starts from the dark-metal ramp with matte clusters, without the metallic edge highlight. Shadows use the navy darkest stop at the alpha values in section 2. These choices complete the slice's floor/rug/shadow palette without adding per-tile colors.

## 5. Floors, walls and cutaway

Every floor family is a 64×32 diamond with anchor (32,16). Background outside the diamond is transparent. Inside the floor coverage is opaque; no anti-aliased perimeter, filtering, translucent seam or individual dark diamond outline. Tile raster ownership and placement from section 2 must produce complete floor coverage.

| Family | Seam and axis rule |
| --- | --- |
| Wood | Separate `axis:i` and `axis:j` variants. Planks parallel to `+i` slope down-right, and those parallel to `+j` slope down-left. Boundary plank widths/colors and grain continuation match adjacent tiles; no independent random rotation. |
| Carpet | Non-directional base plus bounded texture variants with identical edge colors. Texture variation comes from a deterministic tile-coordinate seed. |
| Checker | Two whole-tile dark/light phases chosen by `(i+j) mod 2`, with identical geometry. No random phase or diagonal-screen-coordinate parity. |
| Stone | Matched edge grout and explicit repeat phase/variants. Join endpoints coincide at shared projected boundaries; tiles do not each add two grout pixels to a shared edge. |

Use the family below as the opaque base and add transitions on top, inside its diamond mask. Adjacency is in grid space, not screen left/right. Four edge masks correspond to `±i,±j`; four corner patches repair their meeting points. Combine those eight reusable patches for the 16 edge-neighbor cases. A deterministic material-priority table resolves a three-material junction. Rug edging is furniture, not a third floor family. Preview every family in a 6×6 patch and all transitions at 1×/2×; inspect both grid axes.

Full conversion needs left/right plain walls, windows, closed/open doors, inside/outside corners, end caps, top/base trims and intersection pieces where authored interior walls require them. Left/right are independently shaded; a flipped left wall is not a completed right wall. A window spans one tile edge unless metadata explicitly says two; doubled windows match shared mullions. Its glass opening has a separate interior/backdrop so opacity does not expose accidental void. Door leaf/opening/threshold share one edge anchor; an open leaf does not change the doorway approach tile.

Draw the exterior walls on **`i=−0.5` and `j=−0.5`**. Hide the two near boundaries `i=W−0.5` and `j=H−0.5`, except a ground-level 2px cut edge. Keep them logically present if the room bounds require them. Removing wall pixels never removes collision.

The slice uses this fixed cutaway. For later interior walls, `cutaway:auto` fades an obstructing segment and attached decor to opacity 0.25 over 120ms while its nontransparent mask overlaps a visible actor's head/body; restore over 200ms after the overlap ends. Mark that cutaway segment/decor `hoverPassthrough` while obstructing, including during the fade, so it does not steal actor hover hits. Hover/editor selection may explicitly hide such a segment. Use the shared projected masks/order, not a guessed screen rectangle. The currently drawn rear walls need no automatic fading inside the slice.

## 6. Renderer, engine and compatibility work

### Core

The relevant existing paths are `Models.cs`, `Assets.cs`, `LayoutLoader.cs`, `Office.cs` and `OfficeEditor.cs` under `src/Vibedotcom.Core/`. Implement the following as bounded changes, keeping flat branches and their formulas:

1. Add an explicit projection/view record and shared pure grid↔projected-ground functions, snapping and projected bounds. Remove hardcoded `×16` calculations only from iso paths. Simulation coordinates stay grid coordinates.
2. Load/validate projection-specific manifests, directions, tile art, object parts, contact profiles and walls. Resolve one complete pack per view. Preserve existing roster hash, assignment, work-seat IDs, monitor state and asset publication limits.
3. Keep four-neighbor A*, blocked cells and reachability in grid space. Map motion and interaction facing through the table above. Reuse current path/lifecycle logic; no rotated or pixel-space navigation graph.
4. Resolve iso seated/transition frames, pelvis/hand contacts, phase-matched blink, carried paper and actor shadows. Keep contacts fixed through frame changes, and finish stand-up before walking. Support the new art selection without changing semantic agent states.
5. Build one deterministic scene queue with split parts and seat groups. Compute ground depth before height/visual offsets. Export integer destinations, selected source frames and tie order so WPF and C++ share the result.
6. Add ground diamond picking for editor tile placement. Provide part/actor IDs and ordered draw items for renderer alpha picking. Core need not acquire a WPF bitmap decoder; consumers cache their decoded alpha masks.
7. Editor placement snaps to grid centers; previews show the rotated diamond footprint and approach/seat/contact points. Rotation selects authored variants, swapping geometry and contacts together. Move/rotate a workstation with its linked chair parts as one undoable edit. Do not rotate PNGs. Validate collisions and all approaches before Save, retaining existing permissive preview behavior.
8. Make view/layout/pack selection explicit. Preserve source files on load; save only an explicit editor operation. Rebuild static caches on projection, tiles, wall visibility, asset hash, part geometry or layout changes.

### Layout additions and retaining flat

`layout.json` stays format `version:1` with additive fields. Missing `view` means `flat`. Existing `tileSize:16` remains the legacy navigation/render value and is ignored for iso projection; never change it to 64 or overload it as diamond width. `widthTiles,heightTiles,tiles,zones,door`, furniture IDs and integer grid coordinates retain their meanings.

An illustrative iso header, to be combined with the usual required map fields:

```json
{
  "version": 1,
  "view": "iso",
  "projection": "iso2to1",
  "requiredCapabilities": ["iso2to1", "renderItemsV1"],
  "tileSize": 16,
  "tile": {"width": 64, "height": 32},
  "wallRise": 16,
  "wallHeight": 48,
  "assetPackId": "iso-slice-v1",
  "cutaway": "rear-two-walls",
  "wallSegments": [
    {"id":"rear-left-door", "face":"left", "edge":{"i":-0.5,"j":3.5}, "assetId":"iso/wall-door", "state":"open"},
    {"id":"rear-right-window", "face":"right", "edge":{"i":3.5,"j":-0.5}, "assetId":"iso/wall-window"}
  ]
}
```

Wall `edge` is the segment's first ground corner, with travel in +j for left and +i for right. New furniture fields include `orientation`, `seatProfile`, `seatGroupId`, optional projection-specific asset bindings and per-instance contact overrides. Numeric rotation and orientation must agree. The selected asset-pack ID resolves through installed/bundled local pack configuration; a layout cannot cause a network fetch.

Both views coexist as **two saved layouts and two packs**, using the same simulation/contracts. Existing root/default layout and art remain flat. Add an iso layout/pack and select its `view:"iso"`; the app can switch back to the saved flat layout. A projection-compatible layout may bind the same logical IDs to both packs, but switching a layout with different footprints requires its separately authored counterpart. Do not silently replace flat furniture geometry when the switch changes.

Additive fields are not a promise that old binaries can render iso. New loaders must inspect view and required capabilities before treating this version-1 source as flat. Old version-1 loaders may ignore those fields; do not install the iso source as the default file for an old application.

### WPF app

Update `OfficeView.cs` projection bounds, physical-pixel zoom/pan, tile drawing, mixed-part scene rendering, actor bounds and labels. Use the Core queue and integer destinations; keep the bitmap cache keyed by sheet hash/frame. Cache floors/rear walls separately, without caching a front furniture layer that must interleave with walkers. Preserve uniform window opacity and transparent margins.

Update `OfficeEditorPanel.cs` and hover/name-tag integration to inverse-project the cursor and preview iso footprints/contacts. For sprite hover, inspect scene items in reverse final draw order, reject transparent source pixels, and return the first eligible visible pixel. Normal art is alpha 0/255; faded items use effective alpha, with hit threshold 32/255. The first opaque noninteractive scene item blocks picking through it unless tagged `hoverPassthrough`. Group desk parts under their owner/desk ID. Name plates and the intentional body-to-plate hover gap remain explicit UI targets after scene picking; transparent rectangular sprite padding is never a body hit.

Keep plates horizontal and anchored above the actual visible head, not above cell y=0 or the floor tile. Keep current hover fade/grace/tooltips and away-desk reservation rules. Editor handles/picking take precedence while editing. A view selection updates layout, active pack, camera extent and static caches together; it does not restart agent identity assignment unnecessarily.

### Headless state contract

Current writers use state v5, asset/layout v1 and fallback atlas v2. Emit **`schemaVersion:6` for iso**, while unchanged flat snapshots may remain v5. A v6 consumer supports both. No existing root, agent or pose field is renamed or changes type. `pose.facing` stays the legacy grid-direction string; `pose.x,y` stay continuous navigation coordinates. Fallback atlas stays v2 for bubbles/shared UI, not as a source of substitute flat bodies in iso.

| Additive field | Meaning |
| --- | --- |
| `office.view`, `projection`, `tile`, `wallRise`, `wallHeight`, `wallSegments`, `cutaway` | Active resolved geometry/visibility. Missing in older input means flat. |
| `requiredCapabilities` | Root capabilities needed to interpret the snapshot; reject unsupported values before rendering. |
| `pose.isoFacing`, `deskOrientation`, `seatProfile` | Authoritative iso clip selection, absent/null in flat. |
| `pose.groundScreen`, `seatGround`, `headScreen` | Snapped projected floor point, continuous ground depth point for seating, integer visible head anchor. Do not infer these from sprite alpha bounds. |
| `pose.eyeState` | `open/closed`; exact selected normal/blink source frame is still authoritative. |
| `pose.seatPhase`, `seatPhaseFrame`, `seatAnchor` | Existing fields; iso phase frame range becomes 0..3 and seatAnchor is the resolved projected pelvis contact. |
| `renderItemsVersion:1`, `renderItems` | Ordered complete world scene: floor/wall/shadow/furniture/actor/prop parts, including invisible/hidden part omissions. UI labels are consumer-local. |

Each render item contains stable `id`, `kind`, owner/target references, existing `SpriteFrame`, integer `destination:{x,y,width,height}`, `pass`, `depth`, `isoZOrder`, `partId`, `opacity`, optional `seatGroupId` and `hitTargetId`. Primitive floor/cut-edge items include color and integer polygon vertices instead of a sprite. Items are already ordered. Destination pixels are projected native world pixels, independent of window pan/zoom. Legacy `furnitureSprites` and `pose.sprite` remain populated for inspection; v6 rendering follows renderItems and must not also draw those sprites a second time. Compound furniture can have several renderItems per one instance ID.

Tracker publishes immutable hashed PNGs before their manifest/snapshot, embeds the active manifest, and retains current atomic JSON replacement, sequence/restart and stale-data behavior. Keep the last complete valid scene when referenced images are temporarily unavailable. Only one active view is published per output directory at a time; simultaneous flat/iso consumers need separate tracker outputs, not competing writers to one state.json.

Unsupported v6/capabilities must yield a clear unsupported-view status, never a distorted flat rendering. The current Windhawk parser accepts a broad numeric schema range and still assumes 16px geometry; bumping the number alone does not guard it. Upgrade that parser to an explicit supported-version/capability gate before using an iso output directory. Compatibility is an implementation task, not something this document retroactively supplies.

### Windhawk C++ renderer

In `windhawk/vibedotcom.wh.cpp`, update Snapshot/Frame/Variant parsing, supported schema/capability validation, projected bounds, integer zoom, and image/scene cache keys. Render the v6 queue, including split chair/desk/sofa parts, cutaway walls, exact blink frames and contact shadows. Use supplied destinations and order instead of duplicating A*, assignment, contact solving or `i+j` sorting rules. Keep the flat renderer for older snapshots.

Replace rectangular-only actor hits with alpha-mask hits in reverse draw order and the same effective-alpha threshold; combine them with current plate/tooltip targets. Cache source alpha before opacity is applied. Preserve nearest-neighbor GDI+ behavior, layered-window alpha, read-sharing flags, last-good-snapshot recovery and native window lifecycle. Compare the native scene with WPF before calling desktop iso supported. No desktop-hook or host-window rewrite is part of this conversion.

### Migration and testing

Migrate to a **new iso counterpart**, preserving original layout/art files and stable furniture/desk/seat/agent relationships. Copy tile topology, zones and door first; translate flat wall cells into explicit wall edges only after checking their collision intent. Map furniture asset IDs with an explicit table. If a mapped iso variant preserves the old footprint, approach and rotation meaning, retain placement. If it changes any of them, author the new placement/contact and rerun overlap/reachability validation. Existing 3×2 flat desks do not automatically become the slice's 1×2 desks. Missing iso art is a conversion error for the slice; do not silently show a flat sofa or flat person among iso art.

Keep today's top-down source, runtime pack and default layout available as `flat`, with its 30px people, old clip selection and unchanged geometry. Do not run a destructive global art conversion or change the default view as part of delivering the opt-in slice.

The future implementation should extend existing `CoreTests`, `PackTests`, `OfficeEditorTests`, `SeatedTests`, WPF `RenderingTests` and native `renderer_tests.cpp`, with bounded iso cases:

- Geometry: project/inverse round trips at tile centers and fractional points, negative boundaries, shared-edge ownership, 10×10 bounds and left/right wall endpoints.
- Navigation/editor: the four axis/facing pairs, reachable front/end approaches, collision after all rotations, Save rejection, undo/reload preserving stable seat owners. Optional eight-way behavior gets tests only if implemented.
- Art/anchors: exact cell/row counts, source scale 1, (16,60) foot, 38–42px standing reference, no clipped nontransparent pixels, grounded gait and 1px hand-contact tolerance. Check normal/blink non-eye bytes and asymmetric/mirrored accessories.
- Occlusion: empty/occupied chair reconstruction, desk/sofa near and far walks, both ends of long objects, simultaneous occupants, rear/interior wall decor and an open-door crossing. Cover every orientation and desk profile with synthetic placed poses.
- Hits: diamond transparent corners, overlapping sprites, holes between chair arms, opaque occluders, hidden walls, plate gaps and faded bodies.
- Goldens: freeze simulation seed, timestamps, actor IDs/action times, camera and active pack hashes. Save approved 1× scene rasters for an empty room, four-direction walks, all seat/profile states and multi-tile crossings. Verify 2×/3× are exact nearest-neighbor block expansions. New reference pixels need visual review; never regenerate goldens just to pass.
- Cross-renderer: compare WPF and native world-scene crops with UI labels disabled. Opaque RGBA pixels must match exactly. For premultiplied shadow/fade pixels permit at most 1 value per channel; no positional tolerance. Test 100/150/200% display DPI with integer physical-pixel output.
- Flat regression: run the existing relevant flat pack/state/render cases and retain flat golden pixels. Test a v6 iso rejection by an unsupported consumer and last-good-scene retention after an incomplete publication.

The concept hero is not a pixel golden: its native tile scale and generated details differ. Use it for visual review of warm wood, navy walls, teal seating, character identity, desk proportions and depth readability. Future implementation acceptance uses purpose-made deterministic goldens. For this docs-only task, verification is limited to document completeness, arithmetic, examples and referenced paths; no build, pack generation or app run is required.

## 7. First deliverable: vertical slice

### Exact scope

Ship one opt-in 10×10 room, with these **12 furniture families**, not a claim of only twelve placed instances. Three copies of desk+monitor and three chairs give **16 placed furniture instances**. Split drawing parts are not additional furniture families. Assets export SE/SW/NE/NW for every family, using the reuse rules above.

| Content | Quantity and required coverage |
| --- | --- |
| Room | 100 ground cells; tile centers 0..9. Door entry tile (0,4). Required workspace/pantry/lounge/door zones remain authored. |
| Floors | Two families: i-axis wood and checker. Wood base plus two checker parity bases; four edge and four corner transition patches: 11 cells at 64×32. Blue lounge rug is a furniture asset. |
| Walls | One navy set: plain L/R, window L/R, closed-door L/R, open-door L/R, inner rear corner, end caps L/R, top trims L/R, base trims L/R: 15 logical modules. In the room, draw 20 rear edge segments, replace one right segment with a window and one left segment with a door, plus the corner/end joins. |
| Furniture | Desk+monitor, chair, sofa, plant, bookshelf, TV cabinet, coffee machine, fridge, water cooler, rug, side table, lamp; all four named orientations. Desk ships both seat profiles and off/working/screensaver monitor states; other families need default state. Split required chair/desk/sofa parts. |
| Characters | dev-hoodie, professor, punk-girl; each 76×4 normal and 38×4 blink cells. Total **912 normal + 456 blink = 1,368 stored cells**. Every row is available in a pose review, including darts. |
| Pet | One sleeping/walking cat, matching the hero reference: 32×32 cell, foot (16,28), 14–18px standing silhouette; idle and walk in four facings, four frames each, plus one four-frame curled sleep row. 9 rows/36 cells, 128×288 sheet. Independent nonblocking ground depth and shadow. |
| Props/UI | Cup, water cup, phone, report paper and dart overlays for the three rigs; neutral provider icons and existing names/bubbles. No additional arcade, aquarium or dartboard furniture is needed to review character poses. |
| Behavior | Door entry/exit; four-way walking; sit/stand, working/thinking/waiting; report pickup/delivery; existing coffee/water/TV/phone/chat/nap actions where reachable. A pose review covers rows without a live station or scheduler trigger. |

A concrete placement fixture follows. Coordinates are minimum occupied grid centers; chair locations are desk front approaches. All heights/footprints come from section 4.

| Instance | Position / orientation | Purpose |
| --- | --- | --- |
| Desk 1 / chair 1 | Desk (2,2) NW; chair (1,3) SE | SE-facing worker, eyes visible. |
| Desk 2 / chair 2 | Desk (5,2) NE; chair (5,1) SW | SW-facing worker, eyes visible. |
| Desk 3 / chair 3 | Desk (4,5) SW; chair (5,6) NE | Back-facing worker. Fourth NW-facing worker view tested in the pose fixture/rotation. |
| Sofa | (6,8) NE, 3×1 | Central nap approach (7,7) faces SW, then turns NE to recline; cushion contact distinct from approach. |
| Plant | (0,6) SE | Tall thin overlap case. |
| Bookshelf | (0,0) SE, 1×2 | Left-wall tall object. |
| TV cabinet | (0,8) SE, 1×2 | TV approach (1,8), faces NW. |
| Coffee machine | (9,1) NW | Approach (8,1), faces SE. |
| Fridge | (9,3) NW | Tall pantry prop; no new fridge interaction needed. |
| Water cooler | (9,5) NW | Approach (8,5), faces SE. |
| Rug | (6,7) SW, 3×2 | Nonblocking floor layer beneath sofa area. |
| Side table | (3,8) SE | Compact depth case. |
| Lamp | (4,8) SE | Tall thin light/shadow case. |

Checker floor occupies `i=8..9,j=0..5`; all other cells use wood. The right window occupies the wall edge beside tile (4,0); the left door occupies the edge beside (0,4). Keep the cat's demonstration path clear of pantry objects and work approaches. Assign the three designs to stable synthetic agents for deterministic acceptance, including a supervisor/reporting child pair. This fixture is a target to validate in the implementation, not a layout file created by this document.

### Acceptance criteria

1. At 1×, the room reads as the same office family as the hero and selected strict image: warm wood, navy rear walls, teal chairs, recognizable three character identities, upright CRTs and seated hands meeting keyboards. No stretched flat furniture or half-resolution floor pixels appear.
2. All pixel/cell/row/anchor checks above pass. Floor seams and wall joins have no holes or doubled boundary strokes at 1×, and 2×/3× are exact pixel blocks. Feet, seat pelvis and typing hand contacts do not drift across four phases.
3. Both front facings pass phase-matched face-lock/blink checks. NE/NW show backs with no eyes or accidental frontal mask. Hair parting, bag/accessory side and light direction remain correct after any mirror derivation.
4. WPF runs the room through the opt-in layout `view:"iso"`. Agents walk in all four directions, occupy their authored desks, type/think/wait, raise before leaving and sit again without losing seat ownership. Reporting and existing leisure paths remain reachable.
5. Walkers pass correctly behind/in front of desk, chair and sofa parts, including near/far ends. Seated bodies tuck behind the appropriate front pixels; visible shoes do not hang through the floor. Door crossing and wall attachment order work. Reverse-order alpha hover selects the visible target.
6. The tracker exports valid v6 snapshots and the updated Windhawk renderer displays the same world scene within the stated pixel tolerances. An unsupported consumer rejects it clearly. Native host behavior retains existing tests.
7. Switching back to the existing flat layout/pack restores its appearance and behavior. Flat source art/layouts remain available and the default startup stays flat.

### Parallel lane tasks and dependencies

Each task below is sized for roughly 3–8 Codex-lane hours. Tasks marked “×3” are three separate jobs. At most four lanes work concurrently. Assign exclusive output paths before starting; the engine lane owns shared Core records, the packaging lane alone writes assembled packs/goldens, and art lanes publish only their own finished sources. Workers do not create nested workers.

| Task | Lane / output responsibility | Dependency | Completion evidence |
| --- | --- | --- | --- |
| S0 | Integrator: freeze fields, one tile/desk/character contact fixture, acceptance list | This spec | Hand-reviewed reference fixture and assigned file ownership. |
| G1 | Engine: projection/inverse, snapping/bounds and iso model/loader fields | S0 | Pure geometry and validation cases pass. |
| G2 | Engine: direction mapping, seated selection, transition/contact/blink resolving | G1; character rig fixture | Four facings and seat phases publish correct frames/contacts. |
| G3 | Engine: part depth queue, renderItems and editor geometry | G1; furniture part fixture | Multi-tile and seat ordering fixtures pass. |
| A1 | Environment art: wood/checker and transition cells | S0 | 6×6 seam and transition review. |
| A2 | Environment art: 15 wall modules and joined rear walls | S0 | L/R endpoint and door/window checks. |
| A3 | Furniture art: desk profiles, states, chair split | S0 | Four orientations × two contact profiles align. |
| A4 | Furniture art: sofa, rug, TV cabinet, side table | A3 establishes part convention | Four orientations, empty/occupied recomposition. |
| A5 | Furniture art: plant, shelf, coffee, fridge, water, lamp | S0 | Four orientations, footprint/height/light checks. |
| C1 ×3 | Character art: per-design four-facing identity, idle/walk and seated rig, 4–7 hours | S0; A3 contact fixture | Approved identity, ground/seat/hand anchors. |
| C2a ×3 | Character art: per-design seated rest/thinking/waiting and sit/stand rows 16–35, 3–5 hours | Its C1 | Pelvis/contact and transition checks. |
| C2b ×3 | Character art: per-design actions 36–75 and validated derivations, 4–7 hours | Its C1 | All 76 rows present, loops/props reviewed. |
| C3 ×3 | Character art: per-design eye masks/blink copies and asymmetry review, 3–5 hours | Its C2a and C2b | Non-eye byte comparison and 38-row blink map. |
| P1 | Art: cat and shared carried props | S0 | 9 pet rows, prop attachment checks. |
| W1 | WPF: iso scene drawing, bounds and physical-pixel zoom | G1, G3; placeholder art sufficient | 1×/2×/3× geometry and DPI rendering. |
| W2 | WPF: alpha hover, names and editor/view selection | W1 | Picking, footprint rotation, view-switch cases. |
| H1 | Headless: v6 export/capability gate and coherent publication | G2, G3 | Snapshot/import cases, last-good recovery. |
| N1 | Native: parser/model and renderItems scene | H1; fixture snapshots | WPF/native world-scene pixel comparison. |
| N2 | Native: alpha picking, view bounds/caches, native regression checks | N1 | Hover/DPI/recovery cases pass. |
| I1 | Packaging: active-view manifest, row/anchor audit and 10×10 fixture | A1–A5, C3 ×3, P1; G1 | Complete iso pack and valid reachable layout. |
| Q1 | Integrator: walking/seating/depth/face-lock review and goldens | I1, G2/G3, W2, H1, N2 | All acceptance cases plus flat regression pass. |

Start G1, A1/A2, A3 and the identity/walk portion of C1 on independent lanes after S0; C1's seated rig waits for A3's contact fixture. Native and WPF lanes can use deterministic placeholder snapshots while final art is in production. G3 and A3 must agree on part/contact metadata before more furniture is drawn. The integrator merges shared-contract work before consumer implementations depend on it. Independent production can continue during review; do not divide the shared review workload by the number of lanes.

### Effort and uncertainty

One **Codex-lane hour** is one lane occupied for one wall-clock hour, including its tool/generation waits, repair and local validation. Four simultaneous lanes for one hour cost four lane-hours. These are planning ranges, not measured throughput, human work hours or a quote. Human art approval is an additional bottleneck and is not priced here.

The slice assumes reusable rigs, programmatic repeated/rest/blink rows, several safe mirrored structures with repainting, and one coherent review owner. Full conversion uses C's provisional 200 furniture families, 12–20 floor families, 20–30 wall modules, 6–10 pets and 30–50 extra props. For character coverage it uses the **53 designs in the current runtime pack read during this task**, not C's 25-design assumption; that inventory can change while other agents work. It is a planning denominator, not an exhaustive production census.

| Class/work | Slice lane-hours | Full conversion lane-hours, including slice |
| --- | --- | --- |
| Floors / transitions | 4–7 | 20–36, about 40–80 base/transition tiles |
| Walls / windows / doors | 6–10 | 30–54, about 40–60 face modules plus joins |
| Furniture / orientations / split parts / monitor states | 16–28 | 180–320, 200 families × four orientations before parts/states |
| Character rows / face lock / blink | 42–72 | 650–1,150, 53 × 304 normal + 152 blink cells |
| Pets | 4–7 | 24–44, 6–10 designs with directional rows |
| Carried props / additional ambient animation | 3–6 | 30–56, 30–50 props and 10–15 ambient loops; monitor states counted with furniture |
| Packing / manifest adapters | 5–8 | 18–32 |
| Core / navigation mapping / contacts / depth / editor model | 18–30 | 36–64 |
| WPF / editor UI / hover | 12–20 | 24–42 |
| Tracker contract / Windhawk | 16–28 | 32–56 |
| Integration / migration / goldens / regression | 10–18 | 36–64 |
| **Total** | **136–234** | **1,080–1,918** |

For 53 characters the stored-cell count is **16,112 normal + 8,056 blink = 24,168**; many blink/rest cells are deterministic copies, so stored-cell count is not unique drawing effort. C's 2,000-frame estimate assumed 25×20×4 and excluded engine/UI work; it cannot be reused unchanged for this contract. Limiting a first full roster to 25 would reduce the character planning line to roughly 305–540 lane-hours, with the other class assumptions unchanged. Recount target assets and replace these ranges after measuring one completed desk family and one completed character.

Four lanes give an optimistic slice lower bound of 34–59 elapsed lane-working hours; dependencies, serial integration and art review make **about 50–90 elapsed working hours** a more useful initial scheduling range. This is not calendar duration. Full conversion also cannot be estimated by dividing its total by four without accounting for review, contracts and migration bottlenecks. Reserve additional time if complete manual pixel redraw, more asymmetric designs or repeated generator rejection becomes necessary.

### Risks and owner-review decisions

| Risk | Consequence / response |
| --- | --- |
| Concept scale differs from production | Judge proportions beside the 40/18/12px actor/desk/seat fixture first; no direct pixel comparison with hero. |
| 76 rows are substantially more than today's 13 | Budget the expanded actions and eight typing views; aliases reduce drawing, not coverage. Owner may reduce the row contract before production starts. |
| Two desk profiles need matching monitor/keyboard art | Approve the end profile fixture early; otherwise shipping front-only requires an explicit scope change. |
| Asymmetric identity and fixed light break naive mirroring | Keep anatomical-side references, bake +1px pivot repair and repaint shade/features. |
| One sort key fails for long objects or overlapping seats | Author disjoint depth/contact parts and review both ends with walkers before multiplying assets. |
| Large character atlases/combined packs exceed limits | Separate blink sheets and view packs; group furniture atlases, retain existing safety limits. |
| Old native consumer accepts unknown numeric versions | Implement explicit version/capability rejection before handing it iso state. |
| DPI/fractional fit softens pixels | Validate device-pixel zoom at 150% DPI; keep 1× plus pan when the room does not fit. |
| Existing layout footprints differ | Keep flat intact; author iso counterparts and validate reachable seats instead of automatically shrinking desks. |
| Concurrent agents change shared files/inventory | Assign per-lane ownership and use one integrator/pack publisher; refresh only the changed contract or inventory facts at implementation time. |

Owner veto points: 40px standing bodies; (16,60) foot and (16,44) seated pelvis; 48px walls and 18/12px desk/seat heights; 76 rows with both desk profiles; four-neighbor slice movement; separate view packs/layouts; v6 iso state; fixed upper-left light and proposed ramps; 12 asset families represented by 16 room instances; native parity included in slice acceptance. Changing one of these should update this document before dependent art or engine work starts.
