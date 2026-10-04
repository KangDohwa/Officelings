using System;
using System.IO;
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
        if (layout!=null || profile!=null)
            throw new ArgumentException("ISO owns its layout. --layout and --profile-idle currently require --view flat.");
        if (qa && (!double.IsFinite(quitAfter) || quitAfter<=0 || quitAfter>60))
            throw new ArgumentException("--qa requires --quit-after in (0,60].");
        var runtime=Prepare(assets,demo || smoke!=null,storage);
        if (smoke!=null)
        {
            if (demo)
            {
                IsoEditorSmoke.Render(Path.Combine(StateJson.OutputPath(smoke,storage.Home),"editor-ui-fakes"),runtime.Session?.Pack ??
                    (runtime.Sheets as PackIsoSheetSource)?.RuntimePack ?? throw new InvalidOperationException("Editor smoke requires a runtime pack."));
                if (Environment.GetEnvironmentVariable("VIBEDOTCOM_ISO_EDITOR_SMOKE")=="1") return 0;
            }
            return IsoSmoke.Run(smoke,runtime.Sheets,runtime.Create,settings,storage,demo,smokeTags,home,state);
        }
        new IsoWindow(runtime.Create(0),runtime.Sheets,settings,demo,qa,quitAfter,storage,home,runtime.Preview,editorSession:runtime.Session).Show();
        return app.Run();
    }

    internal static (IIsoSheetSource Sheets,Func<int,IIsoOffice> Create,bool Preview,IsoEditorSession? Session) Prepare(string? assets,bool allowPreview,
        AppStorage? storage=null,IsoEditorSession? retained=null)
    {
        var pack=assets==null ? IsoPack.LoadDefault() : IsoPack.Load(assets);
        var session=retained ?? (storage==null ? null : IsoEditorComposition.Load(pack,storage));
        var resolved=session?.Pack ?? pack; var layout=session?.LastValidLayout ?? pack.Layout;
        return (new PackIsoSheetSource(resolved),seed=>new IsoOffice(resolved,42+seed,layout),false,session);
    }
    internal static void Render(FrameworkElement view,int width,int height,string file)
    {
        var bitmap=Program.RenderBitmap(view,width,height);
        var png=new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream=File.Create(file); png.Save(stream);
    }
}
