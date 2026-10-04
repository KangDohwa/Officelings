param(
    [ValidateSet('Build','Run','All')][string]$Mode = 'All',
    [string]$AppBin,
    [Parameter(Mandatory=$true)][string]$Output,
    [string]$Harness,
    [ValidateSet('Phases','Counts','Live','Pixels','Startup','Activities','Polling','Observer','P36')][string[]]$Suite = @('Startup','Phases','Counts','Live','Pixels','Activities','Polling'),
    [switch]$Resume,
    [ValidateRange(0,60)][int]$WarmupSeconds = 10,
    [ValidateRange(5,600)][int]$SampleSeconds = 15,
    [ValidateSet('normal','topmost','transparent')][string[]]$Modes = @('normal','topmost','transparent'),
    [ValidateSet('normal','topmost','transparent')][string[]]$ActivityModes = @('normal','topmost','transparent'),
    [ValidateSet(0,6,12,24)][int[]]$Counts = @(0,12)
)
$ErrorActionPreference = 'Stop'
$Output = [IO.Path]::GetFullPath($Output)
if ($WarmupSeconds + $SampleSeconds -gt 660) { throw 'Warmup plus measurement must be at most 660 seconds.' }
if ($Mode -ne 'Run') {
    if (!$AppBin) { throw '-AppBin must name an already built Release app directory.' }
    $AppBin = [IO.Path]::GetFullPath($AppBin)
    if (Test-Path -LiteralPath $Output) { throw "Choose a new output directory: $Output" }
    $snapshot = Join-Path $Output 'bin'
    New-Item -ItemType Directory -Path $snapshot | Out-Null
    Copy-Item -LiteralPath (Get-ChildItem -LiteralPath $AppBin -File).FullName -Destination $snapshot
    $build = Join-Path $Output 'harness'
    $savedEnvironment = @{}
    foreach ($key in @('DOTNET_CLI_HOME','DOTNET_SKIP_FIRST_TIME_EXPERIENCE','DOTNET_CLI_TELEMETRY_OPTOUT','DOTNET_GENERATE_ASPNET_CERTIFICATE','DOTNET_ADD_GLOBAL_TOOLS_TO_PATH')) {
        $savedEnvironment[$key] = [Environment]::GetEnvironmentVariable($key,'Process')
    }
    try {
        $env:DOTNET_CLI_HOME = Join-Path $build 'cli-home'
        $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
        $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
        $env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
        $env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'
        & dotnet build (Join-Path $PSScriptRoot 'AppHarness.csproj') -c Release --nologo -m:1 --disable-build-servers `
            '-p:UseSharedCompilation=false' '-p:NuGetAudit=false' '-p:RestoreSources=' `
            "-p:AppBin=$snapshot" "-p:BaseIntermediateOutputPath=$build/obj/" "-p:MSBuildProjectExtensionsPath=$build/obj/" `
            "-p:OutputPath=$build/bin/" "-p:RestorePackagesPath=$build/packages/" '-p:AppendTargetFrameworkToOutputPath=false'
        if ($LASTEXITCODE -ne 0) { throw "Harness build failed: $LASTEXITCODE" }
        foreach ($name in @('Vibedotcom.App.dll','Vibedotcom.Core.dll')) {
            $expected = (Get-FileHash -LiteralPath (Join-Path $snapshot $name) -Algorithm SHA256).Hash
            $actual = (Get-FileHash -LiteralPath (Join-Path "$build/bin" $name) -Algorithm SHA256).Hash
            if ($expected -ne $actual) { throw "Harness selected a different assembly: $name" }
        }
    } finally {
        foreach ($key in $savedEnvironment.Keys) { [Environment]::SetEnvironmentVariable($key,$savedEnvironment[$key],'Process') }
    }
    $Harness = Join-Path $build 'bin/Vibedotcom.Tests.exe'
    [pscustomobject]@{ capturedUtc=[DateTimeOffset]::UtcNow.ToString('o'); appBin=$AppBin; files=@(
        Get-ChildItem -LiteralPath $snapshot -File | ForEach-Object { [pscustomobject]@{name=$_.Name;sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash} }
    ) } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $Output 'assemblies.json') -Encoding UTF8
    if ($Mode -eq 'Build') { Write-Output "Ready for coordinated Run: $Harness"; return }
}
if (!$Harness) { $Harness = Join-Path $Output 'harness/bin/Vibedotcom.Tests.exe' }
$Harness = (Resolve-Path -LiteralPath $Harness).Path
New-Item -ItemType Directory -Path $Output -Force | Out-Null
if ((Test-Path -LiteralPath (Join-Path $Output 'run.json')) -and !$Resume) { throw 'Run already exists; use a new output directory or -Resume.' }
$runMetadata = if ($Resume) { 'resume-' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ') + '.json' } else { 'run.json' }
[pscustomobject]@{ startedUtc=[DateTimeOffset]::UtcNow.ToString('o'); harness=$Harness; suite=$Suite; modes=$Modes; counts=$Counts;
    repeats=3; warmupSeconds=$WarmupSeconds; sampleSeconds=$SampleSeconds; sequential=$true; offscreen=$true;
    activityModes=$ActivityModes;
    cpuFormula='cpuOneCorePercent = 100 * cpuSeconds / wallSeconds; cpuMachinePercent = cpuOneCorePercent / logicalProcessors'
} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $Output $runMetadata) -Encoding UTF8
function RunHarness([string]$command, [string]$name, [string[]]$extra = @()) {
    $pause = Join-Path $Output 'pause.request'
    if (Test-Path -LiteralPath $pause) { Write-Output 'PAUSED between runs; remove pause.request when other builds/tests have finished.' }
    while (Test-Path -LiteralPath $pause) { Start-Sleep -Milliseconds 500 }
    $dir = Join-Path $Output $name
    $finished = if ($command.StartsWith('external-') -or $command -eq 'observer') { 'live.json' } else { switch ($command) { 'live' { 'live.json' } 'live-poll' { 'live.json' } 'activities' { 'live.json' } 'startup' { 'startup.json' } 'pixels' { 'pixels.json' } 'phases' { 'phases-12.json' } 'counts' { 'counts-12.json' } } }
    if ($Resume) {
        $candidates = @($dir) + @(Get-ChildItem -LiteralPath $Output -Directory -Filter "$name-resumed-*" | Select-Object -ExpandProperty FullName)
        foreach ($candidate in $candidates) {
            if ((Test-Path -LiteralPath (Join-Path $candidate $finished) -PathType Leaf) -and
                !(Test-Path -LiteralPath (Join-Path $candidate 'excluded.json'))) {
                Write-Output "KEEP $name ($(Split-Path -Leaf $candidate))"
                return
            }
        }
    }
    if (Test-Path -LiteralPath $dir) {
        if (!$Resume) { throw "Refusing existing result: $dir" }
        $dir += '-resumed-' + [guid]::NewGuid().ToString('N').Substring(0,8)
    }
    Write-Output "START $name"
    if ($command.StartsWith('external-')) {
        & (Join-Path $PSScriptRoot 'collect-external.ps1') -Harness $Harness -Command $command -Output $dir -Count ([int]$extra[0]) -WindowMode $extra[1] -WarmupSeconds ([int]$extra[2]) -SampleSeconds ([int]$extra[3])
    } else { & $Harness $command $dir @extra; if ($LASTEXITCODE -ne 0) { throw "Harness failed ($LASTEXITCODE): $dir" } }
    Write-Output "DONE $name"
}
if ($Suite -contains 'Startup') { foreach ($repeat in 1..3) { RunHarness 'startup' "startup-repeat-$repeat" } }
if ($Suite -contains 'Phases') { foreach ($repeat in 1..3) { RunHarness 'phases' "phases-repeat-$repeat" } }
if ($Suite -contains 'Counts') { foreach ($repeat in 1..3) { RunHarness 'counts' "counts-repeat-$repeat" } }
if ($Suite -contains 'Pixels') { RunHarness 'pixels' 'pixels' }
if ($Suite -contains 'Live') {
    foreach ($count in $Counts) {
        foreach ($windowMode in $Modes) {
            foreach ($repeat in 1..3) {
                RunHarness 'external-live' "live-$windowMode-$count-repeat-$repeat" @([string]$count,$windowMode,[string]$WarmupSeconds,[string]$SampleSeconds)
            }
        }
    }
}
if ($Suite -contains 'Activities') {
    foreach ($windowMode in $ActivityModes) {
        foreach ($repeat in 1..3) { RunHarness 'external-activities' "activities-$windowMode-12-repeat-$repeat" @('12',$windowMode,[string]$WarmupSeconds,[string]$SampleSeconds) }
    }
}
if ($Suite -contains 'Polling') {
    foreach ($repeat in 1..3) { RunHarness 'external-poll' "polling-normal-0-repeat-$repeat" @('0','normal',[string]$WarmupSeconds,[string]$SampleSeconds) }
}
if ($Suite -contains 'Observer') { foreach ($repeat in 1..3) { RunHarness 'observer' "observer-normal-0-repeat-$repeat" @('0','normal',[string]$WarmupSeconds,[string]$SampleSeconds) } }
if ($Suite -contains 'P36') {
    foreach ($windowMode in $Modes) {
        foreach ($repeat in 1..3) { RunHarness 'external-p36' "p36-$windowMode-repeat-$repeat" @('0',$windowMode,[string]$WarmupSeconds,[string]$SampleSeconds) }
    }
}
Write-Output "Results ready: $Output. Summarize with tools/perf/summarize.py."
