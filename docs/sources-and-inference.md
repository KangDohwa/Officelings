# Sources and inference

The original source investigation was read-only on 2026-10-03. The table below follows the current implementation reviewed on 2026-10-04, including later Antigravity work. Fixtures use invented text; real prompts and transcripts must stay out of commits.

## Providers

| Provider | Source | Interpretation |
| --- | --- | --- |
| Codex | `~/.codex/sessions/YYYY/MM/DD/rollout-*.jsonl` | `session_meta.id`, `cwd`, top-level `parent_thread_id` or `source.subagent.thread_spawn.parent_thread_id`, nicknames and roles; `turn_context.model`; function/custom tool calls, reasoning, user messages, final-answer phases and task completion. |
| Claude Code | `~/.claude/projects/<project>/<session>.jsonl` | `sessionId`, `cwd`, `message.model`, thinking/text/tool_use/tool_result and `stop_reason:end_turn`; `last-prompt` supplements the tooltip. Sidechain/`agentId` and `<session>/subagents/agent-<id>.jsonl` identify workers and parents. `parentUuid` links messages, not sessions. Unidentified inline sidechains are skipped. |
| Antigravity | Under `~/.gemini/antigravity/` and `~/.gemini/antigravity-cli/`: `conversation_summaries.db`, `conversations/<id>.db` and `brain/<id>/.system_generated/logs/transcript_full.jsonl` (fallback `transcript.jsonl`). Nonempty WAL files also contribute modification time. | Summary rows provide title, workspace, parent, nesting depth and saved status. Selected `steps` columns refine activity. JSONL `USER_INPUT`, `PLANNER_RESPONSE.tool_calls`, `GENERIC`, `status` and `created_at` supply semantic events. DB/WAL time is a fallback when explicit state is absent. |

With the real user profile selected, Antigravity also queries its running local language server. It locates the HTTP port in `%APPDATA%\Antigravity\logs\language_server.log`, reads local app configuration, and requests `GetAllCascadeTrajectories` at `127.0.0.1` about every five seconds. Proxy use and redirects are disabled. The local CSRF token stays in memory and is omitted from diagnostics. Loaded conversation metadata can keep an idle session alive; a conversation removed from a successful loaded-list result is marked Gone. An alternate `--home` disables this probe.

Antigravity summaries expect database `user_version` 3, and step queries expect version 1. The reader copies each DB and its available WAL/SHM/journal files to a disposable `%TEMP%\Vibedotcom-sqlite-*` directory before querying with Windows `winsqlite3.dll`. Only the copies acquire SQLite locks or sidecars. Copies are checked for stable file metadata and integrity, but sequential copies are not an atomic backup. Failures keep cached metadata or use transcript/time fallback. Opaque step payload blobs are not decoded.

Codex's `session_index.jsonl` and `state_5.sqlite` were considered in the investigation; its database has `threads` and `thread_spawn_edges`. Codex tracking still uses rollout metadata directly. Antigravity's investigated schema includes `trajectory_meta`, `steps` and opaque blobs; `annotations/` contains annotation files. General application/language-server traffic is not a per-conversation activity signal. The current runtime reads the language-server log only to discover its local API port. The earlier investigation used a read-only immutable SQLite URI without creating source WAL/SHM files. See the dated [Antigravity notes](antigravity-notes.md) for that investigation.

## Discovery and tailing

Transcript opens use `FileAccess.Read` and `FileShare.ReadWrite | FileShare.Delete`. Provider files, credentials and settings are not changed. Activity comes from source events and Antigravity status observations; the language-server process-name check only helps discover its API.

File watchers queue changed sources. The processing interval defaults to three seconds. A full discovery sweep runs at startup, roughly every minute, or after watcher overflow/new roots. Recent and tracked files are candidates. Tailing checks file identity and bounded content probes so rewrites can be detected even when length is unchanged.

For large JSONL logs, cold start reads a prefix and suffix of at most 512 KiB each. Later reads continue from byte offsets, retain partial UTF-8 records and recover from truncation or replacement. Records over 2 MiB are skipped with a warning. Up to 8 MiB of appended data is processed per source per scan. A large metadata or prompt record across the bootstrap gap may be unavailable; older history in that gap is not reconstructed.

## Activity and animation

| Rule | Default |
| --- | --- |
| No new activity while Working/Thinking | Idle after 120 seconds. |
| Explicit Waiting | Remains Waiting until a new event or the gone threshold. |
| No liveness | Gone after 30 minutes. Successful Antigravity loaded-list observations can refresh liveness. |
| Worker completes | Reporting for 12 seconds, then Idle. Visual pickup/carry/handover may take longer. |
| Gone retention | The character walks out and fades, stays invisible, then is removed after 60 seconds in Gone state. |

The office uses fixed furnished desks rather than adding desk rows per agent. Core assigns existing seats and handles overflow/queues. An agent ID hash selects a roster design and probes unused eligible designs while available. Supervisors and workers use role variants; legacy art distinguishes ties and plain shirts, while newer designs retain their own role markers.

A* avoids wall/void tiles and furniture collision cells. Activity time starts after arrival. Breaks include coffee, silent TV, phone scrolling, sofa naps, paired chatting and water. Missing parents fall back to the worker's own desk vicinity.

## Interpretation limits

Log events can lag computation or approvals. Tool/result transitions suggest Working/Thinking; explicit input requests and turn completion suggest Waiting. These are observations, not control of the tools' UI. Detached workers without a source cannot be discovered.

The original investigation had eight real Claude files and no subagent transcripts. Worker paths and sidechains were checked with synthetic fixtures rather than a live worker on that machine. Agent/Task calls show a bubble but do not create a worker before its transcript appears. Historical or forked sidechains can be ambiguous.

Antigravity remains `bestEffort:true`, including database and local-API observations. Checkpoint/WAL writes can look like work when semantic state is absent. SHM, empty WAL, annotation writes and general application traffic do not count as activity. Unknown transcript locations or database versions reduce available detail; DB-time-only tracking may remain. Model, project, role and parent relationships stay unknown when the available metadata does not supply them.

See [limits and verification](limits.md) for test scope and [privacy](privacy.md) for where the resulting text appears.
