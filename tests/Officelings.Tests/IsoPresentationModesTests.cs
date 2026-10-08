using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Officelings.App;
using Officelings.Core;
using Xunit;

namespace Officelings.Tests;

public sealed class IsoPresentationModesTests
{
    private sealed class ManualTime : TimeProvider
    {
        internal DateTimeOffset Now = DateTimeOffset.Parse("2026-10-04T00:00:00Z");
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private static void Sta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() => { try { action(); } catch (Exception e) { error = e; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (error != null) ExceptionDispatchInfo.Capture(error).Throw();
    }
    private static IsoView View(bool transparent)
    {
        var source = new SourceManifestIsoSheets(SourceManifestIsoSheets.FindSourceDirectory());
        var agents = FakeIsoOffice.Agents(12); var office = new FakeIsoOffice(source, agents);
        var view = new IsoView(office, source, Path.Combine(Path.GetTempPath(), "officelings-iso-modes-empty-brand"))
        { TransparentBackground = transparent };
        view.SetZoom(1); view.Update(agents, DateTimeOffset.Parse("2026-10-04T00:00:00Z"), 0); return view;
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OwnedOffscreenIsoWindowAppliesTopmostClickThroughAndUniformOpacity(bool transparent) => Sta(() =>
    {
        var view = View(transparent);
        var window = new Window
        {
            Width = 720, Height = 480, Left = -30000, Top = -30000, WindowStartupLocation = WindowStartupLocation.Manual,
            WindowStyle = WindowStyle.None, AllowsTransparency = transparent, ShowActivated = false, ShowInTaskbar = false,
            Background = transparent ? Brushes.Transparent : Brushes.Black, Content = view
        };
        // The existing verification override uses only owned HWNDs and skips
        // activation/taskbar changes. No Explorer desktop messages are sent.
        var modes = new WindowModes(window, desktopParentOverride: 0);
        try
        {
            window.Show(); modes.Attach(); var before = modes.ScreenBounds;
            modes.Apply("topmost", true, true, transparent, .5);
            var handle = new WindowInteropHelper(window).Handle; var style = GetWindowLongPtrW(handle, -20).ToInt64();
            Assert.Equal("topmost", modes.Mode); Assert.True(modes.ClickThrough);
            Assert.NotEqual(0, style & 0x8); Assert.NotEqual(0, style & 0x20); Assert.NotEqual(0, style & 0x08000000);
            Assert.Equal(1, window.Opacity); Assert.Null(modes.OpacityError);
            Assert.Equal(transparent ? .5 : 1, view.Opacity);
            Assert.Equal(before, modes.ScreenBounds); Assert.True(window.Left < -10000);
            modes.Apply("normal", false, true, transparent, 1);
            style = GetWindowLongPtrW(handle, -20).ToInt64();
            Assert.Equal(0, style & 0x8); Assert.Equal(0, style & 0x20); Assert.Equal(1, view.Opacity);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void TransparentIsoMarginsAndHalfOpacityKeepOpaqueScenePixelsUniform() => Sta(() =>
    {
        var view = View(true);
        var full = Officelings.App.Program.RenderBitmap(view, 704, 448);
        var pixels = new byte[full.PixelWidth * full.PixelHeight * 4]; full.CopyPixels(pixels, full.PixelWidth * 4, 0);
        Assert.Equal(0, pixels[3]); Assert.Contains((byte)255, pixels.Where((_, i) => i % 4 == 3));
        view.Opacity = .5;
        var half = Officelings.App.Program.RenderBitmap(view, 704, 448);
        var faded = new byte[pixels.Length]; half.CopyPixels(faded, half.PixelWidth * 4, 0);
        Assert.Equal(0, faded[3]);
        for (var i = 3; i < pixels.Length; i += 4)
            if (pixels[i] == 255) Assert.True(faded[i] is 127 or 128, $"Nonuniform ISO opacity at pixel {i / 4}: {faded[i]}.");
    });

    [Fact]
    public void ViewSettingDefaultsToFlatAndRejectsInvalidStoredView()
    {
        Assert.Equal("flat", new Settings().Normalize().View);
        Assert.Equal("iso", new Settings { View = "iso" }.Normalize().View);
        Assert.Equal("flat", new Settings { View = "wrong" }.Normalize().View);
    }
    [Theory]
    [InlineData(1)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void OffscreenLogicalSizeAndCameraProduceExactPhysicalBitmapBounds(double dpi)
    {
        var viewport = IsoOffscreenRenderer.LogicalSize(1344, 832, new DpiScale(dpi, dpi));
        var camera = IsoView.PhysicalCamera(672, 416, viewport.Width, viewport.Height, dpi, dpi, 2, new());
        var origin = camera.Transform(new Point()); var corner = camera.Transform(new Point(672, 416));
        Assert.Equal(0, origin.X * dpi, 8); Assert.Equal(0, origin.Y * dpi, 8);
        Assert.Equal(1344, corner.X * dpi, 8); Assert.Equal(832, corner.Y * dpi, 8);
    }
    [Fact]
    public void RestoringMinimizedIsoHostPublishesCurrentSceneAndStopsStaticTimer() => Sta(()=>
    {
        using var storage=new AppStorage(true);
        var source=new SourceManifestIsoSheets(SourceManifestIsoSheets.FindSourceDirectory());
        var agents=FakeIsoOffice.Agents(12); var office=new FakeIsoOffice(source,agents);
        var settings=new Settings { View="iso",Left=-30000,Top=-30000,Width=720,Height=480 };
        var window=new IsoWindow(office,source,settings,true,true,0,storage,null,true,agents);
        try
        {
            window.Show(); window.WindowState=WindowState.Minimized;
            office.SetPhase(2); Assert.NotEqual(office.Scene.Revision,window.View.Revision);
            window.WindowState=WindowState.Normal;
            Assert.Equal(office.Scene.Revision,window.View.Revision);
            Assert.False(window.View.AnimationTimerEnabled);
        }
        finally { window.Close(); }
    });
    [Fact]
    public void IconItselfRevealsTheNameEvenAboveTransparentSpritePadding() => Sta(() =>
    {
        var view = View(false); view.TagMode = "icon";
        _ = Officelings.App.Program.RenderBitmap(view, 704, 448);
        var id = view.Scene.Labels.Last().AgentId; var rect = view.AgentIconRect(id);
        Assert.False(rect.IsEmpty);
        var point = new Point(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
        var now = DateTimeOffset.UtcNow;
        view.SimulateHover(point, now); view.AdvanceHover(now.AddMilliseconds(120));
        Assert.False(view.AgentTagRect(id).IsEmpty);
        Assert.True(view.AgentIconRect(id).IsEmpty);
    });
    [Fact]
    public void MovingBodyAndLabelArePickedAgainAtTheStationaryPointer() => Sta(() =>
    {
        var source = new SourceManifestIsoSheets(SourceManifestIsoSheets.FindSourceDirectory());
        var people = FakeIsoOffice.Agents(12); var office = new FakeIsoOffice(source, people);
        var clock = new ManualTime();
        var view = new IsoView(office, source, Path.Combine(Path.GetTempPath(), "officelings-iso-motion-empty-brand"), clock) { TagMode = "icon" };
        var now = clock.Now; view.SetZoom(1); view.Update(people, now, 0);
        using var host = new HwndSource(new HwndSourceParameters("ISO stationary pointer test")
        {
            PositionX = -30000, PositionY = -30000, Width = 704, Height = 448,
            WindowStyle = unchecked((int)0x80000000), ExtendedWindowStyle = 0x08000080
        });
        host.RootVisual = view;
        view.Measure(new Size(704, 448)); view.Arrange(new Rect(0, 0, 704, 448)); view.UpdateLayout();
        view.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        var id = office.Scene.Labels.Last().AgentId; var icon = view.AgentIconRect(id);
        var pointer = new Point(icon.X + icon.Width / 2, icon.Y + icon.Height / 2);
        view.SimulateHover(pointer, now);
        clock.Now = now.AddMilliseconds(120);
        view.AdvanceHover(clock.Now); Assert.False(view.AgentTagRect(id).IsEmpty);
        office.SetSnapshot(office.Scene with
        {
            Revision = office.Scene.Revision + 1,
            Items = office.Scene.Items.Select(i => i.HitTargetId == id ? i with { X = i.X + 96 } : i).ToArray(),
            Labels = office.Scene.Labels.Select(l => l.AgentId == id ? l with { X = l.X + 96 } : l).ToArray()
        });
        var frame = new DispatcherFrame();
        // End the pump after the actual background animation tick has re-picked
        // the pointer, rather than racing it against a higher-priority timer.
        view.Pointer = () =>
        {
            view.Dispatcher.InvokeAsync(() => frame.Continue = false, DispatcherPriority.Background);
            return pointer;
        };
        office.NextChangeSeconds = .05; view.SetAnimationEnabled(true);
        clock.Now = now.AddMilliseconds(170);
        // A deadline only bounds a broken event path; it never completes a passing test.
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var registration = deadline.Token.Register(() =>
            view.Dispatcher.InvokeAsync(() => frame.Continue = false, DispatcherPriority.Send));
        try
        {
            Dispatcher.PushFrame(frame);
            Assert.True(view.AnimationTickCount > 0);
            Assert.Equal(office.Scene.Revision, view.Revision);
            Assert.False(view.HasHoverTarget);
            clock.Now = now.AddMilliseconds(800);
            view.AdvanceHover(clock.Now); Assert.True(view.AgentTagRect(id).IsEmpty);
        }
        finally { view.SetAnimationEnabled(false); }
    });
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtrW(nint hwnd, int index);
}
