# Public-release checklist

Code is MIT and project art/assets are CC BY 4.0, as decided by the owner. This checklist records the remaining release work; it does not authorize a history rewrite, push, upload or publication.

## Rights and repository history

- [x] Choose licenses and add [LICENSE](../LICENSE) and [assets/LICENSE.md](../assets/LICENSE.md). Copyright holder: KangDohwa, year 2026.
- [ ] Review [third-party notices](../THIRD_PARTY_NOTICES.md) against the exact packages and runtimes being distributed. Keep upstream notices required by those distributions.
- [ ] Review the AI image model's terms for the generated art, including redistribution. Keep the art attribution with releases.
- [ ] Search for collisions with the name “Vibedotcom”, including repositories, packages, products and trademarks. No clearance search is recorded here.
- [ ] Re-check the owner-guideline summaries in [TRADEMARKS.md](../TRADEMARKS.md). OpenAI's page blocked an earlier scripted fetch. A later readable extract does not replace a review of the full page and intended use.
- [ ] Consider asking OpenAI, Anthropic and Google for written permission for any public official-logo use. A local download does not grant permission.
- [ ] Keep `EmbedBrandOverlay=false` for shared builds. Inspect the packaged sheets and screenshots as well as obvious logo filenames.
- [ ] Check the installed Windhawk `windhawk_api.h`, API/import-library terms and the intended separate-work treatment. The host's GPL-3.0 license alone does not settle this.
- [ ] Have the Windhawk owner add `// @license         MIT` in the mod's `==WindhawkMod==` metadata block and verify Windhawk accepts it. Code licensing is decided; this metadata is still absent in the reviewed source.
- [ ] Remove official logos from all Git history with [git filter-repo](https://github.com/newren/git-filter-repo), then force-push only after the owner's explicit OK. `.gitignore` prevents new additions but does not remove old commits.

History purge needs a backup, a complete inventory of historical logo paths (including renamed or embedded copies), and coordination with everyone who has a clone. The commands below are an outline for a separately approved clean clone, not a complete inventory or a command to run during documentation work.

```powershell
# Only after the owner's explicit approval, in a backed-up clean clone:
git filter-repo --path assets/brand/originals/ --path assets/brand-overlay/ --invert-paths
# Add every other identified historical logo path to the filter above.
# filter-repo may remove origin; verify the intended remote before restoring it.
git push --force --all origin
git push --force --tags origin
```

Re-inspect all retained refs and release attachments after the rewrite. Coordinate remote/host cleanup and replacement of old clones; a force-push alone does not remove every copy.

## Privacy and verification

- [ ] Use only `--demo` data in screenshots. Inspect prompts, tooltips, paths and project names; exclude real state and logs. The README currently uses two earlier verified demo renders.
- [ ] Review the dated [Core](review-core.md), [App](review-app.md) and [Windhawk](review-windhawk.md) findings against the release commit. Do not treat a passing suite as closure of reproduced bugs.
- [ ] Complete the [Windhawk manual shell checklist](../windhawk/README.md#manual-test-checklist--user-performs-later), including Explorer hosting, unload, logon task and CPU/GDI observations. Check standalone shell behavior and editor Save with a disposable layout too.
- [ ] Build the intended commit, run the relevant tests and inspect the demo outputs. Follow [packaging](../tools/release/README.md) for ZIPs, isolation and guards. Its detached worktree uses committed HEAD.
- [ ] Keep personal `%LOCALAPPDATA%` files, `state.json`, settings, real prompts and official icon overlays out of packages and bug fixtures.
- [ ] Publish SHA-256 hashes in release notes and ship `SHA256SUMS.txt` from the trusted release source. Link the [Windows security notice](windows-security-notice.md).

## If the repository becomes public

- [ ] Enable [GitHub private vulnerability reporting](https://docs.github.com/en/code-security/how-tos/report-and-fix-vulnerabilities/configure-vulnerability-reporting/configure-for-a-repository).
- [ ] Add a security policy with the private reporting route and supported versions before inviting security reports. This checklist does not create that policy or a separate conduct file.
- [ ] Review [contributing](../CONTRIBUTING.md), [privacy](privacy.md) and rights notices in the published tree. Tagging and uploading are separate owner-approved actions.
