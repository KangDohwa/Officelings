using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using Vibedotcom.Core;
using Vibedotcom.Core.Iso;

namespace Vibedotcom.Tests;

internal static class IsoSeatAssemblyChecks
{
    private static void Require(bool ok, string text)
    { if (!ok) throw new InvalidOperationException(text); }
    private static bool Bit(byte[] bytes, int n) => (bytes[n >> 3] & (1 << (n & 7))) != 0;
    private static byte[] DeskMask(string value)
    {
        if(!value.StartsWith("z:",StringComparison.Ordinal))return Convert.FromBase64String(value);
        using var input=new MemoryStream(Convert.FromBase64String(value[2..]));
        using var compressed=new ZLibStream(input,CompressionMode.Decompress);
        using var output=new MemoryStream();compressed.CopyTo(output);
        var bytes=output.ToArray();Require(bytes.Length==256,"Invalid desk oracle mask size");return bytes;
    }

    private static int CheckDeskPixels(IsoPack pack,IsoScene scene)
    {
        var body=scene.Items.Single(i=>i.Layer==IsoLayer.Character);
        var binding=pack.Clip(body.SheetId,body.Clip).ChairFrames?[body.Frame];
        if(binding?.DeskHidden==null)return 0;
        var hidden=DeskMask(binding.DeskHidden);var overlap=DeskMask(binding.DeskOverlap!);var count=0;
        for(var y=0;y<64;y++)for(var x=0;x<32;x++)
        {
            if(!pack.Opaque(body,x,y))continue;
            var px=body.X+x;var py=body.Y+y;
            var desk=scene.Items.Where(i=>i.Layer==IsoLayer.Furniture && i.InstanceId.StartsWith("desk/") &&
                !i.InstanceId.Contains("__monitor__") && !i.InstanceId.EndsWith("__15") && !i.InstanceId.EndsWith("__16") && !i.InstanceId.EndsWith("/screen") &&
                pack.Opaque(i,px-i.X,py-i.Y)).ToArray();
            Require(desk.Length<=1,$"{body.SheetId}/{body.Clip}/{body.Frame} ({x},{y}): duplicate desk pixels: {string.Join(',',desk.Select(i=>i.InstanceId))}");
            Require((desk.Length!=0)==Bit(overlap,y*32+x),"Desk proxy/real art coverage differs");
            if(!Bit(hidden,y*32+x))continue;
            var top=scene.Items.Last(i=>i.Layer is not (IsoLayer.Floor or IsoLayer.Shadow) && pack.Opaque(i,px-i.X,py-i.Y));
            Require(top!=body,$"{body.SheetId}/{body.Clip}/{body.Frame} ({x},{y}): body drawn above nearer desk");count++;
        }
        return count;
    }

    internal static int CheckPixels(IsoPack pack, IsoScene scene)
    {
        var body = scene.Items.Single(i => i.Layer == IsoLayer.Character);
        var clip = pack.Clip(body.SheetId, body.Clip);
        var binding = clip.ChairFrames?[body.Frame] ?? throw new InvalidOperationException("Missing independent chair coverage mask");
        var hidden = DeskMask(binding.Hidden); var overlap = DeskMask(binding.Overlap);
        Require(hidden.Length == 256 && overlap.Length == 256, "Invalid oracle mask size");
        var assembly = scene.Items.Where(i => i.InstanceId.StartsWith("chair/") && i.Layer == IsoLayer.Furniture || i == body).ToArray();
        var count = 0;
        for (var y = 0; y < 64; y++) for (var x = 0; x < 32; x++)
        {
            var n = y * 32 + x;
            if (!pack.Opaque(body, x, y)) { Require(!Bit(overlap,n), "Oracle covers transparent actor texel"); continue; }
            var px = body.X + x; var py = body.Y + y;
            var chair = assembly.Where(i => i != body && pack.Opaque(i,px-i.X,py-i.Y)).ToArray();
            Require(chair.Length <= 1, "Opaque chair texel duplicated");
            Require((chair.Length != 0) == Bit(overlap,n), "Oracle/real chair silhouette differs");
            if (chair.Length == 0) continue;
            var top = assembly.Last(i => pack.Opaque(i,px-i.X,py-i.Y));
            Require((top != body) == Bit(hidden,n), $"{body.SheetId}/{body.Clip}/{body.Frame} ({x},{y}): inverted chair/actor ownership");
            count++;
        }
        return count;
    }

