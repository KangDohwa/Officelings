using System;
using System.Runtime.InteropServices;
using System.ComponentModel;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Vibedotcom.Core;

namespace Vibedotcom.App;

internal sealed class WindowModes(Window window, bool qa = false, nint? desktopParentOverride = null)
{
    public string Mode { get; private set; } = "normal";
    public bool ClickThrough { get; private set; }
    public bool Embedded => desktopHost != 0 && IsWindow(desktopHost) && GetParent(hwnd) == desktopHost &&
        (GetWindowLong(hwnd,Style) & Child) != 0;
    public nint Handle { get { Attach(); return hwnd; } }
    public nint HookHandle => hookSource is { IsDisposed: false } ? hookSource.Handle : 0;
    public string? OpacityError { get; private set; }
    public Rect ScreenBounds
    {
        get
        {
            Attach();
            if (hwnd == 0 || !GetWindowRect(hwnd, out var r)) return new(window.Left, window.Top, window.Width, window.Height);
            var dpi = VisualTreeHelper.GetDpi(window);
            return new(r.Left / dpi.DpiScaleX, r.Top / dpi.DpiScaleY, (r.Right-r.Left) / dpi.DpiScaleX, (r.Bottom-r.Top) / dpi.DpiScaleY);
        }
    }
    private nint desktopHost, desktopAfter, hwnd, requestedShell;
    private HwndSource? hookSource;
    private const int Style = -16, ExStyle = -20, Transparent = 0x20, Layered = 0x80000, NoActivateStyle = 0x08000000;
    private const int Child = 0x40000000, Popup = unchecked((int)0x80000000);
    private const uint NoMove = 2, NoSize = 1, NoZOrder = 4, NoActivate = 0x10, FrameChanged = 0x20;
    private static readonly nint Bottom = new(1), Topmost = new(-1), NotTopmost = new(-2);
    private bool applying, restoringOpacity, closed, closeAttached;
    private byte? requestedAlpha;
    private DispatcherOperation? opacityRestore;

    public void Attach()
    {
        if (closed) return;
        if (!closeAttached)
        {
            closeAttached = true;
            window.Closed += (_,_) =>
            {
                closed = true; requestedAlpha = null;
                opacityRestore?.Abort(); opacityRestore = null;
                if (hookSource is { IsDisposed: false }) hookSource.RemoveHook(Hook);
                hookSource = null; hwnd = 0;
            };
        }
        var current = new WindowInteropHelper(window).Handle;
        var source = current == 0 ? null : HwndSource.FromHwnd(current);
        if (current == hwnd && ReferenceEquals(source,hookSource)) return;
        if (hookSource is { IsDisposed: false }) hookSource.RemoveHook(Hook);
        hwnd = current; hookSource = source;
        hookSource?.AddHook(Hook);
    }

