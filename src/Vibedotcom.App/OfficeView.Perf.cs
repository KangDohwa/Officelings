using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using Vibedotcom.Core;

namespace Vibedotcom.App;

internal sealed partial class OfficeView
{
    private readonly Dictionary<string, string> overflowSeatHosts = new(StringComparer.Ordinal);
    // Capturing predicates belong only to the geometry cache miss. The live
    // occupancy check in FurnitureDepth must not allocate them on every tick.
    private (double Depth, bool Lounge) CacheFurnitureDepth(Furniture f)
    {
        var v = assets.Variant(f);
        var host = office.Furniture.FirstOrDefault(h => h.Id != f.Id && assets.Variant(h).ChairSeatAnchor != null &&
            (f.DeskId != null && f.DeskId == (h.DeskId ?? h.Id) || f.Id.StartsWith(h.Id + "-chair", StringComparison.Ordinal)));
        if (host != null)
        {
            var chair = assets.Variant(host);
            return (host.Y * 16 + chair.ChairSeatAnchor!.Y + 12 + (f.AssetId == chair.ChairBackId ? -.2 : .2), false);
        }
        if (v.ChairSeatAnchor != null && (v.Points.Any(p => p.Facing is "left" or "right") || f.Kind == "table"))
            return (f.Y * 16 + v.ChairSeatAnchor.Y + 12 + .1, false);
        return ((f.Y + f.Height) * 16 + v.ZOrder, true);
    }
    // Ambient frames do not change agent links, desk indicators, tags or room geometry.
    // Keep depth ordering only when a pet actually moves across another drawable.
    private void RenderAmbientChanges()
    {
        var petCount = 0;
        foreach (var item in drawOrder) if (item.Pet != null) petCount++;
        if (petCount != petViews.Count) { RenderScene(); return; }
        foreach (var pet in petViews)
            if (!retained.ContainsKey("p:" + pet.Id)) { RenderScene(); return; }
        var reordered = false;
        foreach (var item in drawOrder)
        {
            if (item.Furniture is {} furniture)
            {
                var frame = furnitureFramesById.GetValueOrDefault(furniture.Id);
                var stamp = furniture.Kind == "clock" ? ambientState?.LocalTime.ToUnixTimeSeconds() ?? 0 :
                    furniture.Kind == "window" && ambientState?.WinterDecor == true ? 1 : 0;
                if (item.Frame == frame && item.AnimationStamp == stamp) continue;
                item.Frame = frame; item.AnimationStamp = stamp;
                Damage(item.Visual, dc => DrawFurniture(dc, furniture));
            }
            else if (item.Pet is {} old)
            {
                foreach (var pet in petViews)
                {
                    if (pet.Id != old.Id) continue;
                    if (pet == old) break;
                    item.Pet = pet;
                    var depth = (pet.Y + 1) * 16;
                    reordered |= depth != item.Depth; item.Depth = depth;
                    Damage(item.Visual, dc => DrawPet(dc, pet));
                    break;
                }
            }
            else if (item.Agent is {} agent && agent.Pose.Action is "darts" or "pingpong" && item.AnimationStamp != activityStamp)
            {
                item.AnimationStamp = activityStamp;
                Damage(item.Visual, dc => DrawAgent(dc, agent));
            }
        }
        if (reordered)
        {
            drawOrder.Sort(static (a, b) => { var c = a.Layer.CompareTo(b.Layer); if (c == 0) c = a.Depth.CompareTo(b.Depth); return c != 0 ? c : StringComparer.Ordinal.Compare(a.Id, b.Id); });
            for (var i = 0; i < drawOrder.Count; i++)
            {
                var visual = drawOrder[i].Visual;
                if (ReferenceEquals(sceneVisual.Children[i], visual)) continue;
                sceneVisual.Children.Remove(visual); sceneVisual.Children.Insert(i, visual);
            }
        }
        // Flying props are separate from the agent body. They still use the authored 8 Hz stamp.
        foreach (var item in overlayOrder)
            if (item.Key[0] == 'g' && item.AnimationStamp != activityStamp && item.Agent is {} agent)
            { item.AnimationStamp = activityStamp; Damage(item.Visual, dc => DrawActivity(dc, agent, true)); }
        sceneDirty = false;
    }
}
