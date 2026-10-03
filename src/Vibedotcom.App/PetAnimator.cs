using System;
using System.Collections.Generic;
using System.Linq;
using Vibedotcom.Core;

namespace Vibedotcom.App;

internal sealed class PetAnimator
{
    private sealed record NapSpot(string FurnitureId, Tile Approach, PixelPoint Cushion);
    private sealed class Pet(Sheet sheet)
    {
        public readonly Sheet Sheet = sheet;
        public readonly Random Random = new(unchecked((int)SpriteAtlas.Hash(sheet.Id)));
        public readonly Queue<Tile> Path = new();
        public readonly Dictionary<(string Action, string Facing), AnimationRow?> Clips = new();
        public PetView? View;
        public double X, Y;
        public bool Placed;
        public string Action = "idle", Facing = "down";
        public string? FollowingId;
        public DateTimeOffset RestUntil, FollowUntil, RefreshAt;
        public NapSpot? Nap;
    }

    private readonly AssetPack assets;
    private readonly Pet[] pets;
    private OfficeLayout? geometry;
    private readonly Dictionary<string, (Furniture Furniture, FurnitureVariant Variant)> furnitureGeometry = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> overflowHosts = new(StringComparer.Ordinal);
    private HashSet<Tile> layoutBlocked = [], blocked = [], reachable = [];
    private readonly HashSet<Tile> extraSeats = [], nextExtraSeats = [];
    private Tile[] floor = [];
    private NapSpot[] naps = [];
    private DateTimeOffset? lastUpdate;
    private IReadOnlyList<AgentView>? lastAgents;
    private bool wasEnabled;
    private IReadOnlyList<PetView> views = [];
    private readonly List<PetView> nextViews = new(2);

    public bool Enabled { get; set; } = true;
    public TimeSpan AnimationInterval { get; private set; }

    public PetAnimator(AssetPack assets)
    {
        this.assets = assets;
        var available = assets.Manifest.Sheets.Where(s => s.Id.StartsWith("pet-", StringComparison.Ordinal) &&
            assets.Images.ContainsKey(s.Id) && s.Rows.Any(r => r.Name == "idle")).OrderBy(s => s.Id, StringComparer.Ordinal).ToArray();
        var count = Math.Min(available.Length, 1 + (int)(SpriteAtlas.Hash(string.Join("|", available.Select(s => s.Id))) % 2));
        pets = available.OrderBy(s => SpriteAtlas.Hash("office:" + s.Id)).ThenBy(s => s.Id, StringComparer.Ordinal)
            .Take(count).Select(s => new Pet(s)).ToArray();
    }

