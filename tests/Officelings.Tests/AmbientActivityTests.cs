using System.Text.Json;
using Officelings.Core;
using Xunit;

namespace Officelings.Tests;

public sealed class AmbientActivityTests
{
    private static readonly DateTimeOffset Epoch = DateTimeOffset.Parse("2026-10-04T00:00:00Z");
    private static AgentInfo Agent(string id, AgentState state = AgentState.Working) =>
        new(id, Provider.Codex, null, false, id, null, null, null, "", null, null, state, Epoch, Epoch, false);
    private static ActivityCatalog Only(string action, double seconds = 30) => new([new(action, 1, seconds, seconds)]);

    private static OfficeLayout Room(params InteractionPoint[] points) => new(24, 18, 16, new(1, 3),
        Enumerable.Range(0, 3).Select(i => new Furniture("desk", 3 + i * 5, 5, 3, 2, Id: "desk-" + i)).ToArray(),
        Tiles: Enumerable.Repeat(1, 24 * 18).ToArray(), Zones: [new("workspace", 1, 3, 15, 14),
            new("pantry", 17, 3, 6, 5), new("lounge", 17, 8, 6, 9, points), new("door", 1, 3, 1, 1)]);

    private static readonly InteractionPoint[] PairPoints = [new("far", 0, 0, "pingpong", "down"), new("near", 4, 7, "pingpong", "up")];
    private static void Settle(OfficeAnimator office, AgentInfo[] agents)
    {
        IReadOnlyList<AgentView> views = [];
        for (var i = 0; i < 160; i++) views = office.Update(agents, Epoch.AddSeconds(i * .1), .1);
        Assert.All(views, v => Assert.Equal("typing", v.Pose.Action));
    }
    private static (DateTimeOffset Time, IReadOnlyList<AgentView> Views) Reach(OfficeAnimator office, AgentInfo[] agents, string action)
    {
        IReadOnlyList<AgentView> views = [];
        for (var i = 0; i < 400; i++)
        {
            var time = Epoch.AddSeconds(20 + i * .1);
            views = office.Update(agents, time, .1);
            if (views.Any(v => v.Pose.Action == action)) return (time, views);
        }
        Assert.Contains(views, v => v.Pose.Action == action);
        throw new InvalidOperationException("Activity not reached.");
    }

    [Fact]
    public void HomeOverridesArePartialAndAcceptFractionalWeightsAndDurations()
    {
        var home = Path.Combine(Path.GetTempPath(), "officelings-activities-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(home);
            var file = Path.Combine(home, "activities.json");
            const string json = """{"version":1,"activities":[{"id":"darts","weight":0.25,"minSeconds":0.5,"maxSeconds":0.75}]}""";
            File.WriteAllText(file, json);
            var warnings = new List<string>();
            var catalog = ActivityCatalog.Load(home, warnings);
            Assert.Empty(warnings);
            Assert.Equal(new ActivityDefinition("darts", .25, .5, .75), catalog.Activities.Single(a => a.Id == "darts"));
            Assert.Equal(ActivityCatalog.Load().Activities.Single(a => a.Id == "reading"), catalog.Activities.Single(a => a.Id == "reading"));
            Assert.Equal(json, File.ReadAllText(file));
        }
        finally { if (Directory.Exists(home)) Directory.Delete(home, true); }
    }

    [Theory]
    [InlineData("{\"version\":2,\"activities\":[]}")]
    [InlineData("{\"version\":1,\"activities\":[{\"id\":\"darts\",\"weight\":-1}]}")]
    [InlineData("{\"version\":1,\"activities\":[{\"id\":\"darts\",\"minSeconds\":50,\"maxSeconds\":1}]}")]
    [InlineData("{\"version\":1,\"activities\":[{\"id\":\"unknown\",\"weight\":1}]}")]
    [InlineData("{\"version\":1,\"activities\":[null]}")]
    [InlineData("broken JSON")]
    public void InvalidHomeCatalogFallsBackAsAUnit(string json)
    {
        var home = Path.Combine(Path.GetTempPath(), "officelings-activities-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(home); File.WriteAllText(Path.Combine(home, "activities.json"), json);
            var warnings = new List<string>();
            Assert.Equal(ActivityCatalog.Load().Activities, ActivityCatalog.Load(home, warnings).Activities);
            Assert.Single(warnings);
        }
        finally { if (Directory.Exists(home)) Directory.Delete(home, true); }
    }

