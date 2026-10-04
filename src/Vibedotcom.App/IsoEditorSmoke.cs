using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Vibedotcom.Core;
using Vibedotcom.Core.Iso;
using Vibedotcom.Core.Iso.Editing;

namespace Vibedotcom.App;

// Explicit offscreen UI-fake suite: never evidence that the unmerged editor/importer implements commands.
internal static class IsoEditorSmoke
{
    internal static readonly DateTimeOffset Epoch=DateTimeOffset.Parse("2026-10-04T00:00:00Z");
    internal static IReadOnlyList<string> Render(string output,IsoPack pack)
    {
        Directory.CreateDirectory(output); var files=new List<string>(); var records=new List<object>();
        var office=new IsoOffice(pack,42) { PetsEnabled=false }; var scene=office.Update([],Epoch,0);
        var sheets=new PackIsoSheetSource(pack); var layout=pack.Layout;
        var worldView=new IsoView(office,sheets,Path.Combine(output,"synthetic-brand")); worldView.Present(scene);
        var inputEditor=new IsoEditorUiFake(pack); var inputSession=new IsoEditorSession(inputEditor,pack,pack,new IsoEditorUiImporterFake(pack),new(Path.Combine(output,"synthetic-home")),new([]),layout);
        inputSession.Enter(new Settings()); worldView.AttachEditor(inputSession);
        var inputDesk=layout.Furniture.First(f=>f.AssetId=="iso/desk"); inputSession.SeatPick(inputDesk.Id);
        var linkedChair=layout.Furniture.First(f=>f.AssetId=="iso/chair" && f.DeskId==inputDesk.Id);
        if (inputSession.SeatChairId!=linkedChair.Id || inputSession.Pending is not IsoSetSeatCommand) throw new InvalidOperationException("Linked chair was not retained for designation.");
        inputSession.Cancel(); inputSession.PreviewCommand(new IsoRotateCommand(inputDesk.Id,180));
        worldView.HandleEditorKey(Key.R,ModifierKeys.None,worldView);
        if (inputEditor.Commands.Single() is not IsoRotateCommand { Rotation:180 }) throw new InvalidOperationException("Rotation preview was not committed exactly.");
        var moved=layout with {Furniture=layout.Furniture.Select(f=>f.Id==inputDesk.Id ? f with {X=3,Y=3} : f).ToImmutableArray()};
        inputEditor.NextResult=new(true,true,inputEditor.State with {Layout=moved},[]);
        inputSession.PreviewCommand(new IsoMoveCommand(inputDesk.Id,new(3,3)));
        if (inputSession.Cursor!=new Tile(3,3)) throw new InvalidOperationException("Coordinate preview fields changed.");
        inputSession.CommitPreview(); inputSession.PreviewCommand(new IsoMoveCommand(inputDesk.Id,new(4,4))); inputSession.Cancel();
        if (inputSession.Cursor!=new Tile(3,3)) throw new InvalidOperationException("Cancel did not restore selection coordinates.");
        var deleted=moved with {Furniture=moved.Furniture.Where(f=>f.Id!=inputDesk.Id && f.DeskId!=inputDesk.Id).ToImmutableArray()};
        inputEditor.NextResult=new(true,true,inputEditor.State with {Layout=deleted},[]); inputSession.Execute(new IsoDeleteCommand(inputDesk.Id));
        if (inputSession.Selection!=null || inputSession.SeatDeskId!=null) throw new InvalidOperationException("Deleted selection references remain.");
        worldView.DetachEditor();
        foreach (var scale in new[] {1,2,3})
        {
            worldView.SetZoom(scale); var file=Path.Combine(output,$"world-art-{scale}x.png");
            IsoApplication.Render(worldView,scene.Width*scale,scene.Height*scale,file); files.Add(file);
        }
        var seats=layout.Furniture.Where(f=>f.AssetId=="iso/desk").Select(f=>new IsoSeatDesignation(f.Id,f.Id,
            layout.Furniture.Single(c=>c.AssetId=="iso/chair" && c.DeskId==f.Id).Id)).ToImmutableArray();
        layout=layout with { WorkSeats=seats };
        var selected=layout.Furniture.First(f=>f.AssetId=="iso/desk");
        var states=new List<(string Name,IsoLayout Layout,string? Selection,IsoEditPreview? Preview,ImmutableArray<IsoValidationIssue> Issues)>
        { ("default-twelve",layout,null,null,[]) };
        foreach (var angle in new[] {0,90,180,270})
        {
            var desk=new IsoPlacement(selected.Id,"iso/desk",4,4,angle);
            var selectedContact=IsoContacts.Desk(desk.X,desk.Y,angle);
            var aligned=new IsoPlacement("chair-selected","iso/chair",selectedContact.Approach.X,selectedContact.Approach.Y,IsoGeometry.Rotation(selectedContact.Facing),DeskId:desk.Id);
            var candidate=layout with { Furniture=new[] {desk,aligned}.ToImmutableArray(),WorkSeats=[new(desk.Id,desk.Id,aligned.Id)] };
            states.Add(($"selected-desk-{angle}",candidate,selected.Id,Preview(pack,candidate,desk,[]),[]));
        }
        var prop=new IsoPlacement("object-ui-1","iso/plant",4,8);
        var valid=layout with { Furniture=layout.Furniture.Append(prop).ToImmutableArray() };
        var error=new IsoValidationIssue("L006",IsoIssueSeverity.Error,"Blocking objects 'object-ui-1' and 'desk-1' overlap at (2,2).",["desk-1","object-ui-1"]);
        states.Add(("valid-ghost",layout,null,Preview(pack,valid,prop,[]),[]));
        states.Add(("invalid-ghost",layout,null,Preview(pack,valid,prop,[error]) with { CanCommit=false },[]));
        var navigation=new IsoValidationIssue("L013",IsoIssueSeverity.Error,"Interaction or seat 'desk-1' is unreachable from the door.",["desk-1"]);
        states.Add(("navigation-draft",valid,null,Preview(pack,valid,prop,[navigation]),[navigation]));
        var chair=new IsoPlacement("chair-ui-1","iso/chair",4,8);
        var loneChair=layout with { Furniture=layout.Furniture.Append(chair).ToImmutableArray() };
        states.Add(("lone-chair",layout,null,Preview(pack,loneChair,chair,[]),[]));
        var computer=new IsoPlacement("computer-ui-1","iso/computer",4,8);
        var loneComputer=layout with { Furniture=layout.Furniture.Append(computer).ToImmutableArray() };
        states.Add(("lone-computer-ui-fake",layout,null,new(loneComputer,[new(4,8),new(4,9)],[],[],true),[]));
        states.Add(("designated-seat",layout,selected.Id,Preview(pack,layout,selected,[]),[]));
        states.Add(("cleared-seat",layout with { WorkSeats=seats.Where(s=>s.DeskId!=selected.Id).ToImmutableArray() },selected.Id,null,[]));
        var copy=selected with { Id="desk-ui-copy",X=4,Y=8 };
        var contact=IsoContacts.Desk(copy.X,copy.Y,copy.Rotation);
        var copyChair=new IsoPlacement("chair-ui-copy","iso/chair",contact.Approach.X,contact.Approach.Y,IsoGeometry.Rotation(contact.Facing),DeskId:copy.Id);
        var duplicateSource=layout with { WorkSeats=seats.RemoveAt(seats.Length-1) };
        var duplicate=duplicateSource with { Furniture=layout.Furniture.Concat(new[] {copy,copyChair}).ToImmutableArray(),
            WorkSeats=duplicateSource.WorkSeats!.Append(new(copy.Id,copy.Id,copyChair.Id)).ToImmutableArray() };
        states.Add(("linked-duplicate",duplicateSource,null,Preview(pack,duplicate,copy,[]),[]));
        var fake=new IsoEditorUiFake(pack,layout);
        var decor=fake.WallPalette.FirstOrDefault(w=>w.ModuleIds.Length==2);
        if (decor!=null)
        {
            var hosts=(layout.WallSegments ?? []).Where(w=>w.HostId==null && w.Face==decor.Face).OrderBy(w=>w.Edge.I+w.Edge.J).Take(2).ToArray();
            var attachments=hosts.Select((h,i)=>new IsoWallSegment("decor-ui-edge-"+i,h.Face,h.Edge,decor.ModuleIds[i],HostId:h.Id,AssemblyId:"decor-ui"));
            var wallLayout=layout with { WallSegments=(layout.WallSegments ?? []).Concat(attachments).ToImmutableArray() };
            states.Add(("two-edge-wall",layout,null,new(wallLayout,[],[],[],true),[]));
        }
        states.Add(("import-success-ui-fake",layout,null,null,[]));
        var importError=new IsoValidationIssue("O008",IsoIssueSeverity.Error,"Custom pixels are outside the declared palette.",[]);
        states.Add(("import-failure",layout,null,null,[importError]));
        states.Add(("grouped-validation",layout,selected.Id,null,[error,navigation,importError,error with { TargetIds=["desk-2","object-ui-2"] }]));
        var previousContext=SynchronizationContext.Current; var previousShutdown=Application.Current.ShutdownMode;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        Application.Current.ShutdownMode=ShutdownMode.OnExplicitShutdown;
        try
        {
        foreach (var success in new[] {true,false})
        {
            var importer=new IsoEditorUiImporterFake(pack) {Validation=success ? [] : [importError]};
            var importSession=new IsoEditorSession(new IsoEditorUiFake(pack,layout),pack,pack,importer,new(Path.Combine(output,"synthetic-home")),new([]),layout)
                {CommitPack=(candidate,catalog)=>[]};
            var dialog=new IsoEditorImportDialog(importSession);
            Complete(dialog.ValidateAsync(Path.Combine(output,"synthetic-source.iso.json")));
            if (importer.Imports!=0 || dialog.Validated!=success) throw new InvalidOperationException("Pure import validation changed state.");
            if (success)
            {
                var importing=dialog.ImportAsync(); Complete(importing);
                if (importing.Result?.Succeeded!=true || importer.Imports!=1) throw new InvalidOperationException("Scripted import did not succeed.");
            }
            var file=Path.Combine(output,success ? "import-dialog-success-ui-fake.png" : "import-dialog-failure.png");
            IsoApplication.Render((FrameworkElement)dialog.Content,540,560,file); files.Add(file); dialog.Close();
        }
        }
        finally
        { SynchronizationContext.SetSynchronizationContext(previousContext); Application.Current.ShutdownMode=previousShutdown; }
        foreach (var entry in states)
        {
            var editor=new IsoEditorUiFake(pack,entry.Layout) { Validation=entry.Issues };
            var session=new IsoEditorSession(editor,pack,pack,new IsoEditorUiImporterFake(pack),new(Path.Combine(output,"synthetic-home")),new([]),layout);
            session.Enter(new Settings()); session.Select(entry.Selection);
            if (entry.Selection==null && session.SelectedWall!=null) throw new InvalidOperationException("Empty selection resolved a wall attachment.");
            if (entry.Preview!=null) { editor.NextPreview=entry.Preview; session.PreviewCommand(new IsoMoveCommand(selected.Id,new(4,8))); }
            var stateScene=scene; var stateCamera=office.Camera;
            if (entry.Name.StartsWith("selected-desk-",StringComparison.Ordinal))
            { var builder=new IsoSceneBuilder(pack,entry.Layout); stateScene=builder.Build([]); stateCamera=builder.Camera; }
            var view=new IsoView(office,sheets,Path.Combine(output,"synthetic-brand")); view.Present(stateScene);
            view.AttachEditor(session,stateCamera);
            foreach (var scale in new[] {1,2,3})
            {
                view.SetZoom(scale); var name=$"{entry.Name}-{scale}x.png"; var file=Path.Combine(output,name);
                IsoApplication.Render(view,stateScene.Width*scale,stateScene.Height*scale,file); files.Add(file);
                records.Add(new {file=name,scale,state=entry.Name,nativeCamera=stateCamera,width=stateScene.Width*scale,height=stateScene.Height*scale,
                    sha256=Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file)))});
            }
            if (entry.Name is "default-twelve" or "grouped-validation" or "import-success-ui-fake" or "import-failure")
            {
                using var panel=new IsoEditorPanel(session,sheets);
                view.Width=double.NaN; view.Height=double.NaN;
                var grid=new Grid(); grid.ColumnDefinitions.Add(new()); grid.ColumnDefinitions.Add(new() {Width=new GridLength(360)});
                grid.Children.Add(view); Grid.SetColumn(panel,1); grid.Children.Add(panel);
                var name=entry.Name+"-controls.png"; var file=Path.Combine(output,name);
                view.SetZoom(1); IsoApplication.Render(grid,1120,900,file); files.Add(file);
                if (entry.Name is "grouped-validation" or "import-failure")
                { panel.ScrollToBottom(); panel.UpdateLayout(); IsoApplication.Render(grid,1120,900,file); }
                grid.Children.Clear();
            }
            if (entry.Name=="default-twelve")
            {
                using var panel=new IsoEditorPanel(session,sheets,2);
                var file=Path.Combine(output,"controls-text-200.png"); IsoApplication.Render(panel,520,900,file); files.Add(file);
                panel.ScrollToBottom(); panel.UpdateLayout();
                var bottom=Path.Combine(output,"controls-text-200-bottom.png"); IsoApplication.Render(panel,520,900,bottom); files.Add(bottom);
            }
            var cropFile=Path.Combine(output,entry.Name+"-crop.png"); view.SetZoom(3);
            IsoApplication.Render(view,1440,960,cropFile);
            var focus=entry.Preview?.Footprint.FirstOrDefault() ?? (session.SelectedFloor is {} focusFloor ? new Tile(focusFloor.X,focusFloor.Y) : new(4,4));
            var target=view.ScreenPoint(IsoEditorOverlay.Project(focus.X,focus.Y,0,stateCamera));
            view.PanBy(new Vector(view.ActualWidth/2-target.X,view.ActualHeight/2-target.Y));
            IsoApplication.Render(view,1440,960,cropFile); files.Add(cropFile); view.DetachEditor();
        }
        var worldScaling=WorldScaling(output);
        File.WriteAllText(Path.Combine(output,"iso-editor-smoke.json"),JsonSerializer.Serialize(new {seed=42,epoch=Epoch,fakes=true,worldScaling,
            nativeCamera=office.Camera,packHashes=pack.Images.OrderBy(p=>p.Key,StringComparer.Ordinal).Select(p=>new {id=p.Key,sha256=Convert.ToHexStringLower(SHA256.HashData(p.Value))}),renders=records,
            files=files.Select(file=>new {file=Path.GetFileName(file),sha256=Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file)))})},StateJson.Options));
        return files;
    }
    private static void Complete(Task task)
    {
        var dispatcher=Dispatcher.CurrentDispatcher; var frame=new DispatcherFrame();
        _=task.ContinueWith(_=>dispatcher.BeginInvoke(()=>frame.Continue=false),TaskScheduler.Default);
        Dispatcher.PushFrame(frame); task.GetAwaiter().GetResult();
    }
    private static object[] WorldScaling(string output)
    {
        static (int Width,int Height,byte[] Pixels) Load(string file)
        {
            var decoder=new PngBitmapDecoder(new Uri(file),BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad);
            var bitmap=new FormatConvertedBitmap(decoder.Frames[0],PixelFormats.Bgra32,null,0);
            var pixels=new byte[bitmap.PixelWidth*bitmap.PixelHeight*4]; bitmap.CopyPixels(pixels,bitmap.PixelWidth*4,0);
            return (bitmap.PixelWidth,bitmap.PixelHeight,pixels);
        }
        var source=Load(Path.Combine(output,"world-art-1x.png")); var results=new List<object>();
        foreach (var scale in new[] {2,3})
        {
            var target=Load(Path.Combine(output,$"world-art-{scale}x.png"));
            if (target.Width!=source.Width*scale || target.Height!=source.Height*scale) throw new InvalidOperationException("World art dimensions changed.");
            var different=0;
            for (var y=0;y<target.Height;y++) for (var x=0;x<target.Width;x++)
            {
                var a=((y/scale)*source.Width+x/scale)*4; var b=(y*target.Width+x)*4;
                if (!source.Pixels.AsSpan(a,4).SequenceEqual(target.Pixels.AsSpan(b,4))) different++;
            }
            results.Add(new {scale,comparedPixels=target.Width*target.Height,differentPixels=different});
            if (different!=0) throw new InvalidOperationException($"World art {scale}x differs at {different} pixels.");
        }
        return results.ToArray();
    }
    private static IsoEditPreview Preview(IsoPack pack,IsoLayout layout,IsoPlacement f,ImmutableArray<IsoValidationIssue> issues)
    {
        var v=pack.Variant(f.AssetId,f.Rotation,f.State);
        var footprint=Enumerable.Range(0,v.FootprintW*v.FootprintH).Select(k=>new Tile(f.X+k%v.FootprintW,f.Y+k/v.FootprintW)).ToImmutableArray();
        var points=ImmutableArray<IsoPreviewPoint>.Empty;
        if (f.AssetId=="iso/desk")
        {
            var contact=IsoContacts.Desk(f.X,f.Y,f.Rotation);
            IsoPreviewPoint Contact(string kind,IsoPixel pixel) { var p=IsoGeometry.Inverse(pixel.X,pixel.Y); return new(f.Id,kind,p); }
            points=[new(f.Id,"approach",new(contact.Approach.X,contact.Approach.Y)),new(f.Id,"seat ground",contact.SeatGround),
                Contact("pelvis",contact.Pelvis),Contact("left hand",contact.HandLeft),Contact("right hand",contact.HandRight)];
        }
        return new(layout,footprint,points,issues,true);
    }
}
