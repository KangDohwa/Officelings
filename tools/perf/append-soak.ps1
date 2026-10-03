$ErrorActionPreference = 'Stop'
$resultRoot = Join-Path $PSScriptRoot 'results'
$summaryFile = Join-Path $resultRoot ('summary-report-' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ') + '.json')
# The last raw rows overlap shutdown; use the first nine scheduled samples only.
& (Join-Path $PSScriptRoot 'analyze.ps1') -Results $resultRoot -MaxSampleSeconds 46 -SummaryFile $summaryFile | Out-Null
$summary = Get-Content -LiteralPath $summaryFile -Raw | ConvertFrom-Json
function Triple($metric, [double]$divisor = 1, [int]$digits = 1) {
    (@($metric.median,$metric.p95,$metric.max) | ForEach-Object { ([double]$_/$divisor).ToString("F$digits",[Globalization.CultureInfo]::InvariantCulture) }) -join '/'
}
$lines = New-Object Collections.Generic.List[string]
$lines.Add('**Accepted short idle samples — not ten-minute soaks**')
$lines.Add('')
$retryCount=@($summary.soak | Where-Object {$_.ready.startupRetries -gt 0}).Count
$retryTotal=($summary.soak | ForEach-Object {$_.ready.startupRetries} | Measure-Object -Sum).Sum
$lines.Add(('All twelve owned windows eventually became ready. The common sampling epoch began only after prolonged startup, so their ages differed substantially. Sampling was stopped at the work budget. The first nine scheduled samples (approximately 5 to 45 seconds) precede shutdown and are used below; later raw rows overlap closure, including a zero working-set row, and are excluded explicitly. Every window decoded 96 sheets; {0} final starts needed {1} layout-budget retries in total. These results are a short concurrent observation, not a controlled ten-minute comparison.' -f $retryCount,$retryTotal))
$lines.Add('')
$lines.Add('Each triple is median/p95/max. cpuOneCorePercent = 100 * cpuSeconds / wallSeconds; cpuMachinePercent = cpuOneCorePercent / logicalProcessors. Memory is MiB, slope is MiB/min. With nine samples, nearest-rank p95 equals max. Short slopes are not leak evidence. GDI/USER/thread counts are OS observations.')
$lines.Add('')
$lines.Add('| Mode / agents | n / last sample s | cpuOneCorePercent | Working set MiB | Private MiB | WS/private slope | GDI | USER | Threads |')
$lines.Add('|---|---:|---:|---:|---:|---:|---:|---:|---:|')
foreach ($s in ($summary.soak | Sort-Object mode,count)) {
    $m=$s.metrics
    $lines.Add(('| {0}/{1} | {2}/{3:F1} | {4} | {5} | {6} | {7:F2}/{8:F2} | {9} | {10} | {11} |' -f $s.mode,$s.count,$s.n,$s.span,(Triple $m.cpuOneCorePercent),(Triple $m.workingSetBytes 1MB),(Triple $m.privateBytes 1MB),($m.workingSetBytes.slopePerMinute/1MB),($m.privateBytes.slopePerMinute/1MB),(Triple $m.gdi 1 0),(Triple $m.user 1 0),(Triple $m.threads 1 0)))
}
$lines.Add('')
$cpuDifferences=@($summary.soak | ForEach-Object { $_.cpuCounterAbsoluteDifference.median })
$minDiff = [double](($cpuDifferences | Measure-Object -Minimum).Minimum)
$maxDiff = [double](($cpuDifferences | Measure-Object -Maximum).Maximum)
$lines.Add(('All accepted rows have PID-matched performance-counter readings. Median absolute CPU-counter/Get-Process differences across conditions ranged {0:F2} to {1:F2} percentage points of one core; they sample at slightly different instants. Private bytes and handles remain separate measures. The final empty-home polls read zero payload bytes.' -f $minDiff,$maxDiff))
$zero=$summary.soak | Where-Object {$_.mode -eq 'normal' -and $_.count -eq 0} | Select-Object -First 1
$lines.Add(('The zero-agent normal process lived {0:F1} seconds and still allocated {1:F2} MiB/s over its whole lifetime ({2} animation ticks; GC 0/1/2 = {3}). This supports investigating quiet-room update work, but does not attribute those allocations to a particular method. The shortest/latest windows had much less settling time.' -f $zero.finished.seconds,($zero.finished.allocatedBytes/$zero.finished.seconds/1MB),$zero.finished.ticks,($zero.finished.gcCollections -join '/')))
$lines.Add('')
$lines.Add('Raw accepted/overlapping rows are in `tools/perf/results/soak-complete/*/samples.csv`; readiness, native styles, retry counts, caches and lifetime GC totals are in adjacent JSON files. `results/summary.json` contains the accepted-sample statistics and per-metric slopes. To reproduce this particular truncation, run `analyze.ps1 -MaxSampleSeconds 46`; use the default unlimited analysis only for a genuinely completed run. The ordinary CLI smoke, desktop mode, visible compositor performance and ten-minute drift remain unmeasured. Measurement processes were stopped around the forty-minute budget and the specified `vd-perf` worktree was removed; other agents'' worktrees were retained.')
$lines.Add('')
$reportPath = Join-Path $PSScriptRoot '../../docs/perf-report.md'
$report = Get-Content -LiteralPath $reportPath -Raw -Encoding UTF8
$marker='**Offscreen rendering: twelve fabricated working agents**'
if ($report.Contains('**Accepted short idle samples')) { throw 'Partial-sample section already exists.' }
$report=$report.Replace($marker,($lines -join "`n")+"`n"+$marker)
Set-Content -LiteralPath $reportPath -Value $report -Encoding UTF8
Write-Output 'Inserted accepted partial-sample table; no ten-minute claim.'
