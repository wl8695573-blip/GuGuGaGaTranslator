using System.Text;
using System.Text.Json;

namespace GuGuGaGaTranslator.Core.Translation;

/// <summary>生成术语表并解析模型返回的文本。</summary>
public static class TermSheetBuilder
{
    /// <summary>术语表生成的系统提示。</summary>
    public const string SystemPrompt =
        "你是游戏本地化术语库编辑,熟悉各款游戏官方简体中文译名(尤其日韩游戏的官方中文版)。"
        + "你的任务是产出一个可以被程序直接解析的术语表。只输出术语表本身,不要解释、不要寒暄。";

    /// <summary>
    /// Ask for a term sheet. When <paramref name="gameName"/> is empty the model is asked to work out the work from the
    /// sample lines instead, which is what makes the feature usable on a game nobody has written a profile for.
    /// </summary>
    public static string BuildUserPrompt(
        string? gameName,
        string targetLanguage,
        string? sampleLines = null,
        string? existingSheet = null,
        string? synopsis = null)
    {
        var builder = new StringBuilder();
        builder.Append("请为下面的游戏整理一份「专有名词 → ");
        builder.Append(TargetName(targetLanguage));
        builder.Append("官方译名」术语表。\n\n");

        if (!string.IsNullOrWhiteSpace(gameName))
        {
            builder.Append("游戏名:").Append(gameName.Trim()).Append('\n');
        }
        else
        {
            builder.Append("游戏名:未知 —— 请先根据下面的资料判断这是哪一部作品,并在第一行写出「作品名: …」。\n");
        }

        if (!string.IsNullOrWhiteSpace(synopsis))
        {
            builder.Append("\n作品资料(玩家提供的简介 / 角色表 / 设定,据此确定是哪一部作品、有哪些专有名词):\n");
            builder.Append(synopsis.Trim()).Append('\n');
        }

        if (!string.IsNullOrWhiteSpace(sampleLines))
        {
            builder.Append("\n从游戏里识别到的台词(可能有识别错误,仅供参考):\n");
            foreach (var line in sampleLines.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(20))
            {
                builder.Append("- ").Append(line).Append('\n');
            }
        }

        if (!string.IsNullOrWhiteSpace(existingSheet))
        {
            builder.Append("\n已经知道的术语(不要重复,只补充新的):\n").Append(existingSheet.Trim()).Append('\n');
        }

        builder.Append("""

            输出要求:
            1. 每行一个术语,格式严格为:原文 = 译名
            2. 如果这个词很容易被译错(常见误译、直译、台版译名、民间译名),在该行末尾加上: | 禁止: 误译1、误译2
            3. 只收专有名词:人名、组织名、地名、招式名、专有设定。不要收普通词汇。
            4. 译名必须是该作品官方简体中文版的写法;没有官方中文版的,用玩家社区最通用的译名,并在该行末尾加 | 备注: 社区通用
            5. 同一个名字在不同语言版本里写法不同时,各列一行 —— 玩家玩的可能是日文版、韩文版或英文版,左边写法对不上就命中不了这条术语。
            6. 一条一行,不要编号,不要表格,不要 Markdown 代码块,不要额外解释。
            7. 最多 40 条,优先收在剧情里反复出现的名字。
            8. 术语表之后空一行,再写一行「世界观: …」—— 用一到三句话说明这是什么作品、什么世界观、主角是谁。这一行会随以后每一句台词一起发给翻译模型,所以要短,但要让模型认得出这部作品。

            示例(照这个格式,不要照抄内容):
            新九人会 = 新九人会 | 禁止: 新九人联盟、新九人协会
            신구인회 = 新九人会 | 禁止: 新老会
            N社 = N公司 | 禁止: N 社

            世界观: 《边狱巴士》的对话,都市世界观,玩家扮演管理者但丁带领十二位罪人回收金枝,专有名词沿用官方简中译名。
            """);

        return builder.ToString();
    }

    /// <summary>Read a model's answer into terms, the work's name, the one-line setting description, and the complaints,
    /// accepting the formats models drift into: the requested sheet syntax, a Markdown table, a numbered list, or JSON.</summary>
    public static (List<GameTerm> Terms, string? GameName, string? Worldview, List<string> Problems) ParseReply(string reply)
    {
        var problems = new List<string>();
        var text = StripFences(reply ?? string.Empty);
        var gameName = ExtractLabeledLine(ref text, GameNameLabels);
        var worldview = ExtractLabeledLine(ref text, WorldviewLabels);

        var terms = LooksLikeJson(text) ? ParseJson(text, problems) : ParseLines(text, problems);

        // A term sheet is short by nature; anything longer is prose that happened to contain an equals sign.
        terms = terms.Where(term => term.Source.Length <= 24 && term.Target.Length <= 24).ToList();

        return (terms, gameName, worldview, problems);
    }

