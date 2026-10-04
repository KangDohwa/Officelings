using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Vibedotcom.App;
using Vibedotcom.Core;
using Vibedotcom.Core.Iso;

// Same finite markers and external collector as AppHarness; product DLLs are
// supplied explicitly so a baseline run cannot accidentally rebuild the app.
internal static class IsoHarness
{
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll")] private static extern bool GetProcessTimes(nint process,out long created,out long exited,out long kernel,out long user);
    [DllImport("user32.dll")] private static extern int GetWindowLong(nint hwnd,int index);
    private static double Cpu() => GetProcessTimes(new(-1),out _,out _,out var kernel,out var user) ? (kernel+user)/10000000d : throw new InvalidOperationException("CPU unavailable.");
    private static void Write(string path,object value) => File.WriteAllText(path,JsonSerializer.Serialize(value,new JsonSerializerOptions { WriteIndented=true }));
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            var command=args[0]; var output=Path.GetFullPath(args[1]); Directory.CreateDirectory(output);
            if(command=="tag-renders") return Tags(output);
            var count=int.Parse(args[2]); var mode=args[3]; var warmup=double.Parse(args[4]); var seconds=double.Parse(args[5]);
            var zoom=command.EndsWith("2x",StringComparison.Ordinal) ? 2 : 1; var demo=command.Contains("demo",StringComparison.Ordinal);
            Write(Path.Combine(output,"runtime.json"),new { command,runtime=RuntimeInformation.FrameworkDescription,app=Identity(typeof(IsoView)),core=Identity(typeof(IsoOffice)) });
            var app=new Application(); var pack=IsoPack.LoadDefault(); var office=new IsoOffice(pack,42);
            var people=count==0 ? Array.Empty<AgentInfo>() : IsoSmoke.TwelveAgents.ToArray();
            var now=DateTimeOffset.UtcNow;
            for(var i=0;i<=600;i++) office.Update(people,now.AddSeconds(-30+i*.05),i==0 ? 0 : .05);
            // Discard live warmup, then demo starts at logical 0 and runs 24 s.
            // IsoWindow's own one-second input maintenance drives the demo.
            var epoch=DateTimeOffset.UtcNow.AddSeconds(warmup);
            using var storage=new AppStorage(true,Path.Combine(output,"synthetic-app"));
            var settings=new Settings { View="iso",Mode="normal",Borderless=true,TransparentBackground=mode=="transparent",Zoom=zoom,Width=720,Height=480,TagMode="hover" };
            var window=new IsoWindow(office,new PackIsoSheetSource(pack),settings,demo,true,0,storage,storage.Home,false,people,epoch);
            var fields=typeof(IsoWindow).GetFields(BindingFlags.Instance|BindingFlags.NonPublic);
            fields.Single(f=>f.Name=="nextPoll").SetValue(window,DateTimeOffset.MaxValue);
            var nativeModes=new WindowModes(window,false,(nint?)0);
            fields.Single(f=>f.Name=="modes").SetValue(window,nativeModes);
            var view=window.View; var timerFields=fields.Where(f=>f.FieldType==typeof(DispatcherTimer)).ToArray();
            long callbacks=0; foreach(var f in timerFields) ((DispatcherTimer)f.GetValue(window)!).Tick+=(_,_)=>callbacks++;
            ((DispatcherTimer)typeof(IsoView).GetField("animation",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(view)!).Tick+=(_,_)=>callbacks++;
            var warm=new DispatcherTimer { Interval=TimeSpan.FromSeconds(Math.Max(.001,warmup)) };
            var end=new DispatcherTimer { Interval=TimeSpan.FromSeconds(seconds) };
            var clock=new Stopwatch(); double startCpu=0; long allocated=0,startTicks=0; int renders=0,tags=0,statics=0,animations=0;
            var gc=new int[3]; var started=false;
            window.ContentRendered+=(_,_)=>
            {
                if(started)return; started=true;
                var hwnd=new WindowInteropHelper(window).Handle;
                Write(Path.Combine(output,"ready.json"),new { pid=Environment.ProcessId,mode,zoom,allowsTransparency=window.AllowsTransparency,
                    noActivate=(GetWindowLong(hwnd,-20)&0x08000000)!=0,width=window.Width,height=window.Height,fakeHome=storage.Home });
                warm.Start();
            };
            warm.Tick+=(_,_)=>
            {
                warm.Stop(); startCpu=Cpu(); allocated=GC.GetTotalAllocatedBytes(true); startTicks=callbacks;
                renders=view.SceneRenderCount; tags=view.TagRenderCount; statics=view.StaticBuildCount; animations=view.AnimationTickCount;
                for(var i=0;i<3;i++)gc[i]=GC.CollectionCount(i);
                clock.Start(); Write(Path.Combine(output,"warm.json"),new {pid=Environment.ProcessId,uiThreadId=GetCurrentThreadId(),cpuSeconds=startCpu}); end.Start();
            };
            end.Tick+=(_,_)=>
            {
                end.Stop(); var wall=clock.Elapsed.TotalSeconds; var cpu=Cpu()-startCpu;
                Write(Path.Combine(output,"external-target.json"),new { count,mode,zoom,fixture=demo ? "iso-demo-0-to-24s" : count==0 ? "iso-empty-with-TV-and-pets" : "iso-twelve-working",warmupSeconds=warmup,sampleSeconds=seconds,
                    measuredWallSeconds=wall,cpuSeconds=cpu,cpuOneCorePercent=100*cpu/wall,cpuMachinePercent=100*cpu/wall/Environment.ProcessorCount,logicalProcessors=Environment.ProcessorCount,
                    allocatedBytes=GC.GetTotalAllocatedBytes(true)-allocated,appScheduledWakeups=callbacks-startTicks,
                    counters=new {SceneRenderCount=view.SceneRenderCount-renders,TagRenderCount=view.TagRenderCount-tags,StaticBuildCount=view.StaticBuildCount-statics,AnimationTickCount=view.AnimationTickCount-animations},
                    gcCollections=Enumerable.Range(0,3).Select(i=>GC.CollectionCount(i)-gc[i]).ToArray(),visible=view.Scene.Labels.Count,
                    note="Unmodified product IsoWindow/office/view, synthetic data, offscreen nonactivating window. Default TV/pets retained. Source polling disabled; demo uses product maintenance. Two finite target markers; collector queries CPU/memory externally. One core=100%." });
                // WPF teardown can wait on the render thread of a layered HWND.
                // The finite marker is already flushed; terminate only this
                // disposable owned fixture, with no shutdown CPU in the sample.
                Environment.Exit(0);
            };
            window.Show(); return app.Run();
        }
        catch(Exception error) { Console.Error.WriteLine(error); return 1; }
    }
    private static object Identity(Type type) => new {path=type.Assembly.Location,mvid=type.Module.ModuleVersionId,sha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(type.Assembly.Location)))};
    private static int Tags(string output)
    {
        _=new Application(); var pack=IsoPack.LoadDefault(); var office=new IsoOffice(pack,42);
        var sheets=new PackIsoSheetSource(pack); var people=IsoSmoke.TwelveAgents;
        var view=new IsoView(office,sheets,Path.Combine(output,"synthetic-brand")) {TagMode="always"};
        var epoch=DateTimeOffset.Parse("2026-10-04T00:00:00Z"); var elapsed=0d;
        var records=new System.Collections.Generic.List<object>();
        foreach(var moment in new[]{.5,2,4,20})
        {
            while(elapsed+1e-9<moment) { var step=Math.Min(.05,moment-elapsed); elapsed+=step; view.Update(people,epoch.AddSeconds(elapsed),step); }
            var scene=view.Scene; var name=$"arrival-{moment:00.0}s";
            view.SetZoom(1); IsoApplication.Render(view,scene.Width,scene.Height,Path.Combine(output,name+".png"));
            var tags=scene.Labels.Select(l=>new {l.AgentId,rect=view.AgentTagRect(l.AgentId)}).ToArray();
            var bodies=scene.Items.Where(i=>i.Layer==IsoLayer.Character).Select(i=>
            {
                var cell=sheets.GetCell(i.SheetId,i.Clip,i.Frame); var pixels=new byte[cell.Width*cell.Height*4];
                new System.Windows.Media.Imaging.FormatConvertedBitmap(cell.Image,System.Windows.Media.PixelFormats.Bgra32,null,0).CopyPixels(pixels,cell.Width*4,0);
                return new {i.InstanceId,i.X,i.Y,cell.Width,cell.Height,alpha=Convert.ToBase64String(pixels.Where((_,n)=>n%4==3).ToArray())};
            }).ToArray();
            records.Add(new {name,scene.Width,scene.Height,tags,bodies,visible=scene.Labels.Count,rendered=view.RenderedAgentTags});
        }
        Write(Path.Combine(output,"tags.json"),records); return 0;
    }
}
