using SharpHook.Data;

namespace UiharuMind.Core.Input;

public interface IInputHookBackend : IDisposable
{
    bool IsRunning { get; }

    bool? IsKeyPressed(KeyCode keyCode);

    event Action? HookEnabled;
    event Action? HookDisabled;
    event Func<KeyCode, bool>? KeyPressed;
    event Action<KeyCode>? KeyReleased;
    event Action<MouseEventData>? MousePressed;
    event Action<MouseEventData>? MouseReleased;
    event Action<MouseEventData>? MouseMoved;
    event Action<MouseEventData>? MouseDragged;
    event Action<MouseWheelEventData>? MouseWheel;

    Task RunAsync();
}
