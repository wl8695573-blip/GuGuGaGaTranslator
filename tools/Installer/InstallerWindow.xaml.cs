using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Windows;
using Microsoft.Win32;

namespace GuGuGaGaTranslator.Installer;

/// <summary>
/// Installs the application from the zip embedded in this executable: copies the
/// files, optionally creates shortcuts, and registers an entry under Programs and
/// features that calls the application's own uninstall switch.
/// </summary>
public partial class InstallerWindow : Window
{
    private const string ProductName = "GuGuGaGaTranslator";
    private const string Version = "1.0.1";
    private const string Payload = "payload.app.zip";
    private const string RegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + ProductName;

    private bool _installed;

    /// <summary>Create the installer window.</summary>
    public InstallerWindow()
    {
        InitializeComponent();

        SubtitleText.Text = $"屏幕实时翻译 · 版本 {Version} · 免安装可用,不需要管理员权限";
        IntroText.Text = "这个程序给游戏窗口里的文字做实时翻译:选中窗口、框住对话区域,译文就会贴在最上层。"
            + "识别完全离线,翻译需要一个 API Key(首次启动时按提示领取,界面上有链接)。";
        PathBox.Text = DefaultDirectory();
        StatusText.Text = $"点击「安装」开始。共 {PayloadSize():N0} MB 会被复制到上面这个目录,不会改动系统设置。";
    }

