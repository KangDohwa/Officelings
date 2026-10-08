param(
    [string]$AppDll = (Join-Path $PSScriptRoot '..\src\Officelings.App\bin\Release\net10.0-windows\Officelings.App.dll'),
    [switch]$Profile,
    [switch]$SkipCompile,
    [switch]$SkipWpf,
    [switch]$Ambient
)
$ErrorActionPreference = 'Stop'
$windhawkRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\windhawk'))
$windhawkOut = Join-Path $windhawkRoot 'artifacts'
$windhawkDemo = Join-Path $windhawkOut 'demo'
$windhawkRender = Join-Path $windhawkOut 'render'
$windhawkApp = (Resolve-Path -LiteralPath $AppDll).ProviderPath
if (-not $SkipCompile) { & (Join-Path $windhawkRoot 'compile.ps1') -Tests }
# Existing WPF offscreen mode: fabricated sessions, no windows, no settings writes.
if (-not $SkipWpf) {
    & dotnet $windhawkApp --demo --smoke-test $windhawkDemo
    if ($LASTEXITCODE -ne 0) { throw "WPF smoke test failed: $LASTEXITCODE" }
}
if (-not (Test-Path -LiteralPath (Join-Path $windhawkDemo 'state.json') -PathType Leaf)) { throw 'No synthetic WPF state. Run without -SkipWpf first.' }
if ($Ambient) {
    & dotnet $windhawkApp --demo --smoke-test (Join-Path $windhawkOut 'ambient') --smoke-ambient
    if ($LASTEXITCODE -ne 0) { throw "WPF ambient smoke failed: $LASTEXITCODE" }
}
# The bundled import libraries name these runtimes .whl. Copy only runtimes into
# our ignored test output; never copy/load the mod DLL into an Explorer process.
foreach ($windhawkRuntime in @('libc++.whl', 'libunwind.whl')) {
    $windhawkRuntimeSource = Join-Path 'C:\ProgramData\Windhawk\Engine\Mods\64' $windhawkRuntime
    if (-not (Test-Path -LiteralPath $windhawkRuntimeSource -PathType Leaf)) { throw "Runtime missing: $windhawkRuntimeSource. See windhawk/README.md." }
    Copy-Item -LiteralPath $windhawkRuntimeSource -Destination (Join-Path $windhawkOut $windhawkRuntime) -Force
}
$windhawkTestArgs = @((Join-Path $windhawkDemo 'state.json'), $windhawkDemo, $windhawkRender)
if ($Profile) { $windhawkTestArgs += '--profile' }
$ErrorActionPreference = 'Continue'
$windhawkTestOutput = & (Join-Path $windhawkOut 'renderer_tests.exe') @windhawkTestArgs 2>&1
$windhawkTestExit = $LASTEXITCODE
$ErrorActionPreference = 'Stop'
$windhawkTestOutput | Tee-Object -FilePath (Join-Path $windhawkOut 'tests.log')
if ($windhawkTestExit -ne 0) { throw "Renderer tests failed: $windhawkTestExit" }
$ErrorActionPreference = 'Continue'
$windhawkHostOutput = & (Join-Path $windhawkOut 'host_tests.exe') (Join-Path $windhawkDemo 'state.json') $windhawkDemo (Join-Path $PSScriptRoot '..\tests\windhawk\host_tests.manifest') 2>&1
$windhawkHostExit = $LASTEXITCODE
$ErrorActionPreference = 'Stop'
$windhawkHostOutput | Tee-Object -FilePath (Join-Path $windhawkOut 'host-tests.log')
if ($windhawkHostExit -ne 0) { throw "Hidden host tests failed: $windhawkHostExit" }
foreach ($windhawkScript in @('windhawk_install_tracker.ps1','windhawk_run_tracker.ps1')) {
    $windhawkTokens = $null; $windhawkErrors = $null
    [Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot $windhawkScript), [ref]$windhawkTokens, [ref]$windhawkErrors) | Out-Null
    if ($windhawkErrors.Count) { throw "PowerShell syntax errors in $windhawkScript : $windhawkErrors" }
}
Write-Output "Verified offscreen only. Compare $windhawkRender\windhawk-2x.png with $windhawkDemo\office-2x.png. Installer parsed; NOT executed."
