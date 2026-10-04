using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Vibedotcom.Core;
using Vibedotcom.Core.Iso;

namespace Vibedotcom.App;

internal sealed partial class IsoView : FrameworkElement
{
    private readonly IIsoOffice office;
    private readonly IIsoSheetSource sheets;
    private readonly ContainerVisual root = new(), sceneVisual = new();
    private readonly DrawingVisual tagsVisual = new();
    private readonly Dictionary<(bool Static,string Id,int Occurrence), Segment> retained = new();
    private readonly List<Segment> order = new();
    private readonly List<(bool Static,string Id,int Occurrence)> expired = new();
    private readonly Dictionary<(bool,string),int> occurrences = new();
    private readonly DispatcherTimer animation;
    private IsoScene scene = new(1,1,Array.Empty<IsoDrawItem>(),Array.Empty<IsoLabel>(),long.MinValue);
    private IReadOnlyList<AgentInfo> agents = Array.Empty<AgentInfo>();
    private DateTimeOffset previous;
    private bool running, transparent;
    private int preferredZoom, zoom = 1;
    private Vector physicalPan;
    private Point? drag;
    private Matrix camera = Matrix.Identity;
    private readonly SolidColorBrush background = new(OfficeView.BackgroundColor);

    private sealed class Segment((bool Static,string Id,int Occurrence) key)
    {
        internal readonly (bool Static,string Id,int Occurrence) Key = key;
        internal readonly DrawingVisual Visual = new();
        internal IsoDrawItem[] Items = Array.Empty<IsoDrawItem>();
        internal BitmapSource? Bitmap;
        internal int Zoom;
        internal bool Used;
    }

    public int PreferredZoom => preferredZoom;
    public int Zoom => zoom;
    public bool TransparentBackground
    {
        get => transparent;
        set { if (transparent==value) return; transparent=value; InvalidateVisual(); }
    }
    internal long Revision => scene.Revision;
    internal IsoScene Scene => scene;
    internal int SceneRenderCount { get; private set; }
    internal int StaticBuildCount { get; private set; }
    internal int DynamicRenderCount { get; private set; }
    internal int AnimationTickCount { get; private set; }
    internal bool AnimationTimerEnabled => animation.IsEnabled;
    internal Func<Point?>? Pointer { get; set; }
    internal int StaticBitmapCount => retained.Values.Count(s=>s.Bitmap!=null);
    internal Matrix Camera => camera;
    protected override int VisualChildrenCount => 1;
    protected override Visual GetVisualChild(int index) => index==0 ? root : throw new ArgumentOutOfRangeException(nameof(index));

    internal IsoView(IIsoOffice office, IIsoSheetSource sheets, string? brandDirectory = null)
    {
        this.office=office; this.sheets=sheets; brandIcons=new(brandDirectory);
        root.Children.Add(sceneVisual); root.Children.Add(tagsVisual); AddVisualChild(root);
        background.Freeze();
        RenderOptions.SetBitmapScalingMode(this,BitmapScalingMode.NearestNeighbor);
        RenderOptions.SetBitmapScalingMode(root,BitmapScalingMode.NearestNeighbor);
        RenderOptions.SetEdgeMode(this,EdgeMode.Aliased);
        TextOptions.SetTextRenderingMode(this,TextRenderingMode.Aliased);
        TextOptions.SetTextFormattingMode(this,TextFormattingMode.Display);
        SnapsToDevicePixels=true; ClipToBounds=true;
        animation=new(DispatcherPriority.Background,Dispatcher);
        animation.Tick+=(_,_)=>
        {
            animation.Stop(); AnimationTickCount++;
            var now=DateTimeOffset.UtcNow;
            if (double.IsFinite(office.NextChangeSeconds)) Update(agents,now,Math.Max(0,(now-previous).TotalSeconds));
            else previous=now;
            if (Pointer!=null && HasHoverTarget) PollHover(Pointer(),now);
            AdvanceHover(now); ScheduleAnimation(now);
        };
        SizeChanged+=(_,_)=> { UpdateCamera(); InvalidateVisual(); };
        MouseLeftButtonDown+=(_,e)=> { drag=e.GetPosition(this); CaptureMouse(); HideTooltip(); };
        MouseMove+=(_,e)=>
        {
            var point=e.GetPosition(this);
            if (drag is {} old) { PanBy(point-old); drag=point; }
            else PollHover(point,DateTimeOffset.UtcNow);
        };
        MouseLeftButtonUp+=(_,_)=> { drag=null; ReleaseMouseCapture(); };
        LostMouseCapture+=(_,_)=>drag=null;
        MouseLeave+=(_,_)=>PollHover(null,DateTimeOffset.UtcNow);
        Unloaded+=(_,_)=> { SetAnimationEnabled(false); HideTooltip(); };
    }

