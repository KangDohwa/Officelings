namespace Officelings.Core.Iso;

public enum IsoPass { Floor, FloorProps, Shadows, RearWalls, Scene, Overlay }

/// IDs break ties ordinally; ZOrder is a tie rank, never an elevation or pixel offset.
public readonly record struct IsoDepthKey(IsoPass Pass, double GroundDepth, int ZOrder, string InstanceId, string PartId)
    : IComparable<IsoDepthKey>
{
    private long GroundUnits => checked((long)Math.Round(GroundDepth * 1_000_000_000_000));
    public int CompareTo(IsoDepthKey other)
    {
        var c = Pass.CompareTo(other.Pass); if (c != 0) return c;
        c = GroundUnits.CompareTo(other.GroundUnits); if (c != 0) return c;
        c = ZOrder.CompareTo(other.ZOrder); if (c != 0) return c;
        c = StringComparer.Ordinal.Compare(InstanceId, other.InstanceId);
        return c != 0 ? c : StringComparer.Ordinal.Compare(PartId, other.PartId);
    }
    // Twelve decimal grid units absorb binary addition noise while retaining subpixel ground ordering. Final ID ties use list order.
    public long Encoded => checked(((long)Pass << 60) + (GroundUnits + 512_000_000_000_000) * 1024 + ZOrder + 512);
    public static double Coarse(int x, int y, int width, int height) => x + y + width + height - 1;
}
