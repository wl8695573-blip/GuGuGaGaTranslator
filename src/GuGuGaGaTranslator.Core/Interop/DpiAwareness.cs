using System.Runtime.Versioning;
using System.Windows;

namespace GuGuGaGaTranslator.Core.Interop;

/// <summary>Process DPI awareness: without it Win32 geometry is DPI-virtualized while <c>BitBlt</c> works in physical pixels.</summary>
public static class DpiAwareness
{
    private static bool _applied;

    /// <summary>Opt this process into per-monitor-v2 awareness; safe to call repeatedly, and a host that already declared awareness keeps it.</summary>
    [SupportedOSPlatform("windows")]
    public static void EnablePerMonitorV2()
    {
        if (_applied)
            return;
        _applied = true;
        try
        {
            NativeMethods.SetProcessDpiAwarenessContext(NativeMethods.DpiAwarenessPerMonitorV2);
        }
        catch (EntryPointNotFoundException)
        {
            // Pre-1703 Windows: fall back to the system awareness declared in the manifest.
        }
    }

    /// <summary>The virtual screen rectangle covering every monitor, in physical pixels.</summary>
    [SupportedOSPlatform("windows")]
    public static Int32Rect VirtualScreen()
    {
        var x = NativeMethods.GetSystemMetrics(NativeMethods.SmXVirtualScreen);
        var y = NativeMethods.GetSystemMetrics(NativeMethods.SmYVirtualScreen);
        var width = NativeMethods.GetSystemMetrics(NativeMethods.SmCxVirtualScreen);
        var height = NativeMethods.GetSystemMetrics(NativeMethods.SmCyVirtualScreen);
        return new Int32Rect(x, y, width, height);
    }
}
