using System.Runtime.ExceptionServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Officelings.App;
using Officelings.Core.Iso;
using Xunit;

namespace Officelings.Tests;

public sealed class IsoSeatRenderQueueTests
{
    [Fact]
    public void NeThinkingQueueKeepsTheBulbThatOwnsTheReportedDeskPixel()
    {
        Exception? error=null;
        var thread=new Thread(()=> { try { Check(); } catch(Exception e) {error=e;} });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if(error!=null) ExceptionDispatchInfo.Capture(error).Throw();
    }
    private static void Check()
    {
        var pack=IsoPack.LoadDefault();
        var rotation=new[]{0,90,180,270}.Single(r=>IsoContacts.Desk(3,3,r).Facing=="ne");
        var seat=IsoContacts.Desk(3,3,rotation); var builder=IsoSeatedOcclusionChecks.Fixture(pack,rotation);
        var design=pack.Characters.Single(c=>c.Id.EndsWith("/dev-hoodie-v2",StringComparison.Ordinal));
        var scene=builder.Build([new("actor",design.Id,"Synthetic",new(seat.Approach.X,seat.Approach.Y),seat.Facing,"thinking",0,"desk","seated")]);
        var ground=IsoGeometry.Ground(seat.Approach.X,seat.Approach.Y);
        var left=builder.Camera.X+ground.X-48; var top=builder.Camera.Y+ground.Y-64;
        scene=scene with {Width=96,Height=96,Items=scene.Items.Select(i=>i with {X=i.X-left,Y=i.Y-top}).ToArray()};
        var sheets=new PackIsoSheetSource(pack); var owner=""; byte[]? expected=null;
        foreach(var item in IsoSeatAssemblyRender.QueueItems(scene))
        {
            var cell=sheets.GetCell(item.SheetId,item.Clip,item.Frame); var x=66-item.X; var y=25-item.Y;
            if(x<0 || y<0 || x>=cell.Width || y>=cell.Height) continue;
            var bytes=new byte[cell.Width*cell.Height*4]; new FormatConvertedBitmap(cell.Image,PixelFormats.Bgra32,null,0).CopyPixels(bytes,cell.Width*4,0);
            var offset=(y*cell.Width+x)*4;
            if(bytes[offset+3]!=255)continue;
            owner=item.InstanceId; expected=bytes[offset..(offset+4)];
        }
        Assert.Equal("actor/effect",owner); Assert.Equal(new byte[]{30,30,25,255},expected);
        var view=new IsoView(new Snapshot(scene),sheets,Path.Combine(Path.GetTempPath(),"synthetic-no-brand")); view.Present(scene); view.SetZoom(4);
        var bitmap=Officelings.App.Program.RenderBitmap(view,384,384);
        var actual=new byte[384*384*4]; bitmap.CopyPixels(actual,384*4,0);
        for(var y=100;y<104;y++) for(var x=264;x<268;x++) Assert.Equal(expected,actual[((y*384+x)*4)..((y*384+x)*4+4)]);
    }
    private sealed class Snapshot(IsoScene scene) : IIsoOffice
    {
        public double NextChangeSeconds=>double.PositiveInfinity;
        public IsoHit? HitTest(int x,int y)=>null;
        public IsoScene Update(IReadOnlyList<Officelings.Core.AgentInfo> agents,DateTimeOffset now,double delta)=>scene;
    }
}
