using System.Text.Json.Serialization;
using System.Windows;
using GuGuGaGaTranslator.Core.Translation;

namespace GuGuGaGaTranslator.Core.Config;

/// <summary>配置中的矩形，只保存位置和尺寸，避免序列化框架类型的派生属性。</summary>
public readonly record struct RegionRect(int X, int Y, int Width, int Height)
{
    /// <summary>True when the rectangle has a positive size. Computed, so it is never persisted.</summary>
    [JsonIgnore]
    public bool IsUsable => Width > 0 && Height > 0;

    public Int32Rect ToInt32Rect() => new(X, Y, Width, Height);

    public override string ToString() => $"{Width}×{Height} @ {X},{Y}";
}

/// <summary>Which window to read, and which part of it.</summary>
public sealed class TargetConfig
{
    /// <summary>窗口标识 process|class，用于窗口重启后的匹配。</summary>
    public string? Identity { get; set; }

    /// <summary>A title substring, used when no identity is recorded or it is gone.</summary>
    public string? TitleHint { get; set; }

    /// <summary>The translation region as an offset inside the target's client area, in physical pixels.</summary>
    public RegionRect? Region { get; set; }

    /// <summary>框选时的客户区尺寸，供后续窗口缩放使用。</summary>
    public int ReferenceClientWidth { get; set; }
    public int ReferenceClientHeight { get; set; }

    /// <summary>window 读取目标窗口合成画面；screen 读取可见屏幕；printwindow 为兼容后端。</summary>
    public string CaptureBackend { get; set; } = "window";
    public int CaptureSettingsVersion { get; set; }

    public bool ManualProfile { get; set; }
    public bool ManualRegion { get; set; }
    public int RegionSettingsVersion { get; set; }
    public bool ManualLanguage { get; set; }
}

/// <summary>目标的阅读设置，不包含服务密钥。按进程和窗口类保存，跨启动继续使用。</summary>
public sealed class SavedTargetConfig
{
    public string Identity { get; set; } = "";
    public RegionRect? Region { get; set; }
    public int ReferenceClientWidth { get; set; }
    public int ReferenceClientHeight { get; set; }
    public string From { get; set; } = "auto";
    public string To { get; set; } = "zh-Hans";
    public string OcrLanguage { get; set; } = "auto";
    public string ProfileId { get; set; } = "";
    public bool ManualProfile { get; set; }
    public bool ManualRegion { get; set; }
    public int RegionSettingsVersion { get; set; }
    public bool ManualLanguage { get; set; }
}

/// <summary>How images are prepared and read.</summary>
public sealed class OcrConfig
{
    /// <summary>Which backend: <c>windows</c> for the in-box recognizer (needs the language's OCR feature), <c>rapidocr</c> for the bundled model.</summary>
    public string Engine { get; set; } = "rapidocr";

    /// <summary>The recognizer language tag, which must match the game's text language.</summary>
    public string Language { get; set; } = "auto";

    /// <summary>Upscale factor applied before recognition; small game text reads far better enlarged.</summary>
    public double Scale { get; set; } = 2.0;

    /// <summary>Contrast-stretch amount in 0–1, for low-contrast text over busy backgrounds.</summary>
    public double Grayscale { get; set; }

    /// <summary>Languages to fall back to when the requested one has no engine installed.</summary>
    public string[] Fallbacks { get; set; } = ["en-US", "zh-Hans-CN"];

    /// <summary>Where the RapidOCR model files live; empty searches beside the executable.</summary>
    public string RapidModelDirectory { get; set; } = string.Empty;

    /// <summary>RapidOCR 检测尺寸；0 使用模型默认值，减小尺寸可降低处理量。</summary>
    public int RapidLimitSideLen { get; set; } = 320;

    /// <summary>Whether RapidOCR should try the CUDA provider before falling back to CPU.</summary>
    public bool RapidUseGpu { get; set; }
}

/// <summary>The language pair and the engine that serves it.</summary>
public sealed class TranslationConfig
{
    public List<PersonalTerm> PersonalTerms { get; set; } = [];
    public string From { get; set; } = "auto";

    public string To { get; set; } = "zh-Hans";

    public TranslatorConfig Translator { get; set; } = new();

    /// <summary>Fixed term translations, such as character and place names.</summary>
    public List<GlossaryEntry> Glossary { get; set; } = [];

