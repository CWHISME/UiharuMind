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
        HistoryWindow window = new(20, 20); //关掉首屏分批,只测整窗语义

        (int from, int to) = window.Reset(5);

        Assert.Equal(0, from);
        Assert.Equal(5, to);
        Assert.False(window.HasEarlier);
    }

    [Fact]
    public void Reset_LongHistoryRendersTailWindow()
    {
        HistoryWindow window = new(20, 20); //关掉首屏分批,只测整窗语义

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
        HistoryWindow window = new(20, 20); //关掉首屏分批,只测整窗语义

        (int from, int to) = window.Reset(20);

        Assert.Equal(0, from);
        Assert.Equal(20, to);
        Assert.False(window.HasEarlier);
    }

    [Fact]
    public void Reset_EmptyHistory()
    {
        HistoryWindow window = new(20, 20); //关掉首屏分批,只测整窗语义

        (int from, int to) = window.Reset(0);

        Assert.Equal(0, from);
        Assert.Equal(0, to);
        Assert.False(window.HasEarlier);
    }

    [Fact]
    public void Extend_PrependsPreviousWindow()
    {
        HistoryWindow window = new(20, 20); //关掉首屏分批,只测整窗语义
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
        HistoryWindow window = new(20, 20); //关掉首屏分批,只测整窗语义
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
        HistoryWindow window = new(20, 20); //关掉首屏分批,只测整窗语义
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
        HistoryWindow window = new(20, 20); //关掉首屏分批,只测整窗语义
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
        HistoryWindow window = new(20, 20); //关掉首屏分批,只测整窗语义
        window.Reset(100); //起点 80

        (int From, int To)? range = window.Extend(3);

        Assert.NotNull(range);
        Assert.Equal(0, range!.Value.From);
        Assert.Equal(3, range.Value.To);
    }

    [Fact]
    public void Clear_ResetsToBeginning()
    {
        HistoryWindow window = new(20, 20); //关掉首屏分批,只测整窗语义
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

    /// <summary>
    /// 首屏只给一小批，凑够整窗的那段记在账上
    /// </summary>
    [Fact]
    public void Reset_RendersFirstScreenOnly()
    {
        HistoryWindow window = new(20, 8);

        (int from, int to) = window.Reset(100);

        Assert.Equal(92, from);
        Assert.Equal(100, to);
        Assert.True(window.HasEarlier);
    }

    [Fact]
    public void FillFirstWindow_TopsUpToFullWindow()
    {
        HistoryWindow window = new(20, 8);
        window.Reset(100);

        (int From, int To)? range = window.FillFirstWindow(100);

        Assert.NotNull(range);
        Assert.Equal(80, range!.Value.From);
        Assert.Equal(92, range.Value.To);
        Assert.Equal(80, window.Start);
        Assert.True(window.HasEarlier);
    }

    /// <summary>
    /// 只欠一次：补齐之后再调用什么都不给，否则会把「加载更早」的那一窗重复插进去
    /// </summary>
    [Fact]
    public void FillFirstWindow_OnlyOnce()
    {
        HistoryWindow window = new(20, 8);
        window.Reset(100);
        window.FillFirstWindow(100);

        Assert.Null(window.FillFirstWindow(100));
        Assert.Equal(80, window.Start);
    }

    /// <summary>
    /// 历史不足一窗：首屏之后补齐正好到开头，此后没有更早的消息
    /// </summary>
    [Fact]
    public void FillFirstWindow_ShortHistoryReachesBeginning()
    {
        HistoryWindow window = new(20, 8);
        window.Reset(12);

        (int From, int To)? range = window.FillFirstWindow(12);

        Assert.NotNull(range);
        Assert.Equal(0, range!.Value.From);
        Assert.Equal(4, range.Value.To);
        Assert.Equal(0, window.Start);
        Assert.False(window.HasEarlier);
    }

    /// <summary>
    /// 历史本来就没超过首屏：不欠账，也就没有可补的
    /// </summary>
    [Fact]
    public void FillFirstWindow_WithinFirstScreenGivesNothing()
    {
        HistoryWindow window = new(20, 8);
        window.Reset(5);

        Assert.Null(window.FillFirstWindow(5));
        Assert.Equal(0, window.Start);
        Assert.False(window.HasEarlier);
    }

    /// <summary>
    /// 用户已经自己往前翻过：首屏那笔账作废，不能在续窗之后又前插一段
    /// </summary>
    [Fact]
    public void FillFirstWindow_AfterExtendIsCancelled()
    {
        HistoryWindow window = new(20, 8);
        window.Reset(100);
        window.Extend(100); //起点 72

        Assert.Null(window.FillFirstWindow(100));
        Assert.Equal(72, window.Start);
    }

    /// <summary>
    /// 运行期裁剪之后：补首屏等于把刚裁掉的一段贴回去，必须作废
    /// </summary>
    [Fact]
    public void FillFirstWindow_AfterSetStartIsCancelled()
    {
        HistoryWindow window = new(20, 8);
        window.Reset(100);

        window.SetStart(95);

        Assert.Null(window.FillFirstWindow(100));
        Assert.Equal(95, window.Start);
    }

    /// <summary>
    /// 首屏大小不超过整窗：给过头等于关掉分批
    /// </summary>
    [Fact]
    public void FirstScreenSize_ClampedToWindowSize()
    {
        Assert.Equal(20, new HistoryWindow(20, 50).FirstScreenSize);
        Assert.Equal(HistoryWindow.DefaultFirstScreenSize, new HistoryWindow(20, 0).FirstScreenSize);
    }

    /// <summary>
    /// 运行期裁剪：起点由外部锚点直接给定，「有更早消息」随之为真
    /// </summary>
    [Fact]
    public void SetStart_MovesWindowStartAndReportsEarlier()
    {
        HistoryWindow window = new(20, 20); //关掉首屏分批,只测整窗语义
        window.Reset(10); //整段都在窗口里,此时没有更早的消息

        window.SetStart(4);

        Assert.Equal(4, window.Start);
        Assert.True(window.HasEarlier);
    }

    /// <summary>
    /// 起点回到 0：到头了就该报没有更早的消息
    /// </summary>
    [Fact]
    public void SetStart_ZeroHasNoEarlier()
    {
        HistoryWindow window = new(20, 20); //关掉首屏分批,只测整窗语义
        window.Reset(100);

        window.SetStart(0);

        Assert.Equal(0, window.Start);
        Assert.False(window.HasEarlier);
    }

    /// <summary>
    /// 负值按 0 处理：锚点算错不该把窗口推到非法下标上
    /// </summary>
    [Fact]
    public void SetStart_NegativeClampsToZero()
    {
        HistoryWindow window = new(20, 20); //关掉首屏分批,只测整窗语义
        window.Reset(100);

        window.SetStart(-5);

        Assert.Equal(0, window.Start);
        Assert.False(window.HasEarlier);
    }

    /// <summary>
    /// 裁剪之后仍然能继续前扩，且前扩是从新起点往前算的
    /// </summary>
    [Fact]
    public void Extend_AfterSetStartContinuesFromTheNewStart()
    {
        HistoryWindow window = new(20, 20); //关掉首屏分批,只测整窗语义
        window.Reset(100);
        window.SetStart(50);

        (int from, int to) = window.Extend(100)!.Value;

        Assert.Equal(30, from);
        Assert.Equal(50, to);
        Assert.Equal(30, window.Start);
    }
}
