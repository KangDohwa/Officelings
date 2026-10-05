using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Vibedotcom.Core;
using Vibedotcom.Core.Iso;
using Vibedotcom.Core.Iso.Editing;
using Xunit.Abstractions;
using Xunit;

namespace Vibedotcom.Tests;

// Core acceptance for docs/iso-editor-spec.md.
[Collection("Iso assets")]
public sealed class IsoEditorCoreTests(ITestOutputHelper output)
{
    [Fact]
    public void L001_SchemaAndCapabilities() => IsoEditorCoreFixture.AssertRule("L001");

    [Fact]
    public void L002_FixedRoomGeometry() => IsoEditorCoreFixture.AssertRule("L002");

    [Fact]
    public void L003_UniqueBoundedIds() => IsoEditorCoreFixture.AssertRule("L003");

    [Fact]
    public void L004_ExactAuthoredVariant() => IsoEditorCoreFixture.AssertRule("L004");

    [Fact]
    public void L005_WholeFootprintAndContactBounds() => IsoEditorCoreFixture.AssertRule("L005");

    [Fact]
    public void L006_BlockingCellOverlap() => IsoEditorCoreFixture.AssertRule("L006");

    [Fact]
    public void L007_PhysicalChairOccupancy() => IsoEditorCoreFixture.AssertRule("L007");

    [Fact]
    public void L008_LoungeRugExclusion() => IsoEditorCoreFixture.AssertRule("L008");

    [Fact]
    public void L009_AssemblyLinkOwnership() => IsoEditorCoreFixture.AssertRule("L009");

    [Fact]
    public void L010_CanonicalSeatDesignation() => IsoEditorCoreFixture.AssertRule("L010");

    [Fact]
    public void L011_SeatLimitAndUniqueness() => IsoEditorCoreFixture.AssertRule("L011");

    [Fact]
    public void L012_OpenDoorAndSpawn() => IsoEditorCoreFixture.AssertRule("L012");

    [Fact]
    public void L013_EveryApproachReachable() => IsoEditorCoreFixture.AssertRule("L013");

    [Fact]
    public void L014_OccupiedSeatAisleAccess() => IsoEditorCoreFixture.AssertRule("L014");

    [Fact]
    public void L015_TilesAndRequiredZones() => IsoEditorCoreFixture.AssertRule("L015");

    [Fact]
    public void L016_CompleteWallHostSlots() => IsoEditorCoreFixture.AssertRule("L016");

    [Fact]
    public void L017_LayoutAndDrawItemLimits() => IsoEditorCoreFixture.AssertRule("L017");

    [Fact]
    public void L018_ConnectedPetPathOutsideReservations() => IsoEditorCoreFixture.AssertRule("L018");

    [Fact]
    public void L019_ResolvedDepthWithoutCycles()
    {
        var pack = IsoPack.LoadDefault(); var v = pack.Variant("iso/desk", 0);
        IsoEditorCoreFixture.ReplaceFurniture(pack, pack.Manifest.Furniture!.Select(a => a.Id != "iso/desk" ? a : a with
        { Variants = a.Variants.Select(old => old.Rotation != 0 ? old : old with { Parts = [v.Parts[0] with { SheetId = "missing" }] }).ToArray() }).ToArray());
        Assert.Contains(new IsoEditor(pack, IsoEditorCoreFixture.Canonical).Validate(), i => i.RuleId == "L019");
    }

    [Fact]
    public void C010_NoOpPreservesRedoAndRevision()
    {
        var e = IsoEditorCoreFixture.Editor(); Assert.True(e.Move("desk-a", new(3, 4)).Succeeded); e.Undo();
        var before = e.State; var result = e.Move("chair-a", new(4, 4));
        Assert.True(result.Succeeded); Assert.False(result.Changed); Assert.Equal(before, e.State); Assert.True(e.State.CanRedo);
    }

    [Fact]
    public void C011_OwnedAtomicSnapshotsAndHistoryLimit()
    {
        var source = IsoEditorCoreFixture.Canonical; var mutable = source.Furniture.ToList(); var tiles = source.Tiles!.ToList();
        var e = new IsoEditor(IsoEditorCoreFixture.Pack, source with { Furniture = mutable, Tiles = tiles });
        mutable.Clear(); tiles[0] = 255; Assert.Equal(2, e.State.Layout.Furniture.Count); Assert.Equal(1, e.State.Layout.Tiles![0]);
        Assert.Throws<NotSupportedException>(() => ((IList<IsoPlacement>)e.State.Layout.Furniture).Add(new("bad", "iso/plant", 1, 1)));
        for (var k = 0; k < 102; k++) Assert.True(e.Move("desk-a", new(k % 2 == 0 ? 3 : 4, 4)).Changed);
        var revision = e.State.Revision; var count = 0; while (e.State.CanUndo) { Assert.True(e.Undo().Succeeded); count++; }
        Assert.Equal(100, count); Assert.Equal(revision + 100, e.State.Revision);
        Assert.True(e.Move("desk-a", new(2, 4)).Succeeded); Assert.False(e.State.CanRedo);
    }

