using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Officelings.Core;
using Forms = System.Windows.Forms;

namespace Officelings.App;

internal sealed partial record Settings
{
    public string View { get; init; } = "flat";
    public string Mode { get; init; } = "normal";
    public string TagMode { get; init; } = "hover";
    public AmbientSettings Ambient { get; init; } = new();
    public string SeasonDecor { get; init; } = "off";
    public string Pets { get; init; } = "on";
    public bool ClickThrough { get; init; }
    public double Zoom { get; init; }
    public double Width { get; init; } = 720;
    public double Height { get; init; } = 480;
    public double? Left { get; init; }
    public double? Top { get; init; }
    public bool Borderless { get; init; }
    public bool TransparentBackground { get; init; }
    public double Opacity { get; init; } = 1;
    public double WorkingSeconds { get; init; } = 120;
    private double? waitingMinutes;
    private double legacyGoneMinutes = 30;
    public double LeaveAfterIdleMinutes { get; init; } = 5;
    public double LeaveAfterWaitingMinutes { get => waitingMinutes ?? legacyGoneMinutes; init => waitingMinutes = value; }
    public double GoneMinutes { get => LeaveAfterWaitingMinutes; init => legacyGoneMinutes = value; }

    internal Settings Normalize()
    {
        static double Number(double value, double fallback, double min, double max) => double.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
        var working = Number(WorkingSeconds, 120, 1, 86400);
        var waiting = Math.Max(Number(LeaveAfterWaitingMinutes, 30, 1, 10080), (working + 1) / 60);
        return (this with { View = View == "iso" ? "iso" : "flat", Mode = Mode is "normal" or "topmost" or "desktop" ? Mode : "normal",
            TagMode = NameTagHover.Normalize(TagMode),
            PrivacyMode = PrivacyMode == null
                ? PrivacyModes.Value(PrivacyModes.Resolve(null, null, Environment.GetEnvironmentVariable("OFFICELINGS_PRIVACY")))
                : PrivacyModes.SharingValue(PrivacyMode),
            Ambient = (Ambient ?? new()).Normalize(), SeasonDecor = SeasonDecor == "auto" ? "auto" : "off",
            Pets = Pets == "off" ? "off" : "on",
            Zoom = Number(Zoom, 0, 0, 6), Width = Number(Width, 720, 400, 3000), Height = Number(Height, 480, 300, 2000),
            Left = Left is {} x && double.IsFinite(x) ? Math.Clamp(x, -100000, 100000) : null,
            Top = Top is {} y && double.IsFinite(y) ? Math.Clamp(y, -100000, 100000) : null,
            Opacity = Number(Opacity, 1, .25, 1), WorkingSeconds = working,
            LeaveAfterIdleMinutes = Math.Max(Number(LeaveAfterIdleMinutes, 5, 1, 10080), (working + 1) / 60),
            LeaveAfterWaitingMinutes = waiting, GoneMinutes = waiting }).NormalizeSharing();
    }

    internal static Settings Read(string path)
    {
        try
        {
            using var file = new MemoryStream(FileBoundary.ReadBytes(path, 65536));
            using var json = JsonDocument.Parse(file);
            return ((JsonSerializer.Deserialize<Settings>(json, StateJson.Options) ?? new()) with
                { PrivacyMode = PrivacyModes.ReadSetting(json.RootElement) ?? "full" }).Normalize();
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or JsonException)
        { return new Settings { PrivacyMode = e is FileNotFoundException or DirectoryNotFoundException ? null : "full" }.Normalize(); }
    }
    internal void Write(string path) => StateJson.WriteAtomic(path,JsonSerializer.SerializeToUtf8Bytes(Normalize(),StateJson.Options));
}

