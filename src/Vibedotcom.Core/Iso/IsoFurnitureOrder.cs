namespace Vibedotcom.Core.Iso;

/// Separating ground planes constrain the painter queue before legacy ground-key ties.
/// A footprint spans tile boundaries, not the source canvas. Height never adds to d.
/// Scratch storage is reused; source bounds and layout relations are cached.
internal sealed class IsoFurnitureOrder
{
    private readonly record struct Volume(double I0, double J0, double I1, double J1, double Z0, double Z1)
    {
        public int Compare(Volume b)
        {
            var before = I1 <= b.I0 || J1 <= b.J0 || Z1 <= b.Z0;
            var after = b.I1 <= I0 || b.J1 <= J0 || b.Z1 <= Z0;
            return before == after ? 0 : before ? -1 : 1;
        }
    }
    private sealed record Furniture(IsoPlacement Placement, IsoFurnitureVariant Variant, Volume Volume,
        Dictionary<string, (int Support, bool Seat)> Parts);
    private readonly IsoPack pack;
    private readonly Dictionary<string, Furniture> furniture = new(StringComparer.Ordinal);
    private readonly Dictionary<(string A, string B), int> relations = [];
    private readonly Dictionary<(string A, string ClipA, string B, string ClipB), bool> overlaps = [];
    private readonly Dictionary<string, (Volume? Volume, string? Seat)> actors = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Instance, string Sheet, string Clip),
        (IsoBounds Bounds, Furniture? Owner, int Support, bool Seat)> metadata = [];
    private readonly HashSet<string> occupiedSeats = new(StringComparer.Ordinal);
    private readonly List<IsoSceneBuilder.Entry> ordered = [];
    private bool[] edges = [], emitted = [];
    private readonly Dictionary<string, List<int>> ownerIndices = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> seatTails = new(StringComparer.Ordinal);
    private readonly PriorityQueue<int, int> ready = new();
    private List<int>[] outgoing = [];
    private int[] spatial = [];
    private readonly IComparer<int> spatialOrder;
    private int[] indegrees = [];
    private IsoBounds[] bounds = [];
    private Furniture?[] owners = [];
    private (int Support, bool Seat)[] parts = [];
    private (Volume? Volume, string? Seat)[] moving = [];
    private Volume ContactVolume(Furniture f, bool seatPart)
    {
        var owner = f.Placement.DeskId ?? f.Placement.Id;
        if (!seatPart || !occupiedSeats.Contains(owner) || !furniture.TryGetValue(owner, out var support)) return f.Volume;
        var seat = support.Variant.SeatProfiles?.FirstOrDefault(s => s.Id is "front" or "lounge");
        if (seat == null) return f.Volume;
        var i = support.Placement.X + seat.SeatGround.I; var j = support.Placement.Y + seat.SeatGround.J;
        return new(i, j, i, j, 0, f.Volume.Z1);
    }
    internal IsoFurnitureOrder(IsoPack pack, IsoLayout layout)
    {
        this.pack = pack;
        spatialOrder = Comparer<int>.Create((a, b) => bounds[a].Left.CompareTo(bounds[b].Left));
        foreach (var f in layout.Furniture)
        {
            var v = pack.Variant(f.AssetId, f.Rotation, f.State);
            var metadata = v.Parts.ToDictionary(p => p.PartId ?? pack.Clip(p.SheetId, p.Animation ?? p.Clip!, p.Direction).PartId!,
                p => (p.SupportRank, p.SeatGroupId != null), StringComparer.Ordinal);
            if (v.Screen is {} screen) metadata["screen"] = (screen.SupportRank, false);
            if (v.Glow is {} glow) metadata["glow"] = (glow.SupportRank, false);
            furniture.Add(f.Id, new(f, v, new(f.X - .5, f.Y - .5, f.X + v.FootprintW - .5,
                f.Y + v.FootprintH - .5, 0, Math.Max(1, v.HeightPx)), metadata));
        }
        foreach (var a in furniture.Values) foreach (var b in furniture.Values)
            relations[(a.Placement.Id, b.Placement.Id)] = a.Volume.Compare(b.Volume);
    }
    internal void Sort(List<IsoSceneBuilder.Entry> entries, IReadOnlyList<IsoActorPose> poses, IReadOnlyList<IsoPetPose> pets)
    {
        actors.Clear(); occupiedSeats.Clear();
        foreach (var pose in poses)
        {
            var seated = pose.DeskId != null && pose.SeatPhase != null;
            if (seated) occupiedSeats.Add(pose.DeskId!);
            actors[pose.AgentId] = seated ? (null, pose.DeskId) :
                (new(pose.Position.X, pose.Position.Y, pose.Position.X, pose.Position.Y, 0, 40), null);
        }
        foreach (var cat in pets.Take(2)) actors[cat.AssetId] =
            (new(cat.Position.X, cat.Position.Y, cat.Position.X, cat.Position.Y, 0, 18), null);
        var start = entries.FindIndex(e => e.Key.Pass == IsoPass.Scene);
        if (start < 0) return;
        var end = start;
        while (end < entries.Count && entries[end].Key.Pass == IsoPass.Scene) end++;
        var count = end - start;
        if (emitted.Length < count)
        {
            var capacity = Math.Max(count, emitted.Length * 2);
            spatial = new int[capacity];
            emitted = new bool[capacity]; indegrees = new int[capacity]; bounds = new IsoBounds[capacity];
            owners = new Furniture?[capacity]; parts = new (int, bool)[capacity]; moving = new (Volume?, string?)[capacity];
            edges = new bool[capacity * capacity];
            outgoing = Enumerable.Range(0, capacity).Select(_ => new List<int>()).ToArray();
        }
        if (ordered.Capacity < entries.Count) ordered.Capacity = entries.Count;
        Array.Clear(edges); Array.Clear(indegrees); Array.Clear(emitted);
        foreach (var indices in ownerIndices.Values) indices.Clear();
        seatTails.Clear(); ready.Clear();
        for (var i = 0; i < count; i++) outgoing[i].Clear();
        for (var i = 0; i < count; i++)
        {
            var entry = entries[start + i]; var key = (entry.Item.InstanceId, entry.Item.SheetId, entry.Item.Clip);
            if (!metadata.TryGetValue(key, out var info))
            {
                var source = pack.OpaqueBounds(entry.Item.SheetId, entry.Item.Clip);
                furniture.TryGetValue(entry.Key.InstanceId, out var owner);
                var part = owner?.Parts.GetValueOrDefault(entry.Key.PartId) ?? default;
                // Baked fronts retain their attached-seat metadata and source bounds.
                info = (source, owner, part.Support, part.Seat || pack.Clip(entry.Item.SheetId, entry.Item.Clip).SeatGroupId != null);
                metadata.Add(key, info);
            }
            bounds[i] = new(info.Bounds.Left + entry.Item.X, info.Bounds.Top + entry.Item.Y,
                info.Bounds.Right + entry.Item.X, info.Bounds.Bottom + entry.Item.Y);
            owners[i] = info.Owner; parts[i] = (info.Support, info.Seat);
            moving[i] = actors.GetValueOrDefault(entry.Key.InstanceId);
            if (owners[i] != null)
            {
                if (!ownerIndices.TryGetValue(entry.Key.InstanceId, out var indices)) ownerIndices[entry.Key.InstanceId] = indices = [];
                indices.Add(i);
            }
        }
        var stride = emitted.Length;
        void Edge(int before, int after)
        { if (!edges[before * stride + after]) { edges[before * stride + after] = true; indegrees[after]++; outgoing[before].Add(after); } }
        // Local assembly constraints do not need a room-wide pair scan. A chain
        // has exactly the same reachability as the previous same-seat clique.
        for (var i = 0; i < count; i++)
        {
            var seat = parts[i].Seat && owners[i] is {} f ? f.Placement.DeskId ?? f.Placement.Id : moving[i].Seat;
            if (seat == null || !occupiedSeats.Contains(seat)) continue;
            if (seatTails.TryGetValue(seat, out var tail)) Edge(tail, i);
            seatTails[seat] = i;
        }
        foreach (var indices in ownerIndices.Values)
        for (var a = 0; a < indices.Count; a++) for (var b = a + 1; b < indices.Count; b++)
        {
            var i = indices[a]; var j = indices[b]; var f = owners[i]!;
            if (parts[i].Seat && parts[j].Seat && occupiedSeats.Contains(f.Placement.DeskId ?? f.Placement.Id)) continue;
            if (occupiedSeats.Contains(f.Placement.Id) && (parts[i].Seat || parts[j].Seat)) continue;
            var relation = parts[i].Support.CompareTo(parts[j].Support);
            if (relation < 0) Edge(i, j); else if (relation > 0) Edge(j, i);
        }
        for (var i = 0; i < count; i++) spatial[i] = i;
        Array.Sort(spatial, 0, count, spatialOrder);
        for (var aIndex = 0; aIndex < count; aIndex++)
        for (var bIndex = aIndex + 1; bIndex < count; bIndex++)
        {
            var first = spatial[aIndex]; var second = spatial[bIndex];
            if (bounds[second].Left >= bounds[first].Right) break;
            var i = Math.Min(first, second); var j = Math.Max(first, second);
            var ba = bounds[i]; var bb = bounds[j];
            if (ba.Left >= bb.Right || bb.Left >= ba.Right || ba.Top >= bb.Bottom || bb.Top >= ba.Bottom) continue;
            var a = entries[start + i]; var b = entries[start + j];
            var fa = owners[i]; var fb = owners[j]; var relation = 0;
            var seatA = parts[i].Seat && fa != null ? fa.Placement.DeskId ?? fa.Placement.Id : moving[i].Seat;
            var seatB = parts[j].Seat && fb != null ? fb.Placement.DeskId ?? fb.Placement.Id : moving[j].Seat;
            // The complete attached assembly retains its input ground/rank/ID
            // order, even for disjoint masks. External footprint constraints
            // may move it globally but must not invert its sitter and parts.
            if (seatA != null && seatA == seatB && occupiedSeats.Contains(seatA)) continue;
            // Linked desk/chair parts interlock physically. Their authored seat
            // queue is local to that assembly, not a rectangle separation.
            if (fa != null && fb != null && (fa.Placement.DeskId == fb.Placement.Id || fb.Placement.DeskId == fa.Placement.Id))
            {
                if (occupiedSeats.Contains(fa.Placement.Id) || occupiedSeats.Contains(fb.Placement.Id))
                { if (parts[i].Seat && parts[j].Seat && pack.AlphaOverlaps(a.Item, b.Item)) Edge(i, j); }
                continue;
            }
            if (ReferenceEquals(fa, fb) && fa != null) continue;
            // Attached seat strips extend to the approach contact outside the
            // furniture footprint. Walkers retain the existing S0 contact-plane
            // rule even for empty linked chairs; the body rectangle cannot replace it.
            if (fa != null && parts[i].Seat && (fa.Placement.DeskId != null || occupiedSeats.Contains(fa.Placement.Id)) && moving[j].Volume != null ||
                fb != null && parts[j].Seat && (fb.Placement.DeskId != null || occupiedSeats.Contains(fb.Placement.Id)) && moving[i].Volume != null)
            { if (pack.AlphaOverlaps(a.Item, b.Item)) Edge(i, j); continue; }
            if (fa != null && fb != null)
                relation = parts[i].Seat && occupiedSeats.Contains(fa.Placement.DeskId ?? fa.Placement.Id) ||
                    parts[j].Seat && occupiedSeats.Contains(fb.Placement.DeskId ?? fb.Placement.Id)
                    ? ContactVolume(fa, parts[i].Seat).Compare(ContactVolume(fb, parts[j].Seat))
                    : relations[(fa.Placement.Id, fb.Placement.Id)];
            else if (fa != null && moving[j].Volume is {} vb) relation = fa.Volume.Compare(vb);
            else if (fb != null && moving[i].Volume is {} va) relation = va.Compare(fb.Volume);
            // Masks cannot introduce an ordering edge when the separating planes
            // have no relation (including bodies already attached to a seat).
            if (relation == 0) continue;
            if (fa != null && fb != null)
            {
                var key = (a.Item.InstanceId, a.Item.Clip, b.Item.InstanceId, b.Item.Clip);
                if (!overlaps.TryGetValue(key, out var overlap)) overlaps[key] = overlap = pack.AlphaOverlaps(a.Item, b.Item);
                if (!overlap) continue;
            }
            else if (!pack.AlphaOverlaps(a.Item, b.Item)) continue;
            if (relation < 0) Edge(i, j); else if (relation > 0) Edge(j, i);
        }
        ordered.Clear();
        for (var i = 0; i < start; i++) ordered.Add(entries[i]);
        for (var i = 0; i < count; i++) if (indegrees[i] == 0) ready.Enqueue(i, i);
        for (var k = 0; k < count; k++)
        {
            // Original input index is the stable S0 priority for unrelated nodes.
            var next = ready.TryDequeue(out var candidate, out _) ? candidate : -1;
            if (next < 0)
            {
                var path = new List<int>(); var state = new byte[count];
                bool Cycle(int node)
                {
                    state[node] = 1; path.Add(node);
                    for (var j = 0; j < count; j++) if (!emitted[j] && edges[node * stride + j])
                    {
                        if (state[j] == 1) { path.Add(j); return true; }
                        if (state[j] == 0 && Cycle(j)) return true;
                    }
                    path.RemoveAt(path.Count - 1); state[node] = 2; return false;
                }
                for (var i = 0; i < count; i++) if (!emitted[i] && state[i] == 0 && Cycle(i)) break;
                throw new InvalidDataException("Cyclic ISO visibility constraints: " + string.Join(" -> ", path.Select(i => entries[start + i].Item.InstanceId)));
            }
            emitted[next] = true; ordered.Add(entries[start + next]);
            foreach (var j in outgoing[next]) if (--indegrees[j] == 0) ready.Enqueue(j, j);
        }
        for (var i = end; i < entries.Count; i++) ordered.Add(entries[i]);
        entries.Clear(); entries.AddRange(ordered);
    }
}
