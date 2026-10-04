using System.Runtime.ExceptionServices;
using Vibedotcom.App;
using Vibedotcom.Core;
using Vibedotcom.Core.Iso;
using Xunit;

namespace Vibedotcom.Tests;

public sealed class IsoIntegrationTests
{
    [Fact]
    public void EmptyEngineWithoutPetsHasInfiniteDeadlineAndReenablingInvalidatesScene()
    {
        var office=new IsoOffice(IsoPack.LoadDefault(),42) { PetsEnabled=false };
        var now=DateTimeOffset.Parse("2026-10-04T00:00:00Z");
        var first=office.Update([],now,0);
        Assert.DoesNotContain(first.Items,i=>i.Layer==IsoLayer.Pet);
        Assert.Equal(double.PositiveInfinity,office.NextChangeSeconds);
        Assert.Same(first,office.Update([],now.AddSeconds(1),1));
        office.PetsEnabled=true;
        var next=office.Update([],now.AddSeconds(1),0);
        Assert.True(next.Revision>first.Revision); Assert.Contains(next.Items,i=>i.Layer==IsoLayer.Pet);
        Assert.True(double.IsFinite(office.NextChangeSeconds));
    }
    private sealed class ClockOffice : IIsoOffice
    {
        public double Elapsed;
        public double NextChangeSeconds=>double.PositiveInfinity;
        public IsoScene Update(IReadOnlyList<AgentInfo> agents,DateTimeOffset now,double deltaSeconds)
        { Elapsed+=deltaSeconds; return new(64,64,[],[],1); }
        public IsoHit? HitTest(int x,int y)=>null;
    }
    private sealed class EmptySheets : IIsoSheetSource
    { public IsoSheetCell GetCell(string sheet,string clip,int frame)=>throw new InvalidOperationException(); }
    [Fact]
    public void SimultaneousArrivalsUseDoorSpacingAndAllNamePlatesStayInBounds()
    {
        Exception? error=null;
        var thread=new Thread(()=>
        {
            try
            {
                var pack=IsoPack.LoadDefault(); var office=new IsoOffice(pack,42); var now=DateTimeOffset.Parse("2026-10-04T00:00:00Z");
                var agents=IsoSmoke.TwelveAgents;
                var scene=office.Update(agents,now,0);
                Assert.Single(scene.Labels);
                for(var frame=1;frame<=10;frame++) scene=office.Update(agents,now.AddSeconds(frame*.05),.05);
                Assert.Equal(2,scene.Labels.Count);
                var view=new IsoView(office,new PackIsoSheetSource(pack),Path.GetTempPath()) { TagMode="always" };
                var crowded=scene with { Revision=scene.Revision+1,Labels=agents.Select(a=>new IsoLabel(a.Id,a.Name,scene.Width/2,90)).ToArray() };
                view.Present(crowded,agents); Vibedotcom.App.Program.RenderBitmap(view,scene.Width,scene.Height);
                foreach(var a in agents)
                { var rect=view.AgentTagRect(a.Id); Assert.True(rect.Top>=0 && rect.Bottom<=scene.Height); }
            }
            catch(Exception e) { error=e; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if(error!=null) ExceptionDispatchInfo.Capture(error).Throw();
    }
    [Fact]
    public void HostInputUpdatesOnlyAdvanceUnconsumedWallTime()
    {
        Exception? error=null;
        var thread=new Thread(()=>
        {
            try
            {
                var office=new ClockOffice(); var view=new IsoView(office,new EmptySheets(),Path.GetTempPath());
                var now=DateTimeOffset.Parse("2026-10-04T00:00:00Z");
                view.Update([],now,0); view.Update([],now.AddSeconds(.4),.4);
                view.AdvanceTo([],now.AddSeconds(1)); view.AdvanceTo([],now.AddSeconds(1));
                Assert.Equal(1,office.Elapsed); view.SetAnimationEnabled(true);
                Assert.False(view.AnimationTimerEnabled); Assert.Equal(0,view.AnimationTickCount);
                view.SetAnimationEnabled(false);
            }
            catch(Exception e) { error=e; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if(error!=null) ExceptionDispatchInfo.Capture(error).Throw();
    }

    [Fact]
    public void WallCapsMeetLastPanelsAndCameraKeepsRoomCompact()
    {
        var pack=IsoPack.LoadDefault(); var office=new IsoOffice(pack,42);
        var scene=office.Update([],DateTimeOffset.UtcNow,0);
        var left=scene.Items.Single(i=>i.InstanceId=="end-cap-l/end");
        var right=scene.Items.Single(i=>i.InstanceId=="end-cap-r/end");
        Assert.Equal(office.Camera.X-320,left.X);
        Assert.Equal(office.Camera.X+319,right.X+pack.Sheet(right.SheetId).CellW-1);
        Assert.InRange(scene.Width,640,720);
        Assert.InRange(scene.Height,384,440);
    }

    [Fact]
    public void EmbeddedPackLayoutAndRuntimeOnlySheetsRenderThroughRealEngine()
    {
        Exception? error=null;
        var thread=new Thread(() =>
        {
            try
            {
                var pack=IsoPack.LoadDefault();
                Assert.Equal(20,pack.Layout.WallSegments!.Count);
                Assert.Equal(12,pack.Layout.Tiles!.Count(t=>t==2));
                Assert.Equal("rear-two-walls",pack.Layout.Cutaway);
                var office=new IsoOffice(pack,42); var source=new PackIsoSheetSource(pack);
                Assert.Equal(12,office.Layout.Furniture.Count(f=>f.AssetId=="iso/desk"));
                var epoch=DateTimeOffset.Parse("2026-10-04T00:00:00Z");
                var agents=Enumerable.Range(0,12).Select(i=>new AgentInfo($"integration:{i:00}",(Provider)(i%3),
                    null,false,$"Agent {i+1}",null,null,null,"",null,null,AgentState.Working,epoch,epoch,false)).ToArray();
                var scene=office.Update(agents,epoch,0);
                var arrival=scene;
                Assert.Contains(arrival.Items,i=>i.SheetId==IsoActorShadow.SheetId);
                for(var frame=1;frame<=400;frame++) scene=office.Update(agents,epoch.AddSeconds(frame*.05),.05);
                Assert.Equal(12,scene.Labels.Count);
                Assert.Equal(12,scene.Items.Count(i=>i.Layer==IsoLayer.Character && i.Clip.StartsWith("typing/")));
                var thinking=office.Update(agents.Select((a,i)=>i==0 ? a with { State=AgentState.Thinking } : a).ToArray(),epoch.AddSeconds(20),0);
                Assert.Contains(thinking.Items,i=>i.SheetId==IsoSharedUi.SheetId);
                foreach(var item in arrival.Items.Concat(scene.Items).Concat(thinking.Items))
                {
                    var cell=source.GetCell(item.SheetId,item.Clip,item.Frame);
                    Assert.True(cell.Image.IsFrozen);
                    Assert.Same(cell,source.GetCell(item.SheetId,item.Clip,item.Frame));
                    Assert.Equal(pack.Frame(item).AnchorX,cell.AnchorX);
                }
                var view=new IsoView(office,source,Path.Combine(Path.GetTempPath(),"iso-integration-empty-brand"));
                view.Present(scene,agents);
                var rendered=Vibedotcom.App.Program.RenderBitmap(view,scene.Width,scene.Height);
                Assert.Equal(scene.Width,rendered.PixelWidth);
                Assert.Equal(scene.Height,rendered.PixelHeight);
                var native=new byte[scene.Width*scene.Height*4]; rendered.CopyPixels(native,scene.Width*4,0);
                foreach(var zoom in new[] {2,3})
                {
                    view.SetZoom(zoom);
                    var scaled=Vibedotcom.App.Program.RenderBitmap(view,scene.Width*zoom,scene.Height*zoom);
                    var pixels=new byte[scaled.PixelWidth*scaled.PixelHeight*4]; scaled.CopyPixels(pixels,scaled.PixelWidth*4,0);
                    for(var y=0;y<scene.Height;y++) for(var x=0;x<scene.Width;x++)
                    for(var dy=0;dy<zoom;dy++) for(var dx=0;dx<zoom;dx++)
                        if(!native.AsSpan((y*scene.Width+x)*4,4).SequenceEqual(pixels.AsSpan(((y*zoom+dy)*scaled.PixelWidth+x*zoom+dx)*4,4)))
                            throw new InvalidOperationException($"Engine pixel changed at {x},{y}, {zoom}x.");
                }
            }
            catch(Exception e) { error=e; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if(error!=null) ExceptionDispatchInfo.Capture(error).Throw();
    }
}
