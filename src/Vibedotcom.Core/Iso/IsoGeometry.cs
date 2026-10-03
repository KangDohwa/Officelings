namespace Vibedotcom.Core.Iso;

public readonly record struct IsoGridPoint(double I, double J);
public readonly record struct IsoPixel(int X, int Y);
public readonly record struct IsoBounds(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
    public IsoBounds Include(IsoBounds other) => new(Math.Min(Left, other.Left), Math.Min(Top, other.Top),
        Math.Max(Right, other.Right), Math.Max(Bottom, other.Bottom));
}

public static class IsoGeometry
{
    public static Position Project(double i, double j, double z = 0) => new(32 * (i - j), 16 * (i + j) - z);
    public static IsoGridPoint Inverse(double screenX, double screenY, double z = 0) =>
        new(screenX / 64 + (screenY + z) / 32, (screenY + z) / 32 - screenX / 64);
    public static int Snap(double value) => checked((int)Math.Floor(value + .5));
    public static IsoPixel Ground(double i, double j)
    {
        var p = Project(i, j); return new(Snap(p.X), Snap(p.Y));
    }
    public static Tile Pick(double x, double y)
    {
        var p = Inverse(x, y); return new(Snap(p.I), Snap(p.J));
    }
    public static bool Owns(double i, double j, Tile tile) => i >= tile.X - .5 && i < tile.X + .5 &&
        j >= tile.Y - .5 && j < tile.Y + .5;
    public static bool TileTexel(int x, int y) => x is >= 0 and < 64 && y is >= 0 and < 32 &&
        Pick(x + .5 - 32, y + .5 - 16) == new Tile(0, 0);
    public static IsoBounds GroundBounds(int width, int height) => new(-32 * height, -16, 32 * width, 16 * (width + height - 1));
    public static string Facing(string legacy) => legacy switch
    {
        "right" => "se", "down" => "sw", "up" => "ne", "left" => "nw",
        _ => throw new ArgumentException("Unknown grid facing: " + legacy)
    };
    public static string Facing(int di, int dj) => (di, dj) switch
    {
        (1, 0) => "se", (0, 1) => "sw", (0, -1) => "ne", (-1, 0) => "nw",
        _ => throw new ArgumentException("Four-neighbor movement required.")
    };
    public static IsoGridPoint Step(string facing) => facing switch
    {
        "se" => new(1, 0), "sw" => new(0, 1), "ne" => new(0, -1), "nw" => new(-1, 0),
        _ => throw new ArgumentException("Unknown iso facing: " + facing)
    };
    public static string Orientation(int rotation) => rotation switch
    { 0 => "se", 90 => "sw", 180 => "nw", 270 => "ne", _ => throw new ArgumentException("Invalid rotation.") };
    public static int Rotation(string facing) => facing switch
    { "se" => 0, "sw" => 90, "nw" => 180, "ne" => 270, _ => throw new ArgumentException("Invalid orientation.") };
}

public sealed record IsoSeatContact(Tile Approach, IsoGridPoint SeatGround, string Facing, IsoPixel Pelvis,
    IsoPixel HandLeft, IsoPixel HandRight);

public static class IsoContacts
{
    public static IsoSeatContact Desk(int x, int y, int rotation, string profile = "front")
    {
        if (profile != "front") throw new InvalidDataException("Only desk seatProfile front is supported.");
        var (a, facing) = rotation switch
        {
            0 => (new Tile(1, 0), "nw"), 90 => (new Tile(1, 1), "ne"),
            180 => (new Tile(-1, 1), "se"), 270 => (new Tile(0, -1), "sw"),
            _ => throw new ArgumentException("Invalid desk rotation.")
        };
        var approach = new Tile(x + a.X, y + a.Y); var ground = IsoGeometry.Ground(approach.X, approach.Y);
        var f = IsoGeometry.Step(facing);
        // Snap the common ground once; all contact offsets below are integral.
        var shift = IsoGeometry.Project(.375 * f.I, .375 * f.J, 18);
        var hands = new IsoPixel(ground.X + (int)shift.X, ground.Y + (int)shift.Y);
        return new(approach, new(approach.X, approach.Y), facing, new(ground.X, ground.Y - 12),
            new(hands.X - 1, hands.Y), new(hands.X + 1, hands.Y));
    }
    public static IsoPixel StandingGrip(string facing) => facing switch
    { "se" => new(24, 44), "sw" => new(8, 44), "ne" => new(24, 40), "nw" => new(8, 40), _ => throw new ArgumentException() };
    public static (IsoPixel Anchor, IsoPixel Offset, int FootY) Lowering(int phase) => phase switch
    {
        0 => (new(16, 40), new(0, -20), 60), 1 => (new(16, 42), new(0, -18), 60),
        2 => (new(16, 43), new(0, -15), 58), 3 => (new(16, 44), new(0, -12), 56),
        _ => throw new ArgumentOutOfRangeException(nameof(phase))
    };
}
