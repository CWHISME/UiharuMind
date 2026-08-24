using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.Core.Utils;
using UiharuMind.Core.Input.Linux;

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

        Log.Warning(capabilities.HasInputDevices
            ? "无权读取 /dev/input/event*（需加入 input 组），降级到 SharpHook，Wayland 下将收不到全局事件。"
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

        Log.Warning($"无权写入 {LinuxInputCapabilities.UinputDevicePath}，降级到 SharpHook，" +
                    "Wayland 下模拟事件不会送达原生应用。");
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