    public void Apply(string mode, bool clickThrough, bool borderless = false, bool transparentBackground = false, double opacity = 1)
    {
        Attach();
        if (hwnd == 0 || applying) return;
        applying = true;
        try
        {
            GetWindowRect(hwnd,out var rect);
            var plan = WindowModePlan.Create(mode,clickThrough,borderless,transparentBackground,opacity,qa);
            // Set this before WPF changes styles or sources so our hook can retain the bit it owns.
            requestedAlpha = window.AllowsTransparency ? null : WindowOpacityPlan.Create(plan.Opacity,false,false).ExpectedAlpha;
            OpacityError = null;
            if (desktopHost != 0)
            {
                SetParent(hwnd,0);
                SetWindowLong(hwnd,Style,(GetWindowLong(hwnd,Style) & ~Child) | Popup);
                SetWindowPos(hwnd,NotTopmost,rect.Left,rect.Top,rect.Right-rect.Left,rect.Bottom-rect.Top,NoActivate|FrameChanged);
                desktopHost = desktopAfter = 0;
            }
            Mode = plan.Mode; ClickThrough = plan.ClickThrough;
            // Verification uses only hidden, owned HWNDs and native SWP_NOACTIVATE positioning.
            var verification = desktopParentOverride.HasValue;
            window.ShowActivated = !plan.NoActivate && !verification;
            if (!verification) window.Topmost = plan.Topmost;
            window.ShowInTaskbar = plan.ShowInTaskbar && !verification;
            window.WindowStyle = window.AllowsTransparency || plan.Borderless ? WindowStyle.None : WindowStyle.SingleBorderWindow;
            // ShowInTaskbar can replace WPF's native source. Move the hook to the current HWND.
            Attach();
            if (hwnd == 0) return;
            // WPF taskbar/source changes can replace the HWND or reinterpret child coordinates.
            if (GetWindowRect(hwnd,out var currentRect) && !currentRect.Equals(rect)) RestoreScreenBounds(rect);
            var ex = GetWindowLong(hwnd,ExStyle);
            if (!window.AllowsTransparency) ex |= Layered;
            ex = plan.ClickThrough ? ex | Transparent : ex & ~Transparent;
            ex = plan.NoActivate || verification ? ex | NoActivateStyle : ex & ~NoActivateStyle;
            SetWindowLong(hwnd,ExStyle,ex);
            if (Mode == "desktop")
            {
                if (desktopParentOverride is { } host)
                {
                    GetWindowThreadProcessId(host,out var process);
                    if (!IsWindow(host) || process != (uint)Environment.ProcessId)
                        throw new ArgumentException("The desktop override must be a live HWND owned by this process.",nameof(desktopParentOverride));
                    EmbedAt(host,0);
                }
                else EmbedDesktop();
            }
            if (!SetWindowPos(hwnd,Embedded ? desktopAfter : Mode == "desktop" ? Bottom : Mode == "topmost" ? Topmost : NotTopmost,
                0,0,0,0,NoMove|NoSize|NoActivate|FrameChanged))
                throw new Win32Exception(Marshal.GetLastWin32Error(),"Cannot position the opacity HWND.");

            var opacityPlan = WindowOpacityPlan.Create(plan.Opacity,window.AllowsTransparency,Embedded);
            window.Opacity = 1;
            if (opacityPlan.Path == WindowOpacityPath.PerPixelContent)
            {
                // Fade content pixels so the per-pixel path also works after WS_CHILD reparenting.
                // Never use SLA here: it disables WPF's UpdateLayeredWindow rendering path.
                window.Background = Brushes.Transparent;
                if (window.Content is not UIElement content)
                    throw new InvalidOperationException("Per-pixel opacity requires UIElement window content.");
                content.Opacity = opacityPlan.Opacity;
            }
            else
            {
                if (window.Content is UIElement content) content.Opacity = 1;
                // Parent, style and z-order must be final before applying native constant alpha.
                RestoreNativeOpacity();
            }
        }
        finally { applying = false; }
    }

    private void RestoreScreenBounds(NativeRect rect)
    {
        var point = new NativePoint { X = rect.Left, Y = rect.Top };
        if ((GetWindowLong(hwnd,Style)&Child) != 0 && GetParent(hwnd) is var parent && parent != 0)
            ScreenToClient(parent,ref point);
        if (!SetWindowPos(hwnd,0,point.X,point.Y,rect.Right-rect.Left,rect.Bottom-rect.Top,NoZOrder|NoActivate|FrameChanged))
            throw new Win32Exception(Marshal.GetLastWin32Error(),"Cannot restore HWND screen bounds.");
    }

    private void RestoreNativeOpacity()
    {
        if (closed || restoringOpacity || requestedAlpha is not { } alpha || hwnd == 0 || window.AllowsTransparency) return;
        restoringOpacity = true;
        try
        {
            var ex = GetWindowLong(hwnd,ExStyle);
            if ((ex&Layered) == 0) SetWindowLong(hwnd,ExStyle,ex|Layered);
            if ((GetWindowLong(hwnd,ExStyle)&Layered) == 0)
                throw new Win32Exception(Marshal.GetLastWin32Error(),"WPF removed WS_EX_LAYERED from the opacity HWND.");
            if (!SetLayeredWindowAttributes(hwnd,0,alpha,2))
                throw new Win32Exception(Marshal.GetLastWin32Error(),"SetLayeredWindowAttributes failed.");
            OpacityError = null;
        }
        catch (Win32Exception e) { OpacityError = $"{e.Message} (Win32 {e.NativeErrorCode})"; throw; }
        finally { restoringOpacity = false; }
    }

    private void QueueOpacityRestore()
    {
        if (closed || applying || restoringOpacity || requestedAlpha == null || opacityRestore != null ||
            window.Dispatcher.HasShutdownStarted) return;
        // AddHook runs before HwndTarget's default position handling. Restore only after it returns.
        opacityRestore = window.Dispatcher.BeginInvoke(DispatcherPriority.Render,new Action(() =>
        {
            opacityRestore = null;
            if (closed) return;
            Attach();
            try { RestoreNativeOpacity(); }
            catch (Win32Exception) { } // OpacityError preserves asynchronous native failures for readback.
        }));
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
        EmbedAt(new(selection.Parent),new(selection.After));
    }

