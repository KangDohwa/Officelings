# Sources and inference

The original source investigation was read-only on 2026-10-03. The table below follows the current implementation reviewed on 2026-10-04, including later Antigravity work. Fixtures use invented text; real prompts and transcripts must stay out of commits.

## Providers

| Provider | Source | Interpretation |
| --- | --- | --- |
| Codex | `~/.codex/sessions/YYYY/MM/DD/rollout-*.jsonl` | `session_meta.id`, `cwd`, top-level `parent_thread_id` or `source.subagent.thread_spawn.parent_thread_id`, nicknames and roles; `turn_context.model`; function/custom tool calls, reasoning, user messages, final-answer phases and task completion. |
| Claude Code | `~/.claude/projects/<project>/<session>.jsonl` | `sessionId`, `cwd`, `message.model`, thinking/text/tool_use/tool_result and `stop_reason:end_turn`; `last-prompt` supplements the tooltip. Sidechain/`agentId` and `<session>/subagents/agent-<id>.jsonl` identify workers and parents. `parentUuid` links messages, not sessions. Unidentified inline sidechains are skipped. |
| Antigravity | Under `~/.gemini/antigravity/` and `~/.gemini/antigravity-cli/`: `conversation_summaries.db`, `conversations/<id>.db` and `brain/<id>/.system_generated/logs/transcript_full.jsonl` (fallback `transcript.jsonl`). Nonempty WAL files also contribute modification time. | Summary rows provide title, workspace, parent, nesting depth and saved status. Selected `steps` columns refine activity. JSONL `USER_INPUT`, `PLANNER_RESPONSE.tool_calls`, `GENERIC`, `status` and `created_at` supply semantic events. DB/WAL time is a fallback when explicit state is absent. |

With the real user profile selected, Antigravity also queries its running local language server. It locates the HTTP port in `%APPDATA%\Antigravity\logs\language_server.log`, reads local app configuration, and requests `GetAllCascadeTrajectories` at `127.0.0.1` about every five seconds. Proxy use and redirects are disabled. The local CSRF token stays in memory and is omitted from diagnostics. Repeated loaded Idle observations do not restart the activity clock. A conversation removed from a successful loaded-list result is marked Gone. An alternate `--home` disables this probe.

Antigravity summaries expect database `user_version` 3, and step queries expect version 1. The reader copies each DB and its available WAL/SHM/journal files to a disposable `%TEMP%\Vibedotcom-sqlite-*` directory before querying with Windows `winsqlite3.dll`. Only the copies acquire SQLite locks or sidecars. Copies are checked for stable file metadata and integrity, but sequential copies are not an atomic backup. Failures keep cached metadata or use transcript/time fallback. Opaque step payload blobs are not decoded.

Codex activity and lineage still come from rollout metadata; its archive state also comes from a disposable copy of `state_5.sqlite`. Antigravity's investigated schema includes `trajectory_meta`, `steps` and opaque blobs; `annotations/` contains annotation files. General application/language-server traffic is not a per-conversation activity signal. The current runtime reads the language-server log only to discover its local API port. See the dated [Antigravity notes](antigravity-notes.md) for the earlier investigation.

## Closed sessions

Checked read-only on 2026-10-04. None of these readers writes to `~/.codex`, `~/.claude` or `~/.gemini`. SQLite is opened only after copying the database and available sidecars to a disposable directory. Copy failures or unknown schemas leave time-based departure available.

**Claude Code:** The inspected `~/.claude/sessions/<pid>.json` files contain `sessionId`, `pid`, `pidDomain` and `procStart`. `sessionId` links to the project transcript and its workers. Here, `pidDomain` is `win32:<computer name>` and `procStart` exactly matches the process's Windows FILETIME start value. Only registrations for this computer's Windows PID domain are checked. A missing process, or a different start value at the same PID, starts a ten-second grace period; after it expires, the mapped session and workers leave. A live process cancels the grace. An observed mapping is retained when its registry file disappears. Missing registrations, foreign/unknown PID domains and access-denied checks use the activity thresholds. A running PID alone cannot reveal a session switch inside that process. The inspected version was 2.1.288, not a documented registry contract. No peer-token field was present in those files; tokens are neither read nor used to contact a peer.

**Codex:** The inspected `state_5.sqlite` `threads` table has `archived` and `archived_at`, but no general `closed` flag. `archived = 1` closes a tracked thread immediately on the next scan. Moving its tracked rollout from `sessions/` into `archived_sessions/` also closes it when the same filename is present there. Returning it to active storage/unarchiving clears that observation; new activity can bring it back. Recorded `session_end`, `session_closed`, `shutdown` or `shutdown_complete` events also close it. These event names are supported when present, not promised to appear on every CLI/Desktop exit. Final answers and `task_complete` mean the work finished, not that the session closed.

