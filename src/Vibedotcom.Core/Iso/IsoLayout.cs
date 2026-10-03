using System.Text.Json;

namespace Vibedotcom.Core.Iso;

public sealed record IsoPlacement(string Id, string AssetId, int X, int Y, int Rotation = 0, string State = "default",
    string? DeskId = null, string SeatProfile = "front", string? Orientation = null);
public sealed record IsoWallSegment(string Id, string Face, IsoGridPoint Edge, string AssetId, string State = "default", string? HostId = null);
public sealed record IsoLayout(int WidthTiles, int HeightTiles, Tile Door, IReadOnlyList<IsoPlacement> Furniture,
    IReadOnlyList<int>? Tiles = null, IReadOnlyList<Zone>? Zones = null, IReadOnlyList<IsoWallSegment>? WallSegments = null,
    int Version = 1, string View = "iso", string Projection = "iso2to1", int TileSize = 16,
    string Cutaway = "rear-two-walls", IReadOnlyList<string>? RequiredCapabilities = null);

public static class IsoLayoutLoader
{
    public static IsoLayout Load(string file, IsoPack pack) => Validate(AssetPack.Read<IsoLayout>(file), pack);
    public static IsoLayout Default(IsoPack pack)
    {
        IsoPlacement[] furniture = [
            new("desk-1", "iso/desk", 2, 2, 180), new("chair-1", "iso/chair", 1, 3, 0, DeskId: "desk-1"),
            new("desk-2", "iso/desk", 5, 2, 270), new("chair-2", "iso/chair", 5, 1, 90, DeskId: "desk-2"),
            new("desk-3", "iso/desk", 4, 5, 90), new("chair-3", "iso/chair", 5, 6, 270, DeskId: "desk-3"),
            new("sofa", "iso/sofa", 6, 8, 270), new("plant", "iso/plant", 0, 6),
            new("shelf", "iso/shelf", 0, 0), new("tv", "iso/tv-cabinet", 0, 8),
            new("coffee", "iso/coffee-machine", 9, 1, 180), new("fridge", "iso/fridge", 9, 3, 180),
            new("water", "iso/water-cooler", 9, 5, 180), new("rug", "iso/rug", 6, 7, 90),
            new("side-table", "iso/side-table", 3, 8), new("lamp", "iso/floor-lamp", 4, 8)
        ];
        var tiles = Enumerable.Range(0, 100).Select(k => k % 10 >= 8 && k / 10 <= 5 ? 2 : 1).ToArray();
        var walls = new List<IsoWallSegment>();
        for (var k = 0; k < 10; k++)
        {
            walls.Add(new("rear-left-" + k, "left", new(-.5, k - .5), k == 4 ? "iso/wall-open-door-l" : "iso/wall-plain-l", k == 4 ? "open" : "default"));
            walls.Add(new("rear-right-" + k, "right", new(k - .5, -.5), k == 4 ? "iso/wall-window-r" : "iso/wall-plain-r"));
        }
        return Validate(new(10, 10, new(0, 4), furniture, tiles,
            [new("workspace", 1, 0, 7, 7), new("pantry", 8, 0, 2, 6), new("lounge", 0, 7, 9, 3), new("door", 0, 4, 1, 1)], walls), pack);
    }
    public static IEnumerable<Tile> Cells(IsoPlacement f, IsoFurnitureVariant v) =>
        (v.BlockedCells ?? Enumerable.Range(0, v.FootprintW * v.FootprintH).Select(k => new Tile(k % v.FootprintW, k / v.FootprintW)).ToArray())
        .Select(t => new Tile(f.X + t.X, f.Y + t.Y));
    public static IsoNavigation Navigation(IsoLayout layout, IsoPack pack)
    {
        var blocked = new HashSet<Tile>();
        if (layout.Tiles != null) for (var k = 0; k < layout.Tiles.Count; k++) if (layout.Tiles[k] is 0 or 255) blocked.Add(new(k % layout.WidthTiles, k / layout.WidthTiles));
        foreach (var f in layout.Furniture) blocked.UnionWith(Cells(f, pack.Variant(f.AssetId, f.Rotation, f.State)));
        return new(layout.WidthTiles, layout.HeightTiles, blocked);
    }
    public static IsoLayout Validate(IsoLayout layout, IsoPack pack)
    {
        if (layout.Version != 1 || layout.View != "iso" || layout.Projection != "iso2to1" || layout.TileSize != 16 ||
            layout.WidthTiles is < 8 or > 128 || layout.HeightTiles is < 8 or > 128 || layout.Furniture == null || layout.Furniture.Count > 1024 ||
            layout.Tiles != null && (layout.Tiles.Count != layout.WidthTiles * layout.HeightTiles || layout.Tiles.Any(t => t is not (0 or 1 or 2 or 255))) ||
            layout.RequiredCapabilities?.Any(c => c is not ("iso2to1" or "renderItemsV1")) == true)
            throw new InvalidDataException("Unsupported iso layout/capabilities.");
        var ids = new HashSet<string>(StringComparer.Ordinal); var occupied = new HashSet<Tile>(); var seats = new HashSet<Tile>();
        foreach (var f in layout.Furniture)
        {
            var v = pack.Variant(f.AssetId, f.Rotation, f.State);
            if (string.IsNullOrWhiteSpace(f.Id) || !ids.Add(f.Id) || f.X < 0 || f.Y < 0 ||
                f.X + v.FootprintW > layout.WidthTiles || f.Y + v.FootprintH > layout.HeightTiles ||
                f.Orientation != null && f.Orientation != v.Orientation || f.SeatProfile != "front") throw new InvalidDataException("Invalid iso placement: " + f.Id);
            foreach (var t in Cells(f, v)) if (!occupied.Add(t)) throw new InvalidDataException("Overlapping blocked iso furniture.");
            foreach (var s in v.SeatProfiles?.Where(s => s.Id == "front") ?? [])
                if (!seats.Add(new(f.X + s.Approach.X, f.Y + s.Approach.Y))) throw new InvalidDataException("Duplicate iso work seat.");
        }
        var nav = Navigation(layout, pack); var reachable = nav.Reachable(layout.Door);
        if (reachable.Count == 0) throw new InvalidDataException("Iso door must be walkable.");
        foreach (var f in layout.Furniture)
        {
            var v = pack.Variant(f.AssetId, f.Rotation, f.State);
            foreach (var p in v.Points ?? []) if (!reachable.Contains(new(f.X + p.X, f.Y + p.Y))) throw new InvalidDataException("Unreachable iso interaction: " + f.Id + "/" + p.Id);
            foreach (var s in v.SeatProfiles ?? []) if (!reachable.Contains(new(f.X + s.Approach.X, f.Y + s.Approach.Y))) throw new InvalidDataException("Unreachable iso seat: " + f.Id);
            if (f.DeskId != null && !layout.Furniture.Any(d => d.Id == f.DeskId)) throw new InvalidDataException("Unknown chair owner.");
        }
        var zones = layout.Zones ?? [];
        if (new[] { "workspace", "pantry", "lounge", "door" }.Any(name => zones.Count(z => z.Name == name) != 1) ||
            zones.Select(z => z.Name).Distinct().Count() != zones.Count || zones.Any(z => z.X < 0 || z.Y < 0 || z.Width <= 0 || z.Height <= 0 || z.X + z.Width > layout.WidthTiles || z.Y + z.Height > layout.HeightTiles))
            throw new InvalidDataException("Invalid/missing iso zones.");
        var wallIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var w in layout.WallSegments ?? [])
        {
            if (!wallIds.Add(w.Id) || w.Face is not ("left" or "right") || !double.IsFinite(w.Edge.I) || !double.IsFinite(w.Edge.J) ||
                !(pack.Manifest.WallModules ?? []).Any(m => m.Id == w.AssetId && m.Face == w.Face)) throw new InvalidDataException("Invalid iso wall.");
        }
        return layout;
    }
}
