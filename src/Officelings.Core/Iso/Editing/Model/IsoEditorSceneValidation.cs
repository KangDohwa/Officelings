using System.Collections.Immutable;
using System.Runtime.CompilerServices;

namespace Officelings.Core.Iso.Editing;

// Strongly connected components of conservative assembly relations isolate
// every possible cycle. Each component keeps the exhaustive rig/phase checks.
internal static class IsoEditorSceneValidation
{
    private readonly record struct Volume(double I0, double J0, double I1, double J1, double Z1)
    {
        internal int Compare(Volume b)
        {
            var before = I1 <= b.I0 || J1 <= b.J0 || Z1 <= 0;
            var after = b.I1 <= I0 || b.J1 <= J0 || b.Z1 <= 0;
            return before == after ? 0 : before ? -1 : 1;
        }
    }
    private sealed class SceneKey(IReadOnlyList<IsoPlacement> furniture, IReadOnlyList<IsoSeatDesignation> seats, string prefix) : IEquatable<SceneKey>
    {
        internal readonly IReadOnlyList<IsoPlacement> Furniture = furniture;
        internal readonly IReadOnlyList<IsoSeatDesignation> Seats = seats;
        internal readonly string Prefix = prefix;
        public bool Equals(SceneKey? other) => other != null && Prefix == other.Prefix && Furniture.SequenceEqual(other.Furniture) && Seats.SequenceEqual(other.Seats);
        public override bool Equals(object? other) => other is SceneKey key && Equals(key);
        public override int GetHashCode()
        {
            var hash = new HashCode(); hash.Add(Prefix, StringComparer.Ordinal);
            foreach (var f in Furniture) hash.Add(f);
            foreach (var seat in Seats) hash.Add(seat);
            return hash.ToHashCode();
        }
    }
    private static readonly ImmutableArray<int> emptyTiles = Enumerable.Repeat(0, 100).ToImmutableArray();
    private sealed record Probe(ImmutableArray<int> Counts, IsoBounds? Bounds, ImmutableArray<ImmutableArray<IsoSceneBuilder.Entry>> Frames)
    {
        internal int Weight => Frames.Sum(f => f.Length);
    }
    private sealed class Cache
    {
        internal IsoManifest? Manifest;
        internal int Entries;
        internal readonly Dictionary<SceneKey, Probe> Probes = [];
    }
    private static readonly ConditionalWeakTable<IsoPack, Cache> caches = new();
    internal static void ValidateAssemblies(IsoLayout layout, IsoPack pack, int emptyCount, List<IsoValidationIssue> issues)
    {
        var groups = layout.Furniture.GroupBy(f => f.DeskId ?? f.Id, StringComparer.Ordinal).Select(g => g.OrderBy(f => f.Id, StringComparer.Ordinal).ToArray()).ToArray();
        var seats = IsoEditorDocument.Seats(layout).ToArray();
        var prefix = ProbePrefix(layout); var cache = caches.GetOrCreateValue(pack);
        Probe GetProbe(IsoPlacement[] group)
        {
            var origin = group[0];
            var local = layout with
            {
                Furniture = group.Select(f => f with { X = f.X - origin.X, Y = f.Y - origin.Y }).ToImmutableArray(),
                WorkSeats = seats.Where(s => group.Any(f => f.Id == s.DeskId)).ToImmutableArray(),
                Tiles = emptyTiles, WallSegments = []
            };
            var key = new SceneKey(local.Furniture, local.WorkSeats!, prefix);
            lock (cache)
            {
                if (!ReferenceEquals(cache.Manifest, pack.Manifest)) { cache.Manifest = pack.Manifest; cache.Probes.Clear(); cache.Entries = 0; }
                if (cache.Probes.TryGetValue(key, out var cached)) return cached;
                var members = group.Select(f => f.Id).ToHashSet(StringComparer.Ordinal);
                var children = groups.Where(g => members.Contains(g[0].Id)).ToArray();
                var probe = children.Length == 1 ? Measure(local, pack, prefix) : Combine(local, pack, prefix,
                    children.Select(g => (GetProbe(g), IsoGeometry.Ground(g[0].X - origin.X, g[0].Y - origin.Y), g[0].X + g[0].Y - origin.X - origin.Y)).ToArray());
                var weight = probe.Weight;
                while (cache.Probes.Count > 0 && (cache.Probes.Count >= 256 || cache.Entries + weight > 65_536))
                {
                    var first = cache.Probes.First(); cache.Entries -= first.Value.Weight; cache.Probes.Remove(first.Key);
                }
                if (weight <= 65_536) { cache.Probes.Add(key, probe); cache.Entries += weight; }
                return probe;
            }
        }
        var bounds = groups.Select(group =>
        {
            var probe = GetProbe(group); if (probe.Bounds is not {} b) return (IsoBounds?)null;
            var offset = IsoGeometry.Ground(group[0].X, group[0].Y);
            return new IsoBounds(b.Left + offset.X, b.Top + offset.Y, b.Right + offset.X, b.Bottom + offset.Y);
        }).ToArray();
        var volumes = groups.Select(group => group.SelectMany(f =>
        {
            var v = pack.Variant(f.AssetId, f.Rotation, f.State);
            var body = new Volume(f.X - .5, f.Y - .5, f.X + v.FootprintW - .5, f.Y + v.FootprintH - .5, Math.Max(1, v.HeightPx));
            var root = group.FirstOrDefault(p => p.Id == (f.DeskId ?? f.Id));
            if (root == null || !seats.Any(s => s.DeskId == root.Id)) return new[] { body };
            var profile = pack.Variant(root.AssetId, root.Rotation, root.State).SeatProfiles?.FirstOrDefault(s => s.Id is "front" or "lounge");
            if (profile == null) return new[] { body };
            // Include contact-plane volumes even for parts that never use them.
            // Extra edges can only enlarge a component, never omit a cycle.
            var i = root.X + profile.SeatGround.I; var j = root.Y + profile.SeatGround.J;
            return new[] { body, new Volume(i, j, i, j, body.Z1) };
        }).ToArray()).ToArray();
        var edges = new bool[groups.Length, groups.Length];
        void Edge(int a, int b) => edges[a, b] = true;
        for (var a = 0; a < groups.Length; a++) for (var b = a + 1; b < groups.Length; b++)
        {
            if (bounds[a] is not {} ba || bounds[b] is not {} bb || ba.Left >= bb.Right || bb.Left >= ba.Right || ba.Top >= bb.Bottom || bb.Top >= ba.Bottom) continue;
            foreach (var va in volumes[a]) foreach (var vb in volumes[b])
            {
                var relation = va.Compare(vb);
                if (relation < 0) Edge(a, b); else if (relation > 0) Edge(b, a);
            }
        }
        // Keep every potentially cyclic group together. The condensation graph
        // is a DAG, so no complete scene can have a cycle across components.
        var indices = Enumerable.Repeat(-1, groups.Length).ToArray(); var low = new int[groups.Length];
        var stack = new Stack<int>(); var active = new bool[groups.Length]; var next = 0;
        var components = new List<IsoPlacement[]>();
        void Visit(int a)
        {
            indices[a] = low[a] = next++; stack.Push(a); active[a] = true;
            for (var b = 0; b < groups.Length; b++) if (edges[a, b])
            {
                if (indices[b] < 0) { Visit(b); low[a] = Math.Min(low[a], low[b]); }
                else if (active[b]) low[a] = Math.Min(low[a], indices[b]);
            }
            if (low[a] != indices[a]) return;
            var members = new List<IsoPlacement>(); int member;
            do { member = stack.Pop(); active[member] = false; members.AddRange(groups[member]); } while (member != a);
            components.Add(members.OrderBy(f => f.Id, StringComparer.Ordinal).ToArray());
        }
        for (var a = 0; a < groups.Length; a++) if (indices[a] < 0) Visit(a);
        var totals = new int[pack.Characters.Count * 10];
        foreach (var group in components)
        {
            var probe = GetProbe(group);
            for (var k = 0; k < totals.Length; k++) totals[k] += probe.Counts[k];
        }
        // Each probe stores its difference from the empty assembly, so this is
        // the exact global count for every original supported design/phase/frame.
        if (totals.Any(delta => emptyCount + delta > 4096)) issues.Add(IsoEditorValidation.Issue("L017", "Iso editor layout limits exceeded."));
    }
    internal static string ProbePrefix(IsoLayout layout)
    {
        var prefix = "editor-probe/";
        while (layout.Furniture.Any(f => f.Id.StartsWith(prefix, StringComparison.Ordinal))) prefix += "/";
        return prefix;
    }
    // Isolated frames already contain the exact compiled rig, authored parts and
    // effects. Translation preserves their local constraints; re-sort every frame
    // with the original room-wide orderer to check all cross-assembly constraints.
    private static Probe Combine(IsoLayout layout, IsoPack pack, string prefix, (Probe Probe, IsoPixel Offset, int Depth)[] children)
    {
        var order = new IsoFurnitureOrder(pack, layout); var work = new List<IsoSceneBuilder.Entry>();
        var poses = IsoEditorDocument.Seats(layout).Select(s => new IsoActorPose(prefix + s.DeskId, "", "", new(0, 0), "se", "typing", 0, s.DeskId, "seated")).ToArray();
        var counts = ImmutableArray.CreateBuilder<int>(pack.Characters.Count * 10); IsoBounds? bounds = null;
        foreach (var (probe, offset, _) in children)
            if (probe.Bounds is {} b) bounds = bounds?.Include(new(b.Left + offset.X, b.Top + offset.Y, b.Right + offset.X, b.Bottom + offset.Y)) ??
                new IsoBounds(b.Left + offset.X, b.Top + offset.Y, b.Right + offset.X, b.Bottom + offset.Y);
        for (var frame = 0; frame < pack.Characters.Count * 10; frame++)
        {
            work.Clear(); var count = 0;
            foreach (var (probe, offset, depth) in children)
            {
                count += probe.Counts[frame];
                foreach (var entry in probe.Frames[frame])
                {
                    var key = entry.Key with { GroundDepth = entry.Key.GroundDepth + depth };
                    work.Add(entry with { Key = key, Item = entry.Item with { X = entry.Item.X + offset.X, Y = entry.Item.Y + offset.Y, Depth = key.Encoded } });
                }
            }
            IsoSceneBuilder.SortEntries(work); order.Sort(work, poses, []); counts.Add(count);
        }
        return new(counts.MoveToImmutable(), bounds, []);
    }
    private static Probe Measure(IsoLayout layout, IsoPack pack, string prefix)
    {
        var builder = new IsoSceneBuilder(pack, layout, false); var initial = builder.Build([]); var empty = initial.Items.Count;
        IsoBounds? bounds = null;
        void Include(IsoScene scene)
        {
            foreach (var item in scene.Items)
            {
                if (item.Depth >> 60 != (int)IsoPass.Scene) continue;
                var b = pack.OpaqueBounds(item.SheetId, item.Clip);
                if (b.Width <= 0 || b.Height <= 0) continue;
                b = new(b.Left + item.X - builder.Camera.X, b.Top + item.Y - builder.Camera.Y,
                    b.Right + item.X - builder.Camera.X, b.Bottom + item.Y - builder.Camera.Y);
                bounds = bounds?.Include(b) ?? b;
            }
        }
        Include(initial);
        var ids = layout.Furniture.Select(f => f.Id).ToHashSet(StringComparer.Ordinal);
        ImmutableArray<IsoSceneBuilder.Entry> Snapshot() => builder.EditorEntries.Where(e => e.Item.Layer is not (IsoLayer.Floor or IsoLayer.Wall) &&
            (ids.Contains(e.Key.InstanceId) || e.Key.InstanceId.StartsWith(prefix, StringComparison.Ordinal)))
            .Select(e => e with { Item = e.Item with { X = e.Item.X - builder.Camera.X, Y = e.Item.Y - builder.Camera.Y } }).ToImmutableArray();
        var snapshots = ImmutableArray.CreateBuilder<ImmutableArray<IsoSceneBuilder.Entry>>(pack.Characters.Count * 10);
        var counts = ImmutableArray.CreateBuilder<int>(pack.Characters.Count * 10);
        var desks = IsoEditorDocument.Seats(layout).Select(s => layout.Furniture.First(f => f.Id == s.DeskId)).ToArray();
        if (desks.Length == 0)
            return new(Enumerable.Repeat(0, pack.Characters.Count * 10).ToImmutableArray(), bounds,
                Enumerable.Repeat(Snapshot(), pack.Characters.Count * 10).ToImmutableArray());
        foreach (var design in pack.Characters)
        foreach (var phase in new[] { "turn", "lower", "seated", "raise" })
        for (var frame = 0; frame < (phase is "lower" or "raise" ? 4 : 1); frame++)
        {
            var poses = desks.Select((f, k) =>
            {
                var c = IsoContacts.Desk(f.X, f.Y, f.Rotation);
                return new IsoActorPose(prefix + f.Id, design.Id, "", new(c.Approach.X, c.Approach.Y), c.Facing,
                    phase == "lower" ? "sit-down" : phase == "raise" ? "stand-up" : "typing", 0, f.Id, phase, frame);
            }).ToArray();
            var scene = builder.Build(poses); Include(scene); counts.Add(scene.Items.Count - empty); snapshots.Add(Snapshot());
        }
        return new(counts.MoveToImmutable(), bounds, snapshots.MoveToImmutable());
    }
}