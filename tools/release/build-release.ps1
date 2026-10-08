[CmdletBinding()]
param(
    [string]$Version,
    [ValidateSet('Release', 'Debug')][string]$Configuration = 'Release',
    [switch]$AllowBrandOverlay
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'package-guard.ps1')
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$releaseRoot = Join-Path $repo 'artifacts/release'
$scratch = Join-Path $env:TEMP ('Officelings-release-' + [Guid]::NewGuid().ToString('N'))
$source = Join-Path $scratch 'source'
$worktreeAdded = $false
$oldEncoding = [Console]::OutputEncoding
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)

function Invoke-ReleaseGit([string[]]$GitArguments) {
    $result = & git -C $repo @GitArguments
    if ($LASTEXITCODE -ne 0) { throw "git failed: $($GitArguments -join ' ')" }
    $result
}

function Invoke-HiddenReleaseProcess([string]$Exe, [string]$Arguments, [string]$RunDirectory, [string]$LogPrefix, [int]$TimeoutSeconds = 180) {
    foreach ($path in @($Exe, $RunDirectory, $LogPrefix)) {
        if ($path.Contains('"') -or $path.Contains("`n") -or $path.Contains("`r")) { throw 'Paths must not contain quotes/newlines.' }
    }
    $start = [Diagnostics.ProcessStartInfo]::new()
    $start.FileName = $Exe
    $start.Arguments = $Arguments
    $start.WorkingDirectory = $RunDirectory
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.EnvironmentVariables['LOCALAPPDATA'] = Join-Path $scratch 'isolated-localappdata'
    $start.EnvironmentVariables['DOTNET_BUNDLE_EXTRACT_BASE_DIR'] = Join-Path $scratch 'bundle-extraction'
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $start
    if (-not $process.Start()) { throw "Could not start smoke test: $Exe" }
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    try {
        $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
        while (-not $process.WaitForExit(1000)) {
            if ([DateTime]::UtcNow -gt $deadline) { throw "Smoke test timed out: $Exe" }
        }
        $process.WaitForExit()
        $exitCode = $process.ExitCode
        $readTasks = [Threading.Tasks.Task[]]@($stdout, $stderr)
        while (-not [Threading.Tasks.Task]::WaitAll($readTasks, 1000)) {
            if ([DateTime]::UtcNow -gt $deadline) { throw "Smoke log capture timed out: $Exe" }
        }
    } finally {
        if (-not $process.HasExited) { $process.Kill(); $process.WaitForExit(5000) | Out-Null }
        # A descendant can inherit the pipes after the direct child exits. Close
        # incomplete streams rather than waiting indefinitely during cleanup.
        $process.StandardOutput.BaseStream.Close()
        $process.StandardError.BaseStream.Close()
        $stdoutText = if ($stdout.Status -eq [Threading.Tasks.TaskStatus]::RanToCompletion) { $stdout.Result } else { 'Log capture interrupted.' }
        $stderrText = if ($stderr.Status -eq [Threading.Tasks.TaskStatus]::RanToCompletion) { $stderr.Result } else { 'Log capture interrupted.' }
        [IO.File]::WriteAllText($LogPrefix + '.stdout.log', $stdoutText, [Text.UTF8Encoding]::new($false))
        [IO.File]::WriteAllText($LogPrefix + '.stderr.log', $stderrText, [Text.UTF8Encoding]::new($false))
        $process.Dispose()
    }
    if ($exitCode -ne 0) { throw "Smoke test exited ${exitCode}: $(Get-Content -LiteralPath ($LogPrefix + '.stderr.log') -Raw)" }
}

