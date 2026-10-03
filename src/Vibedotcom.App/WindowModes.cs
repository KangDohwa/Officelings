using System;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Vibedotcom.Core;

namespace Vibedotcom.App;

internal sealed class WindowModes(Window window, bool qa = false)
{
    public string Mode { get; private set; } = "normal";
    public bool ClickThrough { get; private set; }
    public bool Embedded => desktopHost != 0 && IsWindow(desktopHost);
    public Rect ScreenBounds
    {
        get
        {
            if (hwnd == 0 || !GetWindowRect(hwnd, out var r)) return new(window.Left, window.Top, window.Width, window.Height);
            var dpi = VisualTreeHelper.GetDpi(window);
            return new(r.Left / dpi.DpiScaleX, r.Top / dpi.DpiScaleY, (r.Right-r.Left) / dpi.DpiScaleX, (r.Bottom-r.Top) / dpi.DpiScaleY);
        }
    }
    private nint desktopHost, desktopAfter, hwnd, requestedShell;
    private const int Style = -16, ExStyle = -20, Transparent = 0x20, Layered = 0x80000, NoActivateStyle = 0x08000000;
    private const uint NoMove = 2, NoSize = 1, NoActivate = 0x10, FrameChanged = 0x20;
    private static readonly nint Bottom = new(1), Topmost = new(-1), NotTopmost = new(-2);
    private bool applying;

    public void Attach()
    {
        hwnd = new WindowInteropHelper(window).Handle;
        HwndSource.FromHwnd(hwnd)?.AddHook(Hook);
    }

    public void Apply(string mode, bool clickThrough, bool borderless = false, bool transparentBackground = false, double opacity = 1)
    {
        if (hwnd == 0) return;
        applying = true;
        try
        {
            GetWindowRect(hwnd,out var rect);
            if (desktopHost != 0)
            {
                SetParent(hwnd,0);
                SetWindowLong(hwnd,Style,(GetWindowLong(hwnd,Style) & ~0x40000000) | unchecked((int)0x80000000));
                SetWindowPos(hwnd,NotTopmost,rect.Left,rect.Top,rect.Right-rect.Left,rect.Bottom-rect.Top,NoActivate|FrameChanged);
                desktopHost = desktopAfter = 0;
            }
            var plan = WindowModePlan.Create(mode,clickThrough,borderless,transparentBackground,opacity,qa);
            Mode = plan.Mode; ClickThrough = plan.ClickThrough;
            window.Topmost = plan.Topmost;
            window.ShowInTaskbar = plan.ShowInTaskbar;
            window.ShowActivated = !plan.NoActivate;
            window.WindowStyle = window.AllowsTransparency || plan.Borderless ? WindowStyle.None : WindowStyle.SingleBorderWindow;
            hwnd = new WindowInteropHelper(window).Handle;
            var ex = GetWindowLong(hwnd,ExStyle) | Layered;
            ex = clickThrough ? ex | Transparent : ex & ~Transparent;
            ex = plan.NoActivate ? ex | NoActivateStyle : ex & ~NoActivateStyle;
            SetWindowLong(hwnd,ExStyle,ex);
            // Per-pixel windows use WPF's UpdateLayeredWindow path; mixing SLA into it breaks rendering.
            if (window.AllowsTransparency) window.Opacity = plan.Opacity;
            else SetLayeredWindowAttributes(hwnd,0,(byte)Math.Round(plan.Opacity*255),2);
            if (Mode == "desktop") EmbedDesktop();
            SetWindowPos(hwnd,Embedded ? desktopAfter : Mode == "desktop" ? Bottom : Mode == "topmost" ? Topmost : NotTopmost,
                0,0,0,0,NoMove|NoSize|NoActivate|FrameChanged);
        }
        finally { applying = false; }
    }

    private void EmbedDesktop()
    {
        var progman = FindWindow("Progman",null);
        if (progman == 0) return;
        // Explorer's private wallpaper protocol; topology detection rather than an OS-version guess.
        if (requestedShell != progman)
        {
            SendMessageTimeout(progman,0x052c,new(0xD),0,2,250,out _);
            SendMessageTimeout(progman,0x052c,new(0xD),new(1),2,250,out _);
            requestedShell = progman;
        }
        var topology = new List<DesktopWindow>();
        EnumWindows((candidate,_) =>
        {
            var name = new System.Text.StringBuilder(64); GetClassName(candidate,name,name.Capacity);
            topology.Add(new(candidate.ToInt64(),0,name.ToString(),GetWindowLong(candidate,ExStyle)));
            foreach (var cls in new[] { "SHELLDLL_DefView", "WorkerW" })
            {
                var child = FindWindowEx(candidate,0,cls,null);
                if (child != 0) topology.Add(new(child.ToInt64(),candidate.ToInt64(),cls,GetWindowLong(child,ExStyle)));
            }
            return true;
        },0);
        var selection = WindowModePlan.FindHost(topology,progman.ToInt64());
        if (selection == null) return;
        var host = new nint(selection.Parent); var after = new nint(selection.After);
        GetWindowRect(hwnd,out var rect);
        var point = new NativePoint { X = rect.Left, Y = rect.Top }; ScreenToClient(host,ref point);
        var originalStyle = GetWindowLong(hwnd,Style);
        SetWindowLong(hwnd,Style,(originalStyle & ~unchecked((int)0x80000000)) | 0x40000000);
        SetParent(hwnd,host);
        if (GetParent(hwnd) != host)
        { SetParent(hwnd,0); SetWindowLong(hwnd,Style,originalStyle); return; }
        desktopHost = host; desktopAfter = after;
        SetWindowPos(hwnd,after,point.X,point.Y,rect.Right-rect.Left,rect.Bottom-rect.Top,NoActivate|FrameChanged);
    }

