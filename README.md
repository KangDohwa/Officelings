# Vibedotcom

A passive pixel office for local Codex, Claude Code and Antigravity sessions. Each recent session gets a character; supervisors wear ties, workers use plain shirts, and dotted lines connect known parents. Characters enter through the door, type at desks, wait, deliver reports, take varied breaks and leave when stale. Hover for provider, role, model, project, last prompt, tool, state and elapsed time. There is no task assignment or control of agents.

Phase 1 includes a standalone WPF viewer, a headless tracker and their shared .NET 10 library. **Windhawk is deferred:** this repository does not implement, install or compile a Windhawk mod. Its future input contract is [docs/state-schema.md](docs/state-schema.md).

## Build and run

Windows 11 with the .NET 10 SDK and Windows Desktop runtime. The application and tracker have no third-party runtime dependencies. Only tests require NuGet packages (xUnit and the .NET test SDK); packages restore into the ignored `.packages/` directory.

```powershell
dotnet build Vibedotcom.sln
dotnet test Vibedotcom.sln --no-build
dotnet run --project src/Vibedotcom.App --no-build
```

The WPF app observes logs itself; it does not require the console tracker. Resize normally, use the mouse wheel (with or without Ctrl), `+` / `−`, or the zoom buttons. Right-click the office or tray icon for normal, always-on-top, desktop-level and click-through modes. Double-click the tray icon, or choose **Show office (normal, clickable)**, to recover a hidden/minimized/click-through window. Closing the window exits the app and removes the tray icon.

Settings are persisted atomically in `%LOCALAPPDATA%\Vibedotcom\settings.json`: `mode` (`normal`, `topmost`, `desktop`), `clickThrough`, `zoom` (0.5–4), `width`, `height`, `workingSeconds` (default 120), `goneMinutes` (default 30). Edit the timeouts with the app closed. Invalid settings fall back to defaults. Desktop-level uses Windows bottom z-order, placing the window behind ordinary application windows. It does **not** embed in Explorer's WorkerW or guarantee placement behind desktop icons. Tray recovery remains available.

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

## Sources and inference

Formats were investigated read-only on this machine on 2026-10-03. No real prompts or transcripts are committed; fixtures contain invented example text.

| Provider | Observed source | Implemented interpretation |
| --- | --- | --- |
| Codex | `~/.codex/sessions/YYYY/MM/DD/rollout-*.jsonl` | `session_meta.id`, `cwd`, top-level `parent_thread_id` or `source.subagent.thread_spawn.parent_thread_id`, nicknames/roles; `turn_context.model`; both function and custom tool calls; reasoning, user messages, final-answer phases and task completion. |
| Claude Code | `~/.claude/projects/<project>/<session>.jsonl` | `sessionId`, `cwd`, `message.model`, thinking/text/tool_use/tool_result, `stop_reason:end_turn`; `last-prompt` supplements the tooltip. Sidechain/`agentId` and `<session>/subagents/agent-<id>.jsonl` identify workers and their parent session. `parentUuid` is a message link, never a parent session ID. |
| Antigravity | `~/.gemini/antigravity/conversations/<id>.db`, nonempty `-wal`, and `brain/<id>/.system_generated/logs/transcript_full.jsonl` (fallback `transcript.jsonl`) | One conversation = one agent. DB/WAL modification time gives liveness; JSONL `USER_INPUT`, `PLANNER_RESPONSE.tool_calls`, `GENERIC`, `status`, `created_at` refine activity and prompts. Recent DB writes without a newer semantic event imply working. |

Codex's `session_index.jsonl` and `state_5.sqlite` were considered; the database schema includes `threads` and `thread_spawn_edges`. The MVP uses rollout metadata directly, without a SQLite dependency or database access during tracking. Antigravity DB schema has `trajectory_meta`, `steps` and opaque blob payloads; these blobs are not decoded. `annotations/` contains conversation annotation files. `%APPDATA%\Antigravity\logs` contains general application/language-server logs with background network/plugin traffic; these are not reliable per-conversation activity and are not used. SQLite inspection during investigation used a read-only immutable URI, without creating WAL/SHM sidecars.

