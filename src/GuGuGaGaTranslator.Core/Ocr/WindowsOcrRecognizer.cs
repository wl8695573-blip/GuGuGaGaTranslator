using System.Runtime.Versioning;
using System.Windows;
using GuGuGaGaTranslator.Core.Capture;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Security.Cryptography;

namespace GuGuGaGaTranslator.Core.Ocr;

/// <summary>The recognizer built into Windows. It costs nothing to ship, but each language must be
/// enabled as an optional Windows language feature — Japanese in particular is absent on a default install.</summary>
public sealed class WindowsOcrRecognizer : ITextRecognizer
{
    private readonly OcrEngine _engine;

    private WindowsOcrRecognizer(OcrEngine engine)
    {
        _engine = engine;
        LanguageTag = engine.RecognizerLanguage.LanguageTag;
        Id = $"windows-ocr:{LanguageTag}";
    }

    /// <inheritdoc />
    public string Id { get; }

    /// <inheritdoc />
    public string LanguageTag { get; }

    public static uint MaxImageDimension => OcrEngine.MaxImageDimension;

    /// <summary>The language tags this machine can actually recognize; anything missing is an optional Windows language feature.</summary>
    [SupportedOSPlatform("windows10.0.19041.0")]
    public static IReadOnlyList<string> AvailableLanguages =>
        OcrEngine.AvailableRecognizerLanguages.Select(language => language.LanguageTag).ToArray();

    /// <summary>Create a recognizer for one language, or null when that language's OCR feature is not installed.</summary>
    [SupportedOSPlatform("windows10.0.19041.0")]
    public static WindowsOcrRecognizer? TryCreate(string languageTag)
    {
        // An exact tag can fail where its primary language succeeds, so both are tried.
        var candidates = new List<string> { languageTag };
        var separator = languageTag.IndexOf('-');
        if (separator > 0) candidates.Add(languageTag[..separator]);

        foreach (var candidate in candidates)
        {
            var engine = OcrEngine.TryCreateFromLanguage(new Language(candidate));
            if (engine is not null) return new WindowsOcrRecognizer(engine);
        }

        return null;
    }

    /// <summary>Pick the recognizer for a language, falling back to the given preference order so a machine missing one language still starts.</summary>
    [SupportedOSPlatform("windows10.0.19041.0")]
    public static WindowsOcrRecognizer? TryCreateWithFallback(string preferred, params string[] fallbacks)
    {
        foreach (var tag in new[] { preferred }.Concat(fallbacks))
        {
            var recognizer = TryCreate(tag);
            if (recognizer is not null) return recognizer;
        }

        return null;
    }

    /// <inheritdoc />
    [SupportedOSPlatform("windows10.0.19041.0")]
    public async Task<OcrResult> RecognizeAsync(Frame frame, CancellationToken cancellationToken = default)
    {
        var started = DateTimeOffset.Now;
        var buffer = CryptographicBuffer.CreateFromByteArray(frame.Bgra);
        using var bitmap = SoftwareBitmap.CreateCopyFromBuffer(
            buffer,
            BitmapPixelFormat.Bgra8,
            frame.Width,
            frame.Height,
            BitmapAlphaMode.Premultiplied);

        var result = await _engine.RecognizeAsync(bitmap).AsTask(cancellationToken).ConfigureAwait(false);

        var lines = result.Lines
            .Select(line =>
            {
                var words = line.Words
                    .Select(word => new OcrWord(word.Text, ToRect(word.BoundingRect, frame)))
                    .ToArray();
                // OcrLine carries no rectangle of its own: the line box is the union of its words.
                return new OcrLine(JoinLine(words, LanguageTag), Union(words), words);
            })
            .ToArray();

        return new OcrResult
        {
            Lines = lines,
            RecognizerId = Id,
            LanguageTag = LanguageTag,
            SourceWidth = frame.Width,
            SourceHeight = frame.Height,
            Duration = DateTimeOffset.Now - started,
        };
    }

    /// <summary>Join a line's words the way its script is written: Windows OCR segments Japanese and
    /// Chinese into words, and re-joining those with spaces would inject gaps that are not in the source.</summary>
    internal static string JoinLine(IReadOnlyList<OcrWord> words, string languageTag)
    {
        var separator = IsSpaceDelimited(languageTag) ? " " : string.Empty;
        return string.Join(separator, words.Select(word => word.Text));
    }

    private static bool IsSpaceDelimited(string languageTag) =>
        !languageTag.StartsWith("ja", StringComparison.OrdinalIgnoreCase)
        && !languageTag.StartsWith("zh", StringComparison.OrdinalIgnoreCase);

    private static Int32Rect Union(IReadOnlyList<OcrWord> words)
    {
        if (words.Count == 0) return new Int32Rect(0, 0, 0, 0);

        var left = words.Min(word => word.Box.X);
        var top = words.Min(word => word.Box.Y);
        var right = words.Max(word => word.Box.X + word.Box.Width);
        var bottom = words.Max(word => word.Box.Y + word.Box.Height);
        return new Int32Rect(left, top, right - left, bottom - top);
    }

    /// <summary>Convert a WinRT rectangle to an integer one, clamped to the image. Geometry is reported
    /// in the pixels of the frame that was recognized, so a caller that upscaled divides by its own scale.</summary>
    private static Int32Rect ToRect(Windows.Foundation.Rect rect, Frame frame)
    {
        var x = Math.Clamp((int)Math.Round(rect.X), 0, Math.Max(0, frame.Width - 1));
        var y = Math.Clamp((int)Math.Round(rect.Y), 0, Math.Max(0, frame.Height - 1));
        var width = Math.Clamp((int)Math.Round(rect.Width), 0, frame.Width - x);
        var height = Math.Clamp((int)Math.Round(rect.Height), 0, frame.Height - y);
        return new Int32Rect(x, y, width, height);
    }
}
