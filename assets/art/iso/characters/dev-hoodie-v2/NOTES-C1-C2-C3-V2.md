# dev-hoodie character pilot v2

Numerical handoff is complete; **visual acceptance remains conditional**. This lane writes only here and its ignored `runs/v2/`. Flat art, v1, furniture, pack, code and shared documents were not edited. No commit.

Made the requested normal atlas (128×2432, 38×4), blink atlas (128×1536, 24×4), SE/SW eye masks, both manifest filenames, `iso-contact.png`, `iso-rows-review.png`, `iso-seated-review.png`, `iso-vs-flat.png`, and `v1-vs-v2.png`. [Comparison](v1-vs-v2.png), [seated review](iso-seated-review.png), [final checker output](validation.txt). Props and bubbles stay separate.

## Drawing and derivation

Six selected drawing masters: SE/NE standing, walk contact, seated typing. Eight codex still-image calls ran serially: SE standing used all three permitted attempts; the other masters used one each. SE attempts 1/2 left too little visible face; attempt 3 (`se-idle-a3.png`, copied to `se-idle.png`) was selected. Original locked flat crops own identity; the hero top-left dev worker owns volume/mood. No video, flat shear or rotation was used.

Sprite-gen removed magenta through its chroma pipeline. Each master was uniformly fitted once to its native grid, snapped to identity/material ramps and binary alpha, then used as fixed parts. Head, generated torso, cargos and sneaker pixels remain drawing-derived; arms/contact pixels and joint connections were repaired. Four distinct gait/typing phases, rest/actions, reversed transitions and phase-matched blink are baked derivations, not 248 separately generated drawings. SW/NW use x′=32−x, palette relighting including skin/sneakers, screen-left eye catchlight repair, and separately authored anatomical right-hand phone poses.

Final bodies use 21 exact approved RGB colors. Standing height is 40px; foot (16,60), seated pelvis (16,44), keyboard pairs and transition schedule remain S0. Front head/neck occupies 20px of the standing 40px; rear head occupies 18px. Source seated head is translated −3px; lowering uses 0,−1,−2,−3px and raising reverses that. Pixels are never fitted or recentered per animation frame.

## Checks and measured time

`python tools/iso_check.py assets/art/iso/characters/dev-hoodie-v2 --tolerance 0`: **17 entries, 12 PASS, 5 inapplicable SKIP, 0 FAIL**. `validation.txt` and `runs/v2/check-final.txt` retain the actual output. Additional assertions prove 152 canonical-head byte comparisons, 152 connected body silhouettes, distinct gait/typing phases, exact transition reversal/endpoints, skin at all four idle/fallback grips, and 32 typing fingertip contacts within 1px. Repairs caught a detached gait calf, arm interference with neck pixels and misplaced idle grips. No app build, runtime test or independently approved playback was performed.

Lane occupancy approximately **0.39 lane-hours / 23 minutes**, starting 2026-10-03 20:06:13 UTC, through final checks/notes around 20:29 UTC. Includes reading, generation waits, repair and review. Eight provider reports total **373.951 seconds**; those waits overlap lane work and are already included. Prompts/raw images/reports, selected master hashes, derivation proofs, static reviews and an 8fps walk GIF remain under `runs/v2/`. Fourteen images were viewed, including references and composites.

## What still looks off / spec follow-up

The face is larger/readable and the generated shoulders, hood and bent legs have more volume than v1. I do **not** claim it matches the hero's charm. Standing bodies remain narrow, sneaker clusters busy, gestures/gait mechanical, and sleep a restrained seated recline. Still-phase duplicates and motion fallbacks are intentional. Human identity/animation approval remains necessary before scaling to other characters.

Old A3 parts still obscure the back-facing sitter: visible head/neck texels are NW **161/380 (42.4%)**, NE **157/380 (41.3%)**; SW **354/368 (96.2%)**, SE **345/368 (93.8%)**. `contact-proof.json` names the occluders. The supplied furniture room is used only for C/H alignment; its overlay is explicitly not depth acceptance. Recheck with the parallel furniture v2, preserving all frozen contacts. Character contacts alone do not prove head visibility or hero-like seating.

The integrator should specify a seated-head visibility gate and clarify face-lock displacement **within a stable clip versus between action rigs/transitions**. This v2 preserves identical head bytes at declared offsets, with 1px changes between adjacent lowering phases; if the ≤1px rule means total displacement across an entire transition or across all actions, this interpretation needs explicit approval. No frozen spec/constants were changed here. Do not use the measured 0.39 hours as approved production throughput.

Rebuild: run `runs/v2/normalize.py`, `build.py`, `review_contacts.py`, then `verify.py` with the sprite-gen venv Python from the workspace root. Defaults were not saved; curation was skipped for this lane.
