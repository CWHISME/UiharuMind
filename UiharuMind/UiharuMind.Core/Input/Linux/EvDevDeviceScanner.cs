namespace UiharuMind.Core.Input.Linux;

/// <summary>
/// 一台 evdev 输入设备的描述
/// </summary>
/// <param name="EventPath">/dev/input/eventN 路径</param>
/// <param name="Name">设备名</param>
/// <param name="IsKeyboard">是否挂了 kbd handler</param>
/// <param name="IsPointer">是否挂了 mouse handler</param>
internal readonly record struct EvDevDeviceInfo(string EventPath, string Name, bool IsKeyboard, bool IsPointer);

/// <summary>
/// 从 /proc/bus/input/devices 枚举并分类输入设备。
/// 用 proc 文本而非 EVIOCGBIT ioctl，是因为 proc 全局可读，即使没有 input 组权限也能先探明设备清单，
/// 从而把「有没有设备」和「有没有权限」两种失败区分开来报给用户。
/// </summary>
internal static class EvDevDeviceScanner
{
    private const string ProcDevicesPath = "/proc/bus/input/devices";

    /// <summary>
    /// 枚举所有键盘或指针设备
    /// </summary>
    /// <returns>设备列表，读取失败时返回空列表</returns>
    public static IReadOnlyList<EvDevDeviceInfo> Scan()
    {
        try
        {
            return Parse(File.ReadAllLines(ProcDevicesPath));
        }
        catch (Exception)
        {
            return Array.Empty<EvDevDeviceInfo>();
        }
    }

    /// <summary>
    /// 解析 /proc/bus/input/devices 的内容
    /// </summary>
    /// <param name="lines">文件各行</param>
    /// <returns>键盘或指针设备列表</returns>
    internal static IReadOnlyList<EvDevDeviceInfo> Parse(IEnumerable<string> lines)
    {
        var devices = new List<EvDevDeviceInfo>();
        string name = string.Empty;
        string handlers = string.Empty;

        foreach (var line in lines)
        {
            if (line.StartsWith("N: Name=", StringComparison.Ordinal))
            {
                name = line["N: Name=".Length..].Trim().Trim('"');
                continue;
            }

            if (line.StartsWith("H: Handlers=", StringComparison.Ordinal))
            {
                handlers = line["H: Handlers=".Length..].Trim();
                continue;
            }

            // 设备块之间以空行分隔，此时才是一条记录读完的时刻
            if (!string.IsNullOrWhiteSpace(line)) continue;

            AppendDevice(devices, name, handlers);
            name = string.Empty;
            handlers = string.Empty;
        }

        AppendDevice(devices, name, handlers);
        return devices;
    }

    private static void AppendDevice(List<EvDevDeviceInfo> devices, string name, string handlers)
    {
        if (string.IsNullOrEmpty(handlers)) return;

        var tokens = handlers.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var eventNode = tokens.FirstOrDefault(t => t.StartsWith("event", StringComparison.Ordinal));
        if (eventNode == null) return;

        // kbd 是固定名字，指针则带序号（mouse0、mouse1……），不能按字面量比对
        bool isKeyboard = tokens.Contains("kbd");
        bool isPointer = tokens.Any(token => token.StartsWith("mouse", StringComparison.Ordinal));
        if (!isKeyboard && !isPointer) return;

        devices.Add(new EvDevDeviceInfo($"/dev/input/{eventNode}", name, isKeyboard, isPointer));
    }
}
