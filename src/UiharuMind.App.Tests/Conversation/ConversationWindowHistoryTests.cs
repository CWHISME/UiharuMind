using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Features.Conversation;

namespace UiharuMind.App.Tests.Conversation;

/// <summary>
/// 会话流这一眼该读哪份历史：执行者给了就用它，给不出（未挂接、绑定间隙、装配迁移失败）
/// 回落会话本体，两处都拿不到才交空表给开窗那边守。规则在这里钉死，是因为它错一次的表现
/// 是窗口与内容错位——「已到会话开头」挂在半途，滚到顶再也续不出来
/// </summary>
public class ConversationWindowHistoryTests
{
    private static ChatSession SessionWith(string text)
    {
        ChatSession session = new() { SessionId = "s", IsTransient = true };
        session.History.Add(new ChatMessage(ChatRole.User, text));
        return session;
    }

    /// <summary>执行者读得到就用它（本体只作回落，不抢）</summary>
    [Fact]
    public void RunnerHistory_WinsWhenItHasAnything()
    {
        ChatMessage[] fromRunner = [new(ChatRole.User, "执行者")];

        Assert.Same(fromRunner, ConversationWindowHistory.Choose(fromRunner, SessionWith("本体")));
    }

    /// <summary>执行者给空表或压根没有：回落会话本体</summary>
    [Fact]
    public void FallsBackToTheSession_WhenTheRunnerHasNothing()
    {
        ChatSession session = SessionWith("本体");

        Assert.Same(session.History[0], ConversationWindowHistory.Choose([], session)[0]);
        Assert.Same(session.History[0], ConversationWindowHistory.Choose(null, session)[0]);
    }

    /// <summary>两处都没有：空表（交给开窗那边守，别让它去动窗口）</summary>
    [Fact]
    public void HandsNothing_WhenNeitherHasAnything()
    {
        Assert.Empty(ConversationWindowHistory.Choose(null, null));
        Assert.Empty(ConversationWindowHistory.Choose([], null));
    }
}
