param(
    [string]$Python = 'python',
    [string]$Output,
    [string]$Originals,
    [switch]$Install,
    [switch]$NoRecolor,
    [switch]$Recolor
)

$ErrorActionPreference = 'Stop'
try {
    # PowerShell leaves GNU-style --install in the remaining arguments.
    foreach ($brandExtraArgument in $args) {
        if ($brandExtraArgument -eq '--install') { $Install = $true }
        else { throw "Unknown argument: $brandExtraArgument" }
    }
    if ($NoRecolor -and $Recolor) { throw 'Choose either -NoRecolor (the default) or -Recolor.' }
    $brandFetchArgs = @('-B', (Join-Path $PSScriptRoot 'fetch_brand_icons.py'))
    if ($Output) { $brandFetchArgs += @('--output', $Output) }
    if ($Originals) { $brandFetchArgs += @('--originals', $Originals) }
    if ($Install) { $brandFetchArgs += '--install' }
    if ($Recolor) { $brandFetchArgs += '--recolor' }
    else { $brandFetchArgs += '--no-recolor' }

    # An argument array preserves paths with spaces; never evaluate command strings.
    & $Python @brandFetchArgs
    exit $LASTEXITCODE
} catch {
    [Console]::Error.WriteLine("Brand fetch failed: {0}. Use -Python to select an existing Python with Pillow.", $_.Exception.Message)
    exit 1
}
