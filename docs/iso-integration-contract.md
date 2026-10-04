# Iso integration contract (supervisor, 2026-10-04)

Three lanes build the iso mode in parallel against this contract: **isopack** (pack), **isoengine** (Core simulation + scene), **isorender** (WPF). The flat mode must keep working unchanged; iso is selected by setting `view` = `flat` (default) | `iso` and CLI `--view iso|flat`. Scope is the approved slice: a 10x10 iso office (placement from docs/iso-spec.md), up to 3 character designs (dev-hoodie-v2, punk-girl, professor) mapped from agent ids by the same stable hash rule as the flat roster, desk-sitting as the one seated activity, the cat, furniture families from assets/art/iso/furniture-v3 (chair, workstations, lounge-tv, utility-shelf, utility-lounge). The builder falls back to the matching unchanged furniture-v2 family if its v3 manifest set is incomplete, including separate shadow manifests. Rug, plant and floor-lamp intentionally use v2; all other sources remain current. Source paths/hashes are recorded in sourceManifests. Anything outside is an explicit documented fallback, not a silent skip.

## 1. Pack (isopack produces, isoengine and isorender read)
`assets/art/iso/pack/assets.json` (+ hash-named sheets `assets/art/iso/pack/sheets/pack-<sha256>.png`). Same top-level shape as the flat `assets/pack/assets.json` plus `projection:"iso2to1"`; each sheet has `id`, `kind` (character|blink|floor|wall|furniture|pet|prop|shadow), `file`, `cellW`, `cellH`, `anchor{x,y}`, `rows`/`clips` (name, direction SE|SW|NE|NW, row, frames, fps, loop), `identityPalette`, and for furniture `parts` (partId, depthAnchor{i,j}, isoZOrder, seatGroupId?, hitTargetId?, anchorChecks). Character sheets carry the 38 normal rows + 24 blink rows and the SE/SW eye masks. Embed the iso pack as an assembly resource exactly like the flat pack (Core.csproj) so a build without loose files works. isopack's final report must include the schema summary actually produced; if it differs from this paragraph, the report wins and the other lanes adapt.

Version remains 1. V3 keeps part IDs, source anchors, seat groups, hit targets and furniture footprints. Monitor top is 40, TV top is 48, chair back remains 21; actual ordinary monitor depth anchors come from the v3 source. TV screenStates/glowStates are expanded into explicit variants (default/playing, idle/static, frame-00..03). The runtime reads optional variant glow and part opacity/frame; screen and glow animate together at their authored ground depth and ranks 1/2. Fixed TV states freeze both layers. Height and inputDevices metadata remain available in the JSON; no new scene API fields are needed.

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
