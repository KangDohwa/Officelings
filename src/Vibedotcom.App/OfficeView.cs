using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Vibedotcom.Core;

namespace Vibedotcom.App;

internal sealed class OfficeView : FrameworkElement
{
    internal static readonly Color BackgroundColor = (Color)ColorConverter.ConvertFromString("#302d2b");
    private OfficeLayout office = new OfficeAnimator().Layout;
    private IReadOnlyList<AgentView> agents = Array.Empty<AgentView>();
    private readonly Dictionary<string, AgentView> byId = new();
    private readonly AssetPack assets;
    private readonly Dictionary<string, BitmapSource> sheets = new();
    private readonly Dictionary<(string, int, int, int, int), BitmapSource> sprites = new();
    private readonly Dictionary<(string, int, int, int, int), int> spriteTops = new();
    private readonly Dictionary<string, DrawingGroup> furnitureCache = new();
    private IReadOnlyList<FurnitureSprite> furnitureSprites = Array.Empty<FurnitureSprite>();
    private bool debugTooltip;
    public string? DebugTooltipText { get; private set; }
    private readonly Dictionary<string, SolidColorBrush> brushes = new();
    private readonly Dictionary<string, FormattedText> textCache = new();
    private DrawingGroup? room;
    private int ambient = -1;
    private int zoom = 1;
    private Vector pan;
    private Point? drag;
    private bool autoFit = true, transparent;
    private readonly Popup tooltip = new() { AllowsTransparency = true, StaysOpen = true, IsHitTestVisible = false, Placement = PlacementMode.Relative };
    private readonly TextBlock tipTitle = new() { FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White };
    private readonly TextBlock tipBody = new() { FontSize = 12, Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(233,230,215)), TextWrapping = TextWrapping.Wrap, MaxWidth = 310, Margin = new(0,7,0,0) };
    private string? hovered, tipText;
    private DateTimeOffset hoverSince, tipUpdated;
    public int PreferredZoom => autoFit ? 0 : zoom;
    public int Zoom => zoom;
    public bool TransparentBackground
    {
        get => transparent;
        set { if (transparent == value) return; transparent = value; room = null; InvalidateVisual(); }
    }
    public IReadOnlyList<AgentView> Agents => agents;
    public OfficeLayout Layout => office;

    public OfficeView(AssetPack? assets = null)
    {
        this.assets = assets ?? new();
        foreach (var (id, bytes) in this.assets.Images)
        {
            using var stream = new MemoryStream(bytes);
            var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream; image.EndInit(); image.Freeze(); sheets[id] = image;
        }
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.NearestNeighbor);
        RenderOptions.SetEdgeMode(this, EdgeMode.Aliased); SnapsToDevicePixels = true; ClipToBounds = true;
        var stack = new StackPanel(); stack.Children.Add(tipTitle); stack.Children.Add(tipBody);
        tooltip.Child = new Border { Background = Color("#303d40"), CornerRadius = new(8), Padding = new(15,12,15,12), Child = stack };
        tooltip.PlacementTarget = this;
        tooltip.Opened += (_, _) => WindowModes.MakePopupPassive(tooltip.Child);
        SizeChanged += (_, _) => { if (autoFit) Fit(); else InvalidateVisual(); };
        MouseLeftButtonDown += (_, e) => { drag = e.GetPosition(this); CaptureMouse(); HideTooltip(); };
        MouseMove += (_, e) =>
        {
            if (drag is not { } previous) return;
            var point = e.GetPosition(this); PanBy(point - previous); drag = point;
        };
        MouseLeftButtonUp += (_, _) => { drag = null; ReleaseMouseCapture(); };
        LostMouseCapture += (_, _) => drag = null;
        Unloaded += (_, _) => HideTooltip();
    }

    public void SetZoom(int requested)
    {
        autoFit = requested == 0; pan = new();
        if (autoFit) Fit(); else { zoom = Math.Clamp(requested,1,6); InvalidateVisual(); }
        HideTooltip();
    }
    public void ChangeZoom(int direction) => SetZoom(zoom + direction);
    public void PanBy(Vector offset) { pan += offset; ClampPan(); HideTooltip(); InvalidateVisual(); }
    private void Fit()
    {
        zoom = Math.Clamp((int)Math.Floor(Math.Min(Math.Max(1,ActualWidth - 20) / (office.WidthTiles * 16), Math.Max(1,ActualHeight - 16) / (office.HeightTiles * 16))),1,6);
        pan = new(); InvalidateVisual();
    }
    private Vector Origin => new(Math.Round((ActualWidth - office.WidthTiles * 16 * zoom) / 2 + pan.X), Math.Round((ActualHeight - office.HeightTiles * 16 * zoom) / 2 + pan.Y));
    private void ClampPan()
    {
        pan.X = Math.Clamp(pan.X,-Math.Max(0,(office.WidthTiles * 16 * zoom - ActualWidth)/2 + 32),Math.Max(0,(office.WidthTiles * 16 * zoom - ActualWidth)/2 + 32));
        pan.Y = Math.Clamp(pan.Y,-Math.Max(0,(office.HeightTiles * 16 * zoom - ActualHeight)/2 + 32),Math.Max(0,(office.HeightTiles * 16 * zoom - ActualHeight)/2 + 32));
    }
    public void Update(OfficeLayout layout, IReadOnlyList<AgentView> views, DateTimeOffset now, IReadOnlyList<FurnitureSprite>? furnitureFrames = null)
    {
        var changed = !ReferenceEquals(layout,office);
        if (changed) { office = layout; room = null; furnitureCache.Clear(); textCache.Clear(); if (autoFit) Fit(); }
        furnitureFrames ??= Array.Empty<FurnitureSprite>();
        changed |= !furnitureSprites.SequenceEqual(furnitureFrames); furnitureSprites=furnitureFrames;
        var phase = (int)(now.ToUnixTimeMilliseconds() / 500 % 120);
        changed |= phase != ambient || agents.Count != views.Count;
        if (!changed)
            for (var i = 0; i < views.Count; i++)
                if (views[i].Pose != agents[i].Pose || views[i].Agent.Name != agents[i].Agent.Name) { changed = true; break; }
        ambient = phase; agents = views;
        byId.Clear(); foreach (var agent in agents) byId[agent.Agent.Id] = agent;
        if (changed) InvalidateVisual();
    }

    private SolidColorBrush Color(string color)
    {
        if (brushes.TryGetValue(color,out var brush)) return brush;
        brush = (SolidColorBrush)new BrushConverter().ConvertFromString(color)!; brush.Freeze(); brushes.Add(color,brush); return brush;
    }
    private static string ProviderColor(Provider provider) => provider switch { Provider.Codex => "#329b91", Provider.Claude => "#d57e4e", _ => "#827ed1" };
    private void R(DrawingContext dc, string color, double x, double y, double w, double h) => dc.DrawRectangle(Color(color),null,new(x,y,w,h));
    private void Text(DrawingContext dc, string text, double x, double y, double size, string color = "#394940", bool bold = false, double maxWidth = 500)
    {
        var key = $"{text}|{size}|{color}|{bold}|{maxWidth}";
        if (!textCache.TryGetValue(key,out var formatted))
        {
            if (textCache.Count >= 256) textCache.Clear();
            formatted = new(text,CultureInfo.CurrentCulture,FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI"),FontStyles.Normal,bold ? FontWeights.SemiBold : FontWeights.Normal,FontStretches.Normal),
                size,Color(color),VisualTreeHelper.GetDpi(this).PixelsPerDip) { MaxTextWidth = Math.Max(1,maxWidth), MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis };
            textCache[key] = formatted;
        }
        dc.DrawText(formatted,new(x,y));
    }

    private BitmapSource Image(SpriteFrame f)
    {
        var key = (f.SheetId, f.X, f.Y, f.Width, f.Height);
        if (!sprites.TryGetValue(key, out var image))
        {
            image = new CroppedBitmap(sheets[f.SheetId], new(f.X, f.Y, f.Width, f.Height));
            image.Freeze(); sprites[key] = image;
        }
        return image;
    }
    private Rect SpriteBounds(Pose p)
    {
        var s = p.Sprite!;
        return new(Math.Round((p.X+.5)*16-s.AnchorX*s.Scale+s.OffsetX),
            Math.Round((p.Y+1)*16-s.AnchorY*s.Scale+s.OffsetY),s.Width*s.Scale,s.Height*s.Scale);
    }
    private double HeadY(Pose p)
    {
        var s=p.Sprite!; var key=(s.SheetId,s.X,s.Y,s.Width,s.Height);
        if (!spriteTops.TryGetValue(key,out var top))
        {
            var bitmap=new FormatConvertedBitmap(Image(s),PixelFormats.Bgra32,null,0);
            var pixels=new byte[s.Width*s.Height*4]; bitmap.CopyPixels(pixels,s.Width*4,0);
            top=0;
            for (var y=0;y<s.Height;y++)
            {
                if (!Enumerable.Range(0,s.Width).Any(x=>pixels[(y*s.Width+x)*4+3]>32)) continue;
                top=y; break;
            }
            spriteTops[key]=top;
        }
        return SpriteBounds(p).Top+top*s.Scale;
    }
    private void BuildRoom()
    {
        room = new DrawingGroup();
        using (var dc = room.Open())
        {
            if (!transparent)
            {
                for (var y=0; y<office.HeightTiles; y++) for (var x=0; x<office.WidthTiles; x++)
                {
                    var t = assets.Manifest.TileTypes.First(t=>t.Id==office.Tiles![y*office.WidthTiles+x]);
                    if (t.Kind == "void") continue;
                    if (t.SheetId != null && t.Animation != null)
                    {
                        var f = assets.Frame(t.SheetId,t.Animation,"any",0);
                        dc.DrawImage(Image(f),new(x*16,y*16,16,16));
                    }
                    else
                    {
                        R(dc,t.Color,x*16,y*16,16,16);
                        if (t.Kind == "floor")
                        { R(dc,t.Id == 2 ? "#c9d1b8" : "#d9c6a8",x*16,y*16+15,16,1); if ((x+y)%3==0) R(dc,"#d9c6a8",x*16,y*16,1,15); }
                    }
                }
                foreach (var z in office.Zones ?? Array.Empty<Zone>())
                    if (!assets.Manifest.Furniture.Any(f => f.Id == "cozy-desk"))
                    if (z.Name is "workspace" or "pantry")
                    {
                        R(dc,"#eee5d2",z.X*16+24,24,Math.Min(72,z.Width*16-24),11);
                        Text(dc,z.Name.ToUpperInvariant(),z.X*16+26,25,7,"#59654c",true,maxWidth:z.Width*16-28);
                    }
            }
            if (!office.Furniture.Any(f => f.Kind == "door"))
            {
                var door=office.Door;
                R(dc,"#58675b",door.X*16-16,door.Y*16-18,15,46);
                R(dc,"#88a38e",door.X*16-14,door.Y*16-16,11,42);
                R(dc,"#f1d293",door.X*16-6,door.Y*16+8,2,2);
            }
        }
        room.Freeze();
    }
    private void DrawFurniture(DrawingContext dc, Furniture f)
    {
        var variant = assets.Variant(f);
        if (variant.SheetId != null && variant.Animation != null)
        {
            var s = furnitureSprites.FirstOrDefault(p=>p.Id==f.Id)?.Sprite ?? assets.Frame(variant.SheetId,variant.Animation,"any",0);
            dc.DrawImage(Image(s),new(f.X*16-s.AnchorX*s.Scale+s.OffsetX,f.Y*16-s.AnchorY*s.Scale+s.OffsetY,s.Width*s.Scale,s.Height*s.Scale));
            return;
        }
        if (!furnitureCache.TryGetValue(f.Id,out var drawing))
        {
            drawing = new DrawingGroup();
            using (var draw = drawing.Open()) DrawFallbackFurniture(draw,f);
            drawing.Freeze(); furnitureCache[f.Id] = drawing;
        }
        dc.DrawDrawing(drawing);
    }
    private void DrawFallbackFurniture(DrawingContext dc, Furniture f)
    {
        var x = f.X*16; var y = f.Y*16; var w = f.Width*16; var h = f.Height*16;
            switch (f.Kind)
            {
                case "desk":
                    R(dc,"#cbb898",x+2,y+27,w,7); R(dc,"#806b55",x+3,y+25,3,9); R(dc,"#806b55",x+w-6,y+25,3,9);
                    R(dc,"#ba8e65",x,y+15,w,13); R(dc,"#e2b784",x,y+13,w,11); R(dc,"#f5d5a6",x,y+13,w,2);
                    R(dc,"#414e51",x+11,y+1,25,15); R(dc,"#82b9a6",x+13,y+3,21,11);
                    R(dc,"#414e51",x+22,y+16,3,3); R(dc,"#f8f0d9",x+13,y+20,19,3);
                    R(dc,"#bca47e",x+14,y+21,1,1); R(dc,"#bca47e",x+18,y+21,1,1); R(dc,"#bca47e",x+22,y+21,1,1);
                    R(dc,"#f7ecd5",x+3,y+17,5,5); R(dc,"#ae784e",x+4,y+17,3,1);
                    R(dc,"#516768",x+13,y+37,21,6); R(dc,"#749390",x+15,y+34,17,5); R(dc,"#516768",x+22,y+42,3,5);
                    break;
                case "pantry":
                    R(dc,"#7e927a",x,y,w,h); R(dc,"#ece0b9",x-1,y,w+2,6);
                    for (var cabinet = x+3; cabinet < x+w; cabinet += 24) { R(dc,"#adbc95",cabinet,y+9,20,20); R(dc,"#6b805f",cabinet+15,y+14,2,2); }
                    R(dc,"#424a43",x+8,y-15,17,18); R(dc,"#778276",x+10,y-13,13,5);
                    R(dc,"#ddad65",x+12,y-7,9,3); R(dc,"#3d433e",x+12,y-3,9,3);
                    R(dc,"#fbf1d9",x+15,y-1,4,4); R(dc,"#566457",x+44,y-2,26,5);
                    R(dc,"#a6c4c0",x+46,y-1,22,2); R(dc,"#8f9a88",x+62,y-9,2,8); R(dc,"#8f9a88",x+58,y-9,6,2);
                    R(dc,"#f8f0df",x+32,y-6,6,6); R(dc,"#ca945d",x+33,y-6,4,1);
                    break;
                case "water":
                    R(dc,"#88afbf",x+3,y-9,10,13); R(dc,"#c3e1e5",x+5,y-8,6,9); R(dc,"#778a93",x+5,y+3,6,3);
                    R(dc,"#e7eadb",x+1,y+6,14,24); R(dc,"#b6c8bf",x+3,y+8,10,11); R(dc,"#467dae",x+4,y+9,2,3);
                    R(dc,"#d18771",x+10,y+9,2,3); R(dc,"#647b7b",x+4,y+18,8,2); break;
                case "tv":
                    R(dc,"#445259",x,y-12,w,30); R(dc,"#222f3b",x+3,y-9,w-6,24);
                    R(dc,"#89b8a9",x+4,y-8,w-8,20); R(dc,"#4e7f78",x+4,y+3,w-8,9);
                    R(dc,"#e77862",x+w/2-9,y-3,18,12); R(dc,"#fff4df",x+w/2-2,y,3,6);
                    R(dc,"#596760",x+28,y+18,8,5); R(dc,"#596760",x+18,y+22,28,3); break;
                case "sofa":
                    R(dc,"#496a60",x,y-4,w,h+8); R(dc,"#86a88b",x+4,y-3,w-8,7);
                    R(dc,"#729c7c",x+5,y+5,w-10,10); R(dc,"#4f7766",x+w/2,y+5,2,10);
                    R(dc,"#567764",x,y+1,6,17); R(dc,"#567764",x+w-6,y+1,6,17);
                    R(dc,"#dfc7a0",x+9,y+1,11,9); R(dc,"#aabfa0",x+w-23,y+2,12,8); break;
                case "table":
                    R(dc,"#a18462",x+4,y+6,w-8,10); R(dc,"#e1ba83",x,y,w,8); R(dc,"#f4d3a1",x,y,w,2);
                    R(dc,"#f8efda",x+10,y-4,5,5); R(dc,"#b58156",x+11,y-4,3,1);
                    R(dc,"#f8efda",x+32,y-4,5,5); break;
                case "plant":
                    R(dc,"#bc885f",x+3,y+7,11,10); R(dc,"#e0ac7c",x+2,y+6,13,3);
                    R(dc,"#476a52",x+7,y-9,2,17); break;
                default: R(dc,"#879982",x,y,w,h); break;
            }
        if (f.Kind == "plant")
        { R(dc,"#648764",x-1,y-7,9,5); R(dc,"#90af77",x+8,y-12,8,6); R(dc,"#759c68",x+2,y-16,9,6); }
    }

    protected override void OnRender(DrawingContext dc)
    {
        // Also supplies the empty floor with a hit-test surface for panning.
        dc.DrawRectangle(transparent ? Brushes.Transparent : Color("#302d2b"),null,new(0,0,ActualWidth,ActualHeight));
        var origin = Origin; dc.PushTransform(new TranslateTransform(origin.X,origin.Y)); dc.PushTransform(new ScaleTransform(zoom,zoom));
        if (room == null) BuildRoom(); dc.DrawDrawing(room!);
        var line = new Pen(Color("#9bab9e"),.7) { DashStyle = DashStyles.Dot }; line.Freeze();
        foreach (var child in agents)
            if (child.Pose.Visible && child.Agent.ParentId != null && byId.TryGetValue(child.Agent.ParentId,out var parent) && parent.Pose.Visible &&
                (child.Pose.Action is "entering" or "walking" or "pickup" or "reporting"))
                dc.DrawLine(line,new((child.Pose.X+.5)*16,(child.Pose.Y+.3)*16),new((parent.Pose.Desk.X+.5)*16,(parent.Pose.Desk.Y+.3)*16));
        var scene = office.Furniture.Select(f=>(Layer:assets.Variant(f).Layer,Y:(double)(f.Y+f.Height)*16+assets.Variant(f).ZOrder,Id:f.Id,F:(Furniture?)f,A:(AgentView?)null))
            .Concat(agents.Where(a=>a.Pose.Visible).Select(a=>(Layer:1,Y:(a.Pose.Y+1)*16,Id:a.Agent.Id,F:(Furniture?)null,A:(AgentView?)a)))
            .OrderBy(d=>d.Layer).ThenBy(d=>d.Y).ThenBy(d=>d.Id,StringComparer.Ordinal);
        foreach (var drawable in scene)
        {
            if (drawable.F is {} furniture) DrawFurniture(dc,furniture);
            else if (drawable.A is {} agent) DrawAgent(dc,agent);
        }
        foreach (var desk in office.Furniture.Where(f=>f.AgentId != null))
        {
            if (!byId.TryGetValue(desk.AgentId!,out var owner)) continue;
            var x=desk.X*16; var y=desk.Y*16;
            R(dc,ProviderColor(owner.Agent.Provider),x,y+desk.Height*16-2,5,2);
            if (owner.Pose.ReportOnDesk) { R(dc,"#fff9e7",x+desk.Width*16-12,y+18,9,7); R(dc,"#a19a7e",x+desk.Width*16-11,y+20,6,1); }
            var labelY=(owner.Pose.Desk.Y+1)*16+5;
            R(dc,"#f4e2bd",x-2,labelY,Math.Min(desk.Width*16+8,owner.Agent.Name.Length*4+8),10);
            Text(dc,owner.Agent.Name,x+1,labelY,7,"#392b24",maxWidth:desk.Width*16+8);
        }
        foreach (var agent in agents.Where(a=>a.Pose.Visible))
        { DrawIdentity(dc,agent); DrawBubble(dc,agent); }
        if (agents.Count == 0)
        { Text(dc,"A quiet moment.",30,132,10,bold:true); Text(dc,"Your agents will walk in.",30,151,7,maxWidth:95); }
        dc.Pop(); dc.Pop();
        if (debugTooltip && DebugTooltipText != null)
        {
            var width=Math.Min(430,ActualWidth-16); var height=Math.Min(200,ActualHeight-16);
            dc.DrawRoundedRectangle(Color("#303d40"),null,new(8,8,width,height),8,8);
            var lines=DebugTooltipText.Split('\n');
            for(var i=0;i<lines.Length;i++) Text(dc,lines[i],20,20+i*23,i==0 ? 14 : 12,"#fff4df",i==0,width-24);
        }
    }
    private void DrawAgent(DrawingContext dc, AgentView agent)
    {
        var p=agent.Pose; var bounds=SpriteBounds(p);
        dc.PushOpacity(p.Opacity); dc.DrawImage(Image(p.Sprite!),bounds);
        if (p.CarryingPaper && p.Action is "walking" or "entering")
        { var x=(p.X+.5)*16; var y=(p.Y+1)*16-12; R(dc,"#fff9e7",x+(p.Facing=="left" ? -10 : 4),y,6,8); }
        dc.Pop();
    }
    private void DrawBubble(DrawingContext dc, AgentView agent)
    {
        var p=agent.Pose;
        if (sheets.ContainsKey("icons"))
        {
            var clip = p.Reaction != null ? "bubble-reaction" : p.Action switch
            {
                "napping" => "bubble-napping", "waiting" => "bubble-waiting", "thinking" => "bubble-thinking",
                "reporting" => "bubble-done", "typing" => "bubble-working", _ => null
            };
            if (clip == null) return;
            var f = assets.Frame("icons",clip,"any",0);
            dc.PushOpacity(p.Opacity);
            dc.DrawImage(Image(f),new(Math.Round((p.X+.5)*16+8),HeadY(p)-30,f.Width*f.Scale,f.Height*f.Scale));
            dc.Pop(); return;
        }
        int? bubble=p.Reaction != null ? 5 : p.Action switch
        {
            "napping"=>6,"chatting" when p.ChatPartnerId != null=>7,"coffee"=>ambient%4==0 ? 8 : null,"waiting"=>5,
            "typing" when agent.Agent.ToolKind != null=>agent.Agent.ToolKind switch {"shell"=>0,"file"=>1,"search"=>2,"agent"=>3,_=>4},_=>null
        };
        if (bubble == null) return;
        var s=new SpriteFrame("fallback",6,bubble.Value,bubble.Value*16,6*24,16,24,8,24,1);
        var rect=SpriteBounds(p);
        dc.PushOpacity(p.Opacity); dc.DrawImage(Image(s),new((p.X+.5)*16-8,rect.Top-17,16,24)); dc.Pop();
    }
    private void DrawIdentity(DrawingContext dc, AgentView agent)
    {
        var p=agent.Pose;
        var x=Math.Round((p.X+.5)*16); var y=HeadY(p)-2;
        dc.PushOpacity(p.Opacity);
        // Provider accents travel with the character; the gold crown marks supervisors.
        R(dc,"#392b24",x-7,y-5,14,7); R(dc,ProviderColor(agent.Agent.Provider),x-6,y-4,12,5);
        if (!agent.Agent.IsSubagent)
        {
            R(dc,"#392b24",x-5,y-10,10,6);
            R(dc,"#f4c764",x-4,y-7,8,3);
            R(dc,"#f4c764",x-4,y-9,2,2); R(dc,"#f4c764",x-1,y-10,2,3); R(dc,"#f4c764",x+2,y-9,2,2);
        }
        else { R(dc,"#fff2d2",x-2,y-3,4,3); }
        dc.Pop();
    }
    public Point AgentPoint(AgentView agent)
    {
        var b=SpriteBounds(agent.Pose); var o=Origin;
        return new(o.X+(b.X+b.Width/2)*zoom,o.Y+(b.Y+b.Height/2)*zoom);
    }
    public AgentView? HitAgent(Point point)
    {
        var o=Origin; var world=new Point((point.X-o.X)/zoom,(point.Y-o.Y)/zoom);
        return agents.Where(a=>a.Pose.Visible).OrderByDescending(a=>a.Pose.Y).FirstOrDefault(a=>SpriteBounds(a.Pose).Contains(world));
    }
    public void SimulateHover(Point? point, DateTimeOffset now)
    {
        var agent=point == null ? null : HitAgent(point.Value);
        debugTooltip=agent != null; DebugTooltipText=agent == null ? null : TooltipText(agent,now);
        InvalidateVisual();
    }
    public static string TooltipText(AgentView agent, DateTimeOffset now)
    {
        var a=agent.Agent;
        var task=string.IsNullOrWhiteSpace(a.Task) ? "No task recorded" : a.Task.Length>150 ? a.Task[..147]+"…" : a.Task;
        return $"{a.Provider} · {a.Name}\n{(a.IsSubagent ? "Sub-agent" : "Supervisor")}{(a.Role==null ? "" : " · "+a.Role)}\nModel   {a.Model ?? "unknown"}\nProject   {a.Cwd ?? "unknown"}\nTask   {task}\nTool   {a.Tool ?? "none"}\n{a.State.ToString().ToLowerInvariant()} · {Math.Max(0,(now-a.StateSince).TotalSeconds):0}s · {agent.Pose.Action}";
    }

    public void PollHover(Point? cursor, DateTimeOffset now)
    {
        if (cursor == null || drag != null) { HideTooltip(); return; }
        var point = cursor.Value;
        if (point.X < 0 || point.Y < 0 || point.X >= ActualWidth || point.Y >= ActualHeight) { HideTooltip(); return; }
        var agent = HitAgent(point);
        if (agent == null) { HideTooltip(); return; }
        if (hovered != agent.Agent.Id)
        { HideTooltip(); hovered = agent.Agent.Id; hoverSince = now; }
        if ((now-hoverSince).TotalMilliseconds < 220) return;
        if (!tooltip.IsOpen)
        {
            tooltip.HorizontalOffset = Math.Clamp(point.X+18,0,Math.Max(0,ActualWidth-340));
            tooltip.VerticalOffset = Math.Clamp(point.Y+22,0,Math.Max(0,ActualHeight-190));
            tooltip.IsOpen = true;
        }
        var body=TooltipText(agent,now);
        if (body != tipText && (now-tipUpdated).TotalMilliseconds>=200)
        { tipTitle.Text=body.Split('\n')[0]; tipBody.Text=body[(body.IndexOf('\n')+1)..]; tipText=body; tipUpdated=now; }
    }

    public void HideTooltip() { tooltip.IsOpen = false; hovered = null; tipText = null; }
}