    public IReadOnlyList<PetView> Update(OfficeLayout layout, IReadOnlyList<AgentView> agents, DateTimeOffset now)
    {
        var sameGeometry = SameGeometry(layout);
        var sameSeats = sameGeometry && SameReservations(agents);
        if (now == lastUpdate && Enabled == wasEnabled && sameGeometry && sameSeats && Same(agents, lastAgents) &&
            views.Count == pets.Count(p => assets.Images.ContainsKey(p.Sheet.Id))) return views;
        var seconds = lastUpdate is {} previous ? Math.Clamp((now - previous).TotalSeconds, 0, 1) : 0;
        lastUpdate = now; lastAgents = agents; wasEnabled = Enabled;
        AnimationInterval = TimeSpan.Zero;
        if (!Enabled || pets.Length == 0) return views = [];
        if (!sameGeometry || !sameSeats) RebuildNavigation(layout, agents);
        else geometry = layout;
        if (floor.Length == 0) return views = [];
        nextViews.Clear();
        foreach (var pet in pets)
        {
            if (!assets.Images.ContainsKey(pet.Sheet.Id)) continue;
            if (!pet.Placed)
            {
                var spawn = floor[(int)(SpriteAtlas.Hash(pet.Sheet.Id) % (uint)floor.Length)];
                pet.X = spawn.X; pet.Y = spawn.Y; pet.Placed = true;
                pet.RestUntil = now.AddSeconds(3 + SpriteAtlas.Hash(pet.Sheet.Id) % 5);
            }
            if (pet.Nap is {} reservedNap && HostOccupied(reservedNap, agents))
            {
                pet.Nap = null; StopRoute(pet);
                pet.RestUntil = now.AddSeconds(4);
            }
            if (pet.FollowingId != null)
            {
                AgentView? target = null;
                foreach (var agent in agents)
                    if (agent.Agent.Id == pet.FollowingId && Supervisor(agent)) { target = agent; break; }
                if (target == null || now >= pet.FollowUntil)
                {
                    pet.FollowingId = null; StopRoute(pet);
                    pet.RestUntil = now.AddSeconds(4);
                }
                else if (now >= pet.RefreshAt) Follow(pet, target, now);
            }
            else if (pet.Path.Count == 0 && now >= pet.RestUntil)
            {
                pet.Nap = null;
                ChooseActivity(pet, agents, now);
            }
            Move(pet, seconds);
            if (pet.Path.Count == 0 && pet.Action == "walking")
            {
                pet.Action = pet.Nap == null ? "idle" : "napping";
                pet.Facing = pet.Nap == null ? pet.Facing : "right";
                pet.RestUntil = now.AddSeconds(pet.Nap == null ? pet.Random.Next(3, 9) : pet.Random.Next(15, 26));
            }
            var clip = Clip(pet, pet.Action, pet.Facing) ?? Clip(pet, "idle", pet.Facing)!;
            if (clip.Direction != "any") pet.Facing = clip.Direction;
            var sprite = assets.Frame(pet.Sheet.Id, clip.Name, clip.Direction, now.ToUnixTimeMilliseconds());
            // Unadapted idle rows select a facing; their four cells are not a looping clip.
            if (clip.Name == "idle" && clip.Direction == "any" && clip.Frames == 4)
            {
                var column = clip.Column + (pet.Facing switch { "left" => 1, "right" => 2, "up" => 3, _ => 0 });
                sprite = sprite with { Column = column, X = column * pet.Sheet.CellW };
            }
            if (pet.Action == "napping" && pet.Nap is {} nap)
            {
                var contact = clip.SeatAnchor ?? new PixelPoint(16, 27);
                sprite = sprite with {
                    OffsetX = nap.Cushion.X - (pet.X + .5) * layout.TileSize + (sprite.AnchorX - contact.X) * sprite.Scale,
                    OffsetY = nap.Cushion.Y - (pet.Y + 1) * layout.TileSize + (sprite.AnchorY - contact.Y) * sprite.Scale };
            }
            var previousView = pet.View;
            if (previousView != null && previousView.X == pet.X && previousView.Y == pet.Y &&
                previousView.Action == pet.Action && previousView.Facing == pet.Facing && previousView.Sprite == sprite &&
                previousView.FollowingId == pet.FollowingId && previousView.FurnitureId == pet.Nap?.FurnitureId)
                nextViews.Add(previousView);
            else
                nextViews.Add(pet.View = new("office-" + pet.Sheet.Id, pet.Sheet.Id, pet.X, pet.Y, pet.Action, pet.Facing,
                    sprite, pet.FollowingId, pet.Nap?.FurnitureId));
        }
        if (views.Count != nextViews.Count || !views.SequenceEqual(nextViews)) views = nextViews.ToArray();
        if (views.Count != 0) AnimationInterval = TimeSpan.FromSeconds(views.Any(p => p.Action == "walking") ? 1d / 8 : 1d / 2);
        return views;
    }

    private static AnimationRow? Find(Sheet sheet, string name, string facing)
    {
        AnimationRow? first = null, any = null;
        foreach (var row in sheet.Rows)
            if (row.Name == name)
            {
                if (row.Direction == facing) return row;
                first ??= row;
                if (row.Direction == "any") any ??= row;
            }
        return any ?? first;
    }

