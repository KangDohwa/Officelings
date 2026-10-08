using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Officelings.App;
using Officelings.Core;
using Xunit;

namespace Officelings.Tests;

public sealed class WpfPerfTests
{
    // Fixed scene fingerprints recaptured after the ambient layout/provider changes.
    // Fixed local clock and pets off isolate pixel retention from autonomous pet motion.
    private static readonly string[][] DemoPixels =
    [
        ["0051B2B67F953235424E7116F65F3AA9189621E0D2099FE21897DF7FC41DECF0","CB7D8CA56C00FDFFAF7B65AB31F68B6F6AF153F21BF223932200249E30D92931","1EE447F385916A3F51EF42D7911DCFDC7C7D1A9C82216054A40D24D9E61B9438"],
        ["0F98511E1D4941514CC8E76F7E8B55C19B497DCEB2772D43AFFCB49A15485C9E","457EBB6F0BBCDBEDC0E5E0CA4A4E7B4D0ED67CF833D7019F85E843E6F3E88239","B97A715ADE7CD85842E7990905591955FE5CBF15BE36F9473F3286B0691A72BA"],
        ["2344EF83F991F17C5305F81A69E777B02F4907DA0C855219B87D23489B917952","0559E66BC258633E854E923AEF8EE2176136505887A5B2A7FF8B608662CA8B96","D6B5CCA6335956E84C1912698D65C4991BEDF29173391E2B02B0AC98EB26B786"]
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
    public void DemoPixelsMatchReviewedAmbientSceneAtAllThreeDensities(int seconds,int index) => Sta(()=>
    {
        var epoch=DateTimeOffset.Parse("2026-10-04T00:00:00Z");
        var pack=AssetPack.LoadDefault(useUserPack:false); var office=new OfficeAnimator(42,pack);
        IReadOnlyList<AgentView> poses=[];
        for (var frame=0;frame<=(seconds+18)*10;frame++)
        { var t=-18+frame*.1; poses=office.Update(DemoScenario.Agents(epoch,t),epoch.AddSeconds(t),.1); }
        for (var zoom=1;zoom<=3;zoom++)
        {
            var local=epoch.AddSeconds(seconds).ToOffset(TimeSpan.FromHours(9));
            var view=new OfficeView(pack,Path.Combine(Path.GetTempPath(),"officelings-empty-perf-brand"),localClock:()=>local) { PetsEnabled=false };
            view.SetZoom(zoom); view.Update(office.Layout,poses,epoch.AddSeconds(seconds),office.FurnitureSprites);
            var width=office.Layout.WidthTiles*16*zoom+32; var height=office.Layout.HeightTiles*16*zoom+32;
            var bitmap=Officelings.App.Program.RenderBitmap(view,width,height);
            var pixels=new byte[width*height*4]; bitmap.CopyPixels(pixels,width*4,0);
            var hash=Convert.ToHexString(SHA256.HashData(pixels));
            if (hash!=DemoPixels[index][zoom-1])
            {
                var output=Path.Combine(Path.GetTempPath(),"officelings-perf-pixel-diff"); Directory.CreateDirectory(output);
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
        var window=new OfficeWindow(qa:true,initial:new Settings { Pets="off" },assets:pack,liveOffice:office,storage:storage);
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
        var now=DateTimeOffset.Parse("2026-10-04T00:00:00Z");
        var view=new OfficeView(pack,localClock:()=>now) { PetsEnabled=false }; Assert.Empty(view.DecodedSheets);
        var poses=office.Update([],now,0);
        view.SetZoom(1); view.Update(office.Layout,poses,now,office.FurnitureSprites);
        _=Officelings.App.Program.RenderBitmap(view,720,480);
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
        _=Officelings.App.Program.RenderBitmap(view,720,480);
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
