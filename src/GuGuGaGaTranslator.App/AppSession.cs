using System.IO;
using System.Windows;
using GuGuGaGaTranslator.Core.Imaging;
using GuGuGaGaTranslator.Core.Capture;
using GuGuGaGaTranslator.Core.Config;
using GuGuGaGaTranslator.Core.Diagnostics;
using GuGuGaGaTranslator.Core.Ocr;
using GuGuGaGaTranslator.Core.Pipeline;
using GuGuGaGaTranslator.Core.Translation;
using GuGuGaGaTranslator.Ocr.Rapid;
using GuGuGaGaTranslator.Storage.Sqlite;

namespace GuGuGaGaTranslator.App;

/// <summary>Everything one run owns: the configuration, the translator, the
/// recognizer, the dumper, and the translation loop; the windows are views over it.</summary>
public sealed partial class AppSession : IAsyncDisposable
{
    private ITranslator? _translator;
    private bool _stopping;
    public bool IsStopping => _stopping;
    private Task? _stopTask;
    private FrameDumper? _dumper;
    private WindowCapture? _windowCapture;
    private LanguagePair _languages = new("auto", "zh-Hans");
    private readonly List<string> _recentSources = [];
    private readonly SemaphoreSlim _settingsGate = new(1, 1);

    private async Task ChangeSettingsAsync(Func<Task> action)
    {
        await _settingsGate.WaitAsync().ConfigureAwait(true);
        try { await action().ConfigureAwait(true); }
        finally { _settingsGate.Release(); }
    }

    public ConfigStore Store { get; private set; }
    public bool UsesDefaultDataDirectory { get; }

    public AppSession(string? configDirectory = null)
    {
        Store = new ConfigStore(configDirectory);
        UsesDefaultDataDirectory = configDirectory is null;
        Diagnostics = new DiagnosticsService(Store.Directory);
        Cache.StorageFailed += _ =>
        {
            PersistentCacheAvailable = false;
            Diagnostics.Record(new DiagnosticEvent { Kind = DiagnosticEventKind.CacheUnavailable });
            Notice?.Invoke("磁盘缓存暂不可用，已继续使用内存缓存。");
        };
    }

    public AppConfig Config { get; private set; } = new();

    /// <summary>The shared translation cache, kept across start/stop cycles.</summary>
    public TranslationCache Cache { get; } = new();
    public DiagnosticsService Diagnostics { get; }
    public bool PersistentCacheAvailable
    {
        get; private set;
    }
    private (bool Persist, int Days, int Maximum, string Path)? _cacheSettings;
    public string CacheDatabasePath => Path.Combine(DataDirectory.Location(Config.Storage.CacheDirectory, Store.Directory, "cache"), "translations.sqlite");
    public string DumpDirectory => DataDirectory.Location(Config.Debug.DumpDirectory, Store.Directory, "dumps");
    public string ExportDirectory => DataDirectory.Location(Config.Storage.ExportDirectory, Store.Directory, "exports");
    public string UpdateDirectory => DataDirectory.Location(Config.Storage.UpdateDirectory, Store.Directory, "updates");

    /// <summary>The running loop, or null when stopped.</summary>
    public TranslationPipeline? Pipeline
    {
        get; private set;
    }

    public ITextRecognizer? Recognizer
    {
        get; private set;
    }

    /// <summary>Raised for every loop iteration; subscribers marshal to their own thread.</summary>
    public event Action<PipelineUpdate>? Updated;

    public event Action<string>? Notice;

    public bool IsRunning => Pipeline?.IsRunning == true;