    internal static void AllPoseFrames(IsoPack? supplied = null)
    {
        var pack = supplied ?? IsoPack.LoadDefault(); var frames = 0; var pixels = 0; var deskPixels = 0;
        var minimumBack = 1d; var minimumFront = 1d;
        foreach (var rotation in new[] { 0,90,180,270 })
        {
            var seat = IsoContacts.Desk(3,3,rotation); var builder = IsoSeatedOcclusionChecks.Fixture(pack,rotation);
            foreach (var design in pack.Characters)
            foreach (var action in new[] { "typing","thinking","sleep","sit-down","stand-up","idle" })
            {
                var clip = pack.Clip(design.SheetId,action,seat.Facing);
                foreach(var blink in seat.Facing is "se" or "sw" ? new[]{false,true} : new[]{false})
                for (var frame = 0; frame < clip.Frames; frame++)
                {
                    var phase = action switch { "sit-down" => "lower", "stand-up" => "raise", "idle" => "turn", _ => "seated" };
                    var scene = builder.Build([new("actor",design.Id,"Synthetic",new(seat.Approach.X,seat.Approach.Y),seat.Facing,
                        action,frame/clip.Fps,"desk",phase,frame,Blink:blink)]);
                    pixels += CheckPixels(pack,scene); frames++;
                    deskPixels += CheckDeskPixels(pack,scene);
                    var body = scene.Items.Single(i => i.Layer == IsoLayer.Character);
                    Require(scene.Items.Count(i => i.InstanceId.StartsWith("chair/") && i.Layer == IsoLayer.Furniture)==3, "Draw-item budget exceeded");
                    // Ordinary or attached desk fronts must cover every overlapping actor sample.
                    // The rear keyboard surface stays behind hands. This check is separate from chair ownership.
                    for (var y = 0; y < 64; y++) for (var x = 0; x < 32; x++)
                    {
                        if (!pack.Opaque(body,x,y)) continue;
                        var px=body.X+x; var py=body.Y+y;
                        if (!scene.Items.Any(i=>i.InstanceId.StartsWith("desk/") && i.Depth>body.Depth && pack.Opaque(i,px-i.X,py-i.Y))) continue;
                        var top=scene.Items.Last(i=>i.Layer is not (IsoLayer.Floor or IsoLayer.Shadow) && pack.Opaque(i,px-i.X,py-i.Y));
                        Require(top != body,"Desk front pierced by sitter"); deskPixels++;
                    }
                    // Conservative head ROI: upper 18 source rows of the actual head silhouette.
                    var headTop=Enumerable.Range(0,64).First(y=>Enumerable.Range(0,32).Any(x=>pack.Opaque(body,x,y)));
                    var total=0; var visible=0;
                    for(var y=headTop;y<Math.Min(64,headTop+18);y++) for(var x=0;x<32;x++)
                    {
                        if(!pack.Opaque(body,x,y))continue; total++;
                        var top=scene.Items.Last(i=>i.Layer is not (IsoLayer.Floor or IsoLayer.Shadow) && pack.Opaque(i,body.X+x-i.X,body.Y+y-i.Y));
                        if(top==body)visible++;
                    }
                    var fraction=(double)visible/total; var back=seat.Facing is "ne" or "nw";
                    Require(fraction >= (back?.85:.90),$"{design.Id}/{seat.Facing}/{action}/{frame}: head gate {fraction:P2}");
                    if(back)minimumBack=Math.Min(minimumBack,fraction);else minimumFront=Math.Min(minimumFront,fraction);
                    if(action=="typing") foreach(var h in clip.HandAnchors!)
                    {
                        var handPixels=0;
                        for(var hy=(int)h.Y-1;hy<=(int)h.Y+1;hy++)for(var hx=(int)h.X-1;hx<=(int)h.X+1;hx++)
                        {
                            if(!pack.Opaque(body,hx,hy))continue;handPixels++;
                            var top=scene.Items.Last(i=>i.Layer is not (IsoLayer.Floor or IsoLayer.Shadow) && pack.Opaque(i,body.X+hx-i.X,body.Y+hy-i.Y));
                            Require(top==body,$"{design.Id}/{seat.Facing}/{action}/{frame}: hand pixel ({hx},{hy}) hidden by {top.InstanceId}");
                        }
                        Require(handPixels>0,"No actual keyboard hand pixels");
                    }
                }
            }
        }
        Console.WriteLine($"  geometry oracle: {pack.Characters.Count} pilots, {frames} frames, {pixels} chair intersections, {deskPixels} desk intersections; head back={minimumBack:P2}, front={minimumFront:P2}; hands=100%");
    }

