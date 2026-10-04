using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Vibedotcom.Core;

namespace Vibedotcom.App;

internal sealed partial class OfficeView : FrameworkElement
{
    internal static readonly Color BackgroundColor = (Color)ColorConverter.ConvertFromString("#10282b");
    private OfficeLayout office = new(1, 1, 16, new(0, 0), [], Tiles: [0]);
    private IReadOnlyList<AgentView> agents = Array.Empty<AgentView>();
    private readonly Dictionary<string, AgentView> byId = new();
    private readonly AssetPack assets;
    private readonly BrandIcons brandIcons;
    private static readonly ConditionalWeakTable<AssetPack, Dictionary<string, BitmapSource>> SharedSheets = new();
    private readonly Dictionary<string, BitmapSource> sheets;
    private readonly Dictionary<(string, int, int, int, int), BitmapSource> sprites = new();
    private readonly Dictionary<(string, int, int, int, int), Rect> spriteContent = new();
    private readonly Dictionary<string, DrawingGroup> furnitureCache = new();
    private readonly Dictionary<string, (double Depth, bool Lounge)> furnitureDepths = new();
    private IReadOnlyList<FurnitureSprite> furnitureSprites = Array.Empty<FurnitureSprite>();
    private readonly Dictionary<string, SpriteFrame> furnitureFramesById = new(StringComparer.Ordinal);
    private bool debugTooltip;
    public string? DebugTooltipText { get; private set; }
    private readonly Dictionary<string, SolidColorBrush> brushes = new();
    private readonly Dictionary<(string, double, string, bool, double), FormattedText> textCache = new();
    private readonly Dictionary<string, (string Text, double Width)> names = new();
    private DrawingGroup? room;
    private BitmapSource? roomBitmap;
    private double roomScale;
    private Vector roomOrigin;
    private readonly ContainerVisual sceneVisual = new();
    private readonly DrawingVisual roomVisual = new(), linksVisual = new();
    private readonly ContainerVisual overlayVisual = new();
    private readonly Dictionary<string, RetainedItem> retained = new();
    private readonly List<RetainedItem> drawOrder = new();
    private readonly Dictionary<string, RetainedItem> overlays = new();
    private readonly List<RetainedItem> overlayOrder = new();
    private readonly List<Visual> overlayVisualOrder = new();
    private readonly DrawingVisual editorVisual = new(), quietVisual = new();
    private bool quietDrawn, quietVisible;
    private readonly List<Rect> tagPlates = new();
    private readonly List<string> expiredKeys = new();
    private bool sceneDirty = true;
    private int linksHash, editorHash;
    private bool layersDrawn;
    private sealed class RetainedItem(string key)
    {
        public readonly string Key = key;
        public readonly string Id = key[2..];
        public readonly DrawingVisual Visual = new();
        public Furniture? Furniture;
        public AgentView? Agent;
        public PetView? Pet;
        public long AnimationStamp;
        public SpriteFrame? Frame;
        public int Layer;
        public double Depth;
        public bool Used;
    }
    private readonly DrawingVisual tagsVisual = new();
    private readonly NameTagHover tagHover = new();
    private readonly Dictionary<string, Rect> tagRects = new(), iconRects = new();
    private readonly Dictionary<string, double> tagOffsets = new();
    private string tagMode = "hover";
    private Rect tooltipRect = Rect.Empty;
    internal int SceneRenderCount { get; private set; }
    internal int RoomBuildCount { get; private set; }
    internal int DynamicRenderCount { get; private set; }
    // Sum of changed visual bounds in viewport pixels, not a GPU/compositor counter.
    internal double RedrawPixelArea { get; private set; }
    internal bool TagsAnimating => tagHover.Animating;
    internal bool HasHoverTarget => tagHover.Target != null;
    internal bool TooltipPending => tagHover.Target != null && !tooltip.IsOpen && !debugTooltip;
    internal Rect TooltipRect => tooltipRect;
    internal Rect AgentTagRect(string id) => ScreenRect(tagRects.GetValueOrDefault("agent:" + id, Rect.Empty));
    internal Rect DeskTagRect(string id) => ScreenRect(tagRects.GetValueOrDefault("desk:" + id, Rect.Empty));
    internal Rect AgentIconRect(string id) => ScreenRect(iconRects.GetValueOrDefault("agent:" + id, Rect.Empty));
    internal Rect DeskIconRect(string id) => ScreenRect(iconRects.GetValueOrDefault("desk:" + id, Rect.Empty));
    public string TagMode
    {
        get => tagMode;
        set { var mode = NameTagHover.Normalize(value); if (mode == tagMode) return; tagMode = mode; tagOffsets.Clear(); HideTooltip(); }
    }
    protected override int VisualChildrenCount => 5;
    protected override Visual GetVisualChild(int index) => index switch
    { 0 => roomVisual, 1 => linksVisual, 2 => sceneVisual, 3 => overlayVisual, 4 => tagsVisual, _ => throw new ArgumentOutOfRangeException(nameof(index)) };
    private int ambient = -1;
    private double zoom = 1;
    private Vector pan;
    private Point? drag;
    private bool autoFit = true, transparent;
    private readonly Popup tooltip = new() { AllowsTransparency = true, StaysOpen = true, IsHitTestVisible = false, Placement = PlacementMode.Relative };
    private readonly TextBlock tipTitle = new() { FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White };
    private readonly TextBlock tipBody = new() { FontSize = 12, Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(233,230,215)), TextWrapping = TextWrapping.Wrap, MaxWidth = 310, Margin = new(0,7,0,0) };
    private string? hovered, tipText;
    private DateTimeOffset tipUpdated;
    public int PreferredZoom => autoFit ? 0 : (int)zoom;
    public double Zoom => zoom;
    public bool EditorMode { get; set; }
    public string? EditSelection { get; set; }
    public Tile? EditPreview { get; set; }
    public bool TransparentBackground
    {
        get => transparent;
        set { if (transparent == value) return; transparent = value; InvalidateVisual(); }
    }
    public IReadOnlyList<AgentView> Agents => agents;
    public OfficeLayout Layout => office;
    public int RenderedAgentTags { get; private set; }
    public int RenderedReservedTags { get; private set; }

    internal IReadOnlyDictionary<string, BitmapSource> DecodedSheets => sheets;
    internal static IReadOnlyDictionary<string, BitmapSource> DecodeSheets(AssetPack assets)
    {
        var result = new Dictionary<string, BitmapSource>();
        foreach (var (id, bytes) in assets.Images)
        {
            using var stream = new MemoryStream(bytes);
            var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream; image.EndInit(); image.Freeze(); result[id] = image;
        }
        return result;
    }
    public OfficeView(AssetPack? assets = null, string? brandDirectory = null, IReadOnlyDictionary<string, BitmapSource>? decodedSheets = null,
        Func<DateTimeOffset>? localClock = null)
    {
        this.assets = assets ?? new();
        pets = new(this.assets);
        environment = new(localClock);
        this.localClock = localClock;
        sheets = SharedSheets.GetOrCreateValue(this.assets);
        AddVisualChild(roomVisual); AddVisualChild(linksVisual); AddVisualChild(sceneVisual); AddVisualChild(overlayVisual); AddVisualChild(tagsVisual);
        brandIcons = new(brandDirectory);
        if (decodedSheets != null) lock (sheets) foreach (var (id, image) in decodedSheets) sheets[id] = image;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.NearestNeighbor);
        RenderOptions.SetEdgeMode(this, EdgeMode.Aliased); SnapsToDevicePixels = true; ClipToBounds = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        TextOptions.SetTextRenderingMode(this, TextRenderingMode.Aliased);
        var stack = new StackPanel(); stack.Children.Add(tipTitle); stack.Children.Add(tipBody);
        tooltip.Child = new Border { Background = Color("#303d40"), CornerRadius = new(8), Padding = new(15,12,15,12), Child = stack };
        tooltip.PlacementTarget = this;
        tooltip.Opened += (_, _) => WindowModes.MakePopupPassive(tooltip.Child);
        SizeChanged += (_, _) => { if (autoFit) Fit(); else InvalidateVisual(); };
        MouseLeftButtonDown += (_, e) => { if (EditorMode) return; drag = e.GetPosition(this); CaptureMouse(); HideTooltip(); };
        MouseMove += (_, e) =>
        {
            if (drag is not { } previous) return;
            var point = e.GetPosition(this); PanBy(point - previous); drag = point;
        };
        MouseLeftButtonUp += (_, _) => { drag = null; ReleaseMouseCapture(); };
        LostMouseCapture += (_, _) => drag = null;
        Unloaded += (_, _) => tooltip.IsOpen = false;
    }

    public void SetZoom(int requested)
    {
        autoFit = requested == 0; pan = new();
        if (autoFit) Fit(); else { zoom = Math.Clamp(requested,1,6); InvalidateVisual(); }
        HideTooltip();
    }
    public void ChangeZoom(int direction) => SetZoom(Math.Max(1,(int)Math.Floor(zoom)) + direction);
    public void PanBy(Vector offset) { pan += offset; ClampPan(); HideTooltip(); InvalidateVisual(); }
    private void Fit()
    {
        var fit = Math.Min(Math.Max(1,ActualWidth - 20) / (office.WidthTiles * 16), Math.Max(1,ActualHeight - 40) / (office.HeightTiles * 16));
        zoom = fit >= 1 ? Math.Min(6,Math.Floor(fit)) : Math.Max(.01,fit);
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
        var layoutChanged = !ReferenceEquals(layout,office);
        var changed = layoutChanged;
        if (layoutChanged)
        {
            var geometryChanged = layout.WidthTiles != office.WidthTiles || layout.HeightTiles != office.HeightTiles ||
                !ReferenceEquals(layout.Tiles, office.Tiles) || !ReferenceEquals(layout.Zones, office.Zones) || layout.Door != office.Door ||
                layout.Furniture.Any(f=>f.Kind=="door") != office.Furniture.Any(f=>f.Kind=="door");
            office = layout;
            if (geometryChanged) { room = null; roomBitmap = null; if (autoFit) Fit(); }
            furnitureCache.Clear();
            furnitureDepths.Clear();
            overflowSeatHosts.Clear();
            foreach (var seat in layout.OverflowSeats ?? []) if (seat.FurnitureId != null) overflowSeatHosts[seat.Id] = seat.FurnitureId;
        }
        furnitureFrames ??= Array.Empty<FurnitureSprite>();
        furnitureFrames = AmbientFrames(layout, views, now, furnitureFrames);
        if (!ReferenceEquals(furnitureSprites, furnitureFrames))
        {
            changed = true; furnitureFramesById.Clear();
            foreach (var frame in furnitureFrames) furnitureFramesById[frame.Id] = frame.Sprite;
        }
        furnitureSprites=furnitureFrames;
        var nextPets = pets.Update(layout, views, now);
        changed |= !petViews.SequenceEqual(nextPets); petViews = nextPets;
        var stamp = now.ToUnixTimeMilliseconds() / 125;
        changed |= stamp != activityStamp && views.Any(a => a.Pose.Action is "darts" or "pingpong");
        changed |= hasClock && ambientState?.LocalTime != previousLocalTime;
        previousLocalTime = ambientState?.LocalTime; activityStamp = stamp;
        var phase = (int)(now.ToUnixTimeMilliseconds() / 500 % 120);
        var agentsChanged = agents.Count != views.Count;
        // Only the primitive coffee bubble depends on this clock.
        if (!assets.Images.ContainsKey("icons") && phase != ambient)
            changed |= views.Any(a => a.Pose.Visible && a.Pose.Action == "coffee");
        if (!agentsChanged)
            for (var i = 0; i < views.Count; i++)
                if (views[i] != agents[i]) { agentsChanged = true; break; }
        changed |= agentsChanged;
        ambient = phase; agents = views;
        if (agentsChanged) { byId.Clear(); foreach (var agent in agents) byId[agent.Agent.Id] = agent; }
        if (changed)
        {
            sceneDirty = true;
            if (ActualWidth > 0 && ActualHeight > 0)
            {
                if (!layoutChanged && !agentsChanged && layersDrawn && assets.Images.ContainsKey("icons")) RenderAmbientChanges();
                else RenderScene();
            }
        }
    }

    private SolidColorBrush Color(string color)
    {
        if (brushes.TryGetValue(color,out var brush)) return brush;
        brush = (SolidColorBrush)new BrushConverter().ConvertFromString(color)!; brush.Freeze(); brushes.Add(color,brush); return brush;
    }
    private static string ProviderColor(Provider provider) => provider switch { Provider.Codex => "#329b91", Provider.Claude => "#d57e4e", _ => "#827ed1" };
    private void R(DrawingContext dc, string color, double x, double y, double w, double h) => dc.DrawRectangle(Color(color),null,new(x,y,w,h));
    private FormattedText FormatText(string text, double size, string color, bool bold = false, double maxWidth = 500)
    {
        var key = (text,size,color,bold,maxWidth);
        if (!textCache.TryGetValue(key,out var formatted))
        {
            if (textCache.Count >= 256) textCache.Clear();
            formatted = new(text,CultureInfo.CurrentCulture,FlowDirection.LeftToRight,
                new Typeface(new FontFamily(size <= 6 ? "Small Fonts" : "Segoe UI"),FontStyles.Normal,bold ? FontWeights.SemiBold : FontWeights.Normal,FontStretches.Normal),
                size,Color(color),VisualTreeHelper.GetDpi(this).PixelsPerDip) { MaxTextWidth = Math.Max(1,maxWidth), MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis };
            textCache[key] = formatted;
        }
        return formatted;
    }
    private void Text(DrawingContext dc, string text, double x, double y, double size, string color = "#394940", bool bold = false, double maxWidth = 500)
        => dc.DrawText(FormatText(text,size,color,bold,maxWidth),new(x,y));

    private BitmapSource Image(SpriteFrame f)
    {
        var key = (f.SheetId, f.X, f.Y, f.Width, f.Height);
        if (!sprites.TryGetValue(key, out var image))
        {
            if (!sheets.TryGetValue(f.SheetId, out var sheet))
            {
                using var stream = new MemoryStream(assets.Images[f.SheetId], writable: false);
                var decoded = new BitmapImage(); decoded.BeginInit(); decoded.CacheOption = BitmapCacheOption.OnLoad;
                decoded.StreamSource = stream; decoded.EndInit(); decoded.Freeze(); sheets[f.SheetId] = sheet = decoded;
            }
            image = new CroppedBitmap(sheet, new(f.X, f.Y, f.Width, f.Height));
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
    private Rect ContentBounds(Pose p)
    {
        var raw = UnposedContentBounds(p);
        if (p.Action != "beanbag") return raw;
        var center = RecliningCenter(p);
        return new(Math.Round(center.X - raw.Height / 2), Math.Round(center.Y - raw.Width / 2), raw.Height, raw.Width);
    }
    private static Point RecliningCenter(Pose p) => new((p.X + .5) * 16 + p.Sprite!.OffsetX,
        (p.Y + 1) * 16 + p.Sprite.OffsetY - 4);
    private Rect UnposedContentBounds(Pose p)
    {
        var s=p.Sprite!; var content=ImageContentBounds(s); var bounds=SpriteBounds(p);
        var height = content.Height*s.Scale;
        if (p.SeatAnchor != null && p.SeatPhase != "turn") height = Math.Min(height,Math.Max(0,p.SeatAnchor.Y+1-bounds.Top-content.Top*s.Scale));
        return new(bounds.Left+content.Left*s.Scale,bounds.Top+content.Top*s.Scale,content.Width*s.Scale,height);
    }
    private Rect ImageContentBounds(SpriteFrame s)
    {
        var key=(s.SheetId,s.X,s.Y,s.Width,s.Height);
        if (!spriteContent.TryGetValue(key,out var content))
        {
            var bitmap=new FormatConvertedBitmap(Image(s),PixelFormats.Bgra32,null,0);
            var pixels=new byte[s.Width*s.Height*4]; bitmap.CopyPixels(pixels,s.Width*4,0);
            var left=s.Width; var top=s.Height; var right=0; var bottom=0;
            for (var y=0;y<s.Height;y++) for (var x=0;x<s.Width;x++)
            {
                if (pixels[(y*s.Width+x)*4+3]<=32) continue;
                left=Math.Min(left,x); top=Math.Min(top,y); right=Math.Max(right,x+1); bottom=Math.Max(bottom,y+1);
            }
            content=right>left ? new(left,top,right-left,bottom-top) : new(0,0,s.Width,s.Height);
            spriteContent[key]=content;
        }
        return content;
    }
    private double HeadY(Pose p) => ContentBounds(p).Top;
    private static double AgentDepth(Pose p) => p.SeatAnchor is {} seat && p.SeatPhase != "turn" ? seat.Y + 12 : (p.Y + 1) * 16;
    private Rect FurnitureBounds(Furniture f)
    {
        var v=assets.Variant(f);
        if (v.SheetId==null || v.Animation==null) return new(f.X*16,f.Y*16,f.Width*16,f.Height*16);
        var s=furnitureFramesById.GetValueOrDefault(f.Id) ?? assets.Frame(v.SheetId,v.Animation,"any",0);
        var content=ImageContentBounds(s);
        return new(f.X*16+(content.X-s.AnchorX)*s.Scale+s.OffsetX,f.Y*16+(content.Y-s.AnchorY)*s.Scale+s.OffsetY,content.Width*s.Scale,content.Height*s.Scale);
    }
    private void BuildRoom()
    {
        RoomBuildCount++;
        room = new DrawingGroup();
        RenderOptions.SetBitmapScalingMode(room,BitmapScalingMode.NearestNeighbor);
        using (var dc = room.Open())
        {
            for (var y=0; y<office.HeightTiles; y++) for (var x=0; x<office.WidthTiles; x++)
            {
                var t = assets.Manifest.TileTypes.First(t=>t.Id==office.Tiles![y*office.WidthTiles+x]);
                if (t.Kind == "void") continue;
                // Tile art may contain alpha at its edges. The office surface
                // stays opaque; only void tiles and the window margins are clear.
                R(dc,t.Color,x*16,y*16,16,16);
                if (t.SheetId != null && t.Animation != null)
                {
                    var f = assets.Frame(t.SheetId,t.Animation,"any",0);
                    dc.DrawImage(Image(f),new(x*16,y*16,16,16));
                }
                else
                {
                    if (t.Kind == "floor")
                    { R(dc,t.Id == 2 ? "#c9d1b8" : "#d9c6a8",x*16,y*16+15,16,1); if ((x+y)%3==0) R(dc,"#d9c6a8",x*16,y*16,1,15); }
                }
            }
            foreach (var z in office.Zones ?? Array.Empty<Zone>())
                if (!assets.Manifest.Furniture.Any(f => f.Id == "cozy-desk"))
                if (z.Name is "workspace" or "pantry" && z.Width * 16 > 28)
                {
                    R(dc,"#eee5d2",z.X*16+24,24,Math.Min(72,z.Width*16-24),11);
                    Text(dc,z.Name.ToUpperInvariant(),z.X*16+26,25,7,"#59654c",true,maxWidth:z.Width*16-28);
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
            var s = furnitureFramesById.GetValueOrDefault(f.Id) ?? assets.Frame(variant.SheetId,variant.Animation,"any",0);
            dc.DrawImage(Image(s),new(f.X*16-s.AnchorX*s.Scale+s.OffsetX,f.Y*16-s.AnchorY*s.Scale+s.OffsetY,s.Width*s.Scale,s.Height*s.Scale));
            if (f.Kind == "clock") DrawClock(dc, f, s);
            if (f.AssetId == "cozy-vending-machine" && f.State == "open")
            {
                var x = f.X * 16 - s.AnchorX * s.Scale + s.OffsetX;
                var y = f.Y * 16 - s.AnchorY * s.Scale + s.OffsetY;
                R(dc,"#172b30",x+23*s.Scale,y+53*s.Scale,14*s.Scale,6*s.Scale);
                R(dc,"#9ba69a",x+23*s.Scale,y+52*s.Scale,14*s.Scale,s.Scale);
                R(dc,"#f2ebd3",x+29*s.Scale,y+55*s.Scale,3*s.Scale,3*s.Scale);
            }
            if (f.Kind == "window" && ambientState?.WinterDecor == true)
            { R(dc,"#eee5d2",f.X*16,f.Y*16-2,f.Width*16,2); R(dc,"#b45e4d",f.X*16+f.Width*8-2,f.Y*16-4,4,4); }
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
                    R(dc,"#414e51",x+11,y+1,25,15); R(dc,(f.MonitorState ?? f.State)=="working" ? "#82b9a6" : "#263d40",x+13,y+3,21,11);
                    if((f.MonitorState ?? f.State)=="working") { R(dc,"#eaf4c7",x+15,y+6,10,1);R(dc,"#eaf4c7",x+15,y+9,14,1); }
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
        SceneRenderCount++;
        // Also supplies the empty floor with a hit-test surface for panning.
        dc.DrawRectangle(transparent ? Brushes.Transparent : Color("#10282b"),null,new(0,0,ActualWidth,ActualHeight));
        var origin = Origin;
        var transform = new TransformGroup(); transform.Children.Add(new ScaleTransform(zoom,zoom)); transform.Children.Add(new TranslateTransform(origin.X,origin.Y)); transform.Freeze();
        linksVisual.Transform = sceneVisual.Transform = overlayVisual.Transform = transform;
        if (room == null) BuildRoom();
        // Rasterize static tiles once at the actual camera density. Furniture keeps its
        // original depth ordering in separate retained visuals; WPF tracks their damage.
        var density=zoom*VisualTreeHelper.GetDpi(this).DpiScaleX;
        var dpi=VisualTreeHelper.GetDpi(this).DpiScaleX;
        var pixelWidth=Math.Max(1,(int)Math.Ceiling(ActualWidth*dpi)); var pixelHeight=Math.Max(1,(int)Math.Ceiling(ActualHeight*dpi));
        if (roomBitmap == null || roomScale != density || roomOrigin!=origin || roomBitmap.PixelWidth!=pixelWidth || roomBitmap.PixelHeight!=pixelHeight)
        {
            roomScale = density; roomOrigin=origin;
            var visual = new DrawingVisual(); RenderOptions.SetEdgeMode(visual,EdgeMode.Aliased);
            RenderOptions.SetBitmapScalingMode(visual,BitmapScalingMode.NearestNeighbor);
            TextOptions.SetTextRenderingMode(visual,TextRenderingMode.Aliased);
            // Include the viewport origin in rasterization: at 3x, removing and
            // reapplying it changes WPF's nearest-neighbour rounding at tile edges.
            using (var draw = visual.RenderOpen())
            { draw.PushTransform(new ScaleTransform(dpi,dpi)); draw.PushTransform(new TranslateTransform(origin.X,origin.Y)); draw.PushTransform(new ScaleTransform(zoom,zoom)); draw.DrawDrawing(room!); }
            var bitmap = new RenderTargetBitmap(pixelWidth,pixelHeight,96,96,PixelFormats.Pbgra32);
            bitmap.Render(visual); bitmap.Freeze(); roomBitmap = bitmap;
        }
        using (var draw = roomVisual.RenderOpen()) draw.DrawImage(roomBitmap,new(0,0,roomBitmap.PixelWidth/dpi,roomBitmap.PixelHeight/dpi));
        sceneDirty = true; RenderScene();
    }
    private void Damage(DrawingVisual visual, Action<DrawingContext> draw)
    {
        var bounds = visual.ContentBounds;
        using (var dc = visual.RenderOpen()) draw(dc);
        bounds.Union(visual.ContentBounds);
        if (!bounds.IsEmpty) RedrawPixelArea += bounds.Width*bounds.Height*zoom*zoom;
        DynamicRenderCount++;
    }
    private void RenderScene()
    {
        if (!sceneDirty) return;
        sceneDirty = false;
        // Relationship lines can span several bodies and have their own damage layer.
        var linkKey = new HashCode();
        foreach (var agent in agents)
        {
            var p=agent.Pose;
            if (agent.Agent.ParentId != null)
            { linkKey.Add(agent.Agent.Id); linkKey.Add(agent.Agent.ParentId); linkKey.Add(agent.Agent.State); }
            linkKey.Add(p.X); linkKey.Add(p.Y); linkKey.Add(p.Visible);
        }
        var lh=linkKey.ToHashCode();
        if (!layersDrawn || linksHash != lh) { Damage(linksVisual,DrawLinks); linksHash=lh; }
        foreach (var item in retained.Values) item.Used = false;
        drawOrder.Clear();
        foreach (var furniture in office.Furniture)
        {
            var key = "f:"+furniture.Id;
            if (!retained.TryGetValue(key,out var item)) retained[key] = item = new(key);
            var frame = furnitureFramesById.GetValueOrDefault(furniture.Id);
            var stamp = furniture.Kind == "clock" ? ambientState?.LocalTime.ToUnixTimeSeconds() ?? 0 :
                furniture.Kind == "window" && ambientState?.WinterDecor == true ? 1 : 0;
            if (item.Furniture != furniture || item.Frame != frame || item.AnimationStamp != stamp)
            {
                item.AnimationStamp = stamp;
                item.Furniture = furniture; item.Frame = frame;
                Damage(item.Visual, draw => DrawFurniture(draw,furniture));
            }
            item.Layer = assets.Variant(furniture).Layer; item.Depth = FurnitureDepth(furniture); item.Used = true; drawOrder.Add(item);
        }
        foreach (var agent in agents)
        {
            if (!agent.Pose.Visible) continue;
            var key = "a:"+agent.Agent.Id;
            if (!retained.TryGetValue(key,out var item)) retained[key] = item = new(key);
            var stamp = agent.Pose.Action is "darts" or "pingpong" ? activityStamp : 0;
            if (item.Agent?.Pose != agent.Pose || item.AnimationStamp != stamp)
            { item.Agent = agent; item.AnimationStamp = stamp; Damage(item.Visual, draw => DrawAgent(draw,agent)); }
            item.Layer = 1; item.Depth = AgentDepth(agent.Pose); item.Used = true; drawOrder.Add(item);
        }
        foreach (var pet in petViews)
        {
            var key = "p:" + pet.Id;
            if (!retained.TryGetValue(key, out var item)) retained[key] = item = new(key);
            if (item.Pet != pet) { item.Pet = pet; Damage(item.Visual, dc => DrawPet(dc, pet)); }
            item.Layer = 1; item.Depth = (pet.Y + 1) * 16; item.Used = true; drawOrder.Add(item);
        }
        expiredKeys.Clear();
        foreach (var (key,item) in retained) if (!item.Used) { sceneVisual.Children.Remove(item.Visual); expiredKeys.Add(key); }
        foreach (var key in expiredKeys) retained.Remove(key);
        drawOrder.Sort(static (a,b) => { var c = a.Layer.CompareTo(b.Layer); if (c==0) c=a.Depth.CompareTo(b.Depth); return c!=0 ? c : StringComparer.Ordinal.Compare(a.Id,b.Id); });
        for (var i=0;i<drawOrder.Count;i++)
        {
            var visual=drawOrder[i].Visual;
            if (i < sceneVisual.Children.Count && ReferenceEquals(sceneVisual.Children[i],visual)) continue;
            sceneVisual.Children.Remove(visual); sceneVisual.Children.Insert(i,visual);
        }
        RenderOverlays();
        layersDrawn = true;
        if (PrivacyMode != PrivacyMode.Off || tagMode != "hover" || tagHover.Target != null || tagHover.Animating || debugTooltip) RenderTags();
    }
    private void DrawLinks(DrawingContext dc)
    {
        var line = new Pen(Color("#405cbbb5"),.6) { DashStyle = DashStyles.Dot }; line.Freeze();
        foreach (var child in agents)
            if (child.Pose.Visible && child.Agent.ParentId != null && byId.TryGetValue(child.Agent.ParentId,out var parent) && parent.Pose.Visible &&
                child.Agent.State is AgentState.Working or AgentState.Reporting)
            {
                var start = new Point((child.Pose.X+.5)*16,(child.Pose.Y+1)*16-5);
                var end = new Point((parent.Pose.X+.5)*16,(parent.Pose.Y+1)*16-5);
                var distance = (end-start).Length;
                if (distance > 64 || distance < 12) continue;
                dc.DrawLine(line,start,start+(end-start)*Math.Min(1,40/distance));
            }
    }
    private double FurnitureDepth(Furniture f)
    {
        if (!furnitureDepths.TryGetValue(f.Id,out var cached))
        {
            cached = CacheFurnitureDepth(f);
            furnitureDepths[f.Id]=cached;
        }
        if (!cached.Lounge || agents.Count==0) return cached.Depth;
        // Unsplit lounge seats can change depth when somebody sits down. Keep
        // that small live dependency while retaining geometry/host calculations.
        var seatedDepth=double.NegativeInfinity;
        for (var i = 0; i < agents.Count; i++)
        {
            var a = agents[i];
            if (a.Pose.Visible && a.Pose.SeatAnchor != null && a.Pose.SeatPhase != "turn" &&
                a.Pose.ChairFrontId == null && a.Pose.OverflowSeatId != null &&
                overflowSeatHosts.TryGetValue(a.Pose.OverflowSeatId, out var host) && host == f.Id)
                seatedDepth=Math.Max(seatedDepth,AgentDepth(a.Pose));
        }
        return double.IsNegativeInfinity(seatedDepth) ? cached.Depth : seatedDepth+.2;
    }
    private void RenderOverlays()
    {
        foreach (var item in overlays.Values) item.Used=false;
        overlayOrder.Clear();
        void Add(string key,AgentView agent,Furniture? furniture,Action<DrawingContext> draw,long stamp=0)
        {
            if (!overlays.TryGetValue(key,out var item)) overlays[key]=item=new(key);
            var clockChanged=key[0]=='b' && agent.Pose.Action=="coffee" && !assets.Images.ContainsKey("icons") && item.Depth!=ambient;
            if (item.Agent != agent || item.Furniture != furniture || clockChanged || item.AnimationStamp!=stamp)
            { item.Agent=agent; item.Furniture=furniture; item.AnimationStamp=stamp; Damage(item.Visual,draw); }
            item.Depth=ambient;
            item.Used=true; overlayOrder.Add(item);
        }
        foreach(var agent in agents)
            if (agent.Pose.Visible && agent.Pose.WorkLocation=="overflow" && agent.Pose.ChairFrontId == null && agent.Pose.Action is "typing" or "thinking" or "waiting")
                Add("l:"+agent.Agent.Id,agent,null,dc=>DrawLaptop(dc,agent.Pose));
        foreach (var desk in office.Furniture)
        {
            if (desk.AgentId==null) continue;
            if (!byId.TryGetValue(desk.AgentId!,out var owner)) continue;
            Add("d:"+desk.Id,owner,desk,dc=>
            {
                var x=desk.X*16; var y=desk.Y*16;
                R(dc,ProviderColor(owner.Agent.Provider),x,y+desk.Height*16-2,5,2);
                if (owner.Pose.ReportOnDesk) { R(dc,"#fff9e7",x+desk.Width*16-12,y+18,9,7); R(dc,"#a19a7e",x+desk.Width*16-11,y+20,6,1); }
            });
        }
        var editKey=HashCode.Combine(EditorMode,EditSelection,EditPreview,office);
        if (!layersDrawn || editorHash!=editKey)
        { Damage(editorVisual,dc=> { if (EditorMode) DrawEditorGrid(dc); }); editorHash=editKey; }
        // The editor grid lies between desk indicators and bubbles.
        var bubbleStart=overlayOrder.Count;
        foreach (var agent in agents)
            if (agent.Pose.Visible && agent.Pose.Action is "darts" or "pingpong")
                Add("g:"+agent.Agent.Id,agent,null,dc=>DrawActivity(dc,agent,true),activityStamp);
        foreach (var agent in agents) if (agent.Pose.Visible)
            Add("b:"+agent.Agent.Id,agent,null,dc=>DrawBubble(dc,agent));
        expiredKeys.Clear();
        foreach (var (key,item) in overlays) if (!item.Used) { overlayVisual.Children.Remove(item.Visual); expiredKeys.Add(key); }
        foreach (var key in expiredKeys) overlays.Remove(key);
        if (!quietDrawn || quietVisible!=(agents.Count==0))
        { Damage(quietVisual,dc=> { if (agents.Count==0) { Text(dc,"A quiet moment.",30,132,10,bold:true); Text(dc,"Your agents will walk in.",30,151,7,maxWidth:95); } }); quietDrawn=true; quietVisible=agents.Count==0; }
        var visualOrder=overlayVisualOrder; visualOrder.Clear();
        for (var i=0;i<overlayOrder.Count;i++) { if (i==bubbleStart) visualOrder.Add(editorVisual); visualOrder.Add(overlayOrder[i].Visual); }
        if (bubbleStart==overlayOrder.Count) visualOrder.Add(editorVisual);
        visualOrder.Add(quietVisual);
        for (var i=0;i<visualOrder.Count;i++)
        {
            var visual=visualOrder[i];
            if (i<overlayVisual.Children.Count && ReferenceEquals(overlayVisual.Children[i],visual)) continue;
            overlayVisual.Children.Remove(visual); overlayVisual.Children.Insert(i,visual);
        }
    }
    private bool OwnerAway(AgentView owner)
    {
        var near = Math.Abs(owner.Pose.X-owner.Pose.Desk.X)+Math.Abs(owner.Pose.Y-owner.Pose.Desk.Y) <= 1.5;
        return owner.Pose.Visible && owner.Pose.SeatPhase == null && !(near && owner.Agent.State is AgentState.Working or AgentState.Thinking or AgentState.Waiting);
    }
    private Rect ScreenRect(Rect rect) => rect.IsEmpty ? rect : new(Origin.X + rect.X * zoom, Origin.Y + rect.Y * zoom, rect.Width * zoom, rect.Height * zoom);
    private void RenderTags()
    {
        using var dc = tagsVisual.RenderOpen();
        var origin = Origin; dc.PushTransform(new TranslateTransform(origin.X,origin.Y)); dc.PushTransform(new ScaleTransform(zoom,zoom));
        var plates = tagPlates; plates.Clear();
        tagRects.Clear(); iconRects.Clear(); RenderedAgentTags=RenderedReservedTags=0;
        // Idle relationship hints belong to this layer so hover never rebuilds the scene.
        foreach (var child in agents.Where(a=>a.Pose.Visible && a.Agent.State is not (AgentState.Working or AgentState.Reporting)))
            if (child.Agent.ParentId != null && byId.TryGetValue(child.Agent.ParentId,out var parent) && parent.Pose.Visible &&
                (hovered == child.Agent.Id || hovered == parent.Agent.Id))
            {
                var start = new Point((child.Pose.X+.5)*16,(child.Pose.Y+1)*16-5);
                var end = new Point((parent.Pose.X+.5)*16,(parent.Pose.Y+1)*16-5); var distance=(end-start).Length;
                if (distance is > 12 and <= 64) dc.DrawLine(new Pen(Color("#405cbbb5"),.6) { DashStyle=DashStyles.Dot },start,start+(end-start)*Math.Min(1,40/distance));
            }
        foreach (var agent in agents.Where(a=>a.Pose.Visible).OrderBy(a=>HeadY(a.Pose)).ThenBy(a=>a.Agent.Id,StringComparer.Ordinal))
        {
            var body=ContentBounds(agent.Pose); var anchor=new Point(Math.Round(body.X+body.Width/2),HeadY(agent.Pose)-12);
            DrawModeTag(dc,"agent:"+agent.Agent.Id,agent,anchor,plates,false);
        }
        foreach (var desk in office.Furniture.Where(f=>f.AgentId != null))
        {
            if (!byId.TryGetValue(desk.AgentId!,out var owner) || !OwnerAway(owner)) continue;
            DrawModeTag(dc,"desk:"+desk.Id,owner,new(desk.X*16+desk.Width*8,desk.Y*16+desk.Height*16-13),plates,true);
        }
        foreach (var key in tagOffsets.Keys.Where(k=>!tagRects.ContainsKey(k)).ToArray()) tagOffsets.Remove(key);
        dc.Pop(); dc.Pop();
        DrawPrivacyBadge(dc);
        if (debugTooltip && DebugTooltipText != null)
        {
            dc.DrawRoundedRectangle(Color("#303d40"),null,tooltipRect,8,8);
            var lines=DebugTooltipText.Split('\n');
            for(var i=0;i<lines.Length;i++) Text(dc,lines[i],tooltipRect.X+12,tooltipRect.Y+12+i*23,i==0 ? 14 : 12,"#fff4df",i==0,tooltipRect.Width-24);
        }
    }
    private void DrawModeTag(DrawingContext dc,string key,AgentView agent,Point anchor,List<Rect> plates,bool desk)
    {
        var opacity=tagMode=="always" ? 1 : tagHover.Opacity(key);
        dc.PushOpacity(agent.Pose.Opacity);
        if (tagMode=="icon" && opacity<1)
        {
            var size=ProviderIconSize(agent.Agent);
            var rect=new Rect(anchor.X-size/2d,anchor.Y-4,size,size); iconRects[key]=rect;
            dc.PushOpacity(1-opacity); DrawProviderIcon(dc,agent.Agent,rect.X,rect.Y); dc.Pop();
        }
        if (opacity>0)
        {
            dc.PushOpacity(opacity);
            var plate=DrawTag(dc,agent,anchor,plates,true,key); tagRects[key]=plate;
            if (desk) RenderedReservedTags++; else RenderedAgentTags++;
            if (!desk && agent.Pose.Queued)
            {
                var x=plate.Right+3;var y=plate.Y+2;
                R(dc,"#738b84",x-1,y-1,17,12);R(dc,"#172b30",x,y,15,10);Text(dc,"...",x+2,y-2,8,"#f5edce");
            }
            dc.Pop();
        }
        dc.Pop();
    }
    private void DrawAgent(DrawingContext dc, AgentView agent)
    {
        var p=agent.Pose; var bounds=SpriteBounds(p);
        dc.PushOpacity(p.Opacity);
        var cropLegs = p.SeatAnchor != null && p.SeatPhase != "turn";
        if (cropLegs) dc.PushClip(new RectangleGeometry(new(bounds.X,bounds.Y,bounds.Width,Math.Max(0,p.SeatAnchor!.Y+1-bounds.Y))));
        if (p.Action == "beanbag")
        {
            var raw = UnposedContentBounds(p); var center = RecliningCenter(p);
            var cx = raw.X + raw.Width / 2; var cy = raw.Y + raw.Height / 2;
            dc.PushTransform(new TranslateTransform(Math.Round(center.X - cx), Math.Round(center.Y - cy)));
            dc.PushTransform(new RotateTransform(90, cx, cy));
        }
        dc.DrawImage(Image(p.Sprite!),bounds);
        if (p.Action == "beanbag") { dc.Pop(); dc.Pop(); }
        if (cropLegs) dc.Pop();
        DrawActivity(dc, agent);
        if (p.CarryingPaper && p.Action is "walking" or "entering")
        { var x=(p.X+.5)*16; var y=(p.Y+1)*16-12; R(dc,"#fff9e7",x+(p.Facing=="left" ? -10 : 4),y,6,8); }
        dc.Pop();
    }
    private void DrawLaptop(DrawingContext dc, Pose p)
    {
        var bounds=LaptopBounds(p);var lx=bounds.X-2;var ly=bounds.Y+2;
        dc.PushOpacity(p.Opacity);
        if (assets.Images.ContainsKey("office-extras"))
            dc.DrawImage(Image(assets.Frame("office-extras","laptop","any",0)),bounds);
        else { R(dc,"#19292d",lx,ly,19,11); R(dc,"#8fd7c4",lx+2,ly+2,15,6); R(dc,"#bec9be",lx-1,ly+10,21,3); }
        dc.Pop();
    }
    private Rect LaptopBounds(Pose p)
    {
        var body=ContentBounds(p);
        return new(Math.Round(body.X+body.Width/2-8),Math.Round(body.Bottom-12),16,16);
    }
    private Rect DrawTag(DrawingContext dc, AgentView agent, Point anchor, List<Rect> plates, bool stack,string key)
    {
        var sourceName = TagText(agent);
        if (!names.TryGetValue(sourceName,out var measured))
        {
            var elements = StringInfo.ParseCombiningCharacters(sourceName);
            var text = elements.Length > 14 ? sourceName[..elements[14]]+"…" : sourceName;
            measured = (text,Math.Clamp(Math.Ceiling(FormatText(text,6,"#f5edce").WidthIncludingTrailingWhitespace)+16,27,99));
            if (names.Count >= 256) names.Clear();
            names[sourceName] = measured;
        }
        var iconSize=ProviderIconSize(agent.Agent);
        var name=measured.Text; var width=measured.Width+iconSize-12;
        var plate = new Rect(Math.Round(Math.Clamp(anchor.X-width/2,2,Math.Max(2,office.WidthTiles*16-width-2))),Math.Round(anchor.Y)-5,width,14);
        var baseY=plate.Y;
        if (tagMode!="always") plate.Y-=tagOffsets.GetValueOrDefault(key);
        var outline = plate; outline.Inflate(1,1);
        if (stack)
            for (var attempt=0;attempt<20+plates.Count*8 && plates.Any(p=>p.IntersectsWith(outline));attempt++)
            { plate.Y-=2; outline.Y-=2; }
        // Keep stacking for always mode and simultaneous entering/leaving hover plates.
        // Retain the offset until hidden: a fading neighbour must not move a plate out from under the pointer.
        if (tagMode!="always") tagOffsets[key]=baseY-plate.Y;
        plates.Add(outline);
        R(dc,agent.Agent.IsSubagent ? "#738b84" : "#c99a52",plate.X-1,plate.Y-1,plate.Width+2,plate.Height+2);
        R(dc,"#172b30",plate.X,plate.Y,plate.Width,plate.Height);
        DrawProviderIcon(dc,agent.Agent,plate.X+1,plate.Y+(14-iconSize)/2);
        Text(dc,name,plate.X+iconSize+3,plate.Y+4,6,"#f5edce",maxWidth:plate.Width-iconSize-4);
        return plate;
    }
    private int ProviderIconSize(AgentInfo agent) => zoom < 2 && assets.Images.ContainsKey("provider-icons-10") && brandIcons.Get(agent.Provider, (int)Math.Ceiling(zoom)) == null ? 10 : 12;
    private void DrawProviderIcon(DrawingContext dc,AgentInfo agent,double x,double y)
    {
        var icon = brandIcons.Get(agent.Provider, (int)Math.Ceiling(zoom));
        var size=ProviderIconSize(agent);
        var sheet=size==10 && assets.Images.ContainsKey("provider-icons-10") ? "provider-icons-10" : "provider-icons";
        if (icon == null && assets.Images.ContainsKey(sheet))
        {
            var provider=agent.Provider.ToString().ToLowerInvariant();
            var role=provider+(agent.IsSubagent ? "-sub-agent" : "-supervisor");
            var clip=assets.Manifest.Sheets.First(s=>s.Id==sheet).Rows.Any(r=>r.Name==role) ? role : provider;
            icon = Image(assets.Frame(sheet,clip,"any",0));
        }
        if (icon != null) dc.DrawImage(icon,new(Math.Round(x),Math.Round(y),size,size));
        else DrawNeutralIcon(dc,agent.Provider,x,y);
    }
    private void DrawNeutralIcon(DrawingContext dc, Provider provider, double x, double y)
    {
        // Neutral shapes for custom packs with no provider sheet; no logo lookalikes.
        var rows=provider switch
        {
            Provider.Codex => new[] { "000000000000","000011110000","001111111100","001111111100","011111111110","011111111110","011111111110","011111111110","001111111100","001111111100","000011110000","000000000000" },
            Provider.Claude => new[] { "000000000000","000001000000","000001100000","000001100000","011111111110","001111111100","000111111000","000111111000","000110011000","001100001100","001000000100","000000000000" },
            _ => new[] { "000000000000","000001100000","000011110000","000111111000","001111111100","011111111110","011111111110","001111111100","000111111000","000011110000","000001100000","000000000000" }
        };
        for(var py=0;py<12;py++) for(var px=0;px<12;px++)
            if(rows[py][px]=='1') R(dc,ProviderColor(provider),x+px,y+py,1,1);
    }
    private void DrawBubble(DrawingContext dc, AgentView agent)
    {
        var p=agent.Pose;
        if (p.Queued) return;
        if (assets.Images.ContainsKey("bubble-icons"))
        {
            var glyph = p.Action switch { "beanbag" => "zzz", "aquarium" => "heart", "window" => "sparkles",
                "vending" or "fridge" => "coffee", "reading" => "light-bulb", _ => null };
            if (glyph != null)
            {
                var body = ContentBounds(p); dc.PushOpacity(p.Opacity);
                dc.DrawImage(Image(assets.Frame("bubble-icons",glyph,"any",0)),new(Math.Round(body.Right+2),HeadY(p)-2,12,12));
                dc.Pop(); return;
            }
        }
        if (p.Action == "waiting")
        {
            var body = ContentBounds(p);
            dc.PushOpacity(p.Opacity);
            R(dc,"#738b84",body.Right+2,body.Top,9,10); R(dc,"#172b30",body.Right+3,body.Top+1,7,8);
            Text(dc,"?",body.Right+4,body.Top+1,6,"#f5edce"); dc.Pop(); return;
        }
        if (assets.Images.ContainsKey("icons"))
        {
            var clip = p.Reaction != null ? "bubble-reaction" : p.Action switch
            {
                "napping" => "bubble-napping", "waiting" => "bubble-waiting", "thinking" => "bubble-thinking",
                "reporting" => "bubble-done", "typing" => "bubble-working", _ => null
            };
            if (clip == null) return;
            var f = assets.Frame("icons",clip,"any",0);
            dc.PushOpacity(p.Opacity);
            var body = ContentBounds(p);
            dc.DrawImage(Image(f),new(Math.Round(body.Right+2),HeadY(p)-2,12,12));
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
        dc.PushOpacity(p.Opacity); dc.DrawImage(Image(s),new((p.X+.5)*16+15,rect.Top-41,16,24)); dc.Pop();
    }
    public Point AgentPoint(AgentView agent)
    {
        var b=ContentBounds(agent.Pose); var o=Origin;
        return new(o.X+(b.X+b.Width/2)*zoom,o.Y+(b.Y+b.Height/2)*zoom);
    }
    public AgentView? HitAgent(Point point)
    {
        var o=Origin; var world=new Point((point.X-o.X)/zoom,(point.Y-o.Y)/zoom);
        return agents.Where(a=>a.Pose.Visible).OrderByDescending(a=>AgentDepth(a.Pose)).ThenByDescending(a=>a.Agent.Id,StringComparer.Ordinal)
            .FirstOrDefault(a=>ContentBounds(a.Pose).Contains(world));
    }
    public Tile WorldTile(Point point)
    {
        var o=Origin;
        return new((int)Math.Floor((point.X-o.X)/zoom/16),(int)Math.Floor((point.Y-o.Y)/zoom/16));
    }
    public BitmapSource FurnitureThumbnail(FurnitureAsset asset)
    {
        var v=asset.Variants.FirstOrDefault(v=>v.State=="default") ?? asset.Variants[0];
        if (v.SheetId != null && v.Animation != null) return Image(assets.Frame(v.SheetId,v.Animation,"any",0));
        var drawing=new DrawingVisual();
        using(var dc=drawing.RenderOpen()) DrawFallbackFurniture(dc,new(asset.Kind,0,1,v.FootprintW,v.FootprintH,Id:"thumbnail",AssetId:asset.Id));
        var bitmap=new RenderTargetBitmap(80,80,96,96,PixelFormats.Pbgra32); bitmap.Render(drawing); bitmap.Freeze(); return bitmap;
    }
    public BitmapSource TileThumbnail(TileType tile)
    {
        if(tile.SheetId != null && tile.Animation != null) return Image(assets.Frame(tile.SheetId,tile.Animation,"any",0));
        var drawing=new DrawingVisual(); using(var dc=drawing.RenderOpen()) dc.DrawRectangle(Color(tile.Color),null,new(0,0,16,16));
        var bitmap=new RenderTargetBitmap(16,16,96,96,PixelFormats.Pbgra32);bitmap.Render(drawing);bitmap.Freeze();return bitmap;
    }
    private void DrawEditorGrid(DrawingContext dc)
    {
        var grid=new Pen(Color("#35bfe9da"),.5);
        for(var x=0;x<=office.WidthTiles;x++) dc.DrawLine(grid,new(x*16,0),new(x*16,office.HeightTiles*16));
        for(var y=0;y<=office.HeightTiles;y++) dc.DrawLine(grid,new(0,y*16),new(office.WidthTiles*16,y*16));
        foreach(var zone in office.Zones ?? Array.Empty<Zone>())
        {
            if(zone.Name is not ("workspace" or "pantry" or "lounge")) continue;
            dc.DrawRectangle(null,new Pen(Color("#998fe1ce"),1),new(zone.X*16,zone.Y*16,zone.Width*16,zone.Height*16));
            R(dc,"#172b30",zone.X*16,zone.Y*16,zone.Name.Length*5+4,11);
            Text(dc,zone.Name,zone.X*16+2,zone.Y*16+1,7,"#bcf0df");
        }
        void Marker(Tile tile,string color)
        { dc.DrawRectangle(null,new Pen(Color(color),1),new(tile.X*16+3,tile.Y*16+3,10,10)); }
        Marker(office.Door,"#f9d774");
        foreach(var seat in office.OverflowSeats ?? Array.Empty<OverflowSeat>()) Marker(new(seat.X,seat.Y),"#9fc8ff");
        foreach(var desk in office.Furniture)
            foreach(var seat in assets.Variant(desk).Points.Where(p=>p.Action=="typing")) Marker(new(desk.X+seat.X,desk.Y+seat.Y),"#8ee6b0");
        var selected=office.Furniture.FirstOrDefault(f=>f.Id==EditSelection);
        if(selected != null)
        {
            var tile=EditPreview ?? new Tile(selected.X,selected.Y);
            dc.DrawRectangle(Color("#308ee6b0"),new Pen(Color("#baf4cb"),1),new(tile.X*16,tile.Y*16,selected.Width*16,selected.Height*16));
        }
    }
    public void SimulateHover(Point? point, DateTimeOffset now)
    {
        PollHover(point,now,offscreen:true);
    }
    public static string TooltipText(AgentView agent, DateTimeOffset now, PrivacyMode privacy = PrivacyMode.Off)
    {
        var a=agent.Agent;
        var task=privacy != PrivacyMode.Off ? "Hidden" : string.IsNullOrWhiteSpace(a.Task) ? "No task recorded" : a.Task.Length>150 ? a.Task[..147]+"…" : a.Task;
        return $"{a.Provider} · {a.Name}\n{(a.IsSubagent ? "Sub-agent" : "Supervisor")}{(a.Role==null ? "" : " · "+a.Role)}\nModel   {(privacy==PrivacyMode.Full ? "Hidden" : a.Model ?? "unknown")}\nProject   {(privacy==PrivacyMode.Full ? "Hidden" : a.Cwd ?? "unknown")}\nTask   {task}\nTool   {(privacy!=PrivacyMode.Off ? "Hidden" : a.Tool ?? "none")}\n{a.State.ToString().ToLowerInvariant()} · {Math.Max(0,(now-a.StateSince).TotalSeconds):0}s · {agent.Pose.Action}";
    }

    public void PollHover(Point? cursor, DateTimeOffset now) => PollHover(cursor,now,offscreen:false);
    internal void AdvanceHover(DateTimeOffset now)
    {
        if (tagHover.Advance(now)) RenderTags();
    }
    private (string? Key,AgentView? Agent) HoverTarget(Point? cursor)
    {
        if (cursor is not {} point || drag != null || point.X<0 || point.Y<0 || point.X>=ActualWidth || point.Y>=ActualHeight) return (null,null);
        var origin=Origin; var world=new Point((point.X-origin.X)/zoom,(point.Y-origin.Y)/zoom);
        // Another body takes priority over the incumbent's bridge or fading plate.
        var agent=HitAgent(point);
        if (agent != null) return ("agent:"+agent.Agent.Id,agent);
        foreach (var person in agents.Where(a=>a.Pose.Visible))
        {
            var key="agent:"+person.Agent.Id;
            if (tagRects.TryGetValue(key,out var plate))
            {
                var hit=plate;
                if (tagHover.Target==key) hit.Union(ContentBounds(person.Pose));
                hit.Inflate(2/zoom,2/zoom);
                if (hit.Contains(world)) return (key,person);
            }
            if (iconRects.TryGetValue(key,out var icon) && icon.Contains(world)) return (key,person);
        }
        foreach (var desk in office.Furniture.Where(f=>f.AgentId != null))
        {
            if (!byId.TryGetValue(desk.AgentId!,out var owner) || !OwnerAway(owner)) continue;
            var key="desk:"+desk.Id; var hit=new Rect(desk.X*16,desk.Y*16,desk.Width*16,desk.Height*16);
            if (tagHover.Target==key && tagRects.TryGetValue(key,out var plate)) hit.Union(plate);
            if (hit.Contains(world) || tagRects.TryGetValue(key,out plate) && plate.Contains(world)) return (key,owner);
        }
        return (null,null);
    }
    private void PollHover(Point? cursor,DateTimeOffset now,bool offscreen)
    {
        var (key,agent)=HoverTarget(cursor);
        var previous=tagHover.Target;
        var changed=tagHover.Update(key,now);
        hovered=agent?.Agent.Id;
        if (key!=previous || key==null || EditorMode) CloseTooltip();
        if (changed) RenderTags();
        if (agent==null || EditorMode || (now-tagHover.Entered).TotalMilliseconds<NameTagHover.TooltipDelayMs) return;
        var body=TooltipText(agent,now,PrivacyMode);
        if (body != tipText && (tipText==null || (now-tipUpdated).TotalMilliseconds>=200))
        { tipTitle.Text=body.Split('\n')[0]; tipBody.Text=body[(body.IndexOf('\n')+1)..]; tipText=body; tipUpdated=now; }
        tooltip.Child.Measure(new Size(Math.Max(1,ActualWidth-16),double.PositiveInfinity));
        var size=offscreen ? new Size(Math.Min(340,ActualWidth-16),190) : tooltip.Child.DesiredSize;
        tooltipRect=PlaceTooltip(cursor!.Value,size);
        if (tooltipRect.IsEmpty) { CloseTooltip(); return; }
        if (offscreen)
        { tooltip.IsOpen=false; debugTooltip=true; DebugTooltipText=body; RenderTags(); }
        else
        { tooltip.HorizontalOffset=tooltipRect.X; tooltip.VerticalOffset=tooltipRect.Y; tooltip.IsOpen=true; }
    }
    private Rect PlaceTooltip(Point point,Size size)
    {
        if (size.Width>ActualWidth-16 || size.Height>ActualHeight-16) return Rect.Empty;
        var plate=tagHover.Target is {} key ? ScreenRect(tagRects.GetValueOrDefault(key,Rect.Empty)) : Rect.Empty;
        if (plate.IsEmpty) plate=new(point,new Size(1,1));
        foreach (var candidate in new[] { new Point(plate.Right+12,point.Y+16),new Point(plate.Left-size.Width-12,point.Y+16),
            new Point(point.X+12,plate.Bottom+12),new Point(point.X+12,plate.Top-size.Height-12) })
        {
            var rect=new Rect(Math.Clamp(candidate.X,8,ActualWidth-size.Width-8),Math.Clamp(candidate.Y,8,ActualHeight-size.Height-8),size.Width,size.Height);
            if (!tagRects.Values.Select(ScreenRect).Any(p=> { p.Inflate(4,4); return p.IntersectsWith(rect); })) return rect;
        }
        return Rect.Empty;
    }
    private void CloseTooltip()
    {
        var redraw=debugTooltip;
        tooltip.IsOpen=false; tipText=null; tipTitle.Text=""; tipBody.Text=""; debugTooltip=false; DebugTooltipText=null; tooltipRect=Rect.Empty;
        if (redraw) RenderTags();
    }
    public void HideTooltip() { CloseTooltip(); hovered=null; tagHover.Clear(); RenderTags(); }
}
