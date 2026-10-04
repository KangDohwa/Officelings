namespace Vibedotcom.Core.Iso;

/// Twelve-seat expansion requested by the integration lane; all sixteen spec placements remain in place.
public static class IsoTwelveSeatFixture
{
    internal static IsoLayout OfficeLayout(IsoPack pack)
    {
        var original = IsoLayoutLoader.Default(pack);
        var expected = original.Furniture.Select(f => (f.AssetId, f.X, f.Y, f.Rotation));
        var actual = pack.Layout.Furniture.Select(f => (f.AssetId, f.X, f.Y, f.Rotation));
        return expected.ToHashSet().SetEquals(actual) ? Create(pack) : pack.Layout;
    }
    public static IsoLayout Create(IsoPack pack)
    {
        var layout = pack.Layout; var furniture = layout.Furniture.ToList();
        (int X, int Y, int Rotation)[] additional = [
            (2, 5, 180), (2, 7, 0), (7, 0, 180), (6, 4, 90), (3, 0, 90),
            (8, 6, 90), (1, 0, 0), (8, 4, 270), (9, 8, 180)
        ];
        for (var k = 0; k < additional.Length; k++)
        {
            var p = additional[k]; var id = "desk-" + (k + 4); var seat = IsoContacts.Desk(p.X, p.Y, p.Rotation);
            furniture.Add(new(id, "iso/desk", p.X, p.Y, p.Rotation));
            furniture.Add(new("chair-" + (k + 4), "iso/chair", seat.Approach.X, seat.Approach.Y, IsoGeometry.Rotation(seat.Facing), DeskId: id));
        }
        return IsoLayoutLoader.Validate(layout with { Furniture = furniture.AsReadOnly() }, pack);
    }
}
