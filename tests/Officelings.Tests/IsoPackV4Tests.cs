using System.Text.Json.Nodes;
using Officelings.Core;
using Officelings.Core.Iso;
using Xunit;

namespace Officelings.Tests;

[Collection("Iso assets")]
public class IsoPackV4Tests
{
    private static readonly DateTimeOffset Epoch = DateTimeOffset.Parse("2026-10-04T00:00:00Z");
    private static AgentInfo Agent(string id) =>
        new(id, Provider.Codex, null, false, id, null, null, null, "synthetic", null, null, AgentState.Working, Epoch, Epoch, false);

    [Fact]
    public void EmbeddedAndLoosePacksContainEveryAuthoredDesignAndV3Decor()
    {
        var embedded = IsoPack.LoadDefault(); var loose = IsoPack.Load(Path.Combine(IsoPackTests.SourceRoot, "pack"));
        Assert.Equal(loose.Characters, embedded.Characters);
        Assert.Equal(embedded.Manifest.Sheets.Count(s => s.Kind == "character"), embedded.Characters.Count);
        Assert.Equal(10, embedded.Layout.WallSegments!.Count(w => w.HostId != null));
        Assert.All(embedded.Images, p => Assert.Equal(loose.Images[p.Key], p.Value));
        var scene = new IsoOffice(embedded, 42).Update([], Epoch, 0);
        Assert.Contains(scene.Items, i => i.SheetId == "iso-env-v3-decor" && i.Clip.StartsWith("large-window-day-h0-r/"));
        Assert.Contains(scene.Items, i => i.SheetId == "iso-env-v3-decor" && i.Clip.StartsWith("large-window-day-h1-r/"));
        Assert.DoesNotContain(scene.Items, i => i.Clip.StartsWith("door-nameplate-"));
    }

    [Fact]
    public void RosterUsesFlatUtf16HashAndProbesAllAvailableDesigns()
    {
        var pack = IsoPack.LoadDefault(); var used = new HashSet<string>();
        foreach (var id in new[] { "a", "b", "한글", "emoji-🧑" }.Concat(
            Enumerable.Range(0, pack.Characters.Count + 1).Select(i => $"roster:{i}")))
        {
            var start = (int)(SpriteAtlas.Hash(id) % pack.Characters.Count);
            var expected = Enumerable.Range(0, pack.Characters.Count).Select(k => pack.Characters[(start + k) % pack.Characters.Count])
                .FirstOrDefault(c => !used.Contains(c.Id)) ?? pack.Characters[start];
            var assigned = pack.Assign(Agent(id), used);
            Assert.Equal(expected, assigned); used.Add(assigned.Id);
        }
        Assert.Equal(pack.Characters.Count, used.Count);
        var agents = Enumerable.Range(0, 12).Select(i => Agent($"v4:{i:00}")).ToArray();
        var a = new IsoOffice(pack, 42); var b = new IsoOffice(pack, 42);
        IsoScene left = a.Update(agents, Epoch, 0), right = b.Update(agents.Reverse().ToArray(), Epoch, 0);
        for (var i = 0; i < 400; i++)
        { left = a.Update(agents, Epoch, .05); right = b.Update(agents.Reverse().ToArray(), Epoch, .05); }
        Assert.Equal(left.Items, right.Items);
        Assert.Equal(pack.Characters.Count, left.Items.Where(i => i.Layer == IsoLayer.Character)
            .Select(i => pack.Manifest.Sheets.FirstOrDefault(s => s.Blink?.SheetId == i.SheetId)?.Id ?? i.SheetId).Distinct().Count());
    }

