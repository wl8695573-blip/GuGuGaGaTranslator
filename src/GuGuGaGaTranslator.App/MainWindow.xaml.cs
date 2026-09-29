using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using GuGuGaGaTranslator.Core.Capture;
using GuGuGaGaTranslator.Core.Config;
using GuGuGaGaTranslator.Core.Interop;
using GuGuGaGaTranslator.Core.Ocr;
using GuGuGaGaTranslator.Core.Pipeline;
using GuGuGaGaTranslator.Core.Translation;

namespace GuGuGaGaTranslator.App;

/// <summary>The control window: pick a target, frame a region, start the loop, and watch
/// what it recognized. Every setting is read back into the configuration on save.</summary>
public partial class MainWindow : Window
{
    private const int HotkeyToggle = 9001;
    private const int HotkeyPause = 9002;
    private const int HotkeyRegion = 9003;
    private const int HotkeySource = 9004;
    private const int HotkeyPanel = 9005;
    private const int HotkeyEdit = 9006;

    /// <summary>One dropdown entry: the machine value stored in the configuration plus the sentence a person reads.</summary>
    private sealed record Choice(string Value, string Label)
    {
        /// <inheritdoc />
        public override string ToString() => Label;
    }

    private static readonly Choice AutoLanguage = new("auto", LanguageNames.Label("auto", "让引擎自己判断原文语言"));

    private static readonly Choice[] TranslationLanguages =
    [
        new("ja", LanguageNames.Label("ja")),
        new("en", LanguageNames.Label("en")),
        new("zh-Hans", LanguageNames.Label("zh-Hans")),
        new("zh-Hant", LanguageNames.Label("zh-Hant")),
        new("ko", LanguageNames.Label("ko")),
    ];

    /// <summary>The recognition languages worth offering; availability is annotated when the list is populated.</summary>
    private static readonly Choice[] OcrLanguages =
    [
        new("auto", LanguageNames.Label("auto", "逐个已装的语言试一遍,取最像文字的结果")),
        new("zh-Hans-CN", LanguageNames.Label("zh-Hans-CN")),
        new("zh-Hant-TW", LanguageNames.Label("zh-Hant-TW")),
        new("ja", LanguageNames.Label("ja")),
        new("en-US", LanguageNames.Label("en-US")),
        new("ko", LanguageNames.Label("ko")),
    ];

    private static readonly Choice[] EngineProviders =
    [
        new("mock", "mock —— 只验证链路:不翻译,原样回显原文"),
        new("openai-compatible", "openai-compatible —— AI 模型(DeepSeek / GLM / 本地模型都走这一项)"),
    ];

    /// <summary>Where the translation panel sits relative to the dialogue box.</summary>
    private static readonly Choice[] OverlayPlacements =
    [
        new(OverlayPlacement.Over, "遮盖原文 —— 翻译框盖住原对话框(galgame 推荐)"),
        new(OverlayPlacement.Below, "下方 —— 原文保留,译文贴在对话框下沿"),
        new(OverlayPlacement.Above, "上方 —— 原文保留,译文贴在对话框上沿"),
    ];

    /// <summary>Recognition backends: the in-box one, and the bundled offline model.</summary>
    private static readonly Choice[] OcrEngines =
    [
        new("rapidocr", "RapidOCR 离线(推荐)—— 自带多语言模型,日/英/中通吃,不用装任何语言包,解压即用;比系统引擎慢一点"),
        new("windows", "Windows OCR —— 系统自带,快 3–4 倍;但画面上的文字语言必须先装好对应的系统语言包"),
    ];

    /// <summary>Endpoint presets; they only fill the three fields below.</summary>
    private static readonly (string Label, string BaseUrl, string Model, string PromptStyle)[] EnginePresets =
    [
        ("本地 Ollama · 通用模型(离线免费,但口语日译中一般)", "http://127.0.0.1:11434/v1", "qwen2.5:7b-instruct", "galgame"),
        ("本地 Sakura · galgame 专用(离线免费,推荐)", "http://127.0.0.1:11434/v1", "sakura-galtransl:7b", "sakura"),
        ("DeepSeek 官方 API(付费,极便宜;质量好)", "https://api.deepseek.com", "deepseek-flash", "galgame"),
        ("智谱 GLM(有免费模型,需自备 Key)", "https://open.bigmodel.cn/api/paas/v4", "glm-4-flash", "galgame"),
        ("硅基流动 SiliconFlow(部分模型免费,需自备 Key)", "https://api.siliconflow.cn/v1", "Qwen/Qwen2.5-7B-Instruct", "galgame"),
    ];

    /// <summary>The instruction formats the translator can speak.</summary>
    private static readonly Choice[] PromptStyles =
    [
        new("galgame", "galgame —— 通用游戏本地化指令,适合任何聊天模型"),
        new("sakura", "sakura —— Sakura-GalTransl 训练时的固定格式(该类模型必须选它)"),
    ];

    private readonly AppSession _session;
    private OverlayWindow? _overlay;
    private nint _handle;
    private bool _loadingUi;

    /// <summary>Create the window over a session.</summary>
    public MainWindow(AppSession session)
    {
        _session = session;
        InitializeComponent();

        _session.Updated += OnPipelineUpdate;
        _session.Notice += OnNotice;
        _session.LanguagesChanged += OnLanguagesChanged;
        _session.ProfileChanged += OnProfileChanged;

        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _handle = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(_handle)?.AddHook(OnWindowMessage);
        RegisterHotkeys();

        PopulateChoices();
        LoadConfigIntoUi();
        RefreshWindowList();

        // 上一局还开着的游戏自己就会报出身份:在第一句之前切好档案,而不是等第一个译错的名字。
        await _session.AutoDetectProfileAsync().ConfigureAwait(true);
        SyncProfileControls();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        // 窗口可能在加载之前就被关掉:那时没有句柄,而 HwndSource.FromHwnd(0) 会抛异常而不是
        // 返回 null —— 启动途中关掉曾因此变成未处理异常(首次运行时设置卡片一度是最后一个窗口)。
        if (_handle != 0)
        {
            HwndSource.FromHwnd(_handle)?.RemoveHook(OnWindowMessage);
            HotkeyInterop.Unregister(_handle, HotkeyToggle);
            HotkeyInterop.Unregister(_handle, HotkeyPause);
            HotkeyInterop.Unregister(_handle, HotkeyRegion);
            HotkeyInterop.Unregister(_handle, HotkeySource);
            HotkeyInterop.Unregister(_handle, HotkeyPanel);
            HotkeyInterop.Unregister(_handle, HotkeyEdit);
        }

        _session.Updated -= OnPipelineUpdate;
        _session.Notice -= OnNotice;
        _session.LanguagesChanged -= OnLanguagesChanged;
        _session.ProfileChanged -= OnProfileChanged;
        _overlay?.Close();
    }