    private static AnimationRow? Clip(Pet pet, string action, string facing)
    {
        var key = (action, facing);
        if (pet.Clips.TryGetValue(key, out var cached)) return cached;
        return pet.Clips[key] = action switch {
            "walking" => Find(pet.Sheet, "walk", facing) ?? Find(pet.Sheet, "roll", facing) ??
                Find(pet.Sheet, "walk_" + facing, facing) ?? Find(pet.Sheet, "roll_" + facing, facing),
            "napping" => Find(pet.Sheet, "sleep", facing) ?? Find(pet.Sheet, "napping", facing),
            _ => Find(pet.Sheet, action, facing) };
    }

    private static bool Supervisor(AgentView view) => !view.Agent.IsSubagent && view.Agent.State != AgentState.Gone &&
        view.Pose.Visible && view.Pose.Opacity > 0 && double.IsFinite(view.Pose.X) && double.IsFinite(view.Pose.Y);
    private static Tile At(Pet pet) => new((int)Math.Round(pet.X), (int)Math.Round(pet.Y));
    private static double Distance(Pet pet, double x, double y) => Math.Abs(pet.X - x) + Math.Abs(pet.Y - y);

    private bool HostOccupied(NapSpot nap, IReadOnlyList<AgentView> agents)
    {
        foreach (var agent in agents)
        {
            var pose = agent.Pose;
            if (!pose.Visible || pose.Opacity <= 0) continue;
            if (pose.OverflowSeatId != null && overflowHosts.TryGetValue(pose.OverflowSeatId, out var host) && host == nap.FurnitureId)
                return true;
            var x = pose.SeatAnchor?.X ?? (pose.X + .5) * geometry!.TileSize;
            var y = pose.SeatAnchor?.Y ?? (pose.Y + 1) * geometry!.TileSize - 8;
            if (Math.Abs(x - nap.Cushion.X) + Math.Abs(y - nap.Cushion.Y) <= geometry!.TileSize * 1.5 ||
                Math.Abs(pose.X - nap.Approach.X) + Math.Abs(pose.Y - nap.Approach.Y) < 1) return true;
        }
        return false;
    }

    private void ChooseActivity(Pet pet, IReadOnlyList<AgentView> agents, DateTimeOffset now)
    {
        pet.Action = "idle";
        pet.RestUntil = now.AddSeconds(pet.Random.Next(3, 9));
        if (Clip(pet, "walking", "down") == null)
        {
            pet.Action = pet.Random.Next(2) == 0 && Clip(pet, "head_bob", "down") != null ? "head_bob" : "sit";
            return;
        }
        var supervisors = agents.Where(a => Supervisor(a) && Distance(pet, a.Pose.X, a.Pose.Y) <= 8)
            .OrderBy(a => a.Agent.Id, StringComparer.Ordinal).ToArray();
        if (supervisors.Length != 0 && pet.Random.Next(3) == 0)
        {
            var target = supervisors[pet.Random.Next(supervisors.Length)];
            pet.FollowingId = target.Agent.Id; pet.FollowUntil = now.AddSeconds(pet.Random.Next(5, 11));
            Follow(pet, target, now);
            return;
        }
        if (Clip(pet, "napping", "right") != null && pet.Random.Next(4) == 0)
        {
            var free = naps.Where(n => !pets.Any(p => p != pet && p.Nap?.FurnitureId == n.FurnitureId) && !HostOccupied(n, agents))
                .OrderBy(n => Distance(pet, n.Approach.X, n.Approach.Y)).ToArray();
            if (free.Length != 0 && Route(pet, free[0].Approach))
            {
                pet.Nap = free[0]; pet.Action = "walking";
                return;
            }
        }
        var nearby = floor.Where(t => Distance(pet, t.X, t.Y) is > 0 and <= 4).ToArray();
        if (nearby.Length != 0 && Route(pet, nearby[pet.Random.Next(nearby.Length)], maxSteps: 8)) pet.Action = "walking";
    }

    private void Follow(Pet pet, AgentView target, DateTimeOffset now)
    {
        pet.RefreshAt = now.AddSeconds(.75);
        if (Distance(pet, target.Pose.X, target.Pose.Y) <= 2) { StopRoute(pet); return; }
        Tile? nearest = null; var distance = double.MaxValue;
        foreach (var tile in floor)
            if (Math.Abs(tile.X - target.Pose.X) + Math.Abs(tile.Y - target.Pose.Y) <= 2)
            {
                var candidate = Distance(pet, tile.X, tile.Y);
                if (candidate < distance) { distance = candidate; nearest = tile; }
            }
        if (nearest == null || !Route(pet, nearest.Value)) StopRoute(pet);
        else pet.Action = pet.Path.Count == 0 ? "idle" : "walking";
    }

