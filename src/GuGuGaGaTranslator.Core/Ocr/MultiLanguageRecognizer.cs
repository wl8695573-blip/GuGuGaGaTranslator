using System.Runtime.Versioning;
using GuGuGaGaTranslator.Core.Capture;

namespace GuGuGaGaTranslator.Core.Ocr;

/// <summary>Recognition without being told the language: run every usable installed recognizer over the
/// same frame and keep the reading that looks most like real text. Deliberately a fallback rather than
/// the default, since a wrong-language recognizer still returns plausible characters.</summary>
public sealed class MultiLanguageRecognizer : ITextRecognizer
{
    private readonly IReadOnlyList<ITextRecognizer> _candidates;

    private MultiLanguageRecognizer(IReadOnlyList<ITextRecognizer> candidates)
    {
        _candidates = candidates;
    }

    /// <inheritdoc />
    public string Id => $"windows-ocr:auto({string.Join(",", _candidates.Select(candidate => candidate.LanguageTag))})";

    /// <inheritdoc />
    public string LanguageTag => "auto";

    public IReadOnlyList<ITextRecognizer> Candidates => _candidates;

    /// <summary>Build an automatic recognizer over the installed languages, in the given preference order.</summary>
    [SupportedOSPlatform("windows10.0.19041.0")]
    public static MultiLanguageRecognizer? TryCreate(IEnumerable<string> preferred, int maxCandidates = 3)
    {
        var installed = WindowsOcrRecognizer.AvailableLanguages;
        var candidates = new List<ITextRecognizer>();

        foreach (var tag in preferred)
        {
            // A preference may name a language rather than an exact tag ("en" for "en-US").
            var match = installed.FirstOrDefault(installedTag =>
                installedTag.Equals(tag, StringComparison.OrdinalIgnoreCase)
                || installedTag.StartsWith($"{tag}-", StringComparison.OrdinalIgnoreCase));

            if (match is null) continue;
            if (candidates.Any(candidate => candidate.LanguageTag.Equals(match, StringComparison.OrdinalIgnoreCase))) continue;

            var recognizer = WindowsOcrRecognizer.TryCreate(match);
            if (recognizer is not null) candidates.Add(recognizer);
            if (candidates.Count >= Math.Max(1, maxCandidates)) break;
        }

        if (candidates.Count == 0) return null;
        // With one engine there is nothing to choose between, so the plain recognizer is used instead.
        return candidates.Count == 1 ? null : new MultiLanguageRecognizer(candidates);
    }

    /// <summary>The single recognizer an automatic choice would fall back to, for a one-engine machine.</summary>
    [SupportedOSPlatform("windows10.0.19041.0")]
    public static WindowsOcrRecognizer? TryCreateSingle(IEnumerable<string> preferred) =>
        preferred.Select(WindowsOcrRecognizer.TryCreate).FirstOrDefault(recognizer => recognizer is not null);

    /// <inheritdoc />
    [SupportedOSPlatform("windows10.0.19041.0")]
    public async Task<OcrResult> RecognizeAsync(Frame frame, CancellationToken cancellationToken = default)
    {
        OcrResult? best = null;
        var bestScore = int.MinValue;

        foreach (var candidate in _candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await candidate.RecognizeAsync(frame, cancellationToken).ConfigureAwait(false);
            var score = Score(result.Text);

            // Strictly greater keeps the earlier preference on a tie, which is the point of the ordering.
            if (score > bestScore)
            {
                bestScore = score;
                best = result;
            }
        }

        if (best is null) throw new InvalidOperationException("automatic recognition has no candidate recognizer");

        return new OcrResult
        {
            Lines = best.Lines,
            RecognizerId = $"{Id}:{best.LanguageTag}",
            LanguageTag = best.LanguageTag,
            SourceWidth = best.SourceWidth,
            SourceHeight = best.SourceHeight,
            Duration = best.Duration,
        };
    }

    /// <summary>Score a reading by how much of it is real text: letters and digits count positively, anything
    /// else that is not ordinary punctuation is treated as recognition noise.</summary>
    internal static int Score(string text)
    {
        var meaningful = 0;
        var noise = 0;

        foreach (var character in text)
        {
            if (char.IsLetterOrDigit(character)) meaningful++;
            else if (char.IsWhiteSpace(character) || char.IsPunctuation(character) || char.IsSymbol(character)) continue;
            else noise++;
        }

        return (meaningful * 2) - (noise * 3);
    }
}
