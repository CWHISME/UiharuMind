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

namespace UiharuMind.Core.Tests.Chat;

/// <summary>
/// 后续报告的<b>槽位判据</b>：一个子会话在派活者那里不是想留几条留几条。
///
/// 无脑追加会让历史里躺着两条互相矛盾的"同一次委派的结论"（模型会当成两项并列发现）；
/// 无脑替换会抹掉已被消费的那一份，让派活者据它写的那段回应变得没有来由。
/// 判据是位置："还停在末尾"等于"模型还没读过"。
/// </summary>
public class SubAgentReportHandoffTests
{
    private static ChatMessage Report(string subSessionId) =>
        new(ChatRole.User, "结论")
        {
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                [ChatMessageAnnotations.SubAgentReport] = subSessionId,
            },
        };

    private static ChatMessage Plain(ChatRole role) => new(role, "普通消息");

    [Fact]
    public void NoPreviousReport_Appends()
    {
        (int index, bool replace) = SubAgentReportHandoff.ResolveSlot([Plain(ChatRole.User)], "sub1");

        Assert.Equal(-1, index);
        Assert.False(replace);
    }

    [Fact]
    public void PreviousReportStillAtTail_IsReplaced()
    {
        List<ChatMessage> history = [Plain(ChatRole.User), Report("sub1")];

        (int index, bool replace) = SubAgentReportHandoff.ResolveSlot(history, "sub1");

        Assert.Equal(1, index);
        Assert.True(replace); //模型还没读过它,留着只是垃圾
    }

    [Fact]
    public void PreviousReportAlreadyAnswered_IsKeptAndSuperseded()
    {
        //报告之后又跑过一轮,说明派活者已经据它回应过
        List<ChatMessage> history = [Report("sub1"), Plain(ChatRole.Assistant)];

        (int index, bool replace) = SubAgentReportHandoff.ResolveSlot(history, "sub1");

        Assert.Equal(0, index);
        Assert.False(replace); //抹掉它会让那段回应没有来由
    }

    /// <summary>
    /// 槽位是<b>按子会话</b>分的：两次不同的委派各留各的，互不覆盖。
    /// </summary>
    [Fact]
    public void SlotsAreKeyedBySubSession()
    {
        List<ChatMessage> history = [Report("sub1"), Report("sub2")];

        (int first, bool replaceFirst) = SubAgentReportHandoff.ResolveSlot(history, "sub1");
        (int second, bool replaceSecond) = SubAgentReportHandoff.ResolveSlot(history, "sub2");

        Assert.Equal(0, first);
        Assert.False(replaceFirst); //sub1 的那份后面还有别的,不是末尾
        Assert.Equal(1, second);
        Assert.True(replaceSecond);
    }

    /// <summary>
    /// 回信写成对方发来的（ADR 0062）：「来自」起头带上回执里那种标识，模型照抄就能接着回；
    /// 引一句它回的是哪件事；不再出现「委派」「报告」这些调用的说法
    /// </summary>
    [Fact]
    public void ReplyText_SaysWhoSentItAndWhatItAnswers()
    {
        string text = SubAgentReportHandoff.BuildText("审查员", "sub1", "黑猫，提交前给你看账：", "可以提交",
            supersedes: false, interruption: null, othersPending: 0);

        Assert.StartsWith("来自 审查员 [sub-session: sub1]，回你之前发的「黑猫，提交前给你看账：」：", text);
        Assert.EndsWith("\n\n可以提交", text);
        Assert.DoesNotContain("委派", text);
        Assert.DoesNotContain("报告", text);
        Assert.DoesNotContain("还在等", text);
    }

    /// <summary>匿名、没给身份的：只留标识，不拿任务开头冒充名字</summary>
    [Fact]
    public void ReplyText_WithoutAName_KeepsOnlyTheId()
    {
        string text = SubAgentReportHandoff.BuildText("", "sub1", "查一下", "好了", false, null, 0);

        Assert.StartsWith("来自 [sub-session: sub1]，回你之前发的「查一下」：", text);
    }

    [Fact]
    public void ReplyText_StatesCorrectionAndWhoElseIsPending()
    {
        string text = SubAgentReportHandoff.BuildText("审查员", "sub1", "查一下", "改口了", supersedes: true,
            interruption: null, othersPending: 2);

        Assert.Contains("（更正上一封）：", text);
        Assert.Contains("你还在等 2 位的回信。", text);
    }

    /// <summary>被打断是事实，不是命令：有内容就附上说到一半的，没有就明说没回</summary>
    [Fact]
    public void ReplyText_StatesTheInterruption()
    {
        string partial = SubAgentReportHandoff.BuildText("审查员", "sub1", "查一下", "查到一半", false,
            "在应用退出时被中止，没有跑完", 0);
        string empty = SubAgentReportHandoff.BuildText("审查员", "sub1", "查一下", "", false, "没写完回复就停下了", 0);

        Assert.Contains("对方在应用退出时被中止，没有跑完，以下是它停下前说到的：", partial);
        Assert.EndsWith("查到一半", partial);
        Assert.EndsWith("对方没写完回复就停下了，没回任何内容。", empty);
    }
}
