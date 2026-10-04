using System.Collections.Immutable;
using Vibedotcom.Core.Iso.Editing;

namespace Vibedotcom.Core.Iso;

public sealed partial class IsoOffice
{
    private IsoLayout? pendingLayout;
    private IsoNavigation? pendingNavigation;
    private long editorSceneOffset;
    private readonly Dictionary<Actor, Tile> editorRetreats = [];
    private readonly Dictionary<Pet, Tile> editorPetRetreats = [];

    private static IEnumerable<IsoPlacement> ResolveEditorDesks(IsoLayout layout) => layout.Furniture
        .Where(f => f.AssetId == "iso/desk" && (layout.WorkSeats == null || layout.WorkSeats.Any(s => s.DeskId == f.Id)))
        .OrderBy(f => f.Id, StringComparer.Ordinal);

    /// <summary>Spec §3.3/P003: validates a draft and defers geometry until the old rig is safely vacated.</summary>
    public ImmutableArray<IsoValidationIssue> ApplyEditorState(IsoEditorState state)
    {
        var owned = IsoEditorDocument.Freeze(state.Layout);
        var issues = IsoEditorValidation.Validate(owned, pack);
        if (issues.Any(i => i.Severity == IsoIssueSeverity.Error))
        { CancelEditorSwap(); return issues; }
        if (IsoEditorDocument.Equal(owned, Layout)) { CancelEditorSwap(); return issues; }
        pendingLayout = owned; pendingNavigation = IsoLayoutLoader.Navigation(owned, pack);
        editorRetreats.Clear(); editorPetRetreats.Clear();
        if (!PrepareEditorRetreats())
        {
            CancelEditorSwap();
            return IsoEditorValidation.Sort(issues.Append(IsoEditorValidation.Issue("L018", "The layout leaves no safe connected pet path.")));
        }
        CompleteEditorSwap(); nextDue = time; NextChangeSeconds = 0;
        return issues;
    }

    /// <summary>Spec §§3.3,7.4: pack preflight retains live actors, their designs, clocks and random streams.</summary>
    public ImmutableArray<IsoValidationIssue> UpdateEditorPack(IsoPack replacement)
    {
        if (ReferenceEquals(pack, replacement)) return [];
        var limits = IsoEditorValidation.PackLimits(replacement);
        if (!limits.IsEmpty) return limits;
        var problems = IsoEditorValidation.Validate(Layout, replacement).ToList();
        if (pendingLayout != null) problems.AddRange(IsoEditorValidation.Validate(pendingLayout, replacement));
        if (ordered.Any(a => !replacement.Characters.Any(d => d.Id == a.Design.Id)) ||
            pets.Any(p => replacement.Manifest.Pets?.Any(a => a.Id == p.AssetId) != true))
            problems.Add(IsoEditorValidation.Issue("C006", "The edit would replace an occupied slot or lose an asset reference."));
        if (problems.Any(i => i.Severity == IsoIssueSeverity.Error)) return IsoEditorValidation.Sort(problems);
        pack = replacement; editorSceneOffset = scene.Revision; builder = new(pack, Layout); nextDue = time; NextChangeSeconds = 0;
        return IsoEditorValidation.Sort(problems);
    }

    private void CancelEditorSwap()
    {
        pendingLayout = null; pendingNavigation = null;
        foreach (var a in editorRetreats.Keys)
        {
            a.Path.Clear(); a.PathTarget = null;
            a.Destination = a.DeskId != null ? seats[a.DeskId].Approach : a.Destination;
            if (a.Phase is not (Phase.Lower or Phase.Raise)) { SetPhase(a, Phase.WalkToSeat); SetAction(a, "walk"); }
        }
        editorRetreats.Clear(); editorPetRetreats.Clear(); nextDue = time; NextChangeSeconds = 0;
    }

