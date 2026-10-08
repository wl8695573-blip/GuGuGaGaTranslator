using System.Text;
using System.IO;

namespace GuGuGaGaTranslator.Core.Translation;

/// <summary>术语条目，保存原文、译名和需要替换的错误译法。</summary>
public sealed class GameTerm
{
    /// <summary>Which language <see cref="Source"/> is written in — <c>en</c>, <c>ja</c>, or <c>zh</c>.
    /// Null keeps the older meaning: the source is whatever language the game is being played in.</summary>
    public string? Language { get; set; }

    public string Source { get; set; } = string.Empty;

    public string Target { get; set; } = string.Empty;

    /// <summary>Wordings to rewrite into <see cref="Target"/> wherever they appear.</summary>
    public List<string> Forbidden { get; set; } = [];

    public string? Note { get; set; }

    public string? SourceUrl { get; set; }
    public string? ReviewStatus { get; set; }
    public bool Enabled { get; set; } = true;

    /// <summary>Render the term in the one-line sheet syntax.</summary>
    public override string ToString()
    {
        var builder = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(Language))
            builder.Append(Language).Append(": ");
        builder.Append(Source).Append(" = ").Append(Target);
        if (Forbidden.Count > 0)
            builder.Append(" | 禁止: ").Append(string.Join("、", Forbidden));
        if (!string.IsNullOrWhiteSpace(Note))
            builder.Append(" | 备注: ").Append(Note);
        if (!string.IsNullOrWhiteSpace(SourceUrl))
            builder.Append(" | 出处: ").Append(SourceUrl);
        if (!string.IsNullOrWhiteSpace(ReviewStatus))
            builder.Append(" | 核对: ").Append(ReviewStatus);
        if (!Enabled)
            builder.Append(" | 启用: 否");
        return builder.ToString();
    }
}

/// <summary>游戏档案，包括术语、背景和翻译风格。</summary>
public sealed class GameProfile
{
    /// <summary>A stable key, so the configuration can name this profile after it is renamed.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>The name shown in the picker, such as 「边狱巴士 / Limbus Company」.</summary>
    public string Name { get; set; } = string.Empty;

    public string? Author { get; set; }
    public string? SourceUrl { get; set; }
    public string? License { get; set; }
    public string? ProfileVersion { get; set; }

