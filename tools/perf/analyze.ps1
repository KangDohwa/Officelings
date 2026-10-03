param([string]$Results = (Join-Path $PSScriptRoot 'results'), [double]$MaxSampleSeconds = [double]::PositiveInfinity,
    [string]$SummaryFile = (Join-Path $Results ('summary-' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ') + '.json')))
$ErrorActionPreference = 'Stop'
if (Test-Path -LiteralPath $SummaryFile) { throw 'Refusing to overwrite an existing summary.' }
function Stats($values) {
    $a = @($values | Sort-Object); $n = $a.Count
    if (!$n) { return $null }
    $mid = [Math]::Floor($n/2)
    $median = if ($n % 2) { $a[$mid] } else { ($a[$mid-1]+$a[$mid])/2 }
    [pscustomobject]@{n=$n;median=$median;p95=$a[[Math]::Max(0,[Math]::Ceiling(.95*$n)-1)];max=$a[$n-1]}
}
function Slope($rows, [string]$field) {
    $n=$rows.Count; $sx=0.0; $sy=0.0; $sxy=0.0; $sxx=0.0
    foreach ($row in $rows) { $x=[double]$row.seconds/60; $y=[double]$row.$field; $sx+=$x; $sy+=$y; $sxy+=$x*$y; $sxx+=$x*$x }
    if ($n*$sxx-$sx*$sx -eq 0) { return 0 }
    return ($n*$sxy-$sx*$sy)/($n*$sxx-$sx*$sx)
}
$soak = @()
$soakRoot = Join-Path $Results 'soak-complete'
if (Test-Path -LiteralPath $soakRoot) {
    foreach ($dir in Get-ChildItem -LiteralPath $soakRoot -Directory | Sort-Object Name) {
        $rows = @(Import-Csv -LiteralPath (Join-Path $dir.FullName 'samples.csv') | Where-Object { [double]$_.seconds -le $MaxSampleSeconds -and [double]$_.workingSetBytes -gt 0 })
        if ($rows.Count -eq 0) { continue }
        foreach ($row in $rows) {
            # Read historical field names without rewriting their raw evidence.
            if (!$row.PSObject.Properties['cpuOneCorePercent']) { $row | Add-Member -NotePropertyName cpuOneCorePercent -NotePropertyValue $row.cpuCorePercent }
        }
        $metrics = [ordered]@{}
        foreach ($field in @('cpuOneCorePercent','cpuMachinePercent','workingSetBytes','privateBytes','gdi','user','threads')) {
            $s = Stats @($rows | ForEach-Object { [double]$_.$field })
            $metrics[$field] = [pscustomobject]@{median=$s.median;p95=$s.p95;max=$s.max;slopePerMinute=(Slope $rows $field);lateSlopePerMinute=(Slope @($rows | Where-Object {[double]$_.seconds -ge 300}) $field);first=[double]$rows[0].$field;last=[double]$rows[-1].$field}
        }
        $counterRows = @($rows | Where-Object { $_.counterCpuCorePercent -ne '' })
        $cpuDifference = Stats @($counterRows | ForEach-Object { [Math]::Abs([double]$_.counterCpuCorePercent-[double]$_.cpuOneCorePercent) })
        $ready = Get-Content -LiteralPath (Join-Path $dir.FullName 'ready.json') -Raw | ConvertFrom-Json
        $finished = if (Test-Path -LiteralPath (Join-Path $dir.FullName 'finished.json')) { Get-Content -LiteralPath (Join-Path $dir.FullName 'finished.json') -Raw | ConvertFrom-Json } else { $null }
        $soak += [pscustomobject]@{name=$dir.Name;count=$ready.count;mode=$ready.mode;n=$rows.Count;span=[double]$rows[-1].seconds;metrics=$metrics;counterValidRows=$counterRows.Count;cpuCounterAbsoluteDifference=$cpuDifference;ready=$ready;finished=$finished}
    }
}
$renders=@()
if (Test-Path -LiteralPath (Join-Path $Results 'render')) {
    foreach ($file in Get-ChildItem -LiteralPath (Join-Path $Results 'render') -Filter '*.json') {
        $r=Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
        $renders += [pscustomobject]@{file=$file.Name;zoom=$r.zoom;width=$r.width;height=$r.height;frames=$r.frames;times=(Stats $r.milliseconds);meanMs=($r.milliseconds | Measure-Object -Average).Average;bytesPerFrame=$r.bytesPerFrame;gc=$r.gcCollections;visible=$r.visible;memory=$r.memory}
    }
}
$packs=@()
if (Test-Path -LiteralPath (Join-Path $Results 'pack')) {
    foreach ($file in Get-ChildItem -LiteralPath (Join-Path $Results 'pack') -Filter '*.json') { $packs += Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json }
}
[pscustomobject]@{cpuFormula='cpuOneCorePercent = 100 * cpuSeconds / wallSeconds; cpuMachinePercent = cpuOneCorePercent / logicalProcessors';soak=$soak;render=$renders;pack=$packs} | ConvertTo-Json -Depth 14 | Set-Content -LiteralPath $SummaryFile -Encoding UTF8
foreach ($s in $soak) {
    $m=$s.metrics
    '{0}: n={1}, cpuOneCorePercent={2:N2}/{3:N2}/{4:N2}%; WS={5:N1}/{6:N1}/{7:N1} MiB; private={8:N1}/{9:N1}/{10:N1} MiB; slopes WS/private={11:N2}/{12:N2} MiB/min; GDI={13}/{14}/{15}; USER={16}/{17}/{18}; threads={19}/{20}/{21}' -f $s.name,$s.n,$m.cpuOneCorePercent.median,$m.cpuOneCorePercent.p95,$m.cpuOneCorePercent.max,($m.workingSetBytes.median/1MB),($m.workingSetBytes.p95/1MB),($m.workingSetBytes.max/1MB),($m.privateBytes.median/1MB),($m.privateBytes.p95/1MB),($m.privateBytes.max/1MB),($m.workingSetBytes.slopePerMinute/1MB),($m.privateBytes.slopePerMinute/1MB),$m.gdi.median,$m.gdi.p95,$m.gdi.max,$m.user.median,$m.user.p95,$m.user.max,$m.threads.median,$m.threads.p95,$m.threads.max
}
$renders | Format-Table zoom,frames,meanMs,bytesPerFrame,visible
