using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using GuGuGaGaTranslator.Core.Config;
using GuGuGaGaTranslator.Core.Pipeline;

namespace GuGuGaGaTranslator.Core.Diagnostics;

public enum DiagnosticEventKind
{
    Started, Pipeline, CacheUnavailable, CacheCleared
}

/// <summary>No field accepts screen text, keys, paths, exception messages or prompts.</summary>
public sealed record DiagnosticEvent
{
    public DateTimeOffset At { get; init; } = DateTimeOffset.UtcNow;
    public DiagnosticEventKind Kind { get; init; }
    public PipelineStatus? Status { get; init; }
    public PipelineSkipReason? Skipped { get; init; }
    public double CaptureMs { get; init; }
    public double OcrMs { get; init; }
    public double TranslateMs { get; init; }
    public bool FromCache { get; init; }
}

/// <summary>Rotating, content-free diagnostics with a whitelist-based ZIP export.</summary>
public sealed class DiagnosticsService
{
    private const int MaxLogBytes = 256 * 1024;
    private string _directory;
    private readonly object _gate = new();
    private DateTimeOffset _lastQuietUpdate;
    public bool LoggingAvailable { get; private set; } = true;
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    public DiagnosticsService(string configurationDirectory) =>
        _directory = Path.Combine(configurationDirectory, "logs");

    public void ConfigureDirectory(string root, string configured)
    {
        lock (_gate) _directory = DataDirectory.Location(configured, root, "logs");
    }

    public void Record(DiagnosticEvent record)
    {
        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(_directory);
                var current = Path.Combine(_directory, "runtime.jsonl");
                if (File.Exists(current) && new FileInfo(current).Length >= MaxLogBytes)
                    File.Move(current, Path.Combine(_directory, "runtime.previous.jsonl"), overwrite: true);
                File.AppendAllText(current, JsonSerializer.Serialize(record, Json) + Environment.NewLine);
                LoggingAvailable = true;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                LoggingAvailable = false;
            }
        }
    }

    public void Record(PipelineUpdate update)
    {
        if (update.Status == PipelineStatus.Translating)
            return;
        lock (_gate)
        {
            if (update.Status is PipelineStatus.Unchanged or PipelineStatus.NoText or PipelineStatus.NoRegion or PipelineStatus.Reused)
            {
                if (update.At - _lastQuietUpdate < TimeSpan.FromSeconds(30))
                    return;
                _lastQuietUpdate = update.At;
            }
            Record(new DiagnosticEvent
            {
                Kind = DiagnosticEventKind.Pipeline,
                At = update.At,
                Status = update.Status,
                Skipped = update.Skipped,
                CaptureMs = SafeDuration(update.CaptureDuration.TotalMilliseconds),
                OcrMs = SafeDuration(update.OcrDuration.TotalMilliseconds),
                TranslateMs = SafeDuration(update.TranslateDuration.TotalMilliseconds),
                FromCache = update.FromCache,
            });
        }
    }

    public void Export(string destination, AppConfig config, PipelineStats? stats = null)
    {
        var fullPath = Path.GetFullPath(destination);
        if (!Path.GetExtension(fullPath).Equals(".zip", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("诊断包请保存为 ZIP，避免覆盖配置或数据库文件。");
        var temporary = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var archive = ZipFile.Open(temporary, ZipArchiveMode.Create))
            {
                WriteJson(archive, "environment.json", new
                {
                    schemaVersion = 1,
                    at = DateTimeOffset.UtcNow,
                    appVersion = typeof(AppConfig).Assembly.GetName().Version?.ToString(),
                    os = Environment.OSVersion.VersionString,
                    runtime = Environment.Version.ToString(),
                    architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
                    processorCount = Environment.ProcessorCount,
                    loggingAvailable = LoggingAvailable,
                });
                WriteJson(archive, "settings.json", new
                {
                    // Explicit fields only: profiles, endpoints, paths and credentials cannot enter the export.
                    provider = Known(config.Translation.Translator.Provider, "mock", "openai-compatible", "openai", "local", "caiyun", "youdao", "baidu"),
                    from = Known(config.Translation.From, "auto", "ja", "en", "zh-Hans", "ko"),
                    to = Known(config.Translation.To, "ja", "en", "zh-Hans", "ko"),
                    ocrEngine = Known(config.Ocr.Engine, "rapidocr", "windows"),
                    ocrLanguage = Known(config.Ocr.Language, "auto", "ja", "en-US", "zh-Hans-CN", "ko-KR"),
                    config.Ocr.RapidLimitSideLen,
                    config.Ocr.RapidUseGpu,
                    timeoutSeconds = Math.Clamp(config.Translation.Translator.TimeoutSeconds, 1, 600),
                    historyLines = Math.Clamp(config.Translation.HistoryLines, 0, 12),
                    persistentCache = config.Translation.Cache.Persist,
                    cacheEnabled = config.Translation.Cache.Enabled,
                    matchCacheContext = config.Translation.Cache.MatchContext,
                    memoryCacheEntries = Math.Clamp(config.Translation.Cache.MemoryEntries, 100, 20000),
                    retentionDays = Math.Clamp(config.Translation.Cache.RetentionDays, 1, 365),
                    maximumEntries = Math.Clamp(config.Translation.Cache.MaximumEntries, 100, 100000),
                    config.Pipeline.PollIntervalMs,
                    config.Pipeline.MaskOwnWindows,
                    config.Pipeline.ScriptGuard,
                    config.Overlay.ExcludeFromCapture,
                    config.Overlay.ClickThrough,
                });
                if (stats is not null)
                    WriteJson(archive, "counters.json", stats);
                var entry = archive.CreateEntry("events.jsonl");
                using var writer = new StreamWriter(entry.Open());
                foreach (var record in ReadSafeEvents())
                    writer.WriteLine(JsonSerializer.Serialize(record, Json));
            }
            File.Move(temporary, fullPath, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private List<DiagnosticEvent> ReadSafeEvents()
    {
        var result = new List<DiagnosticEvent>();
        lock (_gate)
        {
            foreach (var name in new[] { "runtime.previous.jsonl", "runtime.jsonl" })
            {
                var path = Path.Combine(_directory, name);
                if (!File.Exists(path) || new FileInfo(path).Length > MaxLogBytes * 2)
                    continue;
                foreach (var line in File.ReadLines(path))
                {
                    try
                    {
                        var item = JsonSerializer.Deserialize<DiagnosticEvent>(line, Json);
                        if (item is null || !Enum.IsDefined(item.Kind)
                            || (item.Status is { } status && !Enum.IsDefined(status))
                            || (item.Skipped is { } skip && !Enum.IsDefined(skip)))
                            continue;
                        // Deserialize and re-serialize a typed whitelist; even extra fields in an edited log are dropped.
                        result.Add(item with
                        {
                            CaptureMs = SafeDuration(item.CaptureMs),
                            OcrMs = SafeDuration(item.OcrMs),
                            TranslateMs = SafeDuration(item.TranslateMs),
                        });
                    }
                    catch (JsonException) { }
                }
            }
        }
        return result;
    }

    private static double SafeDuration(double value) => double.IsFinite(value) ? Math.Clamp(value, 0, 3600000) : 0;
    private static string Known(string value, params string[] allowed) =>
        allowed.FirstOrDefault(candidate => candidate.Equals(value, StringComparison.OrdinalIgnoreCase)) ?? "unknown";
    private static void WriteJson(ZipArchive archive, string name, object value)
    {
        using var stream = archive.CreateEntry(name).Open();
        JsonSerializer.Serialize(stream, value, new JsonSerializerOptions(Json) { WriteIndented = true });
    }
}
