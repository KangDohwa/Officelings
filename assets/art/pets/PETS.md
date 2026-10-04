# Office pets pack

Eight small chibi pets, seven props, and eight baked coat variants. Production files stay in this directory. Load `<id>/<id>.png` with its sibling manifest and README. `preview-pets.png` shows all four idle facings and the right-facing movement strip on the existing oak floor at exact **3× nearest-neighbor**.

| ID | Character | Movement | Extra coats |
| --- | --- | --- | --- |
| `shiba` | Tan shiba-like dog, cream muzzle, curled tail | 4-direction walk | cream, sesame |
| `tuxedo-cat` | Black-and-white cat, ivory bib and paws | 4-direction walk | blue-grey, chocolate |
| `orange-tabby` | Ginger kitten with dark stripes; distinct from env2's cat | 4-direction walk | silver, brown |
| `corgi` | Large ears, white blaze, long low body, short legs | 4-direction walk | sable, cream |
| `hamster-ball` | Golden hamster in a clear blue-rimmed rolling ball | 4-direction roll, 3 frames each | — |
| `robot-buddy` | Cream/teal robot, amber display eyes, antenna, wheels | 4-direction roll, 4 frames each | — |
| `parrot` | Green parrot on a fixed walnut perch | head bob / wing flap; stationary | — |
| `capybara` | Calm caramel capybara with an orange and leaf on its head | slow 4-direction walk | — |

## Coordinates and sheets

Pet cells are **32×32**, no gutters, four columns. Silhouettes are roughly **16–22 px**, smaller than the office people. Foot reference is **(16,29)**. Straight RGBA, binary alpha, zero RGB in transparent pixels; empty trailing cells stay transparent. Use nearest-neighbor sampling and integer placement. Dark selective outlines, small fixed eye pixels and warm flat coat ramps match the office art. The parrot adds a moss-green ramp; hamster ball reflections use dusty blue.

These are the existing **production** `animation.rows` / `frame_layout.rows` conventions, not the runtime version-1 asset manifest or Pixel Agents' 96×96 pet schema. The manifest's explicit rectangles are authoritative. No renderer, pack builder, runtime state, or layout was modified. An adapter must map the clips and source-pixel anchors into its runtime.

## Row order and timing

Land animals have **128×352** sheets. `idle` and `blink` are direction selectors: columns **0 down, 1 left, 2 right, 3 up**. Their four columns are never played as an animation.

| Row | State | Frames | FPS | Playback |
| ---: | --- | ---: | ---: | --- |
| 0 | `idle` | 4 facing choices | 1 | select facing |
| 1 | `walk_down` | 4 | 7; capybara 5 | loop |
| 2 | `walk_left` | 4 | 7; capybara 5 | loop |
| 3 | `walk_right` | 4 | 7; capybara 5 | loop |
| 4 | `walk_up` | 4 | 7; capybara 5 | loop |
| 5 | `sit` | 2 | 2 | loop; down-facing |
| 6 | `sleep` | 3 | 1 | loop; right-facing, closed eyes and breathing |
| 7 | `eat_drink` | 3 | 3 | loop; right-facing |
| 8 | `play` | 4 | 7; capybara 5 | once, then idle |
| 9 | `react` | 2 | 5 | once, hold last briefly; down-facing |
| 10 | `blink` | 4 facing choices | 1 | select facing for a 100 ms closure |

Hamster and robot keep this order and size, replacing rows 1–4 with `roll_down`, `roll_left`, `roll_right`, `roll_up` at **6 fps**. Hamster uses three frames and a transparent fourth cell. The hamster stays upright as rim markers move; the robot animates wheel pixels. Their rest/sleep stays in the ball/on wheels; robot `eat_drink` is a charging indicator. They do not use the supplied animal food/water bowls.

Parrot has **128×288**: row 0 `idle` (4 facing choices), 1 `head_bob` (4, 3 fps, loop), 2 `wing_flap` (4, 6 fps, loop), 3 `sit` (2, 2 fps), 4 `sleep` (3, 1 fps), 5 `eat_drink` (3, 3 fps), 6 `play` (4, 7 fps, once), 7 `react` (2, 5 fps, once), 8 `blink` (4 facing choices). Sit/sleep/eat loop. The perch is pixel-identical in every cell. There are no walk, climb or flight clips. `play` uses the wing flap; eating is a beak-peck indication without a separate perch bowl.

## Face construction and blinks

Each facing owns one accepted opaque head in `<id>/heads/`. Motion is composed from that head plus deterministic paws, tail tips, wheel/rim markers and reposed bodies. Jumps/head bobs use integer head translations. Eyes remain identical across ordinary frames of that facing. Exceptions are closed eyes in sleep/blink, tongue/beak pixels in eat/drink, and ears/antenna in react. Back views have no eyes: `blink-up.png` intentionally matches normal up idle.

