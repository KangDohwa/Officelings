# Vibedotcom — Windhawk Variant B

`vibedotcom.wh.cpp` hosts a passive pixel office in the **shell instance of explorer.exe**. It consumes the [v5 state contract](../docs/state-schema.md), PNG [asset contract](../docs/asset-format.md), and the resolved fixed office. Core supplies seats, stable desk IDs, overflow/queue destinations, monitor frames, animation and collision geometry. The renderer never creates desks or grows the office as membership changes.

Versions **3, 4 and 5** are readable. Unknown fields within those versions are ignored; unknown versions, invalid required types, asset/layout formats other than 1 and fallback atlas formats other than 2 are rejected while the last valid scene remains available. Supplied frames, authored rotation/state variants, anchors and offsets remain authoritative. V5 seat phases use the published contact and linked chair depth, clip below the seat, and do not apply alignment twice.

Name tags default to **Hover**; **Icon** and **Always** match the app's modes. Plates use a 6px font, a 14px body, 10px provider icons below 2× and 12px icons from 2×, with crown/badge role clips. Names show at most 14 Unicode text elements plus an ellipsis; tooltips retain the full name. Hover fades in over 120ms, has 60ms leave grace, then fades out over 200ms. Bodies, plates and their connecting gap keep the same hover. Away owners' desk footprints can also reveal their reservation. Reservations are hidden for invisible owners, during seat phases, and while a Working/Thinking/Waiting owner is within 1.5 Manhattan tiles of the work point.

Departure timing belongs to Core: the defaults are 120 seconds before Working/Thinking becomes Idle, five minutes since real activity for finished/inactive agents, and 30 minutes for explicit input/approval waits. Active tracked descendants keep their parent present; closed-session signals can cause earlier departure. The mod follows supplied break, raising, walking, door-fade and re-entry poses even while semantic state is Gone. It does not start its own leave timer or teleport a departing character.

V5 break rendering includes reclining beanbags, reading props, waiting/leisure bubbles, dispensing flaps and dart/ping-pong overlays. Supplied `ambient` fields control window views, clock hands, string lights and winter decoration. Without them, the mod uses its local clock and the weather/season settings below, including while offline. Supplied `pets` frames render without agent labels, hit targets or desk ownership. The ordinary tracker omits pets, so this mod does not create a second pet simulation.

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
| Name tags (`tagMode`) | `hover` (default), `icon`, `always`; unknown values use `hover`. This is a renderer setting, outside `state.json`. |
| Show tooltip | Read-only cursor polling, 420ms hover delay. Provider, full name/role, model, project, task, tool, state, elapsed and visual action. Popup avoids visible plates and cannot take focus or input. Turning it off still allows name-tag hover. |
| Ambient weather (`ambientWeather`) | `off` (default), `rain`, `snow`, `auto`. Auto selects snow in December and clear views in other months. No network requests. Supplied snapshot ambient takes precedence. |
| Seasonal decoration (`seasonDecor`) | `off` (default), `auto` for December window decoration. Supplied snapshot ambient takes precedence. |
| Transparent background | V5 clears margins/letterbox and void tiles; nonvoid floor/wall tiles retain an opaque base. Versions 3/4 retain their legacy floor-hiding behavior. Per-pixel alpha and global opacity follow the native presentation policy below. |

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

The standalone harness [renderer_tests.cpp](renderer_tests.cpp) **includes the actual mod source** and retains the original regression checks. `VIBEDOTCOM_TEST` excludes all Windhawk callbacks, settings and HWND hosting; there is no DLL loading or UI. Delimited JSON/contract/geometry and GDI+ renderer sections are shared verbatim. V5 checks cover cold/warm parsing, unsupported versions, seat phases, name modes/fades/Unicode, optional ambient/pets, break props, Gone visibility and transparent tiles.

[host_tests.cpp](host_tests.cpp) runs the original `tests/windhawk/host_tests.cpp` checks under a separate test define and adds shared plate/desk metadata checks. It creates only its own hidden windows, with an embedded test-only Windows 8+ compatibility manifest. It exercises cached worker packets, modal-stop notifications, client bounds, tooltip font scaling, list-view suppression, layered-child API admission, and the shutdown cancellation race using an owned local pipe. It does not load the mod DLL or interact with Explorer or existing application windows. Hidden-child API success does not verify visible DWM composition in Explorer. Files under `tests/` are unchanged.