    [Fact]
    public void C012_DuplicateRemapsIdsAndLinks()
    {
        var e = IsoEditorCoreFixture.Editor(); var result = e.Duplicate("chair-a", new(7, 6));
        Assert.True(result.Succeeded, string.Join(";", result.Issues)); var id = result.PrimaryTargetId!;
        Assert.NotEqual("desk-a", id); Assert.Equal(4, e.State.Layout.Furniture.Count);
        var seat = Assert.Single(e.State.Layout.WorkSeats!, s => s.DeskId == id);
        Assert.Equal(id, seat.Id); Assert.NotEqual("chair-a", seat.ChairId); Assert.Contains(e.State.Layout.Furniture, f => f.Id == seat.ChairId && f.DeskId == id);
        Assert.True(e.Undo().Succeeded); Assert.Equal(2, e.State.Layout.Furniture.Count);
    }

    [Fact]
    public void C013_UndoableTwelveSeatReset()
    {
        var e = IsoEditorCoreFixture.Editor(); var before = e.State.Layout; Assert.True(e.ResetDefault().Succeeded);
        Assert.Equal(12, e.State.Layout.WorkSeats!.Count); Assert.Empty(e.Validate()); Assert.True(e.Undo().Succeeded); Assert.Same(before, e.State.Layout);
    }

    [Fact]
    public void P001_FlatTrackerAndPrivacyIsolation()
    {
        using var temp = new IsoEditorCoreFixture.Temp(); var flat = Path.Combine(temp.Root, "layout.json"); File.WriteAllText(flat, "synthetic-flat");
        var e = IsoEditorCoreFixture.Editor(); Assert.True(e.Save(Path.Combine(temp.Root, "iso-layout.json")).Succeeded);
        Assert.Equal("synthetic-flat", File.ReadAllText(flat)); var json = File.ReadAllText(Path.Combine(temp.Root, "iso-layout.json"));
        Assert.DoesNotContain("agentId", json); Assert.DoesNotContain("reservation", json); Assert.DoesNotContain("synthetic", json);
        Assert.Equal(5, StateJson.SchemaVersion);
    }

    [Fact]
    public void P002_LegacyExplicitEmptyAndStartupFallback()
    {
        using var temp = new IsoEditorCoreFixture.Temp(); var pack = IsoEditorCoreFixture.Pack; var importer = new IsoEditorCoreFixture.Importer();
        var absent = IsoEditorStorage.Load(pack, new(temp.Root), importer); Assert.False(absent.UsedUserLayout); Assert.Equal(12, absent.Layout.WorkSeats!.Count);
        var path = Path.Combine(temp.Root, "iso-layout.json");
        File.Copy(IsoEditorCoreFixture.PathTo("valid/legacy-inferred-seat.json"), path); var bytes = File.ReadAllBytes(path);
        var legacy = IsoEditorStorage.Load(pack, new(temp.Root), importer); Assert.True(legacy.UsedUserLayout); Assert.Single(legacy.Layout.WorkSeats!); Assert.Equal(bytes, File.ReadAllBytes(path));
        File.Copy(IsoEditorCoreFixture.PathTo("valid/explicit-empty-seats.json"), path, true);
        Assert.Empty(IsoEditorStorage.Load(pack, new(temp.Root), importer).Layout.WorkSeats!);
        File.WriteAllText(path, "{}"); var failed = IsoEditorStorage.Load(pack, new(temp.Root), importer);
        Assert.False(failed.UsedUserLayout); Assert.Equal(12, failed.Layout.WorkSeats!.Count); Assert.Contains(failed.Issues, i => i.RuleId == "P002"); Assert.Equal("{}", File.ReadAllText(path));
    }

    [Fact]
    public void P003_ReservationAndDesignSurviveRuntimeEdits()
    {
        var e = IsoEditorCoreFixture.Editor(); var office = new IsoOffice(IsoEditorCoreFixture.Pack, 42, e.State.Layout) { PetsEnabled = false };
        AgentInfo[] agents = [IsoEditorCoreFixture.Agent("a")]; var first = IsoEditorCoreFixture.Advance(office, agents, 8);
        var body = Assert.Single(first.Items, i => i.Layer == IsoLayer.Character); Assert.StartsWith("typing/", body.Clip);
        Assert.True(e.Move("desk-a", new(3, 4)).Succeeded); Assert.Empty(office.ApplyEditorState(e.State));
        Assert.Equal(4, office.Layout.Furniture.First(f => f.Id == "desk-a").X);
        var raising = office.Update(agents, IsoEditorCoreFixture.Epoch, .1);
        Assert.Contains(raising.Items, i => i.Layer == IsoLayer.Character && i.Clip.StartsWith("stand-up/"));
        Assert.Equal(4, office.Layout.Furniture.First(f => f.Id == "desk-a").X);
        var moved = IsoEditorCoreFixture.Advance(office, agents, 8); Assert.Equal(3, office.Layout.Furniture.First(f => f.Id == "desk-a").X);
        var after = Assert.Single(moved.Items, i => i.Layer == IsoLayer.Character); Assert.Equal(body.SheetId, after.SheetId); Assert.StartsWith("typing/", after.Clip);
        Assert.True(e.ClearSeat("desk-a").Succeeded); Assert.Empty(office.ApplyEditorState(e.State)); IsoEditorCoreFixture.Advance(office, agents, 4);
        Assert.Empty(office.Layout.WorkSeats!);
    }

