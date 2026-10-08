using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Officelings.App;
using Officelings.Core;
using Officelings.Core.Providers;
using Xunit;

namespace Officelings.Tests;

public sealed class DepartureTests : IDisposable
{
    private readonly string home = Path.Combine(Path.GetTempPath(), "officelings-departure-" + Guid.NewGuid().ToString("N"));
    private static readonly DateTimeOffset Epoch = DateTimeOffset.Parse("2026-10-04T00:00:00Z");
    private TrackerOptions Options => new() { Home = home };
    public DepartureTests() => Directory.CreateDirectory(home);
    public void Dispose() => Directory.Delete(home, true);
    private string PathFor(params string[] parts)
    {
        var path = Path.Combine(new[] { home }.Concat(parts).ToArray());
        Directory.CreateDirectory(Path.GetDirectoryName(path)!); return path;
    }
    private static void Write(string path, string text, double seconds = 0)
    { File.WriteAllText(path, text); File.SetLastWriteTimeUtc(path, Epoch.AddSeconds(seconds).UtcDateTime); }
    private static string Event(string type, object payload, double seconds = 0) =>
        JsonSerializer.Serialize(new { timestamp = Epoch.AddSeconds(seconds).ToString("O"), type, payload }) + "\n";
    private string Codex(string id, string? parent = null, string kind = "task_complete", double seconds = 0)
    {
        var path = PathFor(".codex", "sessions", "rollout-" + id + ".jsonl");
        Write(path, Event("session_meta", new { id, parent_thread_id = parent }, seconds) + Event("event_msg", new { type = kind }, seconds), seconds);
        return path;
    }
    private string Registration(string id, int pid = 987654, string? domain = null)
    {
        var path = PathFor(".claude", "sessions", pid + ".json");
        Write(path, JsonSerializer.Serialize(new { pid, sessionId = id, pidDomain = domain ?? "win32:" + Environment.MachineName,
            procStart = "12345678", peerToken = "invented-unused-peer-token" })); return path;
    }
    private string Claude(string id, string? child = null)
    {
        var path = child == null ? PathFor(".claude", "projects", "demo", id + ".jsonl")
            : PathFor(".claude", "projects", "demo", id, "subagents", "agent-" + child + ".jsonl");
        Write(path, JsonSerializer.Serialize(new { timestamp = Epoch.ToString("O"), type = "assistant", sessionId = id,
            message = new { stop_reason = "end_turn", content = new[] { new { type = "text", text = "Invented completed task." } } } }) + "\n");
        return path;
    }

    [Theory]
    [InlineData(Provider.Codex, false)] [InlineData(Provider.Codex, true)]
    [InlineData(Provider.Claude, false)] [InlineData(Provider.Claude, true)]
    [InlineData(Provider.Antigravity, false)] [InlineData(Provider.Antigravity, true)]
    public void CompletedAgentsLeaveAtFiveMinutesFromRealEvent(Provider provider, bool worker)
    {
        string path;
        if (provider == Provider.Codex) path = Codex("finished", worker ? "parent" : null);
        else if (provider == Provider.Claude) path = Claude("finished", worker ? "child" : null);
        else
        {
            path = PathFor(".gemini", "antigravity", "brain", "finished", "transcript.jsonl");
            Write(path, """{"type":"PLANNER_RESPONSE","status":"DONE","created_at":"2026-10-04T00:00:00Z","content":"Invented completion"}""" + "\n");
            // Antigravity lineage is supplied by its saved summary rather than transcript text.
            if (worker)
            {
                var dbPath = PathFor(".gemini", "antigravity", "conversation_summaries.db");
                using var db = new TestDb(dbPath);
                db.Exec("PRAGMA user_version=3; CREATE TABLE conversation_summaries(conversation_id TEXT,title TEXT,parent_conversation_id TEXT,nesting_depth INTEGER,agent_name TEXT,workspace_uris TEXT,status TEXT,not_fully_idle INTEGER,killed INTEGER,last_modified_time TEXT,last_user_input_time TEXT,step_count INTEGER);" +
                    $"INSERT INTO conversation_summaries VALUES('finished','Invented worker','parent',1,'','[]','CASCADE_RUN_STATUS_IDLE',0,0,'{Epoch:O}','{Epoch:O}',1);");
            }
        }
        using var tracker = new AgentTracker(Options);
        var initial = Assert.Single(tracker.Poll(Epoch)); Assert.Equal(worker, initial.IsSubagent);
        // A maintenance write/mtime refresh is deliberately later than the real completion event.
        File.SetLastWriteTimeUtc(path, Epoch.AddSeconds(299).UtcDateTime);
        Assert.Equal(AgentState.Idle, Assert.Single(tracker.Poll(Epoch.AddSeconds(299))).State);
        Assert.Equal(AgentState.Gone, Assert.Single(tracker.Poll(Epoch.AddMinutes(5))).State);
    }

