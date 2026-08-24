namespace UiharuMind.Core.Input.Linux;

/// <summary>
/// 虚拟桌面的像素尺寸。
/// uinput 的绝对定位设备用 0..量程 的归一化坐标，必须知道桌面有多大才能把像素换算过去；
/// 而桌面尺寸只有 UI 层（Avalonia 的 Screens）知道，所以由它在屏幕信息就绪后写入。
/// </summary>
public static class LinuxDesktopMetrics
{
    private static volatile int _width = 1920;
    private static volatile int _height = 1080;

    public static int Width => _width;

    public static int Height => _height;

    /// <summary>
    /// 更新虚拟桌面尺寸
    /// </summary>
    /// <param name="width">桌面总宽度（像素）</param>
    /// <param name="height">桌面总高度（像素）</param>
    public static void Update(int width, int height)
    {
        if (width > 0) _width = width;
        if (height > 0) _height = height;
    }
}
