using SharpHook.Data;
using UiharuMind.Core.Input.Linux;
using Xunit;

namespace UiharuMind.Core.Tests.Input;

/// <summary>
/// 键码映射表是 Linux 后端与全应用之间唯一的翻译层，一处错位就表现为「某个快捷键永远不触发」，
/// 而这种故障只能在真机上察觉，因此在这里把关键锚点钉死。
/// </summary>
public class EvDevKeyCodeMapperTests
{
    [Theory]
    [InlineData((ushort)30, KeyCode.VcA)]
    [InlineData((ushort)44, KeyCode.VcZ)]
    [InlineData((ushort)2, KeyCode.Vc1)]
    [InlineData((ushort)11, KeyCode.Vc0)]
    [InlineData((ushort)28, KeyCode.VcEnter)]
    [InlineData((ushort)29, KeyCode.VcLeftControl)]
    [InlineData((ushort)97, KeyCode.VcRightControl)]
    [InlineData((ushort)56, KeyCode.VcLeftAlt)]
    [InlineData((ushort)125, KeyCode.VcLeftMeta)]
    [InlineData((ushort)59, KeyCode.VcF1)]
    [InlineData((ushort)88, KeyCode.VcF12)]
    public void ToKeyCode_MapsKernelCodes(ushort evDevCode, KeyCode expected)
    {
        Assert.Equal(expected, EvDevKeyCodeMapper.ToKeyCode(evDevCode));
    }

    [Fact]
    public void ToKeyCode_ReturnsUndefinedForUnknownCode()
    {
        Assert.Equal(KeyCode.VcUndefined, EvDevKeyCodeMapper.ToKeyCode(9999));
    }

    [Fact]
    public void ToEvDevCode_RoundTripsEveryMappedKey()
    {
        foreach (var evDevCode in EvDevKeyCodeMapper.AllKeyCodes)
        {
            var keyCode = EvDevKeyCodeMapper.ToKeyCode(evDevCode);
            Assert.Equal(evDevCode, EvDevKeyCodeMapper.ToEvDevCode(keyCode));
        }
    }

    [Fact]
    public void ToEvDevCode_ReturnsZeroForUnmappedKey()
    {
        Assert.Equal(0, EvDevKeyCodeMapper.ToEvDevCode(KeyCode.VcUndefined));
    }

    [Theory]
    [InlineData((ushort)0x110, MouseButton.Button1)]
    [InlineData((ushort)0x111, MouseButton.Button2)]
    [InlineData((ushort)0x112, MouseButton.Button3)]
    public void ToMouseButton_MapsKernelButtons(ushort evDevCode, MouseButton expected)
    {
        Assert.Equal(expected, EvDevKeyCodeMapper.ToMouseButton(evDevCode));
        Assert.Equal(evDevCode, EvDevKeyCodeMapper.ToEvDevButton(expected));
    }

    [Fact]
    public void ToMouseButton_ReturnsNoButtonForKeyboardCode()
    {
        Assert.Equal(MouseButton.NoButton, EvDevKeyCodeMapper.ToMouseButton(30));
    }
}
