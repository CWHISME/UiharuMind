/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using UiharuMind.Core.AI.Execution.Tools.Accessibility;

namespace UiharuMind.Platform.Windows.Desktop;

/// <summary>
/// 键名（与浏览器同一套，见 BrowserKeys）→ Windows 虚拟键码。只给投递的按键消息用；真实键盘走 SharpHook
/// </summary>
internal static class WindowsKeys
{
    private static readonly Dictionary<string, ushort> Named = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Enter"] = 0x0D, ["NumpadEnter"] = 0x0D, ["Tab"] = 0x09, ["Space"] = 0x20, ["Backspace"] = 0x08, ["Escape"] = 0x1B,
        ["Delete"] = 0x2E, ["Insert"] = 0x2D, ["Home"] = 0x24, ["End"] = 0x23, ["PageUp"] = 0x21, ["PageDown"] = 0x22,
        ["ArrowLeft"] = 0x25, ["ArrowUp"] = 0x26, ["ArrowRight"] = 0x27, ["ArrowDown"] = 0x28,
        ["F1"] = 0x70, ["F2"] = 0x71, ["F3"] = 0x72, ["F4"] = 0x73, ["F5"] = 0x74, ["F6"] = 0x75, ["F7"] = 0x76,
        ["F8"] = 0x77, ["F9"] = 0x78, ["F10"] = 0x79, ["F11"] = 0x7A, ["F12"] = 0x7B,
    };

    // 美式键位上的符号
    private static readonly Dictionary<char, ushort> Symbols = new()
    {
        [';'] = 0xBA, ['='] = 0xBB, [','] = 0xBC, ['-'] = 0xBD, ['.'] = 0xBE, ['/'] = 0xBF, ['`'] = 0xC0,
        ['['] = 0xDB, ['\\'] = 0xDC, [']'] = 0xDD, ['\''] = 0xDE,
    };

    /// <summary>
    /// 一个键名换成虚拟键码（投递的按键带不上修饰键：应用按 GetKeyState 查的是真实键盘）
    /// </summary>
    /// <param name="keys">键名，修饰键在前、主键在最后</param>
    /// <returns>主键的虚拟键码</returns>
    /// <exception cref="AxActionException">带了修饰键，或认不出的键</exception>
    public static ushort Resolve(IReadOnlyList<string> keys)
    {
        if (keys.Count > 1) throw new AxActionException("Key combinations can't be sent to a background window on Windows. Try again with input \"real\".");
        string key = keys[^1];
        if (Named.TryGetValue(key, out ushort named)) return named;
        string bare = key.StartsWith("Key", StringComparison.Ordinal) && key.Length == 4 ? key[3..]
            : key.StartsWith("Digit", StringComparison.Ordinal) && key.Length == 6 ? key[5..]
            : key;
        if (bare.Length == 1)
        {
            char character = char.ToUpperInvariant(bare[0]);
            if (character is >= 'A' and <= 'Z' or >= '0' and <= '9') return character;
            if (Symbols.TryGetValue(character, out ushort symbol)) return symbol;
        }

        throw new AxActionException($"Unknown key \"{key}\". Use names like Enter, Tab, Escape, ArrowDown, F5, or a letter.");
    }
}
