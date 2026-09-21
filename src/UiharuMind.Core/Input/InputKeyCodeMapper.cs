using SharpHook.Data;

namespace UiharuMind.Core.Input;

public static class InputKeyCodeMapper
{
    public static KeyCode CharToKeyCode(char c)
    {
        return c switch
        {
            >= 'a' and <= 'z' => (KeyCode)((int)KeyCode.VcA + (c - 'a')),
            >= 'A' and <= 'Z' => (KeyCode)((int)KeyCode.VcA + (c - 'A')),
            >= '0' and <= '9' => (KeyCode)((int)KeyCode.Vc0 + (c - '0')),
            ' ' => KeyCode.VcSpace,
            '.' => KeyCode.VcPeriod,
            ',' => KeyCode.VcComma,
            ';' => KeyCode.VcSemicolon,
            '\'' => KeyCode.VcQuote,
            '[' => KeyCode.VcOpenBracket,
            ']' => KeyCode.VcCloseBracket,
            '\\' => KeyCode.VcBackslash,
            '/' => KeyCode.VcSlash,
            '-' => KeyCode.VcMinus,
            '=' => KeyCode.VcEquals,
            _ => KeyCode.VcUndefined
        };
    }

    public static KeyCode FromWindowsVirtualKey(int virtualKey)
    {
        if (virtualKey >= 'A' && virtualKey <= 'Z')
        {
            return (KeyCode)((int)KeyCode.VcA + (virtualKey - 'A'));
        }

        if (virtualKey >= '0' && virtualKey <= '9')
        {
            return (KeyCode)((int)KeyCode.Vc0 + (virtualKey - '0'));
        }

        return virtualKey switch
        {
            0x01 => KeyCode.VcEscape,
            0x08 => KeyCode.VcBackspace,
            0x09 => KeyCode.VcTab,
            0x0D => KeyCode.VcEnter,
            0x10 => KeyCode.VcLeftShift,
            0x11 => KeyCode.VcLeftControl,
            0x12 => KeyCode.VcLeftAlt,
            0x14 => KeyCode.VcCapsLock,
            0x1B => KeyCode.VcEscape,
            0x20 => KeyCode.VcSpace,
            0x21 => KeyCode.VcPageUp,
            0x22 => KeyCode.VcPageDown,
            0x23 => KeyCode.VcEnd,
            0x24 => KeyCode.VcHome,
            0x25 => KeyCode.VcLeft,
            0x26 => KeyCode.VcUp,
            0x27 => KeyCode.VcRight,
            0x28 => KeyCode.VcDown,
            0x2D => KeyCode.VcInsert,
            0x2E => KeyCode.VcDelete,
            0x5B => KeyCode.VcLeftMeta,
            0x5C => KeyCode.VcRightMeta,
            0x60 => KeyCode.VcNumPad0,
            0x61 => KeyCode.VcNumPad1,
            0x62 => KeyCode.VcNumPad2,
            0x63 => KeyCode.VcNumPad3,
            0x64 => KeyCode.VcNumPad4,
            0x65 => KeyCode.VcNumPad5,
            0x66 => KeyCode.VcNumPad6,
            0x67 => KeyCode.VcNumPad7,
            0x68 => KeyCode.VcNumPad8,
            0x69 => KeyCode.VcNumPad9,
            0x6A => KeyCode.VcNumPadMultiply,
            0x6B => KeyCode.VcNumPadAdd,
            0x6D => KeyCode.VcNumPadSubtract,
            0x6E => KeyCode.VcNumPadDecimal,
            0x6F => KeyCode.VcNumPadDivide,
            >= 0x70 and <= 0x7B => (KeyCode)((int)KeyCode.VcF1 + (virtualKey - 0x70)),
            0xA0 => KeyCode.VcLeftShift,
            0xA1 => KeyCode.VcRightShift,
            0xA2 => KeyCode.VcLeftControl,
            0xA3 => KeyCode.VcRightControl,
            0xA4 => KeyCode.VcLeftAlt,
            0xA5 => KeyCode.VcRightAlt,
            0xBA => KeyCode.VcSemicolon,
            0xBB => KeyCode.VcEquals,
            0xBC => KeyCode.VcComma,
            0xBD => KeyCode.VcMinus,
            0xBE => KeyCode.VcPeriod,
            0xBF => KeyCode.VcSlash,
            0xC0 => KeyCode.VcBackQuote,
            0xDB => KeyCode.VcOpenBracket,
            0xDC => KeyCode.VcBackslash,
            0xDD => KeyCode.VcCloseBracket,
            0xDE => KeyCode.VcQuote,
            _ => KeyCode.VcUndefined
        };
    }

