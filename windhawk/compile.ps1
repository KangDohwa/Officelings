param([switch]$Tests)
$ErrorActionPreference = 'Stop'
$windhawkOut = Join-Path $PSScriptRoot 'artifacts'
$windhawkOptions = @('-lbcrypt', '-lole32')
& "$PSScriptRoot\poc\compile.ps1" -Source "$PSScriptRoot\vibedotcom.wh.cpp" -OutputDirectory $windhawkOut -OutputName 'vibedotcom' -ModId 'vibedotcom' -AdditionalOptions $windhawkOptions
if ($Tests) {
    & "$PSScriptRoot\poc\compile.ps1" -Source "$PSScriptRoot\..\tests\windhawk\renderer_tests.cpp" -OutputDirectory $windhawkOut -OutputName 'renderer_tests' -Executable -AdditionalOptions ($windhawkOptions + @('-municode'))
}
