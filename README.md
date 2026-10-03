# Vibedotcom

A passive pixel office for local Codex, Claude Code and Antigravity sessions. Each recent session gets a character; supervisors wear ties, workers use plain shirts, and dotted lines connect known parents. Characters enter through the door, type at desks, wait, deliver reports, take varied breaks and leave when stale. Hover for provider, role, model, project, last prompt, tool, state and elapsed time. There is no task assignment or control of agents.

Phase 1 includes a standalone WPF viewer, a headless tracker and their shared .NET 10 library. **Windhawk is deferred:** this repository does not implement, install or compile a Windhawk mod. Its future input contract is [docs/state-schema.md](docs/state-schema.md).

## Build and run

Windows 11 with the .NET 10 SDK and Windows Desktop runtime. The application and tracker have no third-party runtime dependencies. Only tests require NuGet packages (xUnit and the .NET test SDK); packages restore into the ignored `.packages/` directory.

```powershell
dotnet build Vibedotcom.sln
dotnet test Vibedotcom.sln --no-build
dotnet run --project src/Vibedotcom.App --no-build
# Fabricated sessions; no real logs or personal-settings writes:
dotnet run --project src/Vibedotcom.App --no-build -- --demo
```

The WPF app observes logs itself; it does not require the console tracker. It defaults to 720×480 and fits the compact office at integer zoom. Wheel (with or without Ctrl), `+` / `−` selects 1×–6×; drag to pan. Right-click the office or tray for **Fit office to window**, normal, always-on-top, desktop-level, click-through, borderless, transparent background and opacity. Double-click the tray icon or choose **Show normal, clickable office** to recover. Closing exits and removes the tray icon. Hover for a live, passive tooltip; no agent commands are available.

Settings are persisted atomically in `%LOCALAPPDATA%\Vibedotcom\settings.json`: `mode` (`normal`, `topmost`, `desktop`), `clickThrough`, `zoom` (0=auto, integers 1–6), `width`, `height`, `left`, `top`, `borderless`, `transparentBackground`, `opacity` (.25–1), `workingSeconds` (120), `goneMinutes` (30). Older settings are accepted; fractional zoom is rounded. Edit timeouts with the app closed. Invalid/nonfinite values fall back or clamp. Desktop mode embeds under Explorer's icon layer through WorkerW or the Windows 11 24H2 raw-desktop Progman layout, with HWND_BOTTOM fallback. Hover in click-through polls cursor position without disabling click-through or activating the window; tray recovery remains available.

`--demo` starts three provider supervisors and six workers that enter, finish, deliver papers, take breaks and leave on a repeating schedule (initially two workers are already on break). Models/tasks are explicitly fabricated. Repeatable startup overrides: `--zoom 2`, `--mode desktop`, `--click-through`, `--borderless`, `--transparent`, `--opacity .85`, `--quit-after 30`. Overrides use default settings; demo never writes personal settings. `--demo --smoke-test OUTPUT_DIR` renders a fabricated scene without opening a window.

For the future Phase 2 consumer, run the tracker:

```powershell
dotnet run --project src/Vibedotcom.Tracker --no-build
# Brief run against the real machine, output confined to this checkout:
dotnet run --project src/Vibedotcom.Tracker --no-build -- --seconds 15 --output artifacts/live
# One snapshot, or changed activity thresholds:
dotnet run --project src/Vibedotcom.Tracker --no-build -- --once --output artifacts/once
dotnet run --project src/Vibedotcom.Tracker --no-build -- --working-seconds 120 --gone-minutes 60
```

Default output is `%LOCALAPPDATA%\Vibedotcom\state.json` and `atlas.png`. `Ctrl+C` stops the tracker. `--help` lists options, including `--home`, `--scan-seconds` (3) and `--interval-seconds` (0.2). `--home` selects an alternate source home, useful for synthetic fixtures. Only one tracker can write a given output directory. Output inside the selected home's `.codex`, `.claude` or `.gemini` directories is rejected.

For a portable folder install, publish and run its executable; no service or startup registration is installed:

```powershell
dotnet publish src/Vibedotcom.App -c Release -o artifacts/publish/app
dotnet publish src/Vibedotcom.Tracker -c Release -o artifacts/publish/tracker
# artifacts/publish/app/Vibedotcom.App.exe
```

These publishes require the .NET 10 runtime on the destination. No Phase 2 installation is available yet.

## Pluggable art and office