    private bool RigChanged(string id)
    {
        if (pendingLayout == null) return false;
        var old = Layout.Furniture.Where(f => f.Id == id || f.DeskId == id).OrderBy(f => f.Id, StringComparer.Ordinal);
        var current = pendingLayout.Furniture.Where(f => f.Id == id || f.DeskId == id).OrderBy(f => f.Id, StringComparer.Ordinal);
        return !old.SequenceEqual(current) || !ResolveEditorDesks(pendingLayout).Any(f => f.Id == id);
    }
    private Tile PositionTile(Position p) => new(IsoGeometry.Snap(p.X), IsoGeometry.Snap(p.Y));
    private Tile? Retreat(Position position, Tile start, bool pet)
    {
        var oldReserved = seats.Values.Select(s => s.Approach).Where(t => t != start);
        var old = pet ? catNavigation : nav.WithBlocked(oldReserved);
        var newReserved = ResolveEditorDesks(pendingLayout!).Select(f => IsoContacts.Desk(f.X, f.Y, f.Rotation).Approach).ToHashSet();
        // The start may itself be newly blocked. The candidate's door component is authoritative.
        var newReachable = pet ? IsoEditorNavigation.PetComponent(pendingNavigation!, pendingLayout!.Door, newReserved).ToHashSet() :
            pendingNavigation!.WithBlocked(newReserved).Reachable(pendingLayout!.Door);
        return old.Reachable(start).Where(t => (!pet || t != Layout.Door) && newReachable.Contains(t) && !newReserved.Contains(t))
            .OrderBy(t => Math.Abs(t.X - position.X) + Math.Abs(t.Y - position.Y)).ThenBy(t => t.X).ThenBy(t => t.Y)
            .Select(t => (Tile?)t).FirstOrDefault();
    }
    private bool PrepareEditorRetreats()
    {
        if (pendingLayout == null) return true;
        var actorPlans = new Dictionary<Actor, Tile>(); var petPlans = new Dictionary<Pet, Tile>();
        var newSeatOwners = ResolveEditorDesks(pendingLayout).ToDictionary(f => IsoContacts.Desk(f.X, f.Y, f.Rotation).Approach, f => f.Id);
        var newSeats = newSeatOwners.Keys.ToHashSet();
        var petCells = IsoEditorNavigation.PetComponent(pendingNavigation!, pendingLayout.Door, newSeats).ToHashSet();
        foreach (var a in ordered.Where(a => a.Phase is not (Phase.Hidden or Phase.Enter)))
        {
            if (editorRetreats.ContainsKey(a)) continue;
            var start = a.Path.TryPeek(out var step) ? step : PositionTile(a.Position);
            var changed = a.DeskId != null && RigChanged(a.DeskId);
            var newlyReserved = newSeatOwners.TryGetValue(PositionTile(a.Position), out var owner) && owner != a.DeskId;
            if (!changed && !newlyReserved && pendingNavigation!.Walkable(PositionTile(a.Position)) && a.Path.All(pendingNavigation.Walkable)) continue;
            var retreat = Retreat(a.Position, start, false);
            if (retreat == null) return false;
            actorPlans[a] = retreat.Value;
        }
        foreach (var p in pets)
        {
            if (editorPetRetreats.ContainsKey(p)) continue;
            if (petCells.Contains(PositionTile(p.Position)) && p.Path.All(petCells.Contains)) continue;
            var start = p.Path.TryPeek(out var step) ? step : PositionTile(p.Position);
            var retreat = Retreat(p.Position, start, true); if (retreat == null) return false;
            petPlans[p] = retreat.Value;
        }
        foreach (var (a, target) in actorPlans)
        {
            editorRetreats[a] = target;
            if (a.Phase == Phase.Seated) { SetPhase(a, Phase.Raise); SetAction(a, "stand-up"); }
            else if (a.Phase is not (Phase.Lower or Phase.Raise)) { SetPhase(a, Phase.WalkToSeat); SetAction(a, "walk"); }
            a.PathTarget = null;
        }
        foreach (var (p, target) in petPlans)
        {
            var start = p.Path.TryPeek(out var step) ? step : PositionTile(p.Position);
            editorPetRetreats[p] = target;
            p.Path.Clear(); if (p.Position != new Position(start.X, start.Y)) p.Path.Enqueue(start);
            foreach (var t in catNavigation.FindPath(start, target)) p.Path.Enqueue(t);
        }
        return true;
    }
    private bool AdvanceEditorActor(Actor a, double elapsed)
    {
        if (!editorRetreats.TryGetValue(a, out var target)) return false;
        var remaining = elapsed;
        while (a.Phase is Phase.Lower or Phase.Raise)
        {
            var consume = Math.Min(remaining, Math.Max(0, .4 - a.PhaseTime));
            a.PhaseTime += consume; a.ActionTime += consume; remaining -= consume;
            if (a.PhaseTime + 1e-9 < .4) return true;
            if (a.Phase == Phase.Lower) { SetPhase(a, Phase.Raise); SetAction(a, "stand-up"); }
            else { SetPhase(a, Phase.WalkToSeat); SetAction(a, "walk"); a.PathTarget = null; }
        }
        if (a.Position != new Position(target.X, target.Y)) a.ActionTime += Walk(a, target, remaining, 2.5);
        if (a.Position == new Position(target.X, target.Y)) { SetPhase(a, Phase.Stand); SetAction(a, "idle"); }
        return true;
    }
    private bool AdvanceEditorPet(Pet p, double elapsed)
    {
        if (!editorPetRetreats.TryGetValue(p, out var target)) return false;
        var remaining = elapsed;
        p.Action = "walk";
        while (p.Path.TryPeek(out var next) && remaining > 0)
        {
            var dx = next.X - p.Position.X; var dy = next.Y - p.Position.Y; var distance = Math.Abs(dx) + Math.Abs(dy);
            if (distance < 1e-9) { p.Path.Dequeue(); continue; }
            p.Facing = IsoGeometry.Facing(dx == 0 ? 0 : Math.Sign(dx), dy == 0 ? 0 : Math.Sign(dy));
            var consumed = Math.Min(remaining, distance / 1.5); var travel = consumed * 1.5;
            p.Position = travel + 1e-9 >= distance ? new(next.X, next.Y) : new(p.Position.X + Math.Sign(dx) * travel, p.Position.Y + Math.Sign(dy) * travel);
            p.ActionTime += consumed; remaining -= consumed;
            if (p.Position == new Position(next.X, next.Y)) p.Path.Dequeue();
        }
        if (p.Position == new Position(target.X, target.Y)) p.Action = "sleep";
        return true;
    }
    private void CompleteEditorSwap()
    {
        if (pendingLayout == null || editorRetreats.Any(p => p.Key.Phase is Phase.Lower or Phase.Raise || p.Key.Position != new Position(p.Value.X, p.Value.Y)) ||
            editorPetRetreats.Any(p => p.Key.Position != new Position(p.Value.X, p.Value.Y))) return;
        Layout = pendingLayout; nav = pendingNavigation!; editorSceneOffset = scene.Revision; builder = new(pack, Layout);
        seats.Clear(); seatOrder.Clear();
        foreach (var f in ResolveEditorDesks(Layout)) { seats.Add(f.Id, IsoContacts.Desk(f.X, f.Y, f.Rotation)); seatOrder.Add(f.Id); }
        var reserved = seats.Values.Select(s => s.Approach).ToHashSet();
        queueTiles.Clear(); queueTiles.AddRange(nav.Reachable(Layout.Door).Where(t => !reserved.Contains(t) && t != Layout.Door)
            .OrderBy(t => Math.Abs(t.X - Layout.Door.X) + Math.Abs(t.Y - Layout.Door.Y)).ThenBy(t => t.X).ThenBy(t => t.Y));
        catNavigation = nav.WithBlocked(reserved.Append(Layout.Door));
        catTargets = IsoEditorNavigation.PetComponent(nav, Layout.Door, reserved);
        foreach (var a in ordered)
        {
            var displaced = editorRetreats.ContainsKey(a) || a.DeskId != null && !seats.ContainsKey(a.DeskId);
            if (a.DeskId != null && !seats.ContainsKey(a.DeskId)) a.DeskId = null;
            a.Path.Clear(); a.PathTarget = null;
            if (displaced && a.Phase != Phase.Hidden) { SetPhase(a, a.Gone ? Phase.Exit : Phase.WalkToSeat); SetAction(a, "walk"); }
            if (a.DeskId != null && a.Phase is Phase.WalkToSeat or Phase.Stand) a.Destination = seats[a.DeskId].Approach;
        }
        foreach (var p in pets) p.Path.Clear();
        pendingLayout = null; pendingNavigation = null; editorRetreats.Clear(); editorPetRetreats.Clear(); AssignSeats(); nextDue = time;
    }
}
