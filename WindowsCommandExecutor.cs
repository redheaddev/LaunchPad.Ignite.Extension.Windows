using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using LaunchPad.Ignite.Core.Execution;
using LaunchPad.Ignite.Core.Parsing;
using LaunchPad.Ignite.Core.Schema;

namespace LaunchPad.Ignite.Extension.Windows;

public sealed record WindowsAppEntry(string Id, string Name, string ShortcutPath, string Location);

public sealed class StartMenuAppCatalog
{
    private readonly IReadOnlyList<(string Label, string Path)> _roots;

    public StartMenuAppCatalog()
        : this([
            ("User", Environment.GetFolderPath(Environment.SpecialFolder.StartMenu)),
            ("All users", Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu))])
    {
    }

    public StartMenuAppCatalog(IEnumerable<(string Label, string Path)> roots)
    {
        _roots = roots
            .Where(root => !string.IsNullOrWhiteSpace(root.Path))
            .Select(root => (root.Label, Path.GetFullPath(root.Path)))
            .ToArray();
    }

    public IReadOnlyList<WindowsAppEntry> GetApps(CancellationToken cancellationToken = default)
    {
        var discovered = new List<WindowsAppEntry>();
        foreach (var root in _roots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Directory.Exists(root.Path))
            {
                continue;
            }

            var pendingDirectories = new Stack<string>();
            pendingDirectories.Push(root.Path);
            while (pendingDirectories.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var directory = pendingDirectories.Pop();
                try
                {
                    foreach (var entryPath in Directory.EnumerateFileSystemEntries(directory))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        FileAttributes attributes;
                        try
                        {
                            attributes = File.GetAttributes(entryPath);
                        }
                        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                        {
                            continue;
                        }

                        if ((attributes & FileAttributes.ReparsePoint) != 0)
                        {
                            continue;
                        }

                        if ((attributes & FileAttributes.Directory) != 0)
                        {
                            pendingDirectories.Push(entryPath);
                            continue;
                        }

                        if (!string.Equals(Path.GetExtension(entryPath), ".lnk", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        var relativePath = Path.GetRelativePath(root.Path, entryPath);
                        var relativeName = Path.ChangeExtension(relativePath, null) ?? relativePath;
                        var location = Path.GetDirectoryName(relativePath);
                        var locationLabel = string.IsNullOrEmpty(location)
                            ? root.Label
                            : $"{root.Label} - {location}";
                        discovered.Add(new WindowsAppEntry(
                            CreateId(root.Label, relativeName),
                            Path.GetFileNameWithoutExtension(entryPath),
                            Path.GetFullPath(entryPath),
                            locationLabel));
                    }
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                }
            }
        }

        var ordered = discovered
            .OrderBy(app => app.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(app => app.Location, StringComparer.OrdinalIgnoreCase)
            .ThenBy(app => app.ShortcutPath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var duplicateNames = ordered
            .GroupBy(app => app.Name, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var usedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        return ordered.Select(app =>
        {
            var id = app.Id;
            var suffix = 2;
            while (!usedIds.Add(id))
            {
                id = $"{app.Id}-{suffix++}";
            }

            return app with
            {
                Id = id,
                Name = duplicateNames.Contains(app.Name) ? $"{app.Name} - {app.Location}" : app.Name
            };
        }).ToArray();
    }

    public WindowsAppEntry? FindApp(string nameOrId)
        => GetApps().FirstOrDefault(app =>
            string.Equals(app.Name, nameOrId, StringComparison.OrdinalIgnoreCase)
            || string.Equals(app.Id, nameOrId, StringComparison.OrdinalIgnoreCase));

    private static string CreateId(string source, string relativePath)
    {
        var builder = new StringBuilder();
        var appName = Path.GetFileNameWithoutExtension(relativePath);
        var appDirectory = Path.GetDirectoryName(relativePath);
        var selector = string.IsNullOrEmpty(appDirectory)
            ? $"{appName}-{source}"
            : $"{appName}-{appDirectory}-{source}";
        foreach (var character in selector)
        {
            if (char.IsAsciiLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
            }
            else if (builder.Length > 0 && builder[^1] != '-')
            {
                builder.Append('-');
            }
        }

        return builder.ToString().Trim('-');
    }
}

public sealed class WindowsCommandExecutor : ICommandExecutor, IExtensionListFunctionExecutor
{
    private sealed record WindowsCommandTarget(string Executable, string Description, string? Arguments = null);

    [DllImport("powrprof.dll", ExactSpelling = true, SetLastError = true)]
    private static extern byte SetSuspendState(byte hibernate, byte forceCritical, byte disableWakeEvent);

    private static readonly IReadOnlyDictionary<string, WindowsCommandTarget> Targets =
        new Dictionary<string, WindowsCommandTarget>(StringComparer.OrdinalIgnoreCase)
        {
            ["settings"] = new("ms-settings:", "Windows Settings"),
            ["display"] = new("ms-settings:display", "Display settings"),
            ["sound"] = new("ms-settings:sound", "Sound settings"),
            ["network"] = new("ms-settings:network", "Network settings"),
            ["personalization"] = new("ms-settings:personalization", "Personalization settings"),
            ["apps"] = new("ms-settings:appsfeatures", "Installed apps settings"),
            ["update"] = new("ms-settings:windowsupdate", "Windows Update settings"),
            ["network-status"] = new("ms-settings:network-status", "Network status"),
            ["network-wifi"] = new("ms-settings:network-wifi", "Wi-Fi settings"),
            ["network-ethernet"] = new("ms-settings:network-ethernet", "Ethernet settings"),
            ["network-bluetooth"] = new("ms-settings:bluetooth", "Bluetooth settings"),
            ["network-adapters"] = new("ncpa.cpl", "Network Connections"),
            ["network-vpn"] = new("ms-settings:network-vpn", "VPN settings"),
            ["task-manager"] = new("taskmgr.exe", "Task Manager"),
            ["control-panel"] = new("control.exe", "Control Panel"),
            ["control-panel-programs"] = new("appwiz.cpl", "Programs and Features"),
            ["control-panel-network"] = new("ncpa.cpl", "Network Connections"),
            ["control-panel-sound"] = new("mmsys.cpl", "Sound Control Panel"),
            ["control-panel-system"] = new("sysdm.cpl", "System Properties"),
            ["control-panel-environment"] = new("rundll32.exe", "Environment Variables", "sysdm.cpl,EditEnvironmentVariables"),
            ["explorer-home"] = new("explorer.exe", "File Explorer"),
            ["explorer-downloads"] = new("explorer.exe", "Downloads", "shell:Downloads"),
            ["explorer-documents"] = new("explorer.exe", "Documents", "shell:Personal"),
            ["explorer-this-pc"] = new("explorer.exe", "This PC", "shell:MyComputerFolder"),
            ["explorer-recycle-bin"] = new("explorer.exe", "Recycle Bin", "shell:RecycleBinFolder"),
            ["tool-computer-management"] = new("compmgmt.msc", "Computer Management"),
            ["tool-device-manager"] = new("devmgmt.msc", "Device Manager"),
            ["tool-event-viewer"] = new("eventvwr.msc", "Event Viewer"),
            ["tool-services"] = new("services.msc", "Services"),
            ["tool-disk-management"] = new("diskmgmt.msc", "Disk Management"),
            ["tool-resource-monitor"] = new("resmon.exe", "Resource Monitor"),
            ["utility-calculator"] = new("calc.exe", "Calculator"),
            ["utility-run"] = new("explorer.exe", "Run dialog", "shell:::{2559a1f3-21d7-11d4-bdaf-00c04f60b9f0}"),
            ["utility-terminal"] = new("wt.exe", "Windows Terminal"),
            ["utility-character-map"] = new("charmap.exe", "Character Map"),
            ["utility-magnifier"] = new("magnify.exe", "Magnifier"),
            ["utility-snipping-tool"] = new("SnippingTool.exe", "Snipping Tool"),
            ["utility-notepad"] = new("notepad.exe", "Notepad"),
            ["power-lock"] = new("rundll32.exe", "Lock workstation", "user32.dll,LockWorkStation"),
            ["power-restart"] = new("shutdown.exe", "Restart", "/r /t 0"),
            ["power-shutdown"] = new("shutdown.exe", "Shut down", "/s /t 0"),
            ["power-sign-out"] = new("shutdown.exe", "Sign out", "/l")
        };

    private readonly StartMenuAppCatalog _appCatalog;

    public WindowsCommandExecutor()
        : this(new StartMenuAppCatalog())
    {
    }

    public WindowsCommandExecutor(StartMenuAppCatalog appCatalog)
        => _appCatalog = appCatalog;

    public string Kind => "windows";

    public string FunctionName => "apps";

    public Task<IReadOnlyList<CommandListItem>> ExecuteListAsync(
        CommandFunctionDefinition function,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<CommandListItem> items = OperatingSystem.IsWindows()
            ? _appCatalog.GetApps(cancellationToken)
                .Select(app => new CommandListItem(app.Name, app.Name))
                .ToArray()
            : [];
        return Task.FromResult(items);
    }

    public CommandExecutionResult Execute(ParsedCommand command)
    {
        if (!OperatingSystem.IsWindows())
        {
            return new CommandExecutionResult(false, "This command is only available on Windows.");
        }

        if (string.Equals(command.Action.TargetTemplate, "app", StringComparison.OrdinalIgnoreCase))
        {
            return LaunchApp(command);
        }

        if (string.Equals(command.Action.TargetTemplate, "power-sleep", StringComparison.OrdinalIgnoreCase)
            || string.Equals(command.Action.TargetTemplate, "power-hibernate", StringComparison.OrdinalIgnoreCase))
        {
            var hibernate = string.Equals(command.Action.TargetTemplate, "power-hibernate", StringComparison.OrdinalIgnoreCase);
            try
            {
                if (SetSuspendState(hibernate ? (byte)1 : (byte)0, 0, 0) == 0)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }

                return new CommandExecutionResult(true, hibernate ? "Hibernating." : "Entering sleep.");
            }
            catch (Exception exception) when (exception is Win32Exception or DllNotFoundException or EntryPointNotFoundException)
            {
                return new CommandExecutionResult(false, $"Unable to {(hibernate ? "hibernate" : "sleep")}: {exception.Message}");
            }
        }

        if (command.Action.TargetTemplate is "power-restart" or "power-shutdown" or "power-sign-out"
            && (!command.Arguments.TryGetValue("confirm", out var confirmation)
                || !string.Equals(confirmation, "confirm", StringComparison.OrdinalIgnoreCase)))
        {
            return new CommandExecutionResult(false, "Type 'confirm' to authorize this power action.");
        }

        if (!Targets.TryGetValue(command.Action.TargetTemplate, out var target))
        {
            return new CommandExecutionResult(false, "The requested Windows action is not supported.");
        }

        try
        {
            var startInfo = new ProcessStartInfo(target.Executable) { UseShellExecute = true };
            if (target.Arguments is not null)
            {
                startInfo.Arguments = target.Arguments;
            }

            Process.Start(startInfo);
            return new CommandExecutionResult(true, $"Opened {target.Description}.");
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            return new CommandExecutionResult(false, $"Unable to open {target.Description}: {exception.Message}");
        }
    }

    private CommandExecutionResult LaunchApp(ParsedCommand command)
    {
        if (!command.Arguments.TryGetValue("app", out var appNameOrId))
        {
            return new CommandExecutionResult(false, "Choose an app from the Start Menu suggestions.");
        }

        var app = _appCatalog.FindApp(appNameOrId);
        if (app is null)
        {
            return new CommandExecutionResult(false, "That Start Menu app is no longer available. Refresh suggestions and try again.");
        }

        try
        {
            Process.Start(new ProcessStartInfo(app.ShortcutPath) { UseShellExecute = true });
            return new CommandExecutionResult(true, $"Opened {Path.GetFileNameWithoutExtension(app.ShortcutPath)}.");
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or IOException)
        {
            return new CommandExecutionResult(false, $"Unable to open {app.Name}: {exception.Message}");
        }
    }
}