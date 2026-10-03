# Standalone app

The WPF app reads session sources itself. It does not need the headless tracker or Windhawk. Build and run commands are in the [README](../README.md); [tracker usage](tracker.md) covers the separate console program.

## Window and controls

The default window is 720x480 and fits the compact office at an integer zoom. Wheel, with or without Ctrl, and `+` / `-` select 1x through 6x. Drag to pan. Hover shows provider, role, model, project, last prompt, tool, state and elapsed time. The tooltip is passive.

Right-click the office or tray to fit the office, switch between normal, always-on-top and desktop-level modes, or change click-through, borders, transparent background and opacity. Double-click the tray icon or choose **Show normal, clickable office** to recover. Closing exits and removes the icon.

Desktop mode embeds beneath Explorer's icon layer using WorkerW or the Windows 11 24H2 raw-desktop Progman layout. If embedding fails, it falls back to `HWND_BOTTOM`. Click-through hover polls the cursor without activating the window or intercepting clicks. Shell integration has [limits](limits.md#desktop-and-lock-screen).

## Settings

Normal use writes `%LOCALAPPDATA%\Vibedotcom\settings.json` atomically. Close the app before editing timeouts.

| Key | Default and accepted values |
| --- | --- |
| `mode` | `normal`; also `topmost`, `desktop`. |
| `clickThrough` | `false`. |
| `zoom` | `0` for auto-fit; integers 1 through 6. Fractional values are rounded. |
| `width`, `height` | 720, 480; window sizing clamps to 400–3000 and 300–2000 respectively. |
| `left`, `top` | Unset; otherwise saved screen coordinates. |
| `borderless` | `false`. |
| `transparentBackground` | `false`. |
| `opacity` | `1`; applied range 0.25 through 1. |
| `workingSeconds` | 120; applied range 1 through 86400 seconds. |
| `goneMinutes` | 30; applied range 1 through 10080 minutes. It must exceed the working window. |

Older settings are accepted. The current code normalizes invalid/nonfinite values before using or saving them, and raises the gone timeout when needed to exceed the working window. The [app review](review-app.md) records the earlier nonfinite-value save failure. These recent code changes were read for this guide, not rerun as a new verification.

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
| `--asset-dir DIR`, `--layout FILE` | Explicit runtime art/layout inputs. |
| `--zoom 2`, `--mode desktop`, `--click-through`, `--borderless`, `--transparent`, `--opacity .85` | Startup overrides. Supplying a settings override starts from default settings rather than merging saved settings. |
| `--quit-after 30` | Bounded exit after the given seconds. |
| `--smoke-test OUTPUT_DIR` | Offscreen render and interchange exports. Add `--demo` to avoid real sources. |
| `--profile-idle OUTPUT_DIR` | Bounded 20-second process CPU sample using six fabricated idle agents and an empty tracker home. |
| `--verify-opacity OUTPUT_DIR` | Owned-window opacity verification. See [limits](limits.md#verification). |
| `--qa --quit-after 5` | Own-window lifecycle check. QA requires a finite exit time above zero and at most 60 seconds. |

QA uses an offscreen, nonactivating normal window, without a tray or settings writes, and suppresses cursor polling. Demo/QA do not establish that native desktop hosting works. Commands and output details are in [limits and verification](limits.md).

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
