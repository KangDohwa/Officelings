using System.Text.Json;
using Officelings.App;
using Officelings.Core;
using Xunit;

namespace Officelings.Tests;

public sealed class PetTests
{
    private static readonly DateTimeOffset Epoch = DateTimeOffset.Parse("2026-10-04T00:00:00Z");
    private static readonly string[] Facings = ["down", "left", "right", "up"];

    private sealed class Pack : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "Officelings-pets-" + Guid.NewGuid().ToString("N"));
        public AssetPack Assets { get; }
        public Pack(params string[] ids)
        {
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(Path.Combine(directory, "sheet.png"), SpriteAtlas.CreatePng());
            var sheets = ids.Select(id => {
                var rows = Facings.Select((facing, column) => new AnimationRow("idle", facing, 0, 1, 1, column)).ToList();
                if (id == "pet-parrot") rows.Add(new("head_bob", "down", 1, 4, 3));
                else rows.AddRange(Facings.Select((facing, row) => new AnimationRow(
                    id == "pet-hamster-ball" ? "roll" : "walk", facing, row + 1, id == "pet-hamster-ball" ? 3 : 4, 7)));
                rows.Add(new("sit", "down", 5, 2, 2));
                rows.Add(new("sleep", "right", 6, 3, 1, SeatAnchor: new(8, 22)));
                return new Sheet(id, "sheet.png", 16, 24, rows, new(8, 24));
            }).Append(new Sheet("people", "sheet.png", 16, 24, [new("idle", "any", 0, 1, 1)], new(8, 24))).ToArray();
            var desk = new FurnitureAsset("test-desk", "desk", new[] { "default", "working", "screensaver" }.Select(state =>
                new FurnitureVariant(0, state, 2, 1, [new("seat", 1, 1, "typing", "up")])).ToArray());
            var sofa = new FurnitureAsset("test-sofa", "sofa", [new(0, "default", 3, 2,
                [new("nap", 1, 2, "napping", "down")], ChairSeatAnchor: new(24, 18))]);
            var bed = new FurnitureAsset("test-bed", "bed", [new(0, "default", 3, 2,
                [new("nap", 1, 2, "napping", "down")], ChairSeatAnchor: new(24, 20))]);
            var manifest = new AssetManifest(1, sheets, [desk, sofa, bed], [new("person", "people")], []);
            File.WriteAllText(Path.Combine(directory, "assets.json"), JsonSerializer.Serialize(manifest, StateJson.Options));
            Assets = new AssetPack(directory);
            Assert.Empty(Assets.Warnings);
        }
        public void Dispose() => Directory.Delete(directory, true);
    }

    private static OfficeLayout Room(IReadOnlyList<Furniture>? furniture = null, IReadOnlyList<OverflowSeat>? overflow = null) =>
        new(16, 12, 16, new(1, 10), furniture ?? [], Tiles: Enumerable.Range(0, 192)
            .Select(i => i % 16 is 0 or 15 || i / 16 is 0 or 11 ? 0 : 1).ToArray(), OverflowSeats: overflow ?? []);
    private static AgentView Agent(string id, double x, double y, bool worker = false, bool visible = true) =>
        new(new(id, Provider.Codex, worker ? "boss" : null, worker, id, null, null, null, "", null, null,
                AgentState.Working, Epoch, Epoch, false),
            new(x, y, new(3, 3), "idle", "down", 0, 0, visible));
    private static HashSet<Tile> Obstacles(OfficeLayout room, AssetPack pack)
    {
        var blocked = LayoutLoader.Blocked(room, pack);
        blocked.UnionWith(pack.Points(room).Where(p => p.Point.Action == "typing").Select(p => p.Tile));
        blocked.UnionWith((room.OverflowSeats ?? []).Select(s => new Tile(s.X, s.Y)));
        return blocked;
    }
    private static void OnFloor(OfficeLayout room, PetView pet, HashSet<Tile> blocked) =>
        Assert.True(LayoutLoader.Walkable(room, new((int)Math.Round(pet.X), (int)Math.Round(pet.Y)), blocked),
            $"{pet.Id} crossed a blocked or reserved tile at ({pet.X},{pet.Y}).");

    [Fact]
    public void MissingSheetsDisabledPetsAndNoWalkableFloorProduceNoRenderState()
    {
        var missing = new PetAnimator(new AssetPack());
        Assert.True(missing.Enabled);
        Assert.Empty(missing.Update(Room(), [], Epoch));
        Assert.Equal(TimeSpan.Zero, missing.AnimationInterval);
        using var pack = new Pack("pet-shiba");
        var animator = new PetAnimator(pack.Assets);
        Assert.Single(animator.Update(Room(), [], Epoch));
        animator.Enabled = false;
        Assert.Empty(animator.Update(Room(), [], Epoch.AddSeconds(1)));
        Assert.Equal(TimeSpan.Zero, animator.AnimationInterval);
        animator.Enabled = true;
        Assert.Empty(animator.Update(Room() with { Tiles = Enumerable.Repeat(0, 192).ToArray() }, [], Epoch.AddSeconds(2)));
        pack.Assets.Images.Remove("pet-shiba");
        Assert.Empty(new PetAnimator(pack.Assets).Update(Room(), [], Epoch.AddSeconds(3)));
    }

    [Fact]
    public void SelectionAndAnimationAreDeterministicAndLimitedToOneOrTwoAvailablePets()
    {
        using var pack = new Pack("pet-shiba", "pet-tuxedo-cat", "pet-corgi", "pet-capybara");
        using var reversed = new Pack("pet-capybara", "pet-corgi", "pet-tuxedo-cat", "pet-shiba");
        var first = new PetAnimator(pack.Assets); var second = new PetAnimator(reversed.Assets); var room = Room();
        var selected = first.Update(room, [], Epoch);
        Assert.InRange(selected.Count, 1, 2);
        Assert.Equal(selected.Count, selected.Select(p => p.SheetId).Distinct().Count());
        for (var step = 0; step < 300; step++)
        {
            var time = Epoch.AddMilliseconds(step * 125);
            Assert.Equal(first.Update(room, [], time), second.Update(room, [], time));
            Assert.Equal(first.AnimationInterval, second.AnimationInterval);
        }
    }

    [Fact]
    public void RoamingAndFollowingAvoidWallsAllWorkSeatsSeatOverridesAndOverflowReservations()
    {
        using var pack = new Pack("pet-shiba");
        var room = Room([
            new("desk", 4, 4, 2, 1, Id: "desk", AssetId: "test-desk", Seat: new("override", 2, 1, "typing")),
            new("desk", 10, 7, 2, 1, Id: "unused-desk", AssetId: "test-desk")],
            [new("unused-overflow", 7, 5, "up"), new("work-overflow", 8, 5, "up")]);
        var tiles = room.Tiles!.ToArray();
        for (var y = 1; y < 9; y++) tiles[y * 16 + 9] = 0;
        room = room with { Tiles = tiles };
        var blocked = Obstacles(room, pack.Assets);
        var animator = new PetAnimator(pack.Assets);
        var supervisor = Agent("boss", 12, 5);
        supervisor = supervisor with { Pose = supervisor.Pose with { Desk = new(7, 8) } };
        blocked.Add(supervisor.Pose.Desk);
        var walking = false;
        for (var step = 0; step < 1800; step++)
        {
            if (step == 900)
            {
                blocked.Remove(supervisor.Pose.Desk);
                supervisor = supervisor with { Pose = supervisor.Pose with { Desk = new(7, 9) } };
                blocked.Add(supervisor.Pose.Desk);
            }
            var view = Assert.Single(animator.Update(room, [supervisor], Epoch.AddMilliseconds(step * 125)));
            OnFloor(room, view, blocked);
            if (view.Action == "walking")
            {
                walking = true;
                Assert.Equal(TimeSpan.FromMilliseconds(125), animator.AnimationInterval);
                Assert.InRange(view.Sprite.Row, 1, 4);
            }
            else
            {
                Assert.Equal(TimeSpan.Zero, animator.AnimationInterval); // One-cell idle has no visual tick.
                Assert.True(animator.NextUpdateAt > Epoch.AddMilliseconds(step * 125));
            }
        }
        Assert.True(walking);
    }

    [Fact]
    public void FollowsOnlyVisibleSupervisorsRefreshesWithinASecondAndStopsWhenTargetChangesOrLeaves()
    {
        using var pack = new Pack("pet-shiba");
        var room = Room(); var animator = new PetAnimator(pack.Assets);
        var initial = Assert.Single(animator.Update(room, [], Epoch));
        var supervisor = Agent("boss", initial.X, initial.Y);
        var worker = Agent("worker", initial.X, initial.Y, worker: true);
        var hidden = Agent("hidden", initial.X, initial.Y, visible: false);
        PetView view = initial; var step = 1;
        for (; step < 1200; step++)
        {
            view = Assert.Single(animator.Update(room, [hidden, worker, supervisor], Epoch.AddMilliseconds(step * 125)));
            Assert.True(view.FollowingId is null or "boss");
            if (view.FollowingId != null) break;
        }
        Assert.Equal("boss", view.FollowingId);
        var destination = Enumerable.Range(0, 192).Select(i => new Tile(i % 16, i / 16))
            .First(t => LayoutLoader.Walkable(room, t, Obstacles(room, pack.Assets)) && Math.Abs(t.X - view.X) + Math.Abs(t.Y - view.Y) >= 5);
        supervisor = supervisor with { Pose = supervisor.Pose with { X = destination.X, Y = destination.Y } };
        var start = new Position(view.X, view.Y);
        for (var tick = 1; tick <= 8; tick++)
            view = Assert.Single(animator.Update(room, [supervisor, worker, hidden], Epoch.AddMilliseconds((step + tick) * 125)));
        Assert.Equal("boss", view.FollowingId);
        Assert.NotEqual(start, new Position(view.X, view.Y));
        view = Assert.Single(animator.Update(room, [supervisor with { Agent = supervisor.Agent with { IsSubagent = true } }],
            Epoch.AddMilliseconds((step + 9) * 125)));
        Assert.Null(view.FollowingId);
        supervisor = supervisor with { Pose = supervisor.Pose with { X = view.X, Y = view.Y } };
        for (step += 10; step < 2400; step++)
        {
            view = Assert.Single(animator.Update(room, [supervisor], Epoch.AddMilliseconds(step * 125)));
            if (view.FollowingId != null) break;
        }
        Assert.NotNull(view.FollowingId);
        view = Assert.Single(animator.Update(room, [supervisor with { Pose = supervisor.Pose with { Visible = false } }],
            Epoch.AddMilliseconds((step + 1) * 125)));
        Assert.Null(view.FollowingId);
    }

    [Fact]
    public void FollowingEndsAfterAShortVisitEvenWhenTheSupervisorRemainsVisible()
    {
        using var pack = new Pack("pet-shiba");
        var room = Room(); var animator = new PetAnimator(pack.Assets);
        var initial = Assert.Single(animator.Update(room, [], Epoch));
        var supervisor = Agent("boss", initial.X, initial.Y);
        var step = 1; PetView view = initial;
        for (; step < 1200; step++)
        {
            view = Assert.Single(animator.Update(room, [supervisor], Epoch.AddMilliseconds(step * 125)));
            if (view.FollowingId != null) break;
        }
        Assert.Equal("boss", view.FollowingId);
        var ended = false;
        for (var tick = 1; tick <= 88; tick++)
        {
            view = Assert.Single(animator.Update(room, [supervisor], Epoch.AddMilliseconds((step + tick) * 125)));
            if (view.FollowingId == null) { ended = true; break; }
        }
        Assert.True(ended);
    }

    [Theory]
    [InlineData("sofa", "test-sofa", 18)]
    [InlineData("bed", "test-bed", 20)]
    public void NapsAtReachableFloorApproachWithBodyOffsetAlignedToTheCushion(string kind, string assetId, int cushionY)
    {
        using var pack = new Pack("pet-shiba");
        var furniture = new Furniture(kind, 5, 4, 3, 2, Id: "nap-furniture", AssetId: assetId);
        var room = Room([furniture]); var animator = new PetAnimator(pack.Assets);
        var blocked = Obstacles(room, pack.Assets); PetView view = null!; var step = 0;
        for (; step < 2400; step++)
        {
            view = Assert.Single(animator.Update(room, [], Epoch.AddMilliseconds(step * 125)));
            OnFloor(room, view, blocked);
            if (view.Action == "napping") break;
        }
        Assert.Equal("napping", view.Action); Assert.Equal("nap-furniture", view.FurnitureId);
        Assert.Equal(new Position(6, 6), new Position(view.X, view.Y)); Assert.Equal("right", view.Facing);
        Assert.Equal(6, view.Sprite.Row); Assert.Equal(TimeSpan.FromSeconds(1), animator.AnimationInterval); // Authored sleep is 1 FPS.
        var contactX = (view.X + .5) * 16 - view.Sprite.AnchorX * view.Sprite.Scale + view.Sprite.OffsetX + 8 * view.Sprite.Scale;
        var contactY = (view.Y + 1) * 16 - view.Sprite.AnchorY * view.Sprite.Scale + view.Sprite.OffsetY + 22 * view.Sprite.Scale;
        Assert.Equal(5 * 16 + 24, contactX); Assert.Equal(4 * 16 + cushionY, contactY);
        Assert.NotEqual(0, view.Sprite.OffsetY);
        var changed = room with { Furniture = [furniture with { X = 6 }] };
        view = Assert.Single(animator.Update(changed, [], Epoch.AddMilliseconds(step * 125)));
        OnFloor(changed, view, Obstacles(changed, pack.Assets));
        Assert.NotEqual("napping", view.Action);
    }

    [Fact]
    public void UnreachableOrOccupiedSofasAreNeverUsedForNaps()
    {
        using var pack = new Pack("pet-shiba");
        var room = Room([
            new("sofa", 11, 3, 3, 2, Id: "isolated", AssetId: "test-sofa"),
            new("sofa", 3, 3, 3, 2, Id: "reserved", AssetId: "test-sofa")],
            [new("sofa-seat", 4, 5, "down", "reserved")]);
        var tiles = room.Tiles!.ToArray();
        for (var y = 0; y < 12; y++) tiles[y * 16 + 8] = 0;
        room = room with { Tiles = tiles };
        var animator = new PetAnimator(pack.Assets);
        var occupant = Agent("occupant", 2, 8) with {
            Pose = Agent("occupant", 2, 8).Pose with { OverflowSeatId = "sofa-seat", WorkLocation = "overflow" } };
        for (var step = 0; step < 1600; step++)
        {
            var view = Assert.Single(animator.Update(room, [occupant], Epoch.AddMilliseconds(step * 125)));
            Assert.Null(view.FurnitureId); Assert.NotEqual("napping", view.Action); Assert.True(view.X < 8);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EmptyOverflowSofaUsesAnotherFloorApproachAndYieldsToAnAssignedOrNearbyAgent(bool assigned)
    {
        using var pack = new Pack("pet-shiba");
        var room = Room([new("sofa", 3, 3, 3, 2, Id: "sofa", AssetId: "test-sofa")],
            [new("sofa-seat", 4, 5, "down", "sofa"), new("second-seat", 5, 5, "down", "sofa")]);
        var animator = new PetAnimator(pack.Assets); var blocked = Obstacles(room, pack.Assets);
        PetView view = null!; var step = 0;
        for (; step < 2400; step++)
        {
            view = Assert.Single(animator.Update(room, [], Epoch.AddMilliseconds(step * 125)));
            OnFloor(room, view, blocked);
            if (view.Action == "napping") break;
        }
        Assert.Equal("napping", view.Action); Assert.Equal("sofa", view.FurnitureId);
        Assert.DoesNotContain(new Tile((int)view.X, (int)view.Y), room.OverflowSeats!.Select(s => new Tile(s.X, s.Y)));
        var agent = assigned ? Agent("occupant", 1, 9) : Agent("nearby", 4, 3);
        if (assigned) agent = agent with { Pose = agent.Pose with { OverflowSeatId = "sofa-seat", WorkLocation = "overflow" } };
        view = Assert.Single(animator.Update(room, [agent], Epoch.AddMilliseconds((step + 1) * 125)));
        Assert.Null(view.FurnitureId); Assert.NotEqual("napping", view.Action);
        OnFloor(room, view, blocked);
    }

    [Fact]
    public void MonitorAndOwnershipUpdatesPreserveRoutesAndThePetSchedule()
    {
        using var pack = new Pack("pet-shiba");
        var desk = new Furniture("desk", 4, 4, 2, 1, Id: "desk", AssetId: "test-desk");
        var room = Room([desk]); var unchanged = new PetAnimator(pack.Assets); var changing = new PetAnimator(pack.Assets);
        for (var step = 0; step < 600; step++)
        {
            var state = step % 2 == 0 ? "working" : "screensaver";
            var runtime = room with { Furniture = [desk with { AgentId = "owner:" + step, State = state, MonitorState = state }] };
            var time = Epoch.AddMilliseconds(step * 125);
            var actual = changing.Update(runtime, [], time);
            Assert.Equal(unchanged.Update(room, [], time), actual);
            var repeated = changing.Update(runtime with { Furniture = runtime.Furniture.ToArray() }, [], time);
            Assert.Same(actual, repeated);
            Assert.Same(actual[0], repeated[0]);
            Assert.Same(actual[0].Sprite, repeated[0].Sprite);
        }
    }

    [Fact]
    public void RollingPetsUseRollFramesAndTheParrotStaysPerched()
    {
        using var rolling = new Pack("pet-hamster-ball"); using var perched = new Pack("pet-parrot");
        var mover = new PetAnimator(rolling.Assets); var parrot = new PetAnimator(perched.Assets); var room = Room();
        var initial = Assert.Single(parrot.Update(room, [], Epoch)); var walked = false;
        for (var step = 0; step < 600; step++)
        {
            var time = Epoch.AddMilliseconds(step * 125);
            var pet = Assert.Single(mover.Update(room, [], time));
            if (pet.Action == "walking") { walked = true; Assert.InRange(pet.Sprite.Column, 0, 2); }
            var bird = Assert.Single(parrot.Update(room, [Agent("boss", initial.X, initial.Y)], time));
            Assert.Equal(new Position(initial.X, initial.Y), new Position(bird.X, bird.Y));
            Assert.Null(bird.FollowingId); Assert.Null(bird.FurnitureId);
        }
        Assert.True(walked);
    }

    [Fact]
    public void PetUpdatesDoNotChangeTrackerAgentsOrSerializedState()
    {
        using var pack = new Pack("pet-shiba");
        var room = Room(); AgentView[] agents = [Agent("boss", 3, 3)];
        var snapshot = new StateSnapshot(StateJson.SchemaVersion, Epoch, 1, SpriteAtlas.Info, room, agents, []);
        var before = JsonSerializer.Serialize(snapshot, StateJson.Options); var animator = new PetAnimator(pack.Assets);
        for (var step = 0; step < 400; step++) animator.Update(room, agents, Epoch.AddMilliseconds(step * 125));
        Assert.Equal(before, JsonSerializer.Serialize(snapshot, StateJson.Options));
        Assert.Single(snapshot.Agents); Assert.DoesNotContain("\"pets\"", before); Assert.DoesNotContain("office-pet-", before);
    }
}
