namespace GuGuGaGaTranslator.Core.Text;

/// <summary>等待同一段识别文字稳定，避免逐字字幕每新增一个字符就请求翻译。</summary>
public sealed class TextStabilityGate
{
    private string? _candidate;
    private long _changedAt;
    public bool IsWaiting { get; private set; }

    public bool Observe(string text, long nowMs, int settleMs)
    {
        if (!string.Equals(_candidate, text, StringComparison.Ordinal))
        {
            _candidate = text;
            _changedAt = nowMs;
        }
        IsWaiting = nowMs - _changedAt < Math.Clamp(settleMs, 0, 3000);
        return !IsWaiting;
    }

    public void Reset()
    {
        _candidate = null;
        IsWaiting = false;
    }
}
