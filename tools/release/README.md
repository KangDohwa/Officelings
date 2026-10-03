# Release packaging

Run this on Windows with Git, PowerShell 5.1 or newer, and the .NET 10 SDK. From the repository root:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/release/test-release.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tools/release/build-release.ps1 -Version 0.0.0-dryrun
```

The build always uses a fresh detached worktree of HEAD in `%TEMP%`. Commit the source you intend to release before running it; uncommitted work is not included. The temporary worktree, runtime extraction, and smoke output are removed afterward. Other agents can keep editing the main checkout. The build cleans only `artifacts/release/<version>/`, which must be ignored by Git.

For a release candidate, use `-Version 1.2.3` (and optionally `-Configuration Release`, the default). If you omit the version, the script reads `Version` or `VersionPrefix` from the committed `Directory.Build.props`, then falls back to `git describe --tags --always`. The version labels the package; it does not change source files or assembly versions. Review the ZIPs, `SHA256SUMS.txt`, `release-notes.md`, and `smoke-results.json` before separately tagging or uploading anything. This script only creates local artifacts.

Each ZIP contains `app/Vibedotcom.App.exe`, `tracker/Vibedotcom.Tracker.exe`, and the requested license/readme/security files that exist in HEAD, with their relative paths preserved. Missing documents produce warnings. Add any missing license or security notice in a separate approved change before publishing; a dry run can still inspect packaging without them. The notes group commit subjects since the previous reachable tag by Conventional Commit type and preserve the original Korean text. With no previous tag, they include all history through HEAD.

- `Vibedotcom-<version>-win-x64.zip` needs the .NET 10 Windows Desktop Runtime for x64.
- `Vibedotcom-<version>-win-x64-selfcontained.zip` includes the runtime and is larger.

Single-file publishing keeps each program and its embedded art together, so there are fewer files to lose when moving the folder. Native libraries extract at runtime. We enable `IncludeNativeLibrariesForSelfExtract` and leave trimming off; see [Microsoft's single-file documentation](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview). The build copies each executable alone into TEMP and runs it hidden. WPF uses `--demo --smoke-test`; the tracker uses `--once --home` with an empty home because it has no demo switch. Both use an isolated LocalAppData directory, read no real provider logs, and must export the expected embedded sheets with matching SHA256 hashes and no warnings. The WPF check renders offscreen; it does not verify desktop hosting or foreground behavior. Sizes and results are printed; summaries and process logs remain beside the ZIPs.

Official logo paths and filenames are blocked. If a local `assets/brand-overlay` or `assets/brand/originals` PNG exists, the guard also compares its decoded RGBA pixels with packaged PNGs and embedded sheet cells, catching logos hidden behind hashed filenames or changed sheet IDs. This is an exact comparison at native dimensions, not a general logo recognizer. `-AllowBrandOverlay` explicitly permits brand matches and prints a warning; it never bypasses the privacy guard. The pack is not regenerated, and local overlay files are not copied in by this switch. `state.json`, `settings.json`, LocalAppData subtrees, links and junctions (including ancestors) are rejected. Only fresh publish output and the listed documents enter a ZIP; no user profile tree is collected.

To verify downloads, put both ZIPs and the trusted `SHA256SUMS.txt` in one directory, then run:

```powershell
Get-Content .\SHA256SUMS.txt | ForEach-Object {
    $hash, $file = $_ -split '  ', 2
    if ((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ne $hash) { throw "SHA256 mismatch: $file" }
    "OK $file"
}
```

With `sha256sum` installed, `sha256sum --check SHA256SUMS.txt` does the same check. A checksum detects changed bytes; it does not establish who published them. Use the checksum file from the trusted release source.

There is no installer, auto-start registration, Windhawk installation, or code signing. Extract the ZIP and run the app; the tracker is for a separately installed Windhawk mod. Binaries are unsigned. Read [docs/windows-security-notice.md](../../docs/windows-security-notice.md) if available; the build warns if that notice has not been added yet.

<!-- Optional future signing, intentionally disabled. No certificates are configured.
After choosing and approving a certificate, sign the finished EXEs BEFORE making
the ZIPs, then rerun the smoke checks and regenerate ZIPs and SHA256SUMS.txt.
Replace the thumbprint and timestamp URL with your approved values:

signtool sign /sha1 <approved-certificate-thumbprint> /fd SHA256 /tr <approved-timestamp-url> /td SHA256 path\Vibedotcom.App.exe
signtool verify /pa /v path\Vibedotcom.App.exe

Repeat for Vibedotcom.Tracker.exe in both variants. Do not enable this section
until the certificate and release signing process have been explicitly approved.
-->
