[English](CONTRIBUTING.md) | [한국어](CONTRIBUTING.ko.md)

# Contributing

Contributions are welcome, including bug reports, documentation fixes, code and art. Discuss a new dependency or a larger feature in an issue before starting. Keep a PR focused on one change.

## Bug reports

Use the repository's [issues](https://github.com/KangDohwa/Vibedotcom/issues). Include the Windows version and OS build (`winver`), relevant tool versions (Codex, Claude Code, Antigravity or Windhawk), the .NET SDK/runtime version, and the commit or release you used. Describe how to reproduce the problem, what you expected and what happened. Mention window mode, click-through, zoom and any custom pack/layout if relevant.

Attach sanitized diagnostics only. Never paste real prompts or personal paths, raw session logs, credentials or your real `state.json`. Use invented text in reproductions and fixtures. For screenshots, use `--demo` and check tooltips as well as the room. See [privacy](docs/privacy.md). Use a private security-reporting route when one is configured; do not post an exploit with personal data in a public issue.

## Build and check

Use Windows 11 with the .NET 10 SDK. Run these from the repository root:

```powershell
dotnet build Vibedotcom.sln
dotnet test Vibedotcom.sln --no-build
dotnet run --project src/Vibedotcom.App --no-build -- --demo
```

For Python art tools, use an existing environment with Pillow and NumPy. Run the checks affected by your change:

```powershell
python -B -m unittest discover -s tools -p 'test_*.py'
python -B tools/test_anim_normalize.py
# Build the mod and native test executables with installed Windhawk clang:
powershell -NoProfile -ExecutionPolicy Bypass -File windhawk/compile.ps1 -Tests
```

The normalization check is a standalone script, so unittest discovery alone does not run it. The Windhawk command compiles without injecting into Explorer. Native test execution and manual shell checks are in the [Windhawk guide](windhawk/README.md); rendering checks are in [limits](docs/limits.md#verification). Do not register a logon task or manipulate another application's windows as part of a routine test.

## Code and commits

Follow the surrounding code. Keep C# nullable analysis enabled. Prefer the existing platform APIs and avoid adding a dependency without discussion. Add a small relevant test for changed logic when existing checks cannot catch the error; prose-only changes need a link/content review.

Use [Conventional Commits](https://www.conventionalcommits.org/en/v1.0.0/), for example `fix(tracker): handle a replaced session log` or `docs: 사용 안내 정리`. Existing commit summaries are usually Korean. English commits and PRs from other contributors are welcome.

## Art

Submit original work or work you have the rights to contribute under [CC BY 4.0](assets/LICENSE.md). Name the creator, source and license for externally licensed material. Do not include third-party logos or brand marks. AI-assisted art should identify the model/service and applicable terms so the maintainer can review them.

Characters use 32x64 production cells and tiles use 32x32. Keep pixel edges and the declared frame/anchor geometry. Lock eyes per facing, preserve alpha and pixels outside the reviewed face patches, and retain provenance. Face lock does not approve anatomy or motion. Read [art pipeline](docs/art-pipeline.md), [face-lock rules](docs/facelock.md), [seated poses](docs/seated.md) and the [asset format](docs/asset-format.md) before replacing sheets. Include a contact sheet or demo render and report the checks you actually ran.

## Before opening a PR

- Describe the problem and resulting behavior. Keep unrelated cleanup out of the diff.
- List the checks you ran and any checks you could not run.
- Use invented fixtures; remove personal data and official brand files from the diff and images.
- Update affected docs and links. For art, include attribution, provenance and the required previews.
- Confirm that you can contribute the change under the project's license terms.

## Contribution terms and conduct

Submitting a PR offers your code contribution under MIT and your art contribution under CC BY 4.0, the same licenses used for distribution (inbound = outbound). You retain your copyright and must have the rights to make that offer. No separate contributor agreement is required by this guide. A DCO `Signed-off-by` line is welcome but not required; see the [Developer Certificate of Origin](https://developercertificate.org/).

Be kind and discuss the work rather than the person. Respect privacy, give useful feedback and allow maintainers time to respond. Harassment and personal attacks are not welcome.
