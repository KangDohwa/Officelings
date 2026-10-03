using System.Text.Json;

namespace Vibedotcom.Core;

/// <summary>A WPF-free editor. Commands replace immutable layout snapshots; Save rejects invalid drafts.</summary>
public sealed class OfficeEditor
{
    private readonly AssetPack assets;
    private readonly Stack<OfficeLayout> undo = new();
    private readonly Stack<OfficeLayout> redo = new();
    private static readonly string[] RequiredZones = ["workspace", "pantry", "lounge", "door"];

    public OfficeEditor(AssetPack assets, OfficeLayout layout)
    {
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(layout);
        this.assets = assets;
        Layout = Freeze(layout);
        try { Layout = Freeze(LayoutLoader.Prepare(Layout, assets, validate: false)); }
        catch (InvalidDataException) { /* Preserve invalid drafts so Validate can report every error. */ }
    }

    public OfficeLayout Layout { get; private set; }
    public bool CanUndo => undo.Count != 0;
    public bool CanRedo => redo.Count != 0;

    public string Place(string assetId, Tile tile)
    {
        var definition = assets.Manifest.Furniture.FirstOrDefault(f => f.Id == assetId)
            ?? throw new ArgumentException($"Unknown furniture asset '{assetId}'. Select an asset from the active pack.", nameof(assetId));
        var variant = definition.Variants.OrderBy(v => v.State == "default" ? 0 : 1)
            .ThenBy(v => v.State, StringComparer.Ordinal).ThenBy(v => v.Rotation).First();
        var id = NewId("furniture");
        var furniture = new Furniture(definition.Kind, tile.X, tile.Y, variant.FootprintW, variant.FootprintH,
            Id: id, AssetId: definition.Id, Rotation: variant.Rotation, State: variant.State,
            DeskId: variant.Points.Any(p => p.Action == "typing") ? id : null);
        var placed = new List<Furniture> { furniture };
        if (CreateChair(furniture) is {} chair) placed.Add(chair);
        CheckPlacements(placed);
        Commit(Layout with { Furniture = Layout.Furniture.Concat(placed).ToArray() });
        return id;
    }

    public void Move(string id, Tile tile)
    {
        var furniture = Find(id);
        id = furniture.Id;
        if (furniture.X == tile.X && furniture.Y == tile.Y) return;
        var group = Group(furniture);
        var moved = group.Select(f => f with { X = f.X + tile.X - furniture.X, Y = f.Y + tile.Y - furniture.Y }).ToArray();
        var ids = group.Select(f => f.Id).ToHashSet();
        CheckPlacements(moved, ids);
        var seats = (Layout.OverflowSeats ?? []).Select(s => s.FurnitureId != null && ids.Contains(s.FurnitureId)
            ? s with { X = s.X + tile.X - furniture.X, Y = s.Y + tile.Y - furniture.Y } : s).ToArray();
        foreach (var seat in seats.Where(s => s.FurnitureId != null && ids.Contains(s.FurnitureId)))
            RequireTile(new(seat.X, seat.Y), $"Overflow seat '{seat.Id}'");
        Commit(Layout with { Furniture = Replace(moved), OverflowSeats = seats });
    }