    /// <summary>The game whose terms and voice are in force, by <see cref="GameProfile.Id"/>; empty means the plain translator.</summary>
    public string ActiveProfile { get; set; } = string.Empty;

    /// <summary>Pick the profile from the target window's title; choosing one by hand wins until the window changes.</summary>
    public bool AutoDetectProfile { get; set; } = true;

    /// <summary>启用译后术语校正。</summary>
    public bool EnforceTerms { get; set; } = true;

    // Fully qualified: this property's own name shadows the type in this scope.
    public List<GameProfile> GameProfiles { get; set; } = GuGuGaGaTranslator.Core.Translation.GameProfiles.Default();

    /// <summary>How many previous lines travel with each request as context; 0 disables context.</summary>
    public int HistoryLines { get; set; } = 3;

    public TranslationCacheConfig Cache { get; set; } = new();
}

/// <summary>Disk caching is optional; values are encrypted for the current Windows account.</summary>
public sealed class TranslationCacheConfig
{
    public bool Enabled { get; set; } = true;
    public bool Persist { get; set; }
    public bool MatchContext { get; set; } = true;
    public int MemoryEntries { get; set; } = 2000;
    public int RetentionDays { get; set; } = 30;
    public int MaximumEntries { get; set; } = 10000;
}

/// <summary>留空时各类文件跟随数据目录，填写时使用用户指定的绝对目录。</summary>
public sealed class StorageLocations
{
    public string CacheDirectory { get; set; } = "";
    public string LogDirectory { get; set; } = "";
    public string ExportDirectory { get; set; } = "";
    public string UpdateDirectory { get; set; } = "";
}

/// <summary>悬浮层相对识别区域的位置。</summary>
public sealed class OverlayPlacement
{
    public const string Over = "over";

    public const string Below = "below";

    public const string Above = "above";
}

/// <summary>How the translation overlay looks and behaves.</summary>
public sealed class OverlayConfig
{
    /// <summary><c>over</c> covers the original dialogue box, <c>below</c> or <c>above</c> keeps the original visible next to it.</summary>
    public string Placement { get; set; } = OverlayPlacement.Below;

    public bool ShowPanel { get; set; } = true;

    /// <summary>Text size in device-independent pixels.</summary>
    public double FontSize { get; set; } = 24;

    /// <summary>Panel opacity behind the text, in 0–1.</summary>
    public double BackgroundOpacity { get; set; } = 0.82;

    public bool TextOutline { get; set; } = true;

    public int MaxLines { get; set; } = 0;

    /// <summary>Panel width in pixels; 0 follows the captured region's width.</summary>
    public int Width { get; set; }

    /// <summary>Panel height in pixels; 0 grows with the text.</summary>
    public int Height { get; set; }

    public double CornerRadius { get; set; } = 8;

    public double Padding { get; set; } = 14;

    /// <summary>Font family for the translation; empty uses the system default.</summary>
    public string FontFamily { get; set; } = string.Empty;

    /// <summary>Text alignment inside the panel: <c>left</c>, <c>center</c>, or <c>right</c>.</summary>
    public string TextAlign { get; set; } = "left";

    public int OffsetX { get; set; }

    public int OffsetY { get; set; } = 8;

    /// <summary>Let mouse input pass through to the game underneath.</summary>
    public bool ClickThrough { get; set; } = true;

    public bool ShowSource { get; set; }

    /// <summary>在悬浮层上方显示独立的语言控制条。</summary>
    public bool ShowLanguageBar { get; set; } = true;

    /// <summary>请求系统排除悬浮层捕获；录制译文时关闭此选项。</summary>
    public bool ExcludeFromCapture { get; set; } = true;

    public List<LanguagePreset> LanguagePresets { get; set; } = DefaultLanguagePresets.Create();

    /// <summary>The named looks offered by the UI; applying one copies its values into the fields above.</summary>
    public List<OverlayPreset> Presets { get; set; } = DefaultOverlayPresets.Create();
}

/// <summary>A named look, applied by copying its values into the flat overlay settings.</summary>
public sealed class OverlayPreset
{
    public string Name { get; set; } = string.Empty;

    public string Note { get; set; } = string.Empty;

    public string Placement { get; set; } = OverlayPlacement.Below;

