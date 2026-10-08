# Officelings — spec (MVP)

A passive "idle-game" desktop viewer: pixel-art office where every running AI coding agent
(Codex, Claude Code, Antigravity) is a little character. Observation only — no task assignment.

## Behaviour
- One character per agent session/sub-agent. Supervisor (parent) and sub-agents are visually distinct
  (e.g. supervisor has a tie/crown), colored/badged per provider (Codex / Claude / Antigravity).
- States: `working` (typing at desk, tool-specific bubble icon), `thinking`, `waiting` (user input),
  `reporting` (sub-agent finished → walks to parent's desk, hands over a report/paper, then goes idle),
  `idle` (go to pantry/break room: coffee, watching YouTube on a TV, phone, napping, chatting; random pick, with variety),
  `gone` (session ended/stale → walks out the door).
- Sub-agent spawn: new character walks in the door → desk. Parent→child relationship drawn (e.g. dotted line / pointing animation).
- Hover tooltip: provider, agent name/role, model, cwd/project, current task (last user prompt / current tool call), state, elapsed.
- Controls: window resize, zoom in/out (wheel/ctrl+wheel, +/-), nothing else interactive. No task assignment.
- Window modes: normal / always-on-top / desktop-wallpaper-level (behind icons or behind all windows) / click-through toggle
  (right-click tray or context menu is fine; persists in settings).
- Lock screen: NOT possible for normal user-mode apps (Winlogon secure desktop). Don't attempt; document in README.

## Data sources (read-only; discover exact formats yourself from this machine's real files)
- Codex: `~/.codex/sessions/YYYY/MM/DD/rollout-*.jsonl` (+ `session_index.jsonl`, `state_5.sqlite`). Sub-agents are separate
  rollout files; find how parent linkage is recorded in `session_meta`/`source`/spawn tool calls (`spawn_agent`, `wait`, ...).
- Claude Code: `~/.claude/projects/<proj>/<session>.jsonl` and sub-agent (sidechain / Task / Agent tool) transcripts
  (check `isSidechain`, `subagents/` dirs, `Agent`/`Task` tool_use + tool_result for the parent link).
- Antigravity: `~/.gemini/antigravity/` (`conversations/*.db` sqlite, `brain/`, `annotations/`) and
  `%APPDATA%/Antigravity/logs`. Investigate what is feasible; best-effort for MVP (at least: active conversation = alive agent,
  recent db mtime = working). Document limits honestly.
- Must tail incrementally (files are up to tens of MB: never re-read whole files; track offsets; only scan recently modified files).
- A session is "alive" if recently written (configurable, ~2 min working window, ~30 min before `gone`).

## Deliverables
Stack: **.NET 10** (installed: dotnet 10.0.400, Windows 11, no Node needed). Keep it small, few files.

```
src/Officelings.Core/        tracker library: providers (Codex/Claude/Antigravity), AgentState model, incremental tailing,
                            state snapshot JSON writer; sprite/office rendering model shared if sensible
src/Officelings.App/         Variant A — STANDALONE WPF app (no Windhawk). Pixel renderer, tooltip, zoom/resize,
                            on-top / desktop-level / click-through, tray icon, settings persisted in %LOCALAPPDATA%\Officelings
src/Officelings.Tracker/     headless console: runs Core tracker, writes %LOCALAPPDATA%\Officelings\state.json (atomic) and
                            atlas.png (sprite sheet) for Variant B
windhawk/officelings.wh.cpp  Variant B — Windhawk mod (C++). Creates the layered/embedded window (WorkerW/desktop level or topmost),
                            reads state.json + atlas.png, renders with GDI/GDI+, same animations, tooltip on hover, zoom/resize.
                            Must follow Windhawk mod format (// ==WindhawkMod== header, settings in ==WindhawkModSettings== YAML).
tests/                      minimal: parser tests against small fixture jsonl snippets (sanitized), no frameworks beyond xunit if needed
README.md                   build/run/install for both variants, limits (lock screen, Antigravity), data sources
```

Sprites: generate procedurally in code (pixel arrays/palettes) — no external art. Nearest-neighbor scaling always.
Animations driven by a tiny state machine + pathfinding on a tile grid (simple BFS) between: door, desks, pantry, TV/sofa, parent desks.

## Rules
- Never write to or modify anything under `~/.codex`, `~/.claude`, `~/.gemini`. Read-only, open files with FileShare.ReadWrite.
- Don't leak secrets/prompts into repo fixtures; sanitize.
- Verify: `dotnet build` and `dotnet test` must pass; run the tracker briefly against the real machine and report agent counts.
- Windhawk mod can't be compiled here by you unless Windhawk's bundled compiler (`C:\Program Files\Windhawk\Compiler`, clang) works — try it;
  if not, say so clearly.
