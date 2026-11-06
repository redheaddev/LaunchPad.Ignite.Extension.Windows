# LaunchPad.Ignite.Extension.Windows

Windows-specific commands for LaunchPad.Ignite:

- `windows:app <app>` searches user and all-users Start Menu shortcuts and launches the selected app. Suggestions show friendly names and use a whitespace-safe selector when accepted.
- `windows:settings` opens Windows Settings. Actions include `display`, `sound`, `network`, `personalization`, `apps`, and `update`.
- `windows:network` opens Network status by default, or `wifi`, `ethernet`, `bluetooth`, `adapters`, or `vpn`.
- `windows:task-manager` opens Task Manager.
- `windows:control-panel` opens Control Panel. Actions include `programs`, `network`, `sound`, `system`, and `environment` for classic Control Panel applets and dialogs.
- `windows:explorer` opens File Explorer or common locations: `downloads`, `documents`, `this-pc`, and `recycle-bin`.
- `windows:tools` opens Computer Management by default or a named system tool: `device-manager`, `event-viewer`, `services`, `disk-management`, or `resource-monitor`.
- `windows:utilities` opens Calculator by default, or `calculator`, `run`, `terminal`, `character-map`, `magnifier`, `snipping-tool`, or `notepad`.
- `windows:lock` locks the workstation. `windows:power` provides `sleep`, `hibernate`, `restart`, `shutdown`, and `sign-out`; restart, shutdown, and sign-out require the explicit `confirm` argument and a confirmation dialog.

App suggestions are discovered from `.lnk` shortcuts when requested, refreshed by the launcher's two-minute list-function cache, and revalidated before launch. The extension does not accept arbitrary executable paths.

Build and package from this directory:

```powershell
.\scripts\Publish-Extension.ps1
```

The package is written to `artifacts\launchpad-windows-<version>.zip`; pass `-Version` to override the version embedded in the package manifest. Push a `vMAJOR.MINOR.PATCH` tag to this repository to build the package and publish a GitHub Release. Running the `Release Windows Extension` workflow manually builds an Actions artifact without creating a release.

Install the ZIP with `extension install <zip-path>` in LaunchPad.Ignite. The extension project can be built on any platform, but its commands run only on Windows. The Core project reference is relative to the sibling `LaunchPad.Ignite` repository.