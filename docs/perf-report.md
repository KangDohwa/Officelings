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

## Idle CPU 후속 측정 — perf lane, 2026-10-04

이번 기준은 `8a68730`의 Release App/Core/Tracker이며, 앞의 p43 측정과 별도 캠페인이다. 수정 전 DLL과 apphost를 `tools/perf/results/followup-before/`에 복사한 뒤 사용했다. 최종 수정본은 `followup-final/`에 따로 보존했다. App/Core의 `assemblies.json`과 `runtime.json`에 실제 DLL SHA-256/MVID가 있고, 최종 Tracker는 `tracker-bin/`과 `tracker-assemblies.json`에 보존했다. 마지막 solution build 뒤의 App/Core DLL도 측정용 복사본과 SHA-256이 같았다. iso 소스는 수정하지 않았다.

CPU는 모두 **코어 하나의 사용률**이다. `100 × 프로세스 CPU 초 / 실제 측정 초`로 계산하고, 18개 논리 프로세서 전체의 비율로 바꾸지 않았다. CPU·메모리·스레드 조회는 별도 PowerShell 수집기가 실행한다. WPF 대상에는 시작/종료 콜백 두 개만 있고, 주기적인 내부 계측기나 observer 비용 차감은 없다. ETW는 앞서 확인된 OS 정책 차단 때문에 사용하지 않았다.

각 조건을 새 프로세스로 순차 3회 실행했다. 30초의 실시간 warmup을 버리고 empty-room은 30초, p36은 20초, tracker quiet와 Windhawk는 30초, tracker long-soak은 270초를 측정한다. CPU 중앙값은 각 실행의 1초 표본 중앙값 3개 중 최솟값이다. 전체 구간 평균 3개의 평균과 범위도 함께 남긴다. Windows CPU 시간의 양자화로 중앙값이 0이어도 실제 CPU가 0이라는 뜻은 아니다.

p36 fixture는 seed 42의 demo를 -18초부터 36초까지 진행시킨 다음 8명의 합성 입력을 고정한다. 이후 실제 OfficeWindow/Core/View가 계속 움직인다. warmup 뒤에도 최대 2명이 걷는 **p36 시작 장면**이며, 완전히 정지한 8명이나 계속 입력이 바뀌는 CLI demo와 같다고 주장하지 않는다. empty-room도 TV·steam·로컬 시계·펫을 포함한다. WPF 창은 720×480, 1x, (-30000,-30000)의 비활성 창이다. 이 결과로 화면에 보이는 compositor나 Explorer 성능을 추정하지 않는다.

CPU 캠페인 원본은 `followup-before`, `followup-before-p36`, `followup-final`, `tracker-before-{quiet-settled,long}`, `tracker-after-cadence-{quiet,long}`, `windhawk-{before,after}`에 있다. `tracker-after-{quiet-settled,long}`은 deadline 수정 전 중간 결과로 별도 보존했다. 다음 표에서 중앙값은 요청한 **3회 중앙값 중 최솟값**, 평균은 전체 측정 구간 평균 3개의 산술평균이며 괄호는 그 범위다.

Tracker는 기존 E2E와 같이 scenario의 working/idle/waiting 만료 시간을 simulator speed로 나누고 source scan은 제품 기본 3초를 유지한다. 처음 `tracker-*-quiet`에서 기본 만료 시간을 쓴 측정에는 idle 3명이 남아 있었으므로 0명 quiet gate에서 제외했다. baseline 0명 조건은 `tracker-before-quiet-settled`, 최종 수정본은 `tracker-after-cadence-quiet`에서 별도로 측정하고 마지막 state의 live 수를 검사한다.