Normal startup reads `%LOCALAPPDATA%\Vibedotcom\assets\manifest.json` (PNG sheets, clips, roster, furniture and tile definitions) and `%LOCALAPPDATA%\Vibedotcom\layout.json` (tiles, rotated furniture and workspace/pantry/lounge/door zones). Missing or invalid input uses the bundled procedural fallback. Restart to reload; there is no live editor. Art being produced under `assets/art/` is separate and is not automatically bundled. See [docs/asset-format.md](docs/asset-format.md) for a small runtime manifest and the adapter naming rules for those production sheets.

```powershell
dotnet run --project src/Vibedotcom.App --no-build -- --demo --asset-dir C:/MyOfficeArt --layout C:/MyOffice/layout.json
dotnet run --project src/Vibedotcom.Tracker --no-build -- --asset-dir C:/MyOfficeArt --layout C:/MyOffice/layout.json
```

Each agent's ID hash selects a roster design, probing unused eligible designs while available. Core chooses the exact PNG frame, foot anchor and rotation-specific seat; both WPF and the future Windhawk consumer receive that choice. The tracker emits schema v3, asset/layout format 1, hashed `sheets/*.png`, `manifest.json`, source `layout.json` (only if missing), resolved `runtime-layout.json` and the legacy fallback `atlas.png`. It never overwrites an existing editable layout. [docs/state-schema.md](docs/state-schema.md) documents version changes, atomic delivery and render equations.

## Sources and inference

Formats were investigated read-only on this machine on 2026-10-03. No real prompts or transcripts are committed; fixtures contain invented example text.

| Provider | Observed source | Implemented interpretation |
| --- | --- | --- |
| Codex | `~/.codex/sessions/YYYY/MM/DD/rollout-*.jsonl` | `session_meta.id`, `cwd`, top-level `parent_thread_id` or `source.subagent.thread_spawn.parent_thread_id`, nicknames/roles; `turn_context.model`; both function and custom tool calls; reasoning, user messages, final-answer phases and task completion. |
| Claude Code | `~/.claude/projects/<project>/<session>.jsonl` | `sessionId`, `cwd`, `message.model`, thinking/text/tool_use/tool_result, `stop_reason:end_turn`; `last-prompt` supplements the tooltip. Sidechain/`agentId` and `<session>/subagents/agent-<id>.jsonl` identify workers and their parent session. `parentUuid` is a message link, never a parent session ID. |
| Antigravity | `~/.gemini/antigravity/conversations/<id>.db`, nonempty `-wal`, and `brain/<id>/.system_generated/logs/transcript_full.jsonl` (fallback `transcript.jsonl`) | One conversation = one agent. DB/WAL modification time gives liveness; JSONL `USER_INPUT`, `PLANNER_RESPONSE.tool_calls`, `GENERIC`, `status`, `created_at` refine activity and prompts. Recent DB writes without a newer semantic event imply working. |

Codex's `session_index.jsonl` and `state_5.sqlite` were considered; the database schema includes `threads` and `thread_spawn_edges`. The MVP uses rollout metadata directly, without a SQLite dependency or database access during tracking. Antigravity DB schema has `trajectory_meta`, `steps` and opaque blob payloads; these blobs are not decoded. `annotations/` contains conversation annotation files. `%APPDATA%\Antigravity\logs` contains general application/language-server logs with background network/plugin traffic; these are not reliable per-conversation activity and are not used. SQLite inspection during investigation used a read-only immutable URI, without creating WAL/SHM sidecars.

All transcript opens use `FileAccess.Read` and `FileShare.ReadWrite | FileShare.Delete`. No provider file, credential, setting, database or annotation is changed. Discovery enumerates file metadata every three seconds so a resumed old session can be found. Only recently modified logs, plus already-tracked files, are opened; unchanged files are skipped. Cold start reads at most a 512 KiB prefix and 512 KiB suffix for large logs. Subsequent reads continue from byte offsets, preserve partial UTF-8 records and recover after truncation. Records over 2 MiB are skipped with a warning; an unusually large metadata/prompt record crossing the bootstrap gap may be unavailable. Up to 8 MiB of newly appended data is processed per source per scan. This is observation by writes, not process enumeration.

Working/thinking become idle after two minutes without a write. Explicit waiting remains waiting until another event or the 30-minute gone threshold. Workers' completion becomes reporting for 12 seconds, then idle; visual pickup/carry/handover can outlast that window. Gone characters walk to the door and fade, then remain invisible until 60-second retention expires. Idle activities include coffee, silent TV, phone scrolling, sofa naps, paired chatting and water. A* avoids wall/void tiles and furniture collision cells; desk rows and office width adapt to allocated agents. Activity duration starts after arrival.

