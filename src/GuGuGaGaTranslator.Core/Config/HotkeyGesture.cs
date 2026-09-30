using System.Globalization;
using System.Text;
using GuGuGaGaTranslator.Core.Interop;

namespace GuGuGaGaTranslator.Core.Config;

/// <summary>A global hotkey written the way people write it: <c>Ctrl+Alt+T</c>. Parsing lives here rather
/// than in the shell so the configuration file stays hand-editable and the meaning of a gesture is the
/// same wherever it is shown.</summary>
public readonly record struct HotkeyGesture(uint Modifiers, uint VirtualKey)
{
    /// <summary>An unset gesture, used for "this action has no key".</summary>
    public static HotkeyGesture None => default;

    public bool IsEmpty => VirtualKey == 0;

    /// <summary>Only the four modifiers Windows itself understands.</summary>
    public uint RegisterModifiers => Modifiers & (HotkeyInterop.ModControl | HotkeyInterop.ModAlt | HotkeyInterop.ModShift | ModWin);

    public const uint ModWin = 0x0008;

    public static bool TryParse(string? text, out HotkeyGesture gesture)
    {
        gesture = None;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return false;

        uint modifiers = 0;
        var key = 0u;
        foreach (var part in parts)
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl" or "control":
                    modifiers |= HotkeyInterop.ModControl;
                    continue;
                case "alt":
                    modifiers |= HotkeyInterop.ModAlt;
                    continue;
                case "shift":
                    modifiers |= HotkeyInterop.ModShift;
                    continue;
                case "win" or "windows" or "meta" or "super":
                    modifiers |= ModWin;
                    continue;
            }

            var virtualKey = VirtualKeyOf(part);
            if (virtualKey == 0) return false;

            // 「Ctrl+A+B」这种按不出两个主键,判为写错而不是悄悄丢掉一个。
            if (key != 0) return false;
            key = virtualKey;
        }

        if (key == 0) return false;
        gesture = new HotkeyGesture(modifiers, key);
        return true;
    }

    public static HotkeyGesture ParseOrDefault(string? text, HotkeyGesture fallback) =>
        TryParse(text, out var gesture) ? gesture : fallback;

    /// <summary>Render a gesture in the canonical order, so the same combination always reads the same way.</summary>
    public static string Format(uint modifiers, uint virtualKey)
    {
        if (virtualKey == 0) return string.Empty;

        var builder = new StringBuilder();
        if ((modifiers & HotkeyInterop.ModControl) != 0) builder.Append("Ctrl+");
        if ((modifiers & HotkeyInterop.ModAlt) != 0) builder.Append("Alt+");
        if ((modifiers & HotkeyInterop.ModShift) != 0) builder.Append("Shift+");
        if ((modifiers & ModWin) != 0) builder.Append("Win+");
        builder.Append(NameOfVirtualKey(virtualKey) ?? $"0x{virtualKey:X2}");
        return builder.ToString();
    }

    public override string ToString() => Format(Modifiers, VirtualKey);

    /// <summary>The names a person would type for a key, including the aliases the keyboard uses.</summary>
    public static uint VirtualKeyOf(string name)
    {
        var trimmed = name.Trim();
        if (trimmed.Length == 0) return 0;

        if (trimmed.Length == 1)
        {
            var character = char.ToUpperInvariant(trimmed[0]);
            if (character is >= 'A' and <= 'Z' or >= '0' and <= '9') return character;
            return character switch
            {
                ';' => 0xBA,
                '=' => 0xBB,
                ',' => 0xBC,
                '-' => 0xBD,
                '.' => 0xBE,
                '/' => 0xBF,
                '`' => 0xC0,
                '[' => 0xDB,
                '\\' => 0xDC,
                ']' => 0xDD,
                '\'' => 0xDE,
                _ => 0,
            };
        }

        var upper = trimmed.ToUpperInvariant();
        if (upper.Length is 2 or 3 && upper[0] == 'F'
            && int.TryParse(upper[1..], NumberStyles.None, CultureInfo.InvariantCulture, out var function)
            && function is >= 1 and <= 24)
        {
            return (uint)(0x70 + function - 1);
        }

        return upper switch
        {
            "SPACE" or "SPACEBAR" => 0x20,
            "ENTER" or "RETURN" => 0x0D,
            "TAB" => 0x09,
            "ESC" or "ESCAPE" => 0x1B,
            "BACKSPACE" or "BACK" => 0x08,
            "DELETE" or "DEL" => 0x2E,
            "INSERT" or "INS" => 0x2D,
            "HOME" => 0x24,
            "END" => 0x23,
            "PAGEUP" or "PGUP" => 0x21,
            "PAGEDOWN" or "PGDN" => 0x22,
            "UP" => 0x26,
            "DOWN" => 0x28,
            "LEFT" => 0x25,
            "RIGHT" => 0x27,
            "NUMPAD0" or "NUM0" => 0x60,
            "NUMPAD1" or "NUM1" => 0x61,
            "NUMPAD2" or "NUM2" => 0x62,
            "NUMPAD3" or "NUM3" => 0x63,
            "NUMPAD4" or "NUM4" => 0x64,
            "NUMPAD5" or "NUM5" => 0x65,
            "NUMPAD6" or "NUM6" => 0x66,
            "NUMPAD7" or "NUM7" => 0x67,
            "NUMPAD8" or "NUM8" => 0x68,
            "NUMPAD9" or "NUM9" => 0x69,
            "MULTIPLY" => 0x6A,
            "ADD" or "PLUS" => 0x6B,
            "SUBTRACT" or "MINUS" => 0x6D,
            "DECIMAL" => 0x6E,
            "DIVIDE" => 0x6F,
            _ => 0,
        };
    }

    public static string? NameOfVirtualKey(uint virtualKey)
    {
        if (virtualKey is >= 'A' and <= 'Z' or >= '0' and <= '9') return ((char)virtualKey).ToString();
        if (virtualKey is >= 0x70 and <= 0x87) return "F" + (virtualKey - 0x70 + 1).ToString(CultureInfo.InvariantCulture);

        return virtualKey switch
        {
            0x20 => "Space",
            0x0D => "Enter",
            0x09 => "Tab",
            0x1B => "Esc",
            0x08 => "Backspace",
            0x2E => "Delete",
            0x2D => "Insert",
            0x24 => "Home",
            0x23 => "End",
            0x21 => "PageUp",
            0x22 => "PageDown",
            0x26 => "Up",
            0x28 => "Down",
            0x25 => "Left",
            0x27 => "Right",
            0xBA => ";",
            0xBB => "=",
            0xBC => ",",
            0xBD => "-",
            0xBE => ".",
            0xBF => "/",
            0xC0 => "`",
            0xDB => "[",
            0xDC => "\\",
            0xDD => "]",
            0xDE => "'",
            >= 0x60 and <= 0x69 => "Num" + (virtualKey - 0x60).ToString(CultureInfo.InvariantCulture),
            0x6A => "Multiply",
            0x6B => "Add",
            0x6D => "Subtract",
            0x6E => "Decimal",
            0x6F => "Divide",
            _ => null,
        };
    }
}