    [Fact]
    public void P004_AtomicSaveRoundtripBackupAndDirtyBaseline()
    {
        using var temp = new IsoEditorCoreFixture.Temp(); var path = Path.Combine(temp.Root, "iso-layout.json"); var e = IsoEditorCoreFixture.Editor();
        Assert.True(e.Move("desk-a", new(3, 4)).Succeeded); Assert.True(e.State.IsDirty); var revision = e.State.Revision;
        Assert.True(e.Save(path).Succeeded); Assert.False(e.State.IsDirty); Assert.Equal(revision, e.State.Revision); var old = File.ReadAllBytes(path);
        Assert.True(e.Move("desk-a", new(4, 4)).Succeeded); Assert.True(e.Save(path).Succeeded); Assert.Equal(old, File.ReadAllBytes(path + ".bak"));
        Assert.Equal(e.State.Layout.Furniture, IsoLayoutLoader.Load(path, IsoEditorCoreFixture.Pack).Furniture);
        e.Undo(); Assert.True(e.State.IsDirty); e.Redo(); Assert.False(e.State.IsDirty); Assert.Empty(Directory.GetFiles(temp.Root, "*.tmp"));
    }

    [Fact]
    public void GoldenLayoutsHaveAllRequiredRulesAndValidFilesHaveNoErrors()
    {
        using var cases = JsonDocument.Parse(File.ReadAllText(Path.Combine(IsoEditorCoreFixture.Root, "cases.json")));
        foreach (var c in cases.RootElement.GetProperty("cases").EnumerateArray().Where(c => c.GetProperty("kind").GetString() == "layout"))
        {
            var file = Path.Combine(IsoEditorCoreFixture.Root, c.GetProperty("path").GetString()!);
            var errors = new IsoEditor(IsoEditorCoreFixture.Pack, JsonSerializer.Deserialize<IsoLayout>(File.ReadAllText(file), StateJson.Options)!).Validate();
            if (c.GetProperty("valid").GetBoolean()) Assert.True(errors.IsEmpty, file + ": " + string.Join(";", errors.Select(i => i.RuleId + " " + i.Message)));
            else foreach (var rule in c.GetProperty("expectedRules").EnumerateArray()) Assert.Contains(errors, i => i.RuleId == rule.GetString());
        }
    }

    [Fact]
    public void PendingReplacementAndInvalidDraftKeepOneLiveRig()
    {
        var e = IsoEditorCoreFixture.Editor(); var office = new IsoOffice(IsoEditorCoreFixture.Pack, 42, e.State.Layout) { PetsEnabled = false };
        AgentInfo[] agents = [IsoEditorCoreFixture.Agent("a")]; IsoEditorCoreFixture.Advance(office, agents, 8);
        e.Move("desk-a", new(3, 4)); Assert.Empty(office.ApplyEditorState(e.State)); office.Update(agents, IsoEditorCoreFixture.Epoch, .1);
        e.Move("desk-a", new(2, 4)); Assert.Empty(office.ApplyEditorState(e.State));
        IsoEditorCoreFixture.Advance(office, agents, 8); Assert.Equal(2, office.Layout.Furniture.First(f => f.Id == "desk-a").X);
        e.Move("desk-a", new(3, 4)); office.ApplyEditorState(e.State);
        var invalid = new IsoEditor(IsoEditorCoreFixture.Pack, IsoEditorCoreFixture.Raw("invalid/blocked-door.json"));
        Assert.Contains(office.ApplyEditorState(invalid.State), i => i.RuleId == "L012"); IsoEditorCoreFixture.Advance(office, agents, 2);
        Assert.Equal(2, office.Layout.Furniture.First(f => f.Id == "desk-a").X);
    }

