using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.Core.Utils;
using UiharuMind.Core.Input.Linux;
using SharpHook.Data;

namespace UiharuMind.Core.Input;

/// <summary>
/// 按平台能力挑选输入后端，并在能力缺失时逐级降级。
///
/// SharpHook(libuiohook) 在 Linux 只有 X11 的 XRecord/XTest 实现，Wayland 下既收不到全局事件
/// 也无法把模拟事件送进原生 Wayland 应用；evdev/uinput 直接走内核输入子系统，不经显示服务器，
/// 因此成为 Linux 上的首选，SharpHook 退居 X11 会话的兜底。
/// </summary>
internal static class InputBackendFactory
{
    private static IPointerLocator? _pointerLocator;

    /// <summary>
    /// 全局光标定位器。非 Linux 平台的钩子事件自带坐标，返回不可用实例即可。
    /// </summary>
    public static IPointerLocator PointerLocator => _pointerLocator ??= CreatePointerLocator();

    public static IInputHookBackend CreateHookBackend()
    {
        if (!PlatformUtils.IsLinux) return new SharpHookInputHookBackend();

        var capabilities = LinuxInputCapabilities.Probe();
        if (capabilities.CanReadInputDevices)
        {
            Log.Debug("Linux 输入监听：使用 evdev 后端。");
            return new EvDevInputHookBackend(PointerLocator);
        }

        // Wayland 下没有 evdev 就没有任何可用的全局监听：SharpHook(libuiohook) 在 Wayland 收不到事件，
        // 且它自带线程会另开一条 X11 连接，与 Avalonia 的 UI 线程 X11 使用相互踩踏，空闲即触发 xcb 崩溃。
        // 因此 Wayland 下不再降级到 SharpHook，直接给空后端，由权限引导窗提示用户加入 input 组。
        if (PlatformUtils.IsWayland)
        {
            Log.Warning(capabilities.HasInputDevices
                ? "无权读取 /dev/input/event*（需加入 input 组并重新登录），Wayland 下无可用全局监听，已禁用。"
                : "未发现任何输入设备，Wayland 下无可用全局监听，已禁用。");
            return new NullInputHookBackend();
        }

        Log.Warning(capabilities.HasInputDevices
            ? "无权读取 /dev/input/event*（需加入 input 组），降级到 SharpHook。"
            : "未发现任何输入设备，降级到 SharpHook。");
        return new SharpHookInputHookBackend();
    }

    public static IInputSimulatorBackend CreateSimulatorBackend()
    {
        if (!PlatformUtils.IsLinux) return new SharpHookInputSimulatorBackend();

        var capabilities = LinuxInputCapabilities.Probe();
        if (capabilities.CanWriteUinput)
        {
            Log.Debug("Linux 输入模拟：使用 uinput 后端。");
            return new UInputInputSimulatorBackend(() => InputManager.Instance.IsAnyModifierPressed());
        }

        // 同监听：Wayland 下 SharpHook 模拟的事件到不了原生应用，且其 X11 线程会与 Avalonia 冲突，
        // 故同样只作为 X11 会话的兜底，Wayland 下用空后端。
        if (PlatformUtils.IsWayland)
        {
            Log.Warning($"无权写入 {LinuxInputCapabilities.UinputDevicePath}，Wayland 下无可用输入模拟，已禁用。");
            return new NullInputSimulatorBackend();
        }

        Log.Warning($"无权写入 {LinuxInputCapabilities.UinputDevicePath}，降级到 SharpHook。");
        return new SharpHookInputSimulatorBackend();
    }

    private static IPointerLocator CreatePointerLocator()
    {
        if (!PlatformUtils.IsLinux) return new UnavailablePointerLocator();

        var locator = X11PointerLocator.TryCreate();
        if (locator != null) return locator;

        Log.Warning("XWayland 不可用，无法查询全局光标位置，依赖鼠标位置的弹窗将退化为居中显示。");
        return new UnavailablePointerLocator();
    }
}

/// <summary>
/// Wayland 下既无 evdev 读取权限、又无 uinput 写入权限时的空后端：不接入任何全局输入，
/// 避免 SharpHook(libuiohook) 在 Wayland 上起一条 X11 线程与 Avalonia 的 UI 线程 X11 使用互相踩踏。
/// 全局快捷键/模拟在此环境下本就无法工作，缺权限一事交由权限引导窗提示。
/// </summary>
internal sealed class NullInputHookBackend : IInputHookBackend
{
    public bool IsRunning => false;
    public event Action? HookEnabled;
    public event Action? HookDisabled;
    public event Func<KeyCode, bool>? KeyPressed;
    public event Action<KeyCode>? KeyReleased;
    public event Action<MouseEventData>? MousePressed;
    public event Action<MouseEventData>? MouseReleased;
    public event Action<MouseEventData>? MouseMoved;
    public event Action<MouseEventData>? MouseDragged;
    public event Action<MouseWheelEventData>? MouseWheel;

    public Task RunAsync() => Task.CompletedTask;
    public bool? IsKeyPressed(KeyCode keyCode) => null;
    public void Dispose() { }
}

/// <summary>
/// 与 <see cref="NullInputHookBackend"/> 同理，Wayland 下无 uinput 时的空输入模拟后端。
/// </summary>
internal sealed class NullInputSimulatorBackend : IInputSimulatorBackend
{
    public void SendMouseMove(short x, short y) { }
    public void SendMouseMoveRelative(short x, short y) { }
    public Task SendMouseClick(MouseButton button, int delayMs = 100) => Task.CompletedTask;
    public void SimulateMousePress(MouseButton button) { }
    public void SimulateMouseRelease(MouseButton button) { }
    public void SimulateMouseWheel(int wheelDelta) { }
    public Task SendKeyPress(KeyCode keyCode, int delayMs = 100) => Task.CompletedTask;
    public void SimulateKeyPress(KeyCode keyCode) { }
    public void SimulateKeyRelease(KeyCode keyCode) { }
    public Task SendText(string text, int delayBetweenKeys = 50) => Task.CompletedTask;
}
