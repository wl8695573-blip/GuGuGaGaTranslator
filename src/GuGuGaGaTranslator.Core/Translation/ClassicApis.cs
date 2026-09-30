using System.Globalization;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GuGuGaGaTranslator.Core.Translation;

/// <summary>What every classic machine-translation API needs beyond the endpoint: an account, and a shared secret to sign requests with.</summary>
/// <param name="AppId">The account id: 有道 <c>appKey</c>, 百度 <c>appid</c>.</param>
/// <param name="AppSecret">The signing secret: 有道 <c>appSecret</c>, 百度 <c>密钥</c>.</param>
/// <param name="Token">A bearer-style token, for services that use one instead (彩云小译).</param>
/// <param name="EndpointOverride">Replaces the provider's URL; used by the probe's fake server.</param>
public sealed record ClassicApiOptions(
    string AppId = "",
    string AppSecret = "",
    string Token = "",
    string? Domain = null,
    int TimeoutSeconds = 30,
    string? EndpointOverride = null);

/// <summary>The family of "proper" translation APIs — 有道, 百度, 彩云小译, DeepL — as opposed to a chat model told to
/// translate. Each speaks its own dialect: its own endpoint, its own way of proving who is calling, its own names for the
/// languages, and its own answer shape, which is why each one is its own adapter.</summary>
public abstract class ClassicApiTranslator : ITranslator, IDisposable
{
    private protected readonly HttpClient Http;
    private protected readonly ClassicApiOptions Options;
    private readonly bool _ownsClient;

    protected ClassicApiTranslator(ClassicApiOptions options)
        : this(options, new HttpClient(), ownsClient: true)
    {
    }

    protected ClassicApiTranslator(ClassicApiOptions options, HttpClient client, bool ownsClient = false)
    {
        Options = options;
        Http = client;
        _ownsClient = ownsClient;
        Http.Timeout = TimeSpan.FromSeconds(Math.Max(1, options.TimeoutSeconds));
    }

    public abstract string Id { get;  }

    public bool RequiresNetwork => true;

    public abstract string DisplayName { get;  }