    [Fact]
    public void ReversePublishedMasksResolveRootsAndFixedWallsStopSelection()
    {
        var pack = IsoEditorCoreFixture.Pack; var layout = IsoEditorCoreFixture.Canonical; var builder = new IsoSceneBuilder(pack, layout); var scene = builder.Build([]);
        var chair = scene.Items.First(i => i.Layer == IsoLayer.Furniture && i.HitTargetId == "desk-a" && i.InstanceId.StartsWith("chair-a/"));
        var frame = pack.Frame(chair); var pixels = Enumerable.Range(0, frame.Width * frame.Height).Where(k => pack.Opaque(chair, k % frame.Width, k / frame.Width)).ToArray(); Assert.NotEmpty(pixels);
        var p = pixels[0]; var x = chair.X + p % frame.Width; var y = chair.Y + p / frame.Width;
        var only = scene with { Items = [chair with { Depth = long.MaxValue }, chair with { Depth = 0 }] };
        Assert.Equal("desk-a", IsoHitTest.HitTest(pack, layout, only, x, y)!.TargetId);
        var unknown = chair with { InstanceId = "foreground", HitTargetId = null, Depth = long.MinValue };
        Assert.Null(IsoHitTest.HitTest(pack, layout, scene with { Items = [chair with { Depth = long.MaxValue }, unknown] }, x, y));
        Assert.Null(IsoHitTest.HitTest(pack, layout, scene with { Items = [chair with { Alpha = 31 }] }, x, y));
        var camera = builder.Camera; var ground = IsoGeometry.Ground(4, 4);
        Assert.Equal(new Tile(4, 4), IsoHitTest.PickTile(layout, camera, ground.X + camera.X, ground.Y + camera.Y));
        Assert.Null(IsoHitTest.PickTile(layout, camera, double.NaN, 0));
    }

    [Fact]
    public void BenchmarkOneHundredBundledOperations()
    {
        var pack = IsoEditorCoreFixture.Pack; var e = new IsoEditor(pack, pack.Layout); var commands = new double[100]; var previews = new double[100];
        e.Validate(); e.Preview(new IsoMoveCommand("desk-1", new(2, 1)));
        for (var k = 0; k < 100; k++)
        {
            var watch = Stopwatch.StartNew(); var result = k % 2 == 0 ? e.ClearSeat("desk-1") : e.SetSeat("desk-1", "chair-1");
            commands[k] = watch.Elapsed.TotalMilliseconds; Assert.True(result.Succeeded, string.Join(";", result.Issues));
            watch.Restart(); var preview = e.Preview(new IsoMoveCommand("desk-1", new(2, 1))); previews[k] = watch.Elapsed.TotalMilliseconds; Assert.True(preview.CanCommit);
        }
        Array.Sort(commands); Array.Sort(previews);
        output.WriteLine($"100 operations, command+validation p50={commands[49]:F3}ms p95={commands[94]:F3}ms; preview p50={previews[49]:F3}ms p95={previews[94]:F3}ms");
        output.WriteLine($"{System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}, {Environment.ProcessorCount} logical processors, {pack.Layout.Furniture.Count} bundled objects");
    }

    [Fact]
    public void MountedAdapterSuppressesOnlyItsPublishedParts()
    {
        var pack = IsoEditorCoreFixture.Pack; var l = IsoEditorCoreFixture.Canonical;
        var e = new IsoEditor(pack, IsoEditorCoreFixture.Raw("valid/linked-computer.json"));
        var scene = new IsoSceneBuilder(pack, e.State.Layout).Build([]);
        var adapter = pack.Variant("iso/computer", 0);
        foreach (var p in adapter.Parts)
        {
            Assert.DoesNotContain(scene.Items, i => i.InstanceId == "desk-a/" + p.PartId);
            Assert.Contains(scene.Items, i => i.InstanceId == "computer-a/" + p.PartId);
        }
        var retained = pack.Variant("iso/desk", 0).Parts.Where(p => !adapter.Parts.Any(a => a.PartId == p.PartId)).ToArray();
        Assert.NotEmpty(retained); Assert.All(retained, p => Assert.Contains(scene.Items, i => i.InstanceId == "desk-a/" + p.PartId));
        Assert.DoesNotContain(scene.Items, i => i.InstanceId == "desk-a/screen");
        Assert.Contains(scene.Items, i => i.InstanceId == "computer-a/screen");
        AgentInfo[] agents = [IsoEditorCoreFixture.Agent("a")]; var office = new IsoOffice(pack, 42, e.State.Layout);
        Assert.Contains(IsoEditorCoreFixture.Advance(office, agents, 8).Items, i => i.Layer == IsoLayer.Character && i.Clip.StartsWith("typing/"));
    }

    [Fact]
    public void MalformedConstructorInputsAggregateWithoutThrowing()
    {
        var l = IsoEditorCoreFixture.Canonical;
        var missing = new IsoEditor(IsoEditorCoreFixture.Pack, l with { Furniture = null! });
        Assert.Contains(missing.Validate(), i => i.RuleId == "L001"); Assert.False(missing.Place("iso/plant", new(2, 2)).Succeeded);
        Assert.False(missing.Preview(new IsoPlaceCommand("iso/plant", new(2, 2))).CanCommit);
        Assert.Null(IsoHitTest.HitTest(IsoEditorCoreFixture.Pack, missing.State.Layout, new IsoSceneBuilder(IsoEditorCoreFixture.Pack, l).Build([]), 0, 0));
        var nonfinite = new IsoEditor(IsoEditorCoreFixture.Pack, l with { WallSegments = l.WallSegments!.Select((w, k) => k == 0 ? w with { Edge = new(double.NaN, -.5) } : w).ToArray() });
        Assert.Contains(nonfinite.Validate(), i => i.RuleId == "L001"); Assert.Contains(nonfinite.Validate(), i => i.RuleId == "L016");
    }

