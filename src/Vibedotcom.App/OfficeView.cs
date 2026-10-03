using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Controls;
using Vibedotcom.Core;

namespace Vibedotcom.App;

internal sealed class OfficeView : FrameworkElement
{
    private OfficeLayout office = new OfficeAnimator().Layout;
    private IReadOnlyList<AgentView> agents = Array.Empty<AgentView>();
    private readonly BitmapSource[,] sprites = new BitmapSource[SpriteAtlas.Rows, SpriteAtlas.Columns];
    private readonly ToolTip tooltip = new() { MaxWidth = 440, Placement = System.Windows.Controls.Primitives.PlacementMode.Mouse };
    private string? hovered;
    private double zoom = 1.25;
    public double Zoom
    {
        get => zoom;
        set { zoom = Math.Clamp(value, .5, 4); Resize(); }
    }
    private static readonly Brush Ink = Brush("#34443f"), Muted = Brush("#667369");
    private static readonly Brush[] ProviderBrushes = { Brush("#409e88"), Brush("#cd7755"), Brush("#7c89c9") };
    private static Brush Brush(string color) { var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(color)!; brush.Freeze(); return brush; }
    private readonly Dictionary<string, Brush> brushes = new();
    private Brush Color(string color) { if (!brushes.TryGetValue(color, out var result)) brushes[color] = result = Brush(color); return result; }

