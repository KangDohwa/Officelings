# Standalone app

The WPF app observes sessions itself; it needs neither Windhawk nor the separate [Tracker](tracker.md). Run commands from the repository root after the [build](../README.md#quick-start). This reference was checked against the App entry point and Settings partial records on 2026-10-04.

## Flat and isometric views

Select `--view flat|iso`, save `view` in settings, or choose **Isometric view** in either office/tray menu. Flat remains the default. A live toggle replaces the window and carries current agents, geometry and window preferences; it does not preserve every animation phase.

| Feature | Flat | Iso |
| --- | --- | --- |
| Room and characters | 35×20 tiles, twelve desks, overflow seats, larger roster | 10×10 grid, twelve desks, three designs: dev-hoodie-v2, punk-girl, professor |
| Simulation | Work, waits, reports, breaks, departure/re-entry | Work/thinking/waiting, desk seating transitions, reports, departure/re-entry; idle stays seated, excess agents queue |
| Pets | One or two available pet designs roam, follow a supervisor or nap | One roaming/sleeping cat; `pets` on/off applies |
| Atmosphere | Local-time windows/clock/lights, optional rain/snow/December decor | Flat weather/season settings are not connected to the iso scene |
| Tags, privacy and window modes | Supported | Supported; separate scene/host |
| Editing and custom input | **Edit office**, flat pack and `--layout` | No editor UI; `--asset-dir` selects an iso pack, including its own layout; `--layout` rejected |
| Sharing | Office/tray **Sharing** dialog | No Sharing UI or sender; switching away from flat stops that window's sender |
| Interchange | Tracker/exported v5 snapshots | In-process `IIsoOffice`/`IsoScene`; planned v6 export/native consumption is not implemented |
| Profiling | `--profile-idle` | Rejected |

Iso uses its embedded pack, independent of the installed flat pack. The [iso integration contract](iso-integration-contract.md) describes the current API; [S0](iso-slice-s0.md) preserves production geometry and future interchange plans. Those plans are not a claim that every feature is integrated.

## Window and controls

Default size is 720×480. Wheel (with or without Ctrl), `+` and `-` select integer zoom 1–6×; auto-fit may shrink below 1× for a small window. Drag to pan. Hover for provider, role, model, project, recent prompt/tool, state and elapsed time, subject to privacy. Tooltips do not activate the window.

Right-click the office or tray for normal/topmost/desktop mode, click-through, border, background, opacity, **Name tags**, **Privacy**, **Pets** and view switching. Flat also has **Edit office** and **Sharing**. Double-click the tray or choose **Show normal, clickable office** to recover. Closing exits and removes the icon.

**Privacy** offers **Off**, **Hide tasks** (`tasks`) and **Hide all details** (`full`). Core discards sensitive text before state is stored and clears retained local metadata when the mode changes. `tasks` keeps names/models and the project leaf; `full` uses generic provider labels and removes models/projects. Antigravity prompt-derived names are hidden at either enabled level. Demo text remains fabricated and unredacted. See [privacy](privacy.md), including the separate sharing-setting limitation.

**Name tags** defaults to Hover. Icon keeps provider/role icons visible and reveals a full plate on hover; Always keeps full plates. In flat, an away owner's reserved desk can reveal its plate too. Bundled icons are 10px at 1× and 12px from 2×; local brand overrides have precedence. Flat hover fades use 120ms in, 60ms leave grace and 200ms out; body, plate and gap keep the same target. Detail tooltips wait 420ms and avoid visible plates. Iso uses the same mode names and bundled provider art with its own hit tests.

Desktop mode tries WorkerW or the Windows 11 24H2 Progman layout beneath the icon layer, falling back to `HWND_BOTTOM`. These are undocumented shell paths. Offscreen/owned-window tests do not verify Explorer injection, Win+D, mixed-DPI monitor movement or virtual-desktop behavior. Neither view appears on the secure lock screen. See [limits](limits.md#desktop-and-lock-screen).

## Settings

Normal app runs atomically write `%LOCALAPPDATA%\Vibedotcom\settings.json`. Close the app before manual edits. Files over 64 KiB, unreadable files and invalid JSON fall back to normalized defaults.

| Key | Default and accepted values |
| --- | --- |
| `view` | `flat`; also `iso`. Unknown values normalize to flat. |
| `mode` | `normal`; also `topmost`, `desktop`. |
| `tagMode` | `hover`; also `icon`, `always`. Null/unknown values use hover. Not stored in Tracker state. |
| `privacyMode` | `off`; also `tasks`, `full`. Valid CLI override → valid saved key → `VIBEDOTCOM_PRIVACY` → off. |
| `ambient` | Object with `weather`; missing/null uses `{"weather":"off"}`. Flat only. |
| `ambient.weather` | `off`; also `rain`, `snow`, `auto`. Auto selects snow in December, clear otherwise. No weather service. |
| `seasonDecor` | `off`; `auto` adds December window decoration. Flat only. |
| `pets` | `on`; also `off`. Controls flat pets or the iso cat. |
| `sharing` | `off`; also `on`. Saved intent only, never automatic consent. Flat/interactive Tracker only. |
| `sharingServer` | Empty; intended hosted origin `https://vibe-com.rayd.cc`. HTTPS outside loopback; reject credentials, paths, queries and fragments when enabling. |
| `sharingOfficeId` | Empty; enabling requires a canonical nonzero office UUID for the token. |
| `sharingAliases` | Empty object; map `platform:slot` to chosen aliases, e.g. `{"codex:1":"Nova","claude:1":"Moon"}`. Platforms codex/claude/antigravity/other; slots 1–64. |
| `clickThrough` | `false`. |
| `zoom` | `0` auto-fit; normalized range 0–6, rounded to an integer when applied. |
| `width`, `height` | 720, 480; clamp to 400–3000 and 300–2000. |
| `left`, `top` | Unset; finite coordinates clamp to −100000…100000, then recover into monitor working areas. |
| `borderless` | `false`. |
| `transparentBackground` | `false`. |
| `opacity` | `1`; clamp to 0.25–1. |
| `workingSeconds` | 120; clamp to 1–86400 seconds. |
| `leaveAfterIdleMinutes` | 5; clamp to 1–10080 minutes, raised to exceed the working window. |
| `leaveAfterWaitingMinutes` | 30; same range/order rule, for explicit input/approval waits. |
| `goneMinutes` | Deprecated waiting-window alias. Explicit new key wins in either JSON order; saved alias equals the normalized waiting value. |

Booleans use JSON true/false. Invalid/nonfinite numeric values normalize where supported. Normal app menu changes save immediately; startup overrides save on close. Options that create a settings override (zoom, window, tags, atmosphere, timeouts) start from defaults rather than merging all saved fields. Saved `view` is retained unless explicitly overridden; privacy is resolved separately. A lone `--view` or `--privacy` can preserve the other saved settings. Demo/QA/profiling/smoke/opacity checks use disposable storage.

**Sharing adapter detail:** its separate configuration reader recognizes `privacy`, default off; that is not an additional supported App Settings key. App Settings stores `privacyMode` and does not map it to that field. The local Privacy menu therefore does not currently guarantee generic sharing aliases or stop-on-privacy-change behavior. See [sharing privacy limits](privacy.md#optional-presence-sharing).

Quiet Working/Thinking becomes Idle after `workingSeconds`. Finished workers can complete report delivery before flat breaks; departure uses time since real activity. Active descendants keep parents present. Closed-session observations can cause earlier departure; otherwise timeouts apply. Core handles raising, walking to the door, fading and re-entry. See [source inference](sources-and-inference.md#closed-sessions).

## Presence sharing

Sharing starts **off**. In flat, open **Sharing** from the office or tray menu. Enter the hosted origin, your office UUID and ingest bearer token issued by the server. The server/account/public viewer workflow is being built separately; this checkout does not contain or verify it. Creating a token, transmitting presence and publishing an office are separate operations.

Enter one alias per line as `platform:slot=alias`, e.g. `codex:1=Nova`. Slots belong to the current platform snapshot (supervisors before workers), may shift as membership changes, and are not persistent session identities. Missing aliases use `Agent N`. NFC normalization precedes the 24-Unicode-scalar limit; only letters, digits, ASCII spaces, `_`, `-` and `.` remain and spaces collapse. Sanitization does not identify private information.

Click **Preview exact JSON**, review the entire invented-agent example, check **I reviewed this JSON and agree to share presence**, then **Enable sharing**. The first POST sends exactly the displayed UTF-8 bytes. Later heartbeats send complete live presence. Editing server, office or aliases invalidates consent; previews expire after 90 seconds. Tokens are masked and never appear in the body.

Body fields are `v`, `officeId`, `agents` and `ts`; each agent has `platform`, `alias`, `state`, `role`, optional `parentIndex`. Working/thinking → working, explicit wait → waiting, reporting/idle → idle; gone is omitted. Parents refer to earlier supervisors in that array. Names, prompts, tasks/tools, paths, projects/models and local IDs are excluded. Exceeding 64 agents or 8192 UTF-8 bytes stops sharing instead of sending a partial office.

Successful heartbeats use random 10–30 second intervals. Attempts consume at least 10 seconds; 429 respects Retry-After and temporary failures back off. App/Tracker share a timing gate. HTTPS is required outside loopback; redirects, proxies and cookies are disabled. Server rejection stops transmission. The **Sharing** line reports off/sending/live/retry/stopped status.

By default, Windows DPAPI **CurrentUser** stores the token in `%LOCALAPPDATA%\Vibedotcom.Credentials\presence.dpapi`, bound to server and office. Uncheck storage to keep the supplied token in memory for this run. The token does not enter settings, layouts, state, URLs or diagnostics. Hash-named `.cooldown` files hold timing data only. Other processes running as the same user can use CurrentUser DPAPI.

Use **Stop sharing** to cancel the sender. **Also delete the stored token** removes the DPAPI file. Neither action revokes the server token nor unpublishes an office. The intended server contract expires presence within 90 seconds and clears it on revocation/unpublish; that behavior was not verified against the hosted service. Downloaded public data cannot be recalled.

Demo/QA disable sharing. A new process or replacement window (view/transparency changes) requires fresh consent. The protocol supports generic aliases for its `privacy:"full"` input and stop-on-change, but the current `privacyMode` adapter mismatch described above prevents relying on the app menu for this. Stop explicitly before changing privacy.

For headless sharing, enable it and store the token in flat, then close the app. Put its saved sharing configuration in the Tracker output directory's `settings.json`. Run Tracker with `--sharing` interactively, review the complete invented JSON and type `ENABLE`. No flag or redirected input means no uploads. Ctrl+C cancels. The app's Stop sharing saves `sharing:"off"`, which a Tracker reading the same file observes. See [Tracker flags](tracker.md#options).

## Atmosphere, rest activities and art

Flat windows use local time: day 07:00–17:00, sunset 17:00–19:00, night otherwise. Rain/snow replaces clear views; string lights are on 17:00–07:00. Clock hands use the same clock. These settings do not fetch weather.

```json
{ "view": "flat", "ambient": { "weather": "rain" }, "seasonDecor": "auto", "pets": "on" }
```

Flat resting agents can use coffee, water, TV, phone, napping, chatting, darts, ping-pong, arcade, reading, beanbag, aquarium, vending, fridge and window activities. Interaction points are reserved individually. Ping-pong starts after both arrive and stops when either returns to work or leaves. Pets do not become tracked agents or reserve desks.

To tune breaks, place `activities.json` next to app settings or in the Tracker output folder, then restart. Unlisted IDs keep [bundled defaults](../src/Vibedotcom.Core/Data/activities.json); zero weight disables an activity and duration begins on arrival.

```json
{
  "version": 1,
  "activities": [
    { "id": "darts", "weight": 3, "minSeconds": 14, "maxSeconds": 24 },
    { "id": "pingpong", "weight": 4, "minSeconds": 20, "maxSeconds": 33 }
  ]
}
```

Flat startup tries the per-user assets folder and `layout.json`, then bundled `assets/pack/`, then procedural fallback. Invalid custom files can fall back with warnings. Iso loads its own embedded pack; `--asset-dir` must point at iso input there. Restart to reload files. Flat editor instructions and file limits are in [editor](editor.md), [asset format](asset-format.md) and [art pipeline](art-pipeline.md).

```powershell
dotnet run --project src/Vibedotcom.App --no-build -- --demo --asset-dir ./assets/pack --layout ./src/Vibedotcom.Core/Data/layout.json
dotnet run --project src/Vibedotcom.App --no-build -- --view iso --demo --asset-dir ./assets/art/iso/pack
```

Isolated modes skip implicit personal art/layout/brand inputs and reject explicit input inside the normal app-data folder. Editor Save uses a disposable demo folder; cleanup is attempted on exit. Use only synthetic inputs in verification. Not every reader uses the same hardened boundary: stock flat input/publication routes have link/size checks, while iso external pack/layout and explicit smoke snapshot routes have different checks. Do not describe the whole application as safe for arbitrary untrusted inputs.

## Startup and QA flags

This table includes every flag accepted by `Program.Main`. App has no `--help` flag; unknown options fail. Tracker's separate option set is in [tracker.md](tracker.md#options).

| Flag | Meaning and restrictions |
| --- | --- |
| `--demo` | Fabricated scenario, three provider leads and up to six workers; isolated storage, no real provider polling or sharing. |
| `--view flat\|iso` | View selection; default flat. |
| `--privacy off\|tasks\|full` | Startup local redaction; normal runs persist it. Demo text remains fabricated. |
| `--tags hover\|icon\|always` | Name tag override, including smoke. |
| `--ambient-weather off\|rain\|snow\|auto` | Flat window weather. Parsed in iso but not connected to its scene. |
| `--season-decor off\|auto` | Flat December decoration. Parsed in iso but not connected. |
| `--pets on\|off` | Flat pets or iso cat. |
| `--asset-dir DIR` | Flat runtime art pack, or iso pack when using iso. |
| `--layout FILE` | Explicit flat layout. Iso rejects it and owns its pack layout. |
| `--zoom N` | 0 auto-fit, otherwise 1–6 after normalization/rounding. |
| `--mode normal\|topmost\|desktop` | Startup window mode; invalid values normalize to normal. QA forces normal. |
| `--click-through` | Enable click-through. |
| `--borderless` | Hide normal borders. |
| `--transparent` | Enable transparent background. |
| `--opacity N` | Clamp to 0.25–1. |
| `--working-seconds N` | Positive finite working timeout, default 120; Settings applies its limits. |
| `--leave-after-idle-minutes N` | Positive finite idle departure timeout, default 5. |
| `--leave-after-waiting-minutes N` | Positive finite explicit-wait departure timeout, default 30. |
| `--gone-minutes N` | Deprecated waiting alias; new option wins regardless of order. |
| `--quit-after SECONDS` | A positive finite value schedules closure. Required within (0,60] for QA. |
| `--qa` | Nonactivating owned offscreen window, no tray/save/cursor polling; requires bounded quit time. |
| `--home DIR` | Tracker source home, only with QA or smoke. Use an empty/synthetic folder for checks. Does not relocate the normal settings directory. |
| `--smoke-test OUTPUT_DIR` | Offscreen PNGs; flat also exports v5 interchange. Add demo for the synthetic scenarios; no visible office window. |
| `--output PATH`, `--state PATH` | Aliases for a Tracker output directory or `state.json` input; require smoke. Flat uses exported assets from its directory unless asset-dir is supplied; iso reads agents into its own scene. |
| `--smoke-tags` | Requires smoke. Flat captures all three modes with 12 synthetic agents and programmatic hover; iso adds the three modes to its demo captures. |
| `--smoke-ambient` | Requires smoke; flat day/night/rain/snow with synthetic resting agents and 1×/2×/3× icon crops. Iso currently ignores this selector. Flat chooses it before smoke-tags if both are given. |
| `--profile-idle OUTPUT_DIR` | Flat only: 20-second offscreen CPU sample with six synthetic idle agents, pets and empty tracker home; one initial PNG, no per-frame copies. Not a long-term/visible-desktop benchmark. |
| `--verify-opacity OUTPUT_DIR` | Owned-window opacity check; dispatches before view selection. See [verification](limits.md#verification). |

Without demo or an explicit home, smoke uses its isolated empty home; an explicit home/state can contain private data. Demo alone does not sanitize an explicitly supplied snapshot. QA/offscreen rendering does not prove native desktop hosting or Explorer injection.

## Screenshots

The README uses current flat office, iso twelve-seat, flat night and provider-tag images under [docs/img](img/README.md). All were rendered from fabricated sessions without installing brand overrides. Regeneration commands, source filenames, PNG optimization and the recorded flat smoke limitation are in that directory's README.

## Publish

```powershell
dotnet publish src/Vibedotcom.App -c Release -o artifacts/publish/app
dotnet publish src/Vibedotcom.Tracker -c Release -o artifacts/publish/tracker
```

Framework-dependent folders need the runtime described in the [README](../README.md#quick-start). These commands install no service or startup entry. [Release packaging](../tools/release/README.md) creates runtime-dependent and self-contained Windows x64 ZIPs from committed HEAD; uncommitted work is excluded. Review the [release checklist](release-checklist.md) and [unsigned-binary notice](windows-security-notice.md) before distribution.
