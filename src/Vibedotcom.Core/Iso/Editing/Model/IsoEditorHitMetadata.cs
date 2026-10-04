namespace Vibedotcom.Core.Iso.Editing;

internal static class IsoEditorHitMetadata
{
    internal sealed record Part(string? Target, bool Passthrough);
    internal static Dictionary<(string Instance, string Sheet, string Clip), Part> Build(IsoPack pack, IsoLayout layout)
    {
        var result = new Dictionary<(string, string, string), Part>();
        foreach (var f in layout.Furniture ?? [])
        {
            var v = IsoEditorDocument.Variant(pack, f); if (v == null) continue;
            void Add(IsoPart p, string? id = null)
            {
                var c = pack.Clip(p.SheetId, p.Animation ?? p.Clip!, p.Direction);
                result[(f.Id + "/" + (id ?? p.PartId ?? c.PartId ?? c.Key), p.SheetId, c.Key)] = new(f.DeskId ?? f.Id, p.HoverPassthrough);
            }
            foreach (var p in v.Parts) Add(p);
            foreach (var state in pack.Manifest.Furniture!.First(a => a.Id == f.AssetId).Variants.Where(s => s.Rotation == f.Rotation))
            {
                if (state.Screen is {} screen) Add(screen, "screen");
                if (state.Glow is {} glow) Add(glow, "glow");
            }
        }
        foreach (var w in layout.WallSegments ?? [])
        {
            var m = pack.Manifest.WallModules?.FirstOrDefault(m => m.Id == w.AssetId); if (m == null) continue;
            for (var k = 0; k < m.Parts.Count; k++)
            {
                var p = m.Parts[k]; var c = pack.Clip(p.SheetId, p.Animation ?? p.Clip!, p.Direction);
                result[(w.Id + "/" + k.ToString("D2"), p.SheetId, c.Key)] = new(w.HostId == null ? null : w.AssemblyId, p.HoverPassthrough);
            }
        }
        return result;
    }
}
