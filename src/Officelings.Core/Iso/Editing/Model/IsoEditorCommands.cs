using System.Collections.Immutable;

namespace Officelings.Core.Iso.Editing;

internal sealed class IsoEditorCommands
{
    private readonly IsoPack pack;
    private readonly IsoLayout layout;
    private readonly HashSet<string> used;
    internal sealed record Candidate(IsoLayout Layout, ImmutableArray<IsoValidationIssue> Issues, string? Target, long Counter);
    private readonly List<IsoValidationIssue> issues = [];
    private string? target;
    private IsoLayout candidate;
    private long next;
    internal IsoEditorCommands(IsoPack pack, IsoLayout layout, HashSet<string> used, long counter)
    { this.pack = pack; this.layout = layout; this.used = used; candidate = layout; next = counter; }
    internal Candidate Build(IsoEditorCommand command)
    {
        switch (command)
        {
            case IsoPlaceCommand c: Place(c); break;
            case IsoMoveCommand c: Transform(c.InstanceId, c.Origin, null, false); break;
            case IsoRotateCommand c: Rotate(c); break;
            case IsoDeleteCommand c: Delete(c.InstanceId); break;
            case IsoDuplicateCommand c: Duplicate(c); break;
            case IsoSetSeatCommand c: SetSeat(c); break;
            case IsoClearSeatCommand c: ClearSeat(c.DeskId); break;
            case IsoPlaceWallCommand c: Wall(null, c.AssetId, c.HostWallId, false); break;
            case IsoMoveWallCommand c: Wall(c.InstanceId, null, c.HostWallId, false); break;
            case IsoDuplicateWallCommand c: Wall(c.InstanceId, null, c.HostWallId, true); break;
            default: Error("C002", "Unknown or unsupported iso asset ''."); break;
        }
        return new(IsoEditorDocument.Freeze(candidate), IsoEditorValidation.Sort(issues), target, next);
    }
    private void Error(string rule, string message, params string[] ids) => issues.Add(IsoEditorValidation.Issue(rule, message,
        ids.Where(id => IsoEditorDocument.Ids(layout).Contains(id, StringComparer.Ordinal)).ToArray()));
    private void SeatError() => Error("C004", "Select a canonical desk, aligned chair and compatible computer.");
    private void WallError() => Error("C005", "Select an editable wall decoration and a compatible free host.");
    private void SlotError() => Error("C006", "The edit would replace an occupied slot or lose an asset reference.");
    private void RotationError(string asset, string state) => Error("C003", $"Asset '{asset}' has no usable authored rotation for state '{state}'.");
    private string Allocate(string asset)
    {
        var prefix = asset switch { "iso/desk" => "desk", "iso/chair" => "chair", "iso/computer" => "computer", "decor" => "decor", _ => "object" };
        string id; do { id = prefix + "-" + ++next; } while (!used.Add(id)); return id;
    }
    private IsoPlacement? Find(string id)
    {
        var f = layout.Furniture.FirstOrDefault(f => f.Id == id);
        if (f == null) Error("C001", $"Unknown iso instance '{id}'."); return f;
    }
    private IsoPlacement? Root(string id)
    {
        if ((layout.WallSegments ?? []).Any(w => w.Id == id || w.AssemblyId == id) || id is "rear-corner" or "end-cap-l" or "end-cap-r") { WallError(); return null; }
        var f = Find(id);
        return f?.DeskId == null ? f : Find(f.DeskId);
    }
    private IsoFurnitureVariant? Variant(string asset, int rotation, string state)
    {
        var family = pack.Manifest.Furniture?.FirstOrDefault(a => a.Id == asset);
        if (family == null) { Error("C002", $"Unknown or unsupported iso asset '{asset}'."); return null; }
        var v = family.Variants.FirstOrDefault(v => v.Rotation == rotation && v.State == state);
        if (v == null) RotationError(asset, state); return v;
    }
    private static IsoPlacement Metadata(IsoPlacement f, IsoFurnitureVariant v) => f with
    { Orientation = f.Orientation == null ? null : v.Orientation, Width = f.Width == null ? null : v.FootprintW, Height = f.Height == null ? null : v.FootprintH };
    private void Place(IsoPlaceCommand c)
    {
        if (Variant(c.AssetId, c.Rotation, c.State) == null) return;
        if (c.HostDeskId != null)
        {
            var desk = Find(c.HostDeskId); if (desk == null) return;
            if (c.AssetId != "iso/computer" || desk.AssetId != "iso/desk" || desk.X != c.Origin.X || desk.Y != c.Origin.Y || desk.Rotation != c.Rotation) { SeatError(); return; }
            if (layout.Furniture.Any(f => f.DeskId == desk.Id && f.AssetId == "iso/computer")) { SlotError(); return; }
        }
        var id = Allocate(c.AssetId); target = c.HostDeskId ?? id;
        candidate = layout with
        {
            Furniture = layout.Furniture.Append(new(id, c.AssetId, c.Origin.X, c.Origin.Y, c.Rotation, c.State, c.HostDeskId)).ToImmutableArray(),
            WorkSeats = c.HostDeskId == null ? layout.WorkSeats : layout.WorkSeats?.Select(s => s.DeskId == c.HostDeskId ? s with { ComputerId = id } : s).ToImmutableArray()
        };
    }
    private void Rotate(IsoRotateCommand c)
    {
        if ((layout.WallSegments ?? []).FirstOrDefault(w => w.Id == c.InstanceId || w.AssemblyId == c.InstanceId) is {} wall)
        { RotationError(wall.AssetId, wall.State); return; }
        var root = Root(c.InstanceId); if (root == null) return;
        var rotations = pack.Manifest.Furniture?.FirstOrDefault(a => a.Id == root.AssetId)?.Variants.Where(v => v.State == root.State).Select(v => v.Rotation).Distinct().Order().ToArray() ?? [];
        if (c.Rotation == null && rotations.Length < 2) { RotationError(root.AssetId, root.State); return; }
        var rotation = c.Rotation ?? rotations.FirstOrDefault(r => r > root.Rotation, rotations[0]);
        Transform(root.Id, new(root.X, root.Y), rotation, false);
    }
    private void Transform(string id, Tile origin, int? rotation, bool duplicate)
    {
        var root = Root(id); if (root == null) return;
        if (!duplicate && root.X == origin.X && root.Y == origin.Y && (rotation == null || root.Rotation == rotation)) { target = root.Id; return; }
        var transformed = root with { X = origin.X, Y = origin.Y, Rotation = rotation ?? root.Rotation };
        var v = Variant(root.AssetId, transformed.Rotation, root.State); if (v == null) return;
        if (origin.X < 0 || origin.Y < 0 || (long)origin.X + v.FootprintW > 10 || (long)origin.Y + v.FootprintH > 10)
        { Error("L005", $"Object or interaction '{root.Id}' is outside the room.", root.Id); return; }
        transformed = Metadata(transformed, v);
        var group = layout.Furniture.Where(f => f.Id == root.Id || f.DeskId == root.Id).ToArray();
        var remap = group.ToDictionary(f => f.Id, f => duplicate ? Allocate(f.AssetId) : f.Id, StringComparer.Ordinal);
        var result = new List<IsoPlacement>();
        foreach (var f in group)
        {
            var replacement = transformed;
            if (f.Id != root.Id)
            {
                if (!IsoEditorValidation.CanonicalRotation(transformed)) { SeatError(); return; }
                var contact = IsoContacts.Desk(origin.X, origin.Y, transformed.Rotation);
                replacement = f.AssetId == "iso/chair" ? f with { X = contact.Approach.X, Y = contact.Approach.Y, Rotation = IsoGeometry.Rotation(contact.Facing) } :
                    f with { X = origin.X, Y = origin.Y, Rotation = transformed.Rotation };
                var childVariant = Variant(f.AssetId, replacement.Rotation, f.State); if (childVariant == null) return;
                replacement = Metadata(replacement, childVariant);
            }
            result.Add(replacement with { Id = remap[f.Id], DeskId = f.DeskId == null ? null : remap.GetValueOrDefault(f.DeskId, f.DeskId) });
        }
        var copiedSeats = (layout.WorkSeats ?? []).Where(s => s.DeskId == root.Id).Select(s => s with
        { Id = remap[root.Id], DeskId = remap[root.Id], ChairId = remap.GetValueOrDefault(s.ChairId, s.ChairId), ComputerId = s.ComputerId == null ? null : remap.GetValueOrDefault(s.ComputerId, s.ComputerId) });
        candidate = layout with
        {
            Furniture = layout.Furniture.Where(f => duplicate || !remap.ContainsKey(f.Id)).Concat(result).ToImmutableArray(),
            WorkSeats = duplicate ? (layout.WorkSeats ?? []).Concat(copiedSeats).ToImmutableArray() : layout.WorkSeats
        };
        target = remap[root.Id];
    }
    private void Duplicate(IsoDuplicateCommand c)
    {
        var root = Root(c.InstanceId); if (root == null) return;
        var v = Variant(root.AssetId, root.Rotation, root.State); if (v == null) return;
        Transform(root.Id, c.Origin ?? new(root.X + v.FootprintW, root.Y), null, true);
    }
    private void Delete(string id)
    {
        var wall = (layout.WallSegments ?? []).FirstOrDefault(w => w.Id == id || w.AssemblyId == id);
        if (wall != null)
        {
            if (wall.HostId == null || wall.AssemblyId == null) { WallError(); return; }
            candidate = layout with { WallSegments = layout.WallSegments!.Where(w => w.AssemblyId != wall.AssemblyId).ToImmutableArray() }; target = wall.AssemblyId; return;
        }
        var root = Root(id); if (root == null) return;
        candidate = layout with { Furniture = layout.Furniture.Where(f => f.Id != root.Id && f.DeskId != root.Id).ToImmutableArray(),
            WorkSeats = (layout.WorkSeats ?? []).Where(s => s.DeskId != root.Id).ToImmutableArray() }; target = root.Id;
    }
    private void SetSeat(IsoSetSeatCommand c)
    {
        var desk = Find(c.DeskId); var chair = Find(c.ChairId); var computer = c.ComputerId == null ? null : Find(c.ComputerId);
        if (desk == null || chair == null || c.ComputerId != null && computer == null) return;
        var children = layout.Furniture.Where(f => f.DeskId == desk.Id).ToArray();
        if (desk.AssetId != "iso/desk" || desk.DeskId != null || chair.AssetId != "iso/chair" || chair.DeskId != null && chair.DeskId != desk.Id ||
            !IsoEditorValidation.Aligned(chair, desk) || children.Any(f => f.AssetId == "iso/chair" && f.Id != chair.Id || f.AssetId == "iso/computer" && f.Id != c.ComputerId) ||
            computer != null && (computer.AssetId != "iso/computer" || computer.DeskId != null && computer.DeskId != desk.Id || !IsoEditorValidation.Aligned(computer, desk)))
        { SeatError(); return; }
        if ((layout.WorkSeats ?? []).Contains(new IsoSeatDesignation(desk.Id, desk.Id, chair.Id, computer?.Id))) { target = desk.Id; return; }
        candidate = layout with { Furniture = layout.Furniture.Select(f => f.Id == chair.Id || f.Id == computer?.Id ? f with { DeskId = desk.Id } : f).ToImmutableArray(),
            WorkSeats = (layout.WorkSeats ?? []).Where(s => s.DeskId != desk.Id).Append(new(desk.Id, desk.Id, chair.Id, computer?.Id)).ToImmutableArray() }; target = desk.Id;
    }
    private void ClearSeat(string id)
    {
        var desk = Find(id); if (desk == null) return;
        if (desk.AssetId != "iso/desk" || !IsoEditorValidation.CanonicalRotation(desk)) { SeatError(); return; }
        candidate = layout with { WorkSeats = (layout.WorkSeats ?? []).Where(s => s.DeskId != id).ToImmutableArray() }; target = id;
    }
    private void Wall(string? id, string? asset, string hostId, bool duplicate)
    {
        var walls = layout.WallSegments ?? []; IsoWallSegment[] original = [];
        if (id != null)
        {
            var edge = walls.FirstOrDefault(w => w.Id == id || w.AssemblyId == id);
            if (edge == null) { if (layout.Furniture.Any(f => f.Id == id)) WallError(); else Error("C001", $"Unknown iso instance '{id}'."); return; }
            if (edge.HostId == null || edge.AssemblyId == null) { WallError(); return; }
            original = walls.Where(w => w.AssemblyId == edge.AssemblyId).OrderBy(w => IsoEditorWallCatalog.Key(w.AssetId).Index).ToArray();
            asset = IsoEditorWallCatalog.Key(original[0].AssetId).Asset;
        }
        var definition = IsoEditorWallCatalog.Build(pack).FirstOrDefault(d => d.AssetId == asset);
        if (definition == null) { Error("C002", $"Unknown or unsupported iso asset '{asset}'."); return; }
        if (original.Length > 0 && !original.Select(w => w.AssetId).SequenceEqual(definition.ModuleIds)) { WallError(); return; }
        var host = walls.FirstOrDefault(w => w.Id == hostId && w.HostId == null);
        if (host == null || host.Face != definition.Face) { WallError(); return; }
        var hosts = definition.ModuleIds.Select((m, k) => walls.FirstOrDefault(w => w.HostId == null && w.Face == host.Face && w.Edge == IsoEditorWallCatalog.Edge(host.Edge, host.Face, k))).ToArray();
        for (var k = 0; k < hosts.Length; k++)
        {
            var h = hosts[k]; var module = pack.Manifest.WallModules!.First(m => m.Id == definition.ModuleIds[k]);
            if (h == null || (module.HostModule != null ? h.AssetId != module.HostModule : !h.AssetId.Contains("wall-plain-", StringComparison.Ordinal)) ||
                walls.Any(w => w.HostId == h.Id && (duplicate || !original.Contains(w)))) { WallError(); return; }
        }
        var assembly = original.Length == 0 || duplicate ? Allocate("decor") : original[0].AssemblyId!;
        if (!duplicate && original.Length > 0 && original.Select(w => w.HostId).SequenceEqual(hosts.Select(h => h!.Id)))
        { target = assembly; return; }
        var result = definition.ModuleIds.Select((m, k) => new IsoWallSegment(original.Length == 0 || duplicate ? Allocate("decor") : original[k].Id,
            host.Face, hosts[k]!.Edge, m, original.Length == 0 ? "default" : original[k].State, hosts[k]!.Id, assembly));
        candidate = layout with { WallSegments = walls.Where(w => duplicate || !original.Contains(w)).Concat(result).ToImmutableArray() }; target = assembly;
    }
}
