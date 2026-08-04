using UiharuMind.Core.AI.Chat;

namespace UiharuMind.Core.Tests.Chat;

/// <summary>
/// 历史渲染窗口的下标运算。原先散在 ViewModel 的 _historyStart 上，
/// 边界（正好一窗、不足一窗、连续前扩到头）没有任何覆盖。
/// </summary>
public class HistoryWindowTests
{
    [Fact]
    public void Reset_ShortHistoryRendersEverything()
    {
        HistoryWindow window = new(20);

        (int from, int to) = window.Reset(5);

        Assert.Equal(0, from);
        Assert.Equal(5, to);
        Assert.False(window.HasEarlier);
    }

    [Fact]
    public void Reset_LongHistoryRendersTailWindow()
    {
        HistoryWindow window = new(20);

        (int from, int to) = window.Reset(100);

        Assert.Equal(80, from);
        Assert.Equal(100, to);
        Assert.True(window.HasEarlier);
    }

    /// <summary>
    /// 正好一窗：没有更早的消息，「加载更早」按钮不该出现
    /// </summary>
    [Fact]
    public void Reset_ExactlyOneWindowHasNoEarlier()
    {
        HistoryWindow window = new(20);

        (int from, int to) = window.Reset(20);

        Assert.Equal(0, from);
        Assert.Equal(20, to);
        Assert.False(window.HasEarlier);
    }

    [Fact]
    public void Reset_EmptyHistory()
    {
        HistoryWindow window = new(20);

        (int from, int to) = window.Reset(0);

        Assert.Equal(0, from);
        Assert.Equal(0, to);
        Assert.False(window.HasEarlier);
    }

    [Fact]
    public void Extend_PrependsPreviousWindow()
    {
        HistoryWindow window = new(20);
        window.Reset(100);

        (int From, int To)? range = window.Extend(100);

        Assert.NotNull(range);
        Assert.Equal(60, range!.Value.From);
        Assert.Equal(80, range.Value.To);
        Assert.Equal(60, window.Start);
        Assert.True(window.HasEarlier);
    }

    [Fact]
    public void Extend_PartialLastBatchStopsAtZero()
    {
        HistoryWindow window = new(20);
        window.Reset(25); //起点 5

        (int From, int To)? range = window.Extend(25);

        Assert.NotNull(range);
        Assert.Equal(0, range!.Value.From);
        Assert.Equal(5, range.Value.To);
        Assert.Equal(0, window.Start);
        Assert.False(window.HasEarlier);
    }

    [Fact]
    public void Extend_AtBeginningReturnsNull()
    {
        HistoryWindow window = new(20);
        window.Reset(10); //已全部渲染

        Assert.Null(window.Extend(10));
        Assert.False(window.HasEarlier);
    }

    /// <summary>
    /// 连续前扩直到到头，每批不重不漏
    /// </summary>
    [Fact]
    public void Extend_RepeatedlyCoversWholeHistoryExactlyOnce()
    {
        HistoryWindow window = new(20);
        List<int> rendered = new();

        (int from, int to) = window.Reset(95);
        for (int i = from; i < to; i++) rendered.Add(i);

        while (window.Extend(95) is { } range)
        {
            for (int i = range.From; i < range.To; i++) rendered.Add(i);
        }

        Assert.Equal(95, rendered.Count);
        Assert.Equal(Enumerable.Range(0, 95).ToHashSet(), rendered.ToHashSet());
    }

    /// <summary>
    /// 历史在窗口之后被截短（重试丢弃了尾部）时不能给出越界区间
    /// </summary>
    [Fact]
    public void Extend_HistoryShrunkBelowStart_ClampsInsteadOfGoingOutOfRange()
    {
        HistoryWindow window = new(20);
        window.Reset(100); //起点 80

        (int From, int To)? range = window.Extend(3);

        Assert.NotNull(range);
        Assert.Equal(0, range!.Value.From);
        Assert.Equal(3, range.Value.To);
    }

    [Fact]
    public void Clear_ResetsToBeginning()
    {
        HistoryWindow window = new(20);
        window.Reset(100);

        window.Clear();

        Assert.Equal(0, window.Start);
        Assert.False(window.HasEarlier);
    }

    [Fact]
    public void NonPositiveSize_FallsBackToDefault()
    {
        Assert.Equal(HistoryWindow.DefaultSize, new HistoryWindow(0).Size);
        Assert.Equal(HistoryWindow.DefaultSize, new HistoryWindow(-5).Size);
    }
}