All transcript opens use `FileAccess.Read` and `FileShare.ReadWrite | FileShare.Delete`. No provider file, credential, setting, database or annotation is changed. Discovery enumerates file metadata every three seconds so a resumed old session can be found. Only recently modified logs, plus already-tracked files, are opened; unchanged files are skipped. Cold start reads at most a 512 KiB prefix and 512 KiB suffix for large logs. Subsequent reads continue from byte offsets, preserve partial UTF-8 records and recover after truncation. Records over 2 MiB are skipped with a warning; an unusually large metadata/prompt record crossing the bootstrap gap may be unavailable. Up to 8 MiB of newly appended data is processed per source per scan. This is observation by writes, not process enumeration.

Working/thinking become idle after two minutes without a write. Explicit waiting remains waiting until another event or the 30-minute gone threshold. Workers' completion becomes reporting for 12 seconds, then idle. Gone characters remain for 60 seconds to walk out and are then forgotten; old sessions are omitted on a cold start. Idle activities rotate among coffee, TV, phone, napping and chatting. BFS avoids furniture; desks expand into additional rows as needed.

## Limits and verification

- Log events can lag actual computation and approvals. Tool/result transitions infer work/thinking; waiting is inferred from explicit input requests or turn completion, not a live UI probe. Detached/background workers without transcripts cannot be discovered. Cold-start history outside the bounded prefix/suffix is intentionally not reconstructed.
- No Claude subagent transcripts were present in the eight real files investigated. Worker path/sidechain parsing is covered by synthetic fixtures, not a live worker on this machine. Agent/Task tool calls show an agent bubble; they do not invent a worker before its transcript appears. Forked/historical sidechains can be ambiguous.
- Antigravity is always marked `bestEffort:true`. DB checkpoints/WAL writes can look like work. SHM writes, empty WALs, annotations and general application logs do not count as activity. Model, project, role and subagent relationships are unknown when absent from the transcript. Versions with different transcript locations receive DB-mtime-only tracking.
- The app uses nearest-neighbor sprites and a modest 10 fps scene loop. The tracker emits authoritative positions/frames at about 5 fps. Break-room actions are small visual poses; chatting is decorative, and no actual video/audio is played. A missing parent falls back to the worker's own desk.
- Desktop-level is bottom z-order, not Explorer embedding. Fullscreen apps, virtual desktops and Explorer changes may affect visibility. Click-through disables office interaction; use the tray to recover. The normal/always-on-top/click-through native behavior follows [Windows layered-window rules](https://learn.microsoft.com/en-us/windows/win32/winmsg/window-features).
- **Lock-screen display is unavailable.** A normal user application remains on the Default desktop and cannot display on the protected Winlogon desktop. No workaround is attempted. See [Microsoft's desktop security documentation](https://learn.microsoft.com/en-us/windows/win32/winstation/desktops).
- State snapshots contain local project paths and up to 240 characters of the last prompt; treat the output as private local data. No network traffic or uploads are performed by the tracker/viewer.

Parser, tailing, activity, pathfinding, sprite and snapshot checks run with `dotnet test`. A WPF rendering smoke test can run without opening a window or saving settings:

```powershell
Start-Process artifacts/publish/app/Vibedotcom.App.exe -WindowStyle Hidden -ArgumentList '--smoke-test artifacts/app-smoke' -Wait
# Or after a Debug build:
Start-Process src/Vibedotcom.App/bin/Debug/net10.0-windows/Vibedotcom.App.exe -WindowStyle Hidden -ArgumentList '--smoke-test artifacts/app-smoke' -Wait
```

This reads actual recent logs, decodes the generated atlas, advances the simulation and writes `office.png`, `smoke.txt` and the interchange files. It verifies WPF layout/rendering, not manual mouse/tray/native z-order behavior.