    public void Rotate(string id)
    {
        var furniture = Find(id);
        id = furniture.Id;
        var definition = assets.Manifest.Furniture.FirstOrDefault(f => f.Id == (furniture.AssetId ?? furniture.Kind))
            ?? throw new InvalidOperationException($"Furniture '{id}' references an unknown asset.");
        var variants = definition.Variants.Where(v => v.State == furniture.State).OrderBy(v => v.Rotation).ToArray();
        if (variants.Length < 2)
            throw new InvalidOperationException($"Asset '{definition.Id}' has only authored rotation {furniture.Rotation} for state '{furniture.State}'. " +
                "Rotation requires another authored variant with its own footprint, blocked cells and interaction points.");
        var index = Array.FindIndex(variants, v => v.Rotation == furniture.Rotation);
        if (index < 0) throw new InvalidOperationException($"Furniture '{id}' has no authored variant for rotation {furniture.Rotation}.");
        var next = variants[(index + 1) % variants.Length];
        var delta = (next.Rotation - furniture.Rotation + 360) % 360;
        var rotated = furniture with { Rotation = next.Rotation, Width = next.FootprintW, Height = next.FootprintH,
            Seat = RotateSeat(furniture.Seat, furniture.Width, furniture.Height, delta) };
        var group = Group(furniture);
        var replacements = new List<Furniture> { rotated };
        foreach (var chair in group.Skip(1))
        {
            var chairVariant = assets.Manifest.Furniture.First(f => f.Id == (chair.AssetId ?? chair.Kind)).Variants
                .FirstOrDefault(v => v.State == chair.State && v.Rotation == next.Rotation)
                ?? throw new InvalidOperationException($"Linked chair '{chair.Id}' has no authored rotation {next.Rotation}; the desk and chair cannot rotate together.");
            replacements.Add(chair with { Rotation = next.Rotation, Width = chairVariant.FootprintW, Height = chairVariant.FootprintH,
                Seat = RotateSeat(chair.Seat, chair.Width, chair.Height, delta) });
        }
        CheckPlacements(replacements, group.Select(f => f.Id).ToHashSet());
        var seats=(Layout.OverflowSeats ?? []).Select(s=>
        {
            var host=group.FirstOrDefault(f=>f.Id==s.FurnitureId);
            if(host==null) return s;
            var point=RotateSeat(new(s.Id,s.X-host.X,s.Y-host.Y,"typing",s.Facing),host.Width,host.Height,delta)!;
            var seat=s with { X=host.X+point.X,Y=host.Y+point.Y,Facing=point.Facing };
            RequireTile(new(seat.X,seat.Y),$"Overflow seat '{seat.Id}'");return seat;
        }).ToArray();
        Commit(Layout with { Furniture = Replace(replacements),OverflowSeats=seats });
    }

    public void Delete(string id)
    {
        var ids = Group(Find(id)).Select(f => f.Id).ToHashSet();
        Commit(Layout with {
            Furniture = Layout.Furniture.Where(f => !ids.Contains(f.Id)).ToArray(),
            OverflowSeats = Layout.OverflowSeats?.Where(s => s.FurnitureId == null || !ids.Contains(s.FurnitureId)).ToArray()
        });
    }

    public string Duplicate(string id, Tile? target = null)
    {
        var furniture = Find(id);
        id = furniture.Id;
        var newId = NewId("furniture");
        var tile = target ?? new Tile(furniture.X + furniture.Width, furniture.Y);
        var duplicate = furniture with {
            Id = newId, X = tile.X, Y = tile.Y, AgentId = null,
            DeskId = furniture.DeskId != null || assets.Variant(furniture).Points.Any(p => p.Action == "typing") ? newId : null
        };
        var group = Group(furniture);
        var copies = new List<Furniture> { duplicate };
        var ids = new Dictionary<string, string> { [id] = newId };
        foreach (var chair in group.Skip(1))
        {
            var chairId = copies.Count == 1 ? newId + "-chair" : newId + "-chair-" + copies.Count;
            ids.Add(chair.Id, chairId);
            copies.Add(chair with { Id = chairId, DeskId = duplicate.DeskId ?? newId, AgentId = null,
                X = chair.X + tile.X - furniture.X, Y = chair.Y + tile.Y - furniture.Y });
        }
        CheckPlacements(copies);
        var seats = (Layout.OverflowSeats ?? []).ToList();
        foreach (var seat in seats.Where(s => s.FurnitureId != null && ids.ContainsKey(s.FurnitureId)).ToArray())
        {
            var copy = seat with { Id = NewId("overflow"), FurnitureId = ids[seat.FurnitureId!],
                X = seat.X + tile.X - furniture.X, Y = seat.Y + tile.Y - furniture.Y };
            RequireTile(new(copy.X, copy.Y), $"Overflow seat '{copy.Id}'");
            seats.Add(copy);
        }
        Commit(Layout with { Furniture = Layout.Furniture.Concat(copies).ToArray(), OverflowSeats = seats });
        return newId;
    }

