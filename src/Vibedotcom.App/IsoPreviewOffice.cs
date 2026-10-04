using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Vibedotcom.Core;
using Vibedotcom.Core.Iso;

namespace Vibedotcom.App;

// A handmade picture fixture, not a simulation. Its four supplied phases expose
// every desk orientation, the three rigs and authored walking/cat cells to QA.
// It is an explicit demo-only fallback until IsoOffice is integrated.
internal sealed class IsoPreviewOffice : IIsoOffice
{
    private readonly SourceManifestIsoSheets sheets;
    private readonly string artRoot;
    private readonly int phase;
    private IsoScene? scene;
    private IReadOnlyList<AgentInfo>? previousAgents;
    internal IsoPreviewOffice(SourceManifestIsoSheets sheets,string artRoot,int phase=0)
    { this.sheets=sheets; this.artRoot=artRoot; this.phase=phase; }
    public double NextChangeSeconds => double.PositiveInfinity;
    public IsoScene Update(IReadOnlyList<AgentInfo> agents,DateTimeOffset now,double deltaSeconds)
    {
        if (scene!=null && ReferenceEquals(agents,previousAgents)) return scene;
        previousAgents=agents;
        return scene=Build(sheets,artRoot,agents,phase,(scene?.Revision ?? 0)+1);
    }
    public IsoHit? HitTest(int sceneX,int sceneY)
    {
        if (scene==null) return null;
        for (var index=scene.Items.Count-1;index>=0;index--)
        {
            var item=scene.Items[index]; if (item.HitTargetId==null || item.Alpha==0) continue;
            var cell=sheets.GetCell(item.SheetId,item.Clip,item.Frame);
            var x=sceneX-item.X; var y=sceneY-item.Y;
            if (x<0 || y<0 || x>=cell.Width || y>=cell.Height) continue;
            var pixel=new byte[4]; cell.Image.CopyPixels(new System.Windows.Int32Rect(x,y,1,1),pixel,4,0);
            if (pixel[3]!=0) return new(item.HitTargetId,item.Layer==IsoLayer.Character ? "agent" : "furniture");
        }
        return null;
    }
    internal static IReadOnlyList<AgentInfo> Agents(DateTimeOffset epoch,int count=12) => Enumerable.Range(0,count)
        .Select(i=>new AgentInfo($"iso-demo:{i:00}",(Provider)(i%3),i>=3 ? $"iso-demo:{i%3:00}" : null,i>=3,
            i<3 ? new[] { "Dev","Punk","Prof" }[i] : $"Worker {i+1}",i<3 ? "Lead" : "Worker","demo-model","C:/Demo/Office",
            "Fabricated ISO picture fixture",null,null,i<4 ? AgentState.Working : AgentState.Idle,epoch,epoch,false)).ToArray();

