namespace Vibedotcom.Core;

public sealed class OfficeAnimator
{
    private sealed class Character(int order, AgentInfo agent)
    {
        public readonly int Order = order;
        public AgentInfo Agent = agent;
        public string? DeskId, OverflowSeatId;
        public Tile WorkTile;
        public string WorkFacing = "up";
        public CharacterDesign Design = new("fallback-0", "fallback");
        public Position Position;
        public Tile? Target;
        public Queue<Tile> Path = new();
        public AgentState? ObservedState;
        public string Action = "entering", Facing = "down";
        public bool Entering = true, Visible = true;
        public bool Suppressed;
        // 1: fetch, 2: pickup, 3: carry, 4: handover, 5: delivered.
        public int ReportStage;
        public string Leisure = "phone";
        public string LeisureFacing = "down";
        public string LastAction = "";
        public DateTimeOffset ActionSince;
        public bool LeisureArrived;
        public int LeisureSeconds;
        public Tile LeisureTarget;
        public string? ChatPartner;
        public DateTimeOffset PauseUntil, NextLeisure, FadeSince, PaperUntil, ReactUntil;
        public Random BlinkRandom = new(0);
        public DateTimeOffset NextBlink, BlinkUntil;
        public string? SeatedVariant, SeatPhase, ChairBackId, ChairFrontId;
        public string SeatedFacing = "up";
        public PixelPoint? SeatAnchor;
        public DateTimeOffset SeatPhaseSince;
        public int SeatPhaseFrame;
        public string DisplayName = "", ProviderIcon = "";
        public AgentInfo? NamedAgent;
        public AgentView? View;
    }

    private readonly Dictionary<string, Character> characters = [];
    private readonly Dictionary<string, (string Desk, DateTimeOffset LastSeen)> deskHistory = new(StringComparer.Ordinal);
    private readonly HashSet<string> ids = new(StringComparer.Ordinal);
    private readonly List<string> removedIds = [];
    private readonly List<AgentInfo> newcomers = [];
    private readonly Dictionary<string, Character> furnitureOwners = new(StringComparer.Ordinal);
    private readonly Dictionary<(Furniture Furniture, string State), bool> monitorSupport = [];
    private readonly Dictionary<(Tile Start, Tile Target), IReadOnlyList<Tile>> paths = [];
    private (Furniture Furniture, InteractionPoint Point)[] desks = [];
    private HashSet<Tile> workReservations = [];
    private bool workplacesDirty = true;
    private IReadOnlyList<AgentView> views = [];
    private double furnitureFps;
    private readonly Random random;
    private HashSet<Tile> obstacles = [];
    private HashSet<Tile> reachable = [];
    private readonly AssetPack assets;
    private OfficeLayout source;
    private DateTimeOffset started, lastUpdated;
    private int nextOrder;
    private static readonly string[] LeisureActions = ["coffee", "tv", "phone", "napping", "chatting", "water"];
    public OfficeLayout Layout { get; private set; }
    public OfficeLayout SourceLayout => source;
    public IReadOnlyList<FurnitureSprite> FurnitureSprites { get; private set; } = [];
    public bool HasMovingCharacters { get; private set; }
    public TimeSpan AnimationInterval { get; private set; }
    public DateTimeOffset NextUpdateAt { get; private set; } = DateTimeOffset.MaxValue;
    public OfficeAnimator(int? seed = null, AssetPack? assets = null, OfficeLayout? layout = null)
    {
        random = seed.HasValue ? new(seed.Value) : new();
        this.assets = assets ?? new();
        source = LayoutLoader.Prepare(layout ?? LayoutLoader.Load(this.assets, null), this.assets);
        Layout = source;
        RefreshNavigation(); RefreshFurniture(default); RefreshCadence(default);
    }

    // Preview permits temporary collision/reachability errors; save calls Validate separately.
    public void ReloadLayout(OfficeLayout layout, bool validate = false)
    {
        var prepared = LayoutLoader.Prepare(layout, assets, validate);
        source = prepared; Layout = source;
        RefreshNavigation(); AssignWorkplaces();
        foreach (var c in characters.Values)
        {
            c.Target = null; c.Path.Clear(); c.NextLeisure = default; c.ChatPartner = null; c.LeisureArrived = false;
            if (c.SeatPhase != null && (Queued(c) || c.WorkFacing != c.SeatedFacing || Contact(c) != c.SeatAnchor))
            { c.SeatPhase = "raise"; c.SeatPhaseSince = lastUpdated; c.SeatPhaseFrame = 0; }
            var tile = new Tile((int)Math.Round(c.Position.X), (int)Math.Round(c.Position.Y));
            if (!reachable.Contains(tile)) { tile = NearestFree(tile); c.Position = new(tile.X, tile.Y); }
        }
        RefreshFurniture(lastUpdated);
        RefreshCadence(lastUpdated);
    }

