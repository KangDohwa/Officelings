using System.Diagnostics;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Windows;
using Officelings.App;
using Officelings.Core;
using Xunit;

namespace Officelings.Tests;

[CollectionDefinition("Review input budgets", DisableParallelization = true)]
public sealed class ReviewInputBudgetCollection { }

[Collection("Review input budgets")]
public sealed class ReviewFixTests : IDisposable
{
    private readonly string home = Path.Combine(Path.GetTempPath(), "officelings-review-" + Guid.NewGuid().ToString("N"));
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-04T00:00:00Z");
    public ReviewFixTests() => Directory.CreateDirectory(home);
    public void Dispose() => Directory.Delete(home, true);
    private static AgentInfo Agent(string id) => new(id, Provider.Codex, null, false, id, null, null, null, "", null, null, AgentState.Working, Now, Now, false);
    private static OfficeLayout Room(int width = 24, int height = 17, int desks = 0) => new(width, height, 16, new(1, 1),
        Enumerable.Range(0, desks).Select(i => new Furniture("desk", 3 + i * 5, 5, 3, 2, Id: "desk-" + i, DeskId: "desk-" + i)).ToArray(),
        Tiles: Enumerable.Repeat(1, width * height).ToArray(), Zones: [new("workspace", 1, 3, width - 2, height - 4),
            new("pantry", 1, 3, 1, 1), new("lounge", 1, 4, 1, 1), new("door", 1, 1, 1, 1)]);
    private string JsonFile<T>(string name, T value)
    {
        var path = Path.Combine(home, name); File.WriteAllBytes(path, JsonSerializer.SerializeToUtf8Bytes(value, StateJson.Options)); return path;
    }
    private AssetPack Pack(AnimationRow? clip = null, string? sheetId = "people", IReadOnlyList<FurnitureAsset>? furniture = null)
    {
        File.WriteAllBytes(Path.Combine(home, "people.png"), SpriteAtlas.CreatePng());
        var sheet = new Sheet("people", "people.png", 16, 24, [clip ?? new("idle", "any", 0, 1, 1)], new(8, 24));
        JsonFile("assets.json", new AssetManifest(1, [sheet], furniture ?? [], [new("person", sheetId!)], []));
        return new(home);
    }
    private static void Sta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() => { try { action(); } catch (Exception e) { error = e; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (error != null) ExceptionDispatchInfo.Capture(error).Throw();
    }

    [Fact]
    public void F01_SettingsHostileNumbersRoundTripAsFiniteValues()
    {
        var random = new Random(101);
        var values = new[] { "1e999", "-1e999", "-1234567", "1e100", "null", "\"NaN\"", "{}", "[]" };
        foreach (var culture in new[] { "ko-KR", "de-DE" })
        {
            var original = System.Globalization.CultureInfo.CurrentCulture;
            try
            {
                System.Globalization.CultureInfo.CurrentCulture = new(culture);
                for (var i = 0; i < 80; i++)
                {
                    var field = new[] { "opacity", "zoom", "width", "height", "left", "top", "workingSeconds", "goneMinutes", "leaveAfterIdleMinutes", "leaveAfterWaitingMinutes" }[random.Next(10)];
                    var path = Path.Combine(home, "settings.json");
                    File.WriteAllText(path, "{\"" + field + "\":" + values[random.Next(values.Length)] + "}");
                    var settings = Settings.Read(path);
                    var bytes = JsonSerializer.SerializeToUtf8Bytes(settings, StateJson.Options);
                    Assert.Equal(settings, JsonSerializer.Deserialize<Settings>(bytes, StateJson.Options));
                    Assert.InRange(settings.Opacity, .25, 1); Assert.True(settings.GoneMinutes * 60 > settings.WorkingSeconds);
                    new TrackerOptions { Home = home, WorkingWindow = TimeSpan.FromSeconds(settings.WorkingSeconds),
                        LeaveAfterIdle = TimeSpan.FromMinutes(settings.LeaveAfterIdleMinutes), LeaveAfterWaiting = TimeSpan.FromMinutes(settings.LeaveAfterWaitingMinutes) }.Validate();
                }
            }
            finally { System.Globalization.CultureInfo.CurrentCulture = original; }
        }
        var cli = new Settings { Opacity = double.NaN, Zoom = double.PositiveInfinity, Width = -1,
            Height = double.NegativeInfinity, Left = double.NaN, Top = double.PositiveInfinity, WorkingSeconds = double.MaxValue, GoneMinutes = -1 }.Normalize();
        Assert.Null(cli.Left); Assert.Null(cli.Top); Assert.InRange(cli.Width, 400, 3000);
        JsonSerializer.SerializeToUtf8Bytes(cli, StateJson.Options);
    }

    [Fact]
    public void F02_HostileLayoutsFallBackAndPreserveSource()
    {
        var random = new Random(202); var pack = new AssetPack(); var room = Room();
        for (var i = 0; i < 48; i++)
        {
            var huge = random.Next(128, int.MaxValue);
            var layout = (i % 6) switch
            {
                0 => room with { AutoDesks = new(X: huge, Capacity: 1) },
                1 => room with { AutoDesks = new(SpacingY: huge) },
                2 => room with { Furniture = [null!] },
                3 => room with { Furniture = [new("desk", 3, 5, 3, 2)] },
                4 => room with { Zones = [null!] },
                _ => room with { OverflowSeats = [null!] }
            };
            var path = JsonFile("layout.json", layout); var before = File.ReadAllBytes(path); var warnings = new List<string>();
            var loaded = LayoutLoader.Load(pack, path, warnings);
            Assert.NotEmpty(warnings); Assert.Equal(before, File.ReadAllBytes(path)); LayoutLoader.Validate(loaded, pack);
        }
    }

    [Fact]
    public void F03_OverflowingClipsAreRejectedBeforeFrameCreation()
    {
        var random = new Random(303);
        for (var i = 0; i < 32; i++)
        {
            var clip = i % 2 == 0 ? new AnimationRow("idle", "any", random.Next(int.MaxValue / 2, int.MaxValue), 1, 1) :
                new AnimationRow("idle", "any", 0, 256, 1, Column: random.Next(int.MaxValue / 2, int.MaxValue));
            var pack = Pack(clip); Assert.NotEmpty(pack.Warnings);
            var frame = pack.Frame("fallback", "idle", "down", 0);
            Assert.True(frame.X >= 0 && frame.Y >= 0);
        }
    }

    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData("missing")]
    public void F04_CharacterRequiresAnExistingSheet(string? sheetId)
    {
        var pack = Pack(sheetId: sheetId); Assert.NotEmpty(pack.Warnings);
        var office = new OfficeAnimator(42, pack, Room());
        Assert.NotNull(Assert.Single(office.Update([Agent("a")], Now, 0)).Pose.Sprite);
    }

