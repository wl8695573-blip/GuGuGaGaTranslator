using System.Text.RegularExpressions;

namespace GuGuGaGaTranslator.Core.Translation;

/// <summary>仅选取原文实际出现的术语；拉丁词按词边界匹配。</summary>
public static class GlossarySelector
{
    public static IReadOnlyList<GlossaryEntry> Select(string text, IReadOnlyList<GlossaryEntry> glossary) =>
        glossary.Where(entry => Contains(text, entry.Source)).OrderByDescending(entry => entry.Source.Length).Take(80).ToArray();

    public static bool Contains(string text, string source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return false;
        if (source.All(character => character < 128) && source.Any(char.IsLetter))
            return Regex.IsMatch(text, @"(?<![\p{L}\p{N}'])" + Regex.Escape(source) + @"(?![\p{L}\p{N}'])",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        return text.Contains(source, StringComparison.OrdinalIgnoreCase);
    }
}
