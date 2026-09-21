using System.Runtime.InteropServices;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.Input.Windows;

/// <summary>
/// Windows 的修饰键真值查询，走 user32 的异步键盘状态。
/// <para>
/// GetAsyncKeyState 读的是硬件此刻的状态而非消息队列，因此不受本进程有没有收到
/// WM_KEYUP 影响——钩子漏掉的 release 在这里一定体现得出来。
/// </para>
/// </summary>
public sealed class WindowsModifierStateProvider : IModifierStateProvider
{
    private const int VkLeftShift = 0xA0;
    private const int VkRightShift = 0xA1;
    private const int VkLeftControl = 0xA2;
    private const int VkRightControl = 0xA3;
    private const int VkLeftAlt = 0xA4;
    private const int VkRightAlt = 0xA5;
    private const int VkLeftWin = 0x5B;
    private const int VkRightWin = 0x5C;

    /// 高位为 1 表示该键当前处于按下状态
    private const int PressedMask = 0x8000;

    private bool _isAvailable = true;

    public bool IsAvailable => _isAvailable;

    public EModifierKeys GetPressedModifiers()
    {
        if (!_isAvailable) return EModifierKeys.None;

        try
        {
            var modifiers = EModifierKeys.None;
            if (IsDown(VkLeftShift)) modifiers |= EModifierKeys.LeftShift;
            if (IsDown(VkRightShift)) modifiers |= EModifierKeys.RightShift;
            if (IsDown(VkLeftControl)) modifiers |= EModifierKeys.LeftCtrl;
            if (IsDown(VkRightControl)) modifiers |= EModifierKeys.RightCtrl;
            if (IsDown(VkLeftAlt)) modifiers |= EModifierKeys.LeftAlt;
            if (IsDown(VkRightAlt)) modifiers |= EModifierKeys.RightAlt;
            if (IsDown(VkLeftWin)) modifiers |= EModifierKeys.LeftMeta;
            if (IsDown(VkRightWin)) modifiers |= EModifierKeys.RightMeta;
            return modifiers;
        }
        catch (Exception e)
        {
            _isAvailable = false;
            Log.Warning($"Failed to query Windows modifier state: {e.Message}");
            return EModifierKeys.None;
        }
    }

    private static bool IsDown(int virtualKey) => (GetAsyncKeyState(virtualKey) & PressedMask) != 0;

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);
}
