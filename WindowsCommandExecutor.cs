using System.ComponentModel;
using System.Diagnostics;
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
    private static readonly IReadOnlyDictionary<string, (string Target, string Description)> Targets =
        new Dictionary<string, (string Target, string Description)>(StringComparer.OrdinalIgnoreCase)
        {
            ["settings"] = ("ms-settings:", "Windows Settings"),
            ["display"] = ("ms-settings:display", "Display settings"),
            ["sound"] = ("ms-settings:sound", "Sound settings"),
            ["network"] = ("ms-settings:network", "Network settings"),
            ["personalization"] = ("ms-settings:personalization", "Personalization settings"),
            ["apps"] = ("ms-settings:appsfeatures", "Installed apps settings"),
            ["update"] = ("ms-settings:windowsupdate", "Windows Update settings"),
            ["task-manager"] = ("taskmgr.exe", "Task Manager")
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

        if (!Targets.TryGetValue(command.Action.TargetTemplate, out var target))
        {
            return new CommandExecutionResult(false, "The requested Windows action is not supported.");
        }

        try
        {
            Process.Start(new ProcessStartInfo(target.Target) { UseShellExecute = true });
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