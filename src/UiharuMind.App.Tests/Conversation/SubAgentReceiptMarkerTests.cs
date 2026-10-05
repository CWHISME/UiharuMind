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
///
/// 回执里给模型的是真实 ID 的前 8 位短号（<see cref="SubSessionIdAlias"/>），
/// 回放反查回真实 ID 才能开窗。每个用例用互不相同的 ID：Dispatch 的 SaveMeta 会把它
/// 登记进全局 SessionManager，测试并行跑时靠独立前缀隔离，finally 里清掉。
/// </summary>
public class SubAgentReceiptMarkerTests
{
    private static ChatSession SubSession(string id) =>
        new() { SessionId = id, ParentSessionId = "parent" };

    [Fact]
    public void Receipt_CarriesAParseableSubSessionMarker()
    {
        ChatSession session = SubSession("aaaa1111bbbb2222");
        try
        {
            //派出即返回,跑什么不重要——这里要的只是那句回执
            string receipt = BackgroundSubAgentDispatcher.Dispatch(session, _ => Task.FromResult(string.Empty));

            //回执里给模型的是真实 ID 前 8 位短号,回放反查回真实 ID 才能开窗
            Assert.Equal(session.SessionId, ToolCallItem.ParseSubSessionId(receipt));
        }
        finally
        {
            SessionManager.Instance.Delete(session.SessionId);
        }
    }

    /// <summary>
    /// 回退告知必须<b>排在标记行之前</b>。
    ///
    /// 这条看着像排版，其实是上面那条契约的另一半：<c>ParseSubSessionId</c> 认的是末行，
    /// 告知句要是缀在标记之后，「查看过程」入口会连同这次委派一起消失——
    /// 而这个缺陷只在「模型名写错了」那条支路上才触发，实机上极难复现。
    /// </summary>
    [Fact]
    public void Receipt_PutsTheFallbackNoticeBeforeTheMarker()
    {
        ChatSession session = SubSession("bbbb2222cccc3333");
        try
        {
            string receipt = BackgroundSubAgentDispatcher.Dispatch(session, _ => Task.FromResult(string.Empty),
                "Note: there is no model named 'gpt-4o', so this run uses the default model instead.");

            Assert.Contains("no model named 'gpt-4o'", receipt);
            Assert.Equal(session.SessionId, ToolCallItem.ParseSubSessionId(receipt)); //标记仍认得出
            Assert.True(receipt.IndexOf("no model named", StringComparison.Ordinal) <
                        receipt.IndexOf("[sub-session:", StringComparison.Ordinal),
                "回退告知跑到了标记行之后");
        }
        finally
        {
            SessionManager.Instance.Delete(session.SessionId);
        }
    }

    /// <summary>没发生回退就一个字都不加：回执是每次委派都付的钱</summary>
    [Fact]
    public void Receipt_SaysNothingExtra_WhenTheModelResolved()
    {
        ChatSession session = SubSession("cccc3333dddd4444");
        try
        {
            string receipt = BackgroundSubAgentDispatcher.Dispatch(session, _ => Task.FromResult(string.Empty));

            Assert.DoesNotContain("Note:", receipt);
        }
        finally
        {
            SessionManager.Instance.Delete(session.SessionId);
        }
    }

    /// <summary>回执写成「发给了某个人」：带名字，并说明中途可以再发消息补充</summary>
    [Fact]
    public void Receipt_NamesTheRecipientAndAllowsFollowUps()
    {
        ChatSession session = SubSession("ffff6666aaaa7777");
        session.SubAgentRole = "审查员";
        try
        {
            string receipt = BackgroundSubAgentDispatcher.Dispatch(session, _ => Task.FromResult(string.Empty));

            Assert.StartsWith("Sent to \"审查员\".", receipt); //名字加引号:角色过长被截成「…」时不会再接一个句号
            Assert.Contains($"`{SubAgentTool.MessageToolName}` them meanwhile", receipt); //指名续聊工具：中途补充走它
        }
        finally
        {
            SessionManager.Instance.Delete(session.SessionId);
        }
    }

