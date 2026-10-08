using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Threading;
using System.Windows.Threading;
using Officelings.Core;
using Officelings.Core.Iso;
using Officelings.Core.Iso.Editing;

namespace Officelings.App;

// The same real-service UI assertions run in offscreen QA and U001–U007.
internal static class IsoEditorUiAcceptance
{
    private static void Check(bool condition,string message) => IsoEditorAcceptance.Require(condition,message);
    internal static void Run(int rule,IsoPack bundled,string root)
    {
        var session=IsoEditorAcceptance.Create(bundled,root,true);
        switch (rule)
        {
            case 1: Picking(session); break;
            case 2: Preview(session); break;
            case 3: Lifecycle(session); break;
            case 4: Keyboard(session); break;
            case 5: Camera(session); break;
            case 6: Palette(session); break;
            case 7: Accessibility(session); break;
            default: throw new ArgumentOutOfRangeException(nameof(rule));
        }
    }
    private static void Picking(IsoEditorSession session)
    {
        var office=(IsoOffice)session.Runtime!; var published=office.Update([],IsoEditorSmoke.Epoch,0);
        var result=session.Execute(new IsoPlaceCommand("iso/plant",session.Editor.State.Layout.Door));
        Check(result.Succeeded && result.Issues.Any(i=>i.Severity==IsoIssueSeverity.Error),"U001 invalid draft.");
        var draft=session.Editor.GetPickingScene(office.Camera); var target=result.PrimaryTargetId!;
        bool HasHit(IsoScene scene)
        {
            foreach (var item in scene.Items.Where(i=>i.Layer==IsoLayer.Furniture))
            {
                var sheet=session.Pack.Sheet(item.SheetId);
                for (var y=0;y<sheet.CellH;y++) for (var x=0;x<sheet.CellW;x++)
                    if (session.Pack.Opaque(item,x,y) && IsoHitTest.HitTest(session.Pack,session.Editor.State.Layout,scene,item.X+x,item.Y+y)?.TargetId==target) return true;
            }
            return false;
        }
        Check(HasHit(draft) && !HasHit(published),"U001 draft mask mixed with live scene.");
        Check(ReferenceEquals(draft,session.Editor.GetPickingScene(office.Camera)),"U001 picking cache.");
        var view=IsoEditorSmoke.View(session); session.Select(target); var handle=view.EditorOverlay!.Handle;
        Check(view.EditorHit(view.ScreenPoint(new(handle.X+handle.Width/2,handle.Y+handle.Height/2)))?.TargetId==target,"U001 handle precedence.");
        view.DetachEditor();
    }
    private static void Preview(IsoEditorSession session)
    {
        var editor=session.Editor; var revision=editor.State.Revision;
        foreach (var angle in new[] {0,90,180,270})
        {
            var preview=editor.Preview(new IsoPlaceCommand("iso/desk",new(4,4),angle)); var contact=IsoContacts.Desk(4,4,angle);
            Check(preview.CanCommit && preview.Footprint.Length==2,"U002 footprint.");
            var pelvis=preview.Points.Single(p=>p.Kind=="pelvis");
            var projected=IsoGeometry.Project(pelvis.Ground.I,pelvis.Ground.J,pelvis.Elevation);
            Check(contact.Pelvis.X==projected.X && contact.Pelvis.Y==projected.Y,"U002 canonical pelvis.");
            Check(editor.State.Revision==revision && !editor.State.CanUndo,"U002 pure preview.");
        }
        IsoEditorSmoke.Desk(session,0); session.PreviewCommand(new IsoPlaceCommand("iso/desk",new(4,4)));
        var view=IsoEditorSmoke.View(session);
        Check(session.Preview?.CanCommit==false && view.EditorOverlay!.PreviewText.StartsWith("✕ L006",StringComparison.Ordinal),"U002 invalid ghost text.");
        view.DetachEditor();
    }
    private static void Lifecycle(IsoEditorSession session)
    {
        var editor=session.Editor; var runtime=session.Runtime;
        var settings=new Settings {Mode="desktop",ClickThrough=true,Borderless=true,TransparentBackground=true};
        var effective=session.Enter(settings); Check(effective.Mode=="normal" && !effective.ClickThrough,"U003 edit modes.");
        Check(session.Leave(effective)==settings && ReferenceEquals(editor,session.Editor) && ReferenceEquals(runtime,session.Runtime),"U003 lifecycle.");
        session.Execute(new IsoPlaceCommand("iso/desk",new(4,4)));
        Check(!session.Applying && runtime!.AppliedEditorRevision==editor.State.Revision,"U003 copied layout acknowledgement.");
    }
    private static void Keyboard(IsoEditorSession session)
    {
        var id=session.Execute(new IsoPlaceCommand("iso/desk",new(4,4))).PrimaryTargetId!;
        var view=IsoEditorSmoke.View(session); session.Select(id); var revision=session.Editor.State.Revision;
        Check(!view.HandleEditorKey(Key.Delete,ModifierKeys.None,new TextBox()) && session.Editor.State.Revision==revision,"U004 text input immunity.");
        Check(view.HandleEditorKey(Key.R,ModifierKeys.None,view) && session.SelectedFloor!.Rotation==90,"U004 rotate.");
        Check(view.HandleEditorKey(Key.D,ModifierKeys.Control,view) && session.Editor.State.Layout.Furniture.Count==2,"U004 duplicate.");
        view.HandleEditorKey(Key.Delete,ModifierKeys.None,view); Check(session.Editor.State.Layout.Furniture.Count==1,"U004 delete.");
        view.HandleEditorKey(Key.Z,ModifierKeys.Control,view); Check(session.Editor.State.Layout.Furniture.Count==2,"U004 undo.");
        view.HandleEditorKey(Key.Y,ModifierKeys.Control,view); Check(session.Editor.State.Layout.Furniture.Count==1,"U004 redo.");
        view.HandleEditorKey(Key.S,ModifierKeys.Control,view); Check(File.Exists(session.LayoutPath),"U004 save.");
        revision=session.Editor.State.Revision; session.Select(id); session.PreviewCommand(new IsoMoveCommand(id,new(2,3)));
        view.HandleEditorKey(Key.Escape,ModifierKeys.None,view);
        Check(session.Pending==null && session.Editor.State.Revision==revision,"U004 cancel."); view.DetachEditor();
    }
    private static void Camera(IsoEditorSession session)
    {
        var view=IsoEditorSmoke.View(session); Program.RenderBitmap(view,900,700);
        foreach (var dpi in new[] {1d,1.25,1.5,2}) foreach (var zoom in new[] {1,2,3,6})
        {
            var matrix=IsoView.PhysicalCamera(view.Scene.Width,view.Scene.Height,900,700,dpi,dpi,zoom,new(17.25,-9.6));
            var native=IsoEditorOverlay.Project(4,4,0,view.EditorCamera); var dip=matrix.Transform(native); var inverse=matrix; inverse.Invert();
            var point=inverse.Transform(dip);
            Check(IsoHitTest.PickTile(session.Editor.State.Layout,view.EditorCamera,point.X,point.Y)==new Tile(4,4),"U005 inverse camera.");
            Check(Math.Abs(Math.Round(matrix.OffsetX*dpi)-matrix.OffsetX*dpi)<1e-9 && Math.Abs(Math.Round(matrix.OffsetY*dpi)-matrix.OffsetY*dpi)<1e-9,"U005 physical snap.");
        }
        session.PreviewCommand(new IsoPlaceCommand("iso/desk",new(-1,-1)));
        var bounds=view.EditorRenderBounds();
        Check(view.EditorOverlay!.Bounds.Top<0 && bounds.Top<=view.EditorOverlay.Bounds.Top,"U005 out-of-room marker bounds.");
        view.SetZoom(1); Program.RenderBitmap(view,(int)bounds.Width,(int)bounds.Height);
        var top=view.ScreenPoint(new(bounds.Left,bounds.Top));
        Check(top.X>=0 && top.Y>=0,"U005 marker bounds excluded from camera.");
        view.DetachEditor();
    }
    private static void Palette(IsoEditorSession session)
    {
        Check(session.Search("").Count()==session.Editor.Palette.Length && session.Search("computer").Any(a=>a.AssetId=="iso/computer"),"U006 real palette search.");
        var fixture=IsoEditorSmoke.Fixture("objects/valid/test-crate/test-crate.iso.json");
        var previous=SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        try
        {
            var dialog=new IsoEditorImportDialog(session); IsoEditorSmoke.Complete(dialog.ValidateAsync(fixture));
            Check(dialog.Validated,"U006 verified dialog."); var importing=dialog.ImportAsync(); IsoEditorSmoke.Complete(importing);
            Check(importing.Result?.Succeeded==true,"U006 dialog commit."); dialog.Close();
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
        Check(session.Editor.Palette.Any(a=>a.AssetId=="user/test-crate"),"U006 transactional dialog.");
        session.Privacy=PrivacyMode.Full; Check(!session.Search("user/").Any(),"U006 privacy search.");
    }
    private static void Accessibility(IsoEditorSession session)
    {
        using var panel=new IsoEditorPanel(session,new PackIsoSheetSource(session.Pack),2); Program.RenderBitmap(panel,520,900);
        Check(panel.FontSize==SystemFonts.MessageFontSize*2 && panel.VerticalScrollBarVisibility==ScrollBarVisibility.Auto,"U007 scaled scroll.");
        Check(panel.Buttons.Any(b=>AutomationProperties.GetName(b)=="Save (Ctrl+S)") && panel.Buttons.Any(b=>AutomationProperties.GetName(b)=="Done editing"),"U007 reachable actions.");
        Check(panel.Buttons.All(b=>!string.IsNullOrWhiteSpace(AutomationProperties.GetName(b))),"U007 automation names.");
        panel.ScrollToBottom(); Program.RenderBitmap(panel,520,900);
        Check(panel.ScrollableHeight>0 && Math.Abs(panel.ScrollableHeight-panel.VerticalOffset)<.001,"U007 bottom scroll.");
    }
}
