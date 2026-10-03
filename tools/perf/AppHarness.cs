using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Vibedotcom.App;
using Vibedotcom.Core;
using AppProgram = Vibedotcom.App.Program;

namespace Perf;
internal static class Entry
{
    [DllImport("user32.dll")] static extern uint GetGuiResources(IntPtr process, uint flag);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hwnd, int index);
    static object Field(object obj, string name) => obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(obj)!;
    static void SetField(object obj, string name, object value) => obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(obj, value);
    static void Write(string file, object value) => File.WriteAllText(file, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
    static int startupRetries;
    static T RetryLayoutBudget<T>(Func<T> create)
    {
        for (int attempt=0;;attempt++)
        {
            try { return create(); }
            catch (InvalidDataException ex) when (ex.Message.Contains("work/time budget") && attempt<7) { startupRetries++; Console.Error.WriteLine("Retrying unmodified committed constructor after layout wall-clock budget rejection."); }
        }
    }
    static AgentInfo[] Agents(int count, DateTimeOffset now, AgentState state) => Enumerable.Range(0, count).Select(i =>
        new AgentInfo($"perf:{i:00}", (Provider)(i % 3), null, false, $"Invented {i + 1}", "Perf", "fabricated-model", "C:/Invented", "Synthetic performance session", null, null, state, now, now.AddSeconds(-90), false)).ToArray();
    static object Memory()
    {
        using var p = Process.GetCurrentProcess(); p.Refresh();
        return new { workingSet = p.WorkingSet64, privateBytes = p.PrivateMemorySize64, managedBytes = GC.GetTotalMemory(false), gdi = GetGuiResources(p.Handle, 0), user = GetGuiResources(p.Handle, 1), threads = p.Threads.Count };
    }
    [STAThread] public static int Main(string[] args)
    {
        try
        {
            var output = Path.GetFullPath(args[1]); Directory.CreateDirectory(output);
            var app = new Application();
            if (args[0] == "render") return Render(output);
            if (args[0] == "reuse") return Reuse(output);
            if (args[0] == "pack") return Pack(output);
            if (args[0] == "paths") { Write(Path.Combine(output, "paths.json"), new { userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), cwd = Environment.CurrentDirectory }); return 0; }
            if (args[0] == "soak") return Soak(app, output, int.Parse(args[2]), args[3], double.Parse(args[4], System.Globalization.CultureInfo.InvariantCulture));
            throw new ArgumentException("render|pack|soak OUTPUT [COUNT MODE SECONDS]");
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    static (AssetPack Assets, OfficeLayout Layout) Load()
    {
        var assets = AssetPack.LoadDefault(useUserPack: false);
        return (assets, LayoutLoader.Load(assets, null, assets.Warnings));
    }
    static int Render(string output)
    {
        var (assets, layout) = Load(); var now = DateTimeOffset.UtcNow;
        var office = new OfficeAnimator(42, assets, layout); var agents = Agents(12, now, AgentState.Working);
        IReadOnlyList<AgentView> poses = Array.Empty<AgentView>();
        for (int i = 0; i < 120; i++) poses = office.Update(agents, now.AddSeconds(-12 + i * .1), .1);
        var view = new OfficeView(assets, Path.Combine(output, "empty-brand")); var rows = new List<object>();
        foreach (var zoom in new[] { 1, 2, 3, 6 })
        {
            int width = office.Layout.WidthTiles * 16 * zoom + 32, height = office.Layout.HeightTiles * 16 * zoom + 32;
            view.SetZoom(zoom); view.Update(office.Layout, poses, now, office.FurnitureSprites);
            for (int i = 0; i < 3; i++) { view.InvalidateVisual(); _ = AppProgram.RenderBitmap(view, width, height); }
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            var gc = Enumerable.Range(0, 3).Select(GC.CollectionCount).ToArray();
            long alloc = GC.GetTotalAllocatedBytes(true); var times = new List<double>(); var watch = Stopwatch.StartNew();
            for (int i = 0; i < 40 && (i < 8 || watch.Elapsed.TotalSeconds < 20); i++)
            {
                var t = now.AddMilliseconds(i * 125); poses = office.Update(agents, t, .125);
                long start = Stopwatch.GetTimestamp(); view.Update(office.Layout, poses, t, office.FurnitureSprites); view.InvalidateVisual();
                _ = AppProgram.RenderBitmap(view, width, height); times.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            }
            var allocated = GC.GetTotalAllocatedBytes(true) - alloc;
            var collections = Enumerable.Range(0, 3).Select(i => GC.CollectionCount(i) - gc[i]).ToArray();
            Write(Path.Combine(output, $"render-{zoom}x.json"), new { zoom, width, height, agents = 12, visible = poses.Count(p => p.Pose.Visible), frames = times.Count, milliseconds = times, allocatedBytes = allocated, bytesPerFrame = allocated / times.Count, gcCollections = collections, wallSeconds = watch.Elapsed.TotalSeconds, memory = Memory(), note = "Committed Program.RenderBitmap: fresh hidden HwndSource and RenderTargetBitmap each frame; includes View.Update, excludes OfficeAnimator.Update and PNG encoding." });
            rows.Add(new { zoom, meanMs = times.Average(), medianMs = times.Order().ElementAt(times.Count / 2), maxMs = times.Max(), bytesPerFrame = allocated / times.Count, gcCollections = collections });
            Console.WriteLine($"Completed {zoom}x: {times.Count} frames / {watch.Elapsed.TotalSeconds:F1}s.");
        }
        Console.WriteLine(JsonSerializer.Serialize(rows)); return 0;
    }
    static int Pack(string output)
    {
        var watch = Stopwatch.StartNew(); var before = Memory(); var (assets, layout) = Load(); var loaded = Memory(); var loadMs = watch.Elapsed.TotalMilliseconds;
        long decodeStart = Stopwatch.GetTimestamp(); var view = new OfficeView(assets, Path.Combine(output, "empty-brand")); var decoded = Memory(); var decodeMs = Stopwatch.GetElapsedTime(decodeStart).TotalMilliseconds;
        var sheets = (IDictionary)Field(view, "sheets"); long pixels = 0;
        foreach (BitmapSource b in sheets.Values) pixels += (long)b.PixelWidth * b.PixelHeight * ((b.Format.BitsPerPixel + 7) / 8);
        var office = new OfficeAnimator(42, assets, layout); var now = DateTimeOffset.UtcNow; var agents = Agents(12, now, AgentState.Working);
        var poses = office.Update(agents, now, 0); view.Update(office.Layout, poses, now, office.FurnitureSprites); view.SetZoom(1);
        long frameStart = Stopwatch.GetTimestamp(); _ = AppProgram.RenderBitmap(view, 720, 480);
        Write(Path.Combine(output, $"pack-{Environment.ProcessId}.json"), new { loadMs, decodeMs, firstFrameMs = Stopwatch.GetElapsedTime(frameStart).TotalMilliseconds, inProcessFirstFrameMs = watch.Elapsed.TotalMilliseconds, renderTier = RenderCapability.Tier >> 16, embeddedResources = typeof(AssetPack).Assembly.GetManifestResourceNames().Count(n => n.StartsWith("Vibedotcom.Core.Pack.") && n.EndsWith(".png")), decodedSheets = sheets.Count, compressedBytes = assets.Images.Sum(k => (long)k.Value.Length), decodedPixelBytes = pixels, before, loaded, decoded, afterFrame = Memory(), warnings = assets.Warnings });
        Console.WriteLine("Pack/first-frame timing captured."); return 0;
    }
    static int Reuse(string output)
    {
        var (assets, layout) = Load(); var now = DateTimeOffset.UtcNow;
        var office = new OfficeAnimator(42, assets, layout); var agents = Agents(12, now, AgentState.Working);
        IReadOnlyList<AgentView> poses = Array.Empty<AgentView>();
        for (int i = 0; i < 120; i++) poses = office.Update(agents, now.AddSeconds(-12 + i * .1), .1);
        var view = new OfficeView(assets, Path.Combine(output, "empty-brand"));
        foreach (var zoom in new[] { 1, 2, 3, 6 })
        {
            int width = office.Layout.WidthTiles * 16 * zoom + 32, height = office.Layout.HeightTiles * 16 * zoom + 32;
            view.SetZoom(zoom);
            using var source = new HwndSource(new HwndSourceParameters("Perf reusable source") { PositionX = -30000, PositionY = -30000, Width = width, Height = height, WindowStyle = unchecked((int)0x80000000), ExtendedWindowStyle = 0x08000080 });
            var root = new ContainerVisual(); root.Children.Add(view); source.RootVisual = root;
            view.Width = width; view.Height = height; view.Measure(new(width, height)); view.Arrange(new(0, 0, width, height));
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            void Frame(int i)
            {
                var t = now.AddMilliseconds(i * 125); poses = office.Update(agents, t, .125);
                view.Update(office.Layout, poses, t, office.FurnitureSprites); view.InvalidateVisual(); view.UpdateLayout();
                view.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle); bitmap.Clear(); bitmap.Render(root);
            }
            for (int i=0; i<3; i++) Frame(i);
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            var gc = Enumerable.Range(0, 3).Select(GC.CollectionCount).ToArray(); long alloc = GC.GetTotalAllocatedBytes(true); var times = new List<double>(); var watch = Stopwatch.StartNew();
            for (int i=0; i<40 && (i<8 || watch.Elapsed.TotalSeconds<20); i++) { long start=Stopwatch.GetTimestamp(); Frame(i); times.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds); }
            Write(Path.Combine(output, $"reuse-{zoom}x.json"), new {zoom,width,height,agents=12,visible=poses.Count(p=>p.Pose.Visible),frames=times.Count,milliseconds=times,allocatedBytes=GC.GetTotalAllocatedBytes(true)-alloc,bytesPerFrame=(GC.GetTotalAllocatedBytes(true)-alloc)/times.Count,gcCollections=Enumerable.Range(0,3).Select(i=>GC.CollectionCount(i)-gc[i]).ToArray(),memory=Memory(),note="Owned persistent hidden HwndSource and mutable RenderTargetBitmap; includes OfficeAnimator.Update (unlike smoke baseline)."});
            source.RootVisual=null;root.Children.Remove(view); Console.WriteLine($"Reusable {zoom}x: {times.Count} frames / {watch.Elapsed.TotalSeconds:F1}s.");
        }
        return 0;
    }
    static int Soak(Application app, string output, int count, string mode, double seconds)
    {
        if (mode is not ("normal" or "topmost" or "transparent") || seconds <= 0 || seconds > 660) throw new ArgumentException("Bounded owned-window modes only.");
        var foreground = GetForegroundWindow(); var (assets, layout) = Load(); var now = DateTimeOffset.UtcNow;
        var office = RetryLayoutBudget(() => new OfficeAnimator(42, assets, layout)); var agents = Agents(count, now, AgentState.Idle);
        for (int i = 0; i < 40; i++) office.Update(agents, now.AddSeconds(-4 + i * .1), .1);
        var settings = new Settings { Mode = mode == "topmost" ? "topmost" : "normal", TransparentBackground = mode == "transparent", Borderless = true, Zoom = 1, Width = 720, Height = 480 };
        // QA keeps Save/tray/hover suppressed. Main's 60-second CLI cap is not used.
        var window = RetryLayoutBudget(() => new OfficeWindow(false, settings, seconds + 120, assets, layout, true, office, agents));
        ((AgentTracker)Field(window, "tracker")).Dispose();
        var fakeHome = Path.Combine(Path.GetTempPath(), "vd-perf-empty-" + Environment.ProcessId); Directory.CreateDirectory(fakeHome);
        var tracker = new AgentTracker(new TrackerOptions { Home = fakeHome }); SetField(window, "tracker", tracker);
        // Fixed injected list instead of PollAsync replacing it with an empty-home list.
        SetField(window, "nextPoll", DateTimeOffset.MaxValue);
        SetField(window, "settings", settings);
        SetField((OfficeView)Field(window, "view"), "brandIcons", new BrandIcons(Path.Combine(fakeHome, "brand")));
        // Verification override prevents taskbar exposure/activation; never enters Explorer hosting.
        SetField(window, "modes", new WindowModes(window, false, (nint?)0));
        window.Width = 720; window.Height = 480; window.Left = -30000; window.Top = -30000;
        window.WindowStartupLocation = WindowStartupLocation.Manual; window.ShowActivated = false; window.ShowInTaskbar = false;
        var timer = (DispatcherTimer)Field(window, "timer"); long ticks = 0; timer.Tick += (_, _) => ticks++;
        var pollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) }; bool polling = false; long polls = 0;
        var stopTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        stopTimer.Tick += (_, _) => { if (File.Exists(Path.Combine(output, "stop.request"))) { stopTimer.Stop(); window.Close(); } };
        stopTimer.Start();
        pollTimer.Tick += async (_, _) => { if (polling) return; polling = true; try { await System.Threading.Tasks.Task.Run(() => tracker.Poll(DateTimeOffset.UtcNow)); polls++; } finally { polling = false; } };
        var gc = Enumerable.Range(0, 3).Select(GC.CollectionCount).ToArray(); var allocated = GC.GetTotalAllocatedBytes(true); var watch = Stopwatch.StartNew();
        window.ContentRendered += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            Write(Path.Combine(output, "ready.json"), new { pid = Environment.ProcessId, count, mode, startupRetries, hwnd = hwnd.ToInt64(), style = GetWindowLong(hwnd, -16), exStyle = GetWindowLong(hwnd, -20), nativeTopmost = (GetWindowLong(hwnd, -20) & 8) != 0, allowsTransparency = window.AllowsTransparency, foregroundUnchanged = foreground == GetForegroundWindow(), startedUtc = DateTimeOffset.UtcNow, memory = Memory(), fakeHome, decodedSheets = ((IDictionary)Field((OfficeView)Field(window,"view"),"sheets")).Count, warnings = assets.Warnings });
            pollTimer.Start();
        };
        window.Closed += (_, _) =>
        {
            pollTimer.Stop(); stopTimer.Stop(); var view = (OfficeView)Field(window, "view");
            Write(Path.Combine(output, "finished.json"), new { seconds = watch.Elapsed.TotalSeconds, count, mode, ticks, emptyHomePolls = polls, logBytesRead = tracker.BytesRead, lastViewCount = view.Agents.Count, visible = view.Agents.Count(a => a.Pose.Visible), allocatedBytes = GC.GetTotalAllocatedBytes(true) - allocated, gcCollections = Enumerable.Range(0, 3).Select(i => GC.CollectionCount(i) - gc[i]).ToArray(), caches = new { sheets = ((IDictionary)Field(view, "sheets")).Count, sprites = ((IDictionary)Field(view, "sprites")).Count, spriteContent = ((IDictionary)Field(view, "spriteContent")).Count, text = ((IDictionary)Field(view, "textCache")).Count }, memory = Memory(), foregroundSameAtEnd = foreground == GetForegroundWindow() });
        };
        return app.Run(window);
    }
}
