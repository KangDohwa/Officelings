using System.Text.Json;

namespace Vibedotcom.Core;

public static class LayoutLoader
{
    public static OfficeLayout Load(AssetPack assets, string? file, IList<string>? warnings = null)
    {
        var art = assets.Manifest.Furniture.Any(f => f.Id == "cozy-desk");
        using var resource = typeof(LayoutLoader).Assembly.GetManifestResourceStream(art
            ? "Vibedotcom.Core.Data.layout.json" : "Vibedotcom.Core.Data.fallback-layout.json")!;
        var fallback = JsonSerializer.Deserialize<OfficeLayout>(resource, StateJson.Options)!;
        if (file != null && File.Exists(file))
            try { var custom = AssetPack.Read<OfficeLayout>(file); Validate(custom, assets); return WithTiles(custom, assets); }
            catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException or NullReferenceException)
            { warnings?.Add("Layout rejected; using bundled default: " + e.Message); }
        return WithTiles(fallback, assets);
    }
    private static OfficeLayout WithTiles(OfficeLayout layout, AssetPack assets)
    {
        if (layout.Tiles != null) return layout;
        var floor = assets.Manifest.TileTypes.First(t => t.Kind == "floor").Id;
        var wall = assets.Manifest.TileTypes.First(t => t.Kind == "wall").Id;
        var art = layout.AutoDesks?.AssetId == "cozy-desk";
        bool InZone(string name, int x, int y) => (layout.Zones ?? []).Any(z => z.Name == name &&
            x >= z.X && x < z.X + z.Width && y >= z.Y && y < z.Y + z.Height);
        var tiles = Enumerable.Range(0, layout.WidthTiles * layout.HeightTiles).Select(i =>
        {
            var x = i % layout.WidthTiles; var y = i / layout.WidthTiles;
            if (art)
            {
                if (y < 2) return wall;
                if (y == 2) return assets.Manifest.TileTypes.Any(t => t.Id == 6) ? 6 : wall;
                if (x == 0 || x == layout.WidthTiles - 1 || y == layout.HeightTiles - 1 ||
                    InZone("partition", x, y) && !InZone("passage", x, y)) return 5;
                var floorId = InZone("pantry", x, y) ? 2 : InZone("lounge", x, y) ? 3 : 1;
                var quarter = y % 2 * 2 + x % 2;
                var nativeId = quarter == 0 ? floorId : 10 + (floorId - 1) * 3 + quarter - 1;
                return assets.Manifest.TileTypes.Any(t => t.Id == nativeId) ? nativeId : floorId;
            }
            if (y < 3 || x == 0 || x == layout.WidthTiles - 1 || y == layout.HeightTiles - 1) return wall;
            return (layout.Zones ?? []).Any(z => z.Name is "pantry" or "lounge" && x >= z.X && x < z.X + z.Width && y >= z.Y && y < z.Y + z.Height) ? 2 : floor;
        }).ToArray();
        return layout with { Tiles = tiles };
    }
    public static void Validate(OfficeLayout l, AssetPack assets)
    {
        if (l.Version != 1 || l.WidthTiles is < 8 or > 128 || l.HeightTiles is < 8 or > 128 || l.TileSize != 16 || l.Furniture.Count > 1024 ||
            l.Tiles != null && (l.Tiles.Count != l.WidthTiles * l.HeightTiles || l.Tiles.Any(id => !assets.Manifest.TileTypes.Any(t => t.Id == id))))
            throw new InvalidDataException("Unsupported layout dimensions/tiles/version.");
        if (l.Furniture.Any(f => string.IsNullOrWhiteSpace(f.Id)) || l.Furniture.Select(f => f.Id).Distinct().Count() != l.Furniture.Count)
            throw new InvalidDataException("Furniture instance IDs must be unique.");
        var zones = l.Zones ?? [];
        if (new[] { "workspace", "pantry", "lounge", "door" }.Any(n => zones.Count(z => z.Name == n) != 1) ||
            zones.Select(z => z.Name).Distinct().Count() != zones.Count || zones.Any(z => z.X < 0 || z.Y < 0 || z.Width < 1 || z.Height < 1 || z.X + z.Width > l.WidthTiles || z.Y + z.Height > l.HeightTiles))
            throw new InvalidDataException("Required named zones missing/invalid.");
        var occupied = new HashSet<Tile>();
        foreach (var f in l.Furniture)
        {
            var v = assets.Variant(f);
            if (f.Width != v.FootprintW || f.Height != v.FootprintH || f.X < 0 || f.Y < 0 || f.X + f.Width > l.WidthTiles || f.Y + f.Height > l.HeightTiles)
                throw new InvalidDataException("Furniture footprint mismatch: " + f.Id);
            foreach (var t in Cells(f, v)) if (!occupied.Add(t)) throw new InvalidDataException("Overlapping blocked furniture: " + f.Id);
        }
        var complete = WithTiles(l, assets); var blocked = Blocked(complete, assets);
        if (!Walkable(complete, l.Door, blocked)) throw new InvalidDataException("Door must be walkable.");
        foreach (var (tile, _) in assets.Points(complete))
            if (!Walkable(complete, tile, blocked) || tile != l.Door && OfficeAnimator.FindPath(complete, l.Door, tile, blocked).Count == 0)
                throw new InvalidDataException("Unreachable interaction point.");
        if (l.AutoDesks is {} a && (a.X < 0 || a.Y < 3 || a.SpacingX < 1 || a.SpacingY < 1 || a.MaxColumns is < 1 or > 16 || a.MinColumns < 1 || a.MinColumns > a.MaxColumns ||
            !assets.Manifest.Furniture.Any(f => f.Id == a.AssetId && f.Variants.Any(v => v.Rotation == 0 && v.State == "default" && v.Points.Any(p => p.Action == "typing")))))
            throw new InvalidDataException("Invalid automatic desk grid.");
    }
    public static IEnumerable<Tile> Cells(Furniture f, FurnitureVariant v) => (v.BlockedCells ??
        Enumerable.Range(0, f.Width * f.Height).Select(i => new Tile(i % f.Width, i / f.Width)).ToArray()).Select(t => new Tile(f.X + t.X, f.Y + t.Y));
    public static HashSet<Tile> Blocked(OfficeLayout l, AssetPack? assets = null)
    {
        var blocked = new HashSet<Tile>();
        if (l.Tiles != null)
            for (var i = 0; i < l.Tiles.Count; i++)
                if ((assets?.Manifest.TileTypes.FirstOrDefault(t => t.Id == l.Tiles[i])?.Kind ?? (l.Tiles[i] is 0 or 255 ? "wall" : "floor")) != "floor")
                    blocked.Add(new(i % l.WidthTiles, i / l.WidthTiles));
        foreach (var f in l.Furniture)
            if (assets != null) blocked.UnionWith(Cells(f, assets.Variant(f)));
            else for (var y = f.Y; y < f.Y + f.Height; y++) for (var x = f.X; x < f.X + f.Width; x++) blocked.Add(new(x, y));
        return blocked;
    }
    public static bool Walkable(OfficeLayout l, Tile t, HashSet<Tile> blocked) =>
        t.X >= 0 && t.Y >= 0 && t.X < l.WidthTiles && t.Y < l.HeightTiles && !blocked.Contains(t);

    public static OfficeLayout Adapt(OfficeLayout source, AssetPack assets, IReadOnlyList<(string Id, int Slot)> agents)
    {
        var auto = source.AutoDesks;
        var furniture = source.Furniture.ToList();
        var result = source;
        if (auto != null)
        {
            var capacity = agents.Count == 0 ? 0 : agents.Max(a => a.Slot) + 1;
            var columns = auto.AdaptiveColumns ? Math.Clamp(capacity <= 2 ? 1 : capacity <= 6 ? 2 : capacity <= 15 ? 3 : 4, auto.MinColumns, auto.MaxColumns) : auto.MaxColumns;
            var workspace = source.Zones!.First(z => z.Name == "workspace");
            var shift = auto.AdaptiveColumns && auto.Expand ? (columns - auto.MinColumns) * auto.SpacingX : 0;
            var height = auto.Expand ? Math.Min(128, Math.Max(source.HeightTiles, auto.Y + (int)Math.Ceiling((double)capacity / columns) * auto.SpacingY + 2)) : source.HeightTiles;
            var width = source.WidthTiles + shift;
            if (width > 128) throw new InvalidDataException("Office exceeds 128 tiles.");
            furniture = furniture.Select(f => f with {
                X = f.X >= workspace.X + workspace.Width ? f.X + shift : f.X,
                Y = f.Kind == "door" ? f.Y + height - source.HeightTiles : f.Y }).ToList();
            var zones = source.Zones!.Select(z => z.Name == "workspace" ? z with { Width = z.Width + shift, Height = height - z.Y - 1 } :
                z.Name == "door" ? z with { Y = z.Y + height - source.HeightTiles } : z with { X = z.X >= workspace.X + workspace.Width ? z.X + shift : z.X }).ToArray();
            var tiles = new int[width * height];
            var nativeFloors = assets.Manifest.Sheets.Any(s => s.Id == "floor-tiles");
            for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
            {
                var oldX = x >= workspace.X + workspace.Width + shift ? x - shift : Math.Min(x, workspace.X + workspace.Width - 1);
                var oldY = y == height - 1 ? source.HeightTiles - 1 : Math.Min(y, source.HeightTiles - 2);
                var tileId = source.Tiles![oldY * source.WidthTiles + oldX];
                if (nativeFloors && (tileId is >= 1 and <= 3 || tileId is >= 10 and <= 18))
                {
                    var floorId = tileId >= 10 ? 1 + (tileId - 10) / 3 : tileId;
                    var quarter = y % 2 * 2 + x % 2;
                    tileId = quarter == 0 ? floorId : 10 + (floorId - 1) * 3 + quarter - 1;
                }
                tiles[y * width + x] = tileId;
            }
            result = source with { WidthTiles = width, HeightTiles = height, Door = source.Door with { Y = source.Door.Y + height - source.HeightTiles }, Zones = zones, Tiles = tiles };
            var definition = assets.Manifest.Furniture.First(f => f.Id == auto.AssetId);
            var variant = definition.Variants.First(v => v.Rotation == 0 && v.State == "default");
            var fixedSeats = furniture.Where(f => assets.Variant(f).Points.Any(p => p.Action == "typing")).ToArray();
            for (var slot = 0; slot < capacity - fixedSeats.Length; slot++)
            {
                var desk = new Furniture(definition.Kind, auto.X + slot % columns * auto.SpacingX, auto.Y + slot / columns * auto.SpacingY,
                    variant.FootprintW, variant.FootprintH, null, "auto-desk-" + slot, definition.Id);
                if (desk.Y + desk.Height + 1 >= height || desk.X + desk.Width >= width ||
                    Cells(desk, variant).Any(t => Blocked(result with { Furniture = furniture }, assets).Contains(t)))
                    throw new InvalidDataException("Not enough unobstructed desk space.");
                furniture.Add(desk);
            }
        }
        var desks = furniture.Select((f, i) => (f, i)).Where(p => assets.Variant(p.f).Points.Any(p => p.Action == "typing")).ToArray();
        if (agents.Any(a => a.Slot >= desks.Length)) throw new InvalidDataException("Not enough seats; configure autoDesks or more desks.");
        foreach (var a in agents) furniture[desks[a.Slot].i] = desks[a.Slot].f with { AgentId = a.Id };
        result = result with { Furniture = furniture, AutoDesks = null };
        Validate(result, assets);
        return result;
    }
}