    // Custom endpoints and domain-specific results must not share a persistent cache.
    private protected string CacheId(string provider, string defaultEndpoint) => provider + ":" +
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            endpoint = Endpoint(defaultEndpoint),
            Options.Domain,
            Options.AppId
        })))).ToLowerInvariant();

    private protected virtual int MaxCharactersPerRequest => 1000;

    /// <summary>Translate one line, splitting it if the provider caps the request length.</summary>
    public async Task<string> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken = default)
    {
        var text = request.Text?.Trim() ?? string.Empty;
        if (text.Length == 0)
            return string.Empty;

        var pieces = Split(text, MaxCharactersPerRequest);
        var translated = new List<string>(pieces.Count);
        foreach (var piece in pieces)
        {
            translated.Add(await TranslateOneAsync(piece, request.From, request.To, cancellationToken).ConfigureAwait(false));
        }

        return string.Join("\n", translated);
    }

    /// <summary>Translate a single piece, in the provider's own protocol.</summary>
    private protected abstract Task<string> TranslateOneAsync(
        string text,
        string from,
        string to,
        CancellationToken cancellationToken);

    /// <summary>The endpoint to call: the provider's, or the probe's stand-in.</summary>
    private protected string Endpoint(string fallback) =>
        string.IsNullOrWhiteSpace(Options.EndpointOverride) ? fallback : Options.EndpointOverride!.Trim();

    /// <summary>Split a line that is longer than the provider accepts, at a sentence boundary where one exists.</summary>
    private static List<string> Split(string text, int limit)
    {
        if (text.Length <= limit)
            return [text];

        var pieces = new List<string>();
        var remaining = text.AsSpan();
        while (remaining.Length > limit)
        {
            var window = remaining[..limit];
            var cut = window.LastIndexOfAny("。！？!?.\n".AsSpan());
            cut = cut > limit / 3 ? cut + 1 : limit;
            pieces.Add(remaining[..cut].ToString().Trim());
            remaining = remaining[cut..];
        }

        if (remaining.Length > 0)
            pieces.Add(remaining.ToString().Trim());
        return pieces;
    }

    /// <summary>Post a form-encoded body and return the parsed JSON.</summary>
    private protected async Task<JsonDocument> PostFormAsync(
        string url,
        IEnumerable<KeyValuePair<string, string>> fields,
        CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(fields);
        using var response = await Http.PostAsync(url, content, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"{DisplayName} 返回 {(int)response.StatusCode} {response.ReasonPhrase}:{Truncate(body, 300)}");
        }

        try
        {
            return JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            throw new InvalidOperationException($"{DisplayName} 返回的不是 JSON:{Truncate(body, 300)}");
        }
    }

    /// <summary>Post a JSON body with extra headers and return the parsed response.</summary>
    private protected async Task<JsonDocument> PostJsonAsync(
        string url,
        string json,
        IEnumerable<KeyValuePair<string, string>> headers,
        CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        foreach (var (name, value) in headers)
            message.Headers.TryAddWithoutValidation(name, value);

        using var response = await Http.SendAsync(message, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"{DisplayName} 返回 {(int)response.StatusCode} {response.ReasonPhrase}:{Truncate(body, 300)}");
        }

        try
        {
            return JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            throw new InvalidOperationException($"{DisplayName} 返回的不是 JSON:{Truncate(body, 300)}");
        }
    }

    /// <summary>Raise the provider's own error, with the meaning of its code where it is known.</summary>
    private protected void Fail(string code, string? message, IReadOnlyDictionary<string, string> hints)
    {
        var hint = hints.TryGetValue(code, out var known) ? $"{known};" : string.Empty;
        throw new InvalidOperationException(
            $"{DisplayName} 报错 {code}:{hint}{message ?? "没有附带说明"} "
            + "(检查「识别与翻译」页里的应用 ID 与密钥是否填对、服务是否已开通)");
    }

    private protected static string Truncate(string text, int max) => text.Length <= max ? text : text[..max] + "…";

    /// <summary>Lower-case hex MD5, the signing hash 百度 asks for.</summary>
    private protected static string Md5Hex(string text) =>
        Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    /// <summary>Lower-case hex SHA-256, the signing hash 有道 asks for.</summary>
    private protected static string Sha256Hex(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    /// <summary>A fresh random salt, as both 有道 and 百度 require per request.</summary>
    private protected static string Salt() => Guid.NewGuid().ToString("N");

    /// <summary>Seconds since the Unix epoch, which 有道's <c>curtime</c> wants.</summary>
    private protected static string UnixSeconds() =>
        DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);

    /// <summary>Check that the account fields this provider needs are present, and return the one that acts as the identity.</summary>
    private protected virtual string RequireAccount()
    {
        if (string.IsNullOrWhiteSpace(Options.AppId) || string.IsNullOrWhiteSpace(Options.AppSecret))
        {
            throw new InvalidOperationException(
                $"{DisplayName} 需要「应用 ID」和「应用密钥」:在「识别与翻译」页填好,或写进 config.json 的 "
                + "translation.translator.appId / appSecret。");
        }

        return Options.AppId.Trim();
    }

    public void Dispose()
    {
        if (_ownsClient)
            Http.Dispose();
    }
}

/// <summary>有道智云 文本翻译: a form POST to <c>openapi.youdao.com</c> signed with
/// <c>SHA256(appKey + input + salt + curtime + appSecret)</c>; see
/// <see href="https://ai.youdao.com/DOCSIRMA/html/trans/api/wbfy/index.html">有道文本翻译 API</see>.</summary>
public sealed class YoudaoTranslator : ClassicApiTranslator
{
    private const string Url = "https://openapi.youdao.com/api";

