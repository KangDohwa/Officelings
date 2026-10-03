# Synthetic sessions and end-to-end QA

The simulator gives the tracker a pretend home containing invented Codex, Claude Code and Antigravity sessions. It never reads your real sessions. Prompts, model names and projects are made up; project paths start with `C:\sim\project-`. All output must stay below `%TEMP%`, and symlinks/junctions and real provider/application directories are refused.

Run these commands from the repository root. This Python environment is already available on the development machine; the scripts use only the standard library.

```powershell
$py = 'C:\Users\myori\.codex\skills\sprite-gen\.venv\Scripts\python.exe'
& $py tools/simulate/simulate.py --scenario quiet --seed 1
# In another terminal:
dotnet run --project src/Vibedotcom.Tracker -- --home "$env:TEMP\vibedotcom-sim-home" --output "$env:TEMP\vibedotcom-sim-output"
```

The default fake home is `%TEMP%\vibedotcom-sim-home`. Use `--home` for a separate directory below TEMP. A nonempty directory is refused. `--reset` replaces only a home carrying the simulator's ownership marker; it cannot clear an unrelated folder.

## Scenarios

Each scenario starts one or more supervisors for every provider. Times in the JSON are simulated seconds. `--speed 10` runs ten simulated seconds per real second. `--seed` repeats session IDs and configured event ordering; heartbeat count and message IDs can vary with startup delays. Timestamps use the current UTC clock so the tracker sees fresh writes.

| Scenario | What happens | Simulated duration |
| --- | --- | --- |
| `quiet` | Three supervisors wait, complete, expire and resume with the same IDs. | 320 s |
| `busy-12` | Twelve supervisors, three workers, thinking, input requests, a tool failure and returning workers. | 330 s |
| `burst-spawn` | Two bursts reach 45 live agents, then completion and expiry. | 320 s |
| `flaky-writer` | A UTF-8 character split mid-write, a 3 MiB line, malformed JSON, truncation, same-path rotation, a crashed writer and resume. | 620 s |
| `long-soak` | Twelve supervisors and repeated worker churn, with resource measurements in QA. | 6000 s |

```powershell
& $py tools/simulate/simulate.py --scenario quiet --speed 10 --seed 1 --reset
& $py tools/simulate/simulate.py --scenario busy-12 --speed 10 --seed 1 --reset
& $py tools/simulate/simulate.py --scenario burst-spawn --speed 10 --seed 1 --reset
& $py tools/simulate/simulate.py --scenario flaky-writer --speed 10 --seed 1 --reset
& $py tools/simulate/simulate.py --scenario long-soak --speed 20 --seed 1 --reset
```

The scenarios use a 40-second working timeout and a 160-second gone timeout, except `flaky-writer`, whose gone timeout is 400 seconds to leave room between crash-to-idle and expiry. A hand-run tracker needs corresponding flags to reproduce expiry checkpoints. At speed 10, add `--working-seconds 4 --gone-minutes 0.266666667 --scan-seconds 0.25` (use `--gone-minutes 0.666666667` for flaky-writer). The QA script supplies these flags automatically. The core's reporting window and gone retention are not configurable by CLI and remain 12 and 60 real seconds. Counts mean live semantic agents (`state != gone`), including completed agents waiting for their gone timeout; they do not count only visible sprites.

