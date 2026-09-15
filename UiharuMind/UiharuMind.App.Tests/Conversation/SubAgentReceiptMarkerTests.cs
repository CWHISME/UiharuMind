/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Execution.Tools;
using UiharuMind.Features.Conversation.Items;

namespace UiharuMind.App.Tests.Conversation;

/// <summary>
/// 委派回执与工具卡之间的契约：结果文本末尾那行 <c>[sub-session: …]</c>。
///
/// 这是<b>跨模块的格式约定</b>，两边都没有编译期联系——Core 那边写、界面这边用正则认。
/// 委派改成后台执行时回执换过一次措辞，就是在这里断掉的：格式一变，回放历史时卡片
/// 认不出这是一次委派，「查看过程」入口整个消失，而任何测试都没红。
/// </summary>
public class SubAgentReceiptMarkerTests
{
    private static ChatSession SubSession(string id) =>
        new() { SessionId = id, ParentSessionId = "parent" };

    [Fact]
    public void Receipt_CarriesAParseableSubSessionMarker()
    {
        ChatSession session = SubSession("abc123def456");

        //派出即返回,跑什么不重要——这里要的只是那句回执
        string receipt = BackgroundSubAgentDispatcher.Dispatch(session, _ => Task.FromResult(string.Empty));

        Assert.Equal("abc123def456", ToolCallItem.ParseSubSessionId(receipt));
    }

    [Fact]
    public void Receipt_SaysThereIsNoResultYet()
    {
        //模型最容易犯的错是把「已派出」读成「已完成」,这句话是唯一挡在那儿的东西
        ChatSession session = SubSession("abc123");

        string receipt = BackgroundSubAgentDispatcher.Dispatch(session, _ => Task.FromResult(string.Empty));

        Assert.Contains("NO RESULT YET", receipt);
    }
}
