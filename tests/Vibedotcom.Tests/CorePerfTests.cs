using System.Text.Json;
using Vibedotcom.Core;
using Xunit;

namespace Vibedotcom.Tests;

public sealed class CorePerfTests
{
    [Fact]
    public void ExportedPackReloadsForSnapshotSmokeWithoutRelaxingAuthoredPackValidation()
    {
        var path=Path.Combine(Path.GetTempPath(),"vibedotcom-export-roundtrip-"+Guid.NewGuid().ToString("N"));
        try
        {
            var pack=AssetPack.LoadDefault(useUserPack:false); var manifest=pack.Export(path);
            var imported=AssetPack.LoadExported(path);
            Assert.Empty(imported.Warnings);
            Assert.Equal(JsonSerializer.Serialize(manifest,StateJson.Options),JsonSerializer.Serialize(imported.Manifest,StateJson.Options));
            LayoutLoader.Validate(new OfficeAnimator(42,pack).Layout,imported);
            Assert.NotEmpty(new AssetPack(path).Warnings); // Authored packs still reserve fallback.
            var wrong=manifest with { Sheets=manifest.Sheets.Select((s,i)=>i==0 ? s with { Sha256=new string('0',64) } : s).ToArray() };
            StateJson.WriteAtomic(Path.Combine(path,"manifest.json"),JsonSerializer.SerializeToUtf8Bytes(wrong,StateJson.Options));
            Assert.Contains(AssetPack.LoadExported(path).Warnings,w=>w.Contains("dimensions/hash mismatch",StringComparison.Ordinal));
        }
        finally { if (Directory.Exists(path)) Directory.Delete(path,true); }
    }
    private static readonly DateTimeOffset Epoch = DateTimeOffset.Parse("2026-10-04T00:00:00Z");
    private static AgentInfo Agent(string id, AgentState state = AgentState.Working) =>
        new(id, Provider.Codex, null, false, "Google AI Pro", "worker", "models/gpt", null, "", null, null, state, Epoch, Epoch, false);

    private static OfficeLayout StaticRoom(AssetPack pack)
    {
        var layout = LayoutLoader.Load(pack, null);
        return layout with { Furniture = layout.Furniture.Where(f =>
        {
            var asset = pack.Manifest.Furniture.Single(a => a.Id == (f.AssetId ?? f.Kind));
            return asset.Variants.Where(v => v.Rotation == f.Rotation).All(v => v.SheetId == null || v.Animation == null ||
                pack.Manifest.Sheets.Single(s => s.Id == v.SheetId).Rows.First(r => r.Name == v.Animation && r.Direction == "any").Frames == 1);
        }).ToArray() };
    }

    private static OfficeLayout StaticSeatRoom(AssetPack pack)
    {
        var layout = StaticRoom(pack);
        return layout with { OverflowSeats = [layout.OverflowSeats!.Single(s => s.Id == "workspace-laptop-1")] };
    }

