using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Vibedotcom.Core;
using Vibedotcom.Core.Iso;

namespace Vibedotcom.App;

internal sealed partial class IsoWindow
{
    private IsoEditorSession? editorSession;
    private IsoEditorPanel? editorPanel;
    private bool ownsTracker=true;
    internal IsoEditorSession? EditorSession => editorSession;
    private Settings EditorSettings => editorSession?.Editing==true ? settings with
        { Mode="normal",ClickThrough=false,Borderless=false,TransparentBackground=false } : settings;
    internal void EnterEditing()
    {
        if (editorSession?.Editing==true) return;
        if (editorSession==null && sheets is PackIsoSheetSource source && source.RuntimePack is {} pack)
            editorSession=IsoEditorComposition.Load(pack,storage);
        if (editorSession==null)
        { warning="ISO office editing is not available in this build."; return; }
        if (editorSession.Editor.State.Layout.WidthTiles!=10 || editorSession.Editor.State.Layout.HeightTiles!=10)
        { editorSession.Report([new("L002",Vibedotcom.Core.Iso.Editing.IsoIssueSeverity.Error,"The iso editor requires a 10x10 room with 64x32 projection.",[])]); return; }
        editorSession.BindRuntime(office as Vibedotcom.Core.Iso.Editing.IIsoEditorRuntime);
        editorSession.Enter(settings);
        if (AllowsTransparency) { Reopen(); return; }
        ApplyModes(); AttachEditorPanel();
    }
    internal void DoneEditing()
    {
        if (editorSession?.Editing!=true) return;
        DetachEditorPanel(); settings=editorSession.Leave(settings);
        if (AllowsTransparency!=(settings.Mode=="desktop" || settings.TransparentBackground)) { Reopen(); return; }
        ApplyModes();
    }
    private void AttachEditorPanel()
    {
        if (editorPanel!=null || editorSession==null || Content is not DockPanel root) return;
        editorPanel=new(editorSession,sheets) { Width=360 };
        DockPanel.SetDock(editorPanel,Dock.Right); root.Children.Insert(root.Children.Count-1,editorPanel);
        editorPanel.Done+=DoneEditing; editorPanel.Import+=ShowImport; editorPanel.Manage+=ShowManage;
        editorPanel.Navigate+=NavigateEditor; editorSession.PackChanged+=EditorPackChanged;
        view.AttachEditor(editorSession); MinWidth=760; view.Focus();
        root.Background=new SolidColorBrush(OfficeView.BackgroundColor);
    }
    private void DetachEditorPanel()
    {
        if (editorPanel==null) return;
        view.DetachEditor(); editorPanel.Dispose();
        editorPanel.Done-=DoneEditing; editorPanel.Import-=ShowImport; editorPanel.Manage-=ShowManage; editorPanel.Navigate-=NavigateEditor;
        editorSession!.PackChanged-=EditorPackChanged;
        if (Content is Panel root) { root.Children.Remove(editorPanel); root.Background=new SolidColorBrush(OfficeView.BackgroundColor); }
        editorPanel=null; MinWidth=400;
    }
    private bool HandleEditorShortcut(KeyEventArgs e)
    {
        if (editorPanel==null || OwnedWindows.Count>0) return false;
        if (!view.HandleEditorKey(e.Key,Keyboard.Modifiers,e.OriginalSource as DependencyObject)) return false;
        e.Handled=true; return true;
    }
    private void NavigateEditor(string id)
    {
        var f=editorSession!.SelectedFloor;
        var wall=editorSession.SelectedWall ?? editorSession.Editor.State.Layout.WallSegments?.FirstOrDefault(w=>w.Id==id);
        var native=f!=null ? IsoEditorOverlay.Project(f.X,f.Y,0,view.EditorCamera) : wall is {} w ?
            IsoEditorOverlay.Project(w.Edge.I,w.Edge.J,24,view.EditorCamera) : new Point(view.Scene.Width/2d,view.Scene.Height/2d);
        var target=view.ScreenPoint(native);
        view.PanBy(new Vector(view.ActualWidth/2-target.X,view.ActualHeight/2-target.Y)); view.Focus();
    }
    private void EditorPackChanged(IsoPack pack) { sheets=editorSession!.CommittedSheets ?? new PackIsoSheetSource(pack); view.ReplaceEditorSheets(sheets); }
    private void ShowImport() => new IsoEditorImportDialog(editorSession!) { Owner=this }.ShowDialog();
    private void ShowManage() => new IsoEditorManageDialog(editorSession!) { Owner=this }.ShowDialog();
}
