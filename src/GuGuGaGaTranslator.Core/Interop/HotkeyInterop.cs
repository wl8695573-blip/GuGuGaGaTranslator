using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace GuGuGaGaTranslator.Core.Interop;

/// <summary>Global hotkeys: the tool is used while a game holds focus, so its controls have to work without the game losing input.</summary>
public static class HotkeyInterop
{
    public const uint ModAlt = 0x0001;

    public const uint ModControl = 0x0002;

    public const uint ModShift = 0x0004;

    /// <summary>MOD_NOREPEAT, so holding a key does not storm the handler.</summary>
    public const uint ModNoRepeat = 0x4000;

    public const int WmHotkey = NativeMethods.WmHotkey;

    /// <summary>Register a hotkey against a window handle; false when another app already owns it.</summary>
    [SupportedOSPlatform("windows")]
    public static bool Register(nint handle, int id, uint modifiers, uint virtualKey) =>
        NativeMethods.RegisterHotKey(handle, id, modifiers | ModNoRepeat, virtualKey);

    [SupportedOSPlatform("windows")]
    public static bool Unregister(nint handle, int id) =>
        NativeMethods.UnregisterHotKey(handle, id);

    /// <summary>The window the user is working in;「一键框选并翻译」adopts it so one key is enough to start.</summary>
    [SupportedOSPlatform("windows")]
    public static nint ForegroundWindow() => NativeMethods.GetForegroundWindow();
}
