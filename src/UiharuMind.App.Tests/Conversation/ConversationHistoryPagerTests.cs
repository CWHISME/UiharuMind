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
using System.Linq;
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
            Pager = new ConversationHistoryPager(Items, renderer, () => History, () => IsDisplayed, () => true,
                () => false);
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

    /// <summary>搜索跳转往前续：一次一批，目标进窗就停，不把整段历史一口气画出来</summary>
    [Fact]
    public void LoadEarlierToward_StopsOnceTheTargetIsInTheWindow()
    {
        Fixture fixture = new(200);
        fixture.Pager.Replay(fixture.History, liveTail: false);

        int batches = 0;
        while (fixture.Pager.LoadEarlierToward(100)) batches++;

        Assert.Equal(3, batches); //首屏起点 195,每批 40 条:155 → 115 → 75
        Assert.Equal(75, fixture.Pager.Window.Start);
        Assert.False(fixture.Pager.LoadEarlierToward(150)); //已在窗内
        Assert.True(fixture.Pager.HasLoadedEarlier);
    }

    /// <summary>截断重载：不管跳多远都只画命中附近一小段，窗口脱离末尾</summary>
    [Fact]
    public void JumpTo_RendersOnlyAroundTheTarget()
    {
        Fixture fixture = new(600);
        fixture.Pager.Replay(fixture.History, liveTail: false);

        Assert.True(fixture.Pager.JumpTo(100));

        Assert.Equal(40, fixture.Items.Count); //前 10 条、后 30 条
        Assert.Same(fixture.History[90], fixture.Items[0].SourceMessage);
        Assert.True(fixture.Pager.HasLaterMessages);
        Assert.True(fixture.Pager.HasEarlierMessages);
        Assert.True(fixture.Pager.IsInWindow(100));
        Assert.False(fixture.Pager.IsInWindow(599));
    }

    [Fact]
    public void LoadLater_AppendsUntilTheEndThenReattaches()
    {
        Fixture fixture = new(100);
        fixture.Pager.Replay(fixture.History, liveTail: false);
        fixture.Pager.JumpTo(10); //[0, 40)

        Assert.True(fixture.Pager.LoadLater()); //[0, 80)
        Assert.True(fixture.Pager.LoadLater()); //[0, 100),跟上末尾
        Assert.False(fixture.Pager.HasLaterMessages);
        Assert.False(fixture.Pager.LoadLater());
        Assert.Equal(100, fixture.Items.Count);
        Assert.Same(fixture.History[99], fixture.Items[^1].SourceMessage);
    }

    [Fact]
    public void ReturnToLatest_ReplaysTheTailFirstScreen()
    {
        Fixture fixture = new(300);
        fixture.Pager.Replay(fixture.History, liveTail: false);
        Assert.False(fixture.Pager.ReturnToLatest()); //没脱离就什么都不做

        fixture.Pager.JumpTo(5);
        Assert.True(fixture.Pager.ReturnToLatest());

        Assert.False(fixture.Pager.HasLaterMessages);
        Assert.Equal(HistoryWindow.DefaultFirstScreenSize, fixture.Items.Count);
        Assert.Same(fixture.History[299], fixture.Items[^1].SourceMessage);
    }

    /// <summary>脱离末尾时历史有追加：不接在这一小段后面（中间隔着没画的一段），直接回到最新</summary>
    [Fact]
    public void AppendPersisted_WhileDetached_ReturnsToLatest()
    {
        Fixture fixture = new(300);
        fixture.Pager.Replay(fixture.History, liveTail: false);
        fixture.Pager.JumpTo(5);
        int returned = 0;
        fixture.Pager.ReturnedToLatest += () => returned++;

        fixture.History.Add(new ChatMessage(ChatRole.User, "新来的"));
        fixture.Pager.AppendPersisted(fixture.History, 300, ownTurn: false, streaming: false);

        Assert.Equal(1, returned);
        Assert.False(fixture.Pager.HasLaterMessages);
        Assert.Same(fixture.History[300], fixture.Items[^1].SourceMessage);
    }

    /// <summary>脱离末尾时删了窗内一条：往后续不跳过前移进窗边界的那条</summary>
    [Fact]
    public void NoteRemoved_WhileDetached_LoadLaterDoesNotSkip()
    {
        Fixture fixture = new(100);
        fixture.Pager.Replay(fixture.History, liveTail: false);
        fixture.Pager.JumpTo(10); //[0, 40)

        ChatMessage doomed = fixture.History[20];
        fixture.History.RemoveAt(20);
        fixture.Items.Remove(fixture.Items.First(x => ReferenceEquals(x.SourceMessage, doomed))); //删除连卡一起摘
        fixture.Pager.NoteRemoved([20]);
        fixture.Pager.LoadLater(); //[0, 79)

        Assert.Same(fixture.History[39], fixture.Items[39].SourceMessage); //删除前的第 40 条,没被跳过
        Assert.Same(fixture.History[78], fixture.Items[^1].SourceMessage);
    }

    /// <summary>重试截到窗内某条及之后：窗口接回末尾，不必整窗重放</summary>
    [Fact]
    public void NoteRemoved_TruncatingFromInsideTheWindow_Reattaches()
    {
        Fixture fixture = new(100);
        fixture.Pager.Replay(fixture.History, liveTail: false);
        fixture.Pager.JumpTo(10); //[0, 40)

        fixture.History.RemoveRange(25, 75);
        fixture.Pager.NoteRemoved(Enumerable.Range(25, 75).ToList());

        Assert.False(fixture.Pager.HasLaterMessages);
        Assert.False(fixture.Pager.ReturnToLatest()); //已经接回末尾,不会再重放
    }

    [Fact]
    public void JumpTo_NearTheEnd_StaysAttached()
    {
        Fixture fixture = new(50);
        fixture.Pager.Replay(fixture.History, liveTail: false);

        fixture.Pager.JumpTo(45);

        Assert.False(fixture.Pager.HasLaterMessages);
        Assert.Same(fixture.History[49], fixture.Items[^1].SourceMessage);
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
