using System.Text.Json;

namespace Vibedotcom.Core;

public static class LayoutLoader
{
    public static OfficeLayout Load(AssetPack assets, string? file, IList<string>? warnings = null)
    {
        if (file != null && File.Exists(file))
            try { return Prepare(AssetPack.Read<OfficeLayout>(file), assets); }
            catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException or NullReferenceException or OverflowException)
            { warnings?.Add("Layout rejected; using bundled default: " + e.Message); }
        foreach (var name in new[] { "layout.json", "fallback-layout.json" })
        {
            try { return Prepare(Default(name), assets); }
            catch (Exception e) when (e is InvalidDataException or ArgumentException or InvalidOperationException or NullReferenceException or OverflowException) { }
        }
        warnings?.Add("Asset geometry is incompatible with bundled layouts; using safe fallback pack and layout.");
        assets.UseSafeFallback();
        return Prepare(Default("fallback-layout.json"), assets);
    }
    private static OfficeLayout Default(string name)
    {
        using var resource = typeof(LayoutLoader).Assembly.GetManifestResourceStream("Vibedotcom.Core.Data." + name)!;
        return JsonSerializer.Deserialize<OfficeLayout>(resource, StateJson.Options)!;
    }
    internal static OfficeLayout Prepare(OfficeLayout layout, AssetPack assets, bool validate = true)
    {
        ValidateStructure(layout, assets);
        layout = WithTiles(MigrateAutoDesks(layout, assets), assets);
        layout = layout with { Furniture = layout.Furniture.Select(f =>
            assets.Variant(f).Points.Any(p => p.Action == "typing") ? f with { DeskId = f.DeskId ?? f.Id } : f).ToArray(),
            OverflowSeats = layout.OverflowSeats ?? [] };
        ValidateStructure(layout, assets);
        if (validate) Validate(layout, assets);
        return layout;
    }
    private static OfficeLayout WithTiles(OfficeLayout layout, AssetPack assets)
    {
        if (layout.Tiles != null) return layout;
        var floor = assets.Manifest.TileTypes.First(t => t.Kind == "floor").Id;
        var wall = assets.Manifest.TileTypes.First(t => t.Kind == "wall").Id;
        var art = layout.AutoDesks?.AssetId == "cozy-desk" || layout.Furniture.Any(f => f.AssetId == "cozy-desk");
        bool InZone(string name, int x, int y) => (layout.Zones ?? []).Any(z => z.Name == name &&
            x >= z.X && x < z.X + z.Width && y >= z.Y && y < z.Y + z.Height);
        var tiles = Enumerable.Range(0, layout.WidthTiles * layout.HeightTiles).Select(i =>
        {
            var x = i % layout.WidthTiles; var y = i / layout.WidthTiles;
            if (art)
            {
                var woodWall = x >= (layout.Zones ?? []).First(z => z.Name == "lounge").X && assets.Manifest.TileTypes.Any(t => t.Id == 7);
                if (y < 2) return woodWall ? 7 : wall;
                if (y == 2) return woodWall ? 8 : assets.Manifest.TileTypes.Any(t => t.Id == 6) ? 6 : wall;
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
        ValidateStructure(l, assets);
        var occupied = new HashSet<Tile>();
        foreach (var f in l.Furniture)
            foreach (var t in Cells(f, assets.Variant(f)))
                if (!occupied.Add(t)) throw new InvalidDataException("Overlapping blocked furniture: " + f.Id);
        var complete = WithTiles(l, assets); var blocked = Blocked(complete, assets);
        if (!Walkable(complete, l.Door, blocked)) throw new InvalidDataException("Door must be walkable.");
        var workSeats = new HashSet<Tile>();
        foreach (var f in l.Furniture.Where(f => assets.Variant(f).Points.Any(p => p.Action == "typing")))
        {
            var p = assets.Variant(f).Points.First(p => p.Action == "typing");
            if (!workSeats.Add(new(f.X + p.X, f.Y + p.Y))) throw new InvalidDataException("Duplicate work seat.");
        }
        foreach (var s in l.OverflowSeats ?? [])
            if (!workSeats.Add(new(s.X, s.Y))) throw new InvalidDataException("Overflow seats must be distinct from work seats.");
        var reachable = Reachable(complete, l.Door, blocked);
        foreach (var tile in assets.Points(complete).Select(p => p.Tile).Concat((l.OverflowSeats ?? []).Select(s => new Tile(s.X, s.Y))).Distinct())
            if (!reachable.Contains(tile))
                throw new InvalidDataException($"Unreachable interaction point at ({tile.X},{tile.Y}).");
    }
    private static void ValidateStructure(OfficeLayout l, AssetPack assets)
    {
        if (l == null || l.Version != 1 || l.WidthTiles is < 8 or > 128 || l.HeightTiles is < 8 or > 128 || l.TileSize != 16 || l.Furniture == null || l.Furniture.Count > 1024 ||
            l.Tiles != null && (l.Tiles.Count != l.WidthTiles * l.HeightTiles || l.Tiles.Any(id => !assets.Manifest.TileTypes.Any(t => t.Id == id))))
            throw new InvalidDataException("Unsupported layout dimensions/tiles/version.");
        if (l.Furniture.Any(f => f == null || string.IsNullOrWhiteSpace(f.Id)) || l.Furniture.Select(f => f.Id).Distinct().Count() != l.Furniture.Count)
            throw new InvalidDataException("Furniture instance IDs must be unique.");
        var zones = l.Zones ?? [];
        if (zones.Count > 256 || zones.Any(z => z == null || string.IsNullOrWhiteSpace(z.Name)) ||
            zones.Sum(z => (long)(z.Points?.Count ?? 0)) > 4096 ||
            new[] { "workspace", "pantry", "lounge", "door" }.Any(n => zones.Count(z => z.Name == n) != 1) ||
            zones.Select(z => z.Name).Distinct().Count() != zones.Count || zones.Any(z => z.X < 0 || z.Y < 0 || z.Width < 1 || z.Height < 1 || (long)z.X + z.Width > l.WidthTiles || (long)z.Y + z.Height > l.HeightTiles))
            throw new InvalidDataException("Required named zones missing/invalid.");
        if (zones.Any(z => z.Points?.Any(p => p == null || string.IsNullOrWhiteSpace(p.Id) || !Facing(p.Facing) ||
            p.X is < -128 or > 128 || p.Y is < -128 or > 128) == true) ||
            l.Furniture.Sum(f => (long)assets.Variant(f).Points.Count) + zones.Sum(z => (long)(z.Points?.Count ?? 0)) > 8192)
            throw new InvalidDataException("Invalid/excessive interaction points.");
        foreach (var f in l.Furniture)
        {
            var v = assets.Variant(f);
            if (f.Width != v.FootprintW || f.Height != v.FootprintH || f.X < 0 || f.Y < 0 || f.X > l.WidthTiles - f.Width || f.Y > l.HeightTiles - f.Height)
                throw new InvalidDataException("Furniture footprint mismatch: " + f.Id);
            if (f.Seat != null && (string.IsNullOrWhiteSpace(f.Seat.Id) || f.Seat.Action != "typing" || !Facing(f.Seat.Facing) ||
                f.Seat.X is < -128 or > 128 || f.Seat.Y is < -128 or > 128))
                throw new InvalidDataException("Invalid work seat override: " + f.Id);
        }
        var deskIds = l.Furniture.Where(f => assets.Variant(f).Points.Any(p => p.Action == "typing")).Select(f => f.DeskId ?? f.Id).ToArray();
        if (deskIds.Any(string.IsNullOrWhiteSpace) || deskIds.Distinct(StringComparer.Ordinal).Count() != deskIds.Length)
            throw new InvalidDataException("Desk IDs must be unique.");
        var overflow = l.OverflowSeats ?? [];
        if (overflow.Count > 1024 || overflow.Any(s => s == null) || overflow.Select(s => s.Id).Distinct(StringComparer.Ordinal).Count() != overflow.Count ||
            overflow.Any(s => s == null || string.IsNullOrWhiteSpace(s.Id) || !Facing(s.Facing) || s.X < 0 || s.Y < 0 || s.X >= l.WidthTiles || s.Y >= l.HeightTiles ||
                s.FurnitureId != null && !l.Furniture.Any(f => f.Id == s.FurnitureId)))
            throw new InvalidDataException("Invalid overflow seats.");
        if (l.Door.X < 0 || l.Door.Y < 0 || l.Door.X >= l.WidthTiles || l.Door.Y >= l.HeightTiles)
            throw new InvalidDataException("Door outside office.");
        if (l.AutoDesks is {} a && (a.X is < 0 or > 127 || a.Y is < 3 or > 127 || a.SpacingX is < 1 or > 128 || a.SpacingY is < 1 or > 128 || a.MaxColumns is < 1 or > 16 || a.MinColumns < 1 || a.MinColumns > a.MaxColumns ||
            a.Capacity is < 0 or > 1024 ||
            !assets.Manifest.Furniture.Any(f => f.Id == a.AssetId && f.Variants.Any(v => v.Rotation == 0 && v.State == "default" && v.Points.Any(p => p.Action == "typing")))))
            throw new InvalidDataException("Invalid automatic desk grid.");
    }
    private static bool Facing(string facing) => facing is "up" or "down" or "left" or "right";
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

    internal static HashSet<Tile> Reachable(OfficeLayout layout, Tile door, HashSet<Tile> blocked)
    {
        var result = new HashSet<Tile>();
        if (!Walkable(layout, door, blocked)) return result;
        var queue = new Queue<Tile>(); queue.Enqueue(door); result.Add(door);
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while (queue.TryDequeue(out var tile))
        {
            if (result.Count > 16384 || timer.ElapsedMilliseconds > 50)
                throw new InvalidDataException("Layout navigation exceeded its work/time budget.");
            foreach (var next in new Tile[] { new(tile.X + 1, tile.Y), new(tile.X - 1, tile.Y), new(tile.X, tile.Y + 1), new(tile.X, tile.Y - 1) })
                if (Walkable(layout, next, blocked) && result.Add(next)) queue.Enqueue(next);
        }
        return result;
    }

    private static OfficeLayout MigrateAutoDesks(OfficeLayout source, AssetPack assets)
    {
        if (source.AutoDesks is not {} auto) return source;
        // Freeze legacy grids at load. Membership never participates in this migration.
        var definition = assets.Manifest.Furniture.First(f => f.Id == auto.AssetId);
        var variant = definition.Variants.First(v => v.Rotation == 0 && v.State == "default");
        var count = Math.Max(0, auto.Capacity - source.Furniture.Count(f => assets.Variant(f).Points.Any(p => p.Action == "typing")));
        if (count == 0) return source with { AutoDesks = null };
        var columns = Math.Min(auto.MaxColumns, count);
        var workspace = source.Zones!.First(z => z.Name == "workspace");
        var edge = workspace.X + workspace.Width;
        var requiredShift = auto.Expand ? Math.Max(0L, auto.X + (long)(columns - 1) * auto.SpacingX + variant.FootprintW + 1 - edge) : 0;
        var seat = variant.Points.First(p => p.Action == "typing");
        var requiredHeight = auto.Expand ? Math.Max(source.HeightTiles, auto.Y + (long)((count - 1) / columns) * auto.SpacingY +
            Math.Max(variant.FootprintH, seat.Y + 1) + 2) : source.HeightTiles;
        var requiredWidth = source.WidthTiles + requiredShift;
        if (requiredWidth is < 8 or > 128 || requiredHeight is < 8 or > 128) throw new InvalidDataException("Legacy desk capacity exceeds office bounds.");
        var width = (int)requiredWidth; var height = (int)requiredHeight; var shift = (int)requiredShift;
        var bottomDoor = source.Door.Y >= source.HeightTiles - 3;
        var doorShift = bottomDoor ? height - source.HeightTiles : 0;
        var furniture = source.Furniture.Select(f => f with {
            X = f.X >= edge ? f.X + shift : f.X,
            Y = f.Kind == "door" ? f.Y + doorShift : f.Y }).ToList();
        var zones = source.Zones!.Select(z => z.Name == "workspace" ? z with { Width = z.Width + shift, Height = height - z.Y - 1 } :
            z.Name == "door" ? z with { X = z.X >= edge ? z.X + shift : z.X, Y = z.Y + doorShift } : z with {
                X = z.X >= edge ? z.X + shift : z.X,
                Height = z.Name == "partition" ? height - z.Y - 1 : z.Height }).ToArray();
        int[]? tiles = null;
        if (source.Tiles != null)
        {
            tiles = new int[width * height];
            for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
            {
                var oldX = x < edge ? x : x >= edge + shift ? x - shift : edge - 1;
                var oldY = y == height - 1 ? source.HeightTiles - 1 : Math.Min(y, source.HeightTiles - 2);
                tiles[y * width + x] = source.Tiles[oldY * source.WidthTiles + oldX];
            }
        }
        for (var slot = 0; slot < count; slot++)
        {
            var id = "auto-desk-" + slot;
            var desk = new Furniture(definition.Kind, auto.X + slot % columns * auto.SpacingX,
                auto.Y + slot / columns * auto.SpacingY, variant.FootprintW, variant.FootprintH,
                Id: id, AssetId: definition.Id, DeskId: id);
            furniture.Add(desk);
            var frontId = variant.ChairFrontId ?? (auto.AssetId == "cozy-desk" ? "cozy-chair-front" : "chair");
            var offset = variant.ChairOffset ?? new PixelPoint(0, 0);
            if (offset.X % 16 != 0 || offset.Y % 16 != 0) throw new InvalidDataException("Legacy desk chair offset must align to tiles.");
            foreach (var (chairId, suffix) in new[] { (variant.ChairBackId, "-chair-back"), (frontId, "-chair") })
            {
                var chair = assets.Manifest.Furniture.FirstOrDefault(f => f.Id == chairId)?.Variants
                    .FirstOrDefault(v => v.Rotation == 0 && v.State == "default");
                if (chair != null)
                    furniture.Add(new("chair", desk.X + (int)(offset.X / 16), desk.Y + (int)(offset.Y / 16), chair.FootprintW, chair.FootprintH,
                        Id: id + suffix, AssetId: chairId, DeskId: id));
            }
        }
        return source with { WidthTiles = width, HeightTiles = height,
            Door = new(source.Door.X >= edge ? source.Door.X + shift : source.Door.X, source.Door.Y + doorShift),
            Furniture = furniture, Zones = zones, Tiles = tiles, AutoDesks = null,
            OverflowSeats = source.OverflowSeats?.Select(s => s with { X = s.X >= edge ? s.X + shift : s.X }).ToArray() };
    }

    public static OfficeLayout Adapt(OfficeLayout source, AssetPack assets, IReadOnlyList<(string Id, int Slot)> agents)
    {
        // Compatibility entry point: assign only the already-authored desks.
        var result = Prepare(source with { AutoDesks = null }, assets);
        var furniture = result.Furniture.Select(f => f with { AgentId = null }).ToArray();
        var desks = furniture.Select((f, i) => (f, i)).Where(p => assets.Variant(p.f).Points.Any(p => p.Action == "typing")).ToArray();
        foreach (var a in agents.Where(a => a.Slot >= 0 && a.Slot < desks.Length))
            furniture[desks[a.Slot].i] = desks[a.Slot].f with { AgentId = a.Id };
        return result with { Furniture = furniture };
    }
}
