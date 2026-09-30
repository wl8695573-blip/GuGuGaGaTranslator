using System.Runtime.Versioning;
using System.Windows;
using GuGuGaGaTranslator.Core.Interop;

namespace GuGuGaGaTranslator.Core.Capture;

/// <summary>The rectangles covered by this tool's own windows that the operating system would include
/// in a screen capture. The region being translated is read straight off the screen, so anything of
/// ours that overlaps it — the control window, the translation panel, the language switcher — is read
/// back as if it were the game's text, and the frames are masked with these rectangles before
/// recognition. Windows carrying WDA_EXCLUDEFROMCAPTURE are skipped: the capture already lacks them,
/// and blanking their area would erase whatever is visible behind them.</summary>
public static class SelfWindowMask
{
    [SupportedOSPlatform("windows")]
    public static IReadOnlyList<Int32Rect> ScreenRects()
    {
        var processId = (uint)Environment.ProcessId;
        var rects = new List<Int32Rect>();

        NativeMethods.EnumWindows((hwnd, _) =>
        {
            NativeMethods.GetWindowThreadProcessId(hwnd, out var owner);
            if (owner != processId) return true;
            if (!NativeMethods.IsWindowVisible(hwnd)) return true;

            // 读不到亲和性时宁可遮掉:让自家文字混进 OCR 比多遮一块更糟。
            if (NativeMethods.GetWindowDisplayAffinity(hwnd, out var affinity) && affinity != NativeMethods.WdaNone) return true;

            if (!WindowEnumerator.TryReadFrameRect(hwnd, out var rect)) return true;
            if (rect.Width <= 0 || rect.Height <= 0) return true;

            rects.Add(rect);
            return true;
        }, 0);

        return rects;
    }

    /// <summary>Blank every pixel of a frame that falls under one of this tool's own windows.</summary>
    [SupportedOSPlatform("windows")]
    public static void Apply(Frame frame) => Apply(frame, ScreenRects());

    /// <summary>Blank the given screen rectangles out of a frame, translating screen coordinates into
    /// frame-local ones through the frame's own origin.</summary>
    public static void Apply(Frame frame, IReadOnlyList<Int32Rect> screenRects)
    {
        if (screenRects.Count == 0) return;

        var origin = frame.SourceRegion;
        foreach (var rect in screenRects)
        {
            var left = Math.Max(0, rect.X - origin.X);
            var top = Math.Max(0, rect.Y - origin.Y);
            var right = Math.Min(frame.Width, rect.X + rect.Width - origin.X);
            var bottom = Math.Min(frame.Height, rect.Y + rect.Height - origin.Y);
            if (right <= left || bottom <= top) continue;

            for (var y = top; y < bottom; y++)
            {
                var offset = (y * frame.Width + left) * 4;
                for (var x = left; x < right; x++)
                {
                    frame.Bgra[offset] = 0;
                    frame.Bgra[offset + 1] = 0;
                    frame.Bgra[offset + 2] = 0;
                    frame.Bgra[offset + 3] = 255;
                    offset += 4;
                }
            }
        }
    }
}