    [Theory]
    [InlineData("darts")]
    [InlineData("arcade")]
    [InlineData("reading")]
    [InlineData("beanbag")]
    [InlineData("aquarium")]
    [InlineData("vending")]
    [InlineData("fridge")]
    [InlineData("window")]
    public void ConfiguredDurationStartsOnArrivalAndSinglePointHasCapacityOne(string action)
    {
        var office = new OfficeAnimator(42, layout: Room(new InteractionPoint("station", 2, 4, action)), activities: Only(action, 1.25));
        AgentInfo[] working = [Agent("a"), Agent("b")]; Settle(office, working);
        var idle = working.Select(a => a with { State = AgentState.Idle }).ToArray();
        var (arrived, views) = Reach(office, idle, action);
        var active = Assert.Single(views, v => v.Pose.Action == action);
        Assert.Null(active.Pose.ActivityPartnerId);
        Assert.Equal(arrived.AddSeconds(1.25), office.NextUpdateAt);
        views = office.Update(idle, arrived.AddSeconds(.5), 0);
        Assert.Single(views, v => v.Pose.Action == action);
        Assert.Equal(arrived.AddSeconds(1.25), office.NextUpdateAt);
        if (action == "darts") Assert.InRange(office.AnimationInterval.TotalSeconds, 0.001, .125);
        for (var i = 1; i < 120; i++)
        {
            views = office.Update(idle, arrived.AddSeconds(.5 + i * .1), .1);
            Assert.InRange(views.Count(v => v.Pose.Action == action), 0, 1);
        }
    }

    [Fact]
    public void DisabledOrMissingPhoneUsesIdleFloorAndEnabledPhoneUsesItsConfiguredFallbackDuration()
    {
        foreach (var catalog in new[] { new ActivityCatalog([]), new ActivityCatalog([new("phone", 0, .5, .5)]) })
        {
            var office = new OfficeAnimator(42, layout: Room(), activities: catalog);
            AgentInfo[] working = [Agent("a")]; Settle(office, working);
            var (_, views) = Reach(office, [working[0] with { State = AgentState.Idle }], "idle");
            var pose = Assert.Single(views).Pose;
            Assert.Null(pose.ActivitySpotId); Assert.Null(pose.ActivityPartnerId);
            Assert.NotEqual(new Position(pose.Desk.X, pose.Desk.Y), new Position(pose.X, pose.Y));
        }
        var enabled = new OfficeAnimator(42, layout: Room(), activities: Only("phone", .75));
        AgentInfo[] agent = [Agent("a")]; Settle(enabled, agent);
        var (time, _) = Reach(enabled, [agent[0] with { State = AgentState.Idle }], "phone");
        Assert.Equal(time.AddSeconds(.75), enabled.NextUpdateAt);
    }

    [Theory]
    [InlineData(AgentState.Working)]
    [InlineData(AgentState.Thinking)]
    [InlineData(AgentState.Waiting)]
    [InlineData(AgentState.Reporting)]
    [InlineData(AgentState.Gone)]
    public void PingpongRequiresTwoAvailableIdleAgents(AgentState otherState)
    {
        var office = new OfficeAnimator(42, layout: Room(PairPoints), activities: Only("pingpong"));
        AgentInfo[] working = [Agent("a"), Agent("b")]; Settle(office, working);
        AgentInfo[] agents = [working[0] with { State = AgentState.Idle }, working[1] with { State = otherState }];
        for (var i = 0; i < 80; i++)
        {
            var views = office.Update(agents, Epoch.AddSeconds(20 + i * .1), .1);
            Assert.DoesNotContain(views, v => v.Pose.Action == "pingpong" || v.Pose.ActivityPartnerId != null);
        }
    }

    [Fact]
    public void PingpongPartnersAreReciprocalAndStartTheirSharedDurationAfterBothArrive()
    {
        var office = new OfficeAnimator(42, layout: Room(PairPoints), activities: Only("pingpong", 1.5));
        AgentInfo[] working = [Agent("c"), Agent("b"), Agent("a")]; Settle(office, working);
        var agents = working.Select(a => a with { State = AgentState.Idle }).ToArray();
        var waited = false; var started = false;
        for (var i = 0; i < 160; i++)
        {
            var time = Epoch.AddSeconds(20 + i * .1); var views = office.Update(agents, time, .1);
            var paired = views.Where(v => v.Pose.ActivityPartnerId != null).OrderBy(v => v.Agent.Id).ToArray();
            Assert.Equal(2, paired.Length);
            Assert.Equal("a", paired[0].Agent.Id); Assert.Equal("b", paired[1].Agent.Id);
            Assert.Equal("b", paired[0].Pose.ActivityPartnerId); Assert.Equal("a", paired[1].Pose.ActivityPartnerId);
            Assert.All(paired, v => Assert.Null(v.Pose.ChatPartnerId));
            if (paired.Any(v => v.Pose.Action == "waiting")) { waited = true; Assert.DoesNotContain(paired, v => v.Pose.Action == "pingpong"); }
            if (paired.All(v => v.Pose.Action == "pingpong"))
            {
                started = true;
                Assert.Equal(time.AddSeconds(1.5), office.NextUpdateAt);
                Assert.InRange(office.AnimationInterval.TotalSeconds, .001, .125);
                var later = office.Update(agents, time.AddSeconds(1), 0);
                Assert.Equal(2, later.Count(v => v.Pose.Action == "pingpong"));
                Assert.Equal(time.AddSeconds(1.5), office.NextUpdateAt);
                break;
            }
        }
        Assert.True(waited); Assert.True(started);
    }

