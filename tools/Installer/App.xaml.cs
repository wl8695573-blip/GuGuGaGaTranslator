using System.Windows;

namespace GuGuGaGaTranslator.Installer;

/// <summary>Entry point. Silent switches exist so the installer can be verified by script.</summary>
public partial class App : Application
{
    /// <inheritdoc />
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var directory = Argument(e.Args, "--dir");

        // --silent --dir <path>: install with no window, then exit.
        if (e.Args.Any(arg => arg.Equals("--silent", StringComparison.OrdinalIgnoreCase)))
        {
            Shutdown(InstallerWindow.RunSilent(directory ?? DefaultDirectory()));
            return;
        }

        if (directory is not null && new InstallerWindow() is { } window)
        {
            window.Show();
            return;
        }

        new InstallerWindow().Show();
    }

    private static string DefaultDirectory() => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Programs",
        "GuGuGaGaTranslator");

    private static string? Argument(string[] args, string name)
    {
        for (var index = 0; index < args.Length; index++)
        {
            if (args[index].Equals(name, StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length) return args[index + 1];
            if (args[index].StartsWith(name + "=", StringComparison.OrdinalIgnoreCase)) return args[index][(name.Length + 1)..];
        }

        return null;
    }
}
