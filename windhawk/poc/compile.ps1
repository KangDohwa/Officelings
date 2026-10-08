param(
    [string]$Source = "$PSScriptRoot\poc.wh.cpp",
    [string]$OutputDirectory = $PSScriptRoot,
    [string]$OutputName = 'poc',
    [string]$ModId = 'officelings-desktop-poc',
    [string]$ModVersion = '0.1.0',
    [string[]]$AdditionalOptions = @(),
    [switch]$Executable
)
$ErrorActionPreference = 'Stop'
$pocCompiler = 'C:\Program Files\Windhawk\Compiler\bin\clang++.exe'
$pocEngineLib = 'C:/Program Files/Windhawk/Engine/1.7.3/64/windhawk.lib'
$pocInclude = 'C:/Program Files/Windhawk/Compiler/include'

# A response file preserves the literal quotes in the L"..." macros on Windows
# PowerShell 5.1. All compiler output goes to this scratch directory.
$pocArgs = @'
-std=c++23
-O2
-shared
-target x86_64-w64-mingw32
-DUNICODE
-D_UNICODE
-DWINVER=0x0A00
-D_WIN32_WINNT=0x0A00
-D_WIN32_IE=0x0A00
-DNTDDI_VERSION=0x0A000008
-D__USE_MINGW_ANSI_STDIO=0
-DWH_MOD
-DWH_MOD_ID=L\"officelings-desktop-poc\"
-DWH_MOD_VERSION=L\"0.1.0\"
-include windhawk_api.h
-Wl,--export-all-symbols
-Wall
-Wextra
-lgdiplus
-lgdi32
-luser32
'@
if ($ModId -notmatch '^[a-z0-9-]+$' -or $ModVersion -notmatch '^[0-9.]+$' -or $OutputName -notmatch '^[a-zA-Z0-9_-]+$') { throw 'Invalid build identifier' }
$pocArgs = $pocArgs.Replace('officelings-desktop-poc', $ModId).Replace('0.1.0', $ModVersion)
if ($Executable) {
    # Same compiler/target/optimization/Windows macros, without Windhawk linkage.
    $pocArgs = (($pocArgs -split "`r?`n") | Where-Object { $_ -ne '-shared' -and $_ -notmatch '^-DWH_' -and $_ -ne '-include windhawk_api.h' -and $_ -ne '-Wl,--export-all-symbols' }) -join "`n"
} else { $pocArgs += "`n`"$pocEngineLib`"" }
$pocDirectory = [IO.Path]::GetFullPath($OutputDirectory).Replace('\', '/')
$pocSource = [IO.Path]::GetFullPath($Source).Replace('\', '/')
New-Item -ItemType Directory -Force -Path $pocDirectory | Out-Null
$pocExtension = if ($Executable) { 'exe' } else { 'dll' }
$pocArgs += "`n-I`"$pocInclude`"`n`"$pocSource`"`n-o `"$pocDirectory/$OutputName.$pocExtension`"`n" + ($AdditionalOptions -join "`n") + "`n"
$pocBuildName = if ($OutputName -eq 'poc') { 'compile' } else { $OutputName }
$pocResponse = "$pocDirectory/$pocBuildName.rsp"
$pocLog = "$pocDirectory/$pocBuildName.log"
[IO.File]::WriteAllText($pocResponse, $pocArgs, [Text.UTF8Encoding]::new($false))
$ErrorActionPreference = 'Continue'
$pocOutput = & $pocCompiler "@$pocResponse" 2>&1
$pocExit = $LASTEXITCODE
$ErrorActionPreference = 'Stop'
$pocOutput = @($pocOutput | ForEach-Object { $_.ToString() })
[IO.File]::WriteAllLines($pocLog, [string[]]$pocOutput, [Text.UTF8Encoding]::new($false))
$pocOutput
"clang exit code: $pocExit" | Add-Content -LiteralPath $pocLog
if ($pocExit -ne 0) { throw "clang failed with exit code $pocExit" }
Get-Item -LiteralPath "$pocDirectory/$OutputName.$pocExtension" | Select-Object FullName,Length
