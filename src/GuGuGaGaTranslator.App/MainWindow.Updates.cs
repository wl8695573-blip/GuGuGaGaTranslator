using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using GuGuGaGaTranslator.Core.Config;
using GuGuGaGaTranslator.Core.Updates;
using Microsoft.Win32;

namespace GuGuGaGaTranslator.App;

public partial class MainWindow
{
    private readonly UpdateClient _updateClient = new();
    private TermUpdate? _termUpdate;
    private AppUpdate? _appUpdate;
    private CancellationTokenSource? _updateCancellation;
    private bool _checkingUpdates;
    private static string ApplicationVersion => typeof(App).Assembly.GetName().Version?.ToString(3) ?? "1.5.0";

    private void ShowUpdateControls()
    {
        ShowMainControls();
        SettingsTabs.SelectedIndex = 5;
    }

    private void OnShowUpdates(object sender, RoutedEventArgs e) => ShowUpdateControls();

    private void UpdateNotification()
    {
        var available = _termUpdate is not null || _appUpdate is not null;
        UpdateBadgeButton.Visibility = available ? Visibility.Visible : Visibility.Collapsed;
        _floatingBall?.SetUpdateAvailable(available);
    }

    private void InitializeUpdates()
    {
        StartupUpdateCheck.IsChecked = _session.Config.Updates.CheckOnStartup;
        AutoTermsCheck.IsChecked = _session.Config.Updates.AutoApplyTerms;
        UpdateStatusText.Text = $"程序 {ApplicationVersion} · 公共词库 {_session.TermLibraryVersion}";
        if (_session.Config.Updates.CheckOnStartup
            && Path.GetFullPath(_session.Store.Directory).Equals(Path.GetFullPath(ConfigStore.DefaultDirectory()), StringComparison.OrdinalIgnoreCase)
            && (_session.Config.Updates.LastCheck is null || DateTimeOffset.UtcNow - _session.Config.Updates.LastCheck > TimeSpan.FromDays(1)))
            _ = CheckUpdatesAsync(automatic: true);
    }

    private void OnUpdateOptionsChanged(object sender, RoutedEventArgs e)
    {
        _session.Config.Updates.CheckOnStartup = StartupUpdateCheck.IsChecked == true;
        _session.Config.Updates.AutoApplyTerms = AutoTermsCheck.IsChecked == true;
        try { _session.SaveConfig(); }
        catch (Exception) { OnNotice("更新偏好保存失败，请检查配置目录权限。"); }
    }

    private async void OnCheckUpdates(object sender, RoutedEventArgs e) => await CheckUpdatesAsync(automatic: false);