    public IReadOnlyList<AgentView> Update(IReadOnlyList<AgentInfo> agents, DateTimeOffset now, double deltaSeconds)
    {
        if (started == default) started = now;
        lastUpdated = now;
        ids.Clear(); newcomers.Clear(); removedIds.Clear();
        foreach (var agent in agents)
            if (ids.Add(agent.Id) && !characters.ContainsKey(agent.Id)) newcomers.Add(agent);
        foreach (var id in characters.Keys)
            if (!ids.Contains(id)) removedIds.Add(id);
        foreach (var id in removedIds) { characters.Remove(id); workplacesDirty = true; }
        newcomers.Sort((a, b) => StringComparer.Ordinal.Compare(a.Id, b.Id));
        foreach (var agent in newcomers)
        {
            var c = new Character(nextOrder++, agent) { Design = assets.Assign(agent, characters.Values.Select(c => c.Design.Id)),
                BlinkRandom = new(unchecked((int)SpriteAtlas.Hash(agent.Id))) };
            characters[agent.Id] = c; workplacesDirty = true;
        }
        foreach (var agent in agents)
        {
            var c = characters[agent.Id];
            if (c.Agent.ParentId != agent.ParentId || (c.Agent.State == AgentState.Gone) != (agent.State == AgentState.Gone)) workplacesDirty = true;
            c.Agent = agent;
        }
        AssignWorkplaces();
        foreach (var id in ids)
            if (deskHistory.TryGetValue(id, out var history)) deskHistory[id] = (history.Desk, now);
        if (deskHistory.Count > 1024)
        {
            var departed = deskHistory.Keys.Where(id => !ids.Contains(id)).OrderBy(id => deskHistory[id].LastSeen).ToArray();
            foreach (var id in departed.Take(Math.Max(0, departed.Length - 1024))) deskHistory.Remove(id);
        }
        foreach (var agent in newcomers) characters[agent.Id].Position = new(Layout.Door.X, Layout.Door.Y);

        foreach (var agent in agents)
        {
            var c = characters[agent.Id]; var desk = c.WorkTile;
            if (c.Suppressed)
            { c.Entering = true; c.Target = null; c.Path.Clear(); ClearSeat(c); c.Action = "waiting"; continue; }
            if (agent.State != c.ObservedState)
            {
                if (agent.State == AgentState.Reporting) { c.ReportStage = 1; c.PauseUntil = default; }
                else if (agent.State is not AgentState.Idle) c.ReportStage = 0;
                c.NextLeisure = default;
                c.LeisureArrived = false;
                if (agent.State != AgentState.Gone) c.FadeSince = default;
                if (agent.State != AgentState.Gone && !c.Visible)
                { c.Position = new(Layout.Door.X, Layout.Door.Y); c.Visible = c.Entering = true; c.FadeSince = default; }
            }
            c.ObservedState = agent.State;
            var reporting = c.ReportStage is >= 1 and <= 4;
            var idle = agent.State == AgentState.Idle || agent.State == AgentState.Reporting && c.ReportStage == 5;
            if (idle && !reporting && !c.Entering && !Queued(c) && now >= c.NextLeisure) ChooseLeisure(agent, agents, now);
            if (c.ChatPartner != null && (!characters.TryGetValue(c.ChatPartner, out var partner) || partner.ObservedState != AgentState.Idle))
            { c.ChatPartner = null; c.NextLeisure = default; }

            Tile target; string action;
            if (agent.State == AgentState.Gone) { c.Entering = false; target = Layout.Door; action = "exiting"; }
            else if (c.Entering) { target = desk; action = "entering"; }
            else if (Queued(c)) { target = desk; action = "waiting"; }
            else if (reporting)
            {
                var parentDesk = agent.ParentId != null && characters.TryGetValue(agent.ParentId, out var parent) && parent.Agent.State != AgentState.Gone ? parent.WorkTile : desk;
                target = c.ReportStage <= 2 ? desk : NearestFree(new(parentDesk.X + 1, parentDesk.Y));
                action = c.ReportStage <= 2 ? "pickup" : "reporting";
            }
            else if (idle) { target = c.LeisureTarget; action = c.Leisure; }
            else
            {
                target = desk;
                action = agent.State switch { AgentState.Working => "typing", AgentState.Thinking => "thinking", _ => "waiting" };
            }
            var wantsSeat = !Queued(c) && agent.State is AgentState.Working or AgentState.Thinking or AgentState.Waiting;
            var standingUp = false;
            if (c.SeatPhase != null && (!wantsSeat || c.Position != new Position(desk.X, desk.Y) || c.SeatPhase == "raise"))
            {
                if (c.SeatPhase != "raise") { c.SeatPhase = "raise"; c.SeatPhaseSince = now; }
                if ((now - c.SeatPhaseSince).TotalMilliseconds < 240)
                { standingUp = true; c.SeatPhaseFrame = (now - c.SeatPhaseSince).TotalMilliseconds < 120 ? 0 : 1; }
                else ClearSeat(c);
            }
            if (!standingUp && c.Target != target)
            {
                c.Target = target;
                var start = NearestFree(new((int)Math.Round(c.Position.X), (int)Math.Round(c.Position.Y)));
                // Finish the fraction of this tile edge before following the new route.
                // Keeping the exact position matters when a break/exit interrupts a walk.
                var path = PathFrom(start, target);
                c.Path.Clear();
                if (c.Position != new Position(start.X, start.Y)) c.Path.Enqueue(start);
                foreach (var step in path) c.Path.Enqueue(step);
            }
            var budget = standingUp ? 0 : Math.Clamp(deltaSeconds, 0, 1) * 3.5;
            while (budget > 0 && c.Path.TryPeek(out var next))
            {
                var dx = next.X - c.Position.X; var dy = next.Y - c.Position.Y;
                c.Facing = dx != 0 ? dx < 0 ? "left" : "right" : dy < 0 ? "up" : "down";
                var distance = Math.Abs(dx) + Math.Abs(dy);
                if (distance <= budget) { c.Position = new(next.X, next.Y); budget -= distance; c.Path.Dequeue(); }
                else { c.Position = new(c.Position.X + Math.Sign(dx) * budget, c.Position.Y + Math.Sign(dy) * budget); budget = 0; }
            }
            var arrived = c.Path.Count == 0 && c.Position == new Position(target.X, target.Y);
            c.Action = arrived ? action : agent.State == AgentState.Gone ? "exiting" : c.Entering ? "entering" : "walking";
            if (arrived && !standingUp)
            {
                if (c.Entering) { c.Entering = false; c.Target = null; }
                else if (agent.State == AgentState.Gone)
                {
                    if (c.FadeSince == default) c.FadeSince = now;
                    if ((now - c.FadeSince).TotalSeconds >= 1.2) c.Visible = false;
                }
                else if (Queued(c)) c.Facing = "down";
                else if (reporting)
                {
                    c.Facing = c.ReportStage <= 2 ? c.WorkFacing : "left";
                    if (c.ReportStage == 1) { c.ReportStage = 2; c.PauseUntil = now.AddSeconds(.9); }
                    else if (c.ReportStage == 2 && now >= c.PauseUntil) { c.ReportStage = 3; c.Target = null; }
                    else if (c.ReportStage == 3)
                    {
                        c.ReportStage = 4; c.PauseUntil = now.AddSeconds(1.4);
                        if (agent.ParentId != null && characters.TryGetValue(agent.ParentId, out var parent) && parent.Agent.State != AgentState.Gone)
                        { parent.PaperUntil = now.AddSeconds(12); parent.ReactUntil = now.AddSeconds(3); }
                        else c.PaperUntil = now.AddSeconds(12);
                    }
                    else if (c.ReportStage == 4 && now >= c.PauseUntil)
                    { c.ReportStage = 5; c.NextLeisure = default; c.Target = null; }
                }
                else if (!idle) c.Facing = c.WorkFacing;
                else
                {
                    c.Facing = c.LeisureFacing;
                    if (!c.LeisureArrived)
                    {
                        c.LeisureArrived = true; c.NextLeisure = now.AddSeconds(c.LeisureSeconds);
                        if (c.ChatPartner != null && characters.TryGetValue(c.ChatPartner, out var p))
                        { c.NextLeisure = DateTimeOffset.MaxValue; if (p.LeisureArrived) c.NextLeisure = p.NextLeisure = now.AddSeconds(c.LeisureSeconds); }
                    }
                }
            }
            if (arrived && !standingUp && wantsSeat && !c.Entering)
            {
                c.Action = agent.State switch { AgentState.Working => "typing", AgentState.Thinking => "thinking", _ => "waiting" };
                c.Facing = c.WorkFacing;
                var variant = assets.SeatedVariant(c.Design, c.WorkFacing);
                if (variant != null && c.SeatPhase == null)
                {
                    c.SeatedVariant = variant; c.SeatedFacing = c.WorkFacing; c.SeatPhase = "turn"; c.SeatPhaseSince = now;
                    var host = WorkFurniture(c);
                    var v = host == null ? null : assets.Variant(host);
                    c.SeatAnchor = Contact(c);
                    c.ChairBackId = v?.ChairBackId; c.ChairFrontId = v?.ChairFrontId;
                }
                if (c.SeatPhase is "turn" or "lower")
                {
                    var elapsed = (now - c.SeatPhaseSince).TotalMilliseconds;
                    if (elapsed >= 360) { c.SeatPhase = "seated"; c.SeatPhaseFrame = 0; }
                    else if (elapsed >= 120) { c.SeatPhase = "lower"; c.SeatPhaseFrame = elapsed < 240 ? 0 : 1; }
                }
            }
            if (!c.Visible) c.Action = "gone";
            if (c.Action != c.LastAction) { c.LastAction = c.Action; c.ActionSince = now; }
            var still = arrived && c.Visible && c.Action is not ("entering" or "walking" or "exiting" or "gone" or "napping");
            if (!still || c.NextBlink == default)
            {
                c.BlinkUntil = default;
                c.NextBlink = now.AddSeconds(3 + c.BlinkRandom.NextDouble() * 3);
            }
            else if (now >= c.NextBlink)
            {
                c.BlinkUntil = now.AddMilliseconds(150);
                c.NextBlink = now.AddSeconds(3 + c.BlinkRandom.NextDouble() * 3);
            }
        }
        foreach (var (id, c) in characters)
            if (c.ChatPartner != null && (!characters.TryGetValue(c.ChatPartner, out var partner) || partner.ChatPartner != id))
            { c.ChatPartner = null; c.NextLeisure = default; if (c.Action == "chatting") c.Action = "phone"; }
        RefreshFurniture(now);
        RefreshCadence(now);
        AgentView[]? nextViews = views.Count == agents.Count ? null : new AgentView[agents.Count];
        for (var i = 0; i < agents.Count; i++)
        {
            var a = agents[i];
            var c = characters[a.Id];
            if (c.NamedAgent == null || c.NamedAgent.Name != a.Name || c.NamedAgent.Role != a.Role || c.NamedAgent.Model != a.Model ||
                c.NamedAgent.Provider != a.Provider || c.NamedAgent.IsSubagent != a.IsSubagent)
            { c.DisplayName = DisplayName(a); c.ProviderIcon = a.Provider.ToString().ToLowerInvariant(); }
            c.NamedAgent = a;
            var sprite = assets.CharacterFrame(a, c.Design, c.Action, c.Facing, (long)(now - c.ActionSince).TotalMilliseconds, now < c.BlinkUntil);
            if (c.SeatedVariant != null && c.SeatAnchor is {} seat)
            {
                c.Facing = c.SeatedFacing;
                if (c.SeatPhase == "turn") sprite = assets.Frame(c.Design.SheetId, "idle", c.SeatedFacing, 0);
                else
                {
                    var seated = assets.SeatedFrame(c.SeatPhase == "seated" ? a : a with { State = AgentState.Waiting }, c.Design,
                        c.SeatedVariant, c.SeatedFacing, (long)(now - c.ActionSince).TotalMilliseconds, now < c.BlinkUntil);
                    sprite = seated.Frame;
                    var rise = c.SeatPhase == "lower" ? c.SeatPhaseFrame == 0 ? -2 : -1 :
                        c.SeatPhase == "raise" ? c.SeatPhaseFrame == 0 ? -1 : -3 : 0;
                    sprite = sprite with { OffsetX = seat.X - (c.Position.X + .5) * 16 + (sprite.AnchorX - seated.Anchor.X) * sprite.Scale,
                        OffsetY = seat.Y - (c.Position.Y + 1) * 16 + (sprite.AnchorY - seated.Anchor.Y) * sprite.Scale + rise };
                }
            }
            var pose = new Pose(c.Position.X, c.Position.Y, c.WorkTile, c.Action, c.Facing,
                sprite.Row, sprite.Column, c.Visible && !c.Suppressed,
                c.FadeSince == default ? 1 : Math.Clamp(1 - (now - c.FadeSince).TotalSeconds / 1.2, 0, 1),
                c.ReportStage is 3 or 4, c.DeskId != null && now < c.PaperUntil, Queued(c) ? "…" : now < c.ReactUntil ? "!" : null,
                c.ChatPartner, c.Design.Id, sprite, c.DeskId, c.OverflowSeatId, WorkLocation(c), Queued(c), c.DisplayName,
                c.ProviderIcon, c.SeatedVariant, c.SeatAnchor, c.ChairBackId, c.ChairFrontId, c.SeatPhase, c.SeatPhaseFrame);
            if (c.View == null || !ReferenceEquals(c.View.Agent, a) || c.View.Pose != pose) c.View = new(a, pose);
            if (nextViews == null && !ReferenceEquals(views[i], c.View))
            {
                nextViews = new AgentView[agents.Count];
                for (var j = 0; j < i; j++) nextViews[j] = views[j];
            }
            if (nextViews != null) nextViews[i] = c.View;
        }
        if (nextViews != null) views = nextViews;
        return views;
    }

