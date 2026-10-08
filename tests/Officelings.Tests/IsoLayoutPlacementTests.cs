using Officelings.Core;
using Officelings.Core.Iso;
using System.Text.Json;
using Xunit;

namespace Officelings.Tests;

[Collection("Iso assets")]
public sealed class IsoLayoutPlacementTests
{
    private static readonly DateTimeOffset Epoch = DateTimeOffset.Parse("2026-10-04T00:00:00Z");
    private static IEnumerable<Tile> Footprint(IsoPlacement f, IsoPack p)
    {
        var v = p.Variant(f.AssetId, f.Rotation, f.State);
        return Enumerable.Range(0, v.FootprintW * v.FootprintH).Select(k => new Tile(f.X + k % v.FootprintW, f.Y + k / v.FootprintW));
    }
    private static AgentInfo[] Agents(int count) => Enumerable.Range(0, count).Select(i => new AgentInfo(
        $"placement:{i:00}", (Provider)(i % 3), null, false, $"Person {i}", null, null, null, "", null, null,
        AgentState.Working, Epoch, Epoch, false)).ToArray();
    private static IsoScene Advance(IsoOffice office, AgentInfo[] agents, double seconds)
    {
        var scene = office.Update(agents, Epoch, 0);
        for (var n = 1; n <= seconds * 20; n++) scene = office.Update(agents, Epoch.AddSeconds(n * .05), .05);
        return scene;
    }

    [Fact]
    public void ProductionAndFixtureHaveIdenticalPlacementsFloorsAndZones()
    {
        var pack = IsoPack.LoadDefault(); var fixture = IsoTwelveSeatFixture.Create(pack);
        Assert.Equal(34, fixture.Furniture.Count);
        Assert.Equal(fixture.Furniture.OrderBy(f => f.Id).Select(f => (f.Id, f.AssetId, f.X, f.Y, f.Rotation, f.DeskId)),
            pack.Layout.Furniture.OrderBy(f => f.Id).Select(f => (f.Id, f.AssetId, f.X, f.Y, f.Rotation, f.DeskId)));
        Assert.Equal(fixture.Tiles, pack.Layout.Tiles); Assert.Equal(fixture.Zones, pack.Layout.Zones);
        var reference = IsoLayoutLoader.Default(pack);
        Assert.Equal(new[] { (2, 2, 180), (5, 2, 270), (4, 5, 90) },
            reference.Furniture.Where(f => f.AssetId == "iso/desk").Select(f => (f.X, f.Y, f.Rotation)));
    }

    [Fact]
    public void PhysicalFurnitureAndChairsNeverOverlapAndRugStaysInLounge()
    {
        var pack = IsoPack.LoadDefault(); var layout = pack.Layout; var occupied = new HashSet<Tile>();
        foreach (var f in layout.Furniture.Where(f => f.AssetId != "iso/rug"))
            foreach (var tile in Footprint(f, pack)) Assert.True(occupied.Add(tile), $"{f.Id} overlaps at {tile}");
        var rug = Footprint(layout.Furniture.Single(f => f.AssetId == "iso/rug"), pack).ToHashSet();
        foreach (var f in layout.Furniture.Where(f => f.AssetId is "iso/desk" or "iso/chair"))
            Assert.Empty(Footprint(f, pack).Intersect(rug));
        Assert.Equal(49, occupied.Count);
    }

