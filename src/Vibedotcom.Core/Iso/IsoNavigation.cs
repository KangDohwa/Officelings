namespace Vibedotcom.Core.Iso;

/// Navigation uses grid cells; projection and sprite elevation never enter this graph.
public sealed class IsoNavigation
{
    private static readonly Tile[] Steps = [new(1, 0), new(-1, 0), new(0, 1), new(0, -1)];
    private readonly HashSet<Tile> blocked;
    public int Width { get; }
    public int Height { get; }
    public IsoNavigation(int width, int height, IEnumerable<Tile> blocked)
    {
        if (width is < 1 or > 128 || height is < 1 or > 128) throw new ArgumentException("Invalid grid dimensions.");
        Width = width; Height = height; this.blocked = blocked.ToHashSet();
    }
    public IsoNavigation WithBlocked(IEnumerable<Tile> cells) => new(Width, Height, blocked.Concat(cells));
    public bool Walkable(Tile tile) => tile.X >= 0 && tile.X < Width && tile.Y >= 0 && tile.Y < Height && !blocked.Contains(tile);
    public HashSet<Tile> Reachable(Tile start)
    {
        var result = new HashSet<Tile>(); if (!Walkable(start)) return result;
        var queue = new Queue<Tile>(); queue.Enqueue(start); result.Add(start);
        while (queue.TryDequeue(out var tile))
            foreach (var d in Steps)
            {
                var next = new Tile(tile.X + d.X, tile.Y + d.Y);
                if (Walkable(next) && result.Add(next)) queue.Enqueue(next);
            }
        return result;
    }
    /// Empty means unreachable or already at target. Check Walkable/Reachable when that distinction matters.
    public IReadOnlyList<Tile> FindPath(Tile start, Tile target)
    {
        if (!Walkable(start) || !Walkable(target) || start == target) return [];
        var open = new PriorityQueue<Tile, (int Cost, int Order)>(); var order = 0;
        var from = new Dictionary<Tile, Tile>(); var costs = new Dictionary<Tile, int> { [start] = 0 };
        open.Enqueue(start, (0, order++));
        while (open.TryDequeue(out var current, out _))
        {
            if (current == target)
            {
                var path = new List<Tile>(); for (var t = target; t != start; t = from[t]) path.Add(t);
                path.Reverse(); return path;
            }
            foreach (var d in Steps)
            {
                var next = new Tile(current.X + d.X, current.Y + d.Y); var cost = costs[current] + 1;
                if (!Walkable(next) || costs.TryGetValue(next, out var old) && old <= cost) continue;
                costs[next] = cost; from[next] = current;
                open.Enqueue(next, (cost + Math.Abs(next.X - target.X) + Math.Abs(next.Y - target.Y), order++));
            }
        }
        return [];
    }
}