    public bool ShowSource { get; set; }

    public bool ShowPanel { get; set; } = true;

    public double BackgroundOpacity { get; set; } = 0.82;

    public double FontSize { get; set; } = 24;

    public int Width { get; set; }

    public int Height { get; set; }

    public double CornerRadius { get; set; } = 8;

    public double Padding { get; set; } = 14;

    public string TextAlign { get; set; } = "left";

    public int OffsetY { get; set; }
}

/// <summary>The looks a fresh install offers.</summary>
public static class DefaultOverlayPresets
{
    public static List<OverlayPreset> Create() =>
    [
        new()
        {
            Name = "遮盖原文(推荐)",
            Note = "翻译框直接盖住原对话框,原文隐藏 —— galgame 最常见用法",
            Placement = OverlayPlacement.Over, ShowSource = false, ShowPanel = true,
            BackgroundOpacity = 0.92, FontSize = 26, CornerRadius = 8, Padding = 14,
            TextAlign = "left", OffsetY = 0,
        },
        new()
        {
            Name = "下方字幕",
            Note = "原文保留,译文贴在对话框下沿 —— 想对照着看时用",
            Placement = OverlayPlacement.Below, ShowSource = false, ShowPanel = true,
            BackgroundOpacity = 0.82, FontSize = 24, CornerRadius = 8, Padding = 14,
            TextAlign = "left", OffsetY = 8,
        },
        new()
        {
            Name = "极简描边",
            Note = "没有底框,只有带黑边的文字 —— 画面最干净,适合亮色背景",
            Placement = OverlayPlacement.Below, ShowSource = false, ShowPanel = false,
            BackgroundOpacity = 0, FontSize = 28, CornerRadius = 0, Padding = 8,
            TextAlign = "left", OffsetY = 6,
        },
        new()
        {
            Name = "原文+译文对照",
            Note = "上面一行原文、下面一行译文,放在对话框下方 —— 学日语或核对时用",
            Placement = OverlayPlacement.Below, ShowSource = true, ShowPanel = true,
            BackgroundOpacity = 0.86, FontSize = 24, CornerRadius = 8, Padding = 16,
            TextAlign = "left", OffsetY = 8,
        },
    ];
}

/// <summary>One entry of the overlay's language switcher: a direction a person can pick with one click while the game keeps focus.</summary>
public sealed class LanguagePreset
{
    public string Label { get; set; } = string.Empty;

    public string From { get; set; } = "auto";

    public string To { get; set; } = "zh-Hans";

    /// <summary>Recognition language for this direction, or <c>auto</c>; picking a direction also chooses the script to expect.</summary>
    public string Ocr { get; set; } = "auto";

    public override string ToString() => Label;
}

/// <summary>中、日、英、韩的十二种互译方向，以及自动译中。</summary>
public static class DefaultLanguagePresets
{
    public static List<LanguagePreset> Create() =>
    [
        new() { Label = "日 → 中", From = "ja", To = "zh-Hans", Ocr = "ja" },
        new() { Label = "英 → 中", From = "en", To = "zh-Hans", Ocr = "en-US" },
        new() { Label = "自动识别 → 中文", From = "auto", To = "zh-Hans", Ocr = "auto" },
        new() { Label = "中 → 日", From = "zh-Hans", To = "ja", Ocr = "zh-Hans-CN" },
        new() { Label = "中 → 英", From = "zh-Hans", To = "en", Ocr = "zh-Hans-CN" },
        new() { Label = "日 → 英", From = "ja", To = "en", Ocr = "ja" },
        new() { Label = "英 → 日", From = "en", To = "ja", Ocr = "en-US" },
        new() { Label = "韩 → 中", From = "ko", To = "zh-Hans", Ocr = "ko-KR" },
        new() { Label = "中 → 韩", From = "zh-Hans", To = "ko", Ocr = "zh-Hans-CN" },
        new() { Label = "韩 → 日", From = "ko", To = "ja", Ocr = "ko-KR" },
        new() { Label = "日 → 韩", From = "ja", To = "ko", Ocr = "ja" },
        new() { Label = "韩 → 英", From = "ko", To = "en", Ocr = "ko-KR" },
        new() { Label = "英 → 韩", From = "en", To = "ko", Ocr = "en-US" },
    ];

