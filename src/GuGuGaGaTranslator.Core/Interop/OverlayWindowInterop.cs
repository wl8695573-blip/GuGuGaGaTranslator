using System.Runtime.Versioning;

namespace GuGuGaGaTranslator.Core.Interop;

/// <summary>应用鼠标穿透和窗口捕获排除标记。</summary>
public static class OverlayWindowInterop
{
    public static System.Windows.Int32Rect WorkAreaAt(System.Windows.Int32Rect region)
    {
        var rect = new NativeMethods.RECT
        {
            Left = region.X,
            Top = region.Y,
            Right = region.X + region.Width,
            Bottom = region.Y + region.Height,
        };
        var monitor = NativeMethods.MonitorFromRect(in rect, 2);
        var info = new NativeMethods.MONITORINFO { Size = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        return monitor != 0 && NativeMethods.GetMonitorInfoW(monitor, ref info)
            ? new System.Windows.Int32Rect(info.Work.Left, info.Work.Top, info.Work.Right - info.Work.Left, info.Work.Bottom - info.Work.Top)
            : DpiAwareness.VirtualScreen();
    }

    /// <summary>Toggle click-through. Windows are made layered as well, which is what lets a WPF window with a transparent background composite correctly.</summary>
    [SupportedOSPlatform("windows")]
    public static bool ApplyClickThrough(nint handle, bool enabled)
    {
        if (handle == 0)
            return false;

        var style = NativeMethods.GetWindowLongW(handle, NativeMethods.GwlExStyle);
        var updated = enabled
            ? style | NativeMethods.WsExTransparent | NativeMethods.WsExLayered | NativeMethods.WsExNoActivate
            : style & ~(NativeMethods.WsExTransparent | NativeMethods.WsExNoActivate);

        if (updated == style)
            return true;
        return NativeMethods.SetWindowLongPtrW(handle, NativeMethods.GwlExStyle, updated) != 0;
    }

    /// <summary>Exclude a window from screen capture, so the overlay's own text is not captured and re-recognized as source text.</summary>
    [SupportedOSPlatform("windows")]
    public static bool ExcludeFromCapture(nint handle) => ApplyDisplayAffinity(handle, exclude: true);

    /// <summary>Put a window back into screen captures. Both directions are needed: WDA_EXCLUDEFROMCAPTURE is a
    /// property of the window, so a window that was excluded once keeps that flag until it is cleared here.</summary>
    [SupportedOSPlatform("windows")]
    public static bool ApplyDisplayAffinity(nint handle, bool exclude) =>
        handle != 0 && NativeMethods.SetWindowDisplayAffinity(
            handle,
            exclude ? NativeMethods.WdaExcludeFromCapture : NativeMethods.WdaNone);

    /// <summary>Move a window to a physical-pixel position without resizing. WPF's Left/Top are
    /// device-independent and resolve against the primary monitor's scale, which drifts from the
    /// captured pixels on a mixed-DPI desktop.</summary>
    [SupportedOSPlatform("windows")]
    public static bool MoveTo(nint handle, int x, int y, bool topmost = true)
    {
        if (handle == 0)
            return false;
        var flags = NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate | NativeMethods.SwpShowWindow;
        return NativeMethods.SetWindowPos(handle, topmost ? NativeMethods.HwndTopmost : 0, x, y, 0, 0, flags);
    }
}
