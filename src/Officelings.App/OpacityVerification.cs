using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Officelings.Core;

namespace Officelings.App;

internal static class OpacityVerification
{
    private const int Width = 400, Height = 300, Offscreen = -30000;
    private const int Style = -16, ExStyle = -20, Child = 0x40000000, Visible = 0x10000000;
    private const int Layered = 0x80000, Transparent = 0x20, Topmost = 8, NoActivateStyle = 0x08000000;
    private const int Caption = 0x00c00000, Popup = unchecked((int)0x80000000);
    private const uint NoActivate = 0x10, NoZOrder = 4, FrameChanged = 0x20;
    // Independent fixtures, never calculated by the policy under test. Raster rounding may differ by one.
    private static readonly (string Phase, double Opacity, byte Alpha)[] Steps =
    [
        ("preset-100", 1, 255), ("preset-85", .85, 217), ("preset-70", .70, 179),
        ("preset-50", .50, 128), ("preset-30", .30, 77),
        ("live-100-before", 1, 255), ("live-30", .30, 77), ("live-100-after", 1, 255),
        ("taskbar-refresh", 1, 255)
    ];

    public static int Run(string outputDir, AssetPack? assets = null)
    {
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
            throw new InvalidOperationException("Opacity verification must run on the WPF STA thread.");
        var executable = Environment.ProcessPath;
        if (string.Equals(Path.GetFileName(executable),"dotnet.exe",StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Launch the built Officelings.App.exe --verify-opacity DIR so the Windows 8+ compatibility manifest applies; do not launch the DLL with dotnet.");
        outputDir = Path.GetFullPath(outputDir); Directory.CreateDirectory(outputDir);
        assets ??= AssetPack.LoadDefault(useUserPack: false);
        var app = Application.Current ?? new Application();
        var shutdown = app.ShutdownMode; app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var samples = new List<Observation>(); var pixelComparisons = new List<PixelPathComparison>();
        var nativeComparisons = new List<NativeLayeringComparison>(); var caseCount = 0;
        try
        {
            using var host = new HwndSource(new HwndSourceParameters("Officelings opacity verification own parent")
            {
                PositionX = Offscreen, PositionY = Offscreen, Width = Width + 80, Height = Height + 80,
                WindowStyle = Popup, ExtendedWindowStyle = NoActivateStyle | 0x80
            });
            RequireOwn(host.Handle);
            if (!SetWindowPos(host.Handle,0,Offscreen,Offscreen,Width+80,Height+80,NoZOrder|NoActivate|FrameChanged))
                throw new Win32Exception(Marshal.GetLastWin32Error(),"Cannot position the verification parent.");
            if (IsWindowVisible(host.Handle)) throw new InvalidOperationException("Verification parent must stay hidden.");

            foreach (var mode in new[] { "normal", "topmost", "desktop" })
                foreach (var through in new[] { false, true })
                    foreach (var borderless in new[] { false, true })
                        foreach (var transparent in new[] { false, true })
                        {
                            RunCase(++caseCount,mode,through,borderless,transparent,mode == "desktop" || transparent,
                                "application",host,assets,outputDir,samples,pixelComparisons,nativeComparisons);
                            // Exercise SLA on a real WS_CHILD as well as the app's per-pixel desktop surface.
                            if (mode == "desktop" && !transparent)
                                RunCase(++caseCount,mode,through,borderless,false,false,"regular-child",
                                    host,assets,outputDir,samples,pixelComparisons,nativeComparisons);
                        }
        }
        finally { app.ShutdownMode = shutdown; }

        var failed = samples.Count(sample => !sample.Passed) + nativeComparisons.Count(comparison => !comparison.After.Passed);
        var expectedSamples = 28 * (Steps.Length * 2 + 4);
        var passed = failed == 0 && caseCount == 28 && samples.Count == expectedSamples && nativeComparisons.Count == 8;
        File.WriteAllText(Path.Combine(outputDir,"opacity-verification.json"),JsonSerializer.Serialize(new
        {
            passed, executable, cases = caseCount, expectedCases = 28, expectedSamples, actualSamples = samples.Count, failed,
            alphaTolerance = 1, expectedPresetAlpha = new[] { 255,217,179,128,77 },
            measurement = "Hidden own HWNDs; EnsureHandle with an explicitly attached HwndSource.RootVisual; no Show, shell embedding or screen capture.",
            rootCause = "WPF HwndTarget clears WS_EX_LAYERED on opaque HwndSources in WM_STYLECHANGING and UpdateWindowSettings, invalidating external SLA alpha. The native policy preempts only the extended-style layering rule and restores SLA after WPF position handling. Per-pixel sources use content pixel opacity without SLA.",
            rootCauseSource = "https://raw.githubusercontent.com/dotnet/wpf/main/src/Microsoft.DotNet.Wpf/src/PresentationCore/System/Windows/InterOp/HwndTarget.cs",
            rootCauseSourceSections = new[] { "WM_STYLECHANGING: !UsesPerPixelOpacity removes WS_EX_LAYERED", "UpdateWindowSettings: non-per-pixel non-child target removes WS_EX_LAYERED" },
            nativeLayeringOwnershipObserved = nativeComparisons.Any(comparison => comparison.LayeringWasStripped),
            nativeLayeringComparisons = nativeComparisons,
            pixelPathComparison = pixelComparisons, legacyNativePathNotMeasured = true,
            pixelComparisonMeasurement = "RootVisual is ContainerVisual -> content, excluding Window. Legacy Window.Opacity and fixed content pixels are compared for both desktop and top-level controls. These diagnostics do not measure native Window alpha, prove a child-specific legacy bug, or determine pass/fail. No opacity properties are read as proof.",
            layeredChildPrerequisite = "WS_EX_LAYERED on WS_CHILD requires a Windows 8-aware application manifest. Launch the built App.exe so its embedded Win8/Win10 supportedOS entries apply; the dotnet DLL host is rejected.",
            supportedOsPrerequisites = new[] { "{4a2f28e3-53b9-4441-ba9c-d69d4a4a6e38}", "{8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a}" },
            documentation = new
            {
                hiddenHwnd = "https://learn.microsoft.com/en-us/dotnet/api/system.windows.interop.windowinterophelper.ensurehandle",
                layeredChild = "https://learn.microsoft.com/en-us/windows/win32/winmsg/using-windows#using-layered-windows",
                sla = "https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setlayeredwindowattributes",
                gla = "https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getlayeredwindowattributes"
            },
            samples
        },new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        return passed ? 0 : 2;
    }

    private static void RunCase(int id, string mode, bool through, bool borderless, bool transparent, bool perPixel,
        string surface, HwndSource hostSource, AssetPack assets, string outputDir, List<Observation> samples,
        List<PixelPathComparison> pixelComparisons, List<NativeLayeringComparison> nativeComparisons)
    {
        var host = hostSource.Handle;
        var root = new DockPanel
        {
            Width = Width, Height = Height,
            Background = transparent ? Brushes.Transparent : new SolidColorBrush(OfficeView.BackgroundColor)
        };
        var band = new Canvas { Height = 48 }; DockPanel.SetDock(band,Dock.Top);
        var swatch = new Border { Width = 24, Height = 24, Background = Brushes.OrangeRed };
        Canvas.SetLeft(swatch,8); Canvas.SetTop(swatch,8); band.Children.Add(swatch);
        var half = new Border { Width = 24, Height = 24, Background = new SolidColorBrush(Color.FromArgb(128,240,80,32)) };
        Canvas.SetLeft(half,48); Canvas.SetTop(half,8); band.Children.Add(half);
        root.Children.Add(band);
        var office = new OfficeAnimator(42,assets); var view = new OfficeView(assets) { TransparentBackground = transparent };
        view.SetZoom(0); root.Children.Add(view);
        var now = DateTimeOffset.UtcNow;
        view.Update(office.Layout,office.Update(Array.Empty<AgentInfo>(),now,.1),now,office.FurnitureSprites);
        var window = new Window
        {
            Title = "Officelings opacity verification own window", Width = Width, Height = Height,
            Left = Offscreen, Top = Offscreen, WindowStartupLocation = WindowStartupLocation.Manual,
            AllowsTransparency = perPixel, WindowStyle = perPixel || borderless || mode == "desktop" ? WindowStyle.None : WindowStyle.SingleBorderWindow,
            Background = perPixel ? Brushes.Transparent : new SolidColorBrush(OfficeView.BackgroundColor),
            ShowActivated = false, ShowInTaskbar = false, Content = root
        };
        var modes = new WindowModes(window,desktopParentOverride: host);
        // EnsureHandle intentionally does not attach WPF visuals. A visual-only parent preserves
        // the content's opacity during RootVisual rendering without making the Window visible.
        var visualRoot = new ContainerVisual(); visualRoot.Children.Add(root);
        HwndSource? attached = null; var blue = false;
        TaskbarHostState? taskbarHostState = null;
        try
        {
            var original = new WindowInteropHelper(window).EnsureHandle();
            RequireOwn(original); modes.Attach(); AttachRoot();
            if (!SetWindowPos(original,0,Offscreen,Offscreen,Width,Height,NoZOrder|NoActivate|FrameChanged))
                throw new Win32Exception(Marshal.GetLastWin32Error(),"Cannot position the verification HWND.");

            if (!perPixel && mode != "desktop")
            {
                // Reproduce the external SLA failure before activating the policy hook, using only
                // an owned, hidden top-level HWND. GLA and the actual style are independent evidence.
                if (!SetWindowPos(original,mode == "topmost" ? new(-1) : new(-2),0,0,0,0,1|2|NoActivate|FrameChanged))
                    throw new Win32Exception(Marshal.GetLastWin32Error(),"Cannot position the native control HWND.");
                var ex = GetWindowLong(original,ExStyle) | Layered | NoActivateStyle;
                ex = through ? ex | Transparent : ex & ~Transparent;
                SetWindowLong(original,ExStyle,ex);
                var sla = SetLayeredWindowAttributes(original,0,77,2);
                var error = sla ? (int?)null : Marshal.GetLastWin32Error();
                var before = Capture("native-layering-before",.30,null,original,null,false);
                before.SlaSucceeded = sla; before.SlaError = error;
                modes.Apply(mode,through,borderless,transparent,.30); AttachRoot();
                var after = Capture("native-layering-after",.30,77,original,null,false);
                nativeComparisons.Add(new(before,after));
            }

            if (perPixel)
            {
                modes.Apply(mode,through,borderless,transparent,1); AttachRoot();
                var handle = new WindowInteropHelper(window).Handle;
                // Deliberately reproduce the legacy path only for this before/after diagnostic.
                // There is no SLA call on either per-pixel surface.
                window.Opacity = .30;
                var before = Capture("legacy-window-opacity-30",.30,null,handle,null,false);
                modes.Apply(mode,through,borderless,transparent,.30); AttachRoot();
                var after = Capture("fixed-content-opacity-30",.30,77,handle,null,false);
                pixelComparisons.Add(new(before,after));
            }

            foreach (var step in Steps)
            {
                var before = new WindowInteropHelper(window).Handle;
                string? applyError = null;
                try
                {
                    NativeRect? taskbarBounds = null;
                    if (step.Phase == "taskbar-refresh")
                    {
                        if (!GetWindowRect(before,out var screenBounds))
                            throw new Win32Exception(Marshal.GetLastWin32Error(),"Cannot capture bounds before the taskbar mutation.");
                        taskbarBounds = screenBounds;
                        var hostDisposedBefore = hostSource.IsDisposed;
                        var hostAliveBefore = IsWindow(host);
                        // WPF taskbar ownership expects a top-level HWND. Release the own child
                        // before source/owner changes so the native desktop host cannot be mistaken for an owner.
                        DetachOwnChild(before);
                        // Both setters run while hidden: no taskbar button or visible window is created.
                        window.ShowInTaskbar = true; window.ShowInTaskbar = false;
                        taskbarHostState = new(hostDisposedBefore,hostAliveBefore,hostSource.IsDisposed,IsWindow(host));
                        if (taskbarHostState.SourceDisposedAfter || !taskbarHostState.NativeAliveAfter)
                            throw new InvalidOperationException("Taskbar refresh lost the own host: sourceDisposed="+
                                taskbarHostState.SourceDisposedAfter+", nativeAlive="+taskbarHostState.NativeAliveAfter);
                        RequireOwn(host);
                    }
                    modes.Apply(mode,through,borderless,transparent,step.Opacity);
                    AttachRoot();
                    // WPF may temporarily replace a child's parent with its taskbar owner/desktop.
                    // Reattach to the own host via Apply before validating and translating its bounds.
                    if (taskbarBounds is { } restoredBounds)
                        SetOwnBounds(new WindowInteropHelper(window).Handle,restoredBounds);
                }
                catch (Exception e) { applyError = e.Message; }
                Capture(step.Phase,step.Opacity,step.Alpha,before,applyError);
                blue = !blue;
                swatch.Background = new SolidColorBrush(blue ? Color.FromRgb(32,80,240) : Color.FromRgb(240,80,32));
                now = now.AddSeconds(1);
                view.Update(office.Layout,office.Update(Array.Empty<AgentInfo>(),now,.1),now,office.FurnitureSprites);
                root.InvalidateVisual(); view.InvalidateVisual();
                // No second Apply: repaint must retain the effective alpha after all native mutations.
                Capture(step.Phase+"-repaint",step.Opacity,step.Alpha,before,applyError);
                if (step.Phase == "live-30")
                {
                    NativeMutation("native-move-30",new NativeRect
                        { Left=Offscreen+12, Top=Offscreen+16, Right=Offscreen+12+Width, Bottom=Offscreen+16+Height });
                    NativeMutation("native-resize-30",new NativeRect
                        { Left=Offscreen+12, Top=Offscreen+16, Right=Offscreen+12+Width+32, Bottom=Offscreen+16+Height+24 });
                    SetOwnBounds(new WindowInteropHelper(window).Handle,new NativeRect
                        { Left=Offscreen, Top=Offscreen, Right=Offscreen+Width, Bottom=Offscreen+Height });
                }
            }
        }
        catch (Exception e)
        {
            var failed = NewObservation("create",1,255,0); failed.Errors.Add(e.Message); samples.Add(failed);
        }
        finally
        {
            if (attached is { IsDisposed: false }) attached.RootVisual = null;
            window.Close();
        }

        void NativeMutation(string phase, NativeRect bounds)
        {
            var handle = new WindowInteropHelper(window).Handle; string? error = null;
            try { SetOwnBounds(handle,bounds); AttachRoot(); }
            catch (Exception e) { error = e.Message; }
            // No Apply here or before repaint: only the native callback may restore SLA.
            Capture(phase,.30,77,handle,error);
            blue = !blue;
            swatch.Background = new SolidColorBrush(blue ? Color.FromRgb(32,80,240) : Color.FromRgb(240,80,32));
            root.InvalidateVisual(); view.InvalidateVisual();
            Capture(phase+"-repaint",.30,77,handle,error);
        }

        void AttachRoot()
        {
            var handle = new WindowInteropHelper(window).Handle;
            RequireOwn(handle);
            var source = HwndSource.FromHwnd(handle) ?? throw new InvalidOperationException("Current HWND has no WPF source.");
            if (ReferenceEquals(attached,source)) return;
            if (attached is { IsDisposed: false }) attached.RootVisual = null;
            source.RootVisual = visualRoot; attached = source;
        }

        Observation NewObservation(string phase, double opacity, byte? alpha, nint before) => new()
        {
            Case = id, Mode = mode, ClickThrough = through, Borderless = borderless, TransparentBackground = transparent,
            Surface = surface, UsesPerPixel = perPixel, Phase = phase, RequestedOpacity = opacity, ExpectedAlpha = alpha,
            ExpectedParent = mode == "desktop" ? host.ToInt64() : 0, PreviousHwnd = before.ToInt64()
        };

        Observation Capture(string phase, double opacity, byte? expectedAlpha, nint before, string? applyError, bool includeInSamples = true)
        {
            var sample = NewObservation(phase,opacity,expectedAlpha,before);
            sample.DiagnosticOnly = !includeInSamples;
            sample.TaskbarHost = taskbarHostState;
            if (includeInSamples) samples.Add(sample);
            if (applyError != null) sample.Errors.Add(applyError);
            try
            {
                root.Measure(new(Width,Height)); root.Arrange(new(0,0,Width,Height)); root.UpdateLayout();
                DrainDispatcher(window.Dispatcher);
                var handle = new WindowInteropHelper(window).Handle; RequireOwn(handle);
                sample.Hwnd = handle.ToInt64(); sample.HookHwnd = modes.HookHandle.ToInt64();
                sample.HandleChanged = handle != before;
                sample.Parent = GetParent(handle).ToInt64(); sample.Style = GetWindowLong(handle,Style); sample.ExStyle = GetWindowLong(handle,ExStyle);
                sample.IsChild = (sample.Style & Child) != 0; sample.IsOwnParentChild = IsChild(host,handle);
                sample.Visible = IsWindowVisible(handle) || (sample.Style & Visible) != 0;
                if (!GetWindowRect(handle,out var rect)) throw new Win32Exception(Marshal.GetLastWin32Error(),"Cannot read HWND bounds.");
                sample.Bounds = new[] { rect.Left,rect.Top,rect.Right,rect.Bottom };
                sample.MouseActivateResult = SendMessage(handle,0x0021,0,0).ToInt32();
                if (through) sample.HitTestResult = SendMessage(handle,0x0084,0,0).ToInt32();
                GetWindowThreadProcessId(GetForegroundWindow(),out var foregroundProcess);
                sample.OwnForeground = foregroundProcess == (uint)Environment.ProcessId;

                Check(!sample.Visible,"HWND became visible.");
                Check(rect.Right <= SystemParameters.VirtualScreenLeft && rect.Bottom <= SystemParameters.VirtualScreenTop,"HWND left its offscreen location.");
                Check(!sample.OwnForeground,"Verification activated an own HWND.");
                Check(sample.HookHwnd == sample.Hwnd && sample.MouseActivateResult == 3,"No-activate hook is missing from the current HWND.");
                Check(!through || sample.HitTestResult == -1,"Click-through hook is missing from the current HWND.");
                Check((sample.ExStyle & Layered) != 0,"WS_EX_LAYERED is missing.");
                Check(((sample.ExStyle & Transparent) != 0) == through,"Click-through style does not match.");
                Check((sample.ExStyle & NoActivateStyle) != 0,"WS_EX_NOACTIVATE is missing.");
                Check(((sample.ExStyle & Topmost) != 0) == (mode == "topmost"),"Topmost style does not match.");
                Check(((sample.Style & Caption) == 0) == (perPixel || borderless || mode == "desktop"),"Borderless style does not match.");
                Check(sample.IsChild == (mode == "desktop") && sample.IsOwnParentChild == (mode == "desktop"),"WS_CHILD/own parent readback does not match.");
                Check(mode != "desktop" || sample.Parent == host.ToInt64(),"Desktop child is not attached to the own test parent.");
                if (sample.TaskbarHost is { } taskbarHost)
                    Check(!taskbarHost.SourceDisposedBefore && taskbarHost.NativeAliveBefore &&
                        !taskbarHost.SourceDisposedAfter && taskbarHost.NativeAliveAfter,"Own host did not survive the taskbar mutation.");
                sample.OpacityError = modes.OpacityError;
                Check(sample.OpacityError == null,"Native opacity restoration failed: "+sample.OpacityError);

                var readback = ReadAlpha(handle);
                sample.Source = readback.Source; sample.ActualUsesPerPixel = readback.UsesPerPixel;
                sample.RenderedAlpha = readback.Opaque.A; sample.RenderedGapAlpha = readback.Gap.A; sample.RenderedHalfAlpha = readback.Half.A;
                sample.LayeredAlpha = readback.LayeredAlpha; sample.LayeredFlags = readback.LayeredFlags; sample.ReadbackError = readback.Error;
                sample.EffectiveAlpha = readback.EffectiveAlpha; sample.EffectiveOpacity = readback.EffectiveAlpha / 255d;
                Check(readback.UsesPerPixel == perPixel,"Actual HwndSource opacity path does not match.");
                Check(readback.Error == null,"GetLayeredWindowAttributes failed: "+readback.Error);
                Check(perPixel || (readback.LayeredFlags.GetValueOrDefault() & 2) != 0,"Native LWA_ALPHA is missing.");
                if (expectedAlpha is { } targetAlpha)
                {
                    Check(readback.EffectiveAlpha.HasValue && Math.Abs(readback.EffectiveAlpha.Value-targetAlpha) <= 1,"Effective alpha differs from the independent preset.");
                    Check(Math.Abs(readback.Opaque.A-(perPixel ? targetAlpha : 255)) <= 1,"Rendered content alpha does not match its path.");
                    Check(Math.Abs(readback.Gap.A-(transparent ? 0 : perPixel ? targetAlpha : 255)) <= 1,"Background alpha does not match.");
                    var halfAlpha = transparent ? (perPixel ? opacity * 128 : 128) : perPixel ? targetAlpha : 255;
                    Check(Math.Abs(readback.Half.A-halfAlpha) <= 1,"Translucent pixel alpha does not match.");
                }
                Check(blue ? readback.Opaque.B > readback.Opaque.R : readback.Opaque.R > readback.Opaque.B,"Repaint did not reach the actual source pixels.");
                if (phase is "live-30-repaint" or "legacy-window-opacity-30" or "fixed-content-opacity-30")
                {
                    sample.Png = $"opacity-{id:D2}-{surface}-{phase}.png";
                    SaveComposite(readback,Path.Combine(outputDir,sample.Png));
                }
            }
            catch (Exception e) { sample.Errors.Add(e.Message); }
            sample.Passed = sample.Errors.Count == 0;
            return sample;

            void Check(bool condition, string error) { if (!condition) sample.Errors.Add(error); }
        }
    }

    private sealed record Pixel(byte B, byte G, byte R, byte A);
    private sealed record TaskbarHostState(bool SourceDisposedBefore, bool NativeAliveBefore, bool SourceDisposedAfter, bool NativeAliveAfter);
    private sealed record PixelPathComparison(Observation Before, Observation After)
    {
        public bool SameHwnd => Before.Hwnd != 0 && Before.Hwnd == After.Hwnd;
        public bool TopLevelControl => Before.Mode != "desktop";
        public bool LegacyNativePathNotMeasured => true;
    }
    private sealed record NativeLayeringComparison(Observation Before, Observation After)
    {
        public bool LayeringWasStripped => Before.Hwnd != 0 && Before.Hwnd == After.Hwnd &&
            (Before.ExStyle&Layered) == 0 && Before.SlaSucceeded == false && Before.ReadbackError == 87;
    }
    private sealed record AlphaReadback(bool UsesPerPixel, Pixel Opaque, Pixel Gap, Pixel Half, byte? LayeredAlpha,
        uint? LayeredFlags, int? Error, double? EffectiveAlpha, string Source, RenderTargetBitmap Bitmap);

    private static AlphaReadback ReadAlpha(nint handle)
    {
        RequireOwn(handle);
        var source = HwndSource.FromHwnd(handle) ?? throw new InvalidOperationException("No HwndSource for the real HWND.");
        var visual = source.RootVisual ?? throw new InvalidOperationException("The real HWND has no RootVisual.");
        var bitmap = new RenderTargetBitmap(Width,Height,96,96,PixelFormats.Pbgra32); bitmap.Render(visual);
        Pixel At(int x, int y)
        {
            var bytes = new byte[4]; bitmap.CopyPixels(new Int32Rect(x,y,1,1),bytes,4,0);
            return new(bytes[0],bytes[1],bytes[2],bytes[3]);
        }
        var opaque = At(20,20); var gap = At(100,20); var half = At(60,20);
        if (source.UsesPerPixelOpacity)
            // GLA is intentionally not called: it cannot read UpdateLayeredWindow surfaces.
            return new(true,opaque,gap,half,null,null,null,opaque.A,"HwndSource.RootVisual/RenderTargetBitmap alpha",bitmap);
        if (!GetLayeredWindowAttributes(handle,out _,out var alpha,out var flags))
            return new(false,opaque,gap,half,null,null,Marshal.GetLastWin32Error(),null,"GetLayeredWindowAttributes",bitmap);
        return new(false,opaque,gap,half,alpha,flags,null,alpha * opaque.A / 255d,
            "GetLayeredWindowAttributes + RootVisual pixel alpha",bitmap);
    }

    private static void SaveComposite(AlphaReadback readback, string file)
    {
        var composite = new DrawingVisual();
        using (var drawing = composite.RenderOpen())
        {
            drawing.DrawRectangle(Brushes.Black,null,new(0,0,Width,Height));
            drawing.DrawRectangle(Brushes.White,null,new(Width,0,Width,Height));
            if (!readback.UsesPerPixel) drawing.PushOpacity(readback.LayeredAlpha.GetValueOrDefault()/255d);
            drawing.DrawImage(readback.Bitmap,new(0,0,Width,Height));
            drawing.DrawImage(readback.Bitmap,new(Width,0,Width,Height));
            if (!readback.UsesPerPixel) drawing.Pop();
        }
        var bitmap = new RenderTargetBitmap(Width*2,Height,96,96,PixelFormats.Pbgra32); bitmap.Render(composite);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(file); png.Save(stream);
    }

    private static void RequireOwn(nint handle)
    {
        GetWindowThreadProcessId(handle,out var process);
        if (handle == 0 || !IsWindow(handle) || process != (uint)Environment.ProcessId)
            throw new InvalidOperationException("Verification can only inspect its own live HWNDs.");
    }

    private static void SetOwnBounds(nint handle, NativeRect bounds)
    {
        RequireOwn(handle);
        var point = new WindowModes.NativePoint { X = bounds.Left, Y = bounds.Top };
        if ((GetWindowLong(handle,Style)&Child) != 0)
        {
            var parent = GetParent(handle); RequireOwn(parent);
            if (!ScreenToClient(parent,ref point))
                throw new Win32Exception(Marshal.GetLastWin32Error(),"Cannot convert the own child screen bounds.");
        }
        if (!SetWindowPos(handle,0,point.X,point.Y,bounds.Right-bounds.Left,bounds.Bottom-bounds.Top,NoZOrder|NoActivate|FrameChanged))
            throw new Win32Exception(Marshal.GetLastWin32Error(),"Cannot move or resize the own verification HWND.");
    }

    private static void DetachOwnChild(nint handle)
    {
        RequireOwn(handle);
        var style = GetWindowLong(handle,Style);
        if ((style&Child) == 0) return;
        var parent = GetParent(handle); RequireOwn(parent);
        if (!GetWindowRect(handle,out var bounds))
            throw new Win32Exception(Marshal.GetLastWin32Error(),"Cannot capture the own child bounds before detaching.");
        SetParent(handle,0);
        SetWindowLong(handle,Style,(style&~Child)|Popup);
        if ((GetWindowLong(handle,Style)&Child) != 0 || GetParent(handle) == parent)
            throw new InvalidOperationException("Cannot detach the own child before the taskbar mutation.");
        SetOwnBounds(handle,bounds);
    }

    private static void DrainDispatcher(Dispatcher dispatcher)
    {
        var frame = new DispatcherFrame();
        dispatcher.BeginInvoke(DispatcherPriority.ContextIdle,new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private sealed class Observation
    {
        public int Case { get; init; }
        public string Mode { get; init; } = "";
        public string Surface { get; init; } = "";
        public string Phase { get; init; } = "";
        public bool ClickThrough { get; init; }
        public bool Borderless { get; init; }
        public bool TransparentBackground { get; init; }
        public bool UsesPerPixel { get; init; }
        public double RequestedOpacity { get; init; }
        public byte? ExpectedAlpha { get; init; }
        public bool DiagnosticOnly { get; set; }
        public TaskbarHostState? TaskbarHost { get; set; }
        public long ExpectedParent { get; init; }
        public long PreviousHwnd { get; init; }
        public long Hwnd { get; set; }
        public long HookHwnd { get; set; }
        public bool HandleChanged { get; set; }
        public long Parent { get; set; }
        public int Style { get; set; }
        public int ExStyle { get; set; }
        public bool IsChild { get; set; }
        public bool IsOwnParentChild { get; set; }
        public bool Visible { get; set; }
        public int[] Bounds { get; set; } = [];
        public bool OwnForeground { get; set; }
        public int MouseActivateResult { get; set; }
        public int? HitTestResult { get; set; }
        public bool ActualUsesPerPixel { get; set; }
        public string? Source { get; set; }
        public byte? RenderedAlpha { get; set; }
        public byte? RenderedGapAlpha { get; set; }
        public byte? RenderedHalfAlpha { get; set; }
        public byte? LayeredAlpha { get; set; }
        public uint? LayeredFlags { get; set; }
        public int? ReadbackError { get; set; }
        public bool? SlaSucceeded { get; set; }
        public int? SlaError { get; set; }
        public string? OpacityError { get; set; }
        public double? EffectiveAlpha { get; set; }
        public double? EffectiveOpacity { get; set; }
        public string? Png { get; set; }
        public bool Passed { get; set; }
        public List<string> Errors { get; } = [];
    }

    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left,Top,Right,Bottom; }
    [DllImport("user32.dll")] private static extern bool IsWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] private static extern bool IsChild(nint parent,nint child);
    [DllImport("user32.dll")] private static extern nint GetParent(nint hwnd);
    [DllImport("user32.dll",SetLastError=true)] private static extern nint SetParent(nint hwnd,nint parent);
    [DllImport("user32.dll")] private static extern int GetWindowLong(nint hwnd,int index);
    [DllImport("user32.dll",SetLastError=true)] private static extern int SetWindowLong(nint hwnd,int index,int value);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd,out uint process);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern nint SendMessage(nint hwnd,int message,nint wParam,nint lParam);
    [DllImport("user32.dll",SetLastError=true)] private static extern bool GetWindowRect(nint hwnd,out NativeRect rect);
    [DllImport("user32.dll",SetLastError=true)] private static extern bool GetLayeredWindowAttributes(nint hwnd,out uint color,out byte alpha,out uint flags);
    [DllImport("user32.dll",SetLastError=true)] private static extern bool SetLayeredWindowAttributes(nint hwnd,uint color,byte alpha,uint flags);
    [DllImport("user32.dll",SetLastError=true)] private static extern bool ScreenToClient(nint hwnd,ref WindowModes.NativePoint point);
    [DllImport("user32.dll",SetLastError=true)] private static extern bool SetWindowPos(nint hwnd,nint after,int x,int y,int width,int height,uint flags);
}