    [Fact]
    public void WallNoOpAndSceneRevisionSurviveReload()
    {
        var wall = new IsoEditor(IsoEditorCoreFixture.Pack, IsoEditorCoreFixture.Raw("valid/two-edge-decor.json"));
        wall.MoveWall("decor-a", "right-6"); wall.Undo(); var before = wall.State;
        Assert.False(wall.MoveWall("decor-a-edge-1", "right-4").Changed); Assert.Equal(before, wall.State);
        var e = IsoEditorCoreFixture.Editor(); var office = new IsoOffice(IsoEditorCoreFixture.Pack, 42, e.State.Layout) { PetsEnabled = false };
        var first = office.Update([], IsoEditorCoreFixture.Epoch, 0); e.ClearSeat("desk-a"); Assert.Empty(office.ApplyEditorState(e.State));
        var after = office.Update([], IsoEditorCoreFixture.Epoch, 0); Assert.True(after.Revision > first.Revision);
        Assert.Empty(office.Layout.WorkSeats!);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WalkerAndPetRetreatThroughOldNavigationBeforeNewBlocker(bool pet)
    {
        var e = IsoEditorCoreFixture.Editor(); var pack = IsoEditorCoreFixture.Pack;
        var office = new IsoOffice(pack, 42, e.State.Layout) { PetsEnabled = pet };
        AgentInfo[] agents = pet ? [] : [IsoEditorCoreFixture.Agent("walker")];
        var first = IsoEditorCoreFixture.Advance(office, agents, pet ? 0 : .6);
        var body = first.Items.First(i => i.Layer == (pet ? IsoLayer.Pet : IsoLayer.Character));
        var point = Ground(office, pack, body); var tile = new Tile(IsoGeometry.Snap(point.I), IsoGeometry.Snap(point.J));
        Assert.NotEqual(office.Layout.Door, tile);
        var original = office.Layout;
        var result = e.Place("iso/plant", tile); Assert.True(result.Succeeded, string.Join(";", result.Issues)); Assert.Empty(result.Issues);
        Assert.Empty(office.ApplyEditorState(e.State)); Assert.Equal(0, office.NextChangeSeconds); Assert.Same(original, office.Layout);
        var swapped = false;
        for (var k = 0; k < 100; k++)
        {
            var scene = office.Update(agents, IsoEditorCoreFixture.Epoch, .05);
            if (ReferenceEquals(original, office.Layout)) continue;
            swapped = true;
            var nav = IsoLayoutLoader.Navigation(office.Layout, pack);
            foreach (var moving in scene.Items.Where(i => i.Layer == (pet ? IsoLayer.Pet : IsoLayer.Character)))
            {
                var position = Ground(office, pack, moving);
                Assert.True(nav.Walkable(new(IsoGeometry.Snap(position.I), IsoGeometry.Snap(position.J))));
            }
        }
        Assert.True(swapped);
    }

    private static IsoGridPoint Ground(IsoOffice office, IsoPack pack, IsoDrawItem item)
    {
        var clip = pack.Clip(item.SheetId, item.Clip); var anchor = clip.Anchor ?? pack.Sheet(item.SheetId).Anchor;
        return IsoGeometry.Inverse(item.X - office.Camera.X + anchor.X - (clip.FootOffset?.X ?? 0),
            item.Y - office.Camera.Y + anchor.Y - (clip.FootOffset?.Y ?? 0));
    }

    [Fact]
    public void EveryPaletteFamilySupportsWholeInstanceCommands()
    {
        var pack = IsoEditorCoreFixture.Pack; var blank = IsoEditorCoreFixture.Canonical with { Furniture = [], WorkSeats = [] };
        foreach (var asset in pack.Manifest.Furniture!)
        {
            var e = new IsoEditor(pack, blank); var v = asset.Variants.First(x => x.State == "default");
            var placed = e.Place(asset.Id, new(3, 3), v.Rotation, v.State);
            Assert.True(placed.Succeeded, asset.Id + ": " + string.Join(";", placed.Issues)); Assert.Empty(e.State.Layout.WorkSeats!);
            Assert.True(e.Move(placed.PrimaryTargetId!, new(5, 5)).Succeeded);
            if (asset.Variants.Where(x => x.State == v.State).Select(x => x.Rotation).Distinct().Count() > 1)
                Assert.True(e.Rotate(placed.PrimaryTargetId!).Succeeded);
            var copy = e.Duplicate(placed.PrimaryTargetId!, new(1, 1)); Assert.True(copy.Succeeded, asset.Id + ": " + string.Join(";", copy.Issues));
            Assert.Equal(2, e.State.Layout.Furniture.Count); Assert.True(e.Delete(copy.PrimaryTargetId!).Succeeded);
            Assert.Single(e.State.Layout.Furniture); Assert.True(e.Undo().Succeeded); Assert.Equal(2, e.State.Layout.Furniture.Count);
        }
    }

    [Fact]
    public void PetComponentsExcludeDoorAndAnImpossibleRetreatKeepsLiveLayout()
    {
        var door = new Tile(0, 4); Tile[] leaves = [door, new(0, 3), new(0, 5)];
        var blocked = from x in Enumerable.Range(0, 10)
                      from y in Enumerable.Range(0, 10)
                      let tile = new Tile(x, y)
                      where !leaves.Contains(tile)
                      select tile;
        Assert.Empty(IsoEditorNavigation.PetComponent(new(10, 10, blocked), door, []));
        var blank = IsoEditorCoreFixture.Canonical with { Furniture = [], WorkSeats = [] };
        IsoLayout Corridor(params Tile[] cells) => blank with
        {
            Tiles = Enumerable.Range(0, 100).Select(k => k == 40 || cells.Contains(new(k % 10, k / 10)) ? 1 : 0).ToArray()
        };
        var old = Corridor(new(1, 4), new(2, 4)); var candidate = Corridor(new(0, 5), new(0, 6));
        var pack = IsoEditorCoreFixture.Pack; var e = new IsoEditor(pack, candidate); Assert.Empty(e.Validate());
        var office = new IsoOffice(pack, 42, old); var before = office.Layout;
        Assert.Contains(office.ApplyEditorState(e.State), i => i.RuleId == "L018"); Assert.Same(before, office.Layout);
        var scene = office.Update([], IsoEditorCoreFixture.Epoch, .1);
        Assert.All(scene.Items.Where(i => i.Layer == IsoLayer.Pet), i => Assert.NotEqual(door,
            new Tile(IsoGeometry.Snap(Ground(office, pack, i).I), IsoGeometry.Snap(Ground(office, pack, i).J))));
    }

    [Fact]
    public void MountOnEnabledSeatUpdatesTheWholeRigInOneHistoryStep()
    {
        var e = IsoEditorCoreFixture.Editor(); var before = e.State;
        var result = e.Place("iso/computer", new(4, 4), hostDeskId: "desk-a");
        Assert.True(result.Succeeded); Assert.Empty(result.Issues); Assert.Equal("desk-a", result.PrimaryTargetId);
        var computer = Assert.Single(e.State.Layout.Furniture, f => f.AssetId == "iso/computer");
        Assert.Equal(computer.Id, Assert.Single(e.State.Layout.WorkSeats!).ComputerId);
        Assert.Equal(before.Revision + 1, e.State.Revision);
        Assert.True(e.Undo().Succeeded); Assert.Null(Assert.Single(e.State.Layout.WorkSeats!).ComputerId);
        Assert.DoesNotContain(e.State.Layout.Furniture, f => f.AssetId == "iso/computer");
        Assert.True(e.Redo().Succeeded); Assert.Equal(computer.Id, Assert.Single(e.State.Layout.WorkSeats!).ComputerId);
    }

    [Fact]
    public void PublishedScreenStatesRetainAuthoredHoverPassthrough()
    {
        var pack = IsoPack.LoadDefault();
        IsoEditorCoreFixture.ReplaceFurniture(pack, pack.Manifest.Furniture!.Select(a => a.Id == "iso/desk" ? a with
        {
            Variants = a.Variants.Select(v => v with { Screen = v.Screen == null ? null : v.Screen with { HoverPassthrough = true } }).ToArray()
        } : a).ToArray());
        var l = IsoEditorCoreFixture.Canonical; var scene = new IsoSceneBuilder(pack, l).Build([]);
        var screen = scene.Items.First(i => i.InstanceId == "desk-a/screen"); var frame = pack.Frame(screen);
        var pixel = Enumerable.Range(0, frame.Width * frame.Height).First(k => pack.Opaque(screen, k % frame.Width, k / frame.Width));
        Assert.Null(IsoHitTest.HitTest(pack, l, scene with { Items = [screen] }, screen.X + pixel % frame.Width, screen.Y + pixel / frame.Width));
    }

    [Fact]
    public void WorldItemLimitRejectsLargeNonblockingLayoutBeforeDepthOrdering()
    {
        var pack = IsoEditorCoreFixture.Pack;
        var l = IsoEditorCoreFixture.Canonical with
        {
            Furniture = Enumerable.Range(1, 1024).Select(k => new IsoPlacement("computer-" + k, "iso/computer", 3, 3)).ToArray(),
            WorkSeats = []
        };
        Assert.True(new IsoSceneBuilder(pack, l).EditorEmptyItemCount > 4096);
        var e = new IsoEditor(pack, l);
        Assert.Contains(e.Validate(), i => i.RuleId == "L017");
        using var temp = new IsoEditorCoreFixture.Temp(); var path = Path.Combine(temp.Root, "iso-layout.json");
        Assert.Contains(e.Save(path).Issues, i => i.RuleId == "C008"); Assert.False(File.Exists(path));
    }

    [Theory]
    [InlineData("furniture")]
    [InlineData("zones")]
    [InlineData("wallSegments")]
    [InlineData("workSeats")]
    public void StartupNullRowsFallBackAndKeepSource(string array)
    {
        using var temp = new IsoEditorCoreFixture.Temp(); var path = Path.Combine(temp.Root, "iso-layout.json");
        var node = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(IsoEditorCoreFixture.Canonical, StateJson.Options))!;
        node[array] = new System.Text.Json.Nodes.JsonArray((System.Text.Json.Nodes.JsonNode?)null);
        var original = node.ToJsonString(); File.WriteAllText(path, original);
        var result = IsoEditorStorage.Load(IsoEditorCoreFixture.Pack, new(temp.Root), new IsoEditorCoreFixture.Importer());
        Assert.False(result.UsedUserLayout); Assert.Contains(result.Issues, i => i.RuleId == "L001");
        Assert.Contains(result.Issues, i => i.RuleId == "P002"); Assert.Equal(original, File.ReadAllText(path));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StartupNullWallAssetsReturnIssuesWithoutThrowing(bool attachment)
    {
        using var temp = new IsoEditorCoreFixture.Temp(); var path = Path.Combine(temp.Root, "iso-layout.json");
        var l = IsoEditorCoreFixture.Raw("valid/two-edge-decor.json");
        var id = attachment ? "decor-a-edge-0" : "right-4";
        l = l with { WallSegments = l.WallSegments!.Select(w => w.Id == id ? w with { AssetId = null! } : w).ToArray() };
        var original = JsonSerializer.Serialize(l, StateJson.Options); File.WriteAllText(path, original);
        var result = IsoEditorStorage.Load(IsoEditorCoreFixture.Pack, new(temp.Root), new IsoEditorCoreFixture.Importer());
        Assert.False(result.UsedUserLayout); Assert.Contains(result.Issues, i => i.RuleId == "L016");
        Assert.Contains(result.Issues, i => i.RuleId == "P002"); Assert.Equal(original, File.ReadAllText(path));
    }

    [Fact]
    public void StartupByteLimitKeepsLayoutAndBackup()
    {
        using var temp = new IsoEditorCoreFixture.Temp(); var path = Path.Combine(temp.Root, "iso-layout.json");
        using (var stream = File.Create(path)) stream.SetLength(4_194_305);
        File.WriteAllText(path + ".bak", "backup-before");
        var result = IsoEditorStorage.Load(IsoEditorCoreFixture.Pack, new(temp.Root), new IsoEditorCoreFixture.Importer());
        Assert.False(result.UsedUserLayout); Assert.Contains(result.Issues, i => i.RuleId == "L017");
        Assert.Contains(result.Issues, i => i.RuleId == "P002"); Assert.Equal(4_194_305, new FileInfo(path).Length);
        Assert.Equal("backup-before", File.ReadAllText(path + ".bak"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SaveMaterializesOptionalProjectionDefaults(bool missingTile)
    {
        using var temp = new IsoEditorCoreFixture.Temp(); var path = Path.Combine(temp.Root, "iso-layout.json");
        var input = IsoEditorCoreFixture.Canonical with { Tile = missingTile ? null : new(64, 32, null), WallRise = null, WallHeight = null };
        var e = new IsoEditor(IsoEditorCoreFixture.Pack, input); Assert.Empty(e.Validate()); Assert.True(e.Save(path).Succeeded);
        using var json = JsonDocument.Parse(File.ReadAllText(path)); var root = json.RootElement;
        Assert.Equal(32, root.GetProperty("tile").GetProperty("anchor").GetProperty("x").GetInt32());
        Assert.Equal(16, root.GetProperty("tile").GetProperty("anchor").GetProperty("y").GetInt32());
        Assert.Equal(16, root.GetProperty("wallRise").GetInt32()); Assert.Equal(48, root.GetProperty("wallHeight").GetInt32());
        Assert.Null(input.Tile?.Anchor); Assert.Null(input.WallRise); Assert.Null(input.WallHeight);
    }
}

internal static class IsoEditorCoreFixture
{
    internal static readonly DateTimeOffset Epoch = DateTimeOffset.Parse("2026-10-04T00:00:00Z");
    internal static string Root => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../tests/fixtures/iso-editor"));
    internal static string PathTo(string name) => Path.Combine(Root, "layouts", name);
    internal static IsoLayout Raw(string name) => JsonSerializer.Deserialize<IsoLayout>(File.ReadAllText(PathTo(name)), StateJson.Options)!;
    internal static IsoLayout Canonical => Raw("valid/canonical-seat.json");
    private static readonly Lazy<IsoPack> Adapter = new(CreateAdapter);
    internal static IsoPack Pack => Adapter.Value;
    internal static IsoEditor Editor() => new(Pack, Canonical);
    internal static void ReplaceFurniture(IsoPack pack, IReadOnlyList<IsoFurnitureAsset> assets) => typeof(IsoPack).GetProperty(nameof(IsoPack.Manifest))!.SetValue(pack, pack.Manifest with { Furniture = assets });
    // Objects lane owns the production adapter. This test-owned variant exercises its frozen subset contract.
    private static IsoPack CreateAdapter()
    {
        var pack = IsoPack.LoadDefault(); var desk = pack.Manifest.Furniture!.First(a => a.Id == "iso/desk");
        var variants = desk.Variants.Select(v => v with
        {
            Parts = v.Parts.Where(p => (p.PartId ?? "").Contains("monitor", StringComparison.Ordinal)).ToArray(),
            BlockedCells = Array.Empty<Tile>(),
            Points = Array.Empty<IsoInteraction>(),
            SeatProfiles = null,
            HeightPx = 40,
            Shadow = null
        }).ToArray();
        ReplaceFurniture(pack, pack.Manifest.Furniture!.Append(new("iso/computer", variants, "computer")).ToArray()); return pack;
    }
    internal static void AssertRule(string rule)
    {
        using var cases = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "cases.json")));
        var casesForRule = cases.RootElement.GetProperty("cases").EnumerateArray().Where(c => c.GetProperty("kind").GetString() == "layout" && c.GetProperty("expectedRules").EnumerateArray().Any(r => r.GetString() == rule)).ToArray();
        Assert.NotEmpty(casesForRule);
        foreach (var c in casesForRule)
        {
            var layout = JsonSerializer.Deserialize<IsoLayout>(File.ReadAllText(Path.Combine(Root, c.GetProperty("path").GetString()!)), StateJson.Options)!;
            var issues = new IsoEditor(Pack, layout).Validate();
            Assert.Contains(issues, i => i.RuleId == rule && i.Severity == IsoIssueSeverity.Error);
            Assert.Equal(issues.OrderBy(i => i.RuleId, StringComparer.Ordinal).ThenBy(i => string.Join("\0", i.TargetIds), StringComparer.Ordinal).ThenBy(i => i.Message, StringComparer.Ordinal), issues);
            Assert.All(issues, i => Assert.Equal(i.TargetIds.Order(StringComparer.Ordinal).Distinct(), i.TargetIds));
        }
    }
    internal static AgentInfo Agent(string id, AgentState state = AgentState.Working) => new(id, Provider.Codex, null, false, id, null, null, null, "synthetic", null, null, state, Epoch, Epoch, false);
    internal static IsoScene Advance(IsoOffice office, AgentInfo[] agents, double seconds)
    {
        var scene = office.Update(agents, Epoch, 0);
        for (var time = 0d; time < seconds; time += .05) scene = office.Update(agents, Epoch.AddSeconds(time), Math.Min(.05, seconds - time));
        return scene;
    }
    internal sealed class Temp : IDisposable
    {
        internal readonly string Root = Path.Combine(Path.GetTempPath(), "iso-editor-" + Guid.NewGuid().ToString("N"));
        internal Temp() => Directory.CreateDirectory(Root);
        public void Dispose() { Directory.Delete(Root, true); }
    }
    internal sealed class Importer : IsoCustomObjectImporter
    {
        public IsoCustomValidationPreview ValidatePreview(string path,CancellationToken cancellation) => throw new InvalidOperationException("Not used by storage tests.");
        public IsoCustomObjectPlan PrepareImport(IsoCustomImportRequest request,CancellationToken cancellation) => throw new InvalidOperationException("Not used by storage tests.");
        public IsoCustomObjectPlan PrepareRemove(IsoCustomRemoveRequest request) => throw new InvalidOperationException("Not used by storage tests.");
        public IsoCustomObjectResult Commit(IsoCustomObjectPlan plan,CancellationToken cancellation=default) => throw new InvalidOperationException("Not used by storage tests.");
        public ImmutableArray<IsoValidationIssue> Validate(string path) => [];
        public IsoCustomObjectResult LoadCatalog(IsoEditorPaths paths) => new(true, new([]), []);
        public IsoPackMergeResult Merge(IsoPack pack, IsoCustomObjectCatalog catalog) => new(true, pack, []);
        public IsoCustomObjectResult Import(IsoCustomImportRequest request) => throw new InvalidOperationException("Not used by storage tests.");
        public IsoCustomObjectResult Remove(IsoCustomRemoveRequest request) => throw new InvalidOperationException("Not used by storage tests.");
    }
}
