**Performance measurement — committed `280e7a61b0d3c049de75967892debac15627fbcc`**

The results in this first section describe an older build. Current-source profiling, changes and validation are recorded in **After optimization** at the end of this report. CPU always means percent of **one core**, `100 * process CPU seconds / wall seconds`; whole-machine percentages are separately divided by the logical processor count. Earlier “about 1% idle” whole-machine claims on this 18-thread machine meant roughly 18–27% of one core.

Measured on 2026-10-04 KST with Release assemblies built in the detached `%TEMP%/vd-perf` worktree. Build: zero warnings/errors. Runtime: .NET 10.0.11, SDK 10.0.400, Windows NT 10.0.26200, 18 logical processors available to the process. Hardware inventory was denied by the sandbox. Other agents remained active; these are measurements of this constrained, busy environment, not a quiet reference machine.

The rendering, pack and tracker measurements completed. **The requested twelve-condition, ten-minute idle soak did not complete within the approximately forty-minute work budget. There is no ten-minute leak verdict.** Failed startup attempts and short samples are retained, rather than presented as successful soaks. Native desktop hosting was not attempted: committed `WindowModes.EmbedDesktop` sends Explorer's private wallpaper messages, contrary to the requirement not to disturb the shell.

No input, cursor movement, minimization or foreground-changing API was used. Every measurement window was owned, offscreen and nonactivating. `GetForegroundWindow` returned zero in the sandbox, so equality of those readings does not independently prove the foreground was unchanged. Product sources were not modified; personal settings and the real `%LOCALAPPDATA%/Vibedotcom` were not read or written. Assets came from committed embedded resources; tracker homes and brand paths were explicitly temporary/empty. Merely setting environment variables is not asserted to redirect Windows known folders.

**Idle and soak coverage**

The committed CLI has a fixed demo: three leads and up to six workers, with arrivals/departures; it has no count/scenario-file option. `--profile-idle` is a twenty-second, six-agent offscreen test. `--qa` limits CLI runs to sixty seconds and forces normal mode. Consequently the measurement harness calls the unchanged built `OfficeWindow`, `OfficeView` and Core assemblies through the existing friend-assembly access; it is not a rebuilt or patched app.

The harness injects 0/6/12/24 fabricated idle agents, retains QA's no-save/no-tray/no-hover behavior, substitutes a tracker with an explicit empty home, and polls that home once per second without replacing the injected agents. It uses the committed owned-window verification override for native normal/topmost/transparent behavior. Windows are borderless, 720×480, 1x, at (-30000,-30000). Topmost and transparency were checked from native style bits and `AllowsTransparency`. This measures app logic and offscreen window behavior, not visible desktop presentation or compositor cost. Idle characters can still walk, blink and visit leisure zones; the first minute includes settling.

The initial `Get-Counter` collector incurred delays of tens of seconds; its partial CSVs are excluded from idle conclusions. The replacement opens one PDH query, binds Process counters to owned PIDs, and reuses it alongside `Get-Process` and `GetGuiResources`. Its short zero-agent validation sampled at 5.060 and 10.115 seconds with matching private bytes and thread counts. CPU percentages are one-core equivalents; divide by 18 for whole-machine percentages. RSS means Windows working set; private bytes are separately reported.

Repeated starts failed with `InvalidDataException: Layout navigation exceeded its work/time budget`, including a sequential start. The stack identifies `OfficeView.cs:19` creating its default `OfficeAnimator`, whose built-in layout traversal has a 50 ms wall-clock limit (`LayoutLoader.cs:156`). The final harness retries only this constructor rejection, records the retry count and keeps all product assemblies unchanged. These retries are a harness accommodation, not a claim that stock CLI startup succeeds. Final coverage and any available samples are appended below. Median/p95/max use sorted samples and nearest-rank p95; drift is least-squares slope against actual elapsed minutes. Short-run slopes cannot establish or exclude leaks.

**Accepted short idle samples — not ten-minute soaks**

All twelve owned windows eventually became ready. The common sampling epoch began only after prolonged startup, so their ages differed substantially. Sampling was stopped at the work budget. The first nine scheduled samples (approximately 5 to 45 seconds) precede shutdown and are used below; later raw rows overlap closure, including a zero working-set row, and are excluded explicitly. Every window decoded 96 sheets; 4 final starts needed 4 layout-budget retries in total. These results are a short concurrent observation, not a controlled ten-minute comparison.

Each triple is median/p95/max. CPU is % of one core, memory is MiB, slope is MiB/min. With nine samples, nearest-rank p95 equals max. Slopes over about forty seconds are dominated by cache/GC/working-set changes and are not leak evidence. GDI/USER/thread counts are OS observations, not counts of individual WPF objects.

