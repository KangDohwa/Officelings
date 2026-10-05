using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Vibedotcom.Core;
using Vibedotcom.Core.Iso;
using Vibedotcom.Core.Iso.Editing;

namespace Vibedotcom.App;

internal sealed class IsoEditorOverlay
{
    internal static readonly Color Valid=Color.FromRgb(0x2E,0x7D,0x32), Invalid=Color.FromRgb(0xB3,0x26,0x1E), Warning=Color.FromRgb(0x8A,0x5A,0x00);
    internal DrawingVisual Visual { get; } = new();
    internal Rect Handle { get; private set; } = Rect.Empty;
    private Rect bounds = Rect.Empty;
    internal Rect Bounds => bounds;
    internal int BuildCount { get; private set; }
    internal string PreviewText { get; private set; } = "";
    internal bool HitHandle(Point point) => Handle.Contains(point);

    internal void Rebuild(IsoEditorSession session,IIsoSheetSource sheets,IsoPixel camera)
    {
        BuildCount++; Handle=Rect.Empty; bounds=Rect.Empty;
        using var dc=Visual.RenderOpen();
        var selected=session.SelectedFloor;
        if (selected!=null)
        {
            var variant=session.Pack.Variant(selected.AssetId,selected.Rotation,selected.State);
            var pen=new Pen(new SolidColorBrush(IsoEditorPanel.Accent),2);
            for (var y=0;y<variant.FootprintH;y++) for (var x=0;x<variant.FootprintW;x++)
            { Diamond(dc,selected.X+x,selected.Y+y,camera,null,new Pen(Brushes.White,4)); Diamond(dc,selected.X+x,selected.Y+y,camera,null,pen); }
            var center=Project(selected.X+(variant.FootprintW-1)/2d,selected.Y+(variant.FootprintH-1)/2d,0,camera);
            Handle=new(center.X-5,center.Y-5,10,10); dc.DrawRectangle(Brushes.White,pen,Handle); bounds.Union(Handle);
        }
        else if (session.SelectedWall is {} wall)
        {
            foreach (var edge in session.Editor.State.Layout.WallSegments!.Where(w=>w.Id==session.Selection || w.AssemblyId==session.Selection))
                WallOutline(dc,edge,camera,new Pen(new SolidColorBrush(IsoEditorPanel.Accent),2));
            var center=Project(wall.Edge.I,wall.Edge.J,24,camera);
            Handle=new(center.X-5,center.Y-5,10,10); dc.DrawRectangle(Brushes.White,new Pen(Brushes.DarkBlue,2),Handle); bounds.Union(Handle);
        }
        var preview=session.Preview;
        var draft=session.DraftInvalid ? session.Editor.State.Layout : null;
        if (preview==null && draft==null)
        {
            var cursorPen=new Pen(new SolidColorBrush(IsoEditorPanel.Accent),1.5) { DashStyle=DashStyles.Dash };
            Diamond(dc,session.Cursor.X,session.Cursor.Y,camera,null,new Pen(Brushes.White,3));
            Diamond(dc,session.Cursor.X,session.Cursor.Y,camera,null,cursorPen);
            var cursor=Project(session.Cursor.X,session.Cursor.Y,0,camera);
            Text(dc,$"Cursor ({session.Cursor.X},{session.Cursor.Y})",new(cursor.X+36,cursor.Y),cursorPen.Brush,9);
            PreviewText=""; return;
        }
        var candidate=preview?.Candidate ?? draft!;
        var issues=preview?.Issues ?? session.Issues;
        var error=issues.FirstOrDefault(i=>i.Severity==IsoIssueSeverity.Error);
        var brush=new SolidColorBrush(error!=null ? Invalid : Valid); var outline=new Pen(brush,1.5);
        // Draw authored clips from the exact candidate, including its assembly children.
        foreach (var f in candidate.Furniture.Where(f=>!session.LastValidLayout.Furniture.Contains(f) || preview!=null &&
            (!session.Editor.State.Layout.Furniture.Contains(f) || f.Id==session.Selection || f.DeskId==session.Selection)))
            Furniture(dc,session,sheets,f,camera,.5,candidate);
        foreach (var w in (candidate.WallSegments ?? []).Where(w=>w.HostId!=null && !(session.Editor.State.Layout.WallSegments ?? []).Contains(w)))
            WallArt(dc,session.Pack,sheets,w,camera,outline);
        if (preview!=null)
        {
            foreach (var tile in preview.Footprint) Diamond(dc,tile.X,tile.Y,camera,new SolidColorBrush(Color.FromArgb(40,brush.Color.R,brush.Color.G,brush.Color.B)),outline);
            foreach (var f in candidate.Furniture.Where(f=>!session.Editor.State.Layout.Furniture.Contains(f)))
            {
                var variant=session.Pack.Manifest.Furniture?.FirstOrDefault(a=>a.Id==f.AssetId)?.Variants.FirstOrDefault(v=>v.Rotation==f.Rotation && v.State==f.State);
                if (variant==null) continue;
                foreach (var tile in IsoLayoutLoader.Cells(f,variant)) Hatch(dc,tile.X,tile.Y,camera,outline);
            }
            var labelIndex=0;
            var labelAnchor=Project((preview.Footprint.FirstOrDefault()).X,(preview.Footprint.FirstOrDefault()).Y,0,camera);
            foreach (var marker in preview.Points)
            {
                var point=Project(marker.Ground.I,marker.Ground.J,marker.Elevation,camera);
                dc.DrawEllipse(Brushes.White,outline,point,2.5,2.5); bounds.Union(new Rect(point.X-4,point.Y-4,8,8));
                var label=new Point(labelAnchor.X+64,labelAnchor.Y-100+labelIndex*13);
                dc.DrawLine(new Pen(brush,.5),point,new(label.X-2,label.Y+4));
                Text(dc,marker.Kind,label,brush,7); labelIndex++;
            }
        }
        PreviewText=error==null ? "✓ Valid" : "✕ "+error.RuleId+" "+session.IssueText(error);
        if (error!=null && (preview?.CanCommit==true || draft!=null)) PreviewText+=" · Draft only; live office unchanged";
        var anchor=preview?.Footprint.FirstOrDefault() ?? session.Cursor;
        var origin=Project(anchor.X,anchor.Y,0,camera);
        Text(dc,PreviewText,new Point(origin.X+12,origin.Y+18),brush,9);
        if (issues.Any(i=>i.Severity==IsoIssueSeverity.Warning)) Text(dc,"⚠ Warnings; see issue list",new Point(origin.X+12,origin.Y+34),new SolidColorBrush(Warning),9);
    }
    private void Diamond(DrawingContext dc,double i,double j,IsoPixel camera,Brush? fill,Pen outline)
    {
        var center=Project(i,j,0,camera);
        var geometry=new StreamGeometry(); using (var context=geometry.Open())
        { context.BeginFigure(new(center.X,center.Y-16),true,true); context.LineTo(new(center.X+32,center.Y),true,false); context.LineTo(new(center.X,center.Y+16),true,false); context.LineTo(new(center.X-32,center.Y),true,false); }
        geometry.Freeze(); dc.DrawGeometry(fill,outline,geometry); bounds.Union(new Rect(center.X-33,center.Y-17,66,34));
    }
    private static void Hatch(DrawingContext dc,double i,double j,IsoPixel camera,Pen outline)
    {
        var center=Project(i,j,0,camera);
        for (var k=-12;k<=12;k+=6) dc.DrawLine(outline,new(center.X-12,center.Y+k/2d-6),new(center.X+12,center.Y+k/2d+6));
    }
    private void WallOutline(DrawingContext dc,IsoWallSegment wall,IsoPixel camera,Pen pen)
    {
        var a=Project(wall.Edge.I,wall.Edge.J,0,camera);
        var b=Project(wall.Edge.I+(wall.Face=="right" ? 1 : 0),wall.Edge.J+(wall.Face=="left" ? 1 : 0),0,camera);
        dc.DrawLine(pen,a,b); dc.DrawLine(pen,new(a.X,a.Y-48),new(b.X,b.Y-48));
        dc.DrawLine(pen,a,new(a.X,a.Y-48)); dc.DrawLine(pen,b,new(b.X,b.Y-48));
        bounds.Union(new Rect(Math.Min(a.X,b.X)-2,Math.Min(a.Y,b.Y)-50,Math.Abs(a.X-b.X)+4,Math.Abs(a.Y-b.Y)+52));
    }
    private void Furniture(DrawingContext dc,IsoEditorSession session,IIsoSheetSource sheets,IsoPlacement f,IsoPixel camera,double opacity,IsoLayout candidate)
    {
        var pack=session.Pack;
        var v=session.Editor.Palette.FirstOrDefault(a=>a.AssetId==f.AssetId)?.Variants.FirstOrDefault(v=>v.Rotation==f.Rotation && v.State==f.State);
        if (v==null) return; // An unknown authored variant has diagnostics but no substituted art.
        dc.PushOpacity(opacity);
        if (v.Shadow!=null) Part(dc,pack,sheets,v.Shadow,f.X,f.Y,camera);
        var suppressed=Array.Empty<string>();
        string Identity(IsoPart p) => p.PartId ?? pack.Clip(p.SheetId,p.Animation ?? p.Clip!,p.Direction).PartId ?? "";
        if (f.AssetId=="iso/desk" && candidate.Furniture.FirstOrDefault(c=>c.AssetId=="iso/computer" && c.DeskId==f.Id) is {} mounted)
        {
            var computer=pack.Variant(mounted.AssetId,mounted.Rotation,mounted.State);
            suppressed=computer.Parts.Concat(new[] {computer.Screen,computer.Glow}.OfType<IsoPart>()).Select(Identity).ToArray();
        }
        foreach (var p in v.Parts.Concat(v.Screen==null ? [] : new[] {v.Screen}).Concat(v.Glow==null ? [] : new[] {v.Glow}))
            if (!suppressed.Contains(Identity(p),StringComparer.Ordinal)) Part(dc,pack,sheets,p,f.X,f.Y,camera);
        dc.Pop();
    }
    private void WallArt(DrawingContext dc,IsoPack pack,IIsoSheetSource sheets,IsoWallSegment wall,IsoPixel camera,Pen outline)
    {
        var module=pack.Manifest.WallModules?.FirstOrDefault(m=>m.Id==wall.AssetId);
        if (module==null) return;
        dc.PushOpacity(.5); foreach (var part in module.Parts) Part(dc,pack,sheets,part,wall.Edge.I,wall.Edge.J,camera,module.EdgeAnchor); dc.Pop();
        WallOutline(dc,wall,camera,outline);
    }
    private void Part(DrawingContext dc,IsoPack pack,IIsoSheetSource sheets,IsoPart part,double i,double j,IsoPixel camera,PixelPoint? edgeAnchor=null)
    {
        var clip=pack.Clip(part.SheetId,part.Animation ?? part.Clip!,part.Direction);
        var cell=sheets.GetCell(part.SheetId,clip.Key,part.Frame ?? 0);
        var anchor=part.Anchor ?? clip.Anchor ?? clip.EdgeAnchor ?? edgeAnchor ?? pack.Sheet(part.SheetId).Anchor;
        var point=Project(i,j,0,camera);
        var rect=new Rect(point.X-anchor.X+(clip.FootOffset?.X ?? 0),point.Y-anchor.Y+(clip.FootOffset?.Y ?? 0),cell.Width,cell.Height);
        dc.PushOpacity(part.Opacity); dc.DrawImage(cell.Image,rect); dc.Pop(); bounds.Union(rect);
    }
    private void Text(DrawingContext dc,string value,Point origin,Brush brush,double size)
    {
        var text=new FormattedText(value,CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface(SystemFonts.MessageFontFamily,FontStyles.Normal,FontWeights.Normal,FontStretches.Normal),size,brush,1) {MaxTextWidth=220};
        var rect=new Rect(origin.X-3,origin.Y-2,text.Width+6,text.Height+4);
        dc.DrawRectangle(Brushes.White,null,rect); dc.DrawText(text,origin); bounds.Union(rect);
    }
    internal static Point Project(double i,double j,double elevation,IsoPixel camera)
    { var p=IsoGeometry.Project(i,j,elevation); return new(p.X+camera.X,p.Y+camera.Y); }
}
