/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Collections.Generic;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat.Search;
using UiharuMind.Features.Conversation.Items;
using UiharuMind.Features.Conversation.Search;

namespace UiharuMind.App.Tests.Conversation;

/// <summary>命中 → 卡片：按来源消息认，历史挪过位也认得回来，命中在思考就落到思考卡上</summary>
public class ConversationSearchLocatorTests
{
    [Fact]
    public void IndexOf_FollowsTheMessageWhenHistoryShifted()
    {
        ChatMessage target = new(ChatRole.User, "缓存");
        List<ChatMessage> history = [new(ChatRole.User, "新插的"), target];
        SessionSearchHit hit = new(0, target, ESearchHitKind.Text, "缓存");

        Assert.Equal(1, ConversationSearchLocator.IndexOf(history, hit));
        Assert.Equal(-1, ConversationSearchLocator.IndexOf([new ChatMessage(ChatRole.User, "别的")], hit));
    }

    [Fact]
    public void Find_PrefersTheCardOfTheMatchedPart()
    {
        ChatMessage message = new(ChatRole.Assistant, [new TextReasoningContent("缓存"), new TextContent("结论")]);
        ThinkingItem thinking = new() { SourceMessage = message };
        TextConversationItem text = new(isUser: false) { SourceMessage = message };
        List<ConversationItemBase> items = [text, thinking];

        ConversationItemBase? found = ConversationSearchLocator.Find(items, [message],
            new SessionSearchHit(0, message, ESearchHitKind.Thinking, "缓存"));

        Assert.Same(thinking, found);
    }

    [Fact]
    public void Find_FallsBackToTheNearestEarlierCard()
    {
        // 工具结果并进了调用那张卡,结果消息自己没有卡
        ChatMessage call = new(ChatRole.Assistant, [new FunctionCallContent("c1", "Grep")]);
        ChatMessage result = new(ChatRole.Tool, [new FunctionResultContent("c1", "缓存")]);
        ToolCallItem card = new() { SourceMessage = call };

        ConversationItemBase? found = ConversationSearchLocator.Find([card], [call, result],
            new SessionSearchHit(1, result, ESearchHitKind.Tool, "缓存"));

        Assert.Same(card, found);
    }

    [Fact]
    public void Reveal_ExpandsTheThinkingCard()
    {
        ThinkingItem thinking = new();

        ConversationSearchLocator.Reveal(thinking, ESearchHitKind.Thinking);

        Assert.True(thinking.IsExpanded);
    }
}
