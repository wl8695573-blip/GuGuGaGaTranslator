using System.Windows;
using System.Windows.Threading;

namespace GuGuGaGaTranslator.App;

/// <summary>Application entry point. The window is created in code, not through
/// <c>StartupUri</c>, so the session exists before the first window renders.</summary>
public partial class App : Application
{
    private Mutex? _instanceMutex;
    private bool _ownsMutex;

    public AppSession Session { get; private set; } = null!;

    /// <inheritdoc />
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (e.Args.Contains("--verify-installation"))
        {
            Shutdown(PackageSmoke.Run(e.Args));
            return;
        }

        // 由安装程序注册的卸载入口:删快捷方式与注册表项,再安排删除自己所在的目录。
        if (e.Args.Any(arg => arg.Equals("--uninstall", StringComparison.OrdinalIgnoreCase)))
        {
            var steps = new List<string>();
            var ok = Uninstall.Run(steps.Add);
            var quiet = e.Args.Any(arg => arg.Equals("--quiet", StringComparison.OrdinalIgnoreCase));

            if (!quiet)
            {
                MessageBox.Show(
                    string.Join(Environment.NewLine, steps)
                        + (ok ? Environment.NewLine + Environment.NewLine + "关闭此提示后完成文件清理。" : string.Empty),
                    "卸载 GuGuGaGaTranslator",
                    MessageBoxButton.OK,
                    ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
            }


            Shutdown(ok ? 0 : 1);
            return;
        }

        _instanceMutex = new Mutex(false, @"Local\GuGuGaGaTranslator-" + System.Security.Principal.WindowsIdentity.GetCurrent().User!.Value);
        try { _ownsMutex = _instanceMutex.WaitOne(0); }
        catch (AbandonedMutexException) { _ownsMutex = true; }
        if (!_ownsMutex)
        {
            MessageBox.Show("程序已经在运行，请使用已打开的窗口。", "GuGuGaGaTranslator");
            Shutdown();
            return;
        }

        // 清单里已经声明了 per-monitor-v2;这里是程序化的一份,兼顾忽略清单的宿主。
        GuGuGaGaTranslator.Core.Interop.DpiAwareness.EnablePerMonitorV2();

        var configIndex = Array.IndexOf(e.Args, "--config-dir");
        var configDirectory = configIndex >= 0 && configIndex + 1 < e.Args.Length ? e.Args[configIndex + 1] : null;
        Session = new AppSession(configDirectory);
        Session.LoadConfig();

        // 设置卡片期间必须先设成显式退出:WPF 默认会在最后一个窗口关闭时立刻关闭程序,
        // 而此时主窗口还没加载,曾导致 OnClosed 抛「Hwnd of zero is not valid」。
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        if (!Session.Config.SetupCompleted)
        {
            var setup = new SetupWindow(Session);
            setup.ShowDialog();
            Session.Config.SetupCompleted = true;
            Session.SaveConfig();
        }

        var window = new MainWindow(Session);
        MainWindow = window;
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        window.Show();

        // 命令行:--autostart 立即开始翻译,--exit-after 秒数后自动退出,用于计划任务或冒烟验证。
        var autoStart = e.Args.Any(arg => arg.Equals("--autostart", StringComparison.OrdinalIgnoreCase));
        if (autoStart) window.StartAutomatically();

        var exitAfter = ReadSeconds(e.Args, "--exit-after");
        if (exitAfter > 0)
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(exitAfter) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                Shutdown();
            };
            timer.Start();
        }
    }

    /// <summary>Read a numeric option of the form <c>--name 12</c>.</summary>
    private static double ReadSeconds(string[] args, string name)
    {
        for (var index = 0; index < args.Length - 1; index++)
        {
            if (!args[index].Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
            if (double.TryParse(args[index + 1], System.Globalization.CultureInfo.InvariantCulture, out var value)) return value;
        }

        return 0;
    }

    /// <inheritdoc />
    protected override async void OnExit(ExitEventArgs e)
    {
        if (_ownsMutex) { _instanceMutex?.ReleaseMutex(); _ownsMutex = false; }
        _instanceMutex?.Dispose();
        if (Session is not null) await Session.DisposeAsync().ConfigureAwait(false);
        base.OnExit(e);
    }
}
