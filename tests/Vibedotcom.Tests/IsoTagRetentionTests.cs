using System.Runtime.ExceptionServices;
using Vibedotcom.App;
using Vibedotcom.Core;
using Vibedotcom.Core.Iso;
using Xunit;

namespace Vibedotcom.Tests;

public sealed class IsoTagRetentionTests
{
    [Theory]
    [InlineData("always")] [InlineData("hover")] [InlineData("icon")]
    public void AmbientRevisionsRetainTagsAndUnchangedIdleDoesNotAllocate(string mode)
    {
        Exception? error=null;
        var thread=new Thread(()=>
        {
            try
            {
                var pack=IsoPack.LoadDefault(); var office=new Snapshot();
                var view=new IsoView(office,new PackIsoSheetSource(pack),Path.Combine(Path.GetTempPath(),"synthetic-brand")) {TagMode=mode};
                var scene=new IsoScene(300,200,[],[new("a","Synthetic",100,100)],1); view.Present(scene);
                var draws=view.TagRenderCount; var rect=view.AgentTagRect("a");
                view.Present(scene with {Revision=2});
                Assert.Equal(draws,view.TagRenderCount); Assert.Equal(rect,view.AgentTagRect("a"));
                var idle=scene with {Revision=2}; for(var i=0;i<30;i++)view.Present(idle);
                var before=GC.GetAllocatedBytesForCurrentThread(); for(var i=0;i<1000;i++)view.Present(idle);
                Assert.Equal(0,GC.GetAllocatedBytesForCurrentThread()-before); Assert.Equal(draws,view.TagRenderCount);
                view.Present(scene with {Revision=3,Labels=[new("a","Renamed",100,100)]}); Assert.Equal(draws+1,view.TagRenderCount);
                if(mode=="always")
                {
                    view.SetZoom(2); _=Vibedotcom.App.Program.RenderBitmap(view,200,120);
                    var screen=view.AgentTagRect("a");
                    Assert.True(screen.X>=2 && screen.Y>=2 && screen.Right<=198 && screen.Bottom<=118);
                    view.PanBy(new(20,10)); screen=view.AgentTagRect("a");
                    Assert.True(screen.X>=2 && screen.Y>=2 && screen.Right<=198 && screen.Bottom<=118);
                }
            }
            catch(Exception e) {error=e;}
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if(error!=null) ExceptionDispatchInfo.Capture(error).Throw();
    }
    private sealed class Snapshot : IIsoOffice
    {
        public double NextChangeSeconds=>double.PositiveInfinity;
        public IsoHit? HitTest(int x,int y)=>null;
        public IsoScene Update(IReadOnlyList<AgentInfo> agents,DateTimeOffset now,double delta)=>throw new NotSupportedException();
    }
}
