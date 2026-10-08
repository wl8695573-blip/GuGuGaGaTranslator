using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GuGuGaGaTranslator.Core.Translation;

/// <summary>A bounded least-recently-used cache of finished translations. A visual novel repeats lines, and a second call
/// would cost money and add latency for an answer already known.</summary>
public sealed class TranslationCache : IDisposable
{
    private int _capacity;
    private bool _enabled = true;
    private bool _matchContext = true;
    private int _retentionDays = 30;
    private readonly Func<DateTimeOffset> _clock;
    private sealed record Entry(string Text, DateTimeOffset ExpiresAt);
    private readonly Dictionary<string, Entry> _entries;
    private readonly LinkedList<string> _recency = new();
    private readonly object _gate = new();
    private long _hits;
    private long _misses;
    private ITranslationCacheStore? _storage;

    public event Action<Exception>? StorageFailed;

    public void ConfigureStorage(ITranslationCacheStore? storage)
    {
        lock (_gate)
        {
            _storage?.Dispose();
            _storage = storage;
            _entries.Clear();
            _recency.Clear();
        }
    }

    public TranslationCache(int capacity = 2000, Func<DateTimeOffset>? clock = null)
    {
        _capacity = Math.Max(1, capacity);
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
    }

    public void Configure(bool enabled, int capacity, int retentionDays, bool matchContext)
    {
        lock (_gate)
        {
            if (_enabled != enabled || _matchContext != matchContext || _retentionDays != retentionDays)
            {
                _entries.Clear();
                _recency.Clear();
            }
            _enabled = enabled;
            _matchContext = matchContext;
            _capacity = Math.Clamp(capacity, 1, 20000);
            _retentionDays = Math.Clamp(retentionDays, 1, 365);
            Trim();
        }
    }

    private string RequestKey(string translatorId, TranslationRequest request) =>
        KeyFor((_matchContext ? "" : "ignore-context:") + translatorId,
            _matchContext ? request : request with { Context = [] });

    public long Hits => Interlocked.Read(ref _hits);

    public long Misses => Interlocked.Read(ref _misses);

    public int Count
    {
        get
        {
            lock (_gate)
                return _entries.Count;
        }
    }

    /// <summary>Look up a translation and mark it as recently used.</summary>
    public bool TryGet(string translatorId, TranslationRequest request, out string translation)
    {
        lock (_gate)
        {
            if (!_enabled) { translation = ""; return false; }
            var key = RequestKey(translatorId, request);
            if (_entries.TryGetValue(key, out var found) && found.ExpiresAt > _clock())
            {
                _recency.Remove(key);
                _recency.AddFirst(key);
                Interlocked.Increment(ref _hits);
                translation = found.Text;
                return true;
            }

            try
            {
                if (_storage?.Get(key) is { } persisted)
                {
                    Remember(key, persisted, _storage.GetExpiresAt(key));
                    Interlocked.Increment(ref _hits);
                    translation = persisted;
                    return true;
                }
            }
            catch (Exception error) { DisableFailedStorage(error); }
        }

        Interlocked.Increment(ref _misses);
        translation = string.Empty;
        return false;
    }

    /// <summary>Store a translation, evicting the least recently used entry when full.</summary>
    public void Set(string translatorId, TranslationRequest request, string translation)
    {
        if (string.IsNullOrEmpty(translation))
            return;

        lock (_gate)
        {
            if (!_enabled) return;
            var key = RequestKey(translatorId, request);
            Remember(key, translation);
            try
            {
                _storage?.Set(key, translation);
            }
            catch (Exception error) { DisableFailedStorage(error); }
        }
    }

    private void Remember(string key, string translation, DateTimeOffset? expiresAt = null)
    {
        if (_entries.ContainsKey(key))
            _recency.Remove(key);
        _entries[key] = new(translation, expiresAt ?? _clock().AddDays(_retentionDays));
        _recency.AddFirst(key);
        Trim();
    }

    private void Trim()
    {
        while (_entries.Count > _capacity && _recency.Last is { } oldest)
        {
            _recency.RemoveLast();
            _entries.Remove(oldest.Value);
        }
    }

    private void DisableFailedStorage(Exception error)
    {
        try
        {
            _storage?.Dispose();
        }
        catch { }
        _storage = null;
        StorageFailed?.Invoke(error);
    }

    /// <summary>Drop every entry, which the UI does when the engine or language pair changes.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
            _recency.Clear();
            // A failed clear must be reported; the UI must never claim that disk data was deleted.
            _storage?.Clear();
        }
    }

    public void ClearMemory()
    {
        lock (_gate)
        {
            _entries.Clear();
            _recency.Clear();
        }
    }

    public CacheStorageStatistics StorageStatistics()
    {
        lock (_gate)
        {
            try { return _storage?.GetStatistics() ?? new(0, 0); }
            catch (Exception error) { DisableFailedStorage(error); return new(0, 0); }
        }
    }

    public int CleanExpired()
    {
        lock (_gate)
        {
            var expired = _entries.Where(entry => entry.Value.ExpiresAt <= _clock()).Select(entry => entry.Key).ToArray();
            foreach (var key in expired) { _entries.Remove(key); _recency.Remove(key); }
            return expired.Length + (_storage?.CleanExpired() ?? 0);
        }
    }

    public void ResetStatistics() { Interlocked.Exchange(ref _hits, 0); Interlocked.Exchange(ref _misses, 0); }

    public void Dispose() => ConfigureStorage(null);

    /// <summary>
    /// The cache key for a request: engine, language pair, the glossary in force, and the normalized source text. The
    /// glossary belongs in it because the same line translated under a different game profile is a different translation.
    /// </summary>
    public static string KeyFor(string translatorId, TranslationRequest request) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new
            {
                translatorId,
                request
            }))));
}
