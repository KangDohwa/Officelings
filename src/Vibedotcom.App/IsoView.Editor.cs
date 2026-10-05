using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Vibedotcom.Core;
using Vibedotcom.Core.Iso;
using Vibedotcom.Core.Iso.Editing;

namespace Vibedotcom.App;

internal sealed partial class IsoView
{
    private IsoEditorSession? editorSession;
    private IsoEditorOverlay? editorOverlay;
    private Point? editorPan;
    private string? editorDrag;
    private Tile? editorDragStart;
    private Tile? editorOrigin;
    internal IsoEditorOverlay? EditorOverlay => editorOverlay;
    internal bool Editing => editorSession!=null;
    internal IsoPixel EditorCamera => editorCameraOverride ?? (office is IsoOffice engine ? engine.Camera : new IsoPixel());
    private IsoPixel? editorCameraOverride;
    private Rect EditorRenderBounds()
    {
        var bounds=new Rect(0,0,scene.Width,scene.Height);
        if (editorOverlay?.Bounds is {} markers && !markers.IsEmpty) bounds.Union(markers);
        return new(Math.Floor(bounds.Left),Math.Floor(bounds.Top),Math.Ceiling(bounds.Right)-Math.Floor(bounds.Left),Math.Ceiling(bounds.Bottom)-Math.Floor(bounds.Top));
    }

    internal void ReplaceEditorSheets(IIsoSheetSource next)
    { sheets=next; retained.Clear(); RebuildScene(); RefreshEditor(); }

