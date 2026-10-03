using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Vibedotcom.Core;
using Xunit;

namespace Vibedotcom.Tests;

public sealed class CoreTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "Vibedotcom-tests-" + Guid.NewGuid().ToString("N"));
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-03T12:00:10Z");
    public CoreTests() => Directory.CreateDirectory(root);
    public void Dispose() => Directory.Delete(root, true);
    private static string[] Fixture(string name) => File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Fixtures", name + ".jsonl"));
    private static void Apply(TrackedAgent agent, string line, string path = "example.jsonl")
    { using var doc = JsonDocument.Parse(line); LogParser.Apply(agent, doc.RootElement, path, Now); }

    [Fact]
    public void CodexNestedParentCustomToolsAndCompletion()
    {
        var a = new TrackedAgent("codex:child-1", Provider.Codex);
        var lines = Fixture("codex");
        for (var i = 0; i < 4; i++) Apply(a, lines[i]);
        Assert.Equal("codex:parent-1", a.ParentId); Assert.True(a.IsSubagent);
        Assert.Equal("Ada", a.Name); Assert.Equal("researcher", a.Role); Assert.Equal("example-model", a.Model);
        Assert.Equal(AgentState.Working, a.EventState); Assert.Equal("exec_command", a.Tool); Assert.Equal("shell", a.ToolKind);
        Apply(a, lines[4]); Assert.Equal(AgentState.Thinking, a.EventState);
        Apply(a, lines[5]); Assert.Equal(AgentState.Waiting, a.EventState);
        Apply(a, lines[6]); Apply(a, lines[7]); Assert.Equal(AgentState.Reporting, a.EventState);
    }

    [Fact]
    public void ClaudeSidechainParentIsSessionNotMessageUuid()
    {
        var path = Path.Combine(root, "parent-1", "subagents", "agent-worker-1.jsonl");
        var a = new TrackedAgent("claude:parent-1/agent-worker-1", Provider.Claude);
        var lines = Fixture("claude");
        using var first = JsonDocument.Parse(lines[0]);
        Assert.Equal(a.Id, LogParser.Identity(Provider.Claude, path, first.RootElement));
        for (var i = 0; i < 3; i++) Apply(a, lines[i], path);
        Assert.True(a.IsSubagent); Assert.Equal("claude:parent-1", a.ParentId);
        Assert.Equal("example-claude", a.Model); Assert.Equal(AgentState.Working, a.EventState); Assert.Equal("file", a.ToolKind);
        Apply(a, lines[3], path); Assert.Equal("Inspect the example file.", a.Task);
        Apply(a, lines[4], path); Assert.Equal(AgentState.Reporting, a.EventState);
    }

    [Fact]
    public void ProviderFinalAnswersWaitForUserAndIgnoreOlderEvents()
    {
        var a = new TrackedAgent("codex:parent", Provider.Codex);
        Apply(a, Fixture("codex")[6]); Assert.Equal(AgentState.Waiting, a.EventState);
        Apply(a, Fixture("codex")[3]); Assert.Equal(AgentState.Waiting, a.EventState);
        var c = new TrackedAgent("claude:parent", Provider.Claude);
        Apply(c, """{"timestamp":"2026-10-03T12:00:08Z","type":"assistant","sessionId":"parent","message":{"stop_reason":"end_turn","content":[{"type":"text","text":"Done."}]}}""");
        Assert.Equal(AgentState.Waiting, c.EventState); Assert.False(c.IsSubagent);
    }

    [Fact]
    public void AntigravityTranscriptIsBestEffortAndUnderstandsToolCalls()
    {
        var a = new TrackedAgent("antigravity:example", Provider.Antigravity);
        var lines = Fixture("antigravity"); Apply(a, lines[0]); Apply(a, lines[1]);
        Assert.Equal(AgentState.Working, a.EventState); Assert.Equal("search", a.ToolKind);
        Apply(a, lines[2]); Assert.Equal(AgentState.Thinking, a.EventState);
        Apply(a, lines[3]); Assert.Equal(AgentState.Waiting, a.EventState);
        a.LastSeen = Now; Assert.True(a.Snapshot(Now, new()).BestEffort);
    }

    [Fact]
    public void TailKeepsPartialUtf8AndDoesNotReread()
    {
        var path = Path.Combine(root, "tail.jsonl");
        using var writer = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        var bytes = Encoding.UTF8.GetBytes("{\"text\":\"한글\"}\n");
        writer.Write(bytes[..11]); writer.Flush();
        var tail = new JsonlTail(); Assert.Empty(tail.Read(path));
        writer.Write(bytes[11..]); writer.Flush();
        Assert.Equal("{\"text\":\"한글\"}", Assert.Single(tail.Read(path)));
        var read = tail.BytesRead; Assert.Empty(tail.Read(path)); Assert.Equal(read, tail.BytesRead);
        Assert.Equal(bytes.Length, tail.Offset);
    }

    [Fact]
    public void TailBootstrapIsBoundedAndHandlesTruncation()
    {
        var path = Path.Combine(root, "large.jsonl");
        using (var writer = new StreamWriter(path, false, new UTF8Encoding(false)))
        {
            writer.WriteLine("{\"meta\":true}");
            var padding = new string('x', 1000);
            for (var i = 0; i < 10000; i++) writer.WriteLine(padding);
            writer.WriteLine("{\"recent\":true}");
        }
        var tail = new JsonlTail(); var lines = tail.Read(path);
        Assert.Contains("{\"meta\":true}", lines); Assert.Contains("{\"recent\":true}", lines);
        Assert.True(tail.BytesRead <= JsonlTail.BootstrapBytes * 2L);
        File.WriteAllText(path, "{\"reset\":true}\n");
        Assert.Equal("{\"reset\":true}", Assert.Single(tail.Read(path))); Assert.True(tail.WasReset);
    }

    [Fact]
    public void TailDiscardsOversizeRecordAndRecovers()
    {
        var path = Path.Combine(root, "oversize.jsonl"); File.WriteAllText(path, "{}\n");
        var tail = new JsonlTail(); tail.Read(path);
        File.AppendAllText(path, new string('x', JsonlTail.MaxLineBytes + 1) + "\n{\"ok\":true}\n");
        Assert.Equal("{\"ok\":true}", Assert.Single(tail.Read(path))); Assert.Equal(1, tail.DroppedLines);
    }

    [Fact]
    public void TrackerTailsRealShapesAndTransitionsToGoneThenPrunes()
    {
        var dir = Path.Combine(root, ".codex", "sessions", "2026", "10", "03"); Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "rollout-example.jsonl");
        File.WriteAllLines(path, Fixture("codex").Take(4)); File.SetLastWriteTimeUtc(path, Now.UtcDateTime);
        var options = new TrackerOptions { Home = root, ScanInterval = TimeSpan.FromMilliseconds(1), WorkingWindow = TimeSpan.FromSeconds(2), GoneAfter = TimeSpan.FromSeconds(10), GoneRetention = TimeSpan.FromSeconds(2) };
        var tracker = new AgentTracker(options);
        Assert.Equal(AgentState.Working, Assert.Single(tracker.Poll(Now)).State);
        var bytes = tracker.BytesRead; tracker.Poll(Now.AddSeconds(1)); Assert.Equal(bytes, tracker.BytesRead);
        Assert.Equal(AgentState.Idle, Assert.Single(tracker.Poll(Now.AddSeconds(3))).State);
        Assert.Equal(AgentState.Gone, Assert.Single(tracker.Poll(Now.AddSeconds(11))).State);
        Assert.Empty(tracker.Poll(Now.AddSeconds(14)));
        Assert.Empty(new AgentTracker(options).Poll(Now.AddHours(1)));
    }

    [Fact]
    public void TrackerSkipsMalformedLinesAndConsumesLaterAppends()
    {
        var dir = Path.Combine(root, ".claude", "projects", "example"); Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "parent-1.jsonl");
        File.WriteAllText(path, "broken JSON\n" + """{"type":"user","sessionId":"parent-1","message":{"content":"Example"}}""" + "\n");
        File.SetLastWriteTimeUtc(path, Now.UtcDateTime);
        var tracker = new AgentTracker(new() { Home = root, ScanInterval = TimeSpan.FromMilliseconds(1) });
        Assert.Single(tracker.Poll(Now)); Assert.Single(tracker.Warnings);
        var before = tracker.BytesRead;
        var append = """{"type":"assistant","sessionId":"parent-1","message":{"content":[],"stop_reason":"end_turn"}}""" + "\n";
        File.AppendAllText(path, append); File.SetLastWriteTimeUtc(path, Now.AddSeconds(1).UtcDateTime);
        Assert.Equal(AgentState.Waiting, Assert.Single(tracker.Poll(Now.AddSeconds(1))).State);
        Assert.Equal(Encoding.UTF8.GetByteCount(append), tracker.BytesRead - before);
    }

    [Fact]
    public void AntigravityWalCountsButShmAndAnnotationsDoNot()
    {
        var dir = Path.Combine(root, ".gemini", "antigravity", "conversations"); Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "example.db"); File.WriteAllText(path, "synthetic DB placeholder");
        File.SetLastWriteTimeUtc(path, Now.AddHours(-1).UtcDateTime);
        File.WriteAllText(path + "-shm", "synthetic shm"); File.SetLastWriteTimeUtc(path + "-shm", Now.UtcDateTime);
        var tracker = new AgentTracker(new() { Home = root, ScanInterval = TimeSpan.FromMilliseconds(1) });
        Assert.Empty(tracker.Poll(Now));
        File.WriteAllText(path + "-wal", "synthetic WAL"); File.SetLastWriteTimeUtc(path + "-wal", Now.UtcDateTime);
        var agent = Assert.Single(tracker.Poll(Now.AddSeconds(1))); Assert.Equal(AgentState.Working, agent.State); Assert.True(agent.BestEffort);
    }

    [Fact]
    public void WaitingAndReportingHaveDistinctTimeouts()
    {
        var options = new TrackerOptions();
        var a = new TrackedAgent("example", Provider.Codex) { LastSeen = Now };
        a.State(AgentState.Waiting, Now);
        Assert.Equal(AgentState.Waiting, a.Snapshot(Now.AddMinutes(5), options).State);
        a.State(AgentState.Reporting, Now.AddMinutes(5)); a.LastSeen = Now.AddMinutes(5);
        Assert.Equal(AgentState.Reporting, a.Snapshot(Now.AddMinutes(5).AddSeconds(1), options).State);
        Assert.Equal(AgentState.Idle, a.Snapshot(Now.AddMinutes(5).AddSeconds(13), options).State);
    }

    [Fact]
    public void BfsRoutesAroundFurnitureAndAnimationExits()
    {
        var office = new OfficeAnimator(42);
        var info = new AgentInfo("codex:example", Provider.Codex, null, false, "Example", null, null, null, "", null, null, AgentState.Working, Now, Now, false);
        var views = office.Update([info], Now, 0);
        var destination = Assert.Single(views).Pose.Desk;
        var path = OfficeAnimator.FindPath(office.Layout, office.Layout.Door, destination);
        Assert.Equal(destination, path[^1]);
        var previous = office.Layout.Door;
        foreach (var step in path)
        {
            Assert.Equal(1, Math.Abs(previous.X - step.X) + Math.Abs(previous.Y - step.Y));
            Assert.DoesNotContain(office.Layout.Furniture, f => step.X >= f.X && step.X < f.X + f.Width && step.Y >= f.Y && step.Y < f.Y + f.Height);
            previous = step;
        }
        for (var i = 0; i < 100; i++) views = office.Update([info], Now.AddSeconds(i * .1), .1);
        Assert.Equal("typing", Assert.Single(views).Pose.Action);
        info = info with { State = AgentState.Gone };
        for (var i = 0; i < 100; i++) views = office.Update([info], Now.AddSeconds(20 + i * .1), .1);
        Assert.False(Assert.Single(views).Pose.Visible);
    }

    [Fact]
    public void PendingReportReachesParentAfterSemanticWindowAndMissingParentStillFinishes()
    {
        var office = new OfficeAnimator(42);
        AgentInfo Info(string id, string? parent, AgentState state) => new(id, Provider.Codex, parent, parent != null, id, null, null, null, "", null, null, state, Now, Now, false);
        var parent = Info("parent", null, AgentState.Waiting);
        var worker = Info("worker", "parent", AgentState.Reporting);
        IReadOnlyList<AgentView> views = office.Update([parent, worker], Now, 0);
        // The semantic state can be idle while its arrival/report sequence is still pending.
        worker = worker with { State = AgentState.Idle };
        var delivered = false; var pickedUp = false; var carried = false; var reacted = false;
        for (var i = 0; i < 200; i++)
        {
            views = office.Update([parent, worker], Now.AddSeconds(i * .1), .1);
            var w = views.Single(v => v.Agent.Id == "worker");
            pickedUp |= w.Pose.Action == "pickup";
            carried |= w.Pose.Action == "walking" && w.Pose.CarryingPaper;
            reacted |= views[0].Pose.Reaction == "!" && views[0].Pose.ReportOnDesk;
            if (w.Pose.Action == "reporting")
            { delivered = true; Assert.Equal(new Position(views[0].Pose.Desk.X + 1, views[0].Pose.Desk.Y), new Position(w.Pose.X, w.Pose.Y)); }
        }
        Assert.True(pickedUp); Assert.True(carried); Assert.True(delivered); Assert.True(reacted);
        Assert.Contains(views[1].Pose.Action, new[] { "walking", "coffee", "tv", "phone", "napping", "chatting", "water" });
        office = new(42); worker = Info("orphan", "missing", AgentState.Reporting); office.Update([worker], Now, 0);
        worker = worker with { State = AgentState.Idle };
        for (var i = 0; i < 200; i++) views = office.Update([worker], Now.AddSeconds(i * .1), .1);
        Assert.NotEqual("reporting", Assert.Single(views).Pose.Action);
    }

    [Fact]
    public void NewCharactersEnterAtDoorWhenOfficeExpands()
    {
        var office = new OfficeAnimator(42);
        var agents = Enumerable.Range(0, 30).Select(i => new AgentInfo("agent:" + i, Provider.Codex, null, false, "Example", null, null, null, "", null, null, AgentState.Working, Now, Now, false)).ToArray();
        var views = office.Update(agents, Now, 0);
        Assert.True(office.Layout.HeightTiles > 28);
        Assert.All(views, a => Assert.Equal(new Position(office.Layout.Door.X, office.Layout.Door.Y), new Position(a.Pose.X, a.Pose.Y)));
    }

    [Fact]
    public void OutputCannotPointIntoRealOrSelectedProviderHomes()
    {
        Assert.Throws<ArgumentException>(() => StateJson.OutputPath(Path.Combine(root, ".codex", "generated"), root));
        Assert.Throws<ArgumentException>(() => StateJson.OutputPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "generated"), root));
        Assert.Equal(Path.GetFullPath(root), StateJson.OutputPath(root, root));
    }

    [Fact]
    public void AtlasPngHasValidDimensionsAndDecodableRgba()
    {
        var png = SpriteAtlas.CreatePng();
        Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, png[..8]);
        Assert.Equal(720, BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16)));
        Assert.Equal(1176, BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20)));
        using var compressed = new MemoryStream();
        for (var pos = 8; pos < png.Length;)
        {
            var size = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(pos)); var kind = Encoding.ASCII.GetString(png, pos + 4, 4);
            if (kind == "IDAT") compressed.Write(png, pos + 8, size);
            pos += size + 12;
        }
        compressed.Position = 0; using var z = new ZLibStream(compressed, CompressionMode.Decompress); using var rgba = new MemoryStream(); z.CopyTo(rgba);
        Assert.Equal(1176 * (720 * 4 + 1), rgba.Length); Assert.Contains(rgba.ToArray(), b => b != 0);
        var bytes = rgba.ToArray();
        byte[] Cell(int row, int column) => Enumerable.Range(0, 24).SelectMany(y => bytes.Skip((row * 24 + y) * (720 * 4 + 1) + 1 + column * 16 * 4).Take(64)).ToArray();
        Assert.NotEqual(Cell(0, 13), Cell(0, 17)); // front/back
        Assert.NotEqual(Cell(0, 21), Cell(0, 25)); // right/left
        Assert.NotEqual(Cell(0, 3), Cell(0, 4)); // typing hands
        Assert.NotEqual(Cell(0, 0), Cell(7, 0)); // identity palette
        Assert.Contains(Cell(0, 0).Take(16 * 4 * 3), b => b != 0); // head reaches top of cell
    }

    [Fact]
    public void AtomicSnapshotRoundTripsAndLeavesNoTemporaryFiles()
    {
        var office = new OfficeAnimator(); var path = Path.Combine(root, "state.json");
        var snapshot = new StateSnapshot(StateJson.SchemaVersion, Now, 1, SpriteAtlas.Info, office.Layout, [], []);
        StateJson.WriteAtomic(path, snapshot); StateJson.WriteAtomic(path, snapshot with { Sequence = 2 });
        using var reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var result = JsonSerializer.Deserialize<StateSnapshot>(reader, StateJson.Options)!;
        Assert.Equal(3, result.SchemaVersion); Assert.Equal(2, result.Sequence); Assert.Empty(result.Agents); Assert.Equal(17, result.Office.WidthTiles);
        Assert.Empty(Directory.GetFiles(root, "*.tmp"));
    }

    [Fact]
    public void OfficeCompactsAndGoneCharacterFadesAtDoorBeforeDisappearing()
    {
        AgentInfo Info(int i) => new("agent:" + i, Provider.Claude, null, false, "Example", null, null, null, "", null, null, AgentState.Working, Now, Now, false);
        var office = new OfficeAnimator(12);
        office.Update(Enumerable.Range(0, 12).Select(Info).ToArray(), Now, 0);
        var largeWidth = office.Layout.WidthTiles;
        var agent = Info(0);
        office.Update([agent], Now, 0);
        Assert.True(office.Layout.WidthTiles < largeWidth);
        Assert.Single(office.Layout.Furniture, f => f.Kind == "desk");
        for (var i = 0; i < 150; i++) office.Update([agent], Now.AddSeconds(i * .1), .1);
        agent = agent with { State = AgentState.Gone };
        var fading = false; var hidden = false;
        for (var i = 0; i < 200; i++)
        {
            var pose = Assert.Single(office.Update([agent], Now.AddSeconds(16 + i * .1), .1)).Pose;
            if (pose.Visible && pose.Opacity < 1)
            { fading = true; Assert.Equal(new Position(office.Layout.Door.X, office.Layout.Door.Y), new Position(pose.X, pose.Y)); }
            hidden |= !pose.Visible;
        }
        Assert.True(fading); Assert.True(hidden);
    }

    [Fact]
    public void IdleActivitiesVaryAndChattingHasAReciprocalPartner()
    {
        var office = new OfficeAnimator(42);
        var agents = Enumerable.Range(0, 6).Select(i => new AgentInfo("idle:" + i, (Provider)(i % 3), null, false, "Example", null, null, null, "", null, null, AgentState.Idle, Now, Now, false)).ToArray();
        var activities = new HashSet<string>(); var paired = false;
        for (var i = 0; i < 2400; i++)
        {
            var views = office.Update(agents, Now.AddSeconds(i * .1), .1);
            foreach (var v in views.Where(v => v.Pose.Action is not ("walking" or "entering")))
            {
                activities.Add(v.Pose.Action);
                if (v.Pose.Action != "chatting") continue;
                var friend = Assert.Single(views, f => f.Agent.Id == v.Pose.ChatPartnerId);
                Assert.Equal(v.Agent.Id, friend.Pose.ChatPartnerId); paired = true;
            }
        }
        Assert.True(paired);
        Assert.Subset(new HashSet<string> { "coffee", "tv", "phone", "napping", "chatting", "water" }, activities);
        Assert.Equal(6, activities.Count);
    }

    private AssetPack CustomPack(IReadOnlyList<FurnitureAsset>? furniture = null)
    {
        var rows = new AnimationRow[] { new("idle","any",0,1,1),new("walk","down",1,4,4),new("walk","up",2,4,4),new("reporting","any",3,3,5,Loop:false) };
        var manifest = new AssetManifest(1,[new("people","people.png",16,24,rows,new(8,23),.75)],furniture ?? [],
            Enumerable.Range(0,8).Select(i=>new CharacterDesign("design-"+i,"people")).ToArray(),[]);
        File.WriteAllBytes(Path.Combine(root,"people.png"),SpriteAtlas.CreatePng());
        File.WriteAllText(Path.Combine(root,"manifest.json"),JsonSerializer.Serialize(manifest,StateJson.Options));
        return new(root);
    }
    [Fact]
    public void SheetManifestControlsAnimationAnchorRosterAndExport()
    {
        var pack=CustomPack([new("plant","plant",[new(0,"default",1,1,[],SheetId:"people",Animation:"reporting")])]); Assert.Empty(pack.Warnings);
        var f=pack.Frame("people","walk","up",750);
        Assert.Equal(2,f.Row); Assert.Equal(3,f.Column); Assert.Equal(23,f.AnchorY); Assert.Equal(.75,f.Scale);
        Assert.Equal(0,pack.Frame("people","reporting","any",0,3).Column);
        Assert.Equal(2,pack.Frame("people","reporting","any",9999,3).Column);
        var office=new OfficeAnimator(42,pack);
        var agents=Enumerable.Range(0,8).Select(i=>new AgentInfo("art:"+i,Provider.Codex,null,false,"Demo",null,null,null,"",null,null,AgentState.Working,Now,Now,false)).ToArray();
        var views=office.Update(agents,Now,0);
        Assert.Equal(2,office.FurnitureSprites.Count); Assert.All(office.FurnitureSprites,f=>Assert.Equal(0,f.Sprite.Column));
        Assert.Equal(8,views.Select(v=>v.Pose.CharacterId).Distinct().Count());
        Assert.All(views,v=>Assert.Equal("people",v.Pose.Sprite!.SheetId));
        var reversed=office.Update(agents.Reverse().ToArray(),Now,.1);
        Assert.All(reversed,v=>Assert.Equal(views.Single(a=>a.Agent.Id==v.Agent.Id).Pose.CharacterId,v.Pose.CharacterId));
        office.Update(agents,Now.AddSeconds(1),.1); Assert.All(office.FurnitureSprites,f=>Assert.Equal(2,f.Sprite.Column));
        var exported=pack.Export(Path.Combine(root,"output"));
        Assert.All(exported.Sheets,s=>Assert.True(File.Exists(Path.Combine(root,"output",s.File))));
        Assert.Contains(exported.Sheets,s=>s.Id=="people" && s.Width==720 && s.Height==1176 && s.Sha256!.Length==64);
    }
    [Fact]
    public void InvalidArtAndLayoutsFallBackWithoutOverwritingSource()
    {
        var pack=CustomPack();
        var manifest=JsonSerializer.Deserialize<AssetManifest>(File.ReadAllText(Path.Combine(root,"manifest.json")),StateJson.Options)!;
        File.WriteAllText(Path.Combine(root,"manifest.json"),JsonSerializer.Serialize(manifest with { Sheets=[manifest.Sheets[0] with { File="../outside.png" }] },StateJson.Options));
        var rejected=new AssetPack(root); Assert.Single(rejected.Warnings); Assert.Single(rejected.Manifest.Sheets);
        File.WriteAllText(Path.Combine(root,"manifest.json"),JsonSerializer.Serialize(manifest,StateJson.Options));
        File.WriteAllBytes(Path.Combine(root,"people.png"),SpriteAtlas.CreatePng()[..33]);
        var truncated=new AssetPack(root); Assert.Single(truncated.Warnings); Assert.Single(truncated.Manifest.Sheets);
        var file=Path.Combine(root,"layout.json"); const string invalid="{\"version\":999}"; File.WriteAllText(file,invalid);
        var warnings=new List<string>(); var layout=LayoutLoader.Load(pack,file,warnings);
        Assert.Single(warnings); Assert.Equal(1,layout.Version); Assert.Equal(invalid,File.ReadAllText(file));
    }
    [Fact]
    public void RotatedFurnitureSeatAndWallTilesDriveWorkAndPathfinding()
    {
        var variant=new FurnitureVariant(90,"default",2,3,[new("seat",2,1,"typing","left")]);
        var pack=CustomPack([new("side-desk","desk",[variant])]);
        var layout=LayoutLoader.Load(pack,null) with { AutoDesks=null };
        layout=layout with { Furniture=layout.Furniture.Concat([new Furniture("desk",2,5,2,3,null,"side-1","side-desk",90)]).ToArray() };
        var tiles=layout.Tiles!.ToArray(); tiles[9*layout.WidthTiles+2]=0; layout=layout with { Tiles=tiles };
        var file=Path.Combine(root,"layout.json"); File.WriteAllText(file,JsonSerializer.Serialize(layout,StateJson.Options));
        var loaded=LayoutLoader.Load(pack,file,pack.Warnings); Assert.Empty(pack.Warnings);
        var animator=new OfficeAnimator(1,pack,loaded);
        var agent=new AgentInfo("side",Provider.Codex,null,false,"Side",null,null,null,"",null,null,AgentState.Working,Now,Now,false);
        AgentView view=Assert.Single(animator.Update([agent],Now,0)); Assert.Equal(new Tile(4,6),view.Pose.Desk);
        for(var i=0;i<150;i++) view=Assert.Single(animator.Update([agent],Now.AddSeconds(i*.1),.1));
        Assert.Equal("typing",view.Pose.Action); Assert.Equal("left",view.Pose.Facing);
        Assert.DoesNotContain(new Tile(2,9),OfficeAnimator.FindPath(loaded,loaded.Door,new(4,6)));
    }
    [Fact]
    public void AStarFindsShortestDetourAndRejectsBlockedDestination()
    {
        var tiles=Enumerable.Repeat(1,49).ToArray(); foreach(var y in new[] {2,3,4}) tiles[y*7+3]=0;
        var layout=new OfficeLayout(7,7,16,new(1,3),[],Tiles:tiles);
        var path=OfficeAnimator.FindPath(layout,new(1,3),new(5,3));
        Assert.Equal(8,path.Count); Assert.Equal(new Tile(5,3),path[^1]);
        Assert.Empty(OfficeAnimator.FindPath(layout,new(1,3),new(3,3)));
    }
    [Fact]
    public void DepartedMiddleSlotDoesNotLoseSurvivingDeskOrIdentity()
    {
        var animator=new OfficeAnimator(42);
        var agents=Enumerable.Range(0,6).Select(i=>new AgentInfo("slot:"+i,Provider.Codex,null,false,"Demo",null,null,null,"",null,null,AgentState.Working,Now,Now,false)).ToArray();
        var before=animator.Update(agents,Now,0);
        var after=animator.Update(agents.Where((a,i)=>i!=2).ToArray(),Now.AddSeconds(1),.1);
        Assert.Equal(5,after.Count);
        Assert.All(after,v=>Assert.Equal(before.Single(b=>b.Agent.Id==v.Agent.Id).Pose.CharacterId,v.Pose.CharacterId));
        Assert.Equal(6,animator.Layout.Furniture.Count(f=>f.Kind=="desk"));
    }
    [Fact]
    public void ResumingDuringDoorFadeRestoresOpacity()
    {
        var animator=new OfficeAnimator(42);
        var agent=new AgentInfo("resume",Provider.Codex,null,false,"Demo",null,null,null,"",null,null,AgentState.Working,Now,Now,false);
        for(var i=0;i<100;i++) animator.Update([agent],Now.AddSeconds(i*.1),.1);
        agent=agent with { State=AgentState.Gone }; DateTimeOffset time=Now.AddSeconds(11); var sawFade=false;
        for(var i=0;i<200;i++)
        {
            time=Now.AddSeconds(11+i*.1); var p=Assert.Single(animator.Update([agent],time,.1)).Pose;
            if(p.Visible && p.Opacity<1) { sawFade=true; break; }
        }
        Assert.True(sawFade);
        var resumed=Assert.Single(animator.Update([agent with { State=AgentState.Working }],time.AddMilliseconds(100),.1)).Pose;
        Assert.True(resumed.Visible); Assert.Equal(1,resumed.Opacity); Assert.NotEqual("gone",resumed.Action);
    }
    [Fact]
    public void FakeWindowTopologiesCoverClassicRawDesktopAndSafeQaPlans()
    {
        DesktopWindow[] classic=[new(10,0,"WorkerW"),new(1,0,"Progman"),new(2,1,"SHELLDLL_DefView"),new(3,0,"WorkerW")];
        Assert.Equal(new DesktopHost(3,0),WindowModePlan.FindHost(classic,1));
        DesktopWindow[] raw=[new(1,0,"Progman",0x00200000),new(2,1,"SHELLDLL_DefView"),new(3,1,"WorkerW")];
        Assert.Equal(new DesktopHost(1,2),WindowModePlan.FindHost(raw,1));
        Assert.Null(WindowModePlan.FindHost([new(1,0,"Progman"),new(9,0,"WorkerW")],1));
        var desktop=WindowModePlan.Create("desktop",true,false,true,.85);
        Assert.True(desktop.NoActivate); Assert.False(desktop.Topmost); Assert.False(desktop.ShowInTaskbar); Assert.True(desktop.Borderless);
        var top=WindowModePlan.Create("topmost",false,false,false,5); Assert.True(top.Topmost); Assert.Equal(1,top.Opacity);
        var qa=WindowModePlan.Create("desktop",false,false,false,double.NaN,true);
        Assert.Equal("normal",qa.Mode); Assert.True(qa.NoActivate); Assert.False(qa.Topmost); Assert.False(qa.ShowInTaskbar); Assert.Equal(1,qa.Opacity);
        var normal=WindowModePlan.Create("normal",false,false,false,1); Assert.False(normal.ClickThrough); Assert.False(normal.Borderless);
    }
}