    private void EmbedAt(nint host, nint after)
    {
        GetWindowRect(hwnd,out var rect);
        var point = new NativePoint { X = rect.Left, Y = rect.Top }; ScreenToClient(host,ref point);
        var originalStyle = GetWindowLong(hwnd,Style);
        SetWindowLong(hwnd,Style,(originalStyle & ~Popup) | Child);
        SetParent(hwnd,host);
        if (GetParent(hwnd) != host || (GetWindowLong(hwnd,Style)&Child) == 0)
        { SetParent(hwnd,0); SetWindowLong(hwnd,Style,originalStyle); return; }
        desktopHost = host; desktopAfter = after;
        SetWindowPos(hwnd,after,point.X,point.Y,rect.Right-rect.Left,rect.Bottom-rect.Top,NoActivate|FrameChanged);
    }

    public bool DesktopHostLost { get { Attach(); return Mode == "desktop" && !Embedded; } }
    internal bool AnimationSuppressed
    {
        get
        {
            // QA deliberately uses offscreen owned windows to measure live rendering.
            if (qa) return false;
            Attach();
            if (hwnd==0 || !IsWindowVisible(hwnd) || window.WindowState==WindowState.Minimized) return true;
            if (DwmGetWindowAttribute(hwnd,14,out var cloaked,sizeof(int))>=0 && cloaked!=0) return true;
            if (!GetWindowRect(hwnd,out var target)) return false;
            var root=GetAncestor(hwnd,2);
            // Conservative occlusion: a single opaque higher window must cover the
            // entire office. Partial/multiple-window and per-pixel coverage is unknown.
            var candidate=GetTopWindow(0);
            for (var n=0;candidate!=0 && candidate!=root && n<512;n++,candidate=GetWindow(candidate,2))
            {
                if (!IsWindowVisible(candidate) || IsIconic(candidate) || (GetWindowLong(candidate,ExStyle)&(Layered|Transparent))!=0) continue;
                if (DwmGetWindowAttribute(candidate,14,out var hidden,sizeof(int))>=0 && hidden!=0) continue;
                if (GetWindowRect(candidate,out var cover) && cover.Left<=target.Left && cover.Top<=target.Top && cover.Right>=target.Right && cover.Bottom>=target.Bottom) return true;
            }
            return false;
        }
    }
    public Point? Cursor(FrameworkElement element)
    {
        Attach();
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
        if (handle == hwnd && requestedAlpha.HasValue && !window.AllowsTransparency)
        {
            if (message == 0x007c && wParam == new nint(ExStyle) && lParam != 0) // WM_STYLECHANGING
            {
                // HwndTarget always clears this bit for opaque sources. Preempt only that ownership
                // rule; preserve every other requested style, including NoActivate and Transparent.
                var styles = Marshal.PtrToStructure<NativeStyles>(lParam);
                styles.New |= Layered;
                Marshal.StructureToPtr(styles,lParam,false);
                handled = true; return 0;
            }
            if (message is 0x0047 or 0x0005 or 0x0003 or 0x007d) QueueOpacityRestore();
        }
        if ((Mode == "desktop" || ClickThrough || qa || desktopParentOverride.HasValue) && message == 0x0021)
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
    [StructLayout(LayoutKind.Sequential)] private struct NativeStyles { public int Old,New; }
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
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd,out uint process);
    [DllImport("user32.dll")] private static extern nint GetWindow(nint hwnd,uint command);
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint hwnd,uint flags);
    [DllImport("user32.dll")] private static extern nint GetTopWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(nint hwnd);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint hwnd,int attribute,out int value,int size);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern int GetClassName(nint hwnd,System.Text.StringBuilder text,int count);
    [DllImport("user32.dll")] private static extern int GetWindowLong(nint hwnd,int index);
    [DllImport("user32.dll",SetLastError=true)] private static extern int SetWindowLong(nint hwnd,int index,int value);
    [DllImport("user32.dll",SetLastError=true)] private static extern bool SetLayeredWindowAttributes(nint hwnd,uint color,byte alpha,uint flags);
    [DllImport("user32.dll",SetLastError=true)] private static extern bool SetWindowPos(nint hwnd,nint after,int x,int y,int w,int h,uint flags);
}
