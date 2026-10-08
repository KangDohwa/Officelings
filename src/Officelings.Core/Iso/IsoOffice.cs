namespace Officelings.Core.Iso;

/// Desk-only seating simulation for AgentInfo inputs. Flat OfficeAnimator remains untouched.
public sealed partial class IsoOffice : IIsoOffice, Editing.IIsoEditorRuntime
{
    private enum Phase { Enter, WalkToSeat, Turn, Lower, Seated, Raise, Exit, Fade, Stand, Pickup, Carry, Handover, Hidden }
    private sealed class Actor(AgentInfo info, CharacterDesign design, Position position)
    {
        public AgentInfo Info = info;
        public readonly CharacterDesign Design = design;
        public Position Position = position;
        public string Facing = "se", Action = "walk";
        public string? DeskId;
        public Tile Destination;
        public Queue<Tile> Path = new();
        public Tile? PathTarget;
        public Phase Phase = Phase.WalkToSeat;
        public double PhaseTime, ActionTime, NextBlink = 3, BlinkUntil, EntryRemaining;
        public bool Gone, ReportDone;
        public AgentState LastState = info.State;
        public readonly Random BlinkRandom = new(unchecked((int)SpriteAtlas.Hash(info.Id)));
    }
    private sealed class Pet(string assetId, Position position, int seed)
    {
        public readonly string AssetId = assetId;
        public readonly Random Random = new(seed);
        public Position Position = position;
        public Queue<Tile> Path = new();
        public string Facing = "sw", Action = "sleep";
        public double ActionTime, Remaining = 8;
    }
    private IsoPack pack;
    private IsoSceneBuilder builder;
    private IsoNavigation nav;
    private IsoNavigation catNavigation;
    private Tile[] catTargets;
    private readonly Dictionary<string, Actor> actors = new(StringComparer.Ordinal);
    private readonly List<Actor> ordered = [];
    private readonly HashSet<string> present = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IsoSeatContact> seats = new(StringComparer.Ordinal);
    private readonly List<string> seatOrder = [];
    private readonly List<Tile> queueTiles = [];
    private readonly List<IsoActorPose> poses = [];
    private readonly List<Pet> pets = [];
    private readonly List<IsoPetPose> petPoses = [];
    private IsoScene scene;
    private double time, simulatedTime, nextDue;
    private double nextEntrance;
    private bool petsEnabled=true,petVisibilityChanged;
    public bool PetsEnabled
    {
        get=>petsEnabled;
        set { if(petsEnabled==value) return; petsEnabled=value; petVisibilityChanged=true; }
    }
    public IsoLayout Layout { get; private set; }
    public double NextChangeSeconds { get; private set; }
    public IsoOffice(IsoPack pack, int seed) : this(pack, seed, IsoTwelveSeatFixture.OfficeLayout(pack)) { }
    public IsoOffice(IsoPack pack, int seed, IsoLayout layout)
    {
        this.pack = pack; Layout = IsoLayoutLoader.Validate(layout, pack);
        nav = IsoLayoutLoader.Navigation(layout, pack); builder = new(pack, layout);
        foreach (var f in ResolveEditorDesks(layout))
        { seats.Add(f.Id, IsoContacts.Desk(f.X, f.Y, f.Rotation, f.SeatProfile)); seatOrder.Add(f.Id); }
        var reserved = seats.Values.Select(s => s.Approach).ToHashSet();
        queueTiles.AddRange(nav.Reachable(layout.Door).Where(t => !reserved.Contains(t) && t != layout.Door)
            .OrderBy(t => Math.Abs(t.X - layout.Door.X) + Math.Abs(t.Y - layout.Door.Y)).ThenBy(t => t.X).ThenBy(t => t.Y));
        catNavigation = nav.WithBlocked(reserved);
        var catTile = queueTiles.FirstOrDefault(t => t.X >= 2 && t.Y >= 6 && catNavigation.Walkable(t), layout.Door);
        catTargets = catNavigation.Reachable(catTile).OrderBy(t => t.X).ThenBy(t => t.Y).ToArray();
        if (layout.WorkSeats != null)
        {
            catNavigation = catNavigation.WithBlocked([layout.Door]);
            catTargets = Editing.IsoEditorNavigation.PetComponent(nav, layout.Door, reserved);
            catTile = catTargets.FirstOrDefault(t => t.X >= 2 && t.Y >= 6, catTargets.FirstOrDefault());
        }
        var pool = (pack.Manifest.Pets ?? []).OrderBy(p => p.Id, StringComparer.Ordinal).ToList();
        var selection = new Random(seed);
        while (pool.Count > 0 && pets.Count < 2 && catTargets.Length > pets.Count)
        {
            var index = selection.Next(pool.Count); var asset = pool[index]; pool.RemoveAt(index);
            var tile = pets.Count == 0 ? catTile : catTargets.First(t => t != catTile);
            pets.Add(new(asset.Id, new(tile.X, tile.Y), seed ^ unchecked((int)SpriteAtlas.Hash(asset.Id))));
        }
        RefreshPetPoses(); scene = builder.BuildWithPets([], petPoses);
        NextChangeSeconds = Math.Min(builder.NextChangeSeconds, pets.Count == 0 ? double.PositiveInfinity : 8); nextDue = NextChangeSeconds;
    }
    public IsoScene Update(IReadOnlyList<AgentInfo> agents, DateTimeOffset now, double deltaSeconds)
    {
        if (!double.IsFinite(deltaSeconds) || deltaSeconds < 0) throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
        time += deltaSeconds;
        var external = Synchronize(agents) || petVisibilityChanged || pendingLayout != null;
        petVisibilityChanged=false;
        if (!external && time + 1e-9 < nextDue)
        { NextChangeSeconds = Math.Max(0, nextDue - time); return scene; }
        var elapsed = time - simulatedTime; simulatedTime = time;
        AssignSeats();
        if (pendingLayout == null) foreach (var actor in ordered) Advance(actor, elapsed);
        else
        {
            if (!PrepareEditorRetreats()) CancelEditorSwap();
            foreach (var actor in ordered) if (!AdvanceEditorActor(actor, elapsed)) Advance(actor, elapsed);
        }
        AssignSeats();
        if (pendingLayout == null) { if (petsEnabled) foreach (var pet in pets) AdvancePet(pet, elapsed); }
        else foreach (var pet in pets) if (!AdvanceEditorPet(pet, elapsed) && petsEnabled) AdvancePet(pet, elapsed);
        if (pendingLayout != null && !PrepareEditorRetreats()) CancelEditorSwap();
        CompleteEditorSwap();
        poses.Clear(); var next = double.PositiveInfinity;
        foreach (var a in ordered)
        {
            if (a.Phase is Phase.Hidden or Phase.Enter)
            { next=Math.Min(next,NextActorChange(a)); continue; }
            var seated = a.Phase is Phase.Turn or Phase.Lower or Phase.Seated or Phase.Raise;
            var phase = a.Phase switch { Phase.Turn => "turn", Phase.Lower => "lower", Phase.Raise => "raise", Phase.Seated => "seated", _ => null };
            var phaseFrame = a.Phase is Phase.Lower or Phase.Raise ? Math.Clamp((int)Math.Floor(a.PhaseTime * 10 + 1e-9), 0, 3) : 0;
            var blink = time < a.BlinkUntil && a.Phase is Phase.Seated or Phase.Stand;
            var alpha = a.Phase == Phase.Fade ? (byte)Math.Clamp(IsoGeometry.Snap(255 * (1 - a.PhaseTime / .3)), 0, 255) : (byte)255;
            poses.Add(new(a.Info.Id, a.Design.Id, a.Info.Name, a.Position, a.Facing, a.Action, a.ActionTime,
                a.DeskId, phase, phaseFrame, blink, a.Phase is Phase.Carry or Phase.Handover, alpha));
            next = Math.Min(next, NextActorChange(a));
        }
        RefreshPetPoses(); scene = builder.BuildWithPets(poses, petPoses, time);
        if (editorSceneOffset != 0) scene = scene with { Revision = scene.Revision + editorSceneOffset };
        next = Math.Min(next, builder.NextChangeSeconds);
        if (editorPetRetreats.Count > 0) next = Math.Min(next, 1d / 30);
        if(petsEnabled) foreach (var pet in pets) next = Math.Min(next,pet.Action == "walk" ? 1d / 30 : pet.Remaining);
        NextChangeSeconds = Math.Max(0, next); nextDue = time + NextChangeSeconds;
        return scene;
    }
    private bool Synchronize(IReadOnlyList<AgentInfo> incoming)
    {
        present.Clear(); var changed = false;
        for (var index = 0; index < incoming.Count; index++)
        {
            var info = incoming[index];
            if (!present.Add(info.Id)) throw new ArgumentException("Duplicate agent ID.");
            if (!actors.TryGetValue(info.Id, out var a)) { changed = true; continue; }
            if (a.Info.State != info.State || a.Info.Name != info.Name || a.Info.ParentId != info.ParentId || a.Gone != (info.State == AgentState.Gone)) changed = true;
            var returning = a.Gone && info.State != AgentState.Gone;
            a.Info = info; a.Gone = info.State == AgentState.Gone;
            if (returning && a.Phase is Phase.Exit or Phase.Fade or Phase.Hidden)
            {
                a.Destination = a.DeskId != null ? seats[a.DeskId].Approach : a.Destination;
                if (a.Phase == Phase.Hidden) a.Position = new(Layout.Door.X, Layout.Door.Y);
                SetPhase(a, Phase.WalkToSeat); SetAction(a, "walk"); a.PathTarget = null;
            }
            if (a.LastState != info.State) { a.ReportDone = false; a.LastState = info.State; }
        }
        // Ordinal newcomer order + uint32 FNV-1a and unused-design probing match the flat roster.
        if (changed)
        {
            foreach (var info in incoming.Where(i => !actors.ContainsKey(i.Id) && i.State != AgentState.Gone).OrderBy(i => i.Id, StringComparer.Ordinal))
            {
                var design = pack.Assign(info, ordered.Where(a => a.Phase != Phase.Hidden).Select(a => a.Design.Id));
                var a = new Actor(info, design, new(Layout.Door.X, Layout.Door.Y));
                // A poll can discover a dozen sessions at once. Admit them
                // through the one-tile door at walking-distance intervals.
                a.EntryRemaining=Math.Max(0,nextEntrance-time);
                if (a.EntryRemaining>0) a.Phase=Phase.Enter;
                nextEntrance=Math.Max(time,nextEntrance)+.4;
                a.NextBlink = time + 3 + a.BlinkRandom.NextDouble() * 3;
                actors.Add(info.Id, a); ordered.Add(a);
            }
            ordered.Sort((a, b) => StringComparer.Ordinal.Compare(a.Info.Id, b.Info.Id));
        }
        foreach (var a in ordered)
            if (!present.Contains(a.Info.Id) && !a.Gone) { a.Gone = true; changed = true; }
        return changed;
    }
    private void AssignSeats()
    {
        var reserved = ordered.Where(a => a.DeskId != null).Select(a => a.DeskId!).ToHashSet(StringComparer.Ordinal);
        var usedQueue = ordered.Where(a => a.DeskId == null && a.Phase != Phase.Hidden).Select(a => a.Destination).ToHashSet();
        foreach (var a in ordered)
        {
            if (a.Gone || a.DeskId != null || a.Phase==Phase.Enter) continue;
            var seat = seatOrder.FirstOrDefault(s => !reserved.Contains(s));
            if (seat != null)
            {
                a.DeskId = seat; a.Destination = seats[seat].Approach; reserved.Add(seat);
                SetPhase(a, Phase.WalkToSeat); SetAction(a, "walk"); a.PathTarget = null;
            }
            else if (a.Phase == Phase.Hidden)
            { a.Position = new(Layout.Door.X, Layout.Door.Y); SetPhase(a, Phase.WalkToSeat); }
            if (a.DeskId == null && (a.Destination == default || a.Phase == Phase.WalkToSeat && a.PathTarget == null))
            {
                // Overflow has standing destinations, never an invented seated desk clip.
                a.Destination = queueTiles.FirstOrDefault(t => !usedQueue.Contains(t), Layout.Door); usedQueue.Add(a.Destination);
            }
        }
    }
    private void Advance(Actor a, double dt)
    {
        if (a.Phase == Phase.Hidden && a.Gone) return;
        if (a.Phase==Phase.Enter)
        {
            if (a.Gone) { SetPhase(a,Phase.Hidden); return; }
            var consumed=Math.Min(dt,a.EntryRemaining); a.EntryRemaining-=consumed; dt-=consumed;
            if (a.EntryRemaining>1e-9) return;
            SetPhase(a,Phase.WalkToSeat); AssignSeats();
        }
        var remaining = dt; var transitions = 0;
        while (++transitions <= 32)
        {
            var wantsLeaveSeat = a.Gone || a.Info.State == AgentState.Reporting && !a.ReportDone;
            if (a.Phase == Phase.Seated && wantsLeaveSeat) { SetPhase(a, Phase.Raise); SetAction(a, "stand-up"); }
            else if (wantsLeaveSeat && a.Phase is Phase.WalkToSeat or Phase.Stand or Phase.Turn)
            { SetPhase(a, a.Gone ? Phase.Exit : Phase.Pickup); SetAction(a, a.Gone ? "walk" : "pickup"); }
            if (a.Phase is Phase.WalkToSeat or Phase.Exit or Phase.Carry)
            {
                var destination = a.Phase == Phase.Exit ? Layout.Door : a.Destination;
                var used = Walk(a, destination, remaining, 2.5); a.ActionTime += used; remaining -= used;
                if (a.Position != new Position(destination.X, destination.Y)) break;
                if (a.Phase == Phase.Exit) { SetPhase(a, Phase.Fade); SetAction(a, "idle"); }
                else if (a.Phase == Phase.Carry) { SetPhase(a, Phase.Handover); SetAction(a, "report-handover"); }
                else if (a.DeskId != null) { a.Facing = seats[a.DeskId].Facing; SetPhase(a, Phase.Turn); SetAction(a, "idle"); }
                else { SetPhase(a, Phase.Stand); SetAction(a, "idle"); }
                continue;
            }
            if (a.Phase == Phase.Lower || a.Phase == Phase.Raise || a.Phase == Phase.Turn || a.Phase == Phase.Fade ||
                a.Phase == Phase.Pickup || a.Phase == Phase.Handover)
            {
                var duration = a.Phase switch { Phase.Turn => .12, Phase.Lower or Phase.Raise => .4, Phase.Fade => .3, _ => 4d / 6 };
                var consume = Math.Min(remaining, Math.Max(0, duration - a.PhaseTime));
                a.PhaseTime += consume; a.ActionTime += consume; remaining -= consume;
                if (a.PhaseTime + 1e-9 < duration) break;
                switch (a.Phase)
                {
                    case Phase.Turn: SetPhase(a, Phase.Lower); SetAction(a, "sit-down"); break;
                    case Phase.Lower: SetPhase(a, Phase.Seated); SetAction(a, SeatAction(a.Info.State)); break;
                    case Phase.Raise:
                        if (a.Gone) { SetPhase(a, Phase.Exit); SetAction(a, "walk"); }
                        else if (a.Info.State == AgentState.Reporting && !a.ReportDone) { SetPhase(a, Phase.Pickup); SetAction(a, "pickup"); }
                        else { a.Destination = a.DeskId != null ? seats[a.DeskId].Approach : a.Destination; SetPhase(a, Phase.WalkToSeat); SetAction(a, "walk"); a.PathTarget = null; }
                        break;
                    case Phase.Fade: SetPhase(a, Phase.Hidden); a.DeskId = null; a.Path.Clear(); a.PathTarget = null; break;
                    case Phase.Pickup:
                        a.Destination = ReportDestination(a); SetPhase(a, Phase.Carry); SetAction(a, "walk"); a.PathTarget = null; break;
                    case Phase.Handover:
                        a.ReportDone = true; a.Destination = a.DeskId != null ? seats[a.DeskId].Approach : a.Destination;
                        SetPhase(a, a.DeskId != null ? Phase.WalkToSeat : Phase.Stand); SetAction(a, a.DeskId != null ? "walk" : "idle"); a.PathTarget = null; break;
                }
                if (a.Phase == Phase.Hidden) break;
                continue;
            }
            if (a.Phase == Phase.Seated) SetAction(a, SeatAction(a.Info.State));
            if (a.Phase == Phase.Stand) SetAction(a, a.Info.State switch { AgentState.Waiting => "standing-waiting", AgentState.Thinking => "standing-thinking", _ => "idle" });
            a.ActionTime += remaining; a.PhaseTime += remaining; break;
        }
        if (a.Phase is Phase.Seated or Phase.Stand && a.Facing is "se" or "sw")
        {
            if (time >= a.NextBlink)
            { a.BlinkUntil = time + .1; a.NextBlink = time + 3 + a.BlinkRandom.NextDouble() * 3; }
        }
        else { a.BlinkUntil = 0; a.NextBlink = Math.Max(a.NextBlink, time + 3); }
    }
    private static string SeatAction(AgentState state) => state switch
    { AgentState.Working => "typing", AgentState.Thinking => "thinking", AgentState.Waiting => "seated-waiting", _ => "seated-idle" };
    private static void SetPhase(Actor a, Phase phase) { a.Phase = phase; a.PhaseTime = 0; }
    private static void SetAction(Actor a, string action) { if (a.Action != action) { a.Action = action; a.ActionTime = 0; } }
    private double Walk(Actor a, Tile target, double dt, double speed)
    {
        if (a.PathTarget != target)
        {
            var start = a.Path.TryPeek(out var oldNext) ? oldNext : new Tile(IsoGeometry.Snap(a.Position.X), IsoGeometry.Snap(a.Position.Y));
            a.Path.Clear(); if (a.Position != new Position(start.X, start.Y)) a.Path.Enqueue(start);
            var routes = Layout.WorkSeats == null ? nav : nav.WithBlocked(seats.Values.Select(s => s.Approach).Where(t => t != target && t != start));
            foreach (var step in routes.FindPath(start, target)) a.Path.Enqueue(step);
            a.PathTarget = target;
        }
        var consumed = 0d;
        while (a.Path.TryPeek(out var next) && consumed < dt)
        {
            var dx = next.X - a.Position.X; var dy = next.Y - a.Position.Y;
            var distance = Math.Abs(dx) + Math.Abs(dy);
            if (distance < 1e-9) { a.Position = new(next.X, next.Y); a.Path.Dequeue(); continue; }
            a.Facing = IsoGeometry.Facing(dx == 0 ? 0 : Math.Sign(dx), dy == 0 ? 0 : Math.Sign(dy));
            var duration = Math.Min(dt - consumed, distance / speed); var travel = duration * speed;
            if (travel + 1e-9 >= distance) { a.Position = new(next.X, next.Y); a.Path.Dequeue(); }
            else a.Position = new(a.Position.X + Math.Sign(dx) * travel, a.Position.Y + Math.Sign(dy) * travel);
            consumed += duration;
        }
        SetAction(a, "walk"); return consumed;
    }
    private Tile ReportDestination(Actor a)
    {
        var target = a.Info.ParentId != null && actors.TryGetValue(a.Info.ParentId, out var parent) ?
            new Tile(IsoGeometry.Snap(parent.Position.X), IsoGeometry.Snap(parent.Position.Y)) : Layout.Door;
        var reserved = seats.Values.Select(s => s.Approach).ToHashSet();
        return queueTiles.Where(t => !reserved.Contains(t)).OrderBy(t => Math.Abs(t.X - target.X) + Math.Abs(t.Y - target.Y))
            .ThenBy(t => t.X).ThenBy(t => t.Y).FirstOrDefault(Layout.Door);
    }
    private double NextActorChange(Actor a)
    {
        var next = a.Phase switch
        {
            Phase.Enter=>Math.Max(0,a.EntryRemaining),
            Phase.WalkToSeat or Phase.Carry or Phase.Exit or Phase.Fade => 1d / 30,
            Phase.Turn => Math.Max(0, .12 - a.PhaseTime),
            Phase.Lower or Phase.Raise => Math.Max(0, (Math.Floor(a.PhaseTime * 10 + 1e-9) + 1) / 10 - a.PhaseTime),
            Phase.Pickup or Phase.Handover => Math.Max(0, 4d / 6 - a.PhaseTime), _ => double.PositiveInfinity
        };
        if (a.Phase is Phase.Seated or Phase.Stand && a.Facing is "se" or "sw")
            next = Math.Min(next, Math.Max(0, time < a.BlinkUntil ? a.BlinkUntil - time : a.NextBlink - time));
        return next;
    }
    private void RefreshPetPoses()
    {
        petPoses.Clear();
        if (petsEnabled) foreach (var pet in pets) petPoses.Add(new(pet.AssetId, pet.Position, pet.Facing, pet.Action, pet.ActionTime));
    }
    private void AdvancePet(Pet cat, double dt)
    {
        var remaining = dt; var loops = 0;
        while (remaining > 0 && ++loops < 64)
        {
            if (cat.Action == "walk")
            {
                if (!cat.Path.TryPeek(out var next))
                { cat.Action = "sleep"; cat.ActionTime = 0; cat.Remaining = 8 + cat.Random.NextDouble() * 12; continue; }
                var dx = next.X - cat.Position.X; var dy = next.Y - cat.Position.Y; var distance = Math.Abs(dx) + Math.Abs(dy);
                if (distance < 1e-9) { cat.Path.Dequeue(); continue; }
                cat.Facing = IsoGeometry.Facing(dx == 0 ? 0 : Math.Sign(dx), dy == 0 ? 0 : Math.Sign(dy));
                var consume = Math.Min(remaining, distance / 1.4); cat.ActionTime += consume; remaining -= consume;
                if (consume * 1.4 + 1e-9 >= distance) { cat.Position = new(next.X, next.Y); cat.Path.Dequeue(); }
                else cat.Position = new(cat.Position.X + Math.Sign(dx) * consume * 1.4, cat.Position.Y + Math.Sign(dy) * consume * 1.4);
            }
            else
            {
                var consume = Math.Min(remaining, cat.Remaining); cat.ActionTime += consume; cat.Remaining -= consume; remaining -= consume;
                if (cat.Remaining > 1e-9) break;
                if (catTargets.Length < 2) { cat.Remaining = 8; continue; }
                var index = cat.Random.Next(catTargets.Length); var current = new Tile(IsoGeometry.Snap(cat.Position.X), IsoGeometry.Snap(cat.Position.Y));
                if (catTargets[index] == current) index = (index + 1) % catTargets.Length;
                var target = catTargets[index];
                foreach (var t in catNavigation.FindPath(current, target)) cat.Path.Enqueue(t);
                cat.Action = "walk"; cat.ActionTime = 0;
            }
        }
    }
    public IsoHit? HitTest(int sceneX, int sceneY) => builder.HitTest(sceneX, sceneY);
    public Tile? PickTile(int sceneX, int sceneY) => builder.PickTile(sceneX, sceneY);
    public IsoPixel Camera => builder.Camera;
}
