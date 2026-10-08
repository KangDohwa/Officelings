[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [Parameter(Mandatory = $true)][string]$TrackerExe,
    [string]$AssetDirectory = (Join-Path $PSScriptRoot '..\assets\pack'),
    [string]$LayoutFile = (Join-Path $PSScriptRoot '..\src\Officelings.Core\Data\layout.json'),
    [string]$OutputDirectory = (Join-Path $env:LOCALAPPDATA 'Officelings'),
    [string]$TaskName = 'Officelings.Tracker',
    [switch]$Replace
)
$ErrorActionPreference = 'Stop'
# Registration only. Never start a task, build, copy assets or modify Windhawk.
$trackerResolved = (Resolve-Path -LiteralPath $TrackerExe).ProviderPath
if ([IO.Path]::GetExtension($trackerResolved) -ne '.exe') { throw 'TrackerExe must be the published Officelings.Tracker.exe.' }
$assetResolved = (Resolve-Path -LiteralPath $AssetDirectory).ProviderPath
$layoutResolved = (Resolve-Path -LiteralPath $LayoutFile).ProviderPath
$runnerResolved = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot 'windhawk_run_tracker.ps1')).ProviderPath
$outputResolved = [IO.Path]::GetFullPath($OutputDirectory)
foreach ($trackerPath in @($trackerResolved, $assetResolved, $layoutResolved, $runnerResolved, $outputResolved)) {
    if ($trackerPath.Contains('"') -or $trackerPath.Contains("`r") -or $trackerPath.Contains("`n")) { throw 'Paths must not contain quotes or newlines.' }
}
if (-not (Test-Path -LiteralPath (Join-Path $assetResolved 'assets.json') -PathType Leaf)) { throw 'AssetDirectory must contain assets.json.' }
$trackerIdentity = [Security.Principal.WindowsIdentity]::GetCurrent()
$trackerSid = $trackerIdentity.User.Value
$existingTrackerTask = Get-ScheduledTask -TaskName $TaskName -TaskPath '\' -ErrorAction SilentlyContinue
if ($existingTrackerTask -and -not $Replace) { throw "Task '$TaskName' exists. Use -Replace to explicitly replace it." }
$legacyTrackerTask = $null
if ($TaskName -eq 'Officelings.Tracker') {
    $legacyTrackerTask = Get-ScheduledTask -TaskName 'Vibedotcom.Tracker' -TaskPath '\' -ErrorAction SilentlyContinue
    if ($legacyTrackerTask) {
        $legacyTrackerUser = $legacyTrackerTask.Principal.UserId
        if ($legacyTrackerUser -ne $trackerSid) {
            $legacyTrackerUser = ([Security.Principal.NTAccount]::new($legacyTrackerUser)).Translate([Security.Principal.SecurityIdentifier]).Value
        }
        if ($legacyTrackerUser -ne $trackerSid) { throw 'The legacy tracker task belongs to another user; leave it unchanged.' }
    }
}
$trackerArguments = '-NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File "{0}" -TrackerExe "{1}" -AssetDirectory "{2}" -LayoutFile "{3}" -OutputDirectory "{4}"' -f $runnerResolved, $trackerResolved, $assetResolved, $layoutResolved, $outputResolved
$trackerAction = New-ScheduledTaskAction -Execute "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe" -Argument $trackerArguments -WorkingDirectory ([IO.Path]::GetDirectoryName($trackerResolved))
$trackerTrigger = New-ScheduledTaskTrigger -AtLogOn -User $trackerSid
$trackerPrincipal = New-ScheduledTaskPrincipal -UserId $trackerSid -LogonType Interactive -RunLevel Limited
$trackerSettings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances IgnoreNew -RestartCount 3 -RestartInterval (New-TimeSpan -Minutes 1)
if ($PSCmdlet.ShouldProcess("$TaskName ($($trackerIdentity.Name))", 'Register per-user logon tracker task and migrate any legacy task')) {
    Register-ScheduledTask -TaskName $TaskName -TaskPath '\' -Action $trackerAction -Trigger $trackerTrigger -Principal $trackerPrincipal -Settings $trackerSettings -Description 'Read-only Officelings tracker; hidden process, standard user, no Explorer injection.' -Force:$Replace | Select-Object TaskName, State
    if ($legacyTrackerTask) {
        if (-not $existingTrackerTask -and $legacyTrackerTask.State -eq 'Disabled') { Disable-ScheduledTask -TaskName $TaskName -TaskPath '\' | Out-Null }
        # Remove the old autostart entry only after the replacement is registered successfully.
        if ($legacyTrackerTask.State -eq 'Running') { Stop-ScheduledTask -InputObject $legacyTrackerTask }
        Unregister-ScheduledTask -InputObject $legacyTrackerTask -Confirm:$false
        Write-Output 'Migrated the legacy logon task; the replacement starts at next logon.'
    }
    Write-Output 'Registered only; runs at next logon. Keep the tracker, pack, layout and runner paths in place.'
}
