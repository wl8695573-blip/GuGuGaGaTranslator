using System.Diagnostics;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using GuGuGaGaTranslator.Core.Translation;

namespace GuGuGaGaTranslator.App;

/// <summary>The first-run window: pick a provider, paste an API key, confirm it works.</summary>
public partial class SetupWindow : Window
{
    /// <summary>首次设置的服务预设及控制台链接。</summary>
    private static readonly (string Name, string BaseUrl, string Model, string Hint, string Url, string Steps)[] Providers =
    [
        ("DeepSeek 官方(推荐)",
            "https://api.deepseek.com", "deepseek-flash",
            "按量付费；模型可用性、价格和赠送额度请以服务商页面为准。",
            "https://platform.deepseek.com/",
            "注册登录 → 左侧「API keys」→ 创建 → 复制那个 sk- 开头的串"),
        ("智谱 GLM",
            "https://open.bigmodel.cn/api/paas/v4", "glm-4-flash",
            "请先在服务商页面确认模型可用性、价格与限额，再测试连接。",
            "https://open.bigmodel.cn/",
            "注册登录 → 「API Keys」→ 新建 → 复制"),
        ("硅基流动 SiliconFlow",
            "https://api.siliconflow.cn/v1", "Qwen/Qwen2.5-7B-Instruct",
            "模型可用性、价格和免费额度可能变化，请以服务商页面为准。",
            "https://cloud.siliconflow.cn/",
            "注册登录 → 「API 密钥」→ 新建 → 复制"),
        ("本地模型(Ollama 等,无需 Key)",
            "http://127.0.0.1:11434/v1", "qwen2.5:7b-instruct",
            "需先下载模型并启动本地服务。内存和显存需求取决于模型及量化方式。",
            "https://ollama.com/download",
            "下载安装 Ollama → 命令行执行 ollama pull qwen2.5:7b-instruct → 保持它开着"),
        ("自定义(任意 OpenAI 兼容接口)",
            "", "",
            "填入中转、自建或其它厂商的地址与模型名。",
            "",
            ""),
    ];

    private static readonly (string Label, string From, string To)[] Directions =
    [
        ("自动识别 → 中文", "auto", "zh-Hans"),
        ("日 → 中", "ja", "zh-Hans"),
        ("英 → 中", "en", "zh-Hans"),
        ("中 → 日", "zh-Hans", "ja"),
        ("中 → 英", "zh-Hans", "en"),
        ("日 → 英", "ja", "en"),
        ("英 → 日", "en", "ja"),
    ];

    private readonly AppSession _session;
    private bool _loading;
    private readonly CancellationTokenSource _closed = new();

    /// <summary>Create the setup window over a session.</summary>
    public SetupWindow(AppSession session)
    {
        _session = session;
        InitializeComponent();

        _loading = true;

        ProviderCombo.ItemsSource = Providers.Select(provider => provider.Name).ToArray();
        DirectionCombo.ItemsSource = Directions.Select(direction => direction.Label).ToArray();

        var translator = _session.Config.Translation.Translator;
        var index = Array.FindIndex(Providers, provider =>
            !string.IsNullOrWhiteSpace(translator.BaseUrl)
            && provider.BaseUrl.Length > 0
            && translator.BaseUrl.StartsWith(provider.BaseUrl, StringComparison.OrdinalIgnoreCase));
        ProviderCombo.SelectedIndex = index >= 0 ? index : translator.Provider == "mock" ? 0 : Providers.Length - 1;

        ApiKeyBox.Password = translator.ApiKey;
        ApiKeyPlainBox.Text = translator.ApiKey;
        BaseUrlBox.Text = translator.BaseUrl;
        ModelBox.Text = translator.Model;

        var directionIndex = Array.FindIndex(Directions, direction =>
            direction.From == _session.Config.Translation.From && direction.To == _session.Config.Translation.To);
        DirectionCombo.SelectedIndex = directionIndex >= 0 ? directionIndex : 0;

        VersionText.Text = "屏幕实时翻译 · 首次设置";
        StorageHint.Text = "密钥使用 Windows 当前账户加密保存；换电脑或账户后需要重新填写。";
        AdvancedExpander.IsExpanded = string.IsNullOrWhiteSpace(translator.ApiKey);

        _loading = false;
        ApplyProvider(keepKey: true, applyDefaults: translator.Provider == "mock");
        Closed += (_, _) => _closed.Cancel();
    }

