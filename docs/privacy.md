# Privacy

Vibedotcom displays local session information. Its source files are read-only, but the information it extracts can still be private. Turn on privacy before streaming or sharing your screen. Use `--demo` for documentation and bug-report images.

## What is read

The app and tracker read recent Codex session JSONL under `~/.codex`, Claude Code session JSONL under `~/.claude`, and Antigravity sources under `~/.gemini`. Antigravity includes `antigravity` and `antigravity-cli` databases, available DB sidecars and brain transcripts. Provider files and settings are not edited.

The current Antigravity implementation also reads `%APPDATA%\Antigravity\logs\language_server.log` to discover the running local server. It reads the server's local configuration and loaded-conversation summaries. It does not read a credential store; the local API's CSRF token is held in memory and omitted from diagnostics. See [sources and inference](sources-and-inference.md) for exact paths, fallback rules and limits.

## What is written

Normal app/tracker files live under `%LOCALAPPDATA%\Vibedotcom\` by default. Explicit output, layout and art paths can point elsewhere.

| File or folder | Contents and writer |
| --- | --- |
| `state.json` | Tracker snapshot with agent metadata, parent relationships, activity and a `privacy` level. With privacy off, task summaries may retain up to 240 characters of the last prompt. Enabled levels redact metadata in Core before snapshots are made. |
| `runtime-layout.json` | Tracker's resolved office layout. |
| `settings.json` | App window modes, geometry, opacity, zoom, timeouts, `privacyMode` and opt-in sharing settings/aliases. No bearer token. |
| `layout.json` | Editable office layout. The tracker creates it only if absent; editor Save writes a layout. |
| `manifest.json`, `sheets/`, `atlas.png` | Exported runtime art files. Optional custom input packs use `assets/manifest.json`. |
| `brand/` | Optional local brand overrides, written only when you install/copy them or select this output directory. |

The standalone viewer keeps session data in memory and does not continuously publish `state.json`. Its smoke-test mode does export snapshots into the requested output directory. Without `--demo`, smoke tests can read real sources.

With privacy off, Antigravity DB queries make disposable copies in `%TEMP%\Vibedotcom-sqlite-*`, including available WAL, SHM and journal files. They can contain more than the short text shown in the office. Cleanup is attempted when the query finishes; a crash or cleanup failure may leave a copy. With `tasks` or `full`, DB and committed WAL pages are read into memory instead; queries create no raw DB copies or provider sidecars. Unstable databases, active rollback journals, files above 256 MiB or an unavailable SQLite memory API use cached state and transcript/local API fallbacks, with a generic warning.

Demo does not poll real providers. Current demo/QA code uses `%TEMP%\vibedotcom-demo-*` for local settings/layout paths, including editor Save, and attempts cleanup on exit. See [app usage](app-usage.md) for the earlier review finding and the current input restrictions.

Smoke without `--demo` still polls real sources even though its personal pack/layout loading is isolated.

## Where details appear

With privacy off, hover tooltips display project paths and recent task/tool information. The tracker also puts them in `state.json`. Privacy applies to those fields, character plates and reserved-desk plates, including the office editor's live agents. A screenshot or screen share can expose these details if privacy is off, including when the office is embedded at desktop level.

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

## Privacy levels

| Level | Visible session text |
| --- | --- |
| `off` | Existing behavior: names, models, paths, prompts, roles and tools. This remains the default. |
| `tasks` | Provider, agent name, model, state, elapsed time and only the project folder's leaf name. Prompts, tasks, tool text/categories and roles are removed. Antigravity conversation titles are derived from prompts, so its names become generic too. |
| `full` | Provider, state and elapsed time, with stable names such as `Codex`, `Codex #2`, `Claude` and `Antigravity`. Model names, project names and paths are removed. Labels remain stable for an agent during that tracker run. |

Right-click the office or tray, open **Privacy**, and choose **Off**, **Hide tasks** or **Hide all details**. The change clears the current tooltip, retained name drawings and held agent text immediately, then saves `privacyMode` for the next launch. A small viewer badge shows the active level. Turning privacy off does not recover text already discarded from memory; new provider events can supply text again. A restart with privacy off reads the original sources again. Demo mode keeps its fabricated text.

You can also set the level before startup:

```powershell
dotnet run --project src/Vibedotcom.App --no-build -- --privacy full
dotnet run --project src/Vibedotcom.Tracker --no-build -- --privacy tasks
$env:VIBEDOTCOM_PRIVACY = 'full'
```

Or add `"privacyMode": "full"` to `settings.json`. The app uses its normal settings file; the tracker uses the file in its `--output` directory. Startup precedence is `--privacy`, then a valid saved `privacyMode`, then `VIBEDOTCOM_PRIVACY`, then `off`. An app startup override is saved on close; menu choices save immediately. A tracker without `--privacy` follows valid settings changes on its next update. A tracker started with the flag keeps that explicit level until restarted. If the app and tracker use different output/settings directories, set each separately.

## Guarantees and limits

Enabled levels redact at Core text assignment, before agent state and snapshots receive the text. Switching to a stricter level scrubs held state and parser metadata caches. `state.json`, resolved desk ownership, tooltips, tag plates and emitted diagnostics contain only the allowed metadata. Private snapshots use opaque session IDs so a filename fallback cannot reveal a name. The additive root `privacy` field is `off`, `tasks` or `full`; it does not change schema version 5. Windhawk can render already-redacted snapshots without adding its own redaction.

The tracker still reads raw provider logs and local Antigravity data to infer state. Privacy does not edit, encrypt or delete those sources. It does not remove old snapshots, attachments, backups or temp copies left by an earlier run with privacy off. Ordinary permissions still apply. Raw parser buffers can exist briefly in process memory; this feature does not promise protection from memory inspection, OS paging or crash dumps.

Other apps' windows, terminal output, screenshots captured before the switch and names you put in custom artwork or authored layout/asset labels are outside this mode. Provider, session counts, relationships, activity and timing remain visible. In `tasks`, the project leaf and agent/model names are intentionally visible. Use `full` when those names are sensitive.

Presence sharing is independent of these levels: it sends only the allowlisted fields described in the Presence sharing section, and nothing from a level that is off is exposed by it.