    private void RegisterHotkeys()
    {
        var modifiers = HotkeyInterop.ModControl | HotkeyInterop.ModAlt;
        var failures = new List<string>();

        if (!HotkeyInterop.Register(_handle, HotkeyToggle, modifiers, 0x54)) failures.Add("Ctrl+Alt+T");
        if (!HotkeyInterop.Register(_handle, HotkeyPause, modifiers, 0x50)) failures.Add("Ctrl+Alt+P");
        if (!HotkeyInterop.Register(_handle, HotkeyRegion, modifiers, 0x52)) failures.Add("Ctrl+Alt+R");
        if (!HotkeyInterop.Register(_handle, HotkeySource, modifiers, 0x4F)) failures.Add("Ctrl+Alt+O");
        if (!HotkeyInterop.Register(_handle, HotkeyPanel, modifiers, 0x48)) failures.Add("Ctrl+Alt+H");
        if (!HotkeyInterop.Register(_handle, HotkeyEdit, modifiers, 0x55)) failures.Add("Ctrl+Alt+U");

        if (failures.Count > 0)
        {
            HotkeyText.Text += $"\n以下热键被其他程序占用,注册失败:{string.Join("、", failures)}";
        }
    }

    private nint OnWindowMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message != HotkeyInterop.WmHotkey) return 0;
        handled = true;

        switch ((int)wParam)
        {
            case HotkeyToggle:
                if (_session.IsRunning) OnStop(this, new RoutedEventArgs());
                else OnStart(this, new RoutedEventArgs());
                break;
            case HotkeyPause:
                OnPause(this, new RoutedEventArgs());
                break;
            case HotkeyRegion:
                OnPickRegion(this, new RoutedEventArgs());
                break;
            case HotkeySource:
                ToggleOverlayOption(source: true);
                break;
            case HotkeyPanel:
                ToggleOverlayOption(source: false);
                break;
            case HotkeyEdit:
                ToggleOverlayEditMode();
                break;
        }

        return 0;
    }

    private void PopulateChoices()
    {
        _loadingUi = true;

        // 可用性直接写进选项文字:缺日文 OCR 功能是识别不出东西最常见的原因。
        var installed = WindowsOcrRecognizer.AvailableLanguages;
        var ocrChoices = OcrLanguages
            .Select(choice => choice.Value.Equals("auto", StringComparison.OrdinalIgnoreCase)
                || installed.Contains(choice.Value, StringComparer.OrdinalIgnoreCase)
                    ? choice
                    : choice with { Label = $"{choice.Label} —— 未安装语言包,需在 Windows 设置里添加" })
            .ToList();

        foreach (var tag in installed.Where(tag => OcrLanguages.All(choice => !choice.Value.Equals(tag, StringComparison.OrdinalIgnoreCase))))
        {
            ocrChoices.Add(new Choice(tag, LanguageNames.Label(tag, "系统已安装")));
        }

        OcrLanguageCombo.ItemsSource = ocrChoices;

        OcrEngineCombo.ItemsSource = OcrEngines.ToArray();
        PresetCombo.ItemsSource = EnginePresets.Select(preset => new Choice(preset.Label, preset.Label)).ToArray();
        PromptStyleCombo.ItemsSource = PromptStyles.ToArray();
        PlacementCombo.ItemsSource = OverlayPlacements.ToArray();
        OverlayPresetCombo.ItemsSource = _session.Config.Overlay.Presets
            .Select(preset => new Choice(preset.Name, preset.Name))
            .ToArray();
        FromCombo.ItemsSource = new[] { AutoLanguage }.Concat(TranslationLanguages).ToArray();
        ToCombo.ItemsSource = TranslationLanguages.ToArray();
        ProviderCombo.ItemsSource = EngineProviders.ToArray();

        _loadingUi = false;
    }

    /// <summary>Select the entry carrying a machine value, adding it on the spot when a hand-edited configuration names something not in the list.</summary>
    private static void SelectByValue(ComboBox combo, string value)
    {
        if (combo.ItemsSource is not IEnumerable<Choice> choices) return;

        var match = choices.FirstOrDefault(choice => choice.Value.Equals(value, StringComparison.OrdinalIgnoreCase));
        if (match is not null)
        {
            combo.SelectedItem = match;
            return;
        }

        var custom = new Choice(value, $"{value}(来自配置文件)");
        combo.ItemsSource = choices.Append(custom).ToArray();
        combo.SelectedItem = custom;
    }

    /// <summary>Read the machine value out of a dropdown.</summary>
    private static string ValueOf(ComboBox combo, string fallback) =>
        combo.SelectedItem is Choice choice ? choice.Value
        : string.IsNullOrWhiteSpace(combo.Text) ? fallback
        : combo.Text.Trim();

    /// <summary>Restate what the engine settings will actually do: "mock" and a half-filled URL both look like a working configuration.</summary>
    private void UpdateEngineSummary()
    {
        var provider = ValueOf(ProviderCombo, "mock");
        if (provider.Equals("mock", StringComparison.OrdinalIgnoreCase))
        {
            EngineSummary.Text = "当前:不会真正翻译。识别到的原文会被原样加上 [mock …] 前缀显示,用来确认抓屏和识别是否正常。";
            return;
        }

        if (ClassicEngines.TryGetValue(provider, out var classic))
        {
            // 只有手改配置文件才到得了这里:下拉里已经不再提供这些引擎,因为它们收不到术语表
            // 和世界观设定;适配器留着给想要快而免费的引擎、并接受这一代价的人。
            var translator = _session.Config.Translation.Translator;
            var appId = translator.AppId;
            var secret = translator.AppSecret;

            EngineSummary.Text = $"当前:{classic.Name} —— 每句话直接发给它的官方接口,配置来自 config.json:"
                + (classic.NeedsAppId
                    ? $"应用 ID {Mask(appId)} / 密钥 {Mask(secret)}"
                    : $"令牌 {Mask(string.IsNullOrWhiteSpace(secret) ? appId : secret)}")
                + "。这类接口收不到术语表和世界观(协议里没有提示词这一层),译名只能靠「游戏模式」的「禁止译法」在译文落地前改回。";
            return;
        }

        var baseUrl = string.IsNullOrWhiteSpace(BaseUrlBox.Text) ? "(未填地址)" : BaseUrlBox.Text.Trim();
        var model = string.IsNullOrWhiteSpace(ModelBox.Text) ? "(未填模型)" : ModelBox.Text.Trim();
        EngineSummary.Text = $"当前:把识别到的文本发给 {baseUrl.TrimEnd('/')}/chat/completions,模型 {model}。"
            + (string.IsNullOrWhiteSpace(ApiKeyBox.Password) ? "地址是本机的话请确认服务已启动。" : "已填 API Key,将作为在线服务调用。");
    }

    /// <summary>A classic translation API, and whether the account id is its own field (有道, 百度) or the token stands alone (彩云).</summary>
    private sealed record ClassicEngine(string Name, bool NeedsAppId);

    private static readonly Dictionary<string, ClassicEngine> ClassicEngines = new(StringComparer.OrdinalIgnoreCase)
    {
        ["caiyun"] = new("彩云小译", NeedsAppId: false),
        ["youdao"] = new("有道翻译", NeedsAppId: true),
        ["baidu"] = new("百度翻译", NeedsAppId: true),
    };

    /// <summary>Show only enough of a credential to tell whether it is filled.</summary>
    private static string Mask(string value) =>
        value.Length == 0 ? "(未填)" : value.Length <= 6 ? new string('•', value.Length) : value[..3] + new string('•', 6);

    private void OnEngineFieldChanged(object sender, RoutedEventArgs e) => UpdateEngineSummary();

    private void OnOcrEngineChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingUi) return;
        UpdateOcrEngineSummary();
    }

    /// <summary>Restate what the chosen recognition backend needs: one demands an installed language pack, the other ignores the language setting.</summary>
    private void UpdateOcrEngineSummary()
    {
        var engine = ValueOf(OcrEngineCombo, "windows");
        if (engine.Equals("rapidocr", StringComparison.OrdinalIgnoreCase))
        {
            var directory = string.IsNullOrWhiteSpace(RapidModelsBox.Text)
                ? "自动查找 models\\v6"
                : RapidModelsBox.Text.Trim();
            OcrEngineSummary.Text = $"当前:RapidOCR 离线模型(多语言,日/英/中通吃,不需系统语言包)。"
                + $"模型目录:{directory}。「识别语言」对它不生效,「识别前放大倍数」也会被忽略(它自己会缩放)。";
            return;
        }

        OcrEngineSummary.Text = "当前:Windows 自带识别器。必须为画面上的文字语言安装对应的「光学字符识别」功能,"
            + "否则识别不可用或读成乱码;英文/简体中文在中文版 Windows 上通常已自带。";
    }

    private void LoadConfigIntoUi()
    {
        _loadingUi = true;
        var config = _session.Config;
        SelectByValue(OcrEngineCombo, config.Ocr.Engine);
        SelectByValue(OcrLanguageCombo, config.Ocr.Language);
        OcrScaleBox.Text = Text(config.Ocr.Scale);
        OcrGrayBox.Text = Text(config.Ocr.Grayscale);
        RapidLimitSideBox.Text = Text(config.Ocr.RapidLimitSideLen);
        RapidModelsBox.Text = config.Ocr.RapidModelDirectory;
        UpdateOcrEngineSummary();
        SelectByValue(FromCombo, config.Translation.From);
        SelectByValue(ToCombo, config.Translation.To);

        ProviderCombo.Text = string.Empty;
        SelectByValue(ProviderCombo, config.Translation.Translator.Provider);
        BaseUrlBox.Text = config.Translation.Translator.BaseUrl;
        ModelBox.Text = config.Translation.Translator.Model;
        ApiKeyBox.Password = config.Translation.Translator.ApiKey;
        TimeoutBox.Text = Text(config.Translation.Translator.TimeoutSeconds);
        TemperatureBox.Text = Text(config.Translation.Translator.Temperature);
        SelectByValue(PromptStyleCombo, config.Translation.Translator.PromptStyle);
        HistoryLinesBox.Text = Text(config.Translation.HistoryLines);
        UpdateEngineSummary();

        SelectByValue(OverlayPresetCombo, string.Empty);
        OverlayPresetCombo.SelectedIndex = -1;
        SelectByValue(PlacementCombo, config.Overlay.Placement);
        OverlayFontBox.Text = Text(config.Overlay.FontSize);
        OverlayOpacityBox.Text = Text(config.Overlay.BackgroundOpacity);
        OverlayMaxLinesBox.Text = Text(config.Overlay.MaxLines);
        OverlayWidthBox.Text = Text(config.Overlay.Width);
        OverlayHeightBox.Text = Text(config.Overlay.Height);
        OverlayCornerBox.Text = Text(config.Overlay.CornerRadius);
        OverlayPaddingBox.Text = Text(config.Overlay.Padding);
        OverlayFontFamilyBox.Text = config.Overlay.FontFamily;
        OverlayOffsetXBox.Text = Text(config.Overlay.OffsetX);
        OverlayOffsetYBox.Text = Text(config.Overlay.OffsetY);
        ShowPanelCheck.IsChecked = config.Overlay.ShowPanel;
        ShowSourceCheck.IsChecked = config.Overlay.ShowSource;
        TextOutlineCheck.IsChecked = config.Overlay.TextOutline;
        ClickThroughCheck.IsChecked = config.Overlay.ClickThrough;
        LanguageBarCheck.IsChecked = config.Overlay.ShowLanguageBar;
        ExcludeFromCaptureCheck.IsChecked = config.Overlay.ExcludeFromCapture;

        DumpFramesCheck.IsChecked = config.Debug.DumpFrames;
        DumpDirBox.Text = string.IsNullOrWhiteSpace(config.Debug.DumpDirectory)
            ? AppSession.DefaultDumpDirectory()
            : config.Debug.DumpDirectory;
        DumpKeepBox.Text = Text(config.Debug.KeepDumps);
        PollIntervalBox.Text = Text(config.Pipeline.PollIntervalMs);
        ChangeThresholdBox.Text = Text(config.Pipeline.ChangeThresholdBits);
        ForceRefreshBox.Text = Text(config.Pipeline.ForceRefreshMs);

        GameProfileCombo.ItemsSource = ProfileChoices();
        SelectByValue(GameProfileCombo, config.Translation.ActiveProfile);
        AutoProfileCheck.IsChecked = config.Translation.AutoDetectProfile;
        EnforceTermsCheck.IsChecked = config.Translation.EnforceTerms;

        UpdateTargetSummary();
        _loadingUi = false;
        UpdateProfileSummary();
    }

    private bool ReadUiIntoConfig()
    {
        var config = _session.Config;

        config.Ocr.Engine = ValueOf(OcrEngineCombo, "windows");
        config.Ocr.Language = ValueOf(OcrLanguageCombo, "ja");
        config.Ocr.Scale = Number(OcrScaleBox.Text, config.Ocr.Scale);
        config.Ocr.Grayscale = Number(OcrGrayBox.Text, config.Ocr.Grayscale);
        config.Ocr.RapidLimitSideLen = Math.Max(0, (int)Number(RapidLimitSideBox.Text, config.Ocr.RapidLimitSideLen));
        config.Ocr.RapidModelDirectory = RapidModelsBox.Text.Trim();

        config.Translation.From = ValueOf(FromCombo, "ja");
        config.Translation.To = ValueOf(ToCombo, "zh-Hans");
        config.Translation.Translator = config.Translation.Translator with
        {
            Provider = ValueOf(ProviderCombo, "mock"),
            BaseUrl = BaseUrlBox.Text.Trim(),
            Model = ModelBox.Text.Trim(),
            ApiKey = ApiKeyBox.Password,
            TimeoutSeconds = (int)Number(TimeoutBox.Text, config.Translation.Translator.TimeoutSeconds),
            Temperature = Number(TemperatureBox.Text, config.Translation.Translator.Temperature),
            PromptStyle = ValueOf(PromptStyleCombo, "galgame"),
        };
        config.Translation.HistoryLines = Math.Clamp((int)Number(HistoryLinesBox.Text, config.Translation.HistoryLines), 0, 12);
        config.Translation.AutoDetectProfile = AutoProfileCheck.IsChecked == true;
        config.Translation.EnforceTerms = EnforceTermsCheck.IsChecked == true;

        config.Overlay.Placement = ValueOf(PlacementCombo, OverlayPlacement.Below);
        config.Overlay.ShowPanel = ShowPanelCheck.IsChecked == true;
        config.Overlay.FontSize = Number(OverlayFontBox.Text, config.Overlay.FontSize);
        config.Overlay.BackgroundOpacity = Math.Clamp(Number(OverlayOpacityBox.Text, config.Overlay.BackgroundOpacity), 0, 1);
        config.Overlay.MaxLines = Math.Max(1, (int)Number(OverlayMaxLinesBox.Text, config.Overlay.MaxLines));
        config.Overlay.Width = Math.Max(0, (int)Number(OverlayWidthBox.Text, config.Overlay.Width));
        config.Overlay.Height = Math.Max(0, (int)Number(OverlayHeightBox.Text, config.Overlay.Height));
        config.Overlay.CornerRadius = Math.Max(0, Number(OverlayCornerBox.Text, config.Overlay.CornerRadius));
        config.Overlay.Padding = Math.Max(0, Number(OverlayPaddingBox.Text, config.Overlay.Padding));
        config.Overlay.FontFamily = OverlayFontFamilyBox.Text.Trim();
        config.Overlay.OffsetX = (int)Number(OverlayOffsetXBox.Text, config.Overlay.OffsetX);
        config.Overlay.OffsetY = (int)Number(OverlayOffsetYBox.Text, config.Overlay.OffsetY);
        config.Overlay.ShowSource = ShowSourceCheck.IsChecked == true;
        config.Overlay.TextOutline = TextOutlineCheck.IsChecked == true;
        config.Overlay.ClickThrough = ClickThroughCheck.IsChecked == true;
        config.Overlay.ShowLanguageBar = LanguageBarCheck.IsChecked == true;
        config.Overlay.ExcludeFromCapture = ExcludeFromCaptureCheck.IsChecked == true;

        config.Debug.DumpFrames = DumpFramesCheck.IsChecked == true;
        config.Debug.DumpDirectory = DumpDirBox.Text.Trim();
        config.Debug.KeepDumps = Math.Max(1, (int)Number(DumpKeepBox.Text, config.Debug.KeepDumps));
        config.Pipeline.PollIntervalMs = Math.Max(50, (int)Number(PollIntervalBox.Text, config.Pipeline.PollIntervalMs));
        config.Pipeline.ChangeThresholdBits = Math.Max(1, (int)Number(ChangeThresholdBox.Text, config.Pipeline.ChangeThresholdBits));
        config.Pipeline.ForceRefreshMs = Math.Max(0, (int)Number(ForceRefreshBox.Text, config.Pipeline.ForceRefreshMs));

        return true;
    }

    private void OnRefreshWindows(object sender, RoutedEventArgs e) => RefreshWindowList();

    private async void OnWindowSelected(object sender, SelectionChangedEventArgs e)
    {
        UpdateTargetSummary();

        // 选中游戏的那一刻才知道该用哪份档案,所以也在那一刻切换。
        if (WindowList.SelectedItem is WindowInfo window)
        {
            await _session.AutoDetectProfileAsync(window.Title).ConfigureAwait(true);
            SyncProfileControls();
        }
    }

    private void RefreshWindowList()
    {
        var windows = _session.ListWindows();
        WindowList.ItemsSource = windows;

        var identity = _session.Config.Target.Identity;
        if (!string.IsNullOrEmpty(identity))
        {
            var match = windows.FirstOrDefault(window => window.Identity == identity);
            if (match is not null) WindowList.SelectedItem = match;
        }

        UpdateTargetSummary();
    }

    private void UpdateTargetSummary()
    {
        var target = WindowList.SelectedItem as WindowInfo;
        var region = _session.Config.Target.Region;

        if (target is not null)
        {
            TargetSummary.Text = region is { IsUsable: true } selected
                ? $"已选:{target.Title}\n区域(相对客户区):{selected}"
                : $"已选:{target.Title}\n点击「框选区域」划出要翻译的部分";
            return;
        }

        TargetSummary.Text = region is { IsUsable: true } saved
            ? $"区域已保存(相对客户区 {saved}),但目标窗口当前不在列表里"
            : "尚未选择窗口";
    }

    private void OnPickRegion(object sender, RoutedEventArgs e)
    {
        if (WindowList.SelectedItem is not WindowInfo target)
        {
            OnNotice("请先在左侧列表里点选一个窗口。");
            return;
        }

        var wasRunning = _session.IsRunning;
        if (wasRunning) OnStop(this, new RoutedEventArgs());

        // 悬浮层会压在正在框选的区域上,先藏起来。
        _overlay?.Hide();

        var region = RegionSelectorWindow.Select(this);
        if (region is not { Width: >= 8, Height: >= 8 })
        {
            OnNotice("已取消框选。");
            return;
        }

        _session.SetRegion(target, region.Value);
        _session.SaveConfig();
        UpdateTargetSummary();

        // 框完区域就是要翻译:让人再按一次开始按钮,只是代码写成这样的产物。
        OnStart(this, new RoutedEventArgs());
        OnNotice(_session.IsRunning
            ? $"已开始持续翻译,区域相对客户区 {_session.Config.Target.Region};窗口移动会自动跟随,"
                + "按 Ctrl+Alt+R 可重新框选。"
            : $"区域已记录(相对客户区 {_session.Config.Target.Region}),但没能开始翻译 —— 看上方的状态提示。");
    }

    private void OnClearRegion(object sender, RoutedEventArgs e)
    {
        _session.ClearRegion();
        _session.SaveConfig();
        UpdateTargetSummary();
        OnNotice("已清除区域与目标窗口。");
    }

    private void OnStart(object sender, RoutedEventArgs e)
    {
        ReadUiIntoConfig();
        _session.SaveConfig();

        _overlay ??= new OverlayWindow();
        _overlay.LanguageRequested -= OnLanguageRequested;
        _overlay.LanguageRequested += OnLanguageRequested;
        _overlay.LayoutChanged -= OnOverlayLayoutChanged;
        _overlay.LayoutChanged += OnOverlayLayoutChanged;
        _overlay.DirectionsRequested -= OnDirectionsRequested;
        _overlay.DirectionsRequested += OnDirectionsRequested;
        _overlay.ToggleRequested -= OnOverlayToggleRequested;
        _overlay.ToggleRequested += OnOverlayToggleRequested;
        _overlay.ProfileRequested -= OnProfilesRequested;
        _overlay.ProfileRequested += OnProfilesRequested;
        _overlay.Configure(_session.Config.Overlay, _session.Languages);
        _overlay.SetProfile(_session.ActiveProfile?.Name);
        _session.Start();

        if (!_session.IsRunning) return;

        var region = _session.ResolveRegion();
        if (region is { } rect) _overlay.ShowTranslated(string.Empty, "等待台词…", rect);
        else _overlay.Show();
    }

    private void OnPause(object sender, RoutedEventArgs e)
    {
        if (!_session.IsRunning) return;
        var paused = _session.Pipeline?.IsPaused == true;
        _session.SetPaused(!paused);
        StatusText.Text = paused ? "已继续" : "已暂停(仍在轮询,但不做识别)";
    }

    private void OnStop(object sender, RoutedEventArgs e)
    {
        _ = StopAsync();
    }

    private async Task StopAsync()
    {
        await _session.StopAsync().ConfigureAwait(true);
        _overlay?.Hide();
        StatusText.Text = "已停止";
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        ReadUiIntoConfig();
        _session.SaveConfig();
        _overlay?.Configure(_session.Config.Overlay, _session.Languages);
        OnNotice($"配置已保存到 {_session.Store.FilePath}");
    }

    /// <summary>A preset was picked: fill the endpoint fields it names.</summary>
    private void OnPresetChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingUi) return;
        var label = ValueOf(PresetCombo, string.Empty);
        var preset = EnginePresets.FirstOrDefault(entry => entry.Label == label);
        if (preset.Label is null) return;

        ProviderCombo.Text = string.Empty;
        SelectByValue(ProviderCombo, "openai-compatible");
        BaseUrlBox.Text = preset.BaseUrl;
        ModelBox.Text = preset.Model;
        SelectByValue(PromptStyleCombo, preset.PromptStyle);

        // 本机地址不需要 Key;给在线服务配上够本地模型冷启动的超时,只会让错误的 Key 更晚暴露。
        TimeoutBox.Text = preset.BaseUrl.Contains("127.0.0.1", StringComparison.Ordinal) ? "120" : "60";
        UpdateEngineSummary();
    }

    /// <summary>An overlay field changed, so show what the current combination means.</summary>
    private void OnOverlayFieldChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingUi) return;
        UpdateOverlaySummary();
    }

    /// <summary>Apply a named look: a preset is data, copied into the flat overlay settings the overlay reads.</summary>
    private void OnOverlayPresetChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingUi) return;

        var name = ValueOf(OverlayPresetCombo, string.Empty);
        var preset = _session.Config.Overlay.Presets.FirstOrDefault(entry => entry.Name == name);
        if (preset is null) return;

        var overlay = _session.Config.Overlay;
        overlay.Placement = preset.Placement;
        overlay.ShowSource = preset.ShowSource;
        overlay.ShowPanel = preset.ShowPanel;
        overlay.BackgroundOpacity = preset.BackgroundOpacity;
        overlay.FontSize = preset.FontSize;
        overlay.Width = preset.Width;
        overlay.Height = preset.Height;
        overlay.CornerRadius = preset.CornerRadius;
        overlay.Padding = preset.Padding;
        overlay.TextAlign = preset.TextAlign;
        overlay.OffsetY = preset.OffsetY;

        LoadConfigIntoUi();
        OverlayPresetCombo.SelectedItem = OverlayPresetCombo.Items
            .OfType<Choice>()
            .FirstOrDefault(choice => choice.Value == name);
        OverlayPresetSummary.Text = preset.Note;

        _session.SaveConfig();
        _overlay?.Configure(overlay, _session.Languages);
        OnNotice($"已套用外观预设:{preset.Name}(可再微调下面的数值)");
    }

    /// <summary>Describe the placement in force, so the choice is never a guess.</summary>
    private void UpdateOverlaySummary()
    {
        var placement = ValueOf(PlacementCombo, OverlayPlacement.Below);
        var panel = ShowPanelCheck.IsChecked == true;
        var note = placement switch
        {
            OverlayPlacement.Over => "翻译框会盖住原对话框 —— 原文被遮住,建议同时关掉「显示原文」。",
            OverlayPlacement.Above => "翻译框贴在对话框上沿,原文保留在下方。",
            _ => "翻译框贴在对话框下沿,原文保留在上方。",
        };

        OverlayPresetSummary.Text = note + (panel
            ? string.Empty
            : " 当前不显示底框,只剩带描边的文字(靠「文字描边」保证可读)。");
    }

    /// <summary>Set the region to the bottom strip of the target window, where a visual novel puts its dialogue box.</summary>
    private void OnPickBottomStrip(object sender, RoutedEventArgs e)
    {
        if (WindowList.SelectedItem is not WindowInfo target)
        {
            OnNotice("请先在左侧列表里点选游戏窗口。");
            return;
        }

        var client = target.ClientRect;
        if (client.Width < 80 || client.Height < 80)
        {
            OnNotice("这个窗口的客户区太小,没法自动设区域,请手动框选。");
            return;
        }

        // 边距按窗口比例算,任何分辨率下同一次点击都成立;下沿特意留空 —— 那里是
        // SAVE / LOAD / CONFIG / AUTO,把按钮文字翻出来是手框区域最常见的抱怨。
        var width = (int)(client.Width * 0.92);
        var y = (int)(client.Height * 0.66);
        var height = Math.Max(60, (int)(client.Height * 0.27));
        var x = (client.Width - width) / 2;

        _session.SetRegion(target, new Int32Rect(client.X + x, client.Y + y, width, height));
        _session.SaveConfig();
        UpdateTargetSummary();
        OnNotice($"已把区域设为窗口底部(相对客户区 {_session.Config.Target.Region})。"
            + "如果没框准对话框,再用「框选区域」微调一次。");
    }

    /// <summary>Flip one overlay option from a hotkey so a player need not leave the game: <paramref name="source"/> toggles the original line, otherwise the panel.</summary>
    private void ToggleOverlayOption(bool source)
    {
        if (source) ShowSourceCheck.IsChecked = ShowSourceCheck.IsChecked != true;
        else ShowPanelCheck.IsChecked = ShowPanelCheck.IsChecked != true;

        ReadUiIntoConfig();
        _session.SaveConfig();
        _overlay?.Configure(_session.Config.Overlay, _session.Languages);
        UpdateOverlaySummary();

        OnNotice(source
            ? $"原文显示:{(ShowSourceCheck.IsChecked == true ? "开" : "关")}"
            : $"翻译框:{(ShowPanelCheck.IsChecked == true ? "显示" : "已隐藏(只剩文字)")}");
    }

    /// <summary>Reopen the setup card a first run shows, so filling in a key later does not mean hunting through the tabs.</summary>
    private async void OnOpenSetup(object sender, RoutedEventArgs e)
    {
        var wasRunning = _session.IsRunning;
        if (wasRunning) await _session.StopAsync().ConfigureAwait(true);

        var setup = new SetupWindow(_session) { Owner = this };
        var confirmed = setup.ShowDialog();

        LoadConfigIntoUi();
        if (confirmed == true && wasRunning) _session.Start();
        OnNotice(confirmed == true ? "API 设置已保存。" : "未修改 API 设置。");
    }

    /// <summary>The panel was moved or resized with the mouse; the overlay edits the live configuration, so saving and refreshing the fields is all that is left.</summary>
    private void OnOverlayLayoutChanged()
    {
        var overlay = _session.Config.Overlay;
        _session.SaveConfig();

        _loadingUi = true;
        OverlayWidthBox.Text = Text(overlay.Width);
        OverlayHeightBox.Text = Text(overlay.Height);
        OverlayOffsetXBox.Text = Text(overlay.OffsetX);
        OverlayOffsetYBox.Text = Text(overlay.OffsetY);
        _loadingUi = false;

        var size = (overlay.Width == 0 ? "自适应" : overlay.Width.ToString())
            + "×"
            + (overlay.Height == 0 ? "自适应" : overlay.Height.ToString());
        OnNotice($"翻译框已保存:位置偏移 {overlay.OffsetX},{overlay.OffsetY} · 尺寸 {size}"
            + "(按 Ctrl+Alt+U 锁回穿透状态)");
    }

    /// <summary>Lock or unlock the panel: unlocked it can be dragged and resized, which is the only time it takes clicks from the game.</summary>
    private void ToggleOverlayEditMode()
    {
        ClickThroughCheck.IsChecked = ClickThroughCheck.IsChecked != true;
        ReadUiIntoConfig();
        _session.SaveConfig();
        _overlay?.Configure(_session.Config.Overlay, _session.Languages);
        UpdateOverlaySummary();

        OnNotice(ClickThroughCheck.IsChecked == true
            ? "翻译框已锁定:鼠标点击穿透到游戏。"
            : "翻译框已解锁:拖动它移动位置、拖四角缩放大小,调好后按 Ctrl+Alt+U 锁回。");
    }

    /// <summary>The switcher's "其他" button: show the full direction list under the bar and apply whatever comes back.</summary>
    private void OnDirectionsRequested()
    {
        var overlay = _session.Config.Overlay;
        var picked = DirectionChooserWindow.Choose(
            this,
            overlay.LanguagePresets,
            _session.Languages,
            _overlay?.LanguageBarBounds() ?? default);

        if (picked is not null) OnLanguageRequested(picked);
    }

    /// <summary>A switcher toggle: the edit lock, the original line, or the panel.</summary>
    private void OnOverlayToggleRequested(string key)
    {
        switch (key)
        {
            case "edit":
                ToggleOverlayEditMode();
                break;
            case "source":
                ToggleOverlayOption(source: true);
                break;
            case "panel":
                ToggleOverlayOption(source: false);
                break;
        }
    }

    private async void OnLanguageRequested(LanguagePreset preset)
    {
        await _session.ApplyLanguagePresetAsync(preset).ConfigureAwait(true);
        SyncLanguageControls();
    }

    /// <summary>The picker's entries: the plain translator first, then every profile.</summary>
    private Choice[] ProfileChoices()
    {
        var choices = new List<Choice> { new(string.Empty, "不使用 —— 通用翻译(没有术语表)") };
        choices.AddRange(_session.Config.Translation.GameProfiles.Select(profile =>
            new Choice(profile.Id, $"{profile.Name} —— {profile.Terms.Count} 条术语")));
        return [.. choices];
    }

    private async void OnGameProfileChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingUi) return;
        var profile = _session.FindProfile(ValueOf(GameProfileCombo, string.Empty));
        await _session.ApplyProfileAsync(profile).ConfigureAwait(true);
        SyncProfileControls();
    }

    private void OnProfileOptionChanged(object sender, RoutedEventArgs e)
    {
        if (_loadingUi) return;
        ReadUiIntoConfig();
        _session.SaveConfig();
        UpdateProfileSummary();
    }

    /// <summary>Open the editor for the profile in force, creating one when none is.</summary>
    private void OnEditGameProfile(object sender, RoutedEventArgs e)
    {
        var active = _session.ActiveProfile;
        if (active is null)
        {
            OnNewGameProfile(sender, e);
            return;
        }

        OpenProfileEditor(active);
    }

    private void OnNewGameProfile(object sender, RoutedEventArgs e) => OpenProfileEditor(null);

    /// <summary>「✨ AI 生成术语表」: a new profile whose editor starts drafting at once.</summary>
    private void OnGenerateTermSheet(object sender, RoutedEventArgs e) => OpenProfileEditor(null, generate: true);

    private void OpenProfileEditor(GameProfile? profile, bool generate = false)
    {
        var wasRunning = _session.IsRunning;
        var window = new GameProfileWindow(_session, profile) { Owner = this };
        if (generate) window.GenerateOnLoad = true;
        var saved = window.ShowDialog();

        if (saved != true || window.Result is null)
        {
            if (generate) OnNotice("没有保存游戏档案。");
            return;
        }

        var result = window.Result;
        _session.ForgetContext();

        // 新建的档案就是用户想用的那一份;编辑过的只在它本来就生效时才套用。
        if (window.Created || _session.Config.Translation.ActiveProfile.Equals(result.Id, StringComparison.OrdinalIgnoreCase))
        {
            _ = ApplyProfileAndSyncAsync(result);
        }

        SyncProfileControls();
        OnNotice($"「{result.Name}」已保存:{result.Terms.Count} 条术语"
            + $"、{result.Terms.Sum(term => term.Forbidden.Count)} 条禁用译法"
            + (wasRunning ? ",下一句开始生效。" : "。"));
    }

    private async Task ApplyProfileAndSyncAsync(GameProfile? profile)
    {
        await _session.ApplyProfileAsync(profile).ConfigureAwait(true);
        SyncProfileControls();
    }

    /// <summary>Show what one line actually sends: the rules, the term table, the setting description, and the context.</summary>
    private void OnPreviewPrompt(object sender, RoutedEventArgs e)
    {
        ReadUiIntoConfig();
        var profile = _session.CurrentProfile;
        var languages = _session.Languages;

        // 有最近识别到的句子就用它,这样预览的是真实请求,不是示例。
        var sample = _session.RecentSources.LastOrDefault() ?? "（示例原文:这里会放当前这句台词）";
        var request = new TranslationRequest
        {
            Text = sample,
            From = languages.From,
            To = languages.To,
            Glossary = profile.Glossary,
            StyleHint = profile.StyleHint,
            Worldview = profile.Worldview,
        };

        var style = Enum.TryParse<PromptStyle>(_session.Config.Translation.Translator.PromptStyle, ignoreCase: true, out var parsed)
            ? parsed
            : PromptStyle.Galgame;

        var (system, user) = OpenAiCompatibleTranslator.PreviewPrompts(request, style);
        new PromptPreviewWindow(
            _session.ActiveProfile,
            $"{languages.From} → {languages.To}",
            system,
            user)
        {
            Owner = this,
        }.ShowDialog();
    }

    /// <summary>The switcher's game button: list the profiles under the bar and apply the pick.</summary>
    private void OnProfilesRequested()
    {
        var profiles = _session.Config.Translation.GameProfiles;
        var items = new List<ListChooserWindow.Item>
        {
            new("🌐 通用翻译", "不用术语表,也不做术语校正"),
        };
        items.AddRange(profiles.Select(profile => new ListChooserWindow.Item(
            $"🎮 {profile.Name}",
            $"{profile.Terms.Count} 条术语、{profile.Terms.Sum(term => term.Forbidden.Count)} 条禁用译法"
                + (profile.WindowHints.Count == 0 ? string.Empty : $" · 窗口关键字 {string.Join("/", profile.WindowHints)}"))));

        var activeIndex = _session.ActiveProfile is { } active
            ? profiles.FindIndex(profile => profile.Id == active.Id) + 1
            : 0;

        var picked = ListChooserWindow.Choose(this, items, activeIndex, _overlay?.LanguageBarBounds() ?? default);
        if (picked < 0) return;

        var chosen = picked == 0 ? null : profiles[picked - 1];
        _ = ApplyProfileAndSyncAsync(chosen);
    }

    /// <summary>Relabel everything that shows which profile is in force.</summary>
    private void SyncProfileControls()
    {
        _loadingUi = true;
        GameProfileCombo.ItemsSource = ProfileChoices();
        SelectByValue(GameProfileCombo, _session.Config.Translation.ActiveProfile);
        AutoProfileCheck.IsChecked = _session.Config.Translation.AutoDetectProfile;
        EnforceTermsCheck.IsChecked = _session.Config.Translation.EnforceTerms;
        _loadingUi = false;

        UpdateProfileSummary();
        _overlay?.SetProfile(_session.ActiveProfile?.Name);
    }

    private void UpdateProfileSummary()
    {
        var active = _session.ActiveProfile;
        if (active is null)
        {
            GameProfileSummary.Text = "当前生效:通用翻译 —— 没有术语表,专有名词全靠模型自己判断。"
                + "想让「新九人会」这类词译得和官中一致,就新建一份档案。";
            return;
        }

        var bans = active.Terms.Sum(term => term.Forbidden.Count);
        GameProfileSummary.Text = $"当前生效:{active.Name} —— {active.Terms.Count} 条术语、{bans} 条禁用译法"
            + (string.IsNullOrWhiteSpace(active.Worldview) ? " · 没有写世界观(补上它往往比加术语更有效)" : " · 已带世界观描述")            + (_session.Config.Translation.EnforceTerms ? " · 强制校正:开" : " · 强制校正:关(模型可以不理术语表)");
    }

    /// <summary>The profile changed from somewhere else, such as auto-detection.</summary>
    private void OnProfileChanged(GameProfile? profile) =>
        Dispatcher.Invoke(SyncProfileControls);

    /// <summary>The direction changed, wherever it was changed from.</summary>
    private void OnLanguagesChanged(LanguagePair languages) => Dispatcher.Invoke(SyncLanguageControls);

    /// <summary>Resync every control that shows the language direction.</summary>
    private void SyncLanguageControls()
    {
        _loadingUi = true;
        SelectByValue(OcrLanguageCombo, _session.Config.Ocr.Language);
        SelectByValue(FromCombo, _session.Config.Translation.From);
        SelectByValue(ToCombo, _session.Config.Translation.To);
        _loadingUi = false;

        _overlay?.SetLanguages(_session.Languages);
        UpdateEngineSummary();
    }

    /// <summary>Start the loop as if the button had been pressed, for the <c>--autostart</c> command line.</summary>
    public bool StartAutomatically()
    {
        OnStart(this, new RoutedEventArgs());
        return _session.IsRunning;
    }

    /// <summary>Open the current provider's own console so the reader can create a key; the
    /// URL follows whatever the endpoint field already points at, so a proxy or a local server
    /// does not send them to a vendor they are not using.</summary>
    private void OnGetApiKey(object sender, RoutedEventArgs e)
    {
        var baseUrl = BaseUrlBox.Text.Trim();
        var url = baseUrl.Contains("api.deepseek.com", StringComparison.OrdinalIgnoreCase) ? "https://platform.deepseek.com/"
            : baseUrl.Contains("bigmodel.cn", StringComparison.OrdinalIgnoreCase) ? "https://open.bigmodel.cn/"
            : baseUrl.Contains("siliconflow", StringComparison.OrdinalIgnoreCase) ? "https://cloud.siliconflow.cn/"
            : baseUrl.Contains("127.0.0.1", StringComparison.Ordinal) || baseUrl.Contains("localhost", StringComparison.OrdinalIgnoreCase)
                ? "https://ollama.com/download"
                : string.Empty;

        if (url.Length == 0)
        {
            // 没有可指向的页面:选项和链接都在设置卡片里解释,所以把人送到那里,而不是哪里都不送。
            OnOpenSetup(sender, e);
            return;
        }

        OpenExternal(url, "获取 API Key");
    }

    /// <summary>Open Windows' language settings, where the OCR feature is installed.</summary>
    private void OnOpenLanguageSettings(object sender, RoutedEventArgs e) =>
        OpenExternal("ms-settings:regionlanguage", "Windows 语言设置");

    /// <summary>Hand a URL to the shell; <c>UseShellExecute</c> is what makes the browser (or the Settings app, for an <c>ms-settings:</c> URI) open instead of .NET executing the string as a program.</summary>
    private void OpenExternal(string target, string what)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            OnNotice($"{what}:已用系统默认程序打开 {target}");
        }
        catch (Exception exception)
        {
            OnNotice($"{what}打不开({exception.Message}),请手动访问 {target}");
        }
    }

    private void OnProviderChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingUi) return;

        // mock 与真实端点之间是「演示」和「能用」的区别,所以顺手填一个像样的地址;
        // 经典 API 有自己的固定端点,地址和模型栏就不动,免得看起来像个其实无用的设置。
        var provider = ValueOf(ProviderCombo, "mock");
        if (ClassicEngines.ContainsKey(provider))
        {
            UpdateEngineSummary();
            return;
        }

        if (provider.Equals("mock", StringComparison.OrdinalIgnoreCase))
        {
            BaseUrlBox.Text = "http://localhost:11434/v1";
            ModelBox.Text = "qwen2.5:7b-instruct";
        }
        else if (string.IsNullOrWhiteSpace(BaseUrlBox.Text))
        {
            BaseUrlBox.Text = "http://localhost:11434/v1";
        }

        UpdateEngineSummary();
    }

    private void OnClearCache(object sender, RoutedEventArgs e)
    {
        _session.InvalidateTranslation();
        OnNotice($"翻译缓存已清空(命中 {_session.Cache.Hits} 次 / 未命中 {_session.Cache.Misses} 次)。");
    }

    private void OnOpenDumpDirectory(object sender, RoutedEventArgs e) =>
        OpenDirectory(string.IsNullOrWhiteSpace(DumpDirBox.Text) ? AppSession.DefaultDumpDirectory() : DumpDirBox.Text.Trim());

    private void OnOpenConfigDirectory(object sender, RoutedEventArgs e) => OpenDirectory(_session.Store.Directory);

    private void OpenDirectory(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            OnNotice($"打开目录失败:{exception.Message}");
        }
    }

    private void OnPipelineUpdate(PipelineUpdate update)
    {
        // 循环在自己的线程上发布,这里碰到的每个控件都属于 dispatcher。
        Dispatcher.Invoke(() =>
        {
            StatusText.Text = Describe(update);

            if (update.Ocr is not null) SourceBox.Text = update.SourceText;

            if (update.Status == PipelineStatus.NoText)
            {
                SourceBox.Text = "(未识别到文字)";
                return;
            }

            if (update.Translation is null) return;

            TranslationBox.Text = update.Translation;

            var region = _session.ResolveRegion();
            if (region is { } rect) _overlay?.ShowTranslated(update.SourceText, update.Translation, rect);

            var stats = _session.Pipeline?.Stats;
            if (stats is not null)
            {
                StatsText.Text = $"帧 {stats.Frames} · 识别 {stats.Recognitions} · 翻译 {stats.Translations}"
                    + $" · 缓存 {stats.CacheHits} · 去重 {stats.RepeatedLines} · 错误 {stats.Errors}"
                    + $" · 用时 捕获 {update.CaptureDuration.TotalMilliseconds:F0}ms"
                    + $" / 识别 {update.OcrDuration.TotalMilliseconds:F0}ms"
                    + $" / 翻译 {update.TranslateDuration.TotalMilliseconds:F0}ms";
            }
        });
    }

    private static string Describe(PipelineUpdate update) => update.Status switch
    {
        PipelineStatus.NoRegion => "没有可用的区域:目标窗口可能已关闭或最小化。",
        PipelineStatus.Unchanged => "画面未变化,跳过识别。",
        PipelineStatus.NoText => update.Skipped == PipelineSkipReason.WrongScript
            ? $"识别到的是界面文字(「{update.SourceText}」),不像台词,已跳过 —— 这块区域可能压到了游戏的按钮。"
                + "用 Ctrl+Alt+R 把区域收紧到对话框上,或在「调试」页关掉脚本守卫。"
            : $"未识别到文字(变化格数 {update.SignatureDistance})。",
        PipelineStatus.Translating => "翻译中…(译文会边生成边显示)",
        PipelineStatus.Blank => "捕获到的是纯色画面 —— 说明问题在抓屏,不在识别:"
            + "游戏若为全屏独占请改成「无边框窗口」,或窗口被遮挡 / 区域已不在对话框上。",
        PipelineStatus.Translated => update.FromCache
            ? "命中翻译缓存,直接显示。"
            : "已翻译。",
        PipelineStatus.Reused => "与上一句几乎相同,复用已有译文。",
        PipelineStatus.Error => $"出错:{update.Error}",
        _ => update.Status.ToString(),
    };

    private void OnNotice(string message) =>
        Dispatcher.Invoke(() => StatusText.Text = message);

    private static string Text(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static double Number(string text, double fallback) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : fallback;
}
