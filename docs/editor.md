# Office editor

Open **Edit office** from the app menu. Editing temporarily uses normal window mode and pauses click-through so the office can receive pointer input. **Done editing** restores the configured window mode and click-through setting. Agents keep animating through the application's `OfficeAnimator.ReloadLayout` hook; editing does not restart tracking.

The searchable palette includes every furniture asset and tile in the active asset pack, with thumbnails. Select furniture to place it on the snapped grid, or select a tile and choose a paint tool. **Select / move** selects an instance and drags it to another grid position. A desk, computer and its linked foreground chair move, rotate, duplicate and delete together. Clicking the linked chair also selects that prop for model commands.

| Tool / action | Behavior |
| --- | --- |
| Tile brush | Paints a continuous grid line between consecutive pointer positions, including both endpoints. |
| Tile rectangle | Fills the inclusive rectangle between drag endpoints in either direction. |
| Tile fill | Replaces the connected region of the starting tile ID using four-way adjacency. Furniture does not divide the paint region; tile IDs do. |
| Zone: workspace / pantry / lounge | Marks the inclusive rectangle for that named zone. Existing local activity points move with the zone; a smaller rectangle must still contain them. |
| Door / spawn | Marks the entrance and updates the required door zone together. Decorative door furniture is a separate prop. |
| Overflow seat | Click to toggle a spare seat on that tile. The default facing is up. |
| Desk seat | Select a desk first, then mark its seat on the grid. The saved override is relative to the desk. |
| Resize office | Sets an explicit width and height, each 8–128 tiles. Objects, zones, door and interaction/overflow seats must fit; they are never silently clipped. Existing tiles retain their coordinates and added cells use the pack's first floor tile. |
| Reset to default | Restores the bundled layout for the active pack as an undoable edit. Save persists that choice. |

`R` rotates, `Delete` deletes, `Ctrl+D` duplicates, `Ctrl+Z` undoes, `Ctrl+Y` redoes and `Ctrl+S` saves. Shortcuts do not intercept typing in a text box. Duplicate uses a new instance/desk ID and defaults to the position immediately right of the selected footprint; if that position overlaps or exceeds bounds, move the source or choose another position through the model API.

Rotation cycles only the authored variants for the instance's current state, ordered by rotation. The model uses each variant's exact footprint, blocked cells, interaction points and sprite metadata. A prop with only one authored rotation reports an error. Linked chairs need the corresponding authored rotation too. An implicit desk seat comes from the new variant. A custom seat override rotates clockwise by the rotation difference using the old footprint's `(x,y) → (height-1-y,x)` transform and updates facing; its ID/action are preserved. Move keeps its relative coordinates and stable desk ID. Duplicate preserves the override and creates a fresh desk ID.

Placement, movement, rotation and duplication reject blocking furniture overlaps and out-of-bounds footprints or interaction points before changing a snapshot. Nonblocking authored layers, such as rugs and foreground chairs, may overlap another footprint. Painting and markings can temporarily produce an invalid draft. **Validate** lists all independent errors, including missing/duplicate required zones (`workspace`, `pantry`, `lounge`, `door`), invalid IDs/tiles/variants, blocked entrance, shared seats, missing linked furniture and unreachable activity or work/overflow seats. Each seat/activity point must be walkable and reachable from the entrance using the Core A* pathfinder. Live preview permits temporary blocked or unreachable points and gives agents reachable substitute destinations; structurally invalid drafts keep the animator's previous layout while the editor displays the draft for correction.

**Save office** writes `%LOCALAPPDATA%\Vibedotcom\layout.json`. Validation finishes before creating a directory, temporary file or backup. All validation errors appear in the failure message. Runtime furniture `agentId` assignments are cleared in the saved JSON while the editor snapshot remains intact. A successful first save creates the file. Later saves flush a unique sibling temporary file, then call `File.Replace(temporary, destination, destination + ".bak")` to atomically replace the layout and keep its immediately previous contents as `layout.json.bak`. Temporary files are cleaned up on failure; save does not add an undo entry. I/O or access errors are reported without claiming success. Provider source directories are rejected by the shared output-path guard.

