using System.Windows.Media.Imaging;

namespace Vibedotcom.App;

// Only this boundary depends on how the iso sheets are stored. Scene positions
// already include anchors; the renderer never solves furniture/actor contacts.
internal interface IIsoSheetSource
{
    IsoSheetCell GetCell(string sheetId, string clip, int frame);
}

internal sealed record IsoSheetCell(BitmapSource Image, int Width, int Height, int AnchorX, int AnchorY, int Frames);
