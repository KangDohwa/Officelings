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
        var delivered = false;
        for (var i = 0; i < 200; i++)
        {
            views = office.Update([parent, worker], Now.AddSeconds(i * .1), .1);
            var w = views.Single(v => v.Agent.Id == "worker");
            if (w.Pose.Action == "reporting")
            { delivered = true; Assert.Equal(new Position(views[0].Pose.Desk.X, views[0].Pose.Desk.Y), new Position(w.Pose.X, w.Pose.Y)); }
        }
        Assert.True(delivered); Assert.Contains(views[1].Pose.Action, new[] { "walking", "coffee", "tv", "phone", "napping", "chatting" });
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
        Assert.Equal(208, BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16)));
        Assert.Equal(168, BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20)));
        using var compressed = new MemoryStream();
        for (var pos = 8; pos < png.Length;)
        {
            var size = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(pos)); var kind = Encoding.ASCII.GetString(png, pos + 4, 4);
            if (kind == "IDAT") compressed.Write(png, pos + 8, size);
            pos += size + 12;
        }
        compressed.Position = 0; using var z = new ZLibStream(compressed, CompressionMode.Decompress); using var rgba = new MemoryStream(); z.CopyTo(rgba);
        Assert.Equal(168 * (208 * 4 + 1), rgba.Length); Assert.Contains(rgba.ToArray(), b => b != 0);
    }

    [Fact]
    public void AtomicSnapshotRoundTripsAndLeavesNoTemporaryFiles()
    {
        var office = new OfficeAnimator(); var path = Path.Combine(root, "state.json");
        var snapshot = new StateSnapshot(1, Now, 1, SpriteAtlas.Info, office.Layout, [], []);
        StateJson.WriteAtomic(path, snapshot); StateJson.WriteAtomic(path, snapshot with { Sequence = 2 });
        using var reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var result = JsonSerializer.Deserialize<StateSnapshot>(reader, StateJson.Options)!;
        Assert.Equal(1, result.SchemaVersion); Assert.Equal(2, result.Sequence); Assert.Empty(result.Agents); Assert.Equal(48, result.Office.WidthTiles);
        Assert.Empty(Directory.GetFiles(root, "*.tmp"));
    }
}