The headless tracker uses the same layout source format and reads it at startup. Restart the tracker after saving an editor layout to load the changes there; the app's live reload hook does not reload a separate tracker process.

Successfully previewed edits remain in the current app session; saving is needed to retain them on a later launch. An invalid draft cannot be saved. Undo/redo restore complete snapshots, including tile arrays, zones, linked chairs and seat overrides. A new edit clears redo. The model copies input collections and publishes read-only collections so caller mutations cannot alter history.

## Model API

`Vibedotcom.Core.OfficeEditor` has no WPF dependency. Coordinates supplied to commands are absolute office tiles; `Furniture.Seat` is stored as a local `InteractionPoint`. `OverflowSeat.X/Y` are absolute, with an optional `FurnitureId` link. Linked overflow seats move, rotate and duplicate with their furniture and are removed when the prop is deleted. Native window replacement retains the live animator and editor history.

```csharp
public OfficeEditor(AssetPack assets, OfficeLayout layout);
public OfficeLayout Layout { get; }
public bool CanUndo { get; }
public bool CanRedo { get; }
public string Place(string assetId, Tile tile);
public void Move(string id, Tile tile);
public void Rotate(string id);
public void Delete(string id);
public string Duplicate(string id, Tile? target = null);
public void Paint(Tile from, Tile to, int tileId, string shape = "brush");
public void SetZone(string name, Tile from, Tile to);
public void SetDoor(Tile tile);
public void SetOverflowSeat(Tile tile, bool enabled = true);
public void SetDeskSeat(string id, Tile tile);
public void Resize(int width, int height);
public bool Undo();
public bool Redo();
public IReadOnlyList<string> Validate();
public void Save(string path);
public void ResetDefault();
```

`SetOverflowSeat` is idempotent; the UI toggles by passing `!present` as `enabled`. Fill uses only `from`; `to` is ignored. Invalid command arguments throw `ArgumentException`/`ArgumentOutOfRangeException`; rejected moves/rotations/resize throw `InvalidOperationException`. Invalid save throws `InvalidDataException` with the complete error list. No failed command adds history. The constructor uses Core layout preparation for valid legacy layouts and preserves invalid input drafts for `Validate()` to inspect. The UI calls its reload hook after successful mutations and undo/redo and shows reload/validation failures to let the user correct the draft.

The targeted tests are in `tests/Vibedotcom.Tests/OfficeEditorTests.cs`. They cover active-pack palette coverage, authored collision/rotation geometry, custom seat rotation, A* passage changes, desk/chair grouping, input ownership and serialized undo history, flood fill, atomic backup/loader roundtrip with cleared agent IDs, invalid-save no-write behavior and explicit resize bounds.

## Windows QA

From the repository root, run the built app executable after the coordinated build. These paths use the default Debug output; use the matching output directory for another configuration. Launch the `.exe` so its Windows compatibility manifest applies.

```powershell
& ".\src\Vibedotcom.App\bin\Debug\net10.0-windows\Vibedotcom.App.exe" --demo --smoke-test "$env:TEMP/vibedotcom-feedback/final"
& ".\src\Vibedotcom.App\bin\Debug\net10.0-windows\Vibedotcom.App.exe" --verify-opacity "$env:TEMP/vibedotcom-feedback/opacity"
```

The render smoke test writes `normal.png`, `reserved-tags.png`, `overflow.png`, `editor-ui.png` and `auto-fit-small.png` under the supplied `final` directory. Inspect them for normal rendering, reserved-seat labels, overflow agents, editor controls/grid markings and the small-window automatic fit.

The opacity verifier uses hidden windows and samples its own HWND and parent surface. It does not sample the Explorer desktop or a DWM screen capture. Its 28 native cases exercise five opacity presets, live changes, repainting and native movement/resize without another policy application, with 616 samples. Regular windows read the real HWND's alpha using `GetLayeredWindowAttributes`; per-pixel windows capture that HWND's attached root visual and composite sampled pixels offscreen. Separate baseline pixel diagnostics are not evidence of a native `Window.Opacity` failure. Review the generated JSON evidence in the supplied `opacity` directory for case/sample counts, expected versus actual alpha and unchanged parent pixels.
