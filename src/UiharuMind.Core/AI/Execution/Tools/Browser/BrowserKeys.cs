/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Text.RegularExpressions;

namespace UiharuMind.Core.AI.Execution.Tools.Browser;

/// <summary>
/// 模型写来的按键五花八门（Ctrl+A、Cmd+Enter、esc）：统一成 Puppeteer 的键名，并判断一次按键会不会往焦点里写字
/// </summary>
internal static partial class BrowserKeys
{
    private static readonly HashSet<string> NonTypingKeys =
    [
        "Enter", "NumpadEnter", "Tab", "Escape", "Backspace", "Delete", "Insert",
        "ArrowUp", "ArrowDown", "ArrowLeft", "ArrowRight", "Home", "End", "PageUp", "PageDown",
        "Shift", "ShiftLeft", "ShiftRight", "Control", "ControlLeft", "ControlRight", "Alt", "AltLeft", "AltRight",
        "Meta", "MetaLeft", "MetaRight", "CapsLock", "ContextMenu",
    ];

    /// <summary>
    /// 拆成键名：组合键按「+」拆开，修饰键在前、主键在最后
    /// </summary>
    /// <param name="keys">模型写的按键</param>
    /// <returns>键名；写的是空的则为空数组</returns>
    public static string[] Parse(string keys) =>
        keys.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(Normalize).ToArray();

    /// <summary>
    /// 这次按键会不会往焦点里写字。按白名单判、认不出的一律当会写：Puppeteer 的键码名（KeyA、Digit1、Space）
    /// 照样打出字来。带 Control/Meta 的是快捷键，只有粘贴（V、Shift+Insert）算写字
    /// </summary>
    /// <param name="names">拆好的键名（见 <see cref="Parse"/>）</param>
    /// <returns>会写字为 true</returns>
    public static bool TypesText(IReadOnlyList<string> names)
    {
        if (names.Count == 0) return false;
        string key = names[^1];
        IEnumerable<string> modifiers = names.Take(names.Count - 1);
        if (modifiers.Any(m => m is "Control" or "Meta")) return key is "v" or "V" or "KeyV";
        if (key == "Insert") return modifiers.Contains("Shift");
        return !NonTypingKeys.Contains(key) && !FunctionKeyPattern().IsMatch(key);
    }

    private static string Normalize(string key) => key.ToLowerInvariant() switch
    {
        "ctrl" or "control" => "Control",
        "cmd" or "command" or "meta" or "win" or "super" => "Meta",
        "alt" or "option" or "opt" => "Alt",
        "shift" => "Shift",
        "esc" or "escape" => "Escape",
        "return" or "enter" => "Enter",
        "del" or "delete" => "Delete",
        "space" or "spacebar" => "Space",
        "up" => "ArrowUp",
        "down" => "ArrowDown",
        "left" => "ArrowLeft",
        "right" => "ArrowRight",
        "pgup" => "PageUp",
        "pgdn" => "PageDown",
        _ => key.Length == 1 ? key : char.ToUpperInvariant(key[0]) + key[1..],
    };

    [GeneratedRegex(@"^F([1-9]|1[0-9]|2[0-4])$")]
    private static partial Regex FunctionKeyPattern();
}
