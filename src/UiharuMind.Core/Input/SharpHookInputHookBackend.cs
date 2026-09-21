using SharpHook;
using SharpHook.Data;
using SharpHook.Providers;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.Input;

/// <summary>
/// 基于 SharpHook(libuiohook) 的全局监听，Windows / macOS 的主力后端，也是 Linux X11 会话的兜底。
/// <para>
/// 修饰键状态有两路来源，优先级从高到低：平台原生查询（<see cref="IModifierStateProvider"/>，
/// 读的是硬件此刻的真值）、事件自带的 <see cref="UioHookEvent.Mask"/>。
/// 两者都不依赖本类记忆任何状态，因此不存在「release 丢了就永久残留」的可能。
/// </para>
/// </summary>
public class SharpHookInputHookBackend : IInputHookBackend
{
    private readonly IModifierStateProvider _modifierStateProvider;

    private GlobalHookBase? _hook;

    /// 最近一个事件（含鼠标）带来的修饰键位集，原生查询不可用时的兜底真值
    private volatile EModifierKeys _lastEventModifiers;

    public SharpHookInputHookBackend(IModifierStateProvider modifierStateProvider)
    {
        _modifierStateProvider = modifierStateProvider;
    }

    public bool IsRunning => _hook?.IsRunning ?? false;

    public EModifierKeys GetPressedModifiers()
    {
        return _modifierStateProvider.IsAvailable
            ? _modifierStateProvider.GetPressedModifiers()
            : _lastEventModifiers;
    }

    public event Action? HookEnabled;
    public event Action? HookDisabled;
    public event Func<KeyEventInfo, bool>? KeyPressed;
    public event Action<KeyEventInfo>? KeyReleased;
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
        _hook.KeyPressed += (_, e) => e.SuppressEvent = SafeInvoke(KeyPressed, ToKeyEventInfo(e));
        _hook.KeyReleased += (_, e) => SafeInvoke(KeyReleased, ToKeyEventInfo(e));
        _hook.MousePressed += (_, e) => SafeInvoke(MousePressed, TrackModifiers(e, e.Data));
        _hook.MouseReleased += (_, e) => SafeInvoke(MouseReleased, TrackModifiers(e, e.Data));
        _hook.MouseMoved += (_, e) => SafeInvoke(MouseMoved, TrackModifiers(e, e.Data));
        _hook.MouseDragged += (_, e) => SafeInvoke(MouseDragged, TrackModifiers(e, e.Data));
        _hook.MouseWheel += (_, e) => SafeInvoke(MouseWheel, TrackModifiers(e, e.Data));

        await _hook.RunAsync().ConfigureAwait(false);
    }

    private static GlobalHookBase CreateHook()
    {
        return new SimpleGlobalHook(UioHookProvider.Instance);
    }

    private KeyEventInfo ToKeyEventInfo(KeyboardHookEventArgs args)
    {
        var mask = args.RawEvent.Mask;
        _lastEventModifiers = FromEventMask(mask);

        return new KeyEventInfo(
            args.Data.KeyCode,
            GetPressedModifiers());
    }

    /// 鼠标事件同样带修饰键位，白拿的纠正机会：用户松开修饰键后随手动一下鼠标，残留即被抹平
    private TData TrackModifiers<TData>(HookEventArgs args, TData data)
    {
        _lastEventModifiers = FromEventMask(args.RawEvent.Mask);
        return data;
    }

    private static EModifierKeys FromEventMask(EventMask mask)
    {
        var modifiers = EModifierKeys.None;
        if ((mask & EventMask.LeftShift) != 0) modifiers |= EModifierKeys.LeftShift;
        if ((mask & EventMask.RightShift) != 0) modifiers |= EModifierKeys.RightShift;
        if ((mask & EventMask.LeftCtrl) != 0) modifiers |= EModifierKeys.LeftCtrl;
        if ((mask & EventMask.RightCtrl) != 0) modifiers |= EModifierKeys.RightCtrl;
        if ((mask & EventMask.LeftAlt) != 0) modifiers |= EModifierKeys.LeftAlt;
        if ((mask & EventMask.RightAlt) != 0) modifiers |= EModifierKeys.RightAlt;
        if ((mask & EventMask.LeftMeta) != 0) modifiers |= EModifierKeys.LeftMeta;
        if ((mask & EventMask.RightMeta) != 0) modifiers |= EModifierKeys.RightMeta;
        return modifiers;
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
        _lastEventModifiers = EModifierKeys.None;
        if (_hook == null) return;
        if (_hook.IsDisposed) return;
        _hook.Dispose();
        _hook = null;
    }
}
