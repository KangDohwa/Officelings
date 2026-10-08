using System.Collections.Immutable;

namespace Officelings.Core.Iso.Editing;

internal static class IsoEditorValidation
{
    internal static ImmutableArray<IsoValidationIssue> PackLimits(IsoPack pack)
    {
        var decoded = pack.Images.Values.Sum(bytes => bytes.Length < 24 ? 0 :
            (long)System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16, 4)) *
            System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20, 4)) * 4);
        return pack.Manifest.Sheets.Count > 128 || pack.Manifest.Furniture?.Count > 512 || decoded > 128 * 1024 * 1024 ||
            System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(pack.Manifest, IsoEditorDocument.Json).Length > IsoEditorDocument.MaxBytes
            ? [Issue("O010", "The merged iso pack would exceed its limits.")] : [];
    }
    internal static IsoValidationIssue Issue(string rule, string message, params string[] targets) =>
        new(rule, IsoIssueSeverity.Error, message, targets.Where(t => !string.IsNullOrEmpty(t)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToImmutableArray());
    internal static ImmutableArray<IsoValidationIssue> Sort(IEnumerable<IsoValidationIssue> issues) => issues
        .DistinctBy(i => (i.RuleId, i.Severity, i.Message, string.Join("\0", i.TargetIds)))
        .OrderBy(i => i.RuleId, StringComparer.Ordinal).ThenBy(i => string.Join("\0", i.TargetIds), StringComparer.Ordinal)
        .ThenBy(i => i.Message, StringComparer.Ordinal).ToImmutableArray();
    internal static bool Structural(IsoValidationIssue i) => i.Severity == IsoIssueSeverity.Error && i.RuleId is not ("L012" or "L013" or "L014" or "L018");
    internal static ImmutableArray<IsoValidationIssue> Validate(IsoLayout l, IsoPack pack, bool scene = true)
    {
        var errors = new List<IsoValidationIssue>();
        var furniture = l.Furniture ?? []; var walls = l.WallSegments ?? [];
        // Reject resource limits before pairwise diagnostics or any scene/contact work.
        if (furniture.Count > 1024 || walls.Count > 128 || IsoEditorDocument.Bytes(l).Length > IsoEditorDocument.MaxBytes)
            return [Issue("L017", "Iso editor layout limits exceeded.")];
        var known = furniture.Select(f => f.Id).Concat(walls.Select(w => w.Id)).Concat(walls.Select(w => w.AssemblyId ?? "")).ToHashSet(StringComparer.Ordinal);
        void Add(string rule, string message, params string[] ids) => errors.Add(Issue(rule, message, ids.Where(known.Contains).ToArray()));
        var editor = l.EditorVersion != null || l.WorkSeats != null || l.RequiredCapabilities?.Contains("isoEditorV1") == true;
        if (l.Version != 1 || l.View != "iso" || l.Projection != "iso2to1" || l.Furniture == null ||
            l.RequiredCapabilities?.Any(c => c is not ("iso2to1" or "renderItemsV1" or "isoEditorV1")) == true ||
            editor && (l.EditorVersion != 1 || l.WorkSeats == null || new[] { "iso2to1", "renderItemsV1", "isoEditorV1" }.Any(c => l.RequiredCapabilities?.Contains(c) != true)) ||
            walls.Any(w => !double.IsFinite(w.Edge.I) || !double.IsFinite(w.Edge.J)) ||
            l.Tile?.Anchor is {} anchor && (!double.IsFinite(anchor.X) || !double.IsFinite(anchor.Y)))
            Add("L001", "Unsupported iso editor schema or capability.");
        if (l.WidthTiles != 10 || l.HeightTiles != 10 || l.TileSize != 16 || l.Cutaway != "rear-two-walls" ||
            l.Tile is {} tile && (tile.Width != 64 || tile.Height != 32 || tile.Anchor != null && tile.Anchor != new PixelPoint(32, 16)) ||
            l.WallRise is not (null or 16) || l.WallHeight is not (null or 48))
            Add("L002", "The iso editor requires a 10x10 room with 64x32 projection.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in furniture.Select(f => f.Id).Concat(walls.Select(w => w.Id)).Concat(walls.Where(w => w.AssemblyId != null).Select(w => w.AssemblyId!).Distinct(StringComparer.Ordinal)))
            if (string.IsNullOrWhiteSpace(id) || id.Length > 96 || !ids.Add(id)) Add("L003", $"Duplicate or invalid instance ID '{id}'.", id);
        bool Inside(Tile t) => t.X is >= 0 and < 10 && t.Y is >= 0 and < 10;
        var resolved = new Dictionary<string, (IsoPlacement F, IsoFurnitureVariant V)>(StringComparer.Ordinal);
        var blocking = new Dictionary<Tile, List<string>>();
        foreach (var f in furniture)
        {
            var v = IsoEditorDocument.Variant(pack, f);
            if (v == null || f.Orientation != null && f.Orientation != v.Orientation || f.Width != null && f.Width != v.FootprintW || f.Height != null && f.Height != v.FootprintH)
                Add("L004", $"Invalid authored variant for '{f.Id}'.", f.Id);
            if (v == null) continue;
            if (!string.IsNullOrWhiteSpace(f.Id)) resolved.TryAdd(f.Id, (f, v));
            if (v.FootprintW < 1 || v.FootprintH < 1 || !double.IsFinite(v.HeightPx)) Add("L004", $"Invalid authored variant for '{f.Id}'.", f.Id);
            if (f.X < 0 || f.Y < 0 || (long)f.X + v.FootprintW > 10 || (long)f.Y + v.FootprintH > 10 ||
                (v.Points ?? []).Any(p => !Inside(new(f.X + p.X, f.Y + p.Y))) ||
                (v.SeatProfiles ?? []).Any(s => !Inside(new(f.X + s.Approach.X, f.Y + s.Approach.Y))))
                Add("L005", $"Object or interaction '{f.Id}' is outside the room.", f.Id);
            foreach (var cell in IsoLayoutLoader.Cells(f, v))
            {
                if (!blocking.TryGetValue(cell, out var owners)) blocking[cell] = owners = [];
                foreach (var other in owners)
                {
                    var a = StringComparer.Ordinal.Compare(other, f.Id) < 0 ? other : f.Id; var b = a == other ? f.Id : other;
                    Add("L006", $"Blocking objects '{a}' and '{b}' overlap at ({cell.X},{cell.Y}).", a, b);
                }
                owners.Add(f.Id);
            }
        }
        var chairs = new Dictionary<Tile, string>();
        foreach (var f in furniture.Where(f => f.AssetId == "iso/chair"))
        {
            var cell = new Tile(f.X, f.Y);
            if (blocking.TryGetValue(cell, out var owners) || chairs.ContainsKey(cell))
                Add("L007", $"Chair '{f.Id}' overlaps physical furniture at ({cell.X},{cell.Y}).", (owners ?? []).Append(f.Id).Append(chairs.GetValueOrDefault(cell, "")).ToArray());
            chairs.TryAdd(cell, f.Id);
        }
        foreach (var (rug, rv) in resolved.Values.Where(p => p.F.AssetId == "iso/rug"))
        foreach (var (desk, dv) in resolved.Values.Where(p => p.F.AssetId == "iso/desk" && CanonicalRotation(p.F)))
        {
            var contact = IsoContacts.Desk(desk.X, desk.Y, desk.Rotation);
            if (IsoLayoutLoader.Cells(desk, dv).Append(contact.Approach).Any(t => t.X >= rug.X && (long)t.X < (long)rug.X + rv.FootprintW && t.Y >= rug.Y && (long)t.Y < (long)rug.Y + rv.FootprintH))
                Add("L008", $"Workstation '{desk.Id}' overlaps lounge rug '{rug.Id}'.", desk.Id, rug.Id);
        }
        foreach (var f in furniture.Where(f => f.DeskId != null))
        {
            var desk = furniture.FirstOrDefault(d => d.Id == f.DeskId);
            if (desk == null || desk.AssetId != "iso/desk" || desk.DeskId != null || f.AssetId is not ("iso/chair" or "iso/computer") ||
                furniture.Count(c => c.DeskId == f.DeskId && c.AssetId == f.AssetId) > 1 || !Aligned(f, desk))
                Add("L009", $"Invalid assembly link on '{f.Id}'.", f.Id, f.DeskId!);
        }
        var designations = IsoEditorDocument.Seats(l).ToArray(); var approaches = new List<(string Id, Tile Tile)>();
        foreach (var s in designations)
        {
            var desk = furniture.FirstOrDefault(f => f.Id == s.DeskId);
            var chair = furniture.FirstOrDefault(f => f.Id == s.ChairId);
            var computer = furniture.FirstOrDefault(f => f.Id == s.ComputerId);
            if (s.Id != s.DeskId || s.Profile != "front" || desk?.AssetId != "iso/desk" || !CanonicalRotation(desk) ||
                chair?.AssetId != "iso/chair" || chair.DeskId != s.DeskId || !Aligned(chair, desk) ||
                s.ComputerId != null && (computer?.AssetId != "iso/computer" || computer.DeskId != s.DeskId || !Aligned(computer, desk)) ||
                s.ComputerId == null && furniture.Any(f => f.DeskId == s.DeskId && f.AssetId == "iso/computer"))
                Add("L010", $"Work position '{s.Id}' does not match the canonical desk rig.", s.Id, s.DeskId, s.ChairId, s.ComputerId ?? "");
            if (desk?.AssetId == "iso/desk" && CanonicalRotation(desk)) approaches.Add((s.Id, IsoContacts.Desk(desk.X, desk.Y, desk.Rotation).Approach));
        }
        if (designations.Length > 12 || designations.Select(s => s.Id).Distinct(StringComparer.Ordinal).Count() != designations.Length ||
            designations.Select(s => s.DeskId).Distinct(StringComparer.Ordinal).Count() != designations.Length ||
            approaches.Select(a => a.Tile).Distinct().Count() != approaches.Count || approaches.Any(a => a.Tile == l.Door))
            Add("L011", "Work positions exceed 12 or share a desk, ID, approach or door.", designations.Select(s => s.Id).ToArray());
        var zones = l.Zones ?? [];
        if (new[] { "workspace", "pantry", "lounge", "door" }.Any(name => zones.Count(z => z.Name == name) != 1) ||
            zones.Select(z => z.Name).Distinct(StringComparer.Ordinal).Count() != zones.Count ||
            zones.Any(z => z.X < 0 || z.Y < 0 || z.Width <= 0 || z.Height <= 0 || (long)z.X + z.Width > 10 || (long)z.Y + z.Height > 10) ||
            l.Tiles != null && (l.Tiles.Count != 100 || l.Tiles.Any(t => t is not (0 or 1 or 2 or 255))))
            Add("L015", "Invalid iso tiles or required zones.");
        ValidateWalls(l, pack, errors);

        if (l.WidthTiles == 10 && l.HeightTiles == 10)
        {
            var voids = (l.Tiles ?? []).Select((t, k) => (t, k)).Where(p => p.t is 0 or 255).Select(p => new Tile(p.k % 10, p.k / 10));
            var nav = new IsoNavigation(10, 10, blocking.Keys.Concat(voids)); var reachable = nav.Reachable(l.Door);
            if (l.Door != new Tile(0, 4) || !nav.Walkable(l.Door) || zones.Count(z => z.Name == "door" && z.X == 0 && z.Y == 4 && z.Width == 1 && z.Height == 1) != 1 ||
                !walls.Any(w => w.HostId == null && w.Face == "left" && w.Edge == new IsoGridPoint(-.5, 3.5) && w.AssetId?.Contains("wall-open-door-l", StringComparison.Ordinal) == true && w.State is "open" or "default"))
                Add("L012", "The iso door and spawn must remain open and walkable.");
            foreach (var (f, v) in resolved.Values)
            {
                foreach (var p in v.Points ?? []) if (!reachable.Contains(new(f.X + p.X, f.Y + p.Y)))
                    Add("L013", $"Interaction or seat '{f.Id}' is unreachable from the door.", f.Id);
                foreach (var s in v.SeatProfiles ?? []) if (!reachable.Contains(new(f.X + s.Approach.X, f.Y + s.Approach.Y)))
                    Add("L013", $"Interaction or seat '{f.Id}' is unreachable from the door.", f.Id);
            }
            var aisle = nav.WithBlocked(approaches.Select(a => a.Tile)).Reachable(l.Door);
            foreach (var a in approaches)
                if (!Neighbors(a.Tile).Any(aisle.Contains)) Add("L014", $"Seat '{a.Id}' has no reachable aisle with other seats occupied.", a.Id);
            if (pack.Manifest.Pets?.Count > 0 && IsoEditorNavigation.PetComponent(nav, l.Door, approaches.Select(a => a.Tile)).Length < 2)
                Add("L018", "The layout leaves no safe connected pet path.");
        }
        ValidateParts(resolved.Values.Select(p => p.F), pack, errors);
        if (scene && !errors.Any(Structural)) ValidateScene(l, pack, errors);
        return Sort(errors);
    }
    internal static IEnumerable<Tile> Neighbors(Tile t) => new Tile[] { new(t.X - 1, t.Y), new(t.X + 1, t.Y), new(t.X, t.Y - 1), new(t.X, t.Y + 1) };
    internal static bool CanonicalRotation(IsoPlacement? f) => f != null && f.Rotation is 0 or 90 or 180 or 270 && f.SeatProfile == "front";
    internal static bool Aligned(IsoPlacement child, IsoPlacement desk)
    {
        if (!CanonicalRotation(desk)) return false;
        var contact = IsoContacts.Desk(desk.X, desk.Y, desk.Rotation);
        return child.AssetId == "iso/chair" ? new Tile(child.X, child.Y) == contact.Approach && child.Rotation == IsoGeometry.Rotation(contact.Facing) :
            child.AssetId == "iso/computer" && child.X == desk.X && child.Y == desk.Y && child.Rotation == desk.Rotation;
    }
    // The open-door module already defines an open door; authored files may label it "open" while the bundled layout says "default".
    private static string FixedState(IsoWallSegment w) => w.State == "open" && w.AssetId.Contains("open-door", StringComparison.Ordinal) ? "default" : w.State;
    private static void ValidateWalls(IsoLayout l, IsoPack pack, List<IsoValidationIssue> errors)
    {
        var walls = l.WallSegments ?? []; var definitions = IsoEditorWallCatalog.Build(pack);
        void Invalid(IsoWallSegment w) => errors.Add(Issue("L016", $"Invalid or occupied wall host for '{w.Id}'.", w.Id,
            walls.Any(h => h.Id == w.HostId) ? w.HostId! : "", w.AssemblyId ?? ""));
        foreach (var face in new[] { "left", "right" })
        for (var k = 0; k < 10; k++)
        {
            var edge = face == "left" ? new IsoGridPoint(-.5, k - .5) : new IsoGridPoint(k - .5, -.5);
            if (!walls.Any(w => w.HostId == null && w.Face == face && w.Edge == edge))
                errors.Add(Issue("L016", $"Invalid or occupied wall host for 'rear-{face}-{k}'."));
        }
        foreach (var w in walls)
        {
            var module = pack.Manifest.WallModules?.FirstOrDefault(m => m.Id == w.AssetId);
            var lattice = w.Face == "left" ? w.Edge.I == -.5 && w.Edge.J >= -.5 && w.Edge.J <= 8.5 && w.Edge.J + .5 == Math.Floor(w.Edge.J + .5) :
                w.Face == "right" && w.Edge.J == -.5 && w.Edge.I >= -.5 && w.Edge.I <= 8.5 && w.Edge.I + .5 == Math.Floor(w.Edge.I + .5);
            if (!lattice || module == null || module.Face != w.Face || module.Slot != null && w.HostId == null || w.HostId == null && w.AssemblyId != null)
            { Invalid(w); continue; }
            if (w.HostId == null)
            {
                var expected = (pack.Layout.WallSegments ?? []).FirstOrDefault(h => h.HostId == null && h.Face == w.Face && h.Edge == w.Edge);
                if (expected == null || w.AssetId != expected.AssetId || FixedState(w) != FixedState(expected) ||
                    walls.Count(h => h.HostId == null && h.Face == w.Face && h.Edge == w.Edge) > 1) Invalid(w);
                continue;
            }
            var host = walls.FirstOrDefault(h => h.Id == w.HostId && h.HostId == null);
            if (module.Slot == null || w.AssemblyId == null || host == null || host.Face != w.Face || host.Edge != w.Edge ||
                walls.Count(h => h.HostId == w.HostId) > 1 || (module.HostModule != null ? host.AssetId != module.HostModule : host.AssetId?.Contains("wall-plain-", StringComparison.Ordinal) != true)) Invalid(w);
        }
        foreach (var group in walls.Where(w => w.HostId != null).GroupBy(w => w.AssemblyId, StringComparer.Ordinal))
        {
            var run = group.OrderBy(w => IsoEditorWallCatalog.Key(w.AssetId).Index).ToArray();
            var def = definitions.FirstOrDefault(d => d.ModuleIds[0] == run[0].AssetId);
            if (def == null || !run.Select(w => w.AssetId).SequenceEqual(def.ModuleIds) ||
                run.Where((w, k) => w.Face != def.Face || w.Edge != IsoEditorWallCatalog.Edge(run[0].Edge, def.Face, k)).Any())
                foreach (var w in run) Invalid(w);
        }
    }
    private static void ValidateParts(IEnumerable<IsoPlacement> furniture, IsoPack pack, List<IsoValidationIssue> errors)
    {
        foreach (var f in furniture)
        {
            var v = IsoEditorDocument.Variant(pack, f)!;
            try
            {
                foreach (var p in v.Parts.Concat(new[] { v.Shadow, v.Screen, v.Glow }.OfType<IsoPart>()))
                {
                    var clip = pack.Clip(p.SheetId, p.Animation ?? p.Clip!, p.Direction); var sheet = pack.Sheet(p.SheetId);
                    var d = p.DepthAnchor ?? clip.DepthAnchor; var a = p.Anchor ?? clip.Anchor ?? sheet.Anchor;
                    if (d != null && (!double.IsFinite(d.Value.I) || !double.IsFinite(d.Value.J)) || !double.IsFinite(a.X) || !double.IsFinite(a.Y) || !IsoRenderBudget.AnchorFits(a, sheet.CellW, sheet.CellH) ||
                        p.IsoZOrder is < -512 or > 511 || p.SupportRank is < 0 or > 4 || !double.IsFinite(p.Opacity) || p.Opacity is < 0 or > 1 ||
                        p.Frame is {} frame && (frame < 0 || frame >= clip.Frames)) throw new InvalidDataException();
                }
            }
            catch (Exception e) when (e is InvalidDataException or ArgumentException or KeyNotFoundException)
            { errors.Add(Issue("L019", $"Invalid or cyclic scene depth for '{f.Id}'.", f.Id)); }
        }
    }
    private static void ValidateScene(IsoLayout l, IsoPack pack, List<IsoValidationIssue> errors)
    {
        try
        {
            var builder = new IsoSceneBuilder(pack, l, false);
            if (builder.EditorEmptyItemCount > 4096)
            {
                errors.Add(Issue("L017", "Iso editor layout limits exceeded."));
                return;
            }
            var empty = builder.Build([]);
            IsoEditorSceneValidation.ValidateAssemblies(l, pack, empty.Items.Count, errors);
        }
        catch (Exception e) when (e is InvalidDataException or ArgumentException or KeyNotFoundException or InvalidOperationException or OverflowException)
        {
            foreach (var id in l.Furniture.Select(f => f.Id).DefaultIfEmpty("scene")) errors.Add(Issue("L019", $"Invalid or cyclic scene depth for '{id}'.", id == "scene" ? "" : id));
        }
    }
}
