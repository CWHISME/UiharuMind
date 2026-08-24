using SharpHook.Data;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.Input.Linux;

/// <summary>
/// 基于 /dev/uinput 的 Linux 输入模拟。
/// 分三台虚拟设备而非合成一台：libinput 按设备能力判定设备类型，
/// 把相对指针与绝对指针混在同一台设备上会导致它被误判，进而丢事件。
///
/// 绝对移动必须走 ABS 设备：相对位移会被合成器的指针加速曲线改写，位移量与请求值不等，
/// 自动点击回放的落点会逐步漂移。
/// </summary>
internal sealed class UInputInputSimulatorBackend : IInputSimulatorBackend, IDisposable
{
    /// ABS 轴量程。用固定量程而非桌面像素数，桌面分辨率变化时无需重建设备
    private const int AbsoluteAxisRange = 65535;

    /// 注入前等待物理修饰键释放的轮询上限，超时则照常注入
    private const int ModifierWaitAttempts = 20;
    private const int ModifierWaitIntervalMilliseconds = 50;

    private readonly Lazy<UInputDevice?> _keyboard;
    private readonly Lazy<UInputDevice?> _relativePointer;
    private readonly Lazy<UInputDevice?> _absolutePointer;
    private readonly Func<bool> _isAnyModifierPressed;

    public UInputInputSimulatorBackend(Func<bool> isAnyModifierPressed)
    {
        _isAnyModifierPressed = isAnyModifierPressed;

        // 延迟创建：没有自动点击需求的会话不该无谓地在系统里挂三台虚拟设备
        _keyboard = new Lazy<UInputDevice?>(() => UInputDevice.TryCreate(
            "UiharuMind Virtual Keyboard", EvDevKeyCodeMapper.AllKeyCodes, Array.Empty<ushort>()));

        _relativePointer = new Lazy<UInputDevice?>(() => UInputDevice.TryCreate(
            "UiharuMind Virtual Pointer",
            new ushort[]
            {
                LinuxInputNative.BtnLeft, LinuxInputNative.BtnRight, LinuxInputNative.BtnMiddle,
                LinuxInputNative.BtnSide, LinuxInputNative.BtnExtra
            },
            new[] { LinuxInputNative.RelX, LinuxInputNative.RelY, LinuxInputNative.RelWheel }));

        _absolutePointer = new Lazy<UInputDevice?>(() => UInputDevice.TryCreate(
            "UiharuMind Virtual Absolute Pointer",
            new ushort[] { LinuxInputNative.BtnLeft },
            Array.Empty<ushort>(),
            new Dictionary<ushort, int>
            {
                [LinuxInputNative.AbsX] = AbsoluteAxisRange,
                [LinuxInputNative.AbsY] = AbsoluteAxisRange
            }));
    }

    public void SendMouseMove(short x, short y)
    {
        var device = _absolutePointer.Value;
        if (device == null) return;

        int absX = ToAbsolute(x, LinuxDesktopMetrics.Width);
        int absY = ToAbsolute(y, LinuxDesktopMetrics.Height);
        device.Emit(LinuxInputNative.EvAbs, LinuxInputNative.AbsX, absX);
        device.Emit(LinuxInputNative.EvAbs, LinuxInputNative.AbsY, absY);
        device.Sync();
    }

    private static int ToAbsolute(int pixel, int extent)
    {
        if (extent <= 1) return 0;
        long scaled = (long)pixel * AbsoluteAxisRange / (extent - 1);
        return (int)Math.Clamp(scaled, 0, AbsoluteAxisRange);
    }

    public void SendMouseMoveRelative(short x, short y)
    {
        var device = _relativePointer.Value;
        if (device == null) return;

        if (x != 0) device.Emit(LinuxInputNative.EvRel, LinuxInputNative.RelX, x);
        if (y != 0) device.Emit(LinuxInputNative.EvRel, LinuxInputNative.RelY, y);
        device.Sync();
    }

    public async Task SendMouseClick(MouseButton button, int delayMs = 100)
    {
        SimulateMousePress(button);
        await Task.Delay(delayMs);
        SimulateMouseRelease(button);
    }

    public void SimulateMousePress(MouseButton button)
    {
        EmitButton(button, true);
    }

    public void SimulateMouseRelease(MouseButton button)
    {
        EmitButton(button, false);
    }

    private void EmitButton(MouseButton button, bool isDown)
    {
        var device = _relativePointer.Value;
        var code = EvDevKeyCodeMapper.ToEvDevButton(button);
        if (device == null || code == 0) return;

        device.Emit(LinuxInputNative.EvKey, code, isDown ? 1 : 0);
        device.Sync();
    }

    public void SimulateMouseWheel(int wheelDelta)
    {
        var device = _relativePointer.Value;
        if (device == null || wheelDelta == 0) return;

        device.Emit(LinuxInputNative.EvRel, LinuxInputNative.RelWheel, Math.Sign(wheelDelta));
        device.Sync();
    }

    public async Task SendKeyPress(KeyCode keyCode, int delayMs = 100)
    {
        await WaitPhysicalModifiersReleased();
        SimulateKeyPress(keyCode);
        await Task.Delay(delayMs);
        SimulateKeyRelease(keyCode);
    }

    public void SimulateKeyPress(KeyCode keyCode)
    {
        EmitKey(keyCode, true);
    }

    public void SimulateKeyRelease(KeyCode keyCode)
    {
        EmitKey(keyCode, false);
    }

    private void EmitKey(KeyCode keyCode, bool isDown)
    {
        var device = _keyboard.Value;
        var code = EvDevKeyCodeMapper.ToEvDevCode(keyCode);
        if (device == null || code == 0) return;

        device.Emit(LinuxInputNative.EvKey, code, isDown ? 1 : 0);
        device.Sync();
    }

    public async Task SendText(string text, int delayBetweenKeys = 50)
    {
        await WaitPhysicalModifiersReleased();
        foreach (char c in text)
        {
            var keyCode = InputKeyCodeMapper.CharToKeyCode(c);
            if (keyCode == KeyCode.VcUndefined) continue;
            SimulateKeyPress(keyCode);
            await Task.Delay(delayBetweenKeys);
            SimulateKeyRelease(keyCode);
        }
    }

    /// 用户手上可能还按着触发快捷键的修饰键，此刻注入会与之组合成完全不同的按键。
    /// 等它们松开再注入，等不到就照常进行，避免把回放卡死
    private async Task WaitPhysicalModifiersReleased()
    {
        for (int attempt = 0; attempt < ModifierWaitAttempts; attempt++)
        {
            if (!_isAnyModifierPressed()) return;
            await Task.Delay(ModifierWaitIntervalMilliseconds);
        }

        Log.Warning("等待物理修饰键释放超时，仍继续注入。");
    }

    public void Dispose()
    {
        if (_keyboard.IsValueCreated) _keyboard.Value?.Dispose();
        if (_relativePointer.IsValueCreated) _relativePointer.Value?.Dispose();
        if (_absolutePointer.IsValueCreated) _absolutePointer.Value?.Dispose();
    }
}
