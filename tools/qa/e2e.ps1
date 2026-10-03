param(
    [string[]]$Scenario = @('quiet', 'busy-12', 'burst-spawn', 'flaky-writer', 'long-soak'),
    [double]$Speed = 20,
    [int]$Seed = 1,
    [int]$SoakCapSeconds = 600,
    [string]$RendererTests,
    [switch]$DirectTracker,
    [string]$Python = 'C:\Users\myori\.codex\skills\sprite-gen\.venv\Scripts\python.exe'
)
$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $Python -PathType Leaf)) { throw "Python missing: $Python" }
$qaArguments = @((Join-Path $PSScriptRoot 'run_e2e.py'), '--speed', $Speed.ToString([Globalization.CultureInfo]::InvariantCulture),
    '--seed', $Seed.ToString(), '--soak-cap-seconds', $SoakCapSeconds.ToString(), '--scenario') + $Scenario
if ($RendererTests) { $qaArguments += @('--renderer-tests', $RendererTests) }
if ($DirectTracker) { $qaArguments += '--direct-tracker' }
& $Python @qaArguments
exit $LASTEXITCODE
