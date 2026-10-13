using UiharuMind.Core.AI.Execution.Tools.Desktop.Mac;

namespace UiharuMind.Core.Tests.Execution.Desktop;

/// <summary>
/// 修饰键序列（ADR 0084 真实键鼠）：按下的完整序列与累计标志。只把 flags 挂在主键事件上、
/// 不显式发修饰键 down/up，会让系统以为修饰键一直按着（Cmd 卡住 → 点 Dock 变「打开所在位置」）
/// </summary>
public class MacKeysTests
{
    [Fact]
    public void ModifierDowns_CmdShift_AreOrderedWithAccumulatedFlags()
    {
        IReadOnlyList<(ushort KeyCode, ulong Flags)> downs = MacKeys.ModifierDowns(MacKeys.FlagCommand | MacKeys.FlagShift);

        Assert.Equal(
        [
            (MacKeys.KeyCommand, MacKeys.FlagCommand),
            (MacKeys.KeyShift, MacKeys.FlagCommand | MacKeys.FlagShift),
        ], downs);
    }

    [Fact]
    public void ModifierDowns_Single_ReturnsJustThatModifier()
    {
        IReadOnlyList<(ushort KeyCode, ulong Flags)> downs = MacKeys.ModifierDowns(MacKeys.FlagCommand);

        Assert.Equal([(MacKeys.KeyCommand, MacKeys.FlagCommand)], downs);
    }

    [Fact]
    public void ModifierDowns_None_IsEmpty()
    {
        Assert.Empty(MacKeys.ModifierDowns(0));
    }

    [Fact]
    public void ModifierDowns_AllFour_AccumulatesInFixedOrder()
    {
        ulong all = MacKeys.FlagCommand | MacKeys.FlagShift | MacKeys.FlagOption | MacKeys.FlagControl;
        IReadOnlyList<(ushort KeyCode, ulong Flags)> downs = MacKeys.ModifierDowns(all);

        Assert.Equal(4, downs.Count);
        Assert.Equal(MacKeys.KeyCommand, downs[0].KeyCode);
        Assert.Equal(MacKeys.FlagCommand, downs[0].Flags); //第一个按下只带自己
        Assert.Equal(MacKeys.KeyShift, downs[1].KeyCode);
        Assert.Equal(MacKeys.FlagCommand | MacKeys.FlagShift, downs[1].Flags); //第二个累计前一个
        Assert.Equal(MacKeys.KeyOption, downs[2].KeyCode);
        Assert.Equal(all & ~MacKeys.FlagControl, downs[2].Flags);
        Assert.Equal(MacKeys.KeyControl, downs[3].KeyCode);
        Assert.Equal(all, downs[3].Flags); //最后一个全按着
    }
}
