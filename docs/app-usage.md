# Standalone app

The WPF app reads session sources itself. It does not need the headless tracker or Windhawk. Build and run commands are in the [README](../README.md); [tracker usage](tracker.md) covers the separate console program.

## Window and controls

The default window is 720x480 and fits the compact office at an integer zoom. Wheel, with or without Ctrl, and `+` / `-` select 1x through 6x. Drag to pan. Hover shows provider, role, model, project, last prompt, tool, state and elapsed time. The tooltip is passive.

Right-click the office or tray to fit the office, switch between normal, always-on-top and desktop-level modes, or change click-through, borders, transparent background, opacity and **Name tags**. Double-click the tray icon or choose **Show normal, clickable office** to recover. Closing exits and removes the icon.

Name tags default to **Hover**: only the character under the pointer shows its `[provider icon] name` plate. A reserved desk shows its owner's plate only while that desk is hovered and its owner is away. **Icon** keeps a 12×12 world-pixel provider icon above each head and on away owners' desks; hover reveals the full plate. **Always** keeps full plates visible. Both menus apply the selection immediately and save it for the next launch.

Hover plates fade in over 120ms. Leaving has a 60ms boundary grace, then a 200ms fade out. Moving between the character and its plate, including the gap, keeps the same hover. Detail tooltips appear after 420ms on the same target, remain passive and avoid all visible plates; a tooltip is suppressed if the window has no room. Click-through uses the existing passive cursor polling. Only the tag layer redraws during fades.

Desktop mode embeds beneath Explorer's icon layer using WorkerW or the Windows 11 24H2 raw-desktop Progman layout. If embedding fails, it falls back to `HWND_BOTTOM`. Click-through hover polls the cursor without activating the window or intercepting clicks. Shell integration has [limits](limits.md#desktop-and-lock-screen).

## Settings

Normal use writes `%LOCALAPPDATA%\Vibedotcom\settings.json` atomically. Close the app before editing timeouts.

| Key | Default and accepted values |
| --- | --- |
| `mode` | `normal`; also `topmost`, `desktop`. |
| `tagMode` | `hover`; also `icon`, `always`. Missing, null or unknown values fall back to `hover`. Renderer setting; never stored in tracker `state.json`. |
| `clickThrough` | `false`. |
| `zoom` | `0` for auto-fit; integers 1 through 6. Fractional values are rounded. |
| `width`, `height` | 720, 480; window sizing clamps to 400–3000 and 300–2000 respectively. |
| `left`, `top` | Unset; otherwise saved screen coordinates. |
| `borderless` | `false`. |
| `transparentBackground` | `false`. |
| `opacity` | `1`; applied range 0.25 through 1. |
| `workingSeconds` | 120; applied range 1 through 86400 seconds. |
| `leaveAfterIdleMinutes` | 5; applied range 1 through 10080 minutes. Time since real activity for finished/inactive agents. |
| `leaveAfterWaitingMinutes` | 30; applied range 1 through 10080 minutes. Explicit input/approval waits use this window. |
| `goneMinutes` | Deprecated alias for `leaveAfterWaitingMinutes`. Old files still load; the explicit new key wins in either JSON order. |

Older settings are accepted. Invalid/nonfinite values are normalized before use or saving. Both leave thresholds are raised when needed to exceed the working window. Saved settings include the new keys and retain `goneMinutes` with the same waiting value for compatibility.

