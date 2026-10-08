namespace GuGuGaGaTranslator.Core.Capture;

public sealed class CaptureUnavailableException(Exception? inner = null) : InvalidOperationException(
    "目标窗口画面不可读取。请恢复窗口，尝试窗口化或无边框模式；兼容屏幕捕获需保持选区无遮挡。", inner);
