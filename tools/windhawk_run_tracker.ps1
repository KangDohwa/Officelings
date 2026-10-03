param(
    [Parameter(Mandatory = $true)][string]$TrackerExe,
    [Parameter(Mandatory = $true)][string]$AssetDirectory,
    [Parameter(Mandatory = $true)][string]$LayoutFile,
    [Parameter(Mandatory = $true)][string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
try {
    # Start-Process uses one Windows command line; double-quote each complete
    # path argument. Refuse embedded quotes/newlines rather than interpolating code.
    foreach ($trackerPath in @($TrackerExe, $AssetDirectory, $LayoutFile, $OutputDirectory)) {
        if ($trackerPath.Contains('"') -or $trackerPath.Contains("`r") -or $trackerPath.Contains("`n") -or $trackerPath.EndsWith('\')) { throw 'Use absolute paths without trailing slashes, quotes or newlines.' }
    }
    $trackerArgs = '--asset-dir "{0}" --layout "{1}" --output "{2}"' -f $AssetDirectory, $LayoutFile, $OutputDirectory
    $trackerProcess = Start-Process -FilePath $TrackerExe -ArgumentList $trackerArgs -WorkingDirectory ([IO.Path]::GetDirectoryName($TrackerExe)) -WindowStyle Hidden -PassThru
    # Wait keeps Task Scheduler's IgnoreNew/restart/stop policies on the runner.
    # On normal wrapper shutdown, clean up a still-running child. Abrupt task-stop
    # handling is Task Scheduler's responsibility and belongs in the manual test.
    try { $trackerProcess.WaitForExit(); exit $trackerProcess.ExitCode }
    finally { if (-not $trackerProcess.HasExited) { Stop-Process -Id $trackerProcess.Id -ErrorAction SilentlyContinue } }
} catch { Write-Error $_; exit 1 }
