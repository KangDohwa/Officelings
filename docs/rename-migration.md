# Officelings rename migration

The solution, projects, namespaces, assemblies, UI, scripts, attribution and repository links now use Officelings. Existing local installations are handled as follows.

| Legacy identifier | Replacement and migration |
| --- | --- |
| `%LOCALAPPDATA%\Vibedotcom` | `%LOCALAPPDATA%\Officelings`. Normal app startup and Tracker startup using the default output copy the entire legacy directory only when the replacement directory is absent. Demo/QA and explicit custom app homes do not migrate personal data. |
| `%LOCALAPPDATA%\Vibedotcom.Credentials` | `%LOCALAPPDATA%\Officelings.Credentials`. Startup and default credential-store access apply the same copy-once rule, including `presence.dpapi` and persisted sharing cooldowns. Credentials remain outside exported app data. |
| `VIBEDOTCOM_*` environment variables | `OFFICELINGS_*` takes precedence; the legacy names remain read fallbacks for `PRIVACY`, `ISO_EDITOR_MEASURE`, `ISO_EDITOR_SMOKE`, `EMBEDDED_QA` and `BRAND_QA`. CLI and saved privacy settings retain their existing precedence. No system/user environment variables are modified. |
| `Vibedotcom.Tracker` scheduled task | Re-run `tools/windhawk_install_tracker.ps1` with the published replacement executable and desired paths. The installer reads the legacy root-folder task, verifies its owner, registers `Officelings.Tracker`, preserves a disabled legacy task when creating the replacement, then stops/unregisters the old entry. Registration failure leaves the legacy task intact. `-WhatIf` changes nothing; no task is started. Existing replacement tasks still require `-Replace`. |
| `Vibedotcom.Tracker.*` and `Local\Vibedotcom.Sharing.*` mutexes | Renamed to the corresponding Officelings names. These are transient process locks, with no stored data to import. Stop the older app/Tracker before running the replacement, especially when using a shared custom output directory. |

Directory migration uses a temporary sibling and publishes the completed copy atomically. Concurrent startups accept an already completed destination. Existing destinations are never overwritten or merged. Files, nested directories and empty directories are copied; the legacy directories are never moved or deleted. Links/junctions and conflicting destination files fail migration without publishing a partial destination, allowing a later retry after resolving the conflict.

DPAPI remains Windows CurrentUser with no product-specific entropy or description. Copied ciphertext is readable by the same Windows account/profile; migration does not decrypt or log tokens. Deleting the active token does not re-import the preserved legacy backup. This is a local rename, not credential portability between Windows accounts.

There are no product-named registry keys, named pipes or other autostart stores in the application code. Windhawk mod IDs and window class names use Officelings; install the renamed source through Windhawk and carry over any host-managed custom settings there. The frozen mod is not installed or activated by these repository changes.

The separate deployed server is unchanged: `https://vibe-com.rayd.cc`, `/api/v1/presence`, bearer authorization, content type, Retry-After handling, JSON fields/values and opaque token contents remain compatible. Wire boundaries have `// Wire identifier kept for server compatibility.` comments. The brief referenced `docs/wire-protocol.md`, which was absent from this checkout; the existing sharing implementation was used as the contract.

Migration tests use a temporary directory and cover nested copies, legacy preservation, existing destinations, separate credential/cooldown storage, deletion without resurrection, concurrent startup and destination conflicts. Legacy environment fallback and new-name precedence are checked through synthetic Tracker processes.
