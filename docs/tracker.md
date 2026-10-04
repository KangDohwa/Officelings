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

`Ctrl+C` stops a continuous run. Console output reports provider counts, snapshot count and bytes read. Warnings go to stderr. `--help` lists the options. The tracker has no `--demo` or `--view` switch; use an empty or invented `--home` for checks that must not read personal data. It exports flat v5 state regardless of the app's saved `view`; iso v6 interchange remains planned.

For a screen share, start with `--privacy full` or `--privacy tasks`. The tracker redacts in Core before writing files, so Windhawk receives safe text. `tasks` keeps names, models and the project leaf; `full` uses generic agent labels and removes models/projects. Prompt-derived Antigravity titles are hidden at both levels. See [privacy](privacy.md) for the remaining visible metadata and limits.

## Options

| Option | Default or meaning |
| --- | --- |
| `--once` | Write one snapshot and exit. |
| `--seconds N` | Stop after N seconds; otherwise run until interrupted. Use either this or `--once`. |
| `--output DIR` | `%LOCALAPPDATA%\Vibedotcom`. |
| `--privacy off\|tasks\|full` | Explicit level for the whole run. Otherwise use a valid `privacyMode` in the output directory's `settings.json`, then `VIBEDOTCOM_PRIVACY`, then `off`. |
| `--home DIR` | Current user profile. An alternate home selects synthetic or relocated provider sources, skips implicit personal art/layout loading and disables the real-profile Antigravity API probe. |
| `--asset-dir DIR` | Explicit runtime art pack. Otherwise try the per-user pack, bundled pack, then procedural fallback. |
| `--layout FILE` | Explicit editable layout. Otherwise try `%LOCALAPPDATA%\Vibedotcom\layout.json` (or the alternate home's `layout.json`), then the bundled layout. Changing `--output` alone does not change this input path. |
| `--working-seconds N` | 120. Quiet Working/Thinking agents become Idle. |
| `--leave-after-idle-minutes N` | 5. Inactive/finished agents leave after this much time since real activity. |
| `--leave-after-waiting-minutes N` | 30. Explicit input/approval waits stay longer. |
| `--gone-minutes N` | Deprecated alias for the waiting threshold. An explicit `--leave-after-waiting-minutes` wins, in either argument order. |
| `--scan-seconds N` | 3. Source-processing interval, separate from the full discovery sweep. |
| `--interval-seconds N` | 0.2 moving interval, about 5 fps; settled animation is adaptive and unchanged snapshots use a 3s heartbeat. |
| `--sharing` | Explicit opt-in presence sender using saved sharing settings and the DPAPI token. Requires interactive JSON preview and typed `ENABLE`; redirected input never enables it. |
| `--help` | Print usage and exit. |

Numeric option values must be positive and finite. Both leave thresholds must exceed the working window. Only one tracker may write a given output directory. Output beneath the selected home's `.codex`, `.claude` or `.gemini` is rejected.

Without `--privacy`, valid changes to `settings.json` take effect on the next tracker update. A transient missing, unreadable or malformed settings file retains the last valid choice. Stricter levels scrub already-held text; switching back does not restore discarded text. To follow the app's Privacy menu, use the same output/settings directory and omit the tracker flag. The tracker does not write settings.

Finished workers use the idle threshold too. Parents stay while a tracked descendant is active. Closed-session observations can trigger earlier departure; see [provider signals and limits](sources-and-inference.md#closed-sessions). Characters walk from their current position and fade at the door. New activity brings departed characters back through the door. These use the existing v5 Gone/exiting pose fields; Windhawk needs no contract or mod changes.

## Presence sharing

Sharing is off unless `--sharing` is given, saved `sharing:"on"` is present, a stored token is available, and you review the entire invented JSON and type `ENABLE` in an interactive terminal. The intended hosted origin is [vibe-com.rayd.cc](https://vibe-com.rayd.cc); its public viewer and deployed server behavior were not verified here. Uploads use the limited presence protocol, not local `state.json`. See [app sharing setup](app-usage.md#presence-sharing) for consent, timing, DPAPI storage and stop/token deletion.

Use the same output/settings file as the app if you want **Stop sharing** to stop the Tracker. Ctrl+C also cancels it. The sharing configuration reads a separate `privacy` field rather than local `privacyMode`; the local Privacy menu currently cannot be relied on to anonymize chosen aliases or stop uploads. See [privacy limits](privacy.md#optional-presence-sharing).

## Output

The current writer emits [state schema v5](state-schema.md), asset/layout format 1 and fallback atlas format 2.

| File | Purpose |
| --- | --- |
| `state.json` | Timestamp, sequence, agent metadata, selected frames and resolved office state. |
| root `privacy` | Additive `off`, `tasks` or `full` field in `state.json`; older files default to `off`. Schema version remains 5. |
| `manifest.json` | Runtime pack manifest with exported sheet paths. |
| `sheets/<sha256>.png` | Content-addressed sheets used by the manifest. |
| `layout.json` | Editable source layout, created only if absent. An existing file is not overwritten. |
| `runtime-layout.json` | Resolved layout published separately from the editable source. |
| `atlas.png` | Legacy procedural fallback atlas. |

Core selects the exact flat frame, foot anchor, seat position and furniture layers; Windhawk consumes those choices. The iso app builds a separate in-process scene and does not consume flat poses as iso coordinates. See the [state contract](state-schema.md) for atomic JSON delivery and render equations, and [asset format](asset-format.md) for source manifests and layouts.

```powershell
dotnet run --project src/Vibedotcom.Tracker --no-build -- --asset-dir C:/MyOfficeArt --layout C:/MyOffice/layout.json
dotnet publish src/Vibedotcom.Tracker -c Release -o artifacts/publish/tracker
```

A framework-dependent publish needs the .NET 10 runtime on its destination. It installs no service or startup entry. Optional logon-task registration, recovery and removal are described in the [Windhawk guide](../windhawk/README.md).
