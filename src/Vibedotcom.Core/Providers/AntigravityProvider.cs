using System.Globalization;
using System.Text.Json;

namespace Vibedotcom.Core.Providers;

internal sealed class AntigravityProvider : IDisposable
{
    private sealed record Summary(string Id, string Title, string? Parent, int Depth, string? Name,
        string[] Workspaces, string? Status, bool Busy, bool Killed, DateTimeOffset Activity, int Steps);
    private sealed record Catalog(string Stamp, List<Summary> Rows);
    private sealed record Steps(string Stamp, AgentState? State, int Count, DateTimeOffset Activity);
    private sealed record Saved(AgentState State, DateTimeOffset Time, string Revision);
    private readonly TrackerOptions options;
    private readonly PrivacyRedactor privacy;
    private readonly AntigravityRuntime? runtime;
    private readonly Dictionary<string, Catalog> catalogs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Steps> steps = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Saved> applied = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (DateTimeOffset Receipt, AgentState State)> appliedLive = new(StringComparer.Ordinal);
    private readonly HashSet<string> databases = new(StringComparer.OrdinalIgnoreCase), transcripts = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, JsonElement> loaded = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> liveRevisions = new(StringComparer.Ordinal);
    private readonly HashSet<string> departed = [];
    private DateTimeOffset nextRuntime, receipt;

