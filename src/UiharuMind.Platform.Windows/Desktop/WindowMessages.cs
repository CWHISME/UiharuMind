/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Runtime.InteropServices;
using UiharuMind.Core.AI.Execution.Tools.Accessibility;
using UiharuMind.Core.AI.Execution.Tools.Desktop;
using static UiharuMind.Platform.Windows.Desktop.WindowsNative;

namespace UiharuMind.Platform.Windows.Desktop;

/// <summary>
/// 第 2 级：把键鼠消息投递给目标窗口，不动光标、不抢前台。
/// 老式 Win32 控件认；WPF、WinUI、Chromium 多半不认，那时模型改用真实键鼠（ADR 0084）
/// </summary>
internal static class WindowMessages
{
    private const int ScrollLinesPerNotch = 3; //系统默认一格滚三行

    /// <summary>
    /// 投递
    /// </summary>
    /// <param name="hwnd">顶层窗口</param>
    /// <param name="input">要发的（屏幕坐标）</param>
    public static void Post(IntPtr hwnd, DesktopInput input)
    {
        switch (input.Kind)
        {
            case EDesktopInputKind.Click:
                PostClick(hwnd, input);
                break;
            case EDesktopInputKind.Scroll:
                int delta = -input.ScrollLines * WheelDelta / ScrollLinesPerNotch;
                PostMessage(ChildAt(hwnd, (int)input.X, (int)input.Y), WmMouseWheel, (IntPtr)(delta << 16),
                    PointParam((int)input.X, (int)input.Y));
                break;
            case EDesktopInputKind.Key:
                PostKey(Focus(hwnd), WindowsKeys.Resolve(input.Keys));
                break;
            case EDesktopInputKind.SelectAll:
                throw new AxActionException("Key combinations can't be sent to a background window on Windows. Try again with input \"real\".");
            case EDesktopInputKind.Text:
                IntPtr focus = Focus(hwnd);
                foreach (char character in input.Text.Replace("\r\n", "\n"))
                {
                    if (character == '\n') PostKey(focus, 0x0D);
                    else PostMessage(focus, WmChar, character, IntPtr.Zero);
                }

                break;
        }
    }

    private static void PostClick(IntPtr hwnd, DesktopInput input)
    {
        IntPtr target = ChildAt(hwnd, (int)input.X, (int)input.Y);
        Point point = new() { X = (int)input.X, Y = (int)input.Y };
        ScreenToClient(target, ref point);
        IntPtr position = PointParam(point.X, point.Y);
        (uint down, uint up, int button) = input.RightButton
            ? (WmRButtonDown, WmRButtonUp, MkRButton)
            : (WmLButtonDown, WmLButtonUp, MkLButton);
        PostMessage(target, WmMouseMove, IntPtr.Zero, position);
        for (int click = 0; click < Math.Max(1, input.Clicks); click++)
        {
            PostMessage(target, click > 0 && !input.RightButton ? WmLButtonDblClk : down, button, position);
            PostMessage(target, up, IntPtr.Zero, position);
        }
    }

    private static void PostKey(IntPtr target, ushort key)
    {
        PostMessage(target, WmKeyDown, key, 1);
        PostMessage(target, WmKeyUp, key, unchecked((IntPtr)0xC0000001));
    }

    // 点到的最里层子窗口（屏幕坐标）：老式控件各是一个窗口，消息要发给它本身
    private static IntPtr ChildAt(IntPtr hwnd, int x, int y)
    {
        IntPtr current = hwnd;
        for (int depth = 0; depth < 32; depth++)
        {
            Point point = new() { X = x, Y = y };
            ScreenToClient(current, ref point);
            IntPtr child = ChildWindowFromPointEx(current, point, CwpSkipInvisible | CwpSkipTransparent);
            if (child == IntPtr.Zero || child == current) return current;
            current = child;
        }

        return current;
    }

    // 窗口所在线程的键盘焦点；后台窗口也查得到
    private static IntPtr Focus(IntPtr hwnd)
    {
        uint thread = GetWindowThreadProcessId(hwnd, out _);
        GuiThreadInfo info = new() { Size = (uint)Marshal.SizeOf<GuiThreadInfo>() };
        return GetGUIThreadInfo(thread, ref info) && info.Focus != IntPtr.Zero ? info.Focus : hwnd;
    }
}
