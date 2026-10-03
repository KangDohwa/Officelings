using System.Text.Json;
using Vibedotcom.Core;
using Xunit;

namespace Vibedotcom.Tests;

public sealed class OfficeAssignmentTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-03T12:00:00Z");
    private static AgentInfo Agent(string id, AgentState state = AgentState.Working, string? parent = null) =>
        new(id, Provider.Codex, parent, parent != null, id, null, null, null, "", null, null, state, Now, Now, false);

    private static AgentInfo[] SeatedAgents(AssetPack pack, int count)
    {
        var agents = new List<AgentInfo>();
        var designs = new List<string>();
        for (var i = 0; i < 2048 && agents.Count < count; i++)
        {
            var agent = Agent("seated:" + i.ToString("D4"));
            var design = pack.Assign(agent, designs);
            if (new[] { "up", "left", "right" }.Any(facing => pack.SeatedVariant(design, facing) == null)) continue;
            agents.Add(agent); designs.Add(design.Id);
        }
        Assert.Equal(count, agents.Count);
        return agents.ToArray();
    }

    private static IReadOnlyList<AgentView> SettleAtWork(OfficeAnimator office, AgentInfo[] agents)
    {
        IReadOnlyList<AgentView> views = [];
        for (var step = 0; step < 400; step++)
        {
            views = office.Update(agents, Now.AddSeconds(step * .1), .1);
            if (views.All(v => v.Pose.Queued ? v.Pose.Action == "waiting" : v.Pose.SeatPhase == "seated")) return views;
        }
        Assert.All(views.Where(v => !v.Pose.Queued), v => Assert.Equal("seated", v.Pose.SeatPhase));
        return views;
    }

    private static OfficeLayout Room(int desks = 3, int overflow = 2)
    {
        var furniture = Enumerable.Range(0, desks).Select(i => new Furniture("desk", 3 + i % 3 * 5, 5 + i / 3 * 4, 3, 2,
            Id: "desk-" + i, DeskId: "desk-" + i)).Concat([
            new Furniture("sofa", 18, 10, 4, 1, Id: "sofa"),
            new Furniture("pantry", 17, 4, 6, 2, Id: "pantry")]).ToArray();
        return new(24, 18, 16, new(1, 1), furniture, Tiles: Enumerable.Repeat(1, 24 * 18).ToArray(), Zones: [
            new("workspace", 1, 3, 15, 14), new("pantry", 17, 3, 6, 5),
            new("lounge", 17, 8, 6, 9, [new("phone", 2, 3, "phone", "down")]), new("door", 1, 1, 1, 1)],
            OverflowSeats: Enumerable.Range(0, overflow).Select(i => new OverflowSeat("extra-" + i, 19 + i, 11, "up", "sofa")).ToArray());
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void DefaultFurnitureFloorAndLinkedChairsAreFixedAcrossMembership(bool art)
    {
        var pack = art ? AssetPack.LoadDefault(useUserPack: false) : new AssetPack();
        var office = new OfficeAnimator(42, pack);
        var original = office.SourceLayout;
        Assert.Null(original.AutoDesks);
        var geometry = original.Furniture.Select(f => (f.Id, f.DeskId, f.X, f.Y, f.Width, f.Height)).ToArray();
        foreach (var count in new[] { 0, 6, 30, 1, 0 })
        {
            var agents = Enumerable.Range(0, count).Select(i => Agent("fixed:" + i)).ToArray();
            office.Update(agents, Now, 0);
            Assert.Equal((original.WidthTiles, original.HeightTiles, original.Door), (office.Layout.WidthTiles, office.Layout.HeightTiles, office.Layout.Door));
            Assert.Equal(geometry, office.Layout.Furniture.Select(f => (f.Id, f.DeskId, f.X, f.Y, f.Width, f.Height)).ToArray());
            Assert.Equal(original.Tiles!.ToArray(), office.Layout.Tiles!.ToArray());
            var desks = office.Layout.Furniture.Where(f => f.Kind == "desk").ToArray();
            Assert.Equal(12, desks.Length);
            Assert.Equal(12, desks.Select(f => f.DeskId).Distinct().Count());
            Assert.All(desks, f => {
                var variant = pack.Variant(f);
                var layers = new[] { variant.ChairBackId, variant.ChairFrontId }.OfType<string>().ToArray();
                if (layers.Length == 0) Assert.Single(office.Layout.Furniture, c => c.Kind == "chair" && c.Id == f.Id + "-chair");
                else Assert.All(layers, asset => Assert.Single(office.Layout.Furniture,
                    c => c.Kind == "chair" && c.DeskId == f.DeskId && c.AssetId == asset));
            });
            LayoutLoader.Validate(office.Layout, pack);
        }
        Assert.All(office.SourceLayout.Furniture, f => Assert.Null(f.AgentId));
    }

    [Fact]
    public void DefaultOfficeKeepsEightBackSeatsAndAFourSeatPodFacingEachOtherWithReachableWorkplaces()
    {
        var pack = AssetPack.LoadDefault(useUserPack: false);
        var office = new OfficeAnimator(42, pack);
        var layout = office.SourceLayout;
        Assert.Equal((35, 20), (layout.WidthTiles, layout.HeightTiles));
        Assert.Null(layout.AutoDesks);
        var workplaces = layout.Furniture.Where(f => f.Kind == "desk").Select(f =>
            (Furniture: f, Point: Assert.Single(pack.Variant(f).Points, p => p.Action == "typing"))).ToArray();
        Assert.Equal(12, workplaces.Length);
        Assert.Equal(8, workplaces.Count(p => p.Point.Facing == "up"));
        var right = workplaces.Where(p => p.Point.Facing == "right").OrderBy(p => p.Furniture.Y + p.Point.Y).ToArray();
        var left = workplaces.Where(p => p.Point.Facing == "left").OrderBy(p => p.Furniture.Y + p.Point.Y).ToArray();
        Assert.Equal(2, right.Length); Assert.Equal(2, left.Length);
        for (var i = 0; i < 2; i++)
        {
            Assert.Equal(90, right[i].Furniture.Rotation);
            Assert.Equal(270, left[i].Furniture.Rotation);
            Assert.Equal(right[i].Furniture.Y + right[i].Point.Y, left[i].Furniture.Y + left[i].Point.Y);
            Assert.True(right[i].Furniture.X + right[i].Point.X < left[i].Furniture.X + left[i].Point.X);
        }
        var blocked = LayoutLoader.Blocked(layout, pack);
        foreach (var tile in pack.Points(layout).Select(p => p.Tile).Concat(layout.OverflowSeats!.Select(s => new Tile(s.X, s.Y))))
        {
            Assert.True(LayoutLoader.Walkable(layout, tile, blocked));
            if (tile != layout.Door) Assert.NotEmpty(OfficeAnimator.FindPath(layout, layout.Door, tile, blocked));
        }
        var views = SettleAtWork(office, SeatedAgents(pack, 12));
        Assert.Equal(12, views.Select(v => v.Pose.DeskId).Distinct().Count());
        Assert.All(views, view => {
            var desk = Assert.Single(office.Layout.Furniture, f => f.Kind == "desk" && f.DeskId == view.Pose.DeskId);
            var variant = pack.Variant(desk);
            var point = Assert.Single(variant.Points, p => p.Action == "typing");
            Assert.Equal(new Tile(desk.X + point.X, desk.Y + point.Y), view.Pose.Desk);
            Assert.Equal(new Position(view.Pose.Desk.X, view.Pose.Desk.Y), new Position(view.Pose.X, view.Pose.Y));
            Assert.Equal(point.Facing, view.Pose.Facing);
            Assert.Equal("typing", view.Pose.Action);
            Assert.Equal("desk", view.Pose.WorkLocation);
            Assert.Equal(point.Facing == "up" ? "seated_back" : "seated_" + point.Facing, view.Pose.SeatedVariant);
            Assert.Equal("seated", view.Pose.SeatPhase);
            Assert.Equal(variant.ChairBackId, view.Pose.ChairBackId);
            Assert.Equal(variant.ChairFrontId, view.Pose.ChairFrontId);
            if (variant.ChairSeatAnchor is {} anchor)
                Assert.Equal(new PixelPoint(desk.X * 16 + anchor.X, desk.Y * 16 + anchor.Y), view.Pose.SeatAnchor);
        });
    }

    [Fact]
    public void DefaultOverflowClaimsExistingSeatsAndQueuesAfterTheFixedCapacity()
    {
        var pack = AssetPack.LoadDefault(useUserPack: false);
        var office = new OfficeAnimator(42, pack);
        var original = JsonSerializer.Serialize(office.SourceLayout, StateJson.Options);
        var overflow = office.SourceLayout.OverflowSeats!;
        var views = SettleAtWork(office, SeatedAgents(pack, 12 + overflow.Count + 3));
        Assert.Equal(12, views.Count(v => v.Pose.WorkLocation == "desk"));
        Assert.Equal(overflow.Count, views.Count(v => v.Pose.WorkLocation == "overflow"));
        Assert.Equal(3, views.Count(v => v.Pose.Queued));
        Assert.All(views.Where(v => v.Pose.WorkLocation == "overflow"), view => {
            var seat = Assert.Single(overflow, s => s.Id == view.Pose.OverflowSeatId);
            Assert.Null(view.Pose.DeskId);
            Assert.Equal(new Tile(seat.X, seat.Y), view.Pose.Desk);
            Assert.Equal(seat.Facing, view.Pose.Facing);
            Assert.Equal("typing", view.Pose.Action);
            Assert.Equal("seated", view.Pose.SeatPhase);
        });
        Assert.All(views.Where(v => v.Pose.Queued), view => {
            Assert.Equal("waiting", view.Pose.Action);
            Assert.Equal("…", view.Pose.Reaction);
            Assert.Null(view.Pose.DeskId); Assert.Null(view.Pose.OverflowSeatId); Assert.Null(view.Pose.SeatPhase);
        });
        var laptop = Assert.Single(office.Layout.Furniture, f => f.AssetId == "cozy-laptop-table-side");
        Assert.Equal("table", laptop.Kind);
        Assert.Null(laptop.DeskId);
        var variant = pack.Variant(laptop);
        Assert.Empty(variant.Points);
        var laptopSeat = Assert.Single(overflow, s => s.FurnitureId == laptop.Id);
        Assert.Equal((laptop.X - 1, laptop.Y + 1, "right"), (laptopSeat.X, laptopSeat.Y, laptopSeat.Facing));
        var laptopOwner = Assert.Single(views, v => v.Pose.OverflowSeatId == laptopSeat.Id);
        Assert.Equal(variant.ChairBackId, laptopOwner.Pose.ChairBackId);
        Assert.Equal(variant.ChairFrontId, laptopOwner.Pose.ChairFrontId);
        Assert.Equal(new PixelPoint(laptop.X * 16 + variant.ChairSeatAnchor!.X, laptop.Y * 16 + variant.ChairSeatAnchor.Y), laptopOwner.Pose.SeatAnchor);
        Assert.Equal(original, JsonSerializer.Serialize(office.SourceLayout, StateJson.Options));
    }

    [Fact]
    public void SurvivorsKeepDesksAndReturningSessionReusesHistoryOnlyWhenFree()
    {
        var office = new OfficeAnimator(42, layout: Room(3, 0));
        AgentInfo[] agents = [Agent("a"), Agent("b"), Agent("c")];
        var first = office.Update(agents, Now, 0).ToDictionary(v => v.Agent.Id);
        var survivors = office.Update([agents[2], agents[0]], Now.AddSeconds(1), 0);
        Assert.All(survivors, v => {
            Assert.Equal(first[v.Agent.Id].Pose.DeskId, v.Pose.DeskId);
            Assert.Equal(first[v.Agent.Id].Pose.CharacterId, v.Pose.CharacterId);
        });
        var returned = office.Update(agents.Reverse().ToArray(), Now.AddSeconds(2), 0);
        Assert.Equal(first["b"].Pose.DeskId, returned.Single(v => v.Agent.Id == "b").Pose.DeskId);
        office.Update([agents[0], agents[2]], Now.AddSeconds(3), 0);
        var replacement = Agent("d");
        var taken = office.Update([agents[0], agents[2], replacement], Now.AddSeconds(4), 0);
        var occupied = taken.Single(v => v.Agent.Id == "d").Pose.DeskId;
        var busyReturn = office.Update([agents[1], replacement, agents[2], agents[0]], Now.AddSeconds(5), 0);
        Assert.Equal(occupied, busyReturn.Single(v => v.Agent.Id == "d").Pose.DeskId);
        Assert.True(busyReturn.Single(v => v.Agent.Id == "b").Pose.Queued);
        Assert.Equal(3, office.Layout.Furniture.Count(f => f.AgentId != null));
    }

    [Fact]
    public void GoneReleasesCapacityAndQueuedAgentMovesInWithoutMovingOverflowOwner()
    {
        var office = new OfficeAnimator(42, layout: Room(1, 1));
        AgentInfo[] agents = [Agent("a"), Agent("b"), Agent("c")];
        var first = office.Update(agents, Now, 0);
        var extra = first[1].Pose.OverflowSeatId;
        var next = office.Update([agents[0] with { State = AgentState.Gone }, agents[1], agents[2]], Now.AddSeconds(1), .1);
        Assert.Equal(first[0].Pose.DeskId, next[2].Pose.DeskId);
        Assert.False(next[2].Pose.Queued);
        Assert.Equal(extra, next[1].Pose.OverflowSeatId);
        Assert.Null(next[0].Pose.DeskId);
        var resumed = office.Update(agents, Now.AddSeconds(2), 0);
        Assert.True(resumed[0].Pose.Queued);
        Assert.Equal(next[2].Pose.DeskId, resumed[2].Pose.DeskId);
    }

    [Fact]
    public void ChildIsAssignedNearestFreeDeskAfterParentEvenWhenItsIdSortsFirst()
    {
        var office = new OfficeAnimator(42, layout: Room(3, 0));
        var parent = Agent("z-parent");
        var first = office.Update([Agent("first"), Agent("second"), parent], Now, 0);
        var parentDesk = first.Single(v => v.Agent.Id == parent.Id).Pose;
        var free = first.Where(v => v.Agent.Id != parent.Id).Select(v => v.Pose.Desk).ToArray();
        var child = Agent("a-child", parent: parent.Id);
        var views = office.Update([child, parent], Now.AddSeconds(1), 0);
        Assert.Equal(parentDesk.DeskId, views[1].Pose.DeskId);
        int Distance(Tile a) => Math.Abs(a.X - parentDesk.Desk.X) + Math.Abs(a.Y - parentDesk.Desk.Y);
        Assert.Equal(free.Min(Distance), Distance(views[0].Pose.Desk));
        office = new(42, layout: Room(3, 0));
        views = office.Update([child, parent], Now, 0);
        Assert.Equal("desk-0", views[1].Pose.DeskId);
        Assert.Equal("desk-1", views[0].Pose.DeskId);
    }

    [Fact]
    public void OverflowUsesLaptopLocationThenReachableDoorQueueAndEllipsis()
    {
        var pack = new AssetPack(); var office = new OfficeAnimator(42, pack, Room(2, 2));
        var agents = Enumerable.Range(0, 10).Select(i => Agent("agent:" + i.ToString("00"))).ToArray();
        var views = office.Update(agents, Now, 0);
        Assert.Equal(2, views.Count(v => v.Pose.WorkLocation == "desk"));
        Assert.Equal(2, views.Count(v => v.Pose.WorkLocation == "overflow"));
        Assert.Equal(6, views.Count(v => v.Pose.Queued));
        var blocked = LayoutLoader.Blocked(office.Layout, pack);
        Assert.All(views, v => Assert.NotEmpty(OfficeAnimator.FindPath(office.Layout, office.Layout.Door, v.Pose.Desk, blocked)));
        for (var i = 0; i < 150; i++) views = office.Update(agents, Now.AddSeconds(i * .1), .1);
        Assert.All(views.Where(v => v.Pose.WorkLocation == "overflow"), v => {
            Assert.NotNull(v.Pose.OverflowSeatId); Assert.Null(v.Pose.DeskId); Assert.Equal("typing", v.Pose.Action);
        });
        Assert.All(views.Where(v => v.Pose.Queued), v => {
            Assert.Equal("queue", v.Pose.WorkLocation); Assert.Null(v.Pose.DeskId); Assert.Null(v.Pose.OverflowSeatId);
            Assert.Equal("waiting", v.Pose.Action); Assert.Equal("…", v.Pose.Reaction);
            Assert.InRange(Math.Abs(v.Pose.Desk.X - office.Layout.Door.X) + Math.Abs(v.Pose.Desk.Y - office.Layout.Door.Y), 1, 4);
        });
    }

    [Fact]
    public void IdleActivitiesAndFallbackPhoneNeverReserveOccupiedOverflowSeats()
    {
        var office = new OfficeAnimator(42, layout: Room(2, 2));
        AgentInfo[] agents = [Agent("a", AgentState.Idle), Agent("b", AgentState.Idle), Agent("c"), Agent("d")];
        var views = office.Update(agents, Now, 0);
        var reserved = views.Where(v => v.Pose.WorkLocation == "overflow").Select(v => new Position(v.Pose.Desk.X, v.Pose.Desk.Y)).ToArray();
        var observedLeisure = false;
        for (var i = 0; i < 400; i++)
        {
            views = office.Update(agents, Now.AddSeconds(i * .1), .1);
            foreach (var v in views.Where(v => v.Agent.State == AgentState.Idle && v.Pose.Action is "coffee" or "phone" or "napping"))
            { observedLeisure = true; Assert.DoesNotContain(new Position(v.Pose.X, v.Pose.Y), reserved); }
        }
        Assert.True(observedLeisure);
    }

    [Fact]
    public void ReportingFromOverflowToOverflowOrMissingParentNeedsNoDeskFurniture()
    {
        var office = new OfficeAnimator(42, layout: Room(0, 2));
        var parent = Agent("parent", AgentState.Waiting);
        var child = Agent("child", AgentState.Reporting, parent.Id);
        office.Update([parent, child], Now, 0);
        child = child with { State = AgentState.Idle };
        var delivered = false;
        IReadOnlyList<AgentView> views = [];
        for (var i = 0; i < 220; i++)
        {
            views = office.Update([parent, child], Now.AddSeconds(i * .1), .1);
            delivered |= views[0].Pose.Reaction == "!";
            Assert.All(views, v => Assert.False(v.Pose.ReportOnDesk));
        }
        Assert.True(delivered);
        Assert.DoesNotContain(views, v => v.Pose.Action == "reporting");
        office = new(42, layout: Room(0, 1)); child = Agent("orphan", AgentState.Reporting, "missing");
        office.Update([child], Now, 0);
        for (var i = 0; i < 220; i++) views = office.Update([child with { State = AgentState.Idle }], Now.AddSeconds(i * .1), .1);
        Assert.NotEqual("reporting", Assert.Single(views).Pose.Action);
    }

    [Fact]
    public void IdleTripYieldsWhenItsDestinationBecomesReservedForOverflowWork()
    {
        var room = Room(1, 1);
        room = room with { Furniture = room.Furniture.Where(f => f.Kind != "pantry").ToArray() };
        var office = new OfficeAnimator(42, layout: room); var idle = Agent("a", AgentState.Idle);
        var time = Now; AgentView view = null!;
        for (var i = 0; i < 100; i++)
        {
            time = Now.AddSeconds(i * .1); view = Assert.Single(office.Update([idle], time, .1));
            if (view.Pose.Action == "walking") break;
        }
        Assert.Equal("walking", view.Pose.Action);
        var worker = Agent("b"); var extra = room.OverflowSeats![0]; var observedLeisure = false;
        for (var i = 1; i < 160; i++)
        {
            view = office.Update([idle, worker], time.AddSeconds(i * .1), .1)[0];
            if (view.Pose.Action is "phone" or "napping")
            { observedLeisure = true; Assert.NotEqual(new Position(extra.X, extra.Y), new Position(view.Pose.X, view.Pose.Y)); }
        }
        Assert.True(observedLeisure);
    }

    [Fact]
    public void ReloadKeepsLivePositionIdentityAndDeskHistoryAcrossReorderAndSeatEdits()
    {
        var office = new OfficeAnimator(42, layout: Room(2, 0));
        AgentInfo[] agents = [Agent("a"), Agent("b")];
        IReadOnlyList<AgentView> before = [];
        for (var i = 0; i < 80; i++) before = office.Update(agents, Now.AddSeconds(i * .1), .1);
        var edited = office.SourceLayout with { Furniture = office.SourceLayout.Furniture.Reverse()
            .Select(f => f.DeskId == before[0].Pose.DeskId ? f with { Seat = new("seat", 3, 0, "typing", "left") } : f).ToArray() };
        office.ReloadLayout(edited);
        var after = office.Update(agents.Reverse().ToArray(), Now.AddSeconds(8), 0);
        Assert.All(after, v => {
            var old = before.Single(b => b.Agent.Id == v.Agent.Id).Pose;
            Assert.Equal(old.CharacterId, v.Pose.CharacterId); Assert.Equal(old.DeskId, v.Pose.DeskId);
            Assert.Equal(new Position(old.X, old.Y), new Position(v.Pose.X, v.Pose.Y));
        });
        office.Update([agents[0]], Now.AddSeconds(9), 0);
        office.ReloadLayout(office.SourceLayout);
        var returned = office.Update(agents, Now.AddSeconds(10), 0);
        Assert.Equal(before[1].Pose.DeskId, returned[1].Pose.DeskId);
    }

    [Fact]
    public void PreviewToleratesOverlapsAndBlockedSeatWhileStrictValidationPreservesLastLayout()
    {
        var pack = new AssetPack(); var office = new OfficeAnimator(42, pack, Room(2, 0));
        AgentInfo[] agents = [Agent("a"), Agent("b")];
        var before = office.Update(agents, Now, 0);
        var invalid = office.SourceLayout with { Furniture = office.SourceLayout.Furniture.Select(f =>
            f.Id == "desk-1" ? f with { X = 3 } : f.Id == "desk-0" ? f with { Seat = new("seat", 1, 0, "typing") } : f).ToArray() };
        var last = office.Layout;
        Assert.Throws<InvalidDataException>(() => office.ReloadLayout(invalid, validate: true));
        Assert.Same(last, office.Layout);
        office.ReloadLayout(invalid);
        var preview = office.Update(agents, Now.AddSeconds(1), 0);
        Assert.All(preview, v => Assert.Equal(before.Single(b => b.Agent.Id == v.Agent.Id).Pose.DeskId, v.Pose.DeskId));
        var blocked = LayoutLoader.Blocked(office.Layout, pack);
        Assert.All(preview, v => Assert.True(LayoutLoader.Walkable(office.Layout, v.Pose.Desk, blocked)));
        Assert.Throws<InvalidDataException>(() => LayoutLoader.Validate(office.SourceLayout, pack));
    }

    [Fact]
    public void SeatOverrideChangesWorkPointAndFacingWithoutChangingBlockedCells()
    {
        var pack = new AssetPack(); var room = Room(1, 0); var desk = room.Furniture[0];
        var edited = desk with { Seat = new("edited-seat", 3, 0, "typing", "right") };
        Assert.Single(pack.Variant(edited).Points, p => p.Action == "typing");
        Assert.Equal(LayoutLoader.Cells(desk, pack.Variant(desk)), LayoutLoader.Cells(edited, pack.Variant(edited)));
        room = room with { Furniture = room.Furniture.Select(f => f.Id == desk.Id ? edited : f).ToArray() };
        LayoutLoader.Validate(room, pack);
        var office = new OfficeAnimator(42, pack, room); var agent = Agent("seat"); AgentView view = null!;
        for (var i = 0; i < 80; i++) view = Assert.Single(office.Update([agent], Now.AddSeconds(i * .1), .1));
        Assert.Equal(new Tile(6, 5), view.Pose.Desk); Assert.Equal("right", view.Pose.Facing);
        Assert.Equal("typing", view.Pose.Action);
        Assert.Throws<InvalidDataException>(() => LayoutLoader.Validate(room with { OverflowSeats = [new("duplicate", 6, 5, "right")] }, pack));
        Assert.Throws<InvalidDataException>(() => LayoutLoader.Validate(room with { OverflowSeats = [new("blocked", 3, 5, "up")] }, pack));
        Assert.Throws<InvalidDataException>(() => LayoutLoader.Validate(room with { OverflowSeats = [new("unknown", 19, 11, "up", "missing")] }, pack));
    }

    [Fact]
    public void LegacyAutomaticGridMigratesOnceAtLoadWithExplicitCapacityAndPreservesSource()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Vibedotcom-layout-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var pack = new AssetPack(); var legacy = Room(0, 2) with {
                AutoDesks = new("desk", 2, 4, 5, 4, 2, Capacity: 4) };
            var file = Path.Combine(directory, "layout.json");
            var json = JsonSerializer.Serialize(legacy, StateJson.Options); File.WriteAllText(file, json);
            var warnings = new List<string>(); var loaded = LayoutLoader.Load(pack, file, warnings);
            Assert.Empty(warnings); Assert.Null(loaded.AutoDesks); Assert.Equal(json, File.ReadAllText(file));
            Assert.Equal(4, loaded.Furniture.Count(f => f.Kind == "desk"));
            Assert.All(loaded.Furniture.Where(f => f.Kind == "desk"), f => Assert.Single(loaded.Furniture, c => c.Id == f.Id + "-chair"));
            var agents = Enumerable.Range(0, 40).Select(i => (Id: "legacy:" + i, Slot: i)).ToArray();
            var adapted = LayoutLoader.Adapt(loaded, pack, agents);
            Assert.Equal((loaded.WidthTiles, loaded.HeightTiles, loaded.Furniture.Count), (adapted.WidthTiles, adapted.HeightTiles, adapted.Furniture.Count));
            Assert.Equal(4, adapted.Furniture.Count(f => f.AgentId != null));
            var office = new OfficeAnimator(42, pack, loaded);
            var views = office.Update(agents.Select(a => Agent(a.Id)).ToArray(), Now, 0);
            Assert.Equal(4, views.Count(v => v.Pose.WorkLocation == "desk"));
            Assert.Equal(34, views.Count(v => v.Pose.Queued));
            office.Update([], Now.AddSeconds(1), 0);
            Assert.Equal(4, office.Layout.Furniture.Count(f => f.Kind == "desk"));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData("Google AI Pro", "reviewer", null, Provider.Antigravity, false, "reviewer")]
    [InlineData("Google AI Pro · Workspace", "reviewer", null, Provider.Antigravity, false, "reviewer")]
    [InlineData("Google AI Pro (personal)", null, "google/gemini-3-pro", Provider.Antigravity, false, "gemini-3-pro")]
    [InlineData("Ultra", null, "google/gemini-3-pro", Provider.Antigravity, false, "gemini-3-pro")]
    [InlineData("Claude Max", null, null, Provider.Claude, false, "Claude")]
    [InlineData("ChatGPT Pro", "worker", null, Provider.Codex, true, "Codex worker")]
    [InlineData("Night Owl", "worker", "gpt", Provider.Codex, true, "Night Owl")]
    [InlineData("abcdefghijklmnopqrst", null, null, Provider.Codex, false, "abcdefghijklmn…")]
    [InlineData(" ", null, null, Provider.Antigravity, false, "Antigravity")]
    [InlineData("Google AI Ultra", "Ultra", "Pro", Provider.Antigravity, false, "Antigravity")]
    public void DisplayNameRejectsPlanTitlesAndKeepsOriginalNameForTooltip(string name, string? role, string? model, Provider provider, bool worker, string expected)
    {
        var agent = Agent("name") with { Name = name, Role = role, Model = model, Provider = provider, IsSubagent = worker };
        var view = Assert.Single(new OfficeAnimator(42, layout: Room()).Update([agent], Now, 0));
        Assert.Equal(expected, view.Pose.DisplayName); Assert.Equal(name, view.Agent.Name);
        Assert.Equal(provider.ToString().ToLowerInvariant(), view.Pose.ProviderIcon);
    }

    [Fact]
    public void DisplayNameTruncationKeepsUnicodeTextElementsIntact()
    {
        var name = string.Concat(Enumerable.Repeat("👩‍💻", 17));
        var view = Assert.Single(new OfficeAnimator(42, layout: Room()).Update([Agent("unicode") with { Name = name }], Now, 0));
        Assert.Equal(string.Concat(Enumerable.Repeat("👩‍💻", 14)) + "…", view.Pose.DisplayName);
        Assert.Equal(name, view.Agent.Name);
    }

    [Fact]
    public void MonitorMetadataWorksWithoutPngStatesAndVersionFiveKeepsLegacyFields()
    {
        var office = new OfficeAnimator(42, layout: Room(3, 0));
        AgentInfo[] agents = [Agent("a"), Agent("b", AgentState.Idle)];
        var views = office.Update(agents, Now, 0);
        Assert.Equal("screensaver", office.Layout.Furniture.Single(f => f.AgentId == "a").MonitorState);
        Assert.Equal(2, office.Layout.Furniture.Count(f => f.AgentId != null));
        for (var i = 0; i < 80; i++) views = office.Update(agents, Now.AddSeconds(i * .1), .1);
        var desks = office.Layout.Furniture.Where(f => f.Kind == "desk").ToArray();
        Assert.Equal("working", desks.Single(f => f.AgentId == "a").MonitorState);
        Assert.Equal("screensaver", desks.Single(f => f.AgentId == "b").MonitorState);
        Assert.Equal("off", desks.Single(f => f.AgentId == null).MonitorState);
        Assert.All(desks, f => Assert.Equal("default", f.State));
        var snapshot = new StateSnapshot(StateJson.SchemaVersion, Now, 1, SpriteAtlas.Info, office.Layout, views, [], FurnitureSprites: office.FurnitureSprites);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(snapshot, StateJson.Options));
        Assert.Equal(5, json.RootElement.GetProperty("schemaVersion").GetInt32());
        var pose = json.RootElement.GetProperty("agents")[0].GetProperty("pose");
        foreach (var field in new[] { "x", "y", "desk", "action", "facing", "spriteRow", "spriteColumn", "visible", "opacity", "carryingPaper", "reportOnDesk", "reaction", "chatPartnerId", "characterId", "sprite" })
            Assert.True(pose.TryGetProperty(field, out _));
        Assert.Equal(views[0].Pose.DeskId, pose.GetProperty("deskId").GetString());
        Assert.Equal("desk", pose.GetProperty("workLocation").GetString());
        Assert.False(pose.GetProperty("queued").GetBoolean());
        var restored = JsonSerializer.Deserialize<StateSnapshot>(json.RootElement.GetRawText(), StateJson.Options)!;
        Assert.Equal(views[0].Pose.DisplayName, restored.Agents[0].Pose.DisplayName);
        var old = JsonSerializer.Deserialize<Pose>("{\"x\":0,\"y\":0,\"desk\":{\"x\":1,\"y\":1},\"action\":\"typing\",\"facing\":\"up\",\"spriteRow\":0,\"spriteColumn\":0,\"visible\":true}", StateJson.Options)!;
        Assert.Null(old.DeskId); Assert.False(old.Queued); Assert.Equal("desk", old.WorkLocation);
        var deskId = views[0].Pose.DeskId;
        foreach (var state in new[] { AgentState.Thinking, AgentState.Waiting, AgentState.Idle })
        {
            views = office.Update([agents[0] with { State = state }, agents[1]], Now.AddSeconds(9), .1);
            var ownerDesk = office.Layout.Furniture.Single(f => f.AgentId == "a");
            Assert.Equal(deskId, ownerDesk.DeskId); Assert.Equal("screensaver", ownerDesk.MonitorState);
        }
        Assert.Equal("walking", views[0].Pose.Action);
    }

    [Fact]
    public void SupportedMonitorStatesSelectAuthoritativeFurnitureFrames()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Vibedotcom-monitor-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var states = new[] { "off", "working", "screensaver" };
            var rows = states.Select((state, i) => new AnimationRow(state, "any", i, 1, 1)).ToArray();
            var variants = states.Prepend("default").Select(state => new FurnitureVariant(0, state, 3, 2,
                [new("seat", 1, 2, "typing")], SheetId: "monitors", Animation: state == "default" ? "off" : state)).ToArray();
            var manifest = new AssetManifest(1, [new("monitors", "monitors.png", 16, 24, rows, new(8, 24))],
                [new("desk", "desk", variants)], [new("monitor-person","monitors")], []);
            File.WriteAllBytes(Path.Combine(directory, "monitors.png"), SpriteAtlas.CreatePng());
            File.WriteAllText(Path.Combine(directory, "assets.json"), JsonSerializer.Serialize(manifest, StateJson.Options));
            var pack = new AssetPack(directory); Assert.Empty(pack.Warnings);
            var office = new OfficeAnimator(42, pack, Room(3, 0));
            AgentInfo[] agents = [Agent("a"), Agent("b", AgentState.Idle)];
            office.Update(agents, Now, 0);
            Assert.Equal("screensaver", office.Layout.Furniture.Single(f => f.AgentId == "a").State);
            for (var i = 0; i < 80; i++) office.Update(agents, Now.AddSeconds(i * .1), .1);
            Assert.Equal("working", office.Layout.Furniture.Single(f => f.AgentId == "a").State);
            Assert.All(office.Layout.Furniture.Where(f => f.Kind == "desk"), f => {
                Assert.Equal(f.MonitorState, f.State);
                var frame = Assert.Single(office.FurnitureSprites, s => s.Id == f.Id);
                Assert.Equal(f.MonitorState, frame.MonitorState); Assert.Equal(Array.IndexOf(states, f.MonitorState!), frame.Sprite.Row);
            });
            office.Update([], Now.AddSeconds(1), 0);
            Assert.All(office.FurnitureSprites, f => { Assert.Equal("off", f.MonitorState); Assert.Equal(0, f.Sprite.Row); });
        }
        finally { Directory.Delete(directory, true); }
    }
}