Run compilation, existing WPF **offscreen demo**, unit/render checks, script syntax checks and CPU sampling:

```powershell
# Requires an already-built Release WPF DLL; this script never builds or edits src/.
powershell -NoProfile -ExecutionPolicy Bypass -File tools/windhawk_verify.ps1 -Ambient -Profile
```

`-AppDll` selects another existing WPF build. `-Ambient` also exports the existing synthetic day/night/rain/snow smoke scenes. `-SkipCompile` reuses built binaries; `-SkipWpf` reuses an existing synthetic demo. CPU sampling separates fixed supplied ambient from the local-clock refresh cadence and offline breathing. The script copies only the installed `libc++.whl` / `libunwind.whl` runtime files into the test directory, because the bundled import libraries use these filenames. Nothing is copied into Windhawk or loaded into Explorer. The installation script is syntax-parsed only, never executed.

Artifacts:

- `artifacts/vibedotcom.dll`, `vibedotcom.log`, `renderer_tests.exe`, `tests.log`, `host_tests.exe`, `host-tests.log`.
- `artifacts/demo/state.json`, matching hashed PNGs, and WPF `office-2x.png`, `office.png`, `transparent.png`.
- `artifacts/render/windhawk-2x.png`, `windhawk-auto.png`, `windhawk-transparent.png`, `windhawk-offline.png`.
- `artifacts/ambient/*-state.json` and day/night/rain/snow PNGs when `-Ambient` is selected.

The parity harness [tools/windhawk_parity.py](../tools/windhawk_parity.py) uses the existing app's **offscreen** `--demo --smoke-test` output. It verifies exported PNG hashes, renders the same state/poses/frames with the actual C++ renderer, and compares all four decoded straight RGBA channels exactly (including RGB under zero alpha), without resizing or tolerance. Auxiliary WPF snapshots refer to original source filenames; only their sheet paths are relocated to the identical SHA256-verified exported PNG bytes. `--render-only` avoids repeating the entire regression suite for each comparison scenario.

```powershell
# Python 3.9+ standard library only; use your installed Python executable.
python tools/windhawk_parity_tests.py
python tools/windhawk_parity.py --wpf-dir windhawk/artifacts/demo `
    --ambient-dir windhawk/artifacts/ambient --run-native --diff --min-match 90

# Use a WPF smoke export made with the same name-tag mode.
python tools/windhawk_parity.py --wpf-dir windhawk/artifacts/parity/icon/wpf `
    --tag-mode icon --out windhawk/artifacts/parity/icon --run-native --diff --min-match 90
```

`--wpf-dir` accepts an existing smoke output. `--tag-mode hover|icon|always` passes the setting to both renderers without changing state JSON. Ambient smoke exports use Hover independently of that setting. For fresh WPF exports, `--run-wpf --app PATH_TO_APP_EXE` accepts a recorded audited App/Core pair; after reviewing a new build's offscreen smoke path, supply both lowercase `--reviewed-app-sha256` and `--reviewed-core-sha256` pins. An unreviewed pair is rejected. The smoke path must return before constructing the app window and use synthetic sessions/disposable storage.

Comparison images and a JSON report are written below `windhawk/artifacts/parity`. Opening images is optional. The 2x room draws source tiles at the final camera scale so authored 32px tile details are preserved. Provider tag widths use DirectWrite ideal advances. Exact RGBA comparisons report GDI+/WPF font, thin-line and fractional-scale sampling differences without tolerance or image resizing. The small 400×300 auto-fit scene has a larger raster difference; see the recorded results before judging visual equivalence.

Current verification results and historical F1-F11 evidence are recorded in [windhawk-notes.md](../docs/windhawk-notes.md). Offscreen checks do not verify native Explorer parenting, Win+D, actual virtual-desktop switching, mixed-DPI monitor movement, device-removal geometry, or long-term Explorer CPU/GDI behavior. Verification does not inject the DLL, change input/foreground, execute installers or register tasks.

