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
-DWH_MOD_ID=L\"vibedotcom-desktop-poc\"
-DWH_MOD_VERSION=L\"0.1.0\"
-include windhawk_api.h
-Wl,--export-all-symbols
-Wall
-Wextra
-lgdiplus
-lgdi32
-luser32
'@
$pocDirectory = $PSScriptRoot.Replace('\', '/')
$pocArgs += "`n-I`"$pocInclude`"`n`"$pocEngineLib`"`n`"$pocDirectory/poc.wh.cpp`"`n-o `"$pocDirectory/poc.dll`"`n"
[IO.File]::WriteAllText("$PSScriptRoot\compile.rsp", $pocArgs, [Text.UTF8Encoding]::new($false))
$ErrorActionPreference = 'Continue'
$pocOutput = & $pocCompiler "@$PSScriptRoot\compile.rsp" 2>&1
$pocExit = $LASTEXITCODE
$ErrorActionPreference = 'Stop'
$pocOutput = @($pocOutput | ForEach-Object { $_.ToString() })
$pocOutput | Set-Content -LiteralPath "$PSScriptRoot\compile.log" -Encoding UTF8
$pocOutput
"clang exit code: $pocExit" | Add-Content -LiteralPath "$PSScriptRoot\compile.log"
if ($pocExit -ne 0) { throw "clang failed with exit code $pocExit" }
Get-Item -LiteralPath "$PSScriptRoot\poc.dll" | Select-Object FullName,Length
