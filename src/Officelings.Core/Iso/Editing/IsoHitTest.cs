namespace Officelings.Core.Iso.Editing;

/// <summary>Editor picking against the authoritative supplied queue; spec §6.</summary>
public static class IsoHitTest
{
    /// <summary>Spec §6: scene-native pixels minus builder camera; half-open ground diamond ownership.</summary>
    public static Tile? PickTile(IsoLayout layout, IsoPixel camera, double sceneX, double sceneY)
    {
        if (!double.IsFinite(sceneX) || !double.IsFinite(sceneY)) return null;
        var p = IsoGeometry.Inverse(sceneX - camera.X, sceneY - camera.Y);
        if (p.I < -.5 || p.J < -.5 || p.I >= layout.WidthTiles - .5 || p.J >= layout.HeightTiles - .5) return null;
        var tile = new Tile(IsoGeometry.Snap(p.I), IsoGeometry.Snap(p.J));
        return IsoGeometry.Owns(p.I, p.J, tile) && layout.Tiles?.ElementAtOrDefault(tile.Y * layout.WidthTiles + tile.X) is not (0 or 255) ? tile : null;
    }
    /// <summary>Spec §6: reverse queue, exact clip masks, effective alpha ≥32; linked hits return root.</summary>
    /// <remarks>Returns furniture or wall-decor targets. Actors/pets/labels do not intercept editor selection.
    /// Opaque fixed walls stop the search; shadows/floors and authored passthrough parts are skipped.</remarks>
    public static IsoHit? HitTest(IsoPack pack, IsoLayout layout, IsoScene scene, int sceneX, int sceneY)
    {
        var metadata = IsoEditorHitMetadata.Build(pack, layout);
        for (var k = scene.Items.Count - 1; k >= 0; k--)
        {
            var item = scene.Items[k];
            if (item.Layer is not (IsoLayer.Furniture or IsoLayer.Wall) ||
                metadata.TryGetValue((item.InstanceId, item.SheetId, item.Clip), out var part) && part.Passthrough ||
                !pack.Opaque(item, sceneX - item.X, sceneY - item.Y)) continue;
            var target = metadata.TryGetValue((item.InstanceId, item.SheetId, item.Clip), out part) ? part.Target : item.HitTargetId;
            var f = (layout.Furniture ?? []).FirstOrDefault(f => f.Id == target);
            if (f != null) return new(f.DeskId ?? f.Id, (f.DeskId != null || f.AssetId == "iso/desk") ? "desk" : "furniture");
            var wall = (layout.WallSegments ?? []).FirstOrDefault(w => w.Id == target || w.AssemblyId == target);
            return wall?.HostId != null && wall.AssemblyId != null ? new(wall.AssemblyId, "wall") : null;
        }
        return null;
    }
}