    [Fact]
    public void F04_MissingCharacterSheetIdIsRejected()
    {
        Pack(); var path = Path.Combine(home, "assets.json");
        var node = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!;
        node["characters"]![0]!.AsObject().Remove("sheetId"); File.WriteAllText(path, node.ToJsonString());
        Assert.NotEmpty(new AssetPack(home).Warnings);
    }

    [Fact]
    public void F05_PartialCozyPackAndIncompatibleDeskUseAValidFallback()
    {
        foreach (var furniture in new IReadOnlyList<FurnitureAsset>[] {
            [new("cozy-desk", "desk", [new(0, "default", 3, 2, [new("seat", 1, 2, "typing", "up")])])],
            [new("desk", "desk", [new(0, "default", 64, 64, [new("seat", 1, 64, "typing", "up")])])] })
        {
            var pack = Pack(furniture: furniture); Assert.Empty(pack.Warnings);
            var layout = LayoutLoader.Load(pack, null, pack.Warnings); LayoutLoader.Validate(layout, pack);
            Assert.NotNull(new OfficeAnimator(42, pack, layout));
        }
    }

    [Fact]
    public void F06_OneTileWideZonesRenderWithoutAWindow() => Sta(() =>
    {
        var pack = new AssetPack(); var layout = Room(); LayoutLoader.Validate(layout, pack);
        Assert.Empty(new OfficeEditor(pack, layout).Validate());
        var view = new OfficeView(pack, Path.Combine(home, "brand")); view.Update(layout, [], Now);
        Assert.Equal(400, Officelings.App.Program.RenderBitmap(view, 400, 300).PixelWidth);
    });