    private bool Route(Pet pet, Tile target, int maxSteps = int.MaxValue)
    {
        // Finish the current cardinal segment before refreshing a moving route.
        // Replanning from a rounded, fractional position can cut a blocked corner.
        var start = pet.Path.TryPeek(out var next) ? next : At(pet);
        var path = OfficeAnimator.FindPath(geometry!, start, target, blocked);
        if (start != target && path.Count == 0 || path.Count > maxSteps) return false;
        pet.Path.Clear();
        if (pet.X != start.X || pet.Y != start.Y) pet.Path.Enqueue(start);
        foreach (var tile in path) pet.Path.Enqueue(tile);
        return true;
    }

    private static void StopRoute(Pet pet)
    {
        var inTransit = pet.Path.TryPeek(out var next) && (pet.X != Math.Round(pet.X) || pet.Y != Math.Round(pet.Y));
        pet.Path.Clear();
        if (inTransit) pet.Path.Enqueue(next);
        pet.Action = inTransit ? "walking" : "idle";
    }

    private static void Move(Pet pet, double seconds)
    {
        var distance = seconds * (pet.Sheet.Id.StartsWith("pet-capybara", StringComparison.Ordinal) ? .65 : 1.25);
        while (distance > 0 && pet.Path.TryPeek(out var next))
        {
            var dx = next.X - pet.X; var dy = next.Y - pet.Y; var length = Math.Abs(dx) + Math.Abs(dy);
            if (length == 0) { pet.Path.Dequeue(); continue; }
            pet.Facing = dx > 0 ? "right" : dx < 0 ? "left" : dy > 0 ? "down" : "up";
            if (length <= distance) { pet.X = next.X; pet.Y = next.Y; pet.Path.Dequeue(); distance -= length; }
            else { pet.X += dx / length * distance; pet.Y += dy / length * distance; break; }
        }
    }

    private static bool Same<T>(IReadOnlyList<T>? first, IReadOnlyList<T>? second) => ReferenceEquals(first, second) ||
        first != null && second != null && first.SequenceEqual(second);

    private bool SameReservations(IReadOnlyList<AgentView> agents)
    {
        nextExtraSeats.Clear();
        foreach (var agent in agents)
            if (!agent.Pose.Queued && agent.Pose.WorkLocation is "desk" or "overflow" && !layoutBlocked.Contains(agent.Pose.Desk))
                nextExtraSeats.Add(agent.Pose.Desk);
        return nextExtraSeats.SetEquals(extraSeats);
    }

    private bool SameGeometry(OfficeLayout layout)
    {
        if (ReferenceEquals(geometry, layout)) return true;
        if (geometry == null || layout.WidthTiles != geometry.WidthTiles || layout.HeightTiles != geometry.HeightTiles ||
            layout.TileSize != geometry.TileSize || layout.Door != geometry.Door || !Same(layout.Tiles, geometry.Tiles) ||
            !Same(layout.OverflowSeats, geometry.OverflowSeats) || layout.Furniture.Count != furnitureGeometry.Count) return false;
        var zones = layout.Zones ?? []; var oldZones = geometry.Zones ?? [];
        if (zones.Count != oldZones.Count) return false;
        for (var i = 0; i < zones.Count; i++)
            if (zones[i].X != oldZones[i].X || zones[i].Y != oldZones[i].Y || !Same(zones[i].Points, oldZones[i].Points)) return false;
        foreach (var f in layout.Furniture)
        {
            if (!furnitureGeometry.TryGetValue(f.Id, out var old) || f.X != old.Furniture.X || f.Y != old.Furniture.Y ||
                f.Width != old.Furniture.Width || f.Height != old.Furniture.Height || f.Kind != old.Furniture.Kind) return false;
            var variant = assets.Variant(f);
            if (!Same(variant.BlockedCells, old.Variant.BlockedCells) || !Same(variant.Points, old.Variant.Points) ||
                variant.ChairSeatAnchor != old.Variant.ChairSeatAnchor) return false;
        }
        return true;
    }

