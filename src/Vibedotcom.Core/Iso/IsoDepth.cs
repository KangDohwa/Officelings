namespace Vibedotcom.Core.Iso;

public enum IsoPass { Floor, FloorProps, Shadows, RearWalls, Scene, Overlay }

/// IDs break ties ordinally; ZOrder is a tie rank, never an elevation or pixel offset.
public readonly record struct IsoDepthKey(IsoPass Pass, double GroundDepth, int ZOrder, string InstanceId, string PartId)
    : IComparable<IsoDepthKey>
{
    public int CompareTo(IsoDepthKey other)
    {
        var c = Pass.CompareTo(other.Pass); if (c != 0) return c;
        c = ((long)Math.Round(GroundDepth * 1_000_000)).CompareTo((long)Math.Round(other.GroundDepth * 1_000_000)); if (c != 0) return c;
        c = ZOrder.CompareTo(other.ZOrder); if (c != 0) return c;
        c = StringComparer.Ordinal.Compare(InstanceId, other.InstanceId);
        return c != 0 ? c : StringComparer.Ordinal.Compare(PartId, other.PartId);
    }
    // Six decimal ground units preserve every published source anchor. Final ID ties use list order.
    public long Encoded => ((long)Pass << 60) + (checked((long)Math.Round((GroundDepth + 512) * 1_000_000)) << 10) + ZOrder + 512;
    public static double Coarse(int x, int y, int width, int height) => x + y + width + height - 1;
}

