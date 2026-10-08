using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Officelings.Core.Iso;
using Officelings.Core.Iso.Editing;

namespace Officelings.App;

internal static class IsoEditorMeasurements
{
    internal sealed record Result(int ObjectCount,int SeatCount,int Operations,double CommandP95Ms,double PreviewP95Ms,
        double OpenIdleBytesPerFrame,double ClosedIdleBytesPerFrame,double CachedPreviewBytesPerCall,double ImportMs,string Runtime,int Processors);
    private static double P95(double[] values) { Array.Sort(values); return values[(int)Math.Ceiling(values.Length*.95)-1]; }
    internal static Result Run(IsoPack bundled,string root,string fixture)
    {
        var session=IsoEditorAcceptance.Create(bundled,Path.Combine(root,"latency")); var editor=session.Editor;
        IsoEditorAcceptance.Require(editor.State.Layout.Furniture.Count==34 && editor.State.Layout.WorkSeats!.Count==12,"Performance fixture changed.");
        var seats=editor.State.Layout.WorkSeats!.ToArray(); var commands=new double[100]; var previews=new double[100];
        editor.Validate(); editor.Preview(new IsoMoveCommand(seats[0].DeskId,new(4,4)));
        for (var k=0;k<100;k++)
        {
            var seat=seats[(k/2)%seats.Length]; var started=Stopwatch.GetTimestamp();
            var result=k%2==0 ? editor.ClearSeat(seat.DeskId) : editor.SetSeat(seat.DeskId,seat.ChairId,seat.ComputerId);
            editor.Validate(); commands[k]=Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            IsoEditorAcceptance.Require(result.Succeeded,"Measured command was rejected.");
            started=Stopwatch.GetTimestamp(); editor.Preview(new IsoMoveCommand(seats[0].DeskId,new(k%10,k/10)));
            previews[k]=Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        }
        var view=IsoEditorSmoke.View(session); var scene=view.Scene; view.Present(scene);
        var command=new IsoMoveCommand(seats[0].DeskId,new(4,4)); session.PreviewCommand(command);
        var builds=view.EditorOverlay!.BuildCount;
        for (var k=0;k<100;k++) { view.Present(scene); session.PreviewCommand(command); }
        var before=GC.GetAllocatedBytesForCurrentThread();
        for (var k=0;k<1000;k++) view.Present(scene);
        var open=GC.GetAllocatedBytesForCurrentThread()-before;
        before=GC.GetAllocatedBytesForCurrentThread();
        for (var k=0;k<1000;k++) session.PreviewCommand(command);
        var cached=GC.GetAllocatedBytesForCurrentThread()-before;
        IsoEditorAcceptance.Require(view.EditorOverlay.BuildCount==builds && !view.AnimationTimerEnabled,"Idle editor added rebuild/timer work.");
        view.DetachEditor(); view.Present(scene); before=GC.GetAllocatedBytesForCurrentThread();
        for (var k=0;k<1000;k++) view.Present(scene);
        var closed=GC.GetAllocatedBytesForCurrentThread()-before;
        var importing=IsoEditorAcceptance.Create(bundled,Path.Combine(root,"import"),true); var start=Stopwatch.GetTimestamp();
        var plan=importing.Importer.PrepareImport(new(fixture,importing.Paths,importing.Catalog,bundled),CancellationToken.None);
        IsoEditorAcceptance.Require(importing.CommitPack!(plan,CancellationToken.None).Succeeded,"Measured import failed.");
        var importMs=Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        return new(34,12,100,P95(commands),P95(previews),open/1000d,closed/1000d,cached/1000d,importMs,Environment.Version.ToString(),Environment.ProcessorCount);
    }
}
