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
    }

    private readonly Dictionary<string, Character> characters = [];
    private readonly Dictionary<string, string> deskHistory = new(StringComparer.Ordinal);
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
    public OfficeAnimator(int? seed = null, AssetPack? assets = null, OfficeLayout? layout = null)
    {
        random = seed.HasValue ? new(seed.Value) : new();
        this.assets = assets ?? new();
        source = LayoutLoader.Prepare(layout ?? LayoutLoader.Load(this.assets, null), this.assets);
        Layout = source;
        RefreshNavigation(); RefreshFurniture(default);
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
    }

    public IReadOnlyList<AgentView> Update(IReadOnlyList<AgentInfo> agents, DateTimeOffset now, double deltaSeconds)
    {
        if (started == default) started = now;
        lastUpdated = now;
        var ids = agents.Select(a => a.Id).ToHashSet();
        foreach (var id in characters.Keys.Where(k => !ids.Contains(k)).ToArray())
            characters.Remove(id);
        var newcomers = new List<Character>();
        foreach (var agent in agents.OrderBy(a => a.Id, StringComparer.Ordinal))
            if (!characters.ContainsKey(agent.Id))
            {
                var c = new Character(nextOrder++, agent) { Design = assets.Assign(agent, characters.Values.Select(c => c.Design.Id)),
                    BlinkRandom = new(unchecked((int)SpriteAtlas.Hash(agent.Id))) };
                characters[agent.Id] = c; newcomers.Add(c);
            }
        foreach (var agent in agents) characters[agent.Id].Agent = agent;
        AssignWorkplaces();
        foreach (var c in newcomers) c.Position = new(Layout.Door.X, Layout.Door.Y);

        foreach (var agent in agents)
        {
            var c = characters[agent.Id]; var desk = c.WorkTile;
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
                c.Position = new(start.X, start.Y);
                c.Path = new(FindPath(Layout, start, target, obstacles));
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
        return agents.Select(a =>
        {
            var c = characters[a.Id];
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
            return new AgentView(a, new(c.Position.X, c.Position.Y, c.WorkTile, c.Action, c.Facing,
                sprite.Row, sprite.Column, c.Visible,
                c.FadeSince == default ? 1 : Math.Clamp(1 - (now - c.FadeSince).TotalSeconds / 1.2, 0, 1),
                c.ReportStage is 3 or 4, c.DeskId != null && now < c.PaperUntil, Queued(c) ? "…" : now < c.ReactUntil ? "!" : null,
                c.ChatPartner, c.Design.Id, sprite, c.DeskId, c.OverflowSeatId, WorkLocation(c), Queued(c), DisplayName(a),
                a.Provider.ToString().ToLowerInvariant(), c.SeatedVariant, c.SeatAnchor, c.ChairBackId, c.ChairFrontId, c.SeatPhase, c.SeatPhaseFrame));
        }).ToArray();
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
    private HashSet<Tile> WorkReservations() => characters.Values.Where(c => c.Agent.State != AgentState.Gone).Select(c => c.WorkTile).ToHashSet();

    private void AssignWorkplaces()
    {
        var desks = source.Furniture.Where(f => assets.Variant(f).Points.Any(p => p.Action == "typing"))
            .Select(f => (Furniture: f, Point: assets.Variant(f).Points.First(p => p.Action == "typing"))).ToArray();
        var overflow = source.OverflowSeats ?? [];
        var usedDesks = new HashSet<string>(StringComparer.Ordinal);
        var usedOverflow = new HashSet<string>(StringComparer.Ordinal);
        Tile DeskTile(Furniture f, InteractionPoint p) => new(f.X + p.X, f.Y + p.Y);
        foreach (var c in characters.Values.OrderBy(c => c.Order))
        {
            if (c.Agent.State == AgentState.Gone) { SetWorkplace(c, null, null, Layout.Door, "down"); continue; }
            var desk = desks.FirstOrDefault(d => d.Furniture.DeskId == c.DeskId && c.DeskId != null);
            var seat = overflow.FirstOrDefault(s => s.Id == c.OverflowSeatId);
            if (desk.Furniture != null && usedDesks.Add(desk.Furniture.DeskId!))
                SetWorkplace(c, desk.Furniture.DeskId, null, NearestFree(DeskTile(desk.Furniture, desk.Point)), desk.Point.Facing);
            else if (seat != null && usedOverflow.Add(seat.Id))
                SetWorkplace(c, null, seat.Id, NearestFree(new(seat.X, seat.Y)), seat.Facing);
            else SetWorkplace(c, null, null, c.WorkTile, "down");
        }
        var pending = characters.Values.Where(c => Queued(c)).OrderBy(c => c.Order).ToList();
        while (pending.Count > 0)
        {
            // Parents take their place first even when a child sorts before them by ID.
            var c = pending.FirstOrDefault(c => !pending.Any(p => p.Agent.Id == c.Agent.ParentId)) ?? pending[0];
            pending.Remove(c);
            var free = desks.Where(d => !usedDesks.Contains(d.Furniture.DeskId!) && reachable.Contains(DeskTile(d.Furniture, d.Point))).ToArray();
            var chosen = deskHistory.TryGetValue(c.Agent.Id, out var previous) ? free.FirstOrDefault(d => d.Furniture.DeskId == previous) : default;
            var parent = c.Agent.ParentId != null && characters.TryGetValue(c.Agent.ParentId, out var p) && p.Agent.State != AgentState.Gone ? p : null;
            if (chosen.Furniture == null && free.Length > 0)
                chosen = parent == null ? free[0] : free.MinBy(d => Distance(DeskTile(d.Furniture, d.Point), parent.WorkTile));
            if (chosen.Furniture != null)
            {
                var id = chosen.Furniture.DeskId!; usedDesks.Add(id); deskHistory[c.Agent.Id] = id;
                SetWorkplace(c, id, null, DeskTile(chosen.Furniture, chosen.Point), chosen.Point.Facing);
                continue;
            }
            var freeOverflow = overflow.Where(s => !usedOverflow.Contains(s.Id) && reachable.Contains(new(s.X, s.Y))).ToArray();
            var extra = parent == null ? freeOverflow.FirstOrDefault() : freeOverflow.MinBy(s => Distance(new(s.X, s.Y), parent.WorkTile));
            if (extra != null)
            {
                usedOverflow.Add(extra.Id); SetWorkplace(c, null, extra.Id, new(extra.X, extra.Y), extra.Facing);
            }
        }
        var seats = desks.Select(d => DeskTile(d.Furniture, d.Point)).Concat(overflow.Select(s => new Tile(s.X, s.Y))).ToHashSet();
        var queueTiles = reachable.Where(t => t != Layout.Door && Distance(t, Layout.Door) <= 4 && !seats.Contains(t))
            .OrderBy(t => Distance(t, Layout.Door)).ThenBy(t => t.Y).ThenBy(t => t.X).ToArray();
        var usedQueue = new HashSet<Tile>();
        foreach (var c in characters.Values.Where(Queued).OrderBy(c => c.Order))
        {
            var tile = queueTiles.Contains(c.WorkTile) && usedQueue.Add(c.WorkTile) ? c.WorkTile :
                queueTiles.FirstOrDefault(t => !usedQueue.Contains(t), queueTiles.FirstOrDefault(Layout.Door));
            usedQueue.Add(tile); SetWorkplace(c, null, null, tile, "down");
        }
        var reserved = WorkReservations();
        foreach (var c in characters.Values)
            if ((c.Agent.State == AgentState.Idle || c.Agent.State == AgentState.Reporting && c.ReportStage == 5) && reserved.Contains(c.LeisureTarget))
            { c.NextLeisure = default; c.Target = null; c.LeisureArrived = false; }
    }

    private static int Distance(Tile a, Tile b) => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);
    private Furniture? WorkFurniture(Character c) => Layout.Furniture.FirstOrDefault(f => f.DeskId == c.DeskId && c.DeskId != null &&
        assets.Variant(f).Points.Any(p => p.Action == "typing")) ??
        Layout.Furniture.FirstOrDefault(f => f.Id == source.OverflowSeats?.FirstOrDefault(s => s.Id == c.OverflowSeatId)?.FurnitureId);
    private PixelPoint Contact(Character c)
    {
        var host = WorkFurniture(c);
        return host != null && assets.Variant(host).ChairSeatAnchor is {} anchor ? new(host.X * 16 + anchor.X, host.Y * 16 + anchor.Y) :
            new((c.WorkTile.X + .5) * 16, (c.WorkTile.Y + 1) * 16 - 8);
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
        obstacles = LayoutLoader.Blocked(Layout, assets); reachable = [];
        var door = Layout.Door;
        if (!LayoutLoader.Walkable(Layout, door, obstacles))
            door = Enumerable.Range(0, Layout.WidthTiles * Layout.HeightTiles).Select(i => new Tile(i % Layout.WidthTiles, i / Layout.WidthTiles))
                .Where(t => LayoutLoader.Walkable(Layout, t, obstacles)).OrderBy(t => Distance(t, Layout.Door)).FirstOrDefault(Layout.Door);
        if (!LayoutLoader.Walkable(Layout, door, obstacles)) return;
        var queue = new Queue<Tile>(); queue.Enqueue(door); reachable.Add(door);
        while (queue.TryDequeue(out var t))
            foreach (var next in new Tile[] { new(t.X + 1, t.Y), new(t.X - 1, t.Y), new(t.X, t.Y + 1), new(t.X, t.Y - 1) })
                if (LayoutLoader.Walkable(Layout, next, obstacles) && reachable.Add(next)) queue.Enqueue(next);
    }
    private Tile NearestFree(Tile tile, HashSet<Tile>? excluded = null) => reachable.Contains(tile) && excluded?.Contains(tile) != true ? tile :
        reachable.Where(t => excluded?.Contains(t) != true).OrderBy(t => Distance(t, tile)).ThenBy(t => t.Y).ThenBy(t => t.X).FirstOrDefault(Layout.Door);

    private void RefreshFurniture(DateTimeOffset now)
    {
        var owners = characters.Values.Where(c => c.Agent.State != AgentState.Gone && c.DeskId != null).ToDictionary(c => c.DeskId!, StringComparer.Ordinal);
        var furniture = source.Furniture.Select(f =>
        {
            var v = assets.Variant(f);
            if (!v.Points.Any(p => p.Action == "typing")) return f with { AgentId = null, MonitorState = null };
            owners.TryGetValue(f.DeskId!, out var owner);
            var typingHere = owner != null && owner.Agent.State == AgentState.Working && owner.Action == "typing" &&
                owner.Position == new Position(owner.WorkTile.X, owner.WorkTile.Y);
            var monitor = owner == null ? "off" : typingHere ? "working" : "screensaver";
            var state = assets.Manifest.Furniture.First(a => a.Id == (f.AssetId ?? f.Kind)).Variants
                .FirstOrDefault(s => s.Rotation == f.Rotation && s.State == monitor);
            // Screen variants are visual changes; never let them alter authored collision geometry.
            var supported = state != null && state.FootprintW == f.Width && state.FootprintH == f.Height &&
                LayoutLoader.Cells(f, v).ToHashSet().SetEquals(LayoutLoader.Cells(f, state)) &&
                assets.Variant(f with { State = monitor }).Points.SequenceEqual(v.Points);
            return f with { AgentId = owner?.Agent.Id, State = supported ? monitor : f.State, MonitorState = monitor };
        }).ToArray();
        if (!Layout.Furniture.SequenceEqual(furniture)) Layout = source with { Furniture = furniture };
        var elapsed = started == default ? 0 : (long)(now - started).TotalMilliseconds;
        FurnitureSprites = Layout.Furniture.Select(f => (f, v: assets.Variant(f))).Where(p => p.v.SheetId != null && p.v.Animation != null)
            .Select(p => new FurnitureSprite(p.f.Id, assets.Frame(p.v.SheetId!, p.v.Animation!, "any", elapsed, SpriteAtlas.Hash(p.f.Id) % 4), p.f.MonitorState)).ToArray();
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
        blocked ??= LayoutLoader.Blocked(layout);
        if (start == target) return [];
        if (!LayoutLoader.Walkable(layout, start, blocked) || !LayoutLoader.Walkable(layout, target, blocked)) return [];
        var queue = new PriorityQueue<Tile, int>(); var from = new Dictionary<Tile, Tile> { [start] = start };
        var costs = new Dictionary<Tile, int> { [start] = 0 }; queue.Enqueue(start, 0);
        while (queue.TryDequeue(out var current, out _))
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
        return [];
    }
}