    [Fact]
    public void SeededWeightedChoicesAndPartnerIdsIgnoreIncomingAgentOrder()
    {
        var points = PairPoints.Concat([new InteractionPoint("darts", 2, 1, "darts"), new("reading", 4, 3, "reading")]).ToArray();
        var catalog = new ActivityCatalog([new("darts", 5, 1.25, 3.5), new("reading", 1, 2.5, 4.25), new("pingpong", 2, 2, 4)]);
        var first = new OfficeAnimator(84, layout: Room(points), activities: catalog);
        var second = new OfficeAnimator(84, layout: Room(points), activities: catalog);
        AgentInfo[] working = [Agent("c"), Agent("b"), Agent("a")];
        Settle(first, working); Settle(second, working.Reverse().ToArray());
        var idle = working.Select(a => a with { State = AgentState.Idle }).ToArray();
        for (var i = 0; i < 400; i++)
        {
            var time = Epoch.AddSeconds(20 + i * .1);
            var a = first.Update(idle, time, .1).OrderBy(v => v.Agent.Id).Select(v => v.Pose).ToArray();
            var b = second.Update(idle.Reverse().ToArray(), time, .1).OrderBy(v => v.Agent.Id).Select(v => v.Pose).ToArray();
            Assert.Equal(a, b);
        }
    }

    [Theory]
    [InlineData(AgentState.Working)]
    [InlineData(AgentState.Thinking)]
    [InlineData(AgentState.Waiting)]
    [InlineData(AgentState.Reporting)]
    [InlineData(AgentState.Gone)]
    public void SemanticStateChangesCancelBothSidesImmediately(AgentState state)
    {
        var office = new OfficeAnimator(42, layout: Room(PairPoints), activities: Only("pingpong"));
        AgentInfo[] working = [Agent("a"), Agent("b")]; Settle(office, working);
        var idle = working.Select(a => a with { State = AgentState.Idle }).ToArray();
        var (time, _) = Reach(office, idle, "pingpong");
        var views = office.Update([idle[0] with { State = state }, idle[1]], time.AddSeconds(.1), 0);
        Assert.All(views, v => Assert.Null(v.Pose.ActivityPartnerId));
        Assert.DoesNotContain(views, v => v.Pose.Action == "pingpong");
    }

    [Fact]
    public void RemovedPartnerAndRemovedActivityPointsReleaseTheirReservations()
    {
        var room = Room(PairPoints); var office = new OfficeAnimator(42, layout: room, activities: Only("pingpong"));
        AgentInfo[] working = [Agent("a"), Agent("b")]; Settle(office, working);
        var idle = working.Select(a => a with { State = AgentState.Idle }).ToArray();
        var (time, _) = Reach(office, idle, "pingpong");
        Assert.Null(Assert.Single(office.Update([idle[0]], time.AddSeconds(.1), 0)).Pose.ActivityPartnerId);
        office.ReloadLayout(room with { Zones = room.Zones!.Select(z => z.Name == "lounge" ? z with { Points = [] } : z).ToArray() }, true);
        Assert.All(office.Update(idle, time.AddSeconds(.2), 0), v => Assert.Null(v.Pose.ActivityPartnerId));
    }

