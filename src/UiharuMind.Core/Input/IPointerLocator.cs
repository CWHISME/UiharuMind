namespace UiharuMind.Core.Input;

/// <summary>
/// 全局光标位置的带外查询能力。
/// Windows / macOS 的钩子事件自带屏幕坐标，无需此接口；Linux evdev 只给相对位移，
/// 必须另找真值来源，故把这一处显示服务器依赖收敛在这个接口后面：
/// XWayland 下由 X11 实现供给，纯 Wayland 下没有任何合法途径，退化为不可用。
/// </summary>
public interface IPointerLocator : IDisposable
{
    /// <summary>当前环境能否查询到全局光标位置</summary>
    bool IsAvailable { get; }

    /// <summary>
    /// 查询全局光标的像素位置
    /// </summary>
    /// <param name="x">屏幕像素 X</param>
    /// <param name="y">屏幕像素 Y</param>
    /// <returns>查询成功返回 true</returns>
    bool TryGetPosition(out short x, out short y);
}

/// <summary>
/// 恒不可用的光标定位器：用于纯 Wayland 以及不需要带外查询的平台。
/// </summary>
public sealed class UnavailablePointerLocator : IPointerLocator
{
    public bool IsAvailable => false;

    public bool TryGetPosition(out short x, out short y)
    {
        x = 0;
        y = 0;
        return false;
    }

    public void Dispose()
    {
    }
}
