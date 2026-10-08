namespace GuGuGaGaTranslator.Core.Translation;

/// <summary>公共词库升级时保留用户修改、禁用和删除的条目。</summary>
public static class TermLibraryMerge
{
    private static string Key(GameTerm term) => (GameProfiles.LanguageOf(term.Language) ?? "any") + "|" + term.Source.Trim();

    public static List<GameTerm> Merge(GameProfile previous, GameProfile incoming, GameProfile local)
    {
        var old = previous.Terms.ToDictionary(Key, StringComparer.OrdinalIgnoreCase);
        var current = local.Terms.ToDictionary(Key, StringComparer.OrdinalIgnoreCase);
        var merged = GameProfileArchive.Clone(incoming).Terms.ToDictionary(Key, StringComparer.OrdinalIgnoreCase);
        foreach (var key in old.Keys.Where(key => !current.ContainsKey(key))) merged.Remove(key);
        foreach (var term in local.Terms)
        {
            var key = Key(term);
            if (!old.TryGetValue(key, out var baseline) || Changed(term, baseline)) merged[key] = term;
        }
        return merged.Values.ToList();
    }

    private static bool Changed(GameTerm term, GameTerm baseline) =>
        term.Target != baseline.Target || term.Enabled != baseline.Enabled
        || !term.Forbidden.SequenceEqual(baseline.Forbidden, StringComparer.OrdinalIgnoreCase)
        || term.Note != baseline.Note || term.SourceUrl != baseline.SourceUrl
        || !string.IsNullOrWhiteSpace(term.ReviewStatus) && term.ReviewStatus != baseline.ReviewStatus;
}
