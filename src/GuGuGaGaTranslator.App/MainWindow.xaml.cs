using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using GuGuGaGaTranslator.Core.Capture;
using GuGuGaGaTranslator.Core.Config;
using GuGuGaGaTranslator.Core.Interop;
using GuGuGaGaTranslator.Core.Ocr;
using GuGuGaGaTranslator.Core.Pipeline;
using GuGuGaGaTranslator.Core.Translation;
using Microsoft.Win32;

namespace GuGuGaGaTranslator.App;

/// <summary>主窗口，负责目标选择、设置、运行状态和悬浮层控制。</summary>
public partial class MainWindow : Window
{
    private const int HotkeyToggle = 9001;
    private const int HotkeyPause = 9002;
    private const int HotkeyRegion = 9003;
    private const int HotkeySource = 9004;
    private const int HotkeyPanel = 9005;
    private const int HotkeyEdit = 9006;
    private const int HotkeyRegionAndStart = 9007;

    /// <summary>热键绑定，包含配置读写和执行动作。</summary>
    private sealed record HotkeyAction(
        int Id,
        string Label,
        string Hint,
        Func<HotkeyConfig, string> Read,
        Action<HotkeyConfig, string> Write,
        Action Invoke);

    /// <summary>What the region picker hid, so it can be put back exactly as it was.</summary>
    private sealed record HiddenOwnWindows(bool Main, bool Panel);

    /// <summary>下拉选项的配置值和显示名称。</summary>
    private sealed record Choice(string Value, string Label)
    {
        public override string ToString() => Label;
    }

    private static readonly Choice AutoLanguage = new("auto", LanguageNames.Label("auto", "让引擎自己判断原文语言"));

    /// <summary>支持的翻译语言。</summary>
    private static readonly Choice[] TranslationLanguages =
    [
        new("zh-Hans", LanguageNames.Label("zh-Hans")),
        new("ja", LanguageNames.Label("ja")),
        new("en", LanguageNames.Label("en")),
    ];

    /// <summary>可选识别语言，加载时标注系统可用性。</summary>
    private static readonly Choice[] OcrLanguages =
    [
        new("auto", LanguageNames.Label("auto", "逐个已装的语言试一遍,取最像文字的结果")),
        new("ja", LanguageNames.Label("ja")),
        new("en-US", LanguageNames.Label("en-US")),
        new("zh-Hans-CN", LanguageNames.Label("zh-Hans-CN")),
    ];

    private static readonly Choice[] EngineProviders =
    [
        new("mock", "预览模式（mock）：只识别，不翻译"),
        new("openai-compatible", "OpenAI 兼容接口（云端或本地 AI 模型）"),
        new("caiyun", "彩云小译 —— 基础翻译，支持译后术语校正"),
        new("youdao", "有道翻译 —— 基础翻译，支持译后术语校正"),
        new("baidu", "百度翻译 —— 基础翻译，支持译后术语校正"),
    ];

    /// <summary>悬浮层相对识别区域的位置。</summary>
    private static readonly Choice[] OverlayPlacements =
    [
        new(OverlayPlacement.Over, "遮盖原文 —— 翻译框盖住原对话框(galgame 推荐)"),
        new(OverlayPlacement.Below, "下方 —— 原文保留,译文贴在对话框下沿"),
        new(OverlayPlacement.Above, "上方 —— 原文保留,译文贴在对话框上沿"),
    ];

    /// <summary>Recognition backends: the in-box one, and the bundled offline model.</summary>
    private static readonly Choice[] OcrEngines =
    [
        new("rapidocr", "RapidOCR：附带中、日、英模型，无需系统语言功能"),
        new("windows", "Windows OCR —— 系统自带，需对应语言包；耗时取决于区域与语言"),
    ];

    /// <summary>Endpoint presets; they only fill the three fields below.</summary>
    private static readonly (string Label, string BaseUrl, string Model, string PromptStyle)[] EnginePresets =
    [
        ("本地 Ollama：通用模型", "http://127.0.0.1:11434/v1", "qwen2.5:7b-instruct", "galgame"),
        ("本地 Sakura：日译中模型", "http://127.0.0.1:11434/v1", "sakura-galtransl:7b", "sakura"),
        ("DeepSeek 官方 API(按量计费)", "https://api.deepseek.com", "deepseek-flash", "galgame"),
        ("智谱 GLM(需自备 Key)", "https://open.bigmodel.cn/api/paas/v4", "glm-4-flash", "galgame"),
        ("硅基流动 SiliconFlow(需自备 Key)", "https://api.siliconflow.cn/v1", "Qwen/Qwen2.5-7B-Instruct", "galgame"),
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
    private bool _closing;
    private CancellationTokenSource? _providerTestCancellation;
    private List<HotkeyAction> _actions = [];
    private readonly List<int> _registeredHotkeys = [];
    private readonly List<string> _hotkeyFailures = [];

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

        // 启动时按已保存的目标窗口匹配档案。
        await _session.AutoDetectProfileAsync().ConfigureAwait(true);
        SyncProfileControls();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _closing = true;
        _providerTestCancellation?.Cancel();
        // 加载前关闭窗口时句柄为 0，不能传给 HwndSource.FromHwnd。
        if (_handle != 0)
        {
            HwndSource.FromHwnd(_handle)?.RemoveHook(OnWindowMessage);
            UnregisterHotkeys();
        }

        _session.Updated -= OnPipelineUpdate;
        _session.Notice -= OnNotice;
        _session.LanguagesChanged -= OnLanguagesChanged;
        _session.ProfileChanged -= OnProfileChanged;
        _overlay?.ClosePermanently();
    }