    private void ChooseLeisure(AgentInfo agent, IReadOnlyList<AgentInfo> agents, DateTimeOffset now)
    {
        var c = characters[agent.Id];
        if (c.ChatPartner != null && characters.TryGetValue(c.ChatPartner, out var former) && former.ChatPartner == agent.Id)
        { former.ChatPartner = null; former.NextLeisure = default; if (former.Action == "chatting") former.Action = "phone"; }
        c.ChatPartner = null;
        var candidates = LeisureActions.OrderBy(_ => random.Next()).Where(s => s != c.Leisure).ToArray();
        foreach (var activity in candidates)
        {
            var reserved = WorkReservations();
            var spots = assets.Points(Layout).Where(p => p.Point.Action == activity && reachable.Contains(p.Tile) && !reserved.Contains(p.Tile)).ToArray();
            var available = spots.Where(p => !characters.Values.Any(v => v != c &&
                (v.ObservedState == AgentState.Idle || v.ObservedState == AgentState.Reporting && v.ReportStage == 5) && v.LeisureTarget == p.Tile)).ToArray();
            if (available.Length == 0 || activity == "chatting" && available.Length < 2) continue;
            if (activity == "chatting")
            {
                var friend = agents.FirstOrDefault(a => a.Id != agent.Id && a.State == AgentState.Idle &&
                    !characters[a.Id].Entering && !Queued(characters[a.Id]) && characters[a.Id].ChatPartner == null && characters[a.Id].ReportStage is 0 or 5);
                if (friend == null || characters.Values.Any(v => v != c && v.Leisure == "chatting" && v.ChatPartner != null)) continue;
                var p = characters[friend.Id];
                c.ChatPartner = friend.Id; p.ChatPartner = agent.Id; p.Leisure = "chatting";
                p.LeisureTarget = available[1].Tile; p.LeisureFacing = available[1].Point.Facing;
                p.NextLeisure = DateTimeOffset.MaxValue; p.Target = null; p.LeisureArrived = false; p.LeisureSeconds = 16;
            }
            c.Leisure = activity; c.LeisureTarget = available[0].Tile; c.LeisureFacing = available[0].Point.Facing; c.Target = null;
            c.LeisureArrived = false; c.NextLeisure = DateTimeOffset.MaxValue;
            c.LeisureSeconds = activity switch { "coffee" or "water" => random.Next(10, 19), "napping" => random.Next(28, 43), "tv" => random.Next(20, 34), _ => random.Next(14, 25) };
            if (c.ChatPartner != null) characters[c.ChatPartner].LeisureSeconds = c.LeisureSeconds;
            return;
        }
        var lounge = Layout.Zones!.First(z => z.Name == "lounge");
        var unavailable = WorkReservations();
        unavailable.UnionWith(characters.Values.Where(v => v != c && v.LeisureArrived).Select(v => v.LeisureTarget));
        c.Leisure = "phone"; c.LeisureTarget = NearestFree(new(lounge.X + random.Next(lounge.Width), lounge.Y + random.Next(lounge.Height)), unavailable);
        c.LeisureFacing = "down"; c.LeisureSeconds = 15; c.LeisureArrived = false;
        c.NextLeisure = DateTimeOffset.MaxValue; c.Target = null;
    }

