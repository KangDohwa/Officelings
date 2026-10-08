using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Officelings.Core;
using Xunit;

namespace Officelings.Tests;

[Collection("Review input budgets")]
public sealed class TrackerPerfTests : IDisposable
{
    private readonly string home = Path.Combine(Path.GetTempPath(), "officelings-tracker-perf-" + Guid.NewGuid().ToString("N"));
    private static readonly DateTimeOffset Epoch = DateTimeOffset.Parse("2026-10-04T00:00:00Z");
    private static OfficeLayout Room => new(8, 8, 16, new(1, 1), [], Tiles: Enumerable.Repeat(1, 64).ToArray(),
        Zones: [new("workspace", 1, 3, 6, 4), new("pantry", 1, 3, 1, 1), new("lounge", 1, 4, 1, 1), new("door", 1, 1, 1, 1)]);
    public TrackerPerfTests() => Directory.CreateDirectory(home);
    public void Dispose() => Directory.Delete(home, true);

    [Fact]
    public void TrackerDeadlinesDoNotSpinWhenNativeDelayTruncatesMilliseconds()
    {
        var cadence = Assembly.LoadFrom(TrackerBinary()).GetType("TrackerCadence", throwOnError: true)!;
        var delay = cadence.GetMethod("Delay", BindingFlags.NonPublic | BindingFlags.Static)!
            .CreateDelegate<Func<TimeSpan, TimeSpan>>();
        var now = TimeSpan.FromTicks(7); var next = TimeSpan.FromSeconds(1);
        var wakeups = 0;
        while (now < TimeSpan.FromSeconds(30))
        {
            wakeups++;
            // Model Task.Delay's whole-millisecond wait and a tiny amount of work.
            now += TimeSpan.FromMilliseconds(Math.Floor(delay(next - now).TotalMilliseconds)) + TimeSpan.FromTicks(7);
            if (now >= next) next += TimeSpan.FromSeconds(1);
        }
        Assert.Equal(30, wakeups);
        Assert.Equal(TimeSpan.Zero, delay(TimeSpan.FromTicks(-1)));
    }