    [Theory]
    [InlineData("fridge")]
    [InlineData("vending")]
    public void DrinkingFurnitureOpensOnlyAfterArrivalAndClosesWhenInterrupted(string action)
    {
        var pack = AssetPack.LoadDefault(useUserPack: false); var office = new OfficeAnimator(42, pack, activities: Only(action));
        var station = Assert.Single(office.SourceLayout.Furniture, f => pack.Variant(f).Points.Any(p => p.Action == action));
        AgentInfo[] working = [Agent("a")]; Settle(office, working);
        var idle = new[] { working[0] with { State = AgentState.Idle } };
        var (time, views) = Reach(office, idle, action);
        Assert.Equal(station.Id, Assert.Single(views).Pose.ActivitySpotId);
        var open = office.Layout.Furniture.Single(f => f.Id == station.Id);
        Assert.Equal("open", open.State);
        Assert.Equal(LayoutLoader.Cells(station, pack.Variant(station)).OrderBy(t => t.Y).ThenBy(t => t.X),
            LayoutLoader.Cells(open, pack.Variant(open)).OrderBy(t => t.Y).ThenBy(t => t.X));
        office.Update(working, time.AddSeconds(.1), 0);
        Assert.Equal("closed", office.Layout.Furniture.Single(f => f.Id == station.Id).State);
    }

    [Theory]
    [InlineData("reading")]
    [InlineData("beanbag")]
    public void LeisureBodiesUseTheirAuthoredCushionContactsWithoutWorkSeatPhases(string action)
    {
        var pack = AssetPack.LoadDefault(useUserPack: false);
        var agent = Enumerable.Range(0, 512).Select(i => Agent("cushion:" + i)).First(a => pack.SeatedVariant(pack.Assign(a, []), "up") != null);
        var design = pack.Assign(agent, []); var office = new OfficeAnimator(42, pack, activities: Only(action));
        Settle(office, [agent]);
        var (_, views) = Reach(office, [agent with { State = AgentState.Idle }], action);
        var pose = Assert.Single(views).Pose; var sprite = pose.Sprite!;
        var host = office.SourceLayout.Furniture.Single(f => f.Id == pose.ActivitySpotId);
        var contact = pack.Variant(host).ChairSeatAnchor!;
        var x = host.X * 16 + contact.X; var y = host.Y * 16 + contact.Y;
        Assert.Null(pose.SeatPhase);
        if (action == "reading")
        {
            Assert.NotNull(pose.SeatedVariant); Assert.Equal(new PixelPoint(x, y), pose.SeatAnchor);
            var seated = pack.SeatedFrame(agent with { State = AgentState.Idle }, design, pose.SeatedVariant!, pose.Facing, 0, false);
            Assert.Equal(x - (pose.X + .5) * 16 + (sprite.AnchorX - seated.Anchor.X) * sprite.Scale, sprite.OffsetX, 6);
            Assert.Equal(y - (pose.Y + 1) * 16 + (sprite.AnchorY - seated.Anchor.Y) * sprite.Scale, sprite.OffsetY, 6);
        }
        else
        {
            var normal = pack.CharacterFrame(agent, design, "beanbag", pose.Facing, 0);
            Assert.Null(pose.SeatedVariant); Assert.Null(pose.SeatAnchor);
            Assert.Equal(normal.OffsetX + x - (pose.X + .5) * 16, sprite.OffsetX, 6);
            Assert.Equal(normal.OffsetY + y - (pose.Y + 1) * 16, sprite.OffsetY, 6);
            Assert.Equal(normal.Height, sprite.Height);
        }
    }

    [Fact]
    public void DefaultLayoutRetainsFixedWorkCapacityAndReachableAmbientStations()
    {
        var pack = AssetPack.LoadDefault(useUserPack: false);
        using var bundled = typeof(LayoutLoader).Assembly.GetManifestResourceStream("Officelings.Core.Data.layout.json")!;
        LayoutLoader.Validate(JsonSerializer.Deserialize<OfficeLayout>(bundled, StateJson.Options)!, pack);
        var office = new OfficeAnimator(42, pack);
        var layout = office.SourceLayout; LayoutLoader.Validate(layout, pack);
        Assert.Equal(35, layout.WidthTiles); Assert.Equal(20, layout.HeightTiles);
        Assert.Equal(12, layout.Furniture.Count(f => pack.Variant(f).Points.Any(p => p.Action == "typing")));
        Assert.Equal(8, layout.OverflowSeats!.Count);
        Assert.Null(layout.AutoDesks);
        var supplied = pack.Points(layout).Select(p => p.Point.Action).ToHashSet();
        Assert.All(new[] { "darts", "pingpong", "arcade", "reading", "beanbag", "aquarium", "vending", "fridge", "window" }, action => Assert.Contains(action, supplied));
        var before = JsonSerializer.Serialize(layout, StateJson.Options);
        office.Update(Enumerable.Range(0, 40).Select(i => Agent(i.ToString())).ToArray(), Epoch, 0);
        Assert.Equal(before, JsonSerializer.Serialize(office.SourceLayout, StateJson.Options));
    }
}
