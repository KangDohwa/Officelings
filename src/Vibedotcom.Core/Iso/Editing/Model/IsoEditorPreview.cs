using System.Collections.Immutable;

namespace Vibedotcom.Core.Iso.Editing;

internal static class IsoEditorPreview
{
    internal static (ImmutableArray<Tile> Cells, ImmutableArray<IsoPreviewPoint> Points) Markers(IsoLayout l, IsoPack pack, string? target)
    {
        if (target == null) return ([], []);
        var cells = new HashSet<Tile>(); var points = new List<IsoPreviewPoint>();
        foreach (var f in (l.Furniture ?? []).Where(f => f.Id == target || f.DeskId == target))
        {
            var v = IsoEditorDocument.Variant(pack, f); if (v == null) continue;
            for (var y = 0; y < v.FootprintH; y++) for (var x = 0; x < v.FootprintW; x++) cells.Add(new(f.X + x, f.Y + y));
            foreach (var cell in IsoLayoutLoader.Cells(f, v)) points.Add(new(f.Id, "blocked", new(cell.X, cell.Y)));
            foreach (var p in v.Points ?? []) points.Add(new(f.Id, "interaction", new(f.X + p.X, f.Y + p.Y)));
            if (f.AssetId != "iso/desk" || !IsoEditorValidation.CanonicalRotation(f)) continue;
            var c = IsoContacts.Desk(f.X, f.Y, f.Rotation);
            points.Add(new(f.Id, "approach", new(c.Approach.X, c.Approach.Y)));
            points.Add(new(f.Id, "seat-ground", c.SeatGround));
            void Contact(string kind, IsoPixel pixel)
            {
                var elevation = kind == "pelvis" ? 12 : 18;
                var p = IsoGeometry.Inverse(pixel.X, pixel.Y, elevation);
                points.Add(new(f.Id, kind, p, elevation));
            }
            Contact("pelvis", c.Pelvis); Contact("hand-left", c.HandLeft); Contact("hand-right", c.HandRight);
        }
        foreach (var w in l.WallSegments ?? []) if (w.Id == target || w.AssemblyId == target) points.Add(new(w.AssemblyId ?? w.Id, "wall-host", w.Edge));
        return (cells.OrderBy(t => t.X).ThenBy(t => t.Y).ToImmutableArray(), points.Distinct().ToImmutableArray());
    }
}