| 조건 | 수정 전 중앙값 % | 최종 중앙값 % | 수정 전 평균 % (범위) | 최종 평균 % (범위) | 목표 |
|---|---:|---:|---:|---:|---:|
| WPF empty-room normal | 0.000 | 0.000 | 2.146 (1.924–2.337) | 1.439 (1.300–1.665) | ≤1% |
| WPF empty-room transparent | 1.512 | 0.000 | 2.495 (1.925–3.378) | 1.890 (1.249–2.290) | normal의 ≤1.3배 |
| WPF p36 normal | 5.535 | 1.538 | 7.749 (5.845–9.575) | 5.501 (4.190–7.885) | ≤2% |
| WPF p36 transparent | 4.538 | 3.058 | 6.304 (5.147–7.683) | 4.985 (4.291–5.907) | normal의 ≤1.3배 |
| Tracker quiet, live 0명 | 1.541 | 1.371 | 2.547 (2.101–2.869) | 2.050 (1.823–2.345) | ≤0.5% |
| Tracker long-soak, 20× | 15.314 | 12.345 | 28.739 (25.309–32.335) | 23.956 (23.900–24.051) | ≤3% |
| Windhawk local-clock | 10.635 | 5.361 | 10.892 (10.160–11.540) | 8.115 (5.517–12.805) | 기존 15% 비용 축소 |

empty-room은 평균이 1%를 넘고, 중앙값 0은 CPU 시간 양자화 때문에 합격 근거로 쓰지 않는다. p36 normal의 중앙값은 2% 아래로 내려갔지만 전체 구간 평균은 여전히 높다. transparent/normal은 p36 중앙값 기준 **1.99배**이고, empty-room은 두 중앙값이 0이어서 비율을 계산할 수 없다. empty-room 평균 비율도 **1.31배**다. 따라서 transparent의 1.3배 조건은 충족하지 못했다.

Windhawk는 타일 raster를 보존해 중앙값을 약 절반으로 줄였지만, 기존 순서의 동적 가구/캐릭터/태그 GDI+ 렌더와 viewport 복사는 남는다. 전체 평균의 실행별 범위가 넓어 모든 실행에서 같은 개선을 보였다고 주장하지 않는다. 이 표의 첫 before/after부터 별도 프로세스 3회이며, 앞의 15% 기록은 이 조건의 baseline으로 혼용하지 않았다.

Tracker quiet는 **0.5% 목표에 미달**이다. deadline 수정 전 중간 결과는 전체 평균 2.839%로 개선하지 못했지만, 최종 수정 후 2.050%로 baseline보다 약 20% 감소했다. 만료된 파일은 discovery 구간 밖이므로 시작 로그부터 live/total/bytesRead가 모두 0이며, 활성 log tail 부하를 줄인 효과로 설명하지 않는다. long-soak은 최종 중앙값 약 19%, 전체 평균 약 17% 감소했지만 **3% 목표에 미달**이다. 입력은 provider별 live 4↔5명, 전체 33↔36명으로 반복 churn하며, simulator/수집기 CPU는 포함하지 않았다. 이번 최신 장면과 속도별 만료 조건의 결과를 앞의 p43 단일 실행 12.99%와 직접 비교하지 않았다.

WPF empty-room normal 할당은 115–126에서 **57–60 KiB/s**, p36 normal은 644–664에서 **383–389 KiB/s**로 줄었다. 최종 세 번의 p36 normal에서 callback은 15.1–15.5/s, UI thread는 1.47–3.25%, `wpfgfx_cor3.dll`에서 시작한 render thread는 약 1.2–2.5%를 사용했다. empty-room callback도 펫 이동 때문에 5.1–5.5/s였다. 모든 실행에서 root scene/room raster 재생성은 0회였다. GC는 empty-room에서 0회, p36 normal에서 세 번 모두 gen0/gen1/gen2가 2/1/1회였으며 transparent는 1/0/0 또는 2/1/1회였다. 유지된 scene 안의 실제 걷기·펫·가구 갱신과 할당, WPF native render 비용이 남아 있으며, start module만으로 그 native 함수까지 특정할 수는 없다. 보이지 않는 창의 비용도 최종 숫자에 포함했다.

### 확인한 비용과 수정

