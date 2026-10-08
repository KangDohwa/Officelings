using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using System.Windows.Threading;
using Officelings.Core;
using Officelings.Core.Iso;
using Officelings.Core.Iso.Editing;

namespace Officelings.App;

// Real services only. Every root is synthetic and all source fixtures belong to this worktree.
internal static class IsoEditorSmoke
{
    internal static readonly DateTimeOffset Epoch=DateTimeOffset.Parse("2026-10-04T00:00:00Z");
    internal static void Complete(Task task)
    {
        var frame=new DispatcherFrame();
        var dispatcher=Dispatcher.CurrentDispatcher;
        _=task.ContinueWith(_=>dispatcher.BeginInvoke(()=>frame.Continue=false),TaskScheduler.Default);
        Dispatcher.PushFrame(frame); task.GetAwaiter().GetResult();
    }
    internal static string Fixture(string relative)
    {
        var directory=new DirectoryInfo(AppContext.BaseDirectory);
        while (directory!=null)
        {
            var file=Path.Combine(directory.FullName,"tests","fixtures","iso-editor",relative);
            if (File.Exists(file)) return file;
            directory=directory.Parent;
        }
        throw new FileNotFoundException("Synthetic editor fixture is not available.");
    }
    internal static IsoView View(IsoEditorSession session)
    {
        var office=(IsoOffice)session.Runtime!;
        var view=new IsoView(office,new PackIsoSheetSource(session.Pack),Path.Combine(session.Paths.RootDirectory,"brand"));
        view.Present(office.Update([],Epoch,0)); session.Enter(new Settings()); view.AttachEditor(session); return view;
    }
    internal static string Desk(IsoEditorSession session,int angle,bool mounted=true)
    {
        var placed=session.Execute(new IsoPlaceCommand("iso/desk",new(4,4),angle));
        IsoEditorAcceptance.Require(placed.Succeeded,"Cannot place selected desk."); var id=placed.PrimaryTargetId!;
        var contact=IsoContacts.Desk(4,4,angle);
        var chair=session.Execute(new IsoPlaceCommand("iso/chair",contact.Approach,IsoGeometry.Rotation(contact.Facing))).PrimaryTargetId!;
        string? computer=null;
        if (mounted)
        {
            IsoEditorAcceptance.Require(session.Execute(new IsoPlaceCommand("iso/computer",new(4,4),angle,HostDeskId:id)).Succeeded,"Cannot mount authored computer.");
            computer=session.Editor.State.Layout.Furniture.Single(f=>f.AssetId=="iso/computer" && f.DeskId==id).Id;
        }
        IsoEditorAcceptance.Require(session.Execute(new IsoSetSeatCommand(id,chair,computer)).Succeeded,"Cannot designate authored rig.");
        session.Select(id); return id;
    }
    internal static IReadOnlyList<string> Render(string output,IsoEditorSession initial)
    {
        Directory.CreateDirectory(output); var bundled=initial.BuiltInPack;
        Application.Current.ShutdownMode=ShutdownMode.OnExplicitShutdown;
        var fixture=Fixture("objects/valid/test-crate/test-crate.iso.json");
        var groups=new Dictionary<string,int>
        {
            ["commands"]=IsoEditorAcceptance.VerifyCommands(bundled,Path.Combine(output,"checks","commands")),
            ["persistence"]=IsoEditorAcceptance.VerifyPersistence(bundled,Path.Combine(output,"checks","persistence")),
            ["objects"]=IsoEditorAcceptance.VerifyObjects(bundled,Path.Combine(output,"checks","objects"),fixture),
            ["runtime"]=IsoEditorAcceptance.VerifyRuntime(bundled,Path.Combine(output,"checks","runtime"))
        };
        var files=new List<string>(); var records=new List<object>();
        for (var rule=1;rule<=7;rule++) IsoEditorUiAcceptance.Run(rule,bundled,Path.Combine(output,"ui",rule.ToString()));
        IsoEditorSession Create(string name,bool empty=true) => IsoEditorAcceptance.Create(bundled,Path.Combine(output,"roots",name),empty);
        void Capture(string name,IsoEditorSession session)
        {
            var view=View(session);
            var bounds=view.EditorRenderBounds();
            foreach (var scale in new[] {1,2,3})
            {
                view.SetZoom(scale); var file=Path.Combine(output,$"{name}-{scale}x.png");
                IsoApplication.Render(view,(int)bounds.Width*scale,(int)bounds.Height*scale,file); files.Add(file);
                records.Add(new {file=Path.GetFileName(file),scale,revision=session.Editor.State.Revision,camera=view.EditorCamera,
                    sha256=Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file)))});
            }
            if (name is "default-twelve" or "grouped-validation" or "import-success" or "import-failure" or "designated-seat" or "cleared-seat")
            {
                using var panel=new IsoEditorPanel(session,new PackIsoSheetSource(session.Pack));
                var file=Path.Combine(output,name+"-controls.png"); IsoApplication.Render(panel,520,900,file); files.Add(file);
                if (name=="grouped-validation")
                {
                    panel.ScrollToBottom(); panel.UpdateLayout(); var bottom=Path.Combine(output,name+"-controls-bottom.png");
                    IsoApplication.Render(panel,520,900,bottom); files.Add(bottom);
                }
                if (name=="default-twelve")
                {
                    using var scaled=new IsoEditorPanel(session,new PackIsoSheetSource(session.Pack),2);
                    foreach (var bottom in new[] {false,true})
                    {
                        if (bottom) scaled.ScrollToBottom();
                        var text=Path.Combine(output,bottom ? "controls-text-200-bottom.png" : "controls-text-200.png");
                        IsoApplication.Render(scaled,520,900,text); files.Add(text);
                    }
                }
            }
            view.DetachEditor();
        }
        Capture("default-twelve",Create("default",false));
        foreach (var angle in new[] {0,90,180,270})
        {
            var session=Create("desk-"+angle); var id=Desk(session,angle);
            VerifyMounted(session.Pack,session.Editor.State.Layout,id);
            Capture("mounted-desk-"+angle,session);
            session.PreviewCommand(new IsoMoveCommand(id,new(4,4))); Capture("selected-desk-"+angle,session);
        }
        var valid=Create("valid-ghost"); valid.PreviewCommand(new IsoPlaceCommand("iso/plant",new(4,4))); Capture("valid-ghost",valid);
        var invalid=Create("invalid-ghost"); Desk(invalid,0); invalid.Select(null);
        invalid.PreviewCommand(new IsoPlaceCommand("iso/desk",new(4,4)));
        IsoEditorAcceptance.Require(invalid.Preview?.CanCommit==false,"Overlap ghost is not blocked."); Capture("invalid-ghost",invalid);
        foreach (var asset in new[] {"iso/chair","iso/computer"})
        {
            var session=Create(asset[4..]); session.Execute(new IsoPlaceCommand(asset,new(4,4)));
            IsoEditorAcceptance.Require(session.Editor.State.Layout.WorkSeats!.Count==0,"Lone prop acquired a seat.");
            session.Select(null); Capture("lone-"+asset[4..],session);
        }
        var designated=Create("designated"); var desk=Desk(designated,0); Capture("designated-seat",designated);
        designated.Execute(new IsoClearSeatCommand(desk)); Capture("cleared-seat",designated);
        var duplicate=Create("duplicate"); desk=Desk(duplicate,0);
        duplicate.PreviewCommand(new IsoDuplicateCommand(desk,new(7,7))); Capture("linked-duplicate",duplicate);
        var wall=Create("wall"); var decor=wall.Editor.WallPalette.First(d=>d.ModuleIds.Length==2);
        var host=wall.Editor.State.Layout.WallSegments!.Where(w=>w.HostId==null && w.Face==decor.Face)
            .First(w=>wall.Editor.Preview(new IsoPlaceWallCommand(decor.AssetId,w.Id)).CanCommit);
        IsoEditorAcceptance.Require(wall.Execute(new IsoPlaceWallCommand(decor.AssetId,host.Id)).Succeeded,"Two-edge wall placement failed.");
        Capture("two-edge-wall",wall);
        var draft=Create("draft",false); draft.Execute(new IsoPlaceCommand("iso/plant",draft.Editor.State.Layout.Door)); Capture("invalid-draft",draft);
        var grouped=Create("grouped",false); desk=grouped.Editor.State.Layout.Furniture.First(f=>f.AssetId=="iso/desk").Id;
        grouped.PreviewCommand(new IsoMoveCommand(desk,new(-1,-1))); grouped.Report(grouped.Preview!.Issues); Capture("grouped-validation",grouped);
        var previous=SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        try
        {
            foreach (var success in new[] {true,false})
            {
                var name=success ? "import-success" : "import-failure"; var session=Create(name);
                var dialog=new IsoEditorImportDialog(session);
                Complete(dialog.ValidateAsync(success ? fixture : Fixture("objects/invalid/palette/test-crate.iso.json")));
                IsoEditorAcceptance.Require(dialog.Validated==success,"Real import dialog validation changed.");
                if (success)
                {
                    var importing=dialog.ImportAsync(); Complete(importing);
                    IsoEditorAcceptance.Require(importing.Result?.Succeeded==true,"Real dialog import failed.");
                }
                var content=(FrameworkElement)dialog.Content;
                var native=Program.RenderBitmap(content,540,560);
                foreach (var scale in new[] {1,2,3})
                {
                    BitmapSource bitmap=native;
                    if (scale!=1)
                    {
                        var scaled=new RenderTargetBitmap(540*scale,560*scale,96*scale,96*scale,PixelFormats.Pbgra32);
                        scaled.Render(content); scaled.Freeze(); bitmap=scaled;
                    }
                    var file=Path.Combine(output,name+$"-dialog-{scale}x.png");
                    var encoder=new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using (var stream=File.Create(file)) encoder.Save(stream); files.Add(file);
                }
                Capture(name,session); dialog.Close();
            }
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
        var measurements=IsoEditorMeasurements.Run(bundled,Path.Combine(output,"measurements"),fixture);
        ContactSheets(output,files);
        File.WriteAllText(Path.Combine(output,"iso-editor-smoke.json"),JsonSerializer.Serialize(new {seed=42,epoch=Epoch,realServices=true,groups,uiGroups=7,mountedPixelRotations=4,measurements,
            packHashes=initial.Pack.Images.OrderBy(p=>p.Key,StringComparer.Ordinal).Select(p=>new {id=p.Key,sha256=Convert.ToHexStringLower(SHA256.HashData(p.Value))}),renders=records,
            files=files.Select(f=>new {file=Path.GetFileName(f),sha256=Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(f)))})},StateJson.Options));
        IsoEditorAcceptance.Require(measurements.CommandP95Ms<=100 && measurements.PreviewP95Ms<=16,"Editor latency budget exceeded.");
        IsoEditorAcceptance.Require(measurements.OpenIdleBytesPerFrame==0 && measurements.ClosedIdleBytesPerFrame==0 && measurements.CachedPreviewBytesPerCall==0,"Editor idle allocation budget exceeded.");
        return files;
    }
    private static void ContactSheets(string output,IReadOnlyList<string> files)
    {
        for (var start=0;start<files.Count;start+=8)
        {
            var visual=new System.Windows.Media.DrawingVisual();
            RenderOptions.SetBitmapScalingMode(visual,BitmapScalingMode.NearestNeighbor);
            using (var dc=visual.RenderOpen())
            {
                dc.DrawRectangle(Brushes.White,null,new Rect(0,0,1400,2000));
                for (var k=start;k<Math.Min(files.Count,start+8);k++)
                {
                    var decoder=new PngBitmapDecoder(new Uri(files[k]),BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad);
                    var bitmap=decoder.Frames[0]; var column=(k-start)%2; var row=(k-start)/2;
                    var scale=Math.Min(680d/bitmap.PixelWidth,450d/bitmap.PixelHeight);
                    dc.DrawText(new FormattedText(Path.GetFileName(files[k]),System.Globalization.CultureInfo.InvariantCulture,FlowDirection.LeftToRight,
                        new Typeface(SystemFonts.MessageFontFamily,FontStyles.Normal,FontWeights.Normal,FontStretches.Normal),14,Brushes.Black,1),new(column*700+10,row*500+8));
                    dc.DrawImage(bitmap,new Rect(column*700+10,row*500+34,bitmap.PixelWidth*scale,bitmap.PixelHeight*scale));
                }
            }
            var target=new RenderTargetBitmap(1400,2000,96,96,PixelFormats.Pbgra32); target.Render(visual);
            var encoder=new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(target));
            using var stream=File.Create(Path.Combine(output,$"contact-{start/8+1:00}.png")); encoder.Save(stream);
        }
    }
    internal static void VerifyMounted(IsoPack pack,IsoLayout layout,string desk)
    {
        var scene=new IsoSceneBuilder(pack,layout).Build([]); var placement=layout.Furniture.Single(f=>f.Id==desk);
        var mounted=layout.Furniture.Single(f=>f.DeskId==desk && f.AssetId=="iso/computer");
        var v=pack.Variant(mounted.AssetId,mounted.Rotation,mounted.State);
        foreach (var part in v.Parts)
        {
            var id=part.PartId ?? pack.Clip(part.SheetId,part.Animation ?? part.Clip!,part.Direction).PartId;
            IsoEditorAcceptance.Require(!scene.Items.Any(i=>i.InstanceId==desk+"/"+id),"Embedded monitor survived suppression at "+placement.Rotation);
            IsoEditorAcceptance.Require(scene.Items.Count(i=>i.InstanceId==mounted.Id+"/"+id)==1,"Mounted monitor gap/duplicate at "+placement.Rotation);
        }
        foreach (var id in new[] {"screen","glow"})
        {
            IsoEditorAcceptance.Require(!scene.Items.Any(i=>i.InstanceId==desk+"/"+id),"Embedded descriptor survived suppression.");
            if ((id=="screen" ? v.Screen : v.Glow)!=null)
                IsoEditorAcceptance.Require(scene.Items.Count(i=>i.InstanceId==mounted.Id+"/"+id)==1,"Mounted descriptor gap/duplicate.");
        }
        var embedded=layout with {Furniture=layout.Furniture.Where(f=>f.Id!=mounted.Id).ToArray(),
            WorkSeats=layout.WorkSeats?.Select(s=>s.DeskId==desk ? s with {ComputerId=null} : s).ToArray()};
        byte[] Pixels(IsoLayout document)
        {
            var office=new IsoOffice(pack,42,document) {PetsEnabled=false};
            var view=new IsoView(office,new PackIsoSheetSource(pack),Path.Combine(Path.GetTempPath(),"synthetic-brand"));
            view.Present(office.Update([],Epoch,0)); view.SetZoom(1);
            var bitmap=new FormatConvertedBitmap(Program.RenderBitmap(view,view.Scene.Width,view.Scene.Height),PixelFormats.Bgra32,null,0);
            var bytes=new byte[bitmap.PixelWidth*bitmap.PixelHeight*4]; bitmap.CopyPixels(bytes,bitmap.PixelWidth*4,0); return bytes;
        }
        IsoEditorAcceptance.Require(Pixels(layout).SequenceEqual(Pixels(embedded)),"Mounted rig pixels differ from embedded rig at "+placement.Rotation);
    }
}