    [Fact]
    public void AllTwelveApproachesHaveFourWayPathsFromAndToDoorAndFreeAisleAccess()
    {
        var pack = IsoPack.LoadDefault(); var layout = pack.Layout; var nav = IsoLayoutLoader.Navigation(layout, pack);
        var desks = layout.Furniture.Where(f => f.AssetId == "iso/desk").ToArray();
        var seats = desks.Select(f => IsoContacts.Desk(f.X, f.Y, f.Rotation).Approach).ToHashSet();
        Assert.Equal(12, desks.Length); Assert.Equal(12, seats.Count);
        // Occupied chairs are excluded when checking the walking corridor alongside each seat.
        var aisles = nav.WithBlocked(seats); var reached = aisles.Reachable(layout.Door);
        foreach (var seat in seats)
        {
            Assert.True(nav.Walkable(seat)); Assert.NotEmpty(nav.FindPath(layout.Door, seat));
            Assert.NotEmpty(nav.FindPath(seat, layout.Door));
            Assert.Contains(new[] { new Tile(seat.X + 1, seat.Y), new Tile(seat.X - 1, seat.Y),
                new Tile(seat.X, seat.Y + 1), new Tile(seat.X, seat.Y - 1) }, reached.Contains);
        }
        foreach (var x in Enumerable.Range(1, 9).Where(x => !seats.Contains(new Tile(x, 3))))
            Assert.Contains(new Tile(x, 3), reached);
        foreach (var x in Enumerable.Range(0, 10).Where(x => !seats.Contains(new Tile(x, 6))))
            Assert.Contains(new Tile(x, 6), reached);
        foreach (var y in Enumerable.Range(1, 6)) Assert.Contains(new Tile(4, y), reached);
        Assert.Equal(51, reached.Count); // Every physically free tile remains connected with all chairs occupied.
    }

    [Fact]
    public void LinkedChairsFaceTheirDesksAndMatchAuthoredPelvisAndHandContacts()
    {
        var pack = IsoPack.LoadDefault(); var layout = pack.Layout;
        Assert.Equal(new[] { 0, 90, 180, 270 }, layout.Furniture.Where(f => f.AssetId == "iso/desk").Select(f => f.Rotation).Distinct().Order());
        foreach (var desk in layout.Furniture.Where(f => f.AssetId == "iso/desk"))
        {
            var seat = IsoContacts.Desk(desk.X, desk.Y, desk.Rotation);
            var chair = Assert.Single(layout.Furniture, f => f.DeskId == desk.Id);
            Assert.Equal(seat.Approach, new Tile(chair.X, chair.Y)); Assert.Equal(seat.Facing, IsoGeometry.Orientation(chair.Rotation));
            var profile = Assert.Single(pack.Variant(desk.AssetId, desk.Rotation).SeatProfiles!);
            var origin = IsoGeometry.Ground(desk.X, desk.Y);
            Assert.Equal(seat.Pelvis, new IsoPixel(origin.X + (int)profile.ContactOffset.X, origin.Y + (int)profile.ContactOffset.Y));
            Assert.Equal(new[] { seat.HandLeft, seat.HandRight }, profile.HandContacts!.Select(p => new IsoPixel(origin.X + (int)p.X, origin.Y + (int)p.Y)));
            var into = IsoGeometry.Step(seat.Facing);
            Assert.Contains(new Tile(seat.Approach.X + (int)into.I, seat.Approach.Y + (int)into.J), Footprint(desk, pack));
        }
    }

    [Theory]
    [InlineData(2, 1, 0)] // Inside a desk, despite the chair's empty blockedCells.
    [InlineData(0, 4, 0)] // Walkable but detached from its owner.
    [InlineData(2, 0, 180)] // Correct position, wrong facing.
    public void ValidationRejectsOverlappingDetachedOrBackwardChairs(int x, int y, int rotation)
    {
        var pack = IsoPack.LoadDefault(); var layout = pack.Layout;
        Assert.Throws<InvalidDataException>(() => IsoLayoutLoader.Validate(layout with {
            Furniture = layout.Furniture.Select(f => f.Id == "chair-1" ? f with {
                X = x, Y = y, Rotation = rotation, Orientation = IsoGeometry.Orientation(rotation) } : f).ToArray() }, pack));
    }

    [Fact]
    public void ValidationRejectsRugUnderAWorkstation()
    {
        var pack = IsoPack.LoadDefault(); var layout = pack.Layout;
        Assert.Throws<InvalidDataException>(() => IsoLayoutLoader.Validate(layout with {
            Furniture = layout.Furniture.Select(f => f.AssetId == "iso/rug" ? f with { X = 5, Y = 7 } : f).ToArray() }, pack));
    }

