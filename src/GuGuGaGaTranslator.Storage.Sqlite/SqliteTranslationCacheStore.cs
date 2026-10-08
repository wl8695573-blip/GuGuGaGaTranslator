using System.ComponentModel;
using System.IO;
using System.Security.Cryptography;
using GuGuGaGaTranslator.Core.Config;
using GuGuGaGaTranslator.Core.Translation;
using Microsoft.Data.Sqlite;

namespace GuGuGaGaTranslator.Storage.Sqlite;

/// <summary>Bounded SQLite storage containing request hashes and DPAPI-encrypted translations only.</summary>
public sealed class SqliteTranslationCacheStore : ITranslationCacheStore
{
    private readonly SqliteConnection _connection;
    private readonly int _retentionDays;
    private readonly int _capacity;
    private readonly Func<DateTimeOffset> _clock;
    private readonly object _gate = new();
    private readonly string _path;

    public SqliteTranslationCacheStore(string path, int retentionDays = 30, int capacity = 10000,
        Func<DateTimeOffset>? clock = null)
    {
        _retentionDays = Math.Clamp(retentionDays, 1, 365);
        _capacity = Math.Clamp(capacity, 1, 100000);
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        var fullPath = Path.GetFullPath(path);
        _path = fullPath;
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        _connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = fullPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
            DefaultTimeout = 3,
        }.ToString());
        try
        {
            _connection.Open();
            using var version = Command("PRAGMA user_version;");
            if (Convert.ToInt32(version.ExecuteScalar()) is not (0 or 1))
                throw new InvalidDataException("Unsupported translation cache schema.");
            using var setup = Command("""
                PRAGMA secure_delete=ON;
                CREATE TABLE IF NOT EXISTS translations (
                    key TEXT PRIMARY KEY, value TEXT NOT NULL,
                    created_at INTEGER NOT NULL, accessed_at INTEGER NOT NULL);
                CREATE INDEX IF NOT EXISTS translations_access ON translations(accessed_at);
                PRAGMA user_version=1;
                """);
            setup.ExecuteNonQuery();
            Prune();
        }
        catch { _connection.Dispose(); throw; }
    }

    public string? Get(string key)
    {
        lock (_gate)
        {
            string encrypted;
                using (var select = Command("SELECT value FROM translations WHERE key=$key AND created_at > $cutoff;"))
            {
                select.Parameters.AddWithValue("$key", key);
                select.Parameters.AddWithValue("$cutoff", Cutoff());
                if (select.ExecuteScalar() is not string value)
                    return null;
                encrypted = value;
            }
            string translation;
            try
            {
                if (!encrypted.StartsWith(SecretProtection.Prefix, StringComparison.Ordinal))
                    throw new CryptographicException("Cache value is not protected.");
                translation = SecretProtection.Unprotect(encrypted);
            }
            catch (Exception error) when (error is CryptographicException or FormatException or Win32Exception)
            {
                using var remove = Command("DELETE FROM translations WHERE key=$key;");
                remove.Parameters.AddWithValue("$key", key);
                remove.ExecuteNonQuery();
                return null;
            }
            using var touch = Command("UPDATE translations SET accessed_at=$now WHERE key=$key;");
            touch.Parameters.AddWithValue("$now", _clock().ToUnixTimeMilliseconds());
            touch.Parameters.AddWithValue("$key", key);
            touch.ExecuteNonQuery();
            return translation;
        }
    }

    public void Set(string key, string translation)
    {
        if (translation.Length > 65536)
            return;
        lock (_gate)
        {
            using var command = Command("""
                INSERT INTO translations(key,value,created_at,accessed_at) VALUES($key,$value,$now,$now)
                ON CONFLICT(key) DO UPDATE SET value=excluded.value,created_at=excluded.created_at,accessed_at=excluded.accessed_at;
                """);
            command.Parameters.AddWithValue("$key", key);
            command.Parameters.AddWithValue("$value", SecretProtection.Protect(translation));
            command.Parameters.AddWithValue("$now", _clock().ToUnixTimeMilliseconds());
            command.ExecuteNonQuery();
            Prune();
        }
    }

    private long Cutoff() => _clock().AddDays(-_retentionDays).ToUnixTimeMilliseconds();

    private void Prune()
    {
        using var prune = Command("""
            DELETE FROM translations WHERE created_at <= $cutoff;
            DELETE FROM translations WHERE key NOT IN
                (SELECT key FROM translations ORDER BY accessed_at DESC, rowid DESC LIMIT $capacity);
            """);
        prune.Parameters.AddWithValue("$cutoff", Cutoff());
        prune.Parameters.AddWithValue("$capacity", _capacity);
        prune.ExecuteNonQuery();
    }

    public void Clear()
    {
        lock (_gate)
        {
            using var clear = Command("DELETE FROM translations; VACUUM;");
            clear.ExecuteNonQuery();
        }
    }

    public CacheStorageStatistics GetStatistics()
    {
        lock (_gate)
        {
            using var count = Command("SELECT COUNT(*) FROM translations WHERE created_at > $cutoff;");
            count.Parameters.AddWithValue("$cutoff", Cutoff());
            return new(Convert.ToInt64(count.ExecuteScalar()), File.Exists(_path) ? new FileInfo(_path).Length : 0);
        }
    }

    public DateTimeOffset? GetExpiresAt(string key)
    {
        lock (_gate)
        {
            using var select = Command("SELECT created_at FROM translations WHERE key=$key;");
            select.Parameters.AddWithValue("$key", key);
            return select.ExecuteScalar() is long created ? DateTimeOffset.FromUnixTimeMilliseconds(created).AddDays(_retentionDays) : null;
        }
    }

    public int CleanExpired()
    {
        lock (_gate)
        {
            using var count = Command("SELECT COUNT(*) FROM translations;");
            var before = Convert.ToInt32(count.ExecuteScalar());
            Prune();
            var removed = before - Convert.ToInt32(count.ExecuteScalar());
            using var compact = Command("VACUUM;");
            compact.ExecuteNonQuery();
            return removed;
        }
    }

    private SqliteCommand Command(string sql)
    {
        var command = _connection.CreateCommand();
        command.CommandText = sql;
        return command;
    }

    public void Dispose()
    {
        lock (_gate)
            _connection.Dispose();
    }
}
