# Art pipeline

Project runtime pixel art was generated for Officelings in a 2D chibi style, then processed locally. [sprite-gen](https://github.com/aldegad/sprite-gen) (Apache-2.0) was the generation tool. [pixel-agents](https://github.com/pixel-agents-hq/pixel-agents) (MIT) was studied as a design reference; no code or runtime art was copied. See the [design notes](pixel-agents-notes.md) and [third-party notices](../THIRD_PARTY_NOTICES.md).

The art was generated with an AI image model. Project art/assets are [CC BY 4.0](../assets/LICENSE.md). Attribution: Officelings art by KangDohwa (https://github.com/KangDohwa/Officelings). Review the applicable model terms before redistribution. Official logos and brand icons are excluded and have a separate [local overlay guide](../assets/brand/README.md).

## Flat and iso packs

The steps below describe the flat pack in `assets/pack/`. The app also embeds a separate assembled iso pack in `assets/art/iso/pack/`, with `projection:"iso2to1"`, hash-named sheets and its own layout. Pack v5 uses eight designs (dev-hoodie-v2, punk-girl, professor, glasses-bun, hanbok-modern, red-braid, cap-teen, labcoat-tall), four facings, desk seating, carried report art, wall/decor v3 and a cat/shiba/hamster pool in a twelve-seat room. Source revisions and the embedded runtime pack are separate: rebuild and inspect the app after integrating art.

Iso floor cells are 64×32, character cells 32×64, pet/prop cells 32×32; furniture generally uses 128×128 cells (rug differs). Geometry, split depth layers, contacts, blink and alias rules are in [S0](iso-slice-s0.md) and [iso integration](iso-integration-contract.md). S0's v6/native export plan is not the current Tracker output. Do not run the flat pack builder as if it produced the iso pack. See [current captures and regeneration](img/README.md) for both views; inspect the rendered pack after changing source art.

The office chair uses a square 5.5×5.5 cushion matching the backrest width, radius 0.5 corners, z10.5..12 thickness and a 0.25 inset waterfall edge. Spokes, casters and lift are smaller. `iso_workstation_geometry.py` derives desktop/input depths from retained v3 geometry and support depths from committed structural alpha. It bakes disjoint desk remainders and a rank10 desk-over-actor part alongside the three chair parts. Monitor, mug and paper parts retain their original art/depth. Keep a complete copy of the current iso pack before running `python -B tools/iso_seated_geometry.py` and `python -B tools/build_iso_pack.py --seated-only`; the latter preserves shipped character and blink pixels and other art. After merging another pack lane, apply the seated-only refresh to its complete pack.

Run `python -B tools/check_iso_chair_fit.py <pack> <scratch-output> --fixture <scratch-pack>` before the change, then pass `--before <before-metrics.json> --enforce --details` after regeneration. The check records six designs, four facings and seated/transition phases. The enlarged cushion has maximum SE overhang 4px and SW 5px per side; the 4px SW gate remains failing. Chair front-lip coverage stays at 0 lap/thigh pixels, and desk-hidden actor pixels drawn above the desk fall from 19,872 to 0 in 576 engine/WPF crops. `iso_check` proves exact disjoint chair and desk unions before accepting generated transparent cells. `chairFrames` adds optional `deskClips`, `deskFront`, `deskHidden` and `deskOverlap`; masks use `z:` base64 zlib bitsets and JSON remains compact. See [chair geometry and measurements](iso-seated-occlusion.md) for all counts, images and remaining limits.

### Rebuild the iso pack

Read each source folder's NOTES/CHANGES before selection, including cap-teen/labcoat-tall `NOTES-C1-C2-C3.md` and `pets-v2/NOTES-P2.md`. `build_iso_pack.py` runs the final checker with RGB tolerance 0 before publishing and records selected paths/hashes. `CHARACTERS` is the ordered eight-design pool. Inputs include complete furniture-v3 families, env-v3 walls/decor, the cat and final `pets-v2/*.iso.json` exports. A present failing source stops publication. Tests derive roster/pet counts from this list and source/runtime manifests.

Read each source folder's NOTES/CHANGES before selection. `build_iso_pack.py` runs the final checker with RGB tolerance 0 before publishing and records selected paths/hashes. The full-source route below includes complete furniture-v3 families, character rigs, env-v3 walls/decor and pets. At `1fd86d0`, cap-teen/labcoat-tall and pets-v2 sources exist but are not in the shipped pack; packv5 owns that integration. A chair-only change must use `--seated-only`, since the full builder automatically includes those candidates and changes catalog/layout/counts. A present failing source stops publication.

```powershell
python -B tools/build_iso_pack.py
python -B tools/build_iso_pack.py --seated-only
python -B tools/check_iso_chair_fit.py assets/art/iso/pack artifacts/iso-pack-v5/fit --enforce
python -B tools/iso_check.py assets/art/iso/pack/assets.json --tolerance 0
python -B -m unittest discover -s tools -p 'test_*.py'
dotnet build Officelings.sln
dotnet test Officelings.sln
Start-Process src/Officelings.App/bin/Debug/net10.0-windows/Officelings.App.exe -WindowStyle Hidden -ArgumentList '--view iso --demo --tags always --smoke-tags --smoke-test artifacts/iso-pack-v5/smoke' -Wait
dotnet run --project tests/Officelings.IsoChecks -- --render-seated assets/art/iso/pack artifacts/iso-pack-v5/seated
python -B tools/review_iso_seated_renders.py artifacts/iso-pack-v5/seated assets/art/iso/pack
python -B tools/check_iso_actor_visibility.py artifacts/iso-pack-v5/seated assets/art/iso/pack
```

Use a disposable Python environment with the existing `tools/requirements-build.txt` dependencies. V5 verification used `artifacts/venv/Scripts/python.exe`; `python` above means that environment's interpreter. Set `TEMP`/`TMP` to a scratch directory inside the worktree for tests and captures. Do not read live agent profiles. The 10×10 layout has ten wall attachment edges; the two-cell day window inherits right hosts 04/05, and left door 04 stays clear. Nameplate art belongs to closed leaves only. Night/blinds exports remain available without automatic switching. Multi-pet selection remains capped at two and deterministic by seed, with separate shadows and paths around furniture/desk approaches. Dog means shiba; hamster art has no ball. Smoke adds seeds 43/44/45 to the normal seed 42 and records actual character/pet sheet IDs, covering every production design and pet without a new CLI option.

The builder preserves source PNG bytes, adds the missing glasses-bun design entry and bakes chair masks for all eight rigs. The six previous normal/blink hashes are unchanged from base `1fd86d0`. Runtime JSON uses compact encoding because the complete chair bindings would exceed the 4 MiB limit with indentation. After the seated-only refresh, the referenced bundle is 4,505,029 bytes: 74 PNGs / 998,043 bytes, manifest 3,482,575 bytes and layout 24,411 bytes. The 4,194,304-byte per-JSON limit remains unchanged. Builders retain older immutable PNGs; before committing a rebuilt embedded pack, remove only sheets absent from the new manifest's `files` index while no reader needs the old pack. Core embeds the entire sheets directory. V5 removed four obsolete PNGs.

Inspect the empty, three-agent, twelve-agent, arrival, departure and 24-second demo PNGs at each zoom, every seated facing/design and pets next to furniture. The WPF queue export includes props and shared UI in scratch `runtime-sheets/`; the pixel comparator checks the full queue. `check_iso_actor_visibility.py` uses fixed authored cranial alpha for the two new designs and requires ≥90% in every exported facing/phase, with 100% typing hand skin visibility. Their old-v3 SE/SW heads were 76–78% visible; the slim sandwich yields 100%, with all-phase rear minima 91.60/91.67%. No source actor pixels or thresholds were changed. Rear central torso ROIs remain partly hidden by the physical chair back. Checker passes establish geometry/palette contracts, not human motion approval. The [v5 integration contract](iso-integration-contract.md#pack-v5-integration-base-1fd86d0) records counts, assignment remapping, PNG paths and remaining limits.

The full builder preserves source PNG bytes, supplies the missing glasses-bun design entry and bakes seated masks. The seated-only route preserves the existing shipped catalog. Runtime JSON uses compact encoding and compressed diagnostic masks to stay below the 4 MiB limit. The chairfix2 referenced bundle is 3,351,398 bytes: 63 PNGs / 843,908 bytes, manifest 2,483,086 bytes and layout 24,404 bytes. Compared with `1fd86d0`, PNGs grow 56,459 bytes and JSON shrinks 297,399 bytes. Builders retain older immutable PNGs; before committing a rebuilt embedded pack, remove only untracked sheets absent from the manifest's `files` index. Preserve tracked previous sheets for existing readers. Core embeds the entire sheets directory.

Compare the empty, three-agent, twelve-agent, arrival, departure and 24-second demo PNGs at each zoom against the hero and previous captures. Checker passes establish geometry/palette contracts, not visual approval. The [integration contract](iso-integration-contract.md#chairfix2-seated-desk-partition) and [seated record](iso-seated-occlusion.md) describe the current schema, counts, render paths and remaining limits.

## Steps

1. Generate character, prop and tile sheets with sprite-gen. Final character inputs live under `assets/art/sheets/` and `assets/art/roster/<id>/`; environment sets live under `assets/art/env2/` and later batches. Scratch runs are ignored. Review each production manifest and curated frames before proceeding.
2. Run face lock to stabilize eyes per facing and export blink frames into sibling `locked/` directories. It preserves source sheets, alpha and pixels outside the recorded patches. Read the [face-lock rules](facelock.md) and visually inspect automatic profiles.
3. Derive seated back/left/right typing rows from complete, current locked sources. [Seated documentation](seated.md) describes anchors, preserved heads and the prototype's visual limits. Re-run this step after changing a locked source.
4. Normalize props so only intended regions animate. [Normalization](anim-normalize.md) aligns integer frame shifts and fixes body pixels to frame zero. Review the masks and before/after strips; automatic masks are heuristic.
5. Build the runtime pack, then rebuild the app/tracker. The builder prefers locked/seated inputs where supported, normalizes character height and integrates rotation-specific furniture and normalized props. Core embeds `assets/pack/` and supplies frames/layers to both viewers.

## Rerun

Use an existing Python environment with Pillow and NumPy. From the repository root:

```powershell
python tools/facelock.py --all --blink
python tools/facelock_qa.py --all --self-check
python -B tools/seated.py
python -B tools/anim_normalize.py
python tools/build_pack.py
dotnet build Officelings.sln
```

This order follows source dependencies. Coordinate writes with anyone producing the same characters or pack. Face lock/seated skip incomplete or stale inputs. Their current publisher stages immutable `.generations/<hash>/` files and commits one root manifest under a writer lock. Read the file paths in that manifest rather than assuming a root PNG is current. Old generations remain for readers holding an old manifest. The pack builder now stages output and locks its manifest replacement too. The [app review](review-app.md) records the earlier publication and concurrent-builder defects; these recent changes still need release verification. A completed command is not proof that every input was accepted.

For one character, use the `--manifest`, `--sheet` or `--character` examples in the focused guides. Rebuild the pack whenever art changes. `build_pack.py` writes `assets/pack/assets.json` and content-addressed sheets; runtime export then produces the [runtime manifest](asset-format.md). Production manifests and runtime manifests have different schemas.

Characters use 32x64 source cells and tiles use 32x32. Retain straight/binary alpha and nearest-neighbor placement where the production guide requires it. Pack normalization changes size/anchors, so raw source coordinates must not be reused after normalization. An ID hash chooses a roster design and probes unused eligible designs. Core chooses the final frame and seat transform; consumers do not create their own character-selection rules.

## Checks

```powershell
python -B -m unittest discover -s tools -p test_build_pack.py
python -B tools/test_anim_normalize.py
dotnet test Officelings.sln --no-build
Start-Process src/Officelings.App/bin/Debug/net10.0-windows/Officelings.App.exe -WindowStyle Hidden -ArgumentList '--demo --smoke-test artifacts/engine-qa' -Wait
```

Choose checks for the changed step. Pixel-preservation scores check equality, not anatomy, gait, hand contact or loop quality. Inspect contact sheets and playback too; retain honest limitations in the character notes. Use only demo data for screenshots.

## Production references

- [Base art manifest](../assets/art/MANIFEST.md).
- Environment batches: [env2](../assets/art/env2/MANIFEST.md), [env3](../assets/art/env3/MANIFEST.md), [env4](../assets/art/env4/MANIFEST.md).
- Face and motion rules: [face lock](facelock.md), [seated typing](seated.md), [prop normalization](anim-normalize.md).
- Runtime: [assets/layouts](asset-format.md), [state contract](state-schema.md), [editor](editor.md).

Character delivery notes and QA records are listed below. They are art-production records, not release certification.

| Character | Delivery | QA |
| --- | --- | --- |
| afro-sweater | [README](../assets/art/roster/afro-sweater/README.md) | [Notes](../assets/art/roster/afro-sweater/qa-notes.md) |
| apron-baker | [README](../assets/art/roster/apron-baker/README.md) | [Notes](../assets/art/roster/apron-baker/qa-notes.md) |
| astro-hoodie | [README](../assets/art/roster/astro-hoodie/README.md) | [Notes](../assets/art/roster/astro-hoodie/qa-notes.md) |
| beanie-scarf | [README](../assets/art/roster/beanie-scarf/README.md) | [Notes](../assets/art/roster/beanie-scarf/qa-notes.md) |
| cap-teen | [README](../assets/art/roster/cap-teen/README.md) | [Notes](../assets/art/roster/cap-teen/qa-notes.md) |
| cat-hood | [README](../assets/art/roster/cat-hood/README.md) | [Notes](../assets/art/roster/cat-hood/qa-notes.md) |
| chef-sous | [README](../assets/art/roster/chef-sous/README.md) | [Notes](../assets/art/roster/chef-sous/qa-notes.md) |
| cyber-courier | [README](../assets/art/roster/cyber-courier/README.md) | [Notes](../assets/art/roster/cyber-courier/qa-notes.md) |
| dev-hoodie | [README](../assets/art/roster/dev-hoodie/README.md) | [Notes](../assets/art/roster/dev-hoodie/qa-notes.md) |
| gardener-botanist | [README](../assets/art/roster/gardener-botanist/README.md) | [Notes](../assets/art/roster/gardener-botanist/qa-notes.md) |
| glasses-bun | [README](../assets/art/roster/glasses-bun/README.md) | [Notes](../assets/art/roster/glasses-bun/qa-notes.md) |
| granny-knit | [README](../assets/art/roster/granny-knit/README.md) | [Notes](../assets/art/roster/granny-knit/qa-notes.md) |
| hanbok-modern | [README](../assets/art/roster/hanbok-modern/README.md) | [Notes](../assets/art/roster/hanbok-modern/qa-notes.md) |
| hijab-designer | [README](../assets/art/roster/hijab-designer/README.md) | [Notes](../assets/art/roster/hijab-designer/qa-notes.md) |
| labcoat-tall | [README](../assets/art/roster/labcoat-tall/README.md) | [Notes](../assets/art/roster/labcoat-tall/qa-notes.md) |
| manager-suit | [README](../assets/art/roster/manager-suit/README.md) | [Notes](../assets/art/roster/manager-suit/qa-notes.md) |
| mohawk-skater | [README](../assets/art/roster/mohawk-skater/README.md) | [Notes](../assets/art/roster/mohawk-skater/qa-notes.md) |
| nurse-nightowl | [README](../assets/art/roster/nurse-nightowl/README.md) | QA details in delivery notes. |
| professor | [README](../assets/art/roster/professor/README.md) | [Notes](../assets/art/roster/professor/qa-notes.md) |
| punk-girl | [README](../assets/art/roster/punk-girl/README.md) | [Notes](../assets/art/roster/punk-girl/qa-notes.md) |
| punk-grandpa | [README](../assets/art/roster/punk-grandpa/README.md) | [Notes](../assets/art/roster/punk-grandpa/qa-notes.md) |
| red-braid | [README](../assets/art/roster/red-braid/README.md) | [Notes](../assets/art/roster/red-braid/qa-notes.md) |
| robot-intern | [README](../assets/art/roster/robot-intern/README.md) | [Notes](../assets/art/roster/robot-intern/qa-notes.md) |
| viking-sysadmin | [README](../assets/art/roster/viking-sysadmin/README.md) | [Notes](../assets/art/roster/viking-sysadmin/qa-notes.md) |
