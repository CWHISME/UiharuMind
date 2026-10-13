/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using UiharuMind.Core.AI.Execution.Tools.Accessibility;

namespace UiharuMind.Core.AI.Execution.Tools.Desktop.Mac;

/// <summary>
/// 键名（与浏览器同一套，见 BrowserKeys）→ macOS 虚拟键码与修饰键标志
/// </summary>
internal static class MacKeys
{
    public const ulong FlagShift = 0x20000;
    public const ulong FlagControl = 0x40000;
    public const ulong FlagOption = 0x80000;
    public const ulong FlagCommand = 0x100000;

    public const ushort KeyA = 0;

    // 修饰键自己的虚拟键码（发 down/up 事件用；真实键鼠时必须显式按下再松开，
    // 只把 flags 挂在主键事件上会让系统认为修饰键一直按着）
    public const ushort KeyCommand = 55;
    public const ushort KeyShift = 56;
    public const ushort KeyOption = 58;
    public const ushort KeyControl = 59;

    private static readonly Dictionary<string, ushort> Named = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Enter"] = 36, ["NumpadEnter"] = 76, ["Tab"] = 48, ["Space"] = 49, ["Backspace"] = 51, ["Escape"] = 53,
        ["Delete"] = 117, ["Home"] = 115, ["End"] = 119, ["PageUp"] = 116, ["PageDown"] = 121,
        ["ArrowLeft"] = 123, ["ArrowRight"] = 124, ["ArrowDown"] = 125, ["ArrowUp"] = 126,
        ["F1"] = 122, ["F2"] = 120, ["F3"] = 99, ["F4"] = 118, ["F5"] = 96, ["F6"] = 97, ["F7"] = 98, ["F8"] = 100,
        ["F9"] = 101, ["F10"] = 109, ["F11"] = 103, ["F12"] = 111,
    };

    // ANSI 键位：字母、数字与符号
    private static readonly Dictionary<char, ushort> Characters = new()
    {
        ['a'] = 0, ['s'] = 1, ['d'] = 2, ['f'] = 3, ['h'] = 4, ['g'] = 5, ['z'] = 6, ['x'] = 7, ['c'] = 8, ['v'] = 9,
        ['b'] = 11, ['q'] = 12, ['w'] = 13, ['e'] = 14, ['r'] = 15, ['y'] = 16, ['t'] = 17, ['1'] = 18, ['2'] = 19,
        ['3'] = 20, ['4'] = 21, ['6'] = 22, ['5'] = 23, ['='] = 24, ['9'] = 25, ['7'] = 26, ['-'] = 27, ['8'] = 28,
        ['0'] = 29, [']'] = 30, ['o'] = 31, ['u'] = 32, ['['] = 33, ['i'] = 34, ['p'] = 35, ['l'] = 37, ['j'] = 38,
        ['\''] = 39, ['k'] = 40, [';'] = 41, ['\\'] = 42, [','] = 43, ['/'] = 44, ['n'] = 45, ['m'] = 46, ['.'] = 47,
        ['`'] = 50,
    };

    /// <summary>
    /// 一组键名拆成主键码与修饰键标志
    /// </summary>
    /// <param name="keys">键名，修饰键在前、主键在最后</param>
    /// <returns>主键码与标志</returns>
    /// <exception cref="AxActionException">认不出的键</exception>
    public static (ushort KeyCode, ulong Flags) Resolve(IReadOnlyList<string> keys)
    {
        ulong flags = 0;
        foreach (string modifier in keys.Take(keys.Count - 1))
        {
            flags |= modifier switch
            {
                "Shift" => FlagShift,
                "Control" => FlagControl,
                "Alt" => FlagOption,
                "Meta" => FlagCommand,
                _ => throw new AxActionException($"\"{modifier}\" is not a modifier key; put Cmd, Ctrl, Alt or Shift before the key."),
            };
        }

        string key = keys[^1];
        if (Named.TryGetValue(key, out ushort named)) return (named, flags);
        string bare = key.StartsWith("Key", StringComparison.Ordinal) && key.Length == 4 ? key[3..]
            : key.StartsWith("Digit", StringComparison.Ordinal) && key.Length == 6 ? key[5..]
            : key;
        if (bare.Length == 1 && Characters.TryGetValue(char.ToLowerInvariant(bare[0]), out ushort code))
        {
            return (code, flags);
        }

        throw new AxActionException($"Unknown key \"{key}\". Use names like Enter, Tab, Escape, ArrowDown, F5, or a letter.");
    }

    /// <summary>
    /// 修饰键按下的完整序列（键码 + 按下到这一步后的累计标志）：Cmd → Shift → Option → Control。
    /// 真实键鼠时必须逐个显式 down、再逆序 up，只把 flags 挂在主键事件上会让系统以为修饰键一直按着
    /// </summary>
    /// <param name="flags">要按的修饰键</param>
    /// <returns>按下顺序的键码与累计标志；释放顺序取反</returns>
    public static IReadOnlyList<(ushort KeyCode, ulong Flags)> ModifierDowns(ulong flags)
    {
        List<(ushort, ulong)> downs = new();
        ulong acc = 0;
        void Add(ushort code, ulong flag)
        {
            if ((flags & flag) == 0) return;
            acc |= flag;
            downs.Add((code, acc));
        }

        Add(KeyCommand, FlagCommand);
        Add(KeyShift, FlagShift);
        Add(KeyOption, FlagOption);
        Add(KeyControl, FlagControl);
        return downs;
    }
}
