using System.Runtime.Versioning;

namespace GuGuGaGaTranslator.Core.Interop;

/// <summary>The two window-style tricks an overlay needs: let mouse input fall through to the game
/// underneath, and keep the overlay out of screen captures so the pipeline cannot read its own
/// translation back as source text.</summary>
public static class OverlayWindowInterop
{
    /// <summary>Toggle click-through. Windows are made layered as well, which is what lets a WPF window with a transparent background composite correctly.</summary>
    [SupportedOSPlatform("windows")]
    public static bool ApplyClickThrough(nint handle, bool enabled)
    {
        if (handle == 0) return false;

        var style = NativeMethods.GetWindowLongW(handle, NativeMethods.GwlExStyle);
        var updated = enabled
            ? style | NativeMethods.WsExTransparent | NativeMethods.WsExLayered | NativeMethods.WsExNoActivate
            : style & ~(NativeMethods.WsExTransparent | NativeMethods.WsExNoActivate);

        if (updated == style) return true;
        return NativeMethods.SetWindowLongPtrW(handle, NativeMethods.GwlExStyle, updated) != 0;
    }

    /// <summary>Exclude a window from screen capture, so the overlay's own text is not captured and re-recognized as source text.</summary>
    [SupportedOSPlatform("windows")]
    public static bool ExcludeFromCapture(nint handle) =>
        handle != 0 && NativeMethods.SetWindowDisplayAffinity(handle, NativeMethods.WdaExcludeFromCapture);

    /// <summary>Move a window to a physical-pixel position without resizing. WPF's Left/Top are
    /// device-independent and resolve against the primary monitor's scale, which drifts from the
    /// captured pixels on a mixed-DPI desktop.</summary>
    [SupportedOSPlatform("windows")]
    public static bool MoveTo(nint handle, int x, int y, bool topmost = true)
    {
        if (handle == 0) return false;
        var flags = NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate | NativeMethods.SwpShowWindow;
        return NativeMethods.SetWindowPos(handle, topmost ? NativeMethods.HwndTopmost : 0, x, y, 0, 0, flags);
    }
}