    [Fact]
    public void Receipt_SaysThereIsNoResultYet()
    {
        //模型最容易犯的错是把「已派出」读成「已完成」,这句话是唯一挡在那儿的东西
        ChatSession session = SubSession("dddd4444eeee5555");
        try
        {
            string receipt = BackgroundSubAgentDispatcher.Dispatch(session, _ => Task.FromResult(string.Empty));

            Assert.Contains("will write back once, when done", receipt);
        }
        finally
        {
            SessionManager.Instance.Delete(session.SessionId);
        }
    }

    /// <summary>
    /// 实时插话的回执与「已派出」是两种语义：插话不另起一轮、没有独立报告，
    /// 效果并入当前轮——不写清的话主代理会空等第二份报告。但标记行仍是最后一行，
    /// 卡片的「查看过程」入口靠它。
    /// </summary>
    [Fact]
    public void InjectedReceipt_CarriesAParseableSubSessionMarker()
    {
        //插话的对象本就是登记过的子会话,回放据短号反查回它
        ChatSession session = SubSession("eeee5555ffff6666");
        SessionManager.Instance.Add(session);
        try
        {
            string receipt = SubAgentTool.BuildInjectedReceipt(session.SessionId);

            Assert.Equal(session.SessionId, ToolCallItem.ParseSubSessionId(receipt));
        }
        finally
        {
            SessionManager.Instance.Delete(session.SessionId);
        }
    }

    /// <summary>
    /// 普通工具读到的正文恰好以这行收尾、短号又查不到：点了也打不开，不挂入口
    /// </summary>
    [Fact]
    public void UnknownShortId_GetsNoEntry()
    {
        Assert.Empty(ToolCallItem.ParseSubSessionId("报告样例：\n[sub-session: zzzz9999]"));
    }

    /// <summary>
    /// 报错卡（撞号列表）不得挂「查看过程」入口——反过来的半边契约：非回执正文不得以
    /// [sub-session: …] 收尾（与 <see cref="Receipt_PutsTheFallbackNoticeBeforeTheMarker"/>
    /// 的「标记必须在末行」合起来才是完整契约）。
    /// AmbiguousRun 的候选行套括号后，若匿名行在末尾，正文会以标记收尾、正则命中且完整 ID
    /// 精确命中，误挂入口；兜法是引导语在列表之后、正文以「as `to`.」收尾。
    /// 用纯字母数字 ID：横杠（如 sendshort-0001）会被 [A-Za-z0-9]+ 拒之门外，测出来是假绿。
    /// </summary>
    [Fact]
    public void AmbiguousRun_ErrorText_GetsNoEntry()
    {
        string idA = "aaaa1111bbbb2222";
        string idB = "aaaa1111cccc3333";
        SessionManager.Instance.Add(new ChatSession { SessionId = idA, ParentSessionId = "parent" });
        SessionManager.Instance.Add(new ChatSession { SessionId = idB, ParentSessionId = "parent" });
        try
        {
            // AmbiguousRun 的新格式正文：候选行套括号、引导语在列表之后（不以标记收尾）。
            // 旧格式（引导语在前、匿名行收尾）下末行会命中正则且精确命中 idB，返回非空——这条断言会红
            string errorText = $"Error: 'aaaa1111' matches more than one earlier conversation:\n"
                               + $"- [sub-session: {idA}]\n"
                               + $"- [sub-session: {idB}]\n"
                               + "Pass the full id of the one you mean as `to`.";

            Assert.Empty(ToolCallItem.ParseSubSessionId(errorText));
        }
        finally
        {
            SessionManager.Instance.Delete(idA);
            SessionManager.Instance.Delete(idB);
        }
    }

    [Fact]
    public void InjectedReceipt_SaysNoSeparateReport()
    {
        string receipt = SubAgentTool.BuildInjectedReceipt("abc123");

        Assert.Contains("no separate reply", receipt);
        Assert.DoesNotContain("Dispatched", receipt);
    }
}
