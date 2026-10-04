param(
    [Parameter(Mandatory=$true)][string]$Executable,
    [Parameter(Mandatory=$true)][string[]]$Arguments,
    [Parameter(Mandatory=$true)][string]$Output,
    [int]$WarmupSeconds=30, [int]$SampleSeconds=30
)
$ErrorActionPreference='Stop'
if (-not ('PerfOwnedThreadName' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class PerfOwnedThreadName {
    [DllImport("kernel32.dll")] static extern IntPtr OpenThread(uint access, bool inherit, uint id);
    [DllImport("kernel32.dll")] static extern int GetThreadDescription(IntPtr thread, out IntPtr description);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr handle);
    public static string Read(int id) {
        var thread=OpenThread(0x0800,false,(uint)id); if(thread==IntPtr.Zero) return null;
        try { IntPtr name; if(GetThreadDescription(thread,out name)!=0) return null;
            try { return Marshal.PtrToStringUni(name); } finally { LocalFree(name); } }
        finally { CloseHandle(thread); }
    }
}
'@
}
$Output=[IO.Path]::GetFullPath($Output)
if (Test-Path -LiteralPath $Output) { throw "Choose a new output: $Output" }
New-Item -ItemType Directory -Path $Output | Out-Null
$quoted=@($Arguments | ForEach-Object { if ($_ -match '["\r\n]') { throw 'Unsupported argument' }; '"'+$_+'"' })
$owned=Start-Process -FilePath $Executable -ArgumentList $quoted -WindowStyle Hidden -PassThru -RedirectStandardOutput "$Output/stdout.log" -RedirectStandardError "$Output/stderr.log"
$handle=$owned.Handle
$rows=[Collections.Generic.List[object]]::new()
$threadStart=@{}
$clock=[Diagnostics.Stopwatch]::StartNew()
try {
    while ($clock.Elapsed.TotalSeconds -lt $WarmupSeconds) {
        if ($owned.HasExited) { throw 'Target exited during warmup' }
        Start-Sleep -Milliseconds 200
    }
    $owned.Refresh(); $last=$owned.TotalProcessorTime.TotalSeconds; $initial=$last; $start=$clock.Elapsed.TotalSeconds; $wall=$start
    foreach ($thread in $owned.Threads) { try { $threadStart[$thread.Id]=$thread.TotalProcessorTime.TotalSeconds } catch { } }
    while ($clock.Elapsed.TotalSeconds-$start -lt $SampleSeconds) {
        Start-Sleep -Milliseconds 1000
        $owned.Refresh(); if ($owned.HasExited) { throw 'Target exited during sample' }
        $nextWall=$clock.Elapsed.TotalSeconds; $cpu=$owned.TotalProcessorTime.TotalSeconds
        $rows.Add([pscustomobject]@{wallSeconds=$nextWall-$wall;cpuOneCorePercent=100*($cpu-$last)/($nextWall-$wall);workingSetBytes=$owned.WorkingSet64;privateBytes=$owned.PrivateMemorySize64})
        $last=$cpu; $wall=$nextWall
    }
    $values=@($rows.cpuOneCorePercent | Sort-Object); $n=$values.Count
    $median=if ($n%2) { $values[[int][Math]::Floor($n/2)] } else { ($values[$n/2-1]+$values[$n/2])/2 }
    $threadDeltas=@(foreach ($thread in $owned.Threads) { try { if ($threadStart.ContainsKey($thread.Id)) { [pscustomobject]@{id=$thread.Id;description=[PerfOwnedThreadName]::Read($thread.Id);cpuSeconds=$thread.TotalProcessorTime.TotalSeconds-$threadStart[$thread.Id]} } } catch { } })
    [pscustomobject]@{pid=$owned.Id;externalCollector=$true;warmupSeconds=$WarmupSeconds;measuredWallSeconds=$wall-$start;cpuOneCorePercent=100*($last-$initial)/($wall-$start);medianCpuOneCorePercent=$median;samples=$rows;threadCpu=$threadDeltas;
        note='Whole target process, external queries only; warmup discarded. Repeat three times and report minimum median plus all aggregates.'} | ConvertTo-Json -Depth 5 | Set-Content "$Output/process.json" -Encoding UTF8
} finally {
    if (!$owned.HasExited) { Stop-Process -Id $owned.Id }
    $owned.Dispose()
}
