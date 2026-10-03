# Vibedotcom — Windhawk Variant B

`vibedotcom.wh.cpp` hosts a passive pixel office in the **shell instance of explorer.exe**. It consumes the [v4 state contract](../docs/state-schema.md), PNG [asset contract](../docs/asset-format.md), and the resolved fixed office. Core supplies seats, stable desk IDs, overflow/queue destinations, monitor frames, animation and collision geometry. The renderer never creates desks or grows the office as membership changes.

Version 3 remains readable. Additive v4+ snapshots use the v4 rendering rules and unknown fields are ignored; existing required field types, asset/layout format 1 and fallback atlas format 2 are still validated. Visible characters always have a `[provider icon] displayName` tag above their actual alpha silhouette. A reserved desk tag is hidden when its owner is within 1.5 Manhattan tiles of the work point and Working/Thinking/Waiting. Supplied frames, authored rotation/state variants, anchors and offsets remain authoritative; seated poses and future furniture need no renderer-specific roster rules.

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
| Transparent background | Skip floor/walls; keep furniture and characters. Uses premultiplied per-pixel alpha plus global opacity; see the native presentation policy below. |

Wheel/keyboard zoom and native geometry changes are session-local and reset to configured values after a settings reload or host recreation. There are no global input hooks, task controls or tray UI.

## Desktop hosting and failure handling

Classic desktop: a child of the wallpaper WorkerW **following the icon host**, never an arbitrary first WorkerW. Windows 11 24H2+ raised/raw desktop: a layered child of Progman, a sibling of its WorkerW, inserted below `SHELLDLL_DefView`. As in the [PoC notes](../docs/windhawk-notes.md), desktop mode makes the bounded undocumented `0x052C` requests when the selected host is missing, with bounded retry backoff. The mod does not hide, destroy or restyle any shell window.

Missing/unsupported host: a separate `HWND_BOTTOM` window. Failed layer requests and lost WorkerW hosts retry with bounded backoff, even if Progman is unchanged. Layer creation requests run on the independent scene worker, with scalar parameters and checked `SendMessageTimeout` results. The embedded UI thread sends no pointer-bearing shell/control messages. Hover is deliberately suppressed over the desktop list-view area because a safe icon hit-test cannot be inferred without contacting that control.

The scene worker has no HWND or parent/input attachment. It owns state reads, JSON parsing, SHA256, PNG decoding, all GDI+ objects, rendering and cached alpha hit rectangles. The UI thread receives immutable premultiplied pixel packets and cached tooltip metadata, then presents them with GDI. Shared locks cover pointer swaps only. Overlapped reads observe the stop event, cancel on stop/timeout, and drain completion before releasing their buffer/OVERLAPPED; shutdown repeats `CancelSynchronousIo` while joining the worker, including calls that began after the initial cancellation. Native move/size/menu loops have a stop timer and shutdown messages cancel their mode/capture. Both threads are joined before unload, after child windows are destroyed.

Cached PNG dimensions are checked against each candidate manifest, including cache hits. New packs commit only after every referenced sheet validates; failed frame/hit/tooltip work preserves the last complete packet and retries. GDI+ wrappers use throwing global allocation with its matching global deleter, avoiding null-returning `GdiplusBase::operator new` and unsafe wrapper `Clone`/generic-string-format factories. Constructor/API statuses and pixel pointers/strides are checked. Each native graphics call has a destructor-free SEH leaf; a trapped fault becomes an ordinary C++ exception outside that leaf so temporary owners unwind. Guarded placement construction also avoids unsafe wrapper allocators; guarded cleanup never throws. A fault within a partially completed native constructor/API can still leave library-owned internals unrecoverable. WndProcs and thread entries have C++ `catch(...)` boundaries and local clang x64 SEH envelopes; error paths log fixed strings without further allocation. These measures contain the tested failures; they do not establish an absolute guarantee against arbitrary native-library, driver, heap-corruption or process failures.

State reads share read/write/delete access and preserve the last valid scene after malformed/missing files. Snapshots older than ten seconds or more than ten seconds in the future show **tracker offline**. Last positions and layout remain while manifest idle frames and one-pixel breathing continue; work bubbles are hidden. Exact signed int64 sequences, Unicode, JSON limits, finite geometry and safe local relative sheet paths are validated without assuming a `C:` drive exists.

A hidden helper handles broadcasts. Display changes remap child placement and recover only wholly offscreen geometry, preserving valid negative monitor positions. Actual HWND client dimensions, pixels and hit extents stay within the same limits, including maximize and DPI changes. The thread uses target-HWND DPI contexts for coordinate conversion; tooltips rebuild font/padding/measurement at cursor-monitor DPI. Hidden, cloaked, or noncurrent virtual-desktop offices suppress hover and new presentation; cloaked occluders are skipped.

Failed cloak/current-desktop queries suppress activity. If the virtual-desktop service is unavailable, top-level hover is disabled. These decisions avoid treating an unknown visibility result as permission to show a tooltip.

### Native opacity and embedding

