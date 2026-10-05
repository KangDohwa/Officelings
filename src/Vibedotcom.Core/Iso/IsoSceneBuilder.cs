namespace Vibedotcom.Core.Iso;

public sealed record IsoActorPose(string AgentId, string CharacterId, string Name, Position Position,
    string Facing, string Action, double ElapsedSeconds, string? DeskId = null, string? SeatPhase = null,
    int SeatPhaseFrame = 0, bool Blink = false, bool CarryingPaper = false, byte Alpha = 255);
public sealed record IsoCatPose(Position Position, string Facing, string Action, double ElapsedSeconds);
public sealed record IsoPetPose(string AssetId, Position Position, string Facing, string Action, double ElapsedSeconds);

/// Resolves pixels/contacts and orders the complete scene once. Consumers draw the returned list verbatim.
public sealed partial class IsoSceneBuilder
{
    internal sealed record Entry(IsoDrawItem Item, IsoDepthKey Key, bool Passthrough = false);
    private readonly IsoPack pack;
    private readonly IsoLayout layout;
    private readonly List<Entry> staticItems = [];
    private readonly List<Entry> work = [];
    private readonly List<int> animatedParts = [];
    private readonly List<IsoLabel> labels = [];
    private readonly Dictionary<string, (IsoPlacement Placement, IsoSeatProfile Profile)> seats = new(StringComparer.Ordinal);
    private readonly List<(IsoPlacement Placement, IsoPart Part)> screens = [];
    private readonly List<(IsoPlacement Placement, IsoPart Part, string Id)> animatedFurniture = [];
    private readonly Dictionary<string, string> hitKinds = new(StringComparer.Ordinal);
    private IsoScene? previous;
    private Entry[] hitItems = [];
    private readonly IsoPixel camera;
    private readonly int width, height;
    private readonly IsoFurnitureOrder furnitureOrder;
    public double NextChangeSeconds { get; private set; } = double.PositiveInfinity;
    public IsoPixel Camera => camera;
    public IsoSceneBuilder(IsoPack pack, IsoLayout layout)
        : this(pack, layout, true) { }
    internal IsoSceneBuilder(IsoPack pack, IsoLayout layout, bool validate)
    {
        this.pack = pack; this.layout = validate ? IsoLayoutLoader.Validate(layout, pack) : layout;
        enabledSeats = layout.WorkSeats?.Select(s => s.DeskId).ToHashSet(StringComparer.Ordinal);
        furnitureOrder = new(pack, layout);
        foreach (var f in layout.Furniture)
        {
            var v = pack.Variant(f.AssetId, f.Rotation, f.State);
            if (v.SeatProfiles?.FirstOrDefault(s => s.Id is "front" or "lounge") is {} s) seats.Add(f.Id, (f, s));
            hitKinds[f.Id] = f.AssetId == "iso/desk" ? "desk" : "furniture";
        }
        AddFloors(); AddWalls(); AddFurniture();
        var bounds = IsoGeometry.GroundBounds(layout.WidthTiles, layout.HeightTiles);
        // Standing heads and horizontal labels on every reachable edge fit a fixed camera extent.
        bounds = bounds.Include(new(bounds.Left - 32, -96, bounds.Right + 32, bounds.Bottom + 16));
        foreach (var e in staticItems)
        {
            var s = pack.Sheet(e.Item.SheetId);
            bounds = bounds.Include(new(e.Item.X, e.Item.Y, checked(e.Item.X + s.CellW), checked(e.Item.Y + s.CellH)));
        }
        IsoRenderBudget.Check(bounds.Left, bounds.Top, (long)bounds.Right + 16, (long)bounds.Bottom + 16);
        camera = new(checked(8 - bounds.Left), checked(8 - bounds.Top)); width = checked(bounds.Width + 16); height = checked(bounds.Height + 16);
        for (var k = 0; k < staticItems.Count; k++) staticItems[k] = staticItems[k] with
        { Item = staticItems[k].Item with { X = staticItems[k].Item.X + camera.X, Y = staticItems[k].Item.Y + camera.Y } };
        CacheSeatChairs();
    }
    private void AddFloors()
    {
        var floor = pack.Manifest.Sheets.First(s => s.Kind == "floor" && s.Rows!.Any(c => c.Name == "wood-i"));
        for (var j = 0; j < layout.HeightTiles; j++) for (var i = 0; i < layout.WidthTiles; i++)
        {
            var type = layout.Tiles?[j * layout.WidthTiles + i] ?? 1; if (type is 0 or 255) continue;
            var name = type == 2 ? ((i + j) % 2 == 0 ? "checker-dark" : "checker-light") : "wood-i";
            AddStatic("tile-" + i + "-" + j, IsoLayer.Floor, floor.Id, pack.Clip(floor.Id, name, "any"), i, j, IsoPass.Floor, 0, "base");
            if (type != 1) continue;
            bool Checker(int x, int y) => x >= 0 && y >= 0 && x < layout.WidthTiles && y < layout.HeightTiles && layout.Tiles?[y * layout.WidthTiles + x] == 2;
            foreach (var (di, dj, edge) in new[] { (1, 0, "pos-i"), (0, 1, "pos-j"), (-1, 0, "neg-i"), (0, -1, "neg-j") })
                if (Checker(i + di, j + dj)) AddStatic("tile-" + i + "-" + j, IsoLayer.Floor, floor.Id,
                    pack.Clip(floor.Id, "wood-checker-edge-" + edge, "any"), i, j, IsoPass.Floor, 1, edge);
            // Source transitionRule: repair each pair of adjacent active edges, after the edges.
            foreach (var c in floor.Rows!.Where(c => c.Transition && c.Name.Contains("corner")))
            {
                var (di, dj) = c.Name switch
                {
                    "wood-checker-corner-0-1" => (1, 1), "wood-checker-corner-1-2" => (-1, 1),
                    "wood-checker-corner-2-3" => (-1, -1), "wood-checker-corner-3-0" => (1, -1),
                    _ => throw new InvalidDataException("Unknown floor corner patch.")
                };
                if (Checker(i + di, j) && Checker(i, j + dj))
                    AddStatic("tile-" + i + "-" + j, IsoLayer.Floor, floor.Id, c, i, j, IsoPass.Floor, 2, c.Name);
            }
        }
    }
    private void AddWalls()
    {
        foreach (var w in layout.WallSegments ?? [])
        {
            var host = w.HostId == null ? w : layout.WallSegments!.Single(h => h.Id == w.HostId);
            var hostDepth = host.Edge.I + host.Edge.J;
            var module = pack.Manifest.WallModules!.First(m => m.Id == w.AssetId);
            for (var k = 0; k < module.Parts.Count; k++)
            {
                var p = module.Parts[k]; var c = pack.Clip(p.SheetId, p.Animation ?? p.Clip!, p.Direction);
                AddStatic(w.Id, IsoLayer.Wall, p.SheetId, c, w.Edge.I, w.Edge.J, IsoPass.RearWalls, (w.HostId == null ? 0 : 128) + k, k.ToString("D2"), hostDepth,
                    sourceAnchor: c.Anchor ?? c.EdgeAnchor ?? module.EdgeAnchor);
            }
        }
        var v3 = layout.WallSegments?.Any(w => w.AssetId == "iso/wall-plain-l-v3") == true;
        var corner = pack.Manifest.Sheets.FirstOrDefault(s => s.Kind == "wall" && s.Id.EndsWith("-v3", StringComparison.Ordinal) == v3 && s.Rows!.Any(c => c.Name == "inner-rear-corner"));
        if (corner != null) AddStatic("rear-corner", IsoLayer.Wall, corner.Id, pack.Clip(corner.Id, "inner-rear-corner", "any"), -.5, -.5, IsoPass.RearWalls, 30, "join");
        var wallSheet = pack.Manifest.Sheets.First(s => s.Kind == "wall" && s.Id.EndsWith("-v3", StringComparison.Ordinal) == v3 && s.Rows!.Any(c => c.Name == "plain-l"));
        // End-cap cells contain the far endpoint of a full wall span. Anchor
        // them at the last panel's start, as in the pack's wallJoins metadata.
        foreach (var (name, i, j) in new[] { ("end-cap-l", -.5, layout.HeightTiles - 1.5), ("end-cap-r", layout.WidthTiles - 1.5, -.5) })
            if (wallSheet.Rows!.FirstOrDefault(c => c.Name == name) is {} clip)
                AddStatic(name, IsoLayer.Wall, wallSheet.Id, clip, i, j, IsoPass.RearWalls, 30, "end");
    }
    private void AddFurniture()
    {
        foreach (var f in layout.Furniture)
        {
            var v = pack.Variant(f.AssetId, f.Rotation, f.State);
            var suppressed = SuppressedComputerParts(f);
            foreach (var p in v.Parts) if (!suppressed.Contains(PartIdentity(p))) AddFurniturePart(f, v, p);
            if (v.Shadow is {} shadow) AddFurniturePart(f, v, shadow, IsoLayer.Shadow, IsoPass.Shadows);
            if (v.Screen is {} screen && !suppressed.Contains(PartIdentity(screen)))
            {
                if (f.AssetId == "iso/desk" || f.AssetId == "iso/computer" && f.DeskId != null) screens.Add((f, screen));
                else animatedFurniture.Add((f, screen, "screen"));
            }
            if (v.Glow is {} glow && !suppressed.Contains(PartIdentity(glow))) animatedFurniture.Add((f, glow, "glow"));
        }
    }
    private void AddFurniturePart(IsoPlacement f, IsoFurnitureVariant v, IsoPart p, IsoLayer layer = IsoLayer.Furniture, IsoPass pass = IsoPass.Scene)
    {
        var c = pack.Clip(p.SheetId, p.Animation ?? p.Clip!, p.Direction);
        if (v.Layer == 0 && layer == IsoLayer.Furniture) pass = IsoPass.FloorProps;
        var d = p.DepthAnchor ?? c.DepthAnchor ?? new IsoGridPoint(v.FootprintW - .5, v.FootprintH - .5);
        var groundDepth = f.X + f.Y + d.I + d.J;
        var group = p.SeatGroupId ?? c.SeatGroupId;
        if (group != null && seats.TryGetValue(f.DeskId ?? f.Id, out var seat))
            groundDepth = seat.Placement.X + seat.Placement.Y + seat.Profile.SeatGround.I + seat.Profile.SeatGround.J;
        AddStatic(f.Id, layer, p.SheetId, c, f.X, f.Y, pass, p.IsoZOrder != 0 ? p.IsoZOrder : c.IsoZOrder,
            p.PartId ?? c.PartId ?? c.Key, groundDepth, p.HitTargetId ?? c.HitTargetId ?? (layer == IsoLayer.Furniture ? f.DeskId ?? f.Id : null),
            p.Anchor, p.HoverPassthrough, p.Frame ?? 0, (byte)Math.Round(p.Opacity * 255));
        if (p.Frame == null && c.Frames > 1) animatedParts.Add(staticItems.Count - 1);
    }
    private void AddStatic(string id, IsoLayer layer, string sheet, IsoClip clip, double i, double j,
        IsoPass pass, int rank, string part, double? depth = null, string? target = null, PixelPoint? sourceAnchor = null, bool passthrough = false, int frame = 0, byte alpha = 255)
    {
        var s = pack.Sheet(sheet); var a = sourceAnchor ?? clip.Anchor ?? clip.EdgeAnchor ?? s.Anchor; var ground = IsoGeometry.Ground(i, j);
        var key = new IsoDepthKey(pass, depth ?? i + j, rank, id, part);
        staticItems.Add(new(new(id + "/" + part, layer, sheet, clip.Key, frame, checked(ground.X - checked((int)a.X) + checked((int)(clip.FootOffset?.X ?? 0))),
            checked(ground.Y - checked((int)a.Y) + checked((int)(clip.FootOffset?.Y ?? 0))), key.Encoded, target, alpha), key, passthrough));
    }
    public IsoScene Build(IReadOnlyList<IsoActorPose> actors, IsoCatPose? cat = null, double elapsedSeconds = 0) =>
        BuildWithPets(actors, cat == null ? [] : [new("iso/cat", cat.Position, cat.Facing, cat.Action, cat.ElapsedSeconds)], elapsedSeconds);
    public IsoScene BuildWithPets(IReadOnlyList<IsoActorPose> actors, IReadOnlyList<IsoPetPose> pets, double elapsedSeconds = 0)
    {
        work.Clear(); work.AddRange(staticItems); labels.Clear(); NextChangeSeconds = double.PositiveInfinity;
        foreach (var slot in animatedParts)
        {
            var entry = work[slot]; var item = entry.Item; var clip = pack.Clip(item.SheetId, item.Clip);
            work[slot] = entry with { Item = item with { Frame = FrameAt(clip, elapsedSeconds) } };
            NextChangeSeconds = Math.Min(NextChangeSeconds, pack.NextFrameChange(item.SheetId, clip, elapsedSeconds));
        }
        foreach (var (f, original) in screens)
        {
            var owner = actors.FirstOrDefault(a => a.DeskId == (f.DeskId ?? f.Id));
            var state = owner == null ? "off" : owner.Action is "typing" or "seated-typing-front" ? "working" : "screensaver";
            var p = pack.Variant(f.AssetId, f.Rotation, state).Screen ?? original;
            var c = pack.Clip(p.SheetId, p.Animation ?? p.Clip!, p.Direction);
            var v = pack.Variant(f.AssetId, f.Rotation, state); var d = p.DepthAnchor ?? c.DepthAnchor ?? new IsoGridPoint(v.FootprintW - .5, v.FootprintH - .5);
            AddDynamic(f.Id, "screen", IsoLayer.Furniture, p.SheetId, c, IsoGeometry.Ground(f.X, f.Y), f.X + f.Y + d.I + d.J,
                p.IsoZOrder != 0 ? p.IsoZOrder : c.IsoZOrder, elapsedSeconds, f.DeskId ?? f.Id);
        }
        foreach (var (f, p, id) in animatedFurniture)
        {
            var c = pack.Clip(p.SheetId, p.Animation ?? p.Clip!, p.Direction);
            var d = p.DepthAnchor ?? c.DepthAnchor ?? new IsoGridPoint(0, 0);
            var ground = IsoGeometry.Ground(f.X, f.Y);
            var anchor = p.Anchor ?? c.Anchor ?? pack.Sheet(p.SheetId).Anchor;
            AddDynamic(f.Id, id, IsoLayer.Furniture, p.SheetId, c, ground, f.X + f.Y + d.I + d.J,
                p.IsoZOrder != 0 ? p.IsoZOrder : c.IsoZOrder, elapsedSeconds, f.Id,
                (byte)Math.Round(p.Opacity * 255), new(ground.X - (int)anchor.X, ground.Y - (int)anchor.Y), p.Frame);
        }
        foreach (var actor in actors) AddActor(actor);
        foreach (var pet in pets.Take(2)) AddPet(pet);
        SortWithSeatChairs();
        furnitureOrder.Sort(work, actors, pets);
        var changed = previous == null || previous.Items.Count != work.Count || previous.Labels.Count != labels.Count;
        if (!changed)
        {
            for (var k = 0; k < work.Count && !changed; k++)
            {
                var a = previous!.Items[k]; var b = work[k].Item;
                changed = a.InstanceId != b.InstanceId || a.Layer != b.Layer || a.X != b.X || a.Y != b.Y || a.Depth != b.Depth ||
                    a.HitTargetId != b.HitTargetId || a.Alpha != b.Alpha || !pack.SamePixels(a, b);
            }
            for (var k = 0; k < labels.Count && !changed; k++) changed = previous!.Labels[k] != labels[k];
        }
        if (!changed) return previous!;
        hitItems = work.ToArray();
        previous = new(width, height, Array.AsReadOnly(work.Select(e => e.Item).ToArray()), Array.AsReadOnly(labels.ToArray()), (previous?.Revision ?? 0) + 1);
        return previous;
    }
    private static int FrameAt(IsoClip c, double elapsed)
    {
        var tick = Math.Max(0, (long)Math.Floor(elapsed * c.Fps + 1e-9));
        return c.Loop ? (int)(tick % c.Frames) : (int)Math.Min(tick, c.Frames - 1);
    }
    private void AddDynamic(string id, string part, IsoLayer layer, string sheetId, IsoClip clip, IsoPixel ground,
        double depth, int rank, double elapsed, string? target, byte alpha = 255, IsoPixel? destination = null, int? frameOverride = null,
        IsoPass pass = IsoPass.Scene)
    {
        var s = pack.Sheet(sheetId); var a = clip.Anchor ?? clip.EdgeAnchor ?? s.Anchor; var key = new IsoDepthKey(pass, depth, rank, id, part);
        var frame = frameOverride ?? FrameAt(clip, elapsed);
        var dest = destination ?? new IsoPixel(checked(ground.X - checked((int)a.X) + checked((int)(clip.FootOffset?.X ?? 0))), checked(ground.Y - checked((int)a.Y) + checked((int)(clip.FootOffset?.Y ?? 0))));
        work.Add(new(new(id + "/" + part, layer, sheetId, clip.Key, frame, dest.X + camera.X, dest.Y + camera.Y, key.Encoded, target, alpha), key));
        if (frameOverride == null) NextChangeSeconds = Math.Min(NextChangeSeconds, pack.NextFrameChange(sheetId, clip, elapsed));
    }
    private void AddActor(IsoActorPose actor)
    {
        hitKinds[actor.AgentId] = "agent";
        var design = pack.Characters.First(c => c.Id == actor.CharacterId); var normal = pack.Sheet(design.SheetId);
        var clip = pack.Clip(normal.Id, actor.Action, actor.Facing); var frame = FrameAt(clip, actor.ElapsedSeconds);
        var normalClip = clip;
        var sheetId = normal.Id;
        if (actor.Blink && actor.Facing is "se" or "sw" && normal.Blink is {} blink &&
            pack.Sheet(blink.SheetId).Rows!.Any(c => blink.NormalRows[c.Row] == clip.Row))
        { sheetId = blink.SheetId; clip = pack.Clip(sheetId, clip.Key); }
        var ground = IsoGeometry.Ground(actor.Position.X, actor.Position.Y); var depth = actor.Position.X + actor.Position.Y;
        var dest = new IsoPixel(ground.X - 16, ground.Y - 60);
        var hasSeat = actor.DeskId != null && EnabledSeat(actor.DeskId) && seats.TryGetValue(actor.DeskId, out var seat);
        if (hasSeat && actor.SeatPhase != null)
        {
            var seatData = seats[actor.DeskId!]; var s = seatData.Profile; var placement = seatData.Placement;
            depth = placement.X + placement.Y + s.SeatGround.I + s.SeatGround.J;
            if (actor.SeatPhase is "lower" or "raise")
            {
                frame = Math.Clamp(actor.SeatPhaseFrame, 0, 3);
                var phase = clip.PhaseContacts?[frame] ?? throw new InvalidDataException("Transition lacks phaseContacts.");
                dest = new(ground.X + (int)phase.ContactOffset.X - (int)phase.SeatAnchor.X,
                    ground.Y + (int)phase.ContactOffset.Y - (int)phase.SeatAnchor.Y);
            }
            else if (actor.SeatPhase == "seated")
            {
                var source = clip.SeatAnchor ?? throw new InvalidDataException("Seated clip lacks pelvis anchor.");
                var origin = IsoGeometry.Ground(placement.X, placement.Y);
                dest = new(origin.X + (int)s.ContactOffset.X - (int)source.X, origin.Y + (int)s.ContactOffset.Y - (int)source.Y);
            }
        }
        if (!hasSeat || actor.SeatPhase is null or "turn")
            AddDynamic(actor.AgentId, "contact-shadow", IsoLayer.Shadow, IsoActorShadow.SheetId, pack.Clip(IsoActorShadow.SheetId, "contact/any"),
                ground, depth, 0, 0, null, actor.Alpha, pass: IsoPass.Shadows);
        SelectSeatChair(actor, normal.Id, normalClip, frame);
        AddDynamic(actor.AgentId, "body", IsoLayer.Character, sheetId, clip, ground, depth, 0, actor.ElapsedSeconds, actor.AgentId,
            actor.Alpha, dest, actor.SeatPhase is "lower" or "raise" ? frame : null);
        if (clip.Effect == "speech-bubble")
            AddDynamic(actor.AgentId, "effect", IsoLayer.Overlay, IsoSharedUi.SpeechSheetId, pack.Clip(IsoSharedUi.SpeechSheetId, "speech", "any"),
                ground, depth, 0, 0, actor.AgentId, actor.Alpha, new(dest.X + 30, dest.Y + pack.HeadTop(sheetId, clip, frame) - 2), pass: IsoPass.Overlay);
        else if (IsoSharedUi.Glyph(clip.Effect) is {} glyph)
            AddDynamic(actor.AgentId, "effect", IsoLayer.Overlay, IsoSharedUi.SheetId, pack.Clip(IsoSharedUi.SheetId, glyph, "any"),
                ground, depth, 0, 0, actor.AgentId, actor.Alpha, new(dest.X + 30, dest.Y + pack.HeadTop(sheetId, clip, frame) - 2), pass: IsoPass.Overlay);
        labels.Add(new(actor.AgentId, actor.Name, dest.X + 16 + camera.X, dest.Y + pack.HeadTop(sheetId, clip, frame) - 6 + camera.Y));
        if (actor.CarryingPaper || clip.Prop != null)
        {
            var propName = actor.CarryingPaper ? "report-paper" : clip.Prop!;
            var prop = pack.Manifest.CarriedProps!.FirstOrDefault(p => p.Animation == propName || p.Id == propName);
            if (prop != null)
            {
                var grip = clip.HandAnchorsByPhase?[frame].LastOrDefault() ?? clip.HandAnchors?.LastOrDefault();
                var contact = grip != null ? new IsoPixel((int)grip.X, (int)grip.Y) : IsoContacts.StandingGrip(actor.Facing);
                var p = pack.Clip(prop.SheetId, prop.Animation, actor.Facing);
                AddDynamic(actor.AgentId, "held-prop", IsoLayer.Prop, prop.SheetId, p, ground, depth, 1, actor.ElapsedSeconds, actor.AgentId,
                    actor.Alpha, new(dest.X + contact.X - (int)prop.SourceGrip.X, dest.Y + contact.Y - (int)prop.SourceGrip.Y));
            }
        }
    }
    private void AddPet(IsoPetPose cat)
    {
        var pet = pack.Manifest.Pets!.Single(p => p.Id == cat.AssetId); var c = pack.Clip(pet.SheetId, cat.Action, cat.Facing);
        var ground = IsoGeometry.Ground(cat.Position.X, cat.Position.Y); var d = cat.Position.X + cat.Position.Y;
        AddDynamic(pet.Id, "body", IsoLayer.Pet, pet.SheetId, c, ground, d, 0, cat.ElapsedSeconds, pet.Id);
        hitKinds[pet.Id] = "pet";
        if (pet.Shadow != null)
        {
            var shadow = pack.Clip(pet.Shadow.SheetId, cat.Action, cat.Facing);
            AddDynamic(pet.Id, "shadow", IsoLayer.Shadow, pet.Shadow.SheetId, shadow, ground, d, 0, cat.ElapsedSeconds, null, pass: IsoPass.Shadows);
        }
    }
    public Tile? PickTile(int sceneX, int sceneY)
    {
        var tile = IsoGeometry.Pick(sceneX - camera.X, sceneY - camera.Y);
        if (tile.X < 0 || tile.Y < 0 || tile.X >= layout.WidthTiles || tile.Y >= layout.HeightTiles || layout.Tiles?[tile.Y * layout.WidthTiles + tile.X] is 0 or 255) return null;
        return tile;
    }
    public IsoHit? HitTest(int sceneX, int sceneY)
    {
        for (var k = hitItems.Length - 1; k >= 0; k--)
        {
            var e = hitItems[k]; var item = e.Item;
            if (e.Passthrough || item.Layer is IsoLayer.Floor or IsoLayer.Shadow || !pack.Opaque(item, sceneX - item.X, sceneY - item.Y)) continue;
            return item.HitTargetId != null ? new(item.HitTargetId, hitKinds.GetValueOrDefault(item.HitTargetId, "furniture")) : null;
        }
        return null;
    }
}