    /// <summary>Load the configuration from disk, keeping the last error for the UI.</summary>
    public void LoadConfig()
    {
        Config = Store.Load();
        MigrateLegacyCache();
        Diagnostics.ConfigureDirectory(Store.Directory, Config.Storage.LogDirectory);
        LoadBundledTermLibrary();
        // 保留支持的四种语言，修复旧配置中无法识别的语言标签。
        Config.NormalizeLanguages();
        _languages = new LanguagePair(Config.Translation.From, Config.Translation.To);
        ConfigureCache();
        Diagnostics.Record(new DiagnosticEvent { Kind = DiagnosticEventKind.Started });
        if (Store.LastLoadError is { } error)
        {
            Notice?.Invoke($"配置文件无法读取,已回退默认值:{error}");
        }
    }

    public void SaveConfig()
    {
        RememberTargetSettings();
        Store.Save(Config);
        Diagnostics.ConfigureDirectory(Store.Directory, Config.Storage.LogDirectory);
        ConfigureCache();
    }

    private void ConfigureCache(bool force = false)
    {
        var options = Config.Translation.Cache;
        Cache.Configure(options.Enabled, options.MemoryEntries, options.RetentionDays, options.MatchContext);
        var settings = (options.Persist && options.Enabled, Math.Clamp(options.RetentionDays, 1, 365), Math.Clamp(options.MaximumEntries, 100, 100000), CacheDatabasePath);
        if (!force && _cacheSettings == settings)
            return;
        var previousPath = _cacheSettings?.Path;
        _cacheSettings = settings;
        PersistentCacheAvailable = false;
        Cache.ConfigureStorage(null);
        if (!settings.Item1)
            return;
        try
        {
            if (previousPath is not null && !previousPath.Equals(CacheDatabasePath, StringComparison.OrdinalIgnoreCase)
                && File.Exists(previousPath) && !File.Exists(CacheDatabasePath)
                && !File.Exists(previousPath + "-journal") && !File.Exists(previousPath + "-wal"))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(CacheDatabasePath)!);
                File.Move(previousPath, CacheDatabasePath, overwrite: false);
            }
            Cache.ConfigureStorage(new SqliteTranslationCacheStore(CacheDatabasePath, settings.Item2, settings.Item3));
            PersistentCacheAvailable = true;
        }
        catch (Exception)
        {
            Diagnostics.Record(new DiagnosticEvent { Kind = DiagnosticEventKind.CacheUnavailable });
            Notice?.Invoke("磁盘缓存无法打开，继续使用内存缓存。");
        }
    }

    public void ClearCache()
    {
        Pipeline?.InvalidateTranslation();
        Cache.ConfigureStorage(null);
        PersistentCacheAvailable = false;
        _cacheSettings = null;
        // Only this application's exact cache files are removed, including an abandoned SQLite journal.
        try
        {
            File.Delete(CacheDatabasePath);
            File.Delete(CacheDatabasePath + "-journal");
            File.Delete(CacheDatabasePath + "-wal");
            File.Delete(CacheDatabasePath + "-shm");
        }
        finally { ConfigureCache(force: true); }
        Diagnostics.Record(new DiagnosticEvent { Kind = DiagnosticEventKind.CacheCleared });
        Pipeline?.InvalidateTranslation();
    }

    public void ExportDiagnostics(string path) => Diagnostics.Export(path, Config, Pipeline?.Stats);

    /// <summary>The default directory for evidence dumps, under the user's application data.</summary>
    public static string DefaultDumpDirectory() => Path.Combine(ConfigStore.DefaultDirectory(), "dumps");

    /// <summary>List the windows a person can pick, excluding this process's own.</summary>
    public IReadOnlyList<WindowInfo> ListWindows() =>
        WindowEnumerator.List((uint)Environment.ProcessId);

    /// <summary>Find the configured target window, if it is open.</summary>
    public WindowInfo? FindTarget()
    {
        var identity = Config.Target.Identity;
        if (!string.IsNullOrEmpty(identity))
        {
            var byIdentity = WindowEnumerator.FindByIdentity(identity, (uint)Environment.ProcessId);
            if (byIdentity is not null)
                return byIdentity;
        }

        var hint = Config.Target.TitleHint;
        if (string.IsNullOrWhiteSpace(hint))
            return null;
        return ListWindows().FirstOrDefault(window =>
            window.Title.Contains(hint, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Resolve the region to capture right now, in physical screen pixels:
    /// the stored region is an offset inside the client area, so this is what makes it
    /// follow a window that moves.</summary>
    public Int32Rect? ResolveRegion()
    {
        if (Config.Target.Region is not { } local || !local.IsUsable)
            return null;

        var window = FindTarget();
        if (window is null || window.IsMinimized || !window.HasClientArea)
            return null;

        var client = window.ClientRect;
        return TargetRegionResolver.Resolve(local, client, Config.Target.ReferenceClientWidth, Config.Target.ReferenceClientHeight);
    }

    /// <summary>Adopt a window as the target without touching the region it already has; used by「一键框选并翻译」.</summary>
    public void SetTarget(WindowInfo window)
    {
        if (!string.Equals(Config.Target.Identity, window.Identity, StringComparison.OrdinalIgnoreCase))
        {
            RememberTargetSettings();
            var saved = Config.SavedTargets.LastOrDefault(item => item.Identity.Equals(window.Identity, StringComparison.OrdinalIgnoreCase));
            Config.Target.Region = saved?.Region;
            Config.Target.ReferenceClientWidth = saved?.ReferenceClientWidth ?? 0;
            Config.Target.ReferenceClientHeight = saved?.ReferenceClientHeight ?? 0;
            Config.Target.ManualProfile = saved?.ManualProfile ?? false;
            Config.Target.ManualRegion = saved?.ManualRegion ?? false;
            Config.Target.RegionSettingsVersion = 1;
            Config.Target.ManualLanguage = saved?.ManualLanguage ?? false;
            Config.Translation.From = saved?.From ?? "auto";
            Config.Translation.To = saved?.To ?? "zh-Hans";
            Config.Translation.ActiveProfile = saved?.ProfileId ?? "";
            Config.Ocr.Language = saved?.OcrLanguage ?? "auto";
            Config.NormalizeLanguages();
            _languages = new LanguagePair(Config.Translation.From, Config.Translation.To);
            ForgetContext();
        }
        Config.Target.Identity = window.Identity;
        Config.Target.TitleHint = window.Title;
    }

    private void RememberTargetSettings()
    {
        if (string.IsNullOrWhiteSpace(Config.Target.Identity))
            return;
        Config.SavedTargets.RemoveAll(item => item.Identity.Equals(Config.Target.Identity, StringComparison.OrdinalIgnoreCase));
        Config.SavedTargets.Add(new SavedTargetConfig
        {
            Identity = Config.Target.Identity,
            Region = Config.Target.Region,
            ReferenceClientWidth = Config.Target.ReferenceClientWidth,
            ReferenceClientHeight = Config.Target.ReferenceClientHeight,
            From = Config.Translation.From, To = Config.Translation.To, OcrLanguage = Config.Ocr.Language,
            ProfileId = Config.Translation.ActiveProfile,
            ManualProfile = Config.Target.ManualProfile, ManualLanguage = Config.Target.ManualLanguage,
            ManualRegion = Config.Target.ManualRegion, RegionSettingsVersion = 1
        });
        while (Config.SavedTargets.Count > 24)
            Config.SavedTargets.RemoveAt(0);
    }

    public Task SelectTargetAsync(WindowInfo window) => ChangeSettingsAsync(() => SelectTargetCoreAsync(window));

    private async Task SelectTargetCoreAsync(WindowInfo window)
    {
        if (!string.Equals(Config.Target.Identity, window.Identity, StringComparison.OrdinalIgnoreCase))
            await StopAsync().ConfigureAwait(true);
        SetTarget(window);
        await AutoDetectProfileCoreAsync(window.Title, window.ProcessName).ConfigureAwait(true);
        SaveConfig();
        LanguagesChanged?.Invoke(_languages);
        ProfileChanged?.Invoke(ActiveProfile);
    }

    /// <summary>Record a screen-space selection as a client-relative region for the given target window.</summary>
    public void SetRegion(WindowInfo window, Int32Rect screenRegion, bool manual = true)
    {
        var client = window.ClientRect;
        SetTarget(window);
        Pipeline?.InvalidateTranslation();
        Config.Target.Region = new RegionRect(
            screenRegion.X - client.X,
            screenRegion.Y - client.Y,
            screenRegion.Width,
            screenRegion.Height);
        Config.Target.ReferenceClientWidth = client.Width;
        Config.Target.ReferenceClientHeight = client.Height;
        Config.Target.ManualRegion = manual;
        Config.Target.RegionSettingsVersion = 1;
    }

    public void ClearRegion()
    {
        ForgetContext();
        Config.Target.Region = null;
        Config.Target.ManualRegion = false;
        Config.Target.ReferenceClientWidth = Config.Target.ReferenceClientHeight = 0;
    }

    /// <summary>Start the translation loop, rebuilding the recognizer, translator, and dumper from the current configuration.</summary>
    public void Start()
    {
        if (IsRunning || _stopping)
            return;
        if (Config.Translation.Translator.Provider == "caiyun"
            && !CaiyunTranslator.SupportsDirection(Config.Translation.From, Config.Translation.To))
        {
            Notice?.Invoke(TranslationErrors.Describe(new UnsupportedTranslationDirectionException()));
            return;
        }
        if (Recognizer is IDisposable previous)
            previous.Dispose();
        Recognizer = null;
        if (_translator is IDisposable previousTranslator)
            previousTranslator.Dispose();
        _translator = null;

        if (Config.Target.Region is null)
        {
            Notice?.Invoke("还没有框选区域:先选一个窗口,再点「框选区域」。");
            return;
        }

        var recognizer = CreateRecognizer();
        if (recognizer is null)
        {
            var available = string.Join(", ", WindowsOcrRecognizer.AvailableLanguages);
            Notice?.Invoke(
                $"系统里没有 {Config.Ocr.Language} 的识别语言包。当前可用:{available}。"
                + "请在「设置 → 时间和语言 → 语言和区域」里为该语言安装「光学字符识别」功能后重试。"
                + "也可以把「识别语言」设成 auto(自动),它会用已安装的语言逐个尝试。");
            return;
        }

        _languages = new LanguagePair(Config.Translation.From, Config.Translation.To);
        Recognizer = recognizer;
        _translator = TranslatorFactory.Create(Config.Translation.Translator);
        _dumper = Config.Debug.DumpFrames
            ? new FrameDumper(
                DumpDirectory,
                Config.Debug.KeepDumps)
            : null;

        var settings = new PipelineSettings
        {
            RegionProvider = ResolveRegion,
            TargetHandle = () => FindTarget()?.Handle ?? 0,
            IsScreenCapture = Config.Target.CaptureBackend.Equals("screen", StringComparison.OrdinalIgnoreCase),
            CaptureAsync = CaptureFrameAsync,
            Recognizer = recognizer,
            // 每次翻译都重新读,悬浮层的语言切换条才能不重启循环就换方向。
            Languages = () => _languages,
            Translator = _translator,
            // 同理,选游戏档案或改术语表都对下一句生效,而不是下一次运行才生效。
            Profile = () => CurrentProfile,
            Cache = Cache,
            Dumper = _dumper,
            Options = new PipelineOptions
            {
                TextSettleMs = Config.Pipeline.TextSettleMs,
                PollIntervalMs = Config.Pipeline.PollIntervalMs,
                ChangeThresholdBits = Config.Pipeline.ChangeThresholdBits,
                RepeatSimilarity = Config.Pipeline.RepeatSimilarity,
                ForceRefreshMs = Config.Pipeline.ForceRefreshMs,
                TranslateOnlyOnChange = Config.Pipeline.TranslateOnlyOnChange,
                MinTextLength = Config.Pipeline.MinTextLength,
                ScriptGuard = Config.Pipeline.ScriptGuard,
                EnforceTerms = Config.Translation.EnforceTerms,
                MaskOwnWindows = Config.Pipeline.MaskOwnWindows,
                // RapidOCR 内部自己会缩放,提前放大只是多算一遍随后被丢掉的像素;
                // 系统识别器则相反,小字放大后好读得多。
                OcrScale = Config.Ocr.Engine.Equals("rapidocr", StringComparison.OrdinalIgnoreCase)
                    ? 1.0
                    : Config.Ocr.Scale,
                OcrGrayscale = Config.Ocr.Grayscale,
                HistoryLines = Config.Translation.HistoryLines,
                ErrorBackoffMs = Config.Pipeline.ErrorBackoffMs,
            },
        };

        Pipeline = new TranslationPipeline(settings);
        Pipeline.Updated += update =>
        {
            Diagnostics.Record(update);
            if (update.Status is PipelineStatus.Translated or PipelineStatus.Reused)
                Remember(update.SourceText);
            else if (update.Status is PipelineStatus.NoText or PipelineStatus.Blank or PipelineStatus.NoRegion)
                lock (_recentSources) _recentSources.Clear();
            Updated?.Invoke(update);
        };
        Pipeline.Start();

        Notice?.Invoke($"已开始:识别 {recognizer.Id} · 翻译 {_translator.Id}"
            + (Config.Ocr.Language.Equals("auto", StringComparison.OrdinalIgnoreCase)
                ? " · 识别语言为 auto(逐个已安装语言尝试,取最像文字的结果)"
                : recognizer.LanguageTag.StartsWith(Config.Ocr.Language, StringComparison.OrdinalIgnoreCase)
                    ? string.Empty
                    : $"(注意:{Config.Ocr.Language} 不可用,已回退到 {recognizer.LanguageTag})")
            + (_translator is MockTranslator
                ? "。当前引擎为 mock，仅回显原文：[mock 源→目标] 原文。"
                    + "请在“识别与翻译”页选择 openai-compatible，并填写接口地址和模型后再使用翻译功能。"
                : string.Empty));
    }

    /// <summary>Stop the loop and release the engine.</summary>
    public Task StopAsync()
    {
        if (_stopTask is { IsCompleted: false })
            return _stopTask;
        return _stopTask = StopCoreAsync();
    }

    private async Task StopCoreAsync()
    {
        _stopping = true;
        try
        {
            if (Pipeline is not null)
            {
                await Pipeline.StopAsync().ConfigureAwait(false);
                Pipeline = null;
            }

            if (_translator is IDisposable disposable)
                disposable.Dispose();
            _translator = null;
            if (Recognizer is IDisposable recognizerDisposable)
                recognizerDisposable.Dispose();
            Recognizer = null;
            _dumper = null;
            _windowCapture?.Dispose();
            _windowCapture = null;
        }
        finally { _stopping = false; }
    }

    /// <summary>Pause or resume recognition without stopping the loop.</summary>
    public void SetPaused(bool paused) => Pipeline?.SetPaused(paused);

    public void InvalidateTranslation()
    {
        Cache.ClearMemory();
        Pipeline?.InvalidateTranslation();
    }

    /// <summary>Switch to a direction from the overlay's switcher: direction, recognition language, and configuration move together.</summary>
    public Task ApplyLanguagePresetAsync(LanguagePreset preset) => ChangeSettingsAsync(() => ApplyLanguagePresetCoreAsync(preset));

    private async Task ApplyLanguagePresetCoreAsync(LanguagePreset preset)
    {
        Config.Target.ManualLanguage = true;
        var ocrBefore = Config.Ocr.Language;
        Config.Translation.From = preset.From;
        Config.Translation.To = preset.To;
        if (!string.IsNullOrWhiteSpace(preset.Ocr))
            Config.Ocr.Language = preset.Ocr;

        _languages = new LanguagePair(preset.From, preset.To);
        SaveConfig();
        ForgetContext();

        if (IsRunning && !Config.Ocr.Language.Equals(ocrBefore, StringComparison.OrdinalIgnoreCase)
            && Recognizer is not (RapidOcrRecognizer or FourLanguageRecognizer))
        {
            // RapidOCR 使用同一组多语言模型；仅系统识别器需要按语言重建。
            await StopAsync().ConfigureAwait(true);
            Start();
        }

        LanguagesChanged?.Invoke(_languages);
        Notice?.Invoke($"语言已切换为 {preset.Label}(识别 {Config.Ocr.Language})");
    }

    public LanguagePair Languages => _languages;

    /// <summary>Raised after the direction changes, so the windows can resync their controls.</summary>
    public event Action<LanguagePair>? LanguagesChanged;

    public GameProfile? ActiveProfile =>
        GameProfiles.FindById(Config.Translation.ActiveProfile, Config.Translation.GameProfiles);

    /// <summary>Raised after the active profile changes, whichever way it was changed.</summary>
    public event Action<GameProfile?>? ProfileChanged;

    /// <summary>What the pipeline sends with every line: the profile's terms, style, and
    /// worldview, with the hand-typed glossary layered on top. The terms are picked for the language
    /// direction in force right now, so a sheet carrying several languages only sends the relevant one.</summary>
    public ProfileContext CurrentProfile
    {
        get
        {
            var profile = ActiveProfile;
            var languages = _languages;
            return new ProfileContext(
                GameProfiles.Merge(
                    GameProfiles.ForDirection(profile, languages.From, languages.To),
                    Config.Translation.Glossary,
                    Config.Translation.PersonalTerms.Where(term =>
                        (term.From == languages.From || languages.From == "auto") && term.To == languages.To)
                        .Select(term => new GlossaryEntry(term.Source, term.Target, term.Forbidden))),
                string.IsNullOrWhiteSpace(profile?.StyleHint) ? Config.Translation.Translator.StyleHint : profile.StyleHint,
                profile?.Worldview);
        }
    }

    /// <summary>The most recent distinct source lines, for building a term sheet from live play.</summary>
    public IReadOnlyList<string> RecentSources
    {
        get
        {
            lock (_recentSources)
                return [.. _recentSources];
        }
    }

    /// <summary>Switch the game profile: its terms, style, and any language it fixes move together.
    /// A running loop needs a rebuild only when the recognition language changed, because that swaps the recognizer.</summary>
    public Task ApplyProfileAsync(GameProfile? profile, string? because = null, bool manual = true) =>
        ChangeSettingsAsync(() => ApplyProfileCoreAsync(profile, because, manual));

    private async Task ApplyProfileCoreAsync(GameProfile? profile, string? because = null, bool manual = true)
    {
        var previousId = Config.Translation.ActiveProfile;
        var ocrBefore = Config.Ocr.Language;

        Config.Translation.ActiveProfile = profile?.Id ?? string.Empty;
        if (manual)
            Config.Target.ManualProfile = true;
        if (profile is not null && !Config.Target.ManualLanguage)
        {
            if (!string.IsNullOrWhiteSpace(profile.From))
                Config.Translation.From = profile.From;
            if (!string.IsNullOrWhiteSpace(profile.To))
                Config.Translation.To = profile.To;
            if (!string.IsNullOrWhiteSpace(profile.OcrLanguage))
                Config.Ocr.Language = profile.OcrLanguage;
            _languages = new LanguagePair(Config.Translation.From, Config.Translation.To);
        }

        SaveConfig();

        // 已经译好的句子是按旧术语译的,对下一句来说是错误的上文;缓存本身留着 ——
        // 它的键带着术语表,每个档案各自保留自己的答案。
        ForgetContext();

        if (IsRunning && !Config.Ocr.Language.Equals(ocrBefore, StringComparison.OrdinalIgnoreCase)
            && Recognizer is not (RapidOcrRecognizer or FourLanguageRecognizer))
        {
            await StopAsync().ConfigureAwait(true);
            Start();
        }

        LanguagesChanged?.Invoke(_languages);
        ProfileChanged?.Invoke(profile);

        if (previousId == Config.Translation.ActiveProfile)
            return;
        var bans = profile?.Terms.Sum(term => term.Forbidden.Count) ?? 0;
        Notice?.Invoke(profile is null
            ? "已关闭游戏专属模式:回到通用翻译,不做术语校正。"
            : $"{because ?? "已切到"}「{profile.Name}」:{profile.Terms.Count} 条术语"
                + (bans > 0 ? $"、{bans} 条禁用译法" : string.Empty)
                + (Config.Translation.EnforceTerms ? ",按已匹配术语校正译名。" : ",只作为提示(术语校正已关闭)。"));
    }

    /// <summary>Pick the profile that matches a window title and switch to it.</summary>
    public async Task<GameProfile?> AutoDetectProfileAsync(string? title = null, string? processName = null)
    {
        await _settingsGate.WaitAsync().ConfigureAwait(true);
        try { return await AutoDetectProfileCoreAsync(title, processName).ConfigureAwait(true); }
        finally { _settingsGate.Release(); }
    }

    private async Task<GameProfile?> AutoDetectProfileCoreAsync(string? title = null, string? processName = null)
    {
        if (!Config.Translation.AutoDetectProfile || Config.Target.ManualProfile)
            return ActiveProfile;

        var target = FindTarget();
        var match = GameProfiles.MatchWindow(title ?? target?.Title, processName ?? target?.ProcessName, Config.Translation.GameProfiles);
        if ((match?.Id ?? string.Empty) == Config.Translation.ActiveProfile)
            return match;

        await ApplyProfileCoreAsync(match, because: "按目标窗口匹配到", manual: false).ConfigureAwait(true);
        return match;
    }

    public GameProfile? FindProfile(string? id) => GameProfiles.FindById(id, Config.Translation.GameProfiles);

    /// <summary>Drop the running context, so lines translated under the old terms are not reused as history.</summary>
    public void ForgetContext()
    {
        Pipeline?.InvalidateTranslation();
        lock (_recentSources)
            _recentSources.Clear();
    }

    /// <summary>Keep the last few distinct recognized lines: what 「AI 生成术语表」 sends when there are no terms to list yet.</summary>
    private void Remember(string source)
    {
        var line = source?.Trim() ?? string.Empty;
        if (line.Length < 2)
            return;

        lock (_recentSources)
        {
            if (_recentSources.Contains(line, StringComparer.Ordinal))
                return;
            _recentSources.Add(line);
            while (_recentSources.Count > 12)
                _recentSources.RemoveAt(0);
        }
    }

    /// <summary>Ask the configured engine to draft a term sheet, parsed here and not in the window so a probe can use the same path.</summary>
    public async Task<(List<GameTerm> Terms, string? GameName, string? Worldview, List<string> Problems, string Reply)>
        GenerateTermSheetAsync(
            string? gameName,
            string? sampleLines,
            string? existingSheet,
            string? synopsis = null,
            CancellationToken cancellationToken = default)
    {
        // 术语表是一次开着思考模式的长调用,所以它有自己的、长得多的超时。
        var translator = TranslatorFactory.CreateForTermSheet(Config.Translation.Translator);
        try
        {
            if (translator is not IChatCompleter completer)
            {
                throw new InvalidOperationException(
                    $"当前引擎({translator.Id})不能回答普通提问,无法生成术语表。"
                    + "请在「识别与翻译」里把翻译引擎换成 openai-compatible(本地或在线都行)。");
            }

            var reply = await completer
                .CompleteAsync(
                    TermSheetBuilder.SystemPrompt,
                    TermSheetBuilder.BuildUserPrompt(gameName, Config.Translation.To, sampleLines, existingSheet, synopsis),
                    cancellationToken)
                .ConfigureAwait(true);

            var (terms, detected, worldview, problems) = TermSheetBuilder.ParseReply(reply);
            return (terms, detected, worldview, problems, reply);
        }
        finally
        {
            if (translator is IDisposable disposable)
                disposable.Dispose();
        }
    }

    /// <summary>读取目标区域；窗口捕获与屏幕捕获使用各自的坐标来源。</summary>
    private async Task<Frame> CaptureFrameAsync(Int32Rect region, CancellationToken cancellationToken)
    {
        if (Config.Target.CaptureBackend.Equals("screen", StringComparison.OrdinalIgnoreCase))
            return ScreenCapture.CaptureScreenRegion(region);
        var window = FindTarget() ?? throw new InvalidOperationException("目标窗口已关闭。");
        if (Config.Target.CaptureBackend.Equals("window", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                if (_windowCapture?.Handle != window.Handle)
                {
                    _windowCapture?.Dispose();
                    _windowCapture = null;
                    _windowCapture = new WindowCapture(window.Handle);
                }
                return await _windowCapture.CaptureAsync(window, region, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception error) when (error is TimeoutException or NotSupportedException or System.Runtime.InteropServices.COMException)
            {
                // 只回退到读取目标窗口的兼容方式，不把桌面上的遮挡物送入识别。
                var fallback = ScreenCapture.CaptureWindowClient(window, CaptureBackend.PrintWindow);
                var pixels = fallback.Bgra;
                var hasContent = false;
                for (var index = 0; index < pixels.Length; index += 16)
                    if (pixels[index] > 8 || pixels[index + 1] > 8 || pixels[index + 2] > 8) { hasContent = true; break; }
                if (!hasContent)
                    throw new CaptureUnavailableException(error);
                return ImageOps.Crop(fallback, new Int32Rect(region.X - fallback.SourceRegion.X,
                    region.Y - fallback.SourceRegion.Y, region.Width, region.Height));
            }
        }
        var client = ScreenCapture.CaptureWindowClient(window, CaptureBackend.PrintWindow);
        return ImageOps.Crop(client, new Int32Rect(
            region.X - client.SourceRegion.X, region.Y - client.SourceRegion.Y, region.Width, region.Height));
    }

    private ITextRecognizer? CreateRecognizer()
    {
        if (Config.Ocr.Engine.Equals("rapidocr", StringComparison.OrdinalIgnoreCase))
        {
            return new FourLanguageRecognizer(new RapidOcrSettings
            {
                ModelDirectory = string.IsNullOrWhiteSpace(Config.Ocr.RapidModelDirectory)
                    ? RapidOcrRecognizer.DefaultModelDirectory()
                    : Config.Ocr.RapidModelDirectory,
                LimitSideLen = Config.Ocr.RapidLimitSideLen,
                UseGpu = Config.Ocr.RapidUseGpu,
            }, () => Config.Ocr.Language);
        }

        var fallbacks = Config.Ocr.Fallbacks.Length > 0 ? Config.Ocr.Fallbacks : ["en-US", "zh-Hans-CN"];

        if (Config.Ocr.Language.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            var preferred = new[] { "ja", "en-US", "zh-Hans-CN", "ko-KR" }.Concat(fallbacks).Distinct(StringComparer.OrdinalIgnoreCase);
            // 只装了一种引擎就没有可挑的,直接返回单引擎识别器。
            var multi = MultiLanguageRecognizer.TryCreate(preferred, maxCandidates: 4);
            return multi is not null ? multi : MultiLanguageRecognizer.TryCreateSingle(preferred);
        }

        if (Config.Ocr.Language.StartsWith("ko", StringComparison.OrdinalIgnoreCase))
            return WindowsOcrRecognizer.TryCreate(Config.Ocr.Language);
        return WindowsOcrRecognizer.TryCreateWithFallback(Config.Ocr.Language, fallbacks);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        Cache.Dispose();
    }
}
