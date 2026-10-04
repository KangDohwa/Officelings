# dev-hoodie C1 → C2 → C3 pilot

## Handoff status

Character files and numerical validation are complete. Visual pilot acceptance is **conditional**: the two back-facing desk composites obscure too much of the head. Do not release the next character jobs solely from the checker result. Human animation/identity approval and G3/Q1 scene review remain outstanding.

Made: `dev-hoodie-iso.png` (128×2432, 152 normal cells); `dev-hoodie-iso-blink.png` (128×1536, 96 phase-matched cells); `dev-hoodie-eyes-se.png` and `dev-hoodie-eyes-sw.png` (128×2432 each); the requested `dev-hoodie-iso.manifest.json` and identical checker-discoverable `dev-hoodie.iso.json`; `iso-contact.png` (idle/walk, 4×), `iso-rows-review.png` (all 38 rows, 3×), `iso-vs-flat.png`, and `iso-seated-review.png` (A3 parts, all four front profiles, 3×). Repeated still phases and aliases are reused pixels, not 248 unique drawings.

## Repeatable method

1. Read the revised spec/S0/asset format and view the hero, board, deterministic fixture and original identity. Original reference: `assets/art/roster/dev-hoodie/locked/dev-hoodie.png`; seated contract/reference: `assets/art/seated/dev-hoodie/`. The comparison includes untouched flat source, a display-only 30px flat body, and the 40px iso body.
2. Use sprite-gen `gen --provider codex --transparent --alpha-mode chroma --chroma-key magenta` for one SE master, then one NE back reference. Exactly one generation ran at a time. Prompts, cleaned/raw PNGs and actual provider reports are in `runs/pilot/`. No video or motion-row regeneration was used; the explicitly requested programmatic derivation owns the animation.
3. `normalize.py` does binary alpha cleanup, one reference-only nearest-neighbor fit to a 22×40 native body at (5,21), then material/identity palette snapping. It darkens the generator's pale hair glints. There is no per-frame fitting/recentering. The declared 58-color identity palette comes from opaque pixels in the original locked sheet; final normal art uses 26 exact approved colors.
4. `build_pilot.py` retains direction-specific head references and authors an integer hoodie/leg/arm rig underneath them. SE eye landmarks are repaired once into a diagonal face. SW/NW use baked x′=32−x (flip +1), then screen-left lighting repaint preserving curl contrast. Hair, hoodie and neck headphones have no left/right badge/text/accessory asymmetry. The phone is separately posed on the near right arm in SE and far right arm in SW; it is not an uncorrected mirrored device pose. Props are separate bindings, with phase hand anchors; no cup/paper/device is baked into the body.
5. Four distinct walk and typing phases use fixed foot/keyboard contacts. Seated waiting holds thinking phase 0. Sit/stand use the exact S0 contact schedule; raising is the validated reversed lowering sequence. Sit endpoint equals seated rest; stand endpoint equals idle. Source head translation is (0,0) throughout; only `surprised` changes open-eye bytes. Blink closes the same 12 eye texels per facing in every eligible phase.
6. `review_contacts.py` reads A3's `furniture-part-convention.md` and published workstation split parts, places the actor by C−(16,44), and composes the published depth/tie ordering. A3 arrived before seated review; no substitute furniture was used. Its input hashes and all four C/H proofs are retained in `runs/pilot/contact-proof.json`.

Rebuild from the saved references, from the workspace root, with `C:/Users/<user>/.codex/skills/sprite-gen/.venv/Scripts/python.exe`:

```text
assets/art/iso/characters/dev-hoodie/runs/pilot/normalize.py
assets/art/iso/characters/dev-hoodie/runs/pilot/build_pilot.py --stage final
assets/art/iso/characters/dev-hoodie/runs/pilot/review_contacts.py
tools/iso_check.py assets/art/iso/characters/dev-hoodie --tolerance 0
```

## Actual checks and time

- C1 prefix and C2 full-row/alias checks passed at their handoffs; logs are `runs/pilot/check-c1.txt` and `check-c2.txt`. Final `check-final.txt`: **17 entries, 12 PASS, 5 inapplicable SKIP, 0 FAIL**, using exact RGB tolerance 0. An initial strict check caught #EAAA2C, one channel away from identity #EAA92C; it was corrected, and the failed strict log is retained separately.
- The final gate compares all 96 normal/blink cells outside their eye masks. `face-lock-proof.json` additionally proves 48 non-eye head comparisons and 44 non-expression eye comparisons per front facing. Mask coordinates are identical across every frame of the same facing. No NE/NW masks or blink placeholders exist.
- A3 C/H coordinates match exactly, and all **32** physical typing fingertip checks are within 1px. Rig assertions verify four distinct gait/typing phases, exact reversal and equal transition endpoints. The 8fps `runs/pilot/walk-four-facings.gif` is a playback review artifact; playback smoothness was not independently approved. No application build or WPF/runtime behavior test was run.
- Lane occupancy: approximately **0.30 lane-hours (18 minutes)**, starting 2026-10-03 19:46:45 UTC, including reading, tool waits, derivation, repair and static QA. The two provider reports measure **40.000s + 51.047s = 91.047s** generation latency; some derivation/reading overlapped those waits, so do not add that latency to lane occupancy again. Most time was reference/rig repair and review. This is elapsed agent occupancy, not human art hours or a fully approved production-throughput result.

## Honest visual judgment / proposed contract follow-up

The isolated front views are recognizably the same dev-hoodie: black curly afro, brown skin, mustard hoodie, neck headphones, navy cargos and cream sneakers. Warm palette/chibi family matches the hero/board. The afro is rounder, the hoodie has fewer clusters, and the procedural legs/gestures are more mechanical than the original flat art. Phone/coffee depend on P1 overlays for clear reading. Sleep has a restrained seated recline and repeated breathing phases. These are pilot-quality poses, not a claim of final animation polish.

The actual A3 desk composites are the main unresolved visual problem. In the local published-parts queue, visible head texels are NW worker **165/399** at the SE desk and NE worker **168/399** at the SW desk; face-visible workers retain **362/369** and **358/369**. The back views do not show the prominent afro as clearly as the hero. Occluding part IDs/counts are in `contact-proof.json`; this diagnoses the composite, not an independently established A3 or renderer bug.

Before multiplying characters, review the frozen **28px chair back / seated head / front-part masks** together and add an explicit head-visibility visual acceptance criterion. Do not infer success from contact coordinates alone. If a raised seated head or stronger recline is chosen, clarify whether the ≤1px head rule applies within each clip or across different action rigs; the current literal reading makes action-specific head repositioning unavailable. Standing 40px/cell/foot/pelvis geometry has not been revised by this lane. Resolve this through the integrator's spec/A3/G3 review before treating the measured 0.30 hours as a scalable character estimate.
