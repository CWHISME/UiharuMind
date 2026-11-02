/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace UiharuMind.Core.Tests.Chat;

/// <summary>
/// 分叉/复制会话走 <see cref="SessionManager.DeepCopy"/>：会话头与历史分别往返一次 JSON。
/// 历史换成 <see cref="SessionHistory"/> 之后这条路要单独守——它是唯一一处
/// 「拿历史的快照去序列化」的地方，类型对不上会当场抛，而分叉是低频操作，
/// 不测就得等用户点「从这里分出去」才发现。
/// </summary>
public class SessionDeepCopyTests
{
    [Fact]
    public void DeepCopy_CopiesHistoryIntoAnIndependentList()
    {
        ChatSession source = new() { SessionId = "copy-source", Title = "源会话" };
        source.History.Add(new ChatMessage(ChatRole.User, "第一句"));
        source.History.Add(new ChatMessage(ChatRole.Assistant, "第二句"));

        ChatSession copy = SessionManager.Instance.DeepCopy(source);

        Assert.Equal(2, copy.History.Count);
        Assert.Equal("第一句", copy.History[0].Text);
        Assert.Equal("第二句", copy.History[1].Text);
        // 副本必须是新实例：共享的话，在副本里删一条会把源会话的历史也改掉
        Assert.NotSame(source.History[0], copy.History[0]);

        copy.History.Add(new ChatMessage(ChatRole.User, "副本里多说一句"));
        Assert.Equal(2, source.History.Count);
    }
}