    /// <summary>How this profile is recognized: substrings of the game window's title.</summary>
    public List<string> WindowHints { get; set; } = [];
    public List<string> ProcessHints { get; set; } = [];

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
            if (string.IsNullOrWhiteSpace(term.Source))
                continue;
            builder.AppendLine(term.ToString());
        }

        return builder.ToString().TrimEnd();
    }

    /// <summary>Read a sheet, tolerating what people actually paste: full-width equals signs, arrows, tabs, and comment lines.</summary>
    public static List<GameTerm> Parse(string text, out List<string> problems)
    {
        problems = [];
        var terms = new List<GameTerm>();
        if (string.IsNullOrWhiteSpace(text))
            return terms;

        var lineNumber = 0;
        foreach (var raw in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            lineNumber++;
            var line = raw.Trim();
            if (line.Length == 0)
                continue;
            if (line.StartsWith('#') || line.StartsWith("//") || line.StartsWith('－'))
                continue;

            // 「en: Yi Sang = 李箱」这种语言标签:一条表里同时放几种原文,翻译时按当前方向取用。
            var language = SplitLanguage(ref line);
            if (line.Length == 0)
                continue;

            var split = SplitOnce(line);
            if (split is null)
            {
                problems.Add($"第 {lineNumber} 行没有找到「=」:{Shorten(line)}");
                continue;
            }

            var (source, rest) = split.Value;
            var (target, extra) = SplitOnce(rest, '|');

            var term = new GameTerm { Language = language, Source = source.Trim(), Target = target.Trim() };
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
                else if (key == "出处")
                    term.SourceUrl = body;
                else if (key == "核对")
                    term.ReviewStatus = body;
                else if (key == "启用")
                    term.Enabled = body is not ("否" or "false" or "0");
                else
                {
                    problems.Add($"第 {lineNumber} 行有无法识别的段「{key}」(可用:禁止 / 备注 / 出处 / 核对 / 启用)");
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

    /// <summary>Take a leading language tag off a sheet line: <c>en:</c>, <c>ja:</c>, <c>zh:</c> (and their
    /// long forms). Returns the normalised tag and leaves the rest of the line in <paramref name="line"/>.</summary>
    private static string? SplitLanguage(ref string line)
    {
        var colon = line.IndexOfAny([':', '：']);
        if (colon <= 0 || colon > 8)
            return null;

        var tag = line[..colon].Trim().ToLowerInvariant();
        var language = tag switch
        {
            "en" or "english" or "eng" => "en",
            "ja" or "jp" or "japanese" or "jpn" => "ja",
            "ko" or "kor" or "korean" or "ko-kr" => "ko",
            "zh" or "cn" or "chinese" => "zh",
            _ => null,
        };
        if (language is null)
            return null;

        line = line[(colon + 1)..].Trim();
        return language;
    }

    /// <summary>Split on the first of several separators, longest first so <c>=&gt;</c> is not mistaken for <c>=</c>.</summary>
    private static (string Left, string Right)? SplitOnce(string text)
    {
        foreach (var separator in Separators)
        {
            var index = text.IndexOf(separator, StringComparison.Ordinal);
            if (index > 0)
                return (text[..index], text[(index + separator.Length)..]);
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
    /// <summary>The starter profiles: one fully worked example that ships tuned for a real game, plus a syntax
    /// template meant to be replaced by 「AI 生成」 plus a few corrections.</summary>
    public static List<GameProfile> Default() =>
    [
        new()
        {
            Id = "limbus-company",
            Name = "边狱巴士 / Limbus Company",
            WindowHints = ["Limbus", "边狱", "림버스"],
            ProcessHints = ["LimbusCompany"],
            Author = "LCTA 社区维护",
            SourceUrl = "https://www.zeroasso.top/archive/main/",
            ProfileVersion = "1.4.0",
            Note = "Project Moon 的作品。都市世界观,十二位罪人,大量专有名词来自《废墟图书馆》与《脑叶公司》。"
                + "术语表按零协会汉化整理,同时带英文与日文两种原文,中/日/英互译都能用。",
            Worldview = LimbusProfile.Worldview,
            StyleHint = LimbusProfile.StyleHint,
            // 表里同时有 en: 和 ja: 两种原文,翻译时按当前方向取用,见 GameProfiles.ForDirection。
            Terms = LimbusProfile.CreateTerms(),
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

    public static GameProfile? MatchWindow(string? title, string? processName, IReadOnlyList<GameProfile> profiles)
    {
        var process = profiles.FirstOrDefault(profile => profile.ProcessHints.Any(hint =>
            !string.IsNullOrWhiteSpace(hint) && string.Equals(Path.GetFileNameWithoutExtension(hint), processName, StringComparison.OrdinalIgnoreCase)));
        if (process is not null)
            return process;
        // 浏览器中打开的游戏介绍或术语网页不应自动启用游戏档案。
        if (new[] { "chrome", "msedge", "firefox", "brave", "opera", "iexplore" }.Contains(processName, StringComparer.OrdinalIgnoreCase))
            return null;
        return MatchByTitle(title, profiles);
    }

    /// <summary>Pick the profile whose window hints match a window title; the longest matching hint wins, so a profile for
    /// a specific game beats one that merely matches its launcher.</summary>
    public static GameProfile? MatchByTitle(string? title, IReadOnlyList<GameProfile> profiles)
    {
        if (string.IsNullOrWhiteSpace(title))
            return null;

        GameProfile? best = null;
        var bestLength = 0;
        foreach (var profile in profiles)
        {
            foreach (var hint in profile.WindowHints)
            {
                if (string.IsNullOrWhiteSpace(hint))
                    continue;
                if (!title.Contains(hint, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (hint.Length <= bestLength)
                    continue;
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

    /// <summary>The terms that apply to one language direction. A sheet may carry several languages at once
    /// (rows tagged <c>en:</c> / <c>ja:</c> / <c>zh:</c>), and the direction decides which side is used:
    /// translating into Chinese takes the rows written in the source language as they are, translating out
    /// of Chinese flips them, and between two foreign languages the Chinese column acts as the pivot.</summary>
    public static List<GlossaryEntry> ForDirection(GameProfile? profile, string from, string to) =>
        profile is null ? [] : ForDirection(profile.Terms, from, to);

    public static List<GlossaryEntry> ForDirection(IReadOnlyList<GameTerm> terms, string from, string to)
    {
        var result = new List<GlossaryEntry>();

        if (IsChinese(to))
        {
            foreach (var term in terms)
            {
                if (!IsUsable(term))
                    continue;
                // 没标语言的按老格式理解:它就是「游戏原文 → 中文」,任何源语言都用得上。
                if (term.Language is { Length: > 0 } tag
                    && !from.Equals("auto", StringComparison.OrdinalIgnoreCase) && !Matches(tag, from))
                    continue;
                result.Add(Entry(term.Source, term.Target, term.Forbidden));
            }

            return result;
        }

        if (IsChinese(from))
        {
            // 中 → 外语:把标了目标语言的行反过来用。禁止列写的是中文侧的错误写法,反转后不适用。
            foreach (var term in terms)
            {
                if (!IsUsable(term))
                    continue;
                if (term.Language is not { Length: > 0 } tag || !Matches(tag, to))
                    continue;
                result.Add(Entry(term.Target, term.Source, null));
            }

            return result;
        }

        // 外语 → 外语:以中文那列为枢轴,找同一个中文译名在两种外语里各自的写法。
        var pivot = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var term in terms)
        {
            if (!IsUsable(term))
                continue;
            if (term.Language is not { Length: > 0 } toTag || !Matches(toTag, to))
                continue;
            pivot[term.Target.Trim()] = term.Source.Trim();
        }

        foreach (var term in terms)
        {
            if (!IsUsable(term))
                continue;
            if (term.Language is not { Length: > 0 } fromTag || !Matches(fromTag, from))
                continue;
            if (!pivot.TryGetValue(term.Target.Trim(), out var translated))
                continue;
            result.Add(Entry(term.Source, translated, null));
        }

        return result;
    }

    /// <summary>将语言标签归一到中、日、英、韩；不支持的标签返回 null。</summary>
    public static string? LanguageOf(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var text = value.Trim().ToLowerInvariant();
        if (text.StartsWith("zh", StringComparison.Ordinal) || text is "cn" or "chinese")
            return "zh";
        if (text.StartsWith("ja", StringComparison.Ordinal) || text is "jp" or "japanese" or "jpn")
            return "ja";
        if (text.StartsWith("en", StringComparison.Ordinal) || text is "eng" or "english")
            return "en";
        if (text.StartsWith("ko", StringComparison.Ordinal) || text is "kor" or "korean")
            return "ko";
        return null;
    }

    private static bool Matches(string tag, string language) =>
        LanguageOf(tag) is { } left && LanguageOf(language) is { } right && left == right;

    private static bool IsChinese(string language) => LanguageOf(language) == "zh";

    private static bool IsUsable(GameTerm term) =>
        term.Enabled && term.ReviewStatus != "待核对"
        && !(string.IsNullOrWhiteSpace(term.ReviewStatus) && (term.Note?.Contains("待核对", StringComparison.Ordinal) ?? false))
        && !string.IsNullOrWhiteSpace(term.Source) && !string.IsNullOrWhiteSpace(term.Target);

    private static GlossaryEntry Entry(string source, string target, List<string>? forbidden) =>
        new(source.Trim(), target.Trim(), forbidden is { Count: > 0 } ? forbidden.ToArray() : null);

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
                if (string.IsNullOrWhiteSpace(entry.Source))
                    continue;
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
            if (char.IsLetterOrDigit(character) && character < 128)
                builder.Append(character);
            else if (builder.Length > 0 && builder[^1] != '-')
                builder.Append('-');
        }

        var slug = builder.ToString().Trim('-');
        if (slug.Length == 0)
            slug = "game";

        var taken = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);
        if (!taken.Contains(slug))
            return slug;

        for (var suffix = 2; ; suffix++)
        {
            var candidate = $"{slug}-{suffix}";
            if (!taken.Contains(candidate))
                return candidate;
        }
    }
}
