using System.Diagnostics;
using System.IO;
using System.Threading.Channels;
using System.Windows;
using GuGuGaGaTranslator.Core.Capture;
using GuGuGaGaTranslator.Core.Imaging;
using GuGuGaGaTranslator.Core.Ocr;
using GuGuGaGaTranslator.Core.Text;
using GuGuGaGaTranslator.Core.Translation;

namespace GuGuGaGaTranslator.Core.Pipeline;

/// <summary>What the pipeline decided on one iteration.</summary>
public enum PipelineStatus
{
    NoRegion,

    Unchanged,

    NoText,

    Translated,

    /// <summary>A translation is arriving piece by piece; <see cref="PipelineUpdate.Translation"/> holds the text so far.</summary>
    Translating,

    /// <summary>The line was the one already showing, so its translation was reused.</summary>
    Reused,

    /// <summary>The captured image is a flat colour, so the cause is upstream of recognition: an
    /// exclusive-fullscreen game, a covered or minimized window, or a region off the dialogue.</summary>
    Blank,

    Error,
}

/// <summary>Why an iteration produced no translation.</summary>
public enum PipelineSkipReason
{
    None,

    TooShort,

    /// <summary>Recognition returned text in the wrong script, so it read the game's own UI rather than the dialogue.</summary>
    WrongScript,
}

/// <summary>One iteration's outcome, the only thing the pipeline publishes.</summary>
public sealed record PipelineUpdate
{
    public required PipelineStatus Status { get; init; }

    public required DateTimeOffset At { get; init; }

    public Frame? Frame { get; init; }

    public FrameSignature? Signature { get; init; }

    /// <summary>How many signature blocks differed from the previous recognized frame.</summary>
    public int SignatureDistance { get; init; }

    public OcrResult? Ocr { get; init; }

    public string SourceText { get; init; } = string.Empty;

    public string? Translation { get; init; }

    /// <summary>Term corrections applied to <see cref="Translation"/> after the model answered.</summary>
    public IReadOnlyList<TermFix> TermFixes { get; init; } = [];

    public bool FromCache { get; init; }

    public string? Error { get; init; }

    public long Revision { get; init; }

    public PipelineSkipReason Skipped { get; init; }

    public TimeSpan CaptureDuration { get; init; }

    public TimeSpan OcrDuration { get; init; }

    public TimeSpan TranslateDuration { get; init; }

    public TimeSpan TotalDuration { get; init; }
}

/// <summary>Counters the UI shows to make the loop's behavior legible.</summary>
public sealed record PipelineStats
{
    public required long Frames { get; init; }

    public required long Recognitions { get; init; }

    public required long Translations { get; init; }

    public required long CacheHits { get; init; }

    /// <summary>Frames skipped because the text matched the line already shown.</summary>
    public required long RepeatedLines { get; init; }

    public required long Errors { get; init; }
}

/// <summary>Loop tuning carried into the pipeline.</summary>
public sealed record PipelineOptions
{
    public int PollIntervalMs { get; init; } = 400;

    public int ChangeThresholdBits { get; init; } = 6;

    public double RepeatSimilarity { get; init; } = 0.92;

    public int ForceRefreshMs { get; init; } = 5000;

    public bool TranslateOnlyOnChange { get; init; } = true;

    public int MinTextLength { get; init; } = 4;

    /// <summary>Skip results that cannot be the source language, which is how the game's own SAVE / LOAD / CONFIG labels stop being translated as dialogue.</summary>
    public bool ScriptGuard { get; init; } = true;

    /// <summary>Rewrite the glossary's terms into the finished translation, so the official wording holds even when the model chose another.</summary>
    public bool EnforceTerms { get; init; } = true;

    /// <summary>Blank this tool's own windows out of every frame before it is read, so the control window
    /// or the translation panel can never be mistaken for the text being translated.</summary>
    public bool MaskOwnWindows { get; init; } = true;

    public double OcrScale { get; init; } = 2.0;

