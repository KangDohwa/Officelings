namespace Vibedotcom.Core.Iso;

/// A move/rotation returns one new layout; retaining the previous layout gives an atomic undo.
/// Geometry and chair contacts select authored variants, with no source pixel transform.
public static class IsoPlacementEditor
{
    public static IsoLayout MoveRotate(IsoLayout source, IsoPack pack, string instanceId, int x, int y, int rotation)
    {
        var current = source.Furniture.Single(f => f.Id == instanceId);
        var variant = pack.Variant(current.AssetId, rotation, current.State);
        var moved = current with { X = x, Y = y, Rotation = rotation,
            Orientation = current.Orientation != null ? variant.Orientation : null,
            Width = current.Width != null ? variant.FootprintW : null, Height = current.Height != null ? variant.FootprintH : null };
        var contact = current.AssetId == "iso/desk" ? IsoContacts.Desk(x, y, rotation, current.SeatProfile) : null;
        var furniture = source.Furniture.Select(f =>
        {
            if (f.Id == instanceId) return moved;
            if (contact == null || f.DeskId != instanceId || f.AssetId != "iso/chair") return f;
            return f with { X = contact.Approach.X, Y = contact.Approach.Y, Rotation = IsoGeometry.Rotation(contact.Facing),
                Orientation = f.Orientation != null ? contact.Facing : null };
        }).ToArray();
        return IsoLayoutLoader.Validate(source with { Furniture = Array.AsReadOnly(furniture) }, pack);
    }
}
