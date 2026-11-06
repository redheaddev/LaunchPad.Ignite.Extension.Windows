# LaunchPad.Ignite.Extension.Windows

Windows-specific commands for LaunchPad.Ignite:

- `windows:app <app>` searches user and all-users Start Menu shortcuts and launches the selected app. Suggestions show friendly names and use a whitespace-safe selector when accepted.
- `windows:settings` opens Windows Settings. Actions include `display`, `sound`, `network`, `personalization`, `apps`, and `update`.
- `windows:task-manager` opens Task Manager.

App suggestions are discovered from `.lnk` shortcuts when requested, refreshed by the launcher's two-minute list-function cache, and revalidated before launch. The extension does not accept arbitrary executable paths.

Build and publish from this directory:

```powershell
dotnet publish .\LaunchPad.Ignite.Extension.Windows.csproj -c Release -o .\publish
Compress-Archive -Path .\publish\* -DestinationPath .\LaunchPad.Ignite.Extension.Windows-1.0.0.zip -Force
```

Install the ZIP with `extension install <zip-path>` in LaunchPad.Ignite. The extension project can be built on any platform, but its commands run only on Windows. The Core project reference is relative to the sibling `LaunchPad.Ignite` repository.