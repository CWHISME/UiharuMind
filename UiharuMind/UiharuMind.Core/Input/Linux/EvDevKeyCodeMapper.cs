using SharpHook.Data;

namespace UiharuMind.Core.Input.Linux;

/// <summary>
/// Linux 内核键码（uapi/linux/input-event-codes.h 的 KEY_*/BTN_*）与 SharpHook 键码之间的双向映射。
/// 全应用以 SharpHook.Data 的枚举作为输入领域的通用类型，Linux 后端在边界上完成翻译。
/// </summary>
internal static class EvDevKeyCodeMapper
{
    private static readonly Dictionary<ushort, KeyCode> ToKeyCodeMap = BuildToKeyCodeMap();
    private static readonly Dictionary<KeyCode, ushort> ToEvDevMap = BuildToEvDevMap();

    /// <summary>可注入的键码全集，创建 uinput 虚拟键盘时逐个 UI_SET_KEYBIT</summary>
    public static IEnumerable<ushort> AllKeyCodes => ToKeyCodeMap.Keys;

    /// <summary>
    /// 内核键码转 SharpHook 键码
    /// </summary>
    /// <param name="evDevCode">内核 KEY_* 值</param>
    /// <returns>映射结果，未知键返回 VcUndefined</returns>
    public static KeyCode ToKeyCode(ushort evDevCode)
    {
        return ToKeyCodeMap.GetValueOrDefault(evDevCode, KeyCode.VcUndefined);
    }

    /// <summary>
    /// SharpHook 键码转内核键码
    /// </summary>
    /// <param name="keyCode">SharpHook 键码</param>
    /// <returns>内核 KEY_* 值，无对应时返回 0</returns>
    public static ushort ToEvDevCode(KeyCode keyCode)
    {
        return ToEvDevMap.GetValueOrDefault(keyCode, (ushort)0);
    }

    /// <summary>
    /// 内核鼠标按键码转 SharpHook 鼠标按键
    /// </summary>
    /// <param name="evDevCode">内核 BTN_* 值</param>
    /// <returns>映射结果，未知按键返回 NoButton</returns>
    public static MouseButton ToMouseButton(ushort evDevCode)
    {
        return evDevCode switch
        {
            LinuxInputNative.BtnLeft => MouseButton.Button1,
            LinuxInputNative.BtnRight => MouseButton.Button2,
            LinuxInputNative.BtnMiddle => MouseButton.Button3,
            LinuxInputNative.BtnSide => MouseButton.Button4,
            LinuxInputNative.BtnExtra => MouseButton.Button5,
            _ => MouseButton.NoButton
        };
    }

    /// <summary>
    /// SharpHook 鼠标按键转内核按键码
    /// </summary>
    /// <param name="button">SharpHook 鼠标按键</param>
    /// <returns>内核 BTN_* 值，无对应时返回 0</returns>
    public static ushort ToEvDevButton(MouseButton button)
    {
        return button switch
        {
            MouseButton.Button1 => LinuxInputNative.BtnLeft,
            MouseButton.Button2 => LinuxInputNative.BtnRight,
            MouseButton.Button3 => LinuxInputNative.BtnMiddle,
            MouseButton.Button4 => LinuxInputNative.BtnSide,
            MouseButton.Button5 => LinuxInputNative.BtnExtra,
            _ => 0
        };
    }

