using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Officelings.Core;

// Synthetic performance test only. No application, renderer, real home or state output.
internal static class Program
{
    private const int MiB = 1024 * 1024, RecordBytes = 4096;
    private static readonly Process Self = Process.GetCurrentProcess();
    private static readonly PropertyInfo Discovery = typeof(AgentTracker).GetProperty("DiscoveryFilesVisited", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Expected committed tracker discovery counter.");
    private static readonly List<Sample> Samples = [];
    private static string results = "", data = "";
    private static StreamWriter? raw, checks;
    private static readonly Stopwatch Lifetime = new();
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private static int Main(string[] args)
    {
        if (args.Length != 5 || args[0] != "--run" || args[1] != "--results" || args[3] != "--data")
        {
            Console.Error.WriteLine("Measurement requires explicit --run --results <new owned directory> --data <new TEMP directory>.");
            return 2;
        }
        try
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows process I/O counters required.");
            results = ChildOf(args[2], "C:/Codex-Workspace/Officelings/tools/perf/tracker");
            data = ChildOf(args[4], Path.GetTempPath());
            if (Directory.Exists(results) || File.Exists(results) || Directory.Exists(data) || File.Exists(data))
                throw new IOException("Results and data directories must both be new; refusing overwrite.");
            Directory.CreateDirectory(results); Directory.CreateDirectory(data);
            using var guard = new Timer(_ => Environment.Exit(124), null, TimeSpan.FromSeconds(170), Timeout.InfiniteTimeSpan);
            Lifetime.Start();
            raw = NewWriter("polls.jsonl"); checks = NewWriter("checks.jsonl");
            var core = typeof(AgentTracker).Assembly.Location;
            Save("run.json", new
            {
                startedUtc = DateTimeOffset.UtcNow, coreDll = core,
                assemblyVersion = typeof(AgentTracker).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
                coreSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(core))),
                coreMvid = typeof(AgentTracker).Module.ModuleVersionId, runtime = RuntimeInformation.FrameworkDescription,
                os = RuntimeInformation.OSDescription, logicalProcessors = Environment.ProcessorCount,
                serverGc = System.Runtime.GCSettings.IsServerGC, dataRoot = data,
                fixture = "Invented ASCII JSONL, valid fixed 4096-byte records with synthetic_padding. Fresh tracker, warm OS cache after generation; no cache eviction.",
                cadence = "Default TrackerOptions (3s scan); large/archive cases advance logical clock. Steady: 60 real 1s ticks; 25 Codex + 25 Claude; 4096 bytes/file/tick.",
                metrics = "CPU is whole harness process overlapping Poll (including watcher threads); CPU% is one-core equivalent. RSS is Windows WorkingSet before/after, not isolated peak. Private bytes also sampled. Tracker bytes exclude bounded probes. Process I/O includes buffering/probes/metadata, is not physical disk traffic. Allocations include concurrent watcher allocations.",
                limitations = "One process, one pass, ordered cases; JIT warm-up self-check precedes performance cases. RSS carries prior allocations and fixture setup. Windows CPU timer granularity can quantize short Polls to zero; inspect aggregate CPU. No full renderer, Antigravity database/runtime, real corpus or cold disk measurement. Steady overall CPU includes synthetic writes and raw log bookkeeping."
            });
            SelfCheck();
            Large(20); Large(200); Archive(); Steady();
            foreach (var group in Samples.GroupBy(s => s.Scenario))
            {
                var rows = group.ToArray();
                Console.WriteLine($"{group.Key}: polls={rows.Length}, wall_ms={rows.Sum(s => s.WallMs):F3}, cpu_ms={rows.Sum(s => s.CpuMs):F3}, payload_bytes={rows.Sum(s => s.PayloadBytes)}, process_read_bytes={rows.Sum(s => s.ProcessReadBytes)}, max_sampled_rss={rows.Max(s => Math.Max(s.RssBefore, s.RssAfter))}");
            }
            Save("completion.json", new { success = true, endedUtc = DateTimeOffset.UtcNow, elapsedSeconds = Lifetime.Elapsed.TotalSeconds, polls = Samples.Count });
            return 0;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(e);
            if (Directory.Exists(results)) Save("failure.json", new { error = e.ToString(), elapsedSeconds = Lifetime.Elapsed.TotalSeconds });
            return 1;
        }
        finally { raw?.Dispose(); checks?.Dispose(); Self.Dispose(); }
    }

    private static string ChildOf(string path, string parent)
    {
        var full = Path.GetFullPath(path);
        var prefix = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException($"Path must be under {parent}");
        return full;
    }
    private static StreamWriter NewWriter(string name) => new(new FileStream(Path.Combine(results, name), FileMode.CreateNew, FileAccess.Write), new UTF8Encoding(false)) { AutoFlush = true };
    private static void Save(string name, object value) { using var output = NewWriter(name); output.WriteLine(JsonSerializer.Serialize(value, JsonOptions)); }
    private static void Check(string name, bool passed, object? evidence = null)
    {
        checks!.WriteLine(JsonSerializer.Serialize(new { name, passed, evidence }));
        if (!passed) throw new InvalidOperationException("Acceptance failed: " + name);
    }
    private static string Home(string name) { var home = ChildOf(Path.Combine(data, name), data); Directory.CreateDirectory(home); return home; }
    private static string Session(string home, string id, bool claude = false, string folder = "active")
    {
        var path = claude ? Path.Combine(home, ".claude", "projects", folder, id + ".jsonl")
            : Path.Combine(home, ".codex", "sessions", folder, "rollout-" + id + ".jsonl");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!); return path;
    }
    private static AgentTracker Tracker(string home) => new(new TrackerOptions { Home = ChildOf(home, data) });
    private static long Visited(AgentTracker tracker) => (long)Discovery.GetValue(tracker)!;
    private static byte[] JsonLine(object value) => Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value) + "\n");
    private static byte[] Fixed(object value)
    {
        var prefix = JsonSerializer.Serialize(value)[..^1] + ",\"synthetic_padding\":\"";
        var suffix = "\"}\n";
        var count = RecordBytes - Encoding.UTF8.GetByteCount(prefix + suffix);
        if (count < 0) throw new ArgumentException("Fixture record too long.");
        return Encoding.UTF8.GetBytes(prefix + new string('x', count) + suffix);
    }
    private static byte[] Meta(string id, DateTimeOffset now) => Fixed(new { timestamp = now.ToString("O"), type = "session_meta", payload = new { id, source = "cli", model = "synthetic" } });
    private static byte[] Event(DateTimeOffset now, string type = "function_call_output") => Fixed(new
    { timestamp = now.ToString("O"), type = type == "task_complete" ? "event_msg" : "response_item", payload = new { type, output = "invented tool output" } });
    private static byte[] Claude(string id, DateTimeOffset now, bool complete = false) => Fixed(new
    { timestamp = now.ToString("O"), type = complete ? "assistant" : "user", sessionId = id, message = new { content = "invented task", stop_reason = complete ? "end_turn" : null } });
    private static void Append(string path, byte[] bytes, DateTimeOffset? stamp = null)
    {
        using (var output = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete)) output.Write(bytes);
        if (stamp is { } time) File.SetLastWriteTimeUtc(path, time.UtcDateTime);
    }
    private static (Sample Sample, IReadOnlyList<AgentInfo> Agents) Poll(string scenario, int index, AgentTracker tracker, DateTimeOffset now, long appended = 0)
    {
        var bytes = tracker.BytesRead; var visited = Visited(tracker);
        Self.Refresh(); var rss = Self.WorkingSet64; var privateBytes = Self.PrivateMemorySize64;
        var allocations = GC.GetTotalAllocatedBytes(false); int g0 = GC.CollectionCount(0), g1 = GC.CollectionCount(1), g2 = GC.CollectionCount(2);
        var io = Io(); var cpu = Cpu(); var start = Stopwatch.GetTimestamp();
        var agents = tracker.Poll(now);
        var wall = Stopwatch.GetElapsedTime(start).TotalMilliseconds; var cpuAfter = Cpu(); var ioAfter = Io();
        var allocated = GC.GetTotalAllocatedBytes(false) - allocations;
        int d0 = GC.CollectionCount(0) - g0, d1 = GC.CollectionCount(1) - g1, d2 = GC.CollectionCount(2) - g2;
        Self.Refresh();
        var wallSeconds = wall / 1000; var cpuSeconds = (cpuAfter - cpu) / 1000;
        var cpuOneCorePercent = 100 * cpuSeconds / wallSeconds;
        var sample = new Sample(scenario, index, now, wall, cpuAfter - cpu, wallSeconds, cpuSeconds, cpuOneCorePercent,
            cpuOneCorePercent / Environment.ProcessorCount, Environment.ProcessorCount,
            rss, Self.WorkingSet64, privateBytes, Self.PrivateMemorySize64, tracker.BytesRead - bytes,
            (long)(ioAfter.ReadTransferCount - io.ReadTransferCount), (long)(ioAfter.ReadOperationCount - io.ReadOperationCount),
            Visited(tracker) - visited, appended, allocated, d0, d1, d2, agents.Count, tracker.Warnings.ToArray());
        Samples.Add(sample); raw!.WriteLine(JsonSerializer.Serialize(sample, JsonOptions));
        return (sample, agents);
    }
    private static void SelfCheck()
    {
        var home = Home("selfcheck"); var path = Session(home, "check"); var now = DateTimeOffset.UtcNow;
        var complete = JsonLine(new { timestamp = now.ToString("O"), type = "event_msg", payload = new { type = "task_complete" } });
        var split = complete.Length / 2;
        using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
        { file.Write(Meta("check", now)); file.Write(Encoding.UTF8.GetBytes("malformed\n")); file.Write(complete.AsSpan(0, split)); }
        using var tracker = Tracker(home);
        var initial = Poll("selfcheck.initial", 0, tracker, now);
        Check("malformed skipped; partial deferred; identified one agent", initial.Agents.Count == 1 && initial.Agents[0].Id == "codex:check" && initial.Sample.Warnings.Any(w => w.Contains("Malformed")));
        Append(path, complete[split..], now.AddSeconds(4));
        var finished = Poll("selfcheck.partial", 1, tracker, now.AddSeconds(4), complete.Length - split);
        Check("split record completes with exact unread bytes", finished.Sample.PayloadBytes == complete.Length - split && finished.Agents.Single().State == AgentState.Waiting && finished.Sample.Warnings.Length == 0);
        var retry = JsonLine(new { timestamp = now.AddSeconds(8).ToString("O"), type = "event_msg", payload = new { type = "user_message", message = "retry synthetic" } });
        using (var writer = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Write))
        {
            writer.Write(retry); writer.Flush(); File.SetLastWriteTimeUtc(path, now.AddSeconds(8).UtcDateTime);
            var denied = Poll("selfcheck.denied", 2, tracker, now.AddSeconds(8), retry.Length);
            Check("read-denied warns without advancing bytes or losing agent", denied.Sample.PayloadBytes == 0 && denied.Agents.Count == 1 && denied.Sample.Warnings.Any(w => w.Contains("IOException")));
        }
        var resumed = Poll("selfcheck.retry", 3, tracker, now.AddSeconds(12));
        Check("unlock retries exactly once", resumed.Sample.PayloadBytes == retry.Length && resumed.Agents.Single().State == AgentState.Thinking && resumed.Sample.Warnings.Length == 0);
    }
    private static void Large(int mib)
    {
        var home = Home("large-" + mib); var id = "large-" + mib; var path = Session(home, id); var now = DateTimeOffset.UtcNow;
        var filler = Event(now); var final = Event(now, "task_complete");
        using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 1024))
        {
            file.Write(Meta(id, now));
            var block = new byte[MiB]; for (var offset = 0; offset < block.Length; offset += filler.Length) filler.CopyTo(block, offset);
            var remaining = (long)mib * MiB - 2 * RecordBytes;
            while (remaining >= block.Length) { file.Write(block); remaining -= block.Length; }
            file.Write(block.AsSpan(0, (int)remaining)); file.Write(final);
        }
        File.SetLastWriteTimeUtc(path, now.UtcDateTime);
        using var tracker = Tracker(home);
        var cold = Poll($"large{mib}.cold", 0, tracker, now);
        Check($"{mib}MiB exact fixture; bounded prefix+suffix; correct identity/state", new FileInfo(path).Length == (long)mib * MiB && cold.Sample.PayloadBytes == 2L * JsonlTail.BootstrapBytes && cold.Agents.Count == 1 && cold.Agents[0].Id == "codex:" + id && cold.Agents[0].State == AgentState.Waiting && cold.Sample.Warnings.Length == 0);
        var unchanged = Poll($"large{mib}.unchanged", 1, tracker, now.AddSeconds(4));
        Check($"{mib}MiB unchanged payload zero", unchanged.Sample.PayloadBytes == 0 && unchanged.Agents.Count == 1 && unchanged.Sample.Warnings.Length == 0);
        var append = new byte[16 * RecordBytes];
        for (var offset = 0; offset < append.Length; offset += RecordBytes) Event(now.AddSeconds(8), offset == append.Length - RecordBytes ? "task_complete" : "function_call_output").CopyTo(append, offset);
        Append(path, append, now.AddSeconds(8));
        var grown = Poll($"large{mib}.append", 2, tracker, now.AddSeconds(8), append.Length);
        Check($"{mib}MiB append only reads exact 64KiB", grown.Sample.PayloadBytes == append.Length && grown.Agents.Single().State == AgentState.Waiting && grown.Sample.Warnings.Length == 0);
    }
    private static void Archive()
    {
        var home = Home("archive"); var now = DateTimeOffset.UtcNow; var old = now.AddDays(-2); var bytes = Meta("archived", old);
        for (var i = 0; i < 2000; i++)
        { var path = Session(home, "old-" + i, folder: "old-" + i / 100); File.WriteAllBytes(path, bytes); File.SetLastWriteTimeUtc(path, old.UtcDateTime); }
        using var tracker = Tracker(home);
        var cold = Poll("archive2000.startup", 0, tracker, now);
        Check("startup visits 2000 old files without reading payload or spawning agents", cold.Sample.DiscoveryFiles == 2000 && cold.Sample.PayloadBytes == 0 && cold.Agents.Count == 0 && cold.Sample.Warnings.Length == 0);
        var next = Poll("archive2000.unchanged", 1, tracker, now.AddSeconds(4));
        Check("archive is not re-enumerated on next scan", next.Sample.DiscoveryFiles == 0 && next.Sample.PayloadBytes == 0 && next.Agents.Count == 0 && next.Sample.Warnings.Length == 0);
    }
    private static void Steady()
    {
        var home = Home("steady50"); var now = DateTimeOffset.UtcNow; var paths = new string[50];
        for (var i = 0; i < paths.Length; i++)
        {
            var id = "steady-" + i; paths[i] = Session(home, id, i >= 25);
            using var file = new FileStream(paths[i], FileMode.CreateNew, FileAccess.Write);
            file.Write(i < 25 ? Meta(id, now) : Claude(id, now)); file.Write(i < 25 ? Event(now) : Claude(id, now, true));
        }
        using var tracker = Tracker(home);
        var initial = Poll("steady50.startup", 0, tracker, now);
        Check("50 initial agents; seed bytes exact", initial.Agents.Count == 50 && initial.Sample.PayloadBytes == 50L * 2 * RecordBytes && initial.Sample.Warnings.Length == 0);
        var startCpu = Cpu(); var startIo = Io(); int g0 = GC.CollectionCount(0), g1 = GC.CollectionCount(1), g2 = GC.CollectionCount(2);
        var clock = Stopwatch.StartNew();
        for (var tick = 1; tick <= 60; tick++)
        {
            var wait = TimeSpan.FromSeconds(tick) - clock.Elapsed; if (wait > TimeSpan.Zero) Thread.Sleep(wait);
            if (Lifetime.Elapsed.TotalSeconds > 150) throw new TimeoutException("Bounded measurement budget exhausted.");
            now = DateTimeOffset.UtcNow;
            for (var i = 0; i < paths.Length; i++) Append(paths[i], i < 25 ? Event(now) : Claude("steady-" + i, now));
            var current = Poll("steady50.growing", tick, tracker, DateTimeOffset.UtcNow, 50L * RecordBytes);
            Check("steady identities and warnings tick " + tick, current.Agents.Count == 50 && current.Agents.Count(a => a.Provider == Provider.Codex) == 25 && current.Agents.Count(a => a.Provider == Provider.Claude) == 25 && current.Sample.Warnings.Length == 0);
        }
        var drain = Poll("steady50.drain", 61, tracker, DateTimeOffset.UtcNow.AddSeconds(4));
        Check("steady drains all appended bytes; exactly 50 agents", tracker.BytesRead == 50L * RecordBytes * 62 && drain.Agents.Count == 50 && drain.Sample.Warnings.Length == 0);
        var wall = clock.Elapsed.TotalSeconds; var cpu = Cpu() - startCpu; var endIo = Io();
        Save("steady-total.json", new
        {
            wallSeconds = wall, cpuSeconds = cpu / 1000, processCpuMs = cpu, cpuOneCorePercent = 100 * (cpu / 1000) / wall,
            cpuMachinePercent = 100 * (cpu / 1000) / wall / Environment.ProcessorCount, logicalProcessors = Environment.ProcessorCount,
            cpuFormula = "cpuOneCorePercent = 100 * cpuSeconds / wallSeconds; cpuMachinePercent = cpuOneCorePercent / logicalProcessors",
            appendedBytes = 60L * 50 * RecordBytes, payloadBytesIncludingSeed = tracker.BytesRead,
            processReadBytes = endIo.ReadTransferCount - startIo.ReadTransferCount,
            processWriteBytes = endIo.WriteTransferCount - startIo.WriteTransferCount,
            gen0 = GC.CollectionCount(0) - g0, gen1 = GC.CollectionCount(1) - g1, gen2 = GC.CollectionCount(2) - g2,
            note = "Whole-process total includes synthetic appends, watchers, raw measurement writes, metric sampling, sleeps and final logical-clock drain. Poll-only intervals are in polls.jsonl."
        });
    }
    private sealed record Sample(string Scenario, int Index, DateTimeOffset PollTime, double WallMs, double CpuMs, double WallSeconds, double CpuSeconds,
        double CpuOneCorePercent, double CpuMachinePercent, int LogicalProcessors,
        long RssBefore, long RssAfter, long PrivateBefore, long PrivateAfter, long PayloadBytes, long ProcessReadBytes, long ProcessReadOperations,
        long DiscoveryFiles, long AppendedBytes, long AllocatedBytes, int Gen0, int Gen1, int Gen2, int AgentCount, string[] Warnings);
    private static double Cpu()
    {
        if (!GetProcessTimes(new IntPtr(-1), out _, out _, out var kernel, out var user)) throw new Win32Exception(Marshal.GetLastWin32Error());
        return (kernel + user) / 10000.0;
    }
    private static IoCounters Io()
    {
        if (!GetProcessIoCounters(new IntPtr(-1), out var counters)) throw new Win32Exception(Marshal.GetLastWin32Error());
        return counters;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters { public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount, ReadTransferCount, WriteTransferCount, OtherTransferCount; }
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessIoCounters(IntPtr process, out IoCounters counters);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessTimes(IntPtr process, out long creation, out long exit, out long kernel, out long user);
}