    private static string TrackerBinary()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder != null && !Directory.Exists(Path.Combine(folder.FullName, "src", "Officelings.Tracker"))) folder = folder.Parent;
        Assert.NotNull(folder);
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        var binary = Path.Combine(folder.FullName, "src", "Officelings.Tracker", "bin", configuration, "net10.0", "Officelings.Tracker.dll");
        Assert.True(File.Exists(binary), "Build the tracker before these regression tests.");
        return binary;
    }

    // The test project intentionally does not reference the tracker executable.
    private static object Cache(AssetManifest? manifest) => Activator.CreateInstance(
        Assembly.LoadFrom(TrackerBinary()).GetType("TrackerSnapshotCache", throwOnError: true)!,
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, [manifest], null)!;
    private static bool Update(object cache, OfficeLayout office, IReadOnlyList<AgentView> views,
        IReadOnlyList<string> warnings, IReadOnlyList<FurnitureSprite> furniture) =>
        (bool)cache.GetType().GetMethod("Update")!.Invoke(cache, [office, views, warnings, furniture])!;
    private static ReadOnlyMemory<byte> SerializeMemory(object cache, DateTimeOffset now, long sequence) =>
        (ReadOnlyMemory<byte>)cache.GetType().GetMethod("Serialize")!.Invoke(cache, [now, sequence])!;
    private static byte[] Serialize(object cache, DateTimeOffset now, long sequence) => SerializeMemory(cache, now, sequence).ToArray();
    private static byte[] LayoutBytes(object cache) => (byte[])cache.GetType().GetProperty("LayoutBytes")!.GetValue(cache)!;
    private static AgentView View => new(new("codex:invented", Provider.Codex, null, false, "Invented", null, null,
        "C:\\sim\\project-0", "Invented task", null, null, AgentState.Waiting, Epoch, Epoch, false),
        new(1, 1, new(2, 2), "waiting", "up", 0, 0, true, Sprite: new("fallback", 0, 0, 0, 0, 16, 24, 8, 24, 1)));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CachedSnapshotPreservesTheCompleteSchema(bool withManifest)
    {
        var manifest = withManifest ? new AssetPack().Manifest : null;
        var office = Room; var views = new[] { View }; var warnings = new[] { "Invented warning" };
        var furniture = new[] { new FurnitureSprite("desk", View.Pose.Sprite!, "screensaver") };
        var cache = Cache(manifest);
        Assert.True(Update(cache, office, views, warnings, furniture));
        var expected = new StateSnapshot(StateJson.SchemaVersion, Epoch, long.MaxValue - 1, SpriteAtlas.Info,
            office, views, warnings, manifest, furniture);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(JsonSerializer.SerializeToUtf8Bytes(expected, StateJson.Options)),
            JsonNode.Parse(Serialize(cache, Epoch, expected.Sequence))));
    }

    [Fact]
    public void EqualContentReusesLayoutBytesWhileHeartbeatMetadataAdvances()
    {
        var office = Room; var cache = Cache(null);
        Assert.True(Update(cache, office, [View], [], []));
        var layout = LayoutBytes(cache);
        Assert.False(Update(cache, office, [View with { }], [], []));
        Assert.Same(layout, LayoutBytes(cache));
        var first = JsonSerializer.Deserialize<StateSnapshot>(Serialize(cache, Epoch, 1), StateJson.Options)!;
        var second = JsonSerializer.Deserialize<StateSnapshot>(Serialize(cache, Epoch.AddSeconds(3), 2), StateJson.Options)!;
        Assert.Equal(1, first.Sequence); Assert.Equal(2, second.Sequence);
        Assert.Equal(Epoch.AddSeconds(3), second.GeneratedAt);
        Assert.Equal(first.Agents[0], second.Agents[0]);
        Assert.True(Update(cache, office with { Door = new(2, 1) }, [View], [], []));
        Assert.NotSame(layout, LayoutBytes(cache));
    }

    [Fact]
    public void TimestampAndSequenceLengthChangesDoNotAllocateAnotherLargeSnapshot()
    {
        var cache = Cache(null);
        var warning = new string('x', 64 * 1024);
        Assert.True(Update(cache, Room, [View], [warning], []));
        SerializeMemory(cache, Epoch, 9);
        SerializeMemory(cache, Epoch.AddTicks(1), 10);
        SerializeMemory(cache, Epoch.AddSeconds(1), 100000);
        var before = GC.GetAllocatedBytesForCurrentThread();
        ReadOnlyMemory<byte> bytes = default;
        for (var i = 0; i < 64; i++) bytes = SerializeMemory(cache, Epoch.AddTicks(i + 1), 10 + i * 10000);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(allocated < warning.Length, $"Cached serialization allocated {allocated} bytes for 64 publications.");
        var state = JsonSerializer.Deserialize<StateSnapshot>(bytes.Span, StateJson.Options)!;
        Assert.Equal(630010, state.Sequence);
        Assert.Equal(Epoch.AddTicks(64), state.GeneratedAt);
        Assert.Equal(warning, Assert.Single(state.Warnings));
    }

    [Fact]
    public void ChangedAgentWarningAndFurnitureBuffersAreDetected()
    {
        var office = Room; var cache = Cache(null);
        var views = new[] { View }; var warnings = new[] { "First" };
        var furniture = new[] { new FurnitureSprite("screen", View.Pose.Sprite!, "off") };
        Assert.True(Update(cache, office, views, warnings, furniture));
        views[0] = View with { Agent = View.Agent with { LastSeen = Epoch.AddSeconds(1) }, Pose = View.Pose with { X = 2 } };
        Assert.True(Update(cache, office, views, warnings, furniture));
        warnings[0] = "Second";
        Assert.True(Update(cache, office, views, warnings, furniture));
        furniture[0] = furniture[0] with { Sprite = furniture[0].Sprite with { Column = 1 } };
        Assert.True(Update(cache, office, views, warnings, furniture));
        Assert.False(Update(cache, office, views, warnings, furniture));
        var state = JsonSerializer.Deserialize<StateSnapshot>(Serialize(cache, Epoch, 1), StateJson.Options)!;
        Assert.Equal(views[0], state.Agents[0]); Assert.Equal("Second", Assert.Single(state.Warnings));
        Assert.Equal(furniture[0], Assert.Single(state.FurnitureSprites!));
    }

    private Process StartTracker(double seconds)
    {
        File.WriteAllBytes(Path.Combine(home, "people.png"), SpriteAtlas.CreatePng());
        var manifest = new AssetManifest(1, [new("people", "people.png", 16, 24, [new("idle", "any", 0, 1, 1)], new(8, 24))],
            [], [new("person", "people")], [new(1, "floor", "#ffffff")]);
        File.WriteAllBytes(Path.Combine(home, "assets.json"), JsonSerializer.SerializeToUtf8Bytes(manifest, StateJson.Options));
        LayoutLoader.Validate(Room, new AssetPack(home));
        var layout = Path.Combine(home, "layout.json");
        File.WriteAllBytes(layout, JsonSerializer.SerializeToUtf8Bytes(Room, StateJson.Options));
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var argument in new[] { TrackerBinary(), "--home", Path.Combine(home, "logs"), "--output", Path.Combine(home, "output"),
            "--asset-dir", home, "--layout", layout, "--seconds", seconds.ToString(System.Globalization.CultureInfo.InvariantCulture), "--interval-seconds", ".05" })
            start.ArgumentList.Add(argument);
        return Process.Start(start)!;
    }

    private static StateSnapshot Read(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return JsonSerializer.Deserialize<StateSnapshot>(stream, StateJson.Options)!;
    }

    private static async Task<StateSnapshot> WaitForState(Process process, string path, CancellationToken cancellation, long after = 0)
    {
        while (true)
        {
            Assert.False(process.HasExited, "Tracker exited before publishing the expected snapshot.");
            if (File.Exists(path))
            {
                var state = Read(path);
                if (state.Sequence > after) return state;
            }
            await Task.Delay(25, cancellation);
        }
    }

    [Fact]
    public async Task UnchangedRoomPublishesHeartbeatsWithoutRewritingLayout()
    {
        using var process = StartTracker(6.2);
        var errors = process.StandardError.ReadToEndAsync(); var log = process.StandardOutput.ReadToEndAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        var statePath = Path.Combine(home, "output", "state.json");
        try
        {
            var first = await WaitForState(process, statePath, deadline.Token);
            Assert.Equal(8, first.Office.WidthTiles); Assert.Empty(first.Warnings);
            var layoutPath = Path.Combine(home, "output", "runtime-layout.json");
            var layoutTime = File.GetLastWriteTimeUtc(layoutPath); var layoutBytes = File.ReadAllBytes(layoutPath);
            var heartbeat = await WaitForState(process, statePath, deadline.Token, first.Sequence);
            Assert.InRange((heartbeat.GeneratedAt - first.GeneratedAt).TotalSeconds, 2.9, 4.5);
            await process.WaitForExitAsync(deadline.Token);
            Assert.Equal(0, process.ExitCode); Assert.Equal("", await errors); await log;
            var final = Read(statePath);
            Assert.InRange(final.Sequence, 2, 4);
            Assert.InRange((DateTimeOffset.UtcNow - final.GeneratedAt).TotalSeconds, 0, 4);
            Assert.Empty(final.Agents); Assert.Equal(StateJson.SchemaVersion, final.SchemaVersion);
            Assert.Equal(layoutTime, File.GetLastWriteTimeUtc(layoutPath)); Assert.Equal(layoutBytes, File.ReadAllBytes(layoutPath));
            Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(statePath)!, "*.tmp"));
        }
        finally { if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); } }
    }

    [Fact]
    public async Task LockedPublicationRetriesWithAFreshTimestampAndIncreasingSequence()
    {
        using var process = StartTracker(7.5);
        var errors = process.StandardError.ReadToEndAsync(); var log = process.StandardOutput.ReadToEndAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        var statePath = Path.Combine(home, "output", "state.json");
        try
        {
            var first = await WaitForState(process, statePath, deadline.Token);
            using (var consumer = new FileStream(statePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            { await Task.Delay(5000, deadline.Token); Assert.False(process.HasExited); }
            var releasedAt = DateTimeOffset.UtcNow;
            var recovered = await WaitForState(process, statePath, deadline.Token, first.Sequence);
            Assert.True(recovered.GeneratedAt >= releasedAt.AddMilliseconds(-50));
            Assert.InRange((DateTimeOffset.UtcNow - recovered.GeneratedAt).TotalSeconds, 0, 1.5);
            await process.WaitForExitAsync(deadline.Token);
            Assert.Equal(0, process.ExitCode);
            Assert.Single((await errors).Split('\n'), line => line.Contains("retrying", StringComparison.Ordinal)); await log;
            Assert.True(Read(statePath).Sequence > first.Sequence);
        }
        finally { if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); } }
    }
}