기존 WPF는 이미 가구별 retained visual을 사용했다. 이번 수정은 ambient만 바뀔 때 깊이·링크·태그·정적 오버레이를 다시 계산하던 경로를 줄인다. 변경된 clock/TV/steam/window/pet visual만 갱신하고, 펫의 깊이가 바뀔 때만 순서를 조정한다. 방 타일은 유지한다. ambient 가구와 sheet 목록을 보관하고 같은 초의 환경 상태, 같은 방향의 펫 idle frame, 같은 수면 접촉 offset도 재사용한다.

가구 깊이 캐시 적중 경로에는 캡처된 LINQ 조건식과 인터페이스 열거자 할당이 남아 있었다. 이를 캐시 miss 전용 메서드와 좌석 host 사전 조회로 옮겼다. 아래는 기존 changing-TV 계측기의 30 frame warmup 후 300회 결과다. Reflection/boxing 비용을 포함하며, 마지막 두 행은 같은 전체 가구 수와 재사용 인자 배열을 쓰는 control과 함께 비교한다. elapsed 시간은 혼잡한 머신의 단일 진단이며 CPU 최종 수치를 대신하지 않는다.

| 조건 / 단계 | 수정 전 중앙값 ms / B | 최종 중앙값 ms / B |
|---|---:|---:|
| 0명 / View.Update | 0.4540 / 23,208 | 0.17835 / 10,200 |
| 12명 / View.Update | 1.1966 / 73,016 | 0.5764 / 47,896 |
| 12명 / Core.Update | 0.29605 / 6,112 | 0.1273 / 6,112 |
| 12명 / 가구 전체 FurnitureDepth | 0.0221 / 22,008 | 0.0360 / 1,880 |
| 12명 / 같은 횟수의 reflection control | 0.0085 / 1,880 | 0.00385 / 1,880 |

따라서 최종 FurnitureDepth 진단의 할당은 control과 같지만, elapsed 시간의 개선까지 입증한 결과는 아니다. Core의 0명 TV/steam 갱신 할당은 frame당 1,752B로 남았다. 새 동작과 관련 없는 Core 구조를 바꾸지 않았다.

펫의 한 셀 idle에는 visual tick을 예약하지 않고 다음 행동/추적 갱신 시각을 사용한다. 수면 등 느린 클립은 실제 authored FPS, 걷기는 기존 8 FPS를 사용한다. Core도 정지 클립에 최소 4 FPS를 강제하지 않는다. clock만 있는 장면은 1 FPS이며, 일반 창의 animation/maintenance 예약은 기존 방식으로 합친다. 이로써 모든 펫과 TV가 있는 실제 방의 callback 수가 무조건 3/s 미만이라고 주장하지 않는다.

Tracker는 움직이는 frame과 입력 분류를 분리해 live 입력 분류를 최대 2 Hz, quiet 분류를 1 Hz로 실행하고 settings 읽기는 1 Hz로 제한한다. 기존 3초 source scan, watcher, 주기적 reconciliation과 atomic publication/3초 freshness heartbeat는 유지했다. `JsonlTail`은 16 KiB 읽기 버퍼와 두 probe 버퍼를 재사용하며 줄바꿈을 span으로 찾는다. 파일 identity, shrink/regrowth, 같은 크기/mtime overwrite와 partial UTF-8 검사를 유지했다. 2 KiB 합성 append 100회의 warmed allocation은 수정 전 **3,575,200B**, 수정 후 **1,710,400B**였고, payload byte/record 수는 동일하다. buffer 재사용으로 입력 source당 최대 16 KiB가 추가로 남는다.

