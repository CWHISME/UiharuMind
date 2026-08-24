using SharpHook.Data;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.Input.Linux;

/// <summary>
/// 基于 evdev 的 Linux 全局输入监听。
/// 直接读内核 /dev/input/event*，完全绕过显示服务器，因此在 Wayland 下同样有效
/// （libuiohook/SharpHook 在 Linux 只有 X11 的 XRecord 实现，Wayland 下收不到全局事件）。
///
/// 刻意不做 EVIOCGRAB：独占设备才能吞键，但那要求把所有未消费的按键用 uinput 逐个回注，
/// 等于把本应用变成键盘驱动，输入法、游戏、长按连发都进入射程。代价是命中的快捷键
/// 同时也会送达前台应用，需引导用户选择不易冲突的组合键。
/// </summary>
internal sealed class EvDevInputHookBackend : IInputHookBackend
{
    /// poll 超时，决定停止时最长等待多久退出循环
    private const int PollTimeoutMilliseconds = 200;

    /// 内核里 BTN_* 从 0x100 起，之下才是键盘键
    private const ushort FirstButtonCode = 0x100;

    private readonly object _stateLock = new();
    private readonly HashSet<KeyCode> _pressedKeys = new();
    private readonly HashSet<MouseButton> _pressedButtons = new();
    private readonly IPointerLocator _pointerLocator;

    private List<int> _fileDescriptors = new();
    private Thread? _pollThread;
    private TaskCompletionSource? _runCompletion;
    private volatile bool _running;

    private int _pendingRelativeX;
    private int _pendingRelativeY;
    private int _pendingWheel;
    private short _lastX;
    private short _lastY;

    public bool IsRunning => _running;

    public event Action? HookEnabled;
    public event Action? HookDisabled;
    public event Func<KeyCode, bool>? KeyPressed;
    public event Action<KeyCode>? KeyReleased;
    public event Action<MouseEventData>? MousePressed;
    public event Action<MouseEventData>? MouseReleased;
    public event Action<MouseEventData>? MouseMoved;
    public event Action<MouseEventData>? MouseDragged;
    public event Action<MouseWheelEventData>? MouseWheel;

    public EvDevInputHookBackend(IPointerLocator pointerLocator)
    {
        _pointerLocator = pointerLocator;
    }

    /// <summary>
    /// 与 SharpHook 后端不同，这里能给出真实的按下状态，无需返回 null 让上层猜。
    /// </summary>
    public bool? IsKeyPressed(KeyCode keyCode)
    {
        lock (_stateLock)
        {
            return _pressedKeys.Contains(keyCode);
        }
    }

    /// Dispose 后仍可再次 RunAsync：权限引导窗关闭时会走 Stop 再 Start 的路径，
    /// 后端必须是可重启的（SharpHook 后端同样如此）
    public Task RunAsync()
    {
        if (_running) return _runCompletion?.Task ?? Task.CompletedTask;

        var descriptors = OpenDevices();
        if (descriptors.Count == 0)
        {
            throw new InvalidOperationException(
                "无法打开任何 /dev/input/event* 设备。需要将当前用户加入 input 组后重新登录。");
        }

        _fileDescriptors = descriptors;
        _runCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _running = true;

        _pollThread = new Thread(PollLoop)
        {
            IsBackground = true,
            Name = "UiharuMind evdev hook"
        };
        _pollThread.Start();

        SafeInvoke(HookEnabled);
        return _runCompletion.Task;
    }

    private static List<int> OpenDevices()
    {
        var descriptors = new List<int>();
        foreach (var device in EvDevDeviceScanner.Scan())
        {
            int fd = LinuxInputNative.Open(device.EventPath,
                LinuxInputNative.OpenReadOnly | LinuxInputNative.OpenNonBlock);
            if (fd < 0) continue;
            descriptors.Add(fd);
        }

        return descriptors;
    }

    private void PollLoop()
    {
        var pollFds = _fileDescriptors
            .Select(fd => new LinuxInputNative.PollFd { Fd = fd, Events = LinuxInputNative.PollIn })
            .ToArray();

        try
        {
            while (_running)
            {
                int ready = LinuxInputNative.Poll(pollFds, (uint)pollFds.Length, PollTimeoutMilliseconds);
                if (ready <= 0) continue;

                for (int i = 0; i < pollFds.Length; i++)
                {
                    if ((pollFds[i].ReturnedEvents & LinuxInputNative.PollIn) == 0) continue;
                    DrainDevice(pollFds[i].Fd);
                }
            }
        }
        catch (Exception e)
        {
            Log.Error(e);
        }
        finally
        {
            SafeInvoke(HookDisabled);
            _runCompletion?.TrySetResult();
        }
    }

    private void DrainDevice(int fd)
    {
        var inputEvent = default(LinuxInputNative.InputEvent);
        int eventSize = System.Runtime.InteropServices.Marshal.SizeOf<LinuxInputNative.InputEvent>();

        // 非阻塞 fd 上一直读到 EAGAIN，避免残留事件拖到下一轮 poll
        while (_running)
        {
            nint read = LinuxInputNative.Read(fd, ref inputEvent, eventSize);
            if (read != eventSize) return;
            Dispatch(inputEvent);
        }
    }

