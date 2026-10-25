/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Collections.Generic;
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