    /// <summary>Merge an existing sheet with new terms, keeping what the user already had.</summary>
    public static (List<GameTerm> Terms, int Added) Merge(IEnumerable<GameTerm> existing, IEnumerable<GameTerm> incoming)
    {
        var merged = existing.ToList();
        var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < merged.Count; i++)
            index[merged[i].Source] = i;

        var added = 0;
        foreach (var term in incoming)
        {
            if (index.TryGetValue(term.Source, out var at))
            {
                // Fill in the variants the existing entry was missing, but never overwrite wording the user typed by hand.
                var combined = merged[at].Forbidden
                    .Concat(term.Forbidden)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                merged[at].Forbidden = combined;
                continue;
            }

            index[term.Source] = merged.Count;
            merged.Add(term);
            added++;
        }

        return (merged, added);
    }

    /// <summary>Read the language name a model follows more reliably than a bare tag.</summary>
    private static string TargetName(string languageTag) => languageTag.ToLowerInvariant() switch
    {
        "zh" or "zh-hans" or "zh-hans-cn" or "zh-cn" or "chinese" => "简体中文",
        "zh-hant" or "zh-hant-tw" or "traditional chinese" => "繁体中文",
        "ja" or "japanese" => "日语",
        "en" or "english" => "英语",
        "ko" or "korean" => "韩语",
        _ => languageTag,
    };

    /// <summary>Remove a Markdown code fence, which models add even when told not to.</summary>
    private static string StripFences(string text)
    {
        var trimmed = text.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
            return trimmed;

        var firstLineEnd = trimmed.IndexOf('\n');
        if (firstLineEnd < 0)
            return trimmed;
        var body = trimmed[(firstLineEnd + 1)..];
        var closing = body.LastIndexOf("```", StringComparison.Ordinal);
        return closing < 0 ? body : body[..closing];
    }

    private static readonly string[] GameNameLabels =
        ["作品名:", "作品名：", "游戏名:", "游戏名：", "游戏:", "游戏：", "作品:", "作品：", "title:", "Title:"];

    private static readonly string[] WorldviewLabels =
        ["世界观:", "世界观：", "背景:", "背景：", "作品背景:", "作品背景：", "设定:", "设定：", "worldview:", "Worldview:", "setting:", "Setting:"];

    /// <summary>Pull one labeled line — the work's name, or its setting description — out of the reply, and take that line
    /// out of the sheet so it is not parsed as a term.</summary>
    private static string? ExtractLabeledLine(ref string text, string[] prefixes)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        string? found = null;
        var kept = new List<string>(lines.Length);

        foreach (var line in lines)
        {
            var trimmed = line.Trim().TrimStart('#', '*', '-', ' ');
            var isLabelLine = false;

            foreach (var prefix in prefixes)
            {
                if (!trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (found is null)
                {
                    found = trimmed[prefix.Length..].Trim().Trim('「', '」', '『', '』', '"', '\'', '*', ' ');
                }

                isLabelLine = true;
                break;
            }

            if (!isLabelLine)
                kept.Add(line);
        }

        text = string.Join('\n', kept);
        return string.IsNullOrWhiteSpace(found) ? null : found;
    }

    private static bool LooksLikeJson(string text)
    {
        var trimmed = text.TrimStart();
        return trimmed.StartsWith('[') || trimmed.StartsWith('{');
    }

    /// <summary>Read an array of term objects, or an object holding one.</summary>
    private static List<GameTerm> ParseJson(string text, List<string> problems)
    {
        var terms = new List<GameTerm>();
        try
        {
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in new[] { "terms", "glossary", "术语表", "items", "list" })
                {
                    if (root.TryGetProperty(property, out var array) && array.ValueKind == JsonValueKind.Array)
                    {
                        root = array;
                        break;
                    }
                }
            }

            if (root.ValueKind != JsonValueKind.Array)
            {
                problems.Add("JSON 里没有找到术语数组");
                return terms;
            }

            foreach (var item in root.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                    continue;
                var source = ReadString(item, "source", "原文", "term", "src");
                var target = ReadString(item, "target", "译名", "译文", "dst", "translation");
                if (source.Length == 0 || target.Length == 0)
                    continue;

                var term = new GameTerm { Source = source, Target = target, Note = ReadString(item, "note", "备注") };
                term.Forbidden.AddRange(ReadList(item, "forbidden", "禁止", "aliases", "wrong"));
                if (term.Note.Length == 0)
                    term.Note = null;
                terms.Add(term);
            }
        }
        catch (JsonException exception)
        {
            problems.Add($"JSON 解析失败:{exception.Message}");
        }

        return terms;
    }

    /// <summary>Read the line-oriented formats: the sheet syntax, tables, and numbered lists.</summary>
    private static List<GameTerm> ParseLines(string text, List<string> problems)
    {
        var prepared = new StringBuilder();
        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            var line = NormalizeLine(raw);
            if (line.Length == 0)
                continue;
            prepared.Append(line).Append('\n');
        }

        var terms = TermSheet.Parse(prepared.ToString(), out var lineProblems);
        problems.AddRange(lineProblems);
        return terms;
    }

    /// <summary>Fold one line of a model's answer into the sheet syntax: drop list numbering and bullets, turn a Markdown
    /// table row into a term line, and rewrite parenthesised 「禁止」 clauses as the bar form.</summary>
    private static string NormalizeLine(string raw)
    {
        var line = raw.Trim();
        if (line.Length == 0)
            return string.Empty;

        // Markdown table rows: | 原文 | 译名 | 禁止 |
        if (line.StartsWith('|'))
        {
            var cells = line.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (cells.Length == 0 || cells.All(cell => cell.All(character => character is '-' or ':')))
                return string.Empty;
            if (cells.Length == 1)
                return string.Empty;
            if (cells[0].Contains("原文", StringComparison.Ordinal) || cells[0].Contains("source", StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            var source = cells[0];
            var target = cells.Length > 1 ? cells[1] : string.Empty;
            var forbidden = cells.Length > 2 ? cells[2] : string.Empty;
            forbidden = forbidden.Replace("禁止:", string.Empty, StringComparison.Ordinal)
                .Replace("禁止：", string.Empty, StringComparison.Ordinal)
                .Trim();

            return forbidden.Length == 0
                ? $"{source} = {target}"
                : $"{source} = {target} | 禁止: {forbidden}";
        }

        line = line.TrimStart('-', '*', '+', ' ', '\t', '#');
        var digits = 0;
        while (digits < line.Length && char.IsDigit(line[digits]))
            digits++;
        if (digits > 0 && digits < line.Length && line[digits] is '.' or '、' or ')' or '）' or ':')
        {
            line = line[(digits + 1)..].TrimStart();
        }

        line = line.Replace("**", string.Empty, StringComparison.Ordinal);

        // 「新九人会（禁止：新九人联盟）」 → 「新九人会 | 禁止: 新九人联盟」
        foreach (var (open, close) in new[] { ('（', '）'), ('(', ')'), ('【', '】') })
        {
            var start = line.IndexOf(open);
            if (start < 0)
                continue;
            var end = line.IndexOf(close, start);
            if (end < 0)
                continue;

            var inner = line[(start + 1)..end].Trim();
            var isBan = inner.StartsWith("禁止", StringComparison.Ordinal) || inner.StartsWith("禁用", StringComparison.Ordinal)
                || inner.StartsWith("不要", StringComparison.Ordinal) || inner.StartsWith("误译", StringComparison.Ordinal)
                || inner.StartsWith("错译", StringComparison.Ordinal);
            if (!isBan)
                continue;

            foreach (var prefix in new[] { "禁止译法:", "禁止译法：", "禁止:", "禁止：", "禁用:", "禁用：", "不要:", "不要：", "误译:", "误译：", "错译:", "错译：" })
            {
                if (!inner.StartsWith(prefix, StringComparison.Ordinal))
                    continue;
                inner = inner[prefix.Length..].Trim();
                break;
            }

            line = line[..start].TrimEnd() + " | 禁止: " + inner;
            break;
        }

        return line.Trim('「', '」', '『', '』');
    }

    private static string ReadString(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (!element.TryGetProperty(name, out var value))
                continue;
            if (value.ValueKind == JsonValueKind.String)
                return value.GetString()?.Trim() ?? string.Empty;
        }

        return string.Empty;
    }

    private static IEnumerable<string> ReadList(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (!element.TryGetProperty(name, out var value))
                continue;
            if (value.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in value.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } text)
                        yield return text.Trim();
                }
            }
            else if (value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } list)
            {
                foreach (var part in list.Split(['、', ',', '，', ';', '；'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    yield return part;
                }
            }
        }
    }
}
