param([switch]$Tests)
$ErrorActionPreference = 'Stop'
$windhawkOut = Join-Path $PSScriptRoot 'artifacts'
$windhawkOptions = @('-fms-extensions', '-lbcrypt', '-lole32', '-ldwmapi', '-ldwrite')
& "$PSScriptRoot\poc\compile.ps1" -Source "$PSScriptRoot\vibedotcom.wh.cpp" -OutputDirectory $windhawkOut -OutputName 'vibedotcom' -ModId 'vibedotcom' -AdditionalOptions $windhawkOptions
if ($Tests) {
    & "$PSScriptRoot\poc\compile.ps1" -Source "$PSScriptRoot\..\tests\windhawk\renderer_tests.cpp" -OutputDirectory $windhawkOut -OutputName 'renderer_tests' -Executable -AdditionalOptions ($windhawkOptions + @('-municode'))
    # The installed compiler has no resource compiler. Write the standard .res
    # null header and RT_MANIFEST/1 directly; lld links it into this test EXE.
    # Windows checks EXE compatibility for layered children: merely activating
    # a sidecar manifest on the thread did not admit WS_EX_LAYERED | WS_CHILD.
    $windhawkManifestBytes = [IO.File]::ReadAllBytes("$PSScriptRoot\..\tests\windhawk\host_tests.manifest")
    $windhawkResStream = [IO.MemoryStream]::new()
    $windhawkResWriter = [IO.BinaryWriter]::new($windhawkResStream)
    try {
        foreach ($windhawkResource in @(@{ Data = [byte[]]@(); Type = 0; Name = 0; Language = 0 }, @{ Data = $windhawkManifestBytes; Type = 24; Name = 1; Language = 1033 })) {
            $windhawkResWriter.Write([uint32]$windhawkResource.Data.Length)
            $windhawkResWriter.Write([uint32]32)
            foreach ($windhawkOrdinal in @(65535, $windhawkResource.Type, 65535, $windhawkResource.Name)) { $windhawkResWriter.Write([uint16]$windhawkOrdinal) }
            $windhawkResWriter.Write([uint32]0) # DataVersion
            $windhawkResWriter.Write([uint16]48) # MOVEABLE | PURE
            $windhawkResWriter.Write([uint16]$windhawkResource.Language)
            $windhawkResWriter.Write([uint32]0) # Version
            $windhawkResWriter.Write([uint32]0) # Characteristics
            $windhawkResWriter.Write([byte[]]$windhawkResource.Data)
            while ($windhawkResStream.Position % 4) { $windhawkResWriter.Write([byte]0) }
        }
        $windhawkResourcePath = Join-Path $windhawkOut 'host_tests.res'
        [IO.File]::WriteAllBytes($windhawkResourcePath, $windhawkResStream.ToArray())
    } finally { $windhawkResWriter.Dispose() }
    $windhawkQuotedResource = '"' + $windhawkResourcePath.Replace('\', '/') + '"'
    & "$PSScriptRoot\poc\compile.ps1" -Source "$PSScriptRoot\..\tests\windhawk\host_tests.cpp" -OutputDirectory $windhawkOut -OutputName 'host_tests' -Executable -AdditionalOptions ($windhawkOptions + @('-municode', '-lcomctl32', $windhawkQuotedResource))
}