Desktop children and frameless transparent windows use a top-down premultiplied BGRA DIB and `UpdateLayeredWindow(ULW_ALPHA)`, with `AC_SRC_ALPHA` and `SourceConstantAlpha` for the 10-100% setting. Windows supports `WS_EX_LAYERED` children from Windows 8 ([Microsoft API contract](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-updatelayeredwindow)); compatibility policy of the host executable and undocumented shell parenting can still reject a child. The mod does not change Explorer's process DPI policy or compatibility manifest. If child creation/presentation fails, it destroys only its own child and creates a bottom-level top-level per-pixel window. Its Show Desktop/virtual-desktop behavior is not identical to an embedded child.

Framed normal/topmost windows use an opaque worker-composited buffer, `WM_PAINT`/`BitBlt` and `SetLayeredWindowAttributes(LWA_ALPHA)` for uniform opacity. Transparent normal/topmost windows use a frameless per-pixel HWND. A per-pixel HWND never receives `SetLayeredWindowAttributes`; switching policies requires a newly created HWND. No magenta color key is used. See [Microsoft's layered-window contract](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-updatelayeredwindow) and [LWA/ULW interaction](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setlayeredwindowattributes).

A normal user-mode mod cannot render on the secure Winlogon lock screen. Desktop embedding is an undocumented shell feature; the fallback remains available through settings.

## Verification without Explorer injection

Build only:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File windhawk/compile.ps1 -Tests
```

`windhawk/compile.ps1` reuses `poc/compile.ps1`, its **installed Windhawk clang**, C++23, `-O2`, `x86_64-w64-mingw32`, Windows macros, forced `windhawk_api.h`, the 1.7.3 x64 import library and exported callbacks. Added libraries are bcrypt, ole32, dwmapi and dwrite; `-fms-extensions` enables the local SEH envelopes. Output stays in ignored `windhawk/artifacts/`. Running the PoC compiler without arguments retains its original build target.

The standalone harness `tests/windhawk/renderer_tests.cpp` **includes the actual mod source**. `VIBEDOTCOM_TEST` excludes all Windhawk callbacks, settings and HWND hosting; there is no DLL loading or UI. Delimited JSON/contract/geometry and GDI+ renderer sections are shared verbatim.

`tests/windhawk/host_tests.cpp` includes the actual host under a separate test define. It creates only its own hidden windows, with an embedded test-only Windows 8+ compatibility manifest. It exercises cached worker packets, modal-stop notifications, client bounds, tooltip font scaling, list-view suppression, layered-child API admission, and the shutdown cancellation race using an owned local pipe. It does not load the mod DLL or interact with Explorer or existing application windows. Hidden-child API success does not verify visible DWM composition in Explorer.

Run compilation, existing WPF **offscreen demo**, unit/render checks, script syntax checks and CPU sampling:

```powershell
# Requires an already-built Release WPF DLL; this script never builds or edits src/.
powershell -NoProfile -ExecutionPolicy Bypass -File tools/windhawk_verify.ps1 -Profile
```

`-AppDll` selects another existing WPF build. The script copies only the installed `libc++.whl` / `libunwind.whl` runtime files into the test directory, because the bundled import libraries use these filenames. Nothing is copied into Windhawk or loaded into Explorer. The installation script is syntax-parsed only, never executed.

Artifacts:

- `artifacts/vibedotcom.dll`, `vibedotcom.log`, `renderer_tests.exe`, `tests.log`, `host_tests.exe`, `host-tests.log`.
- `artifacts/demo/state.json`, matching hashed PNGs, and WPF `office-2x.png`, `office.png`, `transparent.png`.
- `artifacts/render/windhawk-2x.png`, `windhawk-auto.png`, `windhawk-transparent.png`, `windhawk-offline.png`.

The parity harness [tools/windhawk_parity.py](../tools/windhawk_parity.py) uses the existing app's **offscreen** `--demo --smoke-test` output. It verifies exported PNG hashes, renders the same state/poses/frames with the actual C++ renderer, and compares all four decoded straight RGBA channels exactly (including RGB under zero alpha), without resizing or tolerance. Auxiliary WPF snapshots refer to original source filenames; only their sheet paths are relocated to the identical SHA256-verified exported PNG bytes. `--render-only` avoids repeating the entire regression suite for each comparison scenario.

```powershell
# Python 3.9+ standard library only; use your installed Python executable.
python tools/windhawk_parity.py --run-wpf --run-native --diff
```

`--wpf-dir` accepts an existing smoke output; `--app` selects an audited existing build. Comparison images and a JSON report are written below `windhawk/artifacts/parity`. Opening images is optional. The 2x room draws source tiles at the final camera scale so authored 32px tile details are preserved instead of first disappearing into a 16px intermediate raster. Provider tag widths use DirectWrite ideal advances, matching WPF measurement and collision placement. Differences in GDI+/WPF glyph and line rasterization are reported rather than presented as pixel identity.

Current verification results and F1-F11 evidence are recorded in [windhawk-notes.md](../docs/windhawk-notes.md). Offscreen checks do not verify native Explorer parenting, Win+D, actual virtual-desktop switching, mixed-DPI monitor movement, device-removal geometry, or long-term Explorer CPU/GDI behavior. No DLL injection, input/foreground changes, installer execution or git commit is part of this verification.

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

**This review-fix verification performs no Explorer injection, existing-window/input manipulation, task registration, installer execution or git commit. Native host tests use only owned hidden windows.**
