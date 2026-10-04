using System.Runtime.ExceptionServices;
using Vibedotcom.App;
using Vibedotcom.Core;
using Vibedotcom.Core.Iso;
using Xunit;

namespace Vibedotcom.Tests;

public sealed class IsoIntegrationTests
{
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
                    null,false,$"Agent {i+1}",null,null,null,null,null,null,AgentState.Working,epoch,epoch,false)).ToArray();
                var scene=office.Update(agents,epoch,0);
                for(var frame=1;frame<=400;frame++) scene=office.Update(agents,epoch.AddSeconds(frame*.05),.05);
                Assert.Equal(12,scene.Labels.Count);
                Assert.Equal(12,scene.Items.Count(i=>i.Layer==IsoLayer.Character && i.Clip.StartsWith("typing/")));
                Assert.Contains(scene.Items,i=>i.SheetId==IsoActorShadow.SheetId);
                foreach(var item in scene.Items)
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
            }
            catch(Exception e) { error=e; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if(error!=null) ExceptionDispatchInfo.Capture(error).Throw();
    }
}