    [Theory]
    [InlineData(Provider.Codex)] [InlineData(Provider.Claude)] [InlineData(Provider.Antigravity)]
    public void ExplicitInputWaitUsesThirtyMinuteThreshold(Provider provider)
    {
        var a = new TrackedAgent("waiting", provider) { LastSeen = Epoch };
        var text = provider switch
        {
            Provider.Codex => Event("event_msg", new { type = "request_user_input" }),
            Provider.Claude => """{"type":"assistant","message":{"content":[{"type":"tool_use","name":"AskUserQuestion","id":"question"}]}}""",
            _ => """{"type":"PLANNER_RESPONSE","status":"WAITING_FOR_USER"}"""
        };
        using var doc = JsonDocument.Parse(text); LogParser.Apply(a, doc.RootElement, "waiting.jsonl", Epoch);
        Assert.Equal(AgentState.Waiting, a.Snapshot(Epoch.AddMinutes(5), Options).State);
        Assert.Equal(AgentState.Waiting, a.Snapshot(Epoch.AddMinutes(30).AddTicks(-1), Options).State);
        Assert.Equal(AgentState.Gone, a.Snapshot(Epoch.AddMinutes(30), Options).State);
    }

    [Theory]
    [InlineData(AgentState.Working)] [InlineData(AgentState.Thinking)]
    public void WorkingWindowIsStill120SecondsAndMtimeCannotExtendIt(AgentState state)
    {
        var a = new TrackedAgent("worker", Provider.Codex) { LastSeen = Epoch.AddMinutes(4) }; a.State(state, Epoch);
        Assert.Equal(state, a.Snapshot(Epoch.AddSeconds(119), Options).State);
        Assert.Equal(AgentState.Idle, a.Snapshot(Epoch.AddSeconds(120), Options).State);
        Assert.Equal(AgentState.Gone, a.Snapshot(Epoch.AddSeconds(300), Options).State);
    }

    [Fact]
    public void ActiveDescendantKeepsParentAndGrandparentInOffice()
    {
        Codex("grandparent"); Codex("parent", "grandparent"); Codex("child", "parent", "task_started", 299);
        using var tracker = new AgentTracker(Options);
        tracker.Poll(Epoch);
        var agents = tracker.Poll(Epoch.AddMinutes(5));
        Assert.Equal(AgentState.Thinking, agents.Single(a => a.Id == "codex:child").State);
        Assert.All(agents.Where(a => a.Id != "codex:child"), a => Assert.Equal(AgentState.Idle, a.State));
        // When the child is no longer active, old completed parents can leave.
        Assert.All(tracker.Poll(Epoch.AddSeconds(420)).Where(a => a.Id != "codex:child"), a => Assert.Equal(AgentState.Gone, a.State));
    }

    [Theory]
    [InlineData(303)] [InlineData(400)]
    public void NewRealActivityRevivesRetainedOrPrunedAgent(int seconds)
    {
        var path = Codex("returning");
        using var tracker = new AgentTracker(Options); tracker.Poll(Epoch);
        Assert.Equal(AgentState.Gone, Assert.Single(tracker.Poll(Epoch.AddSeconds(300))).State);
        if (seconds > 360) Assert.Empty(tracker.Poll(Epoch.AddSeconds(361)));
        File.AppendAllText(path, Event("event_msg", new { type = "user_message", message = "Invented new work." }, seconds));
        File.SetLastWriteTimeUtc(path, Epoch.AddSeconds(seconds).UtcDateTime);
        Assert.Equal(AgentState.Thinking, Assert.Single(tracker.Poll(Epoch.AddSeconds(seconds))).State);
    }

