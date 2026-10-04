using System;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Vibedotcom.App;

// The source art has furniture/cat shadows but no standalone human contact
// sheet. This explicit preview-only cell follows iso-spec's 16x6 contact and
// (+8,+4) cast tail. The real pack adapter supplies engine shadow sheet IDs.
internal static class IsoPreviewContactShadow
{
    internal const string SheetId = "iso-preview-contact-shadow";
    internal static readonly IsoSourceClip Clip = new(SheetId, "contact", "any", 0, 0, 1, true, 32, 16, 8, 4, default);
    private static readonly Lazy<IsoSheetCell> cell = new(Create);
    internal static IsoSheetCell Cell => cell.Value;
    private static IsoSheetCell Create()
    {
        var pixels = new byte[32 * 16 * 4];
        for (var y = 0; y < 16; y++) for (var x = 0; x < 32; x++)
        {
            static bool Inside(int x, int y, double cx, double cy)
            { var dx = (x + .5 - cx) / 8; var dy = (y + .5 - cy) / 3; return dx * dx + dy * dy <= 1; }
            var alpha = Inside(x, y, 8, 4) ? (byte)96 : Inside(x, y, 16, 8) ? (byte)48 : (byte)0;
            if (alpha == 0) continue;
            var index = (y * 32 + x) * 4;
            pixels[index] = 0x24; pixels[index + 1] = 0x19; pixels[index + 2] = 0x11; pixels[index + 3] = alpha;
        }
        var bitmap = BitmapSource.Create(32, 16, 96, 96, PixelFormats.Bgra32, null, pixels, 32 * 4); bitmap.Freeze();
        return new(bitmap, 32, 16, 8, 4, 1);
    }
}
