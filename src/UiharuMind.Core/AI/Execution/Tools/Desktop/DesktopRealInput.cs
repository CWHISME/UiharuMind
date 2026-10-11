/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using SharpHook.Data;
using UiharuMind.Core.AI.Execution.Tools.Accessibility;
using UiharuMind.Core.Input;

namespace UiharuMind.Core.AI.Execution.Tools.Desktop;

/// <summary>
/// 第 3 级真实键鼠（ADR 0084）：经应用的输入模拟（<see cref="InputSimulateManager"/>，与自动点击同一套）发到系统，
/// 光标真的会动、键盘真的会按。坐标与输入钩子同一套（Windows 上是物理像素）。
/// macOS 不走这里：那边用私有事件源投递，发出的事件不算进「用户最近一次输入」
/// </summary>
internal static class DesktopRealInput
{
    private const int WheelStep = 120; //一格
    private const int ScrollLinesPerStep = 3;

    private static readonly Dictionary<string, KeyCode> Named = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Enter"] = KeyCode.VcEnter, ["NumpadEnter"] = KeyCode.VcNumPadEnter, ["Tab"] = KeyCode.VcTab,
        ["Space"] = KeyCode.VcSpace, ["Backspace"] = KeyCode.VcBackspace, ["Escape"] = KeyCode.VcEscape,
        ["Delete"] = KeyCode.VcDelete, ["Insert"] = KeyCode.VcInsert, ["Home"] = KeyCode.VcHome, ["End"] = KeyCode.VcEnd,
        ["PageUp"] = KeyCode.VcPageUp, ["PageDown"] = KeyCode.VcPageDown, ["ArrowLeft"] = KeyCode.VcLeft,
        ["ArrowRight"] = KeyCode.VcRight, ["ArrowUp"] = KeyCode.VcUp, ["ArrowDown"] = KeyCode.VcDown,
        ["F1"] = KeyCode.VcF1, ["F2"] = KeyCode.VcF2, ["F3"] = KeyCode.VcF3, ["F4"] = KeyCode.VcF4, ["F5"] = KeyCode.VcF5,
        ["F6"] = KeyCode.VcF6, ["F7"] = KeyCode.VcF7, ["F8"] = KeyCode.VcF8, ["F9"] = KeyCode.VcF9, ["F10"] = KeyCode.VcF10,
        ["F11"] = KeyCode.VcF11, ["F12"] = KeyCode.VcF12,
    };

    /// <summary>
    /// 发一次输入（窗口已提到前台）
    /// </summary>
    /// <param name="input">要发的</param>
    /// <exception cref="AxActionException">认不出的键</exception>
    public static void Send(DesktopInput input)
    {
        InputSimulateManager simulator = InputSimulateManager.Instance;
        switch (input.Kind)
        {
            case EDesktopInputKind.Click:
                MouseButton button = input.RightButton ? MouseButton.Button2 : MouseButton.Button1;
                simulator.SendMouseMove((short)input.X, (short)input.Y);
                for (int click = 0; click < Math.Max(1, input.Clicks); click++)
                {
                    simulator.SimulateMousePress(button);
                    simulator.SimulateMouseRelease(button);
                }

                break;
            case EDesktopInputKind.Scroll:
                // 系统把滚轮发给光标下的窗口：先挪过去。正数往上
                simulator.SendMouseMove((short)input.X, (short)input.Y);
                simulator.SimulateMouseWheel(-input.ScrollLines * WheelStep / ScrollLinesPerStep);
                break;
            case EDesktopInputKind.Key:
                (IReadOnlyList<KeyCode> modifiers, KeyCode key) = Resolve(input.Keys);
                Chord(modifiers, key);
                break;
            case EDesktopInputKind.SelectAll:
                Chord([KeyCode.VcLeftControl], KeyCode.VcA);
                break;
            case EDesktopInputKind.Text:
                // 换行要按回车键；其余不按键位打，输入法、键盘布局都不碍事
                string[] lines = input.Text.Replace("\r\n", "\n").Split('\n');
                for (int i = 0; i < lines.Length; i++)
                {
                    if (i > 0) Chord([], KeyCode.VcEnter);
                    if (lines[i].Length > 0) simulator.SendUnicodeText(lines[i]);
                }

                break;
        }
    }

    /// <summary>
    /// 只按一下 Alt：Windows 只许「刚收到过输入的进程」换前台，借这一下拿到资格
    /// </summary>
    public static void TapAlt() => Chord([], KeyCode.VcLeftAlt);

    private static void Chord(IReadOnlyList<KeyCode> modifiers, KeyCode key)
    {
        InputSimulateManager simulator = InputSimulateManager.Instance;
        foreach (KeyCode modifier in modifiers) simulator.SimulateKeyPress(modifier);
        simulator.SimulateKeyPress(key);
        simulator.SimulateKeyRelease(key);
        foreach (KeyCode modifier in modifiers.Reverse()) simulator.SimulateKeyRelease(modifier);
    }

    private static (IReadOnlyList<KeyCode> Modifiers, KeyCode Key) Resolve(IReadOnlyList<string> keys)
    {
        List<KeyCode> modifiers = new();
        foreach (string modifier in keys.Take(keys.Count - 1))
        {
            modifiers.Add(modifier switch
            {
                "Shift" => KeyCode.VcLeftShift,
                "Control" => KeyCode.VcLeftControl,
                "Alt" => KeyCode.VcLeftAlt,
                "Meta" => KeyCode.VcLeftMeta,
                _ => throw new AxActionException($"\"{modifier}\" is not a modifier key; put Ctrl, Alt, Shift or Win before the key."),
            });
        }

        string key = keys[^1];
        if (Named.TryGetValue(key, out KeyCode named)) return (modifiers, named);
        string bare = key.StartsWith("Key", StringComparison.Ordinal) && key.Length == 4 ? key[3..]
            : key.StartsWith("Digit", StringComparison.Ordinal) && key.Length == 6 ? key[5..]
            : key;
        if (bare.Length == 1 && InputKeyCodeMapper.CharToKeyCode(bare[0]) is var code && code != KeyCode.VcUndefined)
        {
            return (modifiers, code);
        }

        throw new AxActionException($"Unknown key \"{key}\". Use names like Enter, Tab, Escape, ArrowDown, F5, or a letter.");
    }
}
