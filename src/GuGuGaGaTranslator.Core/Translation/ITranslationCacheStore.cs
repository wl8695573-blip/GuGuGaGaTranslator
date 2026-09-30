namespace GuGuGaGaTranslator.Core.Translation;

/// <summary>Optional persistence behind the in-memory cache. Keys contain no original text.</summary>
public interface ITranslationCacheStore : IDisposable
{
    string? Get(string key);
    void Set(string key, string translation);
    void Clear();
}
