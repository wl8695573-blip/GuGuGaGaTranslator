using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GuGuGaGaTranslator.Core.Capture;
using GuGuGaGaTranslator.Core.Ocr;
using GuGuGaGaTranslator.Ocr.Rapid;

internal static class Program
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private sealed record Fixture(string File, string Text, string Language, int DpiPercent, string Appearance, double MaximumCer);
    private sealed record Result(string File, string Status, string? Actual, double? Cer, double? MedianMs, double? P95Ms, double MaximumCer);

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            return Run(args).GetAwaiter().GetResult();
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    private static string Option(string[] args, string name, string fallback)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : fallback;
    }

    private static async Task<int> Run(string[] args)
    {
        var directory = Path.GetFullPath(Option(args, "--fixtures", "tools/Benchmark/fixtures"));
        if (args.Contains("--generate"))
        {
            Generate(directory);
            return 0;
        }
        var engine = Option(args, "--engine", "rapid");
        if (engine is not ("rapid" or "windows"))
            throw new ArgumentException("--engine rapid|windows");
        var iterations = Math.Clamp(int.Parse(Option(args, "--iterations", "3"), CultureInfo.InvariantCulture), 1, 100);
        var output = Path.GetFullPath(Option(args, "--output", ".artifacts/benchmark-" + engine));
        Directory.CreateDirectory(output);
        var fixtures = JsonSerializer.Deserialize<List<Fixture>>(File.ReadAllText(Path.Combine(directory, "manifest.json")), Json)
            ?? throw new InvalidDataException("Empty corpus");
        var results = new List<Result>();
        var initialization = Stopwatch.StartNew();
        using var rapid = engine == "rapid" ? RapidOcrRecognizer.Create(new RapidOcrSettings
        {
            ModelDirectory = Option(args, "--models", "models/v6"),
            LimitSideLen = 320
        }) : null;
        initialization.Stop();
        foreach (var fixture in fixtures)
        {
            // Keep optional user-provided corpora inside their selected root.
            var path = Path.GetFullPath(Path.Combine(directory, fixture.File));
            if (!path.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Fixture path escapes corpus");
            ITextRecognizer? recognizer = rapid is not null ? rapid : WindowsOcrRecognizer.TryCreate(fixture.Language);
            if (recognizer is null)
            {
                results.Add(new(fixture.File, "SKIP: language pack unavailable", null, null, null, null, fixture.MaximumCer));
                continue;
            }
            var frame = ReadFrame(path);
            using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            await recognizer.RecognizeAsync(frame, deadline.Token); // Per-case warmup, excluded from timings.
            var durations = new List<double>();
            var actual = "";
            var worstCer = 0d;
            for (var iteration = 0; iteration < iterations; iteration++)
            {
                var watch = Stopwatch.StartNew();
                var recognized = await recognizer.RecognizeAsync(frame, deadline.Token);
                watch.Stop();
                durations.Add(watch.Elapsed.TotalMilliseconds);
                actual = recognized.Text;
                worstCer = Math.Max(worstCer, Cer(fixture.Text, actual));
            }
            durations.Sort();
            var status = worstCer <= fixture.MaximumCer ? "PASS" : "FAIL";
            results.Add(new(fixture.File, status, actual, worstCer,
                durations[(durations.Count - 1) / 2], durations[(int)Math.Ceiling(durations.Count * .95) - 1], fixture.MaximumCer));
            Console.WriteLine($"{status} {fixture.File} CER={worstCer:P1} median={durations[(durations.Count - 1) / 2]:F0}ms");
        }
        File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            engine,
            iterations,
            initializationMs = initialization.Elapsed.TotalMilliseconds,
            os = Environment.OSVersion.VersionString,
            runtime = Environment.Version.ToString(),
            processorCount = Environment.ProcessorCount,
            limitations = "Synthetic dialogue, not a real-game compatibility claim. Warmups and model initialization excluded from median/P95. Whitespace ignored; NFKC Unicode normalized. Missing Windows languages are skipped.",
            results,
        }, Json));
        var report = new StringBuilder($"# OCR benchmark ({engine})\n\nSynthetic dialogue only; not a real-game compatibility claim. {iterations} measured runs per case, per-case warmup excluded. Model initialization: {initialization.Elapsed.TotalMilliseconds:F0} ms.\n\nCER uses Unicode characters after NFKC normalization and removal of whitespace.\n\n| Fixture | Status | CER | Median ms | P95 ms |\n|---|---|---:|---:|---:|\n");
        foreach (var result in results)
            report.AppendLine($"| {result.File} | {result.Status} | {result.Cer:P1} | {result.MedianMs:F0} | {result.P95Ms:F0} |");
        File.WriteAllText(Path.Combine(output, "results.md"), report.ToString());
        return args.Contains("--check") && (results.Any(result => result.Status == "FAIL") || results.All(result => result.Cer is null)) ? 1 : 0;
    }

    private static Frame ReadFrame(string path)
    {
        var decoder = BitmapDecoder.Create(new Uri(path), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var bitmap = new FormatConvertedBitmap(decoder.Frames[0], PixelFormats.Bgra32, null, 0);
        var pixels = new byte[checked(bitmap.PixelWidth * bitmap.PixelHeight * 4)];
        bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
        return new Frame
        {
            Bgra = pixels,
            Width = bitmap.PixelWidth,
            Height = bitmap.PixelHeight,
            SourceRegion = new Int32Rect(0, 0, bitmap.PixelWidth, bitmap.PixelHeight),
            CapturedAt = DateTimeOffset.UtcNow
        };
    }

    internal static double Cer(string expected, string actual)
    {
        static Rune[] Normalize(string value) => value.Normalize(NormalizationForm.FormKC).EnumerateRunes().Where(rune => !Rune.IsWhiteSpace(rune)).ToArray();
        var left = Normalize(expected);
        var right = Normalize(actual);
        var previous = Enumerable.Range(0, right.Length + 1).ToArray();
        for (var i = 1; i <= left.Length; i++)
        {
            var current = new int[right.Length + 1];
            current[0] = i;
            for (var j = 1; j <= right.Length; j++)
                current[j] = Math.Min(Math.Min(previous[j] + 1, current[j - 1] + 1), previous[j - 1] + (left[i - 1] == right[j - 1] ? 0 : 1));
            previous = current;
        }
        return previous[right.Length] / (double)Math.Max(1, left.Length);
    }

    private static void Generate(string directory)
    {
        Directory.CreateDirectory(directory);
        var fixtures = new List<Fixture>();
        foreach (var (language, text) in new[] { ("en-US", "We will meet again tomorrow."), ("ja", "明日もここで会いましょう。"), ("zh-Hans-CN", "明天我们还会在这里见面。") })
            foreach (var dpi in new[] { 100, 125, 150 })
                foreach (var appearance in new[] { "dark", "light", "low-contrast" })
                {
                    var visual = new DrawingVisual();
                    using (var drawing = visual.RenderOpen())
                    {
                        var light = appearance == "light";
                        drawing.DrawRectangle(new SolidColorBrush(light ? Color.FromRgb(242, 244, 249) : Color.FromRgb(20, 27, 48)), null, new Rect(0, 0, 720, 120));
                        var brush = new SolidColorBrush(light ? Color.FromRgb(20, 27, 48) : appearance == "low-contrast" ? Color.FromRgb(110, 123, 146) : Colors.White);
                        var formatted = new FormattedText(text, CultureInfo.GetCultureInfo(language), FlowDirection.LeftToRight,
                            new Typeface("Microsoft YaHei UI"), 28, brush, dpi / 100d);
                        drawing.DrawText(formatted, new Point(24, 40));
                    }
                    var bitmap = new RenderTargetBitmap(720 * dpi / 100, 120 * dpi / 100, 96d * dpi / 100, 96d * dpi / 100, PixelFormats.Pbgra32);
                    bitmap.Render(visual);
                    var filename = $"{language}-{dpi}-{appearance}.png";
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using (var stream = File.Create(Path.Combine(directory, filename)))
                        encoder.Save(stream);
                    fixtures.Add(new(filename, text, language, dpi, appearance, .15));
                }
        File.WriteAllText(Path.Combine(directory, "manifest.json"), JsonSerializer.Serialize(fixtures, Json));
        Console.WriteLine($"Generated {fixtures.Count} reproducible fixtures.");
    }
}