    /// <summary>Every configurable global hotkey: its text in the configuration, what it does, and how it reads.</summary>
    private List<HotkeyAction> HotkeyActions() =>
    [
        new(HotkeyToggle, "开始 / 停止翻译", "在游戏里按一下就开始,再按一下停",
            config => config.StartStop, (config, value) => config.StartStop = value,
            () => { if (_session.IsRunning) OnStop(this, new RoutedEventArgs()); else OnStart(this, new RoutedEventArgs()); }),
        new(HotkeyRegionAndStart, "一键框选并翻译", "按一下直接进框选,Enter 确认后立刻开始翻译 —— 不用切回控制窗口",
            config => config.RegionAndStart, (config, value) => config.RegionAndStart = value,
            OnRegionAndStart),
        new(HotkeyRegion, "只重新框选区域", "换一块区域,框完接着翻译(已经在翻译时用它)",
            config => config.Region, (config, value) => config.Region = value,
            () => OnPickRegion(this, new RoutedEventArgs())),
        new(HotkeyPause, "暂停 / 继续识别", "只停识别,翻译框保留最后一句",
            config => config.Pause, (config, value) => config.Pause = value,
            () => OnPause(this, new RoutedEventArgs())),
        new(HotkeySource, "显示 / 隐藏原文", "译文上方显示识别到的原文",
            config => config.ShowSource, (config, value) => config.ShowSource = value,
            () => ToggleOverlayOption(source: true)),
        new(HotkeyPanel, "显示 / 隐藏翻译框", "临时把译文藏起来看原画面",
            config => config.TogglePanel, (config, value) => config.TogglePanel = value,
            () => ToggleOverlayOption(source: false)),
        new(HotkeyEdit, "解锁 / 锁定翻译框", "解锁后可拖动、拖四角缩放;锁回后点击穿透给游戏",
            config => config.ToggleEdit, (config, value) => config.ToggleEdit = value,
            ToggleOverlayEditMode),
    ];

    private void UnregisterHotkeys()
    {
        foreach (var id in _registeredHotkeys)
            HotkeyInterop.Unregister(_handle, id);
        _registeredHotkeys.Clear();
    }

    /// <summary>Register every configured hotkey; the ones Windows refuses (already taken, or malformed) are reported.</summary>
    private void RegisterHotkeys()
    {
        if (_handle == 0)
            return;

        _actions = HotkeyActions();
        UnregisterHotkeys();
        _hotkeyFailures.Clear();

        var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var action in _actions)
        {
            var text = action.Read(_session.Config.Hotkeys);
            if (!HotkeyGesture.TryParse(text, out var gesture))
            {
                if (!string.IsNullOrWhiteSpace(text))
                    _hotkeyFailures.Add($"{action.Label}:「{text}」看不懂,已跳过");
                continue;
            }

            if (seen.TryGetValue(gesture.ToString(), out var owner))
            {
                _hotkeyFailures.Add($"{action.Label}:和「{owner}」撞在同一个键 {gesture} 上,已跳过");
                continue;
            }

            if (HotkeyInterop.Register(_handle, action.Id, gesture.RegisterModifiers, gesture.VirtualKey))
            {
                _registeredHotkeys.Add(action.Id);
                seen[gesture.ToString()] = action.Label;
            }
            else
            {
                _hotkeyFailures.Add($"{action.Label}:{gesture} 被其他程序占用了");
            }
        }

