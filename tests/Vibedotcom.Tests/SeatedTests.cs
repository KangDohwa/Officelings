using System.Text.Json;
using Vibedotcom.Core;
using Xunit;

namespace Vibedotcom.Tests;

public sealed class SeatedTests
{
    private static readonly DateTimeOffset Epoch = DateTimeOffset.Parse("2026-10-04T00:00:00Z");
    private static AgentInfo Agent(string id) => new(id, Provider.Codex, null, false, "FourteenLettersLong", null, null, null,
        "", null, null, AgentState.Working, Epoch, Epoch, false);

    [Fact]
    public void ArrivalTurnsThenLowersAndDepartureRaisesBeforeWalking()
    {
        var pack = AssetPack.LoadDefault(useUserPack: false);
        var office = new OfficeAnimator(42, pack);
        var agent = Enumerable.Range(0, 100).Select(i => Agent("sit-transition:" + i))
            .First(a => pack.SeatedVariant(pack.Assign(a, []), "up") != null);
        var phases = new List<string>();
        AgentView view = null!;
        var step = 0;
        for (; step < 1000; step++)
        {
            view = Assert.Single(office.Update([agent], Epoch.AddMilliseconds(step * 40), .04));
            if (view.Pose.SeatPhase is {} phase) phases.Add(phase);
            if (view.Pose.SeatPhase == "seated") break;
        }
        Assert.True(step < 1000);
        Assert.Contains("turn", phases); Assert.Contains("lower", phases);
        Assert.Equal("seated_back", view.Pose.SeatedVariant);
        var position = new Position(view.Pose.X, view.Pose.Y);
        var away = agent with { State = AgentState.Gone };
        var raiseFrames = new HashSet<int>();
        for (var i = 1; i <= 6; i++)
        {
            view = Assert.Single(office.Update([away], Epoch.AddMilliseconds((step + i) * 40), .04));
            Assert.Equal("raise", view.Pose.SeatPhase);
            Assert.Equal(position, new Position(view.Pose.X, view.Pose.Y));
            Assert.Equal("up", view.Pose.Facing);
            raiseFrames.Add(view.Pose.SeatPhaseFrame);
        }
        Assert.Equal(new[] { 0, 1 }, raiseFrames.Order().ToArray());
        view = Assert.Single(office.Update([away], Epoch.AddMilliseconds((step + 8) * 40), .04));
        Assert.Null(view.Pose.SeatPhase);
        Assert.NotEqual(position, new Position(view.Pose.X, view.Pose.Y));
    }

    [Fact]
    public void ReloadPreservesUnchangedSeatAndRaisesBeforeFollowingAMovedChair()
    {
        var pack = AssetPack.LoadDefault(useUserPack: false);
        var office = new OfficeAnimator(42, pack);
        var agent = Enumerable.Range(0, 100).Select(i => Agent("reload-seat:" + i))
            .First(a => pack.SeatedVariant(pack.Assign(a, []), "up") != null);
        AgentView view = null!;
        for (var i = 0; i <= 500; i++) view = Assert.Single(office.Update([agent], Epoch.AddMilliseconds(i * 100), .1));
        office.ReloadLayout(office.SourceLayout, true);
        view = Assert.Single(office.Update([agent], Epoch.AddSeconds(50), 0));
        Assert.Equal("seated", view.Pose.SeatPhase);
        var oldPosition = new Position(view.Pose.X, view.Pose.Y);
        var oldAnchor = view.Pose.SeatAnchor;
        var editor = new OfficeEditor(pack, office.SourceLayout);
        var desk = editor.Layout.Furniture.Single(f => f.DeskId == view.Pose.DeskId && f.Kind == "desk");
        editor.Move(desk.Id, new(desk.X + 1, desk.Y));
        office.ReloadLayout(editor.Layout, true);
        var frames = new HashSet<int>();
        for (var i = 0; i < 6; i++)
        {
            view = Assert.Single(office.Update([agent], Epoch.AddSeconds(50).AddMilliseconds(i * 40), .04));
            Assert.Equal("raise", view.Pose.SeatPhase);
            Assert.Equal(oldPosition, new Position(view.Pose.X, view.Pose.Y));
            Assert.Equal(oldAnchor, view.Pose.SeatAnchor);
            frames.Add(view.Pose.SeatPhaseFrame);
        }
        Assert.Equal(new[] { 0, 1 }, frames.Order().ToArray());
        view = Assert.Single(office.Update([agent], Epoch.AddSeconds(50.28), .04));
        Assert.Null(view.Pose.SeatPhase);
        Assert.NotEqual(oldPosition, new Position(view.Pose.X, view.Pose.Y));
    }