| Mode / agents | n / last sample s | CPU % | Working set MiB | Private MiB | WS/private slope | GDI | USER | Threads |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| normal/0 | 9/45.2 | 19.2/24.8/24.8 | 29.8/36.1/36.1 | 231.8/231.8/231.8 | -0.44/0.03 | 16/16/16 | 24/24/24 | 33/34/34 |
| normal/6 | 9/45.2 | 36.8/59.3/59.3 | 41.7/44.8/44.8 | 235.8/235.8/235.8 | -5.21/-0.05 | 16/16/16 | 24/24/24 | 33/33/33 |
| normal/12 | 9/45.2 | 44.6/71.9/71.9 | 45.0/52.5/52.5 | 246.7/246.8/246.8 | -9.32/-0.06 | 16/16/16 | 24/24/24 | 33/34/34 |
| normal/24 | 9/45.2 | 52.3/87.4/87.4 | 46.6/52.9/52.9 | 245.6/245.7/245.7 | -20.65/-0.02 | 16/17/17 | 25/25/25 | 33/34/34 |
| topmost/0 | 9/45.2 | 20.6/28.1/28.1 | 30.6/34.9/34.9 | 221.5/221.5/221.5 | -4.20/0.08 | 16/16/16 | 25/25/25 | 33/33/33 |
| topmost/6 | 9/45.2 | 43.0/68.3/68.3 | 42.3/47.4/47.4 | 234.7/234.7/234.7 | -6.06/-36.49 | 16/16/16 | 24/24/24 | 33/34/34 |
| topmost/12 | 9/45.2 | 47.9/80.8/80.8 | 42.5/58.0/58.0 | 239.7/240.8/240.8 | -7.34/-11.98 | 16/16/16 | 26/26/26 | 35/37/37 |
| topmost/24 | 9/45.2 | 51.2/93.4/93.4 | 49.4/56.4/56.4 | 249.1/249.2/249.2 | 2.96/0.11 | 16/16/16 | 25/25/25 | 37/38/38 |
| transparent/0 | 9/45.2 | 23.4/33.8/33.8 | 37.4/43.4/43.4 | 221.2/222.0/222.0 | 0.05/-0.26 | 18/18/18 | 26/26/26 | 36/38/38 |
| transparent/6 | 9/45.2 | 67.1/98.5/98.5 | 48.1/50.4/50.4 | 243.2/243.4/243.4 | 15.92/-0.04 | 18/18/18 | 25/26/26 | 34/36/36 |
| transparent/12 | 9/45.2 | 84.5/142.2/142.2 | 57.3/65.2/65.2 | 243.8/249.0/249.0 | 18.84/2.23 | 18/18/18 | 25/25/25 | 34/36/36 |
| transparent/24 | 9/45.2 | 79.5/142.8/142.8 | 74.8/85.3/85.3 | 263.7/272.6/272.6 | 2.15/-5.91 | 18/18/18 | 24/25/25 | 34/35/35 |

All accepted rows have PID-matched performance-counter readings. Median absolute CPU-counter/Get-Process differences across conditions ranged 0.44 to 13.29 percentage points of one core; they sample at different instants, and the recorded PDH formatter also applied its default 100% CPU cap. CPU tables therefore use Get-Process, not the capped counter. The reproduction collector now requests PDH_FMT_NOCAP100; the exact recorded collector is retained as results/soak-complete/sampler.ps1. Private bytes and handles remain separate measures. The final empty-home polls read zero payload bytes.
The zero-agent normal process lived 570.3 seconds and still allocated 1.04 MiB/s over its whole lifetime (4370 animation ticks; GC 0/1/2 = 67/7/1). This supports investigating quiet-room update work, but does not attribute those allocations to a particular method. The shortest/latest windows had much less settling time.

Raw accepted/overlapping rows are in `tools/perf/results/soak-complete/*/samples.csv`; readiness, native styles, retry counts, caches and lifetime GC totals are in adjacent JSON files. `results/summary.json` contains the accepted-sample statistics and per-metric slopes. To reproduce this particular truncation, run `analyze.ps1 -MaxSampleSeconds 46`; use the default unlimited analysis only for a genuinely completed run. The ordinary CLI smoke, desktop mode, visible compositor performance and ten-minute drift remain unmeasured. Measurement processes were stopped around the forty-minute budget and the specified `vd-perf` worktree was removed; other agents' worktrees were retained.

**Offscreen rendering: twelve fabricated working agents**

This calls committed `Program.RenderBitmap`: a fresh hidden `HwndSource`, layout/dispatcher flush, new `RenderTargetBitmap`, rendering and freezing per frame. Timing includes `OfficeView.Update`; it excludes `OfficeAnimator.Update`, PNG encoding and fixture setup. All twelve agents were visible. Three frames warmed each zoom; then each zoom was bounded at forty frames or about twenty seconds, with a minimum of eight frames. Allocations are whole-process `GC.GetTotalAllocatedBytes` deltas; GC columns are `GC.CollectionCount` deltas. `dotnet-counters` was not installed. The arrays in `tools/perf/results/render/` retain every measured frame.

| Zoom / bitmap | Frames | Mean ms | Median / p95 / max ms | Managed KiB/frame | GC 0/1/2 |
|---|---:|---:|---:|---:|---:|
| 1x, 592×352 | 40 | 378.4 | 158.0 / 1,590.8 / 3,112.2 | 576.2 | 8/6/6 |
| 2x, 1152×672 | 38 | 558.7 | 279.3 / 2,087.5 / 4,579.1 | 517.8 | 20/18/18 |
| 3x, 1712×992 | 10 | 2,094.4 | 1,355.5 / 5,200.8 / 5,200.8 | 541.4 | 6/6/6 |
| 6x, 3392×1952 | 19 | 1,158.4 | 822.1 / 4,727.0 / 4,727.0 | 537.1 | 20/19/19 |

The high tails, frequent Gen2 collections and non-monotonic 3x/6x results preclude a clean scaling claim. With ten samples, 3x p95 is simply its maximum. These are costs of the smoke renderer, not interactive animation frame times. A reusable-source/bitmap comparison is implemented as the optional `reuse` harness command but was **not run**, to preserve the time budget; no speedup from it is claimed.

**Pack load and first frame**