internal static partial class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            var demo = args.Contains("--demo"); Settings? initial = null;
            string? viewOverride = null, smoke = null, assetDirectory = null, layoutFile = null, profile = null, verifyOpacity = null, home = null, stateSource = null, privacyOption = null; double quitAfter = 0; var qa = false; var smokeTags=false; var smokeAmbient=false;
            for (var i = 0; i < args.Length; i++)
            {
                string Value() => ++i < args.Length ? args[i] : throw new ArgumentException("Missing option value.");
                double Number() => double.Parse(Value(),CultureInfo.InvariantCulture);
                double Positive() => Number() is var value && double.IsFinite(value) && value > 0 ? value : throw new ArgumentException("Expected a positive finite number.");
                switch (args[i])
                {
                    case "--demo": break;
                    case "--view": viewOverride = ParseView(Value()); break;
                    case "--smoke-test": smoke = Value(); break;
                    case "--smoke-tags": smokeTags = true; break;
                    case "--smoke-ambient": smokeAmbient = true; break;
                    case "--ambient-weather": initial = (initial ?? new()) with { Ambient = new(ParseChoice(Value(), "--ambient-weather", "off", "rain", "snow", "auto")) }; break;
                    case "--season-decor": initial = (initial ?? new()) with { SeasonDecor = ParseChoice(Value(), "--season-decor", "off", "auto") }; break;
                    case "--pets": initial = (initial ?? new()) with { Pets = ParseChoice(Value(), "--pets", "on", "off") }; break;
                    case "--tags": initial = (initial ?? new()) with { TagMode = ParseTags(Value()) }; break;
                    case "--privacy": privacyOption = PrivacyModes.Value(PrivacyModes.Parse(Value())); break;
                    case "--asset-dir": assetDirectory = Value(); break;
                    case "--layout": layoutFile = Value(); break;
                    case "--qa": qa = true; break;
                    case "--home": home = Path.GetFullPath(Value()); break;
                    case "--output":
                    case "--state": stateSource = Path.GetFullPath(Value()); break;
                    case "--profile-idle": profile = Value(); break;
                    case "--verify-opacity": verifyOpacity = Value(); break;
                    case "--zoom": initial = (initial ?? new()) with { Zoom = Number() }; break;
                    case "--mode": initial = (initial ?? new()) with { Mode = Value() }; break;
                    case "--click-through": initial = (initial ?? new()) with { ClickThrough = true }; break;
                    case "--borderless": initial = (initial ?? new()) with { Borderless = true }; break;
                    case "--transparent": initial = (initial ?? new()) with { TransparentBackground = true }; break;
                    case "--opacity": initial = (initial ?? new()) with { Opacity = Number() }; break;
                    case "--working-seconds": initial = (initial ?? new()) with { WorkingSeconds = Positive() }; break;
                    case "--leave-after-idle-minutes": initial = (initial ?? new()) with { LeaveAfterIdleMinutes = Positive() }; break;
                    case "--leave-after-waiting-minutes": initial = (initial ?? new()) with { LeaveAfterWaitingMinutes = Positive() }; break;
                    case "--gone-minutes": initial = (initial ?? new()) with { GoneMinutes = Positive() }; break;
                    case "--quit-after": quitAfter = Number(); break;
                    default: throw new ArgumentException("Usage: Officelings.App [--demo] [--view iso|flat] [--privacy off|tasks|full] [--asset-dir DIR] [--layout FILE] [--zoom 0..6] [--mode normal|topmost|desktop] [--tags hover|icon|always] [--click-through] [--borderless] [--transparent] [--opacity .25..1] [--working-seconds N] [--leave-after-idle-minutes N] [--leave-after-waiting-minutes N] [--gone-minutes N (deprecated)] [--qa --quit-after SECONDS] [--smoke-test OUTPUT_DIR [--smoke-tags]] [--verify-opacity OUTPUT_DIR] [--profile-idle OUTPUT_DIR]");
                }
            }
            using var storage = new AppStorage(demo || qa || profile != null || smoke != null || verifyOpacity != null);
            if (initial != null || privacyOption != null)
                initial = (initial ?? Settings.Read(storage.SettingsPath)) with { PrivacyMode = PrivacyModes.Value(PrivacyModes.Resolve(privacyOption,
                    PrivacyModes.ReadSetting(storage.SettingsPath), Environment.GetEnvironmentVariable("OFFICELINGS_PRIVACY"))) };
            if (home != null && !qa && smoke == null) throw new ArgumentException("--home requires --qa or --smoke-test.");
            if (stateSource != null && smoke == null) throw new ArgumentException("--output/--state requires --smoke-test; pass a tracker output directory or state.json.");
            if (stateSource != null && Directory.Exists(stateSource)) stateSource = Path.Combine(stateSource,"state.json");
            if (smokeTags && smoke==null) throw new ArgumentException("--smoke-tags requires --smoke-test OUTPUT_DIR.");
            if (smokeAmbient && smoke==null) throw new ArgumentException("--smoke-ambient requires --smoke-test OUTPUT_DIR.");
            storage.CheckInput(layoutFile); storage.CheckInput(assetDirectory);
            storage.CheckInput(stateSource);
            if (stateSource != null && assetDirectory == null) assetDirectory = Path.GetDirectoryName(stateSource);
            if (viewOverride != null) initial = (initial ?? (storage.Isolated ? new Settings() : Settings.Read(storage.SettingsPath))) with { View = viewOverride };
            else if (initial != null && !storage.Isolated) initial = initial with { View = Settings.Read(storage.SettingsPath).View };
            var app = new Application();
            if (verifyOpacity != null) return OpacityVerification.Run(verifyOpacity);
            if (initial?.View == "iso" || initial == null && !storage.Isolated && Settings.Read(storage.SettingsPath).View == "iso")
                return IsoApplication.Run(app,demo,initial,quitAfter,qa,storage,home,args.Contains("--asset-dir") ? assetDirectory : null,smoke,smokeTags,layoutFile,stateSource,profile);
            (AssetPack Assets, OfficeLayout Layout) LoadOffice()
            {
                var assets = stateSource != null && assetDirectory != null ? AssetPack.LoadExported(assetDirectory)
                    : AssetPack.LoadDefault(assetDirectory, useUserPack: !storage.Isolated);
                return (assets, LayoutLoader.Load(assets, layoutFile ?? storage.LayoutPath, assets.Warnings));
            }
            if (profile != null) { var loaded = LoadOffice(); return ProfileIdle(profile,loaded.Assets,loaded.Layout,(initial ?? new()).Normalize()); }
            if (smoke != null) { var loaded = LoadOffice(); return smokeAmbient ? AmbientRenders(smoke,loaded.Assets,loaded.Layout,storage.Home) : smokeTags ? TagRenders(smoke,loaded.Assets,loaded.Layout) : Smoke(smoke,demo,loaded.Assets,loaded.Layout,(initial ?? new()).Normalize(),home ?? storage.Home,stateSource); }
            if (qa && (!double.IsFinite(quitAfter) || quitAfter<=0 || quitAfter>60)) throw new ArgumentException("--qa requires --quit-after in (0,60].");
            // Parsing user JSON and validating PNGs never occupies the dispatcher.
            app.Startup += async (_, _) =>
            {
                try
                {
                    var loaded = await LoadOfficeAsync(assetDirectory, layoutFile, storage);
                    new OfficeWindow(demo,initial,quitAfter,loaded.Assets,loaded.Layout,qa,liveOffice: loaded.Office,storage: storage,decodedSheets: loaded.Sheets,trackerHome: home).Show();
                }
                catch (Exception e) { Console.Error.WriteLine(PrivacyModes.Diagnostic(e, PrivacyModes.Resolve(privacyOption, initial?.PrivacyMode ?? PrivacyModes.ReadSetting(storage.SettingsPath), Environment.GetEnvironmentVariable("OFFICELINGS_PRIVACY")))); app.Shutdown(1); }
            };
            return app.Run();
        }
        catch (Exception e)
        {
            var message = PrivacyModes.Diagnostic(e, StartupPrivacy(args));
            if (args.Length > 0) Console.Error.WriteLine(message);
            else MessageBox.Show(message,"Officelings",MessageBoxButton.OK,MessageBoxImage.Error);
            return 1;
        }
    }

    internal static string ParseView(string value) => value is "iso" or "flat" ? value : throw new ArgumentException("--view must be iso or flat.");
    internal static string ParseTags(string value) => value is "hover" or "icon" or "always" ? value : throw new ArgumentException("--tags must be hover, icon or always.");
    private static int TagRenders(string output,AssetPack assets,OfficeLayout layout)
    {
        output=StateJson.OutputPath(output); Directory.CreateDirectory(output);
        var epoch=DateTimeOffset.Parse("2026-10-04T00:00:00Z"); var now=epoch.AddSeconds(140);
        var office=new OfficeAnimator(42,assets,layout);
        var agents=Enumerable.Range(0,12).Select(i=>new AgentInfo($"tags:{i:00}",(Provider)(i%3),i>=3 ? $"tags:{i%3:00}" : null,i>=3,
            $"Agent {i+1}",i<3 ? "Lead" : "Worker","demo-model","C:/Demo/Office","Fabricated tag mode render",null,null,
            i%4==0 ? AgentState.Idle : AgentState.Working,epoch,epoch,false)).ToArray();
        IReadOnlyList<AgentView> poses=Array.Empty<AgentView>();
        for(var frame=0;frame<1400;frame++) poses=office.Update(agents,epoch.AddMilliseconds(frame*100),.1);
        var results=new List<object>();
        foreach(var mode in new[] { "hover","icon","always" })
        {
            var view=new OfficeView(assets,Path.Combine(output,"empty-brand")) { TagMode=mode }; view.SetZoom(2); view.Update(office.Layout,poses,now,office.FurnitureSprites);
            var width=office.Layout.WidthTiles*32+32; var height=office.Layout.HeightTiles*32+32;
            Render(view,width,height,Path.Combine(output,$"tags-{mode}.png"));
            if(view.RenderedAgentTags!=(mode=="always" ? 12 : 0)) throw new InvalidOperationException("Tag mode baseline failed.");
            var baselineTags=view.RenderedAgentTags; var baselineDesks=view.RenderedReservedTags;
            var agent=poses.First(a=>a.Pose.Visible && a.Agent.State==AgentState.Working); var point=view.AgentPoint(agent);
            view.SimulateHover(point,now); view.SimulateHover(point,now.AddMilliseconds(120));
            Render(view,width,height,Path.Combine(output,$"tags-{mode}-hover.png"));
            if(view.AgentTagRect(agent.Agent.Id).IsEmpty) throw new InvalidOperationException("Programmatic tag hover failed.");
            results.Add(new { mode,agents=poses.Count,baselineTags,baselineDesks,hoverTags=view.RenderedAgentTags,hoverRect=view.AgentTagRect(agent.Agent.Id) });
        }
        File.WriteAllText(Path.Combine(output,"tags.json"),JsonSerializer.Serialize(results,StateJson.Options));
        File.WriteAllText(Path.Combine(output,"smoke.txt"),"Offscreen tag modes OK; fabricated agents=12; modes=hover,icon,always; programmatic hover=120ms; logBytesRead=0\n");
        return 0;
    }
    private static int Smoke(string output, bool demo, AssetPack assets, OfficeLayout layout,Settings settings,string home,string? stateSource)
    {
        output = StateJson.OutputPath(output,home); Directory.CreateDirectory(output);
        var privacy = demo ? PrivacyMode.Off : PrivacyModes.Parse(settings.PrivacyMode ?? "off");
        using var tracker = new AgentTracker(new() { Home=home, PrivacyMode=privacy }); var office = new OfficeAnimator(42,assets,layout,ActivityCatalog.Load(home,assets.Warnings));
        var now = demo ? DateTimeOffset.Parse("2026-10-03T12:00:00Z") : DateTimeOffset.UtcNow;
        IReadOnlyList<AgentInfo> sources = Array.Empty<AgentInfo>();
        IReadOnlyList<AgentView> views = Array.Empty<AgentView>();
        var tagMode=settings.TagMode;
        var view = new OfficeView(assets,Path.Combine(home,"brand")) { PrivacyMode=privacy, TagMode=tagMode, AmbientSettings=settings.Ambient, SeasonDecor=settings.SeasonDecor, PetsEnabled=settings.Pets=="on" };
        if (stateSource != null)
        {
            if (new FileInfo(stateSource).Length > 4*1024*1024) throw new InvalidDataException("State exceeds 4 MiB.");
            using var input = new FileStream(stateSource,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
            var snapshot = JsonSerializer.Deserialize<StateSnapshot>(input,StateJson.Options) ?? throw new InvalidDataException("Empty state.");
            snapshot = PrivateSnapshot(snapshot, privacy); view.PrivacyMode=snapshot.Privacy;
            layout = snapshot.Office; LayoutLoader.Validate(layout,assets);
            view.Update(layout,snapshot.Agents,snapshot.GeneratedAt,snapshot.FurnitureSprites);
            foreach (var zoom in new[] { 1,2,3 })
            { view.SetZoom(zoom); Render(view,layout.WidthTiles*16*zoom+32,layout.HeightTiles*16*zoom+32,Path.Combine(output,$"office-{zoom}x.png")); }
            File.WriteAllText(Path.Combine(output,"smoke.txt"),$"Offscreen WPF snapshot OK; agents={snapshot.Agents.Count}; sourceSequence={snapshot.Sequence}; logBytesRead=0\n");
            return 0;
        }
        if (!demo) sources = tracker.Poll(now);
        for (var frame = 0; frame <= 550; frame++)
        {
            var seconds=frame*.1;
            if (demo) sources=DemoScenario.Agents(now,seconds);
            views=office.Update(sources,now.AddSeconds(seconds),.1);
            if (demo && frame is 0 or 120 or 240 or 420 or 550)
            { view.Update(office.Layout,views,now.AddSeconds(seconds),office.FurnitureSprites); view.SetZoom(2); Render(view,1100,780,Path.Combine(output,$"phase-{frame/10:00}.png")); }
        }
        view.Update(office.Layout,views,now.AddSeconds(55),office.FurnitureSprites);
        foreach (var zoom in new[] { 1,2,3 })
        { view.SetZoom(zoom); Render(view,office.Layout.WidthTiles*16*zoom+32,office.Layout.HeightTiles*16*zoom+32,Path.Combine(output,$"office-{zoom}x.png")); }
        view.SetZoom(3); Render(view,520,340,Path.Combine(output,"office-cropped.png"));
        if (views.Count>0)
        {
            var before=view.AgentPoint(views[0]); view.PanBy(new(70,40));
            Render(view,520,340,Path.Combine(output,"office-panned.png"));
            if (view.AgentPoint(views[0])-before != new Vector(70,40)) throw new InvalidOperationException("Programmatic camera pan failed.");
        }
        view.SetZoom(0); Render(view,700,480,Path.Combine(output,"office.png"));
        var hover=views.FirstOrDefault(v=>v.Pose.Visible && view.AgentPoint(v).X>0);
        if (hover != null)
        {
            var point=view.AgentPoint(hover);
            if (view.HitAgent(point) == null) throw new InvalidOperationException("Programmatic sprite hit test failed.");
            view.SimulateHover(point,now.AddSeconds(55)); view.SimulateHover(point,now.AddSeconds(55.42)); Render(view,700,480,Path.Combine(output,"tooltip.png"));
            File.WriteAllText(Path.Combine(output,"tooltip.txt"),view.DebugTooltipText);
            view.SimulateHover(null,now.AddSeconds(56)); view.AdvanceHover(now.AddSeconds(56.26));
            if (view.DebugTooltipText != null) throw new InvalidOperationException("Tooltip did not clear.");
        }
        view.TransparentBackground=true; Render(view,700,480,Path.Combine(output,"transparent.png"));
        var manifest=assets.Export(output);
        StateJson.WriteAtomic(Path.Combine(output,"layout.json"),JsonSerializer.SerializeToUtf8Bytes(office.Layout,StateJson.Options));
        StateJson.WriteAtomic(Path.Combine(output,"state.json"),new StateSnapshot(StateJson.SchemaVersion,now.AddSeconds(55),1,SpriteAtlas.Info,office.Layout,views,PrivacyModes.Diagnostics(tracker.Warnings.Concat(assets.Warnings).ToArray(),privacy),manifest,view.ResolvedFurnitureSprites,view.AmbientState,view.Pets,privacy));
        File.WriteAllText(Path.Combine(output,"smoke.txt"),$"Offscreen WPF render OK; demo={demo}; agents={sources.Count}; tagMode={tagMode}; privacy={PrivacyModes.Value(privacy)}; zooms=1,2,3; hover={hover != null}; logBytesRead={tracker.BytesRead}\n"+string.Join("\n",PrivacyModes.Diagnostics(assets.Warnings.ToArray(),privacy)));
        if(demo) { ReviewRenders(output,assets,layout,tagMode); DepartureRenders(output,assets,layout,tagMode); }
        return 0;
    }

    private static void DepartureRenders(string output, AssetPack assets, OfficeLayout layout, string tagMode)
    {
        using var storage = new AppStorage(true);
        var root = Path.Combine(storage.Home, ".codex", "sessions"); Directory.CreateDirectory(root);
        var path = Path.Combine(root, "rollout-leave-demo.jsonl");
        var epoch = DateTimeOffset.Parse("2026-10-04T00:00:00Z");
        string Event(string type, object payload, DateTimeOffset time) => JsonSerializer.Serialize(new { timestamp = time.ToString("O"), type, payload }) + "\n";
        File.WriteAllText(path, Event("session_meta", new { id = "leave-demo", agent_nickname = "Mira", source = "cli", cwd = "C:/Demo/Office" }, epoch) +
            Event("event_msg", new { type = "task_complete" }, epoch));
        File.SetLastWriteTimeUtc(path, epoch.UtcDateTime);
        using var tracker = new AgentTracker(new() { Home = storage.Home });
        var office = new OfficeAnimator(42, assets, layout);
        var view = new OfficeView(assets, Path.Combine(storage.Home, "brand")) { TagMode = tagMode }; view.SetZoom(2);
        var trace = new List<object>();
        AgentView? last = null; var walkCaptured = false; var doorCaptured = false;
        void Capture(string name, IReadOnlyList<AgentView> views, DateTimeOffset time)
        {
            view.Update(office.Layout, views, time, office.FurnitureSprites);
            Render(view, 1100, 780, Path.Combine(output, name));
        }
        for (var frame = 0; frame <= 3350; frame++)
        {
            var seconds = frame / 10d; var now = epoch.AddSeconds(seconds);
            if (frame == 3350)
            {
                File.AppendAllText(path, Event("event_msg", new { type = "task_started" }, now)); File.SetLastWriteTimeUtc(path, now.UtcDateTime);
                // The scan interval remains three seconds; advance to the next scan for this new event.
                now = epoch.AddSeconds(336);
            }
            var views = office.Update(tracker.Poll(now), now, frame is 3000 or 3350 ? 0 : .1);
            var traveler = views.Single(); var p = traveler.Pose;
            if (frame == 2999) Capture("leave-1-break.png", views, now);
            if (frame == 3000 && last is { } previous && (p.X != previous.Pose.X || p.Y != previous.Pose.Y))
                throw new InvalidOperationException("Departure changed the character's position without a movement step.");
            if (!walkCaptured && traveler.Agent.State == AgentState.Gone && p.Visible &&
                Math.Abs(p.X - office.Layout.Door.X) + Math.Abs(p.Y - office.Layout.Door.Y) > 1 && seconds >= 300.5)
            { Capture("leave-2-walk.png", views, now); walkCaptured = true; }
            if (!doorCaptured && traveler.Agent.State == AgentState.Gone && p.Visible && p.Opacity is > 0 and < 1)
            { Capture("leave-3-door-fade.png", views, now); doorCaptured = true; }
            if (frame == 3350)
            {
                if (last?.Pose.Visible != false || !p.Visible || p.Action != "entering" || p.X != office.Layout.Door.X || p.Y != office.Layout.Door.Y)
                    throw new InvalidOperationException("New activity did not re-enter through the door.");
                Capture("leave-4-return.png", views, now);
            }
            if (seconds >= 299.8 && (frame % 5 == 0 || frame is 2999 or 3000 or 3350))
                trace.Add(new { seconds = (now - epoch).TotalSeconds, state = traveler.Agent.State, p.Action, p.X, p.Y, p.Opacity, p.Visible });
            last = traveler;
        }
        if (!walkCaptured || !doorCaptured) throw new InvalidOperationException("Departure walk/fade was not observed.");
        StateJson.WriteAtomic(Path.Combine(output, "leave-walk.json"), JsonSerializer.SerializeToUtf8Bytes(new
        { fabricated = true, workingSeconds = 120, leaveAfterIdleMinutes = 5, leaveAfterWaitingMinutes = 30, logBytesRead = tracker.BytesRead, trace }, StateJson.Options));
    }

    internal static Task<(AssetPack Assets, OfficeLayout Layout, OfficeAnimator Office, IReadOnlyDictionary<string, BitmapSource> Sheets)> LoadOfficeAsync(string? assetDirectory, string? layoutFile, AppStorage storage) => Task.Run(() =>
    {
        storage.CheckInput(assetDirectory); storage.CheckInput(layoutFile);
        var assets = AssetPack.LoadDefault(assetDirectory, useUserPack: !storage.Isolated);
        var layout = LayoutLoader.Load(assets, layoutFile ?? storage.LayoutPath, assets.Warnings);
        return (assets, layout, new OfficeAnimator(42, assets, layout, ActivityCatalog.Load(storage.Home,assets.Warnings)), (IReadOnlyDictionary<string, BitmapSource>)new Dictionary<string, BitmapSource>());
    });
    internal static void Render(FrameworkElement view,int width,int height,string file)
    {
        var bitmap=RenderBitmap(view,width,height);
        var png=new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream=File.Create(file); png.Save(stream);
    }
    internal static BitmapSource RenderBitmap(FrameworkElement view,int width,int height)
    {
        if (view is IsoView iso) return IsoOffscreenRenderer.RenderBitmap(iso,width,height);
        // A hidden, owned presentation source makes WPF process visual invalidation
        // even for repeated renders at the same size. It is never shown/activated.
        using var source=new HwndSource(new HwndSourceParameters("Officelings offscreen render")
        { PositionX=-30000,PositionY=-30000,Width=width,Height=height,
            WindowStyle=unchecked((int)0x80000000),ExtendedWindowStyle=0x08000080 });
        var root=new ContainerVisual();root.Children.Add(view);source.RootVisual=root;
        view.Width=width; view.Height=height; view.Measure(new(width,height)); view.Arrange(new(0,0,width,height)); view.UpdateLayout();
        view.Dispatcher.Invoke(() => { },DispatcherPriority.ApplicationIdle);
        var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);bitmap.Render(root);bitmap.Freeze();
        source.RootVisual=null;root.Children.Remove(view);return bitmap;
    }
    private static void ReviewRenders(string output,AssetPack assets,OfficeLayout layout,string tagMode)
    {
        var epoch=DateTimeOffset.Parse("2026-10-04T00:00:00Z");
        var deskCount=layout.Furniture.Count(f=>assets.Variant(f).Points.Any(p=>p.Action=="typing"));
        AgentInfo[] Scenario(int count,bool rest) => Enumerable.Range(0,count).Select(i=>new AgentInfo(
            $"review:{i:00}",(Provider)(i%3),i>=3 ? $"review:{i%3:00}" : null,i>=3,
            i==0 ? "Codex worker" : i==1 ? "Claude" : i==2 ? "Antigravity" : i==6 ? "A very long nickname" : $"Worker {i+1}",
            i<3 ? "Lead" : "Worker","demo-model","C:/Demo/Office","Fabricated review session","edit_file","file",
            rest && i%3==0 ? AgentState.Idle : i%5==3 ? AgentState.Thinking : i%5==4 ? AgentState.Waiting : AgentState.Working,epoch,epoch,false)).ToArray();
        foreach(var scenario in new[] { (Name:"normal",Count:deskCount,TakeBreak:false),(Name:"reserved-tags",Count:deskCount,TakeBreak:true),
            (Name:"overflow",Count:deskCount+(layout.OverflowSeats?.Count ?? 0)+3,TakeBreak:false) })
        {
            var office=new OfficeAnimator(42,assets,layout);var agents=Scenario(scenario.Count,scenario.TakeBreak);IReadOnlyList<AgentView> poses=Array.Empty<AgentView>();
            for(var frame=0;frame<1400;frame++) poses=office.Update(agents,epoch.AddSeconds(frame*.1),.1);
            var view=new OfficeView(assets,Path.Combine(output,"empty-brand")) { TagMode=tagMode };view.SetZoom(2);view.Update(office.Layout,poses,epoch.AddSeconds(140),office.FurnitureSprites);
            Render(view,office.Layout.WidthTiles*32+32,office.Layout.HeightTiles*32+32,Path.Combine(output,scenario.Name+".png"));
            if(view.RenderedAgentTags!=(tagMode=="always" ? poses.Count(a=>a.Pose.Visible) : 0)) throw new InvalidOperationException("Incorrect overhead tag mode.");
            if(tagMode=="always" && scenario.Name=="reserved-tags" && view.RenderedReservedTags==0) throw new InvalidOperationException("Away owners need reserved desk tags.");
            if(scenario.Name=="normal" && view.RenderedReservedTags!=0) throw new InvalidOperationException("Working owners must not have duplicated desk tags.");
            if(scenario.Name=="overflow" && (!poses.Any(a=>a.Pose.WorkLocation=="overflow") || !poses.Any(a=>a.Pose.Queued))) throw new InvalidOperationException("Overflow and door queue were not exercised.");
            if(scenario.Name=="normal")
            {
                foreach (var zoom in new[] { 1, 2, 3 })
                { view.SetZoom(zoom); Render(view,office.Layout.WidthTiles*16*zoom+32,office.Layout.HeightTiles*16*zoom+32,Path.Combine(output,$"normal-{zoom}x.png")); }
                view.SetZoom(0);Render(view,400,300,Path.Combine(output,"auto-fit-small.png"));
                view.Width=double.NaN;view.Height=double.NaN;
                var panel=new OfficeEditorPanel(view,assets,office.SourceLayout,Path.Combine(output,"layout.json"));
                var host=new DockPanel { Background=new SolidColorBrush(OfficeView.BackgroundColor) };DockPanel.SetDock(panel,Dock.Left);host.Children.Add(panel);host.Children.Add(view);
                Render(host,1500,840,Path.Combine(output,"editor-ui.png"));panel.Detach();
            }
            StateJson.WriteAtomic(Path.Combine(output,scenario.Name+"-state.json"),new StateSnapshot(StateJson.SchemaVersion,epoch.AddSeconds(140),1,SpriteAtlas.Info,office.Layout,poses,Array.Empty<string>(),assets.Manifest,office.FurnitureSprites));
        }
        var transitionOffice = new OfficeAnimator(42,assets,layout);
        var transitionAgents = Scenario(deskCount,false);
        var transitionView = new OfficeView(assets,Path.Combine(output,"empty-brand")) { TagMode=tagMode }; transitionView.SetZoom(3);
        var captured = new HashSet<string>();
        AgentView? selected = null;
        var tick = 0;
        for (; tick < 2000; tick++)
        {
            var poses = transitionOffice.Update(transitionAgents,epoch.AddMilliseconds(tick*40),.04);
            selected = poses.FirstOrDefault(a=>a.Pose.Facing=="right" && a.Pose.SeatPhase!=null);
            if (selected == null) continue;
            var key = selected.Pose.SeatPhase+"-"+selected.Pose.SeatPhaseFrame;
            if (captured.Add(key))
            {
                transitionView.Update(transitionOffice.Layout,poses,epoch.AddMilliseconds(tick*40),transitionOffice.FurnitureSprites);
                Render(transitionView,layout.WidthTiles*48+32,layout.HeightTiles*48+32,Path.Combine(output,"sit-"+key+".png"));
            }
            if (selected.Pose.SeatPhase == "seated") break;
        }
        if (selected != null)
        {
            transitionAgents = transitionAgents.Select(a=>a.Id==selected.Agent.Id ? a with {State=AgentState.Idle} : a).ToArray();
            for(var i=1;i<=8;i++)
            {
                var poses=transitionOffice.Update(transitionAgents,epoch.AddMilliseconds((tick+i)*40),.04);
                var pose=poses.Single(a=>a.Agent.Id==selected.Agent.Id).Pose;
                var key=pose.SeatPhase+"-"+pose.SeatPhaseFrame;
                if(pose.SeatPhase=="raise" && captured.Add(key))
                {
                    transitionView.Update(transitionOffice.Layout,poses,epoch.AddMilliseconds((tick+i)*40),transitionOffice.FurnitureSprites);
                    Render(transitionView,layout.WidthTiles*48+32,layout.HeightTiles*48+32,Path.Combine(output,"stand-"+key+".png"));
                }
            }
        }
    }
    private static int ProfileIdle(string output,AssetPack assets,OfficeLayout layout,Settings settings)
    {
        output=StateJson.OutputPath(output); Directory.CreateDirectory(output);
        using var profileStorage=new AppStorage(true);
        var now=DateTimeOffset.UtcNow; var office=new OfficeAnimator(42,assets,layout);
        var agents=DemoScenario.Agents(now,30).Take(6).Select(a=>a with { State=AgentState.Idle }).ToArray();
        IReadOnlyList<AgentView> views=Array.Empty<AgentView>();
        for(var i=0;i<300;i++) views=office.Update(agents,now.AddSeconds(i*.1),.1);
        var tagMode=settings.TagMode;
        var view=new OfficeView(assets,Path.Combine(profileStorage.Home,"brand")) { TagMode=tagMode, AmbientSettings=settings.Ambient, SeasonDecor=settings.SeasonDecor, PetsEnabled=settings.Pets=="on" }; view.Update(office.Layout,views,now,office.FurnitureSprites); view.SetZoom(1);
        Render(view,700,480,Path.Combine(output,"idle.png"));
        // Keep one nonactivating offscreen presentation source, just as the live window
        // does. Neither HWND recreation nor per-frame bitmap copies are idle app work.
        using var source=new HwndSource(new HwndSourceParameters("Officelings idle profile")
        { PositionX=-30000,PositionY=-30000,Width=700,Height=480,
            WindowStyle=unchecked((int)0x90000000),ExtendedWindowStyle=0x08000080 });
        source.RootVisual=view;
        view.Measure(new(700,480)); view.Arrange(new(0,0,700,480)); view.UpdateLayout();
        view.Dispatcher.Invoke(() => { },DispatcherPriority.ApplicationIdle);
        using var process=Process.GetCurrentProcess(); var startCpu=process.TotalProcessorTime; var elapsed=Stopwatch.StartNew();
        var frame=new DispatcherFrame(); var ticks=0; var warmupTicks=0; var warmupWall=0d; var warmed=false;
        var previousTime=now.AddSeconds(30);
        using var tracker=new AgentTracker(new() { Home=profileStorage.Home }); var polling=false;
        var timer=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(125) };
        timer.Tick+=async (_,_)=>
        {
            var time=now.AddSeconds(30+elapsed.Elapsed.TotalSeconds);
            view.Update(office.Layout,office.Update(agents,time,(time-previousTime).TotalSeconds),time,office.FurnitureSprites); previousTime=time;
            view.UpdateLayout(); ticks++;
            if (!polling && ticks%8==0) { polling=true; await Task.Run(()=>tracker.Poll(time)); polling=false; }
            var interval=MinimumInterval(office.AnimationInterval,view.AnimationInterval);
            if (interval==TimeSpan.Zero) interval=office.NextUpdateAt==DateTimeOffset.MaxValue ? TimeSpan.FromSeconds(1) : TimeSpan.FromMilliseconds(Math.Max(16,(office.NextUpdateAt-time).TotalMilliseconds));
            timer.Interval=interval;
            if (!warmed && elapsed.Elapsed.TotalSeconds>=5)
            { process.Refresh(); startCpu=process.TotalProcessorTime; warmupWall=elapsed.Elapsed.TotalSeconds; warmupTicks=ticks; warmed=true; }
            if(elapsed.Elapsed.TotalSeconds>=25) { timer.Stop(); frame.Continue=false; }
        };
        timer.Start(); Dispatcher.PushFrame(frame); process.Refresh(); source.RootVisual=null;
        var wall=elapsed.Elapsed.TotalSeconds-warmupWall;
        var cpu=(process.TotalProcessorTime-startCpu).TotalSeconds/wall*100;
        File.WriteAllText(Path.Combine(output,"profile.json"),JsonSerializer.Serialize(new { wallSeconds=wall,warmupSeconds=warmupWall,cpuOneCorePercent=cpu,cpuMachinePercent=cpu/Environment.ProcessorCount,logicalProcessors=Environment.ProcessorCount,ticks=ticks-warmupTicks,tagMode,sceneRenders=view.SceneRenderCount,roomBuilds=view.RoomBuildCount,scenario="six idle/activity agents; adaptive WPF updates in one nonactivating offscreen window; no bitmap capture per frame; empty tracker home; >=5s warmup discarded" },StateJson.Options));
        return cpu<=10 ? 0 : 2;
    }
}