    [Fact]
    public async Task F07_ReachabilityAndStartupHaveBoundedWork()
    {
        var random = new Random(707); var room = Room(128, 128);
        var points = Enumerable.Range(0, 1500).Select(i => new InteractionPoint("point-" + i, 125, 122, "phone", "down")).ToArray();
        room = room with { Zones = room.Zones!.Select(z => z.Name == "workspace" ? z with { Points = points } : z).ToArray() };
        var pack = new AssetPack(); LayoutLoader.Validate(room, pack); // warm the JIT
        var stopwatch = Stopwatch.StartNew(); LayoutLoader.Validate(room, pack);
        Assert.True(stopwatch.ElapsedMilliseconds < 100, $"Validation took {stopwatch.ElapsedMilliseconds} ms");
        Assert.Empty(new OfficeEditor(pack, room).Validate());
        for (var i = 0; i < 8; i++)
        {
            var walls = Enumerable.Range(0, 128 * 128).Select(_ => random.Next(3) == 0 ? 0 : 1).ToArray();
            var hostile = room with { Tiles = walls };
            stopwatch.Restart(); OfficeAnimator.FindPath(hostile, new(1, 1), new(126, 126));
            Assert.True(stopwatch.ElapsedMilliseconds < 100);
        }
        var path = JsonFile("layout.json", room with { Zones = room.Zones!.Select(z => z.Name == "workspace" ? z with { Points = points.Concat(points).Concat(points).ToArray() } : z).ToArray() });
        using var storage = new AppStorage(true, home);
        // Explicit test input is outside the synthetic personal home.
        var outside = Path.Combine(storage.Home, "hostile.json"); Directory.CreateDirectory(storage.Home); File.Copy(path, outside);
        stopwatch.Restart(); var loading = Officelings.App.Program.LoadOfficeAsync(home, outside, storage);
        Assert.True(stopwatch.ElapsedMilliseconds < 100);
        // home is intentionally forbidden: this also exercises the no-personal-input guard.
        await Assert.ThrowsAsync<ArgumentException>(async () => await loading);
        loading = Officelings.App.Program.LoadOfficeAsync(null, outside, storage);
        var result = await loading; Assert.NotEmpty(result.Assets.Warnings); LayoutLoader.Validate(result.Layout, result.Assets);
    }

    [Fact]
    public async Task F08_LockedPublicationRetriesLogsOnceAndResumes()
    {
        var path = Path.Combine(home, "state.json"); StateJson.WriteAtomic(path, "old"u8.ToArray());
        var logs = new List<string>(); var attempts = 0;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        Task publish;
        using (var consumer = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            publish = StateJson.PublishWithRetryAsync(() => { Interlocked.Increment(ref attempts); StateJson.WriteAtomic(path, "new"u8.ToArray()); }, logs.Add, cancellation.Token);
            while (Volatile.Read(ref attempts) < 3) await Task.Delay(25, cancellation.Token);
            Assert.False(publish.IsCompleted); Assert.True(attempts >= 3); Assert.Single(logs);
            Assert.Equal("old", File.ReadAllText(path));
        }
        await publish; Assert.Equal("new", File.ReadAllText(path)); Assert.Single(logs); Assert.Empty(Directory.GetFiles(home, "*.tmp"));
    }

