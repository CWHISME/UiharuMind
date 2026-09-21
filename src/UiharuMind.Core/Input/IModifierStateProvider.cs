namespace UiharuMind.Core.Input;

/// <summary>
/// 修饰键真值的带外查询能力。
/// <para>
/// 钩子事件自带的修饰键位已经够用，但它仍是「上一次事件的快照」；UI 线程随时可能来问
/// 「现在按着什么」（如快捷键录制窗打开的那一刻），此时没有事件可依。各平台都提供了
/// 直接读硬件状态的接口，把这唯一一处平台差异收敛在这个接口后面。
/// </para>
/// </summary>
public interface IModifierStateProvider
{
    /// <summary>当前环境能否查询到修饰键真值</summary>
    bool IsAvailable { get; }

    /// <summary>
    /// 查询当前按下的修饰键
    /// </summary>
    /// <returns>修饰键位集，不可用时返回 <see cref="EModifierKeys.None"/></returns>
    EModifierKeys GetPressedModifiers();
}

/// <summary>
/// 恒不可用的修饰键查询器：用于无原生实现的平台（Linux 由 evdev 后端自带真值，无需此路）。
/// </summary>
public sealed class UnavailableModifierStateProvider : IModifierStateProvider
{
    public bool IsAvailable => false;

    public EModifierKeys GetPressedModifiers() => EModifierKeys.None;
}
