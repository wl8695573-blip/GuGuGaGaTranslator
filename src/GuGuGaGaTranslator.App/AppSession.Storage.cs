using System.IO;
using GuGuGaGaTranslator.Core.Config;

namespace GuGuGaGaTranslator.App;

public sealed partial class AppSession
{
    private void MigrateLegacyCache()
    {
        if (!string.IsNullOrWhiteSpace(Config.Storage.CacheDirectory)) return;
        var old = Path.Combine(Store.Directory, "translations.sqlite");
        if (!File.Exists(old) || File.Exists(CacheDatabasePath)) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CacheDatabasePath)!);
            // 有未完成事务时由原位置的 SQLite 自行恢复，避免只复制数据库丢失事务。
            if (File.Exists(old + "-journal") || File.Exists(old + "-wal"))
                Config.Storage.CacheDirectory = Store.Directory;
            else File.Move(old, CacheDatabasePath, overwrite: false);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Config.Storage.CacheDirectory = Store.Directory;
        }
    }

    public async Task RelocateDataAsync(string destination, CancellationToken cancellationToken = default)
    {
        await ChangeSettingsAsync(async () =>
        {
            var next = DataDirectory.Validate(destination);
            await StopAsync();
            Store.Save(Config);
            var previous = Store;
            var oldDump = Config.Debug.DumpDirectory;
            var oldCache = Config.Storage.CacheDirectory;
            Cache.ConfigureStorage(null);
            _cacheSettings = null;
            try
            {
                await Task.Run(() => DataDirectory.CopyForMigration(previous.Directory, next, cancellationToken), cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (Path.GetFullPath(DumpDirectory).Equals(Path.Combine(previous.Directory, "dumps"), StringComparison.OrdinalIgnoreCase))
                    Config.Debug.DumpDirectory = "";
                if (oldCache.Equals(previous.Directory, StringComparison.OrdinalIgnoreCase)) Config.Storage.CacheDirectory = "";
                var nextStore = new ConfigStore(next);
                nextStore.Save(Config);
                if (UsesDefaultDataDirectory) DataDirectory.Remember(next);
                Store = nextStore;
                Diagnostics.ConfigureDirectory(Store.Directory, Config.Storage.LogDirectory);
                MigrateLegacyCache();
                ConfigureCache(force: true);
            }
            catch
            {
                Store = previous;
                Config.Debug.DumpDirectory = oldDump;
                Config.Storage.CacheDirectory = oldCache;
                ConfigureCache(force: true);
                throw;
            }
        });
    }
}
