param(
    [Parameter(Mandatory=$true)][string]$Harness,
    [Parameter(Mandatory=$true)][string]$Command,
    [Parameter(Mandatory=$true)][string]$Output,
    [int]$Count=0, [string]$WindowMode='normal', [int]$WarmupSeconds=10, [int]$SampleSeconds=15
)
$ErrorActionPreference='Stop'
if (-not ('PerfThreadDescription' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class PerfThreadDescription {
    [DllImport("kernel32.dll")] static extern IntPtr OpenThread(uint access, bool inherit, uint id);
    [DllImport("kernel32.dll")] static extern int GetThreadDescription(IntPtr thread, out IntPtr description);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr handle);
    public static string Read(int id) {
        var handle=OpenThread(0x0800,false,(uint)id);
        if(handle==IntPtr.Zero) return "";
        try {
            IntPtr pointer;
            if(GetThreadDescription(handle,out pointer)!=0 || pointer==IntPtr.Zero) return "";
            try { return Marshal.PtrToStringUni(pointer) ?? ""; }
            finally { LocalFree(pointer); }
        } finally { CloseHandle(handle); }
    }
}
'@
}
$Output=[IO.Path]::GetFullPath($Output)
if (Test-Path -LiteralPath $Output) { throw "Choose a new output directory: $Output" }
New-Item -ItemType Directory -Path $Output | Out-Null
$perfProcess=Start-Process -FilePath $Harness -ArgumentList @($Command,('"'+$Output+'"'),$Count,$WindowMode,$WarmupSeconds,$SampleSeconds) -WindowStyle Hidden -PassThru -RedirectStandardOutput "$Output/process.log" -RedirectStandardError "$Output/process-error.log"
$perfHandle=$perfProcess.Handle # Retain the native handle: Windows PowerShell otherwise loses ExitCode after asynchronous exit.
$perfDeadline=[DateTime]::UtcNow.AddSeconds($WarmupSeconds+$SampleSeconds+90)
$perfSamples=[Collections.Generic.List[object]]::new()
$perfThreadNames=@{}
$perfClock=[Diagnostics.Stopwatch]::StartNew()
try {
    $perfWarm=$null
    while (!$perfWarm) {
        if ($perfProcess.HasExited -or [DateTime]::UtcNow -gt $perfDeadline) { throw 'Target exited or exceeded startup deadline before warm marker.' }
        if (Test-Path -LiteralPath "$Output/warm.json") { try { $perfWarm=Get-Content -LiteralPath "$Output/warm.json" -Raw | ConvertFrom-Json } catch { } }
        if ($perfWarm) { break }
        Start-Sleep -Milliseconds 50
    }
    if ($perfWarm.pid -ne $perfProcess.Id) { throw 'Owned target PID mismatch.' }
    $perfLastWall=$perfClock.Elapsed.TotalSeconds
    $perfProcess.Refresh()
    $perfLastCpu=$perfProcess.TotalProcessorTime.TotalSeconds
    $perfLastThreads=@{}
    foreach ($perfThread in $perfProcess.Threads) { try { $perfLastThreads[$perfThread.Id]=$perfThread.TotalProcessorTime.TotalSeconds; $perfThreadNames[$perfThread.Id]=[PerfThreadDescription]::Read($perfThread.Id) } catch { } }
    while (!(Test-Path -LiteralPath "$Output/external-target.json")) {
        Start-Sleep -Milliseconds 1000
        if ([DateTime]::UtcNow -gt $perfDeadline) { throw 'Target exceeded finite measurement deadline.' }
        if ($perfProcess.HasExited -or (Test-Path -LiteralPath "$Output/external-target.json")) { break }
        $perfWall=$perfClock.Elapsed.TotalSeconds
        $perfProcess.Refresh()
        $perfCpu=$perfProcess.TotalProcessorTime.TotalSeconds
        $perfInterval=$perfWall-$perfLastWall
        $perfThreadRows=@(); $perfThreads=@{}
        foreach ($perfThread in $perfProcess.Threads) {
            try {
                $perfThreadCpu=$perfThread.TotalProcessorTime.TotalSeconds
                $perfThreads[$perfThread.Id]=$perfThreadCpu
                if (!$perfThreadNames.ContainsKey($perfThread.Id)) { $perfThreadNames[$perfThread.Id]=[PerfThreadDescription]::Read($perfThread.Id) }
                if ($perfLastThreads.ContainsKey($perfThread.Id)) { $perfThreadRows += [pscustomobject]@{id=$perfThread.Id;description=$perfThreadNames[$perfThread.Id];isUi=($perfThread.Id -eq $perfWarm.uiThreadId);cpuSeconds=$perfThreadCpu-$perfLastThreads[$perfThread.Id]} }
            } catch { }
        }
        $perfSamples.Add([pscustomobject]@{wallSeconds=$perfInterval;cpuSeconds=$perfCpu-$perfLastCpu;logicalProcessors=[Environment]::ProcessorCount;
            cpuOneCorePercent=100*($perfCpu-$perfLastCpu)/$perfInterval;cpuMachinePercent=100*($perfCpu-$perfLastCpu)/$perfInterval/[Environment]::ProcessorCount;
            workingSetBytes=$perfProcess.WorkingSet64;privateBytes=$perfProcess.PrivateMemorySize64;threads=$perfThreadRows;
            allocatedBytes=$null;logicalRedrawPixelArea=$null;counters=@{};appScheduledWakeups=$null})
        $perfLastWall=$perfWall; $perfLastCpu=$perfCpu; $perfLastThreads=$perfThreads
    }
    if (!$perfProcess.WaitForExit(10000)) { throw 'Target did not exit after its finite end marker.' }
    if (!(Test-Path -LiteralPath "$Output/external-target.json") -or $null -eq $perfProcess.ExitCode -or $perfProcess.ExitCode -ne 0) { throw "Target did not complete (exit=$($perfProcess.ExitCode)); see process-error.log." }
    $perfTarget=Get-Content -LiteralPath "$Output/external-target.json" -Raw | ConvertFrom-Json
    [pscustomobject]@{count=$Count;mode=$WindowMode;fixture=$perfTarget.fixture;warmupSeconds=$WarmupSeconds;sampleSeconds=$SampleSeconds;
        logicalProcessors=$perfTarget.logicalProcessors;externalCollector=$true;samples=$perfSamples;target=$perfTarget;warm=$perfWarm;
        note='One-second process memory/CPU/thread queries run in the collector, not the target. Native target boundary CPU/wall gives the aggregate. Periodic sample coverage excludes partial edge intervals; no fabricated allocation/counter samples and no observer subtraction. Unnamed non-UI threads are not identified as WPF render.'
    } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath "$Output/live.json" -Encoding UTF8
} finally {
    if (!$perfProcess.HasExited) { Stop-Process -Id $perfProcess.Id }
    $perfProcess.Dispose()
}
