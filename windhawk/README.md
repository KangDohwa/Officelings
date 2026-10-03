# Vibedotcom — Windhawk Variant B

`vibedotcom.wh.cpp` hosts a passive pixel office inside the **shell instance of explorer.exe**. It reads the headless tracker's [v3 contract](../docs/state-schema.md): resolved tiles, furniture, positions, selected PNG rectangles, opacity, paper, reactions and agent metadata. Core remains responsible for animation selection, pathfinding, desks and activities. No `src/` changes or additional contract fields are required.

Concurrent Core changes observed during this work raised the writer to **v4**. The mod accepts **both v3 and v4**: v4's optional `pose.displayName`, `workLocation`, `queued`, and `providerIcon` are read; nameplates/provider icons, queued bubbles, overflow laptop overlays and the newer short relationship lines follow the updated WPF renderer. Monitor state still arrives through authoritative furniture frames. This is compatibility with existing concurrent changes, not a request for new fields. Source editors and seat allocation remain in Core/WPF. Integer zoom 1–6 remains intentional even if the newer WPF auto-fit supports fractional downscaling.

## Install the tracker

Run these commands yourself from the repository root. Use a stable local installation directory; the task stores absolute paths. A framework-dependent publish requires the .NET 10 runtime.

```powershell
dotnet publish src/Vibedotcom.Tracker -c Release -o "$env:LOCALAPPDATA\Vibedotcom\tracker"

# Foreground diagnostic run; Ctrl+C stops it.
& "$env:LOCALAPPDATA\Vibedotcom\tracker\Vibedotcom.Tracker.exe" `
    --asset-dir "$PWD\assets\pack" `
    --layout "$PWD\src\Vibedotcom.Core\Data\layout.json"
```

The tracker exports `state.json`, `manifest.json`, `atlas.png`, immutable `sheets/<sha256>.png`, and the resolved runtime layout to `%LOCALAPPDATA%\Vibedotcom`. It observes provider sources read-only. An already-running tracker owns a single-writer mutex; stop the diagnostic instance before starting a background instance.

For a hidden background process started at **your logon**, use the supplied installer:

```powershell
.\tools\windhawk_install_tracker.ps1 `
    -TrackerExe "$env:LOCALAPPDATA\Vibedotcom\tracker\Vibedotcom.Tracker.exe" `
    -WhatIf

# Remove -WhatIf when you want to register it.
.\tools\windhawk_install_tracker.ps1 `
    -TrackerExe "$env:LOCALAPPDATA\Vibedotcom\tracker\Vibedotcom.Tracker.exe"