        UpdateHotkeySummary();
        BuildHotkeyRows();
    }

    /// <summary>Restate the current bindings and anything that failed, in both places that show them.</summary>
    private void UpdateHotkeySummary()
    {
        var lines = _actions
            .Select(action => (Action: action, Text: action.Read(_session.Config.Hotkeys)))
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Text))
            .Select(pair => $"{pair.Text}  {pair.Action.Label}");

        HotkeyText.Text = "全局快捷键：" + Environment.NewLine
            + string.Join(" · ", lines)
            + (_hotkeyFailures.Count > 0
                ? Environment.NewLine + "未注册成功：" + string.Join("；", _hotkeyFailures)
                : string.Empty);

        if (HotkeyStatus is not null)
            HotkeyStatus.Text = HotkeyText.Text;
    }

    /// <summary>Build the editable rows of the 热键 page: one labelled box per action, typing straight into it records a combination.</summary>
    private void BuildHotkeyRows()
    {
        if (HotkeyRows is null)
            return;

        HotkeyRows.Children.Clear();
        var label = (Style)FindResource("FieldLabel");

        foreach (var action in _actions)
        {
            var row = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
            row.Children.Add(new TextBlock
            {
                Text = $"{action.Label} —— {action.Hint}",
                Style = label,
                TextWrapping = TextWrapping.Wrap,
            });

            var box = new TextBox
            {
                Text = action.Read(_session.Config.Hotkeys),
                Width = 240,
                HorizontalAlignment = HorizontalAlignment.Left,
                Tag = action,
                ToolTip = "点进来直接按组合键即可录入;Backspace 清除 = 停用这一项",
            };
            box.PreviewKeyDown += OnHotkeyBoxKeyDown;
            box.LostKeyboardFocus += (_, _) => RefreshHotkeyRows();
            row.Children.Add(box);

            HotkeyRows.Children.Add(row);
        }
    }

    /// <summary>Show the stored bindings again after a recording attempt.</summary>
    private void RefreshHotkeyRows()
    {
        if (HotkeyRows is null)
            return;

        foreach (var child in HotkeyRows.Children)
        {
            if (child is not StackPanel row || row.Children.Count < 2)
                continue;
            if (row.Children[1] is not TextBox box || box.Tag is not HotkeyAction action)
                continue;
            box.Text = action.Read(_session.Config.Hotkeys);
        }
    }

    /// <summary>Record a combination by pressing it: no syntax to type, and nothing is written until it parses.</summary>
    private void OnHotkeyBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox box || box.Tag is not HotkeyAction action)
            return;
        e.Handled = true;

        // 只按修饰键不算一个组合,等真正的按键。
        if (e.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.System)
        {
            return;
        }

        if (e.Key is Key.Back or Key.Delete)
        {
            ApplyHotkey(action, HotkeyGesture.None);
            return;
        }

        if (e.Key == Key.Escape)
        {
            box.Text = action.Read(_session.Config.Hotkeys);
            return;
        }

        var virtualKey = (uint)KeyInterop.VirtualKeyFromKey(e.Key);
        var gesture = new HotkeyGesture(ModifiersOf(Keyboard.Modifiers), virtualKey);
        if (gesture.RegisterModifiers == 0)
        {
            OnNotice("全局热键至少要带一个 Ctrl / Alt / Shift / Win,否则会把普通按键从游戏手里抢走。");
            return;
        }

        ApplyHotkey(action, gesture);
    }

    private void ApplyHotkey(HotkeyAction action, HotkeyGesture gesture)
    {
        action.Write(_session.Config.Hotkeys, gesture.IsEmpty ? string.Empty : gesture.ToString());
        _session.SaveConfig();
        RegisterHotkeys();
        BuildHotkeyRows();

        OnNotice(gesture.IsEmpty
            ? $"已停用「{action.Label}」的热键。"
            : $"「{action.Label}」的热键现在是 {gesture}。");
    }

    private static uint ModifiersOf(ModifierKeys keys)
    {
        uint modifiers = 0;
        if ((keys & ModifierKeys.Control) != 0)
            modifiers |= HotkeyInterop.ModControl;
        if ((keys & ModifierKeys.Alt) != 0)
            modifiers |= HotkeyInterop.ModAlt;
        if ((keys & ModifierKeys.Shift) != 0)
            modifiers |= HotkeyInterop.ModShift;
        if ((keys & ModifierKeys.Windows) != 0)
            modifiers |= HotkeyGesture.ModWin;
        return modifiers;
    }

    private void OnResetHotkeys(object sender, RoutedEventArgs e)
    {
        _session.Config.Hotkeys = HotkeyConfig.Default();
        _session.SaveConfig();
        RegisterHotkeys();
        BuildHotkeyRows();
        OnNotice("热键已恢复默认。");
    }

    private nint OnWindowMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message != HotkeyInterop.WmHotkey)
            return 0;
        handled = true;

        var action = _actions?.FirstOrDefault(candidate => candidate.Id == (int)wParam);
        action?.Invoke();

        return 0;
    }

    /// <summary>Select a region and start translation.</summary>
    private void OnRegionAndStart()
    {
        if (WindowList.SelectedItem is not WindowInfo)
        {
            var foreground = ForegroundTarget();
            if (foreground is null)
            {
                OnNotice("没能认出当前窗口:先在左侧列表里点一个窗口,或用 Ctrl+Alt+R 框选。");
                return;
            }

            _session.SetTarget(foreground);
            RefreshWindowList();
            OnNotice($"已自动选中当前窗口:{foreground.Title}");
        }

        OnPickRegion(this, new RoutedEventArgs());
    }

    /// <summary>The window the user is working in, unless it is one of ours or has no title to remember it by.</summary>
    private static WindowInfo? ForegroundTarget()
    {
        var handle = HotkeyInterop.ForegroundWindow();
        if (handle == 0)
            return null;

        var window = WindowEnumerator.TryDescribe(handle);
        if (window is null)
            return null;
        if (window.ProcessId == Environment.ProcessId)
            return null;
        return string.IsNullOrWhiteSpace(window.Title) ? null : window;
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
                    : choice with
                    {
                        Label = $"{choice.Label} —— 未安装语言包,需在 Windows 设置里添加"
                    })
            .ToList();

        foreach (var tag in installed.Where(tag => OcrLanguages.All(choice => !choice.Value.Equals(tag, StringComparison.OrdinalIgnoreCase))))
        {
            ocrChoices.Add(new Choice(tag, LanguageNames.Label(tag, "系统已安装")));
        }

        OcrLanguageCombo.ItemsSource = ocrChoices;

        OcrEngineCombo.ItemsSource = OcrEngines.ToArray();
        CaptureBackendCombo.ItemsSource = new Choice[]
        {
            new("window", "窗口捕获（推荐，支持遮挡）"),
            new("screen", "屏幕捕获（兼容模式，需无遮挡）"),
            new("printwindow", "PrintWindow（传统窗口兼容）"),
        };
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

    /// <summary>按配置值选择下拉项，自定义值会补充到列表中。</summary>
    private static void SelectByValue(ComboBox combo, string value)
    {
        if (combo.ItemsSource is not IEnumerable<Choice> choices)
            return;

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

    /// <summary>读取下拉项的配置值。</summary>
    private static string ValueOf(ComboBox combo, string fallback) =>
        combo.SelectedItem is Choice choice ? choice.Value
        : string.IsNullOrWhiteSpace(combo.Text) ? fallback
        : combo.Text.Trim();

    /// <summary>Restate what the engine settings will actually do: "mock" and a half-filled URL both look like a working configuration.</summary>
    private void UpdateEngineSummary()
    {
        var provider = ValueOf(ProviderCombo, "mock");
        var classicProvider = ClassicEngines.ContainsKey(provider);
        AISettingsPanel.Visibility = classicProvider ? Visibility.Collapsed : Visibility.Visible;
        ClassicCredentialsPanel.Visibility = classicProvider ? Visibility.Visible : Visibility.Collapsed;
        ClassicIdPanel.Visibility = provider == "caiyun" ? Visibility.Collapsed : Visibility.Visible;
        var chatProvider = provider is "openai-compatible" or "openai" or "local";
        HistoryLinesBox.IsEnabled = chatProvider;
        TemperatureBox.IsEnabled = chatProvider;
        GenerateSheetButton.IsEnabled = chatProvider;
        PreviewPromptButton.IsEnabled = chatProvider;
        ProviderCapabilitiesText.Text = classicProvider
            ? "支持：基础翻译、译后术语校正。上下文、世界观、风格提示和 AI 术语生成不适用。"
            : chatProvider ? "支持：上下文、术语提示、世界观、风格、流式译文和 AI 术语生成。"
            : "仅回显识别到的原文，用于验证框选与 OCR；不会调用翻译服务。";
        if (provider.Equals("mock", StringComparison.OrdinalIgnoreCase))
        {
            EngineSummary.Text = "当前:不会真正翻译。识别到的原文会被原样加上 [mock …] 前缀显示,用来确认抓屏和识别是否正常。";
            return;
        }

        if (ClassicEngines.TryGetValue(provider, out var classic))
        {
            var appId = ClassicIdBox.Text.Trim();
            var secret = ClassicSecretBox.Password;

            EngineSummary.Text = $"当前:{classic.Name} —— 每句话直接发给它的官方接口："
                + (classic.NeedsAppId
                    ? $"应用 ID {Mask(appId)} / 密钥 {Mask(secret)}"
                    : $"令牌 {Mask(secret)}")
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
        if (_loadingUi)
            return;
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
        SelectByValue(CaptureBackendCombo, config.Target.CaptureBackend);
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
        ClassicIdBox.Text = config.Translation.Translator.AppId;
        ClassicSecretBox.Password = config.Translation.Translator.AppSecret;
        if (config.Translation.Translator.Provider == "caiyun" && string.IsNullOrEmpty(ClassicSecretBox.Password))
            ClassicSecretBox.Password = string.IsNullOrEmpty(config.Translation.Translator.ApiKey)
                ? config.Translation.Translator.AppId : config.Translation.Translator.ApiKey;
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
        HideOwnWindowsCheck.IsChecked = config.RegionPicker.HideOwnWindows;

        DumpFramesCheck.IsChecked = config.Debug.DumpFrames;
        DumpDirBox.Text = string.IsNullOrWhiteSpace(config.Debug.DumpDirectory)
            ? AppSession.DefaultDumpDirectory()
            : config.Debug.DumpDirectory;
        DumpKeepBox.Text = Text(config.Debug.KeepDumps);
        PollIntervalBox.Text = Text(config.Pipeline.PollIntervalMs);
        ChangeThresholdBox.Text = Text(config.Pipeline.ChangeThresholdBits);
        ForceRefreshBox.Text = Text(config.Pipeline.ForceRefreshMs);
        PersistentCacheCheck.IsChecked = config.Translation.Cache.Persist;
        UpdateCacheStatus();
        CacheRetentionBox.Text = Text(config.Translation.Cache.RetentionDays);
        CacheCapacityBox.Text = Text(config.Translation.Cache.MaximumEntries);

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
        config.Target.CaptureBackend = ValueOf(CaptureBackendCombo, "window");
        config.Target.CaptureSettingsVersion = 1;

        config.Ocr.Engine = ValueOf(OcrEngineCombo, "windows");
        config.Ocr.Language = ValueOf(OcrLanguageCombo, "auto");
        config.Ocr.Scale = Number(OcrScaleBox.Text, config.Ocr.Scale);
        config.Ocr.Grayscale = Number(OcrGrayBox.Text, config.Ocr.Grayscale);
        config.Ocr.RapidLimitSideLen = Math.Max(0, (int)Number(RapidLimitSideBox.Text, config.Ocr.RapidLimitSideLen));
        config.Ocr.RapidModelDirectory = RapidModelsBox.Text.Trim();

        config.Translation.From = ValueOf(FromCombo, "auto");
        config.Translation.To = ValueOf(ToCombo, "zh-Hans");
        var provider = ValueOf(ProviderCombo, "mock");
        var classic = ClassicEngines.ContainsKey(provider);
        config.Translation.Translator = config.Translation.Translator with
        {
            Provider = provider,
            BaseUrl = BaseUrlBox.Text.Trim(),
            Model = ModelBox.Text.Trim(),
            ApiKey = classic ? "" : ApiKeyBox.Password,
            AppId = ClassicIdBox.Text.Trim(),
            AppSecret = ClassicSecretBox.Password,
            TimeoutSeconds = (int)Number(TimeoutBox.Text, config.Translation.Translator.TimeoutSeconds),
            Temperature = Number(TemperatureBox.Text, config.Translation.Translator.Temperature),
            PromptStyle = ValueOf(PromptStyleCombo, "galgame"),
        };
        config.Translation.HistoryLines = Math.Clamp((int)Number(HistoryLinesBox.Text, config.Translation.HistoryLines), 0, 12);
        config.Translation.AutoDetectProfile = AutoProfileCheck.IsChecked == true;
        config.Translation.EnforceTerms = EnforceTermsCheck.IsChecked == true;
        config.Translation.Cache.Persist = PersistentCacheCheck.IsChecked == true;
        config.Translation.Cache.RetentionDays = Math.Clamp((int)Number(CacheRetentionBox.Text, config.Translation.Cache.RetentionDays), 1, 365);
        config.Translation.Cache.MaximumEntries = Math.Clamp((int)Number(CacheCapacityBox.Text, config.Translation.Cache.MaximumEntries), 100, 100000);

        config.Overlay.Placement = ValueOf(PlacementCombo, OverlayPlacement.Below);
        config.Overlay.ShowPanel = ShowPanelCheck.IsChecked == true;
        config.Overlay.FontSize = Number(OverlayFontBox.Text, config.Overlay.FontSize);
        config.Overlay.BackgroundOpacity = Math.Clamp(Number(OverlayOpacityBox.Text, config.Overlay.BackgroundOpacity), 0, 1);
        config.Overlay.MaxLines = Math.Max(0, (int)Number(OverlayMaxLinesBox.Text, config.Overlay.MaxLines));
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
        config.RegionPicker.HideOwnWindows = HideOwnWindowsCheck.IsChecked == true;

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
            if (match is not null)
                WindowList.SelectedItem = match;
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

        PickRegionFor(target);
    }

    /// <summary>Frame a region for a target and translate it; shared by the button and the hotkey.</summary>
    private void PickRegionFor(WindowInfo target)
    {
        // 和微信截图一样:框选时把自己的窗口收起来,免得控制窗口、翻译框和语言条压在要框的东西上。
        // 关掉这个选项时什么也不藏,方便对照着原来的界面框。
        var hidden = HideOwnWindows(_session.Config.RegionPicker.HideOwnWindows);
        Int32Rect? region;
        try
        {
            region = RegionSelectorWindow.Select(this, _session.Config.RegionPicker.HideOwnWindows, hide =>
            {
                // 勾选框实时生效:先把自己藏起来的恢复回去,再按新选择重来,免得越藏越多。
                RestoreOwnWindows(hidden);
                hidden = HideOwnWindows(hide);
                _session.Config.RegionPicker.HideOwnWindows = hide;
                _session.SaveConfig();
            });
        }
        finally
        {
            RestoreOwnWindows(hidden);
        }

        if (region is not { Width: >= 8, Height: >= 8 })
        {
            OnNotice("已取消框选。");
            return;
        }

        _session.SetRegion(target, region.Value);
        _session.SaveConfig();
        UpdateTargetSummary();

        // Keep the current pipeline running when only the selected region changes.
        if (!_session.IsRunning)
            OnStart(this, new RoutedEventArgs());
        OnNotice(_session.IsRunning
            ? $"已开始持续翻译,区域相对客户区 {_session.Config.Target.Region};窗口移动会自动跟随,"
                + "按 Ctrl+Alt+R 可重新框选。"
            : $"区域已记录(相对客户区 {_session.Config.Target.Region}),但没能开始翻译 —— 看上方的状态提示。");
    }

    /// <summary>Hide the control window and the overlay (panel + switcher) so the screen shows only what is
    /// being framed; returns what was actually hidden, to be handed back to <see cref="RestoreOwnWindows"/>.</summary>
    private HiddenOwnWindows HideOwnWindows(bool hide)
    {
        if (!hide)
            return new HiddenOwnWindows(false, false);

        var main = false;
        if (IsVisible)
        {
            Hide();
            main = true;
        }

        var panel = _overlay?.SuspendForPicker() ?? false;
        return new HiddenOwnWindows(main, panel);
    }

    private void RestoreOwnWindows(HiddenOwnWindows hidden)
    {
        if (hidden.Main)
        {
            // 不抢焦点:框完还要接着玩游戏。
            ShowActivated = false;
            Show();
        }

        _overlay?.ResumeAfterPicker(hidden.Panel);
    }

    private void OnClearRegion(object sender, RoutedEventArgs e)
    {
        _session.ClearRegion();
        _session.SaveConfig();
        UpdateTargetSummary();
        OnNotice("已清除区域与目标窗口。");
    }

    private async void OnStart(object sender, RoutedEventArgs e)
    {
        if (_session.IsStopping)
        {
            await _session.StopAsync().ConfigureAwait(true);
            if (_closing)
                return;
        }
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
        _overlay.DismissRequested -= OnOverlayDismissRequested;
        _overlay.DismissRequested += OnOverlayDismissRequested;
        _overlay.Configure(_session.Config.Overlay, _session.Languages);
        _overlay.SetProfile(_session.ActiveProfile?.Name);
        _session.Start();

        if (!_session.IsRunning)
            return;

        _overlay.Open();
        var region = _session.ResolveRegion();
        if (region is { } rect)
            _overlay.ShowTranslated(string.Empty, "等待台词…", rect);
        else
            _overlay.Show();
    }

    private void OnPause(object sender, RoutedEventArgs e)
    {
        if (!_session.IsRunning)
            return;
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
        _overlay?.Dismiss();
        await _session.StopAsync().ConfigureAwait(true);
        StatusText.Text = "已停止";
    }

    private void OnOverlayDismissRequested() => _ = StopAsync();

    private async void OnSave(object sender, RoutedEventArgs e)
    {
        ReadUiIntoConfig();
        _session.SaveConfig();
        if (_session.IsRunning)
        {
            await _session.StopAsync();
            _session.Start();
        }
        _overlay?.Configure(_session.Config.Overlay, _session.Languages);
        OnNotice($"配置已保存到 {_session.Store.FilePath}");
    }

    /// <summary>A preset was picked: fill the endpoint fields it names.</summary>
    private void OnPresetChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingUi)
            return;
        var label = ValueOf(PresetCombo, string.Empty);
        var preset = EnginePresets.FirstOrDefault(entry => entry.Label == label);
        if (preset.Label is null)
            return;

        if (!Uri.TryCreate(BaseUrlBox.Text.Trim(), UriKind.Absolute, out var previousEndpoint)
            || !Uri.TryCreate(preset.BaseUrl, UriKind.Absolute, out var nextEndpoint)
            || previousEndpoint.Authority != nextEndpoint.Authority)
            ApiKeyBox.Clear();

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
        if (_loadingUi)
            return;
        UpdateOverlaySummary();
    }

    /// <summary>Apply a named look: a preset is data, copied into the flat overlay settings the overlay reads.</summary>
    private void OnOverlayPresetChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingUi)
            return;

        var name = ValueOf(OverlayPresetCombo, string.Empty);
        var preset = _session.Config.Overlay.Presets.FirstOrDefault(entry => entry.Name == name);
        if (preset is null)
            return;

        ReadUiIntoConfig();
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
        // 回填选项会再次触发 SelectionChanged，选中预设时仍需保持保护。
        _loadingUi = true;
        try
        {
            OverlayPresetCombo.SelectedItem = OverlayPresetCombo.Items
                .OfType<Choice>()
                .FirstOrDefault(choice => choice.Value == name);
            OverlayPresetSummary.Text = preset.Note;
        }
        finally { _loadingUi = false; }

        _session.SaveConfig();
        _overlay?.Configure(overlay, _session.Languages);
        OnNotice($"已套用外观预设:{preset.Name}(可再微调下面的数值)");
    }

    /// <summary>更新悬浮层位置说明。</summary>
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

        // Keep the bottom control row outside the default dialogue area.
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
        if (source)
            ShowSourceCheck.IsChecked = ShowSourceCheck.IsChecked != true;
        else
            ShowPanelCheck.IsChecked = ShowPanelCheck.IsChecked != true;

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
        if (wasRunning)
            await _session.StopAsync().ConfigureAwait(true);

        var setup = new SetupWindow(_session) { Owner = this };
        var confirmed = setup.ShowDialog();

        LoadConfigIntoUi();
        if (confirmed == true && wasRunning)
            _session.Start();
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

    /// <summary>The capture switch, applied the moment it is clicked: recording the overlay means the pipeline may
    /// read its own translation back, so this is toggled on and off around a recording rather than left on.</summary>
    private void OnExcludeFromCaptureChanged(object sender, RoutedEventArgs e)
    {
        if (_loadingUi)
            return;

        var exclude = ExcludeFromCaptureCheck.IsChecked == true;
        _session.Config.Overlay.ExcludeFromCapture = exclude;
        _session.SaveConfig();
        _overlay?.Configure(_session.Config.Overlay, _session.Languages);

        OnNotice(exclude
            ? "翻译框已对录屏/截图隐藏。窗口捕获仍读取目标窗口的原文。"
            : "翻译框已允许被录屏/截图拍到。窗口捕获不受影响；屏幕捕获模式下请把翻译框移出识别区域。");
    }

    /// <summary>Remember whether framing hides this tool's own windows; the picker has the same switch, so the
    /// choice survives whichever place it was made.</summary>
    private void OnHideOwnWindowsChanged(object sender, RoutedEventArgs e)
    {
        if (_loadingUi)
            return;

        _session.Config.RegionPicker.HideOwnWindows = HideOwnWindowsCheck.IsChecked == true;
        _session.SaveConfig();
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

        if (picked is not null)
            OnLanguageRequested(picked);
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
        if (_loadingUi)
            return;
        var profile = _session.FindProfile(ValueOf(GameProfileCombo, string.Empty));
        await _session.ApplyProfileAsync(profile).ConfigureAwait(true);
        SyncProfileControls();
    }

    private async void OnProfileOptionChanged(object sender, RoutedEventArgs e)
    {
        if (_loadingUi)
            return;
        ReadUiIntoConfig();
        _session.SaveConfig();
        if (_session.IsRunning)
        {
            await _session.StopAsync();
            _session.Start();
        }
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

    private void OnImportProfile(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "GuGuGaGa 游戏档案 (*.ggprofile.json)|*.ggprofile.json|JSON 文件 (*.json)|*.json" };
        if (dialog.ShowDialog(this) != true)
            return;
        try
        {
            var profile = GameProfileArchive.Read(dialog.FileName);
            profile.Id = ""; // Open as a new copy; never silently replace an existing profile.
            OpenProfileEditor(profile);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            OnNotice("档案导入失败：" + error.Message);
        }
    }

    private void OnExportProfile(object sender, RoutedEventArgs e)
    {
        if (_session.ActiveProfile is not { } profile)
        {
            OnNotice("请先选择要导出的游戏档案。");
            return;
        }
        var dialog = new SaveFileDialog
        {
            Filter = "GuGuGaGa 游戏档案 (*.ggprofile.json)|*.ggprofile.json",
            DefaultExt = ".ggprofile.json",
            FileName = GameProfiles.MakeId(profile.Name, []) + ".ggprofile.json",
        };
        if (dialog.ShowDialog(this) != true)
            return;
        try
        {
            var path = Path.GetFullPath(dialog.FileName);
            var configRoot = Path.GetFullPath(_session.Store.Directory) + Path.DirectorySeparatorChar;
            if (path.StartsWith(configRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("请把档案导出到配置目录之外。");
            GameProfileArchive.Write(path, profile);
            OnNotice("档案已导出（仅作品设定和术语，不含密钥）。");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            OnNotice("档案导出失败：" + error.Message);
        }
    }

    /// <summary>Open a new profile and start term-sheet generation.</summary>
    private void OnGenerateTermSheet(object sender, RoutedEventArgs e) => OpenProfileEditor(null, generate: true);

    private void OpenProfileEditor(GameProfile? profile, bool generate = false)
    {
        var wasRunning = _session.IsRunning;
        var window = new GameProfileWindow(_session, profile) { Owner = this };
        if (generate)
            window.GenerateOnLoad = true;
        var saved = window.ShowDialog();

        if (saved != true || window.Result is null)
        {
            if (generate)
                OnNotice("没有保存游戏档案。");
            return;
        }

        var result = window.Result;
        _session.ForgetContext();

        // New profiles become active immediately; edited profiles remain active only when already selected.
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

        // Prefer the most recently recognized text for the preview.
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
            new("通用翻译", "不使用术语表或术语校正"),
        };
        items.AddRange(profiles.Select(profile => new ListChooserWindow.Item(
            profile.Name,
            $"{profile.Terms.Count} 条术语、{profile.Terms.Sum(term => term.Forbidden.Count)} 条禁用译法"
                + (profile.WindowHints.Count == 0 ? string.Empty : $" · 窗口关键字 {string.Join("/", profile.WindowHints)}"))));

        var activeIndex = _session.ActiveProfile is { } active
            ? profiles.FindIndex(profile => profile.Id == active.Id) + 1
            : 0;

        var picked = ListChooserWindow.Choose(this, items, activeIndex, _overlay?.LanguageBarBounds() ?? default);
        if (picked < 0)
            return;

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
            + (string.IsNullOrWhiteSpace(active.Worldview) ? " · 没有写世界观(补上它往往比加术语更有效)" : " · 已带世界观描述") + (_session.Config.Translation.EnforceTerms ? " · 强制校正:开" : " · 强制校正:关(模型可以不理术语表)");
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

    /// <summary>清除保存的翻译凭据。</summary>
    private void OnClearApiKey(object sender, RoutedEventArgs e)
    {
        ApiKeyBox.Password = "";
        ClassicSecretBox.Password = "";
        _session.Config.Translation.Translator = _session.Config.Translation.Translator with
        {
            ApiKey = "",
            AppSecret = ""
        };
        _session.SaveConfig();
        OnNotice("已删除保存的密钥。停止并重新开始翻译后生效。");
    }

    private void OnGetApiKey(object sender, RoutedEventArgs e)
    {
        var provider = ValueOf(ProviderCombo, "mock");
        var classicUrl = provider switch
        {
            "caiyun" => "https://platform.caiyunapp.com/",
            "youdao" => "https://ai.youdao.com/",
            "baidu" => "https://fanyi-api.baidu.com/",
            _ => "",
        };
        if (classicUrl.Length > 0)
        {
            OpenExternal(classicUrl, "获取 API Key");
            return;
        }
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

    /// <summary>通过系统 Shell 打开网址或设置页面。</summary>
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
        if (_loadingUi)
            return;

        // Traditional APIs use fixed endpoints and do not use these fields.
        var provider = ValueOf(ProviderCombo, "mock");
        if (ClassicEngines.ContainsKey(provider))
        {
            ClassicIdBox.Clear();
            ClassicSecretBox.Clear();
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
        try
        {
            _session.ClearCache();
            OnNotice("已删除内存和磁盘翻译缓存。");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            OnNotice("缓存删除未完成：" + error.Message);
        }
    }

    private void OnExportDiagnostics(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "诊断 ZIP (*.zip)|*.zip",
            DefaultExt = ".zip",
            FileName = "GuGuGaGaTranslator-diagnostics-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".zip",
        };
        if (dialog.ShowDialog(this) != true)
            return;
        try
        {
            _session.ExportDiagnostics(dialog.FileName);
            OnNotice("诊断包已导出。你可以自行查看并附到问题反馈中。");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            OnNotice("诊断包导出失败：" + error.Message);
        }
    }

    private async void OnTestProvider(object sender, RoutedEventArgs e)
    {
        ReadUiIntoConfig();
        TestProviderButton.IsEnabled = false;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Clamp(_session.Config.Translation.Translator.TimeoutSeconds, 1, 120)));
        _providerTestCancellation = cancellation;
        ITranslator? translator = null;
        try
        {
            translator = TranslatorFactory.Create(_session.Config.Translation.Translator);
            var language = _session.Config.Translation.From;
            var text = GameProfiles.LanguageOf(language) switch
            {
                "ja" => "こんにちは。",
                "zh" => "你好。",
                _ => "Hello."
            };
            await translator.TranslateAsync(new()
            {
                Text = text,
                From = language,
                To = _session.Config.Translation.To
            }, cancellation.Token);
            if (IsLoaded)
                OnNotice(translator is MockTranslator ? "预览引擎正常；未调用翻译服务。" : "服务连接测试成功。");
        }
        catch (OperationCanceledException) { if (IsLoaded) OnNotice("连接测试已取消或超时。"); }
        catch (Exception error) { if (IsLoaded) OnNotice("连接测试失败：" + error.Message); }
        finally
        {
            if (translator is IDisposable disposable)
                disposable.Dispose();
            _providerTestCancellation = null;
            TestProviderButton.IsEnabled = true;
        }
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
        var pipeline = _session.Pipeline;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (pipeline is null || !ReferenceEquals(pipeline, _session.Pipeline) || !pipeline.IsCurrent(update))
                return;
            StatusText.Text = Describe(update);

            if (update.Ocr is not null)
                SourceBox.Text = update.SourceText;

            if (update.Status == PipelineStatus.NoText)
            {
                SourceBox.Text = "(未识别到文字)";
                return;
            }

            if (update.Translation is null)
            {
                if (update.Status == PipelineStatus.Translating)
                    _overlay?.ClearText();
                return;
            }

            TranslationBox.Text = update.Translation;

            var region = _session.ResolveRegion();
            if (region is { } rect)
                _overlay?.ShowTranslated(update.SourceText, update.Translation, rect);

            var stats = _session.Pipeline?.Stats;
            if (stats is not null)
            {
                StatsText.Text = $"帧 {stats.Frames} · 识别 {stats.Recognitions} · 翻译 {stats.Translations}"
                    + $" · 缓存 {stats.CacheHits} · 去重 {stats.RepeatedLines} · 错误 {stats.Errors}"
                    + $" · 用时 捕获 {update.CaptureDuration.TotalMilliseconds:F0}ms"
                    + $" / 识别 {update.OcrDuration.TotalMilliseconds:F0}ms"
                    + $" / 翻译 {update.TranslateDuration.TotalMilliseconds:F0}ms";
            }
        }));
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

    private void OnNotice(string message)
    {
        if (Dispatcher.HasShutdownStarted)
            return;
        void Apply()
        {
            StatusText.Text = message;
            UpdateCacheStatus();
        }
        if (Dispatcher.CheckAccess())
            Apply();
        else
            Dispatcher.BeginInvoke(Apply);
    }

    private void UpdateCacheStatus() => CacheStatusText.Text = _session.PersistentCacheAvailable
        ? "当前：磁盘缓存可用（Windows 当前账户加密）。"
        : _session.Config.Translation.Cache.Persist ? "当前：磁盘缓存不可用，已回退内存缓存。" : "当前：仅使用内存缓存。";

    private static string Text(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static double Number(string text, double fallback) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value) ? value : fallback;
}
