using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Vibedotcom.App;
using Vibedotcom.Core;
using Xunit;

namespace Vibedotcom.Tests;

public sealed class WpfPerfTests
{
    // Decoded Pbgra32 hashes measured from unmodified c903758 Release assemblies,
    // independently captured before changing either Core or the WPF renderer.
    private static readonly string[][] DemoPixels =
    [
        ["F31FF4EC5C5C26D02F07010EE12F4F2E42B30879C1DB27D9AECFAA1DDE3A39A8","898C4CE04E694A423E46D2902F70BEB9F4917440E7C52E7BB665EEBF595406C4","0FB3C6DBF9E7752A905631D6191B008A92B29BC23856B9BDC9E7640C460AD159"],
        ["68F2653B37852E954421F3AF0D2B1A1FD7B082AABBBD21B34B8B4E3DFA08B4F0","D3DED12C91327D3C07D1A3C0C5547D29DF823DF721691E7E5201D0EFBFF3E37B","DF9599D8461E8F6F11D269FD02B2220451E99E29361189C9E821C4CFFE12C198"],
        ["03031897ACA686E17FCCC3656C34C1C189EFC1E6EDE2B9D0C0D8D7DB9785238D","88E84D3409A34CEAD5D9932C9400DEA2AAE4D955DC819F73937C4F7CAE971388","6019EC848FB9991E29D2E560EB34AECF38FB5AF7675347EF458A2D9EB07DF1A9"]
    ];
    private static void Sta(Action action)
    {
        Exception? error=null;
        var thread=new Thread(()=> { try { action(); } catch (Exception e) { error=e; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (error!=null) ExceptionDispatchInfo.Capture(error).Throw();
    }
    private static void Flush(OfficeView view)
    {
        view.UpdateLayout(); view.Dispatcher.Invoke(()=> { },DispatcherPriority.ApplicationIdle);
    }
    [Theory]
    [InlineData(0,0)]
    [InlineData(12,1)]
    [InlineData(36,2)]
    public void DemoPixelsMatchUnmodifiedHeadAtAllThreeDensities(int seconds,int index) => Sta(()=>
    {
        var epoch=DateTimeOffset.Parse("2026-10-04T00:00:00Z");
        var pack=AssetPack.LoadDefault(useUserPack:false); var office=new OfficeAnimator(42,pack);
        IReadOnlyList<AgentView> poses=[];
        for (var frame=0;frame<=(seconds+18)*10;frame++)
        { var t=-18+frame*.1; poses=office.Update(DemoScenario.Agents(epoch,t),epoch.AddSeconds(t),.1); }
        for (var zoom=1;zoom<=3;zoom++)
        {
            var view=new OfficeView(pack,Path.Combine(Path.GetTempPath(),"vibedotcom-empty-perf-brand"));
            view.SetZoom(zoom); view.Update(office.Layout,poses,epoch.AddSeconds(seconds),office.FurnitureSprites);
            var width=office.Layout.WidthTiles*16*zoom+32; var height=office.Layout.HeightTiles*16*zoom+32;
            var bitmap=Vibedotcom.App.Program.RenderBitmap(view,width,height);
            var pixels=new byte[width*height*4]; bitmap.CopyPixels(pixels,width*4,0);
            var hash=Convert.ToHexString(SHA256.HashData(pixels));
            if (hash!=DemoPixels[index][zoom-1])
            {
                var output=Path.Combine(Path.GetTempPath(),"vibedotcom-perf-pixel-diff"); Directory.CreateDirectory(output);
                var png=new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
                using var file=File.Create(Path.Combine(output,$"demo-{seconds}s-{zoom}x.png")); png.Save(file);
            }
            Assert.Equal(DemoPixels[index][zoom-1],hash);
        }
    });
    [Fact]
    public void EmptyStaticOfficeStopsAnimationTimerAndHasFewerThanThreeScheduledWakeupsPerSecond() => Sta(()=>
    {
        var pack=AssetPack.LoadDefault(useUserPack:false); var layout=LayoutLoader.Load(pack,null);
        // The bundled TV has three authored frames/second even without agents.
        // This contract exercises a scene with genuinely nothing to animate.
        layout=layout with { Furniture=layout.Furniture.Where(f=>
        {
            var v=pack.Variant(f);
            return v.SheetId==null || v.Animation==null || pack.Manifest.Sheets.Single(s=>s.Id==v.SheetId).Rows.First(r=>r.Name==v.Animation).Frames==1;
        }).ToArray() };
        var office=new OfficeAnimator(42,pack,layout);
        using var storage=new AppStorage(true);
        var window=new OfficeWindow(qa:true,assets:pack,liveOffice:office,storage:storage);
        window.Show();
        try
        {
            var timer=(DispatcherTimer)typeof(OfficeWindow).GetField("timer",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!;
            Assert.Equal(TimeSpan.Zero,office.AnimationInterval); Assert.False(timer.IsEnabled);
            var frame=new DispatcherFrame();
            var end=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(1100) };
            end.Tick+=(_,_)=> { end.Stop(); frame.Continue=false; }; end.Start(); Dispatcher.PushFrame(frame);
            Assert.Equal(0,window.AnimationTickCount); Assert.InRange(window.MaintenanceTickCount,1,3); Assert.Equal(0,window.HoverTickCount);
        }
        finally { window.Close(); }
    });
    [Fact]
    public void SheetsDecodeOnUseAndUnchangedScenesHaveZeroDamage() => Sta(()=>
    {
        var pack=AssetPack.LoadDefault(useUserPack:false); var office=new OfficeAnimator(42,pack);
        var view=new OfficeView(pack); Assert.Empty(view.DecodedSheets);
        var now=DateTimeOffset.Parse("2026-10-04T00:00:00Z"); var poses=office.Update([],now,0);
        view.SetZoom(1); view.Update(office.Layout,poses,now,office.FurnitureSprites);
        _=Vibedotcom.App.Program.RenderBitmap(view,720,480);
        Assert.InRange(view.DecodedSheets.Count,1,pack.Images.Count-1);
        Assert.All(view.DecodedSheets.Values,image=>Assert.True(image.IsFrozen));
        var shared=new OfficeView(pack); Assert.Equal(view.DecodedSheets.Count,shared.DecodedSheets.Count);
        var damage=view.RedrawPixelArea; var draws=view.DynamicRenderCount; var rooms=view.RoomBuildCount;
        for (var i=0;i<240;i++) { view.Update(office.Layout,poses,now.AddMilliseconds(i*125),office.FurnitureSprites); Flush(view); }
        Assert.Equal(damage,view.RedrawPixelArea); Assert.Equal(draws,view.DynamicRenderCount); Assert.Equal(rooms,view.RoomBuildCount);
    });
    [Fact]
    public void AuthoredEmptyRoomStaysBelowIdleAllocationBudgetWithOneRetainedSource() => Sta(()=>
    {
        var pack=AssetPack.LoadDefault(useUserPack:false); var office=new OfficeAnimator(42,pack);
        var view=new OfficeView(pack) { Width=720,Height=480 }; view.SetZoom(1);
        using var source=new HwndSource(new HwndSourceParameters("Idle allocation regression")
        { PositionX=-30000,PositionY=-30000,Width=720,Height=480,WindowStyle=unchecked((int)0x80000000),ExtendedWindowStyle=0x08000080 });
        var root=new ContainerVisual(); root.Children.Add(view); source.RootVisual=root;
        view.Measure(new(720,480)); view.Arrange(new(0,0,720,480));
        var epoch=DateTimeOffset.Parse("2026-10-04T00:00:00Z");
        const int warmup=30,iterations=300;
        const long stepTicks=TimeSpan.TicksPerSecond/3+1;
        void Frame(int index)
        {
            var now=epoch.AddTicks(index*stepTicks);
            var poses=office.Update([],now,stepTicks/(double)TimeSpan.TicksPerSecond);
            view.Update(office.Layout,poses,now,office.FurnitureSprites); Flush(view);
        }
        try
        {
            for (var i=0;i<warmup;i++) Frame(i);
            var rooms=view.RoomBuildCount; var draws=view.DynamicRenderCount;
            var before=GC.GetAllocatedBytesForCurrentThread();
            for (var i=0;i<iterations;i++) Frame(warmup+i);
            var allocated=GC.GetAllocatedBytesForCurrentThread()-before;
            // 300 authored-TV frames represent 100 logical seconds, independent of scheduler load.
            Assert.True(allocated<=100*1024*100,$"Empty Core/View/dispatcher allocated {allocated} bytes over 100 logical seconds.");
            Assert.Equal(rooms,view.RoomBuildCount); Assert.True(view.DynamicRenderCount>draws);
        }
        finally { source.RootVisual=null; root.Children.Remove(view); }
    });
    [Fact]
    public void MovingOneBodyDamagesItsBoundsAndRetainsRoomAndFurniture() => Sta(()=>
    {
        var pack=AssetPack.LoadDefault(useUserPack:false); var office=new OfficeAnimator(42,pack);
        var now=DateTimeOffset.Parse("2026-10-04T00:00:00Z");
        var agent=new AgentInfo("perf:body",Provider.Codex,null,false,"Body",null,null,null,"",null,null,AgentState.Working,now,now,false);
        var poses=office.Update([agent],now,0);
        var view=new OfficeView(pack); view.SetZoom(1); view.Update(office.Layout,poses,now,office.FurnitureSprites);
        _=Vibedotcom.App.Program.RenderBitmap(view,720,480);
        var damage=view.RedrawPixelArea; var rooms=view.RoomBuildCount; var full=view.SceneRenderCount;
        var moved=poses.Select(a=>a with { Pose=a.Pose with { X=a.Pose.X+.1 } }).ToArray();
        view.Update(office.Layout,moved,now.AddMilliseconds(100),office.FurnitureSprites); Flush(view);
        Assert.Equal(rooms,view.RoomBuildCount); Assert.Equal(full,view.SceneRenderCount);
        Assert.InRange(view.RedrawPixelArea-damage,1,720*480/4d);
    });
    [Fact]
    public void HiddenAndFullyCoveredOwnedWindowsSuppressAnimationWithoutActivating() => Sta(()=>
    {
        var window=new Window { Width=400,Height=300,Left=-45000,Top=-45000,WindowStartupLocation=WindowStartupLocation.Manual,
            WindowStyle=WindowStyle.None,ShowActivated=false,ShowInTaskbar=false };
        var cover=new Window { Width=440,Height=340,Left=-45020,Top=-45020,WindowStartupLocation=WindowStartupLocation.Manual,
            WindowStyle=WindowStyle.None,ShowActivated=false,ShowInTaskbar=false };
        var modes=new WindowModes(window);
        try
        {
            window.Show(); modes.Attach(); Assert.False(modes.AnimationSuppressed);
            cover.Show(); Assert.True(modes.AnimationSuppressed);
            cover.Hide(); Assert.False(modes.AnimationSuppressed);
            window.Hide(); Assert.True(modes.AnimationSuppressed);
            window.Show(); Assert.False(modes.AnimationSuppressed);
        }
        finally { cover.Close(); window.Close(); }
    });
}
