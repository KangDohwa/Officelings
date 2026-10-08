namespace Officelings.Core.Iso;

/// Production office: six back-to-back desk pairs, cross aisles and a separate left lounge.
/// The three-seat contact/rotation reference stays available through IsoLayoutLoader.Default.
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
        var layout = IsoLayoutLoader.Default(pack);
        var furniture = new List<IsoPlacement>();
        // Back edges meet intentionally within each pair; usable fronts face the aisles.
        // The three banks start at j=1,4,7. i=4 is a clear central aisle through j=1..6;
        // lower rows leave a lounge buffer and connect around both ends of each pair.
        (int X, int Y, int Rotation)[] desks = [
            (2, 1, 270), (2, 2, 90), (7, 1, 180), (8, 1, 0),
            (2, 4, 270), (2, 5, 90), (6, 4, 180), (7, 4, 0),
            (5, 7, 180), (6, 7, 0), (8, 7, 270), (8, 8, 90)
        ];
        for (var k = 0; k < desks.Length; k++)
        {
            var p = desks[k]; var id = "desk-" + (k + 1); var seat = IsoContacts.Desk(p.X, p.Y, p.Rotation);
            furniture.Add(new(id, "iso/desk", p.X, p.Y, p.Rotation));
            furniture.Add(new("chair-" + (k + 1), "iso/chair", seat.Approach.X, seat.Approach.Y, IsoGeometry.Rotation(seat.Facing), DeskId: id));
        }
        furniture.AddRange([
            new("sofa-1", "iso/sofa", 0, 9, 270), new("rug-1", "iso/rug", 0, 7, 90),
            new("tv-1", "iso/tv-cabinet", 0, 7), new("side-table-1", "iso/side-table", 2, 7),
            new("lamp-1", "iso/floor-lamp", 0, 3), new("plant-1", "iso/plant", 0, 2),
            new("bookshelf-1", "iso/shelf", 0, 0), new("coffee-1", "iso/coffee-machine", 1, 0, 90),
            new("water-1", "iso/water-cooler", 5, 0, 90), new("fridge-1", "iso/fridge", 0, 5)
        ]);
        var tiles = Enumerable.Range(0, 100).Select(k => k / 10 == 0 || k % 10 == 0 && k / 10 <= 2 ? 2 : 1).ToArray();
        return IsoLayoutLoader.Validate(layout with { Furniture = furniture.AsReadOnly(), Tiles = tiles,
            Zones = [new("workspace", 1, 1, 9, 9), new("pantry", 0, 0, 10, 1),
                new("lounge", 0, 6, 3, 4), new("door", 0, 4, 1, 1)] }, pack);
    }
}