    /// <summary>支持的翻译语言。</summary>
    public static readonly string[] SupportedLanguages = ["zh-Hans", "ja", "en", "ko"];

    /// <summary>支持四种翻译语言；auto 仅作为原文语言。</summary>
    public static bool IsSupported(string? language, bool allowAuto) =>
        !string.IsNullOrWhiteSpace(language)
        && ((allowAuto && language.Equals("auto", StringComparison.OrdinalIgnoreCase))
            || SupportedLanguages.Contains(language, StringComparer.OrdinalIgnoreCase));

    /// <summary>Recognition language tags carry a region (<c>en-US</c>, <c>zh-Hans-CN</c>), so they are
    /// matched by prefix rather than against the translation list.</summary>
    public static bool IsSupportedOcr(string? language) =>
        !string.IsNullOrWhiteSpace(language)
        && new[] { "auto", "ja", "ja-JP", "en", "en-US", "en-GB", "ko", "ko-KR", "zh", "zh-CN", "zh-Hans", "zh-Hans-CN", "zh-Hans-SG" }
            .Contains(language, StringComparer.OrdinalIgnoreCase);
}

/// <summary>How often to look, and when a look is worth recognizing.</summary>
public sealed class PipelineConfig
{
    public int TextSettleMs { get; set; } = 450;

    public int PollIntervalMs { get; set; } = 400;

    /// <summary>How many of the 256 signature blocks must change before a frame is recognized.</summary>
    public int ChangeThresholdBits { get; set; } = 6;

    public double RepeatSimilarity { get; set; } = 0.92;

    public int ForceRefreshMs { get; set; } = 5000;

    public bool TranslateOnlyOnChange { get; set; } = true;

    /// <summary>Recognized text shorter than this is treated as noise and not translated. Default 4 rather than 2:
    /// a two-character fragment is usually a UI label or an OCR artifact, not a line of dialogue.</summary>
    public int MinTextLength { get; set; } = 4;

    /// <summary>Ignore recognition results that do not contain the expected script, so the game's own SAVE / LOAD / CONFIG labels are not translated as dialogue.</summary>
    public bool ScriptGuard { get; set; } = true;

    /// <summary>Pause after a capture or recognition error, so a failure cannot spin.</summary>
    public int ErrorBackoffMs { get; set; } = 1500;

    /// <summary>Blank this tool's own windows out of every frame before recognition. Off only makes sense
    /// when the control window and the translation panel are guaranteed to sit outside the region.</summary>
    public bool MaskOwnWindows { get; set; } = true;
}

/// <summary>How the region picker behaves while it is open.</summary>
public sealed class RegionPickerConfig
{
    /// <summary>Hide this tool's own windows while framing, the way a screenshot tool hides itself: what
    /// you frame is then exactly what will be read, with no panel or control bar in the way.</summary>
    public bool HideOwnWindows { get; set; } = true;
}

/// <summary>The global hotkeys, as text such as <c>Ctrl+Alt+T</c>; empty disables one.</summary>
public sealed class HotkeyConfig
{
    public string StartStop { get; set; } = "Ctrl+Alt+T";

    public string Pause { get; set; } = "Ctrl+Alt+P";

    /// <summary>Re-frame the region; translation continues with the new region.</summary>
    public string Region { get; set; } = "Ctrl+Alt+R";

    /// <summary>Frame a region and start translating in one press: the whole tool from one key.</summary>
    public string RegionAndStart { get; set; } = "Ctrl+Alt+S";

    public string ShowSource { get; set; } = "Ctrl+Alt+O";

    public string TogglePanel { get; set; } = "Ctrl+Alt+H";

    public string ToggleEdit { get; set; } = "Ctrl+Alt+U";

    public static HotkeyConfig Default() => new();
}

/// <summary>Evidence capture, so a run can be checked without watching the screen.</summary>
public sealed class DebugConfig
{
    public bool DumpFrames { get; set; }

    public string DumpDirectory { get; set; } = string.Empty;

    public int KeepDumps { get; set; } = 200;
}

/// <summary>The whole persisted configuration, one JSON file.</summary>
public sealed class AppConfig
{
    public UpdateConfig Updates { get; set; } = new();
    public FloatingBallConfig FloatingBall { get; set; } = new();
    public List<SavedTargetConfig> SavedTargets { get; set; } = [];

