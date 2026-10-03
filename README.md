# Vibedotcom

A passive pixel office for local Codex, Claude Code and Antigravity sessions. Each recent session gets a character; supervisors wear ties, workers use plain shirts, and dotted lines connect known parents. Characters enter through the door, type at desks, wait, deliver reports, take varied breaks and leave when stale. Hover for provider, role, model, project, last prompt, tool, state and elapsed time. There is no task assignment or control of agents.

Two variants share one .NET 10 tracker core and one art pack:

- **Standalone** (`src/Vibedotcom.App`): a WPF viewer that reads the logs itself. No Windhawk needed.
- **Windhawk** (`windhawk/`): a C++ Windhawk mod that renders the same office inside Explorer, fed by the headless tracker (`src/Vibedotcom.Tracker`) through `state.json`. Contract: [docs/state-schema.md](docs/state-schema.md).

### Status (work in progress)

| Area | State |
| --- | --- |
| Tracker (Codex, Claude Code, Antigravity), animation state machine, A* pathfinding | Done, covered by tests |
| Standalone WPF viewer, window modes, tooltip, zoom/pan | Done; native desktop-level/click-through behaviour is checked with fake HWND topologies, not yet on the live shell |
| Art: 44+ chibi characters, 37 environment sheets, face-lock (stable eyes), blink | Done; the last roster team is still being produced |
| Fixed pre-furnished office, provider tags, opacity fix, office editor | Done (editor not yet hands-on tested by the author) |
| Seated poses, side-view desks, still-TV animation, official-icon overlay, transparent-background fix | In progress |
| Windhawk mod | In progress; compiles with Windhawk's bundled clang, Explorer injection not yet verified (checklist will live in `windhawk/README.md`) |

Lock-screen display is not possible for a normal user application (see Limits).

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

The headless tracker feeds the Windhawk variant (the standalone app does not need it):

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

These publishes require the .NET 10 runtime on the destination. Windhawk installation steps will be in `windhawk/README.md` once the mod is finished.

## Pluggable art and office

Normal startup reads `%LOCALAPPDATA%\Vibedotcom\assets\manifest.json` (PNG sheets, clips, roster, furniture and tile definitions) and `%LOCALAPPDATA%\Vibedotcom\layout.json` (tiles, rotated furniture and workspace/pantry/lounge/door zones). Missing or invalid input uses the bundled art pack (`assets/pack/`, embedded in the app by `tools/build_pack.py`), and finally a procedural fallback. Restart to reload, or use the in-app office editor (right-click → Edit office, see [docs/editor.md](docs/editor.md)). See [docs/asset-format.md](docs/asset-format.md) for the runtime manifest and layout format.

```powershell
dotnet run --project src/Vibedotcom.App --no-build -- --demo --asset-dir C:/MyOfficeArt --layout C:/MyOffice/layout.json
dotnet run --project src/Vibedotcom.Tracker --no-build -- --asset-dir C:/MyOfficeArt --layout C:/MyOffice/layout.json
```

Each agent's ID hash selects a roster design, probing unused eligible designs while available. Core chooses the exact PNG frame, foot anchor and rotation-specific seat; both WPF and the future Windhawk consumer receive that choice. The tracker emits schema v3, asset/layout format 1, hashed `sheets/*.png`, `manifest.json`, source `layout.json` (only if missing), resolved `runtime-layout.json` and the legacy fallback `atlas.png`. It never overwrites an existing editable layout. [docs/state-schema.md](docs/state-schema.md) documents version changes, atomic delivery and render equations.

## Art pipeline and credits

