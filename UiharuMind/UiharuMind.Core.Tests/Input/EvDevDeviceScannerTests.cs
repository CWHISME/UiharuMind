using UiharuMind.Core.Input.Linux;
using Xunit;

namespace UiharuMind.Core.Tests.Input;

/// <summary>
/// 设备清单解析决定了钩子会打开哪些设备，漏掉键盘就等于快捷键全废，
/// 而 /proc 的分块格式只有空行做边界，容易在收尾处漏掉最后一块。
/// </summary>
public class EvDevDeviceScannerTests
{
    private static readonly string[] SampleLines =
    {
        "I: Bus=0019 Vendor=0000 Product=0001 Version=0000",
        "N: Name=\"Power Button\"",
        "H: Handlers=kbd event0 ",
        "B: EV=3",
        "",
        "I: Bus=0003 Vendor=046d Product=c52b Version=0111",
        "N: Name=\"Logitech USB Receiver Mouse\"",
        "H: Handlers=mouse0 event3 ",
        "B: EV=17",
        "",
        "I: Bus=0000 Vendor=0000 Product=0000 Version=0000",
        "N: Name=\"Video Bus\"",
        "H: Handlers=event9 ",
        "B: EV=3",
        "",
        "I: Bus=0011 Vendor=0001 Product=0001 Version=ab41",
        "N: Name=\"AT Translated Set 2 keyboard\"",
        "H: Handlers=sysrq kbd event2 "
    };

    [Fact]
    public void Parse_KeepsOnlyKeyboardAndPointerDevices()
    {
        var devices = EvDevDeviceScanner.Parse(SampleLines);

        Assert.Equal(3, devices.Count);
        Assert.DoesNotContain(devices, device => device.Name == "Video Bus");
    }

    [Fact]
    public void Parse_ReadsLastBlockWithoutTrailingBlankLine()
    {
        var devices = EvDevDeviceScanner.Parse(SampleLines);

        var keyboard = Assert.Single(devices, device => device.Name == "AT Translated Set 2 keyboard");
        Assert.Equal("/dev/input/event2", keyboard.EventPath);
        Assert.True(keyboard.IsKeyboard);
        Assert.False(keyboard.IsPointer);
    }

    [Fact]
    public void Parse_ClassifiesPointerDevices()
    {
        var devices = EvDevDeviceScanner.Parse(SampleLines);

        var mouse = Assert.Single(devices, device => device.Name == "Logitech USB Receiver Mouse");
        Assert.Equal("/dev/input/event3", mouse.EventPath);
        Assert.True(mouse.IsPointer);
        Assert.False(mouse.IsKeyboard);
    }

    [Fact]
    public void Parse_ReturnsEmptyForBlankInput()
    {
        Assert.Empty(EvDevDeviceScanner.Parse(new[] { "", "  ", "" }));
    }

    /// 本应用自己用 uinput 造的虚拟设备必须被排除，否则监听会收到自己注入的按键，
    /// 形成「一模拟就自触发」的回环
    [Fact]
    public void Parse_SkipsOwnVirtualDevices()
    {
        var lines = new[]
        {
            "I: Bus=0003 Vendor=0000 Product=0000 Version=0000",
            $"N: Name=\"{EvDevDeviceScanner.VirtualDeviceNamePrefix} Keyboard\"",
            "H: Handlers=sysrq kbd event20 ",
            "B: EV=3",
            "",
            "I: Bus=0011 Vendor=0001 Product=0001 Version=ab41",
            "N: Name=\"AT Translated Set 2 keyboard\"",
            "H: Handlers=sysrq kbd event2 "
        };

        var devices = EvDevDeviceScanner.Parse(lines);

        Assert.Single(devices);
        Assert.Equal("/dev/input/event2", devices[0].EventPath);
    }
}
