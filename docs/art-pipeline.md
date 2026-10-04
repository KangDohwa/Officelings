# Art pipeline

Project runtime pixel art was generated for Vibedotcom in a 2D chibi style, then processed locally. [sprite-gen](https://github.com/aldegad/sprite-gen) (Apache-2.0) was the generation tool. [pixel-agents](https://github.com/pixel-agents-hq/pixel-agents) (MIT) was studied as a design reference; no code or runtime art was copied. See the [design notes](pixel-agents-notes.md) and [third-party notices](../THIRD_PARTY_NOTICES.md).

The art was generated with an AI image model. Project art/assets are [CC BY 4.0](../assets/LICENSE.md). Attribution: Vibedotcom art by KangDohwa (https://github.com/KangDohwa/Vibedotcom). Review the applicable model terms before redistribution. Official logos and brand icons are excluded and have a separate [local overlay guide](../assets/brand/README.md).

## Flat and iso packs

The steps below describe the flat pack in `assets/pack/`. The app also embeds a separate assembled iso pack in `assets/art/iso/pack/`, with `projection:"iso2to1"`, hash-named sheets and its own layout. Current iso uses three designs (dev-hoodie-v2, punk-girl, professor), four facings, desk seating, carried report art and a cat in a twelve-seat room. Source furniture-v3 revisions and the embedded runtime pack are separate: a source-art commit alone does not prove that a given app build includes it.

Iso floor cells are 64×32, character cells 32×64, cat/prop cells 32×32; furniture generally uses 128×128 cells (rug differs). Geometry, split depth layers, contacts, blink and alias rules are in [S0](iso-slice-s0.md) and [iso integration](iso-integration-contract.md). S0's v6/native export plan is not the current Tracker output. Do not run the flat pack builder as if it produced the iso pack. See [current captures and regeneration](img/README.md) for both views; inspect the rendered pack after changing source art.

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
dotnet build Vibedotcom.sln
```

This order follows source dependencies. Coordinate writes with anyone producing the same characters or pack. Face lock/seated skip incomplete or stale inputs. Their current publisher stages immutable `.generations/<hash>/` files and commits one root manifest under a writer lock. Read the file paths in that manifest rather than assuming a root PNG is current. Old generations remain for readers holding an old manifest. The pack builder now stages output and locks its manifest replacement too. The [app review](review-app.md) records the earlier publication and concurrent-builder defects; these recent changes still need release verification. A completed command is not proof that every input was accepted.

For one character, use the `--manifest`, `--sheet` or `--character` examples in the focused guides. Rebuild the pack whenever art changes. `build_pack.py` writes `assets/pack/assets.json` and content-addressed sheets; runtime export then produces the [runtime manifest](asset-format.md). Production manifests and runtime manifests have different schemas.

Characters use 32x64 source cells and tiles use 32x32. Retain straight/binary alpha and nearest-neighbor placement where the production guide requires it. Pack normalization changes size/anchors, so raw source coordinates must not be reused after normalization. An ID hash chooses a roster design and probes unused eligible designs. Core chooses the final frame and seat transform; consumers do not create their own character-selection rules.

## Checks

```powershell
python -B -m unittest discover -s tools -p test_build_pack.py
python -B tools/test_anim_normalize.py
dotnet test Vibedotcom.sln --no-build
Start-Process src/Vibedotcom.App/bin/Debug/net10.0-windows/Vibedotcom.App.exe -WindowStyle Hidden -ArgumentList '--demo --smoke-test artifacts/engine-qa' -Wait
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
