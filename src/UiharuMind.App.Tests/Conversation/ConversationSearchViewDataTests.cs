/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using UiharuMind.App.Tests.Headless;
using UiharuMind.Core.AI.Chat.Search;
using UiharuMind.Features.Conversation.Search;

namespace UiharuMind.App.Tests.Conversation;

/// <summary>
/// 搜索栏的导航口径：结果新到旧、只有用户明确要跳才发跳转、重搜时选中原位保住。
/// 防抖靠界面线程上的计时器，所以跑在无头调度线程上
/// </summary>
[Collection(HeadlessCollection.Name)]
public class ConversationSearchViewDataTests
{
    private static readonly TimeSpan PastDebounce = TimeSpan.FromMilliseconds(600);

    private static List<ChatMessage> History(params string[] texts) =>
        texts.Select((x, i) => new ChatMessage(i % 2 == 0 ? ChatRole.User : ChatRole.Assistant, x)).ToList();

    [Fact]
    public void Search_ListsNewestFirst_AndDoesNotJumpUntilAsked()
    {
        HeadlessUi.RunAsync(async () =>
        {
            List<ChatMessage> history = History("缓存一", "无关", "缓存二", "缓存三");
            ConversationSearchViewData search = new(() => history);
            List<SessionSearchHit> jumps = new();
            search.JumpRequested += jumps.Add;

            search.Open();
            search.Query = "缓存";
            await Task.Delay(PastDebounce);

            Assert.Equal([3, 2, 0], search.Hits.Select(x => x.Hit.MessageIndex));
            Assert.Empty(jumps); //边打字边跳就是每敲一个字前插一大段
            Assert.Equal(-1, search.SelectedIndex);
        });
    }

    [Fact]
    public void OlderAndNewer_WrapAndRequestJumps()
    {
        HeadlessUi.RunAsync(async () =>
        {
            List<ChatMessage> history = History("缓存一", "缓存二", "缓存三");
            ConversationSearchViewData search = new(() => history);
            List<SessionSearchHit> jumps = new();
            search.JumpRequested += jumps.Add;
            search.Open();
            search.Query = "缓存";
            await search.SearchNowAsync();

            search.Older(); //第一次落在最新那条
            search.Older();
            search.Newer();
            search.Newer(); //绕回最早

            Assert.Equal([2, 1, 2, 0], jumps.Select(x => x.MessageIndex));
            Assert.Equal("3/3", search.StatusText);
        });
    }

    /// <summary>只有一条命中时再按回车也得能再跳（滚走之后想回来）；选中没变不等于不跳</summary>
    [Fact]
    public void Older_OnTheSameHit_JumpsAgain()
    {
        HeadlessUi.RunAsync(async () =>
        {
            List<ChatMessage> history = History("缓存");
            ConversationSearchViewData search = new(() => history);
            List<SessionSearchHit> jumps = new();
            search.JumpRequested += jumps.Add;
            search.Open();
            search.Query = "缓存";
            await search.SearchNowAsync();

            search.Older();
            search.Older();
            search.JumpToSelected(); //再点一次已选中的那行

            Assert.Equal(3, jumps.Count);
        });
    }

    [Fact]
    public void HistoryChanged_KeepsTheSelectedMessage()
    {
        HeadlessUi.RunAsync(async () =>
        {
            List<ChatMessage> history = History("缓存一", "缓存二");
            ConversationSearchViewData search = new(() => history);
            List<SessionSearchHit> jumps = new();
            search.JumpRequested += jumps.Add;
            search.Open();
            search.Query = "缓存";
            await search.SearchNowAsync();
            search.Older();
            search.Older(); //选中「缓存一」
            ChatMessage selected = search.Hits[search.SelectedIndex].Hit.Message;

            history.Add(new ChatMessage(ChatRole.User, "缓存三"));
            search.NotifyHistoryChanged();
            await Task.Delay(PastDebounce);

            Assert.Equal(3, search.Hits.Count);
            Assert.Same(selected, search.Hits[search.SelectedIndex].Hit.Message);
            Assert.Equal(2, jumps.Count); //原位选回不算用户要跳
        });
    }

    [Fact]
    public void ScopeToggle_ResearchesImmediately()
    {
        HeadlessUi.RunAsync(async () =>
        {
            List<ChatMessage> history = [new(ChatRole.Assistant, [new TextReasoningContent("缓存"), new TextContent("结论")])];
            ConversationSearchViewData search = new(() => history);
            search.Open();
            search.Query = "缓存";
            await search.SearchNowAsync();
            Assert.Empty(search.Hits);

            search.ToggleThinking();
            await Task.Delay(PastDebounce);

            Assert.Single(search.Hits);
        });
    }

    [Fact]
    public void Close_ClearsHitsButKeepsTheQuery()
    {
        HeadlessUi.RunAsync(async () =>
        {
            List<ChatMessage> history = History("缓存");
            ConversationSearchViewData search = new(() => history);
            search.Open();
            search.Query = "缓存";
            await search.SearchNowAsync();

            search.Close();
            Assert.Empty(search.Hits);
            Assert.Equal("缓存", search.Query);

            search.Open(); //重开按原词接着搜
            await Task.Delay(PastDebounce);
            Assert.Single(search.Hits);
        });
    }
}