All pixel art in `assets/` is generated for this project (2D pixel chibi style). Nothing is copied from other projects; [pixel-agents](https://github.com/pixel-agents-hq/pixel-agents) (MIT) was studied only as a design reference, see [docs/pixel-agents-notes.md](docs/pixel-agents-notes.md).

1. **Generate** characters, props and tiles with the [sprite-gen](https://github.com/aldegad/sprite-gen) skill (Apache-2.0). Final sheets live in `assets/art/sheets`, `assets/art/roster/<id>` and `assets/art/env2`; scratch runs are git-ignored. Overview: [assets/art/MANIFEST.md](assets/art/MANIFEST.md), [assets/art/env2/MANIFEST.md](assets/art/env2/MANIFEST.md).
2. **Face-lock**: `python tools/facelock.py --all --blink` makes eyes identical across a facing's frames (re-generated frames otherwise make eyes jitter while walking) and adds blink frames. Output goes to `locked/` next to the originals. See [docs/facelock.md](docs/facelock.md).
3. **Pack**: `python tools/build_pack.py` normalizes character height, prefers locked sheets and writes the runtime pack in `assets/pack/`, which the Core embeds. Re-run it whenever art changes.

Generated assets are produced with an AI image model; check the terms that apply to your use before redistributing them.

## Trademarks, brand assets and licensing

Vibedotcom is an independent, unofficial tool. It is **not affiliated with, endorsed by or sponsored by** OpenAI, Anthropic or Google. "Codex", "ChatGPT", "Claude", "Claude Code", "Gemini" and "Antigravity" are trademarks of their respective owners and are used here only in plain text to say which tools the viewer can observe (nominative use).

Brand-owner rules, summarized from their published guidelines (researched 2026-10-04; re-check before release, they change):

| Owner | What the guidelines say about logos |
| --- | --- |
| [OpenAI](https://openai.com/brand/) | Use logos only as provided; do not modify, recolor, stylize or distort; do not imply endorsement or a partnership; do not put them into your own product branding. Prefer the black/white versions supplied by OpenAI. |
| [Anthropic](https://www.anthropic.com/legal/trademark-guidelines) | Use only as specifically permitted and in materials Anthropic approves beforehand; no alteration of color, font or proportion; no implied sponsorship or affiliation; the guidelines do not say that in-app logo use is allowed, so ask marketing@anthropic.com. |
| [Google](https://about.google/brand-resource-center/guidance/) | Do not use Google marks in your product or brand name or imply affiliation; product icons generally fall under "ask first" ([icon usage guidance](https://about.google/brand-resource-center/products-and-services/)). Google publishes no Gemini-specific third-party rule that permits using the sparkle in an app without asking. |

Consequences for this project (it has no paid features, but trademark rules apply to free software too):

- The **default build ships generic, self-drawn tag icons** (neutral shapes in each provider's accent color) and never bundles official logos.
- Official icons are an **optional, local-only overlay**: `tools/fetch_brand_icons.ps1` downloads them from the owners' own sites onto your machine into a git-ignored folder, and you may also drop `codex.png`, `claude.png`, `antigravity.png` into `%LOCALAPPDATA%\Vibedotcom\brand\`. This is for personal use; do not redistribute builds that embed them without permission. Source list: [assets/brand/README.md](assets/brand/README.md).
- Antigravity is a separate Google product from the Gemini models; the optional overlay uses the Gemini sparkle for it by the author's choice, which Google might consider a misattribution.
- Do not name a fork or package with a provider's name (`Codex-Something`, `Claude-…`, `Gemini-…`).

**Checklist before making the repository public**

1. Purge `assets/brand/originals/` (and any official logo) from git history (history rewrite, force-push); the files are already git-ignored for new commits.
2. Choose a license for the project code (e.g. MIT or Apache-2.0) and add `LICENSE`; keep third-party notices (sprite-gen is Apache-2.0; pixel-agents, MIT, was only a design reference and nothing was copied).
3. Review the terms for AI-generated images that apply to the art in `assets/`, and keep the credit line in this README.
4. Confirm the screenshots contain only fabricated `--demo` data (no real prompts, paths or project names).
5. Optionally ask the three owners for written permission if you want official logos in public releases.

## Sources and inference

Formats were investigated read-only on this machine on 2026-10-03. No real prompts or transcripts are committed; fixtures contain invented example text.

| Provider | Observed source | Implemented interpretation |
| --- | --- | --- |
| Codex | `~/.codex/sessions/YYYY/MM/DD/rollout-*.jsonl` | `session_meta.id`, `cwd`, top-level `parent_thread_id` or `source.subagent.thread_spawn.parent_thread_id`, nicknames/roles; `turn_context.model`; both function and custom tool calls; reasoning, user messages, final-answer phases and task completion. |
| Claude Code | `~/.claude/projects/<project>/<session>.jsonl` | `sessionId`, `cwd`, `message.model`, thinking/text/tool_use/tool_result, `stop_reason:end_turn`; `last-prompt` supplements the tooltip. Sidechain/`agentId` and `<session>/subagents/agent-<id>.jsonl` identify workers and their parent session. `parentUuid` is a message link, never a parent session ID. |
| Antigravity | `~/.gemini/antigravity/conversations/<id>.db`, nonempty `-wal`, and `brain/<id>/.system_generated/logs/transcript_full.jsonl` (fallback `transcript.jsonl`) | One conversation = one agent. DB/WAL modification time gives liveness; JSONL `USER_INPUT`, `PLANNER_RESPONSE.tool_calls`, `GENERIC`, `status`, `created_at` refine activity and prompts. Recent DB writes without a newer semantic event imply working. |

Codex's `session_index.jsonl` and `state_5.sqlite` were considered; the database schema includes `threads` and `thread_spawn_edges`. The MVP uses rollout metadata directly, without a SQLite dependency or database access during tracking. Antigravity DB schema has `trajectory_meta`, `steps` and opaque blob payloads; these blobs are not decoded. `annotations/` contains conversation annotation files. `%APPDATA%\Antigravity\logs` contains general application/language-server logs with background network/plugin traffic; these are not reliable per-conversation activity and are not used. SQLite inspection during investigation used a read-only immutable URI, without creating WAL/SHM sidecars.

All transcript opens use `FileAccess.Read` and `FileShare.ReadWrite | FileShare.Delete`. No provider file, credential, setting, database or annotation is changed. Discovery enumerates file metadata every three seconds so a resumed old session can be found. Only recently modified logs, plus already-tracked files, are opened; unchanged files are skipped. Cold start reads at most a 512 KiB prefix and 512 KiB suffix for large logs. Subsequent reads continue from byte offsets, preserve partial UTF-8 records and recover after truncation. Records over 2 MiB are skipped with a warning; an unusually large metadata/prompt record crossing the bootstrap gap may be unavailable. Up to 8 MiB of newly appended data is processed per source per scan. This is observation by writes, not process enumeration.

Working/thinking become idle after two minutes without a write. Explicit waiting remains waiting until another event or the 30-minute gone threshold. Workers' completion becomes reporting for 12 seconds, then idle; visual pickup/carry/handover can outlast that window. Gone characters walk to the door and fade, then remain invisible until 60-second retention expires. Idle activities include coffee, silent TV, phone scrolling, sofa naps, paired chatting and water. A* avoids wall/void tiles and furniture collision cells. Activity duration starts after arrival. (The current build still grows desk rows with the number of agents; this is being replaced by a fixed, pre-furnished office where agents claim existing desks.)

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