    public static ushort ToWindowsVirtualKey(KeyCode keyCode)
    {
        var name = keyCode.ToString();
        if (name.Length == 3 && name.StartsWith("Vc") && name[2] is >= 'A' and <= 'Z')
        {
            return name[2];
        }

        if (name.StartsWith("VcF") && int.TryParse(name[3..], out var functionKey))
        {
            return (ushort)(0x70 + functionKey - 1);
        }

        return keyCode switch
        {
            KeyCode.Vc0 => 0x30,
            KeyCode.Vc1 => 0x31,
            KeyCode.Vc2 => 0x32,
            KeyCode.Vc3 => 0x33,
            KeyCode.Vc4 => 0x34,
            KeyCode.Vc5 => 0x35,
            KeyCode.Vc6 => 0x36,
            KeyCode.Vc7 => 0x37,
            KeyCode.Vc8 => 0x38,
            KeyCode.Vc9 => 0x39,
            KeyCode.VcEscape => 0x1B,
            KeyCode.VcBackspace => 0x08,
            KeyCode.VcTab => 0x09,
            KeyCode.VcEnter => 0x0D,
            KeyCode.VcSpace => 0x20,
            KeyCode.VcPageUp => 0x21,
            KeyCode.VcPageDown => 0x22,
            KeyCode.VcEnd => 0x23,
            KeyCode.VcHome => 0x24,
            KeyCode.VcLeft => 0x25,
            KeyCode.VcUp => 0x26,
            KeyCode.VcRight => 0x27,
            KeyCode.VcDown => 0x28,
            KeyCode.VcInsert => 0x2D,
            KeyCode.VcDelete => 0x2E,
            KeyCode.VcLeftShift => 0xA0,
            KeyCode.VcRightShift => 0xA1,
            KeyCode.VcLeftControl => 0xA2,
            KeyCode.VcRightControl => 0xA3,
            KeyCode.VcLeftAlt => 0xA4,
            KeyCode.VcRightAlt => 0xA5,
            KeyCode.VcLeftMeta => 0x5B,
            KeyCode.VcRightMeta => 0x5C,
            KeyCode.VcNumPad0 => 0x60,
            KeyCode.VcNumPad1 => 0x61,
            KeyCode.VcNumPad2 => 0x62,
            KeyCode.VcNumPad3 => 0x63,
            KeyCode.VcNumPad4 => 0x64,
            KeyCode.VcNumPad5 => 0x65,
            KeyCode.VcNumPad6 => 0x66,
            KeyCode.VcNumPad7 => 0x67,
            KeyCode.VcNumPad8 => 0x68,
            KeyCode.VcNumPad9 => 0x69,
            KeyCode.VcNumPadMultiply => 0x6A,
            KeyCode.VcNumPadAdd => 0x6B,
            KeyCode.VcNumPadSubtract => 0x6D,
            KeyCode.VcNumPadDecimal => 0x6E,
            KeyCode.VcNumPadDivide => 0x6F,
            KeyCode.VcSemicolon => 0xBA,
            KeyCode.VcEquals => 0xBB,
            KeyCode.VcComma => 0xBC,
            KeyCode.VcMinus => 0xBD,
            KeyCode.VcPeriod => 0xBE,
            KeyCode.VcSlash => 0xBF,
            KeyCode.VcBackQuote => 0xC0,
            KeyCode.VcOpenBracket => 0xDB,
            KeyCode.VcBackslash => 0xDC,
            KeyCode.VcCloseBracket => 0xDD,
            KeyCode.VcQuote => 0xDE,
            _ => 0
        };
    }
}
