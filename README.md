[English](README.md) | [한국어](README.ko.md)

# Vibedotcom

Vibedotcom shows recent local Codex, Claude Code and Antigravity sessions as a small pixel office on Windows. Characters work, wait, deliver reports and take breaks. Hover for session details; flat view draws lines to known parent sessions. It observes sessions and does not assign work or control agents. This is an independent project, with no affiliation or endorsement from the tool owners.

## Features and current state

Source and documentation reviewed on 2026-10-04.

- **Flat and isometric WPF views.** Flat is the default. Select iso with `--view iso`, saved `view:"iso"`, or **Isometric view** in the office/tray menu. The iso engine and embedded pack render a 10×10 room with twelve seats, three character designs, seated work, reporting, arrivals/departures and a roaming/sleeping cat.
- **Life in the flat office.** Twelve fixed desks, overflow seats, an office editor, darts, ping-pong, reading and other breaks, plus roaming pets. Windows, clock hands and lights follow local time. Optional rain, snow and December decoration work offline.
- **Provider icons and privacy.** Original provider icons distinguish supervisors and sub-agents. Name tags support Hover, Icon and Always. `off|tasks|full` privacy levels redact local details in Core before state is stored.
- **Optional presence sharing.** The flat app and interactive Tracker send only aliases, platforms, reduced states and hierarchy after an exact JSON preview and consent. Stored tokens use Windows DPAPI; **Stop sharing** cancels transmission. The intended hosted service is [vibe-com.rayd.cc](https://vibe-com.rayd.cc).
- **Windhawk v5 synchronization.** The native flat renderer reads current seats, provider icons, leisure poses, ambient fields and supplied pets. The ordinary Tracker does not simulate pets. Explorer injection and its [manual shell checklist](windhawk/README.md#manual-test-checklist--user-performs-later) remain unverified.
- **Bounded inputs.** Hardened file routes reject links, reparse points, unsafe relative paths, UNC paths and alternate data streams. JSON, PNG, discovery and parsing have size/work limits. This is not a completed security audit; see [privacy](docs/privacy.md) and [limits](docs/limits.md).

Iso currently has a smaller feature set: idle agents remain at their seats; flat leisure, atmosphere, editor and Sharing UI are not integrated there. `--layout` and `--profile-idle` require flat. The Tracker still exports **v5 flat state**; the planned v6 iso interchange/native renderer is not shipped. See [view differences](docs/app-usage.md#flat-and-isometric-views) and the [iso contract](docs/iso-integration-contract.md).

The public office web viewer is being built for the hosted server. Its source and deployed behavior were not available for verification in this checkout. Token issuance, public visibility and presence expiry depend on that server.

The sharing adapter currently reads `privacy` separately from the app's `privacyMode`. Do not rely on the local Privacy menu to anonymize chosen sharing aliases or stop uploads. Use generic aliases and **Stop sharing**; the payload allowlist still excludes prompts, paths, models and local session IDs. See [sharing limits](docs/privacy.md#optional-presence-sharing).

## Screenshots

Current offscreen smoke renders use only invented `--demo` sessions and bundled art. These show rendering, not Explorer embedding or a live web viewer. [Capture and optimization commands](docs/img/README.md).

![Current flat office with fixed desks and break areas](docs/img/demo-office-2x.png)

![Isometric office with twelve occupied seats and a cat](docs/img/demo-iso-2x.png)

![Flat office at night with resting agents and pets](docs/img/demo-night-2x.png)

![Original provider and role icons in flat name tags](docs/img/demo-provider-tags-2x.png)

## Quick start

Use Windows 11 and the **.NET 10 SDK**. Run from the repository root:

```powershell
dotnet build Vibedotcom.sln
dotnet test Vibedotcom.sln --no-build
# Invented sessions; neither command reads your provider logs:
dotnet run --project src/Vibedotcom.App --no-build -- --demo
dotnet run --project src/Vibedotcom.App --no-build -- --view iso --demo
# Observe your local sessions; privacy full hides names/models/projects:
dotnet run --project src/Vibedotcom.App --no-build -- --privacy full
# Separate flat state writer for Windhawk; Ctrl+C stops it:
dotnet run --project src/Vibedotcom.Tracker --no-build -- --privacy full
# Separate sharing checks: synthetic data and fake HTTP:
dotnet run --project tests/Vibedotcom.SharingChecks
```

The standalone app reads session sources itself and needs neither Windhawk nor the headless Tracker. Windhawk needs the Tracker's exported PNGs and `state.json`; follow its [setup guide](windhawk/README.md). In flat view, right-click **Edit office** to change furniture; see the [editor](docs/editor.md).

WPF and Windhawk are Windows-only. Core/Tracker target .NET 10, but provider discovery and native integrations here are validated on Windows; macOS/Linux viewers are not provided. Framework-dependent app builds need the **.NET 10 Windows Desktop Runtime**, and Tracker needs the .NET runtime. App/Tracker have no extra NuGet runtime packages; test packages restore into ignored `.packages/`. See [publishing](docs/app-usage.md#publish) and the [unsigned-binary notice](docs/windows-security-notice.md).

## Controls and data

- Wheel (with or without Ctrl), `+` and `-`: zoom 1–6×. Drag to pan; hover for details.
- Right-click office or tray: choose view, window mode, click-through, border, background, opacity, name tags, privacy and pets. Flat also has **Sharing** and **Edit office**.
- Double-click the tray icon, or choose **Show normal, clickable office**, to recover. Closing exits and removes the icon.

Settings normally live in `%LOCALAPPDATA%\Vibedotcom\settings.json`. Demo/QA use disposable storage and disable sharing. Normal observation can read private prompt text and paths; choose privacy before screen sharing. Sharing starts off and requires fresh consent each run. Windhawk has separate settings and no tray. Desktop embedding uses undocumented shell behavior with a bottom-window fallback; neither viewer appears on the secure lock screen.

All CLI flags, setting keys, source inference and privacy boundaries are in [app usage](docs/app-usage.md), [Tracker](docs/tracker.md), [sources](docs/sources-and-inference.md) and [privacy](docs/privacy.md).

## Performance and remaining work

The recorded short **flat** offscreen measurements averaged **5.049% of one core** for an empty normal window and **3.144%** for twelve working agents, across three runs each. CPU means process CPU seconds / wall seconds × 100; whole-machine percentages on the 18-thread measurement machine are different. The ≤1% idle target and a ten-minute leak verdict are not established. These are not iso or visible desktop measurements. See [conditions and full results](docs/perf-report.md#after-optimization).

Remaining work includes broader iso behavior and roster conversion, v6/native iso interchange, the public web viewer, the privacy/sharing setting connection, manual Explorer/DPI/virtual-desktop checks, longer performance runs and the [release checklist](docs/release-checklist.md). Parser inference, art pose/contact quality and native hosting have limits; see [verification notes](docs/limits.md) and the dated review records.

## Documentation

- Use: [App](docs/app-usage.md), [Tracker](docs/tracker.md), [Editor](docs/editor.md), [Privacy](docs/privacy.md), [Limits](docs/limits.md), [Synthetic QA](docs/simulator.md).
- Formats: [State v5](docs/state-schema.md), [Assets/layouts](docs/asset-format.md), [Iso integration](docs/iso-integration-contract.md), [S0 production reference](docs/iso-slice-s0.md), [Specification](SPEC.md).
- Art: [Pipeline and credits](docs/art-pipeline.md), [Face lock](docs/facelock.md), [Seated poses](docs/seated.md), [Prop normalization](docs/anim-normalize.md), [Screenshots](docs/img/README.md).
- Delivery: [Windhawk](windhawk/README.md), [Performance](docs/perf-report.md), [Packaging](tools/release/README.md), [Windows security](docs/windows-security-notice.md).
- Contribute: [English](CONTRIBUTING.md), [한국어](CONTRIBUTING.ko.md). Reviews: [Core/Tracker](docs/review-core.md), [App](docs/review-app.md), [Windhawk](docs/review-windhawk.md). These are dated records, not release certification.
- References: [Antigravity](docs/antigravity-notes.md), [Pixel Agents](docs/pixel-agents-notes.md), [Windhawk research](docs/windhawk-notes.md), [Trademarks](TRADEMARKS.md), [한국어](TRADEMARKS.ko.md), [Third-party notices](THIRD_PARTY_NOTICES.md).

## License

Code is [MIT](LICENSE), copyright (c) 2026 KangDohwa. Project art/assets are [CC BY 4.0](assets/LICENSE.md). The art was generated with an AI image model. Third-party logos and brand icons are excluded; see [trademarks](TRADEMARKS.md) and the [local overlay guide](assets/brand/README.md).