    private async Task CheckUpdatesAsync(bool automatic)
    {
        if (_checkingUpdates || _closing) return;
        _checkingUpdates = true;
        _termUpdate = null;
        _appUpdate = null;
        CheckUpdatesButton.IsEnabled = ApplyTermsButton.IsEnabled = InstallUpdateButton.IsEnabled = false;
        UpdateStatusText.Text = "正在检查公共词库与正式版本…";
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(35));
        _updateCancellation = cancellation;
        var messages = new List<string>();
        try
        {
            try
            {
                var offer = await _updateClient.CheckTermsAsync(cancellation.Token);
                _termUpdate = UpdateClient.Newer(offer.Version, _session.TermLibraryVersion) ? offer : null;
                messages.Add(_termUpdate is null ? "公共词库暂无更新" : $"公共词库 {offer.Version} 可更新");
            }
            catch (HttpRequestException error) when (error.StatusCode == System.Net.HttpStatusCode.NotFound)
            { messages.Add("公共词库尚未在 GitHub 发布，可暂用内置版本或导入档案"); }
            catch (Exception) { messages.Add("公共词库检查失败，可稍后手动重试"); }
            if (_closing) return;
            try
            {
                _appUpdate = await _updateClient.CheckAppAsync(ApplicationVersion, cancellation.Token);
                messages.Add(_appUpdate is null ? "暂无更高正式版本" : $"程序 {_appUpdate.Version} 可更新");
            }
            catch (Exception) { messages.Add("程序版本检查失败，可稍后手动重试"); }
            if (_closing) return;
            if (_termUpdate is not null && _session.Config.Updates.AutoApplyTerms)
            {
                await ApplyTermOfferAsync(_termUpdate, cancellation.Token);
                messages.Add("公共词库已自动更新，保留个人修改");
            }
            _session.Config.Updates.LastCheck = DateTimeOffset.UtcNow;
            _session.SaveConfig();
            UpdateStatusText.Text = string.Join("；", messages);
            if (!automatic || _termUpdate is not null || _appUpdate is not null) OnNotice(UpdateStatusText.Text);
        }
        catch (Exception) { if (!_closing) UpdateStatusText.Text = "更新检查未完成，已有程序和术语保留；请稍后重试。"; }
        finally
        {
            _checkingUpdates = false;
            _updateCancellation = null;
            if (!_closing)
            {
                CheckUpdatesButton.IsEnabled = true;
                ApplyTermsButton.IsEnabled = _termUpdate is not null;
                InstallUpdateButton.IsEnabled = _appUpdate is not null;
                UpdateNotification();
            }
        }
    }

    private async Task ApplyTermOfferAsync(TermUpdate offer, CancellationToken cancellationToken)
    {
        var profile = await _updateClient.DownloadTermsAsync(offer, cancellationToken);
        if (_closing) return;
        await _session.UpdateTermLibraryAsync(profile);
        _termUpdate = null;
        SyncProfileControls();
    }

    private async void OnApplyTermsUpdate(object sender, RoutedEventArgs e)
    {
        if (_termUpdate is not { } offer || _checkingUpdates) return;
        _checkingUpdates = true;
        ApplyTermsButton.IsEnabled = CheckUpdatesButton.IsEnabled = InstallUpdateButton.IsEnabled = false;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(35));
        _updateCancellation = cancellation;
        try
        {
            await ApplyTermOfferAsync(offer, cancellation.Token);
            if (!_closing) UpdateStatusText.Text = $"公共词库已更新到 {offer.Version}，个人术语与档案修改保留。";
        }
        catch (Exception) { if (!_closing) UpdateStatusText.Text = "词库更新失败，已有词库保留；请重试或导入本地档案。"; }
        finally
        {
            _checkingUpdates = false; _updateCancellation = null;
            if (!_closing)
            {
                CheckUpdatesButton.IsEnabled = true;
                ApplyTermsButton.IsEnabled = _termUpdate is not null;
                InstallUpdateButton.IsEnabled = _appUpdate is not null;
                UpdateNotification();
            }
        }
    }

    private async void OnInstallAppUpdate(object sender, RoutedEventArgs e)
    {
        if (_appUpdate is not { } offer || _checkingUpdates) return;
        var dialog = new SaveFileDialog { FileName = offer.InstallerName, Filter = "LCTA 安装包 (*.exe)|*.exe",
            InitialDirectory = AppContext.BaseDirectory, Title = "选择新版安装包保存位置" };
        if (dialog.ShowDialog(this) != true) return;
        if (Path.GetFileName(dialog.FileName) != offer.InstallerName)
        { OnNotice("请保留发行文件名，便于核对版本。"); return; }
        _checkingUpdates = true;
        InstallUpdateButton.IsEnabled = CheckUpdatesButton.IsEnabled = ApplyTermsButton.IsEnabled = false;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        _updateCancellation = cancellation;
        try
        {
            UpdateStatusText.Text = "正在下载安装包并核对 SHA256；完成后打开安装器。";
            await _updateClient.DownloadInstallerAsync(offer, dialog.FileName, cancellation.Token);
            if (_closing) return;
            await _session.StopAsync();
            _session.SaveConfig();
            Process.Start(new ProcessStartInfo(dialog.FileName) { UseShellExecute = true });
            Close();
        }
        catch (Exception) { if (!_closing) UpdateStatusText.Text = "安装包下载或校验失败，未启动安装；可稍后重试。"; }
        finally
        {
            _checkingUpdates = false; _updateCancellation = null;
            if (!_closing) { CheckUpdatesButton.IsEnabled = true; InstallUpdateButton.IsEnabled = true; ApplyTermsButton.IsEnabled = _termUpdate is not null; }
        }
    }
}
