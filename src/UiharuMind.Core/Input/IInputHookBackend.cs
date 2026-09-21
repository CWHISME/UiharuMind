using SharpHook.Data;

namespace UiharuMind.Core.Input;

/// <summary>
/// 全局输入监听后端。实现方负责把平台事件翻译成统一事实，其中最关键的是
/// <see cref="KeyEventInfo.Modifiers"/>——必须是操作系统的权威状态，不得由后端自行累加得出。
/// </summary>
public interface IInputHookBackend : IDisposable
{
    bool IsRunning { get; }

    /// <summary>
    /// 带外查询当前按下的修饰键。用于「没有事件可依」的时刻（如快捷键录制窗刚打开）。
    /// </summary>
    /// <returns>修饰键位集</returns>
    EModifierKeys GetPressedModifiers();

    event Action? HookEnabled;
    event Action? HookDisabled;

    /// <summary>按键按下。返回值表示是否吞掉该事件（仅 Windows/macOS 生效）</summary>
    event Func<KeyEventInfo, bool>? KeyPressed;

    event Action<KeyEventInfo>? KeyReleased;
    event Action<MouseEventData>? MousePressed;
    event Action<MouseEventData>? MouseReleased;
    event Action<MouseEventData>? MouseMoved;
    event Action<MouseEventData>? MouseDragged;
    event Action<MouseWheelEventData>? MouseWheel;

    Task RunAsync();
}