    private static readonly Dictionary<string, string> ErrorHints = new()
    {
        ["101"] = "缺少必填参数",
        ["102"] = "不支持的语言类型",
        ["103"] = "翻译文本过长",
        ["108"] = "应用 ID 无效(去控制台确认应用已创建并绑定了文本翻译服务)",
        ["110"] = "应用没有绑定该服务",
        ["202"] = "签名校验失败(appKey/appSecret 不匹配,或文本编码问题)",
        ["203"] = "访问 IP 不在白名单",
        ["206"] = "时间戳无效(本机时间不准)",
        ["207"] = "重放请求(salt 重复)",
        ["401"] = "账户欠费",
        ["411"] = "访问频率受限",
    };

    /// <summary>Create the adapter; appKey goes in <c>AppId</c>, appSecret in <c>AppSecret</c>.</summary>
    public YoudaoTranslator(ClassicApiOptions options)
        : base(options)
    {
    }

    public YoudaoTranslator(ClassicApiOptions options, HttpClient client, bool ownsClient = false)
        : base(options, client, ownsClient)
    {
    }

    public override string Id => CacheId("youdao", Url);

    public override string DisplayName => "有道翻译";

    private protected override int MaxCharactersPerRequest => 4000;

    /// <summary>The value 有道 signs: the text itself up to twenty characters, and past that the first ten, the length,
    /// and the last ten. Getting this wrong produces error 202 and nothing else to go on.</summary>
    public static string SignInput(string text) =>
        text.Length <= 20 ? text : text[..10] + text.Length.ToString(CultureInfo.InvariantCulture) + text[^10..];

    /// <summary>The v3 signature for one request.</summary>
    public static string Sign(string appKey, string text, string salt, string curtime, string appSecret) =>
        Sha256Hex(appKey + SignInput(text) + salt + curtime + appSecret);

    /// <summary>Map a language tag onto 有道's own names.</summary>
    public static string Language(string tag) => tag.ToLowerInvariant() switch
    {
        "auto" => "auto",
        "ja" or "ja-jp" or "japanese" => "ja",
        "ko" or "korean" => "ko",
        "en" or "en-us" or "en-gb" or "english" => "en",
        "zh" or "zh-hans" or "zh-hans-cn" or "zh-cn" or "chinese" => "zh-CHS",
        "zh-hant" or "zh-hant-tw" or "traditional chinese" => "zh-CHT",
        _ => tag,
    };

    private protected override async Task<string> TranslateOneAsync(
        string text,
        string from,
        string to,
        CancellationToken cancellationToken)
    {
        var appKey = RequireAccount();
        var salt = Salt();
        var curtime = UnixSeconds();
        var sign = Sign(appKey, text, salt, curtime, Options.AppSecret.Trim());

        var fields = new List<KeyValuePair<string, string>>
        {
            new("q", text),
            new("from", Language(from)),
            new("to", Language(to)),
            new("appKey", appKey),
            new("salt", salt),
            new("sign", sign),
            new("signType", "v3"),
            new("curtime", curtime),
        };

        if (!string.IsNullOrWhiteSpace(Options.Domain))
            fields.Add(new KeyValuePair<string, string>("domain", Options.Domain!.Trim()));

        using var document = await PostFormAsync(Endpoint(Url), fields, cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;
        var errorCode = root.TryGetProperty("errorCode", out var code) ? code.GetString() ?? "0" : "0";
        if (errorCode != "0")
            Fail(errorCode, null, ErrorHints);

        if (!root.TryGetProperty("translation", out var translation) || translation.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("有道翻译没有返回 translation 字段");
        }

        var lines = translation.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString() ?? string.Empty);

        return string.Join("\n", lines).Trim();
    }
}

/// <summary>百度翻译开放平台: a form POST signed with <c>MD5(appid + q + salt + 密钥)</c>, the simplest signing scheme of
/// the three; see <see href="https://api.fanyi.baidu.com/doc/21">百度翻译开放平台文档</see>. Its language tags are its own:
/// Japanese is <c>jp</c>, not <c>ja</c>.</summary>
public sealed class BaiduTranslator : ClassicApiTranslator
{
    private const string Url = "https://fanyi-api.baidu.com/api/trans/vip/translate";