    private static bool Queued(Character c) => c.Agent.State != AgentState.Gone && c.DeskId == null && c.OverflowSeatId == null;
    private static string WorkLocation(Character c) => c.DeskId != null ? "desk" : c.OverflowSeatId != null ? "overflow" : "queue";
    private HashSet<Tile> WorkReservations() => characters.Values.Where(c => c.Agent.State != AgentState.Gone && !c.Suppressed).Select(c => c.WorkTile).ToHashSet();

    private void AssignWorkplaces()
    {
        if (workplacesDirty)
        {
            if (characters.Count == 0) workReservations.Clear();
            else RebuildWorkplaces();
            workplacesDirty = false;
        }
        CheckLeisureReservations();
    }

    private void RebuildWorkplaces()
    {
        var overflow = source.OverflowSeats ?? [];
        var usedDesks = new HashSet<string>(StringComparer.Ordinal);
        var usedOverflow = new HashSet<string>(StringComparer.Ordinal);
        var usedTiles = new HashSet<Tile>();
        Tile? Reserve(Tile tile)
        {
            var free = reachable.Contains(tile) && !usedTiles.Contains(tile) ? tile :
                reachable.Where(t => !usedTiles.Contains(t)).OrderBy(t => Distance(t, tile)).ThenBy(t => t.Y).ThenBy(t => t.X)
                    .Select(t => (Tile?)t).FirstOrDefault();
            if (free is {} found) usedTiles.Add(found);
            return free;
        }
        Tile DeskTile(Furniture f, InteractionPoint p) => new(f.X + p.X, f.Y + p.Y);
        foreach (var c in characters.Values.OrderBy(c => c.Order))
        {
            c.Suppressed = false;
            if (c.Agent.State == AgentState.Gone) { SetWorkplace(c, null, null, Layout.Door, "down"); continue; }
            var desk = desks.FirstOrDefault(d => d.Furniture.DeskId == c.DeskId && c.DeskId != null);
            var seat = overflow.FirstOrDefault(s => s.Id == c.OverflowSeatId);
            if (desk.Furniture != null && !usedDesks.Contains(desk.Furniture.DeskId!) && Reserve(DeskTile(desk.Furniture, desk.Point)) is {} deskTile)
            { usedDesks.Add(desk.Furniture.DeskId!); SetWorkplace(c, desk.Furniture.DeskId, null, deskTile, desk.Point.Facing); }
            else if (seat != null && !usedOverflow.Contains(seat.Id) && Reserve(new(seat.X, seat.Y)) is {} seatTile)
            { usedOverflow.Add(seat.Id); SetWorkplace(c, null, seat.Id, seatTile, seat.Facing); }
            else SetWorkplace(c, null, null, c.WorkTile, "down");
        }
        var pending = characters.Values.Where(c => Queued(c)).OrderBy(c => c.Order).ToList();
        while (pending.Count > 0)
        {
            // Parents take their place first even when a child sorts before them by ID.
            var c = pending.FirstOrDefault(c => !pending.Any(p => p.Agent.Id == c.Agent.ParentId)) ?? pending[0];
            pending.Remove(c);
            var free = desks.Where(d => !usedDesks.Contains(d.Furniture.DeskId!) && !usedTiles.Contains(DeskTile(d.Furniture, d.Point)) && reachable.Contains(DeskTile(d.Furniture, d.Point))).ToArray();
            var chosen = deskHistory.TryGetValue(c.Agent.Id, out var previous) ? free.FirstOrDefault(d => d.Furniture.DeskId == previous.Desk) : default;
            var parent = c.Agent.ParentId != null && characters.TryGetValue(c.Agent.ParentId, out var p) && p.Agent.State != AgentState.Gone ? p : null;
            if (chosen.Furniture == null && free.Length > 0)
                chosen = parent == null ? free[0] : free.MinBy(d => Distance(DeskTile(d.Furniture, d.Point), parent.WorkTile));
            if (chosen.Furniture != null)
            {
                var id = chosen.Furniture.DeskId!; usedDesks.Add(id); deskHistory[c.Agent.Id] = (id, lastUpdated);
                usedTiles.Add(DeskTile(chosen.Furniture, chosen.Point));
                SetWorkplace(c, id, null, DeskTile(chosen.Furniture, chosen.Point), chosen.Point.Facing);
                continue;
            }
            var freeOverflow = overflow.Where(s => !usedOverflow.Contains(s.Id) && !usedTiles.Contains(new(s.X, s.Y)) && reachable.Contains(new(s.X, s.Y))).ToArray();
            var extra = parent == null ? freeOverflow.FirstOrDefault() : freeOverflow.MinBy(s => Distance(new(s.X, s.Y), parent.WorkTile));
            if (extra != null)
            {
                usedOverflow.Add(extra.Id); usedTiles.Add(new(extra.X, extra.Y)); SetWorkplace(c, null, extra.Id, new(extra.X, extra.Y), extra.Facing);
            }
        }
        if (characters.Values.Any(Queued))
        {
            var seats = desks.Select(d => DeskTile(d.Furniture, d.Point)).Concat(overflow.Select(s => new Tile(s.X, s.Y))).ToHashSet();
            var queueTiles = reachable.Where(t => t != Layout.Door && !usedTiles.Contains(t) && !seats.Contains(t))
                .OrderBy(t => Distance(t, Layout.Door)).ThenBy(t => t.Y).ThenBy(t => t.X).ToArray();
            var usedQueue = new HashSet<Tile>();
            foreach (var c in characters.Values.Where(Queued).OrderBy(c => c.Order))
            {
                var tile = queueTiles.Contains(c.WorkTile) && !usedQueue.Contains(c.WorkTile) ? c.WorkTile :
                    queueTiles.Where(t => !usedQueue.Contains(t)).Select(t => (Tile?)t).FirstOrDefault();
                if (tile is {} free) { usedQueue.Add(free); SetWorkplace(c, null, null, free, "down"); }
                else { c.Suppressed = true; SetWorkplace(c, null, null, new(-1, -1), "down"); }
            }
        }
        workReservations.Clear();
        foreach (var c in characters.Values)
            if (c.Agent.State != AgentState.Gone && !c.Suppressed) workReservations.Add(c.WorkTile);
    }