    public void Paint(Tile from, Tile to, int tileId, string shape = "brush")
    {
        if (!assets.Manifest.TileTypes.Any(t => t.Id == tileId))
            throw new ArgumentException($"Unknown tile ID {tileId}. Select a tile from the active pack.", nameof(tileId));
        if (shape is not ("brush" or "rect" or "fill"))
            throw new ArgumentException("Paint shape must be 'brush', 'rect' or 'fill'.", nameof(shape));
        RequireTile(from, "Paint start");
        if (shape != "fill") RequireTile(to, "Paint end");
        var tiles = TileArray();
        var changed = false;
        void PaintCell(Tile cell)
        {
            var i = cell.Y * Layout.WidthTiles + cell.X;
            if (tiles[i] == tileId) return;
            tiles[i] = tileId;
            changed = true;
        }
        if (shape == "rect")
        {
            for (var y = Math.Min(from.Y, to.Y); y <= Math.Max(from.Y, to.Y); y++)
                for (var x = Math.Min(from.X, to.X); x <= Math.Max(from.X, to.X); x++) PaintCell(new(x, y));
        }
        else if (shape == "fill")
        {
            var sourceId = tiles[from.Y * Layout.WidthTiles + from.X];
            if (sourceId == tileId) return;
            var pending = new Queue<Tile>();
            PaintCell(from);
            pending.Enqueue(from);
            while (pending.TryDequeue(out var cell))
                foreach (var neighbor in Neighbors(cell))
                    if (Inside(Layout, neighbor) && tiles[neighbor.Y * Layout.WidthTiles + neighbor.X] == sourceId)
                    { PaintCell(neighbor); pending.Enqueue(neighbor); }
        }
        else
        {
            var x = from.X; var y = from.Y;
            var dx = Math.Abs(to.X - x); var dy = -Math.Abs(to.Y - y);
            var sx = x < to.X ? 1 : -1; var sy = y < to.Y ? 1 : -1;
            var error = dx + dy;
            while (true)
            {
                PaintCell(new(x, y));
                if (x == to.X && y == to.Y) break;
                var twice = 2 * error;
                if (twice >= dy) { error += dy; x += sx; }
                if (twice <= dx) { error += dx; y += sy; }
            }
        }
        if (changed) Commit(Layout with { Tiles = tiles });
    }

