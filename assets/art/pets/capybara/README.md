# Capybara

`capybara.png` + `capybara.manifest.json`: 32×32 cells, four columns, straight binary RGBA, anchor **(16,29)**. Pet silhouette is about 16–22 px. Use nearest-neighbor.

`idle` and `blink` are facing **choices**, in down / left / right / up order. Never cycle them as animation. The manifest's `frame_layout.rows` owns zero-based source rectangles.

| Row | State | Frames | FPS | Playback |
| ---: | --- | ---: | ---: | --- |
| 0 | `idle` | 4 | 1 | select facing |
| 1 | `walk_down` | 4 | 5 | loop |
| 2 | `walk_left` | 4 | 5 | loop |
| 3 | `walk_right` | 4 | 5 | loop |
| 4 | `walk_up` | 4 | 5 | loop |
| 5 | `sit` | 2 | 2 | loop |
| 6 | `sleep` | 3 | 1 | loop |
| 7 | `eat_drink` | 3 | 3 | loop |
| 8 | `play` | 4 | 5 | once, hold last |
| 9 | `react` | 2 | 5 | once, hold last |
| 10 | `blink` | 4 | 1 | select facing |

Each facing has one accepted head in `heads/`. Walk/sit/play copy those pixels, with integer translations for jumps. `blink-*.png` closes calibrated eye pixels for 100 ms; up has no visible eyes and intentionally matches its normal idle. Schedule visible blinks during stationary idle/sit, about every 3–6 seconds. Sleeping uses closed eyes. Only tongue/beak motion and reacting ears/antenna are designated facial/head exceptions. See manifest `pet.headLock` for native rectangles and eye coordinates.

Action facings and `sleepContact` / `muzzleContact` are in the manifest. To nap on a bed/sofa, align `sleepContact` to the cushion target, keep simulation depth at the floor point and apply furniture foreground occlusion separately. Floor bowls align the muzzle, not the pet's feet. Blink PNGs match idle bodies: for sit/head-bob, transfer only the two-pixel eye delta, translated with the head; do not substitute the idle body.

Limitation: Very calm paw shifts replace an energetic jump. The orange stays attached to the head; sleep/sit bodies use simpler shading.

Generation: provider codex, still-image component-row direction seeds; all motion is deterministic pixel assembly. No video, runtime integration, application build or commit. `../qa/` contains motion GIFs and static 4× contacts. Static pixel checks establish consistency; they do not certify natural live playback.