    private static string DefaultDirectory() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Programs",
        ProductName);

    private static double PayloadSize()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(Payload);
        return stream is null ? 0 : stream.Length / 1024.0 / 1024.0;
    }

    /// <summary>Install without showing a window, for scripted verification.</summary>
    /// <param name="directory">Where to install.</param>
    /// <returns>0 on success.</returns>
    internal static int RunSilent(string directory)
    {
        try
        {
            Install(directory, desktopShortcut: false, startMenuShortcut: true, message: _ => { });
            Console.WriteLine($"installed to {directory}");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"install failed: {exception.GetType().Name}: {exception.Message}");
            return 1;
        }
    }

    private void OnBrowse(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "选择安装目录" };
        if (Directory.Exists(PathBox.Text)) dialog.InitialDirectory = PathBox.Text;
        if (dialog.ShowDialog(this) == true) PathBox.Text = dialog.FolderName;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => Close();

    private async void OnInstall(object sender, RoutedEventArgs e)
    {
        InstallButton.IsEnabled = false;
        CancelButton.IsEnabled = false;
        BrowseButton.IsEnabled = false;

        var directory = PathBox.Text.Trim();
        try
        {
            await Task.Run(() => Install(
                directory,
                DesktopShortcutCheck.IsChecked == true,
                StartMenuShortcutCheck.IsChecked == true,
                message => Dispatcher.Invoke(() => StatusText.Text = message)));

            _installed = true;
            StatusText.Text = $"安装完成:{directory}\r\n"
                + "可以从开始菜单启动,也可以在「设置 → 应用」里卸载。";
            InstallButton.Content = "完成";
            InstallButton.IsEnabled = true;

            if (LaunchCheck.IsChecked == true) Launch(directory);
        }
        catch (Exception exception)
        {
            StatusText.Text = $"安装失败:{exception.Message}";
            InstallButton.IsEnabled = true;
            CancelButton.IsEnabled = true;
            BrowseButton.IsEnabled = true;
            return;
        }

        if (LaunchCheck.IsChecked == true) Close();
    }

    /// <summary>Copy the payload, create shortcuts, and register the uninstall entry.</summary>
    /// <param name="directory">Target directory.</param>
    /// <param name="desktopShortcut">Whether to put a shortcut on the desktop.</param>
    /// <param name="startMenuShortcut">Whether to add a Start Menu entry.</param>
    /// <param name="message">Progress sink.</param>
    private static void Install(string directory, bool desktopShortcut, bool startMenuShortcut, Action<string> message)
    {
        if (string.IsNullOrWhiteSpace(directory)) throw new InvalidOperationException("请先填写安装目录。");

        Directory.CreateDirectory(directory);
        message($"正在解压到 {directory} …");

        using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(Payload)
            ?? throw new InvalidOperationException("这个安装包不完整:没有找到内置的程序文件,请重新下载。"))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Read))
        {
            // The published zip keeps everything under one folder so that
            // extracting it by hand in Explorer makes a folder instead of a mess;
            // an install has its own target folder, so that level is dropped.
            var root = CommonRoot(archive);

            foreach (var entry in archive.Entries)
            {
                var name = entry.FullName.Replace('\\', '/');
                if (name.EndsWith('/') || entry.Name.Length == 0) continue;

                var relative = root.Length > 0 && name.StartsWith(root, StringComparison.Ordinal)
                    ? name[root.Length..]
                    : name;
                if (relative.Length == 0) continue;

                var target = Path.Combine(directory, relative.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                entry.ExtractToFile(target, overwrite: true);
            }
        }

        var exe = Path.Combine(directory, ProductName + ".exe");
        if (!File.Exists(exe)) throw new InvalidOperationException($"解压后没有找到 {ProductName}.exe。");

        if (startMenuShortcut)
        {
            CreateShortcut(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs", ProductName + ".lnk"), exe, directory);
            message("已添加到开始菜单");
        }

        if (desktopShortcut)
        {
            CreateShortcut(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), ProductName + ".lnk"), exe, directory);
            message("已创建桌面快捷方式");
        }

        RegisterUninstall(directory, exe);
        message("已登记到「应用和功能」");
    }

    /// <summary>The single top-level folder every entry shares, or an empty string.</summary>
    /// <param name="archive">The payload.</param>
    /// <returns>The prefix to strip, ending with a slash.</returns>
    private static string CommonRoot(ZipArchive archive)
    {
        string? root = null;
        foreach (var entry in archive.Entries)
        {
            // Compress-Archive in Windows PowerShell writes backslash separators,
            // so both spellings have to be understood here.
            var name = entry.FullName.Replace('\\', '/');
            if (name.EndsWith('/')) continue;

            var slash = name.IndexOf('/');
            if (slash <= 0) return string.Empty;

            var first = name[..(slash + 1)];
            if (root is null) root = first;
            else if (!root.Equals(first, StringComparison.Ordinal)) return string.Empty;
        }

        return root ?? string.Empty;
    }

    /// <summary>Write a .lnk through the shell's own shortcut object.</summary>
    /// <param name="shortcutPath">Where the shortcut goes.</param>
    /// <param name="target">The executable it points at.</param>
    /// <param name="workingDirectory">Working directory for the target.</param>
    private static void CreateShortcut(string shortcutPath, string target, string workingDirectory)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException("系统里没有 WScript.Shell,无法创建快捷方式。");

        dynamic shell = Activator.CreateInstance(shellType)!;
        dynamic link = shell.CreateShortcut(shortcutPath);
        link.TargetPath = target;
        link.WorkingDirectory = workingDirectory;
        link.IconLocation = target;
        link.Description = "屏幕实时翻译";
        link.Save();
    }

    /// <summary>Register the Programs-and-features entry, whose uninstall command is the app itself.</summary>
    /// <param name="directory">Install directory.</param>
    /// <param name="exe">The installed executable.</param>
    private static void RegisterUninstall(string directory, string exe)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RegistryKey)
            ?? throw new InvalidOperationException("无法写入注册表,安装目录已经复制完成,可以从那里直接运行。");

        key.SetValue("DisplayName", ProductName);
        key.SetValue("DisplayVersion", Version);
        key.SetValue("Publisher", ProductName);
        key.SetValue("DisplayIcon", exe);
        key.SetValue("InstallLocation", directory);
        key.SetValue("UninstallString", $"\"{exe}\" --uninstall");
        key.SetValue("QuietUninstallString", $"\"{exe}\" --uninstall --quiet");
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);

        var bytes = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Sum(file => new FileInfo(file).Length);
        key.SetValue("EstimatedSize", (int)(bytes / 1024), RegistryValueKind.DWord);
    }

    private static void Launch(string directory)
    {
        var exe = Path.Combine(directory, ProductName + ".exe");
        if (!File.Exists(exe)) return;

        Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = directory, UseShellExecute = true });
    }

    /// <inheritdoc />
    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        if (!_installed) return;
    }
}