    public OfficeView()
    {
        using var stream = new MemoryStream(SpriteAtlas.CreatePng());
        var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.StreamSource = stream; image.EndInit(); image.Freeze();
        for (var row = 0; row < SpriteAtlas.Rows; row++) for (var column = 0; column < SpriteAtlas.Columns; column++)
        {
            var sprite = new CroppedBitmap(image, new(column * 16, row * 24, 16, 24)); sprite.Freeze(); sprites[row, column] = sprite;
        }
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.NearestNeighbor);
        RenderOptions.SetEdgeMode(this, EdgeMode.Aliased);
        SnapsToDevicePixels = true;
        MouseMove += Hover;
        MouseLeave += (_, _) => { tooltip.IsOpen = false; hovered = null; };
        Unloaded += (_, _) => tooltip.IsOpen = false;
        Resize();
    }

    public void Update(OfficeLayout layout, IReadOnlyList<AgentView> views)
    { office = layout; agents = views; Resize(); }
    private void Resize() { Width = office.WidthTiles * 16 * zoom; Height = office.HeightTiles * 16 * zoom; InvalidateVisual(); }

    protected override void OnRender(DrawingContext dc)
    {
        dc.PushTransform(new ScaleTransform(zoom, zoom));
        var width = office.WidthTiles * 16; var height = office.HeightTiles * 16;
        void Rect(string color, double x, double y, double w, double h) => dc.DrawRectangle(Color(color), null, new(x, y, w, h));
        Rect("#e8dec9", 0, 0, width, height);
        Rect("#f5eddd", 0, 0, width, 48); Rect("#a99e87", 0, 46, width, 2);
        Rect("#a99e87", 0, height - 16, width, 16);
        Rect("#a99e87", 0, 48, 16, height - 48); Rect("#a99e87", width - 16, 48, 16, height - 48);
        for (var y = 64; y < height - 16; y += 32) Rect("#dfd2bb", 16, y, width - 32, 1);
        Rect("#dbe1cb", 608, 49, 144, height - 65);
        for (var x = 72; x < 550; x += 160)
        {
            Rect("#a99e87", x, 8, 100, 30); Rect("#a8c9cf", x + 3, 11, 94, 24);
            Rect("#cde3df", x + 3, 11, 94, 9); Rect("#718e7c", x + 3, 30, 94, 5);
            Rect("#f5eddd", x + 47, 10, 3, 27);
        }
        Text(dc, "VIBEDOTCOM", 620, 14, 11, Ink, true);
        Text(dc, "PANTRY / BREAK ROOM", 620, 64, 9, Ink, true);
        Text(dc, "WORK FLOOR", 34, 65, 9, Muted, true);
        var door = office.Door;
        Rect("#34443f", 0, door.Y * 16 - 12, 16, 44); Rect("#92b5a3", 2, door.Y * 16 - 10, 12, 40);
        Rect("#efd998", 11, door.Y * 16 + 6, 2, 2);
        Text(dc, "IN / OUT", 26, door.Y * 16 + 14, 8, Muted);
        foreach (var f in office.Furniture)
        {
            var x = f.X * 16; var y = f.Y * 16; var w = f.Width * 16; var h = f.Height * 16;
            switch (f.Kind)
            {
                case "desk":
                    Rect("#b7a07b", x, y + 15, w, 14); Rect("#e9bd81", x, y + 13, w, 12);
                    Rect("#997f60", x + 3, y + 28, 3, 4); Rect("#997f60", x + w - 6, y + 28, 3, 4);
                    Rect("#34443f", x + 14, y + 1, 21, 15); Rect("#92b5a3", x + 16, y + 3, 17, 10);
                    Rect("#d8e4c8", x + 18, y + 5, 9, 1); Rect("#d8e4c8", x + 18, y + 8, 12, 1);
                    Rect("#34443f", x + 23, y + 16, 3, 3); Rect("#f5eddd", x + 15, y + 21, 17, 3);
                    var owner = agents.FirstOrDefault(a => a.Agent.Id == f.AgentId);
                    if (owner != null)
                    {
                        dc.DrawRectangle(ProviderBrushes[(int)owner.Agent.Provider], null, new(x, y + 54, 4, 4));
                        Text(dc, owner.Agent.Name, x + 7, y + 52, 8, Ink, maxWidth: 90);
                    }
                    break;
                case "pantry":
                    Rect("#8f9d7d", x, y, w, h); Rect("#ebd5a8", x, y, w, 7);
                    for (var cabinet = x + 2; cabinet < x + w; cabinet += 25) Rect("#ccd1ae", cabinet, y + 10, 21, 18);
                    Rect("#34443f", x + 5, y - 11, 13, 15); Rect("#efcf86", x + 8, y - 6, 6, 5);
                    Rect("#f5eddd", x + 27, y - 6, 6, 6); Rect("#34443f", x + 76, y - 3, 22, 5);
                    break;
                case "table": Rect("#a58863", x, y + 5, w, 11); Rect("#e9bd81", x - 2, y, w + 4, 7); break;
                case "tv":
                    Rect("#34443f", x, y - 16, w, 32); Rect("#718e7c", x + 3, y - 13, w - 6, 25);
                    Rect("#c86c52", x + 23, y - 6, 19, 12); Text(dc, "PLAY", x + 25, y - 5, 6, Brush("#fff6df"));
                    Text(dc, "TV / SOFA", x + 2, y + 24, 8, Ink);
                    break;
                case "sofa": Rect("#536c61", x, y, w, h); Rect("#7fa08a", x + 4, y + 3, w - 8, h - 6); Rect("#536c61", x + w / 2, y + 2, 2, h - 4); break;
                case "plant": Rect("#a58863", x + 4, y + 6, 9, 10); Rect("#536c61", x + 5, y - 8, 7, 17); Rect("#7fa08a", x, y - 4, 16, 7); break;
            }
        }
        var byId = agents.ToDictionary(a => a.Agent.Id);
        var line = new Pen(Color("#a99e87"), 1) { DashStyle = DashStyles.Dot };
        foreach (var child in agents.Where(a => a.Pose.Visible && a.Agent.ParentId != null))
            if (byId.TryGetValue(child.Agent.ParentId!, out var parent) && parent.Pose.Visible)
                dc.DrawLine(line, new((child.Pose.X + .5) * 16, (child.Pose.Y + .5) * 16), new((parent.Pose.X + .5) * 16, (parent.Pose.Y + .5) * 16));
        foreach (var agent in agents.Where(a => a.Pose.Visible).OrderBy(a => a.Pose.Y))
        {
            var pose = agent.Pose; var x = Math.Round((pose.X + .5) * 16 - 8); var y = Math.Round((pose.Y + 1) * 16 - 24);
            if (pose.Facing == "left")
            { dc.PushTransform(new ScaleTransform(-1, 1, x + 8, y + 12)); dc.DrawImage(sprites[pose.SpriteRow, pose.SpriteColumn], new(x, y, 16, 24)); dc.Pop(); }
            else dc.DrawImage(sprites[pose.SpriteRow, pose.SpriteColumn], new(x, y, 16, 24));
            if (agent.Agent.State == AgentState.Working && agent.Agent.ToolKind != null)
            {
                var icon = agent.Agent.ToolKind switch { "shell" => 0, "file" => 1, "search" => 2, "agent" => 3, _ => 4 };
                dc.DrawImage(sprites[6, icon], new(x, y - 18, 16, 24));
            }
        }
        if (agents.Count == 0)
        {
            Text(dc, "The office is quiet.", 170, 200, 18, Ink, true);
            Text(dc, "Recent agent sessions will walk in here automatically.", 170, 234, 11, Ink);
        }
        dc.Pop();
    }

    private void Text(DrawingContext dc, string text, double x, double y, double size, Brush brush, bool bold = false, double maxWidth = 500)
    {
        var formatted = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, bold ? FontWeights.SemiBold : FontWeights.Normal, FontStretches.Normal), size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip)
        { MaxTextWidth = maxWidth, MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis };
        dc.DrawText(formatted, new(x, y));
    }

    private void Hover(object sender, MouseEventArgs e)
    {
        var point = e.GetPosition(this); var x = point.X / zoom / 16; var y = point.Y / zoom / 16;
        var agent = agents.LastOrDefault(a => a.Pose.Visible && Math.Abs(x - (a.Pose.X + .5)) < .8 && y > a.Pose.Y - 1 && y < a.Pose.Y + 1);
        if (agent == null) { tooltip.IsOpen = false; hovered = null; return; }
        if (hovered == agent.Agent.Id && tooltip.IsOpen) return;
        hovered = agent.Agent.Id;
        var a = agent.Agent;
        tooltip.Content = $"{a.Provider} · {a.Name} ({(a.IsSubagent ? "subagent" : "supervisor")})\nRole: {a.Role ?? "unknown"}\nModel: {a.Model ?? "unknown"}\nProject: {a.Cwd ?? "unknown"}\nTask: {(a.Task.Length == 0 ? "unknown" : a.Task)}\nTool: {a.Tool ?? "none"}\nState: {a.State.ToString().ToLowerInvariant()} · {Math.Max(0, (DateTimeOffset.UtcNow - a.StateSince).TotalSeconds):0}s\nLast activity: {a.LastSeen.LocalDateTime:t}{(a.BestEffort ? " · best-effort" : "")}";
        tooltip.PlacementTarget = this; tooltip.IsOpen = true;
    }
}
