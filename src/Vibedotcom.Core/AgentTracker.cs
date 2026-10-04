using System.Collections.Concurrent;
using System.Text.Json;
using Vibedotcom.Core.Providers;

namespace Vibedotcom.Core;

public sealed class AgentTracker : IDisposable
{
    private sealed class Source(Provider provider)
    {
        public readonly JsonlTail Tail = new();
        public readonly Provider Provider = provider;
        public readonly Dictionary<string, TrackedAgent> Cached = new(StringComparer.Ordinal);
        public string? AgentId;
        public DateTime Modified;
    }
    private readonly TrackerOptions options;
    private readonly object gate = new();
    private readonly PrivacyRedactor privacy;
    public PrivacyMode PrivacyMode => privacy.Mode;
    private readonly Dictionary<string, Source> sources = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TrackedAgent> agents = new(StringComparer.Ordinal);
    private readonly HashSet<string> warnings = [];
    private readonly Dictionary<string, FileSystemWatcher> watchers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> dirty = new(StringComparer.OrdinalIgnoreCase);
    private readonly AntigravityProvider antigravity;
    private readonly ClosedSessions closedSessions;
    private DateTimeOffset nextScan, nextDiscovery, lastPoll;
    private int overflow;
    public long BytesRead => sources.Values.Sum(s => s.Tail.BytesRead);
    public IReadOnlyList<string> Warnings => warnings.Order().ToArray();
    internal long DiscoveryFilesVisited { get; private set; }

    public AgentTracker(TrackerOptions? options = null) : this(options ?? new(), null) { }

    internal AgentTracker(TrackerOptions options, Func<int, long?, bool?>? processAlive)
    {
        this.options = options ?? new();
        this.options.Validate();
        privacy = new(this.options.PrivacyMode);
        antigravity = new(this.options, privacy);
        closedSessions = new(this.options.Home, processAlive);
    }

    public IReadOnlyList<AgentInfo> Poll(DateTimeOffset now)
    {
        lock (gate) return PollCore(now);
    }

    public void SetPrivacy(PrivacyMode mode)
    {
        lock (gate)
        {
            privacy.SetMode(mode);
            foreach (var agent in agents.Values.Concat(sources.Values.SelectMany(s => s.Cached.Values)).Distinct()) agent.Scrub();
            antigravity.Scrub();
        }
    }

    public IReadOnlyList<AgentInfo> Redact(IReadOnlyList<AgentInfo> values)
    {
        lock (gate) return privacy.Mode == PrivacyMode.Off ? values : values.Select(privacy.Redact).ToArray();
    }

    private IReadOnlyList<AgentInfo> PollCore(DateTimeOffset now)
    {
        if (now < lastPoll) nextScan = nextDiscovery = now;
        lastPoll = now;
        if (now >= nextScan)
        {
            nextScan = now + options.ScanInterval;
            warnings.Clear();
            var discover = now >= nextDiscovery || Interlocked.Exchange(ref overflow, 0) != 0;
            foreach (var (provider, root, pattern) in new[]
            {
                (Provider.Codex, Path.Combine(options.Home, ".codex", "sessions"), "rollout-*.jsonl"),
                (Provider.Claude, Path.Combine(options.Home, ".claude", "projects"), "*.jsonl")
            })
            {
                if (Watch(root)) discover = true;
                var candidates = sources.Where(s => s.Value.Provider == provider).Select(s => s.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var path in dirty.Keys)
                    if (path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && dirty.TryRemove(path, out _))
                        candidates.Add(path);
                if (discover)
                    foreach (var file in Files(root, pattern, true))
                    {
                        DiscoveryFilesVisited++;
                        if (Recent(file.LastWriteTimeUtc, now) || sources.ContainsKey(file.FullName)) candidates.Add(file.FullName);
                    }
                foreach (var path in candidates)
                {
                    if (provider == Provider.Claude && path.Contains(Path.DirectorySeparatorChar + "tool-results" + Path.DirectorySeparatorChar)) continue;
                    var file = new FileInfo(path);
                    if (file.Exists && (Recent(file.LastWriteTimeUtc, now) || sources.ContainsKey(path))) Read(provider, file);
                }
            }
            if (Watch(Path.Combine(options.Home, ".gemini"))) discover = true;
            var changed = dirty.Keys.Where(p => p.StartsWith(Path.Combine(options.Home, ".gemini") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)).ToArray();
            foreach (var path in changed) dirty.TryRemove(path, out _);
            antigravity.Poll(now, discover, changed, agents, warnings, Read);
            closedSessions.Poll(now, agents, warnings, sources.Where(s => s.Value.Provider == Provider.Codex)
                .Select(s => (s.Key, s.Value.Cached.Keys.ToArray())));
            if (discover) nextDiscovery = now + TimeSpan.FromMinutes(1);
        }
        var activeParents = new HashSet<string>(StringComparer.Ordinal);
        foreach (var child in agents.Values.Where(a => a.StateAt(now, options) is AgentState.Working or AgentState.Thinking or AgentState.Waiting or AgentState.Reporting))
        {
            var parent = child.ParentId;
            var visited = new HashSet<string>(StringComparer.Ordinal) { child.Id };
            while (parent != null && visited.Add(parent))
            {
                activeParents.Add(parent);
                parent = agents.TryGetValue(parent, out var a) ? a.ParentId : null;
            }
        }
        var snapshots = agents.Values.Select(a => (Key:a.Id, Value:a.Snapshot(now, options, activeParents.Contains(a.Id)))).ToArray();
        foreach (var old in snapshots.Where(a => a.Value.State == AgentState.Gone && now - a.Value.StateSince >= options.GoneRetention))
            agents.Remove(old.Key);
        foreach (var path in sources.Where(s => now - new DateTimeOffset(s.Value.Modified, TimeSpan.Zero) >= options.DiscoveryWindow + options.GoneRetention &&
                     !s.Value.Cached.Keys.Any(activeParents.Contains))
                     .Select(s => s.Key).ToArray()) sources.Remove(path);
        return snapshots.Where(a => agents.ContainsKey(a.Key)).Select(a=>a.Value).OrderBy(a => a.Id, StringComparer.Ordinal).ToArray();
    }

