using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Windows;
using GuGuGaGaTranslator.App;
using GuGuGaGaTranslator.Core.Capture;
using GuGuGaGaTranslator.Core.Config;
using GuGuGaGaTranslator.Core.Ocr;
using GuGuGaGaTranslator.Core.Pipeline;
using GuGuGaGaTranslator.Core.Translation;
using GuGuGaGaTranslator.Installation;

internal static class Program
{
    private static int _passed;
    internal static void Check(bool condition, string name)
    {
        if (!condition)
            throw new Exception(name);
        Console.WriteLine("PASS " + name);
        _passed++;
    }

    private static async Task Main()
    {
        var request = new TranslationRequest { Text = "Hello!", From = "en", To = "zh" };
        var key = TranslationCache.KeyFor("engine", request);
        Check(key != TranslationCache.KeyFor("engine", request with
        {
            Worldview = "world"
        }), "cache: worldview");
        Check(key != TranslationCache.KeyFor("engine", request with
        {
            StyleHint = "style"
        }), "cache: style");
        Check(key != TranslationCache.KeyFor("engine", request with
        {
            Context = [new("prior", "previous")]
        }), "cache: context");
        Check(key != TranslationCache.KeyFor("engine", request with
        {
            Text = "Hello?"
        }), "cache: punctuation");
        using var first = new OpenAiCompatibleTranslator(new()
        {
            BaseUrl = "https://one.invalid/v1",
            Model = "same"
        });
        using var second = new OpenAiCompatibleTranslator(new()
        {
            BaseUrl = "https://two.invalid/v1",
            Model = "same"
        });
        Check(first.Id != second.Id, "cache: endpoint");

        var scratch = Path.Combine(Path.GetTempPath(), "gggt-regression-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        try
        {
            Improvements.CheckAll(scratch);
            var config = new AppConfig();
            config.Translation.Translator = config.Translation.Translator with
            {
                ApiKey = "TEST-KEY-ONLY",
                AppSecret = "TEST-SECRET-ONLY"
            };
            var store = new ConfigStore(scratch);
            store.Save(config);
            var disk = File.ReadAllText(store.FilePath);
            Check(!disk.Contains("TEST-KEY-ONLY") && !disk.Contains("TEST-SECRET-ONLY") && disk.Contains("dpapi:v1:"), "DPAPI: no plaintext on disk");
            var loaded = store.Load();
            Check(loaded.Translation.Translator.ApiKey == "TEST-KEY-ONLY" && loaded.Translation.Translator.AppSecret == "TEST-SECRET-ONLY", "DPAPI: round trip");
            File.WriteAllText(store.FilePath, """{"translation":{"translator":{"apiKey":"LEGACY-TEST-KEY"}}}""");
            Check(store.Load().Translation.Translator.ApiKey == "LEGACY-TEST-KEY" &&
                !File.ReadAllText(store.FilePath).Contains("LEGACY-TEST-KEY"), "DPAPI: legacy migration");
            foreach (var path in new[] { @"..\outside.txt", @"C:\outside.txt", "file:stream" })
            {
                var rejected = false;
                try
                {
                    InstallationManifest.ResolveFile(scratch, path);
                }
                catch (InvalidOperationException) { rejected = true; }
                Check(rejected, "installer rejects " + path);
            }
            Check(InstallationManifest.ResolveFile(scratch, "models/test.onnx").StartsWith(scratch), "installer accepts nested payload");
        }
        finally { Directory.Delete(scratch, true); }

        var frame = MakeFrame(new Int32Rect(-100, -100, 32, 32));
        SelfWindowMask.Apply(frame, [new Int32Rect(-105, -105, 10, 10)]);
        Check(frame.Bgra[0] == 0 && frame.Bgra[(10 * 32 + 10) * 4] == 255, "mask clips negative-screen coordinates");
        var recognizer = new FakeRecognizer();
        var session = new AppSession(Path.Combine(Path.GetTempPath(), "gggt-session-" + Guid.NewGuid().ToString("N")));
        typeof(AppSession).GetProperty(nameof(AppSession.Recognizer))!.SetValue(session, recognizer);
        await session.StopAsync();
        Check(recognizer.Disposed, "session disposes OCR");

        await LatestWins();
        await TimeoutRecovery();
        await StreamTimeout();
        await Improvements.CheckApi();
        Console.WriteLine("Passed " + _passed + " regression checks.");
    }

    private static Frame MakeFrame(Int32Rect region) => new()
    {
        Bgra = Enumerable.Repeat((byte)255, region.Width * region.Height * 4).ToArray(),
        Width = region.Width,
        Height = region.Height,
        SourceRegion = region,
        CapturedAt = DateTimeOffset.Now,
    };

    private static TranslationPipeline Pipeline(FakeRecognizer recognizer, ITranslator translator) => new(new()
    {
        RegionProvider = () => new Int32Rect(0, 0, 32, 32),
        Capture = MakeFrame,
        Recognizer = recognizer,
        Translator = translator,
        Languages = () => new("en", "zh"),
        Options = new()
        {
            PollIntervalMs = 10,
            OcrScale = 1,
            TranslateOnlyOnChange = false,
            MaskOwnWindows = false,
            ScriptGuard = false,
            ErrorBackoffMs = 10
        },
    });

    private static async Task LatestWins()
    {
        var recognizer = new FakeRecognizer();
        var translator = new SlowTranslator();
        await using var pipeline = Pipeline(recognizer, translator);
        var results = new ConcurrentQueue<string>();
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        pipeline.Updated += update =>
        {
            if (update.Status == PipelineStatus.Translated)
            {
                results.Enqueue(update.Translation!);
                if (update.Translation == "second translation")
                    done.TrySetResult();
            }
        };
        pipeline.Start();
        await translator.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        recognizer.Text = "a completely different second sentence";
        await done.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await pipeline.StopAsync();
        Check(translator.Canceled && !results.Contains("first translation"), "pipeline cancels and discards stale translation");
        Check(translator.MaxConcurrent == 1, "pipeline has only one active translator");
    }

    private static async Task TimeoutRecovery()
    {
        var translator = new TimeoutTranslator();
        await using var pipeline = Pipeline(new FakeRecognizer(), translator);
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var errors = 0;
        pipeline.Updated += update =>
        {
            if (update.Status == PipelineStatus.Error)
                Interlocked.Increment(ref errors);
            if (update.Status == PipelineStatus.Translated)
                done.TrySetResult();
        };
        pipeline.Start();
        await done.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await pipeline.StopAsync();
        Check(errors > 0 && translator.Calls >= 2, "pipeline retries after network timeout");
    }

    private static async Task StreamTimeout()
    {
        using var http = new HttpClient(new StalledHandler());
        using var translator = new OpenAiCompatibleTranslator(
            new()
            {
                BaseUrl = "https://test.invalid/v1",
                Model = "test",
                TimeoutSeconds = 1
            }, http);
        var watch = Stopwatch.StartNew();
        var timedOut = false;
        try
        {
            await translator.TranslateAsync(new()
            {
                Text = "hello",
                From = "en",
                To = "zh"
            }, _ => { });
        }
        catch (OperationCanceledException) { timedOut = true; }
        Check(timedOut && watch.Elapsed < TimeSpan.FromSeconds(4), "stream deadline includes body reads");
    }
}

internal sealed class FakeRecognizer : ITextRecognizer, IDisposable
{
    public volatile string Text = "first sentence with enough letters";
    public bool Disposed;
    public string Id => "test";
    public string LanguageTag => "en";
    public Task<OcrResult> RecognizeAsync(Frame frame, CancellationToken cancellationToken = default) => Task.FromResult(new OcrResult
    {
        Lines = [new(Text, new Int32Rect(0, 0, 1, 1), [])],
        RecognizerId = Id,
        LanguageTag = LanguageTag,
        SourceWidth = frame.Width,
        SourceHeight = frame.Height,
        Duration = TimeSpan.Zero,
    });
    public void Dispose() => Disposed = true;
}

internal sealed class SlowTranslator : ITranslator
{
    public string Id => "slow";
    public bool RequiresNetwork => false;
    public readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public bool Canceled;
    public int MaxConcurrent;
    private int _active;
    public async Task<string> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken = default)
    {
        MaxConcurrent = Math.Max(MaxConcurrent, Interlocked.Increment(ref _active));
        try
        {
            if (request.Text.StartsWith("first"))
            {
                Started.TrySetResult();
                try
                {
                    await Task.Delay(3000, cancellationToken);
                }
                catch (OperationCanceledException) { Canceled = true; }
                // Deliberately emulate a provider that still returns after cancellation.
                return "first translation";
            }
            return "second translation";
        }
        finally { Interlocked.Decrement(ref _active); }
    }
}

internal sealed class TimeoutTranslator : ITranslator
{
    public string Id => "timeout";
    public bool RequiresNetwork => false;
    public int Calls;
    public Task<string> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken = default) =>
        Interlocked.Increment(ref Calls) == 1 ? Task.FromException<string>(new TaskCanceledException("simulated timeout")) : Task.FromResult("recovered");
}

internal sealed class StalledHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new StalledStream()) });
}

internal sealed class StalledStream : Stream
{
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => 0; set => throw new NotSupportedException();
    }
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        await Task.Delay(Timeout.Infinite, cancellationToken);
        return 0;
    }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override void Flush()
    {
    }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
