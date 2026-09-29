using System.Text;

namespace GuGuGaGaTranslator.Core.Translation;

/// <summary>One proper noun of a work: how it is written on screen, how the work's own localization writes it, and the
/// wordings that must never appear.</summary>
public sealed class GameTerm
{
    public string Source { get; set; } = string.Empty;

    public string Target { get; set; } = string.Empty;

    /// <summary>Wordings to rewrite into <see cref="Target"/> wherever they appear.</summary>
    public List<string> Forbidden { get; set; } = [];

    public string? Note { get; set; }

    /// <summary>Render the term in the one-line sheet syntax.</summary>
    public override string ToString()
    {
        var builder = new StringBuilder();
        builder.Append(Source).Append(" = ").Append(Target);
        if (Forbidden.Count > 0) builder.Append(" | 禁止: ").Append(string.Join("、", Forbidden));
        if (!string.IsNullOrWhiteSpace(Note)) builder.Append(" | 备注: ").Append(Note);
        return builder.ToString();
    }
}

/// <summary>Everything that makes the translator behave like a translator of one particular work: its terms, its setting,
/// and its voice.</summary>
public sealed class GameProfile
{
    /// <summary>A stable key, so the configuration can name this profile after it is renamed.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>The name shown in the picker, such as 「边狱巴士 / Limbus Company」.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>How this profile is recognized: substrings of the game window's title.</summary>
    public List<string> WindowHints { get; set; } = [];

    public string? Note { get; set; }

    public string? From { get; set; }

    public string? To { get; set; }

    public string? OcrLanguage { get; set; }

    /// <summary>What the work is and who is in it; sent with every request, because a model that knows the work recalls
    /// the official wording of terms that are not in the list yet.</summary>
    public string? Worldview { get; set; }

    /// <summary>Extra style instruction, such as 「罪人之间以名字互称,旁白用冷硬口吻」.</summary>
    public string? StyleHint { get; set; }

    public List<GameTerm> Terms { get; set; } = [];

    /// <summary>Render the terms as the editable sheet text.</summary>
    public string TermsAsText() => TermSheet.Format(Terms);

    /// <summary>Show the profile by name in lists and logs.</summary>
    public override string ToString() => string.IsNullOrWhiteSpace(Name) ? Id : Name;
}

/// <summary>The term sheet's text syntax: one term per line, <c>原文 = 译名</c>, with an optional <c>| 禁止: A、B</c> and
/// <c>| 备注: …</c>.</summary>
public static class TermSheet
{
    private static readonly string[] Separators = ["=>", "->", "→", "＝", "=", "\t"];

    /// <summary>Render terms in the sheet syntax.</summary>
    public static string Format(IEnumerable<GameTerm> terms)
    {
        var builder = new StringBuilder();
        foreach (var term in terms)
        {
            if (string.IsNullOrWhiteSpace(term.Source)) continue;
            builder.AppendLine(term.ToString());
        }

        return builder.ToString().TrimEnd();
    }

