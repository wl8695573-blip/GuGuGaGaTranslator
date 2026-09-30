namespace GuGuGaGaTranslator.Core.Translation;

/// <summary>Which engine to use and how to reach it; the default points at a local OpenAI-compatible server.</summary>
public sealed record TranslatorConfig
{
    public string Provider { get; init; } = "mock";

    /// <summary>The API root of an OpenAI-compatible endpoint; the default is Ollama's.</summary>
    public string BaseUrl { get; init; } = "http://localhost:11434/v1";

    public string Model { get; init; } = "qwen2.5:7b-instruct";

    public string ApiKey { get; init; } = string.Empty;

    /// <summary>The account id for a classic translation API: 有道's <c>appKey</c>, 百度's <c>appid</c>.</summary>
    public string AppId { get; init; } = string.Empty;

    /// <summary>The signing secret for a classic translation API: 有道's <c>appSecret</c>, 百度's <c>密钥</c>, 彩云's token.</summary>
    public string AppSecret { get; init; } = string.Empty;

    /// <summary>An optional domain hint; 有道's <c>game</c> domain works for 中英 only.</summary>
    public string? Domain { get; init; }

    /// <summary>Replaces a classic API's own endpoint; used by the probe's stand-in server as well as by a proxy.</summary>
    public string? EndpointOverride { get; init; }

    public int TimeoutSeconds { get; init; } = 60;

    public double Temperature { get; init; } = 0.2;

    public string? StyleHint { get; init; }

    /// <summary>Instruction format: <c>galgame</c> for any chat model, or <c>sakura</c> for the Sakura-GalTransl family,
    /// which was trained on its own wording.</summary>
    public string PromptStyle { get; init; } = "galgame";

    /// <summary>关闭已知服务支持的思考模式；自定义接口不附加服务商字段。</summary>
    public bool DisableThinking { get; init; } = true;
}

/// <summary>Builds the configured translator.</summary>
public static class TranslatorFactory
{
    /// <summary>Create the translator named by a configuration.</summary>
    public static ITranslator Create(TranslatorConfig config) => config.Provider.ToLowerInvariant() switch
    {
        "mock" or "" => new MockTranslator(),
        "openai-compatible" or "openai" or "local" => new OpenAiCompatibleTranslator(new OpenAiTranslatorOptions
        {
            BaseUrl = config.BaseUrl,
            Model = config.Model,
            ApiKey = config.ApiKey,
            TimeoutSeconds = config.TimeoutSeconds,
            Temperature = config.Temperature,
            PromptStyle = ParsePromptStyle(config.PromptStyle),
            DisableThinking = config.DisableThinking,
        }),
        // Each classic API is a different protocol — its own endpoint, its own way
        // of proving who is calling, its own names for the languages.
        "youdao" or "有道" => new YoudaoTranslator(Classic(config)),
        "baidu" or "百度" => new BaiduTranslator(Classic(config)),
        "caiyun" or "彩云" or "彩云小译" => new CaiyunTranslator(Classic(config)),
        _ => throw new ArgumentException(
            $"unknown translator provider '{config.Provider}' "
            + "(known: mock, openai-compatible, youdao, baidu, caiyun)",
            nameof(config)),
    };

    /// <summary>Fold a translator configuration into the classic APIs' shared options.</summary>
    private static ClassicApiOptions Classic(TranslatorConfig config) => new(
        AppId: config.AppId,
        AppSecret: config.AppSecret,
        Token: config.ApiKey,
        Domain: config.Domain,
        TimeoutSeconds: config.TimeoutSeconds,
        EndpointOverride: config.EndpointOverride);

    /// <summary>术语表生成使用较长超时，至少 240 秒。</summary>
    public static ITranslator CreateForTermSheet(TranslatorConfig config) =>
        Create(config with
        {
            TimeoutSeconds = Math.Max(config.TimeoutSeconds, 240)
        });

    /// <summary>Read the prompt style, defaulting to the general one so an unknown value degrades instead of throwing.</summary>
    private static PromptStyle ParsePromptStyle(string value) =>
        Enum.TryParse<PromptStyle>(value, ignoreCase: true, out var style) ? style : PromptStyle.Galgame;
}
