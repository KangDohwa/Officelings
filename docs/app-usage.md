# Standalone app

The WPF app reads session sources itself. It does not need the headless tracker or Windhawk. Build and run commands are in the [README](../README.md); [tracker usage](tracker.md) covers the separate console program.

Run the commands below from the repository root. Normal runs save settings changes and startup overrides when you close the app; demo and QA runs use disposable settings.

## Window and controls

The default window is 720x480 and fits the compact office at an integer zoom. Wheel, with or without Ctrl, and `+` / `-` select 1x through 6x. Drag to pan. Hover shows provider, role, model, project, last prompt, tool, state and elapsed time. The tooltip is passive.

Right-click the office or tray to fit the office, switch between normal, always-on-top and desktop-level modes, or change click-through, borders, transparent background, opacity and **Name tags**. Double-click the tray icon or choose **Show normal, clickable office** to recover. Closing exits and removes the icon.

Name tags default to **Hover**: only the character under the pointer shows its `[provider icon] name` plate. A reserved desk shows its owner's plate only while that desk is hovered and its owner is away. **Icon** keeps the original provider icon above each head and on away owners' desks; hover reveals the full plate. Native 10px icons are used at 1× and 12px icons from 2×, with integer pixel scaling. A crown marks a supervisor; a badge marks a sub-agent. Local brand overrides retain their existing 12px size and precedence. **Always** keeps full plates visible. Both menus apply the selection immediately and save it for the next launch.

Hover plates fade in over 120ms. Leaving has a 60ms boundary grace, then a 200ms fade out. Moving between the character and its plate, including the gap, keeps the same hover. Detail tooltips appear after 420ms on the same target, remain passive and avoid all visible plates; a tooltip is suppressed if the window has no room. Click-through uses the existing passive cursor polling. Only the tag layer redraws during fades.

Desktop mode embeds beneath Explorer's icon layer using WorkerW or the Windows 11 24H2 raw-desktop Progman layout. If embedding fails, it falls back to `HWND_BOTTOM`. Click-through hover polls the cursor without activating the window or intercepting clicks. Shell integration has [limits](limits.md#desktop-and-lock-screen).

## Settings

Normal use writes `%LOCALAPPDATA%\Vibedotcom\settings.json` atomically. Close the app before editing timeouts.

| Key | Default and accepted values |
| --- | --- |
| `mode` | `normal`; also `topmost`, `desktop`. |
| `tagMode` | `hover`; also `icon`, `always`. Missing, null or unknown values fall back to `hover`. Renderer setting; never stored in tracker `state.json`. |
| `ambient.weather` | `off`; also `rain`, `snow`, `auto`. No network requests. `auto` shows snow in December and a clear view in other months. |
| `seasonDecor` | `off`; `auto` adds a small winter window decoration in December. |
| `pets` | `on`; `off` hides the roaming pets. Right-click **Pets** changes and saves this preference. Missing pet sheets simply leave the room without pets. |
| `sharing` | `off`; also `on`. Saved intent only: a fresh JSON preview and confirmation are required before transmission starts. |
| `sharingServer` | Empty; server origin such as `https://presence.example`. HTTPS required outside loopback; credentials, paths, queries and fragments are rejected. |
| `sharingOfficeId` | Empty; nonzero UUID belonging to the ingest token. |
| `sharingAliases` | Empty object; map of `platform:slot` to chosen aliases, for example `{"codex:1":"Nova","claude:1":"Moon"}`. Platforms are `codex`, `claude`, `antigravity`, `other`; slots are 1–64. |
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

## Presence sharing

Sharing starts **off**. Open **Sharing** from the office or tray menu. Enter the server origin, office UUID and ingest bearer token obtained from the server's account page. Creating a token, transmitting presence and making an office public are separate actions. This client does not change the office's public/private setting or implement a web viewer.

Choose aliases using one `platform:slot=alias` per line, such as `codex:1=Nova`. Slots are numbered within each platform in the current snapshot, with supervisors before sub-agents; they can shift when membership changes. They are not persistent session identities. Unlisted slots use `Agent N`. Aliases are normalized to NFC, rejected above 24 Unicode scalar values, then stripped to letters, digits, ASCII spaces, `_`, `-` and `.`. Spaces collapse. Pick aliases without private details; sanitization does not detect personal information.

Click **Preview exact JSON**. The complete JSON is an invented-agent example using the chosen aliases, current office ID and current UTC timestamp. Check **I reviewed this JSON and agree to share presence**, then **Enable sharing**. The first request sends exactly the displayed UTF-8 body. Later requests contain complete live presence, even when unchanged. Editing the server, office or aliases clears the preview and consent; a preview older than 90 seconds must be refreshed. The bearer token is masked in setup and never included in the JSON.

The only body fields are `v`, `officeId`, `agents` and `ts`; agents contain `platform`, `alias`, `state`, `role` and optional `parentIndex`. Working/thinking maps to `working`, explicit waits to `waiting`, and reporting/idle to `idle`; gone agents are omitted. Parents refer only to earlier supervisors in the current array. Local names, prompts, task/tool text, paths, project/model names and session/agent IDs never enter this payload. More than 64 agents or 8,192 UTF-8 bytes stops sharing rather than uploading a partial office.

Successful heartbeats use a random 10–30 second interval. Every attempted request consumes at least 10 seconds; 429 honors both forms of `Retry-After`, and temporary failures use backoff. App and Tracker share a timing gate across restarts. HTTPS is required except for loopback development; proxies and redirects are disabled. Server rejection stops transmission. The visible **Sharing** line shows off, sending, live, retry waiting or the reason sharing stopped.

