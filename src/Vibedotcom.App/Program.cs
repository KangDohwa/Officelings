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
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Vibedotcom.Core;
using Forms = System.Windows.Forms;

namespace Vibedotcom.App;

internal sealed record Settings
{
    public string Mode { get; init; } = "normal";
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
    public double GoneMinutes { get; init; } = 30;
}

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            var demo = args.Contains("--demo"); Settings? initial = null;
            string? smoke = null, assetDirectory = null, layoutFile = null, profile = null; double quitAfter = 0; var qa = false;
            for (var i = 0; i < args.Length; i++)
            {
                string Value() => ++i < args.Length ? args[i] : throw new ArgumentException("Missing option value.");
                double Number() => double.Parse(Value(),CultureInfo.InvariantCulture);
                switch (args[i])
                {
                    case "--demo": break;
                    case "--smoke-test": smoke = Value(); break;
                    case "--asset-dir": assetDirectory = Value(); break;
                    case "--layout": layoutFile = Value(); break;
                    case "--qa": qa = true; break;
                    case "--profile-idle": profile = Value(); break;
                    case "--zoom": initial = (initial ?? new()) with { Zoom = Number() }; break;
                    case "--mode": initial = (initial ?? new()) with { Mode = Value() }; break;
                    case "--click-through": initial = (initial ?? new()) with { ClickThrough = true }; break;
                    case "--borderless": initial = (initial ?? new()) with { Borderless = true }; break;
                    case "--transparent": initial = (initial ?? new()) with { TransparentBackground = true }; break;
                    case "--opacity": initial = (initial ?? new()) with { Opacity = Number() }; break;
                    case "--quit-after": quitAfter = Number(); break;
                    default: throw new ArgumentException("Usage: Vibedotcom.App [--demo] [--asset-dir DIR] [--layout FILE] [--zoom 0..6] [--mode normal|topmost|desktop] [--click-through] [--borderless] [--transparent] [--opacity .25..1] [--qa --quit-after SECONDS] [--smoke-test OUTPUT_DIR] [--profile-idle OUTPUT_DIR]");
                }
            }
            var app = new Application();
            var assets = AssetPack.LoadDefault(assetDirectory, useUserPack: !demo && !qa && profile == null);
            var layout = LayoutLoader.Load(assets,layoutFile ?? (demo || qa || profile != null ? null : Path.Combine(TrackerOptions.OutputDirectory,"layout.json")),assets.Warnings);
            if (profile != null) return ProfileIdle(profile,assets,layout);
            if (smoke != null) return Smoke(smoke,demo,assets,layout);
            if (qa && (!double.IsFinite(quitAfter) || quitAfter<=0 || quitAfter>60)) throw new ArgumentException("--qa requires --quit-after in (0,60].");
            return app.Run(new OfficeWindow(demo,initial,quitAfter,assets,layout,qa));
        }
        catch (Exception e)
        {
            if (args.Length > 0) Console.Error.WriteLine(e);
            else MessageBox.Show(e.Message,"Vibedotcom",MessageBoxButton.OK,MessageBoxImage.Error);
            return 1;
        }
    }

    private static int Smoke(string output, bool demo, AssetPack assets, OfficeLayout layout)
    {
        output = StateJson.OutputPath(output); Directory.CreateDirectory(output);
        using var tracker = new AgentTracker(); var office = new OfficeAnimator(42,assets,layout);
        var now = demo ? DateTimeOffset.Parse("2026-10-03T12:00:00Z") : DateTimeOffset.UtcNow;
        IReadOnlyList<AgentInfo> sources = Array.Empty<AgentInfo>();
        IReadOnlyList<AgentView> views = Array.Empty<AgentView>();
        var view = new OfficeView(assets);
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
            view.SimulateHover(point,now.AddSeconds(55)); Render(view,700,480,Path.Combine(output,"tooltip.png"));
            File.WriteAllText(Path.Combine(output,"tooltip.txt"),view.DebugTooltipText);
            view.SimulateHover(null,now); if (view.DebugTooltipText != null) throw new InvalidOperationException("Tooltip did not clear.");
        }
        view.TransparentBackground=true; Render(view,700,480,Path.Combine(output,"transparent.png"));
        var manifest=assets.Export(output);
        StateJson.WriteAtomic(Path.Combine(output,"layout.json"),JsonSerializer.SerializeToUtf8Bytes(office.Layout,StateJson.Options));
        StateJson.WriteAtomic(Path.Combine(output,"state.json"),new StateSnapshot(StateJson.SchemaVersion,now.AddSeconds(55),1,SpriteAtlas.Info,office.Layout,views,tracker.Warnings.Concat(assets.Warnings).ToArray(),manifest,office.FurnitureSprites));
        File.WriteAllText(Path.Combine(output,"smoke.txt"),$"Offscreen WPF render OK; demo={demo}; agents={sources.Count}; zooms=1,2,3; hover={hover != null}; logBytesRead={tracker.BytesRead}\n"+string.Join("\n",assets.Warnings));
        return 0;
    }
    private static void Render(OfficeView view,int width,int height,string file)
    {
        view.Width=width; view.Height=height; view.Measure(new(width,height)); view.Arrange(new(0,0,width,height)); view.UpdateLayout();
        var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32); bitmap.Render(view);
        var png=new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream=File.Create(file); png.Save(stream);
    }
    private static int ProfileIdle(string output,AssetPack assets,OfficeLayout layout)
    {
        output=StateJson.OutputPath(output); Directory.CreateDirectory(output);
        var now=DateTimeOffset.UtcNow; var office=new OfficeAnimator(42,assets,layout);
        var agents=DemoScenario.Agents(now,30).Take(6).Select(a=>a with { State=AgentState.Idle }).ToArray();
        IReadOnlyList<AgentView> views=Array.Empty<AgentView>();
        for(var i=0;i<300;i++) views=office.Update(agents,now.AddSeconds(i*.1),.1);
        var view=new OfficeView(assets); view.Update(office.Layout,views,now,office.FurnitureSprites); view.SetZoom(1);
        Render(view,700,480,Path.Combine(output,"idle.png"));
        using var process=Process.GetCurrentProcess(); var startCpu=process.TotalProcessorTime; var elapsed=Stopwatch.StartNew();
        var frame=new DispatcherFrame(); var ticks=0;
        using var tracker=new AgentTracker(new() { Home=Path.Combine(output,"empty-home") }); var polling=false;
        var timer=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(125) };
        timer.Tick+=async (_,_)=>
        {
            var time=now.AddSeconds(30+elapsed.Elapsed.TotalSeconds);
            view.Update(office.Layout,office.Update(agents,time,.125),time,office.FurnitureSprites);
            var bitmap=new RenderTargetBitmap(700,480,96,96,PixelFormats.Pbgra32); bitmap.Render(view); ticks++;
            if (!polling && ticks%8==0) { polling=true; await Task.Run(()=>tracker.Poll(time)); polling=false; }
            if(elapsed.Elapsed.TotalSeconds>=20) { timer.Stop(); frame.Continue=false; }
        };
        timer.Start(); Dispatcher.PushFrame(frame); process.Refresh();
        var cpu=(process.TotalProcessorTime-startCpu).TotalSeconds/elapsed.Elapsed.TotalSeconds/Environment.ProcessorCount*100;
        File.WriteAllText(Path.Combine(output,"profile.json"),JsonSerializer.Serialize(new { wallSeconds=elapsed.Elapsed.TotalSeconds,cpuPercent=cpu,logicalProcessors=Environment.ProcessorCount,ticks,scenario="six idle agents; 8fps offscreen rendering; empty tracker home" },StateJson.Options));
        return cpu<2 ? 0 : 2;
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

