using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace GuGuGaGaTranslator.Core.Translation;

public sealed record ModelServicePreset(string Name, string BaseUrl, string Model, string Hint, string Url, string Steps, string PromptStyle = "galgame");

/// <summary>首次设置和主界面共用服务预设。模型名可按账户权限修改。</summary>
public static class ModelServices
{
    public static readonly ModelServicePreset[] Presets =
    [
        new("DeepSeek 官方 API", "https://api.deepseek.com", "deepseek-flash", "使用自己的 API Key；模型和价格以服务商控制台为准。", "https://platform.deepseek.com/", "创建 API Key → 粘贴 → 测试连接"),
        new("智谱 GLM", "https://open.bigmodel.cn/api/paas/v4", "glm-4-flash", "选择当前账户可调用的文本模型。", "https://open.bigmodel.cn/", "创建 API Key → 填写模型名 → 测试连接"),
        new("硅基流动 SiliconFlow", "https://api.siliconflow.cn/v1", "Qwen/Qwen2.5-7B-Instruct", "可读取模型列表，选择支持聊天的文本模型。", "https://cloud.siliconflow.cn/", "创建 API Key → 读取可用模型或填写模型名"),
        new("通义千问 · 阿里云百炼", "https://dashscope.aliyuncs.com/compatible-mode/v1", "qwen-plus", "北京区域示例；请按控制台业务空间填写完整兼容接口地址，Key 与地址区域必须一致。", "https://bailian.console.aliyun.com/", "获取 API Key 和兼容接口地址 → 填写可用模型名"),
        new("Google Gemini", "https://generativelanguage.googleapis.com/v1beta/openai", "gemini-3.8-flash", "需要 Gemini API Key，并能从当前网络访问 Google API；模型可读取或手动填写。", "https://aistudio.google.com/apikey", "创建 API Key → 读取模型列表 → 选择文本聊天模型"),
        new("本地 Ollama", "http://127.0.0.1:11434/v1", "qwen2.5:7b-instruct", "先下载模型并启动本地服务，Key 可留空。", "https://ollama.com/download", "下载模型 → 启动 Ollama → 填写已下载的模型名"),
        new("本地 Sakura · 日译中", "http://127.0.0.1:11434/v1", "sakura-galtransl:7b", "专门用于日译中，需先启动本地 Sakura 服务。", "https://github.com/SakuraLLM/Sakura-13B-Galgame", "启动 Sakura 兼容接口 → 填写模型名", "sakura"),
        new("本地 LM Studio", "http://127.0.0.1:1234/v1", "", "启动 Developer 页的本地服务，然后读取已加载的模型。", "https://lmstudio.ai/", "下载并加载模型 → 启动服务 → 读取可用模型"),
        new("自定义兼容接口", "", "", "填写服务商提供的 Chat Completions 兼容地址、API Key 和模型名。", "", "原生 Claude 等不同协议需通过兼容网关接入，不能直接填原生地址。"),
    ];

    public static ModelServicePreset? Find(string address) => Presets.Where(preset => preset.BaseUrl.Length > 0
        && Uri.TryCreate(preset.BaseUrl, UriKind.Absolute, out var url)
        && Uri.TryCreate(address, UriKind.Absolute, out var current) && current.Host.Equals(url.Host, StringComparison.OrdinalIgnoreCase)
        && current.Port == url.Port).FirstOrDefault();

    public static Uri ModelsEndpoint(string address)
    {
        var value = address.Trim().TrimEnd('/');
        if (value.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase)) value = value[..^17];
        if (!Uri.TryCreate(value, UriKind.Absolute, out var endpoint) || endpoint.Scheme is not ("http" or "https")
            || endpoint.UserInfo.Length > 0 || endpoint.Query.Length > 0 || endpoint.Fragment.Length > 0)
            throw new ArgumentException("请填写完整的 HTTP 或 HTTPS 兼容接口地址。");
        if (endpoint.Host.Equals("api.deepseek.com", StringComparison.OrdinalIgnoreCase) && endpoint.AbsolutePath == "/") value += "/v1";
        return new Uri(value + "/models");
    }

    public static async Task<IReadOnlyList<string>> ListModelsAsync(string address, string apiKey, CancellationToken cancellationToken, HttpClient? client = null)
    {
        using var owned = client is null ? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) : null;
        var http = client ?? owned!;
        using var request = new HttpRequestMessage(HttpMethod.Get, ModelsEndpoint(address));
        if (!string.IsNullOrWhiteSpace(apiKey)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"无法读取模型列表（HTTP {(int)response.StatusCode}）。服务可能不提供此接口，请从控制台复制模型名。");
        if (response.Content.Headers.ContentLength > 2 * 1024 * 1024) throw new InvalidDataException("模型列表超过大小限制。");
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > 2 * 1024 * 1024) throw new InvalidDataException("模型列表超过大小限制。");
            buffer.Write(chunk, 0, read);
        }
        using var json = JsonDocument.Parse(buffer.ToArray());
        if (!json.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("服务未返回兼容格式的模型列表，请手动填写模型名。");
        return data.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.Object && item.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
            .Select(item => item.GetProperty("id").GetString()!).Where(id => id.Length is > 0 and <= 256 && !id.Any(char.IsControl))
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).Take(1000).ToArray();
    }
}
