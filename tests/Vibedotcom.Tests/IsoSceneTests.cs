using Vibedotcom.Core;
using Vibedotcom.Core.Iso;
using Xunit;

namespace Vibedotcom.Tests;

[Collection("Iso assets")]
public class IsoSceneTests
{
    private static IsoLayout FacingFixture(IsoPack pack, int rotation)
    {
        var layout = IsoLayoutLoader.Default(pack); var contact = IsoContacts.Desk(2, 2, rotation);
        return IsoLayoutLoader.Validate(layout with { Furniture = layout.Furniture.Select(f => f.Id switch
        {
            "desk-1" => f with { Rotation = rotation },
            "chair-1" => f with { X = contact.Approach.X, Y = contact.Approach.Y, Rotation = IsoGeometry.Rotation(contact.Facing) },
            _ => f
        }).ToArray() }, pack);
    }
    [Theory]
    [InlineData(0)] [InlineData(90)] [InlineData(180)] [InlineData(270)]
    public void AllDesignsAndTypingPhasesKeepFrozenPelvisHandsAndSeatRanks(int rotation)
    {
        var p = IsoPackTests.Source.Value; var layout = FacingFixture(p, rotation); var b = new IsoSceneBuilder(p, layout);
        var seat = IsoContacts.Desk(2, 2, rotation);
        foreach (var design in p.Characters) for (var phase = 0; phase < 4; phase++)
        {
            var actor = new IsoActorPose("actor", design.Id, "name", new(seat.Approach.X, seat.Approach.Y), seat.Facing,
                "typing", phase / 6d, "desk-1", "seated");
            var scene = b.Build([actor]); var body = Assert.Single(scene.Items.Where(i => i.Layer == IsoLayer.Character));
            Assert.Equal(phase, body.Frame);
            Assert.Equal(b.Camera.X + seat.Pelvis.X - 16, body.X); Assert.Equal(b.Camera.Y + seat.Pelvis.Y - 44, body.Y);
            var c = p.Clip(body.SheetId, body.Clip); Assert.Equal(2, c.HandAnchors!.Count);
            Assert.Equal(b.Camera.X + seat.HandLeft.X, body.X + c.HandAnchors[0].X);
            Assert.Equal(b.Camera.Y + seat.HandLeft.Y, body.Y + c.HandAnchors[0].Y);
            Assert.Equal(b.Camera.X + seat.HandRight.X, body.X + c.HandAnchors[1].X);
            Assert.Contains(scene.Items, i => i.InstanceId.StartsWith("chair-1/") && i.Depth < body.Depth);
            Assert.Contains(scene.Items, i => i.InstanceId.StartsWith("chair-1/") && i.Depth > body.Depth);
            var desk = p.Variant("iso/desk", rotation);
            foreach (var part in desk.Parts.Where(part => part.SeatGroupId != null))
            {
                var item = scene.Items.Single(i => i.InstanceId == "desk-1/" + part.PartId);
                Assert.Equal(part.IsoZOrder < 0, item.Depth < body.Depth);
            }
            Assert.DoesNotContain(scene.Items, i => i.InstanceId == "actor/contact-shadow");
        }
    }
    [Theory]
    [InlineData(0)] [InlineData(90)] [InlineData(180)] [InlineData(270)]
    public void WalkersHaveTheirOwnDepthOnBothSidesAndEnds(int rotation)
    {
        var p = IsoPackTests.Source.Value; var layout = FacingFixture(p, rotation); var b = new IsoSceneBuilder(p, layout);
        foreach (var family in new[] { "iso/desk", "iso/sofa", "iso/tv-cabinet", "iso/shelf" })
        {
            var f = layout.Furniture.First(f => f.AssetId == family); var v = p.Variant(f.AssetId, f.Rotation);
            foreach (var position in new[] { new Position(f.X - 1, f.Y), new Position(f.X, f.Y - 1),
                new Position(f.X + v.FootprintW, f.Y), new Position(f.X, f.Y + v.FootprintH) })
            {
                var scene = b.Build([new("walker", p.Characters[0].Id, "Walker", position, "se", "walk", 0)]);
                var body = Assert.Single(scene.Items.Where(i => i.Layer == IsoLayer.Character));
                Assert.Equal(new IsoDepthKey(IsoPass.Scene, position.X + position.Y, 0, "walker", "body").Encoded, body.Depth);
                Assert.Contains(scene.Items, i => i.InstanceId == "walker/contact-shadow");
                Assert.Empty(Vibedotcom.DepthChecks.DepthOracle.WalkerViolations(p, layout, scene, "walker", position));
            }
        }
    }
    [Fact]
    public void EveryAuthoredAliasEffectUsesExistingSharedUiInEveryFacing()
    {
        var pack = IsoPackTests.Source.Value; var builder = new IsoSceneBuilder(pack, pack.Layout);
        var effects = new HashSet<string>();
        foreach (var design in pack.Characters)
        foreach (var alias in pack.Sheet(design.SheetId).Aliases!.Where(a => a.Effect != null))
        {
            var clip = pack.Clip(design.SheetId, alias.Name, alias.Direction); effects.Add(clip.Effect!);
            var scene = builder.Build([new("actor", design.Id, "Name", new(3, 4), alias.Direction, alias.Name, 0)]);
            var effect = Assert.Single(scene.Items.Where(i => i.InstanceId == "actor/effect"));
            Assert.Equal(IsoLayer.Overlay, effect.Layer);
            Assert.StartsWith("iso/shared-", effect.SheetId);
            Assert.Contains(Enumerable.Range(0, pack.Sheet(effect.SheetId).CellH), y =>
                Enumerable.Range(0, pack.Sheet(effect.SheetId).CellW).Any(x => pack.Opaque(effect, x, y)));
        }
        Assert.Equal(6, effects.Count);
        Assert.DoesNotContain(pack.Manifest.Sheets, s => s.Kind == "character" && s.Projection == "any");
    }
    [Fact]
    public void ReverseAlphaPickingMatchesOpaqueOccludersAndIgnoresSpritePadding()
    {
        var p = IsoPackTests.Source.Value; var b = new IsoSceneBuilder(p, IsoLayoutLoader.Default(p));
        var seat = IsoContacts.Desk(2, 2, 180);
        var scene = b.Build([new("actor", p.Characters[0].Id, "Name", new(seat.Approach.X, seat.Approach.Y), "se", "typing", 0, "desk-1", "seated")]);
        var actorHits = 0; var deskHits = 0;
        var body = scene.Items.Single(i => i.Layer == IsoLayer.Character);
        for (var y = body.Y; y < body.Y + 64; y++) for (var x = body.X; x < body.X + 32; x++)
        {
            var top = scene.Items.Reverse().FirstOrDefault(i => i.Layer is not (IsoLayer.Floor or IsoLayer.Shadow) && p.Opaque(i, x - i.X, y - i.Y));
            var hit = b.HitTest(x, y);
            Assert.Equal(top?.HitTargetId, hit?.TargetId);
            if (hit?.TargetId == "actor") actorHits++;
            if (hit?.TargetId == "desk-1") deskHits++;
        }
        Assert.True(actorHits > 0); Assert.True(deskHits > 0);
        Assert.Null(b.HitTest(0, 0));
        Assert.Equal(new Tile(0, 0), b.PickTile(b.Camera.X, b.Camera.Y));
        Assert.Null(b.PickTile(0, 0));
    }
    [Fact]
    public void TinyFadeAlphaCannotBecomeAnActorHit()
    {
        var p = IsoPackTests.Source.Value; var b = new IsoSceneBuilder(p, IsoLayoutLoader.Default(p));
        var scene = b.Build([new("faded", p.Characters[0].Id, "Faded", new(3, 4), "se", "idle", 0, Alpha: 31)]);
        var body = scene.Items.Single(i => i.Layer == IsoLayer.Character);
        for (var y = 0; y < 64; y++) for (var x = 0; x < 32; x++) Assert.NotEqual("faded", b.HitTest(body.X + x, body.Y + y)?.TargetId);
    }
    [Fact]
    public void FloorTransitionsFollowAllSixteenCardinalNeighborMasks()
    {
        var pack = IsoPackTests.Source.Value;
        Tile[] directions = [new(1, 0), new(0, 1), new(-1, 0), new(0, -1)];
        for (var mask = 0; mask < 16; mask++)
        {
            var tiles = Enumerable.Repeat(1, 100).ToArray();
            for (var k = 0; k < 4; k++) if ((mask & (1 << k)) != 0) tiles[(3 + directions[k].Y) * 10 + 3 + directions[k].X] = 2;
            var layout = IsoLayoutLoader.Default(pack) with { Tiles = tiles }; var builder = new IsoSceneBuilder(pack, layout);
            var scene = builder.Build([]); var parts = scene.Items.Where(i => i.InstanceId.StartsWith("tile-3-3/")).ToArray();
            var edges = Enumerable.Range(0, 4).Count(k => (mask & (1 << k)) != 0);
            var corners = Enumerable.Range(0, 4).Count(k => (mask & (1 << k)) != 0 && (mask & (1 << ((k + 1) % 4))) != 0);
            Assert.Equal(1 + edges + corners, parts.Length);
            Assert.Equal(corners, parts.Count(i => i.Clip.Contains("corner")));
        }
    }
    [Fact]
    public void DeskEditMovesRotatesLinkedChairTogetherAndPreservesOriginalForUndo()
    {
        var pack = IsoPackTests.Source.Value; var source = IsoLayoutLoader.Default(pack);
        var edited = IsoPlacementEditor.MoveRotate(source, pack, "desk-1", 2, 4, 90);
        var desk = edited.Furniture.Single(f => f.Id == "desk-1"); var chair = edited.Furniture.Single(f => f.Id == "chair-1");
        Assert.Equal((2, 4, 90), (desk.X, desk.Y, desk.Rotation));
        Assert.Equal((3, 5, 270), (chair.X, chair.Y, chair.Rotation));
        Assert.Equal((2, 2, 180), (source.Furniture[0].X, source.Furniture[0].Y, source.Furniture[0].Rotation));
        Assert.Equal(source.Furniture.Select(f => f.Id), edited.Furniture.Select(f => f.Id));
    }
    [Fact]
    public void LayoutRejectsOverlapUnreachableDoorAndEndProfileBeforePlayback()
    {
        var p = IsoPackTests.Source.Value; var layout = IsoLayoutLoader.Default(p);
        Assert.Throws<InvalidDataException>(() => IsoLayoutLoader.Validate(layout with { Door = new(2, 2) }, p));
        Assert.Throws<InvalidDataException>(() => IsoLayoutLoader.Validate(layout with { Furniture = layout.Furniture.Append(new("overlap", "iso/plant", 2, 2)).ToArray() }, p));
        Assert.Throws<InvalidDataException>(() => IsoLayoutLoader.Validate(layout with { Furniture = layout.Furniture.Select(f => f.Id == "desk-1" ? f with { SeatProfile = "end" } : f).ToArray() }, p));
    }
}
