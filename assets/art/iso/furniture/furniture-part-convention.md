# Furniture part convention — A3/A4

S0 and iso-spec govern this production handoff. All cells are native 1× RGBA; alpha is 0/255, shadows alone use 0/48/96. No runtime pixel rotations or mirror transforms. Local origin D is the minimum occupied tile center. Destination = snapped P(instance.x,instance.y) − the explicit clip anchor. A chair has its own ground-centered cell; add the desk's chairOffset once.

## Names and source origins

`<family>__<role>__<facing>__<nn>` is stable across states. Names include state suffixes for screens. Roles are ground/rear/front/monitor/screen/side/edge/shadow. Manifest clips carry the physical anchorChecks and each part's ground depthAnchor. Transparent source RGB is zero.

| Family | Cell | SE / NW origin | SW / NE origin | Footprint at rotation0 | Height |
| --- | --- | --- | --- | --- | --- |
| desk | 128×128 | (76,72) | (52,72) | 1×2 | desktop18, monitor36 |
| chair | 128×128 | (64,80) | (64,80) | 1×1, ground centered | cushion12, back28 |
| sofa | 128×128 | (96,56) | (32,56) | 1×3 | cushion12, back24 |
| rug | 192×128 | (112,40) | (80,40) | 2×3 | 0 |
| tv-cabinet | 128×128 | (76,72) | (52,72) | 1×2 | cabinet16, TV38 |
| side-table | 128×128 | (64,72) | (64,72) | 1×1 | 16 |

Orientations are outward usable fronts: SE=0, SW=90, NE=270, NW=180. Atlas/preview ordering is SE,SW,NE,NW. A desk worker and its chair face opposite the desk. Sofa seated facing equals sofa front. Rotations reproject model geometry and repaint screen-left faces lighter; they never move the light. Mirrored source provenance must record pivot correction and repaired shading/accessories if used by a later lane.

## Masks and ordering

Static part masks are disjoint. Their union reconstructs the complete empty object exactly. Rear/top surfaces are divided along ground cells, and long visible faces have per-cell strips. No division by arbitrary source y bands. Screen overlays fill holes in the static housing; their separate mask is the only region allowed to change. The rear CRT/TV view has a small status LED mask because the screen is hidden.

Sort by `(instance.x+instance.y+depthAnchor.i+depthAnchor.j, isoZOrder, instanceId, partId)` ascending. Ground keys are independent of sprite height/source anchor. Rug pass is floor-props, shadow pass shadows, other parts mixed scene. Coarse maximum-footprint depth is insufficient for desk/sofa faces.

When an actor is attached, Core substitutes its seat-ground depth only for assembly parts with `seatGroupId`, and applies rear −20 → actor0 → desk-front10 → chair-front20. The desk front near the keyboard shares that group. Walkers retain their own depth; do not globally raise the assembly. Per-edge `depthAnchor` remains the ordinary walker key. Sofa uses rear−20/front20 at its cushion contact; other strips retain their own ground anchors.

The chair's physical backrest is assigned to front for NE/NW sitters because it occludes the back-facing torso. For SE/SW sitters it belongs to rear. Seat/contact pixels are never debug markers in production PNGs.

The same rule applies to a sofa's near backrest in NE/NW: it belongs to the front occlusion strips. Screen-mask PNGs use opaque cream texels solely as binary mask data. Separate shadow manifests are connected through each variant's shadow descriptor.

## Exact desk front profile

| Desk | Worker/chair | Chair A | Pelvis C from D | Keyboard hand pair from D |
| --- | --- | --- | --- | --- |
| SE | NW | (1,0) | (32,4) | (19,−8),(21,−8) |
| SW | NE | (1,1) | (0,20) | (11,8),(13,8) |
| NE | SW | (0,−1) | (32,−28) | (19,−28),(21,−28) |
| NW | SE | (−1,1) | (−64,−12) | (−53,−12),(−51,−12) |

Only seatProfiles[id=front] ships. `chairSeatAnchor` is C, not the source pelvis. Actor destination is P(D)+C−(16,44). Chair destination is P(D)+P(A)−(64,80). All typing phases retain the pair. End profile is reserved and unsupported. Monitor is at A+3/4 f, keyboard A+3/8 f.

Sofa's SE approach is (1,1), into-sofa vector (−1,0), seatGround=(1/4,1), pelvis C=(−24,8). Explicit rotated points are published in sofa.iso.json. Navigation stays on the walkable approach; it does not move onto blocked cushion cells.

## Source versus adapter

`.iso.json` files are private lane exports. `parts` select clips with explicit direction, anchor, depth and tie rank. `linkedChair` selects iso/chair in the worker facing with its own cell origin; packaging may adapt this to the runtime chair hooks. Empty references and screen masks are validation assets, not catalog entries. Only the packaging lane publishes runtime adapters or writes pack.
