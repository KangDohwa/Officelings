param(
    [ValidateSet('Build', 'Run')][string]$Mode = 'Build',
    [string]$BuildRoot,
    [string]$CoreDll = 'C:/Users/myori/AppData/Local/Temp/vd-perf/src/Vibedotcom.App/bin/Release/net10.0-windows/Vibedotcom.Core.dll'
)
$ErrorActionPreference = 'Stop'
$tempPrefix = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
if (!$BuildRoot) { $BuildRoot = Join-Path $tempPrefix ('vd-tracker-build-' + [guid]::NewGuid().ToString('N')) }
$BuildRoot = [IO.Path]::GetFullPath($BuildRoot)
if (!$BuildRoot.StartsWith($tempPrefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'BuildRoot must be under TEMP.' }
$binary = Join-Path $BuildRoot 'bin/TrackerPerf.dll'
if ($Mode -eq 'Build') {
    if (Test-Path -LiteralPath $BuildRoot) { throw 'BuildRoot must be new; refusing existing build output.' }
    New-Item -ItemType Directory -Path $BuildRoot | Out-Null
    $priorCliHome = $env:DOTNET_CLI_HOME
    $priorPackages = $env:NUGET_PACKAGES
    $priorFirstRun = $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE
    $priorTelemetry = $env:DOTNET_CLI_TELEMETRY_OPTOUT
    $priorCertificate = $env:DOTNET_GENERATE_ASPNET_CERTIFICATE
    $priorToolsPath = $env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH
    try {
        $env:DOTNET_CLI_HOME = Join-Path $BuildRoot 'cli-home'
        $env:NUGET_PACKAGES = Join-Path $BuildRoot 'packages'
        $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
        $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
        $env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
        $env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'
        & dotnet build (Join-Path $PSScriptRoot 'TrackerPerf.csproj') -c Release -m:1 --disable-build-servers `
            '-p:UseSharedCompilation=false' '-p:NuGetAudit=false' '-p:RestoreIgnoreFailedSources=true' `
            '-p:ImportDirectoryBuildProps=false' '-p:ImportDirectoryBuildTargets=false' `
            "-p:CoreDll=$CoreDll" "-p:BaseIntermediateOutputPath=$BuildRoot/obj/" `
            "-p:MSBuildProjectExtensionsPath=$BuildRoot/obj/" "-p:OutputPath=$BuildRoot/bin/" `
            "-p:RestorePackagesPath=$BuildRoot/packages/" '-p:AppendTargetFrameworkToOutputPath=false'
        if ($LASTEXITCODE -ne 0) { throw "Build failed: $LASTEXITCODE" }
    } finally {
        $env:DOTNET_CLI_HOME = $priorCliHome
        $env:NUGET_PACKAGES = $priorPackages
        $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = $priorFirstRun
        $env:DOTNET_CLI_TELEMETRY_OPTOUT = $priorTelemetry
        $env:DOTNET_GENERATE_ASPNET_CERTIFICATE = $priorCertificate
        $env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = $priorToolsPath
    }
    Write-Output "BUILD_ROOT=$BuildRoot"
    Write-Output "READY_BINARY=$binary"
    Write-Output 'Build only. Measurement requires explicit RUN authorization, then -Mode Run -BuildRoot <above>.'
} else {
    if (!(Test-Path -LiteralPath $binary)) { throw 'Built harness missing; use -Mode Build first.' }
    $runId = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ') + '-' + [guid]::NewGuid().ToString('N').Substring(0, 8)
    $resultPath = Join-Path $PSScriptRoot ('results/' + $runId)
    $dataPath = Join-Path $tempPrefix ('vd-tracker-data-' + $runId)
    Write-Output "RESULTS=$resultPath"
    Write-Output "SYNTHETIC_DATA=$dataPath"
    & dotnet $binary --run --results $resultPath --data $dataPath
    if ($LASTEXITCODE -ne 0) { throw "Measurement failed: $LASTEXITCODE; inspect $resultPath" }
}