    private void Dispatch(LinuxInputNative.InputEvent inputEvent)
    {
        switch (inputEvent.Type)
        {
            case LinuxInputNative.EvKey:
                DispatchKey(inputEvent);
                break;
            case LinuxInputNative.EvRel:
                AccumulateRelative(inputEvent);
                break;
            case LinuxInputNative.EvSyn when inputEvent.Code == LinuxInputNative.SynReport:
                FlushRelative();
                break;
        }
    }

    private void DispatchKey(LinuxInputNative.InputEvent inputEvent)
    {
        // value == 2 是内核的自动重复，交给上层会被当成新的一次按下
        if (inputEvent.Value == 2) return;

        bool isDown = inputEvent.Value == 1;
        if (inputEvent.Code >= FirstButtonCode)
        {
            DispatchMouseButton(inputEvent.Code, isDown);
            return;
        }

        var keyCode = EvDevKeyCodeMapper.ToKeyCode(inputEvent.Code);
        if (keyCode == KeyCode.VcUndefined) return;

        lock (_stateLock)
        {
            if (isDown) _pressedKeys.Add(keyCode);
            else _pressedKeys.Remove(keyCode);
        }

        if (isDown)
        {
            // 返回值是「是否吞掉此键」。evdev 旁路监听无法吞键，调用结果只作日志用途之外的语义被丢弃
            SafeInvoke(KeyPressed, keyCode);
        }
        else
        {
            SafeInvoke(KeyReleased, keyCode);
        }
    }

    private void DispatchMouseButton(ushort code, bool isDown)
    {
        var button = EvDevKeyCodeMapper.ToMouseButton(code);
        if (button == MouseButton.NoButton) return;

        lock (_stateLock)
        {
            if (isDown) _pressedButtons.Add(button);
            else _pressedButtons.Remove(button);
        }

        var data = CreateMouseData(button);
        if (isDown) SafeInvoke(MousePressed, data);
        else SafeInvoke(MouseReleased, data);
    }

    private void AccumulateRelative(LinuxInputNative.InputEvent inputEvent)
    {
        switch (inputEvent.Code)
        {
            case LinuxInputNative.RelX:
                _pendingRelativeX += inputEvent.Value;
                break;
            case LinuxInputNative.RelY:
                _pendingRelativeY += inputEvent.Value;
                break;
            case LinuxInputNative.RelWheel:
                _pendingWheel += inputEvent.Value;
                break;
        }
    }

    /// SYN_REPORT 才是一次完整输入报告的边界，按报告合并可避免把一次移动拆成 X、Y 两个事件
    private void FlushRelative()
    {
        int wheel = _pendingWheel;
        bool moved = _pendingRelativeX != 0 || _pendingRelativeY != 0;
        _pendingRelativeX = 0;
        _pendingRelativeY = 0;
        _pendingWheel = 0;

        if (wheel != 0)
        {
            SafeInvoke(MouseWheel, new MouseWheelEventData
            {
                X = _lastX,
                Y = _lastY,
                Rotation = (short)Math.Clamp(wheel, short.MinValue, short.MaxValue),
                Delta = 1,
                Direction = MouseWheelScrollDirection.Vertical,
                Type = MouseWheelScrollType.UnitScroll
            });
        }

        if (!moved) return;

        var data = CreateMouseData(MouseButton.NoButton);
        bool dragging;
        lock (_stateLock)
        {
            dragging = _pressedButtons.Count > 0;
        }

        if (dragging) SafeInvoke(MouseDragged, data);
        else SafeInvoke(MouseMoved, data);
    }

    /// evdev 只给相对位移，屏幕坐标必须向 IPointerLocator 要；拿不到时沿用上一次已知值，
    /// 避免把 (0,0) 当成真实坐标喂给依赖它定位窗口的上层
    private MouseEventData CreateMouseData(MouseButton button)
    {
        if (_pointerLocator.TryGetPosition(out short x, out short y))
        {
            _lastX = x;
            _lastY = y;
        }

        return new MouseEventData
        {
            Button = button,
            Clicks = 1,
            X = _lastX,
            Y = _lastY
        };
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

    private static void SafeInvoke<T>(Func<T, bool>? action, T value)
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

    /// 只停监听、不废掉对象：光标定位器由 InputBackendFactory 持有并复用，此处不得释放，
    /// 否则重启钩子后全局坐标会永久失效
    public void Dispose()
    {
        if (!_running && _fileDescriptors.Count == 0) return;
        _running = false;

        _pollThread?.Join(PollTimeoutMilliseconds * 3);
        _pollThread = null;

        foreach (var fd in _fileDescriptors)
        {
            LinuxInputNative.Close(fd);
        }

        _fileDescriptors = new List<int>();

        lock (_stateLock)
        {
            _pressedKeys.Clear();
            _pressedButtons.Clear();
        }

        _runCompletion?.TrySetResult();
    }
}
