using Vibedotcom.Core;
using Vibedotcom.Core.Iso;
using Xunit;

namespace Vibedotcom.Tests;

[Collection("Iso assets")]
public class IsoOfficeTests
{
    private static readonly DateTimeOffset Epoch = DateTimeOffset.Parse("2026-10-04T00:00:00Z");
    private static AgentInfo Agent(string id, AgentState state = AgentState.Working, string? parent = null) =>
        new(id, Provider.Codex, parent, parent != null, id, null, null, null, "synthetic", null, null, state, Epoch, Epoch, false);
    private static string NormalSheet(IsoPack p, string id) => p.Manifest.Sheets.FirstOrDefault(s => s.Blink?.SheetId == id)?.Id ?? id;
    private static IsoScene Advance(IsoOffice office, AgentInfo[] agents, double seconds)
    {
        var scene = office.Update(agents, Epoch, 0);
        for (var t = 0d; t < seconds; t += .05) scene = office.Update(agents, Epoch.AddSeconds(t), Math.Min(.05, seconds - t));
        return scene;
    }
    [Fact]
    public void SpecPlacementIsFixedAndAllApproachesReachable()
    {
        var pack = IsoPackTests.Source.Value; var layout = IsoLayoutLoader.Default(pack);
        Assert.Equal(10, layout.WidthTiles); Assert.Equal(10, layout.HeightTiles);
        Assert.Equal(new Tile(0, 4), layout.Door); Assert.Equal(16, layout.Furniture.Count);
        Assert.Equal(20, layout.WallSegments!.Count);
        Assert.Equal(12, layout.Tiles!.Count(t => t == 2));
        var reachable = IsoLayoutLoader.Navigation(layout, pack).Reachable(layout.Door);
        foreach (var f in layout.Furniture) foreach (var p in pack.Variant(f.AssetId, f.Rotation).Points ?? [])
            Assert.Contains(new Tile(f.X + p.X, f.Y + p.Y), reachable);
    }
    [Fact]
    public void StableDesignSeatAssignmentsAndOverflowUseStandingPoses()
    {
        var pack = IsoPackTests.Source.Value; var office = new IsoOffice(pack, 42, pack.Layout);
        var agents = Enumerable.Range(0, 14).Select(i => Agent("a" + i.ToString("D2"))).Reverse().ToArray();
        var first = Advance(office, agents, 12);
        var bodies = first.Items.Where(i => i.Layer == IsoLayer.Character).ToArray();
        Assert.Equal(14, bodies.Length); Assert.Equal(3, bodies.Count(i => i.Clip.StartsWith("typing/")));
        Assert.Equal(3, bodies.Where(i => i.HitTargetId is "a00" or "a01" or "a02").Select(i => NormalSheet(pack, i.SheetId)).Distinct().Count());
        var mapping = bodies.ToDictionary(i => i.HitTargetId!, i => NormalSheet(pack, i.SheetId));
        var second = Advance(office, agents.Reverse().ToArray(), 1);
        Assert.All(second.Items.Where(i => i.Layer == IsoLayer.Character), i => Assert.Equal(mapping[i.HitTargetId!], NormalSheet(pack, i.SheetId)));
        Assert.Equal(16, office.Layout.Furniture.Count);
    }
    [Fact]
    public void TurnLowerSeatAndRaiseContactScheduleUsesFourAuthoredFrames()
    {
        var pack = IsoPackTests.Source.Value; var office = new IsoOffice(pack, 42, pack.Layout); var agents = new[] { Agent("solo") };
        var scene = office.Update(agents, Epoch, .8);
        var turn = Assert.Single(scene.Items, i => i.Layer == IsoLayer.Character); Assert.Equal("idle/se", turn.Clip);
        scene = office.Update(agents, Epoch, .12);
        var lower = Assert.Single(scene.Items, i => i.Layer == IsoLayer.Character); Assert.Equal("sit-down/se", lower.Clip); Assert.Equal(0, lower.Frame);
        var seat = IsoContacts.Desk(2, 2, 180); var ground = IsoGeometry.Ground(seat.Approach.X, seat.Approach.Y);
        for (var phase = 1; phase <= 3; phase++)
        {
            scene = office.Update(agents, Epoch, .1); lower = Assert.Single(scene.Items, i => i.Layer == IsoLayer.Character);
            var c = IsoContacts.Lowering(phase); Assert.Equal(phase, lower.Frame);
            Assert.Equal(office.Camera.Y + ground.Y, lower.Y + c.FootY);
        }
        scene = office.Update(agents, Epoch, .1);
        var typing = Assert.Single(scene.Items, i => i.Layer == IsoLayer.Character); Assert.Equal("typing/se", typing.Clip);
        Assert.Equal(office.Camera.X + seat.Pelvis.X - 16, typing.X);
        Assert.Equal(office.Camera.Y + seat.Pelvis.Y - 44, typing.Y);
        var gone = new[] { agents[0] with { State = AgentState.Gone } };
        scene = office.Update(gone, Epoch, 0); var raise = Assert.Single(scene.Items, i => i.Layer == IsoLayer.Character);
        Assert.Equal("stand-up/se", raise.Clip); Assert.Equal(0, raise.Frame); Assert.Equal(typing.X, raise.X); Assert.Equal(typing.Y, raise.Y);
        scene = office.Update(gone, Epoch, .3); raise = Assert.Single(scene.Items, i => i.Layer == IsoLayer.Character);
        Assert.Equal("stand-up/se", raise.Clip); Assert.Equal(3, raise.Frame);
        Assert.Equal(office.Camera.Y + ground.Y, raise.Y + 60);
        scene = office.Update(gone, Epoch, .1); Assert.StartsWith("walk/", Assert.Single(scene.Items, i => i.Layer == IsoLayer.Character).Clip);
        Assert.Empty(Advance(office, gone, 5).Labels);
    }
    [Fact]
    public void WaitingAndRestHoldNeutralSeatedFrameWhileThinkingRunsItsLoop()
    {
        var office = new IsoOffice(IsoPackTests.Source.Value, 42, IsoPackTests.Source.Value.Layout); var agent = Agent("solo");
        Advance(office, [agent], 4);
        var waiting = office.Update([agent with { State = AgentState.Waiting }], Epoch, 0);
        Assert.Equal("seated-waiting/se", Assert.Single(waiting.Items, i => i.Layer == IsoLayer.Character).Clip);
        var still = Advance(office, [agent with { State = AgentState.Waiting }], 2);
        Assert.Equal(0, Assert.Single(still.Items, i => i.Layer == IsoLayer.Character).Frame);
        var rest = office.Update([agent with { State = AgentState.Idle }], Epoch, 0);
        Assert.Equal(0, Assert.Single(rest.Items, i => i.Layer == IsoLayer.Character).Frame);
        var thinking = office.Update([agent with { State = AgentState.Thinking }], Epoch, .5);
        Assert.StartsWith("thinking/", Assert.Single(thinking.Items, i => i.Layer == IsoLayer.Character).Clip);
    }
    [Fact]
    public void MonitorStateAndLabelsFollowVisibleActorsAndExitRemovesThem()
    {
        var office = new IsoOffice(IsoPackTests.Source.Value, 42, IsoPackTests.Source.Value.Layout); var agent = Agent("solo"); var scene = Advance(office, [agent], 5);
        Assert.Contains(scene.Items, i => i.InstanceId == "desk-1/screen" && i.Clip.StartsWith("screen-working/"));
        var label = Assert.Single(scene.Labels); var body = Assert.Single(scene.Items, i => i.Layer == IsoLayer.Character);
        Assert.Equal(agent.Name, label.Text); Assert.Equal(body.X + 16, label.X); Assert.True(label.Y < body.Y + 44);
        scene = office.Update([agent with { State = AgentState.Waiting }], Epoch, 0);
        Assert.Contains(scene.Items, i => i.InstanceId == "desk-1/screen" && i.Clip.StartsWith("screen-screensaver/"));
        scene = Advance(office, [], 6);
        Assert.Empty(scene.Labels); Assert.DoesNotContain(scene.Items, i => i.Layer == IsoLayer.Character);
        Assert.Contains(scene.Items, i => i.InstanceId == "desk-1/screen" && i.Clip.StartsWith("screen-off/"));
    }
    [Fact]
    public void CatWalksOnUnblockedGridAndSleepsAgain()
    {
        var pack = IsoPackTests.Source.Value; var office = new IsoOffice(pack, 42, pack.Layout);
        var first = office.Update([], Epoch, 0); Assert.StartsWith("sleep/", Assert.Single(first.Items, i => i.Layer == IsoLayer.Pet).Clip);
        var sawWalk = false; var sawSleepAgain = false; var nav = IsoLayoutLoader.Navigation(office.Layout, pack);
        for (var t = 0d; t < 90; t += .1)
        {
            var scene = office.Update([], Epoch.AddSeconds(t), .1); var pet = Assert.Single(scene.Items, i => i.Layer == IsoLayer.Pet);
            var anchor = pack.Frame(pet); var tile = IsoGeometry.Pick(pet.X + anchor.AnchorX - office.Camera.X, pet.Y + anchor.AnchorY - office.Camera.Y);
            Assert.True(nav.Walkable(tile));
            Assert.DoesNotContain(tile, office.Layout.Furniture.Where(f => f.AssetId == "iso/desk").Select(f => IsoContacts.Desk(f.X, f.Y, f.Rotation).Approach));
            if (pet.Clip.StartsWith("walk/")) sawWalk = true;
            if (sawWalk && pet.Clip.StartsWith("sleep/")) sawSleepAgain = true;
        }
        Assert.True(sawWalk); Assert.True(sawSleepAgain);
    }
    [Fact]
    public void RevisionIsStableBeforeNextVisibleChangeAndNewNameInvalidatesIt()
    {
        var office = new IsoOffice(IsoPackTests.Source.Value, 42, IsoPackTests.Source.Value.Layout); var agents = new[] { Agent("solo", AgentState.Waiting) };
        var first = Advance(office, agents, 2);
        var next = office.NextChangeSeconds;
        var unchanged = office.Update(agents, Epoch, Math.Min(.0001, next / 2));
        Assert.Same(first, unchanged);
        var renamed = office.Update([agents[0] with { Name = "changed" }], Epoch, 0);
        Assert.True(renamed.Revision > unchanged.Revision); Assert.Equal("changed", Assert.Single(renamed.Labels).Text);
    }
    [Fact]
    public void DefaultOfficeHasTwelveReachableSeatsAndStandingOverflow()
    {
        var p = IsoPackTests.Source.Value; var layout = IsoTwelveSeatFixture.Create(p);
        Assert.Equal(12, layout.Furniture.Count(f => f.AssetId == "iso/desk"));
        Assert.Equal(63, IsoLayoutLoader.Navigation(layout, p).Reachable(layout.Door).Count);
        var office = new IsoOffice(p, 42);
        var agents = Enumerable.Range(0, 15).Select(i => Agent("a" + i.ToString("D2"))).ToArray();
        var scene = Advance(office, agents, 20);
        Assert.Equal(12, scene.Items.Count(i => i.Layer == IsoLayer.Character && i.Clip.StartsWith("typing/")));
        Assert.Equal(15, scene.Labels.Count);
    }
    [Fact]
    public void ReturnDuringRaisingFinishesRaiseAndReseatsWithoutReport()
    {
        var office = new IsoOffice(IsoPackTests.Source.Value, 42, IsoPackTests.Source.Value.Layout); var a = Agent("solo"); var first = Advance(office, [a], 2);
        var design = first.Items.Single(i => i.Layer == IsoLayer.Character).SheetId;
        office.Update([a with { State = AgentState.Gone }], Epoch, .2);
        var returned = office.Update([a], Epoch, .2);
        Assert.DoesNotContain(returned.Items, i => i.Layer == IsoLayer.Prop);
        var final = Advance(office, [a], 2);
        Assert.Contains(final.Items, i => i.Layer == IsoLayer.Character && i.SheetId == design && i.Clip.StartsWith("typing/"));
    }
    [Fact]
    public void ReportLifecycleUsesPaperAndReturnsToSeat()
    {
        var office = new IsoOffice(IsoPackTests.Source.Value, 42, IsoPackTests.Source.Value.Layout); var parent = Agent("a-parent"); var child = Agent("b-child", parent: parent.Id);
        Advance(office, [parent, child], 5); child = child with { State = AgentState.Reporting };
        var paper = false; var handover = false;
        for (var t = 0; t < 400; t++)
        {
            var scene = office.Update([parent, child], Epoch, .05);
            paper |= scene.Items.Any(i => i.HitTargetId == child.Id && i.Layer == IsoLayer.Prop);
            handover |= scene.Items.Any(i => i.HitTargetId == child.Id && i.Clip.StartsWith("report-handover/"));
        }
        Assert.True(paper); Assert.True(handover);
        var last = office.Update([parent, child], Epoch, 0);
        Assert.Contains(last.Items, i => i.HitTargetId == child.Id && i.Layer == IsoLayer.Character && i.Clip.StartsWith("seated-idle/"));
    }
}