Three fresh harness processes loaded the embedded pack, constructed an `OfficeView` and rendered a 720×480 first frame using the same committed smoke-render function. Windows/font/file caches were not flushed. This is in-process time to an offscreen first frame; it excludes CLR/process launch. The complete stock `--smoke-test` command was not timed: its default `BrandIcons` path can read the personal Vibedotcom directory even with `--demo`, whereas this harness supplies an empty brand directory. A full smoke command also renders many review scenes and exports files, so its exit time would not be first-frame latency.

| Stage, three runs | Median | Range |
|---|---:|---:|
| Pack plus layout load | 624.8 ms | 618.6–804.8 ms |
| View construction, including eager decode | 859.6 ms | 777.3–1,017.0 ms |
| First `RenderBitmap` call | 3,442.4 ms | 3,196.5–4,131.9 ms |
| In-process start to first frame | 5,208.6 ms | 4,970.7–5,729.7 ms |

There are **92 embedded pack PNG resources, 96 decoded sheet entries**, 1,900,367 bytes (1.81 MiB) of retained compressed image data, and 52,712,640 bytes (**50.27 MiB**) of decoded pixel storage. Some images are generated/fallback entries; resource and decoded-sheet counts are intentionally different. Immediately after decode, working set was 132.64–133.70 MiB and private bytes 98.27–98.59 MiB. Compared with post-pack-load measurements, decode added 73.75–74.19 MiB of private bytes, including WPF initialization and bitmap overhead. The first frame subsequently raised memory further. These are snapshots, not peak or exclusively bitmap memory.

**Tracker: invented JSONL only**

Raw data: `tools/perf/tracker/results/20261003T180544090Z-dc833495/`. One harness process, fresh trackers per scenario, warm OS cache immediately after fixture creation, fixed valid 4096-byte ASCII records, explicit temporary homes. Large-file and archive cases advance logical scan time; they do not claim cold physical-disk performance. Old archive files are thirty days old and excluded from the live-session window. CPU is whole-process time overlapping each Poll, including watcher threads. Short operations can quantize to zero at the Windows CPU timer resolution. RSS carries prior fixture allocations and system trimming.

| Scenario | Poll wall ms | CPU ms | RSS after / observed max MiB | Tracker payload / process read bytes per Poll |
|---|---:|---:|---:|---:|
| 20 MiB, first tail | 45.88 | 46.875 | 44.85 | 1,048,576 / 1,052,929 |
| 200 MiB, first tail | 72.67 | 62.500 | 50.14 | 1,048,576 / 1,052,929 |
| 20 MiB, unchanged scan | 1.20 | 0, quantized | 44.87 | 0 / 8,704 |
| 200 MiB, unchanged scan | 5.82 | 15.625 | 50.36 | 0 / 8,704 |
| 20 MiB, append 64 KiB | 7.96 | 0, quantized | 45.48 | 65,536 / 74,240 |
| 200 MiB, append 64 KiB | 12.18 | 0, quantized | 51.00 | 65,536 / 74,240 |
| 2,000 old files, first discovery | 28.72 | 46.875 | 14.82 | 0 / 0; 2,000 entries visited |
| 2,000 old files, unchanged Poll | 1.39 | 0, quantized | 15.73 | 0 / 0 |

The bounded cold tail reads 1 MiB regardless of 20/200 MiB total length. This verifies bounded startup reading, not reconstruction of omitted history. `AgentTracker.BytesRead` counts payload tail reads but **omits replacement-detection probes**. Process I/O counters include probes and FileStream buffering; they are logical I/O bytes, not physical disk traffic. The two byte columns must not be conflated.

For steady state, 25 Codex and 25 Claude files each gained 4096 bytes at sixty one-second ticks: 12,288,000 appended bytes. Default scan spacing is three seconds. There were 60 measured Polls, **20 actual growing-file scans**, then one drain. Over the growing Polls, wall time median/p95/max was **0.214/787.465/1,916.062 ms**; the median mostly describes no-scan Polls. Actual scans averaged 604,160 payload bytes plus 435,200 probe/buffering bytes: **1,039,360 process-read bytes per scan**, with 250 read operations per scan. The drain read 204,800 payload / 640,000 process bytes.

The growing Poll intervals accumulated 1,343.75 ms CPU. Over the 70.916-second steady interval, that is 1.90% of one core, but excludes CPU outside those intervals. Whole harness CPU was 9,000 ms, **12.69% of one core / 0.705% of machine capacity**, and includes the synthetic writer, watchers and measurement bookkeeping; it is not isolated tracker CPU. RSS during growing Polls ranged 3.20–49.09 MiB, private bytes peaked at 32.30 MiB, managed allocations totaled 48.31 MiB, GC deltas were 6/0/0. Across the complete steady interval GC deltas were 13/1/0. The complete tracker run took 88.690 seconds; all 74 fixture/read-count checks passed, with no unexpected parser warnings.

**Eight concrete findings**

Gains below are estimates or avoided-work counts, not measured improvements. No fix was applied to the app. The lack of a completed soak means none of these is labeled a confirmed leak.