    private static IReadOnlyList<AgentView> Settle(OfficeAnimator office, AgentInfo[] agents)
    {
        IReadOnlyList<AgentView> views = [];
        for (var i = 0; i < 800; i++) views = office.Update(agents, Epoch.AddMilliseconds(i * 100), .1);
        Assert.All(views, v => Assert.Equal(new Position(v.Pose.Desk.X, v.Pose.Desk.Y), new Position(v.Pose.X, v.Pose.Y)));
        return views;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EmptyStaticOfficeHasNoTimerAndSteadyUpdatesAllocateNothing(bool art)
    {
        var pack = art ? AssetPack.LoadDefault(useUserPack: false) : new AssetPack();
        var office = new OfficeAnimator(42, pack, StaticRoom(pack));
        var agents = Array.Empty<AgentInfo>();
        for (var i = 0; i < 64; i++) office.Update(agents, Epoch.AddMilliseconds(i * 100), .1);
        var layout = office.Layout;
        var sprites = office.FurnitureSprites;
        var views = office.Update(agents, Epoch.AddSeconds(10), .1);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++) office.Update(agents, Epoch.AddSeconds(11).AddMilliseconds(i * 125), .125);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
        Assert.Equal(TimeSpan.Zero, office.AnimationInterval);
        Assert.Equal(DateTimeOffset.MaxValue, office.NextUpdateAt);
        Assert.False(office.HasMovingCharacters);
        Assert.Same(layout, office.Layout);
        Assert.Same(sprites, office.FurnitureSprites);
        Assert.Same(views, office.Update(agents, Epoch.AddSeconds(140), 0));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(12)]
    public void SettledBuiltinOfficeStaysBelowIdleAllocationBudget(int count)
    {
        var office = new OfficeAnimator(42, AssetPack.LoadDefault(useUserPack: false));
        var agents = Enumerable.Range(0, count).Select(i => Agent($"perf:{i:00}")).ToArray();
        Settle(office, agents);
        var layout = office.Layout;
        const int iterations = 800;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < iterations; i++) office.Update(agents, Epoch.AddSeconds(90).AddMilliseconds(i * 125), .125);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        // 800 calls at 8 fps represent 100 seconds; include live furniture and blinking.
        Assert.True(allocated <= 100 * 1024 * 100, $"{count} agents allocated {allocated} bytes over 100 seconds.");
        Assert.Same(layout, office.Layout);
        Assert.False(office.HasMovingCharacters);
        Assert.InRange(office.AnimationInterval.TotalMilliseconds, 125, 1000);
    }

    [Fact]
    public void BuiltinEmptyRoomKeepsItsAuthoredAmbientFrames()
    {
        var pack = AssetPack.LoadDefault(useUserPack: false);
        var office = new OfficeAnimator(42, pack);
        office.Update([], Epoch, 0);
        var before = office.FurnitureSprites.ToArray();
        var animated = office.Layout.Furniture.Where(f =>
        {
            var v = pack.Variant(f);
            return v.SheetId != null && v.Animation != null && pack.Manifest.Sheets.Single(s => s.Id == v.SheetId)
                .Rows.First(r => r.Name == v.Animation).Frames > 1;
        }).ToArray();
        Assert.NotEmpty(animated);
        Assert.True(office.AnimationInterval > TimeSpan.Zero);
        office.Update([], Epoch.AddMilliseconds(350), .35);
        foreach (var f in animated)
            Assert.NotEqual(before.Single(s => s.Id == f.Id).Sprite, office.FurnitureSprites.Single(s => s.Id == f.Id).Sprite);
    }

    [Fact]
    public void CachedOutputsKeepReferencesAndPreviouslyReturnedSnapshotsStayImmutable()
    {
        var pack = AssetPack.LoadDefault(useUserPack: false);
        var office = new OfficeAnimator(42, pack);
        var agents = Enumerable.Range(0, 12).Select(i => Agent($"immutable:{i:00}")).ToArray();
        Settle(office, agents);
        var now = Epoch.AddSeconds(81);
        var views = office.Update(agents, now, 0);
        var layout = office.Layout;
        var sprites = office.FurnitureSprites;
        var snapshot = new StateSnapshot(StateJson.SchemaVersion, now, 1, SpriteAtlas.Info, layout, views, [], pack.Manifest, sprites);
        var original = JsonSerializer.Serialize(snapshot, StateJson.Options);
        Assert.Same(views, office.Update(agents, now, 0));
        Assert.Same(layout, office.Layout);
        Assert.Same(sprites, office.FurnitureSprites);
        var repeatedFrame = pack.Frame("cozy-server", "cozy-server", "any", 0);
        Assert.Same(repeatedFrame, pack.Frame("cozy-server", "cozy-server", "any", 1000));
        var departed = agents.Select(a => a with { State = AgentState.Gone }).ToArray();
        office.Update(departed, now.AddSeconds(1), .1);
        office.ReloadLayout(office.SourceLayout, true);
        office.Update([], now.AddSeconds(2), .1);
        Assert.Equal(original, JsonSerializer.Serialize(snapshot, StateJson.Options));
    }

    [Fact]
    public void CachedViewsKeepTheExactIncomingAgentAndItsTimestampOffset()
    {
        var office = new OfficeAnimator(42);
        var agent = Agent("timestamp-offset");
        var previous = Assert.Single(office.Update([agent], Epoch, 0));
        var shifted = agent with { LastSeen = agent.LastSeen.ToOffset(TimeSpan.FromHours(9)) };
        var current = Assert.Single(office.Update([shifted], Epoch, 0));
        Assert.Same(shifted, current.Agent);
        Assert.Equal(TimeSpan.FromHours(9), current.Agent.LastSeen.Offset);
        Assert.Equal(TimeSpan.Zero, previous.Agent.LastSeen.Offset);
        var first = agent with { Name = "First" };
        var second = agent with { Name = "Second" };
        var duplicates = office.Update([first, second], Epoch, 0);
        Assert.Equal("First", duplicates[0].Pose.DisplayName);
        Assert.Equal("Second", duplicates[1].Pose.DisplayName);
    }

    [Fact]
    public void StaticWaitingCharacterUsesBlinkDeadlinesInsteadOfAFixedTimer()
    {
        var pack = AssetPack.LoadDefault(useUserPack: false);
        var agent = Enumerable.Range(0, 100).Select(i => Agent($"blink-deadline:{i}", AgentState.Waiting))
            .First(a =>
            {
                var design = pack.Assign(a, []);
                return pack.SeatedVariant(design, "right") != null && pack.Manifest.Sheets.Single(s => s.Id == design.SheetId)
                    .Rows.Any(r => r.Name == "blink-seated_right" && r.Direction == "right");
            });
        var office = new OfficeAnimator(42, pack, StaticSeatRoom(pack));
        var previous = Assert.Single(Settle(office, [agent]));
        Assert.Equal("seated", previous.Pose.SeatPhase);
        Assert.Equal(TimeSpan.Zero, office.AnimationInterval);
        var due = office.NextUpdateAt;
        Assert.InRange(due, Epoch.AddSeconds(79.9), Epoch.AddSeconds(86));
        var changed = Assert.Single(office.Update([agent], due, 0));
        Assert.NotEqual(previous.Pose.Sprite!.Row, changed.Pose.Sprite!.Row);
        Assert.Equal(TimeSpan.Zero, office.AnimationInterval);
        var sheet = pack.Manifest.Sheets.Single(s => s.Id == changed.Pose.Sprite.SheetId);
        if (sheet.Rows.Any(r => r.Row == changed.Pose.Sprite.Row && r.Name.StartsWith("blink-", StringComparison.Ordinal)))
            Assert.Equal(due.AddMilliseconds(150), office.NextUpdateAt);
        Assert.True(office.NextUpdateAt > due);
    }

    [Fact]
    public void SeatTransitionsExposeTheirExactDeadlinesAndOnlyWalkingRequestsThirtyFps()
    {
        var pack = AssetPack.LoadDefault(useUserPack: false);
        var agent = Enumerable.Range(0, 100).Select(i => Agent($"seat-deadline:{i}"))
            .First(a => pack.SeatedVariant(pack.Assign(a, []), "right") != null);
        var office = new OfficeAnimator(42, pack, StaticSeatRoom(pack));
        AgentView view = null!;
        var now = Epoch;
        for (var i = 0; i < 1000; i++)
        {
            now = Epoch.AddMilliseconds(i * 40);
            view = Assert.Single(office.Update([agent], now, .04));
            if (view.Pose.SeatPhase == "turn") break;
            Assert.True(office.HasMovingCharacters);
            Assert.InRange(office.AnimationInterval.TotalMilliseconds, 33, 34);
        }
        Assert.Equal("turn", view.Pose.SeatPhase);
        office.Update([agent], now, 0);
        Assert.False(office.HasMovingCharacters);
        Assert.Equal(TimeSpan.Zero, office.AnimationInterval);
        Assert.Equal(now.AddMilliseconds(120), office.NextUpdateAt);
        view = Assert.Single(office.Update([agent], office.NextUpdateAt, 0));
        Assert.Equal("lower", view.Pose.SeatPhase);
        Assert.Equal(0, view.Pose.SeatPhaseFrame);
        Assert.Equal(now.AddMilliseconds(240), office.NextUpdateAt);
        view = Assert.Single(office.Update([agent], office.NextUpdateAt, 0));
        Assert.Equal(1, view.Pose.SeatPhaseFrame);
        Assert.Equal(now.AddMilliseconds(360), office.NextUpdateAt);
        view = Assert.Single(office.Update([agent], office.NextUpdateAt, 0));
        Assert.Equal("seated", view.Pose.SeatPhase);
        Assert.InRange(office.AnimationInterval.TotalMilliseconds, 125, 250);
        var leaving=now.AddMilliseconds(460);
        var idle=agent with { State=AgentState.Idle,StateSince=leaving };
        view=Assert.Single(office.Update([idle],leaving,.1));
        Assert.Equal("raise",view.Pose.SeatPhase);
        Assert.Equal(TimeSpan.Zero,office.AnimationInterval);
        Assert.Equal(leaving.AddMilliseconds(120),office.NextUpdateAt);
        view=Assert.Single(office.Update([idle],office.NextUpdateAt,.12));
        Assert.Equal("raise",view.Pose.SeatPhase);
        Assert.Equal(1,view.Pose.SeatPhaseFrame);
        Assert.Equal(leaving.AddMilliseconds(240),office.NextUpdateAt);
    }
}
