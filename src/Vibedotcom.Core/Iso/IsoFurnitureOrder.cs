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
    private readonly HashSet<string> occupiedSeats = new(StringComparer.Ordinal);
    private readonly List<IsoSceneBuilder.Entry> ordered = [];
    private bool[] edges = [], emitted = [];
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
    internal void Sort(List<IsoSceneBuilder.Entry> entries, IReadOnlyList<IsoActorPose> poses, IsoCatPose? cat)
    {
        actors.Clear(); occupiedSeats.Clear();
        foreach (var pose in poses)
        {
            var seated = pose.DeskId != null && pose.SeatPhase is "seated" or "lower" or "raise";
            if (seated) occupiedSeats.Add(pose.DeskId!);
            actors[pose.AgentId] = seated ? (null, pose.DeskId) :
                (new(pose.Position.X, pose.Position.Y, pose.Position.X, pose.Position.Y, 0, 40), null);
        }
        if (cat != null) actors[pack.Manifest.Pets!.Single().Id] =
            (new(cat.Position.X, cat.Position.Y, cat.Position.X, cat.Position.Y, 0, 18), null);
        var start = entries.FindIndex(e => e.Key.Pass == IsoPass.Scene);
        if (start < 0) return;
        var end = start;
        while (end < entries.Count && entries[end].Key.Pass == IsoPass.Scene) end++;
        var count = end - start;
        if (emitted.Length < count)
        {
            var capacity = Math.Max(count, emitted.Length * 2);
            emitted = new bool[capacity]; indegrees = new int[capacity]; bounds = new IsoBounds[capacity];
            owners = new Furniture?[capacity]; parts = new (int, bool)[capacity]; moving = new (Volume?, string?)[capacity];
            edges = new bool[capacity * capacity];
        }
        if (ordered.Capacity < entries.Count) ordered.Capacity = entries.Count;
        Array.Clear(edges); Array.Clear(indegrees); Array.Clear(emitted);
        for (var i = 0; i < count; i++)
        {
            var entry = entries[start + i]; var source = pack.OpaqueBounds(entry.Item.SheetId, entry.Item.Clip);
            bounds[i] = new(source.Left + entry.Item.X, source.Top + entry.Item.Y, source.Right + entry.Item.X, source.Bottom + entry.Item.Y);
            furniture.TryGetValue(entry.Key.InstanceId, out owners[i]);
            parts[i] = owners[i]?.Parts.GetValueOrDefault(entry.Key.PartId) ?? default;
            moving[i] = actors.GetValueOrDefault(entry.Key.InstanceId);
        }
        var stride = emitted.Length;
        void Edge(int before, int after)
        { if (!edges[before * stride + after]) { edges[before * stride + after] = true; indegrees[after]++; } }
        for (var i = 0; i < count; i++) for (var j = i + 1; j < count; j++)
        {
            var a = entries[start + i]; var b = entries[start + j];
            var fa = owners[i]; var fb = owners[j]; var relation = 0;
            // Linked desk/chair parts interlock physically. Their authored seat
            // queue is local to that assembly, not a rectangle separation.
            if (fa != null && fb != null && (fa.Placement.DeskId == fb.Placement.Id || fb.Placement.DeskId == fa.Placement.Id))
            {
                if (occupiedSeats.Contains(fa.Placement.Id) || occupiedSeats.Contains(fb.Placement.Id))
                { if (parts[i].Seat && parts[j].Seat && pack.AlphaOverlaps(a.Item, b.Item)) Edge(i, j); }
                continue;
            }
            if (fa != null && fb != null && fa == fb)
            {
                // Seat-intersecting parts retain their attached actor's S0 ranks.
                var occupied = occupiedSeats.Contains(fa.Placement.Id);
                if (!occupied || !parts[i].Seat && !parts[j].Seat)
                    relation = parts[i].Support.CompareTo(parts[j].Support);
                if (relation != 0) { if (relation < 0) Edge(i, j); else Edge(j, i); }
                continue;
            }
            // Preserve the authored local seat queue without changing seated actor code.
            if (moving[i].Seat is {} seatA && fb != null && parts[j].Seat && (fb.Placement.Id == seatA || fb.Placement.DeskId == seatA) ||
                moving[j].Seat is {} seatB && fa != null && parts[i].Seat && (fa.Placement.Id == seatB || fa.Placement.DeskId == seatB))
            { if (pack.AlphaOverlaps(a.Item, b.Item)) Edge(i, j); continue; } // input is in legacy-key order
            // Occupied seat strips extend to the approach contact outside the
            // furniture footprint. Walkers retain the existing S0 contact-plane
            // rule for these strips; the body rectangle cannot replace it.
            if (fa != null && parts[i].Seat && occupiedSeats.Contains(fa.Placement.DeskId ?? fa.Placement.Id) && moving[j].Volume != null ||
                fb != null && parts[j].Seat && occupiedSeats.Contains(fb.Placement.DeskId ?? fb.Placement.Id) && moving[i].Volume != null)
            { if (pack.AlphaOverlaps(a.Item, b.Item)) Edge(i, j); continue; }
            var ba = bounds[i]; var bb = bounds[j];
            if (ba.Left >= bb.Right || bb.Left >= ba.Right || ba.Top >= bb.Bottom || bb.Top >= ba.Bottom) continue;
            if (fa != null && fb != null)
            {
                var key = (a.Item.InstanceId, a.Item.Clip, b.Item.InstanceId, b.Item.Clip);
                if (!overlaps.TryGetValue(key, out var overlap)) overlaps[key] = overlap = pack.AlphaOverlaps(a.Item, b.Item);
                if (!overlap) continue;
            }
            else if (!pack.AlphaOverlaps(a.Item, b.Item)) continue;
            if (fa != null && fb != null)
                relation = parts[i].Seat && occupiedSeats.Contains(fa.Placement.DeskId ?? fa.Placement.Id) ||
                    parts[j].Seat && occupiedSeats.Contains(fb.Placement.DeskId ?? fb.Placement.Id)
                    ? ContactVolume(fa, parts[i].Seat).Compare(ContactVolume(fb, parts[j].Seat))
                    : relations[(fa.Placement.Id, fb.Placement.Id)];
            else if (fa != null && moving[j].Volume is {} vb) relation = fa.Volume.Compare(vb);
            else if (fb != null && moving[i].Volume is {} va) relation = va.Compare(fb.Volume);
            if (relation < 0) Edge(i, j); else if (relation > 0) Edge(j, i);
        }
        ordered.Clear();
        for (var i = 0; i < start; i++) ordered.Add(entries[i]);
        for (var k = 0; k < count; k++)
        {
            var next = -1;
            // Stable Kahn traversal. Unrelated entries use the original S0 key.
            for (var i = 0; i < count; i++) if (!emitted[i] && indegrees[i] == 0) { next = i; break; }
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
            for (var j = 0; j < count; j++) if (edges[next * stride + j]) indegrees[j]--;
        }
        for (var i = end; i < entries.Count; i++) ordered.Add(entries[i]);
        entries.Clear(); entries.AddRange(ordered);
    }
}
