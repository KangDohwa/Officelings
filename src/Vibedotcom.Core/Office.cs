namespace Vibedotcom.Core;

public sealed class OfficeAnimator
{
    private sealed class Character(int slot, Position position)
    {
        public readonly int Slot = slot;
        public Position Position = position;
        public Tile Target;
        public Queue<Tile> Path = new();
        public AgentState? PreviousState;
        public AgentState? ObservedState;
        public string Action = "entering", Facing = "right";
        public bool Entering = true, Visible = true;
        public bool ReportPending;
        public int Leisure;
        public DateTimeOffset NextLeisure, ReportArrived;
    }
    private readonly Dictionary<string, Character> characters = [];
    private readonly Random random;
    private static readonly string[] LeisureActions = ["coffee", "tv", "phone", "napping", "chatting"];
    public OfficeLayout Layout { get; private set; } = MakeLayout([]);
    public OfficeAnimator(int? seed = null) => random = seed.HasValue ? new(seed.Value) : new();

    public IReadOnlyList<AgentView> Update(IReadOnlyList<AgentInfo> agents, DateTimeOffset now, double deltaSeconds)
    {
        var ids = agents.Select(a => a.Id).ToHashSet();
        var newcomers = new List<Character>();
        foreach (var id in characters.Keys.Where(k => !ids.Contains(k)).ToArray()) characters.Remove(id);
        foreach (var agent in agents)
            if (!characters.ContainsKey(agent.Id))
            {
                var slot = 0;
                var occupied = characters.Values.Select(c => c.Slot).ToHashSet();
                while (occupied.Contains(slot)) slot++;
                characters[agent.Id] = new(slot, new(Layout.Door.X, Layout.Door.Y));
                newcomers.Add(characters[agent.Id]);
            }
        var previousHeight = Layout.HeightTiles;
        Layout = MakeLayout(characters.Select(p => (p.Key, p.Value.Slot)).ToArray());
        foreach (var newcomer in newcomers) newcomer.Position = new(Layout.Door.X, Layout.Door.Y);
        var obstacles = Blocked(Layout);
        foreach (var agent in agents)
        {
            var c = characters[agent.Id];
            var desk = Desk(c.Slot);
            if (agent.State == AgentState.Reporting && c.ObservedState != AgentState.Reporting)
            { c.ReportPending = true; c.ReportArrived = default; }
            c.ObservedState = agent.State;
            if (agent.State is not (AgentState.Idle or AgentState.Reporting) ||
                c.ReportArrived != default && now - c.ReportArrived >= TimeSpan.FromSeconds(2)) c.ReportPending = false;
            var state = c.ReportPending && agent.State == AgentState.Idle ? AgentState.Reporting : agent.State;
            if (previousHeight != Layout.HeightTiles)
            {
                c.Path.Clear(); c.PreviousState = null;
                c.Position = new(Math.Clamp(Math.Round(c.Position.X), 1, Layout.WidthTiles - 2), Math.Clamp(Math.Round(c.Position.Y), 3, Layout.HeightTiles - 2));
            }
            var changed = c.PreviousState != state;
            if (changed)
            {
                c.PreviousState = state;
                c.ReportArrived = default;
                if (state != AgentState.Gone && !c.Visible)
                { c.Position = new(Layout.Door.X, Layout.Door.Y); c.Visible = c.Entering = true; }
                c.Leisure = random.Next(LeisureActions.Length);
                c.NextLeisure = now.AddSeconds(random.Next(25, 46));
            }
            if (state == AgentState.Idle && now >= c.NextLeisure)
            {
                c.Leisure = (c.Leisure + random.Next(1, LeisureActions.Length)) % LeisureActions.Length;
                c.NextLeisure = now.AddSeconds(random.Next(25, 46));
            }
            Tile target;
            var action = state switch
            {
                AgentState.Working => "typing", AgentState.Thinking => "thinking", AgentState.Waiting => "waiting",
                AgentState.Reporting => "reporting", AgentState.Idle => LeisureActions[c.Leisure], _ => "exiting"
            };
            if (state == AgentState.Gone) { c.Entering = false; target = Layout.Door; }
            else if (c.Entering) target = desk;
            else if (state == AgentState.Reporting)
                target = agent.ParentId != null && characters.TryGetValue(agent.ParentId, out var parent) ? Desk(parent.Slot) : desk;
            else if (state == AgentState.Idle)
                target = c.Leisure switch { 0 => new(40, 9), 1 => new(40, 19), 2 => new(44, 15), 3 => new(43, 21), _ => new(42, 13) };
            else target = desk;
            if (target != c.Target || changed)
            {
                c.Target = target;
                var start = new Tile((int)Math.Round(c.Position.X), (int)Math.Round(c.Position.Y));
                c.Position = new(start.X, start.Y);
                c.Path = new(FindPath(Layout, start, target, obstacles));
            }
            var budget = Math.Clamp(deltaSeconds, 0, 1) * 4; // tiles/second; pause/resume never teleports
            var moving = c.Path.Count > 0;
            while (budget > 0 && c.Path.TryPeek(out var next))
            {
                var dx = next.X - c.Position.X;
                var dy = next.Y - c.Position.Y;
                if (dx != 0) c.Facing = dx < 0 ? "left" : "right";
                var distance = Math.Abs(dx) + Math.Abs(dy);
                if (distance <= budget) { c.Position = new(next.X, next.Y); budget -= distance; c.Path.Dequeue(); }
                else { c.Position = new(c.Position.X + Math.Sign(dx) * budget, c.Position.Y + Math.Sign(dy) * budget); budget = 0; }
            }
            if (c.Path.Count == 0 && c.Position == new Position(target.X, target.Y))
            {
                var arrivedFromEntry = c.Entering;
                if (c.Entering) { c.Entering = false; c.PreviousState = null; }
                if (state == AgentState.Gone) c.Visible = false;
                if (state == AgentState.Reporting && !arrivedFromEntry && c.ReportArrived == default) c.ReportArrived = now;
            }
            c.Action = moving ? state == AgentState.Gone ? "exiting" : c.Entering ? "entering" : "walking" : action;
            if (!c.Visible) c.Action = "gone";
            // A report is handed over for a moment; the tracker then takes the character to leisure.
            if (state == AgentState.Reporting && c.ReportArrived != default && now - c.ReportArrived > TimeSpan.FromSeconds(2)) c.Action = "waiting";
        }
        var tick = (int)(now.ToUnixTimeMilliseconds() / 180 % 2);
        return agents.Select(a =>
        {
            var c = characters[a.Id];
            var frame = c.Action switch
            {
                "entering" or "walking" or "exiting" => 1 + tick, "typing" => 3 + tick,
                "thinking" => 5, "waiting" => 6, "reporting" => 7, "coffee" => 8,
                "tv" => 9, "phone" => 10, "napping" => 11, "chatting" => 12, _ => 0
            };
            return new AgentView(a, new(c.Position.X, c.Position.Y, Desk(c.Slot), c.Action, c.Facing,
                (int)a.Provider * 2 + (a.IsSubagent ? 1 : 0), frame, c.Visible));
        }).ToArray();
    }