    [Fact]
    public async Task F08_TrackerProcessSurvivesAConsumerLock()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder != null && !File.Exists(Path.Combine(folder.FullName, "Officelings.sln"))) folder = folder.Parent;
        Assert.NotNull(folder);
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        var tracker = Path.Combine(folder.FullName, "src", "Officelings.Tracker", "bin", configuration, "net10.0", "Officelings.Tracker.dll");
        Assert.True(File.Exists(tracker), "Build the solution before this process regression test.");
        Pack(); var layout = JsonFile("layout.json", Room()); var output = Path.Combine(home, "output");
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var argument in new[] { tracker, "--home", Path.Combine(home, "logs"), "--output", output, "--asset-dir", home,
            "--layout", layout, "--seconds", "4", "--interval-seconds", ".05" }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var errors = process.StandardError.ReadToEndAsync(); var log = process.StandardOutput.ReadToEndAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try
        {
            var state = Path.Combine(output, "state.json");
            while (!File.Exists(state)) { Assert.False(process.HasExited); await Task.Delay(25, deadline.Token); }
            long sequence;
            using (var consumer = new FileStream(state, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                using var json = JsonDocument.Parse(consumer); sequence = json.RootElement.GetProperty("sequence").GetInt64();
                // Content is quiet now: publication happens at the three-second
                // heartbeat, so keep this consumer open across that actual write.
                await Task.Delay(3250, deadline.Token); Assert.False(process.HasExited);
            }
            await process.WaitForExitAsync(deadline.Token);
            Assert.Equal(0, process.ExitCode);
            using var final = JsonDocument.Parse(File.ReadAllBytes(state));
            Assert.True(final.RootElement.GetProperty("sequence").GetInt64() > sequence);
            Assert.Single((await errors).Split('\n'), line => line.Contains("retrying", StringComparison.Ordinal));
            await log;
        }
        finally { if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); } }
    }

    [Theory]
    [InlineData("demo")] [InlineData("qa")]
    public void F09_DemoAndQaEditorUseOnlyAnIsolatedHome(string mode) => Sta(() =>
    {
        Assert.True(mode is "demo" or "qa");
        var personalLayout = JsonFile("layout.json", Room(24, 17, 1));
        var personalSettings = JsonFile("settings.json", new Settings { Opacity = .5 });
        var beforeLayout = File.ReadAllBytes(personalLayout); var beforeSettings = File.ReadAllBytes(personalSettings);
        using var storage = new AppStorage(true, home);
        // Fail before any read/write if either real-personal selection regresses.
        Assert.False(storage.LayoutPath.StartsWith(home, StringComparison.OrdinalIgnoreCase));
        Assert.False(storage.SettingsPath.StartsWith(home, StringComparison.OrdinalIgnoreCase));
        Assert.Throws<ArgumentException>(() => storage.CheckInput(personalLayout));
        Assert.Throws<ArgumentException>(() => storage.CheckInput(personalSettings));
        Assert.Equal(1, Settings.Read(storage.SettingsPath).Opacity);
        var pack = new AssetPack(); var view = new OfficeView(pack, Path.Combine(storage.Home, "brand"));
        var panel = new OfficeEditorPanel(view, pack, Room(), storage.LayoutPath);
        typeof(OfficeEditorPanel).GetMethod("Save", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(panel, null);
        Assert.True(File.Exists(storage.LayoutPath)); panel.Detach();
        Assert.Equal(beforeLayout, File.ReadAllBytes(personalLayout)); Assert.Equal(beforeSettings, File.ReadAllBytes(personalSettings));
    });

    [Fact]
    public void F11_RestoreAndRecoverClampAcrossMissingMonitorsAndDpi()
    {
        foreach (var dpi in new[] { 1d, 1.5, 2 })
        {
            Rect Scale(Rect r) => new(r.Left / dpi, r.Top / dpi, r.Width / dpi, r.Height / dpi);
            Rect[] areas = [Scale(new(0, 0, 1920, 1040)), Scale(new(1920, 0, 1920, 1040)), Scale(new(0, 1080, 1920, 1040))];
            foreach (var r in new[] { new Rect(2200, 1400, 720, 480), new Rect(-10000, -10000, 720, 480), new Rect(3839, 10, 720, 480) })
            {
                var restored = WindowPlacement.Clamp(Scale(r), areas); Assert.Contains(areas, a => a.Contains(restored));
                Assert.Equal(restored, WindowPlacement.Clamp(restored, areas));
            }
        }
    }

    [Fact]
    public void F12_PreviewSeatsReserveDistinctTilesAndHideWhenFull()
    {
        var pack = new AssetPack(); var room = Room(desks: 2); var office = new OfficeAnimator(42, pack, room);
        var agents = new[] { Agent("a"), Agent("b") }; office.Update(agents, Now, 0);
        office.ReloadLayout(room with { Tiles = Enumerable.Range(0, 24 * 17).Select(i => i == 25 ? 1 : 0).ToArray() }, validate: false);
        var views = office.Update(agents, Now.AddSeconds(1), .1);
        Assert.Single(views, v => v.Pose.Visible);
        Assert.Single(views, v => !v.Pose.Visible && v.Pose.Queued && v.Pose.DeskId == null);
        Assert.Equal(new Tile(1, 1), Assert.Single(views, v => v.Pose.Visible).Pose.Desk);
    }

    [Fact]
    public void F13_WaitingTilesExpandWithoutReuseAndRecoverFromSaturation()
    {
        var pack = new AssetPack(); var room = Room(8, 8); var office = new OfficeAnimator(42, pack, room);
        var agents = Enumerable.Range(0, 80).Select(i => Agent("q" + i.ToString("D3"))).ToArray();
        var views = office.Update(agents, Now, 0); var visible = views.Where(v => v.Pose.Visible).ToArray();
        Assert.Equal(63, visible.Length); Assert.Equal(63, visible.Select(v => v.Pose.Desk).Distinct().Count());
        Assert.All(views.Where(v => !v.Pose.Visible), v => Assert.True(v.Pose.Queued));
        views = office.Update(agents.Skip(60).ToArray(), Now.AddSeconds(1), .1);
        Assert.All(views, v => Assert.True(v.Pose.Visible)); Assert.Equal(20, views.Select(v => v.Pose.Desk).Distinct().Count());
    }

    [Fact]
    public void F14_DepartedDeskHistoryIsBoundedAndRecentReentryKeepsItsDesk()
    {
        var office = new OfficeAnimator(42, new AssetPack(), Room(desks: 1));
        for (var i = 0; i < 2200; i++) office.Update([Agent("session:" + i)], Now.AddSeconds(i), 0);
        office.Update([], Now.AddSeconds(2200), 0);
        var history = (System.Collections.IDictionary)typeof(OfficeAnimator).GetField("deskHistory", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(office)!;
        Assert.InRange(history.Count, 1, 1024); Assert.False(history.Contains("session:0")); Assert.True(history.Contains("session:2199"));
        Assert.Equal("desk-0", Assert.Single(office.Update([Agent("session:2199")], Now.AddSeconds(2201), 0)).Pose.DeskId);
    }
}
