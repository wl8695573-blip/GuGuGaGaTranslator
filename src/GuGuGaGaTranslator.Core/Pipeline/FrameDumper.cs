using System.IO;
using System.Text.Json;
using GuGuGaGaTranslator.Core.Capture;
using GuGuGaGaTranslator.Core.Imaging;
using GuGuGaGaTranslator.Core.Ocr;

namespace GuGuGaGaTranslator.Core.Pipeline;

/// <summary>Writes each recognized frame and its outcome to disk as a PNG plus a JSON record: the pipeline's evidence trail.</summary>
public sealed class FrameDumper
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly object _gate = new();
    private int _sequence;

    public FrameDumper(string directory, int keepLast = 200)
    {
        Directory = directory;
        KeepLast = Math.Max(1, keepLast);
        System.IO.Directory.CreateDirectory(Directory);
    }

    public string Directory { get;  }

    public int KeepLast { get;  }

    public int Written
    {
        get
        {
            lock (_gate)
                return _sequence;
        }
    }

    /// <summary>Write one recognized frame and its outcome; the returned path has no extension.</summary>
    public string Dump(
        Frame frame,
        FrameSignature signature,
        OcrResult? ocr,
        string sourceText,
        string? translation,
        int signatureDistance)
    {
        lock (_gate)
        {
            _sequence++;
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
            var baseName = $"{_sequence:0000}-{stamp}";
            var basePath = Path.Combine(Directory, baseName);

            ImageOps.SavePng(frame, basePath + ".png");

            var record = new
            {
                sequence = _sequence,
                at = DateTimeOffset.Now.ToString("O"),
                frame = new
                {
                    width = frame.Width,
                    height = frame.Height,
                    region = new
                    {
                        x = frame.SourceRegion.X,
                        y = frame.SourceRegion.Y,
                        width = frame.SourceRegion.Width,
                        height = frame.SourceRegion.Height,
                    },
                },
                signature = new
                {
                    rows = signature.Rows,
                    rows2 = signature.Rows2,
                    rows3 = signature.Rows3,
                    rows4 = signature.Rows4,
                    distanceFromPrevious = signatureDistance,
                },
                ocr = ocr is null ? null : new
                {
                    recognizer = ocr.RecognizerId,
                    language = ocr.LanguageTag,
                    durationMs = Math.Round(ocr.Duration.TotalMilliseconds, 1),
                    lineCount = ocr.Lines.Count,
                    text = ocr.Text,
                    lines = ocr.Lines.Select(line => new
                    {
                        line.Text,
                        box = new
                        {
                            x = line.Box.X,
                            y = line.Box.Y,
                            width = line.Box.Width,
                            height = line.Box.Height
                        },
                    }),
                },
                sourceText,
                translation,
                png = baseName + ".png",
            };

            File.WriteAllText(basePath + ".json", JsonSerializer.Serialize(record, Json));
            Prune();
            return basePath;
        }
    }

    private void Prune()
    {
        var frames = System.IO.Directory.GetFiles(Directory, "*.png");
        if (frames.Length <= KeepLast)
            return;

        Array.Sort(frames, StringComparer.Ordinal);
        foreach (var stale in frames.Take(frames.Length - KeepLast))
        {
            TryDelete(stale);
            TryDelete(Path.ChangeExtension(stale, ".json"));
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            foreach (var file in System.IO.Directory.GetFiles(Directory))
            {
                TryDelete(file);
            }

            _sequence = 0;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // A file held open by a viewer is not worth failing a translation for.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
