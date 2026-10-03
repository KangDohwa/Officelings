# Headless tracker

The tracker reads local session sources and writes an office snapshot for [Windhawk](../windhawk/README.md). The standalone WPF app has its own tracker and does not need this process. See [sources and inference](sources-and-inference.md) for provider behavior and [privacy](privacy.md) for the data it retains.

## Run

From the repository root, after `dotnet build Vibedotcom.sln`:

```powershell
dotnet run --project src/Vibedotcom.Tracker --no-build
# A short run against your real sources, with output in this checkout:
dotnet run --project src/Vibedotcom.Tracker --no-build -- --seconds 15 --output artifacts/live
# One snapshot:
dotnet run --project src/Vibedotcom.Tracker --no-build -- --once --output artifacts/once
dotnet run --project src/Vibedotcom.Tracker --no-build -- --working-seconds 120 --leave-after-idle-minutes 5 --leave-after-waiting-minutes 30
```

`Ctrl+C` stops a continuous run. Console output reports provider counts, snapshot count and bytes read. Warnings go to stderr. `--help` lists the options. The tracker has no `--demo` switch; use an empty or invented `--home` for checks that must not read personal data.

## Options

| Option | Default or meaning |
| --- | --- |
| `--once` | Write one snapshot and exit. |
| `--seconds N` | Stop after N seconds; otherwise run until interrupted. Use either this or `--once`. |
| `--output DIR` | `%LOCALAPPDATA%\Vibedotcom`. |
| `--home DIR` | Current user profile. An alternate home selects synthetic or relocated provider sources and disables the real-profile Antigravity API probe. |
| `--asset-dir DIR` | Explicit runtime art pack. Otherwise try the per-user pack, bundled pack, then procedural fallback. |
| `--layout FILE` | Explicit editable layout. Otherwise try `%LOCALAPPDATA%\Vibedotcom\layout.json`, then the bundled layout. Changing `--output` alone does not change this input path. |
| `--working-seconds N` | 120. Quiet Working/Thinking agents become Idle. |
| `--leave-after-idle-minutes N` | 5. Inactive/finished agents leave after this much time since real activity. |
| `--leave-after-waiting-minutes N` | 30. Explicit input/approval waits stay longer. |
| `--gone-minutes N` | Deprecated alias for the waiting threshold. An explicit `--leave-after-waiting-minutes` wins, in either argument order. |
| `--scan-seconds N` | 3. Source-processing interval, separate from the full discovery sweep. |
| `--interval-seconds N` | 0.2. Snapshot/animation interval, about 5 fps. |

Numeric option values must be positive and finite. Both leave thresholds must exceed the working window. Only one tracker may write a given output directory. Output beneath the selected home's `.codex`, `.claude` or `.gemini` is rejected.

Finished workers use the idle threshold too. Parents stay while a tracked descendant is active. Closed-session observations can trigger earlier departure; see [provider signals and limits](sources-and-inference.md#closed-sessions). Characters walk from their current position and fade at the door. New activity brings departed characters back through the door. These use the existing v5 Gone/exiting pose fields; Windhawk needs no contract or mod changes.

## Output

The current writer emits [state schema v5](state-schema.md), asset/layout format 1 and fallback atlas format 2.

| File | Purpose |
| --- | --- |
| `state.json` | Timestamp, sequence, agent metadata, selected frames and resolved office state. |
| `manifest.json` | Runtime pack manifest with exported sheet paths. |
| `sheets/<sha256>.png` | Content-addressed sheets used by the manifest. |
| `layout.json` | Editable source layout, created only if absent. An existing file is not overwritten. |
| `runtime-layout.json` | Resolved layout published separately from the editable source. |
| `atlas.png` | Legacy procedural fallback atlas. |

Core selects the exact frame, foot anchor, seat position and furniture layers. Both viewers consume those choices. See the [state contract](state-schema.md) for atomic JSON delivery and render equations, and [asset format](asset-format.md) for source manifests and layouts.

```powershell
dotnet run --project src/Vibedotcom.Tracker --no-build -- --asset-dir C:/MyOfficeArt --layout C:/MyOffice/layout.json
dotnet publish src/Vibedotcom.Tracker -c Release -o artifacts/publish/tracker
```

A framework-dependent publish needs the .NET 10 runtime on its destination. It installs no service or startup entry. Optional logon-task registration, recovery and removal are described in the [Windhawk guide](../windhawk/README.md).
