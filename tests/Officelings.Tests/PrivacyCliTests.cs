using System.Diagnostics;
using System.Text.Json;
using System.Threading.Channels;
using Officelings.Core;
using Xunit;

namespace Officelings.Tests;

public sealed class PrivacyCliTests : IDisposable
{
    private const string Secret="SECRET-PROMPT-123";
    private const string PrivatePath=@"C:\Users\private\proj";
    private readonly string root=Path.Combine(Path.GetTempPath(),"officelings-privacy-cli-"+Guid.NewGuid().ToString("N"));
    private static string Binary(string project,string framework)
    {
        var configuration=new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../src",project,"bin",configuration,framework,project+".dll"));
    }
    private static string TrackerDll => Binary("Officelings.Tracker","net10.0");
    public PrivacyCliTests()
    {
        var home=Path.Combine(root,"home",".codex","sessions"); Directory.CreateDirectory(home);
        var now=DateTimeOffset.UtcNow.ToString("O");
        string Line(string type,object payload) => JsonSerializer.Serialize(new { timestamp=now,type,payload })+"\n";
        File.WriteAllText(Path.Combine(home,"rollout-synthetic.jsonl"),Line("session_meta",new { id="synthetic",agent_nickname="Mira",model="private-model",cwd=PrivatePath,agent_role=Secret })+
            Line("event_msg",new { type="user_message",message=Secret })+
            Line("response_item",new { type="function_call",name=Secret }));
    }
    public void Dispose() => Directory.Delete(root,true);
    private Process Start(string output,string? option,string? environment,bool once)
    {
        Directory.CreateDirectory(output);
        var start=new ProcessStartInfo("dotnet") { UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true };
        start.ArgumentList.Add(TrackerDll);
        foreach(var argument in new[] { "--home",Path.Combine(root,"home"),"--output",output,"--scan-seconds","0.1" }) start.ArgumentList.Add(argument);
        // Runtime tests own the child's lifetime. A wall-clock exit races the
        // assertions when other parallel tests delay this test's continuation.
        if(once) start.ArgumentList.Add("--once");
        if(option!=null) { start.ArgumentList.Add("--privacy"); start.ArgumentList.Add(option); }
        if(environment==null) start.Environment.Remove("OFFICELINGS_PRIVACY"); else start.Environment["OFFICELINGS_PRIVACY"]=environment;
        return Process.Start(start)!;
    }
    private static string Read(string path)
    { using var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite | FileShare.Delete); using var reader=new StreamReader(file); return reader.ReadToEnd(); }
    private static void Safe(string text)
    { Assert.DoesNotContain(Secret,text); Assert.DoesNotContain(PrivatePath,text); Assert.DoesNotContain(JsonEncodedText.Encode(PrivatePath).ToString(),text); }
    private static async Task<JsonDocument> WaitForState(string output,Process process,Func<JsonElement,bool> ready)
    {
        var changes=Channel.CreateUnbounded<bool>();
        using var watcher=new FileSystemWatcher(output,"state.json") { NotifyFilter=NotifyFilters.FileName | NotifyFilters.LastWrite };
        watcher.Created+=(_,_)=>changes.Writer.TryWrite(true);
        watcher.Changed+=(_,_)=>changes.Writer.TryWrite(true);
        watcher.Renamed+=(_,_)=>changes.Writer.TryWrite(true);
        watcher.Error+=(_,e)=>changes.Writer.TryComplete(e.GetException());
        void Exited(object? sender,EventArgs args) => changes.Writer.TryComplete(new InvalidOperationException("Tracker exited before publishing the requested state."));
        process.EnableRaisingEvents=true;
        process.Exited+=Exited;
        using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(30));
        watcher.EnableRaisingEvents=true;
        changes.Writer.TryWrite(true); // Subscribe before reading, including an already-published state.
        try
        {
            while(await changes.Reader.ReadAsync(deadline.Token))
            {
                if(process.HasExited) throw new InvalidOperationException("Tracker exited before publishing the requested state.");
                JsonDocument state;
                try { state=JsonDocument.Parse(Read(Path.Combine(output,"state.json"))); }
                catch(FileNotFoundException) { continue; }
                if(ready(state.RootElement)) return state;
                state.Dispose();
            }
        }
        finally { process.Exited-=Exited; }
        throw new InvalidOperationException("Tracker state watcher stopped.");
    }
    private static async Task WaitFor(string output,Process process,PrivacyMode mode)
    { using var state=await WaitForState(output,process,state=>state.GetProperty("privacy").GetString()==PrivacyModes.Value(mode)); }

    [Theory]
    [InlineData("off")]
    [InlineData("tasks")]
    [InlineData("full")]
    public async Task TrackerCliRedactsSerializedFilesAndConsoleOutput(string mode)
    {
        var output=Path.Combine(root,Secret,mode); using var process=Start(output,mode,null,true);
        var stdout=process.StandardOutput.ReadToEndAsync(); var stderr=process.StandardError.ReadToEndAsync();
        Assert.True(process.WaitForExit(30000)); Assert.Equal(0,process.ExitCode);
        var state=Read(Path.Combine(output,"state.json")); var logs=await stdout+await stderr;
        using var json=JsonDocument.Parse(state); Assert.Equal(mode,json.RootElement.GetProperty("privacy").GetString());
        Assert.Single(json.RootElement.GetProperty("agents").EnumerateArray());
        if(mode=="off") { Assert.Contains(Secret,state); Assert.Contains(Secret,logs); Assert.Contains(JsonEncodedText.Encode(PrivatePath).ToString(),state); }
        else { Safe(state); Safe(logs); Safe(Read(Path.Combine(output,"runtime-layout.json"))); }
    }

    [Theory]
    [InlineData(null,null,"tasks","tasks")]
    [InlineData(null,"full","tasks","full")]
    [InlineData("off","full","tasks","off")]
    public async Task TrackerStartupUsesCliThenSavedSettingsThenEnvironment(string? option,string? setting,string environment,string expected)
    {
        var output=Path.Combine(root,"output"); Directory.CreateDirectory(output);
        if(setting!=null) StateJson.WriteAtomic(Path.Combine(output,"settings.json"),JsonSerializer.SerializeToUtf8Bytes(new { privacyMode=setting }));
        using var process=Start(output,option,environment,true);
        var stdout=process.StandardOutput.ReadToEndAsync(); var stderr=process.StandardError.ReadToEndAsync();
        Assert.True(process.WaitForExit(30000)); Assert.Equal(0,process.ExitCode); await stdout; await stderr;
        using var state=JsonDocument.Parse(Read(Path.Combine(output,"state.json"))); Assert.Equal(expected,state.RootElement.GetProperty("privacy").GetString());
    }

    [Theory]
    [InlineData("off")]
    [InlineData("tasks")]
    [InlineData("full")]
    public async Task AppPrivacyFlagRunsWithSyntheticHomeAndNonactivatingQaWindow(string mode)
    {
        var start=new ProcessStartInfo("dotnet") { UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true };
        foreach(var argument in new[] { Binary("Officelings.App","net10.0-windows"),"--qa","--home",Path.Combine(root,"home"),"--privacy",mode,"--quit-after","1" }) start.ArgumentList.Add(argument);
        start.Environment.Remove("OFFICELINGS_PRIVACY");
        using var process=Process.Start(start)!;
        var stdout=process.StandardOutput.ReadToEndAsync(); var stderr=process.StandardError.ReadToEndAsync();
        try { Assert.True(process.WaitForExit(30000)); Assert.Equal(0,process.ExitCode); Assert.Empty(await stderr); await stdout; }
        finally { if(!process.HasExited) { process.Kill(); process.WaitForExit(); } }
    }

    [Fact]
    public async Task TrackerRuntimeSettingsScrubAndMalformedSettingsCannotLowerPrivacy()
    {
        var output=Path.Combine(root,"runtime"); var settings=Path.Combine(output,"settings.json");
        using var process=Start(output,null,null,false);
        var stdout=process.StandardOutput.ReadToEndAsync(); var stderr=process.StandardError.ReadToEndAsync();
        try
        {
            await WaitFor(output,process,PrivacyMode.Off); Assert.Contains(Secret,Read(Path.Combine(output,"state.json")));
            foreach(var mode in new[] { PrivacyMode.Tasks,PrivacyMode.Full })
            {
                StateJson.WriteAtomic(settings,JsonSerializer.SerializeToUtf8Bytes(new { privacyMode=PrivacyModes.Value(mode) }));
                await WaitFor(output,process,mode); Safe(Read(Path.Combine(output,"state.json"))); Safe(Read(Path.Combine(output,"runtime-layout.json")));
            }
            using var before=JsonDocument.Parse(Read(Path.Combine(output,"state.json")));
            var sequence=before.RootElement.GetProperty("sequence").GetInt64();
            StateJson.WriteAtomic(settings,"{"u8.ToArray());
            // One publication may already be in flight when settings are replaced.
            // Two later publications prove a new tracker loop read the malformed file.
            using var heartbeat=await WaitForState(output,process,state=>state.GetProperty("sequence").GetInt64()>sequence+1);
            Assert.Equal("full",heartbeat.RootElement.GetProperty("privacy").GetString());
            Safe(heartbeat.RootElement.GetRawText());
            StateJson.WriteAtomic(settings,JsonSerializer.SerializeToUtf8Bytes(new { privacyMode="off" }));
            await WaitFor(output,process,PrivacyMode.Off); Safe(Read(Path.Combine(output,"state.json")));
        }
        finally { if(!process.HasExited) { process.Kill(); process.WaitForExit(); } }
        await stdout; await stderr;
    }
}
