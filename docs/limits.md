# Limits and verification

Vibedotcom observes source activity, not the tools' computation or approval UI. [Sources and inference](sources-and-inference.md) covers provider paths, bounded history and fallback rules. [Privacy](privacy.md) covers the local data shown or exported.

## Tracking and rendering

Log events may arrive late. Detached/background workers without transcripts cannot be discovered. Cold-start history outside the bounded JSONL prefix/suffix is intentionally not reconstructed. Claude worker parsing has synthetic coverage, but the initial investigation saw no live worker transcript. Antigravity remains `bestEffort:true`; its saved state, DB copies and local API can be incomplete or unavailable.

The WPF app renders up to 30 fps while walking and 8 fps otherwise. It redraws when positions/frames change and caches the static room and decoded crops. Live tracker polling runs off the UI thread. Headless snapshots default to about 5 fps. No actual video or audio plays in the office. Missing parents use the worker's own desk vicinity. Rooms larger than the viewport at 1x require panning.

Tests exist for parsing, tailing, activity, pathfinding, sprites and snapshots. They do not imply that every layout, malformed setting or concurrent art publication is safe. The dated [Core review](review-core.md), [App review](review-app.md) and [Windhawk review](review-windhawk.md) record reproduced defects and verification gaps. Compare each finding with the current code before marking it resolved.

## Desktop and lock screen

Standalone desktop hosting uses Explorer's private wallpaper protocol. Shell restarts and virtual-desktop changes can affect visibility. A lost host is retried; failure falls back to bottom z-order. Desktop icon hit testing cannot distinguish every icon shape. Click-through passes clicks through the office; recover through the tray. Raw-desktop compatibility is a best-effort shell integration, following [Windows layered-window rules](https://learn.microsoft.com/en-us/windows/win32/winmsg/window-features) where applicable.

Windhawk runs in shell Explorer and has different hosting and recovery behavior. Use its settings to recover click-through; it has no tray. Its offscreen and owned-window tests do not verify live Explorer parenting, Win+D, virtual-desktop switching, mixed-DPI movement or long-term Explorer CPU/GDI use. See the [manual checklist](../windhawk/README.md#manual-test-checklist--user-performs-later).

Neither variant displays on the secure lock screen. A normal user application remains on the Default desktop and lacks access to the protected Winlogon desktop. No workaround is attempted. [Microsoft's desktop security documentation](https://learn.microsoft.com/en-us/windows/win32/winstation/desktops) explains this separation.

## Verification

After a build, parser/simulation tests run with:

```powershell
dotnet test Vibedotcom.sln --no-build
```

The WPF smoke check renders without opening a window or saving settings. For a fabricated run after publishing, or after a Debug build:

```powershell
Start-Process artifacts/publish/app/Vibedotcom.App.exe -WindowStyle Hidden -ArgumentList '--demo --smoke-test artifacts/app-smoke' -Wait
Start-Process src/Vibedotcom.App/bin/Debug/net10.0-windows/Vibedotcom.App.exe -WindowStyle Hidden -ArgumentList '--demo --smoke-test artifacts/engine-qa' -Wait
```

Earlier README commands without `--demo` are still available for private real-source diagnostics:

```powershell
Start-Process artifacts/publish/app/Vibedotcom.App.exe -WindowStyle Hidden -ArgumentList '--smoke-test artifacts/app-smoke' -Wait
Start-Process src/Vibedotcom.App/bin/Debug/net10.0-windows/Vibedotcom.App.exe -WindowStyle Hidden -ArgumentList '--smoke-test artifacts/app-smoke' -Wait
```

Without `--demo`, these read recent logs. Do not use their output in public screenshots. Demo smoke writes phase captures, `office-1x.png`, `office-2x.png`, `office-3x.png`, a cropped viewport, `tooltip.png`, `transparent.png`, `smoke.txt` and interchange files. Hover uses a programmatic hit-test hook, without real cursor movement or keystrokes. It checks offscreen layout/rendering, not native z-order or tray interaction.

Other bounded checks:

```powershell
# 20-second CPU sample with fabricated idle agents and an empty tracker home:
Start-Process src/Vibedotcom.App/bin/Debug/net10.0-windows/Vibedotcom.App.exe -WindowStyle Hidden -ArgumentList '--profile-idle artifacts/idle-profile' -Wait
# Own-window lifecycle check, offscreen and nonactivating:
Start-Process src/Vibedotcom.App/bin/Debug/net10.0-windows/Vibedotcom.App.exe -WindowStyle Hidden -ArgumentList '--demo --qa --quit-after 5' -Wait
# Owned-window opacity check:
Start-Process src/Vibedotcom.App/bin/Debug/net10.0-windows/Vibedotcom.App.exe -WindowStyle Hidden -ArgumentList '--verify-opacity artifacts/opacity-qa' -Wait
```

QA forces normal z-order, suppresses cursor polling and writes neither tray state nor personal settings. Fake HWND topologies check desktop/topmost/click-through policy and classic/Windows 11 host selection without altering the shell. These checks use no global input, Show Desktop, foreground activation or termination of unrelated processes. The [app review](review-app.md) found an earlier editor Save isolation failure. Current code uses a disposable Save directory for demo/QA; its recent changes need release verification. Use disposable layout inputs for that check.

The idle sample computes process CPU seconds / wall seconds / logical processors. It is a bounded sample, not a performance guarantee for every machine or session set. Windhawk compilation, native harnesses and parity procedures are in its [guide](../windhawk/README.md); Python tool checks are in [contributing](../CONTRIBUTING.md).
