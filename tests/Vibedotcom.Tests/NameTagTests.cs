using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Vibedotcom.App;
using Vibedotcom.Core;
using Xunit;

namespace Vibedotcom.Tests;

public sealed class NameTagTests
{
    private static readonly DateTimeOffset Epoch = DateTimeOffset.Parse("2026-10-04T00:00:00Z");
    private static DateTimeOffset At(int ms) => Epoch.AddMilliseconds(ms);
    private static void Sta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() => { try { action(); } catch (Exception e) { error = e; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (error != null) ExceptionDispatchInfo.Capture(error).Throw();
    }
    private static AgentView Person(string id="parent",double x=7,double y=9,AgentState state=AgentState.Idle,string? parent=null) =>
        new(new(id,Provider.Codex,parent,parent!=null,"Agent "+id,null,"demo-model","C:/Demo/Office","Invented test task",null,null,state,Epoch,Epoch,false),
            new(x,y,new(3,6),"idle","down",0,0,true,Sprite:new("fallback",0,0,0,0,16,24,8,24,1),DeskId:"desk",DisplayName:"Agent "+id));
    private static OfficeView View(string mode="hover",bool desk=false,IReadOnlyList<AgentView>? people=null)
    {
        var view=new OfficeView(new AssetPack(),Path.Combine(Path.GetTempPath(),"vibedotcom-missing-test-brand")) { TagMode=mode };
        view.SetZoom(2);
        var layout=new OfficeLayout(24,16,16,new(1,1),desk ? [new("desk",2,4,3,2,"parent",Id:"desk",DeskId:"desk")] : [],
            Tiles:Enumerable.Repeat(1,24*16).ToArray());
        view.Update(layout,people ?? [Person()],Epoch); return view;
    }
    private static BitmapSource Render(OfficeView view) => Vibedotcom.App.Program.RenderBitmap(view,800,544);
    private static byte[] Pixel(BitmapSource bitmap,int x,int y)
    {
        var bytes=new byte[4]; new FormatConvertedBitmap(bitmap,PixelFormats.Bgra32,null,0).CopyPixels(new Int32Rect(x,y,1,1),bytes,4,0); return bytes;
    }
    private static Point Center(Rect rect) => new(rect.X+rect.Width/2,rect.Y+rect.Height/2);
    private static bool RegionDiffers(BitmapSource a,BitmapSource b,Rect rect)
    {
        if(rect.IsEmpty) return false;
        var x=(int)rect.Left; var y=(int)rect.Top;
        var width=(int)Math.Ceiling(rect.Right)-x; var height=(int)Math.Ceiling(rect.Bottom)-y;
        if(width<=0 || height<=0) return false;
        var region=new Int32Rect(x,y,width,height); var stride=width*4;
        var left=new byte[stride*height]; var right=new byte[left.Length];
        new FormatConvertedBitmap(a,PixelFormats.Bgra32,null,0).CopyPixels(region,left,stride,0);
        new FormatConvertedBitmap(b,PixelFormats.Bgra32,null,0).CopyPixels(region,right,stride,0);
        return !left.AsSpan().SequenceEqual(right);
    }

    [Theory]
    [InlineData("{}","hover")]
    [InlineData("{\"tagMode\":\"hover\"}","hover")]
    [InlineData("{\"tagMode\":\"icon\"}","icon")]
    [InlineData("{\"tagMode\":\"always\"}","always")]
    [InlineData("{\"tagMode\":\"unknown\"}","hover")]
    [InlineData("{\"tagMode\":null}","hover")]
    [InlineData("{\"tagMode\":42}","hover")]
    public void SettingsParsingAndPersistence(string json,string expected)
    {
        var folder=Path.Combine(Path.GetTempPath(),"vibedotcom-tags-settings-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        try
        {
            var path=Path.Combine(folder,"settings.json"); File.WriteAllText(path,json);
            var settings=Settings.Read(path); Assert.Equal(expected,settings.TagMode);
            settings.Write(path); Assert.Equal(settings,Settings.Read(path));
            Assert.Equal(expected,JsonDocument.Parse(File.ReadAllText(path)).RootElement.GetProperty("tagMode").GetString());
            Assert.Empty(Directory.GetFiles(folder,"*.tmp"));
            Assert.Equal("hover",new Settings { TagMode="invalid" }.Normalize().TagMode);
        }
        finally { Directory.Delete(folder,true); }
    }
    [Fact]
    public void CliAcceptsExactlyTheThreeModes()
    {
        foreach(var mode in new[] { "hover","icon","always" }) Assert.Equal(mode,Vibedotcom.App.Program.ParseTags(mode));
        Assert.Throws<ArgumentException>(()=>Vibedotcom.App.Program.ParseTags("wrong"));
    }
    [Fact]
    public void HoverEnterLeaveGraceFadeAndReentryAreContinuous()
    {
        var hover=new NameTagHover(); hover.Update("parent",At(0)); Assert.Equal(0,hover.Opacity("parent")); Assert.True(hover.Animating);
        hover.Advance(At(60)); Assert.Equal(.5,hover.Opacity("parent"),6);
        hover.Advance(At(120)); Assert.Equal(1,hover.Opacity("parent")); Assert.False(hover.Animating);
        hover.Update(null,At(150)); hover.Advance(At(209)); Assert.Equal(1,hover.Opacity("parent"));
        hover.Advance(At(310)); Assert.Equal(.5,hover.Opacity("parent"),6);
        hover.Update("parent",At(310)); Assert.Equal(.5,hover.Opacity("parent"),6);
        hover.Advance(At(370)); Assert.Equal(1,hover.Opacity("parent"));
        hover.Update("child",At(400)); hover.Advance(At(520));
        Assert.Equal(.7,hover.Opacity("parent"),6); Assert.Equal(1,hover.Opacity("child"));
        hover.Advance(At(660)); Assert.Equal(0,hover.Opacity("parent")); Assert.False(hover.Animating);
        hover.Update(null,At(700)); hover.Advance(At(960)); Assert.Equal(0,hover.Opacity("child")); Assert.False(hover.Animating);
    }
    [Theory]
    [InlineData("hover")]
    [InlineData("icon")]
    [InlineData("always")]
    public void PlateAndIconVisibilityUseActualOffscreenPixels(string mode) => Sta(()=>
    {
        var view=View(mode); var baseline=Render(view); var person=view.Agents[0];
        Assert.Equal(mode=="always",!view.AgentTagRect(person.Agent.Id).IsEmpty);
        Assert.Equal(mode=="icon",!view.AgentIconRect(person.Agent.Id).IsEmpty);
        if(mode=="icon")
        {
            var empty=View("hover"); var withoutIcon=Render(empty);
            Assert.True(RegionDiffers(baseline,withoutIcon,view.AgentIconRect(person.Agent.Id)));
        }
        var point=view.AgentPoint(person); view.SimulateHover(point,At(0)); view.SimulateHover(point,At(120));
        var hovered=Render(view); var plate=view.AgentTagRect(person.Agent.Id); Assert.False(plate.IsEmpty);
        Assert.True(view.AgentIconRect(person.Agent.Id).IsEmpty);
        var sample=new Point(plate.Right-4,plate.Top+4);
        Assert.Equal(new byte[] { 0x30,0x2b,0x17,255 },Pixel(hovered,(int)sample.X,(int)sample.Y));
        if(mode!="always") Assert.True(RegionDiffers(baseline,hovered,plate));
        view.SimulateHover(null,At(150)); view.AdvanceHover(At(410)); Render(view);
        Assert.Equal(mode=="always",!view.AgentTagRect(person.Agent.Id).IsEmpty);
        Assert.Equal(mode=="icon",!view.AgentIconRect(person.Agent.Id).IsEmpty);
    });
    [Fact]
    public void BodyPlateBridgeTooltipDelayAndHoverFramesDoNotRedrawScene() => Sta(()=>
    {
        var view=View(); Render(view); var point=view.AgentPoint(view.Agents[0]); var scene=view.SceneRenderCount; var room=view.RoomBuildCount;
        view.SimulateHover(point,At(0)); view.AdvanceHover(At(60));
        view.AdvanceHover(At(120)); var plate=view.AgentTagRect("parent"); Assert.False(plate.IsEmpty);
        view.SimulateHover(new(point.X,plate.Bottom+3),At(140)); Assert.False(view.AgentTagRect("parent").IsEmpty);
        view.SimulateHover(Center(plate),At(200)); Assert.Null(view.DebugTooltipText);
        view.SimulateHover(Center(plate),At(419)); Assert.Null(view.DebugTooltipText); Assert.True(view.TooltipPending);
        view.SimulateHover(Center(plate),At(420)); Assert.NotNull(view.DebugTooltipText); Assert.False(view.TooltipPending);
        Assert.False(view.TooltipRect.IntersectsWith(view.AgentTagRect("parent")));
        Assert.Equal(scene,view.SceneRenderCount); Assert.Equal(room,view.RoomBuildCount);
        view.SimulateHover(null,At(430)); Assert.Null(view.DebugTooltipText);
        view.SimulateHover(point,At(450)); view.AdvanceHover(At(470)); Assert.False(view.AgentTagRect("parent").IsEmpty);
        view.TagMode="always"; Assert.False(view.AgentTagRect("parent").IsEmpty);
        view.TagMode="bad"; Assert.Equal("hover",view.TagMode); Assert.True(view.AgentTagRect("parent").IsEmpty);
    });
    [Theory]
    [InlineData("hover")]
    [InlineData("icon")]
    [InlineData("always")]
    public void DeskReservationOnlyShowsForAwayOwnerAndItsOwnHover(string mode) => Sta(()=>
    {
        var view=View(mode,desk:true); var baseline=Render(view);
        Assert.Equal(mode=="always",!view.DeskTagRect("desk").IsEmpty);
        Assert.Equal(mode=="icon",!view.DeskIconRect("desk").IsEmpty);
        var deskPoint=new Point(16+(2+1.5)*32,16+(4+1)*32);
        view.SimulateHover(deskPoint,At(0)); view.SimulateHover(deskPoint,At(120));
        var hovered=Render(view); var plate=view.DeskTagRect("desk"); Assert.False(plate.IsEmpty);
        if(mode!="always") { Assert.True(view.AgentTagRect("parent").IsEmpty); Assert.True(RegionDiffers(baseline,hovered,plate)); }
        view.SimulateHover(Center(plate),At(200)); Assert.False(view.DeskTagRect("desk").IsEmpty);
        view.SimulateHover(null,At(220)); view.AdvanceHover(At(480)); Assert.Equal(mode=="always",!view.DeskTagRect("desk").IsEmpty);
        foreach(var state in new[] { AgentState.Working,AgentState.Thinking,AgentState.Waiting })
        {
            var working=Person(x:3,y:6,state:state); view.Update(view.Layout,[working],At(500)); Render(view);
            Assert.True(view.DeskTagRect("desk").IsEmpty); Assert.True(view.DeskIconRect("desk").IsEmpty);
            view.SimulateHover(deskPoint,At(500)); view.SimulateHover(deskPoint,At(620)); Assert.True(view.DeskTagRect("desk").IsEmpty);
        }
        view.Update(view.Layout,[Person() with { Pose=Person().Pose with { SeatPhase="seated" } }],At(700)); Render(view);
        Assert.True(view.DeskTagRect("desk").IsEmpty);
        var unassigned=view.Layout with { Furniture=view.Layout.Furniture.Select(f=>f with { AgentId=null }).ToArray() };
        view.Update(unassigned,[Person()],At(800)); Render(view); Assert.True(view.DeskTagRect("desk").IsEmpty);
    });
    [Fact]
    public void ParentChildFadePlatesStackAndEditorHonorsMode() => Sta(()=>
    {
        var parent=Person(x:7,y:9); var child=Person("child",x:8,y:9,parent:"parent");
        var view=View(people:[parent,child]); Render(view);
        view.SimulateHover(view.AgentPoint(parent),At(0)); view.AdvanceHover(At(120));
        view.SimulateHover(view.AgentPoint(child),At(150)); view.AdvanceHover(At(270));
        var p=view.AgentTagRect("parent"); var c=view.AgentTagRect("child");
        Assert.False(p.IsEmpty); Assert.False(c.IsEmpty); p.Inflate(1,1); c.Inflate(1,1); Assert.False(p.IntersectsWith(c));
        var childPlate=view.AgentTagRect("child");
        view.SimulateHover(Center(childPlate),At(430));
        Assert.True(view.AgentTagRect("parent").IsEmpty); Assert.Equal(childPlate,view.AgentTagRect("child"));
        view.EditorMode=true; view.TagMode="icon"; Render(view);
        Assert.True(view.AgentTagRect("parent").IsEmpty); Assert.False(view.AgentIconRect("parent").IsEmpty);
        view.SimulateHover(view.AgentPoint(parent),At(300)); view.SimulateHover(view.AgentPoint(parent),At(720));
        Assert.False(view.AgentTagRect("parent").IsEmpty); Assert.Null(view.DebugTooltipText);
        view.TagMode="always"; Render(view); Assert.Equal(2,view.RenderedAgentTags);
        p=view.AgentTagRect("parent"); c=view.AgentTagRect("child"); Assert.False(p.IntersectsWith(c));
    });
}