    internal static void IdlePerformance()
    {
        var pack=IsoPack.LoadDefault(); var office=new IsoOffice(pack,42,pack.Layout) { PetsEnabled=false };
        var now=DateTimeOffset.Parse("2026-10-04T00:00:00Z");
        var agents=Enumerable.Range(0,12).Select(n=>new AgentInfo("synthetic-"+n,Provider.Codex,null,false,"Synthetic",null,null,null,
            "synthetic",null,null,AgentState.Working,now,now,false)).ToArray();
        for(var t=0;t<1500;t++)office.Update(agents,now.AddSeconds(t*.05),.05);
        // Event-driven idle fast path; no animation deadline is crossed.
        var scene=office.Update(agents,now,0); var watch=new Stopwatch();
        for(var n=0;n<100;n++)office.Update(agents,now,0);
        var before=GC.GetAllocatedBytesForCurrentThread();watch.Start();
        for(var n=0;n<10000;n++)office.Update(agents,now,0);
        watch.Stop();var bytes=GC.GetAllocatedBytesForCurrentThread()-before;
        Require(bytes==0,"Idle update allocated "+bytes);
        Console.WriteLine($"  idle: {bytes} bytes / 10000 updates, {watch.Elapsed.TotalMilliseconds:F3} ms; {scene.Items.Count} draw items, 3/chair (+1)");
    }

    internal static void RejectInvertedOwnership()
    {
        var pack=IsoPack.LoadDefault();var seat=IsoContacts.Desk(3,3,0);var builder=IsoSeatedOcclusionChecks.Fixture(pack,0);
        var scene=builder.Build([new("actor",pack.Characters[0].Id,"Synthetic",new(seat.Approach.X,seat.Approach.Y),"nw","typing",0,"desk","seated")]);
        CheckPixels(pack,scene);
        foreach(var chairLast in new[]{false,true})
        {
            var wrong=scene with {Items=scene.Items.OrderBy(i=>i.Layer==IsoLayer.Character?(chairLast?0:2):1).ToArray()};
            var rejected=false;try{CheckPixels(pack,wrong);}catch(InvalidOperationException){rejected=true;}
            Require(rejected,"Oracle accepted blanket chair/actor ordering");
        }
        Console.WriteLine("  inverted ownership mutations: both directions rejected");
    }