    internal void Update(IReadOnlyList<AgentInfo> people, DateTimeOffset now, double deltaSeconds)
    {
        previous=now;
        Present(office.Update(people,now,deltaSeconds),people);
        ScheduleAnimation(now);
    }

    // Input polling and the animation timer share one clock; never advance the
    // same wall-clock interval once in each host timer.
    internal void AdvanceTo(IReadOnlyList<AgentInfo> people,DateTimeOffset now) =>
        Update(people,now,previous==default ? 0 : Math.Max(0,(now-previous).TotalSeconds));

    internal bool Present(IsoScene next, IReadOnlyList<AgentInfo>? people = null)
    {
        var metadataChanged=people!=null && !ReferenceEquals(agents,people);
        if (metadataChanged) agents=people!;
        if (next.Revision==scene.Revision) { if (metadataChanged) RebuildTags(); return false; }
        if (next.Width<=0 || next.Height<=0) throw new ArgumentException("Iso scene bounds must be positive.");
        var geometryChanged=next.Width!=scene.Width || next.Height!=scene.Height;
        scene=next;
        if (people!=null) agents=people;
        RebuildScene(); RebuildTags();
        if (geometryChanged) { UpdateCamera(); InvalidateVisual(); }
        return true;
    }

    // Cache only consecutive static items. Static desk/chair parts must stay on
    // their original side of each body in the engine's mixed depth queue.
    private bool Static(IsoDrawItem item) => item.Layer is IsoLayer.Floor or IsoLayer.Wall ||
        item.Layer is IsoLayer.Furniture or IsoLayer.Shadow && sheets.GetCell(item.SheetId,item.Clip,item.Frame).Frames==1;
    private void RebuildScene()
    {
        SceneRenderCount++;
        foreach (var value in retained.Values) value.Used=false;
        order.Clear(); occurrences.Clear();
        for (var start=0;start<scene.Items.Count;)
        {
            var isStatic=Static(scene.Items[start]); var end=start+1;
            if (isStatic) while (end<scene.Items.Count && Static(scene.Items[end])) end++;
            var identity=(isStatic,scene.Items[start].InstanceId);
            var occurrence=occurrences.GetValueOrDefault(identity); occurrences[identity]=occurrence+1;
            var key=(isStatic,scene.Items[start].InstanceId,occurrence);
            if (!retained.TryGetValue(key,out var segment))
            {
                retained[key]=segment=new(key);
                RenderOptions.SetBitmapScalingMode(segment.Visual,BitmapScalingMode.NearestNeighbor);
                RenderOptions.SetEdgeMode(segment.Visual,EdgeMode.Aliased);
            }
            var changed=segment.Items.Length!=end-start || segment.Zoom!=zoom;
            if (!changed) for (var i=start;i<end;i++) if (segment.Items[i-start]!=scene.Items[i]) { changed=true; break; }
            if (changed)
            {
                segment.Items=new IsoDrawItem[end-start];
                for (var i=start;i<end;i++) segment.Items[i-start]=scene.Items[i];
                segment.Zoom=zoom;
                if (isStatic) BuildStatic(segment); else DrawDynamic(segment);
            }
            segment.Used=true; order.Add(segment); start=end;
        }
        expired.Clear();
        foreach (var (key,value) in retained) if (!value.Used) expired.Add(key);
        foreach (var key in expired) retained.Remove(key);
        var reordered=sceneVisual.Children.Count!=order.Count;
        if (!reordered) for (var i=0;i<order.Count;i++) if (!ReferenceEquals(sceneVisual.Children[i],order[i].Visual)) { reordered=true; break; }
        if (reordered) { sceneVisual.Children.Clear(); foreach (var segment in order) sceneVisual.Children.Add(segment.Visual); }
    }