function Assert-EmbeddedReleasePack([string]$Output, $Expected, [string]$Package) {
    $actual = Get-Content -LiteralPath (Join-Path $Output 'manifest.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    $state = Get-Content -LiteralPath (Join-Path $Output 'state.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    if (@($state.warnings).Count) { throw "Smoke test warnings: $($state.warnings -join '; ')" }
    foreach ($sheet in $Expected.sheets) {
        $loaded = @($actual.sheets | Where-Object id -eq $sheet.id)
        $expectedHash = (Get-FileHash -LiteralPath (Join-Path (Join-Path $source 'assets/pack') $sheet.file) -Algorithm SHA256).Hash
        if ($loaded.Count -ne 1 -or $loaded[0].sha256 -ne $expectedHash) { throw "Embedded pack missing/changed: $($sheet.id)" }
        $exportedHash = (Get-FileHash -LiteralPath (Join-Path $Output $loaded[0].file) -Algorithm SHA256).Hash
        if ($exportedHash -ne $expectedHash) { throw "Exported embedded sheet hash mismatch: $($sheet.id)" }
    }
    if (@($actual.characters).Count -ne @($Expected.characters).Count -or @($Expected.sheets).Count -eq 0) {
        throw 'Embedded character roster missing or replaced by fallback.'
    }
    # The fabricated smoke state stays in TEMP. Only exported art is inspected;
    # state/settings are never copied into a package.
    Assert-ReleasePackage -PackagePath $Package -BrandRoots @($repo, $source) `
        -AssetManifestPath (Join-Path $Output 'manifest.json') -AllowBrandOverlay:$AllowBrandOverlay
}

try {
    $head = Invoke-ReleaseGit @('rev-parse', 'HEAD')
    & git -C $repo check-ignore -q --no-index artifacts/release/probe.txt
    if ($LASTEXITCODE -ne 0) { throw '.gitignore must ignore artifacts/ before packaging.' }
    New-Item -ItemType Directory -Path $scratch | Out-Null
    Invoke-ReleaseGit @('worktree', 'add', '--detach', $source, $head) | Out-Host
    $worktreeAdded = $true
    if (-not $Version) {
        [xml]$props = Get-Content -LiteralPath (Join-Path $source 'Directory.Build.props') -Raw
        $versionNode = $props.SelectSingleNode('/Project/PropertyGroup/Version')
        if (-not $versionNode) { $versionNode = $props.SelectSingleNode('/Project/PropertyGroup/VersionPrefix') }
        $Version = if ($versionNode -and $versionNode.InnerText -notmatch '\$\(') { $versionNode.InnerText } else {
            (Invoke-ReleaseGit @('describe', '--tags', '--always', $head)) -replace '^v(?=\d)', ''
        }
    }
    if ($Version -notmatch '^[A-Za-z0-9][A-Za-z0-9.+-]{0,100}$') { throw 'Version must be a safe filename component (letters, numbers, dots, + and -).' }
    $out = Join-Path $releaseRoot $Version
    Remove-ReleaseDirectory $out $releaseRoot
    New-Item -ItemType Directory -Path $out -Force | Out-Null
    Write-Host "Packaging HEAD $head as $Version ($Configuration); isolated source: $source"
    $expected = Get-Content -LiteralPath (Join-Path $source 'assets/pack/assets.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    $rows = @()
    $archives = @()
    foreach ($selfContained in @($false, $true)) {
        $variant = if ($selfContained) { 'selfcontained' } else { 'framework-dependent' }
        $suffix = if ($selfContained) { '-selfcontained' } else { '' }
        $package = Join-Path $out $variant
        foreach ($project in @('App', 'Tracker')) {
            $destination = Join-Path $package $project.ToLowerInvariant()
            $projectFile = Join-Path $source "src/Officelings.$project/Officelings.$project.csproj"
            # Native WPF/runtime libraries extract only when executed. No trimming:
            # both executables must retain their embedded resource assemblies.
            & dotnet publish $projectFile -c $Configuration -r win-x64 --self-contained $selfContained.ToString().ToLowerInvariant() `
                -o $destination '-p:PublishSingleFile=true' '-p:IncludeNativeLibrariesForSelfExtract=true' `
                '-p:PublishTrimmed=false' '-p:DebugType=None' '-p:DebugSymbols=false' '-p:UseSharedCompilation=false' '-nodeReuse:false' 2>&1 |
                Tee-Object -FilePath (Join-Path $out "publish-$variant-$project.log") | Out-Host
            if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed: $variant $project" }
            $files = @(Get-ChildItem -LiteralPath $destination -File)
            if ($files.Count -ne 1 -or $files[0].Name -ne "Officelings.$project.exe") { throw "Expected one executable in $destination; found: $($files.Name -join ', ')" }
            $run = Join-Path $scratch "$variant-$project"
            New-Item -ItemType Directory -Path $run | Out-Null
            $exe = Join-Path $run $files[0].Name
            Copy-Item -LiteralPath $files[0].FullName -Destination $exe
            $smoke = Join-Path $run 'smoke'
            $log = Join-Path $out "smoke-$variant-$project"
            if ($project -eq 'App') {
                Invoke-HiddenReleaseProcess $exe ('--demo --smoke-test "{0}"' -f $smoke) $run $log
                $report = Get-Content -LiteralPath (Join-Path $smoke 'smoke.txt') -Raw -Encoding UTF8
                if ($report -notmatch 'demo=True' -or $report -notmatch 'logBytesRead=0' -or $report -match 'fallback|rejected') {
                    throw "WPF demo smoke did not load a clean embedded pack: $report"
                }
                Copy-Item -LiteralPath (Join-Path $smoke 'smoke.txt') -Destination ($log + '.txt')
            } else {
                $emptyHome = Join-Path $run 'empty-home'
                New-Item -ItemType Directory -Path $emptyHome | Out-Null
                # Tracker has no --demo/--smoke-test. Empty home + isolated
                # LOCALAPPDATA exercise the embedded pack without real logs.
                Invoke-HiddenReleaseProcess $exe ('--once --home "{0}" --output "{1}" --layout "{2}"' -f $emptyHome, $smoke, (Join-Path $run 'missing-layout.json')) $run $log
                $trackerReport = Get-Content -LiteralPath ($log + '.stdout.log') -Raw -Encoding UTF8
                if ($trackerReport -notmatch 'log bytes read=0') { throw 'Tracker smoke unexpectedly read logs.' }
            }
            Assert-EmbeddedReleasePack $smoke $expected $package
            $rows += [pscustomobject]@{ Variant = $variant; Project = $project; Bytes = $files[0].Length; Smoke = 'PASS'; EmbeddedSheets = @($expected.sheets).Count }
            Write-Host ('{0} {1}: {2:N2} MiB ({3} bytes); smoke PASS, embedded sheets={4}' -f $variant, $project, ($files[0].Length / 1MB), $files[0].Length, @($expected.sheets).Count)
        }
        $documents = @('LICENSE', 'assets/LICENSE.md', 'TRADEMARKS.md', 'THIRD_PARTY_NOTICES.md', 'docs/windows-security-notice.md')
        $readmes = @(Get-ChildItem -LiteralPath $source -Filter 'README*.md' -File)
        if (-not $readmes.Count) { Write-Warning 'Missing README*.md; skipped.' }
        $documents += @($readmes | ForEach-Object Name)
        foreach ($document in $documents) {
            $documentSource = Join-Path $source $document
            if (Test-Path -LiteralPath $documentSource -PathType Leaf) {
                $target = Join-Path $package $document
                New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
                Copy-Item -LiteralPath $documentSource -Destination $target
            } else { Write-Warning "Missing $document in HEAD; skipped." }
        }
        Assert-ReleasePackage -PackagePath $package -BrandRoots @($repo, $source) -AllowBrandOverlay:$AllowBrandOverlay
        $zip = Join-Path $out "Officelings-$Version-win-x64$suffix.zip"
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        [IO.Compression.ZipFile]::CreateFromDirectory($package, $zip, [IO.Compression.CompressionLevel]::Optimal, $false)
        $archives += $zip
        $packageBytes = (Get-ChildItem -LiteralPath $package -File -Recurse | Measure-Object Length -Sum).Sum
        Write-Host ('{0}: folder {1:N2} MiB; ZIP {2:N2} MiB ({3} bytes)' -f $variant, ($packageBytes / 1MB), ((Get-Item -LiteralPath $zip).Length / 1MB), (Get-Item -LiteralPath $zip).Length)
    }
    $sums = @($archives | ForEach-Object { '{0}  {1}' -f (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant(), [IO.Path]::GetFileName($_) })
    $sums | Set-Content -LiteralPath (Join-Path $out 'SHA256SUMS.txt') -Encoding ASCII
    # At a tagged HEAD, exclude that tag itself when choosing the previous tag.
    # Windows PowerShell treats native stderr as a terminating error under Stop;
    # having no previous tag is expected, rather than a failed release.
    $previousPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $previous = & git -C $repo describe --tags --abbrev=0 "$head^" 2>$null
        if ($LASTEXITCODE -ne 0) { $previous = $null }
    } finally { $ErrorActionPreference = $previousPreference }
    $range = if ($previous) { "$previous..$head" } else { $head }
    $subjects = @(Invoke-ReleaseGit @('log', '--reverse', '--format=%s', $range))
    $groups = [ordered]@{}
    foreach ($subject in $subjects) {
        $group = if ($subject -match '^(?<type>[a-zA-Z]+)(\([^)]*\))?!?:\s') { $Matches.type.ToLowerInvariant() } else { 'other' }
        if (-not $groups.Contains($group)) { $groups[$group] = @() }
        $groups[$group] += $subject
    }
    $notes = @("# Officelings $Version", '', "Source commit: $head", '', $(if ($previous) { "Changes since $previous." } else { 'No previous tag found; includes history through this commit.' }), '')
    foreach ($group in $groups.Keys) { $notes += "## $group", ''; $notes += @($groups[$group] | ForEach-Object { "- $_" }); $notes += '' }
    $notes += @('## Unsigned binaries', '',
        'These binaries are unsigned. Windows may show a warning when you run them. Read [the Windows security notice](docs/windows-security-notice.md) when available and verify the downloaded ZIP against SHA256SUMS.txt before extracting it. Hashes verify integrity; obtain the checksum file from the same trusted release source.', '',
        'From the directory containing the ZIPs and SHA256SUMS.txt:', '', '```powershell',
        'Get-Content .\SHA256SUMS.txt | ForEach-Object {',
        '    $hash, $file = $_ -split ''  '', 2',
        '    if ((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ne $hash) { throw "SHA256 mismatch: $file" }',
        '    "OK $file"', '}', '```', '', 'Or with sha256sum:', '', '```sh', 'sha256sum --check SHA256SUMS.txt', '```', '',
        'Both ZIPs contain app/ and tracker/. The framework-dependent variant requires .NET 10 Windows Desktop Runtime x64; the self-contained variant includes its runtime. No installer or code signing is included.')
    $notes | Set-Content -LiteralPath (Join-Path $out 'release-notes.md') -Encoding UTF8
    [pscustomobject]@{ Version = $Version; Commit = $head; Configuration = $Configuration; AllowBrandOverlay = [bool]$AllowBrandOverlay; PreviousTag = $previous; Executables = $rows } |
        ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $out 'smoke-results.json') -Encoding UTF8
    Write-Host "Release dry run complete: $out (nothing uploaded, tagged or committed)."
} finally {
    if ($worktreeAdded) {
        # Verify the absolute target is this run's TEMP/source before git removes it.
        if ([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($source)) -ne [IO.Path]::GetFullPath($scratch)) { throw 'Unsafe worktree cleanup path.' }
        Invoke-ReleaseGit @('worktree', 'remove', '--force', $source) | Out-Host
    }
    Remove-ReleaseDirectory $scratch ([IO.Path]::GetFullPath($env:TEMP))
    [Console]::OutputEncoding = $oldEncoding
}
