using System.Runtime.Versioning;
using System.Windows;
using GuGuGaGaTranslator.Core.Interop;

namespace GuGuGaGaTranslator.Core.Capture;

/// <summary>获取 OCR 输入中需要遮盖的自身窗口区域；已被系统排除捕获的窗口无需再次遮盖。</summary>
public static class SelfWindowMask
{
    [SupportedOSPlatform("windows")]
    public static IReadOnlyList<Int32Rect> ScreenRects(nint targetHandle = 0)
    {
        var processId = (uint)Environment.ProcessId;
        var rects = new List<Int32Rect>();
        var foundTarget = targetHandle == 0;

        NativeMethods.EnumWindows((hwnd, _) =>
        {
            // EnumWindows enumerates top-level windows from front to back.
            if (targetHandle != 0 && hwnd == targetHandle)
            {
                foundTarget = true;
                return false;
            }
            if (NativeMethods.IsIconic(hwnd))
                return true;
            if (NativeMethods.DwmGetWindowAttribute(hwnd, NativeMethods.DwmwaCloaked, out var cloaked, sizeof(int)) == 0 && cloaked != 0)
                return true;
            NativeMethods.GetWindowThreadProcessId(hwnd, out var owner);
            if (owner != processId)
                return true;
            if (!NativeMethods.IsWindowVisible(hwnd))
                return true;

            // 读不到亲和性时宁可遮掉:让自家文字混进 OCR 比多遮一块更糟。
            if (NativeMethods.GetWindowDisplayAffinity(hwnd, out var affinity) && affinity != NativeMethods.WdaNone)
                return true;

            if (!WindowEnumerator.TryReadFrameRect(hwnd, out var rect))
                return true;
            if (rect.Width <= 0 || rect.Height <= 0)
                return true;

            rects.Add(rect);
            return true;
        }, 0);

        return foundTarget ? rects : [];
    }

    /// <summary>Blank every pixel of a frame that falls under one of this tool's own windows.</summary>
    [SupportedOSPlatform("windows")]
    public static void Apply(Frame frame) => Apply(frame, ScreenRects());

    /// <summary>Blank the given screen rectangles out of a frame, translating screen coordinates into
    /// frame-local ones through the frame's own origin.</summary>
    public static void Apply(Frame frame, IReadOnlyList<Int32Rect> screenRects)
    {
        if (screenRects.Count == 0)
            return;

        var origin = frame.SourceRegion;
        foreach (var rect in screenRects)
        {
            var left = Math.Max(0, rect.X - origin.X);
            var top = Math.Max(0, rect.Y - origin.Y);
            var right = Math.Min(frame.Width, rect.X + rect.Width - origin.X);
            var bottom = Math.Min(frame.Height, rect.Y + rect.Height - origin.Y);
            if (right <= left || bottom <= top)
                continue;

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