Each manifest publishes calibrated head rectangles, eye points, 100 ms full-cell blink files, action facings, a sleep contact and a muzzle contact. A stationary blink can occur every 3–6 seconds. For sit or head-bob, transfer **only the eye delta** from the idle blink and translate it with the head; the full idle blink body is not a seated pose. `tools/facelock.py` was not used or changed.

Dog/cat coats are made with **sprite-gen recolor** using exact RGB maps. Each `variants/` directory contains two sheets, sibling manifests, a bake report, and head/blink crops from those baked sheets. Alpha/geometry is unchanged; eyes and outline colors are outside the coat maps. The maps are in `<id>/recolor.spec.json`. No variant was regenerated.

## Suggested office behaviours

- **Roam:** pause 3–15 seconds, choose a short reachable floor path, play the facing-specific walk/roll, then return to idle. The parrot stays perched and occasionally bobs or flaps.
- **Follow a supervisor:** occasionally choose a nearby supervisor, follow for 5–15 seconds with roughly one-second route refreshes, and stop when the target leaves or a comfortable distance is reached. These are suggested timings, not installed logic.
- **Nap on a sofa or bed:** reserve a cushion contact, approach on the floor, align `pet.sleepContact` to the cushion and loop sleep. Keep collision/depth at the floor approach point; foreground furniture must occlude the appropriate pet pixels.
- **Sit near desks:** choose a reachable point beside a desk, stop clear of the chair/work point and select sit. Occasional blinks/reacts provide activity without moving the desk occupant.
- **Eat/drink and play:** reserve the appropriate prop contact, align the muzzle for bowls, play the brief clip, then release the contact and resume idle/roam. Reaction can accompany a click or a nearby arrival; optional bubbles remain separate assets.

## Props and placement

`props/props.png` is **64×448**, seven 64×64 one-frame rows: `dog_bed`, `cat_tree`, `water_bowl`, `food_bowl`, `ball`, `yarn`, `scratching_post`. Every row is static, 1 fps, loop false; prop reference **(32,61)**. The sibling manifest records exact source-cell targets, floor approach anchors, eligible pets, and the pet contact field to align. These are placement recommendations, not collision/furniture definitions.

```text
propTopLeft = propAnchorWorld - (32,61) * scale
petTopLeft  = propTopLeft + target * scale - petContact * scale
```

Use the pet's `anchor`, `pet.sleepContact` or `pet.muzzleContact` as named by that interaction. Approach points may lie outside the prop cell. The cat tree needs elevated placement/depth handling; no climbing animation is supplied. A separate bed-rim foreground layer and scratching-paw animation are not included.

## Production and verification

Provider **codex**, sprite-gen **2.20.0**, still-image component-row pipeline. Nine calls ran sequentially: eight rows of static pet facing choices and one seven-object prop row. Existing env2 cat/vacuum, office character/sofa and wood floor were read-only style/preview references. Canonical component extraction, atlas composition and seed previews are retained in `runs/`; no equal-width cutting of generated raw images was used. Final deterministic pixel assembly is in `build_pets.py`, and the per-pet PNGs are authoritative.

```powershell
$env:SPRITE_GEN_ROOT='C:\Users\<user>\.codex\skills\sprite-gen'
$env:PYTHONDONTWRITEBYTECODE='1'
& "$env:SPRITE_GEN_ROOT\.venv\Scripts\python.exe" -B assets/art/pets/build_pets.py check
```

`qa/validation.json` records exported-file checks: dimensions, explicit frame rectangles/counts, nonempty cells, safe margins, binary alpha/zero hidden RGB, transparent trailing cells, distinct looping phases, accepted heads reused in locomotion/actions, eye-only blink differences, fixed perch and recolor alpha. `qa/pets-*-4x.png` covers the motions, `qa/props-contacts-4x.png` marks targets, and `qa/coat-variants-4x.png` compares coats. Motion GIFs are supplied for later playback review. Numeric consistency does not establish natural animation.

Final inventory: **17 sheet/manifest pairs, 603 declared frames**: eight primary pets and the seven-frame prop sheet contain 299 frames; eight coat sheets contain 304 more. Exported walking/rolling heads passed 108 exact comparisons; primary actions passed 104, including translated jump/bob heads and closed-eye sleeping heads. Eleven image views were used, mostly 4× composites. Canonical inspection retained low silhouette-similarity warnings for two direction-choice cat rows and the row of unlike props; these comparisons are not movement-loop verdicts.

Weak items: the reposed sleeping bodies are small and simpler than the generated standing art; paw cycles have limited shoulder articulation and corgi steps are subtle. Hamster rolling uses rim markers, robot rolling uses stylized spokes. Parrot wings are schematic, and capybara play is intentionally restrained. Orange tabby's up head was corrected programmatically because its generated view retained a profile. No live runtime playback was certified. No video, alternate provider, defaults change, curation UI, runtime-pack installation, application build, upload/deployment or git commit was performed.