Working/Thinking becomes Idle after `workingSeconds`. Finished workers still deliver their report, then take breaks until the idle threshold. Parents stay while a tracked descendant is active. Departure begins wherever the character is, walks to the door and fades there. Later work re-enters through the door. [Closed-session signals](sources-and-inference.md#closed-sessions) can make agents leave sooner; undetectable closures use these time thresholds.

## Art packs and office editing

Normal startup tries `%LOCALAPPDATA%\Vibedotcom\assets\manifest.json` and `%LOCALAPPDATA%\Vibedotcom\layout.json`. A pack describes PNG sheets, clips, roster, furniture and tiles. Layouts contain tiles, rotated furniture and workspace/pantry/lounge/door zones. Missing or invalid input falls back to the bundled pack in `assets/pack/`, embedded by Core, then procedural art. The current review also records fallback failures for some otherwise accepted custom packs.

Restart to reload files, or right-click and choose **Edit office**. See the [editor](editor.md) and [asset/layout format](asset-format.md). Core selects frames, anchors and rotation-specific seats for both viewers.

```powershell
dotnet run --project src/Vibedotcom.App --no-build -- --demo --asset-dir C:/MyOfficeArt --layout C:/MyOffice/layout.json
dotnet run --project src/Vibedotcom.Tracker --no-build -- --asset-dir C:/MyOfficeArt --layout C:/MyOffice/layout.json
```

Demo/QA skip implicit personal pack and layout loading. Explicit `--asset-dir` and `--layout` remain usable outside the personal app-data directory; isolated modes reject inputs inside it. Demo does not poll provider logs or save personal settings. The [review](review-app.md#f09--p1-demoqa-편집기-저장이-개인-layoutjson-경로로-연결된다) found that editor Save previously reached the personal layout. Current demo/QA code routes Save to a disposable `%TEMP%\vibedotcom-demo-*\layout.json` instead. The app attempts to remove that directory on exit. Use disposable inputs when checking the editor.

## Startup and QA flags

`--demo` repeats a scenario with three provider supervisors and six workers. Two workers begin on break. Models and tasks are fabricated; characters arrive, finish, deliver papers, take breaks and leave.

| Flag | Meaning |
| --- | --- |
| `--demo` | Invented sessions and default settings, without saving personal settings. |
| `--tags hover\|icon\|always` | Name tag startup override, including demo, editor, offscreen smoke and idle profiling. |
| `--asset-dir DIR`, `--layout FILE` | Explicit runtime art/layout inputs. |
| `--working-seconds N`, `--leave-after-idle-minutes N`, `--leave-after-waiting-minutes N` | Startup activity/leave windows; defaults 120 seconds, 5 minutes and 30 minutes. Positive finite values; applied limits and ordering match settings normalization. |
| `--gone-minutes N` | Deprecated waiting-window alias. An explicit new waiting option wins, in either argument order. |
| `--zoom 2`, `--mode desktop`, `--click-through`, `--borderless`, `--transparent`, `--opacity .85` | Startup overrides. Supplying a settings override starts from default settings rather than merging saved settings. |
| `--quit-after 30` | Bounded exit after the given seconds. |
| `--smoke-test OUTPUT_DIR` | Offscreen render and interchange exports. Add `--demo` to avoid real sources. Demo also writes `leave-1-break.png` through `leave-4-return.png` and `leave-walk.json`, using an isolated synthetic tracker and its real five-minute threshold. |
| `--smoke-tags` | With `--smoke-test`, render all three tag modes with 12 fabricated agents and programmatic hover, without reading logs or opening visible windows. Writes six PNGs, `tags.json` and `smoke.txt`. |
| `--profile-idle OUTPUT_DIR` | Bounded 20-second process CPU sample: six fabricated idle agents, 8fps WPF updates in one nonactivating offscreen window and an empty tracker home. Saves one initial image; avoids per-frame bitmap copies. |
| `--verify-opacity OUTPUT_DIR` | Owned-window opacity verification. See [limits](limits.md#verification). |
| `--qa --quit-after 5` | Own-window lifecycle check. QA requires a finite exit time above zero and at most 60 seconds. |

QA uses an offscreen, nonactivating normal window, without a tray or settings writes, and suppresses cursor polling. Demo/QA do not establish that native desktop hosting works. Commands and output details are in [limits and verification](limits.md).

```powershell
& .\src\Vibedotcom.App\bin\Debug\net10.0-windows\Vibedotcom.App.exe --demo --smoke-test "$env:TEMP\vibedotcom-tags" --smoke-tags
```

The offscreen renderer exposes `OfficeView.SimulateHover(point, time)` and `AdvanceHover(time)` to tests. They use the same hit tests and fade state as cursor polling, with no real mouse input. Simulated detail tooltips are drawn into the image instead of opening a popup.

## Screenshots

The README images in `docs/img/` were copied from the existing `artifacts/engine-qa/office-2x.png` and `tooltip.png`. That run's `smoke.txt` reports `demo=True`, nine agents and `logBytesRead=0`. The tooltip contains `demo-codex`, `C:/Demo/TinyWorkshop` and explicitly fabricated task text. These are earlier art renders, not a fresh validation of the current checkout. No image from the real-log `artifacts/app-smoke/` run is used.

For a new offscreen capture after building:

```powershell
Start-Process src/Vibedotcom.App/bin/Debug/net10.0-windows/Vibedotcom.App.exe -WindowStyle Hidden -ArgumentList '--demo --smoke-test artifacts/engine-qa' -Wait
```

Check the new `smoke.txt`, displayed text and brand icons before copying images into `docs/img/`. Use only invented `--demo` sessions. TODO: refresh seated/fixed-office images, add an editor capture with a disposable layout, and capture desktop mode after its manual check.

## Publish

```powershell
dotnet publish src/Vibedotcom.App -c Release -o artifacts/publish/app
dotnet publish src/Vibedotcom.Tracker -c Release -o artifacts/publish/tracker
# Run artifacts/publish/app/Vibedotcom.App.exe
```

These framework-dependent folders need the .NET 10 runtime on the destination; the app needs Windows Desktop Runtime. They install no service or startup entry. [Release packaging](../tools/release/README.md) also creates runtime-dependent and self-contained Windows x64 ZIPs from committed HEAD. It excludes uncommitted work. Review the [public-release checklist](release-checklist.md) and [unsigned-binary notice](windows-security-notice.md) before distributing them.
