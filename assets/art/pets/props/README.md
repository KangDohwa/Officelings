# Pet props

`props.png` + `props.manifest.json`: straight RGBA, 64×64 cells, anchor (32,61), seven static rows in order: dog_bed, cat_tree, water_bowl, food_bowl, ball, yarn, scratching_post. Each is frame 0, fps 1, loop false. Use nearest-neighbor.

The manifest owns interaction targets, floor approach anchors, eligible pets and the pet contact field to align. Coordinates are **source cell pixels**; apply scale once. Elevated cat-tree targets need reservation, reachability and depth/occlusion handling in an adapter. Draw pets over the bed cushion, then an optional rim foreground if desired; no separate rim layer is supplied. Hamster ball, robot and fixed-perch parrot do not eat from these floor bowls.

These are suggested placements, not installed furniture/collision definitions. Cat-tree contacts were checked on the supplied static art; no climb animation or scratching-paw row is supplied.
