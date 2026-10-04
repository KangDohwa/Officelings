# Iso integration contract (supervisor, 2026-10-04)

## Current implementation note

The text below records the integration handoff; acceptance targets are not measured guarantees. Current App uses the embedded `IsoPack`, `IsoOffice`, `IsoView` and a separate `IsoWindow`. Pack v4 has a 10×10 room with twelve seats in six desk pairs, six character designs, wall decor v3 and a cat. Only desk seating is simulated for idle agents; flat rest activities, time/weather/season decoration, editor and Sharing UI are not integrated in iso. Common window modes, tags, local privacy and pets toggle are implemented. `--layout` and `--profile-idle` require flat.

Current interchange remains flat **v5**, not S0 v6; Windhawk has no iso renderer. `--view iso --demo --smoke-test OUTDIR` renders the actual engine/embedded pack with synthetic sessions, including empty/arrival/twelve-seat/departure/cat scenes at 1×/2×/3×. [Current images](img/README.md) and [app usage](app-usage.md#flat-and-isometric-views) describe verification and limitations. The ≤1% one-core CPU target below is not verified for iso.

Three lanes build the iso mode in parallel against this contract: **isopack** (pack), **isoengine** (Core simulation + scene), **isorender** (WPF). The flat mode must keep working unchanged; iso is selected by setting `view` = `flat` (default) | `iso` and CLI `--view iso|flat`. The current design pool is dev-hoodie-v2, punk-girl, professor, glasses-bun, hanbok-modern and red-braid. Agent IDs use the flat roster's uint32 FNV-1a over UTF-16 code units, modulo the eligible design count, followed by unused-design probing. Newcomers are processed in ordinal ID order, and existing actors retain their design. Adding designs can change assignments when an office is recreated. Furniture comes from assets/art/iso/furniture-v3 (chair, workstations, lounge-tv, utility-shelf, utility-lounge). The builder falls back to the matching unchanged furniture-v2 family if its v3 manifest set is incomplete, including separate shadow manifests. Rug, plant and floor-lamp intentionally use v2. Source paths/hashes are recorded in sourceManifests.

## 1. Pack (isopack produces, isoengine and isorender read)
`assets/art/iso/pack/assets.json` (+ hash-named sheets `assets/art/iso/pack/sheets/pack-<sha256>.png`). Same top-level shape as the flat `assets/pack/assets.json` plus `projection:"iso2to1"`; each sheet has `id`, `kind` (character|blink|floor|wall|furniture|pet|prop|shadow), `file`, `cellW`, `cellH`, `anchor{x,y}`, `rows`/`clips` (name, direction SE|SW|NE|NW, row, frames, fps, loop), `identityPalette`, and for furniture `parts` (partId, depthAnchor{i,j}, isoZOrder, seatGroupId?, hitTargetId?, anchorChecks). Character sheets carry the 38 normal rows + 24 blink rows and the SE/SW eye masks. Embed the iso pack as an assembly resource exactly like the flat pack (Core.csproj) so a build without loose files works. isopack's final report must include the schema summary actually produced; if it differs from this paragraph, the report wins and the other lanes adapt.

Version remains 1. V3 keeps part IDs, source anchors, seat groups, hit targets and furniture footprints. Monitor top is 40, TV top is 48, chair back remains 21; actual ordinary monitor depth anchors come from the v3 source. TV screenStates/glowStates are expanded into explicit variants (default/playing, idle/static, frame-00..03). The runtime reads optional variant glow and part opacity/frame; screen and glow animate together at their authored ground depth and ranks 1/2. Fixed TV states freeze both layers. Height and inputDevices metadata remain available in the JSON; no new scene API fields are needed.

### Pack v4 additions

`env-v3/walls.iso.json` supplies the wall panels and integrated trim. `wall-decor.iso.json` retains all day/night/blinds window assemblies, boards, clock, pictures, shelf and lights. The builder compiles each assembly edge into a wall module and preserves `decorAssemblies`. Optional module fields `slot` and `hostModule` describe mounting restrictions; layout attachments use `hostId`, the host's first endpoint and its ground depth. Both Python and Core reject mismatched faces/anchors, duplicate slots and decorations mounted over doors or existing windows. The default room uses ten attachment edges: nine accents including the two-edge day window on right hosts 04/05. The open door remains left host 04. LAB nameplates are appended only to the closed v3 door modules. Night/blinds variants are available art, with no automatic time switching.

glasses-bun's source omits a `characters` entry; the adapter creates its design from the single approved normal sheet. Pixel data, contacts, blink sheets and eye masks are preserved. Chair sandwich masks are rebuilt for all six designs.

The builder discovers cap-teen/labcoat-tall and `pets-v2/*.iso.json` when present, then runs the final source checker. These folders are absent at base commit `beb346a`, so this pack has one real pet, the cat. Core selects up to two distinct pet assets from the ordinal pool using the office seed. Each pet has its own deterministic random stream; four-way paths avoid blocked furniture and reserved desk approaches. Multi-pet selection and empty pools are tested with disposable synthetic cat-sheet copies; dog/rabbit/hamster art remains unverified. `IsoPetPose` and `BuildWithPets` extend the builder while the Scene API below stays unchanged.

The bundle contains 54 sheets and 62 PNGs: PNGs 827,894 bytes, compact `assets.json` 2,844,964 bytes, `layout.json` 24,404 bytes; total 3,697,262 bytes (3.53 MiB). Compact JSON keeps the complete chair metadata below the existing 4 MiB per-JSON limit. Unreferenced sheets were removed before rebuilding embedded resources. Final `iso_check --tolerance 0` results: 30 sources PASS 290 / SKIP 132 / FAIL 0; pack PASS 301 / SKIP 137 / FAIL 0. SKIP rows concern checks that do not apply to a sheet kind.

Validation: the .NET solution passed 560 tests. Python tool discovery passed 79 tests but reported a class setup error from the old five-pilot fixture adding duplicate designs; after fixing its explicit five-design list, all four seated geometry tests passed separately (450 fresh z-buffer frames). The 28 pack-builder tests are included in the 79. No production pixels or visibility thresholds were relaxed; the C# visibility check now includes the new rigs' authored head offsets and hand palettes.

Offscreen QA produced 48 scenario PNGs at 1×/2×/3× plus seven viewport/tag captures under `artifacts/iso-pack-v4/`. Best examples are `iso-twelve-working-2x.png`, `iso-three-1x.png` and `iso-demo-24.0s-3x.png`. All 48 were inspected in zoom contact sheets, with full-size representative and arrival images compared against the hero and previous `docs/img/demo-iso-2x.png`. Remaining visual limits: crowded arrivals can place tags over nearby walkers; the small wall artwork and four-frame gait remain less detailed than the hero. Live DPI/window interaction and iso CPU targets were not measured in this integration.

## 2. Scene API (isoengine implements, isorender consumes)
File `src/Vibedotcom.Core/Iso/IsoContracts.cs`, namespace `Vibedotcom.Core.Iso`. BOTH lanes create this file with IDENTICAL content so the branches merge cleanly. Do not add members; put extra types in other files.
```csharp
namespace Vibedotcom.Core.Iso;
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
`src/Vibedotcom.App/IsoView.cs` (new, `FrameworkElement`, no edits to flat OfficeView beyond what hosting needs): draws `IsoScene` items in order from the iso pack sheets (frozen bitmaps, nearest-neighbor, integer zoom 1x/2x/3x), static layers (Floor, Wall, static furniture) retained in a cached bitmap redrawn only on Revision change/zoom, timer only while `NextChangeSeconds` is finite, name tags using the existing tag modes (hover/icon/always) and provider icons from the existing code path, hit-testing through `IIsoOffice.HitTest` for hover. Window modes (transparent, topmost, click-through, tray, settings, privacy) must work exactly like flat mode because `Program.cs` hosts either view; keep Program.cs edits small (a `view` switch that constructs IsoView+IsoOffice instead of OfficeView+OfficeAnimator). QA path: `--view iso --demo --smoke-test OUTDIR` renders offscreen PNGs (1x/2x/3x, several times, empty and 12-agent scenes) without any visible window. Until the real engine/pack merge in, develop against a small test double `IIsoOffice` that builds a hand-made scene from the real art sheets under assets/art/iso (the renderer lane may create a throwaway pack loader for its own tests; the real pack loader comes from isoengine/isopack).

## 4. Quality bar (supervisor will review the rendered PNGs against assets/art/iso-concept/iso-office-hero.png)
Strict 2:1 projection, per-spec depth sorting with no characters drawn through desks/chairs, seated typists visible (head, hands, forearms) at all four desk orientations, walkers path around furniture, shadows, the cat wandering and sleeping, no flicker/jitter between frames, no per-frame allocations in the idle path, idle CPU target as the flat mode after the perf work (<= 1% of one core when nothing animates).
