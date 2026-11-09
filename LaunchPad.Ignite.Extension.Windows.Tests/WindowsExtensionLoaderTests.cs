using LaunchPad.Ignite.App.Commands;

namespace LaunchPad.Ignite.Extension.Windows.Tests;

public class WindowsExtensionLoaderTests
{
    [Fact]
    public void WindowsExtensionLoadsControlPanelActions()
    {
        using var directory = new TemporaryDirectory();
        var extensionDirectory = Path.Combine(directory.Path, "windows");
        Directory.CreateDirectory(extensionDirectory);
        foreach (var fileName in new[]
                 {
                     "extension.json",
                     "LaunchPad.Ignite.Extension.Windows.dll",
                     "LaunchPad.Ignite.Core.dll"
                 })
        {
            File.Copy(Path.Combine(AppContext.BaseDirectory, fileName), Path.Combine(extensionDirectory, fileName));
        }

        var extensions = new ExtensionLoader([directory.Path]).Load();
        try
        {
            Assert.Empty(extensions.Failures);
            var command = Assert.Single(extensions.Schema.Commands.Where(command => command.Name == "control-panel"));

            Assert.Equal(
                ["default", "programs", "network", "sound", "system", "environment"],
                command.Actions.Select(action => action.Name));
            Assert.Equal(
                ["control-panel", "control-panel-programs", "control-panel-network", "control-panel-sound", "control-panel-system", "control-panel-environment"],
                command.Actions.Select(action => action.TargetTemplate));

            var networkCommand = Assert.Single(extensions.Schema.Commands.Where(command => command.Name == "network"));
            Assert.Equal(
                ["default", "wifi", "ethernet", "bluetooth", "adapters", "vpn"],
                networkCommand.Actions.Select(action => action.Name));
            Assert.Equal(
                ["network-status", "network-wifi", "network-ethernet", "network-bluetooth", "network-adapters", "network-vpn"],
                networkCommand.Actions.Select(action => action.TargetTemplate));

            var explorerCommand = Assert.Single(extensions.Schema.Commands.Where(command => command.Name == "explorer"));
            Assert.Equal(
                ["default", "downloads", "documents", "this-pc", "recycle-bin"],
                explorerCommand.Actions.Select(action => action.Name));

            var toolsCommand = Assert.Single(extensions.Schema.Commands.Where(command => command.Name == "tools"));
            Assert.Equal(
                ["default", "device-manager", "event-viewer", "services", "disk-management", "resource-monitor"],
                toolsCommand.Actions.Select(action => action.Name));

            var utilitiesCommand = Assert.Single(extensions.Schema.Commands.Where(command => command.Name == "utilities"));
            Assert.Equal(
                ["default", "calculator", "run", "terminal", "character-map", "magnifier", "snipping-tool", "notepad"],
                utilitiesCommand.Actions.Select(action => action.Name));
            Assert.Equal(
                ["utility-calculator", "utility-calculator", "utility-run", "utility-terminal", "utility-character-map", "utility-magnifier", "utility-snipping-tool", "utility-notepad"],
                utilitiesCommand.Actions.Select(action => action.TargetTemplate));

            var powerCommand = Assert.Single(extensions.Schema.Commands.Where(command => command.Name == "power"));
            Assert.Equal(
                ["sleep", "hibernate", "restart", "shutdown", "sign-out"],
                powerCommand.Actions.Select(action => action.Name));
            Assert.All(powerCommand.Actions.Where(action => action.Name is "restart" or "shutdown" or "sign-out"), action =>
                Assert.False(string.IsNullOrWhiteSpace(action.ConfirmationPrompt)));
            Assert.All(powerCommand.Actions.Where(action => action.Name is "restart" or "shutdown" or "sign-out"), action =>
            {
                Assert.Equal("confirm", Assert.Single(action.Arguments).Name);
                Assert.Contains("confirm", action.Usage);
            });

            var lockCommand = Assert.Single(extensions.Schema.Commands.Where(command => command.Name == "lock"));
            Assert.Equal("power-lock", Assert.Single(lockCommand.Actions).TargetTemplate);
        }
        finally
        {
            extensions.Unload();
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
