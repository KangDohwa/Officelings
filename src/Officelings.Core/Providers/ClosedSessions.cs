using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace Officelings.Core.Providers;

// Called only by the source scan, on the app's tracker worker, never by the animator.
internal sealed class ClosedSessions(string home, Func<int, long?, bool?>? processAlive = null)
{
    private sealed record Registration(string Session, int Pid, long? Start);
    private readonly Dictionary<string, Registration> claude = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTimeOffset> missing = new(StringComparer.Ordinal);
    private readonly HashSet<string> archived = new(StringComparer.Ordinal);
    private string? codexStamp;

    internal void Poll(DateTimeOffset now, Dictionary<string, TrackedAgent> agents, HashSet<string> warnings,
        IEnumerable<(string Path, string[] Ids)> codexPaths)
    {
        ReadClaude(warnings);
        // One result per distinct registered process per scan (3s by default). No process enumeration.
        var processes = new Dictionary<(int, long?), bool?>();
        var sessions = claude.Values.GroupBy(r => r.Session).ToDictionary(g => g.Key, g =>
        {
            var results = g.Select(r =>
            {
                var key = (r.Pid, r.Start);
                if (!processes.TryGetValue(key, out var alive))
                    processes[key] = alive = (processAlive ?? Alive)(r.Pid, r.Start);
                return alive;
            }).ToArray();
            return results.Any(r => r == true) ? true : results.Any(r => r == null) ? (bool?)null : false;
        });
        foreach (var (session, alive) in sessions)
        {
            if (alive != false) missing.Remove(session);
            else if (!missing.ContainsKey(session) || now < missing[session]) missing[session] = now;
        }
        ReadCodex(warnings);
        var moved = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (path, ids) in codexPaths)
            if (!FileBoundary.Exists(path) && FileBoundary.Exists(Path.Combine(home, ".codex", "archived_sessions", Path.GetFileName(path))))
                moved.UnionWith(ids);
        foreach (var a in agents.Values)
        {
            if (a.Provider == Provider.Claude)
            {
                var session = a.IsSubagent ? a.ParentId ?? a.Id : a.Id;
                a.ClosedAt = missing.TryGetValue(session, out var since) ? since.AddSeconds(10) : null;
            }
            else if (a.Provider == Provider.Codex)
                a.ClosedAt = archived.Contains(a.Id) || moved.Contains(a.Id) ? a.ClosedAt ?? now : null;
        }
    }

    private void ReadClaude(HashSet<string> warnings)
    {
        var root = Path.Combine(home, ".claude", "sessions");
        try
        {
            FileBoundary.Check(root);
            if (!FileBoundary.Exists(root, directory: true)) return;
            using var ancestors = FileBoundary.LockAncestors(Path.Combine(root, ".boundary"));
            foreach (var path in Directory.EnumerateFiles(root, "*.json", new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint }).Take(InputLimits.Sources))
            {
                try
                {
                    using var file = new MemoryStream(FileBoundary.ReadBytes(path, 65536));
                    using var doc = JsonDocument.Parse(file);
                    var r = doc.RootElement;
                    if (!string.Equals(LogParser.String(r, "pidDomain"), "win32:" + Environment.MachineName, StringComparison.OrdinalIgnoreCase) ||
                        LogParser.String(r, "sessionId") is not { Length: > 0 } id ||
                        !r.TryGetProperty("pid", out var pid) || !pid.TryGetInt32(out var number) || number <= 0) continue;
                    long? start = long.TryParse(LogParser.String(r, "procStart"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var ticks) && ticks > 0 ? ticks : null;
                    // Keep an observed mapping if the registry file is cleaned up on exit.
                    if (claude.Count < InputLimits.Sources || claude.ContainsKey(path)) claude[path] = new(LogParser.Key(Provider.Claude, id), number, start);
                    // Peer tokens are not read: probing or messaging Claude is unnecessary.
                }
                catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or InvalidOperationException)
                { warnings.Add("Claude session registration unavailable; using activity timeout."); }
            }
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
        { warnings.Add("Claude session registry unavailable; using activity timeout."); }
    }

    private void ReadCodex(HashSet<string> warnings)
    {
        var path = Path.Combine(home, ".codex", "state_5.sqlite");
        try
        {
            if (!FileBoundary.Exists(path)) { archived.Clear(); codexStamp = null; return; }
            var stamp = SqliteSnapshot.Stamp(path);
            if (stamp == codexStamp) return;
            using var db = new SqliteSnapshot(path);
            var columns = db.Query("PRAGMA table_info(threads)").Select(r => r[1]).ToHashSet();
            if (!columns.Contains("id") || !columns.Contains("archived")) throw new IOException("Unknown Codex threads schema.");
            var ids = db.Query("SELECT id FROM threads WHERE archived = 1").Where(r => !string.IsNullOrEmpty(r[0]))
                .Select(r => LogParser.Key(Provider.Codex, r[0]!)).ToArray();
            archived.Clear(); archived.UnionWith(ids); codexStamp = stamp;
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or DllNotFoundException)
        { warnings.Add("Codex archive state unavailable; using cached state/activity timeout."); }
    }

    private static bool? Alive(int pid, long? expectedStart)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            if (process.HasExited) return false;
            // procStart is a Windows FILETIME in the inspected Claude registry; reject reused PIDs.
            return expectedStart == null || process.StartTime.ToFileTimeUtc() == expectedStart.Value;
        }
        catch (ArgumentException) { return false; } // PID does not exist.
        catch (Exception e) when (e is Win32Exception or InvalidOperationException or NotSupportedException)
        { return null; } // Access denied or a race is unknown, never proof of closure.
    }
}
