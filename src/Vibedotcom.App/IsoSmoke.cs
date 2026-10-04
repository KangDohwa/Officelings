using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using Vibedotcom.Core;
using Vibedotcom.Core.Iso;

namespace Vibedotcom.App;

internal static class IsoSmoke
{
    private static readonly DateTimeOffset Epoch=DateTimeOffset.Parse("2026-10-04T00:00:00Z");
    internal static IReadOnlyList<AgentInfo> TwelveAgents => Enumerable.Range(0,12).Select(i=>
        new AgentInfo($"iso-smoke:{i:00}",(Provider)(i%3),i>=3 ? $"iso-smoke:{i%3:00}" : null,i>=3,
            i<3 ? new[] {"Mira","Jules","Nova"}[i] : $"Worker {i-2}",i<3 ? "Team lead" : "Builder",
            "demo-model","C:/Demo/TinyWorkshop","Synthetic integration render",null,null,AgentState.Working,Epoch,Epoch,false)).ToArray();

    internal static int Run(string output,IIsoSheetSource sheets,Func<int,IIsoOffice> create,Settings settings,
        AppStorage storage,bool demo,bool smokeTags,string? home,string? state)
    {
        output=StateJson.OutputPath(output,home ?? storage.Home); Directory.CreateDirectory(output);
        var summary=new List<object>();
        void Capture(string name,IIsoOffice office,IsoScene scene,IReadOnlyList<AgentInfo> people,double seconds)
        {
            var view=new IsoView(office,sheets,Path.Combine(storage.Home,"brand")) { TagMode=settings.TagMode };
            view.Present(scene,people);
            foreach(var zoom in new[] {1,2,3})
            {
                view.SetZoom(zoom); var file=$"iso-{name}-{zoom}x.png";
                IsoApplication.Render(view,scene.Width*zoom,scene.Height*zoom,Path.Combine(output,file));
                summary.Add(new {file,seconds,zoom,agents=people.Count,visible=scene.Labels.Count,scene.Revision,
                    walking=scene.Items.Count(i=>i.Layer==IsoLayer.Character && i.Clip.StartsWith("walk/")),
                    typing=scene.Items.Count(i=>i.Layer==IsoLayer.Character && i.Clip.StartsWith("typing/")),
                    cat=scene.Items.Any(i=>i.Layer==IsoLayer.Pet),nextChangeSeconds=double.IsFinite(office.NextChangeSeconds) ? office.NextChangeSeconds : (double?)null});
            }
            if(name=="twelve-working")
            {
                view.SetZoom(2); view.TransparentBackground=true;
                IsoApplication.Render(view,scene.Width*2,scene.Height*2,Path.Combine(output,"iso-transparent.png"));
                view.SetZoom(3); IsoApplication.Render(view,520,340,Path.Combine(output,"iso-cropped.png"));
                view.PanBy(new(70,40)); IsoApplication.Render(view,520,340,Path.Combine(output,"iso-panned.png"));
                view.TransparentBackground=false; view.SetZoom(0);
                IsoApplication.Render(view,720,480,Path.Combine(output,"iso-auto-fit.png"));
                foreach(var mode in smokeTags ? new[] {"hover","icon","always"} : new[] {settings.TagMode})
                {
                    view.TagMode=mode; view.SetZoom(2);
                    IsoApplication.Render(view,scene.Width*2,scene.Height*2,Path.Combine(output,$"iso-tags-{mode}.png"));
                }
            }
        }
        void Scenario(string name,Func<double,IReadOnlyList<AgentInfo>> peopleAt,double[] moments,double start=0)
        {
            var office=create(0);
            if(office is IsoOffice engine) engine.PetsEnabled=settings.Pets=="on";
            var scene=office.Update(peopleAt(start),Epoch.AddSeconds(start),0); var elapsed=start;
            foreach(var moment in moments)
            {
                while(elapsed+1e-9<moment)
                {
                    var step=Math.Min(.05,moment-elapsed); elapsed+=step;
                    scene=office.Update(peopleAt(elapsed),Epoch.AddSeconds(elapsed),step);
                }
                Capture(moments.Length==1 ? name : $"{name}-{moment:00.0}s",office,scene,peopleAt(moment),moment);
            }
        }
        if(demo && state==null)
        {
            Scenario("empty",_=>Array.Empty<AgentInfo>(),[0]);
            Scenario("three",_=>DemoScenario.Agents(Epoch,0).Where(a=>!a.IsSubagent).ToArray(),[12]);
            var twelve=TwelveAgents;
            Scenario("arrival",_=>twelve,[.5,2]);
            Scenario("twelve-working",_=>twelve,[20]);
            Scenario("departure",t=>t<20 ? twelve : twelve.Select(a=>a with { State=AgentState.Gone }).ToArray(),[20.7,22,27]);
            Scenario("demo",t=>DemoScenario.Agents(Epoch,t),[0,8,12,24,42,55],-18);
            Scenario("cat",_=>Array.Empty<AgentInfo>(),[9,35]);
        }
        else
        {
            IReadOnlyList<AgentInfo> people; var privacy=PrivacyModes.Parse(settings.PrivacyMode ?? "off");
            if(state!=null)
            {
                if(new FileInfo(state).Length>4*1024*1024) throw new InvalidDataException("State exceeds 4 MiB.");
                using var input=new FileStream(state,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
                var snapshot=JsonSerializer.Deserialize<StateSnapshot>(input,StateJson.Options) ?? throw new InvalidDataException("Empty state.");
                people=Program.PrivateSnapshot(snapshot,privacy).Agents.Select(a=>a.Agent).ToArray();
            }
            else
            {
                using var tracker=new AgentTracker(new() {Home=home ?? storage.Home,PrivacyMode=privacy});
                people=tracker.Poll(DateTimeOffset.UtcNow);
            }
            Scenario("live",_=>people,[20]);
        }
        File.WriteAllText(Path.Combine(output,"iso-smoke.json"),JsonSerializer.Serialize(new {scene="iso-engine",simulation=true,renders=summary},StateJson.Options));
        File.WriteAllText(Path.Combine(output,"smoke.txt"),$"Offscreen ISO engine + embedded pack + WPF OK; renders={summary.Count}; 1x/2x/3x; synthetic demo={demo}; no visible window\n");
        return 0;
    }
}
