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
    /// <summary>The endpoints offered by name; <c>Url</c> is where the key comes from, because
    /// translation is the one part of this program that cannot work offline.</summary>
    private static readonly (string Name, string BaseUrl, string Model, string Hint, string Url, string Steps)[] Providers =
    [
        ("DeepSeek 官方(推荐)",
            "https://api.deepseek.com", "deepseek-flash",
            "按量付费,一句台词约 $0.00003;新账号通常有赠送额度,够跑完一整部 galgame。",
            "https://platform.deepseek.com/",
            "注册登录 → 左侧「API keys」→ 创建 → 复制那个 sk- 开头的串"),
        ("智谱 GLM(有免费模型)",
            "https://open.bigmodel.cn/api/paas/v4", "glm-4-flash",
            "glm-4-flash 目前免费,想零成本先跑起来就选它。",
            "https://open.bigmodel.cn/",
            "注册登录 → 「API Keys」→ 新建 → 复制"),
        ("硅基流动 SiliconFlow(部分模型免费)",
            "https://api.siliconflow.cn/v1", "Qwen/Qwen2.5-7B-Instruct",
            "部分小模型有免费额度,注册即送。",
            "https://cloud.siliconflow.cn/",
            "注册登录 → 「API 密钥」→ 新建 → 复制"),
        ("本地模型(Ollama 等,无需 Key)",
            "http://127.0.0.1:11434/v1", "qwen2.5:7b-instruct",
            "完全离线、不花钱,但要先下载几 GB 的模型文件,显存建议 8 GB 以上。",
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
        ("日 → 中(galgame 常用)", "ja", "zh-Hans"),
        ("自动识别 → 中文", "auto", "zh-Hans"),
        ("英 → 中", "en", "zh-Hans"),
        ("韩 → 中", "ko", "zh-Hans"),
        ("中 → 日", "zh-Hans", "ja"),
        ("中 → 英", "zh-Hans", "en"),
    ];

    private readonly AppSession _session;
    private bool _loading;

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
        ProviderCombo.SelectedIndex = index >= 0 ? index : 0;

        ApiKeyBox.Password = translator.ApiKey;
        ApiKeyPlainBox.Text = translator.ApiKey;
        BaseUrlBox.Text = translator.BaseUrl;
        ModelBox.Text = translator.Model;

        var directionIndex = Array.FindIndex(Directions, direction =>
            direction.From == _session.Config.Translation.From && direction.To == _session.Config.Translation.To);
        DirectionCombo.SelectedIndex = directionIndex >= 0 ? directionIndex : 0;

        VersionText.Text = "屏幕实时翻译 · 首次设置";
        StorageHint.Text = $"密钥只保存在本机:{_session.Store.FilePath}(明文 JSON,请勿分享该文件)";
        AdvancedExpander.IsExpanded = string.IsNullOrWhiteSpace(translator.ApiKey);

        _loading = false;
        ApplyProvider(keepKey: true);
    }

    private void OnDragArea(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
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

    /// <summary>Open a provider's page in the browser, so "get an API key" becomes something the reader can act on.</summary>
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
        if (_loading) return;
        ApplyProvider(keepKey: true);
    }

    private void ApplyProvider(bool keepKey)
    {
        var index = Math.Max(0, ProviderCombo.SelectedIndex);
        var provider = Providers[index];

        if (provider.BaseUrl.Length > 0)
        {
            BaseUrlBox.Text = provider.BaseUrl;
            ModelBox.Text = provider.Model;
        }

        KeyHint.Text = provider.Hint;
        StepsText.Text = provider.Steps.Length == 0 ? string.Empty : $"拿 Key 的步骤:{provider.Steps}";
        StepsText.Visibility = provider.Steps.Length == 0 ? Visibility.Collapsed : Visibility.Visible;

        if (provider.Url.Length > 0)
        {
            KeyLink.NavigateUri = new Uri(provider.Url);
            KeyLinkRun.Text = provider.BaseUrl.Contains("127.0.0.1", StringComparison.Ordinal)
                ? $"下载 Ollama ↗({provider.Url})"
                : $"去 {new Uri(provider.Url).Host} 领 API Key ↗";
            KeyLinkRow.Visibility = Visibility.Visible;
        }
        else
        {
            KeyLinkRow.Visibility = Visibility.Collapsed;
        }

        var needsKey = !provider.BaseUrl.Contains("127.0.0.1", StringComparison.Ordinal);
        ApiKeyBox.IsEnabled = needsKey;
        ApiKeyPlainBox.IsEnabled = needsKey;
        if (!needsKey && !keepKey) ApiKeyBox.Password = string.Empty;
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

    /// <summary>Really call the endpoint once, so a wrong key lands here rather than at the first line of dialogue.</summary>
    private async void OnTestConnection(object sender, RoutedEventArgs e)
    {
        TestButton.IsEnabled = false;
        TestResult.Foreground = Theme.Brush("MutedTextBrush", System.Windows.Media.Color.FromRgb(0x9A, 0xA3, 0xBC));
        TestResult.Text = "正在连接并翻译一句测试文本…";

        var options = new OpenAiTranslatorOptions
        {
            BaseUrl = BaseUrlBox.Text.Trim(),
            Model = ModelBox.Text.Trim(),
            ApiKey = CurrentKey,
            TimeoutSeconds = 40,
            Temperature = 0,
            PromptStyle = PromptStyle.Galgame,
        };

        var watch = Stopwatch.StartNew();
        using var translator = new OpenAiCompatibleTranslator(options);
        try
        {
            var translation = await translator.TranslateAsync(new TranslationRequest
            {
                Text = "こんにちは、いい天気ですね。",
                From = "ja",
                To = "zh-Hans",
            });

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

    /// <summary>Turn a failure into something the user can act on: the raw exception names the symptom, not the setting to change.</summary>
    private static string Describe(Exception exception)
    {
        var message = exception.Message;

        if (exception is HttpRequestException && message.Contains("401", StringComparison.Ordinal))
            return "连接失败(401):API Key 无效或未授权。请确认 Key 复制完整、没有多余空格。";
        if (exception is HttpRequestException && message.Contains("402", StringComparison.Ordinal))
            return "连接失败(402):账户余额不足。请先充值,或换一个有免费额度的服务商。";
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
        var needsKey = !provider.BaseUrl.Contains("127.0.0.1", StringComparison.Ordinal);

        if (needsKey && string.IsNullOrWhiteSpace(CurrentKey))
        {
            TestResult.Foreground = Theme.Brush("ErrorTextBrush", System.Windows.Media.Color.FromRgb(0xE0, 0x7A, 0x7A));
            TestResult.Text = "还没有填 API Key。也可以点「先跳过」,用界面预览模式(只识别、不翻译)。";
            return;
        }

        var config = _session.Config;
        config.Translation.Translator = config.Translation.Translator with
        {
            Provider = "openai-compatible",
            BaseUrl = BaseUrlBox.Text.Trim(),
            Model = ModelBox.Text.Trim(),
            ApiKey = CurrentKey,
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
                "ko" => "ko",
                _ => "auto",
            };
        }

        _session.SaveConfig();
        DialogResult = true;
        Close();
    }
}
