# CHANGES — bookshelf v3

**Frozen S0 changes: NONE. Exact `docs/iso-slice-s0.md` text diff is empty.**
Projection P(i,j,z)=(32(i−j),16(i+j)−z), snap floor(v+0.5), scale1, 128×128 cells, origins SE/NW(80,72) and SW/NE(48,72), 1×2 / 2×1 footprints, height44, existing ground depthAnchor/isoZOrder/partId, light(−2,−1) and shadow(+6,+3; alpha48/96) all remain unchanged. Desk A/C/H and source seated pelvis(16,44) are untouched.

```diff
--- docs/iso-slice-s0.md (before)
+++ docs/iso-slice-s0.md (after)
# No changed lines. This file was not written.
```

Only the private shelf artwork geometry was adjusted to integer 2:1 endpoints. The former model bounds in v2's private builder were not a frozen S0 constant. Their small native-grid correction is:

```diff
- shelf bounds (i0,j0,i1,j1,z0,z1) = (-0.2,-0.35,0.2,1.35,0,44)
+ shelf bounds (i0,j0,i1,j1,z0,z1) = (-0.1875,-0.375,0.1875,1.375,0,44)
```

This moves each unsnapped ideal vertex by at most 1.2px horizontally / 0.6px vertically and keeps the same occupied cells and source anchors. It corrects the malformed visible v2 silhouette rather than changing a room wall, floor grid or integration origin. Manifest differences are confined to actual physical anchorChecks and provenance; all variant metadata, part names, ordering and depth fields are retained exactly.

Four shelf views were hand-pixeled on the native grid. Books vary by row in height/width/color/order. SE/SW show the front; NE/NW show the actual back. Screen-left planes are repainted lighter in each cell, with no runtime reflection. Two native drawing passes, zero new image-generation calls: the deterministic redraw was sufficient for this rectilinear 44px asset. Original v2 and all other lanes remain the fallback unchanged.

Output files are only under `assets/art/iso/furniture-v3/utility-shelf/`. Runtime adapters/packs were not modified.