    internal void AttachEditor(IsoEditorSession session,IsoPixel? cameraOverride=null)
    {
        DetachEditor(); editorSession=session; editorCameraOverride=cameraOverride;
        editorOverlay=new(); root.Children.Add(editorOverlay.Visual); Focusable=true;
        AutomationProperties.SetName(this,"Iso editor canvas"); AutomationProperties.SetHelpText(this,"Arrows move one grid cell; Enter selects or places; Escape cancels.");
        session.Changed+=RefreshEditor; PreviewMouseDown+=EditorMouseDown; PreviewMouseMove+=EditorMouseMove;
        PreviewMouseUp+=EditorMouseUp; PreviewKeyDown+=EditorKeyDown; LostMouseCapture+=EditorCaptureLost;
        HideTooltip(); RefreshEditor();
    }
    internal void DetachEditor()
    {
        if (editorSession==null) return;
        CancelEditorCapture(); editorSession.Changed-=RefreshEditor;
        PreviewMouseDown-=EditorMouseDown; PreviewMouseMove-=EditorMouseMove; PreviewMouseUp-=EditorMouseUp;
        PreviewKeyDown-=EditorKeyDown; LostMouseCapture-=EditorCaptureLost;
        if (editorOverlay!=null) root.Children.Remove(editorOverlay.Visual);
        editorSession=null; editorOverlay=null; editorCameraOverride=null; Focusable=false;
    }
    private void RefreshEditor()
    { editorOverlay?.Rebuild(editorSession!,sheets,EditorCamera); UpdateCamera(); }
    private Tile? EditorTile(Point dip)
    {
        var native=ScenePoint(dip);
        return editorSession!.PickTile(editorSession.Editor.State.Layout,EditorCamera,native.X,native.Y);
    }
    internal IsoHit? EditorHit(Point dip)
    {
        var native=ScenePoint(dip);
        if (editorOverlay?.HitHandle(native)==true && editorSession?.Selection is {} selection) return new(selection,"handle");
        return editorSession!.HitTest(editorSession.Pack,editorSession.Editor.State.Layout,editorSession.Editor.GetPickingScene(EditorCamera),(int)Math.Floor(native.X),(int)Math.Floor(native.Y));
    }
    private void EditorMouseDown(object sender,MouseButtonEventArgs e)
    {
        var point=e.GetPosition(this); Focus(); HideTooltip(); e.Handled=true;
        if (e.ChangedButton==MouseButton.Middle || e.ChangedButton==MouseButton.Left && Keyboard.IsKeyDown(Key.Space))
        { editorPan=point; CaptureMouse(); return; }
        if (e.ChangedButton!=MouseButton.Left) { e.Handled=false; return; }
        if (editorOverlay!.HitHandle(ScenePoint(point)) && editorSession!.Selection!=null)
        {
            editorDrag=editorSession.Selection; editorDragStart=EditorTile(point);
            var f=editorSession.SelectedFloor; editorOrigin=f==null ? null : new(f.X,f.Y); CaptureMouse(); return;
        }
        var session=editorSession!;
        if (session.Tool==IsoEditorTool.Place)
        { if (EditorTile(point) is {} tile) { session.SetCursor(tile); session.CommitPreview(); } }
        else if (session.Tool==IsoEditorTool.WallDecor) { PickHost(point); session.CommitPreview(); }
        else if (EditorHit(point) is {} hit)
        { if (session.Tool==IsoEditorTool.WorkPosition) session.SeatPick(hit.TargetId); else session.Select(hit.TargetId); }
    }
    private void EditorMouseMove(object sender,MouseEventArgs e)
    {
        e.Handled=true; var point=e.GetPosition(this);
        if (editorPan is {} previous) { PanBy(point-previous); editorPan=point; return; }
        if (editorDrag!=null)
        {
            if (editorOrigin is {} origin && editorDragStart is {} start && EditorTile(point) is {} tile)
                editorSession!.PreviewCommand(new IsoMoveCommand(editorDrag,new(origin.X+tile.X-start.X,origin.Y+tile.Y-start.Y)));
            else if (PickHost(point) is {} host) editorSession!.PreviewCommand(new IsoMoveWallCommand(editorDrag,host));
        }
        else if (editorSession!.Tool==IsoEditorTool.Place && EditorTile(point) is {} cursor && cursor!=editorSession.Cursor) editorSession.SetCursor(cursor);
        else if (editorSession.Tool==IsoEditorTool.WallDecor) PickHost(point);
    }
    private string? PickHost(Point point)
    {
        // Host cursor is ground/edge based; never inverse an elevated wall top pixel.
        var native=ScenePoint(point); var ground=IsoGeometry.Inverse(native.X-EditorCamera.X,native.Y-EditorCamera.Y);
        var host=System.Linq.Enumerable.MinBy(System.Linq.Enumerable.Where(editorSession!.Editor.State.Layout.WallSegments ?? [],w=>w.HostId==null),
            w=>Math.Abs(w.Edge.I-ground.I)+Math.Abs(w.Edge.J-ground.J));
        if (host!=null && editorSession.HostId!=host.Id) editorSession.SetHost(host.Id);
        return host?.Id;
    }
    private void EditorMouseUp(object sender,MouseButtonEventArgs e)
    {
        if (e.ChangedButton is not (MouseButton.Left or MouseButton.Middle)) return;
        e.Handled=true;
        if (editorDrag!=null) editorSession!.CommitPreview();
        editorDrag=null; editorPan=null; ReleaseMouseCapture();
    }
    private void EditorCaptureLost(object sender,MouseEventArgs e)
    { if (editorDrag!=null) editorSession?.Cancel(); editorDrag=null; editorPan=null; }
    internal void CancelEditorCapture()
    { editorDrag=null; editorPan=null; editorDragStart=null; editorOrigin=null; editorSession?.Cancel(); if (IsMouseCaptured) ReleaseMouseCapture(); }
    internal static bool IsTextInput(DependencyObject? source) => source is TextBoxBase || source is System.Windows.Controls.ComboBox { IsEditable:true } || source is PasswordBox;
    private void EditorKeyDown(object sender,KeyEventArgs e)
    { e.Handled=HandleEditorKey(e.Key,Keyboard.Modifiers,e.OriginalSource as DependencyObject); }
    internal bool HandleEditorKey(Key key,ModifierKeys modifiers,DependencyObject? source)
    {
        if (editorSession==null || IsTextInput(source)) return false;
        var session=editorSession; var control=(modifiers & ModifierKeys.Control)!=0;
        if (key==Key.Escape) { CancelEditorCapture(); session.SetTool(IsoEditorTool.Select); return true; }
        if (control)
        {
            switch (key) { case Key.D:session.Duplicate(); return true; case Key.Z:session.Undo(); return true; case Key.Y:session.Redo(); return true; case Key.S:session.Save(); return true; }
        }
        if (key is Key.Enter or Key.Left or Key.Right or Key.Up or Key.Down && !ReferenceEquals(source,this)) return false;
        if (key==Key.Enter)
        {
            if (session.Pending!=null) session.CommitPreview();
            else { var point=IsoEditorOverlay.Project(session.Cursor.X,session.Cursor.Y,0,EditorCamera); if (session.HitTest(session.Pack,session.Editor.State.Layout,session.Editor.GetPickingScene(EditorCamera),(int)point.X,(int)point.Y) is {} hit) { if (session.Tool==IsoEditorTool.WorkPosition) session.SeatPick(hit.TargetId); else session.Select(hit.TargetId); } }
            return true;
        }
        if (key==Key.R && session.Selection!=null) { session.RotateSelection(); return true; }
        if (key==Key.Delete && session.Selection!=null) { session.Execute(new IsoDeleteCommand(session.Selection)); return true; }
        var delta=key switch { Key.Left=>(-1,0),Key.Right=>(1,0),Key.Up=>(0,-1),Key.Down=>(0,1),_=>(0,0) };
        if (delta!=(0,0)) { session.MoveCursor(delta.Item1,delta.Item2,session.Tool==IsoEditorTool.Select && session.Selection!=null); return true; }
        return false;
    }
}