    public double OcrGrayscale { get; init; }

    public int ErrorBackoffMs { get; init; } = 1500;

    /// <summary>How many previous lines travel with each request as context; 0 disables context.</summary>
    public int HistoryLines { get; init; } = 3;
}

/// <summary>One language direction, read at the moment a translation is built; <c>auto</c> as the source lets the engine detect it.</summary>
public sealed record LanguagePair(string From, string To);

/// <summary>Everything that makes a translation belong to one particular work: its fixed terms, its setting, and its voice.</summary>
public sealed record ProfileContext(
    IReadOnlyList<GlossaryEntry> Glossary,
    string? StyleHint = null,
    string? Worldview = null)
{
    /// <summary>A profile with nothing in it: the plain translator.</summary>
    public static readonly ProfileContext Empty = new([]);
}

/// <summary>Everything the loop needs, so a settings change is a new pipeline rather than a mutation.</summary>
public sealed record PipelineSettings
{
    /// <summary>Resolves the region to read, in screen pixels, on every iteration: a callback rather than
    /// a rectangle so the region follows the target window as it moves.</summary>
    public required Func<Int32Rect?> RegionProvider { get; init; }

    public required ITextRecognizer Recognizer { get; init; }

    public Func<Int32Rect, Frame>? Capture { get; init; }

    public Func<nint>? TargetHandle { get; init; }

    public bool IsScreenCapture { get; init; } = true;

    public required ITranslator Translator { get; init; }

    /// <summary>Read on every translation, so the overlay's language switcher can change direction without tearing down the loop.</summary>
    public required Func<LanguagePair> Languages { get; init; }

    /// <summary>Read on every translation, so picking or editing a profile applies to the next line.</summary>
    public Func<ProfileContext> Profile { get; init; } = () => ProfileContext.Empty;

    public PipelineOptions Options { get; init; } = new();

    public TranslationCache? Cache { get; init; }

    public FrameDumper? Dumper { get; init; }
}

/// <summary>The long-running translation loop: capture, decide whether the frame is worth reading,
/// recognize, skip lines already translated, translate, publish. The order matters — recognition and
/// translation are the expensive steps, so both are gated on the cheapest evidence that something changed.</summary>
public sealed class TranslationPipeline : IAsyncDisposable
{
    private readonly PipelineSettings _settings;
    private readonly TranslationCache _cache;
    private readonly PipelineOptions _options;

    private CancellationTokenSource? _cancellation;
    private Task? _loop;
    private readonly object _state = new();
    private Channel<TranslationWork> _pending = CreateQueue();
    private CancellationTokenSource? _activeTranslation;
    private long _revision;
    private volatile string? _pendingSource;
    private Int32Rect? _lastRegion;

