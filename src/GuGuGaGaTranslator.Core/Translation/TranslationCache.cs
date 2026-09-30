using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GuGuGaGaTranslator.Core.Translation;

/// <summary>A bounded least-recently-used cache of finished translations. A visual novel repeats lines, and a second call
/// would cost money and add latency for an answer already known.</summary>
public sealed class TranslationCache : IDisposable
{
    private readonly int _capacity;
    private readonly Dictionary<string, string> _entries;
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

    public TranslationCache(int capacity = 2000)
    {
        _capacity = Math.Max(1, capacity);
        _entries = new Dictionary<string, string>(StringComparer.Ordinal);
    }

    public long Hits => Interlocked.Read(ref _hits);

    public long Misses => Interlocked.Read(ref _misses);

    public int Count
    {
        get
        {
            lock (_gate) return _entries.Count;
        }
    }

    /// <summary>Look up a translation and mark it as recently used.</summary>
    public bool TryGet(string translatorId, TranslationRequest request, out string translation)
    {
        var key = KeyFor(translatorId, request);
        lock (_gate)
        {
            if (_entries.TryGetValue(key, out var found))
            {
                _recency.Remove(key);
                _recency.AddFirst(key);
                Interlocked.Increment(ref _hits);
                translation = found;
                return true;
            }

            try
            {
                if (_storage?.Get(key) is { } persisted)
                {
                    Remember(key, persisted);
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
        if (string.IsNullOrEmpty(translation)) return;

        var key = KeyFor(translatorId, request);
        lock (_gate)
        {
            Remember(key, translation);
            try { _storage?.Set(key, translation); }
            catch (Exception error) { DisableFailedStorage(error); }
        }
    }

    private void Remember(string key, string translation)
    {
        if (_entries.ContainsKey(key)) _recency.Remove(key);
        _entries[key] = translation;
        _recency.AddFirst(key);
        while (_entries.Count > _capacity && _recency.Last is { } oldest)
        {
            _recency.RemoveLast();
            _entries.Remove(oldest.Value);
        }
    }

    private void DisableFailedStorage(Exception error)
    {
        try { _storage?.Dispose(); } catch { }
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
        lock (_gate) { _entries.Clear(); _recency.Clear(); }
    }

    public void Dispose() => ConfigureStorage(null);

    /// <summary>
    /// The cache key for a request: engine, language pair, the glossary in force, and the normalized source text. The
    /// glossary belongs in it because the same line translated under a different game profile is a different translation.
    /// </summary>
    public static string KeyFor(string translatorId, TranslationRequest request) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new { translatorId, request }))));
}