    private static readonly Dictionary<string, string> ErrorHints = new()
    {
        ["52001"] = "请求超时",
        ["52002"] = "系统错误",
        ["52003"] = "未授权用户(appid 不对,或服务没开通)",
        ["54000"] = "必填参数为空",
        ["54001"] = "签名错误(appid 与密钥不匹配)",
        ["54003"] = "访问频率受限",
        ["54004"] = "账户余额不足",
        ["54005"] = "长文本请求过于频繁",
        ["58000"] = "客户端 IP 非法(控制台里限制了 IP)",
        ["58001"] = "译文语言方向不支持",
        ["58002"] = "服务当前已关闭",
        ["90107"] = "认证未通过或未生效",
    };

    /// <summary>Create the adapter; appid goes in <c>AppId</c>, 密钥 in <c>AppSecret</c>.</summary>
    public BaiduTranslator(ClassicApiOptions options)
        : base(options)
    {
    }

    public BaiduTranslator(ClassicApiOptions options, HttpClient client, bool ownsClient = false)
        : base(options, client, ownsClient)
    {
    }

    public override string Id => CacheId("baidu", Url);

    public override string DisplayName => "百度翻译";

    private protected override int MaxCharactersPerRequest => 4000;

    /// <summary>The signature 百度 documents: the appid, the text, the salt, the key.</summary>
    public static string Sign(string appId, string text, string salt, string secret) =>
        Md5Hex(appId + text + salt + secret);

    /// <summary>Map a language tag onto 百度's own names — Japanese is <c>jp</c> here.</summary>
    public static string Language(string tag) => tag.ToLowerInvariant() switch
    {
        "auto" => "auto",
        "ja" or "ja-jp" or "japanese" => "jp",
        "ko" or "korean" => "kor",
        "en" or "en-us" or "en-gb" or "english" => "en",
        "zh" or "zh-hans" or "zh-hans-cn" or "zh-cn" or "chinese" => "zh",
        "zh-hant" or "zh-hant-tw" or "traditional chinese" => "cht",
        "fr" or "french" => "fra",
        "de" or "german" => "de",
        "ru" or "russian" => "ru",
        "es" or "spanish" => "spa",
        _ => tag,
    };

    private protected override async Task<string> TranslateOneAsync(
        string text,
        string from,
        string to,
        CancellationToken cancellationToken)
    {
        var appId = RequireAccount();
        var salt = Salt();
        var sign = Sign(appId, text, salt, Options.AppSecret.Trim());

        var fields = new List<KeyValuePair<string, string>>
        {
            new("q", text),
            new("from", Language(from)),
            new("to", Language(to)),
            new("appid", appId),
            new("salt", salt),
            new("sign", sign),
        };

        using var document = await PostFormAsync(Endpoint(Url), fields, cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;

        if (root.TryGetProperty("error_code", out var errorCode) && errorCode.ValueKind == JsonValueKind.String)
        {
            var code = errorCode.GetString() ?? string.Empty;
            var message = root.TryGetProperty("error_msg", out var text0) ? text0.GetString() : null;
            if (code.Length > 0)
                Fail(code, message, ErrorHints);
        }

        if (!root.TryGetProperty("trans_result", out var results) || results.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("百度翻译没有返回 trans_result 字段");
        }

        var lines = results.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.Object && item.TryGetProperty("dst", out _))
            .Select(item => item.GetProperty("dst").GetString() ?? string.Empty);

        return string.Join("\n", lines).Trim();
    }
}

