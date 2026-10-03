using System;
using System.Collections.Generic;
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
    public double Zoom { get; init; } = 1.25;
    public double Width { get; init; } = 1060;
    public double Height { get; init; } = 740;
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
            var app = new Application();
            if (args.Length == 2 && args[0] == "--smoke-test") return Smoke(args[1]);
            if (args.Length != 0) throw new ArgumentException("Usage: Vibedotcom.App [--smoke-test OUTPUT_DIR]");
            var window = new OfficeWindow();
            return app.Run(window);
        }
        catch (Exception e)
        {
            if (args.Length > 0) Console.Error.WriteLine(e);
            else MessageBox.Show(e.Message, "Vibedotcom", MessageBoxButton.OK, MessageBoxImage.Error);
            return 1;
        }
    }

    // Bounded, headless WPF smoke: real logs, real atlas decoder, layout and rendering, no settings writes.
    private static int Smoke(string output)
    {
        output = StateJson.OutputPath(output); Directory.CreateDirectory(output);
        var tracker = new AgentTracker(); var office = new OfficeAnimator(42);
        var now = DateTimeOffset.UtcNow; var agents = tracker.Poll(now);
        IReadOnlyList<AgentView> views = Array.Empty<AgentView>();
        for (var frame = 0; frame < 80; frame++) views = office.Update(agents, now.AddSeconds(frame * .1), .1);
        var view = new OfficeView { Zoom = 1.25 };
        view.Update(office.Layout, views);
        view.Measure(new(view.Width, view.Height)); view.Arrange(new(0, 0, view.Width, view.Height)); view.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(view.Width), (int)Math.Ceiling(view.Height), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(view);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
        using (var stream = File.Create(Path.Combine(output, "office.png"))) png.Save(stream);
        StateJson.WriteAtomic(Path.Combine(output, "state.json"), new StateSnapshot(1, now, 1, SpriteAtlas.Info, office.Layout, views, tracker.Warnings));
        StateJson.WriteAtomic(Path.Combine(output, "atlas.png"), SpriteAtlas.CreatePng());
        File.WriteAllText(Path.Combine(output, "smoke.txt"), $"WPF render OK; agents={agents.Count}; size={bitmap.PixelWidth}x{bitmap.PixelHeight}; logBytesRead={tracker.BytesRead}");
        return 0;
    }
}

internal sealed class OfficeWindow : Window
{
    private readonly string settingsPath = Path.Combine(TrackerOptions.OutputDirectory, "settings.json");
    private Settings settings;
    private readonly OfficeView view = new();
    private readonly TextBlock status = new() { Margin = new(18, 10, 18, 10), Foreground = Brushes.DarkSlateGray, FontSize = 12 };
    private readonly TextBlock counts = new() { VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.DarkSlateGray, Margin = new(16, 0, 16, 0) };
    private readonly AgentTracker tracker;
    private readonly OfficeAnimator office = new();
    private readonly WindowModes modes;
    private readonly Forms.NotifyIcon tray;
    private readonly DispatcherTimer timer;
    private IReadOnlyList<AgentInfo> agents = Array.Empty<AgentInfo>();
    private DateTimeOffset previous = DateTimeOffset.UtcNow, nextPoll;
    private bool polling, closed;

