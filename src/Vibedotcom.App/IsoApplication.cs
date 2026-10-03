using System;
using System.IO;
using System.Reflection;
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
        var artRoot=assets ?? SourceManifestIsoSheets.FindSourceDirectory();
        var source=new SourceManifestIsoSheets(artRoot);
        if (smoke!=null) return Smoke(smoke,source,artRoot,settings.TagMode,storage,smokeTags);
        if (qa && (!double.IsFinite(quitAfter) || quitAfter<=0 || quitAfter>60))
            throw new ArgumentException("--qa requires --quit-after in (0,60].");
        var office=CreateOffice(demo,source,artRoot,out var preview);
        new IsoWindow(office,source,settings,demo,qa,quitAfter,storage,home,preview).Show();
        return app.Run();
    }

    internal static IIsoOffice CreateOffice(bool demo,SourceManifestIsoSheets source,string artRoot,out bool preview)
    {
        var assembly=typeof(IIsoOffice).Assembly;
        var officeType=assembly.GetType("Vibedotcom.Core.Iso.IsoOffice");
        var packType=assembly.GetType("Vibedotcom.Core.Iso.IsoPack");
        if (officeType!=null && packType!=null)
        {
            var loader=packType.GetMethod("LoadDefault",BindingFlags.Public|BindingFlags.Static,null,Type.EmptyTypes,null)
                ?? throw new InvalidOperationException("IsoPack.LoadDefault() is required by the integration contract.");
            var pack=loader.Invoke(null,null)!;
            preview=false;
            return (IIsoOffice)(Activator.CreateInstance(officeType,pack,42) ?? throw new InvalidOperationException("Cannot construct IsoOffice."));
        }
        if (!demo) throw new InvalidOperationException("ISO engine is not integrated yet. --view iso --demo uses an explicitly handmade art preview.");
        preview=true; return new IsoPreviewOffice(source,artRoot);
    }

    internal static int Smoke(string output,SourceManifestIsoSheets source,string artRoot,string tagMode,AppStorage storage,bool smokeTags)
    {
        output=StateJson.OutputPath(output,storage.Home); Directory.CreateDirectory(output);
        var epoch=DateTimeOffset.Parse("2026-10-04T00:00:00Z");
        var summary=new System.Collections.Generic.List<object>();
        foreach (var count in new[] {0,12})
        {
            var agents=IsoPreviewOffice.Agents(epoch,count);
            foreach (var phase in new[] {0,1,2,3})
            {
                var office=new IsoPreviewOffice(source,artRoot,phase);
                var view=new IsoView(office,source,Path.Combine(storage.Home,"brand")) { TagMode=tagMode };
                view.Update(agents,epoch.AddSeconds(phase),0);
                foreach (var zoom in new[] {1,2,3})
                {
                    view.SetZoom(zoom);
                    var file=$"iso-{count}-phase-{phase}-{zoom}x.png";
                    Render(view,672*zoom,416*zoom,Path.Combine(output,file));
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
        File.WriteAllText(Path.Combine(output,"iso-smoke.json"),JsonSerializer.Serialize(new {scene="handmade-source-art-preview",simulation=false,renders=summary},StateJson.Options));
        File.WriteAllText(Path.Combine(output,"smoke.txt"),"Offscreen ISO WPF source-art preview OK; empty and 12 synthetic agents; 1x/2x/3x; four authored phases; no simulation, no visible window, logBytesRead=0\n");
        return 0;
    }
    internal static void Render(FrameworkElement view,int width,int height,string file)
    {
        var bitmap=Program.RenderBitmap(view,width,height);
        var png=new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream=File.Create(file); png.Save(stream);
    }
}