    private void BuildStatic(Segment segment)
    {
        var bounds=Rect.Empty;
        foreach (var item in segment.Items)
        {
            var cell=sheets.GetCell(item.SheetId,item.Clip,item.Frame);
            bounds.Union(new Rect(item.X,item.Y,cell.Width,cell.Height));
        }
        var visual=new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual,BitmapScalingMode.NearestNeighbor);
        RenderOptions.SetEdgeMode(visual,EdgeMode.Aliased);
        using (var dc=visual.RenderOpen())
        {
            dc.PushTransform(new MatrixTransform(1,0,0,1,-bounds.X,-bounds.Y));
            foreach (var item in segment.Items) DrawItem(dc,item);
            dc.Pop();
        }
        // Rasterize at source resolution once. Pre-scaling a cache and drawing
        // it back into native coordinates can filter it before the root zoom.
        var bitmap=new RenderTargetBitmap((int)bounds.Width,(int)bounds.Height,96,96,PixelFormats.Pbgra32);
        bitmap.Render(visual); bitmap.Freeze(); segment.Bitmap=bitmap;
        using (var dc=segment.Visual.RenderOpen()) dc.DrawImage(bitmap,bounds);
        StaticBuildCount++;
    }

    private void DrawDynamic(Segment segment)
    {
        using var dc=segment.Visual.RenderOpen();
        foreach (var item in segment.Items) DrawItem(dc,item);
        DynamicRenderCount++;
    }
    private void DrawItem(DrawingContext dc, IsoDrawItem item)
    {
        var cell=sheets.GetCell(item.SheetId,item.Clip,item.Frame);
        if (item.Alpha!=255) dc.PushOpacity(item.Alpha/255d);
        dc.DrawImage(cell.Image,new Rect(item.X,item.Y,cell.Width,cell.Height));
        if (item.Alpha!=255) dc.Pop();
    }

    public void SetZoom(int requested)
    {
        preferredZoom=requested==0 ? 0 : Math.Clamp(requested,1,6); physicalPan=new();
        HideTooltip(); UpdateCamera(); InvalidateVisual();
    }
    public void ChangeZoom(int direction) => SetZoom(zoom+direction);
    public void PanBy(Vector offset)
    {
        var dpi=VisualTreeHelper.GetDpi(this);
        physicalPan+=new Vector(offset.X*dpi.DpiScaleX,offset.Y*dpi.DpiScaleY);
        // Keep fractional input until the camera snaps the final device translation.
        HideTooltip(); UpdateCamera();
    }
    private static double Snap(double value) => Math.Floor(value+.5);
    internal static Matrix PhysicalCamera(int width,int height,double availableWidth,double availableHeight,
        double dpiX,double dpiY,int zoom,Vector pan) => new(zoom/dpiX,0,0,zoom/dpiY,
            Snap((availableWidth*dpiX-width*zoom)/2+pan.X)/dpiX,
            Snap((availableHeight*dpiY-height*zoom)/2+pan.Y)/dpiY);
    private void UpdateCamera()
    {
        var dpi=VisualTreeHelper.GetDpi(this);
        var desired=preferredZoom==0 ? Math.Clamp((int)Math.Floor(Math.Min(ActualWidth*dpi.DpiScaleX/scene.Width,ActualHeight*dpi.DpiScaleY/scene.Height)),1,6) : preferredZoom;
        if (zoom!=desired) { zoom=desired; RebuildScene(); RebuildTags(); }
        var next=PhysicalCamera(scene.Width,scene.Height,ActualWidth,ActualHeight,dpi.DpiScaleX,dpi.DpiScaleY,zoom,physicalPan);
        if (next==camera) return;
        camera=next; var transform=new MatrixTransform(camera); transform.Freeze(); root.Transform=transform;
    }
    protected override void OnDpiChanged(DpiScale oldDpi,DpiScale newDpi)
    { base.OnDpiChanged(oldDpi,newDpi); UpdateCamera(); InvalidateVisual(); }
    protected override void OnRender(DrawingContext dc)
    {
        UpdateCamera();
        dc.DrawRectangle(transparent ? Brushes.Transparent : background,null,new Rect(0,0,ActualWidth,ActualHeight));
    }
    internal Point ScenePoint(Point point)
    {
        var inverse=camera; inverse.Invert(); return inverse.Transform(point);
    }
    internal Point ScreenPoint(Point point) => camera.Transform(point);
    internal IsoHit? HitTestScene(Point screenPoint)
    {
        var point=ScenePoint(screenPoint);
        if (point.X<0 || point.Y<0 || point.X>=scene.Width || point.Y>=scene.Height) return null;
        return office.HitTest((int)Math.Floor(point.X),(int)Math.Floor(point.Y));
    }

    internal void SetAnimationEnabled(bool enabled)
    {
        running=enabled;
        if (!enabled) animation.Stop(); else { previous=DateTimeOffset.UtcNow; ScheduleAnimation(previous); }
    }
    private void ScheduleAnimation(DateTimeOffset now)
    {
        if (!running) { animation.Stop(); return; }
        var delay=office.NextChangeSeconds;
        if (TagsAnimating) delay=Math.Min(delay,1d/30);
        if (TooltipPending) delay=Math.Min(delay,Math.Max(0,(tagHover.Entered.AddMilliseconds(NameTagHover.TooltipDelayMs)-now).TotalSeconds));
        if (!double.IsFinite(delay)) { animation.Stop(); return; }
        var interval=TimeSpan.FromSeconds(Math.Clamp(delay,1d/30,86400));
        if (animation.Interval!=interval) animation.Interval=interval;
        if (!animation.IsEnabled) animation.Start();
    }
}