    [Fact]
    public void ClosedClaudePidLeavesAfterGraceAndProbesOncePerScan()
    {
        Claude("parent"); Claude("parent", "worker"); var registration = Registration("parent");
        var calls = 0;
        using var tracker = new AgentTracker(Options, (pid, start) => { calls++; Assert.Equal(987654, pid); Assert.Equal(12345678L, start); return false; });
        Assert.All(tracker.Poll(Epoch), a => Assert.NotEqual(AgentState.Gone, a.State));
        tracker.Poll(Epoch.AddSeconds(2.9)); Assert.Equal(1, calls);
        tracker.Poll(Epoch.AddSeconds(3)); Assert.Equal(2, calls);
        File.Delete(registration); // Claude can clean up its registry when exiting.
        Assert.All(tracker.Poll(Epoch.AddSeconds(9)), a => Assert.NotEqual(AgentState.Gone, a.State));
        Assert.All(tracker.Poll(Epoch.AddSeconds(10)), a => Assert.Equal(AgentState.Gone, a.State));
        Assert.Equal(3, calls);
    }

    [Fact]
    public void TransientMissingPidDoesNotCloseAResumedSession()
    {
        Claude("parent"); Registration("parent"); var alive = false;
        using var tracker = new AgentTracker(Options, (_, _) => alive);
        tracker.Poll(Epoch); alive = true;
        Assert.Equal(AgentState.Idle, Assert.Single(tracker.Poll(Epoch.AddSeconds(9))).State);
        Assert.Equal(AgentState.Idle, Assert.Single(tracker.Poll(Epoch.AddSeconds(12))).State);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void UnknownOrForeignPidDomainFallsBackToActivityTimeout(bool foreign)
    {
        Claude("parent"); Registration("parent", domain: foreign ? "linux:other-host:namespace" : null);
        var calls = 0;
        using var tracker = new AgentTracker(Options, (_, _) => { calls++; return null; }); // Access denied is unknown.
        tracker.Poll(Epoch);
        Assert.Equal(AgentState.Idle, Assert.Single(tracker.Poll(Epoch.AddSeconds(30))).State);
        Assert.Equal(AgentState.Gone, Assert.Single(tracker.Poll(Epoch.AddSeconds(300))).State);
        Assert.Equal(foreign ? 0 : 3, calls);
    }

    [Fact]
    public void CodexArchiveFlagUsesCopiedDatabaseAndUnarchiveAllowsNewWork()
    {
        var path = Codex("thread"); var dbPath = PathFor(".codex", "state_5.sqlite");
        using var writer = new TestDb(dbPath);
        writer.Exec("PRAGMA journal_mode=WAL; PRAGMA wal_autocheckpoint=0; CREATE TABLE threads(id TEXT, archived INTEGER); INSERT INTO threads VALUES('thread',0); PRAGMA wal_checkpoint(TRUNCATE);");
        using var tracker = new AgentTracker(Options); tracker.Poll(Epoch);
        writer.Exec("UPDATE threads SET archived=1;"); var fingerprint = Fingerprint(dbPath);
        Assert.Equal(AgentState.Gone, Assert.Single(tracker.Poll(Epoch.AddSeconds(3))).State);
        Assert.Equal(fingerprint, Fingerprint(dbPath));
        writer.Exec("UPDATE threads SET archived=0;");
        File.AppendAllText(path, Event("event_msg", new { type = "task_started" }, 6)); File.SetLastWriteTimeUtc(path, Epoch.AddSeconds(6).UtcDateTime);
        Assert.Equal(AgentState.Thinking, Assert.Single(tracker.Poll(Epoch.AddSeconds(6))).State);
    }

    [Fact]
    public void CodexMovedArchiveClosesWithoutDatabase()
    {
        var path = Codex("thread"); using var tracker = new AgentTracker(Options); tracker.Poll(Epoch);
        File.Move(path, PathFor(".codex", "archived_sessions", Path.GetFileName(path)));
        Assert.Equal(AgentState.Gone, Assert.Single(tracker.Poll(Epoch.AddSeconds(3))).State);
    }

    [Theory]
    [InlineData("shutdown")] [InlineData("shutdown_complete")] [InlineData("session_closed")]
    public void CodexExplicitShutdownClosesButCompletionOnlyStartsIdleTimer(string kind)
    {
        var path = Codex("thread"); using var tracker = new AgentTracker(Options);
        Assert.Equal(AgentState.Idle, Assert.Single(tracker.Poll(Epoch)).State);
        File.AppendAllText(path, Event("event_msg", new { type = kind }, 3)); File.SetLastWriteTimeUtc(path, Epoch.AddSeconds(3).UtcDateTime);
        Assert.Equal(AgentState.Gone, Assert.Single(tracker.Poll(Epoch.AddSeconds(3))).State);
    }

    [Fact]
    public void UnknownCodexDatabaseSchemaDoesNotInventClosure()
    {
        Codex("thread"); var path = PathFor(".codex", "state_5.sqlite");
        using (var writer = new TestDb(path)) writer.Exec("CREATE TABLE threads(id TEXT, status TEXT); INSERT INTO threads VALUES('thread','unknown');");
        using var tracker = new AgentTracker(Options);
        Assert.Equal(AgentState.Idle, Assert.Single(tracker.Poll(Epoch)).State);
        Assert.Contains(tracker.Warnings, w => w.Contains("archive state unavailable"));
        Assert.Equal(AgentState.Gone, Assert.Single(tracker.Poll(Epoch.AddSeconds(300))).State);
    }

    [Fact]
    public void UnknownAntigravityStateFallsBackToTimeThreshold()
    {
        var path = PathFor(".gemini", "antigravity", "brain", "unknown", "transcript.jsonl");
        Write(path, """{"type":"UNKNOWN","status":"UNKNOWN","created_at":"2026-10-04T00:00:00Z"}""" + "\n");
        using var tracker = new AgentTracker(Options);
        Assert.Equal(AgentState.Idle, Assert.Single(tracker.Poll(Epoch)).State);
        Assert.Equal(AgentState.Idle, Assert.Single(tracker.Poll(Epoch.AddSeconds(299))).State);
        Assert.Equal(AgentState.Gone, Assert.Single(tracker.Poll(Epoch.AddSeconds(300))).State);
    }

    [Fact]
    public void KilledAntigravityConversationLeavesAndOverridesRunningSteps()
    {
        var path = PathFor(".gemini", "antigravity", "conversation_summaries.db");
        using var catalog = new TestDb(path);
        catalog.Exec("PRAGMA user_version=3; CREATE TABLE conversation_summaries(conversation_id TEXT,title TEXT,parent_conversation_id TEXT,nesting_depth INTEGER,agent_name TEXT,workspace_uris TEXT,status TEXT,not_fully_idle INTEGER,killed INTEGER,last_modified_time TEXT,last_user_input_time TEXT,step_count INTEGER);" +
            $"INSERT INTO conversation_summaries VALUES('killed','Invented closed conversation','',0,'','[]','CASCADE_RUN_STATUS_IDLE',0,1,'{Epoch:O}','{Epoch:O}',1);");
        var stepsPath = PathFor(".gemini", "antigravity", "conversations", "killed.db");
        using var steps = new TestDb(stepsPath);
        steps.Exec("PRAGMA user_version=1; CREATE TABLE steps(idx INTEGER PRIMARY KEY,step_type INTEGER,status INTEGER); INSERT INTO steps VALUES(1,21,2);");
        File.SetLastWriteTimeUtc(stepsPath, Epoch.UtcDateTime);
        using var tracker = new AgentTracker(Options);
        Assert.Equal(AgentState.Gone, Assert.Single(tracker.Poll(Epoch)).State);
        using var live = JsonDocument.Parse("""{"killed":true,"fullyIdle":true,"notFullyIdle":true}""");
        Assert.Equal(AgentState.Gone, AntigravityProvider.LiveState(live.RootElement));
    }

    [Theory]
    [InlineData("{\"goneMinutes\":45}", 45)]
    [InlineData("{\"goneMinutes\":45,\"leaveAfterWaitingMinutes\":20}", 20)]
    [InlineData("{\"leaveAfterWaitingMinutes\":20,\"goneMinutes\":45}", 20)]
    [InlineData("{}", 30)]
    public void LegacyGoneMinutesMigratesToWaitingOnlyAndCanonicalKeyWins(string json, double expected)
    {
        var path = PathFor("settings.json"); Write(path, json);
        var settings = Settings.Read(path);
        Assert.Equal(5, settings.LeaveAfterIdleMinutes); Assert.Equal(expected, settings.LeaveAfterWaitingMinutes);
        Assert.Equal(expected, settings.GoneMinutes); settings.Write(path); Assert.Equal(settings, Settings.Read(path));
        var options = new TrackerOptions { GoneAfter = TimeSpan.FromMinutes(expected) };
        Assert.Equal(TimeSpan.FromMinutes(expected), options.LeaveAfterWaiting); Assert.Equal(TimeSpan.FromMinutes(5), options.LeaveAfterIdle);
        Assert.Equal(TimeSpan.FromMinutes(20), (options with { LeaveAfterWaiting = TimeSpan.FromMinutes(20), GoneAfter = TimeSpan.FromMinutes(45) }).LeaveAfterWaiting);
    }

    [Theory]
    [InlineData(AgentState.Idle)] [InlineData(AgentState.Working)]
    public void ExitStartsAtExactPositionAndReturnEntersAtDoor(AgentState initial)
    {
        var office = new OfficeAnimator(42);
        AgentInfo Info(AgentState state) => new("traveler", Provider.Codex, null, false, "Invented traveler", null, null, null, "", null, null, state, Epoch, Epoch, false);
        IReadOnlyList<AgentView> views = [];
        for (var frame = 0; frame < 203; frame++) views = office.Update([Info(initial)], Epoch.AddSeconds(frame * .1), .1);
        var before = Assert.Single(views).Pose;
        var departure = Assert.Single(office.Update([Info(AgentState.Gone)], Epoch.AddSeconds(20.3), 0)).Pose;
        Assert.Equal(before.X, departure.X); Assert.Equal(before.Y, departure.Y); Assert.Equal("exiting", departure.Action);
        for (var frame = 204; frame < 600; frame++) views = office.Update([Info(AgentState.Gone)], Epoch.AddSeconds(frame * .1), .1);
        Assert.False(Assert.Single(views).Pose.Visible);
        var returning = Assert.Single(office.Update([Info(AgentState.Working)], Epoch.AddSeconds(60), 0)).Pose;
        Assert.True(returning.Visible); Assert.Equal("entering", returning.Action);
        Assert.Equal(office.Layout.Door.X, returning.X); Assert.Equal(office.Layout.Door.Y, returning.Y);
    }

    private static string Fingerprint(string path) => string.Join("|", new[] { "", "-wal", "-shm" }.Select(suffix =>
    {
        var file = new FileInfo(path + suffix);
        if (!file.Exists) return "missing";
        using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return Convert.ToHexString(SHA256.HashData(stream)) + file.LastWriteTimeUtc.Ticks;
    }));
    private sealed class TestDb : IDisposable
    {
        private readonly IntPtr db;
        public TestDb(string path) => Assert.Equal(0, SqliteSnapshot.Native.sqlite3_open_v2(path, out db, 6, IntPtr.Zero));
        public void Exec(string sql) => Assert.Equal(0, sqlite3_exec(db, sql, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero));
        public void Dispose() => Assert.Equal(0, SqliteSnapshot.Native.sqlite3_close(db));
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_exec(IntPtr db, [MarshalAs(UnmanagedType.LPUTF8Str)] string sql, IntPtr callback, IntPtr arg, IntPtr error);
    }
}
