param(
    [Parameter(Mandatory=$true)][string]$Harness,
    [Parameter(Mandatory=$true)][string]$Output,
    [int]$Seconds = 600,
    [string[]]$Modes = @('normal','topmost','transparent'),
    [int[]]$Counts = @(0,6,12,24)
)
$ErrorActionPreference = 'Stop'
if ($Seconds -lt 10 -or $Seconds -gt 600) { throw 'Duration must be 10..600 seconds.' }
$Output = [IO.Path]::GetFullPath($Output)
New-Item -ItemType Directory -Path $Output -Force | Out-Null
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class PerfNative {
 [DllImport("user32.dll")] public static extern uint GetGuiResources(IntPtr h, uint flag);
 [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
 [StructLayout(LayoutKind.Sequential)] public struct Value { public uint Status; public double Number; }
 [DllImport("pdh.dll", CharSet=CharSet.Unicode)] public static extern uint PdhOpenQuery(string source, IntPtr data, out IntPtr query);
 [DllImport("pdh.dll", CharSet=CharSet.Unicode)] public static extern uint PdhAddEnglishCounter(IntPtr query, string path, IntPtr data, out IntPtr counter);
 [DllImport("pdh.dll")] public static extern uint PdhCollectQueryData(IntPtr query);
 [DllImport("pdh.dll")] public static extern uint PdhGetFormattedCounterValue(IntPtr counter, uint format, out uint type, out Value value);
 [DllImport("pdh.dll")] public static extern uint PdhCloseQuery(IntPtr query);
 public static IntPtr Open() { IntPtr q; uint rc = PdhOpenQuery(null,IntPtr.Zero,out q); if(rc != 0) throw new Exception("PDH open status 0x"+rc.ToString("X")); return q; }
}
'@
$runs = @()
$query = [IntPtr]::Zero
try {
    foreach ($mode in $Modes) {
        foreach ($count in $Counts) {
            $dir = Join-Path $Output "$mode-$count"
            New-Item -ItemType Directory -Path $dir -Force | Out-Null
            $psi = New-Object Diagnostics.ProcessStartInfo
            $psi.FileName = $Harness
            # A unique directory also keeps previous partial measurements out of this run.
            if (Test-Path -LiteralPath (Join-Path $dir 'samples.csv')) { throw "Choose a new output directory: $dir" }
            $psi.Arguments = 'soak "' + $dir + '" ' + $count + ' ' + $mode + ' ' + $Seconds
            $psi.WorkingDirectory = Split-Path $Harness
            $psi.UseShellExecute = $false
            $psi.CreateNoWindow = $true
            $psi.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
            $psi.RedirectStandardError = $true
            $psi.RedirectStandardOutput = $true
            $process = [Diagnostics.Process]::Start($psi)
            $runs += [pscustomobject]@{Process=$process;Dir=$dir;Mode=$mode;Count=$count;LastCpu=0.0;LastTime=0.0}
            # Serialize startup: the committed layout validator has a short wall-clock
            # budget and can reject its own built-in layout under simultaneous startup.
            $startup = [Diagnostics.Stopwatch]::StartNew()
            while (-not (Test-Path -LiteralPath (Join-Path $dir 'ready.json')) -and $startup.Elapsed.TotalSeconds -lt 120) {
                Start-Sleep -Milliseconds 100
                if ($process.HasExited) {
                    $detail=$process.StandardError.ReadToEnd(); Set-Content -LiteralPath (Join-Path $dir 'stderr.txt') -Value $detail
                    throw "Owned harness startup failed ($mode/$count): $detail"
                }
            }
            if (-not (Test-Path -LiteralPath (Join-Path $dir 'ready.json'))) { throw 'Serialized startup exceeded its deadline.' }
            Write-Output "Ready $mode/$count (PID $($process.Id))."
        }
    }
    $watch = [Diagnostics.Stopwatch]::StartNew()
    while ($watch.Elapsed.TotalSeconds -lt 120 -and @($runs | Where-Object { -not (Test-Path -LiteralPath (Join-Path $_.Dir 'ready.json')) }).Count -gt 0) {
        Start-Sleep -Milliseconds 250
        foreach ($run in $runs) { if ($run.Process.HasExited) { $detail = $run.Process.StandardError.ReadToEnd(); Set-Content -LiteralPath (Join-Path $run.Dir 'stderr.txt') -Value $detail; throw "Harness exited before ready: $($run.Mode)/$($run.Count), exit $($run.Process.ExitCode): $detail" } }
    }
    foreach ($run in $runs) {
        if (-not (Test-Path -LiteralPath (Join-Path $run.Dir 'ready.json'))) { throw 'Ready timeout.' }
        $p = Get-Process -Id $run.Process.Id
        $run.LastCpu = $p.TotalProcessorTime.TotalSeconds
        $run.LastTime = 0.0
    }
    $instances = (New-Object Diagnostics.PerformanceCounterCategory('Process')).GetInstanceNames() | Where-Object { $_ -like 'Vibedotcom.Tests*' }
    $query = [PerfNative]::Open()
    $bindings = @()
    foreach ($instance in $instances) {
        $idCounter = New-Object Diagnostics.PerformanceCounter('Process','ID Process',$instance,$true)
        $processId = [int]$idCounter.NextValue(); $idCounter.Dispose()
        if ($runs.Process.Id -notcontains $processId) { continue }
        $handles = @{}
        foreach ($name in @('% Processor Time','Working Set','Private Bytes','Thread Count')) {
            $handle = [IntPtr]::Zero
            if ([PerfNative]::PdhAddEnglishCounter($query,"\Process($instance)\$name",[IntPtr]::Zero,[ref]$handle) -ne 0) { throw "Cannot bind PDH counter: $instance/$name" }
            $handles[$name]=$handle
        }
        $bindings += [pscustomobject]@{Pid=$processId;Handles=$handles}
    }
    if ($bindings.Count -ne $runs.Count) { throw 'Could not map every owned PID to a performance counter instance.' }
    [void][PerfNative]::PdhCollectQueryData($query)
    $watch.Restart()
    foreach ($run in $runs) { $run.LastCpu = (Get-Process -Id $run.Process.Id).TotalProcessorTime.TotalSeconds; $run.LastTime=0.0 }
    $foregroundStart = [PerfNative]::GetForegroundWindow().ToInt64()
    $counterErrors = @()
    for ($index=1; $index -le [Math]::Floor($Seconds/5); $index++) {
        $delay = $index*5 - $watch.Elapsed.TotalSeconds
        if ($delay -gt 0) { Start-Sleep -Milliseconds ([int]($delay*1000)) }
        $status = [PerfNative]::PdhCollectQueryData($query)
        if ($status -ne 0) { $counterErrors += "PDH collection status $status" }
        $elapsed = $watch.Elapsed.TotalSeconds
        foreach ($run in $runs) {
            $p = Get-Process -Id $run.Process.Id -ErrorAction SilentlyContinue
            if ($null -eq $p) { throw "Harness exited inside sample window: $($run.Mode)/$($run.Count)" }
            $cpu = $p.TotalProcessorTime.TotalSeconds
            $cpuCore = ($cpu-$run.LastCpu)/($elapsed-$run.LastTime)*100
            $run.LastCpu = $cpu; $run.LastTime = $elapsed
            $binding = $bindings | Where-Object { $_.Pid -eq $p.Id } | Select-Object -First 1
            function CounterValue([string]$name) {
                $value = New-Object PerfNative+Value; $type = [uint32]0
                $rc = [PerfNative]::PdhGetFormattedCounterValue($binding.Handles[$name],0x200,[ref]$type,[ref]$value)
                if ($rc -eq 0 -and $value.Status -le 1) { $value.Number } else { $null }
            }
            [pscustomobject]@{
                seconds=$elapsed;pid=$p.Id;mode=$run.Mode;agents=$run.Count;
                cpuCorePercent=$cpuCore;cpuMachinePercent=$cpuCore/[Environment]::ProcessorCount;
                workingSetBytes=$p.WorkingSet64;privateBytes=$p.PrivateMemorySize64;
                gdi=[PerfNative]::GetGuiResources($p.Handle,0);user=[PerfNative]::GetGuiResources($p.Handle,1);threads=$p.Threads.Count;
                counterCpuCorePercent=(CounterValue '% Processor Time');counterWorkingSetBytes=(CounterValue 'Working Set');counterPrivateBytes=(CounterValue 'Private Bytes');counterThreads=(CounterValue 'Thread Count');
                foregroundHwnd=[PerfNative]::GetForegroundWindow().ToInt64()
            } | Export-Csv -LiteralPath (Join-Path $run.Dir 'samples.csv') -NoTypeInformation -Append
        }
        if ($index % 12 -eq 0) { Write-Output "Sampled $([int]$elapsed)s / $Seconds s across $($runs.Count) owned offscreen windows." }
    }
    [pscustomobject]@{seconds=$watch.Elapsed.TotalSeconds;logicalProcessors=[Environment]::ProcessorCount;concurrentProcesses=$runs.Count;foregroundStart=$foregroundStart;foregroundEnd=[PerfNative]::GetForegroundWindow().ToInt64();counterErrors=$counterErrors;createdUtc=[DateTimeOffset]::UtcNow.ToString('o')} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $Output 'sampling.json') -Encoding UTF8
    foreach ($run in $runs) { Set-Content -LiteralPath (Join-Path $run.Dir 'stop.request') -Value 'Measurement complete' }
    foreach ($run in $runs) { if (-not $run.Process.WaitForExit(30000)) { $run.Process.Kill(); throw 'Owned harness exceeded deadline.' } }
} finally {
    foreach ($run in $runs) { if (-not $run.Process.HasExited) { $run.Process.Kill() }; $run.Process.Dispose() }
    if ($query -ne [IntPtr]::Zero) { [void][PerfNative]::PdhCloseQuery($query) }
}
