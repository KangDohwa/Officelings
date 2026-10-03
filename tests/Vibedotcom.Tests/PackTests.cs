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
        Assert.Equal(office.Layout.Door.Y, office.Layout.Furniture.Single(f => f.Kind == "door").Y);
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
