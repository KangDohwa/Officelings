using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Officelings.App;

internal static class IsoOffscreenRenderer
{
    // ISO callers specify physical pixel dimensions. A 96-DPI render target
    // attached to a 150%-DPI HWND would otherwise shrink an integer device zoom
    // to fractional bitmap pixels. Keep layout, camera and raster DPI together.
    internal static BitmapSource RenderBitmap(IsoView view, int width, int height)
    {
        using var source = new HwndSource(new HwndSourceParameters("Officelings ISO offscreen render")
        {
            PositionX = -30000, PositionY = -30000, Width = width, Height = height,
            WindowStyle = unchecked((int)0x80000000), ExtendedWindowStyle = 0x08000080
        });
        var root = new ContainerVisual(); root.Children.Add(view); source.RootVisual = root;
        try
        {
            var dpi = VisualTreeHelper.GetDpi(view);
            var size = LogicalSize(width, height, dpi);
            view.Width = size.Width; view.Height = size.Height;
            view.Measure(size); view.Arrange(new Rect(new Point(), size)); view.UpdateLayout();
            view.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            var bitmap = new RenderTargetBitmap(width, height, 96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
            bitmap.Render(root); bitmap.Freeze(); return bitmap;
        }
        finally { source.RootVisual = null; root.Children.Remove(view); }
    }
    internal static Size LogicalSize(int width, int height, DpiScale dpi)
        => new(width / dpi.DpiScaleX, height / dpi.DpiScaleY);
}