    /// <summary>Read a sheet, tolerating what people actually paste: full-width equals signs, arrows, tabs, and comment lines.</summary>
    public static List<GameTerm> Parse(string text, out List<string> problems)
    {
        problems = [];
        var terms = new List<GameTerm>();
        if (string.IsNullOrWhiteSpace(text)) return terms;

        var lineNumber = 0;
        foreach (var raw in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            lineNumber++;
            var line = raw.Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith('#') || line.StartsWith("//") || line.StartsWith('－')) continue;

            var split = SplitOnce(line);
            if (split is null)
            {
                problems.Add($"第 {lineNumber} 行没有找到「=」:{Shorten(line)}");
                continue;
            }

            var (source, rest) = split.Value;
            var (target, extra) = SplitOnce(rest, '|');

            var term = new GameTerm { Source = source.Trim(), Target = target.Trim() };
            foreach (var segment in (extra ?? string.Empty).Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var (label, value) = SplitOnce(segment, ':', '：');
                var body = value?.Trim() ?? string.Empty;
                if (label is null)
                {
                    // A bare segment after a bar is a forbidden variant.
                    term.Forbidden.AddRange(SplitList(segment));
                    continue;
                }

                var key = label.Trim();
                if (key.StartsWith("禁止", StringComparison.Ordinal) || key.StartsWith("禁用", StringComparison.Ordinal)
                    || key.StartsWith("不要", StringComparison.Ordinal) || key.StartsWith("错译", StringComparison.Ordinal)
                    || key.Equals("禁", StringComparison.Ordinal))
                {
                    term.Forbidden.AddRange(SplitList(body));
                }
                else if (key.StartsWith("备注", StringComparison.Ordinal) || key.StartsWith("说明", StringComparison.Ordinal))
                {
                    term.Note = body;
                }
                else
                {
                    problems.Add($"第 {lineNumber} 行有无法识别的段「{key}」(可用:禁止 / 备注)");
                }
            }

            term.Forbidden = term.Forbidden
                .Where(variant => variant.Length > 0
                    && !variant.Equals(term.Target, StringComparison.OrdinalIgnoreCase)
                    && !variant.Equals(term.Source, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (term.Source.Length == 0 || term.Target.Length == 0)
            {
                problems.Add($"第 {lineNumber} 行缺少原文或译名:{Shorten(line)}");
                continue;
            }

            terms.Add(term);
        }

        return terms;
    }

    public static List<GameTerm> Parse(string text) => Parse(text, out _);

    /// <summary>Split on the first of several separators, longest first so <c>=&gt;</c> is not mistaken for <c>=</c>.</summary>
    private static (string Left, string Right)? SplitOnce(string text)
    {
        foreach (var separator in Separators)
        {
            var index = text.IndexOf(separator, StringComparison.Ordinal);
            if (index > 0) return (text[..index], text[(index + separator.Length)..]);
        }

        return null;
    }

    private static (string Left, string? Right) SplitOnce(string text, params char[] separators)
    {
        var index = text.IndexOfAny(separators);
        return index < 0 ? (text, null) : (text[..index], text[(index + 1)..]);
    }

    /// <summary>Split a variant list written with any of the usual separators. Spaces are deliberately not separators: a
    /// wrong rendering may well contain one, such as 「N 社」 for 「N社」.</summary>
    private static IEnumerable<string> SplitList(string text) =>
        text.Split(['、', ',', '，', ';', '；', '/', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string Shorten(string text) => text.Length <= 40 ? text : text[..40] + "…";
}

/// <summary>The profiles a fresh install ships with, plus the lookups that decide which one a running game should use.</summary>
public static class GameProfiles
{
    /// <summary>The starter profiles: deliberately few and honest, each one a syntax example meant to be replaced by
    /// 「AI 生成」 plus a few corrections.</summary>
    public static List<GameProfile> Default() =>
    [
        new()
        {
            Id = "limbus-company",
            Name = "边狱巴士 / Limbus Company",
            WindowHints = ["Limbus", "边狱", "림버스"],
            Note = "Project Moon 的作品。都市世界观,十二位罪人,大量专有名词来自《废墟图书馆》与《脑叶公司》。",
            Worldview = "《边狱巴士》(Limbus Company, Project Moon 出品)的对话。背景是「都市」这一巨型都市国家,"
                + "玩家扮演管理者但丁,带领十二位「罪人」乘坐巴士回收「金枝」。"
                + "专有名词沿用官方简体中文译名(与《废墟图书馆》《脑叶公司》一致),不要另造译名。",
            StyleHint = "罪人之间以名字或绰号互称,语气现代、口语化、夹带黑色幽默;不要用文言或书面官腔。",
            Terms =
            [
                new()
                {
                    Source = "新九人会",
                    Target = "新九人会",
                    Forbidden = ["新九人联盟", "新九人协会", "新九人會"],
                    Note = "N 公司相关组织的官方译名,不要意译成「联盟」。",
                },
                new()
                {
                    Source = "N社",
                    Target = "N公司",
                    Forbidden = ["N 社", "恩社"],
                },
            ],
        },
        new()
        {
            Id = "example-galgame",
            Name = "示例 · 某部 galgame(照这个格式自己改)",
            WindowHints = ["example"],
            Note = "留作格式参考:窗口标题关键字 + 术语表。填好之后把名字改掉即可。",
            Worldview = "在这里用一两句话写清这是什么作品、什么世界观、主角是谁。模型知道作品名之后,"
                + "会自动沿用该作品官方译名里那些你没列出来的词。",
            StyleHint = "在这里写语气要求,例如「日常对话口语化,不要书面语」。",
            Terms =
            [
                new() { Source = "先輩", Target = "学姐", Forbidden = ["前辈", "先辈"], Note = "称呼统一用「学姐」" },
                new() { Source = "喫茶店", Target = "咖啡店", Forbidden = ["吃茶店"] },
            ],
        },
    ];

    public static GameProfile? FindById(string? id, IReadOnlyList<GameProfile> profiles) =>
        string.IsNullOrWhiteSpace(id)
            ? null
            : profiles.FirstOrDefault(profile => profile.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    /// <summary>Pick the profile whose window hints match a window title; the longest matching hint wins, so a profile for
    /// a specific game beats one that merely matches its launcher.</summary>
    public static GameProfile? MatchByTitle(string? title, IReadOnlyList<GameProfile> profiles)
    {
        if (string.IsNullOrWhiteSpace(title)) return null;

        GameProfile? best = null;
        var bestLength = 0;
        foreach (var profile in profiles)
        {
            foreach (var hint in profile.WindowHints)
            {
                if (string.IsNullOrWhiteSpace(hint)) continue;
                if (!title.Contains(hint, StringComparison.OrdinalIgnoreCase)) continue;
                if (hint.Length <= bestLength) continue;
                best = profile;
                bestLength = hint.Length;
            }
        }

        return best;
    }

    public static List<GlossaryEntry> ToGlossary(GameProfile? profile) =>
        profile is null
            ? []
            : profile.Terms
                .Where(term => !string.IsNullOrWhiteSpace(term.Source) && !string.IsNullOrWhiteSpace(term.Target))
                .Select(term => new GlossaryEntry(
                    term.Source.Trim(),
                    term.Target.Trim(),
                    term.Forbidden.Count > 0 ? term.Forbidden.ToArray() : null))
                .ToList();

    /// <summary>Merge term lists, with the later list winning for the same source term so a hand-typed glossary can
    /// override a profile without editing it.</summary>
    public static List<GlossaryEntry> Merge(params IEnumerable<GlossaryEntry>[] lists)
    {
        var merged = new List<GlossaryEntry>();
        var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var list in lists)
        {
            foreach (var entry in list)
            {
                if (string.IsNullOrWhiteSpace(entry.Source)) continue;
                if (index.TryGetValue(entry.Source, out var existing))
                {
                    merged[existing] = entry;
                    continue;
                }

                index[entry.Source] = merged.Count;
                merged.Add(entry);
            }
        }

        return merged;
    }

    /// <summary>Build the id for a profile name: ASCII letters and digits kept, everything else folded into dashes, and a
    /// numeric suffix when that collides.</summary>
    public static string MakeId(string name, IEnumerable<string> existing)
    {
        var builder = new StringBuilder();
        foreach (var character in name.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character) && character < 128) builder.Append(character);
            else if (builder.Length > 0 && builder[^1] != '-') builder.Append('-');
        }

        var slug = builder.ToString().Trim('-');
        if (slug.Length == 0) slug = "game";

        var taken = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);
        if (!taken.Contains(slug)) return slug;

        for (var suffix = 2; ; suffix++)
        {
            var candidate = $"{slug}-{suffix}";
            if (!taken.Contains(candidate)) return candidate;
        }
    }
}
