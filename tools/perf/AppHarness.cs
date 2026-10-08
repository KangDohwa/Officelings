using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Officelings.App;
using Officelings.Core;
using AppProgram = Officelings.App.Program;

namespace Perf;
internal static class Entry
{
    [DllImport("user32.dll")] static extern uint GetGuiResources(IntPtr process, uint flag);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll")] static extern bool GetThreadTimes(IntPtr thread, out long created, out long exited, out long kernel, out long user);
    [DllImport("kernel32.dll")] static extern IntPtr OpenThread(uint access, bool inherit, uint id);
    [DllImport("kernel32.dll")] static extern int GetThreadDescription(IntPtr thread, out IntPtr description);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr handle);
    static double? UiCpu() => GetThreadTimes(new IntPtr(-2), out _, out _, out long kernel, out long user) ? (kernel + user) / 10000000d : null;
    sealed class SamplerCost
    {
        public double milliseconds { get; set; }
        public double counterMilliseconds { get; set; }
        public double memoryMilliseconds { get; set; }
        public double threadMilliseconds { get; set; }
        public double? cpuSecondsUi { get; set; }
        public long allocatedBytesCurrentThread { get; set; }
    }
    static Dictionary<int, double> ThreadCpu(Process process, Dictionary<int, string> names)
    {
        var values = new Dictionary<int, double>();
        foreach (ProcessThread thread in process.Threads)
        {
            using (thread)
            {
                try
                {
                    values[thread.Id] = thread.TotalProcessorTime.TotalSeconds;
                    if (names.ContainsKey(thread.Id)) continue;
                    string name = ""; var handle = OpenThread(0x0800, false, (uint)thread.Id);
                    if (handle != IntPtr.Zero)
                    {
                        try { if (GetThreadDescription(handle, out var pointer) == 0 && pointer != IntPtr.Zero) { try { name = Marshal.PtrToStringUni(pointer) ?? ""; } finally { LocalFree(pointer); } } }
                        finally { CloseHandle(handle); }
                    }
                    names[thread.Id] = name;
                }
                catch (System.ComponentModel.Win32Exception) { }
                catch (InvalidOperationException) { }
            }
        }
        return values;
    }
    static object Field(object obj, string name) => obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(obj)!;
    static void SetField(object obj, string name, object value) => obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(obj, value);
    static void Write(string file, object value)
    {
        using var stream = new FileStream(file, FileMode.CreateNew, FileAccess.Write);
        JsonSerializer.Serialize(stream, value, new JsonSerializerOptions { WriteIndented = true });
    }
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
            Write(Path.Combine(output, "runtime.json"), new { command = args[0], startedUtc = DateTimeOffset.UtcNow, runtime = RuntimeInformation.FrameworkDescription,
                app = new { path = typeof(OfficeView).Assembly.Location, mvid = typeof(OfficeView).Module.ModuleVersionId, sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(OfficeView).Assembly.Location))) },
                core = new { path = typeof(OfficeAnimator).Assembly.Location, mvid = typeof(OfficeAnimator).Module.ModuleVersionId, sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(OfficeAnimator).Assembly.Location))) } });
            var app = new Application();
            if (args[0] == "render") return Render(output);
            if (args[0] == "reuse") return Reuse(output);
            if (args[0] == "pack") return Pack(output);
            if (args[0] == "phases") return Phases(output);
            if (args[0] == "pixels") return Pixels(output);
            if (args[0] == "pixel-diff") return PixelDiff(output, args[2], args[3]);
            if (args[0] == "counts") return Counts(output);
            if (args[0] == "changing-tv") return ChangingTv(output);
            if (args[0] == "startup") return Startup(app, output);
            if (args[0] == "live" || args[0] == "observer") return Live(app, output, int.Parse(args[2]), args[3], double.Parse(args[4], System.Globalization.CultureInfo.InvariantCulture), double.Parse(args[5], System.Globalization.CultureInfo.InvariantCulture), observer: args[0] == "observer", threadCpu: args.Length > 6 && args[6] == "threads");
            if (args[0].StartsWith("external-",StringComparison.Ordinal)) return Live(app, output, int.Parse(args[2]), args[3], double.Parse(args[4], System.Globalization.CultureInfo.InvariantCulture), double.Parse(args[5], System.Globalization.CultureInfo.InvariantCulture), activities: args[0]=="external-activities", pollEnabled: args[0]=="external-poll", observer: args[0]=="external-observer", external:true, scene36:args[0]=="external-p36");
            if (args[0] == "activities") return Live(app, output, int.Parse(args[2]), args[3], double.Parse(args[4], System.Globalization.CultureInfo.InvariantCulture), double.Parse(args[5], System.Globalization.CultureInfo.InvariantCulture), activities: true);
            if (args[0] == "live-poll") return Live(app, output, int.Parse(args[2]), args[3], double.Parse(args[4], System.Globalization.CultureInfo.InvariantCulture), double.Parse(args[5], System.Globalization.CultureInfo.InvariantCulture), pollEnabled: true);
            if (args[0] == "paths") { Write(Path.Combine(output, "paths.json"), new { userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), cwd = Environment.CurrentDirectory }); return 0; }
            if (args[0] == "soak") return Soak(app, output, int.Parse(args[2]), args[3], double.Parse(args[4], System.Globalization.CultureInfo.InvariantCulture));
            throw new ArgumentException("render|reuse|pack|phases|pixels|counts|changing-tv|startup OUTPUT; live|activities|live-poll OUTPUT COUNT MODE WARMUP_SECONDS SAMPLE_SECONDS; pixel-diff OUTPUT BASELINE CURRENT; soak OUTPUT COUNT MODE SECONDS");
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    static (AssetPack Assets, OfficeLayout Layout) Load()
    {
        var assets = AssetPack.LoadDefault(useUserPack: false);
        return (assets, LayoutLoader.Load(assets, null, assets.Warnings));
    }
    static readonly DateTimeOffset Epoch = DateTimeOffset.Parse("2026-10-04T00:00:00Z");
    static double Median(IEnumerable<double> values)
    {
        var sorted = values.Order().ToArray();
        return sorted.Length % 2 == 0 ? (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2 : sorted[sorted.Length / 2];
    }
    static IReadOnlyList<AgentView> Settle(OfficeAnimator office, AgentInfo[] agents, DateTimeOffset now)
    {
        IReadOnlyList<AgentView> poses = Array.Empty<AgentView>();
        for (int i = 0; i <= 1200; i++) poses = office.Update(agents, now.AddSeconds(-120 + i * .1), .1);
        if (poses.Any(p => p.Pose.Visible && (p.Pose.Action is "walking" or "entering" or "exiting" || p.Pose.SeatPhase is "turn" or "lower" or "raise")))
            throw new InvalidOperationException("Working fixture did not settle.");
        return poses;
    }
    static object MeasurePhase(string name, Action call, int iterations, string note, int warmupIterations = 20)
    {
        for (int i = 0; i < warmupIterations; i++) call(); // JIT/cache warmup is discarded.
        var times = new double[iterations]; var bytes = new long[iterations];
        for (int i = 0; i < iterations; i++)
        {
            long allocated = GC.GetAllocatedBytesForCurrentThread(), started = Stopwatch.GetTimestamp();
            call();
            times[i] = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            bytes[i] = GC.GetAllocatedBytesForCurrentThread() - allocated;
        }
        return new { phase = name, iterations, warmupIterations, milliseconds = times, allocatedBytesCurrentThread = bytes,
            medianMs = Median(times), medianAllocatedBytes = Median(bytes.Select(b => (double)b)), note };
    }
    static int Phases(string output)
    {
        var (assets, layout) = Load();
        foreach (int count in new[] { 0, 12 })
        {
            var office = RetryLayoutBudget(() => new OfficeAnimator(42, assets, layout));
            var agents = Agents(count, Epoch, AgentState.Working); var poses = Settle(office, agents, Epoch);
            var view = RetryLayoutBudget(() => new OfficeView(assets, Path.Combine(output, "empty-brand")));
            view.SetZoom(1); view.Width = 720; view.Height = 480; view.Measure(new(720, 480)); view.Arrange(new(0, 0, 720, 480));
            view.Update(office.Layout, poses, Epoch, office.FurnitureSprites);
            var assign = typeof(OfficeAnimator).GetMethod("AssignWorkplaces", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var furniture = typeof(OfficeAnimator).GetMethod("RefreshFurniture", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var render = typeof(OfficeView).GetMethod("OnRender", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var update = typeof(OfficeAnimator).GetMethod("Update")!;
            var viewUpdate = typeof(OfficeView).GetMethod("Update")!;
            var control = typeof(object).GetMethod("ToString")!;
            object[] coreArgs = { agents, Epoch, .125 }, furnitureArgs = { Epoch }, viewArgs = { office.Layout, poses, Epoch, office.FurnitureSprites };
            var drawing = new DrawingVisual();
            using var dc = drawing.RenderOpen(); object[] renderArgs = { dc };
            int frame = 0;
            var rows = new List<object>();
            rows.Add(MeasurePhase("reflection.control", () => control.Invoke(office, null), 500, "Diagnostic reflection/ToString control overhead; not subtracted from other phases."));
            rows.Add(MeasurePhase("Core.Update", () => { coreArgs[1] = Epoch.AddMilliseconds(frame++ * 125); update.Invoke(office, coreArgs); }, 500, "Includes AssignWorkplaces and RefreshFurniture. Reflection, boxed logical time and result allocations included."));
            rows.Add(MeasurePhase("Core.AssignWorkplaces", () => assign.Invoke(office, null), 500, "Isolated settled invocation; reflection overhead included."));
            rows.Add(MeasurePhase("Core.RefreshFurniture", () => furniture.Invoke(office, furnitureArgs), 500, "Isolated fixed-time invocation; reflection overhead included."));
            rows.Add(MeasurePhase("View.Update", () => viewUpdate.Invoke(view, viewArgs), 500, "Fixed settled snapshot; no rendering/dispatcher work; reflection overhead included."));
            rows.Add(MeasurePhase("View.OnRender", () => render.Invoke(view, renderArgs), 100, "Direct synchronous recording into an open DrawingContext; reflection overhead included; excludes rasterization, HWND and RenderTargetBitmap."));
            rows.Add(MeasurePhase("Program.RenderBitmap", () => { view.InvalidateVisual(); _ = AppProgram.RenderBitmap(view, 720, 480); }, 8, "Includes layout/dispatcher, OnRender, fresh hidden HWND and bitmap rasterization. Not subtractable from direct OnRender.", 3));
            Write(Path.Combine(output, $"phases-{count}.json"), new { count, state = "working", settled = true, visible = poses.Count(p => p.Pose.Visible), width = 720, height = 480, rows,
                coreMvid = typeof(OfficeAnimator).Module.ModuleVersionId, appMvid = typeof(OfficeView).Module.ModuleVersionId });
            Console.WriteLine($"Phase measurements complete: {count} settled working agents.");
        }
        return 0;
    }
    static int Pixels(string output)
    {
        var (assets, layout) = Load(); var records = new List<object>();
        foreach (int seconds in new[] { 0, 12, 36 })
        {
            var office = RetryLayoutBudget(() => new OfficeAnimator(42, assets, layout));
            IReadOnlyList<AgentView> poses = Array.Empty<AgentView>();
            for (int frame = 0; frame <= (seconds + 18) * 10; frame++)
            {
                double t = -18 + frame * .1;
                poses = office.Update(DemoScenario.Agents(Epoch, t), Epoch.AddSeconds(t), .1);
            }
            foreach (int zoom in new[] { 1, 2, 3 })
            {
                var view = RetryLayoutBudget(() => new OfficeView(assets, Path.Combine(output, "empty-brand")));
                view.SetZoom(zoom); view.Update(office.Layout, poses, Epoch.AddSeconds(seconds), office.FurnitureSprites);
                int width = office.Layout.WidthTiles * 16 * zoom + 32, height = office.Layout.HeightTiles * 16 * zoom + 32;
                var bitmap = AppProgram.RenderBitmap(view, width, height);
                var file = Path.Combine(output, $"demo-{seconds}s-{zoom}x.png");
                var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
                using (var stream = new FileStream(file, FileMode.CreateNew, FileAccess.Write)) png.Save(stream);
                var pixels = new byte[width * height * 4]; bitmap.CopyPixels(pixels, width * 4, 0);
                records.Add(new { seconds, zoom, width, height, visible = poses.Count(p => p.Pose.Visible), file = Path.GetFileName(file), pixelSha256 = Convert.ToHexString(SHA256.HashData(pixels)) });
            }
        }
        Write(Path.Combine(output, "pixels.json"), new { epoch = Epoch, seed = 42, stepSeconds = .1, startSeconds = -18, records });
        Console.WriteLine("Saved nine deterministic demo pixel references."); return 0;
    }
    static int PixelDiff(string output, string baseline, string current)
    {
        var rows = new List<object>(); bool passed = true;
        foreach (int seconds in new[] { 0, 12, 36 }) foreach (int zoom in new[] { 1, 2, 3 })
        {
            string name = $"demo-{seconds}s-{zoom}x.png";
            static BitmapSource ReadImage(string path)
            {
                using var stream = File.OpenRead(path);
                var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
                var bitmap = new FormatConvertedBitmap(frame, PixelFormats.Pbgra32, null, 0); bitmap.Freeze(); return bitmap;
            }
            var a = ReadImage(Path.Combine(baseline, name)); var b = ReadImage(Path.Combine(current, name));
            if (a.PixelWidth != b.PixelWidth || a.PixelHeight != b.PixelHeight) throw new InvalidDataException("Pixel dimensions differ: " + name);
            int width = a.PixelWidth, height = a.PixelHeight; var oldPixels = new byte[width * height * 4]; var newPixels = new byte[oldPixels.Length];
            a.CopyPixels(oldPixels, width * 4, 0); b.CopyPixels(newPixels, width * 4, 0);
            int changed = 0, left = width, top = height, right = -1, bottom = -1, maxChannelDelta = 0;
            for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
            {
                int index = (y * width + x) * 4; bool different = false;
                for (int channel = 0; channel < 4; channel++)
                {
                    int delta = Math.Abs(oldPixels[index + channel] - newPixels[index + channel]);
                    maxChannelDelta = Math.Max(maxChannelDelta, delta); different |= delta != 0;
                }
                if (!different) continue;
                changed++; left = Math.Min(left, x); top = Math.Min(top, y); right = Math.Max(right, x); bottom = Math.Max(bottom, y);
            }
            passed &= changed == 0;
            rows.Add(new { file = name, seconds, zoom, width, height, changedPixels = changed, changedPercent = 100d * changed / (width * height), maxChannelDelta,
                changedBounds = changed == 0 ? (object?)null : new { left, top, right, bottom } });
            Console.WriteLine($"{name}: {changed} changed pixels; max channel delta {maxChannelDelta}.");
        }
        Write(Path.Combine(output, "pixel-diff.json"), new { passed, format = "Pbgra32", baseline = Path.GetFullPath(baseline), current = Path.GetFullPath(current), rows });
        return passed ? 0 : 1;
    }
    static Dictionary<string, double?> Counters(OfficeWindow? window, OfficeView view, OfficeAnimator? office = null)
    {
        var counters = new Dictionary<string, double?>();
        foreach (string name in new[] { "AnimationTickCount", "MaintenanceTickCount", "HoverTickCount", "SceneRenderCount", "DynamicRenderCount", "RedrawPixelArea", "RoomBuildCount" })
        {
            object? target = name.EndsWith("TickCount", StringComparison.Ordinal) ? window : view;
            var property = target?.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            counters[name] = property == null ? null : Convert.ToDouble(property.GetValue(target));
        }
        if (office != null)
            foreach (string name in new[] { "UpdateCount", "WorkplaceAssignmentCount", "FurnitureRefreshCount" })
            {
                var property = office.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                counters["Core." + name] = property == null ? null : Convert.ToDouble(property.GetValue(office));
            }
        return counters;
    }
    static Dictionary<string, double?> Difference(Dictionary<string, double?> before, Dictionary<string, double?> after)
        => after.ToDictionary(p => p.Key, p => p.Value.HasValue && before[p.Key].HasValue ? p.Value - before[p.Key] : null);
    static int Counts(string output)
    {
        const int iterations = 240, warmupIterations = 40, width = 720, height = 480;
        var (assets, layout) = Load();
        foreach (int count in new[] { 0, 12 })
        {
            var workloadLayout = count == 0 ? layout with { Furniture = Array.Empty<Furniture>(), OverflowSeats = Array.Empty<OverflowSeat>(), AutoDesks = null } : layout;
            var office = RetryLayoutBudget(() => new OfficeAnimator(42, assets, workloadLayout));
            var agents = Agents(count, Epoch, AgentState.Working); var poses = Settle(office, agents, Epoch);
            var view = RetryLayoutBudget(() => new OfficeView(assets, Path.Combine(output, "empty-brand")));
            view.SetZoom(1); view.Width = width; view.Height = height;
            using var source = new HwndSource(new HwndSourceParameters("Perf counts owned offscreen source")
            { PositionX = -30000, PositionY = -30000, Width = width, Height = height, WindowStyle = unchecked((int)0x80000000), ExtendedWindowStyle = 0x08000080 });
            var root = new ContainerVisual(); root.Children.Add(view); source.RootVisual = root;
            view.Measure(new(width, height)); view.Arrange(new(0, 0, width, height));
            for (int i = 0; i < warmupIterations; i++)
            {
                var now = Epoch.AddMilliseconds(i * 125); poses = office.Update(agents, now, .125);
                view.Update(office.Layout, poses, now, office.FurnitureSprites); view.UpdateLayout();
                view.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            }
            var coreBytes = new long[iterations]; var viewBytes = new long[iterations];
            var coreMs = new double[iterations]; var viewMs = new double[iterations];
            var before = Counters(null, view, office); long startAllocated = GC.GetTotalAllocatedBytes(true), startTicks = Stopwatch.GetTimestamp();
            for (int i = 0; i < iterations; i++)
            {
                var now = Epoch.AddMilliseconds((warmupIterations + i) * 125);
                long allocated = GC.GetAllocatedBytesForCurrentThread(), start = Stopwatch.GetTimestamp();
                poses = office.Update(agents, now, .125);
                coreMs[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds; coreBytes[i] = GC.GetAllocatedBytesForCurrentThread() - allocated;
                allocated = GC.GetAllocatedBytesForCurrentThread(); start = Stopwatch.GetTimestamp();
                view.Update(office.Layout, poses, now, office.FurnitureSprites); view.UpdateLayout();
                view.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                viewMs[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds; viewBytes[i] = GC.GetAllocatedBytesForCurrentThread() - allocated;
            }
            double wallSeconds = Stopwatch.GetElapsedTime(startTicks).TotalSeconds; long allocatedBytes = GC.GetTotalAllocatedBytes(true) - startAllocated;
            var delta = Difference(before, Counters(null, view, office));
            double? logicalArea = delta["RedrawPixelArea"] ?? delta["SceneRenderCount"] * width * height;
            Write(Path.Combine(output, $"counts-{count}.json"), new { count, fixture = count == 0 ? "static-empty" : "settled-working-default", state = "working", settled = true, iterations, warmupIterations, width, height,
                counters = delta, logicalRedrawPixelArea = logicalArea, redrawAreaSource = delta["RedrawPixelArea"].HasValue ? "product logical viewport damage" : "baseline full OnRender viewport area",
                allocatedBytes, wallSeconds, coreAllocatedBytesCurrentThread = coreBytes, viewAllocatedBytesCurrentThread = viewBytes, coreMilliseconds = coreMs, viewMilliseconds = viewMs,
                note = "Fixed update count, no CPU/time gate. Empty fixture removes all furniture and overflow seats, including animated TV. Core and View+dispatcher thread allocation deltas are separate. No forced invalidation, bitmap or PNG encoding. Logical area is not compositor/GPU traffic." });
            source.RootVisual = null; root.Children.Remove(view);
            Console.WriteLine($"Count regression workload complete: {count} agents, {iterations} updates.");
        }
        return 0;
    }
    static double DepthControl(Furniture furniture) => 0;
    static int ChangingTv(string output)
    {
        const int iterations = 300, warmupIterations = 30, width = 720, height = 480;
        long stepTicks = TimeSpan.TicksPerSecond / 3 + 1;
        double stepSeconds = stepTicks / (double)TimeSpan.TicksPerSecond;
        var (assets, layout) = Load();
        foreach (int count in new[] { 0, 12 })
        {
            var office = RetryLayoutBudget(() => new OfficeAnimator(42, assets, layout));
            var agents = Agents(count, Epoch, AgentState.Working); var poses = Settle(office, agents, Epoch);
            var view = new OfficeView(assets, Path.Combine(output, "empty-brand")); view.SetZoom(1); view.Width=width; view.Height=height;
            using var source = new HwndSource(new HwndSourceParameters("Perf changing TV owned offscreen source")
            { PositionX=-30000, PositionY=-30000, Width=width, Height=height, WindowStyle=unchecked((int)0x80000000), ExtendedWindowStyle=0x08000080 });
            var root = new ContainerVisual(); root.Children.Add(view); source.RootVisual=root;
            view.Measure(new(width,height)); view.Arrange(new(0,0,width,height));
            Action drain = () => { };
            for (int i=0;i<warmupIterations;i++)
            {
                var now=Epoch.AddTicks(i*stepTicks); poses=office.Update(agents,now,stepSeconds);
                view.Update(office.Layout,poses,now,office.FurnitureSprites); view.UpdateLayout(); view.Dispatcher.Invoke(drain,DispatcherPriority.ApplicationIdle);
            }
            var coreTimes=new double[iterations]; var coreBytes=new long[iterations];
            var viewTimes=new double[iterations]; var viewBytes=new long[iterations];
            var dispatchTimes=new double[iterations]; var dispatchBytes=new long[iterations];
            var before=Counters(null,view,office);
            for (int i=0;i<iterations;i++)
            {
                var now=Epoch.AddTicks((i+warmupIterations)*stepTicks);
                long allocated=GC.GetAllocatedBytesForCurrentThread(), start=Stopwatch.GetTimestamp();
                poses=office.Update(agents,now,stepSeconds);
                coreTimes[i]=Stopwatch.GetElapsedTime(start).TotalMilliseconds; coreBytes[i]=GC.GetAllocatedBytesForCurrentThread()-allocated;
                allocated=GC.GetAllocatedBytesForCurrentThread(); start=Stopwatch.GetTimestamp();
                view.Update(office.Layout,poses,now,office.FurnitureSprites);
                viewTimes[i]=Stopwatch.GetElapsedTime(start).TotalMilliseconds; viewBytes[i]=GC.GetAllocatedBytesForCurrentThread()-allocated;
                allocated=GC.GetAllocatedBytesForCurrentThread(); start=Stopwatch.GetTimestamp();
                view.UpdateLayout(); view.Dispatcher.Invoke(drain,DispatcherPriority.ApplicationIdle);
                dispatchTimes[i]=Stopwatch.GetElapsedTime(start).TotalMilliseconds; dispatchBytes[i]=GC.GetAllocatedBytesForCurrentThread()-allocated;
            }
            object Row(string phase, double[] times, long[] bytes) => new { phase,iterations,warmupIterations,milliseconds=times,allocatedBytesCurrentThread=bytes,
                medianMs=Median(times),medianAllocatedBytes=Median(bytes.Select(b=>(double)b)) };
            var delta=Difference(before,Counters(null,view,office));
            var depth=typeof(OfficeView).GetMethod("FurnitureDepth",BindingFlags.Instance|BindingFlags.NonPublic)!;
            var control=typeof(Entry).GetMethod(nameof(DepthControl),BindingFlags.Static|BindingFlags.NonPublic)!;
            object[] depthArgs=new object[1]; var furniture=office.Layout.Furniture;
            var rows=new object[] { Row("Core.Update.changingTV",coreTimes,coreBytes),Row("View.Update.changingTV",viewTimes,viewBytes),Row("View.Dispatcher.changingTV",dispatchTimes,dispatchBytes),
                MeasurePhase("reflection.FurnitureDepth.control.fullOffice",()=> { foreach(var item in furniture) { depthArgs[0]=item; control.Invoke(null,depthArgs); } },300,"Same reused argument array, furniture enumeration, double return boxing and reflection signature; returns zero without geometry work."),
                MeasurePhase("View.FurnitureDepth.fullOffice",()=> { foreach(var item in furniture) { depthArgs[0]=item; depth.Invoke(view,depthArgs); } },300,"One full furniture pass per sample. Reused invocation arguments; includes reflection and double return boxing. Control is separate, not subtracted.") };
            Write(Path.Combine(output,$"changing-{count}.json"),new { count,fixture="default-office-changing-TV",state="settled-working",furnitureCount=furniture.Count,logicalHz=1/stepSeconds,
                iterations,warmupIterations,width,height,counters=delta,rows,note="Built-in TV preserved. Direct Core.Update and View.Update allocation brackets are separate from dispatcher work. Persistent hidden HWND; no bitmap/PNG capture or input. FurnitureDepth diagnostic follows frame workload on the same settled scene." });
            source.RootVisual=null; root.Children.Remove(view);
            Console.WriteLine($"Changing TV/depth phases complete: {count} agents, {furniture.Count} furniture.");
        }
        return 0;
    }
    static int Startup(Application app, string output)
    {
        using var storage = new AppStorage(true, Path.Combine(output, "isolated-app"));
        var clock = Stopwatch.StartNew();
        var loader = typeof(AppProgram).GetMethod("LoadOfficeAsync", BindingFlags.Static | BindingFlags.NonPublic)!;
        var loaded = RetryLayoutBudget(() => ((System.Threading.Tasks.Task<(AssetPack Assets, OfficeLayout Layout, OfficeAnimator Office, IReadOnlyDictionary<string, BitmapSource> Sheets)>)
            loader.Invoke(null, new object?[] { null, null, storage })!).GetAwaiter().GetResult());
        double loadMs = clock.Elapsed.TotalMilliseconds; int decodedAtLoad = loaded.Sheets.Count;
        var settings = new Settings { Mode = "normal", Borderless = true, Zoom = 1, Width = 720, Height = 480 };
        var window = RetryLayoutBudget(() => new OfficeWindow(false, settings, 0, loaded.Assets, loaded.Layout, true, loaded.Office, storage: storage, decodedSheets: loaded.Sheets));
        double constructorMs = clock.Elapsed.TotalMilliseconds - loadMs;
        var view = (OfficeView)Field(window, "view"); int decodedAfterConstructor = view.DecodedSheets.Count;
        window.Width = 720; window.Height = 480; window.Left = -30000; window.Top = -30000;
        window.WindowStartupLocation = WindowStartupLocation.Manual; window.ShowActivated = false; window.ShowInTaskbar = false;
        bool rendered = false;
        window.ContentRendered += (_, _) =>
        {
            if (rendered) return; rendered = true;
            double firstContentRenderedMs = clock.Elapsed.TotalMilliseconds;
            using var process = Process.GetCurrentProcess();
            Write(Path.Combine(output, "startup.json"), new { loadOfficeAsyncMs = loadMs, constructorMs, firstContentRenderedMs,
                processStartToFirstContentRenderedMs = (DateTime.UtcNow - process.StartTime.ToUniversalTime()).TotalMilliseconds,
                decodedAtLoad, decodedAfterConstructor, decodedAtFirstFrame = view.DecodedSheets.Count, startupRetries,
                mode = "normal", offscreen = true, width = window.Width, height = window.Height, counters = Counters(window, view, loaded.Office), memory = Memory(),
                note = "Fresh harness process calling product LoadOfficeAsync and OfficeWindow, using QA isolation and an owned nonactivating normal HWND. ContentRendered is an offscreen first-render marker, not visible desktop latency. LoadOfficeAsync uses the product lazy/eager strategy unchanged; no explicit DecodeSheets call. No 1.5-second visible-startup claim." });
            window.Close();
        };
        int result = app.Run(window);
        return rendered ? result : 1;
    }
    static int Live(Application app, string output, int count, string mode, double warmupSeconds, double sampleSeconds, bool activities = false, bool pollEnabled = false, bool observer = false, bool threadCpu = false, bool external = false, bool scene36 = false)
    {
        if (mode is not ("normal" or "topmost" or "transparent") || count < 0 || count > 24 || warmupSeconds < 0 || sampleSeconds < 5 || warmupSeconds + sampleSeconds > 660)
            throw new ArgumentException("Bounded owned offscreen windows only.");
        if (pollEnabled && (count != 0 || activities)) throw new ArgumentException("Real empty-home polling requires zero fixture agents.");
        if (observer && (count != 0 || activities || pollEnabled)) throw new ArgumentException("Observer control requires zero agents and no polling.");
        var (assets, layout) = Load(); var now = DateTimeOffset.UtcNow;
        var office = RetryLayoutBudget(() => new OfficeAnimator(42, assets, layout)); var agents = Agents(count, now, activities ? AgentState.Idle : AgentState.Working);
        if (scene36)
        {
            var epoch = now.AddSeconds(-36);
            for (int i = 0; i <= 540; i++)
            {
                var seconds = -18 + i * .1;
                agents = DemoScenario.Agents(epoch, seconds).ToArray();
                office.Update(agents, epoch.AddSeconds(seconds), .1);
            }
            count = agents.Length;
        }
        else if (activities)
            for (int i = 0; i <= 900; i++) office.Update(agents, now.AddSeconds(-90 + i * .1), .1);
        else _ = Settle(office, agents, now);
        var settings = new Settings { Mode = mode == "topmost" ? "topmost" : "normal", TransparentBackground = mode == "transparent", Borderless = true, Zoom = 1, Width = 720, Height = 480 };
        using var storage = new AppStorage(true, Path.Combine(output, "isolated-app"));
        Directory.CreateDirectory(storage.Home);
        var window = RetryLayoutBudget(() => new OfficeWindow(false, settings, 0, assets, layout, true, office, agents, storage: storage));
        var tracker = (AgentTracker)Field(window, "tracker");
        if (!pollEnabled) SetField(window, "nextPoll", DateTimeOffset.MaxValue);
        SetField(window, "settings", settings);
        SetField(window, "modes", new WindowModes(window, false, (nint?)0));
        window.Width = 720; window.Height = 480; window.Left = -30000; window.Top = -30000;
        window.WindowStartupLocation = WindowStartupLocation.Manual; window.ShowActivated = false; window.ShowInTaskbar = false;
        Window activeWindow = window;
        if (observer)
        {
            activeWindow = new Window { Width=720, Height=480, Left=-30000, Top=-30000, WindowStartupLocation=WindowStartupLocation.Manual,
                ShowActivated=false, ShowInTaskbar=false, WindowStyle=WindowStyle.None, Background=Brushes.Black,
                Content=new System.Windows.Controls.Border { Background=Brushes.Black }, AllowsTransparency=mode=="transparent" };
            var blankModes = new WindowModes(activeWindow, false, (nint?)0);
            activeWindow.SourceInitialized += (_,_) => blankModes.Attach();
            activeWindow.ContentRendered += (_,_) => blankModes.Apply(mode, false, true, mode=="transparent");
        }
        var view = (OfficeView)Field(window, "view"); long ticks = 0;
        long movingTicks=0; int maximumMoving=0;
        var productTimers = window.GetType().GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .Where(f => f.FieldType == typeof(DispatcherTimer) && f.GetValue(window) is DispatcherTimer).ToArray();
        foreach (var productTimer in productTimers.Select(f => (DispatcherTimer)f.GetValue(window)!).Distinct())
            productTimer.Tick += (_, _) => { ticks++; if (external && (activities || scene36)) { int moving=0; foreach(var agent in view.Agents) if(agent.Pose.Visible && agent.Pose.Action is "walking" or "entering" or "exiting") moving++; if(moving>0) movingTicks++; maximumMoving=Math.Max(maximumMoving,moving); } };
        var clock = new Stopwatch(); var samples = new List<object>(); var sampleTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        var startupDeadline = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
        startupDeadline.Tick += (_, _) =>
        {
            startupDeadline.Stop();
            Write(Path.Combine(output,"startup-failure.json"),new { reason="No offscreen ContentRendered within 60 seconds", observer, mode });
            activeWindow.Close();
        };
        using var process = Process.GetCurrentProcess();
        int uiThreadId = (int)GetCurrentThreadId(); var threadNames = new Dictionary<int,string>(); var lastThreads = new Dictionary<int,double>();
        bool started = false; double lastWall = 0, lastCpu = 0; long lastAllocated = 0, lastTicks = 0;
        double? lastUiCpu = null;
        var lastCounters = new Dictionary<string, double?>(); var gcStart = new int[3];
        var warmTimer=new DispatcherTimer { Interval=TimeSpan.FromSeconds(Math.Max(.001,warmupSeconds)) };
        var endTimer=new DispatcherTimer { Interval=TimeSpan.FromSeconds(sampleSeconds) };
        double? externalUiStart=null; long externalMovingStart=0;
        string fixture=observer ? "observer-only-blank" : pollEnabled ? "empty-home-real-polling" : scene36 ? "demo-p36-fixed-inputs" : activities ? "native-idle-activities" : "default-office-including-animated-TV";
        warmTimer.Tick += (_,_) =>
        {
            warmTimer.Stop(); started=true; lastCounters=Counters(window,view,office); lastTicks=ticks; externalMovingStart=movingTicks; maximumMoving=0;
            for(int i=0;i<3;i++) gcStart[i]=GC.CollectionCount(i);
            lastWall=clock.Elapsed.TotalSeconds; lastCpu=process.TotalProcessorTime.TotalSeconds; externalUiStart=UiCpu(); lastAllocated=GC.GetTotalAllocatedBytes(true);
            Write(Path.Combine(output,"warm.json"),new { pid=Environment.ProcessId,utc=DateTimeOffset.UtcNow,seconds=lastWall,cpuSeconds=lastCpu,uiCpuSeconds=externalUiStart,uiThreadId,trackerLastPollAt=pollEnabled ? (DateTimeOffset)Field(tracker,"lastPoll") : (DateTimeOffset?)null });
            endTimer.Start();
        };
        endTimer.Tick += (_,_) => { endTimer.Stop(); activeWindow.Close(); };
        sampleTimer.Tick += (_, _) =>
        {
            long observerStart=Stopwatch.GetTimestamp(), observerAllocated=GC.GetAllocatedBytesForCurrentThread();
            double? observerUiCpu=UiCpu(); var observerCost=new SamplerCost();
            double wall = clock.Elapsed.TotalSeconds; process.Refresh();
            double cpu = process.TotalProcessorTime.TotalSeconds; long allocated = GC.GetTotalAllocatedBytes(true);
            long counterStart=Stopwatch.GetTimestamp();
            var counters = Counters(window, view, office);
            observerCost.counterMilliseconds=Stopwatch.GetElapsedTime(counterStart).TotalMilliseconds;
            long memoryStart=Stopwatch.GetTimestamp(); long workingSetBytes=process.WorkingSet64, privateBytes=process.PrivateMemorySize64;
            observerCost.memoryMilliseconds=Stopwatch.GetElapsedTime(memoryStart).TotalMilliseconds;
            long threadStart=Stopwatch.GetTimestamp(); var currentThreads=threadCpu ? ThreadCpu(process,threadNames) : null;
            observerCost.threadMilliseconds=Stopwatch.GetElapsedTime(threadStart).TotalMilliseconds;
            bool close=false;
            if (!started && wall >= warmupSeconds)
            {
                started = true; lastWall = wall; lastCpu = cpu; lastAllocated = allocated; lastTicks = ticks; lastCounters = counters;
                for (int i = 0; i < 3; i++) gcStart[i] = GC.CollectionCount(i);
                lastUiCpu=observerUiCpu; if (currentThreads != null) lastThreads=currentThreads;
            }
            else if (started)
            {
                double interval = wall - lastWall, cpuSeconds = cpu - lastCpu;
                var delta = Difference(lastCounters, counters);
                int movingVisibleCount = view.Agents.Count(a => a.Pose.Visible && a.Pose.Action is "walking" or "entering" or "exiting");
                var movingProperty = office.GetType().GetProperty("HasMovingCharacters");
                samples.Add(new { seconds = wall, wallSeconds = interval, cpuSeconds, logicalProcessors = Environment.ProcessorCount,
                    cpuOneCorePercent = 100 * cpuSeconds / interval, cpuMachinePercent = 100 * cpuSeconds / interval / Environment.ProcessorCount,
                    allocatedBytes = allocated - lastAllocated, appScheduledWakeups = ticks - lastTicks, counters = delta,
                    logicalRedrawPixelArea = delta["RedrawPixelArea"] ?? delta["SceneRenderCount"] * view.ActualWidth * view.ActualHeight,
                    redrawAreaSource = delta["RedrawPixelArea"].HasValue ? "product logical viewport damage" : "baseline full OnRender viewport area",
                    movingVisibleCount, hasMovingCharacters = movingProperty == null ? movingVisibleCount > 0 : (bool)movingProperty.GetValue(office)!,
                    movingObservationSource = movingProperty == null ? "visible pose actions" : "Core.HasMovingCharacters",
                    trackerLastPollAt = pollEnabled ? (DateTimeOffset)Field(tracker, "lastPoll") : (DateTimeOffset?)null,
                    workingSetBytes, privateBytes, sampler=observerCost,
                    uiCpuSeconds=observerUiCpu-lastUiCpu, otherThreadCpuSeconds=cpuSeconds-(observerUiCpu-lastUiCpu),
                    threads=currentThreads?.Where(t=>lastThreads.ContainsKey(t.Key)).Select(t=>new { id=t.Key, description=threadNames.GetValueOrDefault(t.Key,""), isUi=t.Key==uiThreadId, cpuSeconds=t.Value-lastThreads[t.Key] }).ToArray() });
                lastWall = wall; lastCpu = cpu; lastAllocated = allocated; lastTicks = ticks; lastCounters = counters;
                lastUiCpu=observerUiCpu; if (currentThreads != null) lastThreads=currentThreads;
                close=wall >= warmupSeconds + sampleSeconds;
            }
            observerCost.milliseconds=Stopwatch.GetElapsedTime(observerStart).TotalMilliseconds;
            observerCost.allocatedBytesCurrentThread=GC.GetAllocatedBytesForCurrentThread()-observerAllocated;
            observerCost.cpuSecondsUi=UiCpu()-observerUiCpu;
            if (close) activeWindow.Close();
        };
        activeWindow.ContentRendered += (_, _) =>
        {
            if (clock.IsRunning) return;
            startupDeadline.Stop();
            var hwnd = new WindowInteropHelper(activeWindow).Handle;
            Write(Path.Combine(output, "ready.json"), new { pid = Environment.ProcessId, count, mode, fixture = pollEnabled ? "empty-home-real-polling" : activities ? "native-idle-activities" : "default-office-including-animated-TV", pollEnabled, state = activities ? "idle" : "working", settled = !activities, startupRetries,
                observer, threadCpuEnabled=threadCpu, uiThreadId, nativeTopmost = (GetWindowLong(hwnd, -20) & 8) != 0, allowsTransparency = activeWindow.AllowsTransparency, noActivate = (GetWindowLong(hwnd, -20) & 0x08000000) != 0,
                fakeHome = storage.Home, observedProductTimers = productTimers.Select(f => f.Name).ToArray(), width = window.Width, height = window.Height, viewWidth = view.ActualWidth, viewHeight = view.ActualHeight, counters = Counters(window, view, office) });
            clock.Start(); if(external) warmTimer.Start(); else sampleTimer.Start();
        };
        activeWindow.Closed += (_, _) =>
        {
            startupDeadline.Stop();
            sampleTimer.Stop();
            warmTimer.Stop(); endTimer.Stop();
            if (!clock.IsRunning) return;
            if(external)
            {
                double elapsed=clock.Elapsed.TotalSeconds-lastWall, cpuSeconds=process.TotalProcessorTime.TotalSeconds-lastCpu;
                double? uiCpuSeconds=UiCpu()-externalUiStart;
                long allocatedBytes=GC.GetTotalAllocatedBytes(true)-lastAllocated; var delta=Difference(lastCounters,Counters(window,view,office));
                Write(Path.Combine(output,"external-target.json"),new { count,mode,fixture,pollEnabled,observer,activities,warmupSeconds,sampleSeconds,logicalProcessors=Environment.ProcessorCount,
                    measuredWallSeconds=elapsed,cpuSeconds,cpuOneCorePercent=100*cpuSeconds/elapsed,cpuMachinePercent=100*cpuSeconds/elapsed/Environment.ProcessorCount,
                    uiThreadId,uiCpuSeconds,otherThreadCpuSeconds=cpuSeconds-uiCpuSeconds,cpuEnd=lastCpu+cpuSeconds,
                    allocatedBytes,appScheduledWakeups=ticks-lastTicks,counters=delta,logicalRedrawPixelArea=delta["RedrawPixelArea"] ?? delta["SceneRenderCount"]*view.ActualWidth*view.ActualHeight,
                    movingAnimationTicks=movingTicks-externalMovingStart,maximumMovingVisible=maximumMoving,trackerLastPollAt=pollEnabled ? (DateTimeOffset)Field(tracker,"lastPoll") : (DateTimeOffset?)null,
                    gcCollections=Enumerable.Range(0,3).Select(i=>GC.CollectionCount(i)-gcStart[i]).ToArray(),payloadBytes=tracker.BytesRead,
                    note="Target has no periodic observer. Two finite measurement callbacks only; native process/UI CPU and allocation/counter deltas at bounds. Start marker serialization is included. External collector performs one-second CPU/memory/thread reads in its own process. No observer-cost subtraction. Non-UI CPU is not exclusively WPF render. Activities record moving ticks, not moving-only CPU intervals." });
                return;
            }
            Write(Path.Combine(output, "live.json"), new { count, mode, fixture = observer ? "observer-only-blank" : pollEnabled ? "empty-home-real-polling" : activities ? "native-idle-activities" : "default-office-including-animated-TV", observer, threadCpuEnabled=threadCpu, uiThreadId, pollEnabled, state = activities ? "idle" : "working", settled = !activities, warmupSeconds, sampleSeconds, actualWallSeconds = clock.Elapsed.TotalSeconds,
                cpuFormula = "cpuOneCorePercent = 100 * cpuSeconds / wallSeconds; cpuMachinePercent = cpuOneCorePercent / logicalProcessors", logicalProcessors = Environment.ProcessorCount,
                samples, observedProductTimers = productTimers.Select(f => f.Name).ToArray(), gcCollections = Enumerable.Range(0, 3).Select(i => GC.CollectionCount(i) - gcStart[i]).ToArray(), payloadBytes = tracker.BytesRead,
                attributionNote="Sampler callback wall time, UI CPU and current-thread allocations are observed, not assumed small. Optional ProcessThreads sampling adds its own cost. UI ID is known; descriptions are OS-reported. Other CPU includes all non-UI threads; unnamed threads are not identified as WPF render. Thread/process reads occur at different instants; exited threads and rounding leave a residual.",
                note = observer ? "Blank owned offscreen WPF HWND, same counter/memory sampler and native window mode. Product OfficeWindow is constructed but never shown or loaded, so none of its timers start. No animation, polling or input."
                    : pollEnabled ? "Zero agents, explicit empty temporary tracker home, actual product PollAsync enabled. Private tracker lastPoll timestamp is observed; no fabricated poll count. App-scheduled timer callbacks, not OS/ETW wakeups. Warmup discarded; default animated TV preserved."
                    : activities ? "Native idle/leisure after 90 logical seconds, then discarded live warmup. Mixed activity; movingVisibleCount is observed at the interval endpoint. Product Animate/WPF; tracker polling disabled; no input. Allocation includes sampler overhead; logical damage is not compositor traffic."
                    : "Warmup discarded. Fixed settled working agents. Product Animate and WPF rendering; tracker polling disabled to preserve fixture. Allocation includes sampler overhead. RedrawPixelArea is product logical damage area, not compositor/GPU traffic. Missing counters are null." });
        };
        if (observer) activeWindow.Closed += (_,_) => window.Close();
        startupDeadline.Start();
        int result=app.Run(activeWindow);
        return clock.IsRunning ? result : 1;
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
        Write(Path.Combine(output, $"pack-{Environment.ProcessId}.json"), new { loadMs, decodeMs, firstFrameMs = Stopwatch.GetElapsedTime(frameStart).TotalMilliseconds, inProcessFirstFrameMs = watch.Elapsed.TotalMilliseconds, renderTier = RenderCapability.Tier >> 16, embeddedResources = typeof(AssetPack).Assembly.GetManifestResourceNames().Count(n => n.StartsWith("Officelings.Core.Pack.") && n.EndsWith(".png")), decodedSheets = sheets.Count, compressedBytes = assets.Images.Sum(k => (long)k.Value.Length), decodedPixelBytes = pixels, before, loaded, decoded, afterFrame = Memory(), warnings = assets.Warnings });
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
