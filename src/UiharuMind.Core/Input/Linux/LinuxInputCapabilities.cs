namespace UiharuMind.Core.Input.Linux;

/// <summary>
/// 一次 Linux 输入能力探测的结果
/// </summary>
/// <param name="HasInputDevices">/proc 里能看到键盘或指针设备</param>
/// <param name="CanReadInputDevices">能真正打开 /dev/input/event*（需 input 组）</param>
/// <param name="CanWriteUinput">能打开 /dev/uinput 写入（需 uinput 组 + udev 规则）</param>
public readonly record struct LinuxInputCapabilityReport(
    bool HasInputDevices,
    bool CanReadInputDevices,
    bool CanWriteUinput);

/// <summary>
/// 探测 Linux 输入相关的权限与设备可用性。
/// 探测与使用分开，是为了让权限引导界面能在不启动钩子的前提下逐项报告缺什么。
/// </summary>
public static class LinuxInputCapabilities
{
    public const string UinputDevicePath = "/dev/uinput";

    /// <summary>
    /// 执行一次能力探测
    /// </summary>
    /// <returns>各项能力的探测结果</returns>
    public static LinuxInputCapabilityReport Probe()
    {
        var devices = EvDevDeviceScanner.Scan();
        bool canRead = devices.Any(device => CanOpen(device.EventPath, LinuxInputNative.OpenReadOnly));
        bool canWriteUinput = CanOpen(UinputDevicePath, LinuxInputNative.OpenWriteOnly);
        return new LinuxInputCapabilityReport(devices.Count > 0, canRead, canWriteUinput);
    }

    private static bool CanOpen(string path, int flags)
    {
        int fd = LinuxInputNative.Open(path, flags | LinuxInputNative.OpenNonBlock);
        if (fd < 0) return false;
        LinuxInputNative.Close(fd);
        return true;
    }
}
