using System.IO;
using System.Text.Json;
using System.Windows.Media.Imaging;
using Vibedotcom.App;
using Vibedotcom.Core;
using Vibedotcom.Core.Iso;

namespace Vibedotcom.Tests;

internal static class IsoSeatAssemblyRender
{
    private sealed class Snapshot(IsoScene scene) : IIsoOffice
    {
        public double NextChangeSeconds => double.PositiveInfinity;
        public IsoHit? HitTest(int x,int y)=>null;
        public IsoScene Update(IReadOnlyList<AgentInfo> a,DateTimeOffset t,double d)=>scene;
    }
    internal static void Export(string packDirectory,string output)
    {
        var pack=IsoPack.Load(packDirectory);Directory.CreateDirectory(output);
        var view=new IsoView(new Snapshot(new(1,1,[],[],0)),new PackIsoSheetSource(pack),Path.Combine(output,"synthetic-no-brand"));
        long revision=0;var queues=new List<object>();
        void Save(IsoScene scene,string name,int zoom)
        {
            scene=scene with { Labels=[],Revision=++revision };view.Present(scene);view.SetZoom(zoom);
            var bitmap=Vibedotcom.App.Program.RenderBitmap(view,scene.Width*zoom,scene.Height*zoom);
            var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream=File.Create(Path.Combine(output,name+".png"));png.Save(stream);
        }
        foreach(var rotation in new[]{0,90,180,270})
        {
            var seat=IsoContacts.Desk(3,3,rotation);var builder=IsoSeatedOcclusionChecks.Fixture(pack,rotation);
            foreach(var design in pack.Characters)
            foreach(var action in new[]{"typing","thinking","sleep","sit-down","stand-up","idle"})for(var phase=0;phase<4;phase++)
            {
                var clip=pack.Clip(design.SheetId,action,seat.Facing);
                var seatPhase=action switch { "idle"=>"turn", "sit-down"=>"lower", "stand-up"=>"raise", _=>"seated" };
                var scene=builder.Build([new("actor",design.Id,"Synthetic",new(seat.Approach.X,seat.Approach.Y),seat.Facing,action,
                    phase/clip.Fps,"desk",seatPhase,phase)]);
                var ground=IsoGeometry.Ground(seat.Approach.X,seat.Approach.Y);
                var left=builder.Camera.X+ground.X-48;var top=builder.Camera.Y+ground.Y-64;
                scene=scene with { Width=96,Height=96,Items=scene.Items.Select(i=>i with {X=i.X-left,Y=i.Y-top}).ToArray() };
                var name=design.Id.Split('/')[^1]+"-"+seat.Facing+"-"+action+"-"+phase+"-4x";
                Save(scene,name,4);
                queues.Add(new { name, scene=scene with {Items=scene.Items.Where(i=>i.Layer is IsoLayer.Character or IsoLayer.Furniture).ToArray()},
                    frames=scene.Items.Where(i=>i.Layer is IsoLayer.Character or IsoLayer.Furniture).Select(i=>new {i.InstanceId,frame=pack.Frame(i),file=pack.Sheet(i.SheetId).File}) });
            }
        }
        var whole=new IsoSceneBuilder(pack,pack.Layout);var actors=new List<IsoActorPose>();
        foreach(var desk in pack.Layout.Furniture.Where(f=>f.AssetId=="iso/desk"))
        {
            var s=IsoContacts.Desk(desk.X,desk.Y,desk.Rotation);var design=pack.Characters[actors.Count%pack.Characters.Count];
            actors.Add(new("synthetic-"+actors.Count,design.Id,"Synthetic",new(s.Approach.X,s.Approach.Y),s.Facing,"typing",0,desk.Id,"seated"));
        }
        Save(whole.Build(actors),"twelve-agents-2x",2);
        File.WriteAllText(Path.Combine(output,"queue.json"),JsonSerializer.Serialize(queues,new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"Rendered {queues.Count} seated WPF crops at 4x and twelve-agent scene at 2x into {output}");
    }
}
