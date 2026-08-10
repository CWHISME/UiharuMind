using UiharuMind.Core.AI.Execution;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// 钉死输入预算的换算。预留量必须随上下文缩放：取固定 8192 会让一个 4096 上下文的本地模型
/// 算出负预算，构造阈值时当场抛；上下文未知时必须得出 0，表示「不压缩」而不是「压到零」。
/// </summary>
public class HistoryCompactionTests
{
    [Theory]
    [InlineData(4096, 512)] //4096/8=512,落在下限上
    [InlineData(128_000, 8192)] //16000 被上限夹回 8192
    [InlineData(1_048_576, 8192)]
    [InlineData(1000, 512)] //125 被下限抬到 512
    public void Reserve_ScalesWithContextAndStaysClamped(int contextLength, int expected)
    {
        Assert.Equal(expected, HistoryCompaction.ReserveFor(contextLength));
    }

    [Theory]
    [InlineData(4096, 3584)]
    [InlineData(128_000, 119_808)]
    [InlineData(1_048_576, 1_040_384)]
    public void InputBudget_IsContextMinusReserve(int contextLength, int expected)
    {
        Assert.Equal(expected, HistoryCompaction.InputBudgetFor(contextLength));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void UnknownContext_YieldsZeroBudgetMeaningNoCompaction(int contextLength)
    {
        Assert.Equal(0, HistoryCompaction.InputBudgetFor(contextLength));
    }

    [Fact]
    public void TinyContext_StillYieldsPositiveBudget()
    {
        //预留量的下限比上下文本身还大时,预算必须仍然为正,否则阈值算出 0 会把历史压干净
        Assert.True(HistoryCompaction.InputBudgetFor(256) > 0);
    }

    [Fact]
    public void EvictionFiresBeforeTruncation()
    {
        //折叠工具结果是更温和的一步,水位必须低于截断,否则永远轮不到它
        Assert.True(HistoryCompaction.ToolEvictionThreshold < HistoryCompaction.TruncationThreshold);
    }

    [Fact]
    public void Create_ReturnsAStrategy()
    {
        Assert.NotNull(HistoryCompaction.Create(() => 128_000));
    }
}