/// <summary>彩云小译: a JSON POST carrying a token in <c>x-authorization</c> and the direction as one string (<c>ja2zh</c>);
/// see <see href="https://docs-preview.caiyunapp.com/lingocloud-api/index.html">彩云小译 API</see>.</summary>
public sealed class CaiyunTranslator : ClassicApiTranslator
{
    private const string Url = "https://api.interpreter.caiyunai.com/v1/translator";

    /// <summary>The token 彩云 publishes for trying the API out.</summary>
    public const string TestToken = "3975l6lr5pcbvidl6jl2";

    /// <summary>Create the adapter; the token goes in <c>Token</c> (or <c>AppId</c>, for convenience).</summary>
    public CaiyunTranslator(ClassicApiOptions options)
        : base(options)
    {
    }

    public CaiyunTranslator(ClassicApiOptions options, HttpClient client, bool ownsClient = false)
        : base(options, client, ownsClient)
    {
    }

    public override string Id => CacheId("caiyun", Url);

    public override string DisplayName => "彩云小译";

    private protected override string RequireAccount()
    {
        var token = !string.IsNullOrWhiteSpace(Options.AppSecret) ? Options.AppSecret.Trim()
            : !string.IsNullOrWhiteSpace(Options.Token) ? Options.Token.Trim() : Options.AppId.Trim();
        if (token.Length == 0)
        {
            throw new InvalidOperationException(
                "彩云小译需要访问令牌:在「识别与翻译」页的「应用密钥」里填 token"
                + "(或先用官方测试 token " + TestToken + " 试一下),也可以写进 config.json 的 translation.translator.appSecret。");
        }

        return token;
    }

    /// <summary>彩云's single direction string, such as <c>ja2zh</c>.</summary>
    public static string Direction(string from, string to) => $"{Code(from)}2{Code(to)}";

    /// <summary>彩云's own short codes.</summary>
    private static string Code(string tag) => tag.ToLowerInvariant() switch
    {
        "auto" => "auto",
        "ja" or "ja-jp" or "japanese" => "ja",
        "ko" or "korean" => "ko",
        "en" or "en-us" or "en-gb" or "english" => "en",
        "zh" or "zh-hans" or "zh-hans-cn" or "zh-cn" or "chinese" => "zh",
        "zh-hant" or "zh-hant-tw" or "traditional chinese" => "zh-Hant",
        _ => tag,
    };

    private protected override async Task<string> TranslateOneAsync(
        string text,
        string from,
        string to,
        CancellationToken cancellationToken)
    {
        var token = RequireAccount();
        var direction = Direction(from, to);
        var body = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["source"] = new[] { text },
            ["trans_type"] = direction,
            ["detect"] = direction.StartsWith("auto", StringComparison.OrdinalIgnoreCase),
            ["media"] = "text",
            ["request_id"] = "gugugaga",
        });

        using var document = await PostJsonAsync(
            Endpoint(Url),
            body,
            [new KeyValuePair<string, string>("x-authorization", "token " + token)],
            cancellationToken).ConfigureAwait(false);

        var root = document.RootElement;
        if (root.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String)
        {
            // 彩云 reports trouble in a message field rather than by status code.
            var text0 = message.GetString() ?? string.Empty;
            if (text0.Length > 0 && !root.TryGetProperty("target", out _))
            {
                var hint = text0.Contains("rate limit", StringComparison.OrdinalIgnoreCase)
                    // The published test token is shared by everybody, so it is rate limited often.
                    ? "(这是官方公开的测试 token,很多人共用,随时会被限流;去 platform.caiyunapp.com 申请自己的 token 填进来即可)"
                    : "(检查 token 是否有效、翻译方向是否被支持)";
                throw new InvalidOperationException($"彩云小译报错:{text0} {hint}");
            }
        }

        if (!root.TryGetProperty("target", out var target))
            throw new InvalidOperationException("彩云小译没有返回 target 字段");

        var lines = target.ValueKind == JsonValueKind.Array
            ? target.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString() ?? string.Empty)
            : [target.GetString() ?? string.Empty];

        return string.Join("\n", lines).Trim();
    }
}
