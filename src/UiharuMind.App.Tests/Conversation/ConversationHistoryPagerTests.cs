/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Collections.Generic;
using System.Collections.ObjectModel;
using Microsoft.Extensions.AI;
using UiharuMind.App.Tests.TestDoubles;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Features.Conversation;
using UiharuMind.Features.Conversation.Items;

namespace UiharuMind.App.Tests.Conversation;

/// <summary>
/// 历史开窗的两个状态位。「有更早」错了，顶部那个「加载更早」要么点不出东西、要么该有却不见；
/// 「翻过更早」错了，短会话一进来就挂着「已到会话开头」
/// </summary>
public class ConversationHistoryPagerTests
{
    private sealed class StubHost : IConversationItemActionHost
    {
        public ChatSession? Session => null;
        public bool IsGenerating => false;
        public void Rerun(ChatMessage? input) { }
        public void NotifySessionsChanged() { }
        public void NotifyItemsWired() { }
    }

    private sealed class Fixture
    {
        public ObservableCollection<ConversationItemBase> Items { get; } = new();
        public List<ChatMessage> History { get; } = new();
        public bool IsDisplayed { get; set; } = true;
        public ConversationHistoryPager Pager { get; }

        public Fixture(int messageCount)
        {
            for (int i = 0; i < messageCount; i++)
            {
                History.Add(new ChatMessage(i % 2 == 0 ? ChatRole.User : ChatRole.Assistant, $"m{i}"));
            }

            ConversationItemActions actions = new(Items, new StubHost(), new RecordingMessageService());
            ConversationHistoryRenderer renderer = new(Items, actions, () => null, () => false, () => null, () => null);
            Pager = new ConversationHistoryPager(Items, renderer, () => History, () => IsDisplayed, () => true);
        }
    }

    [Fact]
    public void Replay_ShowsFirstScreenOnly()
    {
        Fixture fixture = new(30);

        fixture.Pager.Replay(fixture.History, liveTail: false);

        Assert.Equal(HistoryWindow.DefaultFirstScreenSize, fixture.Items.Count);
        Assert.True(fixture.Pager.HasEarlierMessages);
        Assert.False(fixture.Pager.HasLoadedEarlier);
    }

    [Fact]
    public void Replay_ShortSession_HasNothingEarlier()
    {
        Fixture fixture = new(3);

        fixture.Pager.Replay(fixture.History, liveTail: false);

        Assert.Equal(3, fixture.Items.Count);
        Assert.False(fixture.Pager.HasEarlierMessages);
    }

    /// <summary>补齐首窗不是用户往前翻，不该点亮「已到会话开头」</summary>
    [Fact]
    public void FillFirstWindow_DoesNotCountAsLoadingEarlier()
    {
        Fixture fixture = new(30);
        fixture.Pager.Replay(fixture.History, liveTail: false);

        Assert.True(fixture.Pager.FillFirstWindow());

        Assert.Equal(HistoryWindow.DefaultSize, fixture.Items.Count);
        Assert.False(fixture.Pager.HasLoadedEarlier);
    }

    [Fact]
    public void LoadEarlier_UntilStart_ClearsHasEarlier()
    {
        Fixture fixture = new(12);
        fixture.Pager.Replay(fixture.History, liveTail: false);

        while (fixture.Pager.LoadEarlier())
        {
        }

        Assert.Equal(12, fixture.Items.Count);
        Assert.True(fixture.Pager.HasLoadedEarlier);
        Assert.False(fixture.Pager.HasEarlierMessages);
    }

    /// <summary>不在界面上的实例按后台上限裁，裁完「有更早」要跟着亮</summary>
    [Fact]
    public void TrimToBudget_NotDisplayed_TrimsToBackgroundBudget()
    {
        Fixture fixture = new(40);
        fixture.Pager.Replay(fixture.History, liveTail: false);
        while (fixture.Pager.LoadEarlier())
        {
        }

        fixture.IsDisplayed = false;
        fixture.Pager.TrimToBudget();

        Assert.True(fixture.Items.Count <= ConversationItemWindowTrimmer.DefaultBackgroundMaxItems);
        Assert.True(fixture.Pager.HasEarlierMessages);
    }

    [Fact]
    public void Reset_ClearsBothFlags()
    {
        Fixture fixture = new(30);
        fixture.Pager.Replay(fixture.History, liveTail: false);
        fixture.Pager.LoadEarlier();

        fixture.Pager.Reset();

        Assert.False(fixture.Pager.HasEarlierMessages);
        Assert.False(fixture.Pager.HasLoadedEarlier);
    }
}
