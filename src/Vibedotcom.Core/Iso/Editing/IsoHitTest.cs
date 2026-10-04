namespace Vibedotcom.Core.Iso.Editing;

/// <summary>Editor picking against the authoritative published queue; spec §6.</summary>
public static class IsoHitTest
{
    /// <summary>Spec §6: scene-native pixels minus builder camera; half-open ground diamond ownership.</summary>
    public static Tile? PickTile(IsoLayout layout, IsoPixel camera, double sceneX, double sceneY) =>
        throw new NotImplementedException();
    /// <summary>Spec §6: reverse queue, published clip masks, effective alpha ≥32; linked hits return root.</summary>
    /// <remarks>Returns furniture or wall-decor targets. Actors/pets/labels do not intercept editor selection.
    /// Opaque fixed walls stop the search; shadows/floors and authored passthrough parts are skipped.</remarks>
    public static IsoHit? HitTest(IsoPack pack, IsoLayout layout, IsoScene scene, int sceneX, int sceneY) =>
        throw new NotImplementedException();
}