internal static class DemoScenario
{
    public static IReadOnlyList<AgentInfo> Agents(DateTimeOffset epoch, double seconds)
    {
        var now = epoch.AddSeconds(seconds); var result = new List<AgentInfo>();
        string[] leads = ["Mira","Jules","Nova"], workers = ["Pip","Ada","Kai","Wren","Remy","Sol"];
        string[] models = ["demo-codex","demo-claude","demo-antigravity"];
        for (var p = 0; p < 3; p++)
        {
            var state = p == 2 && (int)(seconds/12)%3==0 ? AgentState.Thinking : p == 1 && (int)(seconds/12)%3==1 ? AgentState.Waiting : AgentState.Working;
            result.Add(new($"demo:lead:{p}",(Provider)p,null,false,leads[p],"Team lead",models[p],"C:/Demo/TinyWorkshop",
                "Building a cozy pixel office. These sessions are fabricated.",p==0 ? "exec_command" : p==1 ? "edit_file" : "search",p==0 ? "shell" : p==1 ? "file" : "search",state,now,epoch.AddSeconds(Math.Floor(seconds/12)*12),false));
        }
        for (var i = 0; i < 6; i++)
        {
            var start = i < 2 ? -32-i*4 : 6+(i-2)*5;
            if (seconds < start) continue;
            var age = (seconds-start)%84;
            if (age >= 72) continue;
            var state = age < 16 ? AgentState.Working : age < 20 ? AgentState.Reporting : age < 55 ? AgentState.Idle : AgentState.Gone;
            var since = age < 16 ? 0 : age < 20 ? 16 : age < 55 ? 20 : 55;
            var p = i%3;
            result.Add(new($"demo:worker:{i}",(Provider)p,$"demo:lead:{p}",true,workers[i],i%2==0 ? "Builder" : "Reviewer",models[p],"C:/Demo/TinyWorkshop",
                i%2==0 ? "Adding tiny details to the workshop." : "Reviewing the animation and report hand-off.","read_file","file",state,now,now.AddSeconds(-(age-since)),false));
        }
        return result;
    }
}