    private sealed record TranslationWork(long Revision, TranslationRequest Request, PipelineUpdate Update);
    private static Channel<TranslationWork> CreateQueue() => Channel.CreateBounded<TranslationWork>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true, SingleWriter = true });

    public bool IsCurrent(PipelineUpdate update) => update.Revision == Interlocked.Read(ref _revision);

    private FrameSignature? _lastSignature;
    private double[]? _lastCells;
    private string? _lastSourceText;
    private string? _lastTranslation;
    private readonly List<TranslationHistory> _recent = [];
    private DateTimeOffset _lastRecognizedAt = DateTimeOffset.MinValue;

    private long _frames;
    private long _recognitions;
    private long _translations;
    private long _cacheHits;
    private long _repeats;
    private long _errors;

    private volatile bool _forceNext = true;
    private volatile bool _paused;

    public TranslationPipeline(PipelineSettings settings)
    {
        _settings = settings;
        _options = settings.Options;
        _cache = settings.Cache ?? new TranslationCache();
    }

    /// <summary>Raised once per iteration, on the loop's own thread: subscribers that touch a UI must marshal.</summary>
    public event Action<PipelineUpdate>? Updated;

    public bool IsRunning => _loop is { IsCompleted: false };

    public bool IsPaused => _paused;

    public TranslationCache Cache => _cache;

    public PipelineStats Stats => new()
    {
        Frames = Interlocked.Read(ref _frames),
        Recognitions = Interlocked.Read(ref _recognitions),
        Translations = Interlocked.Read(ref _translations),
        CacheHits = Interlocked.Read(ref _cacheHits),
        RepeatedLines = Interlocked.Read(ref _repeats),
        Errors = Interlocked.Read(ref _errors),
    };

    /// <summary>Start the loop; a running pipeline keeps running.</summary>
    public void Start()
    {
        if (IsRunning) return;
        _cancellation = new CancellationTokenSource();
        _forceNext = true;
        _pending = CreateQueue();
        var token = _cancellation.Token;
        _loop = Task.WhenAll(Task.Run(() => LoopAsync(token)), Task.Run(() => TranslateLoopAsync(token)));
    }

    /// <summary>Ask the next iteration to recognize regardless of change detection.</summary>
    public void Invalidate() => _forceNext = true;

    /// <summary>Forget the line already translated and the running context, so the next recognition is
    /// translated again: after a language, engine, or glossary change the old lines would mislead it.</summary>
    public void InvalidateTranslation()
    {
        lock (_state)
        {
            _revision++;
            _activeTranslation?.Cancel();
            _pendingSource = null;
            _lastSourceText = null;
            _lastTranslation = null;
            _lastSignature = null;
            _recent.Clear();
            _forceNext = true;
        }
    }

    public void SetPaused(bool paused)
    {
        _paused = paused;
        InvalidateTranslation();
    }

    public async Task StopAsync()
    {
        if (_cancellation is null) return;

        await _cancellation.CancelAsync().ConfigureAwait(false);
        if (_loop is not null)
        {
            try
            {
                await _loop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Cancellation is how the loop ends.
            }
        }

        _cancellation.Dispose();
        _cancellation = null;
        _loop = null;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);

    private async Task LoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var iteration = Stopwatch.StartNew();
            try
            {
                if (!_paused) await IterateAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                Interlocked.Increment(ref _errors);
                Publish(new PipelineUpdate
                {
                    Status = PipelineStatus.Error,
                    At = DateTimeOffset.Now,
                    Error = $"{exception.GetType().Name}: {exception.Message}",
                    TotalDuration = iteration.Elapsed,
                });
                await SafeDelayAsync(_options.ErrorBackoffMs, cancellationToken).ConfigureAwait(false);
            }

            var remaining = _options.PollIntervalMs - (int)iteration.ElapsedMilliseconds;
            if (remaining > 0) await SafeDelayAsync(remaining, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task IterateAsync(CancellationToken cancellationToken)
    {
        var iteration = Stopwatch.StartNew();
        var region = _settings.RegionProvider();
        if (region is not { Width: > 0, Height: > 0 })
        {
            InvalidateTranslation();
            Publish(new PipelineUpdate
            {
                Status = PipelineStatus.NoRegion,
                At = DateTimeOffset.Now,
                TotalDuration = iteration.Elapsed,
            });
            await SafeDelayAsync(_options.PollIntervalMs, cancellationToken).ConfigureAwait(false);
            return;
        }

        var captureWatch = Stopwatch.StartNew();
        if (_lastRegion != region)
        {
            InvalidateTranslation();
            _lastRegion = region;
        }
        var revision = Interlocked.Read(ref _revision);
        var frame = _settings.Capture?.Invoke(region.Value) ?? ScreenCapture.CaptureScreenRegion(region.Value);
        // 先抹掉自己的窗口再判断画面有没有变:否则语言条上的按钮高亮、翻译框里换了一句话,
        // 都会被当成「游戏画面变了」而触发一次多余的识别。
        if (_options.MaskOwnWindows && _settings.IsScreenCapture)
            SelfWindowMask.Apply(frame, SelfWindowMask.ScreenRects(_settings.TargetHandle?.Invoke() ?? 0));
        captureWatch.Stop();
        Interlocked.Increment(ref _frames);

        var signature = FrameHasher.Compute(frame);
        var cells = FrameHasher.BlockLuminance(frame);
        var distance = _lastCells is null
            ? FrameSignature.BitCount
            : FrameHasher.ChangedCellCount(_lastCells, cells);
        var forced = _forceNext || (DateTimeOffset.Now - _lastRecognizedAt).TotalMilliseconds >= _options.ForceRefreshMs;

        if (!forced && distance < _options.ChangeThresholdBits && _options.TranslateOnlyOnChange)
        {
            if (_pendingSource is not null) return;
            Publish(new PipelineUpdate
            {
                Status = PipelineStatus.Unchanged,
                At = DateTimeOffset.Now,
                Signature = signature,
                SignatureDistance = distance,
                SourceText = _lastSourceText ?? string.Empty,
                Translation = _pendingSource is null ? _lastTranslation : null,
                CaptureDuration = captureWatch.Elapsed,
                TotalDuration = iteration.Elapsed,
            });
            return;
        }

        _forceNext = false;

        var prepared = ImageOps.Upscale(ImageOps.ToGrayscale(frame, _options.OcrGrayscale), _options.OcrScale);
        var ocr = await _settings.Recognizer.RecognizeAsync(prepared, cancellationToken).ConfigureAwait(false);
        Interlocked.Increment(ref _recognitions);
        if (revision != Interlocked.Read(ref _revision) || _paused) return;

        _lastSignature = signature;
        _lastCells = cells;
        _lastRecognizedAt = DateTimeOffset.Now;

        var sourceText = TextNormalizer.Tidy(ocr.Text);
        var skip = SkipReason(sourceText, _settings.Languages().From);
        if (skip != PipelineSkipReason.None || sourceText.Length < _options.MinTextLength)
        {
            InvalidateTranslation();
            // A flat colour means the problem is upstream of recognition, not in it.
            var blank = sourceText.Length == 0 && IsBlank(frame);
            Publish(new PipelineUpdate
            {
                Status = blank ? PipelineStatus.Blank : PipelineStatus.NoText,
                At = DateTimeOffset.Now,
                Frame = frame,
                Signature = signature,
                SignatureDistance = distance,
                Ocr = ocr,
                SourceText = sourceText,
                Skipped = skip == PipelineSkipReason.None ? PipelineSkipReason.TooShort : skip,
                CaptureDuration = captureWatch.Elapsed,
                OcrDuration = ocr.Duration,
                TotalDuration = iteration.Elapsed,
            });
            _settings.Dumper?.Dump(frame, signature, ocr, sourceText, null, distance);
            return;
        }

        // The same line re-read with a character of OCR noise is not a new line; reusing
        // its translation is what keeps a static box from paying per poll.
        if (_pendingSource is null && _lastSourceText is not null
            && _lastTranslation is not null
            && TextNormalizer.Similarity(_lastSourceText, sourceText) >= _options.RepeatSimilarity)
        {
            Interlocked.Increment(ref _repeats);
            Publish(new PipelineUpdate
            {
                Status = PipelineStatus.Reused,
                At = DateTimeOffset.Now,
                Frame = frame,
                Signature = signature,
                SignatureDistance = distance,
                Ocr = ocr,
                SourceText = sourceText,
                Translation = _lastTranslation,
                FromCache = true,
                CaptureDuration = captureWatch.Elapsed,
                OcrDuration = ocr.Duration,
                TotalDuration = iteration.Elapsed,
            });
            _settings.Dumper?.Dump(frame, signature, ocr, sourceText, _lastTranslation, distance);
            return;
        }

        lock (_state)
        {
            if (revision != _revision || _paused) return;
            if (_pendingSource is not null && TextNormalizer.Similarity(_pendingSource, sourceText) >= _options.RepeatSimilarity)
                return;
        }
        var languages = _settings.Languages();
        var profile = _settings.Profile();
        var request = new TranslationRequest
        {
            Text = sourceText,
            From = languages.From,
            To = languages.To,
            Glossary = profile.Glossary,
            StyleHint = profile.StyleHint,
            Worldview = profile.Worldview,
            // The recent lines keep pronouns and tone consistent across a conversation.
            Context = [],
        };

        lock (_state)
        {
            if (revision != _revision || _paused) return;
            _revision++;
            _activeTranslation?.Cancel();
            _pendingSource = sourceText;
            _pending.Writer.TryWrite(new TranslationWork(_revision, request, new PipelineUpdate
            {
                Status = PipelineStatus.Translating, At = DateTimeOffset.Now, Frame = frame,
                Signature = signature, SignatureDistance = distance, Ocr = ocr, SourceText = sourceText,
                CaptureDuration = captureWatch.Elapsed, OcrDuration = ocr.Duration,
                Revision = _revision,
            }));
        }
    }

    private async Task TranslateLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var work in _pending.Reader.ReadAllAsync(cancellationToken))
            {
                using var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                TranslationRequest request;
                lock (_state)
                {
                    if (work.Revision != _revision || _paused) continue;
                    _activeTranslation = requestCancellation;
                    request = work.Request with { Context = _options.HistoryLines > 0 ? [.. _recent] : [] };
                }
                try
                {
                    await TranslateWorkAsync(work, request, requestCancellation.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (requestCancellation.IsCancellationRequested) { }
                catch (Exception exception)
                {
                    lock (_state)
                    {
                        if (work.Revision == _revision)
                        {
                            Interlocked.Increment(ref _errors);
                            Publish(work.Update with { Status = PipelineStatus.Error, Error = exception.Message });
                        }
                    }
                    await SafeDelayAsync(_options.ErrorBackoffMs, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    lock (_state)
                    {
                        if (ReferenceEquals(_activeTranslation, requestCancellation)) _activeTranslation = null;
                        if (work.Revision == _revision) { _pendingSource = null; _forceNext = true; }
                    }
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    private async Task TranslateWorkAsync(TranslationWork work, TranslationRequest request, CancellationToken cancellationToken)
    {
        var translateWatch = Stopwatch.StartNew();
        lock (_state)
        {
            if (work.Revision != _revision) return;
            Publish(work.Update);
        }
        string translation;
        var fromCache = false;
        IReadOnlyList<TermFix> fixes = [];
        if (_cache.TryGet(_settings.Translator.Id, request, out var cached))
        {
            translation = cached;
            fromCache = true;
            Interlocked.Increment(ref _cacheHits);
        }
        else if (_settings.Translator is IStreamingTranslator streaming)
        {
            // Report the growing text so the overlay fills in as the model writes.
            translation = await streaming
                .TranslateAsync(request, partial =>
                {
                    lock (_state)
                    {
                        if (work.Revision != _revision || cancellationToken.IsCancellationRequested) return;
                        Publish(work.Update with {
                            Translation = Enforce(partial, request, _options, out _),
                            TranslateDuration = translateWatch.Elapsed,
                        });
                    }
                }, cancellationToken)
                .ConfigureAwait(false);

            translation = Enforce(translation, request, _options, out fixes);
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _translations);
        }
        else
        {
            translation = await _settings.Translator.TranslateAsync(request, cancellationToken).ConfigureAwait(false);
            translation = Enforce(translation, request, _options, out fixes);
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _translations);
        }

        translateWatch.Stop();
        lock (_state)
        {
            if (work.Revision != _revision || cancellationToken.IsCancellationRequested) return;
            _cache.Set(_settings.Translator.Id, request, translation);
            _lastSourceText = request.Text;
            _lastTranslation = translation;
            Remember(request.Text, translation);
            Publish(work.Update with
            {
                Status = PipelineStatus.Translated, At = DateTimeOffset.Now,
                Translation = translation, TermFixes = fixes, FromCache = fromCache,
                TranslateDuration = translateWatch.Elapsed,
                TotalDuration = work.Update.CaptureDuration + work.Update.OcrDuration + translateWatch.Elapsed,
            });
        }
        if (work.Update.Frame is { } frame && work.Update.Signature is { } signature)
            _settings.Dumper?.Dump(frame, signature, work.Update.Ocr!, request.Text, translation, work.Update.SignatureDistance);
    }

    /// <summary>Apply the game profile's terms to a translation.</summary>
    private static string Enforce(
        string translation,
        TranslationRequest request,
        PipelineOptions options,
        out IReadOnlyList<TermFix> fixes)
    {
        if (!options.EnforceTerms || request.Glossary.Count == 0)
        {
            fixes = [];
            return translation;
        }

        var (text, applied) = TermEnforcer.Apply(translation, request.Glossary);
        fixes = applied;
        return text;
    }

    /// <summary>Add a translated line to the rolling context window.</summary>
    private void Remember(string source, string translation)
    {
        var capacity = Math.Max(0, _options.HistoryLines);
        if (capacity == 0) return;

        // A repeated line (a re-read, a cached repeat) should not fill the window with duplicates.
        if (_recent.Count > 0 && _recent[^1].Source == source) return;

        _recent.Add(new TranslationHistory(source, translation));
        while (_recent.Count > capacity) _recent.RemoveAt(0);
    }

    /// <summary>Whether the frame is a flat colour, which points at a capture problem rather than a recognition one.</summary>
    internal static bool IsBlank(Frame frame)
    {
        var blocks = FrameHasher.BlockLuminance(frame);
        var min = double.MaxValue;
        var max = double.MinValue;
        foreach (var value in blocks)
        {
            if (value < min) min = value;
            if (value > max) max = value;
        }

        return max - min < 4;
    }

    /// <summary>Decide whether a recognition result can be a line of dialogue in the configured source language.</summary>
    internal PipelineSkipReason SkipReason(string text, string from)
    {
        if (!_options.ScriptGuard) return PipelineSkipReason.None;
        if (from.Equals("auto", StringComparison.OrdinalIgnoreCase)) return PipelineSkipReason.None;
        if (text.Length == 0) return PipelineSkipReason.TooShort;

        var expects = ExpectedScript(from);
        if (expects is null) return PipelineSkipReason.None;

        // Two expected-script characters tell dialogue from a Latin UI label; one could be a logo glyph.
        var matches = text.Count(character => expects(character));
        return matches >= 2 ? PipelineSkipReason.None : PipelineSkipReason.WrongScript;
    }

    /// <summary>The character test for a source language, or null for Latin-script languages the guard cannot help with.</summary>
    private static Func<char, bool>? ExpectedScript(string from) => from.ToLowerInvariant() switch
    {
        // Any CJK character counts for both Chinese and Japanese: a Japanese line
        // can be all kanji, so separating them would reject valid dialogue.
        "ja" or "japanese" => IsCjk,
        "zh" or "zh-hans" or "zh-hant" or "zh-hans-cn" or "zh-hant-tw" or "chinese" => IsCjk,
        _ => null,
    };

    private static bool IsCjk(char character) => character switch
    {
        >= '\u3040' and <= '\u30FF' => true,   // kana
        >= '\u3400' and <= '\u4DBF' => true,   // CJK extension A
        >= '\u4E00' and <= '\u9FFF' => true,   // CJK unified
        >= '\uF900' and <= '\uFAFF' => true,   // compatibility ideographs
        _ => false,
    };

    private void Publish(PipelineUpdate update)
    {
        try
        {
            Updated?.Invoke(update with { Revision = update.Revision == 0 ? Interlocked.Read(ref _revision) : update.Revision });
        }
        catch (Exception)
        {
            // A subscriber's failure must not stop the translation loop.
        }
    }

    private static async Task SafeDelayAsync(int milliseconds, CancellationToken cancellationToken)
    {
        if (milliseconds <= 0) return;
        try
        {
            await Task.Delay(milliseconds, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }
}
