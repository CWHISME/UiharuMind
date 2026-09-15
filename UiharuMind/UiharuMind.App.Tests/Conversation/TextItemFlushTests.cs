/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using UiharuMind.Features.Conversation.Items;

namespace UiharuMind.App.Tests.Conversation;

/// <summary>
/// 文本条目的收尾冲刷：<b>只对流式条目成立</b>。
///
/// 旁白与子会话的后续报告是直接赋 <c>Message</c> 造出来的，一个字都没进过流式缓冲。
/// 把它们也冲一遍等于拿空缓冲覆盖正文——实机症状是「交回主代理」第二次点下去，
/// 主会话里那条报告当场变成空白气泡（后续报告原地替换走的正是冲刷那条路）。
/// </summary>
public class TextItemFlushTests
{
    [Fact]
    public void Flush_KeepsTextThatWasAssignedDirectly()
    {
        TextConversationItem item = new(isUser: false, isNarration: true) { Message = "子代理的结论" };

        item.Flush();

        Assert.Equal("子代理的结论", item.Message);
    }

    [Fact]
    public void Flush_PublishesTheStreamedTail()
    {
        //流式条目的正文只有缓冲这一个来源,收尾冲刷照常把最后一段补上
        TextConversationItem item = new(isUser: false);
        item.Append("前半");
        item.Append("后半");

        item.Flush();

        Assert.Equal("前半后半", item.Message);
    }
}
