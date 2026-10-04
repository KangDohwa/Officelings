using System.Diagnostics;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Vibedotcom.App;
using Vibedotcom.Core;
using Xunit;

namespace Vibedotcom.Tests;

public sealed class PrivacyTests : IDisposable
{
    private const string Secret = "SECRET-PROMPT-123";
    private const string PrivatePath = @"C:\Users\private\proj";
    private readonly string root = Path.Combine(Path.GetTempPath(),"vibedotcom-privacy-test-"+Guid.NewGuid().ToString("N"));
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
    public PrivacyTests() => Directory.CreateDirectory(root);
    public void Dispose() => Directory.Delete(root,true);
    private string Codex(string id="parent")
    {
        var directory=Path.Combine(root,".codex","sessions"); Directory.CreateDirectory(directory);
        var path=Path.Combine(directory,"rollout-"+id+".jsonl");
        string Line(string type,object payload) => JsonSerializer.Serialize(new { type,timestamp=Now.ToString("O"),payload })+"\n";
        File.WriteAllText(path,Line("session_meta",new { id,agent_nickname="Mira",agent_role=Secret,model="private-model",cwd=PrivatePath })+
            Line("event_msg",new { type="user_message",message=Secret })+
            Line("response_item",new { type="function_call",name=Secret }));
        File.SetLastWriteTimeUtc(path,Now.UtcDateTime); return path;
    }
    private static AgentView View(AgentInfo agent) => new(agent,new(7,9,new(3,6),"typing","down",0,0,true,
        Sprite:new("fallback",0,0,0,0,16,24,8,24,1),DisplayName:agent.Name));
    private static OfficeLayout Layout(AgentInfo agent) => new(24,16,16,new(1,1),
        [new("desk",2,4,3,2,agent.Id,Id:"desk",DeskId:"desk")],Tiles:Enumerable.Repeat(1,24*16).ToArray());
    private static string Serialized(AgentInfo agent,PrivacyMode mode) => Encoding.UTF8.GetString(JsonSerializer.SerializeToUtf8Bytes(
        new StateSnapshot(StateJson.SchemaVersion,Now,1,SpriteAtlas.Info,Layout(agent),[View(agent)],[],Privacy:mode),StateJson.Options));
    private static void Private(string text)
    { Assert.DoesNotContain(Secret,text); Assert.DoesNotContain(PrivatePath,text); Assert.DoesNotContain(JsonEncodedText.Encode(PrivatePath).ToString(),text); }
    private static void Sta(Action action)
    {
        Exception? error=null; var thread=new Thread(()=> { try { action(); } catch(Exception e) { error=e; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if(error!=null) ExceptionDispatchInfo.Capture(error).Throw();
    }

    [Theory]
    [InlineData(PrivacyMode.Off)]
    [InlineData(PrivacyMode.Tasks)]
    [InlineData(PrivacyMode.Full)]
    public void SourceRedactionPrecedesAgentSnapshotsAndSerialization(PrivacyMode mode)
    {
        Codex(); using var tracker=new AgentTracker(new() { Home=root,PrivacyMode=mode });
        var agent=Assert.Single(tracker.Poll(Now)); var json=Serialized(agent,mode);
        Assert.Contains("\"privacy\": \""+PrivacyModes.Value(mode)+"\"",json);
        Assert.Equal(AgentState.Working,agent.State);
        if(mode==PrivacyMode.Off)
        { Assert.Contains(Secret,json); Assert.Contains(JsonEncodedText.Encode(PrivatePath).ToString(),json); Assert.Equal(PrivatePath,agent.Cwd); }
        else
        {
            Private(json); Private(OfficeView.TooltipText(View(agent),Now));
            Assert.Empty(agent.Task); Assert.Null(agent.Role); Assert.Null(agent.Tool); Assert.Null(agent.ToolKind);
            Assert.StartsWith("private:",agent.Id);
            Assert.Equal(mode==PrivacyMode.Tasks ? "proj" : null,agent.Cwd);
            Assert.Equal(mode==PrivacyMode.Tasks ? "private-model" : null,agent.Model);
            Assert.Equal(mode==PrivacyMode.Tasks ? "Mira" : "Codex",agent.Name);
        }
    }

    [Fact]
    public void TextSettersNeverKeepPrivateValuesAndPromptDerivedTitlesAreHidden()
    {
        foreach(var mode in new[] { PrivacyMode.Tasks,PrivacyMode.Full })
        {
            var redactor=new PrivacyRedactor(mode);
            foreach(var provider in Enum.GetValues<Provider>())
            {
                var agent=new TrackedAgent(provider.ToString(),provider,redactor)
                { Name=provider==Provider.Antigravity ? Secret : "Mira",Task=Secret,Role=Secret,Tool=Secret,ToolKind=Secret,Cwd=PrivatePath,Model="private-model" };
                Assert.Empty(agent.Task); Assert.Null(agent.Tool); Assert.Null(agent.ToolKind); Assert.Null(agent.Role);
                Private(JsonSerializer.Serialize(agent.Snapshot(Now,new()),StateJson.Options));
                Assert.Equal(mode==PrivacyMode.Tasks && provider!=Provider.Antigravity ? "Mira" : provider.ToString(),agent.Name);
            }
        }
    }

    [Fact]
    public void SwitchingScrubsHeldValuesAndDoesNotResurrectOldLogText()
    {
        Codex(); using var tracker=new AgentTracker(new() { Home=root,ScanInterval=TimeSpan.FromMilliseconds(1) });
        Assert.Contains(Secret,Serialized(Assert.Single(tracker.Poll(Now)),PrivacyMode.Off));
        tracker.SetPrivacy(PrivacyMode.Tasks); var tasks=Assert.Single(tracker.Poll(Now.AddMilliseconds(2))); Private(Serialized(tasks,PrivacyMode.Tasks));
        tracker.SetPrivacy(PrivacyMode.Full); var full=Assert.Single(tracker.Poll(Now.AddMilliseconds(3))); Assert.Equal("Codex",full.Name); Assert.Null(full.Cwd);
        foreach(var mode in new[] { PrivacyMode.Off,PrivacyMode.Tasks,PrivacyMode.Full,PrivacyMode.Off })
        {
            tracker.SetPrivacy(mode); var agent=Assert.Single(tracker.Poll(Now.AddMilliseconds(4)));
            Private(Serialized(agent,mode)); Assert.Null(agent.Model); Assert.Null(agent.Cwd); Assert.Equal("Codex",agent.Name);
        }
        var second=Codex("second"); tracker.SetPrivacy(PrivacyMode.Full);
        var agents=tracker.Poll(Now.AddMinutes(1));
        Assert.Contains(agents,a=>a.Name=="Codex"); Assert.Contains(agents,a=>a.Name=="Codex #2");
        var labels=agents.ToDictionary(a=>a.Id,a=>a.Name);
        tracker.SetPrivacy(PrivacyMode.Tasks); tracker.SetPrivacy(PrivacyMode.Full);
        Assert.All(tracker.Poll(Now.AddMinutes(1)),a=>Assert.Equal(labels[a.Id],a.Name));
    }

    [Theory]
    [InlineData("off")]
    [InlineData("tasks")]
    [InlineData("full")]
    public void SettingsPersistAndStartupPrecedenceIsExplicit(string mode)
    {
        var path=Path.Combine(root,"settings.json"); new Settings { PrivacyMode=mode }.Write(path);
        Assert.Equal(mode,Settings.Read(path).PrivacyMode); Assert.Equal(mode,PrivacyModes.ReadSetting(path));
        Assert.Equal(PrivacyMode.Full,PrivacyModes.Resolve("full",mode,"tasks"));
        Assert.Equal(PrivacyModes.Parse(mode),PrivacyModes.Resolve(null,mode,"full"));
        Assert.Equal(PrivacyMode.Tasks,PrivacyModes.Resolve(null,null,"tasks"));
        Assert.Equal(PrivacyMode.Off,PrivacyModes.Resolve(null,null,null));
        Assert.Throws<ArgumentException>(()=>PrivacyModes.Parse(Secret));
    }

    [Fact]
    public void RedactedFilenameIdentityStaysOpaqueAfterReturningToOff()
    {
        Codex(Secret); using var tracker=new AgentTracker(new() { Home=root,ScanInterval=TimeSpan.FromMilliseconds(1) });
        Assert.Contains(Secret,Serialized(Assert.Single(tracker.Poll(Now)),PrivacyMode.Off));
        tracker.SetPrivacy(PrivacyMode.Full); var id=Assert.Single(tracker.Poll(Now.AddMilliseconds(2))).Id;
        tracker.SetPrivacy(PrivacyMode.Off); var agent=Assert.Single(tracker.Poll(Now.AddMilliseconds(3)));
        Assert.Equal(id,agent.Id); Private(Serialized(agent,PrivacyMode.Off));
    }

    [Theory]
    [InlineData(PrivacyMode.Off)]
    [InlineData(PrivacyMode.Tasks)]
    [InlineData(PrivacyMode.Full)]
    public async Task PublicationDiagnosticsDoNotPrintHiddenText(PrivacyMode mode)
    {
        var messages=new List<string>(); var attempts=0;
        await StateJson.PublishWithRetryAsync(()=> { if(attempts++==0) throw new IOException(Secret+" "+PrivatePath); },messages.Add,privacy:mode);
        var output=Assert.Single(messages);
        if(mode==PrivacyMode.Off) { Assert.Contains(Secret,output); Assert.Contains(PrivatePath,output); } else Private(output);
        Assert.Equal(2,attempts);
    }

    [Fact]
    public void RuntimeMenusScrubTooltipAndTagCachesBeforeReturning() => Sta(()=>
    {
        Codex(); using var tracker=new AgentTracker(new() { Home=root }); var agents=tracker.Poll(Now);
        using var storage=new AppStorage(false,Path.Combine(root,"app")); var pack=new AssetPack();
        var window=new OfficeWindow(initial:new Settings { PrivacyMode="off",TagMode="always",Pets="off",Left=-30000,Top=-30000 },
            assets:pack,liveAgents:agents,storage:storage,trackerHome:root);
        try
        {
            var ownedView=window.PrivacyView;
            ownedView.Measure(new Size(800,544)); ownedView.Arrange(new Rect(0,0,800,544)); ownedView.UpdateLayout();
            var bitmap=new RenderTargetBitmap(800,544,96,96,System.Windows.Media.PixelFormats.Pbgra32); bitmap.Render(ownedView);
            foreach(var mode in new[] { PrivacyMode.Tasks,PrivacyMode.Full,PrivacyMode.Off,PrivacyMode.Full })
            {
                window.SetPrivacy(mode); var view=window.PrivacyView;
                Assert.Equal(mode,window.PrivacyLevel);
                Assert.Equal(PrivacyModes.Value(mode),Settings.Read(storage.SettingsPath).PrivacyMode);
                Private(JsonSerializer.Serialize(view.Agents,StateJson.Options));
                Assert.All(view.Agents,a=> { Private(view.TagText(a)); Private(OfficeView.TooltipText(a,Now)); });
                var textCache=typeof(OfficeView).GetField("textCache",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(view)!;
                foreach(var key in ((System.Collections.IDictionary)textCache).Keys) Private(key.ToString()!);
                Assert.Null(view.DebugTooltipText);
            }
            Assert.Contains(window.ContextMenu.Items.Cast<MenuItem>(),m=>Equals(m.Header,"Privacy"));
            var privacyMenu=window.ContextMenu.Items.Cast<MenuItem>().Single(m=>Equals(m.Header,"Privacy"));
            ((MenuItem)privacyMenu.Items[1]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.Equal(PrivacyMode.Tasks,window.PrivacyLevel); Assert.Equal("tasks",Settings.Read(storage.SettingsPath).PrivacyMode);
            using var tray=(IDisposable)typeof(OfficeWindow).GetMethod("BuildTrayMenu",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,null)!;
            var items=(System.Collections.IEnumerable)tray.GetType().GetProperty("Items")!.GetValue(tray)!;
            Assert.Contains(items.Cast<object>(),m=>Equals(m.GetType().GetProperty("Text")!.GetValue(m),"Privacy"));
        }
        finally { window.Close(); }
    });

    [Fact]
    public void OffscreenTooltipAndTagRendersForAllThreeModes() => Sta(()=>
    {
        var output=Path.Combine(Path.GetTempPath(),"vibedotcom-privacy"); Directory.CreateDirectory(output);
        foreach(var mode in Enum.GetValues<PrivacyMode>())
        {
            var redactor=new PrivacyRedactor(mode);
            var raw=new AgentInfo("antigravity:synthetic",Provider.Antigravity,null,false,Secret,null,"private-model",PrivatePath,Secret,Secret,"other",AgentState.Working,Now,Now,false);
            var agent=redactor.Redact(raw); var person=View(agent); var layout=Layout(agent);
            var view=new OfficeView(new AssetPack(),Path.Combine(root,"empty-brand")) { PrivacyMode=mode,TagMode="always",PetsEnabled=false }; view.SetZoom(2);
            view.Update(layout,[person],Now); Save(Program.RenderBitmap(view,800,544),Path.Combine(output,PrivacyModes.Value(mode)+"-tag.png"));
            var point=view.AgentPoint(person); view.SimulateHover(point,Now); view.SimulateHover(point,Now.AddMilliseconds(420));
            Save(Program.RenderBitmap(view,800,544),Path.Combine(output,PrivacyModes.Value(mode)+"-tooltip.png"));
            var text=Assert.IsType<string>(view.DebugTooltipText); var tag=view.TagText(person);
            if(mode==PrivacyMode.Off) { Assert.Contains(Secret,text); Assert.Contains(PrivatePath,text); Assert.Contains(Secret,tag); }
            else { Private(text); Private(tag); }
            File.WriteAllText(Path.Combine(output,PrivacyModes.Value(mode)+"-tooltip.txt"),text);
            File.WriteAllText(Path.Combine(output,PrivacyModes.Value(mode)+"-tag.txt"),tag);
            StateJson.WriteAtomic(Path.Combine(output,PrivacyModes.Value(mode)+"-state.json"),Encoding.UTF8.GetBytes(Serialized(agent,mode)));
        }
    });
    private static void Save(BitmapSource bitmap,string path)
    { var encoder=new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var file=File.Create(path); encoder.Save(file); }
}
