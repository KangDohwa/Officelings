using System;
using System.Text.Json;
using Officelings.Core;
using Officelings.Core.Iso;
using Officelings.Core.Iso.Editing;
using Xunit;
namespace Officelings.Tests;
// Command acceptance for docs/iso-editor-spec.md.
[Collection("Iso assets")]
public sealed class IsoEditorCommandTests
{
    [Fact]
    public void C001_UnknownCommandTargets()
    {
        foreach (var action in new Func<IsoEditor, IsoEditResult>[]
        {
            e => e.Move("missing", new(2, 2)), e => e.Rotate("missing"), e => e.Delete("missing"),
            e => e.Duplicate("missing"), e => e.ClearSeat("missing"),
            e => e.MoveWall("missing", "right-1"), e => e.DuplicateWall("missing", "right-1")
        }) Reject(action, "C001");
    }
    [Fact]
    public void C002_UnknownOrWrongKindAssets()
    {
        Reject(e => e.Place("missing", new(2, 2)), "C002");
        Reject(e => e.Place("iso/wall-plain-l-v3", new(2, 2)), "C002");
        Reject(e => e.PlaceWall("iso/desk", "right-1"), "C002");
    }
    [Fact]
    public void C003_AuthoredRotationAndStatePreconditions()
    {
        Reject(e => e.Place("iso/desk", new(2, 2), 45), "C003");
        Reject(e => e.Place("iso/desk", new(2, 2), 0, "missing"), "C003");
        Reject(e => e.Rotate("desk-a", 45), "C003");
    }
    [Fact]
    public void C004_CanonicalSeatAndMountPreconditions()
    {
        Reject(e => e.SetSeat("chair-a", "desk-a"), "C004");
        var l = IsoEditorCoreFixture.Canonical;
        var e = new IsoEditor(IsoEditorCoreFixture.Pack, l with
        {
            Furniture = l.Furniture.Select(f => f.Id == "chair-a" ? f with
            {
                X = 6,
                DeskId = null
            } : f).ToArray(),
            WorkSeats = []
        });
        var before = e.State;
        var result = e.SetSeat("desk-a", "chair-a");
        Assert.False(result.Succeeded);
        Assert.Contains(result.Issues, i => i.RuleId == "C004");
        Assert.Equal(before, e.State);
    }
    [Fact]
    public void C005_EditableWallCommandPreconditions()
    {
        Reject(e => e.Move("left-0", new(2, 2)), "C005");
        Reject(e => e.Delete("left-0"), "C005");
        Reject(e => e.Duplicate("left-0"), "C005");
        Reject(e => e.MoveWall("desk-a", "left-0"), "C005");
    }
    [Fact]
    public void C006_MountedSlotAndPackReferences()
    {
        Place_MountRejectsWrongRootAndOccupiedSlot();
        UpdatePack_RejectsMissingCurrentUndoRedoAssets();
    }
    [Fact]
    public void C007_EmptyUndoAndRedo()
    {
        var e = IsoEditorCoreFixture.Editor();
        Assert.Equal("Nothing to undo.", Assert.Single(e.Undo().Issues).Message);
        Assert.Equal("Nothing to redo.", Assert.Single(e.Redo().Issues).Message);
        Assert.Equal(0, e.State.Revision);
    }
    [Fact]
    public void C008_InvalidSaveHasZeroWrites()
    {
        using var temp = new IsoEditorCoreFixture.Temp();
        var path = Path.Combine(temp.Root, "never-created", "iso-layout.json");
        var e = new IsoEditor(IsoEditorCoreFixture.Pack, IsoEditorCoreFixture.Raw("invalid/blocked-door.json"));
        var before = e.State;
        var r = e.Save(path);
        Assert.False(r.Succeeded);
        Assert.Contains(r.Issues, i => i.RuleId == "C008");
        Assert.Equal(before, e.State);
        Assert.False(Directory.Exists(Path.GetDirectoryName(path)));
    }
    [Fact]
    public void C009_SaveIoFailureRetainsHistoryAndBaseline()
    {
        using var temp = new IsoEditorCoreFixture.Temp();
        var e = IsoEditorCoreFixture.Editor();
        e.Move("desk-a", new(3, 4));
        var before = e.State;
        var r = e.Save(temp.Root);
        Assert.False(r.Succeeded);
        Assert.Contains(r.Issues, i => i.RuleId == "C009");
        Assert.Equal(before, e.State);
        Assert.Empty(Directory.GetFiles(temp.Root));
    }
    [Fact]
    public void Place_RejectsUnknownAsset() => C002_UnknownOrWrongKindAssets();
    [Fact]
    public void Place_RejectsAbsentRotationOrState() => C003_AuthoredRotationAndStatePreconditions();
    [Fact]
    public void Place_RejectsOutOfBoundsAndBlockingOverlap()
    {
        Reject(e => e.Place("iso/plant", new(10, 1)), "L005");
        Reject(e => e.Place("iso/plant", new(4, 4)), "L006");
    }
    [Fact]
    public void Place_DeskStartsWithoutChairOrDesignation()
    {
        var e = IsoEditorCoreFixture.Editor();
        var r = e.Place("iso/desk", new(7, 6));
        Assert.True(r.Succeeded, string.Join(";", r.Issues));
        Assert.Equal(3, e.State.Layout.Furniture.Count);
        Assert.Single(e.State.Layout.WorkSeats!);
        Assert.DoesNotContain(e.State.Layout.Furniture, f => f.DeskId == r.PrimaryTargetId);
    }
    [Fact]
    public void Place_MountRejectsWrongRootAndOccupiedSlot()
    {
        Reject(e => e.Place("iso/computer", new(4, 4), 0, hostDeskId: "chair-a"), "C004");
        Reject(e => e.Place("iso/computer", new(3, 4), 0, hostDeskId: "desk-a"), "C004");
        var e = IsoEditorCoreFixture.Editor();
        e.ClearSeat("desk-a");
        var r = e.Place("iso/computer", new(4, 4), 0, hostDeskId: "desk-a");
        Assert.True(r.Succeeded, string.Join(";", r.Issues));
        var before = e.State;
        Assert.Contains(e.Place("iso/computer", new(4, 4), 0, hostDeskId: "desk-a").Issues, i => i.RuleId == "C006");
        Assert.Equal(before, e.State);
        Assert.Equal("desk-a", r.PrimaryTargetId);
        Assert.True(e.SetSeat("desk-a", "chair-a", Assert.Single(e.State.Layout.Furniture, f => f.AssetId == "iso/computer").Id).Succeeded);
    }
    [Fact]
    public void Move_RejectsUnknownTarget() => Reject(e => e.Move("unknown", new(3, 3)), "C001");
    [Fact]
    public void Move_ResolvesLinkedPartsAndRejectsWholeGroupBounds()
    {
        var e = IsoEditorCoreFixture.Editor();
        var r = e.Move("chair-a", new(3, 4));
        Assert.True(r.Succeeded);
        Assert.Equal("desk-a", r.PrimaryTargetId);
        Assert.Equal(new Tile(4, 4), new Tile(e.State.Layout.Furniture.First(f => f.Id == "chair-a").X, e.State.Layout.Furniture.First(f => f.Id == "chair-a").Y));
        var before = e.State;
        Assert.False(e.Move("chair-a", new(9, 9)).Succeeded);
        Assert.Equal(before, e.State);
    }
    [Fact]
    public void Move_RetainsSeatIdsAndAllowsNavigationOnlyDraft()
    {
        var e = IsoEditorCoreFixture.Editor();
        var r = e.Place("iso/plant", new(0, 4));
        Assert.True(r.Succeeded);
        Assert.Contains(r.Issues, i => i.RuleId == "L012");
        Assert.Equal("desk-a", Assert.Single(e.State.Layout.WorkSeats!).Id);
        Assert.True(e.Undo().Succeeded);
        Assert.Empty(e.Validate());
    }
    [Fact]
    public void Rotate_RejectsUnknownTarget() => Reject(e => e.Rotate("unknown"), "C001");
    [Fact]
    public void Rotate_RejectsSingleRotationAndMissingLinkedVariant()
    {
        var pack = IsoPack.LoadDefault();
        IsoEditorCoreFixture.ReplaceFurniture(pack, pack.Manifest.Furniture!.Select(a => a.Id == "iso/chair" ? a with
        {
            Variants = a.Variants.Where(v => v.Rotation == 180).ToArray()
        } : a).ToArray());
        var e = new IsoEditor(pack, IsoEditorCoreFixture.Canonical);
        var before = e.State;
        Assert.Contains(e.Rotate("chair-a").Issues, i => i.RuleId == "C003");
        Assert.Equal(before, e.State);
        var l = IsoEditorCoreFixture.Canonical with
        {
            Furniture = [new("prop", "iso/plant", 3, 3)],
            WorkSeats = []
        };
        var one = new IsoEditor(pack, l);
        IsoEditorCoreFixture.ReplaceFurniture(pack, pack.Manifest.Furniture!.Select(a => a.Id == "iso/plant" ? a with
        {
            Variants = a.Variants.Where(v => v.Rotation == 0).ToArray()
        } : a).ToArray());
        Assert.Contains(one.Rotate("prop").Issues, i => i.RuleId == "C003");
    }
    [Fact]
    public void Rotate_CyclesCurrentStateAndRecomputesFourFacingContacts()
    {
        var e = IsoEditorCoreFixture.Editor();
        foreach (var rotation in new[] { 90, 180, 270, 0 })
        {
            var r = e.Rotate("chair-a");
            Assert.True(r.Succeeded, string.Join(";", r.Issues));
            var desk = e.State.Layout.Furniture.First(f => f.Id == "desk-a");
            var chair = e.State.Layout.Furniture.First(f => f.Id == "chair-a");
            var c = IsoContacts.Desk(desk.X, desk.Y, rotation);
            Assert.Equal(rotation, desk.Rotation);
            Assert.Equal(c.Approach, new Tile(chair.X, chair.Y));
            Assert.Equal(IsoGeometry.Rotation(c.Facing), chair.Rotation);
            Assert.Equal("desk-a", Assert.Single(e.State.Layout.WorkSeats!).Id);
        }
    }
    [Fact]
    public void Delete_RejectsUnknownOrFixedHostTarget()
    {
        Reject(e => e.Delete("unknown"), "C001");
        Reject(e => e.Delete("left-4"), "C005");
    }
    [Fact]
    public void Delete_RemovesWholeGroupOrWallAssembly()
    {
        var e = IsoEditorCoreFixture.Editor();
        Assert.True(e.Delete("chair-a").Succeeded);
        Assert.Empty(e.State.Layout.Furniture);
        Assert.Empty(e.State.Layout.WorkSeats!);
        Assert.True(e.Undo().Succeeded);
        var wall = WallEditor();
        Assert.True(wall.Delete("decor-a-edge-1").Succeeded);
        Assert.DoesNotContain(wall.State.Layout.WallSegments!, w => w.AssemblyId == "decor-a");
        Assert.Equal(20, wall.State.Layout.WallSegments!.Count);
    }
    [Fact]
    public void Duplicate_RejectsUnknownTarget() => Reject(e => e.Duplicate("unknown"), "C001");
    [Fact]
    public void Duplicate_RejectsInvalidDefaultTargetWithoutSearching()
    {
        var e = IsoEditorCoreFixture.Editor();
        var before = e.State;
        var p = e.Preview(new IsoDuplicateCommand("desk-a"));
        Assert.False(p.CanCommit);
        Assert.False(e.Duplicate("desk-a").Succeeded);
        Assert.Equal(before, e.State);
    }
    [Fact]
    public void Duplicate_CopiesEnabledDesignationWithoutReservation()
    {
        new IsoEditorCoreTests(new SilentOutput()).C012_DuplicateRemapsIdsAndLinks();
    }
    [Fact]
    public void SetSeat_RejectsMissingIds()
    {
        Reject(e => e.SetSeat("missing", "chair-a"), "C001");
        Reject(e => e.SetSeat("desk-a", "missing"), "C001");
        Reject(e => e.SetSeat("desk-a", "chair-a", "missing"), "C001");
    }
    [Fact]
    public void SetSeat_RejectsMisalignedOrAlreadyOwnedParts() => C004_CanonicalSeatAndMountPreconditions();
    [Fact]
    public void SetSeat_RejectsCustomRigAndThirteenthSeat()
    {
        var pack = IsoEditorCoreFixture.Pack;
        var l = new IsoEditor(pack, pack.Layout).State.Layout;
        var f = l.Furniture.Where(f => f.AssetId is "iso/desk" or "iso/chair").Concat(new IsoPlacement[]{
 new("desk-extra","iso/desk",0,7,270),new("chair-extra","iso/chair",0,6,90) }).ToArray();
        var e = new IsoEditor(pack, l with
        {
            Furniture = f
        });
        var before = e.State;
        var r = e.SetSeat("desk-extra", "chair-extra");
        Assert.False(r.Succeeded);
        Assert.Contains(r.Issues, i => i.RuleId == "L011");
        Assert.Equal(before, e.State);
        Reject(e2 => e2.SetSeat("chair-a", "desk-a"), "C004");
    }
    [Fact]
    public void ClearSeat_RejectsUnknownOrNoncanonicalDesk()
    {
        Reject(e => e.ClearSeat("missing"), "C001");
        Reject(e => e.ClearSeat("chair-a"), "C004");
    }
    [Fact]
    public void ClearSeat_KeepsLinksAndIsIdempotent()
    {
        var e = IsoEditorCoreFixture.Editor();
        var furniture = e.State.Layout.Furniture;
        Assert.True(e.ClearSeat("desk-a").Changed);
        Assert.Equal(furniture, e.State.Layout.Furniture);
        Assert.Empty(e.State.Layout.WorkSeats!);
        var before = e.State;
        Assert.False(e.ClearSeat("desk-a").Changed);
        Assert.Equal(before, e.State);
    }
    [Fact]
    public void PlaceWall_RejectsUnknownDefinitionOrOccupiedHost()
    {
        Reject(e => e.PlaceWall("unknown", "right-0"), "C002");
        var e = WallEditor();
        var before = e.State;
        Assert.Contains(e.PlaceWall(Window, "right-4").Issues, i => i.RuleId == "C005");
        Assert.Equal(before, e.State);
    }
    [Fact]
    public void PlaceWall_RejectsPartialTwoEdgeHostRun() => Reject(e => e.PlaceWall(Window, "right-9"), "C005");
    [Fact]
    public void MoveWall_RejectsWrongFaceAndPreservesEveryEdgeId()
    {
        var e = WallEditor();
        var before = e.State;
        Assert.Contains(e.MoveWall("decor-a-edge-1", "left-0").Issues, i => i.RuleId == "C005");
        Assert.Equal(before, e.State);
        var ids = e.State.Layout.WallSegments!.Where(w => w.AssemblyId == "decor-a").Select(w => w.Id).ToArray();
        Assert.True(e.MoveWall("decor-a-edge-1", "right-6").Succeeded);
        var moved = e.State.Layout.WallSegments!.Where(w => w.AssemblyId == "decor-a").ToArray();
        Assert.Equal(ids, moved.Select(w => w.Id));
        Assert.Equal(new[]{
"right-6","right-7"}
       , moved.Select(w => w.HostId));
    }
    [Fact]
    public void DuplicateWall_RejectsOccupiedRunAndAllocatesFreshIds()
    {
        var e = WallEditor();
        var before = e.State;
        Assert.Contains(e.DuplicateWall("decor-a", "right-4").Issues, i => i.RuleId == "C005");
        Assert.Equal(before, e.State);
        var r = e.DuplicateWall("decor-a", "right-6");
        Assert.True(r.Succeeded);
        Assert.NotEqual("decor-a", r.PrimaryTargetId);
        Assert.Equal(2, e.State.Layout.WallSegments!.Count(w => w.AssemblyId == r.PrimaryTargetId));
        Assert.Equal(2, e.State.Layout.WallSegments!.Count(w => w.AssemblyId == "decor-a"));
    }
    [Fact]
    public void Preview_MatchesDispatchWithoutConsumingIdsOrHistory()
    {
        var e = IsoEditorCoreFixture.Editor();
        var before = e.State;
        var command = new IsoDuplicateCommand("desk-a", new(7, 6));
        foreach (var unknown in new IsoEditorCommand[] { new IsoMoveCommand("missing", new(2, 2)), new IsoPlaceCommand("missing", new(2, 2)) })
        {
            var invalid = e.Preview(unknown);
            Assert.False(invalid.CanCommit); Assert.Empty(invalid.Footprint); Assert.Empty(invalid.Points);
        }
        var p = e.Preview(command);
        var p2 = e.Preview(command);
        Assert.Equal(before, e.State);
        Assert.Equal(JsonSerializer.Serialize(p.Candidate, StateJson.Options), JsonSerializer.Serialize(p2.Candidate, StateJson.Options));
        Assert.True(p.CanCommit);
        Assert.Contains(p.Points, x => x.Kind == "pelvis");
        Assert.Contains(p.Points, x => x.Kind == "blocked");
        var result = e.Duplicate("desk-a", new(7, 6));
        Assert.True(result.Succeeded);
        Assert.Equal(JsonSerializer.Serialize(p.Candidate, StateJson.Options), JsonSerializer.Serialize(e.State.Layout, StateJson.Options));
    }
    [Fact]
    public void UpdatePack_RejectsMissingCurrentUndoRedoAssets()
    {
        var original = IsoEditorCoreFixture.Pack;
        var replacement = IsoPack.LoadDefault();
        var e = new IsoEditor(original, IsoEditorCoreFixture.Raw("valid/linked-computer.json"));
        var before = e.State;
        Assert.Contains(e.UpdatePack(replacement).Issues, i => i.RuleId == "C006");
        Assert.Equal(before, e.State);
        e.Delete("desk-a");
        before = e.State;
        Assert.Contains(e.UpdatePack(replacement).Issues, i => i.RuleId == "C006");
        Assert.Equal(before, e.State);
        e.Undo();
        e.Redo();
        Assert.Contains("iso/computer", e.ReferencedAssetIds);
        Assert.False(e.UpdatePack(replacement).Succeeded);
    }
    [Fact]
    public void Save_RejectsOversizeJsonAndUnsafeOutputBeforeWrite()
    {
        using var temp = new IsoEditorCoreFixture.Temp();
        var path = Path.Combine(temp.Root, "never", "iso-layout.json");
        var l = IsoEditorCoreFixture.Canonical;
        var oversized = new IsoEditor(IsoEditorCoreFixture.Pack, l with
        {
            Furniture = l.Furniture.Select(f => f with
            {
                State = new string('x', 4_194_304)
            }).ToArray()
        });
        var r = oversized.Save(path);
        Assert.False(r.Succeeded);
        Assert.Contains(r.Issues, i => i.RuleId == "L017");
        Assert.False(Directory.Exists(Path.GetDirectoryName(path)));
        Assert.Throws<ArgumentException>(() => StateJson.OutputPath(Path.Combine(temp.Root, ".codex", "iso-layout.json"), temp.Root));
    }
    private const string Window = "iso/env-v3/large-window-day-r";
    private static IsoEditor WallEditor() => new(IsoEditorCoreFixture.Pack, IsoEditorCoreFixture.Raw("valid/two-edge-decor.json"));
    private static void Reject(Func<IsoEditor, IsoEditResult> command, string rule)
    {
        var e = IsoEditorCoreFixture.Editor();
        var before = e.State;
        var result = command(e);
        Assert.False(result.Succeeded);
        Assert.False(result.Changed);
        Assert.Contains(result.Issues, i => i.RuleId == rule);
        Assert.Equal(before, e.State);
    }
    private sealed class SilentOutput : Xunit.Abstractions.ITestOutputHelper
    {
        public void WriteLine(string message)
        {
        }
        public void WriteLine(string format, params object[] args)
        {
        }
    }
}