internal sealed partial class OfficeWindow : Window
{
    private readonly AppStorage storage;
    private string settingsPath => storage.SettingsPath;
    private Settings settings;
    private readonly bool demo, qa;
    private readonly DateTimeOffset epoch = DateTimeOffset.UtcNow;
    private readonly OfficeView view;
    private readonly DockPanel surface = new();
    private OfficeEditorPanel? editor;
    private OfficeEditor? pendingEditor;
    private readonly AssetPack assets;
    private readonly TextBlock status = new() { Margin = new(16,7,16,7), Foreground = new SolidColorBrush(Color.FromRgb(231,209,172)), FontSize = 11 };
    private readonly TextBlock counts = new() { VerticalAlignment = VerticalAlignment.Center, Foreground = new SolidColorBrush(Color.FromRgb(231,209,172)), FontSize = 11 };
    private readonly AgentTracker tracker;
    private readonly string? trackerHome;
    private readonly OfficeAnimator office;
    private readonly WindowModes modes;
    private Forms.NotifyIcon? tray;
    private readonly DispatcherTimer timer;
    private readonly DispatcherTimer maintenance, hoverTimer;
    private IReadOnlyList<AgentInfo> agents = Array.Empty<AgentInfo>();
    private DateTimeOffset previous = DateTimeOffset.UtcNow, nextPoll, nextHostCheck, saveAfter;
    private bool polling, closed;
    private string warning = "";
    private bool animationSuspended;
    private DateTimeOffset nextMaintenance;
    internal long AnimationTickCount { get; private set; }
    internal long MaintenanceTickCount { get; private set; }
    internal long HoverTickCount { get; private set; }