    private void CheckLeisureReservations()
    {
        foreach (var c in characters.Values)
            if ((c.Agent.State == AgentState.Idle || c.Agent.State == AgentState.Reporting && c.ReportStage == 5) && workReservations.Contains(c.LeisureTarget))
            { c.NextLeisure = default; c.Target = null; c.LeisureArrived = false; }
    }

    private static int Distance(Tile a, Tile b) => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);
    private Furniture? WorkFurniture(Character c) => Layout.Furniture.FirstOrDefault(f => f.DeskId == c.DeskId && c.DeskId != null &&
        assets.Variant(f).Points.Any(p => p.Action == "typing")) ??
        Layout.Furniture.FirstOrDefault(f => f.Id == source.OverflowSeats?.FirstOrDefault(s => s.Id == c.OverflowSeatId)?.FurnitureId);
    private PixelPoint Contact(Character c)
    {
        var host = WorkFurniture(c);
        if (host != null && assets.Variant(host).ChairSeatAnchor is {} anchor)
        {
            // An unsplit sofa can host multiple overflow seats. Its nap point
            // identifies the contact's reference tile even after rotation.
            var first = c.OverflowSeatId == null ? null : source.OverflowSeats?
                .Where(s => s.FurnitureId == host.Id).OrderBy(s => s.X).ThenBy(s => s.Y).FirstOrDefault();
            var reference = assets.Variant(host).Points.FirstOrDefault(p => p.Action == "napping");
            var rx = reference == null ? first?.X : host.X + reference.X;
            var ry = reference == null ? first?.Y : host.Y + reference.Y;
            return new(host.X * 16 + anchor.X + (first == null ? 0 : (c.WorkTile.X - rx!.Value) * 16),
                host.Y * 16 + anchor.Y + (first == null ? 0 : (c.WorkTile.Y - ry!.Value) * 16));
        }
        return new((c.WorkTile.X + .5) * 16, (c.WorkTile.Y + 1) * 16 - 8);
    }
    private static void ClearSeat(Character c)
    {
        c.SeatedVariant = c.SeatPhase = c.ChairBackId = c.ChairFrontId = null;
        c.SeatAnchor = null; c.SeatPhaseFrame = 0;
    }
    private static void SetWorkplace(Character c, string? deskId, string? overflowId, Tile tile, string facing)
    {
        if (c.DeskId != deskId || c.OverflowSeatId != overflowId || c.WorkTile != tile || c.WorkFacing != facing)
        { c.Target = null; c.Path.Clear(); c.NextLeisure = default; c.ChatPartner = null; c.LeisureArrived = false; }
        c.DeskId = deskId; c.OverflowSeatId = overflowId; c.WorkTile = tile; c.WorkFacing = facing;
    }
    private void RefreshNavigation()
    {
        workplacesDirty = true; paths.Clear(); monitorSupport.Clear();
        desks = source.Furniture.Where(f => assets.Variant(f).Points.Any(p => p.Action == "typing"))
            .Select(f => (Furniture: f, Point: assets.Variant(f).Points.First(p => p.Action == "typing"))).ToArray();
        obstacles = LayoutLoader.Blocked(Layout, assets); reachable = [];
        var door = Layout.Door;
        if (!LayoutLoader.Walkable(Layout, door, obstacles))
            door = Enumerable.Range(0, Layout.WidthTiles * Layout.HeightTiles).Select(i => new Tile(i % Layout.WidthTiles, i / Layout.WidthTiles))
                .Where(t => LayoutLoader.Walkable(Layout, t, obstacles)).OrderBy(t => Distance(t, Layout.Door)).FirstOrDefault(Layout.Door);
        if (!LayoutLoader.Walkable(Layout, door, obstacles)) return;
        reachable = LayoutLoader.Reachable(Layout, door, obstacles);
    }
    private Tile NearestFree(Tile tile, HashSet<Tile>? excluded = null) => reachable.Contains(tile) && excluded?.Contains(tile) != true ? tile :
        reachable.Where(t => excluded?.Contains(t) != true).OrderBy(t => Distance(t, tile)).ThenBy(t => t.Y).ThenBy(t => t.X).FirstOrDefault(Layout.Door);

    private IReadOnlyList<Tile> PathFrom(Tile start, Tile target)
    {
        if (paths.TryGetValue((start, target), out var path)) return path;
        path = FindPath(Layout, start, target, obstacles);
        // Empty results can also mean the search hit its time budget; retry those normally.
        if (path.Count != 0 || start == target)
        {
            if (paths.Count >= 1024) paths.Clear();
            paths[(start, target)] = path;
        }
        return path;
    }

    private void RefreshFurniture(DateTimeOffset now)
    {
        furnitureOwners.Clear();
        foreach (var c in characters.Values)
            if (c.Agent.State != AgentState.Gone && c.DeskId != null) furnitureOwners.Add(c.DeskId, c);
        Furniture[]? furniture = null;
        for (var i = 0; i < source.Furniture.Count; i++)
        {
            var f = source.Furniture[i];
            var v = assets.Variant(f);
            string? ownerId = null, monitor = null;
            var state = f.State;
            for (var p = 0; p < v.Points.Count; p++)
                if (v.Points[p].Action == "typing")
                {
                    furnitureOwners.TryGetValue(f.DeskId!, out var owner);
                    ownerId = owner?.Agent.Id;
                    var typingHere = owner != null && owner.Agent.State == AgentState.Working && owner.Action == "typing" &&
                        owner.Position == new Position(owner.WorkTile.X, owner.WorkTile.Y);
                    monitor = owner == null ? "off" : typingHere ? "working" : "screensaver";
                    if (SupportedMonitor(f, v, monitor)) state = monitor;
                    break;
                }
            var previous = Layout.Furniture[i];
            if (previous.AgentId == ownerId && previous.MonitorState == monitor && previous.State == state) continue;
            furniture ??= Layout.Furniture.ToArray();
            furniture[i] = f with { AgentId = ownerId, State = state, MonitorState = monitor };
        }
        if (furniture != null) Layout = source with { Furniture = furniture };
        var elapsed = started == default ? 0 : (long)(now - started).TotalMilliseconds;
        var count = 0;
        List<FurnitureSprite>? sprites = null;
        furnitureFps = 0;
        for (var i = 0; i < Layout.Furniture.Count; i++)
        {
            var f = Layout.Furniture[i]; var v = assets.Variant(f);
            if (v.SheetId == null || v.Animation == null) continue;
            var frame = assets.Frame(v.SheetId, v.Animation, "any", elapsed, SpriteAtlas.Hash(f.Id) % 4);
            furnitureFps = Math.Max(furnitureFps, assets.AnimationFps(v.SheetId, v.Animation, "any", elapsed));
            var previous = count < FurnitureSprites.Count ? FurnitureSprites[count] : null;
            var same = previous != null && previous.Id == f.Id && previous.Sprite == frame && previous.MonitorState == f.MonitorState;
            if (!same && sprites == null)
            {
                sprites = new(Layout.Furniture.Count);
                for (var j = 0; j < count; j++) sprites.Add(FurnitureSprites[j]);
            }
            if (sprites != null) sprites.Add(same ? previous! : new(f.Id, frame, f.MonitorState));
            count++;
        }
        if (sprites != null) FurnitureSprites = sprites.ToArray();
        else if (count != FurnitureSprites.Count) FurnitureSprites = FurnitureSprites.Take(count).ToArray();
    }

    private bool SupportedMonitor(Furniture f, FurnitureVariant v, string monitor)
    {
        if (monitorSupport.TryGetValue((f, monitor), out var supported)) return supported;
        return monitorSupport[(f, monitor)] = ValidateMonitor(f, v, monitor);
    }

    private bool ValidateMonitor(Furniture f, FurnitureVariant v, string monitor)
    {
        var state = assets.Manifest.Furniture.First(a => a.Id == (f.AssetId ?? f.Kind)).Variants
            .FirstOrDefault(s => s.Rotation == f.Rotation && s.State == monitor);
        // Screen variants are visual changes; never let them alter authored collision geometry.
        return state != null && state.FootprintW == f.Width && state.FootprintH == f.Height &&
            LayoutLoader.Cells(f, v).ToHashSet().SetEquals(LayoutLoader.Cells(f, state)) &&
            assets.Variant(f with { State = monitor }).Points.SequenceEqual(v.Points);
    }

    private void NextDue(DateTimeOffset time, DateTimeOffset now)
    {
        if (time == default || time == DateTimeOffset.MaxValue) return;
        if (time < now) time = now;
        if (time < NextUpdateAt) NextUpdateAt = time;
    }

    private void RefreshCadence(DateTimeOffset now)
    {
        var fps = Math.Min(furnitureFps, 8);
        HasMovingCharacters = false;
        NextUpdateAt = DateTimeOffset.MaxValue;
        foreach (var c in characters.Values)
        {
            if (!c.Visible || c.Suppressed) continue;
            var elapsed = (long)(now - c.ActionSince).TotalMilliseconds;
            var moving = c.Path.Count > 0 && c.SeatPhase != "raise";
            HasMovingCharacters |= moving;
            if (moving) fps = 30;
            else if (c.SeatPhase is "turn" or "lower" or "raise")
            {
                var seatElapsed = (now - c.SeatPhaseSince).TotalMilliseconds;
                var next = c.SeatPhase == "raise" ? seatElapsed < 120 ? 120 : 240 :
                    seatElapsed < 120 ? 120 : seatElapsed < 240 ? 240 : 360;
                NextDue(c.SeatPhaseSince.AddMilliseconds(next), now);
            }
            else
            {
                var animation = c.SeatedVariant != null ? assets.SeatedAnimationFps(c.Agent, c.Design, c.SeatedVariant,
                    c.SeatedFacing, elapsed, now < c.BlinkUntil) :
                    assets.CharacterAnimationFps(c.Agent, c.Design, c.Action, c.Facing, elapsed, now < c.BlinkUntil);
                if (animation > 0) fps = Math.Max(fps, Math.Clamp(animation, 4, 8));
            }
            if (c.FadeSince != default)
            { fps = Math.Max(fps, 8); NextDue(c.FadeSince.AddSeconds(1.2), now); }
            if (c.Target == null && c.SeatPhase is not ("turn" or "lower" or "raise")) NextUpdateAt = now;
            if (c.ReportStage is 2 or 4) NextDue(c.PauseUntil, now);
            if ((c.Agent.State == AgentState.Idle || c.Agent.State == AgentState.Reporting && c.ReportStage == 5) && !c.Entering && !Queued(c) &&
                c.SeatPhase is not ("turn" or "lower" or "raise"))
            {
                if (c.NextLeisure == default) NextUpdateAt = now;
                else NextDue(c.NextLeisure, now);
            }
            if (c.Action is not ("entering" or "walking" or "exiting" or "gone" or "napping") && c.Design.SheetId != "fallback")
            { NextDue(c.NextBlink, now); if (now < c.BlinkUntil) NextDue(c.BlinkUntil, now); }
            if (now < c.PaperUntil) NextDue(c.PaperUntil, now);
            if (now < c.ReactUntil) NextDue(c.ReactUntil, now);
        }
        AnimationInterval = fps > 0 ? TimeSpan.FromSeconds(1 / fps) : TimeSpan.Zero;
    }

    private static string DisplayName(AgentInfo agent)
    {
        var provider = agent.Provider.ToString();
        var name = agent.Name.Trim();
        if (name.Length == 0 || PlanTitle(name))
        {
            var role = agent.Role?.Trim();
            var model = agent.Model?.Split('/', '\\').Last().Trim();
            name = !string.IsNullOrWhiteSpace(role) && !PlanTitle(role) ?
                role.ToLowerInvariant() is "worker" or "subagent" ? provider + " worker" : role :
                !string.IsNullOrWhiteSpace(model) && !PlanTitle(model) ? model : provider + (agent.IsSubagent ? " worker" : "");
        }
        var elements = System.Globalization.StringInfo.ParseCombiningCharacters(name);
        return elements.Length <= 14 ? name : name[..elements[14]] + "…";
    }
    private static bool PlanTitle(string value)
    {
        var title = string.Join(" ", value.Replace('-', ' ').Replace('_', ' ').Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();
        if (title is "pro" or "ultra" or "plus" or "max" or "premium") return true;
        string[] labels = ["google ai pro", "google ai ultra", "google one", "gemini advanced", "gemini ultra",
            "chatgpt plus", "chatgpt pro", "chatgpt team", "claude pro", "claude max", "antigravity pro", "codex pro"];
        return labels.Any(label => title == label || title.StartsWith(label, StringComparison.Ordinal) && title.Length > label.Length &&
            (char.IsWhiteSpace(title[label.Length]) || char.IsPunctuation(title[label.Length])));
    }

    public static IReadOnlyList<Tile> FindPath(OfficeLayout layout, Tile start, Tile target, HashSet<Tile>? blocked = null)
    {
        if (layout.WidthTiles is < 1 or > 128 || layout.HeightTiles is < 1 or > 128) return [];
        blocked ??= LayoutLoader.Blocked(layout);
        if (start == target) return [];
        if (!LayoutLoader.Walkable(layout, start, blocked) || !LayoutLoader.Walkable(layout, target, blocked)) return [];
        var queue = new PriorityQueue<Tile, int>(); var from = new Dictionary<Tile, Tile> { [start] = start };
        var costs = new Dictionary<Tile, int> { [start] = 0 }; queue.Enqueue(start, 0);
        var timer = System.Diagnostics.Stopwatch.StartNew(); var work = 0;
        while (queue.TryDequeue(out var current, out _))
        {
            if (++work > 65536 || work >= 4096 && timer.ElapsedMilliseconds > 50) return [];
            foreach (var next in new Tile[] { new(current.X + 1, current.Y), new(current.X - 1, current.Y), new(current.X, current.Y + 1), new(current.X, current.Y - 1) })
            {
                if (!LayoutLoader.Walkable(layout, next, blocked)) continue;
                var cost = costs[current] + 1;
                if (costs.TryGetValue(next, out var old) && old <= cost) continue;
                costs[next] = cost;
                from[next] = current;
                if (next == target)
                {
                    var path = new List<Tile>(); for (var step = target; step != start; step = from[step]) path.Add(step);
                    path.Reverse(); return path;
                }
                queue.Enqueue(next, cost + Math.Abs(next.X - target.X) + Math.Abs(next.Y - target.Y));
            }
        }
        return [];
    }
}
