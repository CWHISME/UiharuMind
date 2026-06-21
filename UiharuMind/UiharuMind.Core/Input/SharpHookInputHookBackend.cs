using System.Diagnostics;
using SharpHook;
using SharpHook.Data;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.Input;

public class SharpHookInputHookBackend : IInputHookBackend
{
    private GlobalHookBase? _hook;

    public bool IsRunning => _hook?.IsRunning ?? false;

    public bool? IsKeyPressed(KeyCode keyCode)
    {
        return null;
    }

    public event Action? HookEnabled;
    public event Action? HookDisabled;
    public event Func<KeyCode, bool>? KeyPressed;
    public event Action<KeyCode>? KeyReleased;
    public event Action<MouseEventData>? MousePressed;
    public event Action<MouseEventData>? MouseReleased;
    public event Action<MouseEventData>? MouseMoved;
    public event Action<MouseEventData>? MouseDragged;
    public event Action<MouseWheelEventData>? MouseWheel;

    public async Task RunAsync()
    {
        Dispose();

        _hook = CreateHook();
        _hook.HookEnabled += (_, _) => HookEnabled?.Invoke();
        _hook.HookDisabled += (_, _) => HookDisabled?.Invoke();
        _hook.KeyPressed += (_, e) => e.SuppressEvent = KeyPressed?.Invoke(e.Data.KeyCode) == true;
        _hook.KeyReleased += (_, e) => KeyReleased?.Invoke(e.Data.KeyCode);
        _hook.MousePressed += (_, e) => MousePressed?.Invoke(e.Data);
        _hook.MouseReleased += (_, e) => MouseReleased?.Invoke(e.Data);
        _hook.MouseMoved += (_, e) => MouseMoved?.Invoke(e.Data);
        _hook.MouseDragged += (_, e) => MouseDragged?.Invoke(e.Data);
        _hook.MouseWheel += (_, e) => MouseWheel?.Invoke(e.Data);

        await _hook.RunAsync().ConfigureAwait(false);
    }

    private static GlobalHookBase CreateHook()
    {
        return new SimpleGlobalHook(GlobalHookType.All);
    }

    public void Dispose()
    {
        if (_hook == null) return;
        if (_hook.IsDisposed) return;
        _hook.Dispose();
        _hook = null;
    }
}
