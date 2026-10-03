using System.Text.Json;

namespace Vibedotcom.Core;

public sealed class AgentTracker
{
    private sealed class Source(Provider provider)
    {
        public readonly JsonlTail Tail = new();
        public readonly Provider Provider = provider;
        public string? AgentId;
        public DateTime Modified;
        public long Length;
    }
    private readonly TrackerOptions options;
    private readonly Dictionary<string, Source> sources = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TrackedAgent> agents = new(StringComparer.Ordinal);
    private readonly HashSet<string> warnings = [];
    private DateTimeOffset nextScan;
    public long BytesRead => sources.Values.Sum(s => s.Tail.BytesRead);
    public IReadOnlyList<string> Warnings => warnings.Order().ToArray();

    public AgentTracker(TrackerOptions? options = null)
    {
        this.options = options ?? new();
        this.options.Validate();
    }

    public IReadOnlyList<AgentInfo> Poll(DateTimeOffset now)
    {
        if (now >= nextScan)
        {
            nextScan = now + options.ScanInterval;
            warnings.Clear();
            Scan(Provider.Codex, Path.Combine(options.Home, ".codex", "sessions"), "rollout-*.jsonl", now);
            Scan(Provider.Claude, Path.Combine(options.Home, ".claude", "projects"), "*.jsonl", now);
            ScanAntigravity(now);
        }
        var snapshots = agents.Values.Select(a => a.Snapshot(now, options)).ToArray();
        foreach (var old in snapshots.Where(a => a.State == AgentState.Gone && now - a.StateSince >= options.GoneRetention))
            agents.Remove(old.Id);
        foreach (var path in sources.Where(s => now - new DateTimeOffset(s.Value.Modified, TimeSpan.Zero) >= options.GoneAfter + options.GoneRetention)
                     .Select(s => s.Key).ToArray()) sources.Remove(path);
        return snapshots.Where(a => agents.ContainsKey(a.Id)).OrderBy(a => a.Id, StringComparer.Ordinal).ToArray();
    }

    private IEnumerable<FileInfo> Files(string root, string pattern, bool recursive)
    {
        if (!Directory.Exists(root)) return [];
        try
        {
            return new DirectoryInfo(root).EnumerateFiles(pattern, new EnumerationOptions
            { RecurseSubdirectories = recursive, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint }).ToArray();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { warnings.Add($"Cannot enumerate {root}: {e.GetType().Name}"); return []; }
    }

    private void Scan(Provider provider, string root, string pattern, DateTimeOffset now)
    {
        foreach (var file in Files(root, pattern, true))
        {
            if (provider == Provider.Claude && file.FullName.Contains(Path.DirectorySeparatorChar + "tool-results" + Path.DirectorySeparatorChar)) continue;
            if (now - new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero) < options.GoneAfter || sources.ContainsKey(file.FullName))
                Read(provider, file);
        }
    }

    private void Read(Provider provider, FileInfo file)
    {
        try
        {
            var exists = sources.TryGetValue(file.FullName, out var source);
            if (!exists) sources[file.FullName] = source = new(provider);
            if (exists && source!.Modified == file.LastWriteTimeUtc && source.Length == file.Length && source.Tail.Offset >= file.Length) return;
            var lines = source!.Tail.Read(file.FullName);
            if (source.Tail.WasReset && source.AgentId != null) { agents.Remove(source.AgentId); source.AgentId = null; }
            source.Modified = file.LastWriteTimeUtc;
            source.Length = file.Length;
            foreach (var line in lines)
            {
                try
                {
                    using var doc = JsonDocument.Parse(line);
                    if (doc.RootElement.ValueKind != JsonValueKind.Object) continue;
                    var id = source.AgentId ??= LogParser.Identity(provider, file.FullName, doc.RootElement);
                    if (!agents.TryGetValue(id, out var agent)) agents[id] = agent = new(id, provider);
                    LogParser.Apply(agent, doc.RootElement, file.FullName, new(file.LastWriteTimeUtc, TimeSpan.Zero));
                }
                catch (JsonException) { warnings.Add($"Malformed JSONL skipped ({provider})."); }
            }
            if (source.AgentId != null && agents.TryGetValue(source.AgentId, out var current))
                current.LastSeen = new(file.LastWriteTimeUtc, TimeSpan.Zero);
            if (source.Tail.DroppedLines > 0) warnings.Add($"Oversize JSONL records skipped ({provider}; >2 MiB).");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { warnings.Add($"Cannot read {provider} log: {e.GetType().Name}"); }
    }

    private void ScanAntigravity(DateTimeOffset now)
    {
        var root = Path.Combine(options.Home, ".gemini", "antigravity");
        var databases = Files(Path.Combine(root, "conversations"), "*.db", false);
        foreach (var file in databases)
        {
            var wal = new FileInfo(file.FullName + "-wal");
            var modified = file.LastWriteTimeUtc;
            if (wal.Exists && wal.Length > 0 && wal.LastWriteTimeUtc > modified) modified = wal.LastWriteTimeUtc;
            var id = LogParser.Key(Provider.Antigravity, Path.GetFileNameWithoutExtension(file.Name));
            if (now - new DateTimeOffset(modified, TimeSpan.Zero) >= options.GoneAfter && !agents.ContainsKey(id)) continue;
            var transcript = Path.Combine(root, "brain", Path.GetFileNameWithoutExtension(file.Name), ".system_generated", "logs", "transcript_full.jsonl");
            if (!File.Exists(transcript)) transcript = Path.Combine(Path.GetDirectoryName(transcript)!, "transcript.jsonl");
            if (File.Exists(transcript)) Read(Provider.Antigravity, new(transcript));
            if (!agents.TryGetValue(id, out var a)) agents[id] = a = new(id, Provider.Antigravity);
            var time = new DateTimeOffset(modified, TimeSpan.Zero);
            if (time > a.LastSeen) a.LastSeen = time;
            // DB/WAL writes newer than the transcript are activity only, not parsed tool events.
            if (time > a.LastEvent.AddSeconds(3) && time > a.LastEvent) a.State(AgentState.Working, time);
        }
        // Some versions retain brain transcripts even without a conversation DB.
        foreach (var file in Files(Path.Combine(root, "brain"), "transcript*.jsonl", true))
        {
            if (file.Name is not ("transcript_full.jsonl" or "transcript.jsonl")) continue;
            if (file.Directory?.Name != "logs") continue;
            if (file.Name == "transcript.jsonl" && File.Exists(Path.Combine(file.DirectoryName!, "transcript_full.jsonl"))) continue;
            if (now - new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero) < options.GoneAfter || sources.ContainsKey(file.FullName)) Read(Provider.Antigravity, file);
        }
    }
}