    [Fact]
    public void OverflowStandsAtUniqueFreeCellsAndTakesAVacatedSeat()
    {
        var pack = IsoPack.LoadDefault(); var office = new IsoOffice(pack, 42); var agents = Agents(15);
        var scene = Advance(office, agents, 25); var bodies = scene.Items.Where(i => i.Layer == IsoLayer.Character).ToArray();
        Assert.Equal(12, bodies.Count(i => i.Clip.StartsWith("typing/")));
        var standing = bodies.Where(i => !i.Clip.StartsWith("typing/")).ToArray(); Assert.Equal(3, standing.Length);
        var cells = standing.Select(i => IsoGeometry.Pick(i.X + 16 - office.Camera.X, i.Y + 60 - office.Camera.Y)).ToArray();
        Assert.Equal(3, cells.Distinct().Count());
        var nav = IsoLayoutLoader.Navigation(office.Layout, pack);
        var approaches = office.Layout.Furniture.Where(f => f.AssetId == "iso/desk").Select(f => IsoContacts.Desk(f.X, f.Y, f.Rotation).Approach).ToHashSet();
        Assert.All(cells, cell => { Assert.True(nav.Walkable(cell)); Assert.DoesNotContain(cell, approaches); Assert.NotEqual(office.Layout.Door, cell); });
        var departed = bodies.First(i => i.Clip.StartsWith("typing/")).HitTargetId;
        var remaining = agents.Where(a => a.Id != departed).ToArray();
        scene = Advance(office, remaining, 20);
        Assert.Equal(12, scene.Items.Count(i => i.Layer == IsoLayer.Character && i.Clip.StartsWith("typing/")));
        Assert.Equal(14, scene.Labels.Count);
        Assert.DoesNotContain(scene.Labels, l => l.AgentId == departed);
    }

    [Fact]
    public void ReversedInputHasIdenticalSceneAndAllActorsExitThroughDoor()
    {
        var pack = IsoPack.LoadDefault(); var a = new IsoOffice(pack, 42); var b = new IsoOffice(pack, 42); var agents = Agents(12);
        var first = Advance(a, agents, 25); var second = Advance(b, agents.Reverse().ToArray(), 25);
        Assert.Equal(first.Items, second.Items); Assert.Equal(first.Labels, second.Labels);
        var gone = agents.Select(p => p with { State = AgentState.Gone }).ToArray();
        var walking = Advance(a, gone, 1);
        Assert.Contains(walking.Items, i => i.Layer == IsoLayer.Character && i.Clip.StartsWith("walk/"));
        var empty = Advance(a, gone, 15);
        Assert.Empty(empty.Labels); Assert.DoesNotContain(empty.Items, i => i.Layer == IsoLayer.Character);
    }

    [Fact]
    public void AuthoredThreeAgentFixturePublishesActualDeskOwnersAndContacts()
    {
        var pack = IsoPack.LoadDefault(); var office = new IsoOffice(pack, 20261004);
        using var json = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(IsoPackTests.SourceRoot, "pack/layout.json")));
        var fixture = json.RootElement.GetProperty("acceptanceFixture").GetProperty("agents").EnumerateArray().ToArray();
        var agents = fixture.Select(a => new AgentInfo(a.GetProperty("id").GetString()!, Provider.Claude,
            a.TryGetProperty("parentId", out var parent) ? parent.GetString() : null, a.TryGetProperty("parentId", out _),
            a.GetProperty("id").GetString()!, null, null, null, "", null, null, AgentState.Working, Epoch, Epoch, false)).ToArray();
        var scene = Advance(office, agents, 20);
        foreach (var agent in fixture)
        {
            var id = agent.GetProperty("id").GetString();
            var desk = pack.Layout.Furniture.Single(f => f.Id == agent.GetProperty("deskId").GetString());
            var contact = IsoContacts.Desk(desk.X, desk.Y, desk.Rotation);
            var body = Assert.Single(scene.Items, i => i.Layer == IsoLayer.Character && i.HitTargetId == id);
            Assert.Equal("typing/" + contact.Facing, body.Clip);
            Assert.Equal(office.Camera.X + contact.Pelvis.X - 16, body.X);
            Assert.Equal(office.Camera.Y + contact.Pelvis.Y - 44, body.Y);
        }
    }
}