    /// <summary>Whether the first-run setup card has been shown, so a person who skipped it is not asked again on every launch.</summary>
    public bool SetupCompleted { get; set; }

    public TargetConfig Target { get; set; } = new();

    public OcrConfig Ocr { get; set; } = new();

    public TranslationConfig Translation { get; set; } = new();

    public OverlayConfig Overlay { get; set; } = new();

    public RegionPickerConfig RegionPicker { get; set; } = new();

    public HotkeyConfig Hotkeys { get; set; } = new();

    public PipelineConfig Pipeline { get; set; } = new();

    public DebugConfig Debug { get; set; } = new();
    public StorageLocations Storage { get; set; } = new();

    /// <summary>Drop language choices this version no longer offers, so a configuration carried over from an
    /// older build cannot point at a language the interface can no longer select. Supports 中 / 日 / 英 / 韩.</summary>
    private static string ValidDirectoryOrEmpty(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        try { return DataDirectory.Validate(value); }
        catch (Exception error) when (error is ArgumentException or System.IO.IOException or NotSupportedException) { return ""; }
    }

    public void NormalizeLanguages()
    {
        Target ??= new();
        FloatingBall ??= new();
        Updates ??= new();
        Ocr ??= new();
        Translation ??= new();
        Overlay ??= new();
        RegionPicker ??= new();
        Hotkeys ??= new();
        Pipeline ??= new();
        Debug ??= new();
        Storage ??= new();
        Storage.CacheDirectory = ValidDirectoryOrEmpty(Storage.CacheDirectory);
        Storage.LogDirectory = ValidDirectoryOrEmpty(Storage.LogDirectory);
        Storage.ExportDirectory = ValidDirectoryOrEmpty(Storage.ExportDirectory);
        Storage.UpdateDirectory = ValidDirectoryOrEmpty(Storage.UpdateDirectory);
        SavedTargets ??= [];
        SavedTargets.RemoveAll(target => target is null || string.IsNullOrWhiteSpace(target.Identity));
        SavedTargets = SavedTargets.TakeLast(24).ToList();
        if (Target.RegionSettingsVersion < 1)
        {
            Target.ManualRegion = Target.Region is not null;
            Target.RegionSettingsVersion = 1;
        }
        foreach (var saved in SavedTargets.Where(saved => saved.RegionSettingsVersion < 1))
        {
            saved.ManualRegion = saved.Region is not null;
            saved.RegionSettingsVersion = 1;
        }
        Pipeline.TextSettleMs = Math.Clamp(Pipeline.TextSettleMs, 0, 3000);
        Translation.HistoryLines = Math.Clamp(Translation.HistoryLines, 0, 8);
        // 旧版默认屏幕捕获会读到遮挡物，首次升级改用窗口捕获；之后保留手动选择。
        if (Target.CaptureSettingsVersion < 1)
        {
            if (Target.CaptureBackend is "screen" or null)
                Target.CaptureBackend = "window";
            Target.CaptureSettingsVersion = 1;
        }
        if (Target.CaptureBackend is not ("window" or "screen" or "printwindow"))
            Target.CaptureBackend = "window";
        Translation.Translator ??= new();
        Translation.Cache ??= new();
        Translation.GameProfiles ??= [];
        Translation.Glossary ??= [];
        Translation.PersonalTerms ??= [];
        Translation.PersonalTerms.RemoveAll(term => term is null || string.IsNullOrWhiteSpace(term.Source) || string.IsNullOrWhiteSpace(term.Target));
        Translation.GameProfiles.RemoveAll(profile => profile is null);
        Ocr.Fallbacks ??= [];
        Overlay.LanguagePresets ??= [];
        Overlay.Presets ??= [];
        Overlay.Presets.RemoveAll(preset => preset is null);
        Ocr.Engine ??= "rapidocr";
        Ocr.RapidModelDirectory ??= "";
        Overlay.FontFamily ??= "Microsoft YaHei UI";
        Overlay.TextAlign ??= "left";
        Debug.DumpDirectory = ValidDirectoryOrEmpty(Debug.DumpDirectory);
        Translation.Translator = Translation.Translator with
        {
            Provider = Translation.Translator.Provider ?? "mock",
            BaseUrl = Translation.Translator.BaseUrl ?? "",
            Model = Translation.Translator.Model ?? "",
            ApiKey = Translation.Translator.ApiKey ?? "",
            AppId = Translation.Translator.AppId ?? "",
            AppSecret = Translation.Translator.AppSecret ?? "",
            PromptStyle = Translation.Translator.PromptStyle ?? "galgame",
        };
        if (!DefaultLanguagePresets.IsSupported(Translation.From, allowAuto: true))
            Translation.From = "auto";
        if (!DefaultLanguagePresets.IsSupported(Translation.To, allowAuto: false))
            Translation.To = "zh-Hans";
        if (!DefaultLanguagePresets.IsSupportedOcr(Ocr.Language))
            Ocr.Language = "auto";
        Ocr.Fallbacks = [.. Ocr.Fallbacks.Where(DefaultLanguagePresets.IsSupportedOcr)];

        Translation.Cache.RetentionDays = Math.Clamp(Translation.Cache.RetentionDays, 1, 365);
        Translation.Cache.MaximumEntries = Math.Clamp(Translation.Cache.MaximumEntries, 100, 100000);
        Translation.Cache.MemoryEntries = Math.Clamp(Translation.Cache.MemoryEntries, 100, 20000);
        foreach (var profile in Translation.GameProfiles)
        {
            profile.Id ??= "";
            profile.Name ??= "";
            profile.WindowHints ??= [];
            profile.ProcessHints ??= [];
            profile.ProcessHints.RemoveAll(hint => string.IsNullOrWhiteSpace(hint));
            if (profile.Id == "limbus-company" && profile.ProcessHints.Count == 0)
                profile.ProcessHints.Add("LimbusCompany");
            profile.WindowHints.RemoveAll(hint => string.IsNullOrWhiteSpace(hint));
            profile.Terms ??= [];
            profile.Terms.RemoveAll(term => term is null);
            foreach (var term in profile.Terms)
            {
                term.Source ??= "";
                term.Target ??= "";
                term.Forbidden ??= [];
                if (string.IsNullOrWhiteSpace(term.ReviewStatus) && term.Note?.Contains("待核对", StringComparison.Ordinal) == true)
                {
                    term.ReviewStatus = "待核对";
                    term.Enabled = false;
                }
            }
            if (!string.IsNullOrEmpty(profile.From) && !DefaultLanguagePresets.IsSupported(profile.From, true))
                profile.From = null;
            if (!string.IsNullOrEmpty(profile.To) && !DefaultLanguagePresets.IsSupported(profile.To, false))
                profile.To = null;
            if (!string.IsNullOrEmpty(profile.OcrLanguage) && !DefaultLanguagePresets.IsSupportedOcr(profile.OcrLanguage))
                profile.OcrLanguage = null;
        }

        var kept = Overlay.LanguagePresets
            .Where(preset => preset is not null && DefaultLanguagePresets.IsSupported(preset.From, allowAuto: true)
                && DefaultLanguagePresets.IsSupported(preset.To, allowAuto: false))
            .ToList();

        // 六个方向一个都不剩(例如旧配置只有韩语方向)就换回默认那套。
        Overlay.LanguagePresets = kept.Count > 0 ? kept : DefaultLanguagePresets.Create();
        foreach (var preset in DefaultLanguagePresets.Create().Where(preset => preset.From == "ko" || preset.To == "ko"))
            if (!Overlay.LanguagePresets.Any(existing => existing.From == preset.From && existing.To == preset.To))
                Overlay.LanguagePresets.Add(preset);
    }
}

/// <summary>用户添加的方向专属术语，优先于公共词库。</summary>
public sealed class PersonalTerm
{
    public string From { get; set; } = "en";
    public string To { get; set; } = "zh-Hans";
    public string Source { get; set; } = "";
    public string Target { get; set; } = "";
    public List<string> Forbidden { get; set; } = [];
}

public sealed class FloatingBallConfig
{
    public bool Enabled { get; set; } = true;
    public int? X { get; set; }
    public int? Y { get; set; }
}

public sealed class UpdateConfig
{
    public bool CheckOnStartup { get; set; } = true;
    public bool AutoApplyTerms { get; set; }
    public DateTimeOffset? LastCheck { get; set; }
    public GameProfile? TermBaseline { get; set; }
}