    internal AntigravityProvider(TrackerOptions options, PrivacyRedactor? privacy = null)
    {
        this.options = options;
        this.privacy = privacy ?? new(options.PrivacyMode);
        if (Path.GetFullPath(options.Home).Equals(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), StringComparison.OrdinalIgnoreCase))
            runtime = new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Antigravity", "logs"));
    }

    internal void Poll(DateTimeOffset now, bool discover, string[] dirty, Dictionary<string, TrackedAgent> agents,
        HashSet<string> warnings, Action<Provider, FileInfo> read)
    {
        if (now >= nextRuntime || now < receipt)
        {
            nextRuntime = now.AddSeconds(5);
            var observed = runtime?.Read();
            if (observed != null)
            {
                foreach (var id in loaded.Keys.Except(observed.Keys)) departed.Add(id);
                foreach (var id in observed.Keys) departed.Remove(id);
                loaded = observed.ToDictionary(p => p.Key, p =>
                {
                    var revision = PrivacyRedactor.OpaqueId(p.Value.GetRawText());
                    var unchanged = liveRevisions.GetValueOrDefault(p.Key) == revision && loaded.ContainsKey(p.Key);
                    liveRevisions[p.Key] = revision;
                    return unchanged ? loaded[p.Key] : PrivateLive(p.Value);
                }, StringComparer.Ordinal);
                receipt = now;
            }
        }
        foreach (var storage in new[] { "antigravity", "antigravity-cli" })
        {
            var root = Path.Combine(options.Home, ".gemini", storage);
            var summaryPath = Path.Combine(root, "conversation_summaries.db");
            if (File.Exists(summaryPath)) ReadCatalog(summaryPath, warnings);
            if (discover)
            {
                Discover(Path.Combine(root, "conversations"), "*.db", false, databases, warnings, now);
                Discover(Path.Combine(root, "brain"), "transcript*.jsonl", true, transcripts, warnings, now);
            }
            foreach (var id in loaded.Keys)
            {
                var path = Path.Combine(root, "conversations", id + ".db");
                if (File.Exists(path)) databases.Add(path);
                foreach (var filename in new[] { "transcript_full.jsonl", "transcript.jsonl" })
                {
                    path = Path.Combine(root, "brain", id, ".system_generated", "logs", filename);
                    if (File.Exists(path)) transcripts.Add(path);
                }
            }
            foreach (var path in dirty.Where(p => p.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
            {
                var database = path.EndsWith("-wal", StringComparison.OrdinalIgnoreCase) ? path[..^4]
                    : path.EndsWith("-journal", StringComparison.OrdinalIgnoreCase) ? path[..^8] : path;
                if (database.EndsWith(".db", StringComparison.OrdinalIgnoreCase) && Path.GetFileName(Path.GetDirectoryName(database)) == "conversations") databases.Add(database);
                if (IsTranscript(path)) transcripts.Add(path);
            }
        }
        var summaries = catalogs.Values.SelectMany(c => c.Rows).GroupBy(s => s.Id)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(s => s.Activity).ThenByDescending(s => s.Status != null).First(), StringComparer.Ordinal);

        foreach (var path in transcripts.ToArray())
        {
            if (!File.Exists(path)) { transcripts.Remove(path); continue; }
            if (!IsTranscript(path)) continue;
            if (Path.GetFileName(path) == "transcript.jsonl" && File.Exists(Path.Combine(Path.GetDirectoryName(path)!, "transcript_full.jsonl"))) continue;
            var file = new FileInfo(path);
            var id = LogParser.Identity(Provider.Antigravity, path, default)["antigravity:".Length..];
            var age = now - new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero);
            if (age < options.GoneAfter || loaded.ContainsKey(id)) read(Provider.Antigravity, file);
            else if (age >= options.GoneAfter + options.GoneRetention) transcripts.Remove(path);
        }

        foreach (var path in databases.ToArray())
        {
            var file = new FileInfo(path);
            if (!file.Exists) { databases.Remove(path); steps.Remove(path); continue; }
            var id = Path.GetFileNameWithoutExtension(path);
            var key = LogParser.Key(Provider.Antigravity, id);
            if (departed.Contains(id) && !agents.ContainsKey(key)) continue;
            summaries.TryGetValue(id, out var summary);
            var modified = file.LastWriteTimeUtc;
            var wal = new FileInfo(path + "-wal");
            if (wal.Exists && wal.Length > 0 && wal.LastWriteTimeUtc > modified) modified = wal.LastWriteTimeUtc;
            var time = new DateTimeOffset(modified, TimeSpan.Zero);
            var activity = summary?.Activity ?? time;
            if (now - activity >= options.GoneAfter && !loaded.ContainsKey(id) && !agents.ContainsKey(key))
            { databases.Remove(path); steps.Remove(path); continue; }
            ReadSteps(path, warnings);
            if (!agents.TryGetValue(key, out var a)) agents[key] = a = new(key, Provider.Antigravity, privacy);
            if (summary != null) ApplySummary(a, summary);
            var explicitState = summary == null ? null : SavedState(summary.Status, summary.Busy, summary.Killed);
            var revision = "summary";
            // A newly added step may precede the next summary write. Re-query existing idx statuses too.
            if (explicitState != AgentState.Gone && steps.TryGetValue(path, out var saved) && (explicitState == null || saved.Count > (summary?.Steps ?? 0)))
            {
                explicitState = saved.State ?? explicitState;
                if (saved.State != null) { activity = saved.Activity; revision = $"steps:{saved.Count}:{saved.State}"; }
            }
            if (activity > a.LastSeen) a.LastSeen = activity;
            if (explicitState is { } state)
                ApplySaved(a, state, activity, revision);
            else if (!a.Finished && a.EventState is not (AgentState.Waiting or AgentState.Reporting or AgentState.Gone) && time > a.LastEvent)
                LogParser.SetState(a, AgentState.Working, time);
        }
        foreach (var summary in summaries.Values)
        {
            var key = LogParser.Key(Provider.Antigravity, summary.Id);
            if (departed.Contains(summary.Id) && !agents.ContainsKey(key)) continue;
            if (!loaded.ContainsKey(summary.Id) && now - summary.Activity >= options.GoneAfter && !agents.ContainsKey(key)) continue;
            if (!agents.TryGetValue(key, out var a)) agents[key] = a = new(key, Provider.Antigravity, privacy);
            ApplySummary(a, summary);
            if (summary.Activity > a.LastSeen) a.LastSeen = summary.Activity;
            if (SavedState(summary.Status, summary.Busy, summary.Killed) is { } state &&
                !databases.Any(p => Path.GetFileNameWithoutExtension(p) == summary.Id)) ApplySaved(a, state, summary.Activity, "summary");
        }
        if (now - receipt < options.GoneAfter)
            foreach (var (id, value) in loaded)
            {
                var key = LogParser.Key(Provider.Antigravity, id);
                if (!agents.TryGetValue(key, out var a)) agents[key] = a = new(key, Provider.Antigravity, privacy);
                ApplyLiveMetadata(a, value);
                if (LiveState(value) is { } state)
                {
                    var observation = (receipt, state);
                    if ((!appliedLive.TryGetValue(id, out var previous) || previous.State != state ||
                        state is AgentState.Working or AgentState.Thinking && previous.Receipt != receipt) && receipt >= a.LastEvent)
                    {
                        if (receipt > a.LastSeen) a.LastSeen = receipt;
                        LogParser.SetState(a, state, receipt);
                    }
                    appliedLive[id] = observation;
                }
            }
        foreach (var id in departed)
            if (agents.TryGetValue(LogParser.Key(Provider.Antigravity, id), out var a)) ApplyState(a, AgentState.Gone, now);
    }

    private void ReadCatalog(string path, HashSet<string> warnings)
    {
        try
        {
            var stamp = SqliteSnapshot.Stamp(path);
            if (catalogs.TryGetValue(path, out var old) && old.Stamp == stamp) return;
            using var db = new SqliteSnapshot(path, privacy.Mode != PrivacyMode.Off);
            if (db.Query("PRAGMA user_version").Single()[0] != "3") throw new IOException("Unknown Antigravity summary schema.");
            var rows = db.Query("SELECT conversation_id,title,parent_conversation_id,nesting_depth,agent_name,workspace_uris,status,not_fully_idle,killed,last_modified_time,last_user_input_time,step_count FROM conversation_summaries");
            var result = new List<Summary>();
            foreach (var r in rows)
            {
                if (string.IsNullOrEmpty(r[0])) continue;
                var modified = LogParser.ParseTime(r[9]) ?? DateTimeOffset.MinValue;
                var input = LogParser.ParseTime(r[10]) ?? DateTimeOffset.MinValue;
                result.Add(new(r[0]!, privacy.Task(r[1] ?? ""), Empty(r[2]), Number(r[3]), privacy.Detail(Empty(r[4])),
                    (string.IsNullOrWhiteSpace(r[5]) ? [] : JsonSerializer.Deserialize<string[]>(r[5]!) ?? [])
                        .Select(WorkspacePath).Select(privacy.Cwd).OfType<string>().ToArray(),
                    Empty(r[6]), Number(r[7]) != 0, Number(r[8]) != 0, modified > input ? modified : input, Number(r[11])));
            }
            catalogs[path] = new(stamp, result); // Preserve the previous good catalog after any copy/query failure.
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or DllNotFoundException or EntryPointNotFoundException)
        { warnings.Add($"Antigravity summary unavailable; retaining cached metadata ({e.GetType().Name})."); }
    }

    private void ReadSteps(string path, HashSet<string> warnings)
    {
        try
        {
            var stamp = SqliteSnapshot.Stamp(path);
            if (steps.TryGetValue(path, out var old) && old.Stamp == stamp) return;
            using var db = new SqliteSnapshot(path, privacy.Mode != PrivacyMode.Off);
            if (db.Query("PRAGMA user_version").Single()[0] != "1") throw new IOException("Unknown Antigravity step schema.");
            var rows = db.Query("SELECT idx,step_type,status FROM steps WHERE idx IN (SELECT idx FROM steps ORDER BY idx DESC LIMIT 8) OR status IN (1,2,8,9,11) ORDER BY idx DESC LIMIT 24");
            AgentState? state = null;
            if (rows.Count > 0)
            {
                var active = rows.FirstOrDefault(r => Number(r[2]) is 1 or 2 or 8 or 9 or 11);
                state = active == null ? AgentState.Idle : Number(active[2]) == 9
                    ? Number(active[1]) == 138 ? AgentState.Waiting : AgentState.Thinking
                    : Number(active[1]) == 15 ? AgentState.Thinking : AgentState.Working;
            }
            var count = rows.Count == 0 ? 0 : Number(rows[0][0]) + 1;
            var modified = File.GetLastWriteTimeUtc(path);
            var wal = new FileInfo(path + "-wal");
            if (wal.Exists && wal.Length > 0 && wal.LastWriteTimeUtc > modified) modified = wal.LastWriteTimeUtc;
            var activity = old != null && old.State == state && old.Count == count ? old.Activity : new DateTimeOffset(modified, TimeSpan.Zero);
            steps[path] = new(stamp, state, count, activity);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or DllNotFoundException or EntryPointNotFoundException)
        { warnings.Add($"Antigravity steps unavailable; using summary/transcript fallback ({e.GetType().Name})."); }
    }

    private static void ApplyState(TrackedAgent a, AgentState state, DateTimeOffset time)
    {
        // Saved/runtime explicit state outranks transcript creation time and maintenance mtime.
        LogParser.SetState(a, state, time > a.LastEvent ? time : a.LastEvent);
    }

    private void ApplySaved(TrackedAgent a, AgentState state, DateTimeOffset time, string revision)
    {
        var observation = new Saved(state, time, revision);
        if (applied.TryGetValue(a.Id, out var previous) && previous == observation) return;
        applied[a.Id] = observation;
        // An old stored IDLE/RUNNING must not erase a newer transcript's real user/completion event.
        if (time >= a.LastEvent) LogParser.SetState(a, state, time);
    }

    private static void ApplySummary(TrackedAgent a, Summary s)
    {
        if (s.Title.Length > 0) a.Name = LogParser.Clip(s.Title);
        else if (s.Name != null) a.Name = LogParser.Clip(s.Name);
        if (a.Task.Length == 0) a.Task = LogParser.Clip(s.Title);
        if (s.Parent != null && LogParser.Key(Provider.Antigravity, s.Parent) != a.Id) a.ParentId = LogParser.Key(Provider.Antigravity, s.Parent);
        a.IsSubagent |= a.ParentId != null || s.Depth > 0;
        a.Cwd = s.Workspaces.FirstOrDefault() ?? a.Cwd;
    }

    internal static AgentState? SavedState(string? status, bool busy, bool killed) => killed ? AgentState.Gone
        : busy ? AgentState.Working : status switch
        {
            "CASCADE_RUN_STATUS_IDLE" => AgentState.Idle,
            "CASCADE_RUN_STATUS_RUNNING" or "CASCADE_RUN_STATUS_BUSY" or "CASCADE_RUN_STATUS_CANCELING" => AgentState.Working,
            _ => null
        };

    internal static AgentState? LiveState(JsonElement root)
    {
        if (Flag(root, "killed")) return AgentState.Gone;
        if (Flag(root, "fullyIdle")) return AgentState.Idle;
        return SavedState(LogParser.String(root, "status"), Flag(root, "notFullyIdle"), Flag(root, "killed"));
    }

    private static void ApplyLiveMetadata(TrackedAgent a, JsonElement root)
    {
        var annotations = Get(root, "annotations");
        var title = LogParser.String(annotations, "title") ?? LogParser.String(root, "summary");
        if (!string.IsNullOrEmpty(title)) a.Name = LogParser.Clip(title);
        var metadata = Get(root, "trajectoryMetadata");
        var parent = LogParser.String(metadata, "parentConversationId");
        if (parent != null && LogParser.Key(Provider.Antigravity, parent) != a.Id) a.ParentId = LogParser.Key(Provider.Antigravity, parent);
        var depth = Get(metadata, "nestingDepth");
        a.IsSubagent |= a.ParentId != null || (depth.ValueKind == JsonValueKind.Number && depth.TryGetInt32(out var number) && number > 0);
        var boundary = Get(Get(Get(root, "latestTaskBoundaryStep"), "step"), "taskBoundary");
        if (LogParser.String(boundary, "taskName") is { } task) a.Task = LogParser.Clip(task);
        var workspaces = Get(root, "workspaces");
        if (LogParser.String(root, "privacyCwd") is {} privateCwd) a.Cwd = privateCwd;
        if (workspaces.ValueKind == JsonValueKind.Array)
            a.Cwd = workspaces.EnumerateArray().Select(w => WorkspacePath(LogParser.String(w, "workspaceFolderAbsoluteUri"))).FirstOrDefault(p => p != null) ?? a.Cwd;
    }

    internal void Scrub()
    {
        foreach (var path in catalogs.Keys.ToArray())
        {
            var catalog = catalogs[path];
            catalogs[path] = catalog with { Rows = catalog.Rows.Select(s => s with
            { Title = privacy.Task(s.Title), Name = privacy.Detail(s.Name), Workspaces = s.Workspaces.Select(privacy.Cwd).OfType<string>().ToArray() }).ToList() };
        }
        loaded = loaded.ToDictionary(p => p.Key, p => PrivateLive(p.Value), StringComparer.Ordinal);
    }

    private JsonElement PrivateLive(JsonElement root)
    {
        if (privacy.Mode == PrivacyMode.Off) return root;
        var metadata = Get(root, "trajectoryMetadata");
        var workspaces = Get(root, "workspaces");
        var cwd = LogParser.String(root, "privacyCwd") ?? (workspaces.ValueKind == JsonValueKind.Array
            ? workspaces.EnumerateArray().Select(w => WorkspacePath(LogParser.String(w, "workspaceFolderAbsoluteUri"))).FirstOrDefault(p => p != null) : null);
        var status = LogParser.String(root, "status");
        var depth = Get(metadata,"nestingDepth");
        return JsonSerializer.SerializeToElement(new
        {
            status = status is "CASCADE_RUN_STATUS_IDLE" or "CASCADE_RUN_STATUS_RUNNING" or "CASCADE_RUN_STATUS_BUSY" or "CASCADE_RUN_STATUS_CANCELING" ? status : null,
            killed = Flag(root, "killed"), fullyIdle = Flag(root, "fullyIdle"), notFullyIdle = Flag(root, "notFullyIdle"),
            trajectoryMetadata = new { parentConversationId = LogParser.String(metadata, "parentConversationId"), nestingDepth = depth.ValueKind == JsonValueKind.Number && depth.TryGetInt32(out var number) ? number : 0 },
            privacyCwd = privacy.Cwd(cwd)
        });
    }

    internal static string? WorkspacePath(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || !uri.IsFile) return null;
        var path = uri.LocalPath;
        // An encoded drive colon is parsed as a Unix file URI even on Windows.
        if (OperatingSystem.IsWindows() && path.Length >= 4 && path[0] == '/' && char.IsAsciiLetter(path[1]) && path[2] == ':' && path[3] == '/')
            path = path[1..].Replace('/', '\\');
        return path;
    }
    private static string? Empty(string? value) => string.IsNullOrEmpty(value) ? null : value;
    private static int Number(string? value) => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0;
    private static bool Flag(JsonElement root, string key) => Get(root, key).ValueKind == JsonValueKind.True;
    private static JsonElement Get(JsonElement root, string key) => root.ValueKind == JsonValueKind.Object && root.TryGetProperty(key, out var value) ? value : default;

    private static bool IsTranscript(string path) => (Path.GetFileName(path) is "transcript.jsonl" or "transcript_full.jsonl") &&
        (Path.GetFileName(Path.GetDirectoryName(path)) == "logs" ||
            Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(path))) == "brain");

    private void Discover(string root, string pattern, bool recursive, HashSet<string> paths, HashSet<string> warnings, DateTimeOffset now)
    {
        if (!Directory.Exists(root)) return;
        try
        {
            foreach (var path in Directory.EnumerateFiles(root, pattern, new EnumerationOptions
            { RecurseSubdirectories = recursive, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint }))
            {
                var modified = File.GetLastWriteTimeUtc(path);
                if (path.EndsWith(".db", StringComparison.OrdinalIgnoreCase))
                {
                    var wal = new FileInfo(path + "-wal");
                    if (wal.Exists && wal.Length > 0 && wal.LastWriteTimeUtc > modified) modified = wal.LastWriteTimeUtc;
                }
                if (now - new DateTimeOffset(modified, TimeSpan.Zero) < options.GoneAfter) paths.Add(path);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { warnings.Add($"Cannot discover Antigravity files ({e.GetType().Name})."); }
    }

    public void Dispose() => runtime?.Dispose();
}
