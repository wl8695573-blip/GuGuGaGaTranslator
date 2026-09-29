using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Windows;
using GuGuGaGaTranslator.Core.Capture;
using GuGuGaGaTranslator.Core.Ocr;
using RapidOcrNet;
using SkiaSharp;
// RapidOcrNet also defines an OcrResult; alias the core's so both stay usable in one file.
using CoreOcrResult = GuGuGaGaTranslator.Core.Ocr.OcrResult;

namespace GuGuGaGaTranslator.Ocr.Rapid;

/// <summary>Tuning for the offline recognizer; defaults are the model's own recommendations, and the
/// resizing knobs exist because the PP-OCRv6 preset upscales the short side to 736 px.</summary>
public sealed record RapidOcrSettings
{
    public string? ModelDirectory { get; init; }

    public bool UseGpu { get; init; }

    /// <summary>Short-side target for the detector's adaptive resize; lower is faster and loses small
    /// text first, 0 keeps the preset's 736.</summary>
    public int LimitSideLen { get; init; }

    /// <summary>Hard cap on the longer side; 0 keeps the preset's adaptive behaviour.</summary>
    public int ImgResize { get; init; }

    /// <summary>Whether to run the 0°/180° classifier; off is safe for upright text and saves a forward pass.</summary>
    public bool DoAngle { get; init; } = true;

    public bool ReturnWordBox { get; init; }
}

/// <summary>An offline recognizer over PaddleOCR ONNX models, driven through RapidOcrNet; the bundled
/// multilingual model avoids the optional, administrator-installed Windows Japanese OCR feature.</summary>
public sealed class RapidOcrRecognizer : ITextRecognizer, IDisposable
{
    private readonly RapidOcr _ocr;
    private readonly RapidOcrOptions _options;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    private RapidOcrRecognizer(RapidOcr ocr, RapidOcrOptions options, string modelDirectory)
    {
        _ocr = ocr;
        _options = options;
        ModelDirectory = modelDirectory;
    }

    /// <inheritdoc />
    public string Id => "rapidocr:pp-ocrv6-small";

    /// <inheritdoc />
    public string LanguageTag => "multi";

    public string ModelDirectory { get; }

    /// <summary>The model files one recognizer needs, all inside one directory.</summary>
    public readonly record struct ModelFiles(
        string Directory,
        string Detector,
        string Classifier,
        string Recognizer,
        string Dictionary)
    {
        public bool IsComplete =>
            File.Exists(Detector) && File.Exists(Classifier) && File.Exists(Recognizer) && File.Exists(Dictionary);

        public string Missing()
        {
            var missing = new List<string>();
            if (!File.Exists(Detector)) missing.Add(Path.GetFileName(Detector));
            if (!File.Exists(Classifier)) missing.Add(Path.GetFileName(Classifier));
            if (!File.Exists(Recognizer)) missing.Add(Path.GetFileName(Recognizer));
            if (!File.Exists(Dictionary)) missing.Add(Path.GetFileName(Dictionary));
            return string.Join(", ", missing);
        }
    }

    /// <summary>The expected model file names; the classifier comes from the library's bundled v5
    /// set, the rest from PP-OCRv6.</summary>
    public static readonly string[] ExpectedFileNames =
    [
        "PP-OCRv6_det_small.onnx",
        "ch_PP-LCNet_x0_25_textline_ori_cls_mobile.onnx",
        "PP-OCRv6_rec_small.onnx",
        "ppocrv6_small_dict.txt",
    ];

    public static ModelFiles ResolveModels(string directory) => new(
        directory,
        Path.Combine(directory, ExpectedFileNames[0]),
        Path.Combine(directory, ExpectedFileNames[1]),
        Path.Combine(directory, ExpectedFileNames[2]),
        Path.Combine(directory, ExpectedFileNames[3]));

    public static string DefaultModelDirectory()
    {
        var baseDirectory = AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(baseDirectory, "models", "v6"),
            Path.GetFullPath(Path.Combine(baseDirectory, "..", "..", "..", "..", "..", "models", "v6")),
        };

        foreach (var candidate in candidates)
        {
            if (ResolveModels(candidate).IsComplete) return candidate;
        }

