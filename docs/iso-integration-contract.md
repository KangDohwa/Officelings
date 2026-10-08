# Iso integration contract (supervisor, 2026-10-04)

## Current implementation note

The text below records the integration handoff; acceptance targets are not measured guarantees. Current App uses the embedded `IsoPack`, `IsoOffice`, `IsoView` and a separate `IsoWindow`. Pack v5 has a 10×10 room with twelve seats in six desk pairs, eight character designs, wall decor v3 and a cat/shiba/hamster pool, with at most two pets visible. Only desk seating is simulated for idle agents; flat rest activities, time/weather/season decoration, editor and Sharing UI are not integrated in iso. Common window modes, tags, local privacy and pets toggle are implemented. `--layout` and `--profile-idle` require flat.

Current interchange remains flat **v5**, not S0 v6; Windhawk has no iso renderer. `--view iso --demo --smoke-test OUTDIR` renders the actual engine/embedded pack with synthetic sessions, including empty/arrival/twelve-seat/departure/cat scenes at 1×/2×/3×. [Current images](img/README.md) and [app usage](app-usage.md#flat-and-isometric-views) describe verification and limitations. The ≤1% one-core CPU target below is not verified for iso.

Three lanes build the iso mode in parallel against this contract: **isopack** (pack), **isoengine** (Core simulation + scene), **isorender** (WPF). The flat mode must keep working unchanged; iso is selected by setting `view` = `flat` (default) | `iso` and CLI `--view iso|flat`. The current design pool, in order, is dev-hoodie-v2, punk-girl, professor, glasses-bun, hanbok-modern, red-braid, cap-teen and labcoat-tall. Agent IDs use the flat roster's uint32 FNV-1a over UTF-16 code units, modulo the eligible design count, followed by unused-design probing. Newcomers are processed in ordinal ID order, and existing actors retain their design. Adding designs can change assignments when an office is recreated. Furniture comes from assets/art/iso/furniture-v3 (chair, workstations, lounge-tv, utility-shelf, utility-lounge). The builder falls back to the matching unchanged furniture-v2 family if its v3 manifest set is incomplete, including separate shadow manifests. Rug, plant and floor-lamp intentionally use v2. Source paths/hashes are recorded in sourceManifests.

## 1. Pack (isopack produces, isoengine and isorender read)
`assets/art/iso/pack/assets.json` (+ hash-named sheets `assets/art/iso/pack/sheets/pack-<sha256>.png`). Same top-level shape as the flat `assets/pack/assets.json` plus `projection:"iso2to1"`; each sheet has `id`, `kind` (character|blink|floor|wall|furniture|pet|prop|shadow), `file`, `cellW`, `cellH`, `anchor{x,y}`, `rows`/`clips` (name, direction SE|SW|NE|NW, row, frames, fps, loop), `identityPalette`, and for furniture `parts` (partId, depthAnchor{i,j}, isoZOrder, seatGroupId?, hitTargetId?, anchorChecks). Character sheets carry the 38 normal rows + 24 blink rows and the SE/SW eye masks. Embed the iso pack as an assembly resource exactly like the flat pack (Core.csproj) so a build without loose files works. isopack's final report must include the schema summary actually produced; if it differs from this paragraph, the report wins and the other lanes adapt.

Version remains 1. V3 keeps part IDs, source anchors, seat groups, hit targets and furniture footprints. Monitor top is 40, TV top is 48, chair back remains 21; actual ordinary monitor depth anchors come from the v3 source. TV screenStates/glowStates are expanded into explicit variants (default/playing, idle/static, frame-00..03). The runtime reads optional variant glow and part opacity/frame; screen and glow animate together at their authored ground depth and ranks 1/2. Fixed TV states freeze both layers. Height and inputDevices metadata remain available in the JSON; no new scene API fields are needed.

### Pack v4 additions (historical)

### Chairfix2 seated desk partition

The three chair part IDs and ranks remain unchanged. Optional `chairFrames.deskClips` contains flat source/remainder clip pairs, `deskFront` selects a generated `iso-desk-sandwich` clip, and `deskHidden`/`deskOverlap` carry diagnostic coverage. Remainders retain original desk IDs/ranks/anchors; the extra part is `desk__front__<orientation>__17`, seatGroupId `seat`, rank10. Its disjoint union with remainders exactly reproduces the original structural desk pixels. Monitors, mugs and paper retain their existing parts. Empty seats use the original static desk. Core caches replacements at layout creation and selects them per pose frame; no new Scene API fields or global depth offsets are used. Ties remain chair-back -20, actor 0, desk-front 10, chair-front 20.

`seatedOracle.version` is 2; pack version stays 1. Diagnostic bitsets use `z:` plus base64 zlib little-endian bytes, and checkers accept legacy raw base64. Compact JSON is 2,483,086 bytes, below 4 MiB; referenced PNG growth is 56,459 bytes. Keep runtime and generated pack changes together. Apply the two seated regeneration commands to packv5's complete merged pack, preserving its catalog/layout/pets. At base `1fd86d0`, committed cap-teen/labcoat-tall/pets-v2 sources remain outside this lane's shipped pack; the full-source builder would include them. [Current measurements and failures](iso-seated-occlusion.md) supersede the historical v4 validation below: desk-over-body violations are 0 in tested crops, but SW cushion overhang remains 5px against the requested 4px gate.

### Pack v4 additions

`env-v3/walls.iso.json` supplies the wall panels and integrated trim. `wall-decor.iso.json` retains all day/night/blinds window assemblies, boards, clock, pictures, shelf and lights. The builder compiles each assembly edge into a wall module and preserves `decorAssemblies`. Optional module fields `slot` and `hostModule` describe mounting restrictions; layout attachments use `hostId`, the host's first endpoint and its ground depth. Both Python and Core reject mismatched faces/anchors, duplicate slots and decorations mounted over doors or existing windows. The default room uses ten attachment edges: nine accents including the two-edge day window on right hosts 04/05. The open door remains left host 04. LAB nameplates are appended only to the closed v3 door modules. Night/blinds variants are available art, with no automatic time switching.

glasses-bun's source omits a `characters` entry; the adapter creates its design from the single approved normal sheet. Pixel data, contacts, blink sheets and eye masks are preserved. Chair sandwich masks are rebuilt for all six designs.

The v4 builder discovers cap-teen/labcoat-tall and `pets-v2/*.iso.json` when present, then runs the final source checker. These folders were absent at base commit `beb346a`, so v4 had one real pet, the cat. Core selects up to two distinct pet assets from the ordinal pool using the office seed. Each pet has its own deterministic random stream; four-way paths avoid blocked furniture and reserved desk approaches. V4 tested multi-pet selection and empty pools with disposable synthetic cat-sheet copies. `IsoPetPose` and `BuildWithPets` extend the builder while the Scene API below stays unchanged.

The bundle contains 54 sheets and 62 PNGs: PNGs 827,894 bytes, compact `assets.json` 2,844,964 bytes, `layout.json` 24,404 bytes; total 3,697,262 bytes (3.53 MiB). Compact JSON keeps the complete chair metadata below the existing 4 MiB per-JSON limit. Unreferenced sheets were removed before rebuilding embedded resources. Final `iso_check --tolerance 0` results: 30 sources PASS 290 / SKIP 132 / FAIL 0; pack PASS 301 / SKIP 137 / FAIL 0. SKIP rows concern checks that do not apply to a sheet kind.

Validation: the .NET solution passed 560 tests. Python tool discovery passed 79 tests but reported a class setup error from the old five-pilot fixture adding duplicate designs; after fixing its explicit five-design list, all four seated geometry tests passed separately (450 fresh z-buffer frames). The 28 pack-builder tests are included in the 79. No production pixels or visibility thresholds were relaxed; the C# visibility check now includes the new rigs' authored head offsets and hand palettes.

Offscreen QA produced 48 scenario PNGs at 1×/2×/3× plus seven viewport/tag captures under `artifacts/iso-pack-v4/`. Best examples are `iso-twelve-working-2x.png`, `iso-three-1x.png` and `iso-demo-24.0s-3x.png`. All 48 were inspected in zoom contact sheets, with full-size representative and arrival images compared against the hero and previous `docs/img/demo-iso-2x.png`. Remaining visual limits: crowded arrivals can place tags over nearby walkers; the small wall artwork and four-frame gait remain less detailed than the hero. Live DPI/window interaction and iso CPU targets were not measured in this integration.

### Pack v5 integration (base 1fd86d0)

Designs 7/8 are cap-teen and labcoat-tall. The real pet pool is cat, dog (shiba) and hamster; the hamster walks outside a ball. All three use independent ground shadows and the existing seeded, maximum-two selection/path logic. No actor source pixels, chair geometry, proxy or visibility thresholds changed. The six previous designs' normal/blink PNG hashes are unchanged from the base pack.

The final bundle has 62 sheets and 74 referenced PNGs: PNGs **998,043 bytes**, compact `assets.json` **3,482,575 bytes**, `layout.json` **24,411 bytes**, total **4,505,029 bytes**. The JSON limit remains 4,194,304 bytes, leaving 711,729 bytes. Four obsolete, unreferenced PNGs were removed from the embedded sheets directory. Full source build followed by `--seated-only` baked 1,456 resolved chair-frame bindings into 337 deduplicated cells.

The assignment algorithm is unchanged. Running actors retain their assigned design; a recreated office now takes the same hash modulo eight, so remapping is expected. The acceptance fixture changes child punk-girl → red-braid, supervisor professor → labcoat-tall, worker red-braid → dev-hoodie-v2. These synthetic twelve-agent assignments document the recreated-office effect; 9/12 change:

| Agent ID | v4 | v5 |
| --- | --- | --- |
| iso-smoke:00 | dev-hoodie-v2 | dev-hoodie-v2 |
| iso-smoke:01 | red-braid | glasses-bun |
| iso-smoke:02 | hanbok-modern | cap-teen |
| iso-smoke:03 | glasses-bun | punk-girl |
| iso-smoke:04 | professor | hanbok-modern |
| iso-smoke:05 | punk-girl | labcoat-tall |
| iso-smoke:06 | dev-hoodie-v2 | professor |
| iso-smoke:07 | red-braid | red-braid |
| iso-smoke:08 | hanbok-modern | dev-hoodie-v2 |
| iso-smoke:09 | glasses-bun | glasses-bun |
| iso-smoke:10 | red-braid | punk-girl |
| iso-smoke:11 | dev-hoodie-v2 | cap-teen |

The old-v3 figures below come from each author's frozen chair/desk snapshot. After figures use the real slim-chair sandwich and exported WPF queue, including desk parts, props and shared UI. Heads use the same fixed cranial alpha: cap's immutable idle prefix y<38 (249/250 samples) and labcoat's authored `head-*.png`, shifted by declared phase offsets. The checker verifies head RGBA against the actor before counting final ownership.

| Character / facing | Old v3 typing head | v5 typing head | v5 minimum across all exported phases | Old → v5 typing torso ROI |
| --- | ---: | ---: | ---: | ---: |
| cap-teen SE | 77.91% | 100.00% | 100.00% | 100.00 → 100.00% |
| cap-teen SW | 76.31% | 100.00% | 100.00% | 100.00 → 100.00% |
| cap-teen NE | 96.40% | 96.40% | 94.00% | 82.28 → 87.34% |
| cap-teen NW | 91.60% | 91.60% | 91.60% | 73.24 → 78.87% |
| labcoat-tall SE | 78.03% | 100.00% | 100.00% | 100.00 → 100.00% |
| labcoat-tall SW | 76.66% | 100.00% | 100.00% | 100.00 → 100.00% |
| labcoat-tall NE | 97.79% | 97.79% | 96.32% | 75.00 → 80.56% |
| labcoat-tall NW | 92.39% | 92.39% | 91.67% | 61.11 → 66.67% |

Both new rigs exceed 90% head visibility in every exported facing/action/phase; all typing hand skin ROIs are 100%. The chair-fit checker passed 592 authored seated/transition frames: front overhang ≤2px per side, front-lip/lap coverage zero, no below-cushion lap proxy samples and exact chair part unions. The independent fresh z-buffer check covers 720 pose frames. All 768 WPF crops match their full recorded queue at 4×: 2,174,534 native opaque texels. The exporter now includes shared UI/props; omitting them previously caused a thinking-bubble comparison mismatch. The seated-owner filter also recognizes the actual `actor/body` instance.

Validation: selected sources at tolerance 0 **PASS 334 / SKIP 154 / FAIL 0** (34 manifests); pack **PASS 346 / SKIP 159 / FAIL 0**. Python discovery **85/85** and full .NET tests **561/561** passed; solution build has zero warnings/errors. Windows safe-file Python checks needed execution outside the sandbox because ancestor handles were denied; all temporary data was directed to this worktree's `artifacts/tmp`.

`--view iso --demo --tags always --smoke-tags --smoke-test artifacts/iso-pack-v5/smoke` exports 66 scenario PNGs at 1×/2×/3× plus seven viewport/tag images. Existing scenarios still use seed 42; extra `pets-seed-43/44/45` scenarios at 9/35 seconds cover every real pet. No new CLI option is needed. The smoke summary records character sheet IDs and pet IDs/coordinates. All eight normal designs and all three pets occur. Scenario contact sheets at each zoom, four all-design typing boards, furniture/pet crops and representative full-size scenes were viewed. Best paths: `artifacts/iso-pack-v5/smoke/iso-twelve-working-2x.png`, `iso-demo-24.0s-3x.png`, `artifacts/iso-pack-v5/review/pets-furniture-3x.png`, and `artifacts/iso-pack-v5/seated/*-typing-board-4x.png`.

Remaining limits, in order: (1) the physical chair back still hides 10/15 cap and 14/24 labcoat central torso samples in NE/NW; the fixed ROI excludes head/hands and no torso acceptance threshold is specified; (2) always-visible tags can cover nearby walkers in crowded arrivals, as in v4; (3) four-frame gait and small pet feet remain visually coarse. No head gate failures or observed pet/furniture/wall penetration remain in these captures. Live window/DPI interaction, human motion approval and the iso CPU target were not measured. Scratch renders and test results are not committed.

## 2. Scene API (isoengine implements, isorender consumes)
File `src/Officelings.Core/Iso/IsoContracts.cs`, namespace `Officelings.Core.Iso`. BOTH lanes create this file with IDENTICAL content so the branches merge cleanly. Do not add members; put extra types in other files.
```csharp
namespace Officelings.Core.Iso;
public enum IsoLayer { Floor, Wall, Shadow, Furniture, Character, Pet, Prop, Overlay }
/// X,Y = top-left of the sheet cell in scene pixel space (origin = scene top-left, y down). Items are sorted back-to-front by the engine; draw in list order.
public sealed record IsoDrawItem(string InstanceId, IsoLayer Layer, string SheetId, string Clip, int Frame, int X, int Y, long Depth, string? HitTargetId = null, byte Alpha = 255);
public sealed record IsoLabel(string AgentId, string Text, int X, int Y);
public sealed record IsoHit(string TargetId, string Kind);
public sealed record IsoScene(int Width, int Height, IReadOnlyList<IsoDrawItem> Items, IReadOnlyList<IsoLabel> Labels, long Revision);
public interface IIsoOffice
{
    /// Same inputs as the flat OfficeAnimator.Update. Revision increases only when something visible changed (renderer may skip redraw when unchanged).
    IsoScene Update(IReadOnlyList<AgentInfo> agents, DateTimeOffset now, double deltaSeconds);
    IsoHit? HitTest(int sceneX, int sceneY);
    /// Seconds until the next visible change if nothing external happens (0 = now, double.PositiveInfinity = static).
    double NextChangeSeconds { get; }
}
```
The concrete engine class is `IsoOffice : IIsoOffice` with constructor `IsoOffice(IsoPack pack, int seed)`; `IsoPack.LoadDefault()` loads the embedded iso pack (and `IsoPack.Load(string dir)` for tests).

## 3. Renderer (isorender implements)
`src/Officelings.App/IsoView.cs` (new, `FrameworkElement`, no edits to flat OfficeView beyond what hosting needs): draws `IsoScene` items in order from the iso pack sheets (frozen bitmaps, nearest-neighbor, integer zoom 1x/2x/3x), static layers (Floor, Wall, static furniture) retained in a cached bitmap redrawn only on Revision change/zoom, timer only while `NextChangeSeconds` is finite, name tags using the existing tag modes (hover/icon/always) and provider icons from the existing code path, hit-testing through `IIsoOffice.HitTest` for hover. Window modes (transparent, topmost, click-through, tray, settings, privacy) must work exactly like flat mode because `Program.cs` hosts either view; keep Program.cs edits small (a `view` switch that constructs IsoView+IsoOffice instead of OfficeView+OfficeAnimator). QA path: `--view iso --demo --smoke-test OUTDIR` renders offscreen PNGs (1x/2x/3x, several times, empty and 12-agent scenes) without any visible window. Until the real engine/pack merge in, develop against a small test double `IIsoOffice` that builds a hand-made scene from the real art sheets under assets/art/iso (the renderer lane may create a throwaway pack loader for its own tests; the real pack loader comes from isoengine/isopack).

## 4. Quality bar (supervisor will review the rendered PNGs against assets/art/iso-concept/iso-office-hero.png)
Strict 2:1 projection, per-spec depth sorting with no characters drawn through desks/chairs, seated typists visible (head, hands, forearms) at all four desk orientations, walkers path around furniture, shadows, the cat wandering and sleeping, no flicker/jitter between frames, no per-frame allocations in the idle path, idle CPU target as the flat mode after the perf work (<= 1% of one core when nothing animates).
