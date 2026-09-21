using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Shared.Windows.Focus;

/// <summary>
/// 按当前平台创建窗口取焦点行为。
/// </summary>
public static class WindowFocusBehaviorFactory
{
    /// <summary>
    /// 为一个窗口创建取焦点行为。
    /// </summary>
    /// <returns>macOS 上是带状态的 <see cref="MacWindowFocusBehavior"/>，其它平台是默认行为</returns>
    public static IWindowFocusBehavior Create()
    {
        return PlatformUtils.IsMacOS ? new MacWindowFocusBehavior() : new DefaultWindowFocusBehavior();
    }
}
