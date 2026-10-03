param(
    [Parameter(Mandatory=$true)][string]$Harness,
    [Parameter(Mandatory=$true)][string]$Output,
    [ValidateRange(5,600)][int]$Seconds = 600,
    [ValidateRange(0,60)][int]$WarmupSeconds = 10,
    [ValidateSet('normal','topmost','transparent')][string[]]$Modes = @('normal','topmost','transparent'),
    [ValidateSet(0,6,12,24)][int[]]$Counts = @(0,6,12,24)
)
$ErrorActionPreference = 'Stop'
# Three fresh processes per condition, sequentially; readiness and warmup discarded.
& (Join-Path $PSScriptRoot 'benchmark.ps1') -Mode Run -Harness $Harness -Output $Output -Suite @('Live') `
    -WarmupSeconds $WarmupSeconds -SampleSeconds $Seconds -Modes $Modes -Counts $Counts
