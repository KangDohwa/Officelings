param(
    [Parameter(Mandatory=$true)][string]$Tracker,
    [Parameter(Mandatory=$true)][string]$Python,
    [Parameter(Mandatory=$true)][string]$Output,
    [ValidateSet('quiet','long-soak')][string]$Scenario='quiet',
    [int]$WarmupSeconds=30, [int]$SampleSeconds=30
)
$ErrorActionPreference='Stop'
$Output=[IO.Path]::GetFullPath($Output)
if (Test-Path -LiteralPath $Output) { throw 'Use a new output directory.' }
New-Item -ItemType Directory -Path $Output | Out-Null
$savedTemp=$env:TEMP; $savedTmp=$env:TMP
try {
    $env:TEMP=Join-Path $Output 'synthetic-temp'; $env:TMP=$env:TEMP
    New-Item -ItemType Directory -Path $env:TEMP | Out-Null
    $simulator=Join-Path $PSScriptRoot '../simulate/simulate.py'
    foreach ($repeat in 1..3) {
        $fixtureHome=Join-Path $env:TEMP "home-$repeat"
        $state=Join-Path $Output "state-$repeat"
        $sim=$null
        try {
            if ($Scenario -eq 'quiet') {
                & $Python -B $simulator --scenario quiet --home $fixtureHome --speed 1000 > "$Output/simulator-$repeat.log"
                if ($LASTEXITCODE -ne 0) { throw 'Synthetic fixture generation failed' }
            } else {
                $sim=Start-Process -FilePath $Python -ArgumentList @('-B',('"'+$simulator+'"'),'--scenario','long-soak','--home',('"'+$fixtureHome+'"'),'--speed','20') -WindowStyle Hidden -PassThru -RedirectStandardOutput "$Output/simulator-$repeat.log" -RedirectStandardError "$Output/simulator-error-$repeat.log"
                $deadline=[DateTime]::UtcNow.AddSeconds(30)
                while (!(Test-Path "$fixtureHome/manifest.json")) {
                    if ($sim.HasExited -or [DateTime]::UtcNow -gt $deadline) { throw 'Simulator did not initialize' }
                    Start-Sleep -Milliseconds 100
                }
            }
            $fixture=Get-Content "$fixtureHome/manifest.json" -Raw | ConvertFrom-Json
            $workingSeconds=if ($fixture.scenario.working_seconds) { [double]$fixture.scenario.working_seconds } else { 40.0 }
            $goneSeconds=if ($fixture.scenario.gone_seconds) { [double]$fixture.scenario.gone_seconds } else { 160.0 }
            $working=($workingSeconds/$fixture.speed).ToString([Globalization.CultureInfo]::InvariantCulture)
            $goneMinutes=($goneSeconds/$fixture.speed/60).ToString([Globalization.CultureInfo]::InvariantCulture)
            & (Join-Path $PSScriptRoot 'collect-process.ps1') -Executable $Tracker -Arguments @('--home',$fixtureHome,'--output',$state,'--seconds',([string]($WarmupSeconds+$SampleSeconds+90)),'--working-seconds',$working,'--leave-after-idle-minutes',$goneMinutes,'--leave-after-waiting-minutes',$goneMinutes) -Output (Join-Path $Output "repeat-$repeat") -WarmupSeconds $WarmupSeconds -SampleSeconds $SampleSeconds
            if ($Scenario -eq 'quiet') {
                $finalState=Get-Content "$state/state.json" -Raw | ConvertFrom-Json
                if (@($finalState.agents | Where-Object { $_.agent.state -ne 'gone' }).Count) { throw 'Quiet fixture still contains live agents.' }
            }
            Write-Output "$Scenario repeat $repeat completed"
        } finally {
            if ($sim) { if (!$sim.HasExited) { Stop-Process -Id $sim.Id }; $sim.Dispose() }
        }
    }
} finally { $env:TEMP=$savedTemp; $env:TMP=$savedTmp }
