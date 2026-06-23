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
        _hook.HookEnabled += (_, _) => SafeInvoke(HookEnabled);
        _hook.HookDisabled += (_, _) => SafeInvoke(HookDisabled);
        _hook.KeyPressed += (_, e) => e.SuppressEvent = SafeInvoke(KeyPressed, e.Data.KeyCode);
        _hook.KeyReleased += (_, e) => SafeInvoke(KeyReleased, e.Data.KeyCode);
        _hook.MousePressed += (_, e) => SafeInvoke(MousePressed, e.Data);
        _hook.MouseReleased += (_, e) => SafeInvoke(MouseReleased, e.Data);
        _hook.MouseMoved += (_, e) => SafeInvoke(MouseMoved, e.Data);
        _hook.MouseDragged += (_, e) => SafeInvoke(MouseDragged, e.Data);
        _hook.MouseWheel += (_, e) => SafeInvoke(MouseWheel, e.Data);

        await _hook.RunAsync().ConfigureAwait(false);
    }

    private static GlobalHookBase CreateHook()
    {
        return new SimpleGlobalHook(GlobalHookType.All);
    }

    private static void SafeInvoke(Action? action)
    {
        try
        {
            action?.Invoke();
        }
        catch (Exception e)
        {
            Log.Error(e);
        }
    }

    private static bool SafeInvoke<T>(Func<T, bool>? action, T value)
    {
        try
        {
            return action?.Invoke(value) == true;
        }
        catch (Exception e)
        {
            Log.Error(e);
            return false;
        }
    }

    private static void SafeInvoke<T>(Action<T>? action, T value)
    {
        try
        {
            action?.Invoke(value);
        }
        catch (Exception e)
        {
            Log.Error(e);
        }
    }

    public void Dispose()
    {
        if (_hook == null) return;
        if (_hook.IsDisposed) return;
        _hook.Dispose();
        _hook = null;
    }
}