    private void OnDragArea(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnClose(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void OnSkip(object sender, RoutedEventArgs e)
    {
        // 不填 Key 直接离开也是被支持的:抓屏和识别照常工作,状态栏会明说当前只有 mock 引擎。
        DialogResult = false;
        Close();
    }

    /// <summary>打开服务控制台。</summary>
    private void OnOpenLink(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        try
        {
            // UseShellExecute 才会让 shell 挑浏览器;否则 .NET 会把这个网址当成程序去执行。
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Uri.AbsoluteUri)
            {
                UseShellExecute = true,
            });
        }
        catch (Exception exception)
        {
            TestResult.Foreground = Theme.Brush("ErrorTextBrush", System.Windows.Media.Color.FromRgb(0xE0, 0x7A, 0x7A));
            TestResult.Text = $"没能打开浏览器,请手动访问:{e.Uri.AbsoluteUri}({exception.Message})";
        }

        e.Handled = true;
    }

    private void OnProviderChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading)
            return;
        ApplyProvider(keepKey: false);
    }

    private void ApplyProvider(bool keepKey, bool applyDefaults = true)
    {
        var index = Math.Max(0, ProviderCombo.SelectedIndex);
        var provider = Providers[index];

        if (applyDefaults && provider.BaseUrl.Length > 0)
        {
            BaseUrlBox.Text = provider.BaseUrl;
            ModelBox.Text = provider.Model;
        }

        KeyHint.Text = provider.Hint;
        StepsText.Text = provider.Steps.Length == 0 ? string.Empty : $"配置步骤：{provider.Steps}";
        StepsText.Visibility = provider.Steps.Length == 0 ? Visibility.Collapsed : Visibility.Visible;

        if (provider.Url.Length > 0)
        {
            KeyLink.NavigateUri = new Uri(provider.Url);
            KeyLinkRun.Text = provider.BaseUrl.Contains("127.0.0.1", StringComparison.Ordinal)
                ? $"下载 Ollama ↗({provider.Url})"
                : $"打开 {new Uri(provider.Url).Host} 控制台 ↗";
            KeyLinkRow.Visibility = Visibility.Visible;
        }
        else
        {
            KeyLinkRow.Visibility = Visibility.Collapsed;
        }

        var needsKey = !provider.BaseUrl.Contains("127.0.0.1", StringComparison.Ordinal);
        ApiKeyBox.IsEnabled = needsKey;
        ApiKeyPlainBox.IsEnabled = needsKey;
        if (!keepKey)
        {
            ApiKeyBox.Password = string.Empty;
            ApiKeyPlainBox.Text = string.Empty;
        }
        TestResult.Text = string.Empty;
    }

    private void OnToggleShowKey(object sender, RoutedEventArgs e)
    {
        if (ShowKeyCheck.IsChecked == true)
        {
            ApiKeyPlainBox.Text = ApiKeyBox.Password;
            ApiKeyPlainBox.Visibility = Visibility.Visible;
            ApiKeyBox.Visibility = Visibility.Collapsed;
        }
        else
        {
            ApiKeyBox.Password = ApiKeyPlainBox.Text;
            ApiKeyPlainBox.Visibility = Visibility.Collapsed;
            ApiKeyBox.Visibility = Visibility.Visible;
        }
    }

    /// <summary>The key currently typed, from whichever of the two boxes is live.</summary>
    private string CurrentKey => ShowKeyCheck.IsChecked == true ? ApiKeyPlainBox.Text : ApiKeyBox.Password;

    /// <summary>发送示例文本检查连接。</summary>
    private async void OnTestConnection(object sender, RoutedEventArgs e)
    {
        TestButton.IsEnabled = false;
        TestResult.Foreground = Theme.Brush("MutedTextBrush", System.Windows.Media.Color.FromRgb(0x9A, 0xA3, 0xBC));
        TestResult.Text = "正在连接并翻译一句测试文本…";

        var options = new OpenAiTranslatorOptions
        {
            BaseUrl = BaseUrlBox.Text.Trim(),
            Model = ModelBox.Text.Trim(),
            ApiKey = CurrentKey.Trim(),
            TimeoutSeconds = 40,
            Temperature = 0,
            PromptStyle = PromptStyle.Galgame,
        };

        var watch = Stopwatch.StartNew();
        try
        {
            using var translator = new OpenAiCompatibleTranslator(options);
            var translation = await translator.TranslateAsync(new TranslationRequest
            {
                Text = "こんにちは、いい天気ですね。",
                From = "ja",
                To = "zh-Hans",
            }, _closed.Token);

            watch.Stop();
            TestResult.Foreground = Theme.Brush("OkTextBrush", System.Windows.Media.Color.FromRgb(0x5F, 0xC9, 0x8A));
            TestResult.Text = $"连接成功({watch.ElapsedMilliseconds} ms)\n"
                + $"こんにちは、いい天気ですね。 → {translation}\n"
                + "确认效果满意后点「开始使用」。";
        }
        catch (Exception exception)
        {
            watch.Stop();
            TestResult.Foreground = Theme.Brush("ErrorTextBrush", System.Windows.Media.Color.FromRgb(0xE0, 0x7A, 0x7A));
            TestResult.Text = Describe(exception);
        }
        finally
        {
            TestButton.IsEnabled = true;
        }
    }

    /// <summary>将连接错误转换为配置提示。</summary>
    private static string Describe(Exception exception)
    {
        var message = exception.Message;

        if (exception is HttpRequestException && message.Contains("401", StringComparison.Ordinal))
            return "连接失败(401):API Key 无效或未授权。请确认 Key 复制完整、没有多余空格。";
        if (exception is HttpRequestException && message.Contains("402", StringComparison.Ordinal))
            return "连接失败（402）：请检查账户余额和模型调用权限。";
        if (exception is HttpRequestException && message.Contains("404", StringComparison.Ordinal))
            return "连接失败(404):模型名或接口地址不对。DeepSeek 用 https://api.deepseek.com + deepseek-flash。";
        if (exception is HttpRequestException && message.Contains("429", StringComparison.Ordinal))
            return "连接失败(429):请求过于频繁或超出配额,稍后再试。";
        if (exception is TaskCanceledException or OperationCanceledException)
            return "连接超时:地址不通或服务很慢。本机模型首次调用要加载几十秒,可把超时调大。";
        if (message.Contains("No connection could be made", StringComparison.OrdinalIgnoreCase)
            || message.Contains("actively refused", StringComparison.OrdinalIgnoreCase))
            return "连不上:地址或端口不对,或本机服务(如 Ollama)没有启动。";
        if (message.Contains("name or service not known", StringComparison.OrdinalIgnoreCase)
            || message.Contains("such host is known", StringComparison.OrdinalIgnoreCase))
            return "域名解析失败:检查接口地址拼写,或确认本机网络可以访问该服务。";

        return $"连接失败:{message}";
    }

    private void OnStart(object sender, RoutedEventArgs e)
    {
        var index = Math.Max(0, ProviderCombo.SelectedIndex);
        var provider = Providers[index];
        var direction = Directions[Math.Max(0, DirectionCombo.SelectedIndex)];
        if (!Uri.TryCreate(BaseUrlBox.Text.Trim(), UriKind.Absolute, out var endpoint)
            || endpoint.Scheme is not ("http" or "https") || string.IsNullOrWhiteSpace(ModelBox.Text))
        {
            TestResult.Text = "请填写有效的 HTTP/HTTPS 接口地址和模型名。";
            return;
        }
        var needsKey = !endpoint.IsLoopback;

        if (needsKey && string.IsNullOrWhiteSpace(CurrentKey))
        {
            TestResult.Foreground = Theme.Brush("ErrorTextBrush", System.Windows.Media.Color.FromRgb(0xE0, 0x7A, 0x7A));
            TestResult.Text = "请填写 API Key，或选择预览模式检查识别结果。";
            return;
        }

        var config = _session.Config;
        config.Translation.Translator = config.Translation.Translator with
        {
            Provider = "openai-compatible",
            BaseUrl = BaseUrlBox.Text.Trim(),
            Model = ModelBox.Text.Trim(),
            ApiKey = CurrentKey.Trim(),
            TimeoutSeconds = needsKey ? 60 : 120,
        };
        config.Translation.From = direction.From;
        config.Translation.To = direction.To;

        // 选方向同时也等于选该期待哪种文字,所以识别语言跟着走 —— 除非用户已经自己设过。
        if (config.Ocr.Language is "auto" or "")
        {
            config.Ocr.Language = direction.From switch
            {
                "ja" => "ja",
                "en" => "en-US",
                "zh-Hans" => "zh-Hans-CN",
                _ => "auto",
            };
        }

        _session.SaveConfig();
        DialogResult = true;
        Close();
    }
}