    internal static IsoScene Build(SourceManifestIsoSheets sheets,string artRoot,IReadOnlyList<AgentInfo> agents,int phase,long revision)
    {
        const int ox=336,oy=80;
        var ground=new List<IsoDrawItem>(); var walls=new List<IsoDrawItem>();
        var shadows=new List<IsoDrawItem>(); var objects=new List<(IsoDrawItem Item,double Depth,int Tie)>();
        var labels=new List<IsoLabel>();
        static (int X,int Y) Project(double i,double j) => ((int)Math.Floor(32*(i-j)+.5)+ox,(int)Math.Floor(16*(i+j)+.5)+oy);
        IsoDrawItem Place(string id,IsoLayer layer,string sheet,string clip,int frame,double i,double j,long depth=0,string? hit=null,int offsetX=0,int offsetY=0)
        {
            var source=sheets.Clip(sheet,clip); var p=Project(i,j);
            return new(id,layer,sheet,source.Key,Math.Max(0,frame)%source.Frames,p.X-source.AnchorX+offsetX,p.Y-source.AnchorY+offsetY,depth,hit);
        }
        for (var i=0;i<10;i++) for (var j=0;j<10;j++)
        {
            var checker=i>=8 && j<=5;
            ground.Add(Place($"floor:{i}:{j}",IsoLayer.Floor,"iso-env-floors",checker ? ((i+j)%2==0 ? "checker-dark" : "checker-light") : "wood-i",0,i,j));
            if (!checker && i==7 && j<=5) ground.Add(Place($"floor-edge:{i}:{j}",IsoLayer.Floor,"iso-env-floors","wood-checker-edge-pos-i",0,i,j));
            if (!checker && i>=8 && j==6) ground.Add(Place($"floor-edge:{i}:{j}",IsoLayer.Floor,"iso-env-floors","wood-checker-edge-neg-j",0,i,j));
        }
        for (var n=0;n<10;n++)
        {
            walls.Add(Place($"wall-l:{n}",IsoLayer.Wall,"iso-env-walls","plain-l",0,-.5,n-.5));
            walls.Add(Place($"wall-r:{n}",IsoLayer.Wall,"iso-env-walls","plain-r",0,n-.5,-.5));
        }
        walls.Add(Place("window-glass",IsoLayer.Wall,"iso-env-walls","window-glass-r",0,3.5,-.5));
        walls.Add(Place("window-frame",IsoLayer.Wall,"iso-env-walls","window-frame-r",0,3.5,-.5));
        walls.Add(Place("door-leaf",IsoLayer.Wall,"iso-env-walls","door-leaf-closed-l",0,-.5,3.5));
        walls.Add(Place("door-frame",IsoLayer.Wall,"iso-env-walls","door-frame-l",0,-.5,3.5));
        walls.Add(Place("wall-corner",IsoLayer.Wall,"iso-env-wall-corner","inner-rear-corner",0,-.5,-.5));

        JsonElement Variant(string relative,string direction)
        {
            using var doc=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(artRoot,relative)));
            return doc.RootElement.GetProperty("furniture")[0].GetProperty("variants").EnumerateArray()
                .First(v=>v.GetProperty("orientation").GetString()==direction && v.GetProperty("state").GetString()=="default").Clone();
        }
        void Furniture(string id,string relative,double i,double j,string direction,double? seatedDepth=null)
        {
            var variant=Variant(relative,direction);
            if (variant.TryGetProperty("shadow",out var shadow))
                shadows.Add(Place(id+":shadow",IsoLayer.Shadow,shadow.GetProperty("sheetId").GetString()!,
                    shadow.GetProperty("animation").GetString()!+"/"+shadow.GetProperty("direction").GetString()!,0,i,j));
            foreach (var part in variant.GetProperty("parts").EnumerateArray())
            {
                var source=sheets.Clip(part.GetProperty("sheetId").GetString()!,part.GetProperty("animation").GetString()!+"/"+part.GetProperty("direction").GetString()!);
                var anchor=part.GetProperty("depthAnchor");
                var depth=i+j+anchor.GetProperty("i").GetDouble()+anchor.GetProperty("j").GetDouble();
                if (seatedDepth!=null && part.TryGetProperty("seatGroupId",out _)) depth=seatedDepth.Value;
                var tie=part.GetProperty("isoZOrder").GetInt32();
                var item=Place(id+":"+part.GetProperty("partId").GetString(),IsoLayer.Furniture,source.SheetId,source.Key,0,i,j,(long)Math.Round(depth*1000),id);
                if (variant.TryGetProperty("renderPass",out var pass) && pass.GetString()=="floor-props") ground.Add(item with { Layer=IsoLayer.Floor });
                else objects.Add((item,depth,tie));
            }
        }
        string[] directions=["nw","ne","sw","se"];
        (int I,int J)[] desks=[(2,2),(5,2),(4,5),(7,5)];
        string[] rigs=["dev-hoodie-v2-normal","punk-girl-normal","professor-normal"];
        for (var index=0;index<4;index++)
        {
            var location=desks[index]; var direction=directions[index];
            var variant=Variant("furniture-v2/workstations/desk.iso.json",direction);
            var profile=variant.GetProperty("seatProfiles")[0];
            var seat=profile.GetProperty("seatGround");
            var si=location.I+seat.GetProperty("i").GetDouble(); var sj=location.J+seat.GetProperty("j").GetDouble();
            var facing=profile.GetProperty("isoFacing").GetString()!;
            var depth=si+sj;
            Furniture($"desk:{index}","furniture-v2/workstations/desk.iso.json",location.I,location.J,direction,depth);
            var chair=Variant("furniture-v2/workstations/chair.iso.json",facing);
            // Chair contact is 12px above its ground anchor. The desk profile's
            // authored contact may differ from its navigation approach.
            var contact=profile.GetProperty("contactOffset");
            var projected=Project(location.I,location.J);
            var cx=contact.GetProperty("x").GetInt32(); var cy=contact.GetProperty("y").GetInt32()+12;
            var ci=location.I+cx/64d+cy/32d; var cj=location.J+cy/32d-cx/64d;
            Furniture($"chair:{index}","furniture-v2/workstations/chair.iso.json",ci,cj,facing,depth);
            if (index>=agents.Count) continue;
            var agent=agents[index]; var rig=rigs[SpriteAtlas.Hash(agent.Id)%3];
            var source=sheets.Clip(rig,"seated-typing-front/"+facing);
            var item=new IsoDrawItem("agent:"+agent.Id,IsoLayer.Character,rig,source.Key,phase%4,
                projected.X+contact.GetProperty("x").GetInt32()-16,projected.Y+contact.GetProperty("y").GetInt32()-44,
                (long)Math.Round(depth*1000),agent.Id);
            objects.Add((item,depth,0)); labels.Add(new(agent.Id,agent.Name,item.X+16,item.Y+19));
        }
        Furniture("sofa","furniture-v2/lounge/sofa.iso.json",6,8,"ne");
        Furniture("plant","furniture-v2/utility/plant.iso.json",0,6,"se");
        Furniture("shelf","furniture-v2/utility/shelf.iso.json",0,0,"se");
        Furniture("tv","furniture-v2/lounge/tv-cabinet.iso.json",0,8,"se");
        Furniture("coffee","furniture-v2/utility/coffee-machine.iso.json",9,1,"nw");
        Furniture("fridge","furniture-v2/utility/fridge.iso.json",9,3,"nw");
        Furniture("water","furniture-v2/utility/water-cooler.iso.json",9,5,"nw");
        Furniture("rug","furniture-v2/lounge/rug.iso.json",6,7,"sw");
        Furniture("table","furniture-v2/lounge/side-table.iso.json",3,8,"se");
        Furniture("lamp","furniture-v2/utility/floor-lamp.iso.json",4,8,"se");
        (double I,double J)[] walkers=[(2,5),(3,3),(6,3),(7,4),(2,7),(4,7),(8,6),(8,7)];
        for (var index=4;index<Math.Min(12,agents.Count);index++)
        {
            var point=walkers[index-4]; var agent=agents[index]; var rig=rigs[SpriteAtlas.Hash(agent.Id)%3];
            var facing=new[] { "se","sw","ne","nw" }[(index-4)%4];
            shadows.Add(Place("contact:"+agent.Id,IsoLayer.Shadow,IsoPreviewContactShadow.SheetId,"contact",0,point.I,point.J));
            var item=Place("agent:"+agent.Id,IsoLayer.Character,rig,"walk/"+facing,phase%4,point.I,point.J,(long)Math.Round((point.I+point.J)*1000),agent.Id);
            objects.Add((item,point.I+point.J,0)); labels.Add(new(agent.Id,agent.Name,item.X+16,item.Y+19));
        }
        var catClip=phase==3 ? "sleep" : "walk/se";
        shadows.Add(Place("cat-shadow",IsoLayer.Shadow,"iso-cat-shadow",catClip,phase%4,5,8));
        objects.Add((Place("cat",IsoLayer.Pet,"iso-cat-normal",catClip,phase%4,5,8,13000),13,0));
        var ordered=ground.Concat(shadows).Concat(walls).Concat(objects.OrderBy(o=>o.Depth).ThenBy(o=>o.Tie)
            .ThenBy(o=>o.Item.InstanceId,StringComparer.Ordinal).Select(o=>o.Item)).ToArray();
        return new(672,416,ordered,labels.ToArray(),revision);
    }
}