    [Fact]
    public void DecorRejectsDoorOverlapDuplicateSlotsAndWrongAnchor()
    {
        var pack = IsoPack.LoadDefault(); var walls = pack.Layout.WallSegments!.ToArray();
        var index = Array.FindIndex(walls, w => w.HostId != null); var decor = walls[index];
        var door = walls.Single(w => w.AssetId == "iso/wall-open-door-l-v3");
        foreach (var replacement in new[] { decor with { HostId = door.Id, Edge = door.Edge }, decor with { Edge = new(99, 99) } })
        {
            var modified = walls.ToArray(); modified[index] = replacement;
            Assert.Throws<InvalidDataException>(() => IsoLayoutLoader.Validate(pack.Layout with { WallSegments = modified }, pack));
        }
        Assert.Throws<InvalidDataException>(() => IsoLayoutLoader.Validate(pack.Layout with
            { WallSegments = walls.Append(decor with { Id = "duplicate" }).ToArray() }, pack));
    }

    [Fact]
    public void SyntheticPetPoolSelectsAtMostTwoBySeedAndAvoidsFurniture()
    {
        // Extend the real pool with synthetic copies to exercise arbitrary pool sizes.
        var root = Path.Combine(IsoPackTests.SourceRoot, "../../../artifacts/iso-v4-pets-" + Guid.NewGuid().ToString("N"));
        root = Path.GetFullPath(root); Directory.CreateDirectory(Path.Combine(root, "sheets"));
        try
        {
            var source = Path.Combine(IsoPackTests.SourceRoot, "pack");
            foreach (var file in Directory.EnumerateFiles(Path.Combine(source, "sheets"))) File.Copy(file, Path.Combine(root, "sheets", Path.GetFileName(file)));
            File.Copy(Path.Combine(source, "layout.json"), Path.Combine(root, "layout.json"));
            var manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(source, "assets.json")))!;
            var pool = manifest["pets"]!.AsArray(); var cat = pool[0]!;
            foreach (var id in new[] { "iso/test-dog", "iso/test-rabbit", "iso/test-hamster" })
            { var pet = cat.DeepClone(); pet["id"] = id; pool.Add(pet); }
            File.WriteAllText(Path.Combine(root, "assets.json"), manifest.ToJsonString());
            var pack = IsoPack.Load(root); var a = new IsoOffice(pack, 42); var b = new IsoOffice(pack, 42);
            var selections = new HashSet<string>(); var sawWalk = false;
            for (var seed = 0; seed < 12; seed++)
                selections.Add(string.Join(",", new IsoOffice(pack, seed).Update([], Epoch, 0).Items.Where(i => i.Layer == IsoLayer.Pet).Select(i => i.InstanceId)));
            Assert.True(selections.Count > 1);
            var nav = IsoLayoutLoader.Navigation(a.Layout, pack);
            var seats = a.Layout.Furniture.Where(f => f.AssetId == "iso/desk").Select(f => IsoContacts.Desk(f.X, f.Y, f.Rotation).Approach).ToHashSet();
            for (var i = 0; i < 900; i++)
            {
                var scene = a.Update([], Epoch, .1); Assert.Equal(scene.Items, b.Update([], Epoch, .1).Items);
                var pets = scene.Items.Where(p => p.Layer == IsoLayer.Pet).ToArray(); Assert.Equal(2, pets.Length);
                foreach (var pet in pets)
                {
                    var frame = pack.Frame(pet); var tile = IsoGeometry.Pick(pet.X + frame.AnchorX - a.Camera.X, pet.Y + frame.AnchorY - a.Camera.Y);
                    Assert.True(nav.Walkable(tile)); Assert.DoesNotContain(tile, seats); sawWalk |= pet.Clip.StartsWith("walk/");
                }
            }
            Assert.True(sawWalk); a.PetsEnabled = false;
            Assert.DoesNotContain(a.Update([], Epoch, 0).Items, i => i.Layer == IsoLayer.Pet);
            manifest["pets"] = new JsonArray();
            File.WriteAllText(Path.Combine(root, "assets.json"), manifest.ToJsonString());
            Assert.DoesNotContain(new IsoOffice(IsoPack.Load(root), 42).Update([], Epoch, 0).Items, i => i.Layer == IsoLayer.Pet);
        }
        finally { Directory.Delete(root, true); }
    }
}
