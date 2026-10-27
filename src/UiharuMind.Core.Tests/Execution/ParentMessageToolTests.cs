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
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Execution.Delivery;
using UiharuMind.Core.AI.Execution.Tools;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// 子代理给主代理发消息的那把 SendMessage（<see cref="ParentMessageTool"/>）：
/// 信的形状、信在派活者历史里的身份。
///
/// 与 <see cref="SubAgentReportHandoff"/> 的区别要钉住：中途来信<b>不是交回</b>——
/// 不占报告的槽位（<see cref="SubAgentReportHandoff.ResolveSlot"/> 认不出它就不会原地替换），
/// 渲染成旁白卡片而不是用户气泡。
/// </summary>
public class ParentMessageToolTests
{
    [Fact]
    public void MessageText_SaysWhoSentIt_AndHowToReply()
    {
        string text = ParentMessageTool.BuildText("小A", "abcdef1234567890", "可以改这个文件吗？");

        Assert.Contains("来自 小A[abcdef12]，工作途中的消息：", text);
        Assert.Contains("可以改这个文件吗？", text);
        // 末尾教派活者怎么回：用他自己那把 SendMessage，to 填这个短号
        Assert.Contains("`SendMessage`", text);
        Assert.Contains("[sub-session: abcdef12]", text);
    }

    /// <summary>匿名的子代理没有名字：用「一位子代理」起头，派活者照旧认得是它</summary>
    [Fact]
    public void MessageText_WithoutAName_SaysASubAgentSentIt()
    {
        string text = ParentMessageTool.BuildText(string.Empty, "abcdef1234567890", "hello");

        Assert.Contains("来自一位子代理[abcdef12]", text);
        Assert.DoesNotContain("小A", text);
    }

    /// <summary>插进派活者那一轮与落进历史共用同一种身份：user 消息、带 SubAgentMessage 标记、不是报告</summary>
    [Fact]
    public void Compose_BuildsAUserMessage_MarkedAsSubAgentMessage()
    {
        ParentMessageTool.Letter letter = new("parent", "abcdef1234567890", "body");
        ChatMessage message = letter.Compose(new ChatSession { IsTransient = true })!;

        Assert.Equal(ChatRole.User, message.Role);
        Assert.Equal("body", message.Text);
        Assert.True(ChatMessageAnnotations.IsSubAgentMessage(message));
        Assert.Equal("abcdef1234567890", ChatMessageAnnotations.ReadSubAgentMessageSession(message));
        Assert.False(ChatMessageAnnotations.IsSubAgentReport(message));
    }

    /// <summary>写是纯追加：同一子代理的上一封中途来信还在末尾也不替换——它不是一次性的结论</summary>
    [Fact]
    public void Write_Appends_EvenWhenAnEarlierMessageFromSameChildIsAtTheTail()
    {
        ChatSession parent = new() { IsTransient = true };
        parent.History.Add(new ChatMessage(ChatRole.User, "早前的一封")
        {
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                [ChatMessageAnnotations.SubAgentMessage] = "child-1",
            },
        });

        ParentMessageTool.Letter letter = new("parent", "child-1", "新的一封");
        ELetterWrite outcome = letter.Write(parent);

        Assert.Equal(ELetterWrite.Written, outcome);
        Assert.Equal(2, parent.History.Count);
        Assert.True(ChatMessageAnnotations.IsSubAgentMessage(parent.History[1]));
    }

    /// <summary>中途来信不占报告的槽位：ResolveSlot 认不出它，回信才不会原地替换掉一封提问</summary>
    [Fact]
    public void SubAgentMessage_DoesNotOccupyTheReportSlot()
    {
        List<ChatMessage> history =
        [
            new(ChatRole.User, "途中来信")
            {
                AdditionalProperties = new AdditionalPropertiesDictionary
                {
                    [ChatMessageAnnotations.SubAgentMessage] = "sub1",
                },
            },
        ];

        (int index, bool replace) = SubAgentReportHandoff.ResolveSlot(history, "sub1", "request-key");

        Assert.Equal(-1, index);
        Assert.False(replace);
    }

    /// <summary>来源标记不落 Attribution 那条过滤：它是派活者历史的一部分，要落盘、要供给模型</summary>
    [Fact]
    public void SubAgentMessage_IsOwnedByUs()
    {
        ChatMessage message = new(ChatRole.User, "来自子代理的话");
        ChatMessageAnnotations.MarkSubAgentMessage(message, "child-1");

        Assert.True(SessionChatHistoryProvider.IsOwnedByUs(message));
        Assert.True(ChatMessageAnnotations.IsSubAgentMessage(message));
        Assert.False(ChatMessageAnnotations.IsSubAgentMessage(new ChatMessage(ChatRole.User, "hello")));
    }
}