    private void RebuildNavigation(OfficeLayout layout, IReadOnlyList<AgentView> agents)
    {
        geometry = layout;
        furnitureGeometry.Clear();
        foreach (var f in layout.Furniture) furnitureGeometry[f.Id] = (f, assets.Variant(f));
        layoutBlocked = LayoutLoader.Blocked(layout, assets);
        layoutBlocked.UnionWith(assets.Points(layout).Where(p => p.Point.Action == "typing").Select(p => p.Tile));
        layoutBlocked.UnionWith((layout.OverflowSeats ?? []).Select(s => new Tile(s.X, s.Y)));
        SameReservations(agents);
        extraSeats.Clear(); extraSeats.UnionWith(nextExtraSeats);
        blocked = new(layoutBlocked); blocked.UnionWith(extraSeats);
        overflowHosts.Clear();
        foreach (var seat in layout.OverflowSeats ?? [])
            if (seat.FurnitureId != null) overflowHosts[seat.Id] = seat.FurnitureId;
        var walkable = Enumerable.Range(0, layout.WidthTiles * layout.HeightTiles).Select(i => new Tile(i % layout.WidthTiles, i / layout.WidthTiles))
            .Where(t => LayoutLoader.Walkable(layout, t, blocked)).ToArray();
        reachable = [];
        // Cache one connected floor component. Existing A* supplies every actual route.
        var queue = new Queue<Tile>();
        if (walkable.Length != 0)
        {
            var start = walkable.OrderBy(t => Math.Abs(t.X - layout.Door.X) + Math.Abs(t.Y - layout.Door.Y)).First();
            reachable.Add(start); queue.Enqueue(start);
        }
        while (queue.TryDequeue(out var tile))
            foreach (var next in new Tile[] { new(tile.X + 1, tile.Y), new(tile.X - 1, tile.Y), new(tile.X, tile.Y + 1), new(tile.X, tile.Y - 1) })
                if (LayoutLoader.Walkable(layout, next, blocked) && reachable.Add(next)) queue.Enqueue(next);
        floor = walkable.Where(reachable.Contains).ToArray();
        var spots = new List<NapSpot>();
        foreach (var f in layout.Furniture.Where(f => f.Kind is "sofa" or "bed" or "dog_bed" ||
            (f.AssetId ?? f.Kind).EndsWith("-bed", StringComparison.Ordinal)))
        {
            var variant = assets.Variant(f);
            var contact = variant.ChairSeatAnchor ?? new PixelPoint(f.Width * layout.TileSize / 2d, f.Height * layout.TileSize / 2d);
            foreach (var point in variant.Points.Where(p => p.Action is "napping" or "sleep" or "pet-napping"))
            {
                var authored = new Tile(f.X + point.X, f.Y + point.Y);
                Tile? approach = reachable.Contains(authored) ? authored : floor.Where(t =>
                        Math.Max(f.X - t.X, Math.Max(0, t.X - (f.X + f.Width - 1))) +
                        Math.Max(f.Y - t.Y, Math.Max(0, t.Y - (f.Y + f.Height - 1))) == 1)
                    .OrderBy(t => Math.Abs(t.X - authored.X) + Math.Abs(t.Y - authored.Y)).Select(t => (Tile?)t).FirstOrDefault();
                if (approach is {} tile) spots.Add(new(f.Id, tile, new(f.X * layout.TileSize + contact.X, f.Y * layout.TileSize + contact.Y)));
            }
        }
        naps = spots.ToArray();
        foreach (var pet in pets)
        {
            pet.Path.Clear(); pet.Nap = null; pet.FollowingId = null; pet.Action = "idle";
            if (!reachable.Contains(At(pet))) pet.Placed = false;
            else { pet.X = Math.Round(pet.X); pet.Y = Math.Round(pet.Y); }
        }
    }
}