## Manual test checklist — user performs later

- [ ] Enable only in shell Explorer; opening a separate folder process produces no second office.
- [ ] Normal window appears without changing the foreground. Move/resize it; verify auto and fixed zooms 1–6, wheel and `+`/`-`, including a small fractional auto-fit viewport. At v5, transparency clears margins/void while floor/walls remain visible; opacity fades the complete office.
- [ ] Topmost stays above ordinary windows. Switching back to normal removes topmost behavior.
- [ ] Click-through reaches applications/desktop icons beneath the office. Recover by turning it off in Windhawk settings.
- [ ] Check Hover, Icon and Always at 1×/2×: provider art, supervisor crown/sub-agent badge, 14-element Unicode truncation and collision stacking. Move body → gap → plate and away owner's desk → plate; verify 120ms/60ms/200ms fades. Unassigned desks, invisible owners and seated/raising owners receive no reservation.
- [ ] Hover each visible agent in normal/topmost/desktop/through modes. Verify full name and all metadata after 420ms, elapsed updates, no plate overlap, no focus/input capture, and no tooltip over an occluding window or desktop icon. Turn tooltips off and confirm name-tag hover still works.
- [ ] Desktop level is below icons and normal application windows. On 24H2+ check Progman/DefView/WorkerW sibling order and visibility. On a classic topology check the selected wallpaper WorkerW.
- [ ] Unsupported topology logs fallback and displays a bottom-level window. It must not alter unrelated shell windows.
- [ ] Restart Explorer / change monitor layout or DPI. Confirm recreation and correct scale/order, no duplicate or abandoned windows. Explorer restart behavior is not simulated by the test harness.
- [ ] Observe work, waits, reports, paper transfer, fades, parent links and leisure. Compare with the WPF app using the same pack/layout; do not expect frame synchronization between their independent running simulations.
- [ ] With a v5 tracker, check turn/lower/seated/raise at back-facing desks, the profile pod and overflow furniture: chair back/body/desk/front order, clipped calves, no doubled seat offset, `?` waits, queued bubbles, laptops and assigned monitor frames. Compare the same snapshot with WPF; the mod must not assign seats or simulate the queue.
- [ ] Using disposable session sources, verify inactive/finished departure at five minutes and explicit waits at 30 minutes, with Working/Thinking becoming Idle after 120s. An active descendant keeps its parent present. Leave begins at the current break/chair position, finishes raising, walks to the door, fades and hides; resumed work re-enters from the door. Core controls these timings.
- [ ] Check beanbag rotation/contact, reading book, leisure bubbles, vending flap and dart/ping-pong overlays. A pair stops when either agent resumes work or leaves. Supplied poses remain authoritative during breaks and Gone walks/fades.
- [ ] Check local day 07:00–17:00, sunset 17:00–19:00, night and 17:00–07:00 lights; clock hands, rain/snow/auto weather, December decoration and offline updates. Use synthetic timestamped `ambient` snapshots for fixed cases and confirm they override settings. Supplied test pets render without labels, tooltips or desk claims; ordinary tracker output has no pets.
- [ ] Stop the tracker for over ten seconds: offline toast, retained map/agents, idle animation, no stale working bubble. Restart: fresh state and Core-selected frames return, including sequence reset.
- [ ] Point state path at disposable missing/truncated JSON, schema 6, invalid seat/ambient/pet fields, or missing/damaged PNGs: last valid scene survives; a restored complete snapshot recovers. Do not use real profile files for fault tests.
- [ ] Measure Explorer CPU for at least a minute while idle, hovering, minimized/occluded and actively animated; target under 2% idle. Check memory/GDI objects while toggling settings and disabling/re-enabling the mod. Confirm clean unload removes every office/tooltip and the worker thread.
- [ ] Register the task yourself, log out/in, confirm hidden tracker and fresh state; stop/restart it and verify its child exits. Remove the task if unwanted.

**Automated verification uses offscreen rendering, synthetic state and owned hidden windows. Enabling the mod in Explorer and performing the checklist remain manual steps.**
