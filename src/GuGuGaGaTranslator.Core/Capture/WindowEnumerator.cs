using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Windows;
using GuGuGaGaTranslator.Core.Interop;

namespace GuGuGaGaTranslator.Core.Capture;

/// <summary>Enumerates the top-level windows a person would recognize: visible, titled, not shell-cloaked, with a real client area.</summary>
public static class WindowEnumerator
{
    /// <summary>List the candidate target windows, ordered by process then title so the order is stable between refreshes.</summary>
    [SupportedOSPlatform("windows")]
    public static IReadOnlyList<WindowInfo> List(uint? excludeProcessId = null)
    {
        var found = new List<WindowInfo>();
        var processNames = new Dictionary<uint, string>();

        NativeMethods.EnumWindows((hwnd, _) =>
        {
            var info = Describe(hwnd, excludeProcessId, processNames);
            if (info is not null) found.Add(info);
            return true;
        }, 0);

        return found
            .OrderBy(w => w.ProcessName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(w => w.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>Re-read one window's geometry, so a region defined against a client area follows the window as it moves.</summary>
    [SupportedOSPlatform("windows")]
    public static WindowInfo? TryDescribe(nint handle)
    {
        if (handle == 0 || !NativeMethods.IsWindow(handle)) return null;
        return Describe(handle, null, new Dictionary<uint, string>(), requireVisible: false);
    }

    /// <summary>Find the window that best matches a persisted identity, so a saved profile survives the target restarting.</summary>
    [SupportedOSPlatform("windows")]
    public static WindowInfo? FindByIdentity(string identity, uint? excludeProcessId = null) =>
        List(excludeProcessId).FirstOrDefault(w => w.Identity == identity);

    private static WindowInfo? Describe(
        nint hwnd,
        uint? excludeProcessId,
        Dictionary<uint, string> processNames,
        bool requireVisible = true)
    {
        if (requireVisible && !NativeMethods.IsWindowVisible(hwnd)) return null;

        // A cloaked window is on the desktop but not on screen (another virtual
        // desktop, or a suspended UWP shell window): capturing it yields black.
        if (NativeMethods.DwmGetWindowAttribute(hwnd, NativeMethods.DwmwaCloaked, out var cloaked, sizeof(int)) == 0 && cloaked != 0)
            return null;

        // Tool windows are palettes and floating helpers, never translation targets.
        var exStyle = NativeMethods.GetWindowLongW(hwnd, NativeMethods.GwlExStyle);
        if ((exStyle & NativeMethods.WsExToolWindow) != 0) return null;

        var title = ReadTitle(hwnd);
        if (string.IsNullOrWhiteSpace(title)) return null;

        NativeMethods.GetWindowThreadProcessId(hwnd, out var processId);
        if (excludeProcessId is not null && processId == excludeProcessId.Value) return null;

        if (!TryReadClientRect(hwnd, out var clientRect)) return null;
        if (!TryReadFrameRect(hwnd, out var frameRect)) return null;
        if (clientRect.Width <= 0 || clientRect.Height <= 0) return null;

        if (!processNames.TryGetValue(processId, out var processName))
        {
            processName = ReadProcessName(processId);
            processNames[processId] = processName;
        }

        return new WindowInfo
        {
            Handle = hwnd,
            Title = title,
            ClassName = ReadClassName(hwnd),
            ProcessId = processId,
            ProcessName = processName,
            ClientRect = clientRect,
            FrameRect = frameRect,
            IsMinimized = NativeMethods.IsIconic(hwnd),
            IsOwned = NativeMethods.GetWindow(hwnd, NativeMethods.GwOwner) != 0,
        };
    }

    [SupportedOSPlatform("windows")]
    public static bool TryReadClientRect(nint hwnd, out Int32Rect rect)
    {
        rect = default;
        if (!NativeMethods.GetClientRect(hwnd, out var client)) return false;
        var origin = new NativeMethods.POINT { X = 0, Y = 0 };
        if (!NativeMethods.ClientToScreen(hwnd, ref origin)) return false;
        rect = new Int32Rect(origin.X, origin.Y, client.Right - client.Left, client.Bottom - client.Top);
        return true;
    }

    /// <summary>Read the whole window rectangle, preferring the DWM's visible bounds over GetWindowRect's.</summary>
    [SupportedOSPlatform("windows")]
    public static bool TryReadFrameRect(nint hwnd, out Int32Rect rect)
    {
        rect = default;
        if (NativeMethods.DwmGetWindowAttributeRect(hwnd, NativeMethods.DwmwaExtendedFrameBounds, out var extended, Marshal.SizeOf<NativeMethods.RECT>()) == 0)
        {
            rect = new Int32Rect(extended.Left, extended.Top, extended.Right - extended.Left, extended.Bottom - extended.Top);
            return true;
        }

        if (!NativeMethods.GetWindowRect(hwnd, out var window)) return false;
        rect = new Int32Rect(window.Left, window.Top, window.Right - window.Left, window.Bottom - window.Top);
        return true;
    }

    private static string ReadTitle(nint hwnd)
    {
        var length = NativeMethods.GetWindowTextLengthW(hwnd);
        if (length <= 0) return string.Empty;
        var buffer = new char[length + 1];
        var copied = NativeMethods.GetWindowTextW(hwnd, buffer, buffer.Length);
        return new string(buffer, 0, Math.Max(copied, 0));
    }

    private static string ReadClassName(nint hwnd)
    {
        var buffer = new char[256];
        var copied = NativeMethods.GetClassNameW(hwnd, buffer, buffer.Length);
        return new string(buffer, 0, Math.Max(copied, 0));
    }

    private static string ReadProcessName(uint processId)
    {
        try
        {
            using var process = Process.GetProcessById((int)processId);
            return process.ProcessName;
        }
        catch (ArgumentException)
        {
            // The process exited between enumeration and this read.
            return $"pid-{processId}";
        }
        catch (InvalidOperationException)
        {
            return $"pid-{processId}";
        }
    }

    public static string ToDisplayList(IEnumerable<WindowInfo> windows)
    {
        var builder = new StringBuilder();
        foreach (var window in windows) builder.AppendLine(window.Display);
        return builder.ToString();
    }
}
