/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 *
 * Latest Update: 2024.10.07
 ****************************************************************************/

using SharpHook.Data;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.Core.Singletons;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.Input;

/// <summary>
/// 全局输入的统一入口：管钩子生命周期、向应用分发输入事件、持有指针状态。
/// <para>
/// 按键状态交给 <see cref="KeyPressTracker"/>，快捷键交给 <see cref="ShortcutRegistry"/>，
/// 点击判定交给 <see cref="MouseClickDetector"/>，平台差异交给 <see cref="IInputHookBackend"/>。
/// 本类自己不保存任何「可能与操作系统失同步」的状态。
/// </para>
/// <para>
/// 所有事件都在钩子线程上同步回调，UI 订阅方须自行封送到 UI 线程（Core 层不依赖 UI）。
/// 回调里不要做耗时操作：macOS 的事件钩子有超时，超时会被系统直接停用。
/// </para>
/// </summary>
public class InputManager : Singleton<InputManager>, IInitialize
{
    /// <summary>
    /// 当前鼠标信息
    /// </summary>
    public static MouseEventData MouseData { get; set; }

    /// <summary>
    /// 上一次鼠标按下位置信息
    /// </summary>
    public static MouseEventData MousePressedData { get; set; }

    /// <summary>
    /// 上一次鼠标释放位置信息
    /// </summary>
    public static MouseEventData MouseReleasedData { get; set; }

    /// <summary>
    /// 功能是否启用
    /// </summary>
    public bool IsRunning => _hookBackend.IsRunning;

    public event Action<KeyCode>? EventOnKeyDown;
    public event Action<KeyCode>? EventOnKeyUp;
    public event Action<MouseEventData>? EventOnMousePressed;
    public event Action<MouseEventData>? EventOnMouseReleased;
    public event Action<MouseEventData>? EventOnMouseMoved;
    public event Action<MouseWheelEventData>? EventOnMouseWheel;

    /// <summary>
    /// 鼠标点击事件（仅在短按时触发，长按不触发）
    /// </summary>
    public event Action<MouseEventData>? EventOnMouseClicked;

    private readonly IInputHookBackend _hookBackend;
    private readonly KeyPressTracker _keyPressTracker = new();
    private readonly ShortcutRegistry _shortcutRegistry = new();
    private readonly MouseClickDetector _clickDetector = new();

    /// 本次 Start 期间钩子是否成功启用过，用于区分「启动失败」与「启用后被停掉」
    private volatile bool _isEnabled;

    /// 0 空闲 / 1 启动中，挡住 DummyWindow 与权限引导窗的重复调用
    private int _startGate;

    public InputManager()
    {
        _hookBackend = InputBackendFactory.CreateHookBackend();
        _hookBackend.HookEnabled += OnHookEnabled;
        _hookBackend.HookDisabled += OnHookDisabled;
        _hookBackend.KeyPressed += OnKeyPressed;
        _hookBackend.KeyReleased += OnKeyReleased;
        _hookBackend.MousePressed += OnMousePressed;
        _hookBackend.MouseReleased += OnMouseReleased;
        _hookBackend.MouseMoved += OnMouseMoved;
        _hookBackend.MouseDragged += OnMouseDragged;
        _hookBackend.MouseWheel += OnMouseWheel;
    }

    public void OnInitialize()
    {
        // Start is triggered by DummyWindow after the Avalonia app is ready.
    }

    /// <summary>
    /// 启动全局钩子。已在运行或启动中时直接返回，故多处调用是安全的。
    /// </summary>
    /// <param name="onFailed">启动失败（通常是缺权限）时回调，钩子曾成功启用过则不回调</param>
    public async void Start(Action onFailed)
    {
        if (_hookBackend.IsRunning) return;
        if (Interlocked.CompareExchange(ref _startGate, 1, 0) != 0) return;

        _isEnabled = false;
        try
        {
            await _hookBackend.RunAsync().ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Log.Warning(e.ToString());
            Stop();
            if (!_isEnabled) onFailed.Invoke();
        }
        finally
        {
            Volatile.Write(ref _startGate, 0);
        }
    }

    public void Stop()
    {
        try
        {
            _hookBackend.Dispose();
        }
        catch (Exception)
        {
            // ignored
        }
        finally
        {
            ClearPressedState();
        }
    }

    /// <summary>
    /// 查询按键是否处于按下状态。修饰键走操作系统真值，不受漏事件影响。
    /// </summary>
    /// <param name="keyCode">键码</param>
    /// <returns>按下返回 True</returns>
    public bool IsPressed(KeyCode keyCode)
    {
        if (keyCode.IsModifier()) _keyPressTracker.SyncModifiers(_hookBackend.GetPressedModifiers());
        return _keyPressTracker.IsPressed(keyCode);
    }

    public void ClearPressedState()
    {
        _keyPressTracker.Reset();
        _clickDetector.Reset();
    }

