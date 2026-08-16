using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Execution.Assembly;
using UiharuMind.Core.AI.Execution.History;

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
        Assert.NotNull(HistoryCompaction.Create(() => 128_000, new TurnInputEstimate()));
    }

    [Fact]
    public void HistoryQuota_DeductsFixedOverhead()
    {
        int budget = HistoryCompaction.InputBudgetFor(128_000);
        Assert.Equal(budget - 25_600, HistoryCompaction.HistoryQuotaFor(128_000, 25_600));
        //没有固定开销时退回输入预算本身,即本条引入之前的口径
        Assert.Equal(budget, HistoryCompaction.HistoryQuotaFor(128_000, 0));
    }

    /// <summary>
    /// 复现促成这条改动的那个场景：128k 模型挂一套 21k 的 MCP 工具集（连系统提示合计 25.6k 固定开销）。
    /// 旧口径把截断水位乘在<b>输入预算</b>上，于是水位 + 固定开销已经超出上下文上限——
    /// 截断在请求早就发不出去之后才动手。新口径乘在历史额度上，加回固定开销必须仍在上限之内。
    /// </summary>
    [Fact]
    public void TruncationWatermark_LeavesRoomForFixedOverhead()
    {
        const int context = 128_000;
        const int fixedOverhead = 25_600;

        int oldWatermark = (int)(HistoryCompaction.InputBudgetFor(context) * HistoryCompaction.TruncationThreshold);
        Assert.True(oldWatermark + fixedOverhead > context, "旧口径本就该是超的,否则这个测试没在测该测的东西");

        int watermark = (int)(HistoryCompaction.HistoryQuotaFor(context, fixedOverhead) *
                              HistoryCompaction.TruncationThreshold);
        Assert.True(watermark + fixedOverhead < context);
    }

    [Fact]
    public void HistoryQuota_FloorsAtZeroWhenOverheadEatsTheBudget()
    {
        //固定开销自己就吃光预算:额度为 0,触发条件据此一律不压缩——此时压缩救不了,只会白毁历史
        Assert.Equal(0, HistoryCompaction.HistoryQuotaFor(8192, 100_000));
    }
}