    private bool Recent(DateTime time, DateTimeOffset now) => now - new DateTimeOffset(time, TimeSpan.Zero) < options.DiscoveryWindow;

    private bool Watch(string root)
    {
        if (watchers.ContainsKey(root) || !Directory.Exists(root)) return false;
        try
        {
            var watcher = new FileSystemWatcher(root)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName | NotifyFilters.DirectoryName
            };
            void Changed(object sender, FileSystemEventArgs e)
            {
                if (e.FullPath.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase) || e.FullPath.EndsWith(".db", StringComparison.OrdinalIgnoreCase) ||
                    e.FullPath.EndsWith("-wal", StringComparison.OrdinalIgnoreCase) || e.FullPath.EndsWith("-journal", StringComparison.OrdinalIgnoreCase))
                    dirty[e.FullPath] = 0;
                else if (e.ChangeType is WatcherChangeTypes.Created or WatcherChangeTypes.Renamed && Directory.Exists(e.FullPath))
                    Interlocked.Exchange(ref overflow, 1);
            }
            watcher.Changed += Changed; watcher.Created += Changed; watcher.Deleted += Changed;
            watcher.Renamed += (sender, e) => { Changed(sender, e); dirty[e.OldFullPath] = 0; };
            watcher.Error += (_, _) => Interlocked.Exchange(ref overflow, 1);
            watcher.EnableRaisingEvents = true;
            watchers[root] = watcher;
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { warnings.Add($"Cannot watch provider files: {e.GetType().Name}"); return false; }
    }

    private IEnumerable<FileInfo> Files(string root, string pattern, bool recursive)
    {
        if (!Directory.Exists(root)) yield break;
        IEnumerator<FileInfo>? iterator = null;
        try
        {
            iterator = new DirectoryInfo(root).EnumerateFiles(pattern, new EnumerationOptions
            { RecurseSubdirectories = recursive, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint }).GetEnumerator();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { warnings.Add($"Cannot enumerate provider files: {e.GetType().Name}"); }
        if (iterator == null) yield break;
        using (iterator)
            while (true)
            {
                FileInfo file;
                try { if (!iterator.MoveNext()) break; file = iterator.Current; }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                { warnings.Add($"Cannot enumerate provider files: {e.GetType().Name}"); break; }
                yield return file;
            }
    }

    private void Read(Provider provider, FileInfo file)
    {
        if (!sources.TryGetValue(file.FullName, out var source)) sources[file.FullName] = source = new(provider);
        void Liveness()
        {
            var seen = new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero);
            source.Modified = file.LastWriteTimeUtc;
            foreach (var a in source.Cached.Values)
                if (seen > a.LastSeen) a.LastSeen = seen;
        }
        try
        {
            Liveness();
            var lines = source.Tail.Read(file.FullName); // Always check handle identity and bounded content probes.
            file.Refresh();
            if (!file.Exists) return;
            if (source.Tail.WasReset)
            {
                foreach (var id in source.Cached.Keys)
                    if (!sources.Values.Any(s => s != source && s.Cached.ContainsKey(id))) agents.Remove(id);
                source.Cached.Clear(); source.AgentId = null;
            }
            source.Modified = file.LastWriteTimeUtc;
            foreach (var line in lines)
            {
                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var root = doc.RootElement;
                    if (root.ValueKind != JsonValueKind.Object) continue;
                    var type = LogParser.String(root, "type");
                    if (type == null) continue;
                    if (provider == Provider.Claude)
                    {
                        // Unidentified inline sidechains must never mutate the supervisor.
                        if (root.TryGetProperty("isSidechain", out var side) && side.ValueKind == JsonValueKind.True &&
                            LogParser.String(root, "agentId") == null && !file.Name.StartsWith("agent-", StringComparison.Ordinal)) continue;
                        if (LogParser.String(root, "type") is "summary" or "file-history-snapshot" or "progress" or "queue-operation") continue;
                    }
                    var id = provider == Provider.Claude || provider == Provider.Codex && type == "session_meta" ? LogParser.Identity(provider, file.FullName, root)
                        : source.AgentId ??= LogParser.Identity(provider, file.FullName, root);
                    if (provider == Provider.Codex && type == "session_meta") source.AgentId = id;
                    if (!agents.TryGetValue(id, out var a))
                    {
                        if (!source.Cached.TryGetValue(id, out a)) a = new(id, provider, privacy);
                        agents[id] = a;
                    }
                    source.Cached[id] = a;
                    LogParser.Apply(a, root, file.FullName, new(file.LastWriteTimeUtc, TimeSpan.Zero));
                }
                catch (JsonException) { warnings.Add($"Malformed JSONL skipped ({provider})."); }
            }
            Liveness();
            if (source.Tail.DroppedLines > 0) warnings.Add($"Oversize JSONL records skipped ({provider}; >2 MiB).");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { warnings.Add($"Cannot read {provider} log: {e.GetType().Name}"); }
    }

    public void Dispose()
    {
        foreach (var watcher in watchers.Values) watcher.Dispose();
        antigravity.Dispose();
    }
}
