namespace Vibedotcom.Core;

public sealed class OfficeAnimator
{
    private sealed class Character(int slot)
    {
        public readonly int Slot = slot;
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
    }

    private readonly Dictionary<string, Character> characters = [];
    private readonly Random random;
    private HashSet<Tile> obstacles = [];
    private readonly AssetPack assets;
    private readonly OfficeLayout source;
    private DateTimeOffset started;
    private static readonly string[] LeisureActions = ["coffee", "tv", "phone", "napping", "chatting", "water"];
    public OfficeLayout Layout { get; private set; }
    public OfficeLayout SourceLayout => source;
    public IReadOnlyList<FurnitureSprite> FurnitureSprites { get; private set; } = [];
    public OfficeAnimator(int? seed = null, AssetPack? assets = null, OfficeLayout? layout = null)
    {
        random = seed.HasValue ? new(seed.Value) : new();
        this.assets = assets ?? new();
        source = layout ?? LayoutLoader.Load(this.assets, null);
        Layout = MakeLayout(); obstacles = LayoutLoader.Blocked(Layout, this.assets);
    }

    public IReadOnlyList<AgentView> Update(IReadOnlyList<AgentInfo> agents, DateTimeOffset now, double deltaSeconds)
    {
        if (started == default) started = now;
        var ids = agents.Select(a => a.Id).ToHashSet();
        var layoutChanged = false;
        foreach (var id in characters.Keys.Where(k => !ids.Contains(k)).ToArray())
        { characters.Remove(id); layoutChanged = true; }
        var newcomers = new List<Character>();
        foreach (var agent in agents.OrderBy(a => a.Id, StringComparer.Ordinal))
            if (!characters.ContainsKey(agent.Id))
            {
                var occupied = characters.Values.Select(c => c.Slot).ToHashSet();
                var slot = 0; while (occupied.Contains(slot)) slot++;
                var c = new Character(slot) { Design = assets.Assign(agent, characters.Values.Select(c => c.Design.Id)) };
                characters[agent.Id] = c; newcomers.Add(c); layoutChanged = true;
            }
        if (layoutChanged)
        {
            Layout = MakeLayout(); obstacles = LayoutLoader.Blocked(Layout, assets);
            foreach (var c in characters.Values)
            {
                c.Target = null; c.Path.Clear(); c.NextLeisure = default; c.ChatPartner = null; c.LeisureArrived = false;
                var tile = NearestFree(new((int)Math.Round(c.Position.X), (int)Math.Round(c.Position.Y)));
                c.Position = new(tile.X, tile.Y);
            }
        }
        foreach (var c in newcomers) c.Position = new(Layout.Door.X, Layout.Door.Y);

        foreach (var agent in agents)
        {
            var c = characters[agent.Id]; var desk = Desk(c.Slot);
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
            if (idle && !reporting && !c.Entering && now >= c.NextLeisure) ChooseLeisure(agent, agents, now);
            if (c.ChatPartner != null && (!characters.TryGetValue(c.ChatPartner, out var partner) || partner.ObservedState != AgentState.Idle))
            { c.ChatPartner = null; c.NextLeisure = default; }

            Tile target; string action;
            if (agent.State == AgentState.Gone) { c.Entering = false; target = Layout.Door; action = "exiting"; }
            else if (c.Entering) { target = desk; action = "entering"; }
            else if (reporting)
            {
                var parentDesk = agent.ParentId != null && characters.TryGetValue(agent.ParentId, out var parent) ? Desk(parent.Slot) : desk;
                target = c.ReportStage <= 2 ? desk : NearestFree(new(parentDesk.X + 1, parentDesk.Y));
                action = c.ReportStage <= 2 ? "pickup" : "reporting";
            }
            else if (idle) { target = c.LeisureTarget; action = c.Leisure; }
            else
            {
                target = desk;
                action = agent.State switch { AgentState.Working => "typing", AgentState.Thinking => "thinking", _ => "waiting" };
            }
            if (c.Target != target)
            {
                c.Target = target;
                var start = NearestFree(new((int)Math.Round(c.Position.X), (int)Math.Round(c.Position.Y)));
                c.Position = new(start.X, start.Y);
                c.Path = new(FindPath(Layout, start, target, obstacles));
            }
            var budget = Math.Clamp(deltaSeconds, 0, 1) * 3.5;
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
            if (arrived)
            {
                if (c.Entering) { c.Entering = false; c.Target = null; }
                else if (agent.State == AgentState.Gone)
                {
                    if (c.FadeSince == default) c.FadeSince = now;
                    if ((now - c.FadeSince).TotalSeconds >= 1.2) c.Visible = false;
                }
                else if (reporting)
                {
                    c.Facing = c.ReportStage <= 2 ? DeskPoint(c.Slot).Facing : "left";
                    if (c.ReportStage == 1) { c.ReportStage = 2; c.PauseUntil = now.AddSeconds(.9); }
                    else if (c.ReportStage == 2 && now >= c.PauseUntil) { c.ReportStage = 3; c.Target = null; }
                    else if (c.ReportStage == 3)
                    {
                        c.ReportStage = 4; c.PauseUntil = now.AddSeconds(1.4);
                        if (agent.ParentId != null && characters.TryGetValue(agent.ParentId, out var parent))
                        { parent.PaperUntil = now.AddSeconds(12); parent.ReactUntil = now.AddSeconds(3); }
                        else c.PaperUntil = now.AddSeconds(12);
                    }
                    else if (c.ReportStage == 4 && now >= c.PauseUntil)
                    { c.ReportStage = 5; c.NextLeisure = default; c.Target = null; }
                }
                else if (!idle) c.Facing = DeskPoint(c.Slot).Facing;
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
            if (!c.Visible) c.Action = "gone";
            if (c.Action != c.LastAction) { c.LastAction = c.Action; c.ActionSince = now; }
        }
        foreach (var (id, c) in characters)
            if (c.ChatPartner != null && (!characters.TryGetValue(c.ChatPartner, out var partner) || partner.ChatPartner != id))
            { c.ChatPartner = null; c.NextLeisure = default; if (c.Action == "chatting") c.Action = "phone"; }
        FurnitureSprites = Layout.Furniture.Select(f => (f, v: assets.Variant(f))).Where(p => p.v.SheetId != null && p.v.Animation != null)
            .Select(p => new FurnitureSprite(p.f.Id,assets.Frame(p.v.SheetId!,p.v.Animation!,"any",(long)(now-started).TotalMilliseconds,SpriteAtlas.Hash(p.f.Id)%4))).ToArray();
        return agents.Select(a =>
        {
            var c = characters[a.Id];
            var sprite = assets.CharacterFrame(a, c.Design, c.Action, c.Facing, (long)(now - c.ActionSince).TotalMilliseconds);
            return new AgentView(a, new(c.Position.X, c.Position.Y, Desk(c.Slot), c.Action, c.Facing,
                sprite.Row, sprite.Column, c.Visible,
                c.FadeSince == default ? 1 : Math.Clamp(1 - (now - c.FadeSince).TotalSeconds / 1.2, 0, 1),
                c.ReportStage is 3 or 4, now < c.PaperUntil, now < c.ReactUntil ? "!" : null, c.ChatPartner, c.Design.Id, sprite));
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
            var spots = assets.Points(Layout).Where(p => p.Point.Action == activity && !obstacles.Contains(p.Tile)).ToArray();
            var available = spots.Where(p => !characters.Values.Any(v => v != c &&
                (v.ObservedState == AgentState.Idle || v.ObservedState == AgentState.Reporting && v.ReportStage == 5) && v.LeisureTarget == p.Tile)).ToArray();
            if (available.Length == 0 || activity == "chatting" && available.Length < 2) continue;
            if (activity == "chatting")
            {
                var friend = agents.FirstOrDefault(a => a.Id != agent.Id && a.State == AgentState.Idle &&
                    !characters[a.Id].Entering && characters[a.Id].ChatPartner == null && characters[a.Id].ReportStage is 0 or 5);
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
        c.Leisure = "phone"; c.LeisureTarget = NearestFree(new(lounge.X + random.Next(lounge.Width), lounge.Y + random.Next(lounge.Height)));
        c.LeisureFacing = "down"; c.LeisureSeconds = 15; c.LeisureArrived = false;
        c.NextLeisure = DateTimeOffset.MaxValue; c.Target = null;
    }

    private Furniture DeskFurniture(int slot) => Layout.Furniture.Where(f => assets.Variant(f).Points.Any(p => p.Action == "typing")).ElementAt(slot);
    private InteractionPoint DeskPoint(int slot) => assets.Variant(DeskFurniture(slot)).Points.First(p => p.Action == "typing");
    private Tile Desk(int slot) { var f = DeskFurniture(slot); var p = DeskPoint(slot); return new(f.X + p.X, f.Y + p.Y); }
    private OfficeLayout MakeLayout()
    {
        return LayoutLoader.Adapt(source, assets, characters.Select(p => (p.Key, p.Value.Slot)).OrderBy(p => p.Slot).ToArray());
    }

    private Tile NearestFree(Tile tile)
    {
        tile = new(Math.Clamp(tile.X, 0, Layout.WidthTiles - 1), Math.Clamp(tile.Y, 0, Layout.HeightTiles - 1));
        if (!obstacles.Contains(tile)) return tile;
        return Enumerable.Range(0, Layout.HeightTiles).SelectMany(y => Enumerable.Range(0, Layout.WidthTiles).Select(x => new Tile(x, y)))
            .Where(t => !obstacles.Contains(t)).MinBy(t => Math.Abs(t.X - tile.X) + Math.Abs(t.Y - tile.Y));
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
