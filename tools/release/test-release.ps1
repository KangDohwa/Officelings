[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'package-guard.ps1')
$parent = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../artifacts/release'))
$fixture = Join-Path $parent ('guard-test-' + [Guid]::NewGuid().ToString('N'))
$failed = 0
function Test-Guard([string]$Name, [scriptblock]$Action, [string]$ExpectedError) {
    try {
        & $Action
        if ($ExpectedError) { throw "Expected rejection matching: $ExpectedError" }
        Write-Host "PASS: $Name"
    } catch {
        if ($ExpectedError -and $_.Exception.Message -match $ExpectedError -and $_.Exception.Message -notmatch '^Expected rejection') {
            Write-Host "PASS: $Name (rejected: $($_.Exception.Message))"
        } else { $script:failed++; Write-Host "FAIL: $Name : $($_.Exception.Message)" }
    }
}
try {
    $dirty = Join-Path $fixture 'dirty'
    $clean = Join-Path $fixture 'clean'
    New-Item -ItemType Directory -Path $dirty, $clean -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $dirty 'claude-logo.png') -Value 'dummy logo, deliberately not a real image'
    Set-Content -LiteralPath (Join-Path $clean 'README.md') -Value 'Clean package fixture.'
    Test-Guard 'dummy claude-logo.png fails' { Assert-ReleasePackage $dirty } 'brand logo'
    Test-Guard 'clean package passes' { Assert-ReleasePackage $clean } ''
    Test-Guard 'explicit brand override passes' { Assert-ReleasePackage $dirty -AllowBrandOverlay } ''
    foreach ($name in @('state.json', 'settings.json')) {
        Set-Content -LiteralPath (Join-Path $dirty $name) -Value '{}'
        Test-Guard "$name fails even with brand override" { Assert-ReleasePackage $dirty -AllowBrandOverlay } 'private state/settings'
        Remove-Item -LiteralPath (Join-Path $dirty $name)
    }
    $private = Join-Path $fixture 'LocalAppData'
    New-Item -ItemType Directory -Path $private | Out-Null
    Test-Guard 'LocalAppData subtree fails' { Assert-ReleasePackage $fixture } 'LocalAppData path'
    Remove-Item -LiteralPath $private
    Test-Guard 'actual LocalAppData root fails' { Assert-ReleasePackage $env:LOCALAPPDATA } '%LOCALAPPDATA%'
    $junctionTarget = Join-Path $fixture 'private-target'
    $junction = Join-Path $fixture 'alias'
    New-Item -ItemType Directory -Path (Join-Path $junctionTarget 'package') -Force | Out-Null
    New-Item -ItemType Junction -Path $junction -Target $junctionTarget | Out-Null
    try {
        Test-Guard 'ancestor junction fails with override' {
            Assert-ReleasePackage (Join-Path $junction 'package') -AllowBrandOverlay
        } 'links/junctions'
    } finally { [IO.Directory]::Delete($junction) }

    # Independent tiny image fixture proves a renamed, embedded tag is caught.
    Add-Type -AssemblyName System.Drawing
    $overlay = Join-Path $fixture 'reference/assets/brand-overlay'
    $art = Join-Path $fixture 'art'
    New-Item -ItemType Directory -Path $overlay, $art -Force | Out-Null
    $pixel = [Drawing.Bitmap]::new(2, 2)
    try {
        $pixel.SetPixel(0, 0, [Drawing.Color]::Red)
        $pixel.Save((Join-Path $overlay 'claude.png'), [Drawing.Imaging.ImageFormat]::Png)
        $pixel.Save((Join-Path $art 'hash123.png'), [Drawing.Imaging.ImageFormat]::Png)
    } finally { $pixel.Dispose() }
    $manifest = Join-Path $art 'manifest.json'
    Set-Content -LiteralPath $manifest -Value '{"sheets":[{"id":"provider-icons","file":"hash123.png","cellW":2,"cellH":2}]}'
    Test-Guard 'hashed tag matching overlay pixels fails' {
        Assert-ReleasePackage $art -BrandRoots @(Join-Path $fixture 'reference') -AssetManifestPath $manifest
    } 'embedded provider-icons matches'
    Test-Guard 'hashed tag override passes' {
        Assert-ReleasePackage $art -BrandRoots @(Join-Path $fixture 'reference') -AssetManifestPath $manifest -AllowBrandOverlay
    } ''
    Test-Guard 'renamed loose PNG matches without manifest' {
        Assert-ReleasePackage $art -BrandRoots @(Join-Path $fixture 'reference')
    } 'hash123.png matches'
    Set-Content -LiteralPath $manifest -Value '{"sheets":[{"id":"characters","file":"hash123.png","cellW":2,"cellH":2}]}'
    Test-Guard 'renamed embedded sheet ID still fails' {
        Assert-ReleasePackage $art -BrandRoots @(Join-Path $fixture 'reference') -AssetManifestPath $manifest
    } 'embedded characters matches'
    $different = [Drawing.Bitmap]::new(2, 2)
    try {
        $different.SetPixel(0, 0, [Drawing.Color]::Blue)
        $different.Save((Join-Path $art 'hash123.png'), [Drawing.Imaging.ImageFormat]::Png)
    } finally { $different.Dispose() }
    Test-Guard 'different pixels pass with overlay present' {
        Assert-ReleasePackage $art -BrandRoots @(Join-Path $fixture 'reference') -AssetManifestPath $manifest
    } ''
} finally { Remove-ReleaseDirectory $fixture $parent }
if ($failed) { Write-Host "FAIL: $failed release guard checks"; exit 1 }
Write-Host 'PASS: all release guard checks'