## Run the QA harness

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/qa/e2e.ps1
# A short reproduction:
powershell -NoProfile -ExecutionPolicy Bypass -File tools/qa/e2e.ps1 -Scenario flaky-writer -Speed 20 -Seed 1
# The soak alone (five minutes of timeline, hard wall-clock cap of ten minutes):
powershell -NoProfile -ExecutionPolicy Bypass -File tools/qa/e2e.ps1 -Scenario long-soak -Speed 20 -SoakCapSeconds 600
# Reuse an existing compiled test exe without writing to windhawk/:
powershell -NoProfile -ExecutionPolicy Bypass -File tools/qa/e2e.ps1 -RendererTests "$PWD\windhawk\artifacts\renderer_tests.exe"
# If the SDK run launcher stalls, use the same freshly built tracker apphost:
powershell -NoProfile -ExecutionPolicy Bypass -File tools/qa/e2e.ps1 -DirectTracker -RendererTests "$PWD\windhawk\artifacts\renderer_tests.exe"
# Small checks for the simulator and the QA rejection paths:
& $py -B tests/e2e/test_simulator.py
```

By default QA runs all five scenarios at speed 20. It pins HEAD, makes a detached git worktree inside its TEMP run directory and builds the tracker once there. Your uncommitted changes are excluded. It runs `dotnet run --no-build --no-restore --project ... -- --home ... --output ...`; provider files and all application-data paths are isolated. The Windhawk `compile.ps1 -Tests` build also happens only inside that worktree. The compiled renderer exe is invoked offscreen; its installed runtime libraries are copied into the disposable build directory. `-RendererTests` instead invokes an existing compiled exe read-only, with no rebuild or copies into its directory; that directory must already contain its runtime libraries. The table records which exe was used. There is no Explorer injection, foreground change or input automation. The worktree is removed in `finally`; synthetic homes, logs and captures remain for inspection.

At each checkpoint the simulator pauses ordinary writes and waits for a QA acknowledgement. Startup keeps supervisors fresh while builds/launches settle. A malformed-line checkpoint repeats bad lines until QA observes the transient diagnostic; identities and intended states stay fixed. Checkpoint messages are immutable files, so the QA control channel does not repeat the product's replacement failure described below. QA allows up to two seconds for scanning/publishing to settle. It compares the independently specified provider counts and each session's ID, parent and worker flag; checks explicit state expectations; and rejects agents still working after a stopped writer's working timeout plus 0.5 seconds. It validates checkpoint/final JSON, version against HEAD's `docs/state-schema.md`, required metadata types, timestamps, finite coordinates, map/frame bounds and relative sheet paths. Exported PNG hashes are checked at startup. This is a stdlib implementation of the documented contract, not a third-party JSON Schema engine. Unknown additive fields are allowed.

The first scenario also tests an ordinary live reader opened with the documented `FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE`, held across three publication intervals. Tracker termination or failure to advance the sequence after releasing the reader is a **FAIL**, with its own log. On this machine a native Win32 `MoveFileExW` cannot replace an open destination; an earlier tracker exited, while the newer tracker retries (see the findings below). To complete the other checks independently, QA briefly suspends only its own tracker process while copying each checkpoint, then resumes it in `finally`. It does not read continuously between checkpoints. Recorded pause time is subtracted from the CPU denominator. Any failing live-reader check remains in the final table and makes the whole run exit nonzero. Guarded captures validate session tracking and rendering; the separate live-reader probe validates overlap recovery.

The current WPF `Program.cs` has no `--home`: its smoke path creates `AgentTracker()` using the real home. QA prints a **SKIP** for fake-home WPF smoke. It does not run that unsafe source selection. If a later HEAD implements `--home`, QA detects the flag and runs the offscreen smoke with the fake home and isolated application data.

The default tracker launch uses `dotnet run` with build servers disabled. `-DirectTracker` is an explicit fallback for a stalled SDK launcher: it runs `Vibedotcom.Tracker.exe` from the freshly built TEMP worktree with the same arguments. The table and `run.json` record the bypass. This still tests the tracker but does not verify the SDK launcher itself.

During the soak QA samples the actual tracker apphost every five seconds, rather than the `dotnet run` launcher. CPU average after a 20-second warmup must be at most 3% of **one logical core**; the calculation is CPU seconds divided by active wall seconds (excluding checkpoint suspension), without dividing by the machine's core count. RSS must never exceed 200 MiB in the samples. A read-only Windows system-handle query identifies File objects using a temporary anchor handle: the last three steady samples' median must not exceed the first three. Transient peaks are reported separately, not mistaken for growth. Total process handles are also recorded. An unavailable metric is a failure. This can reveal sustained leaks, but five-second sampling cannot catch every brief peak. The accelerated scan interval and churn are part of the measured workload.

Results live in `%TEMP%\vibedotcom-e2e\<timestamp>-<random>\`. `results.json` and `failures.json` accompany the printed table. Each scenario keeps its JSON, event journal, simulator/tracker logs, checkpoint intent and actual snapshots, observations, renderer PNGs and `samples.csv`. A failed check exits nonzero while other scenarios continue. Build/environment failures are distinguished by their log and check name. These tools do not fix product source or commit anything.

## Add a scenario

Copy one of `tools/simulate/scenarios/*.json` and give it a new `name`. Version 1 requires `duration`, `supervisors`, `events` and `checkpoints`. A minimal example:

```json
{
  "version": 1,
  "name": "my-scene",
  "duration": 100,
  "supervisors": {"codex": 1, "claude": 1, "antigravity": 1},
  "events": [
    {"at": 10, "action": "spawn", "index": 0, "count": 2},
    {"at": 50, "action": "end", "target": "workers"}
  ],
  "checkpoints": [
    {"at": 30, "label": "two workers each", "counts": {"codex": 3, "claude": 3, "antigravity": 3}}
  ]
}
```

Run it with `--scenario path/to/my-scene.json`, or pass that path to `e2e.ps1 -Scenario`. Events default to all providers and supervisor targets. `provider` can be `codex`, `claude`, `antigravity` or `all`; `target` can be `supervisors`, `workers` or `all`; `index` selects a supervisor's zero-based index (workers receive stable creation indexes). `spawn` creates `count` workers for each selected supervisor. `every` and `until` repeat an event. Ended sessions ignore later events until `resume` or `user`, so later worker churn does not rewrite old transcripts.

Actions are `user`, `resume`, `thinking`, `working`, `waiting`, `result`, `error`, `finish`, `end`, `idle`, `silent_end`, `partial`, `huge`, `malformed`, `truncate` and `rotate`. `result` and `error` write a tool result for the latest tool call. `finish` writes a final response; `end` writes it and stops further heartbeats; `silent_end` stops without a terminal record. Both normal endings and crashes disappear through the provider's real inactivity timeout. `partial.delay` controls when the remainder is appended; `huge.bytes` controls padding size. Rotation replaces the same transcript path and keeps the old file under a non-JSONL extension. It preserves the session ID.

`heartbeat_seconds` defaults to 10; active thinking/working/waiting sessions stay fresh, while idle/finished/ended sessions stop writing. Set `working_seconds` and `gone_seconds` to change the scaled QA timeouts. Write checkpoint `counts` yourself for all three providers. Optional `states` lists allowed live states for every agent at that checkpoint; `warning_contains` checks collected diagnostics. Leave enough room around timeout boundaries for real scan latency. Do not put real prompts, real project paths or credentials in scenario files. The writer's text is fixed and invented; scenarios control timing and populations.

## Screenshots and GIFs

QA saves offscreen renderer captures from the peak live snapshot in each scenario, together with the original state and exported sheets. Use those files when preparing screenshots or assembling a GIF. The default WPF app cannot consume this fake home yet; use these Windhawk renderer captures or the app's separate fabricated `--demo` mode. Check the chosen capture came from the TEMP QA run. Its prompts and paths are invented, so it is safe to show the tooltip or provider details without exposing a real conversation. The simulator itself does not capture the desktop or move the cursor.

## Format limits

Codex writes rollout metadata, turn context, reasoning, function/custom tool calls, spawn results with child rollout parent linkage, and task completion. Claude writes user/assistant messages, tool use/results, thinking and end-turn records. Workers are sidechains under `<parent-session>/subagents/agent-<id>.jsonl`; `parentUuid` links messages and is never used as a session parent. No live Claude worker was present in the inspected files, so worker linkage follows the provider/fixture contract rather than a claimed observed live worker.

Antigravity uses the observed `user_version=1` conversation tables and `user_version=3` summary table, plus `brain/<id>/.system_generated/logs/transcript_full.jsonl`. SQLite blobs are empty; the provider reads scalar status fields and summary parent/workspace metadata. This models saved-file tracking, not the language-server API, protobuf contents or every possible WAL/checkpoint race. The flaky-writer mutations apply to transcripts. SQLite changes are ordinary committed transactions.

## Findings from the first QA runs

1. **Soak CPU exceeds the requested limit.** HEAD `4293d5c` completed all soak population/parent/lifecycle checkpoints at speed 20. The measured average was 12.265% of one logical core, above 3%; peak sampled RSS was 92.5 MiB. File-handle medians were 41→41, with a transient peak of 51, so these samples do not show a handle leak. The earlier peak-cap check incorrectly treated that transient as growth; current QA checks median growth and still reports the peak. Run the command below to reproduce the workload; machine load and timing affect the numbers.

   ```powershell
   powershell -NoProfile -ExecutionPolicy Bypass -File tools/qa/e2e.ps1 -Scenario long-soak -Speed 20 -SoakCapSeconds 600 -DirectTracker -RendererTests "$PWD\windhawk\artifacts\renderer_tests.exe"
   ```

2. **Earlier publisher crash, subsequently fixed by another worker.** On HEAD `8fd93fa`, the tracker exited with `Access to the path is denied.` when a conforming shared reader overlapped `StateJson.WriteAtomic`'s destination replacement. A separate native Win32 probe reproduced error 5 while the destination was open. By HEAD `1b9531c`, another worker had added `PublishWithRetryAsync`; the live-reader probe survived on that HEAD. The QA still checks survival and sequence recovery. Reproduction on an old checkout uses the command below; current HEAD should pass that row. The simulator/QA work did not edit product source. Microsoft describes the relevant file-operation constraints in [Moving and replacing files](https://learn.microsoft.com/en-us/windows/win32/fileio/moving-and-replacing-files).

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/qa/e2e.ps1 -Scenario quiet -Speed 20 -RendererTests "$PWD\windhawk\artifacts\renderer_tests.exe"
```

   Look for `live shared-reader publisher` and `tracker-reader-probe.log`. The small test suite retains the raw Windows replacement limitation as an explicit expected failure; it is not counted as a passing regression. The e2e tracker publisher check is never marked expected or suppressed.

3. **SDK launch stalled during this session.** Some `dotnet run --no-build --once` invocations exceeded 60 seconds or stalled on startup. The same compiled apphost completed the direct probe in about six seconds. This is recorded as an execution/environment limitation, not asserted to be a tracker parsing defect. Use `-DirectTracker` to isolate core behavior.

Validation on 2026-10-04 used split reruns after correcting simulator/control timing. Quiet and burst-spawn passed on HEAD `1b9531c`; busy-12 and flaky-writer then passed all checkpoints and renderer checks on the same HEAD. The soak on `4293d5c` collected 62 five-second samples, ending at 340.55 real seconds including startup; every soak lifecycle/schema checkpoint passed, with CPU alone above the requested resource limit. The small suite reported nine passes and one explicit expected Windows replacement failure. WPF fake-home smoke was skipped because `--home` is absent.

The retained logs are under `%TEMP%\vibedotcom-e2e\20261004-032540-29ck4iwm` (quiet/burst), `20261004-033302-u7e1mg3o` (final busy/flaky), and `20261004-031355-2m8yz6ql` (soak CSV and `resource-recheck.json`). Older failures in these folders are preserved rather than rewritten as passes. Every run's temporary HEAD worktree was removed; unrelated worktrees were left alone.
