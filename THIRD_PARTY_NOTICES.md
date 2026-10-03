# Third-party notices

Vibedotcom code is [MIT](LICENSE), copyright (c) 2026 KangDohwa. Project art/assets are [CC BY 4.0](assets/LICENSE.md), with attribution “Vibedotcom art by KangDohwa (https://github.com/KangDohwa/Vibedotcom)”. The art was generated with an AI image model. Third-party logos and brand icons are excluded; see [TRADEMARKS.md](TRADEMARKS.md).

This inventory was checked on 2026-10-04 against `src/*/*.csproj`, the test project, its resolved NuGet metadata, and `tools/*.py` imports, including imports inside functions. Links point to upstream licenses rather than copied or invented license text. Distribution-specific notices still need review for the exact release contents.

## Tools and references

| Component | License/source | Use |
| --- | --- | --- |
| [sprite-gen](https://github.com/aldegad/sprite-gen) | [Apache-2.0](https://github.com/aldegad/sprite-gen/blob/main/LICENSE) | Tool used to generate art. Its license is for the tool, not a substitute for image-model output terms. |
| [pixel-agents](https://github.com/pixel-agents-hq/pixel-agents) | [MIT](https://github.com/pixel-agents-hq/pixel-agents/blob/main/LICENSE) | Design reference only. No code or runtime art was copied. Local reference screenshots used during art review are not project-authored art or runtime-pack assets. See [research notes](docs/pixel-agents-notes.md). |
| [Windhawk](https://github.com/ramensoftware/windhawk) | [GPL-3.0](https://github.com/ramensoftware/windhawk/blob/main/LICENSE) | Separately installed host and compiler environment. This project's mod is intended as a separate MIT work using its API. Verify the installed `windhawk_api.h` and import-library terms before relying on that treatment; it is not a legal conclusion. No Windhawk host binary is included in the app ZIPs. |

## .NET and NuGet

The app, Core and tracker have no third-party NuGet runtime PackageReference. They use .NET 10; the app also uses WPF/Windows Forms and Windows APIs. Self-contained packages include a .NET runtime, whose own third-party notices must be preserved.

| Component/version | Upstream license | Use |
| --- | --- | --- |
| .NET 10 SDK/runtime | [MIT, with upstream third-party notices](https://github.com/dotnet/runtime/blob/main/LICENSE.TXT), [runtime notices](https://github.com/dotnet/runtime/blob/main/THIRD-PARTY-NOTICES.TXT) | Build tools and application runtime. |
| WPF and Windows Forms | [WPF MIT](https://github.com/dotnet/wpf/blob/main/LICENSE.TXT), [Windows Forms MIT](https://github.com/dotnet/winforms/blob/main/LICENSE.TXT) | Standalone UI, tray and desktop integration. |
| Microsoft.NET.Test.Sdk 17.14.1 | [MIT](https://github.com/microsoft/vstest/blob/main/LICENSE) | Test discovery and execution; direct test dependency. |
| xunit 2.9.3 | [Apache-2.0, with listed MIT components](https://github.com/xunit/xunit/blob/v2-2.9.3/license.txt) | Unit tests; direct dependency. |
| xunit.runner.visualstudio 3.1.5 | [Apache-2.0, with listed MIT components](https://github.com/xunit/visualstudio.xunit/blob/main/License.txt) | Test adapter; private test dependency. |
| Microsoft.CodeCoverage, Microsoft.TestPlatform.ObjectModel, Microsoft.TestPlatform.TestHost 17.14.1 | [MIT](https://github.com/microsoft/vstest/blob/main/LICENSE) | Resolved test SDK dependencies for collection and hosting. |
| Newtonsoft.Json 13.0.3 | [MIT](https://github.com/JamesNK/Newtonsoft.Json/blob/13.0.3/LICENSE.md) | Resolved test-host dependency, not app parsing. |
| xunit.core, xunit.assert, xunit.extensibility.core, xunit.extensibility.execution 2.9.3 | [Apache-2.0 and upstream exceptions](https://github.com/xunit/xunit/blob/v2-2.9.3/license.txt) | Resolved test implementation dependencies. |
| xunit.abstractions 2.0.3 | [Apache-2.0](https://github.com/xunit/abstractions.xunit/blob/main/license.txt) | Test runner abstractions, resolved transitively. |
| xunit.analyzers 1.18.0 | [Apache-2.0](https://github.com/xunit/xunit.analyzers/blob/main/LICENSE) | Build-time test analysis, resolved transitively. |

The Antigravity DB reader calls the Windows-provided `winsqlite3.dll`; no SQLite NuGet package or DLL is bundled by this project. SQLite's upstream code is [public domain](https://sqlite.org/copyright.html). That does not relicense the Windows system component or grant a right to redistribute Windows files. The Windhawk build uses its installed compiler, API headers and runtime libraries, which are not covered by Vibedotcom's MIT grant.

## Python tools

| Component | Upstream license | Use |
| --- | --- | --- |
| Python and standard library | [PSF license and included notices](https://docs.python.org/3/license.html) | CLI, JSON, hashing, filesystem, unittest, local process launching and optional URL downloads. |
| Pillow (`PIL`) | [MIT-CMU](https://github.com/python-pillow/Pillow/blob/main/LICENSE) | PNG/image processing in face lock, seated poses, normalization, pack building, brand fetching and their image tests. |
| NumPy | [BSD-3-Clause](https://github.com/numpy/numpy/blob/main/LICENSE.txt) | Pixel masks and array operations in face lock, QA, seated poses and normalization. |
| CairoSVG | [LGPL-3.0](https://github.com/Kozea/CairoSVG/blob/main/LICENSE) | Optional existing SVG renderer used by the local brand-fetch tool. If unavailable, the tool uses an owner-hosted PNG. It is not installed or bundled by the project. |

`facelock`, `anim_normalize`, `build_pack` and other imports of neighboring scripts are project code. No other external Python package is imported by the reviewed tools. The Windhawk parity script uses only the standard library. Existing Python environments may include additional transitive/native dependencies; review their notices if you distribute such an environment. App releases do not ship it.

## Fonts and brand assets

No font files (`.ttf`, `.otf`, `.woff`, `.woff2`, `.fon`) are present in the reviewed repository. The WPF renderer requests installed Windows fonts, including Segoe UI and Small Fonts. These are system fonts under Microsoft's terms, not CC BY 4.0 project assets; see [Microsoft's font redistribution guidance](https://learn.microsoft.com/en-us/typography/fonts/font-faq). The native renderer likewise uses installed system typography. No font redistribution permission is claimed.

Optional official icons come from the URLs in [assets/brand/README.md](assets/brand/README.md). They are local downloads, not licensed project art. Check the [trademark summaries](TRADEMARKS.md) and [release checklist](docs/release-checklist.md) before distributing anything that contains them.
