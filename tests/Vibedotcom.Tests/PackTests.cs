using System.Text.Json;
using Vibedotcom.Core;
using Xunit;

namespace Vibedotcom.Tests;

public sealed class PackTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-03T12:00:00Z");
    private static bool IsP5(CharacterDesign c) => new[] { "codex-", "claude-", "antigravity-" }
        .Any(prefix => c.Id.StartsWith(prefix, StringComparison.Ordinal));
    private static AgentInfo Agent(string id, Provider provider = Provider.Codex, bool worker = false) =>
        new(id, provider, worker ? "parent" : null, worker, id, null, null, null, "", null, null, AgentState.Working, Now, Now, false);

    [Fact]
    public void BundledRuntimePackLoadsRealArtAndAllProviderVariants()
    {
        var pack = AssetPack.LoadDefault(useUserPack: false);
        Assert.Empty(pack.Warnings);
        Assert.True(pack.Manifest.Characters.Count >= 36);
        Assert.Equal(36, pack.Manifest.Characters.Count(IsP5));
        Assert.All(pack.Manifest.Characters, c => {
            Assert.NotEqual("fallback", c.SheetId);
            Assert.Null(c.Provider); Assert.Null(c.Role);
            var sheet = Assert.Single(pack.Manifest.Sheets, s => s.Id == c.SheetId);
            Assert.Equal(1, sheet.Scale); Assert.Equal(new PixelPoint(16, 60), sheet.Anchor);
            Assert.Equal(4, sheet.Rows.Count(r => r.Name == "idle" && r.Frames == 1));
        });
        Assert.All(pack.Manifest.Sheets, s => Assert.Equal((s.Width, s.Height), AssetPack.PngSize(pack.Images[s.Id])));
        Assert.Contains(pack.Manifest.TileTypes, t => t.Id == 1 && t.SheetId != null);
        Assert.Contains(pack.Manifest.Furniture, f => f.Id == "cozy-desk");
    }

    [Fact]
    public void AssignmentUsesAllDesignsAndIsStableAcrossOrderProviderAndUpdates()
    {
        var pack = AssetPack.LoadDefault(useUserPack: false);
        var roster = pack.Manifest.Characters;
        var agents = Enumerable.Range(0, roster.Count).Select(i => Agent("agent:" + i, (Provider)(i % 3), i % 2 == 0)).ToArray();
        var first = new OfficeAnimator(42, pack).Update(agents, Now, 0);
        var secondOffice = new OfficeAnimator(42, pack);
        var second = secondOffice.Update(agents.Reverse().ToArray(), Now, 0);
        Assert.Equal(roster.Count, first.Select(v => v.Pose.CharacterId).Distinct().Count());
        Assert.All(second, v => Assert.Equal(first.Single(a => a.Agent.Id == v.Agent.Id).Pose.CharacterId, v.Pose.CharacterId));
        var survivors = secondOffice.Update(agents.Where((_, i) => i != 2).Reverse().ToArray(), Now.AddSeconds(1), .1);
        Assert.All(survivors, v => Assert.Equal(first.Single(a => a.Agent.Id == v.Agent.Id).Pose.CharacterId, v.Pose.CharacterId));
        var single = Agent("independent-id");
        var expected = roster[(int)(SpriteAtlas.Hash(single.Id) % roster.Count)].Id;
        foreach (var provider in Enum.GetValues<Provider>()) foreach (var worker in new[] { false, true })
            Assert.Equal(expected, pack.Assign(single with { Provider = provider, IsSubagent = worker }, []).Id);
    }

    [Fact]
    public void AuthoredFacingFrameRatesAnchorsAndNonLoopClipsArePreserved()
    {
        var pack = AssetPack.LoadDefault(useUserPack: false);
        var sheet = pack.Manifest.Characters.First(IsP5).SheetId;
        Assert.Equal(0, pack.Frame(sheet, "idle", "down", 9000).Column);
        Assert.Equal(1, pack.Frame(sheet, "idle", "left", 9000).Column);
        Assert.Equal(2, pack.Frame(sheet, "idle", "right", 9000).Column);
        Assert.Equal(3, pack.Frame(sheet, "idle", "up", 9000).Column);
        Assert.Equal(3, pack.Frame(sheet, "walk", "left", 500).Column);
        Assert.Equal(0, pack.Frame(sheet, "walk", "left", 600).Column);
        Assert.Equal(0, pack.Frame(sheet, "reporting", "any", 0, 3).Column);
        Assert.Equal(2, pack.Frame(sheet, "reporting", "any", 9999, 3).Column);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(6)] [InlineData(9)] [InlineData(30)] [InlineData(48)]
    public void DenseDefaultRoomsKeepSeatsAndActivitiesReachableAsMembershipChanges(int count)
    {
        var pack = AssetPack.LoadDefault(useUserPack: false);
        var office = new OfficeAnimator(42, pack);
        var agents = Enumerable.Range(0, count).Select(i => Agent("office:" + i)).ToArray();
        var views = office.Update(agents, Now, 0);
        LayoutLoader.Validate(office.Layout, pack);
        Assert.Equal(count, office.Layout.Furniture.Count(f => f.AgentId != null));
        Assert.Equal(count, views.Select(v => v.Pose.Desk).Distinct().Count());
        Assert.Contains(1, office.Layout.Tiles!); Assert.Contains(2, office.Layout.Tiles!); Assert.Contains(3, office.Layout.Tiles!);
        var blocked = LayoutLoader.Blocked(office.Layout, pack);
        Assert.All(views, v => Assert.NotEmpty(OfficeAnimator.FindPath(office.Layout, office.Layout.Door, v.Pose.Desk, blocked)));
        Assert.Equal(office.Layout.Door.Y - 1, office.Layout.Furniture.Single(f => f.Kind == "door").Y);
        Assert.Equal(count + 1, office.Layout.Furniture.Count(f => f.Kind == "desk"));
        Assert.Equal(3, office.Layout.Door.Y);
        Assert.All(views, v => Assert.Equal("up", pack.Variant(office.Layout.Furniture.Single(f => f.AgentId == v.Agent.Id)).Points.Single(p => p.Action == "typing").Facing));
    }

    [Fact]
    public void StillCharactersBlinkBrieflyAtIndependentThreeToSixSecondIntervals()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Vibedotcom-blink-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var fallback = new AssetPack();
            var desk = fallback.Manifest.Furniture.Single(f => f.Id == "desk");
            desk = desk with { Variants = desk.Variants.Select(v => v with {
                Points = v.Points.Select(p => p with { Facing = "down" }).ToArray() }).ToArray() };
            var rows = new AnimationRow[] { new("idle","down",0,1,1,Loop:false), new("idle","up",0,1,1,Loop:false),
                new("walk","any",0,2,4,Column:1), new("typing","down",0,1,1,Column:3,Loop:false),
                new("blink-typing","down",0,1,1,Column:6,Loop:false) };
            var manifest = new AssetManifest(1,[new("people","people.png",16,24,rows,new(8,24))],
                [desk],[new("blink-person","people")],[]);
            File.WriteAllBytes(Path.Combine(directory,"people.png"),SpriteAtlas.CreatePng());
            File.WriteAllText(Path.Combine(directory,"assets.json"),JsonSerializer.Serialize(manifest,StateJson.Options));
            var pack = new AssetPack(directory); Assert.Empty(pack.Warnings);
            var agent = Agent("blink-timing"); var design = pack.Manifest.Characters[0];
            Assert.Equal(pack.CharacterFrame(agent,design,"walking","down",200),pack.CharacterFrame(agent,design,"walking","down",200,blink:true));
            Assert.Equal(pack.CharacterFrame(agent,design,"thinking","up",200),pack.CharacterFrame(agent,design,"thinking","up",200,blink:true));
            var office = new OfficeAnimator(42,pack);
            DateTimeOffset? closedSince = null, previousBlink = null;
            var closures = 0;
            for (var i=0;i<2000;i++)
            {
                var time=Now.AddMilliseconds(i*50);
                var view=Assert.Single(office.Update([agent],time,.05));
                var closed=view.Pose.Sprite!.Column==6;
                if (view.Pose.Action is "walking" or "entering" or "exiting") Assert.False(closed);
                if (closed && closedSince==null)
                {
                    closedSince=time; closures++;
                    if (previousBlink!=null) Assert.InRange((time-previousBlink.Value).TotalSeconds,3,6.05);
                    previousBlink=time;
                }
                else if (!closed && closedSince!=null)
                {
                    Assert.InRange((time-closedSince.Value).TotalMilliseconds,100,200);
                    closedSince=null;
                }
            }
            Assert.True(closures>=10);
        }
        finally { Directory.Delete(directory,true); }
    }

    [Fact]
    public void SmallRosterShrinksTheRoomAndKeepsOneSpareDesk()
    {
        var pack=AssetPack.LoadDefault(useUserPack:false); var office=new OfficeAnimator(42,pack);
        var agents=Enumerable.Range(0,24).Select(i=>Agent("shrink:"+i)).ToArray();
        office.Update(agents,Now,0); var large=office.Layout;
        office.Update([agents[0]],Now.AddSeconds(1),0);
        Assert.True(office.Layout.WidthTiles<large.WidthTiles);
        Assert.True(office.Layout.HeightTiles<large.HeightTiles);
        Assert.Equal(2,office.Layout.Furniture.Count(f=>f.Kind=="desk"));
        Assert.Equal(large.Door,office.Layout.Door);
    }

    [Fact]
    public void AssetsJsonDirectoryLoadsTheSameImagesAsEmbeddedPack()
    {
        var pack = AssetPack.LoadDefault(useUserPack: false);
        var directory = Path.Combine(Path.GetTempPath(), "Vibedotcom-pack-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var manifest = pack.Manifest with { Sheets = pack.Manifest.Sheets.Where(s => s.Id != "fallback").ToArray() };
            foreach (var sheet in manifest.Sheets)
            {
                var file = Path.Combine(directory, sheet.File);
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                File.WriteAllBytes(file, pack.Images[sheet.Id]);
            }
            File.WriteAllText(Path.Combine(directory, "assets.json"), JsonSerializer.Serialize(manifest, StateJson.Options));
            var loaded = new AssetPack(directory);
            Assert.Empty(loaded.Warnings);
            Assert.Equal(pack.Manifest.Characters, loaded.Manifest.Characters);
            Assert.All(manifest.Sheets, s => Assert.Equal(s.Sha256, loaded.Manifest.Sheets.Single(t => t.Id == s.Id).Sha256));
        }
        finally { Directory.Delete(directory, true); }
    }
}
