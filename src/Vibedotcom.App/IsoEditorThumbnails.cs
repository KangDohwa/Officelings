using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Vibedotcom.Core;
using Vibedotcom.Core.Iso;
using Vibedotcom.Core.Iso.Editing;

namespace Vibedotcom.App;

internal sealed class IsoEditorThumbnails(IsoPack pack,IIsoSheetSource sheets)
{
    private readonly Dictionary<string,ImageSource> cache=new(System.StringComparer.Ordinal);
    internal ImageSource Get(IsoWallDecorDefinition decor)
    {
        if (cache.TryGetValue(decor.AssetId,out var image)) return image;
        var group=new DrawingGroup();
        using (var dc=group.Open())
        {
            for (var index=0;index<decor.ModuleIds.Length;index++)
            {
                var module=pack.Manifest.WallModules!.Single(m=>m.Id==decor.ModuleIds[index]);
                var origin=IsoGeometry.Project(decor.Face=="right" ? index : 0,decor.Face=="left" ? index : 0,0);
                foreach (var part in module.Parts) Draw(dc,part,new Point(origin.X,origin.Y),module.EdgeAnchor);
            }
        }
        group.Freeze(); image=new DrawingImage(group); image.Freeze(); cache[decor.AssetId]=image; return image;
    }
    internal ImageSource Get(IsoEditableAsset asset)
    {
        if (cache.TryGetValue(asset.AssetId,out var image)) return image;
        var variant=asset.Variants.OrderBy(v=>v.State=="default" ? 0 : 1).ThenBy(v=>v.State,System.StringComparer.Ordinal).ThenBy(v=>v.Rotation).First();
        var group=new DrawingGroup();
        using (var dc=group.Open())
        {
            foreach (var part in variant.Parts.Concat(variant.Screen==null ? [] : new[] {variant.Screen}).Concat(variant.Glow==null ? [] : new[] {variant.Glow}))
                Draw(dc,part,new Point());
        }
        group.Freeze(); image=new DrawingImage(group); image.Freeze(); cache[asset.AssetId]=image; return image;
    }
    private void Draw(DrawingContext dc,IsoPart part,Point origin,PixelPoint? edgeAnchor=null)
    {
        var clip=pack.Clip(part.SheetId,part.Animation ?? part.Clip!,part.Direction);
        var cell=sheets.GetCell(part.SheetId,clip.Key,part.Frame ?? 0);
        var anchor=part.Anchor ?? clip.Anchor ?? clip.EdgeAnchor ?? edgeAnchor ?? pack.Sheet(part.SheetId).Anchor;
        dc.PushOpacity(part.Opacity);
        dc.DrawImage(cell.Image,new Rect(origin.X-anchor.X+(clip.FootOffset?.X ?? 0),origin.Y-anchor.Y+(clip.FootOffset?.Y ?? 0),cell.Width,cell.Height)); dc.Pop();
    }
}
