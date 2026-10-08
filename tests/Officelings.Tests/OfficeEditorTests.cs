using System.Text.Json;
using Officelings.Core;
using Xunit;

namespace Officelings.Tests;

public sealed class OfficeEditorTests : IDisposable
{
    private readonly string root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Officelings-editor-tests-" + Guid.NewGuid().ToString("N")));

    public OfficeEditorTests() => Directory.CreateDirectory(root);
    public void Dispose() => Directory.Delete(root, true);
    private static string Json(OfficeLayout layout) => JsonSerializer.Serialize(layout, StateJson.Options);
    private static OfficeLayout Persisted(OfficeLayout layout) => layout with {
        Furniture = layout.Furniture.Select(f => f with { AgentId = null }).ToArray()
    };

    private static OfficeLayout Room(int width = 12, int height = 12) => new(width, height, 16, new(1, 1), [],
        Tiles: Enumerable.Repeat(1, width * height).ToArray(), Zones: [
            new("workspace", 1, 1, 5, 7), new("pantry", 7, 1, 2, 3),
            new("lounge", 7, 5, 2, 4), new("door", 1, 1, 1, 1)
        ], OverflowSeats: []);

    private AssetPack AuthoredPack()
    {
        var folder = Path.Combine(root, "pack");
        Directory.CreateDirectory(folder);
        var manifest = new AssetManifest(1,
            [new("people", "people.png", 16, 24, [new("idle", "any", 0, 1, 1)], new(8, 24))],
            [
                new("rotor", "desk", [
                    new(0, "default", 3, 1, [new("seat", 1, 1, "typing", "up")], [new(0, 0), new(2, 0)]),
                    new(90, "default", 2, 3, [new("seat", 2, 1, "typing", "left")], [new(0, 0), new(0, 1), new(1, 2)]),
                    new(270, "default", 4, 2, [new("seat", 2, 2, "typing", "down")], [new(0, 0), new(3, 1)])
                ]),
                new("turn-desk", "desk", [
                    new(0, "default", 3, 2, [new("seat", 1, 2, "typing", "up")]),
                    new(90, "default", 2, 3, [new("seat", -1, 1, "typing", "right")]),
                    new(270, "default", 2, 3, [new("seat", 2, 1, "typing", "left")])
                ]),
                new("paired-desk", "desk", [
                    new(0, "default", 3, 2, [new("seat", -1, 1, "typing", "right")],
                        ChairBackId: "right-chair-back", ChairFrontId: "right-chair-front",
                        ChairOffset: new(-16, 16), ChairSeatAnchor: new(-8, 28)),
                    new(180, "default", 3, 2, [new("seat", 3, 1, "typing", "left")],
                        ChairBackId: "left-chair-back", ChairFrontId: "left-chair-front",
                        ChairOffset: new(48, 16), ChairSeatAnchor: new(56, 28))
                ]),
                new("right-chair-back", "chair", [new(0, "default", 1, 1, [], [])]),
                new("right-chair-front", "chair", [new(0, "default", 1, 1, [], [])]),
                new("left-chair-back", "chair", [new(0, "default", 1, 1, [], [])]),
                new("left-chair-front", "chair", [new(0, "default", 1, 1, [], [])]),
                new("no-turn", "plant", [new(0, "default", 1, 1, [])])
            ], [new("person", "people")], []);
        File.WriteAllBytes(Path.Combine(folder, "people.png"), SpriteAtlas.CreatePng());
        File.WriteAllText(Path.Combine(folder, "assets.json"), JsonSerializer.Serialize(manifest, StateJson.Options));
        var assets = new AssetPack(folder);
        Assert.Empty(assets.Warnings);
        return assets;
    }

