using System;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media.Imaging;
using Vibedotcom.Core;
using Vibedotcom.Core.Iso;

namespace Vibedotcom.App;

internal static class IsoApplication
{
    internal static int Run(Application app,bool demo,Settings? initial,double quitAfter,bool qa,AppStorage storage,
        string? home,string? assets,string? smoke,bool smokeTags,string? layout,string? state,string? profile)
    {
        var settings=(initial ?? (storage.Isolated ? new Settings { View="iso" } : Settings.Read(storage.SettingsPath))).Normalize();
        if (state!=null || layout!=null || profile!=null)
            throw new ArgumentException("ISO does not consume flat layout/state/profile options. Its engine owns the ISO layout; use --view flat for those options.");
        if (qa && (!double.IsFinite(quitAfter) || quitAfter<=0 || quitAfter>60))
            throw new ArgumentException("--qa requires --quit-after in (0,60].");
        var runtime=Prepare(assets,demo || smoke!=null);
        if (smoke!=null) return Smoke(smoke,runtime.Sheets,runtime.Create,runtime.Preview,settings.TagMode,storage,smokeTags);
        new IsoWindow(runtime.Create(0),runtime.Sheets,settings,demo,qa,quitAfter,storage,home,runtime.Preview).Show();
        return app.Run();
    }

    internal static (IIsoSheetSource Sheets,Func<int,IIsoOffice> Create,bool Preview) Prepare(string? assets,bool allowPreview)
    {
        var pack=assets==null ? IsoPack.LoadDefault() : IsoPack.Load(assets);
        return (new PackIsoSheetSource(pack),_=>new IsoOffice(pack,42),false);
    }
    internal static int Smoke(string output,IIsoSheetSource source,Func<int,IIsoOffice> create,bool preview,string tagMode,AppStorage storage,bool smokeTags)
    {
        output=StateJson.OutputPath(output,storage.Home); Directory.CreateDirectory(output);
        var epoch=DateTimeOffset.Parse("2026-10-04T00:00:00Z");
        var summary=new System.Collections.Generic.List<object>();
        foreach (var count in new[] {0,12})
        {
            var agents=IsoPreviewOffice.Agents(epoch,count);
            foreach (var phase in new[] {0,1,2,3})
            {
                var office=create(phase);
                var view=new IsoView(office,source,Path.Combine(storage.Home,"brand")) { TagMode=tagMode };
                if (preview) view.Update(agents,epoch.AddSeconds(phase),0);
                else for (var frame=0;frame<=600+phase*120;frame++) view.Update(agents,epoch.AddSeconds(frame*.1),.1);
                foreach (var zoom in new[] {1,2,3})
                {
                    view.SetZoom(zoom);
                    var file=$"iso-{count}-phase-{phase}-{zoom}x.png";
                    Render(view,view.Scene.Width*zoom,view.Scene.Height*zoom,Path.Combine(output,file));
                    summary.Add(new { file,count,phase,zoom,sceneRenders=view.SceneRenderCount,staticBuilds=view.StaticBuildCount });
                }
                if (phase==0 && count==12)
                {
                    view.TransparentBackground=true; view.SetZoom(2);
                    Render(view,1384,872,Path.Combine(output,"iso-transparent.png"));
                    view.SetZoom(3); Render(view,520,340,Path.Combine(output,"iso-cropped.png"));
                    view.PanBy(new(70,40)); Render(view,520,340,Path.Combine(output,"iso-panned.png"));
                    view.SetZoom(0); Render(view,400,300,Path.Combine(output,"iso-auto-fit-small.png"));
                    foreach(var mode in smokeTags ? new[] {"hover","icon","always"} : new[] {tagMode})
                    {
                        view.TagMode=mode; view.SetZoom(2); view.TransparentBackground=false;
                        Render(view,1344,832,Path.Combine(output,$"iso-tags-{mode}.png"));
                    }
                }
            }
        }
        File.WriteAllText(Path.Combine(output,"iso-smoke.json"),JsonSerializer.Serialize(new {scene=preview ? "handmade-source-art-preview" : "iso-engine",simulation=!preview,renders=summary},StateJson.Options));
        File.WriteAllText(Path.Combine(output,"smoke.txt"),preview ? "Offscreen ISO WPF source-art preview OK; empty and 12 synthetic agents; 1x/2x/3x; four authored phases; no simulation, no visible window, logBytesRead=0\n" : "Offscreen ISO engine WPF OK; empty and 12 synthetic agents; 1x/2x/3x; no visible window, logBytesRead=0\n");
        return 0;
    }
    internal static void Render(FrameworkElement view,int width,int height,string file)
    {
        var bitmap=Program.RenderBitmap(view,width,height);
        var png=new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream=File.Create(file); png.Save(stream);
    }
}