    private static Dictionary<ushort, KeyCode> BuildToKeyCodeMap()
    {
        return new Dictionary<ushort, KeyCode>
        {
            [1] = KeyCode.VcEscape,
            [2] = KeyCode.Vc1, [3] = KeyCode.Vc2, [4] = KeyCode.Vc3, [5] = KeyCode.Vc4, [6] = KeyCode.Vc5,
            [7] = KeyCode.Vc6, [8] = KeyCode.Vc7, [9] = KeyCode.Vc8, [10] = KeyCode.Vc9, [11] = KeyCode.Vc0,
            [12] = KeyCode.VcMinus, [13] = KeyCode.VcEquals, [14] = KeyCode.VcBackspace, [15] = KeyCode.VcTab,
            [16] = KeyCode.VcQ, [17] = KeyCode.VcW, [18] = KeyCode.VcE, [19] = KeyCode.VcR, [20] = KeyCode.VcT,
            [21] = KeyCode.VcY, [22] = KeyCode.VcU, [23] = KeyCode.VcI, [24] = KeyCode.VcO, [25] = KeyCode.VcP,
            [26] = KeyCode.VcOpenBracket, [27] = KeyCode.VcCloseBracket, [28] = KeyCode.VcEnter,
            [29] = KeyCode.VcLeftControl,
            [30] = KeyCode.VcA, [31] = KeyCode.VcS, [32] = KeyCode.VcD, [33] = KeyCode.VcF, [34] = KeyCode.VcG,
            [35] = KeyCode.VcH, [36] = KeyCode.VcJ, [37] = KeyCode.VcK, [38] = KeyCode.VcL,
            [39] = KeyCode.VcSemicolon, [40] = KeyCode.VcQuote, [41] = KeyCode.VcBackQuote,
            [42] = KeyCode.VcLeftShift, [43] = KeyCode.VcBackslash,
            [44] = KeyCode.VcZ, [45] = KeyCode.VcX, [46] = KeyCode.VcC, [47] = KeyCode.VcV, [48] = KeyCode.VcB,
            [49] = KeyCode.VcN, [50] = KeyCode.VcM,
            [51] = KeyCode.VcComma, [52] = KeyCode.VcPeriod, [53] = KeyCode.VcSlash,
            [54] = KeyCode.VcRightShift, [55] = KeyCode.VcNumPadMultiply,
            [56] = KeyCode.VcLeftAlt, [57] = KeyCode.VcSpace, [58] = KeyCode.VcCapsLock,
            [59] = KeyCode.VcF1, [60] = KeyCode.VcF2, [61] = KeyCode.VcF3, [62] = KeyCode.VcF4, [63] = KeyCode.VcF5,
            [64] = KeyCode.VcF6, [65] = KeyCode.VcF7, [66] = KeyCode.VcF8, [67] = KeyCode.VcF9, [68] = KeyCode.VcF10,
            [69] = KeyCode.VcNumLock, [70] = KeyCode.VcScrollLock,
            [71] = KeyCode.VcNumPad7, [72] = KeyCode.VcNumPad8, [73] = KeyCode.VcNumPad9,
            [74] = KeyCode.VcNumPadSubtract,
            [75] = KeyCode.VcNumPad4, [76] = KeyCode.VcNumPad5, [77] = KeyCode.VcNumPad6, [78] = KeyCode.VcNumPadAdd,
            [79] = KeyCode.VcNumPad1, [80] = KeyCode.VcNumPad2, [81] = KeyCode.VcNumPad3, [82] = KeyCode.VcNumPad0,
            [83] = KeyCode.VcNumPadDecimal,
            [86] = KeyCode.Vc102, [87] = KeyCode.VcF11, [88] = KeyCode.VcF12,
            [89] = KeyCode.VcUnderscore, [90] = KeyCode.VcKatakana, [91] = KeyCode.VcHiragana,
            [92] = KeyCode.VcConvert, [93] = KeyCode.VcKatakanaHiragana, [94] = KeyCode.VcNonConvert,
            [95] = KeyCode.VcJpComma, [96] = KeyCode.VcNumPadEnter, [97] = KeyCode.VcRightControl,
            [98] = KeyCode.VcNumPadDivide, [99] = KeyCode.VcPrintScreen, [100] = KeyCode.VcRightAlt,
            [102] = KeyCode.VcHome, [103] = KeyCode.VcUp, [104] = KeyCode.VcPageUp, [105] = KeyCode.VcLeft,
            [106] = KeyCode.VcRight, [107] = KeyCode.VcEnd, [108] = KeyCode.VcDown, [109] = KeyCode.VcPageDown,
            [110] = KeyCode.VcInsert, [111] = KeyCode.VcDelete,
            [113] = KeyCode.VcVolumeMute, [114] = KeyCode.VcVolumeDown, [115] = KeyCode.VcVolumeUp,
            [116] = KeyCode.VcPower, [117] = KeyCode.VcNumPadEquals, [119] = KeyCode.VcPause,
            [121] = KeyCode.VcNumPadSeparator,
            [122] = KeyCode.VcHangul, [123] = KeyCode.VcHanja, [124] = KeyCode.VcYen,
            [125] = KeyCode.VcLeftMeta, [126] = KeyCode.VcRightMeta, [127] = KeyCode.VcContextMenu,
            [138] = KeyCode.VcHelp, [140] = KeyCode.VcAppCalculator, [142] = KeyCode.VcSleep,
            [155] = KeyCode.VcAppMail, [156] = KeyCode.VcBrowserFavorites, [158] = KeyCode.VcBrowserBack,
            [159] = KeyCode.VcBrowserForward, [161] = KeyCode.VcMediaEject, [163] = KeyCode.VcMediaNext,
            [164] = KeyCode.VcMediaPlay, [165] = KeyCode.VcMediaPrevious, [166] = KeyCode.VcMediaStop,
            [172] = KeyCode.VcBrowserHome, [173] = KeyCode.VcBrowserRefresh,
            [183] = KeyCode.VcF13, [184] = KeyCode.VcF14, [185] = KeyCode.VcF15, [186] = KeyCode.VcF16,
            [187] = KeyCode.VcF17, [188] = KeyCode.VcF18, [189] = KeyCode.VcF19, [190] = KeyCode.VcF20,
            [191] = KeyCode.VcF21, [192] = KeyCode.VcF22, [193] = KeyCode.VcF23, [194] = KeyCode.VcF24,
            [217] = KeyCode.VcBrowserSearch, [226] = KeyCode.VcMediaSelect
        };
    }

    private static Dictionary<KeyCode, ushort> BuildToEvDevMap()
    {
        var map = new Dictionary<KeyCode, ushort>();
        foreach (var pair in ToKeyCodeMap)
        {
            // 正向表里没有重复的 KeyCode，这里用 TryAdd 只是防御未来新增映射时的意外覆盖
            map.TryAdd(pair.Value, pair.Key);
        }

        return map;
    }
}
