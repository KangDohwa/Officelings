using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Vibedotcom.App;

internal sealed class WindowModes(Window window)
{
    public string Mode { get; private set; } = "normal";
    private const int ExStyle = -20, Transparent = 0x20, Layered = 0x80000;
    private const uint NoMove = 2, NoSize = 1, NoActivate = 0x10, FrameChanged = 0x20;
    private static readonly nint Bottom = new(1), Topmost = new(-1), NotTopmost = new(-2);

    public void Attach() => HwndSource.FromHwnd(new WindowInteropHelper(window).Handle)?.AddHook(Hook);

    public void Apply(string mode, bool clickThrough)
    {
        Mode = mode is "topmost" or "desktop" ? mode : "normal";
        window.Topmost = Mode == "topmost";
        window.ShowInTaskbar = Mode != "desktop";
        window.WindowStyle = Mode == "desktop" ? WindowStyle.None : WindowStyle.SingleBorderWindow;
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == 0) return;
        var style = GetWindowLong(handle, ExStyle);
        style = clickThrough ? style | Transparent | Layered : style & ~(Transparent | Layered);
        SetWindowLong(handle, ExStyle, style);
        if (clickThrough) SetLayeredWindowAttributes(handle, 0, 255, 2);
        SetWindowPos(handle, Mode == "desktop" ? Bottom : Mode == "topmost" ? Topmost : NotTopmost,
            0, 0, 0, 0, NoMove | NoSize | NoActivate | FrameChanged);
    }

    private nint Hook(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (Mode == "desktop" && message == 0x0046) // WM_WINDOWPOSCHANGING: remain beneath ordinary windows
        {
            var position = Marshal.PtrToStructure<WindowPosition>(lParam);
            position.InsertAfter = Bottom; position.Flags = (position.Flags & ~4u) | NoActivate;
            Marshal.StructureToPtr(position, lParam, false);
        }
        return 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowPosition { public nint Handle, InsertAfter; public int X, Y, Width, Height; public uint Flags; }
    [DllImport("user32.dll")] private static extern int GetWindowLong(nint hwnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(nint hwnd, int index, int value);
    [DllImport("user32.dll")] private static extern bool SetLayeredWindowAttributes(nint hwnd, uint color, byte alpha, uint flags);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int w, int h, uint flags);
}
