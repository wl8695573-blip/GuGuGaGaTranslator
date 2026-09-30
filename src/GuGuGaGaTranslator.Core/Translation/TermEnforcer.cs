using System.Text;

namespace GuGuGaGaTranslator.Core.Translation;

/// <summary>One substitution the enforcer made, for the status line and the logs.</summary>
public sealed record TermFix(string From, string To, string Reason);

/// <summary>Rewrites a finished translation so the terms in the glossary are the terms that appear, whatever the model
/// decided; longest variants are applied first, so a rule for 「N公司新九人会」 cannot be half-eaten by one for 「新九人会」.</summary>
public static class TermEnforcer
{
    /// <summary>Apply a glossary to a translation.</summary>
    public static (string Text, IReadOnlyList<TermFix> Fixes) Apply(
        string translation,
        IReadOnlyList<GlossaryEntry> glossary)
    {
        if (string.IsNullOrEmpty(translation) || glossary.Count == 0)
            return (translation, []);

        var text = translation;
        var fixes = new List<TermFix>();
        var deleted = false;

        // Forbidden wordings, longest first, collected up front so the ordering is global rather than per entry.
        var forbidden = glossary
            .SelectMany(entry => (entry.Forbidden ?? []).Select(variant => (Variant: variant, entry.Target)))
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Variant) && pair.Variant.Length >= 2)
            .OrderByDescending(pair => pair.Variant.Length)
            .ToList();

        foreach (var (variant, target) in forbidden)
        {
            if (!text.Contains(variant, StringComparison.OrdinalIgnoreCase))
                continue;

            // When the required form is already somewhere in the line, the wrong one is a duplicate, so it is dropped.
            var required = text.Contains(target, StringComparison.OrdinalIgnoreCase);
            text = Replace(text, variant, required ? string.Empty : target);
            deleted |= required;
            fixes.Add(new TermFix(variant, required ? "(删除重复)" : target, "禁用译法"));
        }

        // A source term the model left alone.
        foreach (var entry in glossary)
        {
            if (string.IsNullOrWhiteSpace(entry.Source) || string.IsNullOrWhiteSpace(entry.Target))
                continue;
            if (entry.Source.Equals(entry.Target, StringComparison.OrdinalIgnoreCase))
                continue;
            if (entry.Source.Trim().Length < 2)
                continue;
            if (!text.Contains(entry.Source, StringComparison.OrdinalIgnoreCase))
                continue;

            var replaced = Replace(text, entry.Source, entry.Target, out var count);
            if (count == 0)
                continue;
            text = replaced;
            fixes.Add(new TermFix(entry.Source, entry.Target, "原文未译"));
        }

        // Tidying runs only when something was actually deleted: it exists to clean up the wreckage of a deletion, not
        // to restyle the model's punctuation.
        return (deleted ? Tidy(text) : text, fixes);
    }

    /// <summary>A short, stable fingerprint of a glossary, for cache keys: an FNV-1a hash over the sorted entries, because
    /// <see cref="string.GetHashCode()"/> is not stable across processes.</summary>
    public static string Fingerprint(IReadOnlyList<GlossaryEntry> glossary)
    {
        if (glossary.Count == 0)
            return string.Empty;

        var parts = glossary
            .Select(entry => $"{entry.Source}={entry.Target}|{string.Join(",", entry.Forbidden ?? [])}")
            .OrderBy(part => part, StringComparer.Ordinal)
            .ToArray();

        unchecked
        {
            const ulong offset = 14695981039346656037;
            const ulong prime = 1099511628211;
            var hash = offset;
            foreach (var character in string.Join("\n", parts))
            {
                hash ^= character;
                hash *= prime;
            }

            return hash.ToString("x16");
        }
    }

    /// <summary>Replace every occurrence of a word. Latin words are matched whole, so a rule for 「N Corp」 cannot rewrite
    /// the inside of 「N Corporation」; CJK text has no word boundaries, so it is matched as a substring.</summary>
    private static string Replace(string text, string word, string replacement) => Replace(text, word, replacement, out _);

    private static string Replace(string text, string word, string replacement, out int count)
    {
        count = 0;
        if (word.Length == 0)
            return text;
        if (!NeedsWordBoundaries(word))
        {
            count = CountOccurrences(text, word);
            return count == 0 ? text : text.Replace(word, replacement, StringComparison.OrdinalIgnoreCase);
        }

        var builder = new StringBuilder(text.Length);
        var index = 0;
        while (index < text.Length)
        {
            var found = text.IndexOf(word, index, StringComparison.OrdinalIgnoreCase);
            if (found < 0)
            {
                builder.Append(text, index, text.Length - index);
                break;
            }

            var before = found == 0 ? '\0' : text[found - 1];
            var afterIndex = found + word.Length;
            var after = afterIndex >= text.Length ? '\0' : text[afterIndex];

            if (IsWordCharacter(before) || IsWordCharacter(after))
            {
                // Part of a longer word: keep it and continue past the match.
                builder.Append(text, index, afterIndex - index);
                index = afterIndex;
                continue;
            }

            builder.Append(text, index, found - index).Append(replacement);
            count++;
            index = afterIndex;
        }

        return builder.ToString();
    }

    /// <summary>Whether a term is written in Latin letters, and so needs whole-word matching.</summary>
    private static bool NeedsWordBoundaries(string word) =>
        word.Any(character => char.IsLetter(character)) && word.All(character => character < 128);

    private static int CountOccurrences(string text, string word)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(word, index, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            count++;
            index += word.Length;
        }

        return count;
    }

    private static bool IsWordCharacter(char character) => char.IsLetterOrDigit(character) || character == '\'';

    /// <summary>Clean up what a deletion leaves behind: an emptied bracket pair, doubled punctuation, stray spaces.</summary>
    private static string Tidy(string text)
    {
        var result = text;
        foreach (var pair in new[] { "（）", "()", "「」", "『』", "【】", "[]", "〈〉", "《》" })
        {
            result = result.Replace(pair, string.Empty, StringComparison.Ordinal);
        }

        foreach (var pair in new[] { "，，", "。。", "、、", "：：", ",,", "  " })
        {
            while (result.Contains(pair, StringComparison.Ordinal))
            {
                result = result.Replace(pair, pair[..1], StringComparison.Ordinal);
            }
        }

        return result.Trim();
    }
}
