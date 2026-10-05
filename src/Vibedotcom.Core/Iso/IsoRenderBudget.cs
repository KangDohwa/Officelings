namespace Vibedotcom.Core.Iso;

// Source-resolution render caches have a separate budget from decoded pack sheets.
internal static class IsoRenderBudget
{
    internal const int MaxDimension = 8192;
    internal const long MaxPixels = 16_777_216;
    internal static void Check(long left, long top, long right, long bottom)
    {
        var width = right - left; var height = bottom - top;
        if (width <= 0 || height <= 0 || width > MaxDimension || height > MaxDimension || width * height > MaxPixels ||
            left < int.MinValue || top < int.MinValue || right > int.MaxValue || bottom > int.MaxValue)
            throw new InvalidDataException("Iso render bounds exceed the bitmap budget.");
    }
    internal static bool AnchorFits(PixelPoint anchor, int width, int height)
    {
        if (!double.IsFinite(anchor.X) || !double.IsFinite(anchor.Y) || anchor.X < int.MinValue || anchor.X > int.MaxValue ||
            anchor.Y < int.MinValue || anchor.Y > int.MaxValue) return false;
        try
        {
            Check(Math.Min(-352L, -(long)anchor.X - 320), Math.Min(-96L, -(long)anchor.Y - 16),
                Math.Max(352L, -(long)anchor.X + 320 + width), Math.Max(336L, -(long)anchor.Y + 304 + height));
            return true;
        }
        catch (InvalidDataException) { return false; }
    }
}