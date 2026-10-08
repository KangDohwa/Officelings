using System.Runtime.ExceptionServices;
using Officelings.App;
using Officelings.Core;
using Officelings.Core.Iso;
using Xunit;

namespace Officelings.Tests;

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
                    view.SetZoom(2); _=Officelings.App.Program.RenderBitmap(view,200,120);
                    var screen=view.AgentTagRect("a");
                    Assert.True(screen.X>=2 && screen.Y>=2 && screen.Right<=198 && screen.Bottom<=118);
                    view.PanBy(new(20,10)); screen=view.AgentTagRect("a");
                    Assert.True(screen.X>=2 && screen.Y>=2 && screen.Right<=198 && screen.Bottom<=118);
                    // A grapheme can contain several fallback glyphs. The
                    // capped plate must also bound the actual painted text.
                    view.SetZoom(1);
                    var name=string.Concat(Enumerable.Repeat("👨‍👩‍👧‍👦",14));
                    view.Present(scene with {Revision=4,Labels=[new("a",name,100,100)]});
                    var bitmap=Officelings.App.Program.RenderBitmap(view,300,200);
                    var pixels=new byte[300*200*4]; bitmap.CopyPixels(pixels,300*4,0);
                    var plate=view.AgentTagRect("a"); Assert.False(plate.IsEmpty); plate.Inflate(1,1);
                    var background=OfficeView.BackgroundColor;
                    for(var y=0;y<200;y++) for(var x=0;x<300;x++)
                    {
                        var p=(y*300+x)*4;
                        if(pixels[p]!=background.B || pixels[p+1]!=background.G || pixels[p+2]!=background.R)
                            Assert.True(plate.Contains(new System.Windows.Point(x+.5,y+.5)),"Text painted outside the collision box.");
                    }
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
