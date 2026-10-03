# Shared by build-release.ps1 and test-release.ps1; dot-sourcing does not build.
Set-StrictMode -Version Latest

function Assert-ReleaseAncestors([string]$Path) {
    $ancestor = [IO.Path]::GetFullPath($Path)
    while ($ancestor) {
        if (Test-Path -LiteralPath $ancestor) {
            if ((Get-Item -LiteralPath $ancestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
                throw "Package guard: links/junctions are not allowed: $ancestor"
            }
        }
        $ancestor = [IO.Path]::GetDirectoryName($ancestor)
    }
}

function Get-ReleaseEntries([string]$Path) {
    $item = Get-Item -LiteralPath $Path -Force -ErrorAction Stop
    if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) {
        throw "Package guard: links/junctions are not allowed: $($item.FullName)"
    }
    $item
    if ($item.PSIsContainer) {
        foreach ($child in Get-ChildItem -LiteralPath $item.FullName -Force) {
            Get-ReleaseEntries $child.FullName
        }
    }
}

function Assert-ReleasePackage {
    param(
        [Parameter(Mandatory = $true)][string]$PackagePath,
        [string[]]$BrandRoots = @(),
        [string]$AssetManifestPath,
        [switch]$AllowBrandOverlay
    )
    $root = [IO.Path]::GetFullPath($PackagePath).TrimEnd('\', '/')
    Assert-ReleaseAncestors $root
    if ($env:LOCALAPPDATA) {
        $local = [IO.Path]::GetFullPath($env:LOCALAPPDATA).TrimEnd('\', '/')
        if ($root -eq $local -or $root.StartsWith($local + '\', [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Package guard: a package under %LOCALAPPDATA% is private machine data.'
        }
    }
    $brands = @()
    $entries = @(Get-ReleaseEntries $root)
    foreach ($entry in $entries) {
        $relative = $entry.FullName.Substring($root.Length).TrimStart('\', '/') -replace '\\', '/'
        if ($entry.Name -match '^(state|settings)\.json$' -or
            $relative -match '(^|/)(%LOCALAPPDATA%|LocalAppData)(/|$)|(^|/)AppData/Local(/|$)') {
            throw "Package guard: private state/settings or LocalAppData path: $relative"
        }
        if ($relative -match '(^|/)assets/brand/(originals)(/|$)|(^|/)(assets/)?brand-overlay(/|$)' -or
            (-not $entry.PSIsContainer -and $entry.Extension -match '^\.(png|svg|ico|jpe?g|webp|gif|bmp)$' -and
             $entry.BaseName -match '(chatgpt|openai|claude|anthropic|gemini|codex|antigravity)')) {
            $brands += $relative
        }
    }

    # Hashed filenames and changed sheet IDs must not hide matching logo pixels.
    $overlays = @(foreach ($brandRoot in $BrandRoots) {
        foreach ($folder in @('assets/brand-overlay', 'assets/brand/originals')) {
            $path = Join-Path $brandRoot $folder
            if (Test-Path -LiteralPath $path) {
                Get-ReleaseEntries $path | Where-Object { -not $_.PSIsContainer -and $_.Extension -eq '.png' }
            }
        }
    })
    if ($overlays.Count) {
        Add-Type -AssemblyName System.Drawing
        $images = @(foreach ($entry in $entries | Where-Object { -not $_.PSIsContainer -and $_.Extension -eq '.png' }) {
            [pscustomobject]@{ Path = $entry.FullName; Name = $entry.Name; CellW = 0; CellH = 0 }
        })
        if ($AssetManifestPath) {
            $manifest = Get-Content -LiteralPath $AssetManifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
            $manifestRoot = [IO.Path]::GetFullPath((Split-Path -Parent $AssetManifestPath)).TrimEnd('\', '/')
            foreach ($sheet in $manifest.sheets) {
                $sheetPath = [IO.Path]::GetFullPath((Join-Path $manifestRoot $sheet.file))
                if (-not $sheetPath.StartsWith($manifestRoot + '\', [StringComparison]::OrdinalIgnoreCase)) {
                    throw 'Package guard: asset sheet escapes its manifest directory.'
                }
                if ($sheet.cellW -le 0 -or $sheet.cellH -le 0) { throw 'Package guard: invalid icon cell size.' }
                $images += [pscustomobject]@{ Path = $sheetPath; Name = "embedded $($sheet.id)"; CellW = $sheet.cellW; CellH = $sheet.cellH }
            }
        }
        foreach ($candidate in $images) {
            $bitmap = [Drawing.Bitmap]::new($candidate.Path)
            try {
                $cellW = if ($candidate.CellW) { $candidate.CellW } else { $bitmap.Width }
                $cellH = if ($candidate.CellH) { $candidate.CellH } else { $bitmap.Height }
                foreach ($overlay in $overlays) {
                    $reference = [Drawing.Bitmap]::new($overlay.FullName)
                    try {
                        if ($reference.Width -ne $cellW -or $reference.Height -ne $cellH) { continue }
                        for ($y = 0; $y + $cellH -le $bitmap.Height; $y += $cellH) {
                            for ($x = 0; $x + $cellW -le $bitmap.Width; $x += $cellW) {
                                $same = $true
                                for ($py = 0; $py -lt $reference.Height -and $same; $py++) {
                                    for ($px = 0; $px -lt $reference.Width; $px++) {
                                        if ($bitmap.GetPixel($x + $px, $y + $py).ToArgb() -ne $reference.GetPixel($px, $py).ToArgb()) {
                                            $same = $false; break
                                        }
                                    }
                                }
                                if ($same) { $brands += "$($candidate.Name) matches $($overlay.Name)" }
                            }
                        }
                    } finally { $reference.Dispose() }
                }
            } finally { $bitmap.Dispose() }
        }
    }
    if ($brands.Count) {
        $message = "Package guard: official/possible brand logo detected: $($brands -join ', '). Use -AllowBrandOverlay explicitly only for an authorized redistribution."
        if (-not $AllowBrandOverlay) { throw $message }
        Write-Warning $message
    }
}

function Remove-ReleaseDirectory([string]$Path, [string]$Parent) {
    # Every recursive removal is restricted to a direct child of its known root.
    $target = [IO.Path]::GetFullPath($Path).TrimEnd('\', '/')
    $allowed = [IO.Path]::GetFullPath($Parent).TrimEnd('\', '/')
    if ([IO.Path]::GetDirectoryName($target) -ne $allowed) { throw "Refusing cleanup outside $allowed : $target" }
    Assert-ReleaseAncestors $target
    if (Test-Path -LiteralPath $target) {
        Get-ReleaseEntries $target | Out-Null
        Remove-Item -LiteralPath $target -Recurse -Force
    }
}
