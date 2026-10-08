using System.Runtime.Versioning;
using GuGuGaGaTranslator.Core.Capture;
using GuGuGaGaTranslator.Core.Ocr;

namespace GuGuGaGaTranslator.Ocr.Rapid;

/// <summary>按所选原文语言使用模型；自动模式比较普通模型与韩语候选。</summary>
[SupportedOSPlatform("windows")]
public sealed class FourLanguageRecognizer : ITextRecognizer, IDisposable
{
    private readonly RapidOcrRecognizer _general;
    private readonly Lazy<RapidOcrRecognizer> _korean;
    private readonly Func<string> _language;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    public FourLanguageRecognizer(RapidOcrSettings settings, Func<string> language)
    {
        _general = RapidOcrRecognizer.Create(settings);
        _korean = new(() => RapidOcrRecognizer.Create(settings with { Korean = true }));
        _language = language;
    }

    public string Id => "rapidocr:four-languages";
    public string LanguageTag => "auto";

    public async Task<OcrResult> RecognizeAsync(Frame frame, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var language = _language();
            if (language.StartsWith("ko", StringComparison.OrdinalIgnoreCase))
                return await _korean.Value.RecognizeAsync(frame, cancellationToken).ConfigureAwait(false);
            var general = await _general.RecognizeAsync(frame, cancellationToken).ConfigureAwait(false);
            if (!language.Equals("auto", StringComparison.OrdinalIgnoreCase))
                return general;
            var korean = await _korean.Value.RecognizeAsync(frame, cancellationToken).ConfigureAwait(false);
            var hangul = korean.Text.Count(character => character is >= '\uAC00' and <= '\uD7AF');
            var letters = korean.Text.Count(char.IsLetter);
            var cjk = general.Text.Count(character => character is >= '\u3040' and <= '\u30FF' or >= '\u4E00' and <= '\u9FFF');
            // 不因英文字母的同分候选而切换；中文、日文候选有足够文字时优先保留。
            return hangul >= 2 && hangul * 2 >= letters && cjk < hangul ? korean : general;
        }
        finally { _gate.Release(); }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _general.Dispose();
        if (_korean.IsValueCreated) _korean.Value.Dispose();
        _gate.Dispose();
    }
}
