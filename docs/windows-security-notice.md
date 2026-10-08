# Unsigned Windows builds

Officelings binaries are not code-signed. Windows may show SmartScreen's “Windows protected your PC” prompt for an unfamiliar download. This notice is not a claim that every download using this name is safe.

## Before running

Use **More info → Run anyway** only for a build you compiled yourself or a release whose source and hash you have verified. Microsoft describes this [SmartScreen prompt](https://learn.microsoft.com/en-us/windows/mixed-reality/design/add-custom-home-environments) for downloaded executables. If that option is unavailable because of a managed policy, follow your organization's process. Do not turn off SmartScreen or antivirus globally to run the app.

Release notes should publish SHA-256 hashes, alongside `SHA256SUMS.txt`, for each ZIP. Obtain the expected hash from the trusted release source and compare it before extracting:

```powershell
Get-FileHash -LiteralPath .\Officelings-<version>-win-x64.zip -Algorithm SHA256
```

Replace `<version>` with the downloaded version and compare the complete hash. A matching hash confirms bytes match the published value; it does not establish the publisher's identity. The [packaging guide](../tools/release/README.md) shows a check for every file in `SHA256SUMS.txt`. PowerShell documents [Get-FileHash](https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.utility/get-filehash).

## Antivirus reports

Desktop mode embeds an app window beneath the desktop shell's icons, and tracking reads local session files. The separate Windhawk mod runs in Explorer. These behaviors may attract security-tool scrutiny, but no specific antivirus false positive has been verified here. A detection should be investigated against the source and release hash rather than dismissed.

The Windhawk mod is distributed as source and compiled locally by Windhawk; no prebuilt mod binary is shipped in the release packaging. Build-only verification also creates a local DLL, without loading it into Explorer. Enable the mod only when you intend to run its [manual shell checks](../windhawk/README.md).

The tracker reads `~/.codex`, `~/.claude` and `~/.gemini` read-only. Antigravity DBs are queried through disposable copies, and its running server is queried over loopback HTTP. The app/tracker source reviewed here has no external upload endpoint. Details, output files and cleanup are in [privacy](privacy.md).

## Build from source

On Windows 11 with the .NET 10 SDK, from the repository root:

```powershell
dotnet build Officelings.sln
dotnet test Officelings.sln --no-build
dotnet run --project src/Officelings.App --no-build -- --demo
```

Start with the invented-data demo. For portable folders, follow [app publishing](app-usage.md#publish). A framework-dependent app needs the .NET 10 Windows Desktop Runtime; the release script can also make a self-contained ZIP. Source builds do not provide a code-signing identity.