    public OfficeWindow(bool demo = false, Settings? initial = null, double quitAfter = 0, AssetPack? assets = null, OfficeLayout? layout = null, bool qa = false,
        OfficeAnimator? liveOffice = null, IReadOnlyList<AgentInfo>? liveAgents = null, DateTimeOffset? demoEpoch = null, AppStorage? storage = null,
        IReadOnlyDictionary<string, BitmapSource>? decodedSheets = null, string? trackerHome = null)
    {
        this.demo = demo;
        this.trackerHome = trackerHome;
        this.storage = storage ?? new AppStorage(demo || qa);
        this.qa = qa; this.epoch=demoEpoch ?? DateTimeOffset.UtcNow;
        this.assets = assets ?? new(); view = new(this.assets, Path.Combine(this.storage.Home,"brand"), decodedSheets); office = liveOffice ?? new(42,this.assets,layout,ActivityCatalog.Load(this.storage.Home,this.assets.Warnings));
        agents=liveAgents ?? Array.Empty<AgentInfo>();
        settings = (initial ?? ReadSettings()).Normalize();
        view.PrivacyMode = demo ? PrivacyMode.Off : PrivacyModes.Parse(settings.PrivacyMode ?? "off");
        view.TagMode = settings.TagMode;
        view.AmbientSettings=settings.Ambient; view.SeasonDecor=settings.SeasonDecor; view.PetsEnabled=settings.Pets=="on";
        if (qa) settings=settings with { Mode="normal",Borderless=true,Width=400,Height=300,Left=-30000,Top=-30000 };
        Title = demo ? "Officelings — demo office" : "Officelings — agent office";
        Width = Finite(settings.Width,720,400,3000); Height = Finite(settings.Height,480,300,2000);
        MinWidth = 400; MinHeight = 300;
        AllowsTransparency = settings.Mode == "desktop" || settings.TransparentBackground;
        WindowStyle = AllowsTransparency || settings.Borderless ? WindowStyle.None : WindowStyle.SingleBorderWindow;
        Background = AllowsTransparency ? Brushes.Transparent : new SolidColorBrush(OfficeView.BackgroundColor);
        ShowInTaskbar = !qa && settings.Mode != "desktop"; Topmost = !qa && settings.Mode == "topmost";
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        if (settings.Left is { } left && settings.Top is { } top)
        { WindowStartupLocation = WindowStartupLocation.Manual; Place(WindowPlacement.Clamp(new(left,top,Width,Height), WorkingAreas())); }
        if (qa) { WindowStartupLocation=WindowStartupLocation.Manual; Left=-30000; Top=-30000; }
        view.SetZoom((int)Math.Round(Finite(settings.Zoom,0,0,6)));
        ShowActivated = !qa && settings.Mode != "desktop" && !settings.ClickThrough;
        var options = new TrackerOptions
        {
            PrivacyMode = view.PrivacyMode,
            Home = trackerHome ?? (this.storage.Isolated ? this.storage.Home : new TrackerOptions().Home),
            WorkingWindow = TimeSpan.FromSeconds(Finite(settings.WorkingSeconds,120,1,86400)),
            LeaveAfterIdle = TimeSpan.FromMinutes(settings.LeaveAfterIdleMinutes),
            LeaveAfterWaiting = TimeSpan.FromMinutes(settings.LeaveAfterWaitingMinutes)
        };
        tracker = new(options); modes = new(this,qa);
        if (view.PrivacyMode != PrivacyMode.Off)
        { agents=tracker.Redact(agents); office.ClearAgentCaches(); }
        var root = new DockPanel { Background=settings.TransparentBackground ? Brushes.Transparent : new SolidColorBrush(OfficeView.BackgroundColor) };
        var bar = new DockPanel { Margin = new(16,9,16,9), LastChildFill = true };
        DockPanel.SetDock(counts,Dock.Right); bar.Children.Add(counts);
        bar.Children.Add(new TextBlock { Text = demo ? "Officelings  ·  demo" : "Officelings", FontSize = 17, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(244,226,189)) });
        DockPanel.SetDock(bar,Dock.Top); root.Children.Add(bar);
        DockPanel.SetDock(status,Dock.Bottom); root.Children.Add(status);
        surface.Children.Add(view);root.Children.Add(surface); Content = root;
        InitializeSharing();
        PreviewMouseWheel += (_,e) => { if(editor != null && !view.IsMouseOver) return; Zoom(e.Delta>0 ? 1 : -1); e.Handled = true; };
        PreviewKeyDown += (_,e) =>
        {
            if(editor?.HandleKey(e)==true) { e.Handled=true;return; }
            if(Keyboard.FocusedElement is TextBox) return;
            if (e.Key is Key.Add or Key.OemPlus) { Zoom(1); e.Handled = true; }
            if (e.Key is Key.Subtract or Key.OemMinus) { Zoom(-1); e.Handled = true; }
        };
        ContextMenu = BuildContextMenu();
        SourceInitialized += (_,_) => modes.Attach();
        ContentRendered += (_,_) =>
        {
            ApplyModes();
            if (!qa && tray==null) Dispatcher.BeginInvoke(DispatcherPriority.Background,new Action(CreateTray));
        };
        LocationChanged += (_,_) => saveAfter = DateTimeOffset.UtcNow.AddSeconds(1);
        SizeChanged += (_,_) => saveAfter = DateTimeOffset.UtcNow.AddSeconds(1);
        if (demo && liveOffice == null)
            for (var frame = 0; frame < 180; frame++)
            { var t = -18+frame*.1; agents = DemoScenario.Agents(epoch,t); view.Update(office.Layout,office.Update(agents,epoch.AddSeconds(t),.1),epoch.AddSeconds(t),office.FurnitureSprites); }
        else view.Update(office.Layout,office.Update(agents,DateTimeOffset.UtcNow,0),DateTimeOffset.UtcNow,office.FurnitureSprites);
        timer = new(DispatcherPriority.Background,Dispatcher);
        timer.Tick += (_,_) => Animate();
        maintenance = new(DispatcherPriority.Background,Dispatcher) { Interval=TimeSpan.FromSeconds(1) };
        maintenance.Tick += (_,_) => Maintain();
        hoverTimer = new(DispatcherPriority.Background,Dispatcher) { Interval=TimeSpan.FromMilliseconds(100) };
        hoverTimer.Tick += (_,_) => { HoverTickCount++; RefreshHover(); };
        Loaded += (_,_) => { maintenance.Start(); ScheduleAnimation(); Maintain(); };
        StateChanged += (_,_) => { animationSuspended=WindowState==WindowState.Minimized; ScheduleAnimation(); };
        if (!qa)
        {
            MouseMove += (_,_) => RefreshHover();
            MouseLeave += (_,_) => { view.PollHover(null,DateTimeOffset.UtcNow); ScheduleAnimation(); };
        }
        if (double.IsFinite(quitAfter) && quitAfter>0)
        {
            var stop = new DispatcherTimer { Interval = TimeSpan.FromSeconds(quitAfter) };
            stop.Tick += (_,_) => { stop.Stop(); Close(); }; stop.Start();
        }
        Closed += (_,_) => { closed = true; timer.Stop(); maintenance.Stop(); hoverTimer.Stop(); view.HideTooltip(); if (tray != null) { tray.Visible = false; tray.Dispose(); } if (!polling) tracker.Dispose(); Save(); };
        status.Text = demo ? "Fabricated sessions · wheel / + / − to zoom · drag to pan" : "Reading recent sessions…";
    }

    private static double Finite(double value,double fallback,double min,double max) => double.IsFinite(value) ? Math.Clamp(value,min,max) : fallback;
    private Settings ReadSettings()
        => Settings.Read(settingsPath);
    private void Save()
    {
        if (demo || qa) return; // QA must not overwrite personal settings.
        var bounds = WindowState == WindowState.Normal ? modes.ScreenBounds : RestoreBounds;
        if (bounds.IsEmpty || !double.IsFinite(bounds.Left) || !double.IsFinite(bounds.Top)) return;
        settings = (settings with { Width = bounds.Width, Height = bounds.Height, Left = bounds.Left, Top = bounds.Top, Zoom = view.PreferredZoom }).Normalize();
        try { settings.Write(settingsPath); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or JsonException) { warning = "Cannot save settings: "+PrivacyModes.Diagnostic(e,view.PrivacyMode); }
    }
    private void ApplyModes()
    {
        view.TransparentBackground = settings.TransparentBackground;
        modes.Apply(editor != null ? "normal" : settings.Mode,editor == null && settings.ClickThrough,settings.Borderless,settings.TransparentBackground,Finite(settings.Opacity,1,.25,1));
        view.HideTooltip();
    }
    private void Zoom(int amount) { view.ChangeZoom(amount); Save(); }
    private void Fit() { view.SetZoom(0); Save(); }
    private void SetMode(string mode)
    {
        settings = settings with { Mode = mode };
        if (AllowsTransparency != (mode == "desktop" || settings.TransparentBackground)) Reopen();
        else { ApplyModes(); Save(); }
    }
    private void ToggleClickThrough() { settings = settings with { ClickThrough = !settings.ClickThrough }; ApplyModes(); Save(); }
    private void SetTags(string mode) { settings=settings with { TagMode=mode }; view.TagMode=mode; Save(); }
    private void TogglePets() { settings=settings with { Pets=settings.Pets=="on" ? "off" : "on" }; view.PetsEnabled=settings.Pets=="on"; var now=DateTimeOffset.UtcNow; view.Update(office.Layout,office.Update(agents,now,0),now,office.FurnitureSprites); ScheduleAnimation(); Save(); }
    private void ToggleBorderless() { settings = settings with { Borderless = !settings.Borderless }; ApplyModes(); Save(); }
    private void ToggleTransparent()
    {
        settings = settings with { TransparentBackground = !settings.TransparentBackground };
        if (AllowsTransparency != (settings.Mode == "desktop" || settings.TransparentBackground)) Reopen();
        else { if(Content is Panel root) root.Background = settings.TransparentBackground ? Brushes.Transparent : new SolidColorBrush(OfficeView.BackgroundColor); ApplyModes(); Save(); }
    }
    private void Reopen(Rect? restoredBounds = null)
    {
        var bounds = restoredBounds ?? modes.ScreenBounds;
        settings = settings with { Width = bounds.Width, Height = bounds.Height, Left = bounds.Left, Top = bounds.Top, Zoom = view.PreferredZoom };
        // WPF fixes AllowsTransparency at HWND creation. Replace only when that native surface changes.
        var wasEditing=editor != null;
        var replacement = new OfficeWindow(demo,settings,0,assets,editor?.Editor.Layout ?? office.SourceLayout,qa,office,agents,epoch,storage,view.DecodedSheets);
        replacement.pendingEditor=editor?.Editor;
        if(wasEditing) replacement.ToggleEditor();
        Application.Current.MainWindow=replacement; replacement.Show(); Close();
    }
    private void OpacityTo(double value) { settings = settings with { Opacity = value }; ApplyModes(); Save(); }
    private void Recover()
    {
        settings = settings with { Mode = "normal", ClickThrough = false, Borderless = false, TransparentBackground = false };
        var bounds = WindowPlacement.Clamp(modes.ScreenBounds, WorkingAreas());
        if (AllowsTransparency) { Reopen(bounds); return; }
        ApplyModes(); Show(); WindowState = WindowState.Normal; Place(bounds); Save();
    }
    private Rect[] WorkingAreas()
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        return Forms.Screen.AllScreens.Select(s => new Rect(s.WorkingArea.Left / dpi.DpiScaleX, s.WorkingArea.Top / dpi.DpiScaleY,
            s.WorkingArea.Width / dpi.DpiScaleX, s.WorkingArea.Height / dpi.DpiScaleY)).ToArray();
    }
    private void Place(Rect bounds) { Width = bounds.Width; Height = bounds.Height; Left = bounds.Left; Top = bounds.Top; }
    private (string Title,Action Action,Func<bool>? Check)[] MenuItems() =>
    [
        ("Isometric view",SwitchIso,()=>false),
        ("Normal",()=>SetMode("normal"),()=>settings.Mode=="normal"),
        ("Always on top",()=>SetMode("topmost"),()=>settings.Mode=="topmost"),
        ("Desktop level",()=>SetMode("desktop"),()=>settings.Mode=="desktop"),
        ("Click-through",ToggleClickThrough,()=>settings.ClickThrough),
        ("Borderless",ToggleBorderless,()=>settings.Borderless),
        ("Transparent background",ToggleTransparent,()=>settings.TransparentBackground),
        ("Pets",TogglePets,()=>settings.Pets=="on"),
        ("Sharing",ShowSharing,()=>sharingSender?.Enabled==true),
        ("Opacity 100%",()=>OpacityTo(1),()=>settings.Opacity==1),
        ("Opacity 85%",()=>OpacityTo(.85),()=>settings.Opacity==.85),
        ("Opacity 70%",()=>OpacityTo(.70),()=>settings.Opacity==.70),
        ("Opacity 50%",()=>OpacityTo(.50),()=>settings.Opacity==.50),
        ("Opacity 30%",()=>OpacityTo(.30),()=>settings.Opacity==.30),
        ("Edit office",ToggleEditor,()=>editor != null),
        ("Fit office to window",Fit,()=>view.PreferredZoom==0),
        ("Show normal, clickable office",Recover,null),
        ("Exit",Close,null)
    ];
    private ContextMenu BuildContextMenu()
    {
        var menu = new ContextMenu();
        AddPrivacyMenu(menu);
        var tags=new MenuItem { Header="Name tags" }; menu.Items.Add(tags);
        foreach (var mode in new[] { "hover","icon","always" })
        {
            var item=new MenuItem { Header=TagTitle(mode) }; tags.Items.Add(item);
            item.Click+=(_,_)=>SetTags(mode); menu.Opened+=(_,_)=>item.IsChecked=settings.TagMode==mode;
        }
        foreach (var entry in MenuItems())
        {
            var item = new MenuItem { Header = entry.Title };
            item.Click += (_,_) => entry.Action(); menu.Items.Add(item);
            menu.Opened += (_,_) => item.IsChecked = entry.Check?.Invoke() ?? false;
        }
        return menu;
    }
    private Forms.ContextMenuStrip BuildTrayMenu()
    {
        var menu = new Forms.ContextMenuStrip();
        AddPrivacyMenu(menu);
        var tags=new Forms.ToolStripMenuItem("Name tags"); menu.Items.Add(tags);
        foreach (var mode in new[] { "hover","icon","always" })
        {
            var item=new Forms.ToolStripMenuItem(TagTitle(mode)); tags.DropDownItems.Add(item);
            item.Click+=(_,_)=>Dispatcher.Invoke(()=>SetTags(mode)); menu.Opening+=(_,_)=>item.Checked=settings.TagMode==mode;
        }
        foreach (var entry in MenuItems())
        {
            var item = new Forms.ToolStripMenuItem(entry.Title);
            item.Click += (_,_) => Dispatcher.Invoke(entry.Action); menu.Items.Add(item);
            menu.Opening += (_,_) => item.Checked = entry.Check?.Invoke() ?? false;
        }
        return menu;
    }
    private static string TagTitle(string mode) => mode switch { "hover"=>"Hover", "icon"=>"Icon", _=>"Always" };
    private void CreateTray()
    {
        if (closed || qa || tray != null) return;
        // First paint precedes WinForms/icon initialization; QA needs no tray at all.
        tray = new Forms.NotifyIcon { Text = demo ? "Officelings — demo" : "Officelings — passive office", Icon = System.Drawing.SystemIcons.Application, Visible = true };
        tray.DoubleClick += (_,_) => Dispatcher.Invoke(Recover); tray.ContextMenuStrip = BuildTrayMenu();
    }
    private void Animate()
    {
        if (closed) return;
        AnimationTickCount++;
        var now = DateTimeOffset.UtcNow;
        if (demo) agents = DemoScenario.Agents(epoch,(now-epoch).TotalSeconds);
        if (!animationSuspended && WindowState != WindowState.Minimized)
        {
            var views = office.Update(agents,now,(now-previous).TotalSeconds);
            view.Update(editor?.Editor.Layout ?? office.Layout,views,now,office.FurnitureSprites);
            view.AdvanceHover(now);
        }
        previous = now;
        if (now>=nextMaintenance) Maintain();
        ScheduleAnimation();
    }
    private void ScheduleAnimation()
    {
        if (closed || animationSuspended || WindowState==WindowState.Minimized) { timer.Stop(); hoverTimer.Stop(); if (!closed) maintenance.Start(); return; }
        if (!office.HasMovingCharacters && !view.TagsAnimating && !view.TooltipPending) hoverTimer.Stop();
        var interval = view.TagsAnimating ? TimeSpan.FromSeconds(1d/30) : Program.MinimumInterval(office.AnimationInterval,view.AnimationInterval);
        if (settings.TransparentBackground && office.HasMovingCharacters && !view.TagsAnimating)
            interval=TimeSpan.FromSeconds(.1);
        if (demo && interval==TimeSpan.Zero) interval=TimeSpan.FromMilliseconds(125);
        var nextUpdate = office.NextUpdateAt < view.NextUpdateAt ? office.NextUpdateAt : view.NextUpdateAt;
        if (nextUpdate!=DateTimeOffset.MaxValue)
        {
            var remaining=(nextUpdate-DateTimeOffset.UtcNow).TotalMilliseconds;
            if (interval==TimeSpan.Zero) interval=TimeSpan.FromMilliseconds(Math.Max(16,remaining));
            else if (remaining>0)
            {
                var deadline=TimeSpan.FromMilliseconds(Math.Max(1000d/30,remaining));
                if (deadline<interval) interval=deadline;
            }
        }
        if (interval==TimeSpan.Zero) { timer.Stop(); maintenance.Start(); return; }
        // Continuous animation already wakes the dispatcher. Coalesce log/visibility
        // checks with those frames instead of introducing an extra periodic wakeup.
        if (interval<=TimeSpan.FromSeconds(1)) maintenance.Stop(); else maintenance.Start();
        if (timer.Interval!=interval) timer.Interval=interval;
        if (!timer.IsEnabled) { previous=DateTimeOffset.UtcNow; timer.Start(); }
    }
    private void RefreshHover()
    {
        if (qa || closed || animationSuspended) { hoverTimer.Stop(); return; }
        view.PollHover(modes.Cursor(view),DateTimeOffset.UtcNow);
        if (view.HasHoverTarget && (office.HasMovingCharacters || view.TagsAnimating || view.TooltipPending)) hoverTimer.Start(); else hoverTimer.Stop();
        ScheduleAnimation();
    }
    private void Maintain()
    {
        if (closed) return;
        MaintenanceTickCount++;
        var now=DateTimeOffset.UtcNow;
        nextMaintenance=now.AddSeconds(1);
        var suspended=WindowState==WindowState.Minimized || modes.AnimationSuppressed;
        if (suspended!=animationSuspended)
        { animationSuspended=suspended; previous=now; ScheduleAnimation(); }
        if (!animationSuspended && !timer.IsEnabled)
        {
            var views=office.Update(agents,now,(now-previous).TotalSeconds); previous=now;
            view.Update(editor?.Editor.Layout ?? office.Layout,views,now,office.FurnitureSprites);
            ScheduleAnimation();
        }
        if (!qa && (view.HasHoverTarget || modes.ClickThrough || modes.Mode=="desktop") && !hoverTimer.IsEnabled) RefreshHover();
        if (now>=nextHostCheck)
        {
            nextHostCheck = now.AddSeconds(2);
            if (modes.DesktopHostLost) ApplyModes();
            var countText = string.Join("  ·  ",Enum.GetValues<Provider>().Select(p=>$"{p} {agents.Count(a=>a.Provider==p&&a.State!=AgentState.Gone)}"));
            if (counts.Text!=countText) counts.Text=countText;
            var issue=string.Join("  ",PrivacyModes.Diagnostics(assets.Warnings.Concat(warning.Length>0 ? new[] { warning } : Array.Empty<string>()).Concat(modes.OpacityError != null ? new[] { "Opacity: "+modes.OpacityError } : Array.Empty<string>()).ToArray(),view.PrivacyMode));
            status.Text = issue.Length>0 ? issue : editor != null ? "Editing office · live agents · click-through paused · Save applies layout" : settings.Mode=="desktop" && !modes.Embedded ? "Desktop host unavailable; bottom-level fallback. Recover from tray." : $"{agents.Count(a=>a.State!=AgentState.Gone)} agents  ·  {view.Zoom:0.##}×{(view.PreferredZoom==0 ? " auto" : "")}  ·  wheel / + / −  ·  drag to pan  ·  right-click for modes";
        }
        if (!demo && !polling && now>=nextPoll) { nextPoll = now.AddSeconds(1); _ = PollAsync(now); }
        if (saveAfter!=default && now>=saveAfter) { saveAfter = default; Save(); }
    }
    private void ToggleEditor()
    {
        if(editor != null)
        { var old=editor;editor=null;old.Detach();surface.Children.Remove(old);ApplyModes();return; }
        editor=new OfficeEditorPanel(view,assets,office.SourceLayout,storage.LayoutPath,pendingEditor);pendingEditor=null;
        editor.LayoutChanged+=()=>
        {
            try { office.ReloadLayout(editor.Editor.Layout);warning=""; }
            catch(Exception e) when(e is IOException or ArgumentException or InvalidOperationException) { warning="Preview: "+PrivacyModes.Diagnostic(e,view.PrivacyMode); }
            view.Update(editor.Editor.Layout,view.Agents,DateTimeOffset.UtcNow,office.FurnitureSprites);
        };
        editor.ExitRequested+=ToggleEditor;
        DockPanel.SetDock(editor,Dock.Left);surface.Children.Insert(0,editor);ApplyModes();
    }
    private async Task PollAsync(DateTimeOffset now)
    {
        polling = true;
        var privacyVersion = privacyRevision;
        try
        {
            var current = await Task.Run(()=>tracker.Poll(now));
            if (closed || privacyVersion != privacyRevision) return;
            var changed=!agents.SequenceEqual(current);
            agents = current; warning = tracker.Warnings.Count>0 ? string.Join("  ",tracker.Warnings) : "";
            if (!animationSuspended && changed)
            {
                var time=DateTimeOffset.UtcNow;
                view.Update(office.Layout,office.Update(agents,time,(time-previous).TotalSeconds),time,office.FurnitureSprites);
                previous=time; ScheduleAnimation();
            }
        }
        catch (Exception e) { warning = "Reading logs failed: "+PrivacyModes.Diagnostic(e,view.PrivacyMode); }
        finally { polling = false; if (closed) tracker.Dispose(); }
    }
}
