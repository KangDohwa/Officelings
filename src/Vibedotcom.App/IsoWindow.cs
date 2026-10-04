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
internal sealed partial class IsoWindow : Window
{
    private readonly IIsoOffice office;
    private IIsoSheetSource sheets;
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
    private DateTimeOffset saveAfter,nextPoll;
    private readonly DateTimeOffset epoch;
    private string warning="";
    internal IsoView View => view;
    internal WindowModes Modes => modes;

    internal IsoWindow(IIsoOffice office,IIsoSheetSource sheets,Settings settings,bool demo,bool qa,double quitAfter,
        AppStorage storage,string? trackerHome,bool preview,IReadOnlyList<AgentInfo>? currentAgents=null,DateTimeOffset? demoEpoch=null,
        IsoEditorSession? editorSession=null,AgentTracker? retainedTracker=null)
    {
        this.office=office; this.sheets=sheets; this.settings=settings.Normalize();
        this.demo=demo; this.qa=qa; this.storage=storage; this.trackerHome=trackerHome; this.preview=preview;
        this.editorSession=editorSession; editorSession?.BindRuntime(office as Vibedotcom.Core.Iso.Editing.IIsoEditorRuntime);
        if (editorSession!=null) editorSession.Privacy=PrivacyModes.Parse(this.settings.PrivacyMode ?? "off");
        epoch=demoEpoch ?? DateTimeOffset.UtcNow;
        if(office is IsoOffice engine) engine.PetsEnabled=settings.Pets=="on";
        view=new(office,sheets,storage.Isolated ? Path.Combine(storage.Home,"brand") : null)
        { TagMode=settings.TagMode,TransparentBackground=settings.TransparentBackground };
        modes=new(this,qa);
        view.Pointer=()=>qa || closed || suspended ? null : modes.Cursor(view);
        tracker=retainedTracker ?? new(new() { Home=trackerHome ?? (storage.Isolated ? storage.Home : new TrackerOptions().Home),
            PrivacyMode=demo ? PrivacyMode.Off : PrivacyModes.Parse(settings.PrivacyMode ?? "off"),
            WorkingWindow=TimeSpan.FromSeconds(settings.WorkingSeconds),LeaveAfterIdle=TimeSpan.FromMinutes(settings.LeaveAfterIdleMinutes),
            LeaveAfterWaiting=TimeSpan.FromMinutes(settings.LeaveAfterWaitingMinutes) });
        Title=demo ? "Vibedotcom — ISO demo office" : "Vibedotcom — ISO agent office";
        Width=settings.Width; Height=settings.Height; MinWidth=400; MinHeight=300;
        AllowsTransparency=EditorSettings.Mode=="desktop" || EditorSettings.TransparentBackground;
        WindowStyle=AllowsTransparency || EditorSettings.Borderless ? WindowStyle.None : WindowStyle.SingleBorderWindow;
        Background=AllowsTransparency ? Brushes.Transparent : new SolidColorBrush(OfficeView.BackgroundColor);
        ShowActivated=!qa && EditorSettings.Mode!="desktop" && !EditorSettings.ClickThrough;
        ShowInTaskbar=!qa && EditorSettings.Mode!="desktop"; Topmost=!qa && EditorSettings.Mode=="topmost";
        WindowStartupLocation=WindowStartupLocation.CenterScreen;
        if (settings.Left is {} left && settings.Top is {} top)
        { WindowStartupLocation=WindowStartupLocation.Manual; Place(WindowPlacement.Clamp(new(left,top,Width,Height),WorkingAreas())); }
        if (qa) { WindowStartupLocation=WindowStartupLocation.Manual; Left=-30000; Top=-30000; }
        var root=new DockPanel { Background=settings.TransparentBackground ? Brushes.Transparent : new SolidColorBrush(OfficeView.BackgroundColor) };
        var title=new TextBlock { Text=demo ? "Vibedotcom  ·  ISO demo" : "Vibedotcom  ·  ISO",Margin=new(16,9,16,9),FontSize=17,FontWeight=FontWeights.SemiBold,Foreground=Brushes.Wheat };
        DockPanel.SetDock(title,Dock.Top); root.Children.Add(title);
        DockPanel.SetDock(status,Dock.Bottom); root.Children.Add(status); root.Children.Add(view); Content=root;
        if (editorSession?.Editing==true) AttachEditorPanel();
        agents=currentAgents ?? Array.Empty<AgentInfo>();
        view.SetZoom((int)Math.Round(settings.Zoom));
        if (demo && currentAgents==null)
            for (var frame=0;frame<180;frame++)
            { var t=-18+frame*.1; agents=DemoScenario.Agents(epoch,t); view.Update(agents,epoch.AddSeconds(t),.1); }
        view.AdvanceTo(agents,DateTimeOffset.UtcNow);
        ContextMenu=BuildContextMenu();
        PreviewMouseWheel+=(_,e)=> { if (editorPanel?.IsMouseOver==true) return; Zoom(e.Delta>0 ? 1 : -1); e.Handled=true; };
        PreviewKeyDown+=(_,e)=>
        {
            if (HandleEditorShortcut(e) || IsoView.IsTextInput(e.OriginalSource as DependencyObject)) return;
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
            DetachEditorPanel();
            closed=true; maintenance.Stop(); view.SetAnimationEnabled(false); view.HideTooltip();
            if (tray!=null) { tray.Visible=false; tray.Dispose(); }
            if (!polling && ownsTracker) tracker.Dispose(); Save();
        };
    }
    private void ApplyModes()
    {
        var effective=EditorSettings;
        view.TransparentBackground=effective.TransparentBackground;
        modes.Apply(effective.Mode,effective.ClickThrough,effective.Borderless,effective.TransparentBackground,effective.Opacity);
        view.HideTooltip();
    }
    private void Save()
    {
        if (demo || qa) return;
        var bounds=WindowState==WindowState.Normal ? modes.ScreenBounds : RestoreBounds;
        if (bounds.IsEmpty) return;
        settings=(settings with { Left=bounds.Left,Top=bounds.Top,Width=bounds.Width,Height=bounds.Height,Zoom=view.PreferredZoom }).Normalize();
        try { settings.Write(storage.SettingsPath); }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException) { warning="Cannot save settings: "+PrivacyModes.Diagnostic(e,tracker.PrivacyMode); }
    }
    private void Zoom(int direction) { view.ChangeZoom(direction); Save(); }
    private void SetTags(string mode) { settings=settings with { TagMode=mode }; view.TagMode=mode; Save(); }
    private void TogglePets()
    {
        settings=settings with { Pets=settings.Pets=="on" ? "off" : "on" };
        if(office is IsoOffice engine) engine.PetsEnabled=settings.Pets=="on";
        view.AdvanceTo(agents,DateTimeOffset.UtcNow); Save();
    }
    private void Fit() { view.SetZoom(0); Save(); }
    private void Change(Settings next)
    {
        settings=next;
        if (AllowsTransparency!=(EditorSettings.Mode=="desktop" || EditorSettings.TransparentBackground)) Reopen();
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
        DetachEditorPanel();
        ownsTracker=false;
        var replacement=new IsoWindow(office,sheets,next,demo,qa,0,storage,trackerHome,preview,agents,epoch,editorSession,tracker);
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
        ("Isometric view",SwitchFlat,()=>true),
        ("Edit office",EnterEditing,()=>editorSession?.Editing==true),
        ("Done editing",DoneEditing,null),
        ("Normal",()=>Change(settings with { Mode="normal" }),()=>settings.Mode=="normal"),
        ("Always on top",()=>Change(settings with { Mode="topmost" }),()=>settings.Mode=="topmost"),
        ("Desktop level",()=>Change(settings with { Mode="desktop" }),()=>settings.Mode=="desktop"),
        ("Click-through",()=>Change(settings with { ClickThrough=!settings.ClickThrough }),()=>settings.ClickThrough),
        ("Borderless",()=>Change(settings with { Borderless=!settings.Borderless }),()=>settings.Borderless),
        ("Transparent background",()=>Change(settings with { TransparentBackground=!settings.TransparentBackground }),()=>settings.TransparentBackground),
        ("Pets",TogglePets,()=>settings.Pets=="on"),
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
        AddPrivacyMenu(menu);
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
        AddPrivacyMenu(menu);
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
        if (!value) view.AdvanceTo(agents,DateTimeOffset.UtcNow);
        view.SetAnimationEnabled(!value);
    }
    private void RefreshHover()
    {
        if (qa || closed || suspended || view.Editing) return;
        view.PollHover(modes.Cursor(view),DateTimeOffset.UtcNow);
    }
    private void Maintain()
    {
        if (closed) return;
        var now=DateTimeOffset.UtcNow;
        var hidden=WindowState==WindowState.Minimized || modes.AnimationSuppressed;
        SetSuspended(hidden);
        if (modes.DesktopHostLost) ApplyModes();
        if (demo && !hidden) { agents=DemoScenario.Agents(epoch,(now-epoch).TotalSeconds); view.AdvanceTo(agents,now); }
        if (!qa && (view.HasHoverTarget || modes.ClickThrough || modes.Mode=="desktop")) RefreshHover();
        var message=warning.Length>0 ? warning : preview ? "Handmade source-art preview · ISO simulation pending · wheel / + / − · drag to pan"
            : $"{agents.Count(a=>a.State!=AgentState.Gone)} agents · {view.Zoom}× · wheel / + / − · drag to pan · right-click for modes";
        if (status.Text!=message) status.Text=message;
        if (!demo && !polling && now>=nextPoll)
        { nextPoll=now.AddSeconds(agents.Count==0 ? 3 : 1); _=PollAsync(); }
        if (saveAfter!=default && now>=saveAfter) { saveAfter=default; Save(); }
        // A static scene needs only source discovery/host maintenance. Keep
        // hover and demo input responsive while avoiding one-second idle wakes.
        var interval=TimeSpan.FromSeconds(!demo && !double.IsFinite(office.NextChangeSeconds) && !view.HasHoverTarget && !view.TagsAnimating && !view.TooltipPending ? 3 : 1);
        if (maintenance.Interval!=interval) maintenance.Interval=interval;
    }
    private async Task PollAsync()
    {
        polling=true;
        try
        {
            var current=await Task.Run(()=> { lock(tracker) return tracker.Poll(DateTimeOffset.UtcNow); });
            if (closed) return;
            if (!agents.SequenceEqual(current))
            {
                agents=current;
                if (!suspended) view.AdvanceTo(agents,DateTimeOffset.UtcNow);
            }
            warning=tracker.Warnings.Count>0 ? string.Join("  ",PrivacyModes.Diagnostics(tracker.Warnings,tracker.PrivacyMode)) : "";
        }
        catch(Exception e) { warning="Reading logs failed: "+PrivacyModes.Diagnostic(e,tracker.PrivacyMode); }
        finally { polling=false; if (closed && ownsTracker) tracker.Dispose(); }
    }
}
