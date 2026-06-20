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

namespace UiharuMind.Core.Input;

public class InputManager : Singleton<InputManager>, IInitialize
{
    /// <summary>
    /// 当前鼠标信息
    /// </summary>
    public static MouseEventData MouseData;

    /// <summary>
    /// 上一次鼠标按下位置信息
    /// </summary>
    public static MouseEventData MousePressedData;

    /// <summary>
    /// 上一次鼠标释放位置信息
    /// </summary>
    public static MouseEventData MouseReleasedData;

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
    private readonly object _stateLock = new();
    private readonly HashSet<KeyCode> _pressedKeys = new();
    private readonly Timer? _pressedStateSyncTimer;
    private int _registeredShortcutSuspendCount;

    /// <summary>
    /// 组合键组合数据
    /// </summary>
    private readonly List<KeyCombinationData> _keyCombinations = new();

    // 鼠标按下时间记录，用于判断是否为点击
    private readonly Dictionary<MouseButton, DateTime> _mousePressTimes = new();

    /// <summary>
    /// 点击阈值（毫秒），超过此时间视为长按而非点击
    /// </summary>
    private const int ClickThresholdMs = 300;

    //是否启用过
    private bool _isEnabled;

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
        _pressedStateSyncTimer = new Timer(_ => SyncPressedStateWithBackend(), null, 1000, 1000);
    }

    public void OnInitialize()
    {
        // Start is triggered by DummyWindow after the Avalonia app is ready.
    }

    public async void Start(Action onFailed)
    {
        try
        {
            await _hookBackend.RunAsync().ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Log.Error("Input hook failed. On Windows, games running as administrator require UiharuMind to run with the same privilege.");
            Log.Warning(e.Message);
            Stop();
            if (!_isEnabled) onFailed.Invoke();
        }
    }

    public void Stop()
    {
        try
        {
            _hookBackend.Dispose();
            ClearPressedState();
        }
        catch (Exception)
        {
            // ignored
        }
    }

    public bool IsPressed(KeyCode keyCode)
    {
        SyncPressedStateWithBackend();
        lock (_stateLock)
        {
            return _pressedKeys.Contains(keyCode);
        }
    }

    public void ClearPressedState()
    {
        lock (_stateLock)
        {
            _pressedKeys.Clear();
            _mousePressTimes.Clear();
        }
    }

    public IDisposable SuspendRegisteredShortcuts()
    {
        Interlocked.Increment(ref _registeredShortcutSuspendCount);
        ClearPressedState();
        return new RegisteredShortcutSuspendScope(this);
    }

    public void RegisterKey(KeyCombinationData keyCombination)
    {
        _keyCombinations.Add(keyCombination);
    }

    public void UnRegisterKey(KeyCombinationData keyCombination)
    {
        _keyCombinations.Remove(keyCombination);
    }

    public void ClearRegisteredKeys()
    {
        _keyCombinations.Clear();
    }

    //=========Event Handler=========

    private bool OnKeyPressed(KeyCode keyCode)
    {
        SyncPressedStateWithBackend();

        // 如果这个键已经处于按下状态，说明是操作系统的键盘重复事件，不触发 EventOnKeyDown
        lock (_stateLock)
        {
            if (!_pressedKeys.Add(keyCode))
            {
                return false;
            }
        }

        EventOnKeyDown?.Invoke(keyCode);
        if (Volatile.Read(ref _registeredShortcutSuspendCount) > 0)
        {
            return false;
        }

        foreach (var keyCombination in _keyCombinations)
        {
            if (keyCombination.MainKeyCode != keyCode) continue;
            if (keyCombination.DecorateKeyCodes != null && !keyCombination.DecorateKeyCodes.All(IsPressed)) continue;
            try
            {
                keyCombination.OnTrigger?.Invoke();
            }
            catch (Exception e)
            {
                Log.Warning(e.Message);
            }

            return true;
        }

        return false;
    }

    private void OnKeyReleased(KeyCode keyCode)
    {
        lock (_stateLock)
        {
            _pressedKeys.Remove(keyCode);
        }

        EventOnKeyUp?.Invoke(keyCode);
    }

    private void OnMousePressed(MouseEventData data)
    {
        lock (_stateLock)
        {
            _mousePressTimes[data.Button] = DateTime.Now;
        }

        EventOnMousePressed?.Invoke(data);
        MouseData = data;
        MousePressedData = data;
    }

    private void OnMouseReleased(MouseEventData data)
    {
        var button = data.Button;

        DateTime pressTime;
        lock (_stateLock)
        {
            if (!_mousePressTimes.TryGetValue(button, out pressTime))
            {
                pressTime = default;
            }
            else
            {
                _mousePressTimes.Remove(button);
            }
        }

        if (pressTime != default)
        {
            var duration = (int)(DateTime.Now - pressTime).TotalMilliseconds;
            if (duration <= ClickThresholdMs)
            {
                EventOnMouseClicked?.Invoke(data);
            }
        }

        EventOnMouseReleased?.Invoke(data);
        MouseData = data;
        MouseReleasedData = data;
    }

    private void OnMouseMoved(MouseEventData data)
    {
        EventOnMouseMoved?.Invoke(data);
        MouseData = data;
    }

    private void OnMouseDragged(MouseEventData data)
    {
        EventOnMouseMoved?.Invoke(data);
        MouseData = data;
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

    private void ResumeRegisteredShortcuts()
    {
        if (Interlocked.CompareExchange(ref _registeredShortcutSuspendCount, 0, 0) <= 0) return;
        Interlocked.Decrement(ref _registeredShortcutSuspendCount);
        ClearPressedState();
    }

    private void SyncPressedStateWithBackend()
    {
        if (_pressedKeys.Count == 0) return;
        lock (_stateLock)
        {
            _pressedKeys.RemoveWhere(keyCode => _hookBackend.IsKeyPressed(keyCode) == false);
        }
    }

    private sealed class RegisteredShortcutSuspendScope(InputManager inputManager)
        : IDisposable
    {
        private InputManager? _inputManager = inputManager;

        public void Dispose()
        {
            var inputManager = _inputManager;
            if (inputManager == null) return;
            _inputManager = null;
            inputManager.ResumeRegisteredShortcuts();
        }
    }
}