    /// <summary>
    /// 取当前按下的修饰键，读的是操作系统真值而非事件累加值
    /// </summary>
    /// <returns>修饰键位集</returns>
    public EModifierKeys GetPressedModifiers()
    {
        var modifiers = _hookBackend.GetPressedModifiers();
        _keyPressTracker.SyncModifiers(modifiers);
        return modifiers;
    }

    /// <summary>
    /// 是否有任意修饰键处于物理按下状态。
    /// 输入模拟在注入前需要据此等待用户松手，否则注入的组合键会与用户手上按着的修饰键叠加。
    /// </summary>
    /// <returns>有任一修饰键按下返回 True</returns>
    public bool IsAnyModifierPressed() => GetPressedModifiers() != EModifierKeys.None;

    /// <summary>
    /// 给定的修饰键组合是否与当前按下的修饰键完全一致（不分左右）。
    /// 与快捷键触发共用一套排他语义，避免「热键触发得了、停止判定却不认」的不一致。
    /// </summary>
    /// <param name="modifiers">待比对的修饰键键码</param>
    /// <returns>完全一致返回 True</returns>
    public bool MatchesPressedModifiers(IEnumerable<KeyCode>? modifiers)
    {
        return modifiers.ToModifiers().Fold() == GetPressedModifiers().Fold();
    }

    /// <summary>
    /// 取当前鼠标的像素位置。
    /// Linux 的 evdev 事件只带相对位移，屏幕坐标要向定位器带外查询；
    /// 其余平台的钩子事件自带坐标，直接用最近一次事件的值。
    /// </summary>
    /// <returns>鼠标像素坐标</returns>
    public static (short X, short Y) GetPointerPosition()
    {
        if (InputBackendFactory.PointerLocator.TryGetPosition(out short x, out short y)) return (x, y);
        return (MouseData.X, MouseData.Y);
    }

    /// <summary>
    /// 全局光标位置当前是否可查询。不可查询时，依赖鼠标位置的弹窗应退化为居中显示。
    /// </summary>
    public static bool IsPointerPositionAvailable =>
        InputBackendFactory.PointerLocator.IsAvailable || !PlatformUtils.IsLinux;

    /// <summary>
    /// 挂起已注册快捷键的分发，用于快捷键录制界面。释放返回的句柄即恢复。
    /// </summary>
    /// <returns>释放即恢复的句柄</returns>
    public IDisposable SuspendRegisteredShortcuts() => _shortcutRegistry.Suspend();

    public void RegisterKey(KeyCombinationData keyCombination) => _shortcutRegistry.Register(keyCombination);

    public void UnRegisterKey(KeyCombinationData keyCombination) => _shortcutRegistry.Unregister(keyCombination);

    public void ClearRegisteredKeys() => _shortcutRegistry.Clear();

    //=========Event Handler=========

    private bool OnKeyPressed(KeyEventInfo info)
    {
        // 自己注入的按键不参与状态与快捷键，否则「模拟一次快捷键」会把自己再触发一遍
        if (info.IsSimulated)
        {
            EventOnKeyDown?.Invoke(info.KeyCode);
            return false;
        }

        // 操作系统的键盘连发会反复发 KeyPressed，只有首次按下才算一次输入
        if (!_keyPressTracker.TryBeginPress(info)) return false;

        EventOnKeyDown?.Invoke(info.KeyCode);
        return _shortcutRegistry.TryTrigger(info.KeyCode, info.Modifiers);
    }

    private void OnKeyReleased(KeyEventInfo info)
    {
        if (!info.IsSimulated) _keyPressTracker.EndPress(info);
        EventOnKeyUp?.Invoke(info.KeyCode);
    }

    private void OnMousePressed(MouseEventData data)
    {
        _clickDetector.BeginPress(data.Button);
        MouseData = data;
        MousePressedData = data;
        EventOnMousePressed?.Invoke(data);
    }

    private void OnMouseReleased(MouseEventData data)
    {
        MouseData = data;
        MouseReleasedData = data;
        if (_clickDetector.EndPressIsClick(data.Button)) EventOnMouseClicked?.Invoke(data);
        EventOnMouseReleased?.Invoke(data);
    }

    private void OnMouseMoved(MouseEventData data)
    {
        MouseData = data;
        EventOnMouseMoved?.Invoke(data);
    }

    private void OnMouseDragged(MouseEventData data)
    {
        OnMouseMoved(data);
    }

    private void OnMouseWheel(MouseWheelEventData data)
    {
        EventOnMouseWheel?.Invoke(data);
    }

    private void OnHookEnabled()
    {
        Log.Debug("OnHookEnabled");
        _isEnabled = true;
    }

    private void OnHookDisabled()
    {
        Log.Debug("OnHookDisabled");
        ClearPressedState();
    }
}