By default the token is stored with Windows DPAPI **CurrentUser** in `%LOCALAPPDATA%\Vibedotcom.Credentials\presence.dpapi`, bound to the chosen server and office. Uncheck token storage to keep the supplied token in memory for this run. The token is never written to settings, layouts, exported state, URLs or diagnostics. The credential folder also holds token-hash-named `.cooldown` files containing only timing data. See [privacy](privacy.md#optional-presence-sharing).

Open **Sharing** and click **Stop sharing** to cancel transmission. **Also delete the stored token** removes the DPAPI file. Stopping does not revoke a server token or unpublish an office. Presence expires within 90 seconds; revocation or unpublishing clears it immediately in the server process. Downloaded data cannot be recalled. Anyone who learns a public office URL can save or redistribute aliases, platforms, states, hierarchy and approximate activity times.

Demo and QA disable sharing. A new app process or a replacement window (for example, changing transparency) starts with transmission off and requires a fresh preview to resume. If a `privacy:"full"` setting is supplied by the privacy feature, previews and live payloads force generic `Agent N` aliases. Changing privacy mode during sharing stops transmission and requires new consent. This checkout has no separate privacy-mode selector.

For the headless Tracker, first enable sharing and store the token in the app, then close the app to stop its sender. The saved `sharing:"on"` must be in the Tracker output directory's `settings.json`. Run `Vibedotcom.Tracker --sharing` in an interactive terminal, review its full invented JSON, and type `ENABLE`. Without this flag, or with redirected standard input, it sends nothing. Ctrl+C cancels sharing. The app's **Stop sharing** sets `sharing:"off"`, which the Tracker observes; changing privacy mode also stops it. Token deletion remains available in the app.

The dependency-free checks use fake HTTP, synthetic tokens and temporary settings only:

```powershell
dotnet build tests/Vibedotcom.SharingChecks -m:1 -p:UseSharedCompilation=false
dotnet tests/Vibedotcom.SharingChecks/bin/Debug/net10.0-windows/Vibedotcom.SharingChecks.dll
```

## Art packs and office editing

Windows follow your computer's local time: day from 07:00 to 17:00, sunset from 17:00 to 19:00, and night until 07:00. Rain or snow replaces the clear view. The wall clock uses the same local time; string lights come on from 17:00 until 07:00. These effects keep working offline. For example:

```json
{ "ambient": { "weather": "rain" }, "seasonDecor": "auto", "pets": "on" }
```

Resting agents can play darts, pair up at the ping-pong table, use the arcade, read, lie on a bean bag, watch the fish, get a drink or look outside. Each interaction point admits one agent. Ping-pong waits for both players to arrive and stops if either returns to work or leaves. The room stays 35 by 20 tiles, with twelve desks and the existing overflow seats.

To adjust the frequency or length of breaks, place `activities.json` next to `settings.json`, then restart. Unlisted activities keep the [bundled defaults](../src/Vibedotcom.Core/Data/activities.json). A zero weight disables an activity; durations are seconds counted after arrival. The tracker reads the same file from its output directory.

```json
{
  "version": 1,
  "activities": [
    { "id": "darts", "weight": 3, "minSeconds": 14, "maxSeconds": 24 },
    { "id": "pingpong", "weight": 4, "minSeconds": 20, "maxSeconds": 33 }
  ]
}
```

Available IDs are `coffee`, `water`, `tv`, `phone`, `napping`, `chatting`, `darts`, `pingpong`, `arcade`, `reading`, `beanbag`, `aquarium`, `vending`, `fridge` and `window`. Invalid files use the defaults and show a warning. Pets are part of the viewer's animation, separate from tracked agents: one or two available designs roam, sometimes follow a supervisor, and nap on an available cushion. They avoid work seats and never reserve a desk.

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
| `--ambient-weather off\|rain\|snow\|auto`, `--season-decor off\|auto`, `--pets on\|off` | Ambient startup overrides, including offscreen smoke and idle profiling. |
| `--asset-dir DIR`, `--layout FILE` | Explicit runtime art/layout inputs. |
| `--working-seconds N`, `--leave-after-idle-minutes N`, `--leave-after-waiting-minutes N` | Startup activity/leave windows; defaults 120 seconds, 5 minutes and 30 minutes. Positive finite values; applied limits and ordering match settings normalization. |
| `--gone-minutes N` | Deprecated waiting-window alias. An explicit new waiting option wins, in either argument order. |
| `--zoom 2`, `--mode desktop`, `--click-through`, `--borderless`, `--transparent`, `--opacity .85` | Startup overrides. Supplying a settings override starts from default settings rather than merging saved settings. |
| `--quit-after 30` | Bounded exit after the given seconds. |
| `--smoke-test OUTPUT_DIR` | Offscreen render and interchange exports. Add `--demo` to avoid real sources. Demo also writes `leave-1-break.png` through `leave-4-return.png` and `leave-walk.json`, using an isolated synthetic tracker and its real five-minute threshold. |
| `--smoke-tags` | With `--smoke-test`, render all three tag modes with 12 fabricated agents and programmatic hover, without reading logs or opening visible windows. Writes six PNGs, `tags.json` and `smoke.txt`. |
| `--smoke-ambient` | With `--smoke-test`, capture day, night, rain and snow with fabricated resting agents, plus 1×/2×/3× tag crops. Uses a fake local clock, reads no provider logs and opens no visible window. |
| `--profile-idle OUTPUT_DIR` | Bounded 20-second process CPU sample: six fabricated idle agents and available pets, adaptive 8?30fps WPF updates in one nonactivating offscreen window and an empty tracker home. Saves one initial image; avoids per-frame bitmap copies. |
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
