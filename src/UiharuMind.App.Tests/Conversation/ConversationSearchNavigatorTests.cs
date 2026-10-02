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
using UiharuMind.Core.AI.Chat.Search;
using UiharuMind.Features.Conversation;
using UiharuMind.Features.Conversation.Items;
using UiharuMind.Features.Conversation.Search;

namespace UiharuMind.App.Tests.Conversation;

/// <summary>搜索跳转的决策：已在窗内直接滚、空闲时截断重载、有轮在跑分批续、命中没了就不跳</summary>
public class ConversationSearchNavigatorTests
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
        public bool CanReload { get; set; } = true;
        public ConversationHistoryPager Pager { get; }
        public ConversationSearchNavigator Navigator { get; }

        public Fixture(int messageCount)
        {
            for (int i = 0; i < messageCount; i++)
                History.Add(new ChatMessage(i % 2 == 0 ? ChatRole.User : ChatRole.Assistant, $"m{i}"));

            ConversationItemActions actions = new(Items, new StubHost(), new RecordingMessageService());
            ConversationHistoryRenderer renderer = new(Items, actions, () => null, () => false, () => null, () => null);
            Pager = new ConversationHistoryPager(Items, renderer, () => History, () => true, () => true, () => false);
            Navigator = new ConversationSearchNavigator(Items, Pager, () => History, () => CanReload);
            Pager.Replay(History, liveTail: false);
        }

        public SessionSearchHit Hit(int index) => new(index, History[index], ESearchHitKind.Text, $"m{index}");
    }

    [Fact]
    public void Plan_InWindow_DoesNotReload()
    {
        Fixture fixture = new(100);

        Assert.Equal(ESearchJumpPlan.InWindow, fixture.Navigator.Plan(fixture.Hit(99)));
        Assert.False(fixture.Pager.HasLaterMessages);
    }

    [Fact]
    public void Plan_OutOfWindowWhileIdle_ReloadsAroundTheHit()
    {
        Fixture fixture = new(300);
        SessionSearchHit hit = fixture.Hit(50);

        Assert.Equal(ESearchJumpPlan.Reloaded, fixture.Navigator.Plan(hit));
        Assert.True(fixture.Pager.HasLaterMessages);
        Assert.NotNull(fixture.Navigator.Find(hit));
        Assert.Same(hit, fixture.Navigator.Anchor);
    }

    [Fact]
    public void Plan_WhileBusy_FallsBackToIncrementalUntilTheHitIsDrawn()
    {
        Fixture fixture = new(300) { CanReload = false };
        SessionSearchHit hit = fixture.Hit(200);

        Assert.Equal(ESearchJumpPlan.Incremental, fixture.Navigator.Plan(hit));
        while (fixture.Navigator.LoadEarlierToward(hit)) { }

        Assert.NotNull(fixture.Navigator.Find(hit));
        Assert.False(fixture.Pager.HasLaterMessages); //分批续窗不脱离末尾
    }

    [Fact]
    public void Plan_HitNoLongerInHistory_IsNotFound()
    {
        Fixture fixture = new(10);
        SessionSearchHit hit = fixture.Hit(3);
        fixture.History.RemoveAt(3);

        Assert.Equal(ESearchJumpPlan.NotFound, fixture.Navigator.Plan(hit));
    }

    [Fact]
    public void ReturnToLatest_ClearsTheAnchor()
    {
        Fixture fixture = new(300);
        fixture.Navigator.Plan(fixture.Hit(50));

        fixture.Pager.ReturnToLatest();

        Assert.Null(fixture.Navigator.Anchor);
    }
}
