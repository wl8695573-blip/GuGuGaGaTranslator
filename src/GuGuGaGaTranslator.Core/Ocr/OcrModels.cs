using System.Windows;

namespace GuGuGaGaTranslator.Core.Ocr;

/// <summary>One recognized word and where it sat in the image.</summary>
public sealed record OcrWord(string Text, Int32Rect Box);

/// <summary>One recognized line and where it sat in the image.</summary>
public sealed record OcrLine(string Text, Int32Rect Box, IReadOnlyList<OcrWord> Words);

/// <summary>The outcome of one recognition pass: the text, the geometry it came from, and the timings.</summary>
public sealed record OcrResult
{
    public required IReadOnlyList<OcrLine> Lines { get; init; }

    public required string RecognizerId { get; init; }

    public required string LanguageTag { get; init; }

    public required int SourceWidth { get; init; }

    public required int SourceHeight { get; init; }

    public required TimeSpan Duration { get; init; }

    public string Text => string.Join(Environment.NewLine, Lines.Select(line => line.Text));

    public bool IsEmpty => Lines.Count == 0 || string.IsNullOrWhiteSpace(Text);

    /// <summary>The union of all line rectangles, or null when there is no text: it separates "the box is empty" from "unreadable text".</summary>
    public Int32Rect? ContentBounds
    {
        get
        {
            if (Lines.Count == 0) return null;
            var left = Lines.Min(line => line.Box.X);
            var top = Lines.Min(line => line.Box.Y);
            var right = Lines.Max(line => line.Box.X + line.Box.Width);
            var bottom = Lines.Max(line => line.Box.Y + line.Box.Height);
            return new Int32Rect(left, top, right - left, bottom - top);
        }
    }
}
