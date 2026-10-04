# Privacy

Vibedotcom displays local session information. Its source files are read-only, but the information it extracts can still be private. Use `--demo` whenever you capture images for documentation or a bug report.

## What is read

The app and tracker read recent Codex session JSONL under `~/.codex`, Claude Code session JSONL under `~/.claude`, and Antigravity sources under `~/.gemini`. Antigravity includes `antigravity` and `antigravity-cli` databases, available DB sidecars and brain transcripts. Provider files and settings are not edited.

The current Antigravity implementation also reads `%APPDATA%\Antigravity\logs\language_server.log` to discover the running local server. It reads the server's local configuration and loaded-conversation summaries. It does not read a credential store; the local API's CSRF token is held in memory and omitted from diagnostics. See [sources and inference](sources-and-inference.md) for exact paths, fallback rules and limits.

## What is written

Normal app/tracker files live under `%LOCALAPPDATA%\Vibedotcom\` by default. Explicit output, layout and art paths can point elsewhere.

| File or folder | Contents and writer |
| --- | --- |
| `state.json` | Tracker snapshot with agent names, models, project folders/paths, parent relationships and activity. Task summaries may retain up to 240 characters of the last prompt; tool names/text summaries also appear where parsed. |
| `runtime-layout.json` | Tracker's resolved office layout. |
| `settings.json` | App window modes, geometry, opacity, zoom, timeouts and opt-in sharing settings/aliases. No bearer token. |
| `layout.json` | Editable office layout. The tracker creates it only if absent; editor Save writes a layout. |
| `manifest.json`, `sheets/`, `atlas.png` | Exported runtime art files. Optional custom input packs use `assets/manifest.json`. |
| `brand/` | Optional local brand overrides, written only when you install/copy them or select this output directory. |

The standalone viewer keeps session data in memory and does not continuously publish `state.json`. Its smoke-test mode does export snapshots into the requested output directory. Without `--demo`, smoke tests can read real sources.

Antigravity DB queries make disposable copies in `%TEMP%\Vibedotcom-sqlite-*`, including available WAL, SHM and journal files. They can contain more than the short text shown in the office. Cleanup is attempted when the query finishes; a crash or cleanup failure may leave a copy.

Demo does not poll real providers. Current demo/QA code uses `%TEMP%\vibedotcom-demo-*` for local settings/layout paths, including editor Save, and attempts cleanup on exit. See [app usage](app-usage.md) for the earlier review finding and the current input restrictions.

Smoke without `--demo` still polls real sources even though its personal pack/layout loading is isolated.

## Where details appear

Hover tooltips display project paths and recent task/tool information. The tracker also puts them in `state.json`. A screenshot or screen share can expose the tooltip or agent names, including when the office is embedded at desktop level. Bug-report attachments may expose the same data even if the main room looks harmless.

Do not attach your real `state.json` or raw session logs. Replace paths, names, prompts and tool arguments with invented examples. Check diagnostic output and screenshots before posting. Fixtures must contain invented text only.

## Connections and helper scripts

Presence sharing is off by default. Only the explicit Sharing consent flow enables external uploads described below. The app and tracker also make local HTTP requests to Antigravity at `127.0.0.1`. Calling this “no network access” would be inaccurate. The local client disables proxies and redirects, and an alternate tracker `--home` disables the real-profile API probe.

The source check covered `src/` and `tools/` for HTTP clients, sockets, process starts and fetch commands. The optional `tools/fetch_brand_icons.py` and its PowerShell wrapper download owner-hosted icons only when you run them. They keep raw files in `assets/brand/originals/` and processed icons in `assets/brand-overlay/` by default. `--install` copies outputs to the per-user `brand/` folder. These paths and overrides are described in the [brand guide](../assets/brand/README.md).

Other process launches in `tools/` are for local publishing, verification or the optional Windhawk tracker task. Build/package restore and the separate AI art-generation workflow may use their own network services.

## Optional presence sharing

The office and tray menus have **Sharing**. Review the entire invented-agent JSON and explicitly confirm before any request is made. The first POST sends those exact bytes; later whole snapshots send live presence every 10–30 seconds, with longer waits for rate limits or failures. Saved `sharing:"on"` does not bypass consent. Demo and QA cannot enable transmission. Headless Tracker sharing requires `--sharing` plus interactive JSON confirmation; redirected input never enables it.

The upload is built with an explicit field allowlist: `v`, `officeId`, `agents[platform,alias,state,role,parentIndex]`, `ts`. It contains only protocol version, office UUID, chosen/generic aliases, platforms, reduced working/idle/waiting state, supervisor/sub role, array-local parent indexes and the current send-time timestamp. It never serializes local `state.json` or `AgentInfo`. Prompts, task text, tool arguments/results, paths, project names, model names, session IDs, local agent IDs, machine names, local source timestamps, layouts and art metadata are excluded.

Aliases are user-entered. NFC normalization, a 24-scalar limit and character sanitization remove markup and formatting controls, but do not identify every offensive word or private detail. Alias slots can change as agents join or leave. Full privacy mode, when present as `privacy:"full"`, forces generic `Agent N` aliases in both consent and live presence. The allowlist applies in every privacy mode. Changing privacy mode stops sharing and requires a new preview.

The bearer token travels only in the `Authorization` header. HTTPS is required outside loopback; the production client disables proxies, cookies and redirects. Token storage uses Windows DPAPI **CurrentUser** in `%LOCALAPPDATA%\Vibedotcom.Credentials\presence.dpapi`, outside the normal app/export folder, bound to one server origin and office UUID. The token is absent from settings, layouts, state, previews, URLs, logs and surfaced exceptions. `.cooldown` files in this folder contain only timing data and are named by a token hash so App/Tracker/restarts respect the same request limit. Choosing not to store a supplied token keeps it in memory for that run.

Enabling transmission does not make an office public. Public visitors can see aliases, platforms, states, hierarchy and approximate activity times. Anyone who knows a public office URL can save or redistribute that data. **Stop sharing** cancels outgoing requests; an already transmitted request cannot be recalled. Presence expires within 90 seconds. Revoking a token or unpublishing clears it immediately in the server process, but previously downloaded data cannot be recalled. **Also delete the stored token** removes the local DPAPI file; it does not revoke the server token.

## Permissions and cleanup

Default files inherit the Windows user-profile directory's ACL. State, settings, layouts and exported art are not encrypted and have no private custom ACL. The sharing credential is the DPAPI exception described above; other processes running as the same Windows user can also use CurrentUser DPAPI. Custom or shared output directories use their own permissions.

Close the app, stop the tracker, and disable the Windhawk mod before deleting `%LOCALAPPDATA%\Vibedotcom\`. Deleting that folder removes its state, settings, layouts, exported art and local overrides; a tracker published inside it is removed too. It does not delete provider logs, explicit output folders or repository-local brand downloads.

Stop sharing and select token deletion, or separately remove `%LOCALAPPDATA%\Vibedotcom.Credentials\` after all senders have stopped, to remove the encrypted credential and timing files. Deleting the normal app folder does not remove this sibling folder or revoke the server token.

If you registered the optional task under its default name, remove it first:

```powershell
Stop-ScheduledTask -TaskName 'Vibedotcom.Tracker'
Unregister-ScheduledTask -TaskName 'Vibedotcom.Tracker' -Confirm
```

Use your chosen name if you supplied `-TaskName`. Confirm the tracker child has stopped; task-stop behavior remains a manual check in the [Windhawk guide](../windhawk/README.md). Remove leftover `Vibedotcom-sqlite-*` copies and `vibedotcom-demo-*` directories from your temp folder after all readers have stopped. Also clean any custom output folders you created.

## Possible privacy mode

A future mode could hide task text and project paths in local tooltips and snapshots. This checkout has no such switch. Presence sharing already filters all private local fields; its `privacy:"full"` integration additionally forces generic aliases when that setting is supplied.