internal sealed class OfficeWindow : Window
{
    private readonly string settingsPath = Path.Combine(TrackerOptions.OutputDirectory,"settings.json");
    private Settings settings;
    private readonly bool demo, qa;
    private readonly DateTimeOffset epoch = DateTimeOffset.UtcNow;
    private readonly OfficeView view;
    private readonly AssetPack assets;
    private readonly TextBlock status = new() { Margin = new(16,7,16,7), Foreground = new SolidColorBrush(Color.FromRgb(231,209,172)), FontSize = 11 };
    private readonly TextBlock counts = new() { VerticalAlignment = VerticalAlignment.Center, Foreground = new SolidColorBrush(Color.FromRgb(231,209,172)), FontSize = 11 };
    private readonly AgentTracker tracker;
    private readonly OfficeAnimator office;
    private readonly WindowModes modes;
    private readonly Forms.NotifyIcon tray;
    private readonly DispatcherTimer timer;
    private IReadOnlyList<AgentInfo> agents = Array.Empty<AgentInfo>();
    private DateTimeOffset previous = DateTimeOffset.UtcNow, nextPoll, nextHover, nextHostCheck, saveAfter;
    private bool polling, closed;
    private string warning = "";

    public OfficeWindow(bool demo = false, Settings? initial = null, double quitAfter = 0, AssetPack? assets = null, OfficeLayout? layout = null, bool qa = false)
    {
        this.demo = demo;
        this.qa = qa; this.assets = assets ?? new(); view = new(this.assets); office = new(42,this.assets,layout);
        settings = initial ?? (demo || qa ? new() : ReadSettings());
        if (qa) settings=settings with { Mode="normal",Borderless=true,Width=400,Height=300,Left=-30000,Top=-30000 };
        Title = demo ? "Vibedotcom — demo office" : "Vibedotcom — agent office";
        Width = Finite(settings.Width,720,400,3000); Height = Finite(settings.Height,480,300,2000);
        MinWidth = 400; MinHeight = 300;
        AllowsTransparency = settings.Mode == "desktop" || settings.TransparentBackground;
        WindowStyle = AllowsTransparency || settings.Borderless ? WindowStyle.None : WindowStyle.SingleBorderWindow;
        Background = settings.TransparentBackground ? Brushes.Transparent : new SolidColorBrush(OfficeView.BackgroundColor);
        ShowInTaskbar = !qa && settings.Mode != "desktop"; Topmost = !qa && settings.Mode == "topmost";
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        if (settings.Left is { } left && settings.Top is { } top && double.IsFinite(left) && double.IsFinite(top) &&
            new Rect(SystemParameters.VirtualScreenLeft,SystemParameters.VirtualScreenTop,SystemParameters.VirtualScreenWidth,SystemParameters.VirtualScreenHeight)
                .IntersectsWith(new Rect(left,top,Width,Height)))
        { WindowStartupLocation = WindowStartupLocation.Manual; Left = left; Top = top; }
        if (qa) { WindowStartupLocation=WindowStartupLocation.Manual; Left=-30000; Top=-30000; }
        view.SetZoom((int)Math.Round(Finite(settings.Zoom,0,0,6)));
        ShowActivated = !qa && settings.Mode != "desktop" && !settings.ClickThrough;
        var options = new TrackerOptions
        {
            WorkingWindow = TimeSpan.FromSeconds(Finite(settings.WorkingSeconds,120,1,86400)),
            GoneAfter = TimeSpan.FromMinutes(Finite(settings.GoneMinutes,30,1,10080))
        };
        if (options.GoneAfter <= options.WorkingWindow) options = new();
        tracker = new(options); modes = new(this,qa);
        var root = new DockPanel();
        var bar = new DockPanel { Margin = new(16,9,16,9), LastChildFill = true };
        DockPanel.SetDock(counts,Dock.Right); bar.Children.Add(counts);
        bar.Children.Add(new TextBlock { Text = demo ? "Vibedotcom  ·  demo" : "Vibedotcom", FontSize = 17, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(244,226,189)) });
        DockPanel.SetDock(bar,Dock.Top); root.Children.Add(bar);
        DockPanel.SetDock(status,Dock.Bottom); root.Children.Add(status);
        root.Children.Add(view); Content = root;
        PreviewMouseWheel += (_,e) => { Zoom(e.Delta>0 ? 1 : -1); e.Handled = true; };
        PreviewKeyDown += (_,e) =>
        {
            if (e.Key is Key.Add or Key.OemPlus) { Zoom(1); e.Handled = true; }
            if (e.Key is Key.Subtract or Key.OemMinus) { Zoom(-1); e.Handled = true; }
        };
        ContextMenu = BuildContextMenu();
        tray = new Forms.NotifyIcon { Text = demo ? "Vibedotcom — demo" : "Vibedotcom — passive office", Icon = System.Drawing.SystemIcons.Application, Visible = !qa };
        tray.DoubleClick += (_,_) => Dispatcher.Invoke(Recover); tray.ContextMenuStrip = BuildTrayMenu();
        SourceInitialized += (_,_) => modes.Attach();
        ContentRendered += (_,_) => ApplyModes();
        LocationChanged += (_,_) => saveAfter = DateTimeOffset.UtcNow.AddSeconds(1);
        SizeChanged += (_,_) => saveAfter = DateTimeOffset.UtcNow.AddSeconds(1);
        if (demo)
            for (var frame = 0; frame < 180; frame++)
            { var t = -18+frame*.1; agents = DemoScenario.Agents(epoch,t); view.Update(office.Layout,office.Update(agents,epoch.AddSeconds(t),.1),epoch.AddSeconds(t),office.FurnitureSprites); }
        timer = new(TimeSpan.FromMilliseconds(1000d/30),DispatcherPriority.Background,(_,_) => Animate(),Dispatcher);
        if (double.IsFinite(quitAfter) && quitAfter>0)
        {
            var stop = new DispatcherTimer { Interval = TimeSpan.FromSeconds(quitAfter) };
            stop.Tick += (_,_) => { stop.Stop(); Close(); }; stop.Start();
        }
        Closed += (_,_) => { closed = true; timer.Stop(); view.HideTooltip(); tray.Visible = false; tray.Dispose(); if (!polling) tracker.Dispose(); Save(); };
        status.Text = demo ? "Fabricated sessions · wheel / + / − to zoom · drag to pan" : "Reading recent sessions…";
    }

    private static double Finite(double value,double fallback,double min,double max) => double.IsFinite(value) ? Math.Clamp(value,min,max) : fallback;
    private Settings ReadSettings()
    {
        try
        {
            using var file = new FileStream(settingsPath,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
            return JsonSerializer.Deserialize<Settings>(file,StateJson.Options) ?? new();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }
    private void Save()
    {
        if (demo || qa) return; // QA must not overwrite personal settings.
        var bounds = WindowState == WindowState.Normal ? modes.ScreenBounds : RestoreBounds;
        if (bounds.IsEmpty || !double.IsFinite(bounds.Left) || !double.IsFinite(bounds.Top)) return;
        settings = settings with { Width = bounds.Width, Height = bounds.Height, Left = bounds.Left, Top = bounds.Top, Zoom = view.PreferredZoom };
        try { StateJson.WriteAtomic(settingsPath,JsonSerializer.SerializeToUtf8Bytes(settings,StateJson.Options)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { warning = "Cannot save settings: "+e.Message; }
    }
    private void ApplyModes()
    {
        view.TransparentBackground = settings.TransparentBackground;
        modes.Apply(settings.Mode,settings.ClickThrough,settings.Borderless,settings.TransparentBackground,Finite(settings.Opacity,1,.25,1));
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
    private void ToggleBorderless() { settings = settings with { Borderless = !settings.Borderless }; ApplyModes(); Save(); }
    private void ToggleTransparent()
    {
        settings = settings with { TransparentBackground = !settings.TransparentBackground };
        if (AllowsTransparency != (settings.Mode == "desktop" || settings.TransparentBackground)) Reopen();
        else { Background = settings.TransparentBackground ? Brushes.Transparent : new SolidColorBrush(OfficeView.BackgroundColor); ApplyModes(); Save(); }
    }
    private void Reopen()
    {
        var bounds = modes.ScreenBounds;
        settings = settings with { Width = bounds.Width, Height = bounds.Height, Left = bounds.Left, Top = bounds.Top, Zoom = view.PreferredZoom };
        // WPF fixes AllowsTransparency at HWND creation. Replace only when that native surface changes.
        var replacement = new OfficeWindow(demo,settings,0,assets,office.SourceLayout,qa);
        Application.Current.MainWindow=replacement; replacement.Show(); Close();
    }
    private void OpacityTo(double value) { settings = settings with { Opacity = value }; ApplyModes(); Save(); }
    private void Recover()
    {
        settings = settings with { Mode = "normal", ClickThrough = false, Borderless = false, TransparentBackground = false };
        if (AllowsTransparency) { Reopen(); return; }
        ApplyModes(); Show(); WindowState = WindowState.Normal; Save();
    }
    private (string Title,Action Action,Func<bool>? Check)[] MenuItems() =>
    [
        ("Normal",()=>SetMode("normal"),()=>settings.Mode=="normal"),
        ("Always on top",()=>SetMode("topmost"),()=>settings.Mode=="topmost"),
        ("Desktop level",()=>SetMode("desktop"),()=>settings.Mode=="desktop"),
        ("Click-through",ToggleClickThrough,()=>settings.ClickThrough),
        ("Borderless",ToggleBorderless,()=>settings.Borderless),
        ("Transparent background",ToggleTransparent,()=>settings.TransparentBackground),
        ("Opacity 100%",()=>OpacityTo(1),()=>settings.Opacity==1),
        ("Opacity 85%",()=>OpacityTo(.85),()=>settings.Opacity==.85),
        ("Opacity 65%",()=>OpacityTo(.65),()=>settings.Opacity==.65),
        ("Fit office to window",Fit,()=>view.PreferredZoom==0),
        ("Show normal, clickable office",Recover,null),
        ("Exit",Close,null)
    ];
    private ContextMenu BuildContextMenu()
    {
        var menu = new ContextMenu();
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
        foreach (var entry in MenuItems())
        {
            var item = new Forms.ToolStripMenuItem(entry.Title);
            item.Click += (_,_) => Dispatcher.Invoke(entry.Action); menu.Items.Add(item);
            menu.Opening += (_,_) => item.Checked = entry.Check?.Invoke() ?? false;
        }
        return menu;
    }
    private void Animate()
    {
        if (closed) return;
        var now = DateTimeOffset.UtcNow;
        if (demo) agents = DemoScenario.Agents(epoch,(now-epoch).TotalSeconds);
        if (WindowState != WindowState.Minimized)
        {
            var views = office.Update(agents,now,(now-previous).TotalSeconds);
            view.Update(office.Layout,views,now,office.FurnitureSprites);
            timer.Interval=TimeSpan.FromMilliseconds(views.Any(a=>a.Pose.Visible && a.Pose.Action is "walking" or "entering" or "exiting") ? 1000d/30 : 125);
            if (!qa && now>=nextHover) { view.PollHover(modes.Cursor(view),now); nextHover = now.AddMilliseconds(100); }
        }
        previous = now;
        if (now>=nextHostCheck)
        {
            nextHostCheck = now.AddSeconds(2);
            if (modes.DesktopHostLost) ApplyModes();
            counts.Text = string.Join("  ·  ",Enum.GetValues<Provider>().Select(p=>$"{p} {agents.Count(a=>a.Provider==p&&a.State!=AgentState.Gone)}"));
            var issue=string.Join("  ",assets.Warnings.Concat(warning.Length>0 ? new[] { warning } : Array.Empty<string>()));
            status.Text = issue.Length>0 ? issue : settings.Mode=="desktop" && !modes.Embedded ? "Desktop host unavailable; bottom-level fallback. Recover from tray." : $"{agents.Count(a=>a.State!=AgentState.Gone)} agents  ·  {view.Zoom}×{(view.PreferredZoom==0 ? " auto" : "")}  ·  wheel / + / −  ·  drag to pan  ·  right-click for modes";
        }
        if (!demo && !polling && now>=nextPoll) { nextPoll = now.AddSeconds(1); _ = PollAsync(now); }
        if (saveAfter!=default && now>=saveAfter) { saveAfter = default; Save(); }
    }
    private async Task PollAsync(DateTimeOffset now)
    {
        polling = true;
        try
        {
            var current = await Task.Run(()=>tracker.Poll(now));
            if (closed) return;
            agents = current; warning = tracker.Warnings.Count>0 ? string.Join("  ",tracker.Warnings) : "";
        }
        catch (Exception e) { warning = "Reading logs failed: "+e.Message; }
        finally { polling = false; if (closed) tracker.Dispose(); }
    }
}