`session_index.jsonl` had `id`, `thread_name` and `updated_at`; it does not establish closure. `thread_source` describes origin, not whether a thread is open. A Desktop app-server may serve several threads, so its process lifetime is not used as a per-thread signal. The [official app-server documentation](https://learn.chatgpt.com/docs/app-server) distinguishes archive from unload/`thread/closed`. Those notifications live on the app-server transport; this tracker does not subscribe to it, and closing a tab or exiting the CLI may leave no durable marker. Such sessions leave after the idle or waiting timeout.

**Antigravity:** The existing saved `killed` marker and removal from a successful local loaded-conversation result trigger departure. `killed` wins over running/idle step heuristics. The inspected summary databases had Idle or empty status values and no separate closed column. Loaded Idle is not ongoing work. An unavailable language server or a failed request cannot prove that a particular conversation closed: the server can cover multiple conversations, and transcript `DONE` marks completion rather than closure. Unknown saved/transcript state falls back to available activity times. This provider remains best effort.

Claude PID results are cached within each source scan (three seconds by default), with one lookup per registered process and no process enumeration. A deadline can expire between scans without another lookup. The app runs provider scans, database copies and liveness checks on its tracker worker, away from the UI thread. Antigravity's existing runtime probe is limited to its five-second polling cadence.

## Discovery and tailing

Transcript opens use `FileAccess.Read` and `FileShare.ReadWrite | FileShare.Delete`. Provider files, credentials and settings are not changed. Activity comes from source events and Antigravity status observations; the language-server process-name check only helps discover its API.

File watchers queue changed sources. The processing interval defaults to three seconds. A full discovery sweep runs at startup, roughly every minute, or after watcher overflow/new roots. Recent and tracked files are candidates. Tailing checks file identity and bounded content probes so rewrites can be detected even when length is unchanged.

For large JSONL logs, cold start reads a prefix and suffix of at most 512 KiB each. Later reads continue from byte offsets, retain partial UTF-8 records and recover from truncation or replacement. Records over 2 MiB are skipped with a warning. Up to 8 MiB of appended data is processed per source per scan. A large metadata or prompt record across the bootstrap gap may be unavailable; older history in that gap is not reconstructed.

## Activity and animation

| Rule | Default |
| --- | --- |
| No new activity while Working/Thinking | Idle after 120 seconds. |
| Finished or otherwise inactive | Gone five minutes after the last real activity event. Idle breaks continue until then. |
| Explicit Waiting for input/approval | Waiting until new activity or 30 minutes after the last activity event. |
| Parent with an active tracked child/descendant | Stays while the descendant is Working, Thinking, Reporting or explicitly Waiting. Reliable closure still takes precedence. |
| Closed session | Gone on the next source scan; Claude missing-PID detection includes a ten-second grace. |
| Worker completes | Reporting for 12 seconds, then Idle. Visual pickup/carry/handover may take longer. |
| Gone retention | The character walks out and fades, stays invisible, then is removed after 60 seconds in Gone state. |

Completion makes a supervisor Idle and a worker Reporting, then Idle. Both use the five-minute threshold. File maintenance, metadata-only records and repeated loaded-idle observations do not extend the clock. When no semantic event is available, the provider's file/database activity time is a best-effort fallback. `workingSeconds`, `leaveAfterIdleMinutes` and `leaveAfterWaitingMinutes` configure these windows. The deprecated `goneMinutes` setting/CLI option maps only to the waiting window; an explicit waiting value wins.

The office uses fixed furnished desks rather than adding desk rows per agent. Core assigns existing seats and handles overflow/queues. An agent ID hash selects a roster design and probes unused eligible designs while available. Supervisors and workers use role variants; legacy art distinguishes ties and plain shirts, while newer designs retain their own role markers.

A* avoids wall/void tiles and furniture collision cells. Break duration starts after arrival. Breaks include coffee, silent TV, phone scrolling, sofa naps, paired chatting and water. Missing parents fall back to the worker's own desk vicinity. Departure reroutes from the exact current position, finishes the current partial tile edge, walks to the door and uses the existing fade there. New activity during that walk cancels departure from the current position; after the character has disappeared, it enters through the door.

## Interpretation limits

Log events can lag computation or approvals. Tool/result transitions suggest Working/Thinking; structured input/approval requests suggest Waiting. A final response alone is completion, so an unstructured question in plain text may use the shorter idle timeout. These are observations, not control of the tools' UI. Detached workers without a source cannot be discovered.

The original investigation had eight real Claude files and no subagent transcripts. Worker paths and sidechains were checked with synthetic fixtures rather than a live worker on that machine. Agent/Task calls show a bubble but do not create a worker before its transcript appears. Historical or forked sidechains can be ambiguous.

Antigravity remains `bestEffort:true`, including database and local-API observations. Checkpoint/WAL writes can look like work when semantic state is absent. SHM, empty WAL, annotation writes and general application traffic do not count as activity. Unknown transcript locations or database versions reduce available detail; DB-time-only tracking may remain. Model, project, role and parent relationships stay unknown when the available metadata does not supply them.

See [limits and verification](limits.md) for test scope and [privacy](privacy.md) for where the resulting text appears.
