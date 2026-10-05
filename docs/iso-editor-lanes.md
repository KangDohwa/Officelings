# Iso editor implementation lanes

This ownership contract accompanies [iso-editor-spec](iso-editor-spec.md). The three original lanes are merged at `9cec3a9`; integration lane edwire implements only the supervisor-approved additive contract 1.1 revisions in spec §7.5. Work stays in each lane's own worktree; synthetic temporary data only. Do not rebuild or edit bundled production art/packs. All other public signatures and numbered rules remain frozen.

The integration QA uses the editor's accumulated overlay bounds for both the camera and captures; U005 covers negative marker coordinates. This internal rendering correction adds no public contract or fixture fields.

The spec's historical handoff notes are not unresolved service dependencies: edwire uses the merged virtual computer adapter, aggregate validator and real storage resolver. Full-suite restoration remains outstanding. Edfix meets the unchanged U008 latency limits on the recorded machine; measurements, regression evidence and unavailable checks are recorded in the integration QA and [independent review follow-up](iso-editor-review.md).

## 1. Exclusive file ownership

Paths below are relative to the repository. A lane owns only its named files/patterns. New helpers stay in its named folder/prefix. Tests/fixtures follow the same ownership; no other lane removes a Skip or updates an expectation it does not own. Existing flat tests remain enabled.

