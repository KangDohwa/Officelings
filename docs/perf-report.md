**Performance measurement — committed `280e7a61b0d3c049de75967892debac15627fbcc`**

Measured on 2026-10-04 KST with Release assemblies built in the detached `%TEMP%/vd-perf` worktree. Build: zero warnings/errors. Runtime: .NET 10.0.11, SDK 10.0.400, Windows NT 10.0.26200, 18 logical processors available to the process. Hardware inventory was denied by the sandbox. Other agents remained active; these are measurements of this constrained, busy environment, not a quiet reference machine.

The rendering, pack and tracker measurements completed. **The requested twelve-condition, ten-minute idle soak did not complete within the approximately forty-minute work budget. There is no ten-minute leak verdict.** Failed startup attempts and short samples are retained, rather than presented as successful soaks. Native desktop hosting was not attempted: committed `WindowModes.EmbedDesktop` sends Explorer's private wallpaper messages, contrary to the requirement not to disturb the shell.

No input, cursor movement, minimization or foreground-changing API was used. Every measurement window was owned, offscreen and nonactivating. `GetForegroundWindow` returned zero in the sandbox, so equality of those readings does not independently prove the foreground was unchanged. Product sources were not modified; personal settings and the real `%LOCALAPPDATA%/Vibedotcom` were not read or written. Assets came from committed embedded resources; tracker homes and brand paths were explicitly temporary/empty. Merely setting environment variables is not asserted to redirect Windows known folders.

**Idle and soak coverage**

The committed CLI has a fixed demo: three leads and up to six workers, with arrivals/departures; it has no count/scenario-file option. `--profile-idle` is a twenty-second, six-agent offscreen test. `--qa` limits CLI runs to sixty seconds and forces normal mode. Consequently the measurement harness calls the unchanged built `OfficeWindow`, `OfficeView` and Core assemblies through the existing friend-assembly access; it is not a rebuilt or patched app.

The harness injects 0/6/12/24 fabricated idle agents, retains QA's no-save/no-tray/no-hover behavior, substitutes a tracker with an explicit empty home, and polls that home once per second without replacing the injected agents. It uses the committed owned-window verification override for native normal/topmost/transparent behavior. Windows are borderless, 720×480, 1x, at (-30000,-30000). Topmost and transparency were checked from native style bits and `AllowsTransparency`. This measures app logic and offscreen window behavior, not visible desktop presentation or compositor cost. Idle characters can still walk, blink and visit leisure zones; the first minute includes settling.

The initial `Get-Counter` collector incurred delays of tens of seconds; its partial CSVs are excluded from idle conclusions. The replacement opens one PDH query, binds Process counters to owned PIDs, and reuses it alongside `Get-Process` and `GetGuiResources`. Its short zero-agent validation sampled at 5.060 and 10.115 seconds with matching private bytes and thread counts. CPU percentages are one-core equivalents; divide by 18 for whole-machine percentages. RSS means Windows working set; private bytes are separately reported.

Repeated starts failed with `InvalidDataException: Layout navigation exceeded its work/time budget`, including a sequential start. The stack identifies `OfficeView.cs:19` creating its default `OfficeAnimator`, whose built-in layout traversal has a 50 ms wall-clock limit (`LayoutLoader.cs:156`). The final harness retries only this constructor rejection, records the retry count and keeps all product assemblies unchanged. These retries are a harness accommodation, not a claim that stock CLI startup succeeds. Final coverage and any available samples are appended below. Median/p95/max use sorted samples and nearest-rank p95; drift is least-squares slope against actual elapsed minutes. Short-run slopes cannot establish or exclude leaks.

**Accepted short idle samples ??not ten-minute soaks**

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

