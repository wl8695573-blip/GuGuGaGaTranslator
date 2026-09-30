using System.Text;

namespace GuGuGaGaTranslator.Core.Text;

/// <summary>Text normalization and similarity. OCR of the same dialogue line drifts by a character or
/// two between frames, so the pipeline compares normalized forms and a similarity ratio instead of
/// raw strings — otherwise every flicker of recognition noise would buy a fresh translation.</summary>
public static class TextNormalizer
{
    /// <summary>Normalize text for comparison: fold width variants, drop all whitespace (Japanese and
    /// Chinese are not written with spaces), and lowercase.</summary>
    public static string ForComparison(string text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        var normalized = text.Normalize(NormalizationForm.FormKC);
        var builder = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            if (char.IsWhiteSpace(character))
                continue;
            builder.Append(char.ToLowerInvariant(character));
        }

        return builder.ToString();
    }

    /// <summary>A 0–1 similarity ratio between two strings, based on edit distance over their normalized forms.</summary>
    public static double Similarity(string left, string right)
    {
        var a = ForComparison(left);
        var b = ForComparison(right);

        if (a.Length == 0 && b.Length == 0)
            return 1;
        if (a.Length == 0 || b.Length == 0)
            return 0;
        if (string.Equals(a, b, StringComparison.Ordinal))
            return 1;

        var distance = Levenshtein(a, b);
        return 1.0 - (distance / (double)Math.Max(a.Length, b.Length));
    }

    /// <summary>Edit distance with a rolling row, so memory is proportional to the shorter string rather than the product of both.</summary>
    public static int Levenshtein(string left, string right)
    {
        if (left.Length == 0)
            return right.Length;
        if (right.Length == 0)
            return left.Length;

        // Iterate over the shorter string for the smaller row.
        if (left.Length > right.Length)
            (left, right) = (right, left);

        var previous = new int[left.Length + 1];
        var current = new int[left.Length + 1];
        for (var i = 0; i <= left.Length; i++)
            previous[i] = i;

        for (var j = 1; j <= right.Length; j++)
        {
            current[0] = j;
            for (var i = 1; i <= left.Length; i++)
            {
                var cost = left[i - 1] == right[j - 1] ? 0 : 1;
                current[i] = Math.Min(
                    Math.Min(current[i - 1] + 1, previous[i] + 1),
                    previous[i - 1] + cost);
            }

            (previous, current) = (current, previous);
        }

        return previous[left.Length];
    }

    /// <summary>Collapse runs of whitespace and full-width spaces into single spaces, for text that gets displayed rather than compared.</summary>
    public static string Tidy(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var builder = new StringBuilder(text.Length);
        var pendingSpace = false;
        foreach (var character in text.Normalize(NormalizationForm.FormKC))
        {
            if (char.IsWhiteSpace(character))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(character);
        }

        return builder.ToString();
    }
}
