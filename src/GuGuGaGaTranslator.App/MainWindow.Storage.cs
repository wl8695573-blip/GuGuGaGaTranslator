using System.IO;
using System.Windows;
using System.Windows.Controls;
using GuGuGaGaTranslator.Core.Config;
using Microsoft.Win32;

namespace GuGuGaGaTranslator.App;

public partial class MainWindow
{
    private bool _storageBusy;
    private CancellationTokenSource? _storageMigrationCancellation;
    private void LoadStorageControls()
    {
        DataRootBox.Text = _session.Store.Directory;
        CacheDirectoryBox.Text = _session.Config.Storage.CacheDirectory;
        LogDirectoryBox.Text = _session.Config.Storage.LogDirectory;
        ExportDirectoryBox.Text = _session.Config.Storage.ExportDirectory;
        UpdateDirectoryBox.Text = _session.Config.Storage.UpdateDirectory;
        StorageStatusText.Text = $"当前配置：{_session.Store.FilePath}\n缓存：{_session.CacheDatabasePath}\n证据：{_session.DumpDirectory}";
    }

    private static string CheckedOptionalDirectory(TextBox box) => string.IsNullOrWhiteSpace(box.Text) ? "" : DataDirectory.Validate(box.Text.Trim());
    private void ReadStorageControls()
    {
        var cache = CheckedOptionalDirectory(CacheDirectoryBox);
        var logs = CheckedOptionalDirectory(LogDirectoryBox);
        var exports = CheckedOptionalDirectory(ExportDirectoryBox);
        var updates = CheckedOptionalDirectory(UpdateDirectoryBox);
        var evidence = CheckedOptionalDirectory(DumpDirBox);
        _session.Config.Storage.CacheDirectory = cache;
        _session.Config.Storage.LogDirectory = logs;
        _session.Config.Storage.ExportDirectory = exports;
        _session.Config.Storage.UpdateDirectory = updates;
        _session.Config.Debug.DumpDirectory = evidence;
    }

    private void BrowseDirectory(TextBox field)
    {
        var dialog = new OpenFolderDialog { Title = "选择保存目录", Multiselect = false };
        if (Directory.Exists(field.Text)) dialog.InitialDirectory = field.Text;
        if (dialog.ShowDialog(this) == true) field.Text = dialog.FolderName;
    }
    private void OnBrowseDataRoot(object sender, RoutedEventArgs e) => BrowseDirectory(DataRootBox);
    private void OnBrowseCacheDirectory(object sender, RoutedEventArgs e) => BrowseDirectory(CacheDirectoryBox);
    private void OnBrowseLogDirectory(object sender, RoutedEventArgs e) => BrowseDirectory(LogDirectoryBox);
    private void OnBrowseExportDirectory(object sender, RoutedEventArgs e) => BrowseDirectory(ExportDirectoryBox);
    private void OnBrowseUpdateDirectory(object sender, RoutedEventArgs e) => BrowseDirectory(UpdateDirectoryBox);
    private void OnBrowseDumpDirectory(object sender, RoutedEventArgs e) => BrowseDirectory(DumpDirBox);
    private void OnBrowseModelsDirectory(object sender, RoutedEventArgs e) => BrowseDirectory(RapidModelsBox);

    private async void OnApplyStorage(object sender, RoutedEventArgs e) => await SaveSettingsAsync();

    private async void OnMigrateData(object sender, RoutedEventArgs e)
    {
        MigrateDataButton.IsEnabled = false;
        _storageBusy = true;
        SettingsTabs.IsEnabled = WindowList.IsEnabled = false;
        _floatingBall?.Hide();
        using var cancellation = new CancellationTokenSource();
        _storageMigrationCancellation = cancellation;
        try
        {
            await _session.RelocateDataAsync(DataRootBox.Text.Trim(), cancellation.Token);
            _overlay?.Dismiss();
            DumpDirBox.Text = _session.Config.Debug.DumpDirectory;
            LoadStorageControls();
            OnNotice("数据目录已迁移，配置与账户密钥继续使用；原目录保留为备份。翻译已停止，请重新开始。");
        }
        catch (OperationCanceledException) { OnNotice("迁移已取消，原目录继续使用。新目录可能有部分副本，请选择空目录重试。"); }
        catch (Exception error) { OnNotice("迁移未完成，原数据目录保留：" + error.Message); }
        finally
        {
            _storageBusy = false;
            _storageMigrationCancellation = null;
            if (!_closing)
            {
                MigrateDataButton.IsEnabled = SettingsTabs.IsEnabled = WindowList.IsEnabled = true;
                _floatingBall?.Show();
            }
        }
    }

    private void OnClearMemoryCache(object sender, RoutedEventArgs e)
    {
        _session.Cache.ClearMemory();
        _session.Pipeline?.InvalidateTranslation();
        OnNotice("已清空内存缓存，磁盘缓存仍保留。");
    }
    private void OnPruneCache(object sender, RoutedEventArgs e)
    {
        try { OnNotice($"已清理 {_session.Cache.CleanExpired()} 条过期或超出容量的缓存记录。"); }
        catch (Exception) { OnNotice("缓存清理失败，请检查目录权限和剩余空间。"); }
    }
    private void OnRefreshCacheStats(object sender, RoutedEventArgs e) => UpdateCacheStatus();

    private string PrepareExportDirectory()
    {
        try { Directory.CreateDirectory(_session.ExportDirectory); return _session.ExportDirectory; }
        catch (Exception) { OnNotice("默认导出目录不可用，请在保存窗口中选择其他目录。"); return ""; }
    }

    private string PrepareUpdateDirectory()
    {
        try { Directory.CreateDirectory(_session.UpdateDirectory); return _session.UpdateDirectory; }
        catch (Exception) { OnNotice("默认下载目录不可用，请在保存窗口中选择其他目录。"); return ""; }
    }
}
