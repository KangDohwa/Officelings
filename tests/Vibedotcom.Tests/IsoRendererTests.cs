using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Vibedotcom.App;
using Vibedotcom.Core;
using Vibedotcom.Core.Iso;
using Xunit;

namespace Vibedotcom.Tests;

public sealed class IsoRendererTests
{
    private static readonly DateTimeOffset Epoch=DateTimeOffset.Parse("2026-10-04T00:00:00Z");
    private static void Sta(Action action)
    {
        Exception? error=null;
        var thread=new Thread(()=> { try { action(); } catch(Exception e) { error=e; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (error!=null) ExceptionDispatchInfo.Capture(error).Throw();
    }
    private static (IsoView View,FakeIsoOffice Office,SourceManifestIsoSheets Sheets,AgentInfo[] Agents) Fixture(int count=12,string mode="hover")
    {
        var sheets=new SourceManifestIsoSheets(SourceManifestIsoSheets.FindSourceDirectory());
        var agents=FakeIsoOffice.Agents(count); var office=new FakeIsoOffice(sheets,agents);
        var view=new IsoView(office,sheets,Path.Combine(Path.GetTempPath(),"vibedotcom-iso-missing-brand")) { TagMode=mode };
        view.SetZoom(1); view.Update(agents,Epoch,0); return (view,office,sheets,agents);
    }
    private static BitmapSource Render(IsoView view,int zoom=1)
    { view.SetZoom(zoom); return Vibedotcom.App.Program.RenderBitmap(view,672*zoom,416*zoom); }
    private static byte[] Pixels(BitmapSource bitmap)
    {
        var result=new byte[bitmap.PixelWidth*bitmap.PixelHeight*4];
        new FormatConvertedBitmap(bitmap,PixelFormats.Pbgra32,null,0).CopyPixels(result,bitmap.PixelWidth*4,0); return result;
    }
    private static Point OpaqueAgentPoint(IsoView view,SourceManifestIsoSheets sheets,string id)
    {
        var item=view.Scene.Items.First(i=>i.Layer==IsoLayer.Character && i.HitTargetId==id);
        var cell=sheets.GetCell(item.SheetId,item.Clip,item.Frame); var pixels=Pixels(cell.Image);
        for (var y=0;y<cell.Height;y++) for (var x=0;x<cell.Width;x++)
            if (pixels[(y*cell.Width+x)*4+3]!=0 && view.HitTestScene(view.ScreenPoint(new(item.X+x+.25,item.Y+y+.25)))?.TargetId==id)
                return view.ScreenPoint(new(item.X+x+.25,item.Y+y+.25));
        throw new InvalidOperationException("No exposed body pixel in hand-made scene.");
    }

    [Fact]
    public void RealSheetsAreLazyFrozenAndSourceCellAnchorsStayIntact() => Sta(()=>
    {
        var sheets=new SourceManifestIsoSheets(SourceManifestIsoSheets.FindSourceDirectory());
        Assert.Empty(sheets.DecodedSheets);
        var frame=sheets.GetCell("dev-hoodie-v2-normal","walk:se",1);
        Assert.True(frame.Image.IsFrozen); Assert.Equal(32,frame.Width); Assert.Equal(64,frame.Height);
        Assert.Equal(16,frame.AnchorX); Assert.Equal(60,frame.AnchorY);
        Assert.Same(frame,sheets.GetCell("dev-hoodie-v2-normal","walk:se",1));
        Assert.Single(sheets.DecodedSheets); Assert.All(sheets.DecodedSheets.Values,i=>Assert.True(i.IsFrozen));
        Assert.Throws<InvalidDataException>(()=>sheets.GetCell("dev-hoodie-v2-normal","walk",0));
    });
    [Fact]
    public void UnchangedRevisionHasZeroAllocationsAndZeroRedraws() => Sta(()=>
    {
        var (view,office,_,agents)=Fixture(); Render(view);
        for(var i=0;i<30;i++) view.Update(agents,Epoch,0);
        var staticBuilds=view.StaticBuildCount; var dynamicDraws=view.DynamicRenderCount; var sceneDraws=view.SceneRenderCount;
        var before=GC.GetAllocatedBytesForCurrentThread();
        for(var i=0;i<1000;i++) view.Update(agents,Epoch,0);
        var allocated=GC.GetAllocatedBytesForCurrentThread()-before;
        Assert.Equal(0,allocated); Assert.Equal(staticBuilds,view.StaticBuildCount);
        Assert.Equal(dynamicDraws,view.DynamicRenderCount); Assert.Equal(sceneDraws,view.SceneRenderCount);
    });
    [Fact]
    public void StaticRunsRetainDepthOrderAndOnlyChangedRunsRebuild() => Sta(()=>
    {
        var (view,office,_,agents)=Fixture(); Render(view);
        var statics=view.StaticBuildCount; var dynamics=view.DynamicRenderCount;
        var original=view.Scene;
        var changed=original.Items.Select(i=>i.Layer==IsoLayer.Character ? i with { Frame=(i.Frame+1)%4 } : i).ToArray();
        view.Present(original with { Items=changed,Revision=original.Revision+1 },agents);
        Assert.Equal(statics,view.StaticBuildCount); Assert.True(view.DynamicRenderCount>dynamics);
        Assert.InRange(view.StaticBitmapCount,2,30);
        Render(view,2); Assert.True(view.StaticBuildCount>statics);
        var zoomBuilds=view.StaticBuildCount; Render(view,2); Assert.Equal(zoomBuilds,view.StaticBuildCount);
        Assert.Equal(changed.Select(i=>i.InstanceId),view.Scene.Items.Select(i=>i.InstanceId));
    });
    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void IntegerZoomMakesExactNearestNeighborBlocks(int zoom) => Sta(()=>
    {
        var (view,_,_,_)=Fixture(); var native=Pixels(Render(view)); var scaled=Pixels(Render(view,zoom));
        for(var y=0;y<416;y++) for(var x=0;x<672;x++) for(var dy=0;dy<zoom;dy++) for(var dx=0;dx<zoom;dx++)
        {
            var expected=native.AsSpan((y*672+x)*4,4);
            var actual=scaled.AsSpan(((y*zoom+dy)*672*zoom+x*zoom+dx)*4,4);
            if(!expected.SequenceEqual(actual)) throw new Xunit.Sdk.XunitException($"Non-integer pixel block at {x},{y}, zoom={zoom}.");
        }
    });
    [Theory]
    [InlineData(1,1)] [InlineData(1.5,1.5)] [InlineData(2,2)] [InlineData(1.25,1.5)]
    public void DpiMapsToIntegerPhysicalPixels(double dpiX,double dpiY)
    {
        var matrix=IsoView.PhysicalCamera(672,416,801,503,dpiX,dpiY,2,new(5,-7));
        var point=matrix.Transform(new Point(121,73)); var next=matrix.Transform(new Point(122,74));
        Assert.Equal(2,(next.X-point.X)*dpiX,8); Assert.Equal(2,(next.Y-point.Y)*dpiY,8);
        Assert.Equal(Math.Round(point.X*dpiX),point.X*dpiX,8); Assert.Equal(Math.Round(point.Y*dpiY),point.Y*dpiY,8);
    }
    [Fact]
    public void HitMappingUsesEngineAndFollowsPanZoomAndCropping() => Sta(()=>
    {
        var (view,office,sheets,agents)=Fixture(); Render(view,3);
        var point=OpaqueAgentPoint(view,sheets,agents[0].Id); var hit=view.HitTestScene(point);
        Assert.Equal(agents[0].Id,hit?.TargetId); var before=office.LastHit;
        view.PanBy(new(31,17)); Assert.Equal(hit,view.HitTestScene(point+new Vector(31,17)));
        Assert.Equal(before,office.LastHit);
        Assert.Null(view.HitTestScene(view.ScreenPoint(new Point(-1,0))));
        Assert.Null(view.HitTestScene(view.ScreenPoint(new Point(672,416))));
        view.SetZoom(0); Vibedotcom.App.Program.RenderBitmap(view,300,180); Assert.Equal(1,view.Zoom);
    });
    [Fact]
    public void InfiniteDeadlineStopsTimerAndFiniteDeadlineResumesIt() => Sta(()=>
    {
        var (view,office,_,agents)=Fixture(0); Render(view);
        view.SetAnimationEnabled(true);
        try
        {
            Assert.False(view.AnimationTimerEnabled);
            office.NextChangeSeconds=.5; view.Update(agents,Epoch,0); Assert.True(view.AnimationTimerEnabled);
            office.NextChangeSeconds=double.PositiveInfinity; view.Update(agents,Epoch,0); Assert.False(view.AnimationTimerEnabled);
            Assert.Equal(0,view.AnimationTickCount);
        }
        finally { view.SetAnimationEnabled(false); }
    });
    [Theory]
    [InlineData("hover")] [InlineData("icon")] [InlineData("always")]
    public void TagModesAndHoverDoNotRedrawStaticScene(string mode) => Sta(()=>
    {
        var (view,_,sheets,agents)=Fixture(mode:mode); Render(view,2);
        Assert.Equal(mode=="always" ? 12 : 0,view.RenderedAgentTags);
        Assert.Equal(mode=="icon" ? 12 : 0,view.RenderedProviderIcons);
        var point=OpaqueAgentPoint(view,sheets,agents[0].Id); var builds=view.StaticBuildCount; var draws=view.SceneRenderCount;
        view.SimulateHover(point,Epoch); view.AdvanceHover(Epoch.AddMilliseconds(120));
        Vibedotcom.App.Program.RenderBitmap(view,1344,832);
        Assert.False(view.AgentTagRect(agents[0].Id).IsEmpty); Assert.True(view.AgentIconRect(agents[0].Id).IsEmpty);
        view.SimulateHover(point,Epoch.AddMilliseconds(420)); Assert.Contains("Synthetic iso render scene",view.DebugTooltipText);
        Assert.Equal(builds,view.StaticBuildCount); Assert.Equal(draws,view.SceneRenderCount);
        view.SimulateHover(null,Epoch.AddMilliseconds(430)); view.AdvanceHover(Epoch.AddMilliseconds(690));
        Assert.Null(view.DebugTooltipText);
    });
    [Fact]
    public void UpdatedPrivacyMetadataRefreshesTooltipWithoutSceneRevision() => Sta(()=>
    {
        var (view,_,sheets,agents)=Fixture(); Render(view,2);
        var point=OpaqueAgentPoint(view,sheets,agents[0].Id);
        view.SimulateHover(point,Epoch); view.SimulateHover(point,Epoch.AddMilliseconds(420));
        var draws=view.SceneRenderCount;
        var safe=agents.Select(a=>a.Id==agents[0].Id ? a with { Task="Filtered task",Cwd=null,Model=null } : a).ToArray();
        view.Update(safe,Epoch.AddMilliseconds(500),0);
        Assert.Contains("Filtered task",view.DebugTooltipText);
        Assert.DoesNotContain("Synthetic iso render scene",view.DebugTooltipText);
        Assert.DoesNotContain("C:/Synthetic/Office",view.DebugTooltipText);
        Assert.Equal(draws,view.SceneRenderCount);
    });
    [Fact]
    public void RepeatedInstanceIdsKeepEverySplitFurniturePartInQueueOrder() => Sta(()=>
    {
        var (view,_,_,agents)=Fixture(); var original=Pixels(Render(view));
        var scene=view.Scene;
        var repeated=scene.Items.Select(i=>i.Layer==IsoLayer.Furniture ? i with { InstanceId="shared-furniture" } : i).ToArray();
        view.Present(scene with { Items=repeated,Revision=scene.Revision+1 },agents);
        Assert.Equal(original,Pixels(Render(view)));
    });
}