        return candidates[0];
    }

    [SupportedOSPlatform("windows")]
    public static RapidOcrRecognizer Create(RapidOcrSettings settings)
    {
        var directory = settings.ModelDirectory ?? DefaultModelDirectory();
        var files = ResolveModels(directory);
        if (!files.IsComplete)
        {
            throw new InvalidOperationException(
                $"RapidOCR 模型不完整,{directory} 缺少:{files.Missing()}。"
                + $"需要的文件是 {string.Join("、", ExpectedFileNames)}。");
        }

        var sessionOptions = RapidOcr.GetDefaultSessionOptions();
        if (settings.UseGpu)
        {
            try
            {
                sessionOptions.AppendExecutionProvider_CUDA();
            }
            catch (Exception)
            {
                // The CPU build of ONNX Runtime carries no CUDA provider, so this is the normal path.
                sessionOptions.AppendExecutionProvider_CPU();
            }
        }

        var ocr = new RapidOcr();
        // Absolute paths keep the recognizer independent of the working directory and of the NuGet
        // package's own copy of the classifier.
        ocr.InitModels(files.Detector, files.Classifier, files.Recognizer, files.Dictionary, sessionOptions);

        // PP-OCRv6 wants the short-side adaptive preprocessing; the v5 default's 1024 long-side cap
        // starves these models of resolution.
        var options = RapidOcrOptions.PPOCRv6 with
        {
            DoAngle = settings.DoAngle,
            ReturnWordBox = settings.ReturnWordBox,
        };
        if (settings.LimitSideLen > 0) options = options with { LimitSideLen = settings.LimitSideLen };
        if (settings.ImgResize > 0) options = options with { ImgResize = settings.ImgResize };

        return new RapidOcrRecognizer(ocr, options, directory);
    }

    [SupportedOSPlatform("windows")]
    public static RapidOcrRecognizer Create(string modelDirectory, bool useGpu = false) =>
        Create(new RapidOcrSettings { ModelDirectory = modelDirectory, UseGpu = useGpu });

    /// <inheritdoc />
    [SupportedOSPlatform("windows")]
    public async Task<CoreOcrResult> RecognizeAsync(Frame frame, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // One RapidOcr instance owns its ONNX sessions; serializing calls stops two recognitions
        // racing on the same buffers.
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var started = DateTimeOffset.Now;
        try
        {
            using var bitmap = ToBitmap(frame);
            var rapid = await _ocr
                .DetectAsync(bitmap, _options, progress: null, cancellationToken)
                .ConfigureAwait(false);

            var lines = rapid.TextBlocks
                .Where(block => !string.IsNullOrWhiteSpace(block.Text))
                .Select(block => new OcrLine(
                    block.Text,
                    Bounds(block.BoxPoints, frame),
                    [new OcrWord(block.Text, Bounds(block.BoxPoints, frame))]))
                .ToArray();

            return new CoreOcrResult
            {
                Lines = lines,
                RecognizerId = Id,
                LanguageTag = LanguageTag,
                SourceWidth = frame.Width,
                SourceHeight = frame.Height,
                Duration = DateTimeOffset.Now - started,
            };
        }
        finally
        {
            _gate.Release();
        }
    }

    private static SKBitmap ToBitmap(Frame frame)
    {
        var info = new SKImageInfo(frame.Width, frame.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        var bitmap = new SKBitmap(info);
        Marshal.Copy(frame.Bgra, 0, bitmap.GetPixels(), frame.Bgra.Length);
        return bitmap;
    }

    /// <summary>The axis-aligned box enclosing a detected polygon, clamped to the image.</summary>
    private static Int32Rect Bounds(SKPointI[]? points, Frame frame)
    {
        if (points is not { Length: > 0 }) return new Int32Rect(0, 0, 0, 0);

        var left = points.Min(point => point.X);
        var top = points.Min(point => point.Y);
        var right = points.Max(point => point.X);
        var bottom = points.Max(point => point.Y);

        var x = Math.Clamp(left, 0, Math.Max(0, frame.Width - 1));
        var y = Math.Clamp(top, 0, Math.Max(0, frame.Height - 1));
        var width = Math.Clamp(right - x, 0, frame.Width - x);
        var height = Math.Clamp(bottom - y, 0, frame.Height - y);
        return new Int32Rect(x, y, width, height);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _ocr.Dispose();
        _gate.Dispose();
    }
}