    public void SetZone(string name, Tile from, Tile to)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Zone name cannot be empty.", nameof(name));
        RequireTile(from, $"Zone '{name}' start");
        RequireTile(to, $"Zone '{name}' end");
        var previous = (Layout.Zones ?? []).FirstOrDefault(z => z.Name == name);
        var zone = new Zone(name, Math.Min(from.X, to.X), Math.Min(from.Y, to.Y),
            Math.Abs(to.X - from.X) + 1, Math.Abs(to.Y - from.Y) + 1, previous?.Points);
        foreach (var point in zone.Points ?? [])
            if (point.X < 0 || point.Y < 0 || point.X >= zone.Width || point.Y >= zone.Height)
                throw new InvalidOperationException($"Zone '{name}' would exclude interaction point '{point.Id}'. Enlarge the zone to include its local ({point.X},{point.Y}) point.");
        var door = name == "door" ? new Tile(zone.X, zone.Y) : Layout.Door;
        if (zone == previous && door == Layout.Door) return;
        var zones = (Layout.Zones ?? []).Where(z => z.Name != name).Append(zone).ToArray();
        Commit(Layout with { Zones = zones, Door = door });
    }

    public void SetDoor(Tile tile) => SetZone("door", tile, tile);

    public void SetOverflowSeat(Tile tile, bool enabled = true)
    {
        if (enabled) RequireTile(tile, "Overflow seat");
        var seats = (Layout.OverflowSeats ?? []).ToList();
        var present = seats.Any(s => s.X == tile.X && s.Y == tile.Y);
        if (enabled == present) return;
        if (enabled) seats.Add(new(NewId("overflow"), tile.X, tile.Y, "up"));
        else seats.RemoveAll(s => s.X == tile.X && s.Y == tile.Y);
        Commit(Layout with { OverflowSeats = seats });
    }

    /// <summary>Accepts a world tile and stores the seat relative to the selected desk.</summary>
    public void SetDeskSeat(string id, Tile tile)
    {
        var furniture = Find(id);
        id = furniture.Id;
        var authored = assets.Variant(furniture with { Seat = null });
        var point = authored.Points.FirstOrDefault(p => p.Action == "typing")
            ?? throw new InvalidOperationException($"Furniture '{id}' has no authored typing seat. Select a desk.");
        RequireTile(tile, $"Desk '{id}' seat");
        var seat = (furniture.Seat ?? point) with { X = tile.X - furniture.X, Y = tile.Y - furniture.Y };
        if (seat == furniture.Seat) return;
        Commit(Layout with { Furniture = Replace(furniture with { Seat = seat, DeskId = furniture.DeskId ?? id }) });
    }

    public void Resize(int width, int height)
    {
        if (width is < 8 or > 128 || height is < 8 or > 128)
            throw new ArgumentOutOfRangeException(nameof(width), "Office width and height must each be between 8 and 128 tiles.");
        if (width == Layout.WidthTiles && height == Layout.HeightTiles) return;
        var resized = Layout with { WidthTiles = width, HeightTiles = height };
        var errors = BoundsErrors(resized);
        if (errors.Count != 0) throw new InvalidOperationException(ErrorMessage("Resize would put objects outside the office. Move them or enlarge the requested bounds:", errors));
        var source = TileArray();
        var floor = assets.Manifest.TileTypes.First(t => t.Kind == "floor").Id;
        var tiles = Enumerable.Repeat(floor, width * height).ToArray();
        for (var y = 0; y < Math.Min(height, Layout.HeightTiles); y++)
            Array.Copy(source, y * Layout.WidthTiles, tiles, y * width, Math.Min(width, Layout.WidthTiles));
        Commit(resized with { Tiles = tiles });
    }

    public bool Undo()
    {
        if (!undo.TryPop(out var previous)) return false;
        redo.Push(Layout);
        Layout = previous;
        return true;
    }

    public bool Redo()
    {
        if (!redo.TryPop(out var next)) return false;
        undo.Push(Layout);
        Layout = next;
        return true;
    }

    public IReadOnlyList<string> Validate()
    {
        var layout = Layout;
        var errors = BoundsErrors(layout);
        if (layout.Version != 1) errors.Add($"Layout version {layout.Version} is unsupported; use version 1.");
        if (layout.WidthTiles is < 8 or > 128 || layout.HeightTiles is < 8 or > 128)
            errors.Add("Office width and height must each be between 8 and 128 tiles.");
        if (layout.TileSize != 16) errors.Add("Tile size must be 16 pixels.");
        var furnitureList = layout.Furniture ?? [];
        if (layout.Furniture == null) errors.Add("Supply a furniture list; an empty list is allowed.");
        if (furnitureList.Count > 1024) errors.Add("Remove furniture to stay within the 1024 instance limit.");
        var zones = layout.Zones ?? [];
        foreach (var name in RequiredZones)
            if (zones.Count(z => z.Name == name) != 1) errors.Add($"Create exactly one '{name}' zone.");
        foreach (var duplicate in zones.GroupBy(z => z.Name).Where(g => g.Count() > 1))
            errors.Add($"Zone name '{duplicate.Key}' is repeated; give zones unique names.");
        foreach (var zone in zones)
        {
            if (string.IsNullOrWhiteSpace(zone.Name)) errors.Add("Give every zone a nonempty name.");
            if (zone.Name == "door" && !Contains(zone, layout.Door))
                errors.Add("The door zone must contain the entrance tile; use SetDoor to move both together.");
            foreach (var point in zone.Points ?? [])
                if (point.X < 0 || point.Y < 0 || point.X >= zone.Width || point.Y >= zone.Height)
                    errors.Add($"Zone '{zone.Name}' excludes interaction point '{point.Id}'; enlarge the zone.");
        }
        foreach (var duplicate in furnitureList.GroupBy(f => f.Id).Where(g => string.IsNullOrWhiteSpace(g.Key) || g.Count() > 1))
            errors.Add($"Furniture instance ID '{duplicate.Key}' must be nonempty and unique.");

        var blocked = new HashSet<Tile>();
        var dimensionsValid = layout.WidthTiles is >= 8 and <= 128 && layout.HeightTiles is >= 8 and <= 128;
        var tilesValid = dimensionsValid && layout.Tiles?.Count == layout.WidthTiles * layout.HeightTiles;
        if (!tilesValid) errors.Add("Supply exactly width × height tile IDs in row-major order.");
        var tileTypes = assets.Manifest.TileTypes.ToDictionary(t => t.Id);
        if (layout.Tiles != null && dimensionsValid)
        {
            foreach (var missing in layout.Tiles.Where(id => !tileTypes.ContainsKey(id)).Distinct())
                errors.Add($"Unknown tile ID {missing}; replace it with a tile from the active pack.");
            for (var i = 0; i < Math.Min(layout.Tiles.Count, layout.WidthTiles * layout.HeightTiles); i++)
                if (!tileTypes.TryGetValue(layout.Tiles[i], out var type) || type.Kind != "floor")
                    blocked.Add(new(i % layout.WidthTiles, i / layout.WidthTiles));
        }
        var occupied = new Dictionary<Tile, string>();
        var overlaps = new HashSet<(string, string)>();
        var points = new List<(Tile Tile, string Label, bool Seat)>();
        var deskIds = new List<string>();
        foreach (var furniture in furnitureList)
        {
            FurnitureVariant variant;
            try { variant = assets.Variant(furniture); }
            catch (InvalidDataException e) { errors.Add($"Furniture '{furniture.Id}': {e.Message}. Select an authored asset/rotation/state."); continue; }
            if (furniture.Width != variant.FootprintW || furniture.Height != variant.FootprintH)
                errors.Add($"Furniture '{furniture.Id}' must use authored footprint {variant.FootprintW}×{variant.FootprintH} for rotation {furniture.Rotation}.");
            if (furniture.Kind == "desk" && !variant.Points.Any(p => p.Action == "typing"))
                errors.Add($"Desk '{furniture.Id}' requires an authored typing seat; choose a desk asset with seat information.");
            if (furniture.Seat is {} seat && (string.IsNullOrWhiteSpace(seat.Id) || seat.Action != "typing" ||
                !assets.Variant(furniture with { Seat = null }).Points.Any(p => p.Action == "typing")))
                errors.Add($"Furniture '{furniture.Id}' seat override requires a nonempty ID, an authored typing seat and action 'typing'.");
            if (variant.Points.Any(p => p.Action == "typing")) deskIds.Add(furniture.DeskId ?? furniture.Id);
            foreach (var cell in LayoutLoader.Cells(furniture with { Width = variant.FootprintW, Height = variant.FootprintH }, variant).Distinct())
            {
                if (occupied.TryGetValue(cell, out var owner) && overlaps.Add((owner, furniture.Id)))
                    errors.Add($"Furniture '{furniture.Id}' overlaps '{owner}' at blocked tile ({cell.X},{cell.Y}); move or delete one instance.");
                occupied.TryAdd(cell, furniture.Id);
                blocked.Add(cell);
            }
            foreach (var point in variant.Points)
            {
                if (!FacingValid(point.Facing)) errors.Add($"Furniture '{furniture.Id}' point '{point.Id}' needs facing up, down, left or right.");
                points.Add((new(furniture.X + point.X, furniture.Y + point.Y), $"Furniture '{furniture.Id}' {point.Action} point '{point.Id}'", point.Action == "typing"));
            }
        }
        foreach (var duplicate in deskIds.GroupBy(id => id).Where(g => string.IsNullOrWhiteSpace(g.Key) || g.Count() > 1))
            errors.Add($"Desk ID '{duplicate.Key}' must be nonempty and unique.");
        foreach (var zone in zones)
            foreach (var point in zone.Points ?? [])
            {
                if (!FacingValid(point.Facing)) errors.Add($"Zone '{zone.Name}' point '{point.Id}' needs facing up, down, left or right.");
                points.Add((new(zone.X + point.X, zone.Y + point.Y), $"Zone '{zone.Name}' point '{point.Id}'", point.Action == "typing"));
            }
        var overflow = layout.OverflowSeats ?? [];
        if (overflow.Count > 1024) errors.Add("Remove overflow seats to stay within the 1024 seat limit.");
        foreach (var duplicate in overflow.GroupBy(s => s.Id).Where(g => string.IsNullOrWhiteSpace(g.Key) || g.Count() > 1))
            errors.Add($"Overflow seat ID '{duplicate.Key}' must be nonempty and unique.");
        foreach (var seat in overflow)
        {
            if (!FacingValid(seat.Facing)) errors.Add($"Overflow seat '{seat.Id}' needs facing up, down, left or right.");
            if (seat.FurnitureId != null && !furnitureList.Any(f => f.Id == seat.FurnitureId))
                errors.Add($"Overflow seat '{seat.Id}' references missing furniture '{seat.FurnitureId}'; remove or relink the seat.");
            points.Add((new(seat.X, seat.Y), $"Overflow seat '{seat.Id}'", true));
        }
        foreach (var duplicate in points.Where(p => p.Seat).GroupBy(p => p.Tile).Where(g => g.Count() > 1))
            errors.Add($"Seats share tile ({duplicate.Key.X},{duplicate.Key.Y}): {string.Join(", ", duplicate.Select(p => p.Label))}; move a seat.");
        var doorWalkable = dimensionsValid && tilesValid && LayoutLoader.Walkable(layout, layout.Door, blocked);
        if (!doorWalkable) errors.Add($"Entrance ({layout.Door.X},{layout.Door.Y}) must be an in-bounds floor tile without blocking furniture; move the door or clear its tile.");
        var reachable = new Dictionary<Tile, bool>();
        foreach (var point in points)
        {
            if (!Inside(layout, point.Tile) || blocked.Contains(point.Tile))
                errors.Add($"{point.Label} at ({point.Tile.X},{point.Tile.Y}) must be on an in-bounds unblocked floor tile.");
            else if (doorWalkable)
            {
                if (!reachable.TryGetValue(point.Tile, out var found))
                    reachable[point.Tile] = found = point.Tile == layout.Door || OfficeAnimator.FindPath(layout, layout.Door, point.Tile, blocked).Count != 0;
                if (!found) errors.Add($"{point.Label} at ({point.Tile.X},{point.Tile.Y}) is unreachable from the door; paint a floor passage or move blocking furniture.");
            }
        }
        if (layout.AutoDesks is {} auto && (auto.X < 0 || auto.Y < 3 || auto.SpacingX < 1 || auto.SpacingY < 1 ||
            auto.MaxColumns is < 1 or > 16 || auto.MinColumns < 1 || auto.MinColumns > auto.MaxColumns ||
            auto.Capacity is < 0 or > 1024 ||
            !assets.Manifest.Furniture.Any(f => f.Id == auto.AssetId && f.Variants.Any(v => v.Rotation == 0 && v.State == "default" && v.Points.Any(p => p.Action == "typing")))))
            errors.Add("Automatic desks require an authored default/rotation-0 typing asset, a nonnegative X, Y ≥ 3, positive spacing, capacity 0–1024 and 1 ≤ minColumns ≤ maxColumns ≤ 16.");
        if (layout.AutoDesks != null && errors.Count == 0)
            try { LayoutLoader.Prepare(layout, assets); }
            catch (InvalidDataException e) { errors.Add("Legacy automatic desk grid cannot be loaded: " + e.Message); }
        return errors.AsReadOnly();
    }

    public void Save(string path)
    {
        var errors = Validate();
        if (errors.Count != 0) throw new InvalidDataException(ErrorMessage("Office layout cannot be saved. Fix these errors:", errors));
        var destination = StateJson.OutputPath(path);
        var persisted = Layout with { Furniture = Layout.Furniture.Select(f => f with { AgentId = null }).ToArray() };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(persisted, StateJson.Options);
        if (bytes.Length > 4 * 1024 * 1024) throw new InvalidDataException("Layout exceeds the loader's 4 MiB limit; remove excess furniture or interaction points.");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { stream.Write(bytes); stream.Flush(true); }
            if (File.Exists(destination)) File.Replace(temporary, destination, destination + ".bak");
            else File.Move(temporary, destination);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public void ResetDefault()
    {
        var defaults = LayoutLoader.Load(assets, null);
        if (JsonSerializer.Serialize(defaults, StateJson.Options) != JsonSerializer.Serialize(Layout, StateJson.Options)) Commit(defaults);
    }

    private Furniture Find(string id)
    {
        var furniture = Layout.Furniture.FirstOrDefault(f => f.Id == id)
            ?? throw new ArgumentException($"Unknown furniture instance '{id}'. Select an existing instance.", nameof(id));
        if (furniture.Kind == "chair")
        {
            var desk = Layout.Furniture.FirstOrDefault(f => f.Id != furniture.Id &&
                (furniture.Id == f.Id + "-chair" || furniture.DeskId != null && furniture.DeskId == (f.DeskId ?? f.Id)) &&
                assets.Variant(f).Points.Any(p => p.Action == "typing"));
            if (desk != null) return desk;
        }
        return furniture;
    }

    private Furniture[] Group(Furniture furniture) => new[] { furniture }.Concat(Layout.Furniture.Where(f =>
        f.Id != furniture.Id && f.Kind == "chair" && (f.Id == furniture.Id + "-chair" ||
            f.DeskId != null && f.DeskId == (furniture.DeskId ?? furniture.Id)))).ToArray();

    private Furniture? CreateChair(Furniture desk)
    {
        if (desk.AssetId != "cozy-desk") return null;
        var definition = assets.Manifest.Furniture.FirstOrDefault(f => f.Id == "cozy-chair-front");
        if (definition == null) return null;
        var variant = definition.Variants.FirstOrDefault(v => v.Rotation == desk.Rotation && v.State == "default")
            ?? throw new InvalidOperationException($"Asset 'cozy-chair-front' requires an authored rotation {desk.Rotation} to accompany this desk.");
        return new(definition.Kind, desk.X, desk.Y, variant.FootprintW, variant.FootprintH,
            Id: desk.Id + "-chair", AssetId: definition.Id, Rotation: variant.Rotation, State: variant.State, DeskId: desk.DeskId ?? desk.Id);
    }

    private Furniture[] Replace(Furniture furniture) => Layout.Furniture.Select(f => f.Id == furniture.Id ? furniture : f).ToArray();

    private Furniture[] Replace(IReadOnlyList<Furniture> furniture)
    {
        var replacements = furniture.ToDictionary(f => f.Id);
        return Layout.Furniture.Select(f => replacements.GetValueOrDefault(f.Id, f)).ToArray();
    }

    private void CheckPlacements(IReadOnlyList<Furniture> furniture, IReadOnlySet<string>? ignored = null)
    {
        var occupied = new Dictionary<Tile, string>();
        foreach (var other in Layout.Furniture.Where(f => ignored?.Contains(f.Id) != true))
            foreach (var cell in LayoutLoader.Cells(other, assets.Variant(other)))
                occupied.TryAdd(cell, other.Id);
        foreach (var item in furniture)
        {
            var variant = assets.Variant(item);
            if (!RectangleInside(Layout, item.X, item.Y, variant.FootprintW, variant.FootprintH))
                throw new InvalidOperationException($"Furniture '{item.Id}' footprint {variant.FootprintW}×{variant.FootprintH} at ({item.X},{item.Y}) exceeds the office bounds; move it or explicitly Resize first.");
            foreach (var point in variant.Points)
                RequireTile(new(item.X + point.X, item.Y + point.Y), $"Furniture '{item.Id}' point '{point.Id}'");
            foreach (var cell in LayoutLoader.Cells(item, variant).Distinct())
            {
                if (occupied.TryGetValue(cell, out var owner))
                    throw new InvalidOperationException($"Furniture '{item.Id}' overlaps '{owner}' at blocked tile ({cell.X},{cell.Y}); choose a free position.");
                occupied.Add(cell, item.Id);
            }
        }
    }

    private List<string> BoundsErrors(OfficeLayout layout)
    {
        var errors = new List<string>();
        void Check(Tile tile, string label)
        { if (!Inside(layout, tile)) errors.Add($"{label} at ({tile.X},{tile.Y}) exceeds {layout.WidthTiles}×{layout.HeightTiles} office bounds."); }
        Check(layout.Door, "Door");
        foreach (var furniture in layout.Furniture ?? [])
        {
            if (!RectangleInside(layout, furniture.X, furniture.Y, furniture.Width, furniture.Height))
                errors.Add($"Furniture '{furniture.Id}' footprint at ({furniture.X},{furniture.Y}) exceeds {layout.WidthTiles}×{layout.HeightTiles} office bounds.");
            try
            {
                foreach (var point in assets.Variant(furniture).Points)
                    Check(new(furniture.X + point.X, furniture.Y + point.Y), $"Furniture '{furniture.Id}' point '{point.Id}'");
            }
            catch (InvalidDataException) { /* Validate reports unknown authored variants separately. */ }
        }
        foreach (var zone in layout.Zones ?? [])
        {
            if (!RectangleInside(layout, zone.X, zone.Y, zone.Width, zone.Height))
                errors.Add($"Zone '{zone.Name}' exceeds {layout.WidthTiles}×{layout.HeightTiles} office bounds.");
            foreach (var point in zone.Points ?? []) Check(new(zone.X + point.X, zone.Y + point.Y), $"Zone '{zone.Name}' point '{point.Id}'");
        }
        foreach (var seat in layout.OverflowSeats ?? []) Check(new(seat.X, seat.Y), $"Overflow seat '{seat.Id}'");
        if (layout.AutoDesks is {} auto) Check(new(auto.X, auto.Y), "Automatic desk origin");
        return errors;
    }

    private int[] TileArray()
    {
        if (Layout.WidthTiles is < 8 or > 128 || Layout.HeightTiles is < 8 or > 128 || Layout.Tiles?.Count != Layout.WidthTiles * Layout.HeightTiles)
            throw new InvalidOperationException("Editing tiles requires valid dimensions and exactly width × height tile IDs. Load a complete layout first.");
        return Layout.Tiles!.ToArray();
    }

    private void RequireTile(Tile tile, string label)
    {
        if (!Inside(Layout, tile)) throw new ArgumentOutOfRangeException(nameof(tile), $"{label} ({tile.X},{tile.Y}) is outside the office; explicitly Resize before using it.");
    }

    private void Commit(OfficeLayout next)
    {
        var snapshot = Freeze(next);
        undo.Push(Layout);
        Layout = snapshot;
        redo.Clear();
    }

    private static OfficeLayout Freeze(OfficeLayout layout) => layout with {
        Furniture = layout.Furniture == null ? null! : Array.AsReadOnly(layout.Furniture.ToArray()),
        Tiles = layout.Tiles == null ? null : Array.AsReadOnly(layout.Tiles.ToArray()),
        Zones = layout.Zones == null ? null : Array.AsReadOnly(layout.Zones.Select(z => z with {
            Points = z.Points == null ? null : Array.AsReadOnly(z.Points.ToArray()) }).ToArray()),
        OverflowSeats = layout.OverflowSeats == null ? null : Array.AsReadOnly(layout.OverflowSeats.ToArray())
    };

    private static string ErrorMessage(string message, IReadOnlyList<string> errors) =>
        message + Environment.NewLine + string.Join(Environment.NewLine, errors.Select(e => "- " + e));
    private static string NewId(string prefix) => prefix + "-" + Guid.NewGuid().ToString("N");
    private static bool Inside(OfficeLayout layout, Tile tile) => tile.X >= 0 && tile.Y >= 0 && tile.X < layout.WidthTiles && tile.Y < layout.HeightTiles;
    private static bool RectangleInside(OfficeLayout layout, int x, int y, int width, int height) =>
        x >= 0 && y >= 0 && width > 0 && height > 0 && (long)x + width <= layout.WidthTiles && (long)y + height <= layout.HeightTiles;
    private static bool Contains(Zone zone, Tile tile) => tile.X >= zone.X && tile.Y >= zone.Y && (long)tile.X < (long)zone.X + zone.Width && (long)tile.Y < (long)zone.Y + zone.Height;
    private static bool FacingValid(string facing) => facing is "up" or "down" or "left" or "right";
    private static InteractionPoint? RotateSeat(InteractionPoint? seat, int width, int height, int delta)
    {
        if (seat == null) return null;
        var x = seat.X; var y = seat.Y;
        var turns = delta / 90;
        for (var i = 0; i < turns; i++)
        {
            (x, y) = (height - 1 - y, x);
            (width, height) = (height, width);
        }
        string[] facings = ["up", "right", "down", "left"];
        var facing = Array.IndexOf(facings, seat.Facing);
        if (facing < 0) throw new InvalidOperationException("A custom seat must face up, down, left or right before rotation.");
        return seat with { X = x, Y = y, Facing = facings[(facing + turns) % 4] };
    }
    private static IEnumerable<Tile> Neighbors(Tile tile) => new Tile[] {
        new(tile.X - 1, tile.Y), new(tile.X + 1, tile.Y), new(tile.X, tile.Y - 1), new(tile.X, tile.Y + 1)
    };
}
