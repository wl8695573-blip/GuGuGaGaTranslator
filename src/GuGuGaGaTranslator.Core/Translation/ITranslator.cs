namespace GuGuGaGaTranslator.Core.Translation;

/// <summary>One fixed translation for a term, applied on every request that carries it.</summary>
/// <param name="Forbidden">Wrong wordings, sent to the model as negative examples and rewritten by <c>TermEnforcer</c> in the finished text.</param>
public sealed record GlossaryEntry(string Source, string Target, IReadOnlyList<string>? Forbidden = null);

/// <summary>One line of dialogue context; a rolling window of these keeps pronouns, names, and tone consistent.</summary>
public sealed record TranslationHistory(string Source, string Translation);

/// <summary>What a translator is asked to translate.</summary>
public sealed record TranslationRequest
{
    public required string Text { get; init; }

    /// <summary>Source language tag, or <c>auto</c> to let the engine detect it.</summary>
    public required string From { get; init; }

    public required string To { get; init; }

    public IReadOnlyList<GlossaryEntry> Glossary { get; init; } = [];

    public string? StyleHint { get; init; }

    /// <summary>作品背景，供翻译请求使用。</summary>
    public string? Worldview { get; init; }

    /// <summary>The lines translated before this one, oldest first.</summary>
    public IReadOnlyList<TranslationHistory> Context { get; init; } = [];
}

/// <summary>支持流式译文回调的翻译器。</summary>
public interface IStreamingTranslator : ITranslator
{
    /// <summary>Translate, calling <paramref name="onDelta"/> with the text so far as it arrives.</summary>
    /// <param name="onDelta">Receives the partial translation, growing each call.</param>
    Task<string> TranslateAsync(
        TranslationRequest request,
        Action<string> onDelta,
        CancellationToken cancellationToken = default);
}

/// <summary>独立聊天请求接口，用于生成术语表。</summary>
public interface IChatCompleter
{
    /// <summary>Ask the model one question and return its reply.</summary>
    Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default);
}

/// <summary>A translation engine; implementations must be safe to call concurrently, and a throw means "leave the previous translation in place".</summary>
public interface ITranslator
{
    /// <summary>A stable identifier for logs, cache keys, and dumps.</summary>
    string Id
    {
        get;
    }

    /// <summary>True when this engine needs a network round trip, which the UI surfaces.</summary>
    bool RequiresNetwork
    {
        get;
    }

    /// <summary>Translate one request.</summary>
    Task<string> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken = default);
}