    [Fact]
    public void DeskWithoutSeatCannotBeSaved()
    {
        var assets=AuthoredPack();
        var layout=Room() with { Furniture=[new("desk",3,3,1,1,Id:"missing-seat",AssetId:"no-turn")] };
        var editor=new OfficeEditor(assets,layout);
        Assert.Contains(editor.Validate(),error=>error.Contains("requires an authored typing seat",StringComparison.Ordinal));
        var path=Path.Combine(root,"missing-seat.json");
        Assert.Throws<InvalidDataException>(()=>editor.Save(path));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void LinkedOverflowSeatsRotateWithFurnitureAndUndoRestoresTheirFacing()
    {
        var assets=AssetPack.LoadDefault(useUserPack:false);
        var sofa=new Furniture("sofa",5,5,3,2,Id:"sofa",AssetId:"cozy-sofa");
        var layout=Room() with { Furniture=[sofa],OverflowSeats=[new("spare",6,7,"up","sofa")] };
        var editor=new OfficeEditor(assets,layout);
        editor.Rotate("sofa");
        var seat=Assert.Single(editor.Layout.OverflowSeats!);
        Assert.Equal((4,6,"right"),(seat.X,seat.Y,seat.Facing));
        Assert.Empty(editor.Validate());Assert.True(editor.Undo());
        Assert.Equal(Json(layout),Json(editor.Layout));
    }

    [Fact]
    public void EveryBundledFurnitureAndTileCanBeSelectedWithoutAKindWhitelist()
    {
        var assets = AssetPack.LoadDefault(useUserPack: false);
        foreach (var definition in assets.Manifest.Furniture)
        {
            var editor = new OfficeEditor(assets, Room(64, 64));
            var id = editor.Place(definition.Id, new(10, 10));
            var placed = Assert.Single(editor.Layout.Furniture, f => f.Id == id);
            var variant = assets.Variant(placed);
            Assert.Equal(definition.Kind, placed.Kind);
            Assert.Equal(definition.Id, placed.AssetId);
            Assert.Equal((variant.FootprintW, variant.FootprintH), (placed.Width, placed.Height));
            Assert.Empty(editor.Validate());
        }
        var tiles = new OfficeEditor(assets, Room());
        foreach (var tile in assets.Manifest.TileTypes)
        {
            tiles.Paint(new(3, 3), new(3, 3), tile.Id);
            Assert.Equal(tile.Id, tiles.Layout.Tiles![3 * 12 + 3]);
        }
        var before = tiles.Layout;
        Assert.Throws<ArgumentException>(() => tiles.Paint(new(3, 3), new(3, 3), -123));
        Assert.Same(before, tiles.Layout);
    }

    [Fact]
    public void PlacementUsesAuthoredBlockedCellsAndRotationUsesActualAsymmetricVariants()
    {
        var assets = AuthoredPack();
        var editor = new OfficeEditor(assets, Room(16, 16));
        var desk = editor.Place("rotor", new(4, 4));
        var plant = editor.Place("no-turn", new(5, 4)); // A hole inside the desk footprint is deliberately walkable.
        var original = Json(editor.Layout);
        var before = editor.Layout;
        Assert.Throws<InvalidOperationException>(() => editor.Place("plant", new(4, 4)));
        Assert.Throws<InvalidOperationException>(() => editor.Move(desk, new(5, 4)));
        Assert.Same(before, editor.Layout);
        editor.Rotate(desk);
        var rotated = Assert.Single(editor.Layout.Furniture, f => f.Id == desk);
        Assert.Equal((90, 2, 3), (rotated.Rotation, rotated.Width, rotated.Height));
        Assert.Null(rotated.Seat);
        Assert.Equal("left", assets.Variant(rotated).Points.Single().Facing);
        Assert.Contains(new Tile(5, 6), LayoutLoader.Blocked(editor.Layout, assets));
        Assert.DoesNotContain(new Tile(6, 4), LayoutLoader.Blocked(editor.Layout, assets));
        editor.Rotate(desk);
        Assert.Equal((270, 4, 2), editor.Layout.Furniture.Where(f => f.Id == desk).Select(f => (f.Rotation, f.Width, f.Height)).Single());
        editor.Rotate(desk);
        Assert.Equal(original, Json(editor.Layout));
        before = editor.Layout;
        var error = Assert.Throws<InvalidOperationException>(() => editor.Rotate(plant));
        Assert.Contains("only authored rotation 0", error.Message);
        Assert.Same(before, editor.Layout);
        Assert.Empty(editor.Validate());
    }

    [Fact]
    public void ExplicitSeatFollowsMoveAndClockwiseRotationWhileImplicitSeatUsesTheManifest()
    {
        var assets = AuthoredPack();
        var editor = new OfficeEditor(assets, Room(16, 16));
        var id = editor.Place("turn-desk", new(4, 4));
        editor.SetDeskSeat(id, new(6, 7));
        editor.Move(id, new(5, 4));
        var original = Json(editor.Layout);
        var deskId = editor.Layout.Furniture.Single().DeskId;
        Assert.Equal(new InteractionPoint("seat", 2, 3, "typing", "up"), editor.Layout.Furniture.Single().Seat);
        editor.Rotate(id);
        Assert.Equal(new InteractionPoint("seat", -2, 2, "typing", "right"), editor.Layout.Furniture.Single().Seat);
        Assert.Equal(new Tile(3, 6), assets.Points(editor.Layout).Single().Tile);
        editor.Rotate(id);
        Assert.Equal(new InteractionPoint("seat", 3, 0, "typing", "left"), editor.Layout.Furniture.Single().Seat);
        editor.Rotate(id);
        Assert.Equal(original, Json(editor.Layout));
        Assert.Equal(deskId, editor.Layout.Furniture.Single().DeskId);
        Assert.Empty(editor.Validate());
        Assert.True(editor.Undo());
        Assert.Equal(270, editor.Layout.Furniture.Single().Rotation);
        Assert.True(editor.Redo());
        Assert.Equal(original, Json(editor.Layout));
    }

    [Fact]
    public void ClosingAPassageReportsBothUnreachableWorkAndOverflowSeatsAndOpeningItRestoresAStar()
    {
        var assets = new AssetPack();
        var editor = new OfficeEditor(assets, Room());
        var desk = editor.Place("desk", new(8, 4));
        editor.SetOverflowSeat(new(10, 8));
        editor.Paint(new(6, 0), new(6, 11), 0, "rect");
        var errors = editor.Validate();
        Assert.Contains(errors, e => e.Contains(desk) && e.Contains("unreachable"));
        Assert.Contains(errors, e => e.Contains("Overflow seat") && e.Contains("unreachable"));
        Assert.Empty(OfficeAnimator.FindPath(editor.Layout, editor.Layout.Door, new(9, 6), LayoutLoader.Blocked(editor.Layout, assets)));
        editor.Paint(new(6, 8), new(6, 8), 1);
        Assert.Empty(editor.Validate());
        var blocked = LayoutLoader.Blocked(editor.Layout, assets);
        var path = OfficeAnimator.FindPath(editor.Layout, editor.Layout.Door, new(9, 6), blocked);
        Assert.Contains(new Tile(6, 8), path);
        var previous = editor.Layout.Door;
        foreach (var step in path)
        {
            Assert.Equal(1, Math.Abs(step.X - previous.X) + Math.Abs(step.Y - previous.Y));
            Assert.DoesNotContain(step, blocked);
            previous = step;
        }
        Assert.Equal(new Tile(9, 6), previous);
    }

    [Fact]
    public void DeskCommandsCarryLegacyForegroundChairsAndLinkedSeatsWithoutGhostFurniture()
    {
        var assets = AssetPack.LoadDefault(useUserPack: false);
        var editor = new OfficeEditor(assets, Room(32, 32));
        var id = editor.Place("cozy-desk", new(4, 4));
        var chair = Assert.Single(editor.Layout.Furniture, f => f.Id == id + "-chair");
        Assert.Equal("cozy-chair-front", chair.AssetId);
        Assert.Equal(id, chair.DeskId);
        Assert.Single(editor.Layout.Furniture, f => f.Id == id + "-chair-back" && f.AssetId == "cozy-chair-back");
        var legacy = editor.Layout with {
            Furniture = editor.Layout.Furniture.Select(f => f.Id == chair.Id ? f with { DeskId = null } : f).ToArray(),
            OverflowSeats = [new("linked-seat", 2, 6, "left", id)]
        };
        editor = new OfficeEditor(assets, legacy);
        var deskId = editor.Layout.Furniture.Single(f => f.Id == id).DeskId;
        editor.Move(chair.Id, new(8, 8));
        Assert.All(editor.Layout.Furniture, f => Assert.Equal((8, 8), (f.X, f.Y)));
        Assert.Equal(deskId, editor.Layout.Furniture.Single(f => f.Id == id).DeskId);
        Assert.Equal((6, 10), editor.Layout.OverflowSeats!.Select(s => (s.X, s.Y)).Single());
        Assert.Equal((4, 4), (chair.X, chair.Y));
        var beforeRotation = Json(editor.Layout);
        editor.Rotate(id);
        Assert.All(editor.Layout.Furniture, f => Assert.Equal((90, 2, 3), (f.Rotation, f.Width, f.Height)));
        Assert.All(editor.Layout.Furniture, f => Assert.Equal((8, 8), (f.X, f.Y)));
        editor.Rotate(id); editor.Rotate(id); editor.Rotate(id);
        Assert.Equal(beforeRotation, Json(editor.Layout));
        var duplicate = editor.Duplicate(chair.Id, new(14, 8));
        Assert.NotEqual(id, duplicate);
        Assert.Equal(6, editor.Layout.Furniture.Count);
        Assert.Equal(duplicate, editor.Layout.Furniture.Single(f => f.Id == duplicate).DeskId);
        Assert.Equal(duplicate, editor.Layout.Furniture.Single(f => f.Id == duplicate + "-chair").DeskId);
        Assert.Equal((14, 8), editor.Layout.Furniture.Where(f => f.Id == duplicate + "-chair").Select(f => (f.X, f.Y)).Single());
        Assert.Contains(editor.Layout.OverflowSeats!, s => s.FurnitureId == duplicate && s.X == 12 && s.Y == 10);
        editor.Delete(chair.Id);
        Assert.Equal(3, editor.Layout.Furniture.Count);
        Assert.DoesNotContain(editor.Layout.Furniture, f => f.Id == id || f.Id == chair.Id);
        Assert.Equal(duplicate, Assert.Single(editor.Layout.OverflowSeats!).FurnitureId);
        var deleted = Json(editor.Layout);
        Assert.True(editor.Undo());
        Assert.Equal(6, editor.Layout.Furniture.Count);
        Assert.True(editor.Redo());
        Assert.Equal(deleted, Json(editor.Layout));
        var before = editor.Layout;
        Assert.Throws<InvalidOperationException>(() => editor.Move(duplicate, new(31, 30)));
        Assert.Same(before, editor.Layout);
        Assert.Empty(editor.Validate());
    }

    [Fact]
    public void AuthoredSideDeskChairsMoveRotateDuplicateAndDeleteAsOneGroup()
    {
        var assets = AuthoredPack();
        var editor = new OfficeEditor(assets, Room(32, 32));
        var id = editor.Place("paired-desk", new(6, 6));
        Assert.Equal(3, editor.Layout.Furniture.Count);
        Assert.All(editor.Layout.Furniture.Where(f => f.Kind == "chair"), chair => {
            Assert.Equal(id, chair.DeskId);
            Assert.Equal((5, 7), (chair.X, chair.Y));
            Assert.Empty(LayoutLoader.Cells(chair, assets.Variant(chair)));
        });
        editor.Move(id + "-chair-front", new(10, 10));
        var beforeRotation = Json(editor.Layout);
        editor.Rotate(id + "-chair-back");
        var desk = Assert.Single(editor.Layout.Furniture, f => f.Id == id);
        Assert.Equal((180, 3, 2), (desk.Rotation, desk.Width, desk.Height));
        Assert.Null(desk.Seat);
        Assert.Equal(new InteractionPoint("seat", 3, 1, "typing", "left"), Assert.Single(assets.Variant(desk).Points));
        Assert.All(editor.Layout.Furniture.Where(f => f.Kind == "chair"), chair => {
            Assert.Equal((13, 11), (chair.X, chair.Y));
            Assert.Equal(0, chair.Rotation); // The metadata selects separate, already authored left-facing assets.
            Assert.StartsWith("left-chair-", chair.AssetId!);
        });
        editor.Rotate(id);
        Assert.Equal(beforeRotation, Json(editor.Layout));
        editor.Rotate(id);
        var duplicate = editor.Duplicate(id + "-chair-front", new(18, 10));
        Assert.All(editor.Layout.Furniture.Where(f => f.DeskId == duplicate && f.Kind == "chair"), chair => {
            Assert.Equal((21, 11), (chair.X, chair.Y));
            Assert.Null(chair.AgentId);
        });
        Assert.Single(editor.Layout.Furniture, f => f.Id == duplicate + "-chair-back");
        Assert.Single(editor.Layout.Furniture, f => f.Id == duplicate + "-chair-front");
        editor.Delete(id + "-chair-back");
        Assert.Equal(3, editor.Layout.Furniture.Count);
        Assert.DoesNotContain(editor.Layout.Furniture, f => f.DeskId == id);
        var deleted = Json(editor.Layout);
        Assert.True(editor.Undo());
        Assert.Equal(6, editor.Layout.Furniture.Count);
        Assert.True(editor.Redo());
        Assert.Equal(deleted, Json(editor.Layout));
        var before = editor.Layout;
        Assert.Throws<ArgumentOutOfRangeException>(() => editor.Move(duplicate, new(29, 10))); // The desk fits, but its separate seat does not.
        Assert.Same(before, editor.Layout);
        Assert.Empty(editor.Validate());
    }

    [Fact]
    public void CustomSideSeatKeepsChairLayersAlignedAcrossRotationAndSave()
    {
        var assets = AuthoredPack();
        var editor = new OfficeEditor(assets, Room(32, 32));
        var id = editor.Place("paired-desk", new(6, 6));
        editor.SetDeskSeat(id + "-chair-front", new(5, 9));
        Assert.All(editor.Layout.Furniture.Where(f => f.Kind == "chair"), chair => Assert.Equal((5, 9), (chair.X, chair.Y)));
        var custom = Json(editor.Layout);
        editor.Rotate(id);
        var desk = Assert.Single(editor.Layout.Furniture, f => f.Id == id);
        Assert.Equal(new InteractionPoint("seat", 3, -2, "typing", "left"), desk.Seat);
        Assert.All(editor.Layout.Furniture.Where(f => f.Kind == "chair"), chair => Assert.Equal((9, 4), (chair.X, chair.Y)));
        Assert.Empty(editor.Validate());
        var file = Path.Combine(root, "paired-seat.json");
        editor.Save(file);
        var warnings = new List<string>();
        var loaded = LayoutLoader.Load(assets, file, warnings);
        Assert.Empty(warnings);
        Assert.Equal(Json(editor.Layout), Json(loaded));
        editor.Rotate(id);
        Assert.Equal(custom, Json(editor.Layout));
        var before = editor.Layout;
        Assert.Throws<ArgumentOutOfRangeException>(() => editor.Move(id, new(0, 6))); // The separate seat would leave the room.
        Assert.Same(before, editor.Layout);
    }

    [Fact]
    public void LaptopTableChairsAndExplicitOverflowSeatFollowItsAuthoredFacing()
    {
        var assets = AssetPack.LoadDefault(useUserPack: false);
        var editor = new OfficeEditor(assets, Room(32, 32));
        var id = editor.Place("cozy-laptop-table-side", new(6, 6));
        var table = Assert.Single(editor.Layout.Furniture, f => f.Id == id);
        Assert.Equal("table", table.Kind);
        Assert.Null(table.DeskId);
        Assert.Empty(assets.Variant(table).Points);
        editor = new OfficeEditor(assets, editor.Layout with { OverflowSeats = [new("laptop-seat", 5, 7, "right", id)] });
        editor.Move(id + "-chair-front", new(10, 10));
        Assert.All(editor.Layout.Furniture, f => Assert.Equal((10, 10), (f.X, f.Y)));
        Assert.Equal((9, 11, "right"), editor.Layout.OverflowSeats!.Select(s => (s.X, s.Y, s.Facing)).Single());
        var beforeRotation = Json(editor.Layout);
        editor.Rotate(id + "-chair-back");
        Assert.All(editor.Layout.Furniture, f => Assert.Equal(270, f.Rotation));
        Assert.Equal((13, 11, "left"), editor.Layout.OverflowSeats!.Select(s => (s.X, s.Y, s.Facing)).Single());
        editor.Rotate(id);
        Assert.Equal(beforeRotation, Json(editor.Layout));
        var duplicate = editor.Duplicate(id + "-chair-front", new(18, 10));
        Assert.Null(editor.Layout.Furniture.Single(f => f.Id == duplicate).DeskId);
        Assert.Contains(editor.Layout.OverflowSeats!, s => s.FurnitureId == duplicate && s.X == 17 && s.Y == 11);
        editor.Delete(id + "-chair-back");
        Assert.Equal(3, editor.Layout.Furniture.Count);
        Assert.Equal(duplicate, Assert.Single(editor.Layout.OverflowSeats!).FurnitureId);
        Assert.Empty(editor.Validate());
    }

    [Fact]
    public void UndoRedoRestoresCompleteSerializedSnapshotsAndNewEditsDiscardRedo()
    {
        var editor = new OfficeEditor(new AssetPack(), Room(16, 16));
        var snapshots = new List<string> { Json(editor.Layout) };
        void Capture() => snapshots.Add(Json(editor.Layout));
        var id = editor.Place("desk", new(4, 4)); Capture();
        editor.SetDeskSeat(id, new(5, 7)); Capture();
        editor.SetOverflowSeat(new(2, 6)); Capture();
        editor.Duplicate(id, new(9, 4)); Capture();
        editor.SetDoor(new(2, 2)); Capture();
        editor.Paint(new(0, 0), new(4, 2), 2, "brush"); Capture();
        editor.Resize(18, 18); Capture();
        editor.Move(id, new(4, 9)); Capture();
        editor.Paint(new(17, 17), new(99, 99), 2, "fill"); Capture(); // Fill uses only its start tile.
        for (var i = snapshots.Count - 2; i >= 0; i--)
        { Assert.True(editor.Undo()); Assert.Equal(snapshots[i], Json(editor.Layout)); }
        Assert.False(editor.CanUndo);
        Assert.False(editor.Undo());
        for (var i = 1; i < snapshots.Count; i++)
        { Assert.True(editor.Redo()); Assert.Equal(snapshots[i], Json(editor.Layout)); }
        Assert.False(editor.CanRedo);
        Assert.False(editor.Redo());
        Assert.True(editor.Undo());
        editor.Place("plant", new(0, 17));
        Assert.False(editor.CanRedo);
        var edited = Json(editor.Layout);
        editor.ResetDefault();
        Assert.Equal(Json(LayoutLoader.Load(new AssetPack(), null)), Json(editor.Layout));
        Assert.True(editor.Undo());
        Assert.Equal(edited, Json(editor.Layout));
    }

    [Fact]
    public void InputCollectionsAndOlderSnapshotsCannotBeChangedByTheCaller()
    {
        var tiles = Enumerable.Repeat(1, 144).ToArray();
        Furniture[] furniture = [new("plant", 1, 5, 1, 1, Id: "plant", AssetId: "plant")];
        InteractionPoint[] points = [new("phone", 2, 2, "phone")];
        var zones = Room().Zones!.Select(z => z.Name == "workspace" ? z with { Points = points } : z).ToArray();
        OverflowSeat[] seats = [new("extra", 2, 8, "up")];
        var editor = new OfficeEditor(new AssetPack(), Room() with { Tiles = tiles, Furniture = furniture, Zones = zones, OverflowSeats = seats });
        var original = Json(editor.Layout);
        tiles[0] = 999;
        furniture[0] = furniture[0] with { X = 11 };
        points[0] = points[0] with { X = 99 };
        zones[0] = zones[0] with { Width = 99 };
        seats[0] = seats[0] with { X = 99 };
        Assert.Equal(original, Json(editor.Layout));
        Assert.Throws<NotSupportedException>(() => ((IList<int>)editor.Layout.Tiles!)[0] = 3);
        editor.SetOverflowSeat(new(3, 8));
        Assert.True(editor.Undo());
        Assert.Equal(original, Json(editor.Layout));
        Assert.Empty(editor.Validate());
    }

    [Fact]
    public void FloodFillStopsAtTileBoundariesAndRectanglesAndBrushesIncludeBothEndpoints()
    {
        var editor = new OfficeEditor(new AssetPack(), Room());
        editor.Paint(new(6, 11), new(6, 0), 0, "rect");
        var beforeFill = Json(editor.Layout);
        editor.Paint(new(2, 2), new(10, 10), 2, "fill");
        var filledTiles = editor.Layout.Tiles!;
        Assert.Equal(72, filledTiles.Count(id => id == 2));
        Assert.Equal(12, filledTiles.Count(id => id == 0));
        Assert.Equal(1, filledTiles[2 * 12 + 8]);
        Assert.True(editor.Undo());
        Assert.Equal(beforeFill, Json(editor.Layout));
        editor.Paint(new(0, 0), new(5, 3), 2, "brush");
        var brushedTiles = editor.Layout.Tiles!;
        Assert.Equal(2, brushedTiles[0]);
        Assert.Equal(2, brushedTiles[3 * 12 + 5]);
        Assert.Equal(6, brushedTiles.Count(id => id == 2));
        Assert.False(editor.CanRedo);
    }

    [Fact]
    public void SaveRoundTripsLocalSeatsAndIdsClearsRuntimeAgentIdsAndReplacesThePreviousBackup()
    {
        var assets = new AssetPack();
        var source = Room(16, 16) with {
            Furniture = [new("desk", 4, 4, 3, 2, "runtime-agent", "assigned", "desk", DeskId: "stable-desk",
                Seat: new("custom-seat", 1, 3, "typing", "up"))],
            OverflowSeats = [new("extra-seat", 2, 6, "left", "assigned")]
        };
        var editor = new OfficeEditor(assets, source);
        var file = Path.Combine(root, "saved", "layout.json");
        editor.Save(file);
        var first = File.ReadAllBytes(file);
        Assert.False(File.Exists(file + ".bak"));
        Assert.False(editor.CanUndo);
        var warnings = new List<string>();
        var loaded = LayoutLoader.Load(assets, file, warnings);
        Assert.Empty(warnings);
        Assert.Equal(Json(Persisted(editor.Layout)), Json(loaded));
        Assert.Null(loaded.Furniture.Single().AgentId);
        Assert.Equal("runtime-agent", editor.Layout.Furniture.Single().AgentId);
        Assert.Equal("runtime-agent", source.Furniture.Single().AgentId);
        editor.Move("assigned", new(4, 8));
        editor.Save(file);
        var second = File.ReadAllBytes(file);
        Assert.Equal(first, File.ReadAllBytes(file + ".bak"));
        editor.Paint(new(0, 0), new(0, 0), 2);
        editor.Save(file);
        Assert.Equal(second, File.ReadAllBytes(file + ".bak"));
        warnings.Clear();
        loaded = LayoutLoader.Load(assets, file, warnings);
        Assert.Empty(warnings);
        Assert.Equal(Json(Persisted(editor.Layout)), Json(loaded));
        Assert.Equal(new InteractionPoint("custom-seat", 1, 3, "typing", "up"), loaded.Furniture.Single().Seat);
        Assert.Equal("stable-desk", loaded.Furniture.Single().DeskId);
        Assert.Equal((2, 10, "left"), loaded.OverflowSeats!.Select(s => (s.X, s.Y, s.Facing)).Single());
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(file)!, "*.tmp"));
    }

    [Fact]
    public void InvalidSaveListsIndependentErrorsBeforeTouchingDestinationsBackupsOrDirectories()
    {
        var tiles = Room().Tiles!.ToArray();
        tiles[3 * 12 + 3] = 77;
        var invalid = Room() with {
            Version = 2, TileSize = 32, Door = new(-1, 0), Tiles = tiles,
            Zones = [new("lounge", 7, 5, 2, 4), new("door", 1, 1, 1, 1)],
            Furniture = [
                new("desk", 4, 4, 3, 2, Id: "desk-a", AssetId: "desk", DeskId: "same"),
                new("desk", 4, 4, 3, 2, Id: "desk-b", AssetId: "desk", DeskId: "same"),
                new("unknown", 0, 0, 1, 1, Id: "unknown", AssetId: "missing")
            ],
            OverflowSeats = [new("spill", 2, 6, "sideways", "missing-instance")]
        };
        var editor = new OfficeEditor(new AssetPack(), invalid);
        var errors = editor.Validate();
        Assert.Contains(errors, e => e.Contains("workspace"));
        Assert.Contains(errors, e => e.Contains("pantry"));
        Assert.Contains(errors, e => e.Contains("overlaps") && e.Contains("desk-a") && e.Contains("desk-b"));
        Assert.Contains(errors, e => e.Contains("Desk ID 'same'"));
        Assert.Contains(errors, e => e.Contains("Unknown tile ID 77"));
        Assert.Contains(errors, e => e.Contains("unknown") && e.Contains("variant"));
        Assert.Contains(errors, e => e.Contains("spill") && e.Contains("facing"));
        Assert.Contains(errors, e => e.Contains("missing-instance"));
        var existing = Path.Combine(root, "existing.json");
        File.WriteAllText(existing, "original-layout");
        File.WriteAllText(existing + ".bak", "original-backup");
        var error = Assert.Throws<InvalidDataException>(() => editor.Save(existing));
        Assert.All(errors, e => Assert.Contains(e, error.Message));
        Assert.Equal("original-layout", File.ReadAllText(existing));
        Assert.Equal("original-backup", File.ReadAllText(existing + ".bak"));
        var folder = Path.Combine(root, "must-not-be-created");
        Assert.Throws<InvalidDataException>(() => editor.Save(Path.Combine(folder, "layout.json")));
        Assert.False(Directory.Exists(folder));
        Assert.Empty(Directory.GetFiles(root, "*.tmp", SearchOption.AllDirectories));
        var incomplete = new OfficeEditor(new AssetPack(), invalid with { WidthTiles = 7, Furniture = null!, Zones = null, Tiles = null });
        Assert.Contains(incomplete.Validate(), e => e.Contains("furniture list"));
        Assert.Contains(incomplete.Validate(), e => e.Contains("between 8 and 128"));
    }

    [Fact]
    public void ResizePreservesRowMajorTilesAndRejectsClippingFurnitureZonesDoorsAndSeatOverrides()
    {
        var editor = new OfficeEditor(new AssetPack(), Room());
        editor.Paint(new(1, 2), new(1, 2), 2);
        editor.Paint(new(10, 10), new(10, 10), 2);
        editor.Resize(16, 14);
        Assert.Equal(2, editor.Layout.Tiles![2 * 16 + 1]);
        Assert.Equal(2, editor.Layout.Tiles[10 * 16 + 10]);
        Assert.Equal(1, editor.Layout.Tiles[13 * 16 + 15]);
        var id = editor.Place("desk", new(12, 8));
        var before = editor.Layout;
        Assert.Contains(id, Assert.Throws<InvalidOperationException>(() => editor.Resize(12, 12)).Message);
        Assert.Same(before, editor.Layout);
        editor.Delete(id);
        editor.SetOverflowSeat(new(15, 13));
        Assert.Contains("Overflow seat", Assert.Throws<InvalidOperationException>(() => editor.Resize(12, 12)).Message);
        editor.SetOverflowSeat(new(15, 13), false);
        editor.SetDoor(new(15, 1));
        Assert.Contains("Door", Assert.Throws<InvalidOperationException>(() => editor.Resize(12, 12)).Message);
        editor.SetDoor(new(1, 1));
        editor.SetZone("pantry", new(13, 1), new(15, 3));
        Assert.Contains("pantry", Assert.Throws<InvalidOperationException>(() => editor.Resize(12, 12)).Message);
        editor.SetZone("pantry", new(7, 1), new(8, 3));
        id = editor.Place("desk", new(4, 4));
        editor.SetDeskSeat(id, new(15, 10));
        Assert.Contains("point 'seat'", Assert.Throws<InvalidOperationException>(() => editor.Resize(12, 12)).Message);
        editor.SetDeskSeat(id, new(5, 6));
        editor.Resize(12, 12);
        Assert.Equal(144, editor.Layout.Tiles!.Count);
        Assert.Equal(2, editor.Layout.Tiles[2 * 12 + 1]);
        Assert.Equal(2, editor.Layout.Tiles[10 * 12 + 10]);
        Assert.Empty(editor.Validate());
        before = editor.Layout;
        Assert.Throws<ArgumentOutOfRangeException>(() => editor.Resize(129, 12));
        Assert.Same(before, editor.Layout);
    }
}
