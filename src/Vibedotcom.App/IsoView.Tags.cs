using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Vibedotcom.Core;

namespace Vibedotcom.App;

internal sealed partial class IsoView
{
    private readonly NameTagHover tagHover = new();
    private readonly BrandIcons brandIcons;
    private readonly IsoProviderIcons bundledIcons=new();
    private readonly Dictionary<string,AgentInfo> byId = new(StringComparer.Ordinal);
    private readonly Dictionary<string,Rect> tagRects = new(StringComparer.Ordinal), iconRects = new(StringComparer.Ordinal);
    private readonly Dictionary<string,FormattedText> textCache = new(StringComparer.Ordinal);
    private readonly Dictionary<Provider,DrawingImage> neutralIcons = new();
    private readonly List<Rect> plates = new();
    private readonly SolidColorBrush plateFill = Frozen(0x17,0x2b,0x30), plateText = Frozen(0xf5,0xed,0xce);
    private readonly SolidColorBrush leadEdge = Frozen(0xc9,0x9a,0x52), childEdge = Frozen(0x73,0x8b,0x84);
    private string tagMode = "hover";
    private Point? hoverPoint;
    private Rect tooltipRect = Rect.Empty;
    public string TagMode
    {
        get => tagMode;
        set { var mode=NameTagHover.Normalize(value); if (tagMode==mode) return; tagMode=mode; HideTooltip(); RenderTags(); }
    }
    internal bool TagsAnimating => tagHover.Animating;
    internal bool HasHoverTarget => tagHover.Target!=null;
    internal bool TooltipPending => tagHover.Target!=null && DebugTooltipText==null && byId.ContainsKey(tagHover.Target);
    internal string? DebugTooltipText { get; private set; }
    internal int RenderedAgentTags { get; private set; }
    internal int RenderedProviderIcons { get; private set; }
    internal int TagRenderCount { get; private set; }
    internal Rect AgentTagRect(string id) => ScreenRect(tagRects.GetValueOrDefault(id,Rect.Empty));
    internal Rect AgentIconRect(string id) => ScreenRect(iconRects.GetValueOrDefault(id,Rect.Empty));
    internal Rect TooltipRect => ScreenRect(tooltipRect);
    private Rect ScreenRect(Rect rect) => rect.IsEmpty ? Rect.Empty : new MatrixTransform(camera).TransformBounds(rect);
    private static SolidColorBrush Frozen(byte r,byte g,byte b)
    { var brush=new SolidColorBrush(Color.FromRgb(r,g,b)); brush.Freeze(); return brush; }
    private FormattedText NameText(string name)
    {
        if (textCache.TryGetValue(name,out var text)) return text;
        if (textCache.Count>=512) textCache.Clear();
        text=new(name,CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface("Consolas"),6,plateText,1);
        textCache[name]=text; return text;
    }
    private void RebuildTags()
    {
        byId.Clear(); foreach (var agent in agents) byId[agent.Id]=agent;
        if (DebugTooltipText!=null) DebugTooltipText=tagHover.Target!=null && byId.TryGetValue(tagHover.Target,out var current) ? TooltipText(current,previous) : null;
        RenderTags();
    }
    private void RenderTags()
    {
        TagRenderCount++; tagRects.Clear(); iconRects.Clear(); plates.Clear();
        RenderedAgentTags=RenderedProviderIcons=0;
        using var dc=tagsVisual.RenderOpen();
        foreach (var label in scene.Labels)
        {
            byId.TryGetValue(label.AgentId,out var agent);
            var opacity=tagMode=="always" ? 1 : tagHover.Opacity(label.AgentId);
            if (opacity>0)
            {
                var elements=StringInfo.ParseCombiningCharacters(label.Text);
                var name=elements.Length>14 ? label.Text[..elements[14]]+"…" : label.Text;
                var text=NameText(name);
                var width=Math.Clamp(Math.Ceiling(text.WidthIncludingTrailingWhitespace)+17,27,112);
                var rect=new Rect(Math.Clamp(label.X-width/2,1,Math.Max(1,scene.Width-width-1)),Math.Clamp(label.Y-15,1,Math.Max(1,scene.Height-15)),width,14);
                rect=new(Snap(rect.X),Snap(rect.Y),rect.Width,rect.Height);
                var outline=rect; outline.Inflate(1,1);
                var desiredY=rect.Y;
                while(rect.Y>=3 && plates.Any(p=>p.IntersectsWith(outline))) { rect.Y-=2; outline.Y-=2; }
                if (plates.Any(p=>p.IntersectsWith(outline)))
                {
                    rect.Y=desiredY; outline.Y=desiredY-1;
                    while(rect.Bottom+2<=scene.Height-1 && plates.Any(p=>p.IntersectsWith(outline))) { rect.Y+=2; outline.Y+=2; }
                }
                plates.Add(outline); tagRects[label.AgentId]=rect;
                dc.PushOpacity(opacity);
                dc.DrawRectangle(agent?.IsSubagent==true ? childEdge : leadEdge,null,outline);
                dc.DrawRectangle(plateFill,null,rect);
                DrawProviderIcon(dc,agent,new Rect(rect.X+1,rect.Y+1,12,12));
                dc.DrawText(text,new Point(rect.X+15,rect.Y+4)); dc.Pop(); RenderedAgentTags++;
            }
            if (tagMode=="icon" && opacity<1)
            {
                var rect=new Rect(label.X-6,label.Y-14,12,12); iconRects[label.AgentId]=rect;
                dc.PushOpacity(1-opacity); DrawProviderIcon(dc,agent,rect); dc.Pop(); RenderedProviderIcons++;
            }
        }
        if (DebugTooltipText!=null && hoverPoint is {} point)
        {
            var lines=DebugTooltipText.Split('\n');
            var width=Math.Min(250,scene.Width-4); var height=lines.Length*10+12;
            var x=Math.Clamp(point.X+8,2,Math.Max(2,scene.Width-width-2));
            var y=Math.Clamp(point.Y+14,2,Math.Max(2,scene.Height-height-2));
            tooltipRect=new(Snap(x),Snap(y),width,height);
            dc.DrawRectangle(leadEdge,null,new Rect(tooltipRect.X-1,tooltipRect.Y-1,width+2,height+2));
            dc.DrawRectangle(plateFill,null,tooltipRect);
            for (var i=0;i<lines.Length;i++)
            {
                var text=NameText(lines[i]);
                dc.PushClip(new RectangleGeometry(tooltipRect)); dc.DrawText(text,new Point(tooltipRect.X+6,tooltipRect.Y+6+i*10)); dc.Pop();
            }
        }
        else tooltipRect=Rect.Empty;
    }
    private void DrawProviderIcon(DrawingContext dc,AgentInfo? agent,Rect rect)
    {
        var provider=agent?.Provider ?? Provider.Codex;
        var icon=brandIcons.Get(provider,zoom) ?? bundledIcons.Get(provider,agent?.IsSubagent ?? false,zoom);
        if (icon!=null) { dc.DrawImage(icon,icon.PixelWidth==10 ? new Rect(rect.X+1,rect.Y+1,10,10) : rect); return; }
        if (!neutralIcons.TryGetValue(provider,out var generic))
        {
            string[] rows=provider switch
            {
                Provider.Codex => ["000000000000","000011110000","001111111100","001111111100","011111111110","011111111110","011111111110","011111111110","001111111100","001111111100","000011110000","000000000000"],
                Provider.Claude => ["000000000000","000001000000","000001100000","000001100000","011111111110","001111111100","000111111000","000111111000","000110011000","001100001100","001000000100","000000000000"],
                _ => ["000000000000","000001100000","000011110000","000111111000","001111111100","011111111110","011111111110","001111111100","000111111000","000011110000","000001100000","000000000000"]
            };
            var brush=provider switch { Provider.Codex=>Frozen(0x32,0x9b,0x91),Provider.Claude=>Frozen(0xd5,0x7e,0x4e),_=>Frozen(0x82,0x7e,0xd1) };
            var drawing=new DrawingGroup();
            using (var context=drawing.Open())
                for (var y=0;y<12;y++) for (var x=0;x<12;x++) if (rows[y][x]=='1') context.DrawRectangle(brush,null,new Rect(x,y,1,1));
            // Preserve the 12x12 canvas of the existing neutral provider path.
            using (var context=drawing.Append()) context.DrawRectangle(Brushes.Transparent,null,new Rect(0,0,12,12));
            drawing.Freeze(); generic=new(drawing); generic.Freeze(); neutralIcons[provider]=generic;
        }
        dc.DrawImage(generic,rect);
    }
    private string TooltipText(AgentInfo agent,DateTimeOffset now)
    {
        var clip=scene.Items.FirstOrDefault(i=>i.Layer==Vibedotcom.Core.Iso.IsoLayer.Character && i.HitTargetId==agent.Id)?.Clip ?? "idle";
        var action=clip.Contains("typing",StringComparison.OrdinalIgnoreCase) ? "typing" : clip.Contains("walk",StringComparison.OrdinalIgnoreCase) ? "walk"
            : clip.Contains("thinking",StringComparison.OrdinalIgnoreCase) ? "thinking" : clip.Contains("sleep",StringComparison.OrdinalIgnoreCase) ? "sleep" : "idle";
        return OfficeView.TooltipText(new(agent,new(0,0,new(0,0),action,"down",0,0,true)),now);
    }
    internal void PollHover(Point? point,DateTimeOffset now)
    {
        var target=(string?)null;
        if (point is {} screen)
        {
            var native=ScenePoint(screen); hoverPoint=native;
            foreach (var (id,rect) in tagRects)
            {
                var bridge=rect; bridge.Height+=8;
                if (bridge.Contains(native)) { target=id; break; }
            }
            if (target==null)
                foreach (var (id,rect) in iconRects)
                    if (rect.Contains(native)) { target=id; break; }
            target ??= HitTestScene(screen)?.TargetId;
        }
        else hoverPoint=null;
        var old=tagHover.Target;
        var changed=tagHover.Update(target,now);
        if (old!=target || target==null) { changed|=DebugTooltipText!=null; DebugTooltipText=null; }
        if (target!=null && byId.TryGetValue(target,out var agent) && (now-tagHover.Entered).TotalMilliseconds>=NameTagHover.TooltipDelayMs)
        {
            // The supplied AgentInfo is already privacy-filtered by the tracker.
            var text=TooltipText(agent,now);
            changed|=text!=DebugTooltipText; DebugTooltipText=text;
        }
        if (changed) RenderTags();
        ScheduleAnimation(now);
    }
    internal void SimulateHover(Point? point,DateTimeOffset now) => PollHover(point,now);
    internal void AdvanceHover(DateTimeOffset now)
    {
        if (TagsAnimating && tagHover.Advance(now)) RenderTags();
        if (TooltipPending && hoverPoint is {} point && (now-tagHover.Entered).TotalMilliseconds>=NameTagHover.TooltipDelayMs)
            PollHover(ScreenPoint(point),now);
    }
    internal void HideTooltip()
    {
        var changed=HasHoverTarget || TagsAnimating || DebugTooltipText!=null;
        tagHover.Clear(); DebugTooltipText=null; hoverPoint=null;
        if (changed) RenderTags();
        ScheduleAnimation(DateTimeOffset.UtcNow);
    }
}