| Priority / before public release? | Evidence | Smallest useful change | Estimated gain |
|---|---|---|---|
| 1 — Yes | Built-in layout startup failed under both simultaneous and sequential starts; 50 ms wall-clock guard at `LayoutLoader.cs:156`; redundant default animator at `OfficeView.cs:19`. | Initialize the view's layout from its supplied animator/layout, and retain a bounded traversal without treating short scheduler stalls as invalid geometry. | One redundant layout preparation per view; removes the observed false-rejection class. Failure-rate improvement needs a fresh startup run. |
| 2 — Yes | 96 sheets eagerly decoded in `OfficeView.cs:63–68`; 50.27 MiB pixel storage; ~74 MiB decode-phase private-byte increase. | Decode a sheet on first use; share frozen decoded sheets for the same immutable pack, including window replacements. | Up to 50.27 MiB of deferred pixel storage, less whatever the first scene uses; avoid duplicate storage per additional view. No fixed percentage asserted. |
| 3 — Yes, remeasure first | `Program.Animate` keeps updating at 8 Hz when not walking; `OfficeView.Update` changes ambient phase twice/second even with no agents. Short zero-agent validation used ~21–22% of one core. | Schedule quiet updates at actual animation/ambient deadlines; skip agent work when the list is empty. | Reducing eligible 8 Hz work to 2 Hz removes 75% of those update calls. Total CPU saving is unmeasured, and active furniture must retain its cadence. |
| 4 — Profile before release | `Office.Update` repeatedly sorts/builds arrays; `RefreshFurniture` rebuilds owner dictionaries, furniture and sprite records at lines 413–433. | Refresh ownership/static furniture only on ownership/state/layout changes; update animated sprite frames separately. | Avoid one dictionary and complete static-furniture projection per eligible tick. Allocation saving needs per-method attribution; process allocation is not proof of this method's share. |
| 5 — No, smoke/tooling path | `Program.RenderBitmap` creates/disposes a hidden HWND and allocates a bitmap every frame; 518–576 KiB managed/frame, many Gen2 collections. | Reuse an owned presentation source and render target when dimensions match. | Avoid one HWND lifecycle and a fresh 0.79–25.26 MiB pixel target per frame for these sizes. Time/GC gains were not measured; live app rendering has a different lifecycle. |
| 6 — Yes for measurement accuracy; optimization after validation | Unchanged files report zero payload but read 8,704 process bytes; fifty files add 435,200 bytes/scan. `JsonlTail.Probe` reads/copies bounded probes. | Expose probe bytes separately; reuse probe buffers/reads while retaining same-size/same-mtime replacement detection. | Correct the current zero-read claim; current probe/buffering overhead is 8.30 MiB across twenty growing scans. Any I/O saving depends on preserving replacement checks. |
| 7 — No | `JsonlTail` appends bytes individually with `MemoryStream.WriteByte`; each cold 1 MiB tail allocates ~2.15 MiB. | Locate newlines in spans and append contiguous line segments; retain partial-line/length/error behavior. | Replace roughly 1,048,576 byte-level append calls with about 256 line-segment appends for these fixtures. JSON parsing remains; overall speedup is unknown. |
| 8 — Yes for reproducible QA | `BrandIcons.cs:17` defaults to personal output storage even for demo/QA; missing icons try three densities per provider and cache failures. This prevented an honestly isolated stock smoke timing. | Route QA/demo brand lookup to the same explicit isolated storage policy as assets/settings. | Remove personal-folder reads from isolated runs and up to nine initial missing-file probes/exceptions at a density. Startup-time saving is unmeasured. |

**Reproduction**

Run from the repository root. These commands measure committed HEAD, leave product sources untouched, and keep build/synthetic files temporary. Each result directory must be new. Do not replace the isolated harness with a bare normal CLI launch: the committed CLI does not isolate brand lookup. The harness assembly name uses the app's existing `InternalsVisibleTo("Vibedotcom.Tests")`, not source copying.