    [Fact]
    public void SeatedWaitingFreezesFrameZeroAndThinkingTapsSlowlyWithRareNod()
    {
        var pack = AssetPack.LoadDefault(useUserPack: false);
        var design = pack.Manifest.Characters.First(c => pack.SeatedVariant(c, "left") != null);
        var agent = Agent("seat-frame-policy");
        var waiting = agent with { State = AgentState.Waiting };
        var first = pack.SeatedFrame(waiting, design, "seated_left", "left", 0, false);
        var sheet = pack.Manifest.Sheets.Single(s => s.Id == design.SheetId);
        var row = sheet.Rows.Single(r => r.Name == "seated_left");
        for (var i = 0; i < 9000; i += 100)
            Assert.Equal(first, pack.SeatedFrame(waiting, design, "seated_left", "left", i, false));
        Assert.Equal(row.Column, first.Frame.Column);
        var thinking = agent with { State = AgentState.Thinking };
        var columns = Enumerable.Range(0, 80).Select(i => pack.SeatedFrame(thinking, design, "seated_left", "left", i * 100, false).Frame.Column).ToArray();
        Assert.Contains(row.Column + 1, columns); Assert.Contains(row.Column + 3, columns);
        Assert.Equal(6, columns.Count(c => c == row.Column + 2));
        var open = pack.SeatedFrame(thinking, design, "seated_left", "left", 6200, false);
        var closed = pack.SeatedFrame(thinking, design, "seated_left", "left", 6200, true);
        Assert.Equal(open.Frame.Column, closed.Frame.Column); Assert.Equal(open.Anchor, closed.Anchor);
        Assert.NotEqual(open.Frame.Row, closed.Frame.Row);
    }

    [Fact]
    public void OverflowPelvisUsesCushionsAndTwoSofaOccupantsKeepDistinctContacts()
    {
        var pack = AssetPack.LoadDefault(useUserPack: false); var office = new OfficeAnimator(42, pack);
        var agents = new List<AgentInfo>(); var used = new List<string>();
        for (var i = 0; agents.Count < 20; i++)
        {
            var a = Agent($"overflow-contact:{i:0000}"); var design = pack.Assign(a, used);
            if (new[] { "up", "left", "right" }.Any(f => pack.SeatedVariant(design, f) == null)) continue;
            agents.Add(a); used.Add(design.Id);
        }
        IReadOnlyList<AgentView> views = [];
        for (var i = 0; i < 800; i++) views = office.Update(agents, Epoch.AddMilliseconds(i * 100), .1);
        var contacts = new Dictionary<string, PixelPoint>();
        foreach (var seat in office.SourceLayout.OverflowSeats!.Where(s => s.Id.StartsWith("lounge-sofa") || s.Id == "lounge-armchair" || s.Id.StartsWith("pantry-")))
        {
            var p = views.Single(v => v.Pose.OverflowSeatId == seat.Id).Pose;
            Assert.Equal("seated", p.SeatPhase); Assert.NotNull(p.SeatAnchor);
            var host = office.SourceLayout.Furniture.Single(f => f.Id == seat.FurnitureId);
            var expectedY = host.AssetId switch { "cozy-sofa" => 18, "cozy-armchair" => 19, _ => -5 };
            Assert.Equal(host.Y * 16 + expectedY, p.SeatAnchor!.Y);
            contacts[seat.Id] = p.SeatAnchor;
        }
        Assert.Equal(16, contacts["lounge-sofa-2"].X - contacts["lounge-sofa-1"].X);
        Assert.Equal(contacts["lounge-sofa-1"].Y, contacts["lounge-sofa-2"].Y);
    }

    [Fact]
    public void SeatContactMatchesPublishedChairAndV5FieldsRemainAdditive()
    {
        var pack = AssetPack.LoadDefault(useUserPack: false);
        var office = new OfficeAnimator(42, pack);
        var agents = Enumerable.Range(0, 12).Select(i => Agent($"anchor:{i:00}")).ToArray();
        IReadOnlyList<AgentView> views = [];
        for (var i = 0; i < 500; i++) views = office.Update(agents, Epoch.AddMilliseconds(i * 100), .1);
        foreach (var view in views)
        {
            var p = view.Pose; var s = p.Sprite!;
            var design = pack.Manifest.Characters.Single(c => c.Id == p.CharacterId);
            if (pack.SeatedVariant(design, p.Facing) == null)
            { Assert.Null(p.SeatedVariant); Assert.Null(p.SeatPhase); continue; }
            Assert.Equal("seated", p.SeatPhase);
            var row = pack.Manifest.Sheets.Single(t => t.Id == s.SheetId).Rows.Single(r => r.Name == p.SeatedVariant);
            Assert.Equal(p.SeatAnchor!.X, (p.X + .5) * 16 - s.AnchorX * s.Scale + s.OffsetX + row.SeatAnchor!.X * s.Scale, 8);
            Assert.Equal(p.SeatAnchor.Y, (p.Y + 1) * 16 - s.AnchorY * s.Scale + s.OffsetY + row.SeatAnchor.Y * s.Scale, 8);
            Assert.True(System.Globalization.StringInfo.ParseCombiningCharacters(p.DisplayName!).Length <= 15);
        }
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(new StateSnapshot(StateJson.SchemaVersion, Epoch, 1,
            SpriteAtlas.Info, office.Layout, views, [], pack.Manifest, office.FurnitureSprites), StateJson.Options));
        Assert.Equal(5, json.RootElement.GetProperty("schemaVersion").GetInt32());
        var pose = json.RootElement.GetProperty("agents")[0].GetProperty("pose");
        foreach (var field in new[] { "x", "y", "desk", "action", "sprite", "deskId", "workLocation", "seatedVariant", "seatAnchor", "chairBackId", "chairFrontId", "seatPhase", "seatPhaseFrame" })
            Assert.True(pose.TryGetProperty(field, out _), field);
    }
}