## Limits and verification

- Log events can lag actual computation and approvals. Tool/result transitions infer work/thinking; waiting is inferred from explicit input requests or turn completion, not a live UI probe. Detached/background workers without transcripts cannot be discovered. Cold-start history outside the bounded prefix/suffix is intentionally not reconstructed.
- No Claude subagent transcripts were present in the eight real files investigated. Worker path/sidechain parsing is covered by synthetic fixtures, not a live worker on this machine. Agent/Task tool calls show an agent bubble; they do not invent a worker before its transcript appears. Forked/historical sidechains can be ambiguous.
- Antigravity is always marked `bestEffort:true`. DB checkpoints/WAL writes can look like work. SHM writes, empty WALs, annotations and general application logs do not count as activity. Model, project, role and subagent relationships are unknown when absent from the transcript. Versions with different transcript locations receive DB-mtime-only tracking.
- The app uses up to 30 fps while walking and 8 fps otherwise; it redraws when positions/frames change and caches the static room and decoded crops. Tracker polling runs off the UI thread. The tracker emits frames at about 5 fps. No actual video/audio plays. Missing parents fall back to the worker's own desk vicinity. Worlds larger than the viewport at 1× require panning.
- Desktop embedding uses Explorer's private wallpaper protocol; shell/virtual-desktop changes can affect visibility. A lost host is retried, failure falls back to bottom z-order. Desktop icon hit testing cannot distinguish every icon shape. Click-through keeps clicks passing through; use the tray to change modes. Native behavior follows [Windows layered-window rules](https://learn.microsoft.com/en-us/windows/win32/winmsg/window-features); raw-desktop compatibility is best-effort shell integration.
- **Lock-screen display is unavailable.** A normal user application remains on the Default desktop and cannot display on the protected Winlogon desktop. No workaround is attempted. See [Microsoft's desktop security documentation](https://learn.microsoft.com/en-us/windows/win32/winstation/desktops).
- State snapshots contain local project paths and up to 240 characters of the last prompt; treat the output as private local data. No network traffic or uploads are performed by the tracker/viewer.

Parser, tailing, activity, pathfinding, sprite and snapshot checks run with `dotnet test`. A WPF rendering smoke test can run without opening a window or saving settings:

```powershell
Start-Process artifacts/publish/app/Vibedotcom.App.exe -WindowStyle Hidden -ArgumentList '--smoke-test artifacts/app-smoke' -Wait
# Or after a Debug build:
Start-Process src/Vibedotcom.App/bin/Debug/net10.0-windows/Vibedotcom.App.exe -WindowStyle Hidden -ArgumentList '--smoke-test artifacts/app-smoke' -Wait
```

With `--demo`, this reads no provider logs and writes phase captures, `office-1x/2x/3x.png`, a cropped viewport, `tooltip.png`, `transparent.png`, `smoke.txt` and interchange files. Without `--demo`, it reads recent logs. Hover is checked with a programmatic hit-test hook; no real cursor movement or keys are generated. It verifies offscreen layout/rendering, not native shell z-order or tray interaction.

```powershell
Start-Process src/Vibedotcom.App/bin/Debug/net10.0-windows/Vibedotcom.App.exe -WindowStyle Hidden -ArgumentList '--demo --smoke-test artifacts/engine-qa' -Wait
# 20-second bounded CPU sample, no visible window, fabricated idle agents/empty tracker home:
Start-Process src/Vibedotcom.App/bin/Debug/net10.0-windows/Vibedotcom.App.exe -WindowStyle Hidden -ArgumentList '--profile-idle artifacts/idle-profile' -Wait
# Optional own-window lifecycle QA: nonactivating, offscreen, no tray/settings writes, bounded exit:
Start-Process src/Vibedotcom.App/bin/Debug/net10.0-windows/Vibedotcom.App.exe -WindowStyle Hidden -ArgumentList '--demo --qa --quit-after 5' -Wait
```

QA mode forces normal z-order and suppresses cursor polling. Desktop/topmost/click-through policy and classic/Win11 raw-desktop host selection are tested with fake HWND topologies, without changing the user's shell. No global keyboard/mouse input, Show Desktop, foreground activation or unrelated process termination is used. The controlled idle CPU sample is process CPU seconds / wall seconds / logical processors; it is a bounded measurement, not a guarantee for every log/art load.