최종 tracker 복사본의 synchronous 단계 진단은 별도로 30초 warmup을 버렸다. quiet에서 Poll/Core는 각 45회인데 바깥 루프는 44.69초 동안 **1,291회**였다. `Task.Delay(TimeSpan)`의 밀리초 절삭으로 미래 deadline의 1ms 미만 잔여 시간이 즉시 완료되는 경로를 확인했다. [.NET 10의 ValidateTimeout/Delay 구현](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.Private.CoreLib/src/System/Threading/Tasks/Task.cs#L5316-L5329)도 이 동작을 확인해 준다. `TrackerCadence.Delay`가 양수 대기 시간을 밀리초 단위로 올림하도록 고쳤고, 같은 진단의 44.70초에서 루프 **46회**, Poll/Core 각 **45회**가 됐다. 논리 30초를 native delay의 절삭과 작은 작업 시간으로 시뮬레이션하는 회귀 검사도 정확히 30회 호출을 확인한다. 이것은 루프 호출 수이며 OS wakeup 수는 아니다.

수정 후 quiet 단계의 thread CPU는 publication 390.625ms, Poll 218.75ms로 약 0.87% / 0.49%에 해당했다. GC는 0회, 전체 진단 할당은 약 101 KiB/s였다. 별도 churn 진단 44.96초에서는 Poll **6,750ms / 73,895,704B**, publication **1,328.125ms / 6,869,728B**였고 GC는 13/1/1회였다. 각각 약 15.0% / 3.0%의 thread CPU로, 남은 주된 비용은 source 조회·검증·파싱을 포함한 Poll과 atomic publication이다. Core stage CPU는 15.625ms였다. Poll 안의 개별 native 함수까지 분리하지 않았고 파일 경계·overwrite 검사를 완화하지 않았다. 이 throwaway 진단은 계측/delegate 비용과 Windows thread CPU 양자화를 포함하고, async retry·대기·background 작업은 단계 밖이므로 최종 프로세스 CPU나 모든 잔여 비용의 설명을 대신하지 않는다. 원본은 `tracker-probe/`와 `tracker-probe-cadence/`다.

Windhawk는 clock만 바뀔 때 다시 그리던 타일을 최종 camera density의 viewport bitmap으로 보관한다. camera/크기/투명 모드/검증된 layout·pack/타일 내용이 바뀌면 다시 만든다. 픽셀을 그대로 복사하므로 두 번째 보간을 추가하지 않는다. 동적 가구·캐릭터·태그는 기존 순서로 그린다. 추가로 viewport 한 장의 PARGB 메모리가 필요하며, 이번 720×520 조건은 약 1.43 MiB다. native start-address 모듈 조회는 스레드의 시작 모듈을 확인할 뿐, 실행 중인 함수나 CPU stack은 아니다.

### 회귀 검증과 제한

검토된 ambient flat golden 9개는 그대로 통과했다. 수정 전후의 독립적인 PNG capture를 decoded Pbgra32 SHA-256으로 비교해 **9/9 동일**했다. 추가 incremental clock/weather/lights 검사는 1/2/3x에서 fresh render와 모든 픽셀을 비교한다. Windhawk의 기존 normal/auto/offline/small/transparent PNG **5개는 파일 바이트까지 동일**했다. native renderer **427개**, owned hidden host/metadata **69개** 검사가 통과했다.

새 count 검사는 같은 civil second의 환경 상태 1,000회에 0B 할당, clock의 30 logical seconds에 정확히 30 visual redraw와 작은 redraw area, authored empty room의 30 logical seconds에 80 KiB/s 이하 할당과 root/room 보존, JsonlTail의 합성 append당 20 KiB 이하를 확인한다. 실제 경과 시간이나 머신 CPU를 테스트 gate로 쓰지 않는다. 펫 테스트의 고정 500ms 기대값은 한 셀 idle의 예약 없음/미래 행동 시각과 1 FPS sleep의 1초 주기로 바꿨다. 픽셀 기대값은 바꾸지 않았다.

Release build는 오류 0개, 기존 iso xUnit 분석기 경고 17개다. deadline 수정 전 전체 494개 실행은 **492 통과 / 2 실패 / 0 skip**였다. `IsoPresentationModesTests.MovingBodyAndLabelArePickedAgainAtTheStationaryPointer`는 수정 전 App/Core DLL로도 같은 줄에서 재현됐고 iso 소스는 그대로 두었다. 같은 전체 실행의 locked-publication timestamp 검사는 단독 관련 검사에서 통과했다. App/Core 관련 검사는 **16/16 통과**, 마지막 deadline 변경 후 Tracker 관련 검사는 새 호출 수 검사까지 **8/8 통과**다. 마지막 변경 후 전체 suite를 반복하지 않았다. 기본 Windows TEMP에서 발생한 파일 경계 잠금 실패는 합성 TEMP를 이 워크트리 안으로 옮겨 재검증했다. 이 실패를 제품 경계 검사 완화로 해결하지 않았다.

공유 파일의 변경 위치는 다음과 같다. `Models.cs`, Settings, Core/Iso와 App/Iso*는 수정하지 않았다.

| 공유 파일 | 위치 / 변경 |
|---|---|
| `src/Vibedotcom.App/OfficeView.cs` | Update 196–244: overflow host 갱신, agent 변경 분리, ambient 전용 경로 선택. FurnitureDepth 564–591: cold cache 메서드와 indexed occupancy 조회. |
| `src/Vibedotcom.App/Program.cs` | ScheduleAnimation 757–763: Office와 Pet의 다음 갱신 시각 중 빠른 값 선택. |
| `src/Vibedotcom.Core/Office.cs` | RefreshCadence 706: 정지 clip의 authored FPS 유지. |
| `src/Vibedotcom.Tracker/Program.cs` | ReadPrivacy 44–56, main loop 87–104: settings 읽기·입력 분류 주기 분리. 148: 미래 deadline 대기의 밀리초 올림. |

합칠 때 `OfficeView.Perf.cs`의 새 partial과 Ambient의 `NextUpdateAt`, `TrackerCadence.cs`도 함께 반영해야 한다. authored 낮은 FPS의 표시 간격과 tracker 입력 분류의 최대 0.5–1초 지연은 의도한 변화다. Windhawk에는 viewport 한 장, JsonlTail에는 source별 읽기 버퍼가 추가된다. 실제 화면·Explorer 주입·OS wakeup, 10분 이상의 메모리 안정성은 이번 offscreen CPU/픽셀 검사로 확인하지 않았다. native/managed 함수별 전체 CPU stack도 확보하지 못했다.

`dotnet-counters`는 워크트리 안의 tool/cache와 공개 NuGet source만 지정해 설치를 한 번 시도했지만 `https://api.nuget.org/v3/index.json` 서비스 인덱스 로드에 실패했다. 설치를 다시 시도하거나 권한·네트워크 설정을 바꾸지 않았다. 별도의 tracker 복사본 진단은 stopwatch/current-thread allocation/Windows thread CPU/GC counts를 사용하며 최종 CPU 표와 분리한다.

커밋은 승인된 lane branch에서 시도했지만 공유 Git 메타데이터 `C:/Codex-Workspace/Vibedotcom/.git/worktrees/perf/index.lock` 생성이 **Permission denied**로 실패했다. 차단 시 중단하라는 lane 규칙에 따라 staging/commit을 다시 시도하거나 다른 방식으로 우회하지 않았다. 변경은 이 워크트리에 unstaged 상태로 남아 있다.

## ISO CPU — main 210c9fd, lane/polish

2026-10-04~05 KST, Release/.NET 10, 18 logical processors. 수정 전에 보존한 main App/Core DLL을 `tools/perf/IsoHarness.csproj`의 `AppBin` 참조로 사용했다. 기존 `collect-external.ps1` 수집기를 재사용해 각 조건을 새 프로세스에서 순차 실행했다. 720×480 비활성 offscreen 창, 합성 TEMP/home/brand, tags=hover, 기본 TV·펫을 유지했다. 빈 방/작업 입력은 30 logical seconds 정착 후 30초 live warmup을 버렸다. demo epoch는 warmup 길이만큼 늦춰 약 0초부터의 **24초 live interval**을 측정했다. 각 조건은 한 번이며, 아래 CPU는 native process CPU delta / 실제 약 24초 wall time의 **평균, 한 코어 %**다. 머신 %는 18로 나눈다. 측정 중 자체 빌드·테스트는 멈췄으며 다른 lane은 계속 실행됐다.

| 입력 / zoom | normal 평균 % | transparent 평균 % | 한 코어 목표 / 결과 |
|---|---:|---:|---|
| empty, 1x | 14.309 | 12.982 | ≤1 / 미달 |
| empty, 2x | 15.421 | 18.068 | ≤1 / 미달 |
| 12 working, 1x | 16.531 | 22.962 | ≤4 / 미달 |
| 12 working, 2x | 24.379 | 21.597 | ≤4 / 미달 |
| 24초 demo, 1x | 62.318 | 55.938 | ≤10 / 미달 |
| 24초 demo, 2x | 53.303 | 66.680 | ≤10 / 미달 |

1x 작업의 transparent/normal 비율은 1.389로 1.3×를 넘었다. 다른 짧은 조건의 비율이나 quantized 1초 중앙값만으로 모드 예산 충족을 인증하지 않는다. 첫 transparent/12/2x는 종료 표식 뒤 WPF 종료 대기 10초를 넘겨 제외했다. 하네스가 측정 표식을 기록한 뒤 자기 프로세스만 종료하도록 바꾼 재실행을 채택했다. 제품 DLL은 그대로다. 원시 결과와 DLL SHA/MVID는 `tools/perf/results/polish-main/`에만 보존하며 커밋하지 않는다(`transparent-working-2x-retry`가 채택본).

가구·펫 프레임마다 빈 태그 그림까지 다시 열던 경로를 없앴다. 위치·이름·인물 경계가 그대로면 태그를 재사용한다. 아래 after는 `b7bf802` App/Core의 별도 새 프로세스이며 1x empty만 다시 측정했다. 마지막 always-only 글리프 clipping 보강은 이 CPU cohort에 포함되지 않는다.

| 1x empty | CPU 평균 전→후 % | 관리 할당 전→후 KiB/s | 태그 redraw 전→후, 24초 |
|---|---:|---:|---:|
| normal | 14.309→16.588 | 244.49→243.97 | 125→0 |
| transparent | 12.982→17.902 | 255.48→226.99 | 132→0 |

불필요한 태그 redraw 제거는 확인했지만 CPU 감소는 측정하지 못했다. idle ≤1%, 할당 ≤100 KiB/s는 여전히 미달이다. after app callback은 normal 6.286/s, transparent 6.039/s이며 OS wakeup 수는 아니다. 기본 펫의 보행과 TV가 포함된 빈 방을 정적 scene으로 취급하지 않는다. 정지 revision와 warmed 배치 1,000회는 각각 0B이며, 인물·태그·viewport 충돌 검사와 hover/icon 전후 PNG 0 changed pixels를 확인했다. PNG 계측은 WPF 태그 outline과 원본 인물 alpha>0 texels의 교차를 센다. 가구에 가려진 인물 texel도 포함하는 보수적 수치다. 도착 2초 **497→0**, 4초 **229→0**이고 0.5/2/4/20초의 태그 교차·화면 밖 픽셀은 모두 0이다. 최종 always-only 글리프 경계 보강 후 관련 기존/새 렌더 검사 **39개**가 통과했다.

전체 Release build는 경고/오류 0개. 최종 전체 .NET은 **573 pass / 0 fail / 86 skip**다. 첫 전체 실행은 572 pass / 1 fail / 86 skip였고, 실패한 기존 `TrackerPerfTests.LockedPublicationRetriesWithAFreshTimestampAndIncreasingSequence`는 기준/현재 바이너리의 단독 실행에서 각각 1 pass였다. 최종 전체 실행에서도 통과했다. 새 회귀 12개, Python unittest 91개와 별도 parity 9개/animation normalize fixture가 통과했다. 새 PNG 계측기의 파일 핸들 경고를 수정한 뒤 관련 Python 3개도 ResourceWarning=error로 통과했다. before/after 좌석 WPF 768크롭은 각각 2,120,194 opaque texels가 전체 큐와 일치했다. 실제 화면/compositor, 다른 DPI의 live 실행, 장기 soak와 수정 후 작업/demo CPU는 검증하지 않았다.

