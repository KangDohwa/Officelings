[English](TRADEMARKS.md) | [한국어 요약](TRADEMARKS.ko.md)

# Trademarks and brand icons

Vibedotcom is independent and unofficial. It is not affiliated with, endorsed by or sponsored by OpenAI, Anthropic or Google. Codex, ChatGPT, Claude, Claude Code, Gemini and Antigravity are names or trademarks of their respective owners. Plain-text references identify the tools the viewer observes. This is the project's intended nominative use, not a claim of permission or a legal conclusion.

## Owner guidelines

Research date: 2026-10-04. These are summaries, not legal advice. Check the current source and the details of your intended use before release.

| Owner | Summary relevant to logos |
| --- | --- |
| [OpenAI](https://openai.com/brand/) | Use supplied artwork under its usage terms. Do not modify, recolor, stylize or distort it, imply endorsement, or incorporate it into your own branding. The supplied black/white marks are the reference for the earlier guidance. An earlier scripted fetch was blocked; a readable web extract was checked, but the full wording still needs a release review. |
| [Anthropic](https://www.anthropic.com/legal/trademark-guidelines) | The guidelines require specific permission and prior approval of materials. They prohibit changes to color, font or proportions and unauthorized implications of sponsorship or affiliation. They do not grant blanket permission for an in-app logo. The listed contact is marketing@anthropic.com. |
| [Google](https://about.google/brand-resource-center/guidance/) | Do not put Google marks in your product or brand name or imply affiliation. Check the [product and icon guidance](https://about.google/brand-resource-center/products-and-services/) and its linked [brand-element rules](https://about.google/brand-resource-center/brand-elements/); some uses require permission. This research did not establish permission to use the Gemini sparkle in this app without asking. |

## Project rules

The project has no paid features. That does not remove trademark obligations.

- Shared default builds use generic tag icons drawn for this project, in provider accent colors. Official logos are not bundled by default; `EmbedBrandOverlay=false` remains the default.
- Official icons are an optional local overlay. [The fetch scripts and source list](assets/brand/README.md) download owner-hosted files into ignored directories. The app also accepts `codex.png`, `claude.png` and `antigravity.png`, with optional density variants, in `%LOCALAPPDATA%\Vibedotcom\brand\`.
- Fetching is a personal local workflow, not a determination that personal display is permitted. Do not redistribute a build with these icons without reviewing the owners' terms and obtaining any required permission. Resizing, masking corners or recoloring can conflict with the guidelines.
- The overlay uses a ChatGPT icon for Codex and the Gemini sparkle for Antigravity. Antigravity is a separate Google product from the Gemini models. These mappings are the author's choices, not the owners' designation of those product logos; they may misattribute a mark.
- Do not use a provider name to brand a fork or package, such as `Codex-Something`, `Claude-...` or `Gemini-...`.

The [MIT code license](LICENSE) and [CC BY 4.0 art license](assets/LICENSE.md) grant no rights to third-party logos or brand icons. Public-release work, including removal of official marks from Git history, is tracked in the [release checklist](docs/release-checklist.md).