| Owner | Exclusive implementation and tests |
| --- | --- |
| core | Replace bodies in `Core/Iso/Editing/IsoEditor.cs`, `IsoHitTest.cs`, `IsoEditorStorage.cs`; create `Core/Iso/Editing/Model/*.cs` for aggregate validation, snapshot ownership, command helpers, persistence and hit metadata. Here and below `Core` means `src/Vibedotcom.Core`. Create `Core/Iso/IsoOffice.Editor.cs`, `IsoSceneBuilder.Editor.cs`, `IsoEditorWallCatalog.cs`. Own `tests/Vibedotcom.Tests/IsoEditorCoreTests.cs`, `IsoEditorCommandTests.cs`; `tests/fixtures/iso-editor/layouts/**`; `docs/iso-editor-core.md` only if implementation notes are needed. |
| objects | Create `Core/Iso/Editing/Objects/*.cs`, including public concrete `LocalIsoCustomObjectImporter : IsoCustomObjectImporter`, source parser/security/PNG validation/catalog publisher/computer adapter/thumbnail generator. Create `Core/Iso/IsoPack.Custom.cs` for the narrow clone/merge hook. Own `tools/iso_custom_object.py` (or a new `tools/IsoCustomObject` C# project, choose one), `tools/test_iso_custom_object.py` if Python chosen, `docs/iso-custom-objects.md`, `tests/Vibedotcom.Tests/IsoEditorObjectTests.cs`, `tests/fixtures/iso-editor/objects/**`. CLI uses the same rule IDs and format; do not clone a different editor schema. |
| ui | Create `src/Vibedotcom.App/IsoEditorPanel.cs`, `IsoEditorOverlay.cs`, `IsoEditorImportDialog.cs`, `IsoEditorSession.cs`, `IsoWindow.Editor.cs`, `IsoView.Editor.cs`, `IsoEditorSmoke.cs`; additional new `IsoEditor*.cs` App files permitted. Own `tests/Vibedotcom.Tests/IsoEditorUiTests.cs` and new `IsoEditorUi*Tests.cs`; `tests/fixtures/iso-editor/render/**`; `docs/iso-editor-ui.md` only if QA notes are needed. WPF types stay in App/tests. |
| supervisor / contract | `docs/iso-editor-spec.md`, `docs/iso-editor-lanes.md`, `Core/Iso/Editing/IIsoEditor.cs`, `IsoEditorContracts.cs`, `IsoCustomObjectContracts.cs`, fixture root `cases.json`, `README.md` and `.gitattributes`. Definitions are already supplied. Lanes report discrepancies; supervisor makes coordinated additive revisions. |

The document/test patterns do not authorize unrelated files. Core does not edit object service implementations; objects does not edit editor command/history code; UI does not add Core behavior or load artist source files. Do not touch `Models.cs`, `Office.cs`, `OfficeView.cs`, `OfficeEditor.cs`, Settings or `src/Vibedotcom.Tracker/Program.cs` in these lanes.

## 2. Shared-file hunks

One writer per shared file. Do not copy an entire parallel version. Keep required edits at the named insertion points and report actual line/member positions in the final lane report.

| File | Writer / permitted insertion |
| --- | --- |
| `Core/Iso/IsoLayout.cs` | This handoff only adds trailing `IsoWallSegment.AssemblyId` and `IsoLayout.EditorVersion/WorkSeats`. Core later owns Load/Read/Validate/Navigation: recognize isoEditorV1, preserve legacy inference, integrate aggregate editor rules and mounted/nonblocking links; helpers go in Model files. No objects/UI changes. |
| `Core/Iso/IsoOffice.cs` | Core adds `partial`, implements `IIsoEditorRuntime`, and makes only layout/pack/nav/builder/pet path cache fields replaceable. Constructor seat initialization calls the editor-compatible resolver, keeping legacy behavior. AssignSeats/runtime hooks retain owners by ID, occupied-aisle routes, raise/retreat behavior. Large reload logic goes in IsoOffice.Editor.cs. |
| `Core/Iso/IsoSceneBuilder.cs` | Core owns designation contacts, mounted computer suppression/root hit metadata and retiring-rig rendering hooks. Keep class partial and public IsoScene/IsoDrawItem API unchanged. New helpers in IsoSceneBuilder.Editor.cs. |
| `Core/Iso/IsoSeatChairs.cs`, `IsoFurnitureOrder.cs` | Core only: narrow hooks needed for mounted/retiring assemblies; keep original clip bindings, proxy/visibility thresholds and ground/rank ordering. Report each altered member. |
| `Core/Iso/IsoPack.cs` | Objects only: add `partial` to class and at most narrow internal cache/clone construction hooks for IsoPack.Custom.cs. Never call Build with duplicated Core/shared UI sheets or lose chair-frame alias overrides. Core consumes public Variant/Sheet/Clip/Images and merged adapter metadata; request additional hooks through supervisor. |
| `src/Vibedotcom.App/IsoWindow.cs` | UI: add partial editor lifecycle/menu calls at constructor, MenuEntries/mode/reopen/close paths. Keep the same IsoOffice instance and session through a required native replacement. Main logic in IsoWindow.Editor.cs. |
| `src/Vibedotcom.App/IsoView.cs` | UI: add `partial`, editor-only input/overlay/camera hooks, tooltip suppression and handle precedence. Retain physical camera/scheduler behavior; no new idle timer. Main logic in IsoView.Editor.cs. |
| `src/Vibedotcom.App/IsoApplication.cs` | UI wires IsoEditorStorage.Load, explicit AppStorage root and LocalIsoCustomObjectImporter into Prepare/Run and returns the resolved layout+pack. Synthetic smoke/QA roots must never read the personal catalog/layout. Constructor call adjustments stay local. |
| `src/Vibedotcom.App/Program.Iso.cs` | UI only: SwitchIso uses storage resolver/session and SwitchFlat retains iso session; privacy replacement preserves safe editor document, not old text. No changes to flat OfficeWindow behavior beyond these iso switch hunks. |
| `src/Vibedotcom.App/AppStorage.cs` | UI may add a single `IsoLayoutPath` property alongside LayoutPath, derived from Home. No personal-root discovery inside Core. |
| `tests/Vibedotcom.Tests/Vibedotcom.Tests.csproj` | Supervisor only, if fixture output copying is useful: one `None Include="../fixtures/iso-editor/**/*" ...` link/copy item. Existing tests may resolve repository paths instead, so no required edit. |

No changes are required in Core.csproj/solution/shared asset records for this handoff. Additional projects or manifest schema edits belong to supervisor coordination. Prefix/folder ownership is the default; it does not authorize changing the public contract to resolve a compile error.

## 3. Consumed and provided interfaces

All new public types are in `Vibedotcom.Core.Iso.Editing`. Core depends only on existing WPF-free Core types and framework `ImmutableArray`; no new NuGet dependency. Every public member has XML references to the numbered spec.

| Lane | Consumes | Provides |
| --- | --- | --- |
| core | IsoPack, IsoLayout, IsoContacts, canonical computer adapter variant when available; frozen command/result/state/seat contracts; IsoCustomObjectImporter for startup only. | Concrete IsoEditor implementing IIsoEditor; Preview/Validate/Save/history; static IsoHitTest; IsoEditorStorage.Load; IsoOffice implementing IIsoEditorRuntime.ApplyEditorState/UpdateEditorPack. Aggregate errors conform to L/C/P rules. |
| objects | IsoCustomObjectManifest/variant/catalog/request/result contracts; IsoSheet/IsoPart/IsoInteraction/runtime pack; existing local output/input guard. No IsoEditor internals. | LocalIsoCustomObjectImporter.Validate/Import/LoadCatalog/Remove/Merge; virtual iso/computer family, immutable catalog entries, unchanged user PNG bytes, deterministic thumbnails; bounded local CLI and artist examples. O rules. |
| ui | IIsoEditor (inject a UI-owned fake immediately), IsoEditPreview and all issue/result types; IsoHitTest; IIsoEditorRuntime; IsoCustomObjectImporter (inject a UI-owned fake until objects merge); resolved IIsoOffice+IIsoSheetSource. | Input, panel/overlay/handles/ghost, keyboard/focus/status, dialog and pack swap lifecycle, same-office reload, offscreen editor states and native smoke evidence. U rules. |

Flow: storage loads local catalog through importer → Merge creates one complete pack → storage validates/selects user or bundled layout → IsoEditorComposition.Load creates real services → UI creates IsoOffice+IsoEditor with that same pack/layout → every changed State is passed to ApplyEditorState. Invalid drafts cancel pending swaps and retain the last valid live layout; valid drafts apply after safe retreat. AppliedEditorRevision acknowledges complete publication and clears Applying without reference comparisons. Draft picking uses IIsoEditor.GetPickingScene. Explicit Save writes the separate iso layout.

Integration lane edwire owns the supervisor-approved additive §7.5 APIs and coordinated spec/caller/test updates after base 9cec3a9. PrepareImport/PrepareRemove make no writes; CommitPack preflights editor/history/live/pending runtime and sheet decoding before importer Commit publishes disk, then all consumers adopt one pack synchronously. Remove receives the union of live/draft/history/saved/backup references. ValidatePreview owns captured validated bytes and immutable metadata/PNG/hash. GetEditorSeatStatus contains only seat ID/status. No second reservation store exists in UI. Production and demo smoke use real services; named doubles are only for UI unit tests.

The three implementations are merged at 9cec3a9. Production and synthetic demo smoke use concrete services. Named UI doubles now live in the test project. Snapshot inputs using older read-only Core records are deep-frozen at service boundaries, not assumed immutable because a record contains IReadOnlyList. AppStorage accepts an explicit isolated synthetic root for reproducible integration checks; production root resolution is unchanged.

The real-service QA runner and xUnit U001–U007 call the same assertion bodies; those seven rules are enabled after offscreen execution passes. U008 retains its original 100 ms/16 ms gates and Skip because latency fails. NuGet restore failures do not count as suite passes. See [integration QA](iso-editor-integration.md) for actual group counts, captures, measurements, shared-file hunks and native cases still unverified.

## 4. Merge order and verification

1. Supervisor merges this specification/API/test fixture handoff first; capture its commit as all three implementation bases.
2. Merge objects next: service/pack hooks/computer adapter. Verify built-in sheet/character/palette/rig equality, O rule fixtures, merged count/byte/decoded limits and content hashes. This resolves the core/UI runtime dependency without creating editor internals.
3. Merge core: commands/validation/history/storage/runtime seat and hit integration. Verify L/C/P rules, all twelve occupied approaches, live edit transitions, mounted adapter suppression and existing seated/flat goldens.
4. Merge UI: panel/lifecycle/input/offscreen states using the concrete merged services. Verify U rules and shared App hunks. UI branch may be developed and tested with fakes in parallel; rebase/merge only under supervisor direction, never rewrite published history.
5. Supervisor runs full solution build/test and Python tools suite inside its integration worktree, reviews deterministic renders and records native/manual cases not exercised. No push/deploy is implied.

Lanes keep moderately granular Korean Conventional Commits (`type(scope): 요약`, ≤72 characters, no attribution trailers) after coherent work passes its checks. Do not commit bin/obj/.packages/artifacts/traces. Tests using temp output set TEMP/TMP to a synthetic directory in that lane worktree. The supervisor supplied a 561 .NET/85 Python baseline; this base's Python discovery actually finds 88 tests (including chairfix2 additions). Keep all existing tests passing with new skeleton cases explicitly skipped; skip counts are not behavior evidence. Final reports use actual discovered counts, not the historical baseline.

## 5. Supervisor acceptance checklist

- Public signatures, XML section references, root version1/editorVersion1/capability and explicit-empty seats agree across schema, JSON fixtures and all callers; no duplicated alternative APIs.
- Place desk creates no chair/seat; independently placed canonical chair/computer can be linked by SetSeat. Move/Rotate/Delete/Duplicate operate as one assembly step, retain IDs on moves and remap them on copies; standalone/custom decorative objects never acquire seats implicitly.
- Command geometry/preconditions are atomic; navigation-invalid drafts remain visible only as editor overlays; all issue lists use stable rule IDs/messages; invalid Save performs no write and new edits clear redo.
- Default/reset use shipped twelve seats; every enabled approach has a door path and occupied-seat aisle access; pets remain outside reservations; edited actors retain design/seat ownership or explicitly release deleted seats and finish transitions safely.
- Runtime mixed queue/masks/ground keys/seat ranks remain authoritative; empty/occupied/walking chair sandwich and monitor-only replacement are verified without source-pixel changes or weakened visibility tests.
- Separate iso persistence selects user layout only after whole-pack/layout validation; legacy files still load, explicit empty seats stays empty, backups/dirty tracking are correct; tracker flat v5 and flat layout/pack regression remain green.
- Import limits/path/link/PNG/decode/palette validation, stable content hash, original PNG preservation, catalog transaction/no partial visibility and referenced removal are tested with synthetic data. Combined pack limits include generated/shared UI sheets; no network initiated by input data.
- Whole wall assemblies move/duplicate/delete atomically, HostModule/face/edge/slot rules apply, fixed open door/hosts remain intact, no orphan edge of a two-host window.
- UI uses same runtime/editor/session through lifecycle; restores mode/click-through, preserves privacy/tag behavior, honors keyboard text focus, DPI/zoom/pan and accessibility; Import failure leaves active services coherent.
- Every L/C/O/P/U skeleton owned by an implementation lane is replaced with meaningful assertions and enabled for completed behavior. Golden fixture index expectations remain authoritative; new invalid inputs may legitimately return more independent errors.
- Offscreen images are visually reviewed with deterministic seeds/hashes; native window/DPI/tray/manual checks and editor latency/allocation measurements are reported separately. The seated cushion fit gate (5px per side) belongs to the art pipeline; editor lanes do not change it.
- Final reports identify exact shared-file members/line positions, commits, actual test counts, unverified cases and merge risks; all changes fit the assigned file ownership.