    public bool DesktopHostLost => Mode == "desktop" && (desktopHost == 0 || !IsWindow(desktopHost));
    public Point? Cursor(FrameworkElement element)
    {
        if (!IsWindowVisible(hwnd) || window.WindowState == WindowState.Minimized || !GetCursorPos(out var point)) return null;
        var hit = WindowFromPoint(point);
        if (!ClickThrough && Mode != "desktop" && hit != hwnd && !IsChild(hwnd,hit)) return null;
        if (Mode == "desktop" && hit != hwnd && !IsChild(hwnd,hit))
        {
            var cls = new System.Text.StringBuilder(64); GetClassName(hit,cls,cls.Capacity);
            if (cls.ToString() is not ("Progman" or "WorkerW" or "SHELLDLL_DefView" or "SysListView32")) return null;
        }
        if (ClickThrough && Mode != "desktop")
        {
            // A transparent window is omitted from WindowFromPoint. Check higher siblings
            // without changing its style or activating anything.
            var previous = GetWindow(hwnd,3);
            for (var count = 0; previous != 0 && count < 100; count++, previous = GetWindow(previous,3))
            {
                if (!IsWindowVisible(previous) || (GetWindowLong(previous,ExStyle)&Transparent)!=0) continue;
                GetWindowRect(previous,out var r);
                if (point.X >= r.Left && point.X < r.Right && point.Y >= r.Top && point.Y < r.Bottom) return null;
            }
        }
        try { return element.PointFromScreen(new(point.X,point.Y)); }
        catch (InvalidOperationException) { return null; }
    }

    public static void MakePopupPassive(Visual child)
    {
        if (PresentationSource.FromVisual(child) is not HwndSource source) return;
        var ex = GetWindowLong(source.Handle,ExStyle);
        SetWindowLong(source.Handle,ExStyle,ex|NoActivateStyle|Transparent);
    }

    private nint Hook(nint handle, int message, nint wParam, nint lParam, ref bool handled)
    {
        if ((Mode == "desktop" || ClickThrough || qa) && message == 0x0021)
        { handled = true; return new(3); } // MA_NOACTIVATE
        if (ClickThrough && message == 0x0084) { handled = true; return new(-1); }
        if (!applying && Mode == "desktop" && message == 0x0046)
        {
            var position = Marshal.PtrToStructure<WindowPosition>(lParam);
            position.InsertAfter = Embedded ? desktopAfter : Bottom;
            position.Flags = (position.Flags & ~4u) | NoActivate;
            Marshal.StructureToPtr(position,lParam,false);
        }
        return 0;
    }

    [StructLayout(LayoutKind.Sequential)] internal struct NativePoint { public int X,Y; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left,Top,Right,Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct WindowPosition { public nint Handle,InsertAfter; public int X,Y,Width,Height; public uint Flags; }
    private delegate bool EnumWindowsProc(nint hwnd,nint parameter);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern nint FindWindow(string cls,string? title);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern nint FindWindowEx(nint parent,nint after,string cls,string? title);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback,nint parameter);
    [DllImport("user32.dll")] private static extern nint SendMessageTimeout(nint hwnd,uint message,nint wParam,nint lParam,uint flags,uint timeout,out nint result);
    [DllImport("user32.dll")] private static extern nint SetParent(nint hwnd,nint parent);
    [DllImport("user32.dll")] private static extern nint GetParent(nint hwnd);
    [DllImport("user32.dll")] private static extern bool ScreenToClient(nint hwnd,ref NativePoint point);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint hwnd,out NativeRect rect);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll")] private static extern nint WindowFromPoint(NativePoint point);
    [DllImport("user32.dll")] private static extern bool IsChild(nint parent,nint child);
    [DllImport("user32.dll")] private static extern bool IsWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] private static extern nint GetWindow(nint hwnd,uint command);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern int GetClassName(nint hwnd,System.Text.StringBuilder text,int count);
    [DllImport("user32.dll")] private static extern int GetWindowLong(nint hwnd,int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(nint hwnd,int index,int value);
    [DllImport("user32.dll")] private static extern bool SetLayeredWindowAttributes(nint hwnd,uint color,byte alpha,uint flags);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint hwnd,nint after,int x,int y,int w,int h,uint flags);
}
