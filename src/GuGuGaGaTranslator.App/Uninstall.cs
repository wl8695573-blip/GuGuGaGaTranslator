using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace GuGuGaGaTranslator.App;

/// <summary>
/// Removes an installed copy: shortcuts, the Programs-and-features entry, and
/// then the folder itself once this process has exited.
/// </summary>
/// <remarks>
/// The application uninstalls itself rather than shipping a second binary, so an
/// installation costs one executable and nothing else.
/// </remarks>
internal static class Uninstall
{
    internal const string ProductName = "GuGuGaGaTranslator";
    internal const string RegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + ProductName;

    internal static string InstallDirectory => AppContext.BaseDirectory.TrimEnd('\\');

    internal static string ShortcutPath(Environment.SpecialFolder folder) => Path.Combine(
        Environment.GetFolderPath(folder),
        "Programs",
        ProductName + ".lnk");

    /// <summary>Delete the shortcuts and the Programs-and-features entry.</summary>
    /// <param name="onMessage">Receives one line per step, or null for silence.</param>
    /// <returns>True when everything was removed.</returns>
    internal static bool Run(Action<string>? onMessage = null)
    {
        try
        {
            foreach (var shortcut in new[]
            {
                ShortcutPath(Environment.SpecialFolder.StartMenu),
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory) is { Length: > 0 } desktop
                    ? Path.Combine(desktop, ProductName + ".lnk")
                    : string.Empty,
            })
            {
                if (shortcut.Length > 0 && File.Exists(shortcut))
                {
                    File.Delete(shortcut);
                    onMessage?.Invoke($"已删除快捷方式 {shortcut}");
                }
            }

            using var key = Registry.CurrentUser.OpenSubKey(RegistryKey, writable: true);
            if (key is not null)
            {
                key.Close();
                Registry.CurrentUser.DeleteSubKeyTree(RegistryKey, throwOnMissingSubKey: false);
                onMessage?.Invoke("已从「应用和功能」里移除");
            }

            return true;
        }
        catch (Exception exception)
        {
            onMessage?.Invoke($"清理时出错(可以手动删除安装目录):{exception.Message}");
            return false;
        }
    }

    /// <summary>
    /// Delete the folder once this process has exited, which is the only moment
    /// a running executable's own files can be removed.
    /// </summary>
    /// <param name="onMessage">Receives the outcome line.</param>
    internal static void ScheduleDirectoryRemoval(Action<string>? onMessage = null)
    {
        var script = $"ping -n 4 127.0.0.1 >nul & rmdir /s /q \"{InstallDirectory}\"";
        try
        {
            Process.Start(new ProcessStartInfo("cmd.exe", "/c " + script)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden,
            });

            onMessage?.Invoke($"安装目录将在几秒后删除:{InstallDirectory}");
        }
        catch (Exception exception)
        {
            onMessage?.Invoke($"无法自动删除安装目录,请手动删除 {InstallDirectory}:{exception.Message}");
        }
    }
}