    private static Tile Desk(int slot) => new(4 + slot % 5 * 7, 8 + slot / 5 * 5);

    private static OfficeLayout MakeLayout((string Id, int Slot)[] slots)
    {
        var height = Math.Max(28, 13 + (slots.Length == 0 ? 0 : slots.Max(s => s.Slot) / 5) * 5);
        var furniture = new List<Furniture>
        {
            new("pantry", 39, 6, 7, 2), new("table", 42, 11, 3, 1),
            new("tv", 40, 16, 4, 1), new("sofa", 42, 20, 4, 1),
            new("plant", 37, 4, 1, 1), new("plant", 2, 3, 1, 1)
        };
        furniture.AddRange(slots.OrderBy(s => s.Slot).Select(s =>
        { var desk = Desk(s.Slot); return new Furniture("desk", desk.X - 1, desk.Y - 2, 3, 2, s.Id); }));
        return new(48, height, 16, new(1, height - 3), furniture);
    }

    private static HashSet<Tile> Blocked(OfficeLayout layout)
    {
        var result = new HashSet<Tile>();
        foreach (var f in layout.Furniture)
            for (var y = f.Y; y < f.Y + f.Height; y++)
                for (var x = f.X; x < f.X + f.Width; x++) result.Add(new(x, y));
        return result;
    }

    public static IReadOnlyList<Tile> FindPath(OfficeLayout layout, Tile start, Tile target, HashSet<Tile>? blocked = null)
    {
        blocked ??= Blocked(layout);
        if (start == target) return [];
        var queue = new Queue<Tile>();
        var from = new Dictionary<Tile, Tile> { [start] = start };
        queue.Enqueue(start);
        while (queue.TryDequeue(out var current))
        {
            foreach (var next in new Tile[] { new(current.X + 1, current.Y), new(current.X - 1, current.Y), new(current.X, current.Y + 1), new(current.X, current.Y - 1) })
            {
                if (next.X < 1 || next.Y < 3 || next.X >= layout.WidthTiles - 1 || next.Y >= layout.HeightTiles - 1 || blocked.Contains(next) || from.ContainsKey(next)) continue;
                from[next] = current;
                if (next == target)
                {
                    var path = new List<Tile>();
                    for (var step = target; step != start; step = from[step]) path.Add(step);
                    path.Reverse();
                    return path;
                }
                queue.Enqueue(next);
            }
        }
        return []; // Unreachable destinations stay put rather than walking through furniture.
    }
}
