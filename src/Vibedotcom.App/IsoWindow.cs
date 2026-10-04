using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Vibedotcom.Core;
using Vibedotcom.Core.Iso;
using Forms=System.Windows.Forms;

namespace Vibedotcom.App;

// Separate host keeps the flat host and its rendering/output path untouched.
// Native modes, passive popup rules, storage and tray recovery are shared.
internal sealed class IsoWindow : Window
{
    private readonly IIsoOffice office;
    private readonly IIsoSheetSource sheets;
    private readonly AppStorage storage;
    private readonly bool demo,qa,preview;
    private readonly string? trackerHome;
    private readonly IsoView view;
    private readonly WindowModes modes;
    private readonly AgentTracker tracker;
    private readonly DispatcherTimer maintenance;
    private readonly TextBlock status=new() { Margin=new(16,7,16,7),FontSize=11,Foreground=Brushes.Wheat };
    private Forms.NotifyIcon? tray;
    private Settings settings;
    private IReadOnlyList<AgentInfo> agents=Array.Empty<AgentInfo>();
    private bool closed,polling,suspended;
    private DateTimeOffset previous=DateTimeOffset.UtcNow,saveAfter;
    private string warning="";
    internal IsoView View => view;
    internal WindowModes Modes => modes;

    internal IsoWindow(IIsoOffice office,IIsoSheetSource sheets,Settings settings,bool demo,bool qa,double quitAfter,
        AppStorage storage,string? trackerHome,bool preview,IReadOnlyList<AgentInfo>? currentAgents=null)
    {
        this.office=office; this.sheets=sheets; this.settings=settings.Normalize();
        this.demo=demo; this.qa=qa; this.storage=storage; this.trackerHome=trackerHome; this.preview=preview;
        view=new(office,sheets,storage.Isolated ? Path.Combine(storage.Home,"brand") : null)
        { TagMode=settings.TagMode,TransparentBackground=settings.TransparentBackground };
        modes=new(this,qa);
        view.Pointer=()=>qa || closed || suspended ? null : modes.Cursor(view);
        tracker=new(new() { Home=trackerHome ?? (storage.Isolated ? storage.Home : new TrackerOptions().Home),
            WorkingWindow=TimeSpan.FromSeconds(settings.WorkingSeconds),LeaveAfterIdle=TimeSpan.FromMinutes(settings.LeaveAfterIdleMinutes),
            LeaveAfterWaiting=TimeSpan.FromMinutes(settings.LeaveAfterWaitingMinutes) });
        Title=demo ? "Vibedotcom — ISO demo office" : "Vibedotcom — ISO agent office";
        Width=settings.Width; Height=settings.Height; MinWidth=400; MinHeight=300;
        AllowsTransparency=settings.Mode=="desktop" || settings.TransparentBackground;
        WindowStyle=AllowsTransparency || settings.Borderless ? WindowStyle.None : WindowStyle.SingleBorderWindow;
        Background=AllowsTransparency ? Brushes.Transparent : new SolidColorBrush(OfficeView.BackgroundColor);
        ShowActivated=!qa && settings.Mode!="desktop" && !settings.ClickThrough;
        ShowInTaskbar=!qa && settings.Mode!="desktop"; Topmost=!qa && settings.Mode=="topmost";
        WindowStartupLocation=WindowStartupLocation.CenterScreen;
        if (settings.Left is {} left && settings.Top is {} top)
        { WindowStartupLocation=WindowStartupLocation.Manual; Place(WindowPlacement.Clamp(new(left,top,Width,Height),WorkingAreas())); }
        if (qa) { WindowStartupLocation=WindowStartupLocation.Manual; Left=-30000; Top=-30000; }
        var root=new DockPanel { Background=settings.TransparentBackground ? Brushes.Transparent : new SolidColorBrush(OfficeView.BackgroundColor) };
        var title=new TextBlock { Text=demo ? "Vibedotcom  ·  ISO demo" : "Vibedotcom  ·  ISO",Margin=new(16,9,16,9),FontSize=17,FontWeight=FontWeights.SemiBold,Foreground=Brushes.Wheat };
        DockPanel.SetDock(title,Dock.Top); root.Children.Add(title);
        DockPanel.SetDock(status,Dock.Bottom); root.Children.Add(status); root.Children.Add(view); Content=root;
        agents=currentAgents ?? (demo ? IsoPreviewOffice.Agents(DateTimeOffset.UtcNow) : Array.Empty<AgentInfo>());
        view.SetZoom((int)Math.Round(settings.Zoom)); view.Update(agents,DateTimeOffset.UtcNow,0);
        ContextMenu=BuildContextMenu();
        PreviewMouseWheel+=(_,e)=> { Zoom(e.Delta>0 ? 1 : -1); e.Handled=true; };
        PreviewKeyDown+=(_,e)=>
        {
            if (e.Key is Key.Add or Key.OemPlus) { Zoom(1); e.Handled=true; }
            if (e.Key is Key.Subtract or Key.OemMinus) { Zoom(-1); e.Handled=true; }
        };
        SourceInitialized+=(_,_)=>modes.Attach();
        ContentRendered+=(_,_)=>
        {
            ApplyModes();
            if (!qa) Dispatcher.BeginInvoke(DispatcherPriority.Background,new Action(CreateTray));
        };
        maintenance=new(DispatcherPriority.Background,Dispatcher) { Interval=TimeSpan.FromSeconds(1) };
        maintenance.Tick+=(_,_)=>Maintain();
        Loaded+=(_,_)=> { maintenance.Start(); view.SetAnimationEnabled(true); Maintain(); };
        StateChanged+=(_,_)=>SetSuspended(WindowState==WindowState.Minimized);
        MouseMove+=(_,_)=>RefreshHover(); MouseLeave+=(_,_)=>view.PollHover(null,DateTimeOffset.UtcNow);
        LocationChanged+=(_,_)=>saveAfter=DateTimeOffset.UtcNow.AddSeconds(1);
        SizeChanged+=(_,_)=>saveAfter=DateTimeOffset.UtcNow.AddSeconds(1);
        if (double.IsFinite(quitAfter) && quitAfter>0)
        {
            var stop=new DispatcherTimer { Interval=TimeSpan.FromSeconds(quitAfter) };
            stop.Tick+=(_,_)=> { stop.Stop(); Close(); }; stop.Start(); Closed+=(_,_)=>stop.Stop();
        }
        Closed+=(_,_)=>
        {
            closed=true; maintenance.Stop(); view.SetAnimationEnabled(false); view.HideTooltip();
            if (tray!=null) { tray.Visible=false; tray.Dispose(); }
            if (!polling) tracker.Dispose(); Save();
        };
    }
    private void ApplyModes()
    {
        view.TransparentBackground=settings.TransparentBackground;
        modes.Apply(settings.Mode,settings.ClickThrough,settings.Borderless,settings.TransparentBackground,settings.Opacity);
        view.HideTooltip();
    }
    private void Save()
    {
        if (demo || qa) return;
        var bounds=WindowState==WindowState.Normal ? modes.ScreenBounds : RestoreBounds;
        if (bounds.IsEmpty) return;
        settings=(settings with { View="iso",Left=bounds.Left,Top=bounds.Top,Width=bounds.Width,Height=bounds.Height,Zoom=view.PreferredZoom }).Normalize();
        try { settings.Write(storage.SettingsPath); }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException) { warning="Cannot save settings: "+e.Message; }
    }
    private void Zoom(int direction) { view.ChangeZoom(direction); Save(); }
    private void SetTags(string mode) { settings=settings with { TagMode=mode }; view.TagMode=mode; Save(); }
    private void Fit() { view.SetZoom(0); Save(); }
    private void Change(Settings next)
    {
        settings=next;
        if (AllowsTransparency!=(settings.Mode=="desktop" || settings.TransparentBackground)) Reopen();
        else
        {
            if (Content is Panel root) root.Background=settings.TransparentBackground ? Brushes.Transparent : new SolidColorBrush(OfficeView.BackgroundColor);
            ApplyModes(); Save();
        }
    }
    private void Reopen(Rect? recovered=null)
    {
        var bounds=recovered ?? modes.ScreenBounds;
        var next=settings with { Left=bounds.Left,Top=bounds.Top,Width=bounds.Width,Height=bounds.Height,Zoom=view.PreferredZoom };
        var replacement=new IsoWindow(office,sheets,next,demo,qa,0,storage,trackerHome,preview,agents);
        Application.Current.MainWindow=replacement; replacement.Show(); Close();
    }
    private void Recover()
    {
        settings=settings with { Mode="normal",ClickThrough=false,Borderless=false,TransparentBackground=false };
        var bounds=WindowPlacement.Clamp(modes.ScreenBounds,WorkingAreas());
        if (AllowsTransparency) { Reopen(bounds); return; }
        ApplyModes(); Show(); WindowState=WindowState.Normal; Place(bounds); Save();
    }
    private Rect[] WorkingAreas()
    {
        var dpi=VisualTreeHelper.GetDpi(this);
        return Forms.Screen.AllScreens.Select(s=>new Rect(s.WorkingArea.Left/dpi.DpiScaleX,s.WorkingArea.Top/dpi.DpiScaleY,
            s.WorkingArea.Width/dpi.DpiScaleX,s.WorkingArea.Height/dpi.DpiScaleY)).ToArray();
    }
    private void Place(Rect bounds) { Left=bounds.Left; Top=bounds.Top; Width=bounds.Width; Height=bounds.Height; }
    private (string Title,Action Action,Func<bool>? Check)[] MenuItems() =>
    [
        ("Normal",()=>Change(settings with { Mode="normal" }),()=>settings.Mode=="normal"),
        ("Always on top",()=>Change(settings with { Mode="topmost" }),()=>settings.Mode=="topmost"),
        ("Desktop level",()=>Change(settings with { Mode="desktop" }),()=>settings.Mode=="desktop"),
        ("Click-through",()=>Change(settings with { ClickThrough=!settings.ClickThrough }),()=>settings.ClickThrough),
        ("Borderless",()=>Change(settings with { Borderless=!settings.Borderless }),()=>settings.Borderless),
        ("Transparent background",()=>Change(settings with { TransparentBackground=!settings.TransparentBackground }),()=>settings.TransparentBackground),
        ("Opacity 100%",()=>Change(settings with { Opacity=1 }),()=>settings.Opacity==1),
        ("Opacity 85%",()=>Change(settings with { Opacity=.85 }),()=>settings.Opacity==.85),
        ("Opacity 70%",()=>Change(settings with { Opacity=.7 }),()=>settings.Opacity==.7),
        ("Opacity 50%",()=>Change(settings with { Opacity=.5 }),()=>settings.Opacity==.5),
        ("Opacity 30%",()=>Change(settings with { Opacity=.3 }),()=>settings.Opacity==.3),
        ("Fit office to window",Fit,()=>view.PreferredZoom==0),
        ("Show normal, clickable office",Recover,null),
        ("Exit",Close,null)
    ];
    private ContextMenu BuildContextMenu()
    {
        var menu=new ContextMenu(); var tags=new MenuItem { Header="Name tags" }; menu.Items.Add(tags);
        foreach(var mode in new[] {"hover","icon","always"})
        { var item=new MenuItem { Header=mode }; tags.Items.Add(item); item.Click+=(_,_)=>SetTags(mode); menu.Opened+=(_,_)=>item.IsChecked=settings.TagMode==mode; }
        foreach(var entry in MenuItems())
        { var item=new MenuItem { Header=entry.Title }; menu.Items.Add(item); item.Click+=(_,_)=>entry.Action(); menu.Opened+=(_,_)=>item.IsChecked=entry.Check?.Invoke() ?? false; }
        return menu;
    }
    private void CreateTray()
    {
        if (closed || qa || tray!=null) return;
        tray=new() { Text="Vibedotcom — ISO office",Icon=System.Drawing.SystemIcons.Application,Visible=true };
        tray.DoubleClick+=(_,_)=>Dispatcher.Invoke(Recover);
        var menu=new Forms.ContextMenuStrip(); var tags=new Forms.ToolStripMenuItem("Name tags"); menu.Items.Add(tags);
        foreach(var mode in new[] {"hover","icon","always"})
        { var item=new Forms.ToolStripMenuItem(mode); tags.DropDownItems.Add(item); item.Click+=(_,_)=>Dispatcher.Invoke(()=>SetTags(mode)); menu.Opening+=(_,_)=>item.Checked=settings.TagMode==mode; }
        foreach(var entry in MenuItems())
        { var item=new Forms.ToolStripMenuItem(entry.Title); menu.Items.Add(item); item.Click+=(_,_)=>Dispatcher.Invoke(entry.Action); menu.Opening+=(_,_)=>item.Checked=entry.Check?.Invoke() ?? false; }
        tray.ContextMenuStrip=menu;
    }
    private void SetSuspended(bool value)
    {
        if (suspended==value || closed) return;
        suspended=value;
        if (!value) { var now=DateTimeOffset.UtcNow; view.Update(agents,now,Math.Max(0,(now-previous).TotalSeconds)); previous=now; }
        view.SetAnimationEnabled(!value);
    }
    private void RefreshHover()
    {
        if (qa || closed || suspended) return;
        view.PollHover(modes.Cursor(view),DateTimeOffset.UtcNow);
    }
    private void Maintain()
    {
        if (closed) return;
        var now=DateTimeOffset.UtcNow;
        var hidden=WindowState==WindowState.Minimized || modes.AnimationSuppressed;
        SetSuspended(hidden);
        if (modes.DesktopHostLost) ApplyModes();
        if (!qa && (view.HasHoverTarget || modes.ClickThrough || modes.Mode=="desktop")) RefreshHover();
        var message=warning.Length>0 ? warning : preview ? "Handmade source-art preview · ISO simulation pending · wheel / + / − · drag to pan"
            : $"{agents.Count(a=>a.State!=AgentState.Gone)} agents · {view.Zoom}× · wheel / + / − · drag to pan · right-click for modes";
        if (status.Text!=message) status.Text=message;
        if (!demo && !polling) _=PollAsync();
        if (saveAfter!=default && now>=saveAfter) { saveAfter=default; Save(); }
    }
    private async Task PollAsync()
    {
        polling=true;
        try
        {
            var current=await Task.Run(()=>tracker.Poll(DateTimeOffset.UtcNow));
            if (closed) return;
            if (!agents.SequenceEqual(current))
            {
                agents=current;
                if (!suspended) { var now=DateTimeOffset.UtcNow; view.Update(agents,now,(now-previous).TotalSeconds); previous=now; }
            }
            warning=tracker.Warnings.Count>0 ? string.Join("  ",tracker.Warnings) : "";
        }
        catch(Exception e) { warning="Reading logs failed: "+e.Message; }
        finally { polling=false; if (closed) tracker.Dispose(); }
    }
}