/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Collections.Generic;
using System.Threading;
using UiharuMind.Features.Conversation;
using UiharuMind.Features.Conversation.Items;
using UiharuMind.Shared.Utils.Tools;

namespace UiharuMind.App.Tests.Conversation;

/// <summary>整理交接文档的占位卡：同一次整理只挂一张，收掉才跟底，切会话清场后切回来能重新挂上</summary>
public class HandoffWritingPlaceholderTests
{
    private readonly List<ConversationItemBase> _items = [];
    private int _scrolls;

    private HandoffWritingPlaceholder NewPlaceholder() => new(_items, () => _scrolls++);

    [Fact]
    public void Show_Twice_AddsOneCard()
    {
        HandoffWritingPlaceholder placeholder = NewPlaceholder();

        placeholder.Show();
        placeholder.Show();

        Assert.IsType<HandoffWritingItem>(Assert.Single(_items));
        Assert.Equal(1, _scrolls);
    }

    [Fact]
    public void Hide_RemovesCardAndScrolls()
    {
        HandoffWritingPlaceholder placeholder = NewPlaceholder();
        placeholder.Show();

        placeholder.Hide();

        Assert.Empty(_items);
        Assert.Equal(2, _scrolls);
    }

    [Fact]
    public void Hide_WhenNotShown_DoesNothing()
    {
        NewPlaceholder().Hide();

        Assert.Equal(0, _scrolls);
    }

    /// <summary>占位卡的跑表：首值等节拍，节拍一拍就填上耗时；跑表由收掉/清场停掉</summary>
    [Fact]
    public void Show_ThenFlush_FillsElapsedText()
    {
        HandoffWritingPlaceholder placeholder = NewPlaceholder();
        placeholder.Show();

        HandoffWritingItem item = Assert.IsType<HandoffWritingItem>(Assert.Single(_items));
        try
        {
            // 建卡可能不在 UI 线程，首值留给节拍回调，不在这里写
            Assert.Equal(string.Empty, item.TimeText);

            ((IStreamFlushTarget)item).FlushForDisplay();

            Assert.NotEqual(string.Empty, item.TimeText);
        }
        finally
        {
            item.StopTiming(); // FlushForDisplay 会自续进全局泵，测试结束前摘掉
        }
    }

    /// <summary>
    /// 占位卡 live 跑表与工具卡同一口径：满 1 秒后向下取整到整秒，不会出现 1.1s、2.2s 这种小数
    /// </summary>
    [Fact]
    public void Flush_OverOneSecond_FloorsToWholeSeconds()
    {
        HandoffWritingPlaceholder placeholder = NewPlaceholder();
        placeholder.Show();
        HandoffWritingItem item = Assert.IsType<HandoffWritingItem>(Assert.Single(_items));
        try
        {
            Thread.Sleep(1100); // 跨过 1 秒，落在向下取整分支
            ((IStreamFlushTarget)item).FlushForDisplay();

            Assert.Equal("1s", item.TimeText); // 约 1.1s，取整即 1s，不含小数
        }
        finally
        {
            item.StopTiming();
        }
    }

    /// <summary>
    /// 流式进度：驱动方每几十字报一次已写出的 token 数，占位卡随同一拍跑表上屏。
    /// 报数只记整数、不动布局——每块都刷会把会话流刷得直抖
    /// </summary>
    [Fact]
    public void ReportOutputTokens_ShowsOnNextFlush()
    {
        HandoffWritingPlaceholder placeholder = NewPlaceholder();
        placeholder.Show();
        HandoffWritingItem item = Assert.IsType<HandoffWritingItem>(Assert.Single(_items));
        try
        {
            Assert.Equal(string.Empty, item.TokenText); //还没报过：不显示

            placeholder.ReportOutputTokens(1234);
            Assert.Equal(string.Empty, item.TokenText); //记数不等于上屏，得等节拍

            ((IStreamFlushTarget)item).FlushForDisplay();

            Assert.Equal("1.2k token", item.TokenText);
        }
        finally
        {
            item.StopTiming();
        }
    }

    /// <summary>卡收掉之后迟到的进度丢掉：那是上一发的尾巴，别让它污染下一张卡</summary>
    [Fact]
    public void ReportOutputTokens_AfterHide_IsDropped()
    {
        HandoffWritingPlaceholder placeholder = NewPlaceholder();
        placeholder.Show();
        placeholder.Hide();

        placeholder.ReportOutputTokens(999); //不该抛

        Assert.Empty(_items);
    }

    /// <summary>清场停表后再挂一张，跑表从新条目自己的起点重新走</summary>
    [Fact]
    public void Forget_ThenShow_StartsFreshTimingOnNewCard()
    {
        HandoffWritingPlaceholder placeholder = NewPlaceholder();
        placeholder.Show();
        HandoffWritingItem first = Assert.IsType<HandoffWritingItem>(Assert.Single(_items));
        _items.Clear();

        placeholder.Forget();
        placeholder.Show();

        HandoffWritingItem second = Assert.IsType<HandoffWritingItem>(Assert.Single(_items));
        Assert.NotSame(first, second);
        try
        {
            ((IStreamFlushTarget)second).FlushForDisplay();
            Assert.NotEqual(string.Empty, second.TimeText);
        }
        finally
        {
            second.StopTiming();
        }
    }
}
