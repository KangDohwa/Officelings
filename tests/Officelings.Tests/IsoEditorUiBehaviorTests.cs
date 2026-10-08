using System;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using Officelings.App;
using Officelings.Core;
using Officelings.Core.Iso;
using Officelings.Core.Iso.Editing;
using Xunit;

namespace Officelings.Tests;

public sealed class IsoEditorUiBehaviorTests
{
    private static void Sta(Action action)
    {
        Exception? error=null; var thread=new Thread(()=> { try {action();} catch(Exception e) {error=e;} });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (error!=null) ExceptionDispatchInfo.Capture(error).Throw();
    }
    private static (IsoEditorSession Session,IsoEditorUiFake Editor,IsoPack Pack) Create()
    {
        var pack=IsoPack.LoadDefault(); var editor=new IsoEditorUiFake(pack);
        return (new(editor,pack,pack,new IsoEditorUiImporterFake(pack),new(Path.Combine(Path.GetTempPath(),"iso-editor-ui-"+Guid.NewGuid().ToString("N"))),new([]),pack.Layout),editor,pack);
    }
    private static IsoView View(IsoEditorSession session,IsoPack pack)
    {
        var office=new IsoOffice(pack,42) {PetsEnabled=false}; var view=new IsoView(office,new PackIsoSheetSource(pack),Path.GetTempPath());
        view.Update([],IsoEditorSmoke.Epoch,0); view.AttachEditor(session); view.SetZoom(1);
        Officelings.App.Program.RenderBitmap(view,view.Scene.Width,view.Scene.Height); return view;
    }
    [Fact]
    public void PreviewIsExactPureAndCached() => Sta(()=>
    {
        var (session,editor,pack)=Create(); var original=editor.State;
        var preview=new IsoEditPreview(original.Layout,[new(2,2)],[new("desk-1","pelvis",new(2,2),12)],[],true); editor.NextPreview=preview;
        var command=new IsoMoveCommand("desk-1",new(2,2)); session.PreviewCommand(command); session.PreviewCommand(command);
        Assert.Same(preview,session.Preview); Assert.Same(original,editor.State); Assert.Equal(1,editor.PreviewCalls); Assert.Empty(editor.Commands);
        var view=View(session,pack); Assert.Equal("✓ Valid",view.EditorOverlay!.PreviewText); view.DetachEditor();
    });
    [Fact]
    public void ModeRestoreKeepsEditorAndRuntime()
    {
        var (session,editor,_)=Create(); var runtime=new IsoEditorUiRuntimeFake(); session.BindRuntime(runtime);
        var configured=new Settings {Mode="desktop",ClickThrough=true,Borderless=true,TransparentBackground=true,TagMode="always",PrivacyMode="full"};
        var effective=session.Enter(configured); Assert.Equal("normal",effective.Mode); Assert.False(effective.ClickThrough); Assert.False(effective.Borderless);
        Assert.Equal(configured,session.Leave(effective)); Assert.Same(editor,session.Editor); Assert.Same(runtime,session.Runtime);
    }
    [Fact]
    public void KeyboardIgnoresTextAndCancelAddsNoCommand() => Sta(()=>
    {
        var (session,editor,pack)=Create(); var view=View(session,pack); session.Select("desk-1");
        Assert.False(view.HandleEditorKey(Key.Delete,ModifierKeys.None,new TextBox())); Assert.Empty(editor.Commands);
        Assert.True(view.HandleEditorKey(Key.R,ModifierKeys.None,view)); Assert.IsType<IsoRotateCommand>(Assert.Single(editor.Commands));
        session.PreviewCommand(new IsoMoveCommand("desk-1",new(3,3))); var count=editor.Commands.Count;
        view.HandleEditorKey(Key.Escape,ModifierKeys.None,view); Assert.Null(session.Pending); Assert.Equal(count,editor.Commands.Count);
        Assert.False(view.HandleEditorKey(Key.Tab,ModifierKeys.None,view)); view.DetachEditor();
    });
    [Fact]
    public void PhysicalCameraInputSelectsKnownTilesAndTargets() => Sta(()=>IsoEditorReviewCases.Run("R19"));
    [Fact]
    public void EmptySelectionNeverSelectsLegacyAttachments()
    {
        var (session,_,_)=Create(); Assert.Null(session.SelectedWall);
        var wall=session.Editor.State.Layout.WallSegments!.First(w=>w.HostId!=null);
        session.Select(wall.Id); Assert.Same(wall,session.SelectedWall);
        session.Select(null); Assert.Null(session.SelectedWall);
    }
    [Fact]
    public void PaletteSearchCoversLogicalFamilies()
    {
        var (session,editor,_)=Create(); Assert.Contains(session.Search("DESK"),a=>a.AssetId=="iso/desk");
        Assert.Contains(session.Search("iso/computer"),a=>a.AssetId=="iso/computer"); Assert.Empty(session.Search("no-such-object"));
        Assert.Equal(editor.Palette.Length,session.Search("").Count());
    }
    [Fact]
    public void ScaledControlsHaveAutomationAndReachableSaveDone() => Sta(()=>
    {
        var (session,_,pack)=Create(); using var panel=new IsoEditorPanel(session,new PackIsoSheetSource(pack),2);
        Officelings.App.Program.RenderBitmap(panel,520,900);
        Assert.Equal(SystemFonts.MessageFontSize*2,panel.FontSize); Assert.Equal(ScrollBarVisibility.Auto,panel.VerticalScrollBarVisibility);
        Assert.Contains(panel.Buttons,b=>AutomationProperties.GetName(b)=="Done editing"); Assert.Contains(panel.Buttons,b=>AutomationProperties.GetName(b)=="Save (Ctrl+S)");
        Assert.All(panel.Buttons,b=>Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(b))));
    });
    [Fact]
    public void IdlePreviewDoesNotRebuildAndDetachRemovesOverlay() => Sta(()=>
    {
        var (session,editor,pack)=Create(); var view=View(session,pack); var command=new IsoMoveCommand("desk-1",new(2,2));
        session.PreviewCommand(command); var calls=editor.PreviewCalls; var builds=view.EditorOverlay!.BuildCount;
        for (var i=0;i<100;i++) session.PreviewCommand(command);
        Assert.Equal(calls,editor.PreviewCalls); Assert.Equal(builds,view.EditorOverlay.BuildCount);
        view.DetachEditor(); Assert.Null(view.EditorOverlay); Assert.False(view.AnimationTimerEnabled); Assert.False(view.Editing);
    });
    [Fact]
    public void RejectedCommandKeepsSelectionAndCandidate()
    {
        var (session,editor,_)=Create(); session.Select("desk-1"); var original=editor.State;
        editor.NextResult=new(false,false,original,[new("L005",IsoIssueSeverity.Error,"Object is outside the room.",["desk-1"])]);
        session.PreviewCommand(new IsoMoveCommand("desk-1",new(-1,-1))); var candidate=session.Preview;
        session.Execute(new IsoMoveCommand("desk-1",new(-1,-1)));
        Assert.Same(original,editor.State); Assert.Equal("desk-1",session.Selection); Assert.Same(candidate,session.Preview);
    }
    [Fact]
    public void InvalidDraftCancelsRuntimeSwapWithoutBecomingLastValid()
    {
        var (session,editor,_)=Create(); var runtime=new IsoEditorUiRuntimeFake(); session.BindRuntime(runtime); var live=session.LastValidLayout;
        var state=editor.State with {Revision=1,IsDirty=true,Layout=live with {Door=new(9,9)}};
        session.Accept(new(true,true,state,[new("L012",IsoIssueSeverity.Error,"The iso door and spawn must remain open and walkable.",[])]));
        Assert.Same(state,Assert.Single(runtime.Applied)); Assert.Same(live,session.LastValidLayout); Assert.False(session.Applying);
    }
    [Fact]
    public void HandlePrecedesInjectedCorePicking() => Sta(()=>
    {
        var pack=IsoPack.LoadDefault(); var editor=new IsoEditorUiFake(pack); var calls=0;
        var session=new IsoEditorSession(editor,pack,pack,new IsoEditorUiImporterFake(pack),new(Path.GetTempPath()),new([]),pack.Layout)
            {HitTest=(_,_,_,_,_)=> {calls++; return new("lower-object","furniture");}};
        var view=View(session,pack); session.Select("desk-1"); var rect=view.EditorOverlay!.Handle;
        Assert.Equal("desk-1",view.EditorHit(view.ScreenPoint(new(rect.X+rect.Width/2,rect.Y+rect.Height/2)))!.TargetId);
        Assert.Equal(0,calls); view.DetachEditor();
    });
    [Fact]
    public void UnreadableBackupBlocksRemovalBeforeImporter()
    {
        var (session,_,_)=Create(); Directory.CreateDirectory(session.Paths.RootDirectory);
        try
        {
            File.WriteAllText(session.LayoutPath+".bak","{"); var importer=(IsoEditorUiImporterFake)session.Importer;
            var result=session.Remove("user/test-crate"); Assert.False(result.Succeeded); Assert.Equal(0,importer.Removals); Assert.Contains(result.Issues,i=>i.RuleId=="O011");
        }
        finally {Directory.Delete(session.Paths.RootDirectory,true);}
    }
    [Fact]
    public void PreviewOnlyNavigationErrorsRemainCommittableAndLabelled() => Sta(()=>
    {
        var (session,editor,pack)=Create(); var issue=new IsoValidationIssue("L013",IsoIssueSeverity.Error,"Interaction or seat 'desk-1' is unreachable from the door.",["desk-1"]);
        editor.NextPreview=new(editor.State.Layout,[new(2,2)],[],[issue],true); session.PreviewCommand(new IsoMoveCommand("desk-1",new(2,2)));
        var view=View(session,pack); Assert.Contains("Draft only; live office unchanged",view.EditorOverlay!.PreviewText);
        Assert.Contains(issue,session.DisplayIssues); Assert.False(session.CanSave); session.Save(); Assert.Equal(0,editor.SaveCalls);
        session.CommitPreview(); Assert.Single(editor.Commands); view.DetachEditor();
    });
}
