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

    /// <summary>条目被整体清掉之后只丢引用；整理还在跑时切回来要能再挂一张</summary>
    [Fact]
    public void Forget_AfterItemsCleared_AllowsShowingAgain()
    {
        HandoffWritingPlaceholder placeholder = NewPlaceholder();
        placeholder.Show();
        _items.Clear();

        placeholder.Forget();
        placeholder.Show();

        Assert.IsType<HandoffWritingItem>(Assert.Single(_items));
    }
}