    internal static void Walkers(IsoPack? supplied=null)
    {
        var pack=supplied??IsoPack.LoadDefault();var frames=0;var intersections=0;
        foreach(var rotation in new[]{0,90,180,270})
        {
            var seat=IsoContacts.Desk(3,3,rotation);var builder=IsoSeatedOcclusionChecks.Fixture(pack,rotation);
            foreach(var design in pack.Characters)foreach(var direction in new[]{-1,1})
            for(var step=0;step<4;step++)for(var frame=0;frame<4;frame++)
            {
                var position=new Position(seat.Approach.X+direction*(.75-step*.25),seat.Approach.Y);
                var scene=builder.Build([new("walker",design.Id,"Synthetic",position,seat.Facing,"walk",frame/8d)]);
                var body=scene.Items.Single(i=>i.Layer==IsoLayer.Character);
                var chair=scene.Items.Where(i=>i.InstanceId.StartsWith("chair/")&&i.Layer==IsoLayer.Furniture).ToArray();
                var expectedBody=new IsoDepthKey(IsoPass.Scene,position.X+position.Y,0,"walker","body").Encoded;
                Require(body.Depth==expectedBody,"Walker borrowed attached assembly rank");
                Require(chair.All(i=>i.SheetId=="iso-chair-parts"),"Empty chair retained previous sitter mask");
                for(var y=0;y<64;y++)for(var x=0;x<32;x++)
                {
                    if(!pack.Opaque(body,x,y))continue;
                    var px=body.X+x;var py=body.Y+y;
                    var part=chair.SingleOrDefault(i=>pack.Opaque(i,px-i.X,py-i.Y));if(part==null)continue;
                    var rank=part.InstanceId.Contains("__rear__")?-20:20;
                    var expectedChair=new IsoDepthKey(IsoPass.Scene,seat.Approach.X+seat.Approach.Y,rank,"chair",part.InstanceId.Split('/')[1]).Encoded;
                    var top=scene.Items.Last(i=>(i==body||chair.Contains(i))&&pack.Opaque(i,px-i.X,py-i.Y));
                    Require((top==body)==(expectedBody>expectedChair),"Walker/chair pixel violates ordinary ground-depth contract");intersections++;
                }
                frames++;
            }
        }
        Console.WriteLine($"  ordinary-depth walkers: {frames} in/out frames, {intersections} chair intersections");
    }

    internal static void ComparePerformance(string beforeDirectory,string afterDirectory)
    {
        static (double Ms,double CpuMs,long Bytes,int Items) Measure(IsoPack pack)
        {
            var builder=new IsoSceneBuilder(pack,pack.Layout);var actors=pack.Layout.Furniture.Where(f=>f.AssetId=="iso/desk").Select((f,n)=>
            {
                var seat=IsoContacts.Desk(f.X,f.Y,f.Rotation);return new IsoActorPose("synthetic-"+n,pack.Characters[n%pack.Characters.Count].Id,
                    "Synthetic",new(seat.Approach.X,seat.Approach.Y),seat.Facing,"typing",0,f.Id,"seated");
            }).ToArray();
            var scene=builder.Build(actors);for(var k=0;k<100;k++)builder.Build(actors);
            var samples=new double[15];var cpuSamples=new double[15];long bytes=0;
            using var process=Process.GetCurrentProcess();
            for(var run=0;run<samples.Length;run++)
            {
                var watch=new Stopwatch();var cpuStart=process.TotalProcessorTime;
                var start=GC.GetAllocatedBytesForCurrentThread();watch.Start();
                for(var k=0;k<500;k++)builder.Build(actors);
                watch.Stop();bytes=GC.GetAllocatedBytesForCurrentThread()-start;samples[run]=watch.Elapsed.TotalMilliseconds;
                cpuSamples[run]=(process.TotalProcessorTime-cpuStart).TotalMilliseconds;
            }
            Array.Sort(samples);Array.Sort(cpuSamples);return(samples[samples.Length/2],cpuSamples[cpuSamples.Length/2],bytes,scene.Items.Count);
        }
        var before=Measure(IsoPack.Load(beforeDirectory));
        if(Path.GetFullPath(beforeDirectory)==Path.GetFullPath(afterDirectory))
        {
            Console.WriteLine($"12-agent composition / 500 builds (15-run medians): wall {before.Ms:F3} ms, CPU {before.CpuMs:F3} ms, {before.Bytes} B, {before.Items} items");
            return;
        }
        var after=Measure(IsoPack.Load(afterDirectory));
        Console.WriteLine($"12-agent composition / 500 builds (15-run medians): before wall {before.Ms:F3} ms, CPU {before.CpuMs:F3} ms, {before.Bytes} B, {before.Items} items; after wall {after.Ms:F3} ms, CPU {after.CpuMs:F3} ms, {after.Bytes} B, {after.Items} items");
    }
}