# Optional: explicitly start the registered task now.
Start-ScheduledTask -TaskName 'Vibedotcom.Tracker'
```

The installer registers only; it does not run the tracker, copy/publish files, install a mod or request elevation. The principal is the current user's SID, interactive logon, limited privileges. The action runs `tools/windhawk_run_tracker.ps1` through hidden Windows PowerShell and starts the tracker with `Start-Process -WindowStyle Hidden`. It waits for the child so Task Scheduler can report its exit code, ignore duplicate starts and restart failures. Existing tasks require explicit `-Replace`. `-AssetDirectory`, `-LayoutFile`, `-OutputDirectory`, and `-TaskName` can be overridden. Keep the repository/runner, published executable and configured source files at these paths.

To remove the task yourself:

```powershell
Stop-ScheduledTask -TaskName 'Vibedotcom.Tracker'
Unregister-ScheduledTask -TaskName 'Vibedotcom.Tracker' -Confirm
```

Task registration, logon, task restart and task-stop behavior have **not** been executed during this implementation. Confirm that stopping the task also stops its tracker child in your manual test.

## Add the mod in Windhawk

1. Open Windhawk, choose **Create a new mod**, and open its source editor.
2. Replace the example source with the complete contents of [vibedotcom.wh.cpp](vibedotcom.wh.cpp). It is one self-contained file; there are no downloaded headers or adjacent source dependencies.
3. Save and compile in the editor. Use **Compile Mod** / enable only when ready to perform the manual Explorer tests below. These operations load code into your Explorer session.
4. Start with `normal`, click-through off, automatic zoom, and the default state path. Start the tracker separately and check the office.
5. Change settings in Windhawk. Changes request a controlled unload/reload. Windhawk retains the settings.

The mod has no function hooks. A folder-only Explorer process is rejected. If the shell has not appeared yet, the UI thread waits and verifies its PID before creating windows. Closing the normal office stops its UI until the mod is reloaded; disabling the mod also removes it.

## Settings and controls

| Setting | Behavior |
| --- | --- |
| Mode | `normal`, `topmost`, `desktop-level`. Creation/show/host repairs use `SWP_NOACTIVATE`. Normal mode can receive focus when deliberately clicked. |
| Click through | Layered `WS_EX_TRANSPARENT`; desktop/through windows additionally use `WS_EX_NOACTIVATE`. Recover through Windhawk settings. |
| Zoom | `0` auto-fit, or integer `1`–`6`; nearest-neighbor. Mouse wheel and `+`/`-` work when the window receives input. |
| Opacity | 10–100 percent for the whole office. |
| X/Y, width/height | Screen pixels, including negative monitor coordinates. Normal/topmost windows have a native movable, resizable frame. Desktop mode uses the configured dimensions. |
| State path | Local path, environment variables accepted. Default `%LOCALAPPDATA%\Vibedotcom\state.json`. |
| Pack path | **Exported PNG root**, blank = state directory. The embedded manifest remains authoritative. This is normally blank: source `assets/pack/` belongs to the tracker's `--asset-dir`, because exported sheet filenames differ. |
| FPS cap | 1–60, default 30. It is a maximum, not a constant paint timer. Live frames are sampled from the tracker's nominal 200ms snapshots. |
| Show tooltip | Read-only cursor polling, 220ms hover delay. Provider, name/role, model, project, task, tool, state, elapsed and visual action. Popup cannot take focus or input. |
| Transparent background | Skip floor/walls; keep furniture and characters. The native window uses a magenta color key and global opacity. Partial-alpha edges over this key can differ from WPF's per-pixel alpha. |

Wheel/keyboard zoom and native geometry changes are session-local and reset to configured values after a settings reload or host recreation. There are no global input hooks, task controls or tray UI.

## Desktop hosting and failure handling

Classic desktop: a child of the wallpaper WorkerW **following the icon host**, never an arbitrary first WorkerW. Windows 11 24H2+ raised/raw desktop: a layered child of Progman, a sibling of its WorkerW, inserted below `SHELLDLL_DefView`. As in the [PoC notes](../docs/windhawk-notes.md), desktop mode makes the bounded undocumented `0x052C` requests once per Progman HWND. The mod does not hide, destroy or restyle any shell window.

Missing/unsupported host: a separate `HWND_BOTTOM` window, logged explicitly. A hidden top-level helper receives shell/display/theme broadcasts, and a two-second probe repairs host identity/order. A destroyed parent invalidates the tracked HWND and causes recreation. DPI awareness is **thread-local**; embedded windows match their parent's context. Explorer's process DPI policy is untouched.

The original small JSON parser checks syntax, UTF-8/surrogates, exact signed int64 sequences, duplicate keys, nesting/count/size limits, major versions, tile references, frame bounds and finite geometry. Unknown additive fields are accepted. Exact bytes of previously validated `assets`/`office` objects are reused. PNGs are bounded, decoded using GDI+, SHA256-checked, and committed only after the new pack is complete. Paths must stay within a local export root; URLs, UNC paths, traversal and alternate streams are rejected.

State reads share read/write/delete access and check length/mtime across the read. Failures retry after 40ms, 100ms, then 200ms and retain the last valid snapshot and decoded images. State older than ten seconds, missing initial state, or a timestamp more than ten seconds in the future displays **tracker offline**. After disconnection, the last positions and layout remain; characters use their manifest's idle clip plus a one-pixel breathing motion at 1Hz. Live simulation is never extrapolated. Old work bubbles are hidden until a fresh snapshot arrives.

The UI thread owns windows, tooltip, renderer, buffers and GDI+ lifetime. Unload signals its event, joins the thread, destroys the windows/classes and releases graphics before `GdiplusShutdown`. No graphics objects have global destructors. Painting is driven by visual changes, resize and expose events; unchanged semantic/timestamp updates do not repaint.

A normal user-mode mod cannot render on the secure Winlogon lock screen. Desktop embedding is an undocumented shell feature; the fallback remains available through settings.

## Verification without Explorer injection

Build only:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File windhawk/compile.ps1 -Tests
```

`windhawk/compile.ps1` reuses `poc/compile.ps1`, its **installed Windhawk clang**, C++23, `-O2`, `x86_64-w64-mingw32`, Windows macros, forced `windhawk_api.h`, the 1.7.3 x64 import library and exported callbacks. Added libraries are bcrypt and ole32. Output stays in ignored `windhawk/artifacts/`. Running the PoC compiler without arguments retains its original build target.

The standalone harness `tests/windhawk/renderer_tests.cpp` **includes the actual mod source**. `VIBEDOTCOM_TEST` excludes all Windhawk callbacks, settings and HWND hosting; there is no DLL loading or UI. Delimited JSON/contract/geometry and GDI+ renderer sections are shared verbatim.

Run compilation, existing WPF **offscreen demo**, unit/render checks, script syntax checks and CPU sampling:

