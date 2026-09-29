using System.Text;

namespace GuGuGaGaTranslator.Core.Translation;

/// <summary>A translator that echoes a marked-up copy of the input, so the capture → OCR → translate → overlay loop can run
/// before any engine is configured; a glossary term with forbidden wordings comes back wrong on purpose, so the term enforcer is measurable offline.</summary>
public sealed class MockTranslator : ITranslator, IChatCompleter
{
    /// <inheritdoc />
    public string Id => "mock";

    /// <inheritdoc />
    public bool RequiresNetwork => false;

    /// <inheritdoc />
    public Task<string> TranslateAsync(TranslationRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var text = request.Text.Trim();

        foreach (var entry in request.Glossary)
        {
            if (entry.Forbidden is not { Count: > 0 } variants || string.IsNullOrWhiteSpace(entry.Source)) continue;
            if (!text.Contains(entry.Source, StringComparison.OrdinalIgnoreCase)) continue;
            text = text.Replace(entry.Source, variants[0], StringComparison.OrdinalIgnoreCase);
        }

        return Task.FromResult($"[{Id} {request.From}→{request.To}] {text}");
    }

    /// <summary>Answer a term-sheet request with a small canned sheet, so the 「AI 生成术语表」 path can be run offline.</summary>
    public Task<string> CompleteAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var name = "（mock 示例游戏）";
        foreach (var line in userPrompt.Split('\n'))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("游戏名:", StringComparison.Ordinal)) continue;
            var value = trimmed["游戏名:".Length..].Trim();
            if (value.Length > 0) name = value;
            break;
        }

        var builder = new StringBuilder();
        builder.Append("作品名: ").Append(name).Append('\n');
        builder.Append("新九人会 = 新九人会 | 禁止: 新九人联盟、新九人协会\n");
        builder.Append("N社 = N公司 | 禁止: N 社\n");
        builder.Append("リムバスカンパニー = 边狱公司 | 备注: mock 数据\n");

        return Task.FromResult(builder.ToString());
    }
}
