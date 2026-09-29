using GuGuGaGaTranslator.Core.Capture;

namespace GuGuGaGaTranslator.Core.Ocr;

/// <summary>A text recognizer. The pipeline knows only this interface, which is what keeps the choice of engine open.</summary>
public interface ITextRecognizer
{
    /// <summary>A stable identifier for logs, cache keys, and dumps.</summary>
    string Id { get; }

    string LanguageTag { get; }

    Task<OcrResult> RecognizeAsync(Frame frame, CancellationToken cancellationToken = default);
}
