using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Vibedotcom.Core;
using Vibedotcom.Core.Providers;
using Xunit;

namespace Vibedotcom.Tests;

public sealed class TrackerFixTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "Vibedotcom-tracker-fixes-" + Guid.NewGuid().ToString("N"));
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-03T12:00:10Z");
    public TrackerFixTests() => Directory.CreateDirectory(root);
    public void Dispose() => Directory.Delete(root, true);
    private string PathFor(params string[] parts)
    {
        var path = Path.Combine(new[] { root }.Concat(parts).ToArray());
        Directory.CreateDirectory(Path.GetDirectoryName(path)!); return path;
    }
    private static void Write(string path, string text, DateTimeOffset? time = null)
    { File.WriteAllText(path, text, new UTF8Encoding(false)); File.SetLastWriteTimeUtc(path, (time ?? Now).UtcDateTime); }
    private static string Codex(string type, object payload, DateTimeOffset? time = null) =>
        JsonSerializer.Serialize(new { timestamp = (time ?? Now).ToString("O"), type, payload }) + "\n";
    private static void Apply(TrackedAgent a, string text, DateTimeOffset? time = null)
    { using var doc = JsonDocument.Parse(text); LogParser.Apply(a, doc.RootElement, "example.jsonl", time ?? Now); }
    private TrackerOptions Options => new() { Home = root, WorkingWindow = TimeSpan.FromSeconds(2), LeaveAfterIdle = TimeSpan.FromSeconds(10), GoneAfter = TimeSpan.FromSeconds(10),
        GoneRetention = TimeSpan.FromSeconds(2), ScanInterval = TimeSpan.FromMilliseconds(1) };

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void TailDetectsOverwriteReplacementAndRegrowth(bool replace, bool grow)
    {
        var path = PathFor("tail.jsonl"); Write(path, "{\"old\":1}\n");
        var created = File.GetCreationTimeUtc(path); var written = File.GetLastWriteTimeUtc(path);
        var tail = new JsonlTail(); tail.Read(path);
        var text = "{\"new\":2}\n" + (grow ? "{\"next\":3}\n" : "");
        if (replace) { var next = PathFor("next.jsonl"); Write(next, text); File.Move(next, path, true); }
        else Write(path, text);
        File.SetCreationTimeUtc(path, created); File.SetLastWriteTimeUtc(path, written);
        Assert.Contains("{\"new\":2}", tail.Read(path)); Assert.True(tail.WasReset);
        File.AppendAllText(path, "{\"last\":4}\n");
        Assert.Equal("{\"last\":4}", Assert.Single(tail.Read(path))); Assert.False(tail.WasReset);
        Assert.Equal(new FileInfo(path).Length, tail.Offset);
    }

    [Fact]
    public void ReplacementDiscardsPendingUtf8()
    {
        var path = PathFor("partial.jsonl");
        var bytes = Encoding.UTF8.GetBytes("{\"text\":\"한글\"}"); File.WriteAllBytes(path, bytes[..11]);
        var tail = new JsonlTail(); Assert.Empty(tail.Read(path));
        Write(path, "{\"new\":true}\n");
        Assert.Equal("{\"new\":true}", Assert.Single(tail.Read(path))); Assert.True(tail.WasReset);
    }

    [Theory]
    [InlineData("\n", false)]
    [InlineData("\r\n", false)]
    [InlineData("\n", true)]
    public void BootstrapPreservesBoundaryAndSkipsOnlyPartialLine(string newline, bool partial)
    {
        var path = PathFor("large.jsonl");
        var suffix = (partial ? "inside한글🙂" + newline : "") + "{\"recent\":true}" + newline;
        var suffixBytes = Encoding.UTF8.GetBytes(suffix);
        using (var file = File.Create(path))
        {
            file.Write(Encoding.UTF8.GetBytes("{}" + newline));
            var prefixLength = JsonlTail.BootstrapBytes * 2 + 100;
            file.Write(Encoding.UTF8.GetBytes(new string('x', prefixLength - (int)file.Position - newline.Length) + newline));
            if (partial) { file.Position -= newline.Length; file.Write(Encoding.ASCII.GetBytes(new string('x', newline.Length))); }
            file.Write(suffixBytes);
            file.Write(Encoding.ASCII.GetBytes(new string('\n', JsonlTail.BootstrapBytes - suffixBytes.Length)));
        }
        var tail = new JsonlTail(); var lines = tail.Read(path);
        Assert.Contains("{\"recent\":true}", lines);
        if (partial) Assert.DoesNotContain(lines, s => s.Contains("inside"));
        Assert.True(tail.BytesRead <= JsonlTail.BootstrapBytes * 2L);
    }

    [Fact]
    public void CompletedOversizeAndResetReleaseLargeBuffersButPartialSurvives()
    {
        var path = PathFor("buffer.jsonl"); Write(path, "{}");
        var tail = new JsonlTail(); tail.Read(path);
        File.AppendAllText(path, new string('x', 1024 * 1024)); tail.Read(path);
        Assert.True(tail.RetainedBytes >= 1024 * 1024);
        File.AppendAllText(path, "\n"); Assert.Single(tail.Read(path)); Assert.True(tail.RetainedBytes <= 64 * 1024);
        File.AppendAllText(path, new string('x', JsonlTail.MaxLineBytes + 1) + "\n{}\n");
        Assert.Equal("{}", Assert.Single(tail.Read(path))); Assert.True(tail.RetainedBytes <= 64 * 1024);
        File.AppendAllText(path, new string('x', 1024 * 1024)); tail.Read(path);
        Write(path, "{}\n"); tail.Read(path); Assert.True(tail.RetainedBytes <= 64 * 1024);
    }

    [Fact]
    public void TrackerDoesNotBypassReplacementWithIdenticalMetadata()
    {
        var path = PathFor(".codex", "sessions", "rollout-example.jsonl");
        Write(path, Codex("session_meta", new { id = "first1" }));
        using var tracker = new AgentTracker(Options); Assert.Equal("codex:first1", Assert.Single(tracker.Poll(Now)).Id);
        var next = PathFor("next.jsonl"); Write(next, Codex("session_meta", new { id = "second" }));
        File.Move(next, path, true); File.SetLastWriteTimeUtc(path, Now.UtcDateTime);
        Assert.Equal("codex:second", Assert.Single(tracker.Poll(Now.AddSeconds(1))).Id);
    }

    [Fact]
    public void MixedClaudeRecordsRemainSeparateAndUnknownSidechainIsDeferred()
    {
        var path = PathFor(".claude", "projects", "example", "parent.jsonl");
        Write(path, """
            {"type":"progress","sessionId":"parent"}
            {"type":"user","sessionId":"parent","message":{"content":"Parent task"}}
            {"type":"assistant","sessionId":"parent","agentId":"one","isSidechain":true,"message":{"stop_reason":"end_turn","content":[]}}
            {"type":"assistant","sessionId":"parent","agentId":"two","isSidechain":true,"message":{"stop_reason":"end_turn","content":[]}}
            {"type":"assistant","sessionId":"parent","isSidechain":true,"message":{"stop_reason":"end_turn","content":[]}}
            {"type":"assistant","sessionId":"parent","message":{"stop_reason":"end_turn","content":[]}}
            """ + "\n");
        using var tracker = new AgentTracker(Options); var agents = tracker.Poll(Now);
        Assert.Equal(3, agents.Count);
        var parent = agents.Single(a => a.Id == "claude:parent"); Assert.False(parent.IsSubagent); Assert.Null(parent.ParentId);
        Assert.Equal(AgentState.Idle, parent.State);
        Assert.All(agents.Where(a => a.IsSubagent), a => { Assert.Equal("claude:parent", a.ParentId); Assert.NotEqual(a.Id, a.ParentId); });
    }

    [Fact]
    public void PrunedWorkerResumesWithCachedMetadataAndReplacementClearsIt()
    {
        var path = PathFor(".codex", "sessions", "rollout-child.jsonl");
        Write(path, Codex("session_meta", new { id = "child", parent_thread_id = "parent", agent_nickname = "Example", agent_role = "reviewer", model = "test", cwd = "C:/Example" }) +
            Codex("event_msg", new { type = "session_end" }));
        using var tracker = new AgentTracker(Options); tracker.Poll(Now); Assert.Empty(tracker.Poll(Now.AddSeconds(3)));
        File.AppendAllText(path, Codex("event_msg", new { type = "user_message", message = "Resume" }, Now.AddSeconds(4)));
        File.SetLastWriteTimeUtc(path, Now.AddSeconds(4).UtcDateTime);
        var a = Assert.Single(tracker.Poll(Now.AddSeconds(4))); Assert.True(a.IsSubagent); Assert.Equal("codex:parent", a.ParentId);
        Assert.Equal("Example", a.Name); Assert.Equal("reviewer", a.Role); Assert.Equal("test", a.Model); Assert.Equal("C:/Example", a.Cwd);
        Write(path, Codex("session_meta", new { id = "child", source = "cli" }, Now.AddSeconds(5)), Now.AddSeconds(5));
        a = Assert.Single(tracker.Poll(Now.AddSeconds(5))); Assert.False(a.IsSubagent); Assert.Null(a.ParentId); Assert.Null(a.Model);
    }

    [Theory]
    [InlineData("review")]
    [InlineData("compact")]
    [InlineData("memory_consolidation")]
    [InlineData("unknown")]
    public void StringSubagentSourceMeansWorkerWithoutInventedParent(string role)
    {
        var a = new TrackedAgent("codex:child", Provider.Codex);
        Apply(a, Codex("session_meta", new { id = "child", source = new { subagent = role }, agent_type = "tester" }));
        Assert.True(a.IsSubagent); Assert.Null(a.ParentId); Assert.Equal("tester", a.Role);
    }

    [Fact]
    public void OlderTasksContextAndMissingMetadataNeverRewindFields()
    {
        var a = new TrackedAgent("codex:child", Provider.Codex);
        Apply(a, Codex("session_meta", new { id = "child", parent_thread_id = "parent", agent_nickname = "Example", model = "new", cwd = "C:/New" }));
        Apply(a, Codex("event_msg", new { type = "user_message", message = "Latest task" }));
        Apply(a, Codex("event_msg", new { type = "user_message", message = "Old task" }, Now.AddSeconds(-5)));
        Apply(a, Codex("turn_context", new { model = "old", cwd = "C:/Old" }, Now.AddSeconds(-5)));
        Apply(a, Codex("session_meta", new { id = "child", source = "cli" }, Now.AddSeconds(-5)));
        Assert.Equal("Latest task", a.Task); Assert.Equal("new", a.Model); Assert.Equal("C:/New", a.Cwd);
        Assert.True(a.IsSubagent); Assert.Equal("codex:parent", a.ParentId); Assert.Equal("Example", a.Name);
        Apply(a, Codex("event_msg", new { type = "user_message", message = "Equal-time task" })); Assert.Equal("Equal-time task", a.Task);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(240)]
    [InlineData(360)]
    public void FutureTimestampCannotBlockNormalCompletion(int futureSeconds)
    {
        var a = new TrackedAgent("codex:parent", Provider.Codex);
        Apply(a, Codex("event_msg", new { type = "task_started" }, Now.AddSeconds(futureSeconds)));
        Apply(a, Codex("event_msg", new { type = "task_complete" }));
        Assert.Equal(AgentState.Idle, a.EventState); Assert.Equal(Now, a.LastEvent);
    }

    [Theory]
    [InlineData("2026-10-03T12:00:10Z")]
    [InlineData("2026-10-03T21:00:10+09:00")]
    [InlineData("2026-10-03T08:00:10-04:00")]
    [InlineData("2026-10-03 12:00:10+00:00")]
    [InlineData("2026-10-03T12:00:10")]
    public void TimestampPolicyIsUtcAndCultureIndependent(string timestamp)
    {
        var old = CultureInfo.CurrentCulture;
        try { CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-GB"); Assert.Equal(Now, LogParser.ParseTime(timestamp)); }
        finally { CultureInfo.CurrentCulture = old; }
        Assert.Null(LogParser.ParseTime("0001-01-01 00:00:00+00:00"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ParallelClaudeQuestionWaitsUntilMatchingResult(bool reverse)
    {
        var a = new TrackedAgent("claude:parent", Provider.Claude);
        var question = "{\"type\":\"tool_use\",\"name\":\"AskUserQuestion\",\"id\":\"q\"}";
        var read = "{\"type\":\"tool_use\",\"name\":\"Read\",\"id\":\"r\"}";
        Apply(a, "{\"type\":\"assistant\",\"message\":{\"content\":[" + (reverse ? read + "," + question : question + "," + read) + "]}}");
        Assert.Equal(AgentState.Waiting, a.EventState);
        Apply(a, """{"type":"user","message":{"content":[{"type":"tool_result","tool_use_id":"r","content":"Read complete"}]}}""");
        Assert.Equal(AgentState.Waiting, a.EventState);
        Apply(a, """{"type":"user","message":{"content":[{"type":"tool_result","tool_use_id":"q","content":"Answer"}]}}""");
        Assert.Equal(AgentState.Thinking, a.EventState); Assert.Equal("", a.Task);
    }

    [Fact]
    public void ReadDeniedWriterPreservesLivenessAndRetriesUnreadBytes()
    {
        var path = PathFor(".codex", "sessions", "rollout-parent.jsonl");
        Write(path, Codex("session_meta", new { id = "parent" }) + Codex("event_msg", new { type = "task_started" }));
        using var tracker = new AgentTracker(Options); tracker.Poll(Now); var before = tracker.BytesRead;
        var append = Codex("event_msg", new { type = "task_complete" }, Now.AddSeconds(11));
        using (var writer = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Write))
        {
            writer.Write(Encoding.UTF8.GetBytes(append)); writer.Flush(); File.SetLastWriteTimeUtc(path, Now.AddSeconds(11).UtcDateTime);
            var a = Assert.Single(tracker.Poll(Now.AddSeconds(11))); Assert.Equal(AgentState.Gone, a.State); Assert.Equal(Now.AddSeconds(11), a.LastSeen);
            Assert.Equal(before, tracker.BytesRead); Assert.Contains(tracker.Warnings, w => w.Contains("IOException"));
        }
        Assert.Equal(AgentState.Idle, Assert.Single(tracker.Poll(Now.AddSeconds(12))).State);
        Assert.Equal(Encoding.UTF8.GetByteCount(append), tracker.BytesRead - before);
        before = tracker.BytesRead; tracker.Poll(Now.AddSeconds(13)); Assert.Equal(before, tracker.BytesRead);
    }

    [Fact]
    public void SameStateResumeAfterTimeoutStartsNewElapsedTime()
    {
        var a = new TrackedAgent("codex:parent", Provider.Codex) { LastSeen = Now };
        Apply(a, Codex("response_item", new { type = "function_call", name = "exec_command" }));
        Assert.Equal(AgentState.Idle, a.Snapshot(Now.AddSeconds(3), Options).State);
        Apply(a, Codex("response_item", new { type = "function_call", name = "exec_command" }, Now.AddSeconds(4)), Now.AddSeconds(4));
        Assert.Equal(Now.AddSeconds(4), a.StateSince); Assert.Equal(AgentState.Working, a.DisplayState);
    }

    [Fact]
    public void SteadyScanAvoidsArchiveAndWatcherFindsResumedOldFolder()
    {
        var archive=Path.GetDirectoryName(PathFor(".codex","sessions","old","rollout-0.jsonl"))!;
        var record=Encoding.UTF8.GetBytes("{}\n");
        for (var i = 0; i < 1000; i++)
        { var file=Path.Combine(archive,$"rollout-{i}.jsonl"); File.WriteAllBytes(file,record); File.SetLastWriteTimeUtc(file,Now.AddDays(-1).UtcDateTime); }
        using var tracker = new AgentTracker(Options); Assert.Empty(tracker.Poll(Now)); var visited = tracker.DiscoveryFilesVisited;
        tracker.Poll(Now.AddSeconds(1)); Assert.Equal(visited, tracker.DiscoveryFilesVisited);
        var path = PathFor(".codex", "sessions", "old", "rollout-1.jsonl");
        File.AppendAllText(path, Codex("session_meta", new { id = "resumed" }, Now.AddSeconds(2))); File.SetLastWriteTimeUtc(path, Now.AddSeconds(2).UtcDateTime);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        Assert.True(SpinWait.SpinUntil(() => tracker.Poll(Now.AddSeconds(2).AddMilliseconds(clock.ElapsedMilliseconds)).Any(a => a.Id == "codex:resumed"), 3000));
        Assert.Equal(visited, tracker.DiscoveryFilesVisited);
    }

    [Fact]
    public void SqliteSnapshotIncludesWalAndNeverChangesOriginalFiles()
    {
        var path = PathFor("wal.db");
        using var writer = new TestDb(path);
        writer.Exec("PRAGMA journal_mode=WAL; PRAGMA wal_autocheckpoint=0; CREATE TABLE state(value TEXT); INSERT INTO state VALUES('IDLE'); PRAGMA wal_checkpoint(TRUNCATE);");
        writer.Exec("UPDATE state SET value='RUNNING';");
        var before = Fingerprint(path);
        using (var copy = new SqliteSnapshot(path)) Assert.Equal("RUNNING", copy.Query("SELECT value FROM state").Single()[0]);
        Assert.Equal(before, Fingerprint(path));
    }

    [Fact]
    public void SavedIdleTitleWorkspaceAndLineageWinOverMaintenanceAndCliMirror()
    {
        var summaryPath = PathFor(".gemini", "antigravity", "conversation_summaries.db");
        using var catalog = SummaryDb(summaryPath, "CASCADE_RUN_STATUS_IDLE", "child", "parent");
        var dbPath = PathFor(".gemini", "antigravity", "conversations", "child.db");
        using var db = StepDb(dbPath, 3);
        var mirrorPath = PathFor(".gemini", "antigravity-cli", "conversation_summaries.db");
        using var mirror = SummaryDb(mirrorPath, "", "child", "parent");
        var transcript = PathFor(".gemini", "antigravity", "brain", "child", ".system_generated", "logs", "transcript.jsonl");
        Write(transcript, """{"type":"PLANNER_RESPONSE","status":"DONE","created_at":"2026-10-03T12:00:10Z","content":"Complete"}""" + "\n");
        File.SetLastWriteTimeUtc(dbPath, Now.AddSeconds(1).UtcDateTime);
        using var tracker = new AgentTracker(Options);
        var a = Assert.Single(tracker.Poll(Now.AddSeconds(1))); Assert.Equal(AgentState.Idle, a.State); Assert.True(a.BestEffort);
        Assert.Equal("Example title", a.Name); Assert.Equal("antigravity:parent", a.ParentId); Assert.True(a.IsSubagent); Assert.Equal("C:\\Example\\Project", a.Cwd, ignoreCase: true);
        File.SetLastWriteTimeUtc(dbPath, Now.AddSeconds(2).UtcDateTime);
        a = Assert.Single(tracker.Poll(Now.AddSeconds(2))); Assert.Equal(AgentState.Idle, a.State); Assert.Equal(Now, a.LastSeen);
    }

    [Fact]
    public void SameSqliteStepRunningToDoneIsObservedWithoutNewIndex()
    {
        var path = PathFor(".gemini", "antigravity", "conversations", "example.db");
        using var writer = StepDb(path, 2); File.SetLastWriteTimeUtc(path, Now.UtcDateTime);
        using var tracker = new AgentTracker(Options); Assert.Equal(AgentState.Working, Assert.Single(tracker.Poll(Now)).State);
        writer.Exec("UPDATE steps SET status=3 WHERE idx=0;"); File.SetLastWriteTimeUtc(path, Now.AddSeconds(1).UtcDateTime);
        Assert.Equal(AgentState.Idle, Assert.Single(tracker.Poll(Now.AddSeconds(1))).State);
    }

    [Fact]
    public void NewUserInputRevivesAfterOlderSavedIdleAndMaintenanceDoesNotResetElapsed()
    {
        var path = PathFor(".gemini", "antigravity", "conversation_summaries.db");
        using var catalog = SummaryDb(path, "CASCADE_RUN_STATUS_IDLE");
        var transcript = PathFor(".gemini", "antigravity", "brain", "example", "transcript.jsonl");
        Write(transcript, """{"type":"PLANNER_RESPONSE","status":"DONE","created_at":"2026-10-03T12:00:10Z","content":"Complete"}""" + "\n");
        using var tracker = new AgentTracker(Options); Assert.Equal(AgentState.Idle, Assert.Single(tracker.Poll(Now)).State);
        File.AppendAllText(transcript, """{"type":"USER_INPUT","status":"DONE","created_at":"2026-10-03T12:00:11Z","content":"New example task"}""" + "\n");
        File.SetLastWriteTimeUtc(transcript, Now.AddSeconds(1).UtcDateTime);
        var a = Assert.Single(tracker.Poll(Now.AddSeconds(1))); Assert.Equal(AgentState.Thinking, a.State); Assert.Equal("New example task", a.Task);
        a = Assert.Single(tracker.Poll(Now.AddSeconds(4))); Assert.Equal(AgentState.Idle, a.State); var since = a.StateSince;
        a = Assert.Single(tracker.Poll(Now.AddSeconds(5))); Assert.Equal(since, a.StateSince);
    }

    [Fact]
    public void UnchangedStoredRunningAndMaintenanceDoNotResetIdleElapsed()
    {
        var path = PathFor(".gemini", "antigravity", "conversations", "example.db");
        using var db = StepDb(path, 2); File.SetLastWriteTimeUtc(path, Now.UtcDateTime);
        using var tracker = new AgentTracker(Options); tracker.Poll(Now);
        var a = Assert.Single(tracker.Poll(Now.AddSeconds(3))); Assert.Equal(AgentState.Idle, a.State); var since = a.StateSince;
        File.SetLastWriteTimeUtc(path, Now.AddSeconds(4).UtcDateTime);
        a = Assert.Single(tracker.Poll(Now.AddSeconds(4))); Assert.Equal(AgentState.Idle, a.State); Assert.Equal(since, a.StateSince); Assert.Equal(Now, a.LastSeen);
    }

    [Fact]
    public void TranscriptCompletionSurvivesUnknownDatabaseMaintenance()
    {
        var path = PathFor(".gemini", "antigravity", "conversations", "example.db"); Write(path, "unsupported database");
        var transcript = PathFor(".gemini", "antigravity", "brain", "example", ".system_generated", "logs", "transcript.jsonl");
        Write(transcript, """{"type":"PLANNER_RESPONSE","status":"DONE","created_at":"2026-10-03T12:00:10Z","content":"Complete"}""" + "\n");
        File.SetLastWriteTimeUtc(path, Now.AddSeconds(1).UtcDateTime);
        using var tracker = new AgentTracker(Options); Assert.Equal(AgentState.Idle, Assert.Single(tracker.Poll(Now.AddSeconds(1))).State);
    }

    [Fact]
    public void BrokenSummaryRetainsLastGoodMetadataAndIdle()
    {
        var path = PathFor(".gemini", "antigravity", "conversation_summaries.db");
        using (var db = SummaryDb(path, "CASCADE_RUN_STATUS_IDLE")) { }
        using var tracker = new AgentTracker(Options); Assert.Equal("Example title", Assert.Single(tracker.Poll(Now)).Name);
        Write(path, "corrupt snapshot", Now.AddSeconds(1));
        var a = Assert.Single(tracker.Poll(Now.AddSeconds(1))); Assert.Equal("Example title", a.Name); Assert.Equal(AgentState.Idle, a.State);
        Assert.Contains(tracker.Warnings, w => w.Contains("retaining cached metadata"));
    }

    [Fact]
    public void StaleRunningCatalogDoesNotSpawnAndShmOrEmptyWalCannotReviveIt()
    {
        var path = PathFor(".gemini", "antigravity", "conversation_summaries.db");
        using var db = SummaryDb(path, "CASCADE_RUN_STATUS_RUNNING", time: Now.AddHours(-1));
        var conversation = PathFor(".gemini", "antigravity", "conversations", "example.db");
        Write(conversation, "placeholder", Now.AddHours(-1)); Write(conversation + "-wal", "", Now); Write(conversation + "-shm", "view-only", Now);
        using var tracker = new AgentTracker(Options); Assert.Empty(tracker.Poll(Now));
    }

    [Fact]
    public void RuntimeUsesConversationMapKeysAndExplicitIdleOverBusyHeuristic()
    {
        using var doc = JsonDocument.Parse("""{"trajectorySummaries":{"conversation":{"trajectoryId":"different-id","status":"CASCADE_RUN_STATUS_IDLE"}}}""");
        Assert.Equal("conversation", Assert.Single(AntigravityRuntime.Loaded(doc.RootElement)).Key);
        using var empty = JsonDocument.Parse("{}"); Assert.Empty(AntigravityRuntime.Loaded(empty.RootElement));
        using var invalid = JsonDocument.Parse("{\"unknown\":true}"); Assert.Throws<JsonException>(() => AntigravityRuntime.Loaded(invalid.RootElement));
        using var wrongRoot = JsonDocument.Parse("[]"); Assert.Throws<JsonException>(() => AntigravityRuntime.Loaded(wrongRoot.RootElement));
        using var idle = JsonDocument.Parse("""{"fullyIdle":true,"status":"CASCADE_RUN_STATUS_BUSY","hasActiveChildren":true}""");
        Assert.Equal(AgentState.Idle, AntigravityProvider.LiveState(idle.RootElement));
        Assert.Null(AntigravityProvider.SavedState("UNKNOWN", false, false));
    }

    private static string Fingerprint(string path) => string.Join("|", new[] { "", "-wal", "-shm" }.Select(s =>
    {
        if (!File.Exists(path + s)) return "missing";
        using var stream = new FileStream(path + s, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return Convert.ToHexString(SHA256.HashData(stream)) + File.GetLastWriteTimeUtc(path + s).Ticks;
    }));

    private static TestDb SummaryDb(string path, string status, string id = "example", string parent = "", DateTimeOffset? time = null)
    {
        var db = new TestDb(path);
        db.Exec("PRAGMA user_version=3; CREATE TABLE conversation_summaries(conversation_id TEXT,title TEXT,parent_conversation_id TEXT,nesting_depth INTEGER,agent_name TEXT,workspace_uris TEXT,status TEXT,not_fully_idle INTEGER,killed INTEGER,last_modified_time TEXT,last_user_input_time TEXT,step_count INTEGER);");
        db.Exec($"INSERT INTO conversation_summaries VALUES('{id}','Example title','{parent}',0,'','[\"file:///c%3A/Example/Project\"]','{status}',0,0,'{(time ?? Now):O}','0001-01-01 00:00:00+00:00',1);");
        return db;
    }
    private static TestDb StepDb(string path, int status)
    {
        var db = new TestDb(path); db.Exec($"PRAGMA user_version=1; CREATE TABLE steps(idx INTEGER PRIMARY KEY,step_type INTEGER,status INTEGER); INSERT INTO steps VALUES(0,21,{status});"); return db;
    }
    private sealed class TestDb : IDisposable
    {
        private readonly IntPtr handle;
        public TestDb(string path) => Assert.Equal(0, SqliteSnapshot.Native.sqlite3_open_v2(path, out handle, 6, IntPtr.Zero));
        public void Exec(string sql) => Assert.Equal(0, sqlite3_exec(handle, sql, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero));
        public void Dispose() => Assert.Equal(0, SqliteSnapshot.Native.sqlite3_close(handle));
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_exec(IntPtr db, [MarshalAs(UnmanagedType.LPUTF8Str)] string sql, IntPtr callback, IntPtr arg, IntPtr error);
    }
}