```powershell
# Requires an already-built Release WPF DLL; this script never builds or edits src/.
powershell -NoProfile -ExecutionPolicy Bypass -File tools/windhawk_verify.ps1 -Profile
```

`-AppDll` selects another existing WPF build. The script copies only the installed `libc++.whl` / `libunwind.whl` runtime files into the test directory, because the bundled import libraries use these filenames. Nothing is copied into Windhawk or loaded into Explorer. The installation script is syntax-parsed only, never executed.

Artifacts:

- `artifacts/vibedotcom.dll`, `vibedotcom.log`, `renderer_tests.exe`, `tests.log`.
- `artifacts/demo/state.json`, matching hashed PNGs, and WPF `office-2x.png`, `office.png`, `transparent.png`.
- `artifacts/render/windhawk-2x.png`, `windhawk-auto.png`, `windhawk-transparent.png`, `windhawk-offline.png`.

The verified **existing v3 WPF build** exports a fabricated demo with nine agents and an expanded 37×24 map. The WPF and mod renders use **the same final snapshot and exported PNGs**. The 1216×800 2× comparison was opened and visually inspected: tiles, furniture, agent placement, identity accents, bubbles and depth order match. 99.71% of RGBA pixels match exactly (auto-fit 99.76%); text/relationship-line rasterization differs, so this is not a pixel-identical claim. The v4 fields/renderer branches are additionally tested with fabricated snapshots (`windhawk-v4-additions.png`). The concurrently edited v4 WPF sources were not rebuilt here; a v4 WPF comparison is still pending its author's build.

The standalone CPU profile measures process CPU seconds / elapsed seconds, as a percentage of **one logical core**. It includes 5Hz local JSON reads/validation and cached pack handling with an unchanged live visual scene, then separately measures 1Hz offscreen disconnected idle drawing. This is a bounded harness measurement, not measured Explorer CPU or a guarantee for arbitrary map/agent counts. Native paints, tooltip/host polling and busy animation still require the manual checks below.

Recorded verification (2026-10-04): both x64 targets compiled without warnings; **55 checks passed**, including v4 parsing, authoritative frames, queue/laptop drawing and unassigned desk sentinel tests. The final v3 harness profile measured **0.19% of one core** for unchanged live state (zero repeated paints), and **1.89%** for disconnected idle rendering. The task scripts passed PowerShell syntax parsing. Explorer injection, scheduled-task installation and full v4 WPF comparison remain unverified.

## Manual test checklist — user performs later

- [ ] Enable only in shell Explorer; opening a separate folder process produces no second office.
- [ ] Normal window appears without changing the foreground. Move/resize it; verify auto and fixed zooms 1–6, wheel and `+`/`-`, opacity and transparency.
- [ ] Topmost stays above ordinary windows. Switching back to normal removes topmost behavior.
- [ ] Click-through reaches applications/desktop icons beneath the office. Recover by turning it off in Windhawk settings.
- [ ] Hover each visible agent in normal/topmost/desktop/through modes. Verify all metadata, elapsed updates, popup delay, no focus/input capture, no tooltip over an occluding window or desktop icon.
- [ ] Desktop level is below icons and normal application windows. On 24H2+ check Progman/DefView/WorkerW sibling order and visibility. On a classic topology check the selected wallpaper WorkerW.
- [ ] Unsupported topology logs fallback and displays a bottom-level window. It must not alter unrelated shell windows.
- [ ] Restart Explorer / change monitor layout or DPI. Confirm recreation and correct scale/order, no duplicate or abandoned windows. Explorer restart behavior is not simulated by the test harness.
- [ ] Observe work, waits, reports, paper transfer, fades, parent links and leisure. Compare with the WPF app using the same pack/layout; do not expect frame synchronization between their independent running simulations.
- [ ] With the concurrently updated v4 tracker/WPF build, verify display names/provider icons, queued bubbles, overflow laptops and monitor frames. The mod must use supplied poses/frames rather than assigning seats or simulating the queue.
- [ ] Stop the tracker for over ten seconds: offline toast, retained map/agents, idle animation, no stale working bubble. Restart: fresh state and Core-selected frames return, including sequence reset.
- [ ] Point state path at a missing/test file, a truncated JSON file, or a snapshot with a missing PNG: last valid scene survives; a restored complete snapshot recovers. Use disposable copies for these checks.
- [ ] Measure Explorer CPU for at least a minute while idle, hovering, minimized/occluded and actively animated; target under 2% idle. Check memory/GDI objects while toggling settings and disabling/re-enabling the mod. Confirm clean unload removes every office/tooltip and the worker thread.
- [ ] Register the task yourself, log out/in, confirm hidden tracker and fresh state; stop/restart it and verify its child exits. Remove the task if unwanted.

**No Explorer injection, live window/input manipulation, task registration, installer execution or git commit was performed during development.**