```powershell
$perfWorktree = Join-Path $env:TEMP 'vd-perf'
$perfBuild = Join-Path $env:TEMP ('vd-perf-harness-' + [guid]::NewGuid().ToString('N'))
git worktree add --detach $perfWorktree HEAD
try {
    dotnet build "$perfWorktree/src/Vibedotcom.App/Vibedotcom.App.csproj" -c Release --nologo
    $perfAppBin = "$perfWorktree/src/Vibedotcom.App/bin/Release/net10.0-windows"
    dotnet build tools/perf/AppHarness.csproj -c Release --nologo `
        "-p:AppBin=$perfAppBin" "-p:BaseIntermediateOutputPath=$perfBuild/obj/" `
        "-p:OutputPath=$perfBuild/bin/"
    $perfHarness = "$perfBuild/bin/Vibedotcom.Tests.exe"
    & $perfHarness render tools/perf/results/render
    1..3 | ForEach-Object { & $perfHarness pack tools/perf/results/pack }
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/perf/soak.ps1 `
        -Harness $perfHarness -Output tools/perf/results/soak-complete -Seconds 600
    # Process-scoped Bypass does not change the machine/user execution policy.
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/perf/analyze.ps1
    $perfTrackerBuild = Join-Path $env:TEMP ('vd-tracker-build-' + [guid]::NewGuid().ToString('N'))
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/perf/tracker/tracker.ps1 `
        -Mode Build -BuildRoot $perfTrackerBuild -CoreDll "$perfAppBin/Vibedotcom.Core.dll"
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/perf/tracker/tracker.ps1 `
        -Mode Run -BuildRoot $perfTrackerBuild
} finally {
    # Only the named temporary worktree; never the user's active worktree.
    git worktree remove --force $perfWorktree
}
```

`soak.ps1` kills only processes it created on failure and bounds readiness, sampling and shutdown. Startup retries and partial outputs must be inspected, not silently discarded. `results/pilot*`, `results/soak`, `results/soak-pdh` and `results/soak-final` are diagnostic/aborted runs; they are not ten-minute evidence. The raw renderer/pack records and tracker fixture checks are reproducible observations independent of the proposed fixes. No commit, upload or product change was made.

## After optimization

Measured on 2026-10-04 KST. The new before baseline is the unmodified App/Core/Tracker at `c9037585c1eb863aa7a64d83be206e68f9a0a8be`, captured before editing; it is **not** the historical `280e7a6` build above. The after build contains the current working-tree performance changes. Concurrent art commits advanced HEAD during the work, without changing `src/`, tests or the embedded `assets/pack/`; their files were preserved. App/Core DLL hashes and runtime MVIDs accompany the harness records. A first diagnostic after directory accidentally resolved historical DLLs through MSBuild default items: `current-first-after` is excluded. The harness projects now disable those implicit items and the build script verifies copied/reference DLL SHA-256 equality.

This remained a busy 18-logical-processor machine. Own build/test work was paused during live intervals; other agents were still running. Processes run sequentially, with three repeats per WPF condition. Startup/readiness and at least ten seconds of live warmup are discarded, followed by roughly fifteen seconds of one-second CPU samples. Tables report the minimum of the three per-run medians, plus the arithmetic mean of the three full-window aggregate percentages. Raw per-run medians, p95/max and aggregate `CPU seconds / wall seconds` remain available: Windows CPU accounting can give a zero one-second median even when the aggregate is nonzero. The separate machine percentage divides the one-core percentage by 18. These short runs do not establish a ten-minute leak verdict.

An observer control found another material measurement error: the original sampler queried process memory inside the target UI thread once per second. Three blank WPF windows with no product timers or drawing still had a minimum per-run CPU median of **1.534% of one core** (0.0852% of machine capacity); the sampler UI CPU median was 1.529%, with median memory-query time at least 13.05ms. Those internal live samples include observer overhead and are excluded from the final live comparison. The replacement PowerShell collector queries CPU/memory/threads outside the target; native target start/end counters provide the full aggregate and allocation/timer/damage totals. No estimated observer cost is subtracted. See `current-after-observer/observer-summary.json` and [the collector methodology](../tools/perf/README.md).

The windows are nonactivating, offscreen, 720×480 at 1x, with temporary homes and brand directories. Fixed working fixtures disable source polling so it cannot replace the injected agents; a separate empty-home fixture exercises actual background polling. Normal/topmost/transparent are verified from native styles. Desktop hosting, visible compositor cost and visible foreground launch latency are not inferred from these runs. No real input, shell minimization or foreground-changing operation was used.

**Live process measurements**

The complete matched matrix below precedes the final transparent walking pacing/deadline correction. Every row has three before and three after processes. CPU columns are percent of **one core**, followed by the separate percentage of the 18-processor machine. The aggregate column retains the range of full-window averages across repeats; it prevents a quantized zero median from being mistaken for zero CPU.

| Mode / workload | Before min-median core / machine % | After min-median core / machine % | Before mean core / machine % | After mean core / machine % | After aggregate one-core range % |
|---|---:|---:|---:|---:|---:|
| normal / 0 | 18.333 / 1.018 | 1.432 / 0.080 | 25.581 / 1.421 | 5.049 / 0.281 | 3.847–7.453 |
| normal / 12 working | 31.522 / 1.751 | 0.761 / 0.042 | 33.953 / 1.886 | 3.144 / 0.175 | 2.584–4.051 |
| normal / 12 mixed activities | 37.301 / 2.072 | 0.000 / 0.000 | 39.700 / 2.206 | 4.259 / 0.237 | 2.071–7.379 |
| topmost / 0 | 15.155 / 0.842 | 0.000 / 0.000 | 22.978 / 1.277 | 2.982 / 0.166 | 2.395–3.433 |
| topmost / 12 working | 17.552 / 0.975 | 1.531 / 0.085 | 27.676 / 1.538 | 2.842 / 0.158 | 2.497–3.430 |
| topmost / 12 mixed activities | 22.928 / 1.274 | 0.764 / 0.042 | 30.765 / 1.709 | 3.316 / 0.184 | 2.183–3.933 |
| transparent / 0 | 10.753 / 0.597 | 0.000 / 0.000 | 18.254 / 1.014 | 3.812 / 0.212 | 2.081–5.404 |
| transparent / 12 working | 29.702 / 1.650 | 1.521 / 0.084 | 40.667 / 2.259 | 3.635 / 0.202 | 2.804–4.677 |
| transparent / 12 mixed activities | 49.625 / 2.757 | 6.110 / 0.339 | 65.970 / 3.665 | 9.490 / 0.527 | 6.022–12.881 |
| normal / 0, actual source polling | 14.501 / 0.806 | 0.000 / 0.000 | 21.666 / 1.204 | 4.018 / 0.223 | 3.528–4.677 |

Mixed-activity after runs had up to four characters moving simultaneously and 245–308 moving animation callbacks per measured window. They are not twelve continuously walking characters. Empty normal allocation fell from **1,052.4 to 58.1 KiB/s**; all three after repeats were 58.1–58.4 KiB/s. App scheduled callbacks fell from 7.08 to **2.92/s**, with zero root scene redraws and zero room rebuilds during measurement. This callback count excludes OS/render-thread wakeups, which have not been measured. The matched count/allocation/damage comparison passed **52/52** gates. Raw records and repeat-level CPU/memory/allocation totals are in `current-before-external/`, `current-after-measured/comparison-summary.json` and `current-after-measured/final-matrix.csv`.

The short matrix does **not** establish the idle ≤1% target or the transparent/topmost ≤1.3× target. A separate traced diagnostic observed 32 background-JIT batches compiling 160 methods, continuing 10.72 seconds beyond the ten-second warm marker. Their 239ms summed elapsed time is not CPU time and is not subtracted. Most diagnostic process CPU was outside the UI thread, but its dominant native function remains unidentified. `dotnet-trace` was installed in the disposable results directory; sampled Dispatcher.GetMessage/SystemEvents waits are not CPU hotspots. Kernel WPR capture failed with OS policy error `0xc5585011`, and left no recording active. See `native-diagnostics/` and `jit-reader/diagnostic.json`.

Three separate representative native-memory diagnostics used `PROCESS_MEMORY_COUNTERS_EX2.PrivateWorkingSetSize`, rather than confusing private committed bytes or total RSS with private working set. Peaks were **121.5 MB** (normal/0), **143.3 MB** (normal/12), and **185.5 MB** (transparent/12), below 200 MB in those runs. They are not three-repeat memory results for every workload or a long-term growth test; the traced normal/0 CPU is excluded from the clean cohort. Total RSS and private commit are retained separately in the raw records.

| Requested budget | Evidence from the short matrix |
|---|---|
| Empty idle ≤1% of one core | Not met by normal/0 median (1.432%); averages were 3.847–7.453%. |
| Fewer than 3 wakeups/s; no animation ticks for a static scene | 2.92 app callbacks/s in the authored empty room; static-scene timer test has zero animation callbacks. OS wakeups remain unmeasured. |
| Twelve mostly working ≤4% | All mode minimum medians were below 4%; some full-window averages exceeded 4%. |
| Walking/activities ≤10% | Normal/topmost mixed-activity averages were below 10%; transparent reached 12.881% before the final pacing correction. |
| Transparent/topmost within 1.3× normal | Not established by the short noisy/quantized matrix. |
| Empty allocation ≤100 KiB/s | Met in all three normal/0 repeats (58.1–58.4 KiB/s); a long-term growth bound is not established. |
| Visible first frame ≤1.5s | Offscreen marker met in three after runs; visible launch unmeasured. |
| Private working set ≤200 MB | Met in the three representative native-memory diagnostics; broader/longer coverage unmeasured. |

The final pacing/deadline build completed an additional three normal and three transparent mixed-activity runs **before the stop instruction**. These are a separate cohort, not replacements selected from the earlier matrix:

| Mode / 12 mixed activities | Minimum of three medians, core / machine % | Mean of three aggregates, core / machine % | Aggregate core range % |
|---|---:|---:|---:|
| normal | 2.985 / 0.166 | 6.763 / 0.376 | 3.944–8.687 |
| transparent, 10 fps walking | 1.521 / 0.084 | 5.334 / 0.296 | 3.121–7.584 |

Normal repeats had 227–252 moving callbacks, transparent 124–137, and three to five characters moving simultaneously. Transparent app callbacks were 10.04–10.32/s. Both cohorts' measured activity averages were below 10%, but changing activity and machine noise still prevent certification of the 1.3× mode budget. Records: `current-after-activity-delta/summary.json` and `wrap-up/activity-delta-means.json`. The longer-warmup comparison was interrupted after only one before run; `current-before-warm45/` is excluded and there is no accepted matching after cohort. At the user's stop instruction, optimization and further CPU campaigns stopped; incomplete runs remain marked and are not used to claim a pass.

**Hotspots and changes**

The throwaway friend-assembly harness brackets actual product methods with Stopwatch and current-thread allocation counters after warmup. Reflection/boxing overhead is retained and separately controlled. Each cell below is minimum-of-three call medians, milliseconds / managed bytes. Fixed settled snapshots isolate the phases; Core.Update includes its own subphases. Direct OnRender records drawing commands without rasterization. Full RenderBitmap includes the smoke HWND/bitmap lifecycle and dispatcher work, so these measurements must not be subtracted to estimate an isolated HWND cost.

| Agents / phase | Before ms / B | After ms / B |
|---|---:|---:|
| 0 / Core.Update | 0.0791 / 61,536 | 0.0218 / 32 |
| 0 / AssignWorkplaces | 0.0298 / 17,088 | 0.0001 / 0 |
| 0 / RefreshFurniture | 0.0449 / 43,720 | 0.0201 / 0 |
| 12 / Core.Update | 0.3191 / 87,416 | 0.0430 / 3,888 |
| 12 / AssignWorkplaces | 0.0628 / 21,792 | 0.0002 / 0 |
| 12 / RefreshFurniture | 0.0642 / 44,672 | 0.0226 / 0 |
| 12 / direct View.OnRender | 0.7237 / 172,752 | 0.2215 / 82,680 |
| 12 / smoke RenderBitmap, 1x | 68.9551 / 390,260 | 14.5089 / 125,708 |

Records: `tools/perf/results/current-before/phases-repeat-{1,2,3}` and `current-after/phases-repeat-{1,2,3}`. The empty direct Core call's 32 bytes include diagnostic boxed arguments; the unboxed static-room regression test measures **0B for 1,000 warmed calls**. Smoke timing is not a live frame time: production animation retains one window and does not allocate a RenderTargetBitmap/HwndSource every frame.

Core now retains unchanged workplace assignments, geometry, furniture and immutable view/sprite outputs; bounded path and atlas/row/frame caches avoid repeated navigation and lookup work. WPF retains the rasterized room and separate furniture/body/overlay visuals. Only changed visual bounds are rerecorded; the static bitmap is regenerated for geometry/camera/DPI changes. This uses WPF's retained drawing invalidation, rather than manually repainting the whole scene. `RedrawPixelArea` is the sum of logical changed bounds, not an OS/GPU/compositor dirty-region measurement. Overlapping bounds may be counted more than once.

A second attribution pass caught a remaining O(furniture²) depth/host search on every TV change: a warmed pass over 64 furniture objects allocated 50,744B and took 0.840ms. Retaining geometry depths while checking live lounge-seat occupancy reduced it to 2,080B / 0.0124ms. Direct changing-TV Core+View work fell from 63,999B / 0.777ms to 16,400B / 0.202ms per frame. These are mean diagnostic calls in `results/depth-probe`, not live CPU estimates; the final live matrix measures the complete process independently. Earlier phase rows above precede this last depth cache.

The animation timer stops when no animation/deadline exists, wakes for scheduled blink/seat deadlines, follows authored ambient rates up to 8 fps and uses up to 30 fps for walking/tag fades. The final pacing correction samples transparent walking at 10 fps while tag fades remain 30 fps; authored poses/pixels at a specified animation time are unchanged, but transparent walking presentation has fewer intermediate samples. Past immediate deadlines cannot turn a continuously animating window into a 60 fps loop, and turn/lower/raise phases retain their actual 120/240/360ms deadlines. Visibility/source maintenance is coalesced into continuous animation callbacks, otherwise checked at 1 Hz. Minimized, hidden, DWM-cloaked or conservatively fully covered windows suspend animation; one higher opaque covering rectangle is detected, while unions of multiple or layered windows are not inferred. A low-frequency check permits resumption. Sheets decode on first use, freeze and share within the immutable pack; initial pack/layout preparation remains asynchronous. Text measurements and reusable scene ordering buffers are cached. Actual source polling runs on a background task.

Fresh offscreen process-start-to-ContentRendered measurements were **5,937 / 6,078 / 2,279ms before**, and **1,165 / 1,119 / 1,497ms after** (three processes each; no cache flush). After loading and construction, zero sheets were decoded; the first scene used 36 shared sheets instead of eagerly decoding 96. The measured after marker was below 1.5s in these runs, but it is an offscreen ContentRendered marker, so the visible-launch requirement remains unverified. Deferred tray creation removed a separately attributed 337ms constructor cost; QA does not create the tray. See `startup-repeat-{1,2,3}/startup.json` in the matched directories.

Tracker retains serialized static atlas/assets/layout and compares dynamic output before publishing, reuses serialization buffers, and updates at 1–2 Hz settled / nominal 5 Hz moving. An additional allocation finding showed that exact-length output arrays still reallocated when timestamp precision or sequence digits changed; serialization now exposes the reusable buffer as ReadOnlyMemory and atomic delivery synchronously writes its span. Compact cached JSON sections reduced the representative state from 744,903B to 356,387B while remaining equivalent as parsed JSON; three warmed native-CPU write probes reduced median CPU per 100 atomic writes from 156.25ms to 109.375ms. These probes attribute a change and do not stand in for E2E CPU. Unchanged snapshots publish a three-second freshness heartbeat; every successful publication advances sequence, with a fresh timestamp on each retry. Atomic delivery remains unchanged. See the additive [state contract](state-schema.md) and isolated WPF `--home` / `--output` smoke options in [limits](limits.md). E2E now includes the WPF snapshot renderer. Its exported manifest has an explicit import path with hash/dimension validation; ordinary authored packs still reserve the fallback ID.

**Tracker targets remain unmet**

Tracker values below are full-window mean CPU, **not minimum-of-three medians**: only one accepted run exists per listed stage. Quiet windows are not directly matched: the baseline uses the scenario's original resource interval; the latest quiet value restarts the tracker on the completed fake home, discards 20 seconds, then measures approximately 40 seconds. Long-soak uses 20× simulator speed, discards 20 seconds, and measures approximately 280 active seconds. Simulator/renderer/collector CPU is outside the tracker process.

| Tracker workload | Before mean core / machine % | Measured after mean core / machine % | Target one-core % | Result |
|---|---:|---:|---:|---|
| quiet | 4.554 / 0.253 | **2.892 / 0.161** | ≤0.5 | Not met; latest compact-buffer build, 0 live agents, stable 40 file handles. |
| long-soak, 20× | 10.468 / 0.582 | **12.986 / 0.721** | ≤3 | Not met; this run precedes the final compact JSON/cadence changes. No later complete long-soak CPU result exists. |

The long-soak run's lifecycle/schema/shared-reader and offscreen Windhawk/WPF smoke checks passed, while CPU failed. It peaked at 85.1 MiB RSS with stable median file handles (44→44); latest quiet peaked at 69.5 MiB RSS. Earlier quiet startup handle growth is not evidence of an unbounded leak. Records: `tracker-e2e/baseline-repeat-1/`, `tracker-e2e/after-repeat-1/long-soak/cpu-summary.json` and `tracker-e2e/improved-quiet/cpu-summary.json`.

Why the CPU budgets are not claimed: retained rendering and cached publication demonstrably remove work, but they do not explain all remaining process CPU. Actual completed-home phase diagnostics independently checked 0 and 12 live inputs. A sleeping main thread still accumulated 0.594 / 1.188 process CPU seconds over 12 seconds, with zero CPU on that main thread. A separate traced quiet run observed compilation activity on a background thread, but tracing itself adds cost. Neither observation identifies the dominant uninstrumented native function, nor permits subtraction from E2E CPU. The kernel recorder was blocked by OS policy; further actual-loop attribution, runtime-configuration comparisons and complete reruns were not finished before the user requested stopping. No experimental tiered-compilation setting was applied to the product. See `tracker-e2e/native-phase-summary.json` and `tracker-e2e/native-trace-quiet/`; these diagnostics are not replacement gate results.

**Deterministic regression checks and validation**

Three demo times (0, 12, 36 s), each at 1x/2x/3x, match the original decoded Pbgra32 SHA-256 exactly: **9/9**, with no intentionally different pixels. A nearest-neighbor cache rounding error found at 3x was fixed by rasterizing at the original viewport origin and transform. The golden hashes were captured from the unmodified before DLLs; after-rendered images did not become their own expectations.

Regression tests enforce zero animation ticks for static empty scenes, zero static Core allocations, bounded settled Core allocations, unchanged room/damage counters, small bounds for one moving body, lazy/frozen/shared images, and heartbeat/locked-publisher freshness/sequence behavior. A retained-source empty-room test preserves changing TV frames and caps Core+View+dispatcher thread allocations at 100 KiB/s over 300 frames / 100 logical seconds, after 30 warmup frames; no wall-clock speed gate or per-frame bitmap capture is involved. Background-thread allocations are covered by the separate whole-process live collector. The count harness performs 240 fixed updates after 40 warmup updates and compares allocations, draw counts and logical area without timing gates. [Benchmark instructions](../tools/perf/README.md) describe fresh assembly snapshots, warmup, sequential repeats, pixel comparison and count-based regression gates.

Final wrap-up Release `dotnet build Vibedotcom.sln -c Release`: **0 warnings, 0 errors**. Full `dotnet test ... --no-build`: **241 passed, 0 skipped**, approximately 40 s, including all nine baseline pixel comparisons and the changing-TV allocation gate. Python tool unit tests: **28 passed** (65.2 s); animation-normalization checks: **PASS**; quiet-tail QA unit checks: **4 passed**. All five simulator scenarios completed isolated generation/checkpoints at 1000× as functional checks; no tracker CPU campaign or live E2E measurement was added during wrap-up. The earlier E2E unit result remains **10 tests OK, including one existing expected failure**; it was not rerun as that same suite during wrap-up. Final logs, TRX, scenario logs and slowest-test JSON are under `tools/perf/results/wrap-up/`. Pixel tests still check all pixels; slow loops were reduced by copying tile buffers once, comparing spans, and formatting assertions only on failure. The archive discovery fixture still contains 1,000 files, with redundant directory/UTF-8 work removed. The publisher-lock test holds the lock across the three-second heartbeat; its further proposed shortening was not applied after the stop instruction.

The slowest five tests in the final complete run (busy-machine elapsed durations, not standalone benchmarks):

| Test | Seconds |
|---|---:|
| RenderingTests.TransparentMarginsPreserveOpaqueTilesAndSpritesAndUniformOpacity | 8.523 |
| TrackerPerfTests.LockedPublicationRetriesWithAFreshTimestampAndIncreasingSequence | 8.124 |
| NameTagTests.PlateAndIconVisibilityUseActualOffscreenPixels (icon) | 7.994 |
| TrackerPerfTests.UnchangedRoomPublishesHeartbeatsWithoutRewritingLayout | 7.098 |
| WpfPerfTests.DemoPixelsMatchUnmodifiedHeadAtAllThreeDensities (0 s) | 6.438 |

`windhawk/` was read only. Corresponding follow-ups there are adaptive SceneLoopBody/UiLoopBody scheduling, cached Renderer::Room geometry with dirty Renderer::Render/CopyPixels updates, reused Scene/HitBoxes ordering buffers, and stable ParseSnapshot/Renderer::Load metadata validation. Windhawk already caches same-path/SHA decoded images and crops and skips unchanged PresentLatest pixels; eager sheet decoding should not be attributed to it.

**Reproduction and next work, not executed during wrap-up**

[The benchmark script](../tools/perf/benchmark.ps1) and [its methodology](../tools/perf/README.md) build a fresh frozen assembly snapshot, discard warmup, run sequential three-repeat suites, retain native aggregates, and compare independently captured pixels. Example after an already completed Release build:

```powershell
.\tools\perf\benchmark.ps1 -Mode All -AppBin src/Vibedotcom.App/bin/Release/net10.0-windows -Output tools/perf/results/NEW_RUN
# Tracker/E2E source-tree run; existing Windhawk renderer is used read-only:
.\tools\qa\e2e.ps1 -Workspace -DirectTracker -Scenario quiet,long-soak -Speed 20 -QuietSteadySeconds 60
```

Do not reuse a result directory. Before trusting another CPU result, use the external collector, retain both minimum medians and aggregate means, and establish a matching warmup protocol from observed settling rather than discard a favorable suffix. Next priorities are uninstrumented actual-loop Tracker phase attribution (including background CPU), a complete latest-code long-soak/quiet rerun, and supported native CPU stacks on a machine whose recording policy permits them. Then verify visible startup/compositor performance, OS wakeups and matched mode ratios on a quiet normal machine. Runtime/tiered-compilation alternatives need comparative evidence before a product change. These are future tasks, not claims of work completed here.

