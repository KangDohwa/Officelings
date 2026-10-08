using Officelings.Core;
using Officelings.Core.Iso;
using Xunit;

namespace Officelings.Tests;

[Collection("Iso assets")]
public class IsoPackV5Tests
{
    [Fact]
    public void EveryProductionPetHasDeterministicGroundShadowAndWalksAroundFurniture()
    {
        var pack = IsoPack.LoadDefault(); var epoch = DateTimeOffset.Parse("2026-10-04T00:00:00Z");
        var pool = pack.Manifest.Pets!; var seen = new HashSet<string>();
        Assert.All(pool, pet => Assert.NotNull(pet.Shadow));
        for (var seed = 42; seed < 46; seed++)
        {
            var a = new IsoOffice(pack, seed); var b = new IsoOffice(pack, seed);
            var nav = IsoLayoutLoader.Navigation(a.Layout, pack);
            var reserved = a.Layout.Furniture.Where(f => f.AssetId == "iso/desk")
                .Select(f => IsoContacts.Desk(f.X, f.Y, f.Rotation).Approach).ToHashSet();
            for (var tick = 0; tick < 360; tick++)
            {
                var scene = a.Update([], epoch.AddSeconds(tick * .1), .1);
                Assert.Equal(scene.Items, b.Update([], epoch.AddSeconds(tick * .1), .1).Items);
                var pets = scene.Items.Where(i => i.Layer == IsoLayer.Pet).ToArray();
                Assert.Equal(Math.Min(2, pool.Count), pets.Length);
                foreach (var body in pets)
                {
                    var asset = pool.Single(p => p.SheetId == body.SheetId); seen.Add(asset.Id);
                    var frame = pack.Frame(body);
                    var tile = IsoGeometry.Pick(body.X + frame.AnchorX - a.Camera.X, body.Y + frame.AnchorY - a.Camera.Y);
                    Assert.True(nav.Walkable(tile)); Assert.DoesNotContain(tile, reserved);
                    var shadow = Assert.Single(scene.Items, i => i.Layer == IsoLayer.Shadow && i.SheetId == asset.Shadow!.SheetId);
                    var shadowFrame = pack.Frame(shadow);
                    Assert.Equal(body.X + frame.AnchorX, shadow.X + shadowFrame.AnchorX);
                    Assert.Equal(body.Y + frame.AnchorY, shadow.Y + shadowFrame.AnchorY);
                }
            }
        }
        Assert.Equal(pool.Select(p => p.Id).Order(), seen.Order());
    }
}
