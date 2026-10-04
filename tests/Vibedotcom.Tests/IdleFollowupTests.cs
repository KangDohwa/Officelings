using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Vibedotcom.App;
using Vibedotcom.Core;
using Xunit;

namespace Vibedotcom.Tests;

public sealed class IdleFollowupTests
{
    private static readonly DateTimeOffset Epoch = DateTimeOffset.Parse("2026-10-04T12:00:00+09:00");
    private static void Sta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() => { try { action(); } catch (Exception e) { error = e; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (error != null) ExceptionDispatchInfo.Capture(error).Throw();
    }
    private static string Brand => Path.Combine(Path.GetTempPath(), "vd-idle-followup-missing-brand");

    [Fact]
    public void AmbientSampleReusesTheSameCivilSecondWithoutAllocations()
    {
        var environment = new AmbientEnvironment { Settings = new("rain") };
        var state = environment.Sample(Epoch);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++) Assert.Same(state, environment.Sample(Epoch.AddMilliseconds(i)));
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        environment.Settings = new("snow"); Assert.Equal("snow", environment.Sample(Epoch).Weather);
        Assert.NotSame(state, environment.Sample(Epoch.AddSeconds(1)));
        Assert.Equal(TimeSpan.Zero, environment.Sample(Epoch.ToUniversalTime()).LocalTime.Offset);
    }

    [Fact]
    public void ClockOnlySchedulesOneFrameAndDamagesOneSmallVisualPerLogicalSecond() => Sta(() =>
    {
        var pack = AssetPack.LoadDefault(useUserPack: false); var layout = LayoutLoader.Load(pack, null);
        layout = layout with { Furniture = layout.Furniture.Where(f => f.Kind == "clock").ToArray() };
        var view = new OfficeView(pack, Brand) { PetsEnabled = false };
        view.SetZoom(1); view.Update(layout, [], Epoch);
        _ = Vibedotcom.App.Program.RenderBitmap(view, 720, 480);
        Assert.Equal(TimeSpan.FromSeconds(1), view.AnimationInterval);
        var redraws = view.DynamicRenderCount; var rooms = view.RoomBuildCount; var area = view.RedrawPixelArea;
        for (var i = 1; i <= 240; i++) view.Update(layout, [], Epoch.AddMilliseconds(i * 125));
        Assert.Equal(30, view.DynamicRenderCount - redraws);
        Assert.Equal(rooms, view.RoomBuildCount);
        Assert.InRange(view.RedrawPixelArea - area, 1, 30 * 64 * 64);
    });

    [Fact]
    public void AnimatedEmptyOfficeBoundsAllocationsAndRetainsRoomAcrossSlowFrames() => Sta(() =>
    {
        var pack = AssetPack.LoadDefault(useUserPack: false); var office = new OfficeAnimator(42, pack);
        var view = new OfficeView(pack, Brand) { Width = 720, Height = 480 }; view.SetZoom(1);
        using var source = new HwndSource(new HwndSourceParameters("Idle followup counts")
        { PositionX = -30000, PositionY = -30000, Width = 720, Height = 480, WindowStyle = unchecked((int)0x80000000), ExtendedWindowStyle = 0x08000080 });
        var root = new ContainerVisual(); root.Children.Add(view); source.RootVisual = root;
        view.Measure(new(720, 480)); view.Arrange(new(0, 0, 720, 480));
        Action drain = () => { };
        void Frame(int i)
        {
            var now = Epoch.AddMilliseconds(i * 125);
            var poses = office.Update([], now, .125);
            view.Update(office.Layout, poses, now, office.FurnitureSprites);
            view.UpdateLayout(); view.Dispatcher.Invoke(drain, DispatcherPriority.ApplicationIdle);
        }
        for (var i = 0; i < 240; i++) Frame(i);
        var roomBuilds = view.RoomBuildCount; var sceneDraws = view.SceneRenderCount;
        var damage = view.RedrawPixelArea; var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 240; i < 480; i++) Frame(i);
        var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(bytes <= 80 * 1024 * 30, $"Allocated {bytes} bytes over 30 logical seconds");
        Assert.Equal(roomBuilds, view.RoomBuildCount); Assert.Equal(sceneDraws, view.SceneRenderCount);
        Assert.InRange(view.RedrawPixelArea - damage, 1, 720d * 480 * 30 / 2);
        source.RootVisual = null; root.Children.Remove(view);
    });

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void IncrementalClockWeatherAndLightsMatchFreshPixels(int zoom) => Sta(() =>
    {
        var pack = AssetPack.LoadDefault(useUserPack: false); var layout = LayoutLoader.Load(pack, null);
        var local = Epoch; var incremental = new OfficeView(pack, Brand, localClock: () => local) { PetsEnabled = false };
        incremental.SetZoom(zoom); incremental.Update(layout, [], local);
        var width = layout.WidthTiles * 16 * zoom + 32; var height = layout.HeightTiles * 16 * zoom + 32;
        _ = Vibedotcom.App.Program.RenderBitmap(incremental, width, height);
        foreach (var weather in new[] { "off", "rain", "snow" }) foreach (var second in new[] { 1, 2, 17 })
        {
            local = Epoch.AddHours(10).AddSeconds(second); incremental.AmbientSettings = new(weather);
            incremental.SeasonDecor = "auto"; incremental.Update(layout, [], local);
            var fresh = new OfficeView(pack, Brand, localClock: () => local) { PetsEnabled = false, AmbientSettings = new(weather), SeasonDecor = "auto" };
            fresh.SetZoom(zoom); fresh.Update(layout, [], local);
            var a = Vibedotcom.App.Program.RenderBitmap(incremental, width, height);
            var b = Vibedotcom.App.Program.RenderBitmap(fresh, width, height);
            var pixelsA = new byte[width * height * 4]; var pixelsB = new byte[pixelsA.Length];
            a.CopyPixels(pixelsA, width * 4, 0); b.CopyPixels(pixelsB, width * 4, 0);
            Assert.True(pixelsA.AsSpan().SequenceEqual(pixelsB), $"Pixels differ at {zoom}x, {weather}, {second}s");
        }
    });

    [Fact]
    public void GrowingJsonlTailReusesBuffersWithinAnAllocationBudget()
    {
        var root = Path.Combine(Path.GetTempPath(), "vd-tail-counts-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root); var path = Path.Combine(root, "synthetic.jsonl");
        try
        {
            File.WriteAllText(path, "{}\n"); var tail = new JsonlTail(); tail.Read(path);
            var line = new string('x', 2048) + "\n"; long allocated = 0;
            for (var i = 0; i < 110; i++)
            {
                File.AppendAllText(path, line); var before = GC.GetAllocatedBytesForCurrentThread();
                var values = tail.Read(path); var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
                Assert.Single(values); Assert.Equal(2048, values[0].Length);
                if (i >= 10) allocated += bytes;
            }
            // Includes file-boundary checks and decoded 2 KiB strings. The frozen
            // baseline allocates ~35 KiB/append; retained buffers keep this below 20.
            Assert.True(allocated <= 20 * 1024 * 100, $"Tail allocated {allocated} bytes across 100 synthetic appends");
            Assert.Equal(3 + 110 * 2049, tail.BytesRead); Assert.Equal(0, tail.DroppedLines);
        }
        finally { Directory.Delete(root, true); }
    }
}
