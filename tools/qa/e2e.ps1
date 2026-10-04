param(
    [string[]]$Scenario = @('quiet', 'busy-12', 'burst-spawn', 'flaky-writer', 'long-soak'),
    [double]$Speed = 20,
    [int]$Seed = 1,
    [int]$SoakCapSeconds = 600,
    [int]$QuietSteadySeconds = 0,
    [string]$RendererTests,
    [switch]$DirectTracker,
    [switch]$Workspace,
    [string]$Python = 'python'
)
$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $Python -PathType Leaf)) { throw "Python missing: $Python" }
$qaArguments = @((Join-Path $PSScriptRoot 'run_e2e.py'), '--speed', $Speed.ToString([Globalization.CultureInfo]::InvariantCulture),
    '--seed', $Seed.ToString(), '--soak-cap-seconds', $SoakCapSeconds.ToString(), '--scenario') + $Scenario
if ($RendererTests) { $qaArguments += @('--renderer-tests', $RendererTests) }
if ($DirectTracker) { $qaArguments += '--direct-tracker' }
if ($Workspace) { $qaArguments += '--workspace' }
if ($QuietSteadySeconds) { $qaArguments += @('--quiet-steady-seconds', $QuietSteadySeconds.ToString()) }
& $Python @qaArguments
exit $LASTEXITCODE