    public OfficeWindow()
    {
        settings = ReadSettings();
        Title = "Vibedotcom — passive agent office";
        Width = Finite(settings.Width, 1060, 520, 3000); Height = Finite(settings.Height, 740, 420, 2000);
        MinWidth = 520; MinHeight = 420; Background = new SolidColorBrush(Color.FromRgb(245, 237, 221));
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        view.Zoom = Finite(settings.Zoom, 1.25, .5, 4);
        var options = new TrackerOptions
        {
            WorkingWindow = TimeSpan.FromSeconds(Finite(settings.WorkingSeconds, 120, 1, 86400)),
            GoneAfter = TimeSpan.FromMinutes(Finite(settings.GoneMinutes, 30, 1, 10080))
        };
        if (options.GoneAfter <= options.WorkingWindow) options = new();
        tracker = new(options); modes = new(this);
        var root = new DockPanel();
        var bar = new DockPanel { Margin = new(18, 12, 18, 12) };
        var zoom = new StackPanel { Orientation = Orientation.Horizontal };
        var minus = new Button { Content = "Zoom −", Padding = new(10, 4, 10, 4), Margin = new(4, 0, 0, 0) };
        var plus = new Button { Content = "Zoom +", Padding = new(10, 4, 10, 4), Margin = new(4, 0, 0, 0) };
        minus.Click += (_, _) => Zoom(-.25); plus.Click += (_, _) => Zoom(.25); zoom.Children.Add(minus); zoom.Children.Add(plus);
        DockPanel.SetDock(zoom, Dock.Right); bar.Children.Add(zoom);
        DockPanel.SetDock(counts, Dock.Right); bar.Children.Add(counts);
        bar.Children.Add(new TextBlock { Text = "Vibedotcom", FontSize = 21, FontWeight = FontWeights.SemiBold, Foreground = Brushes.DarkSlateGray });
        DockPanel.SetDock(bar, Dock.Top); root.Children.Add(bar);
        DockPanel.SetDock(status, Dock.Bottom); root.Children.Add(status);
        var scroll = new ScrollViewer { Content = view, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        root.Children.Add(scroll); Content = root;
        PreviewMouseWheel += (_, e) => { Zoom(e.Delta > 0 ? .25 : -.25); e.Handled = true; };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key is Key.Add or Key.OemPlus) { Zoom(.25); e.Handled = true; }
            if (e.Key is Key.Subtract or Key.OemMinus) { Zoom(-.25); e.Handled = true; }
        };
        ContextMenu = BuildContextMenu();
        tray = new Forms.NotifyIcon { Text = "Vibedotcom — passive agent office", Icon = System.Drawing.SystemIcons.Application, Visible = true };
        tray.DoubleClick += (_, _) => Dispatcher.Invoke(Recover);
        tray.ContextMenuStrip = BuildTrayMenu();
        SourceInitialized += (_, _) => { modes.Attach(); modes.Apply(settings.Mode, settings.ClickThrough); };
        timer = new(TimeSpan.FromMilliseconds(100), DispatcherPriority.Background, (_, _) => Animate(), Dispatcher);
        Closed += (_, _) => { closed = true; timer.Stop(); tray.Visible = false; tray.Dispose(); Save(); };
        status.Text = "Reading recent sessions…  Right-click for window modes.";
    }

    private static double Finite(double value, double fallback, double min, double max) => double.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
    private Settings ReadSettings()
    {
        try
        {
            using var file = new FileStream(settingsPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return JsonSerializer.Deserialize<Settings>(file, StateJson.Options) ?? new();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }

    private void Save()
    {
        settings = settings with { Width = Width, Height = Height, Zoom = view.Zoom };
        try { StateJson.WriteAtomic(settingsPath, JsonSerializer.SerializeToUtf8Bytes(settings, StateJson.Options)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { status.Text = "Cannot save settings: " + e.Message; }
    }
    private void Zoom(double amount) { view.Zoom += amount; Save(); }
    private void SetMode(string mode) { settings = settings with { Mode = mode }; modes.Apply(mode, settings.ClickThrough); Save(); }
    private void ToggleClickThrough() { settings = settings with { ClickThrough = !settings.ClickThrough }; modes.Apply(settings.Mode, settings.ClickThrough); Save(); }
    private void Recover() { settings = settings with { Mode = "normal", ClickThrough = false }; modes.Apply("normal", false); Show(); WindowState = WindowState.Normal; Activate(); Save(); }

    private ContextMenu BuildContextMenu()
    {
        var menu = new ContextMenu();
        void Item(string title, Action action) { var item = new MenuItem { Header = title }; item.Click += (_, _) => action(); menu.Items.Add(item); }
        Item("Normal window", () => SetMode("normal")); Item("Always on top", () => SetMode("topmost"));
        Item("Desktop level (behind windows)", () => SetMode("desktop")); Item("Toggle click-through", ToggleClickThrough);
        menu.Items.Add(new Separator()); Item("Restore normal, clickable window", Recover); Item("Exit", Close);
        return menu;
    }

    private Forms.ContextMenuStrip BuildTrayMenu()
    {
        var menu = new Forms.ContextMenuStrip();
        void Item(string title, Action action) => menu.Items.Add(title, null, (_, _) => Dispatcher.Invoke(action));
        Item("Show office (normal, clickable)", Recover); menu.Items.Add(new Forms.ToolStripSeparator());
        Item("Normal window", () => SetMode("normal")); Item("Always on top", () => SetMode("topmost"));
        Item("Desktop level (behind windows)", () => SetMode("desktop")); Item("Toggle click-through", ToggleClickThrough);
        Item("Zoom in", () => Zoom(.25)); Item("Zoom out", () => Zoom(-.25)); menu.Items.Add(new Forms.ToolStripSeparator()); Item("Exit", Close);
        return menu;
    }

    private void Animate()
    {
        if (closed) return;
        var now = DateTimeOffset.UtcNow;
        var views = office.Update(agents, now, (now - previous).TotalSeconds);
        view.Update(office.Layout, views); previous = now;
        if (!polling && now >= nextPoll) { nextPoll = now.AddSeconds(1); _ = PollAsync(now); }
    }
    private async Task PollAsync(DateTimeOffset now)
    {
        polling = true;
        try
        {
            var current = await Task.Run(() => tracker.Poll(now));
            if (closed) return;
            agents = current;
            counts.Text = string.Join("   ·   ", Enum.GetValues<Provider>().Select(p => $"{p} {agents.Count(a => a.Provider == p && a.State != AgentState.Gone)}"));
            status.Text = tracker.Warnings.Count > 0 ? string.Join("  ", tracker.Warnings) : "Observation only  ·  Supervisors wear ties  ·  Wheel or +/− to zoom  ·  Right-click for window modes";
        }
        catch (Exception e) { status.Text = "Reading logs failed: " + e.Message; }
        finally { polling = false; }
    }
}
