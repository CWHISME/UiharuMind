/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Execution.Tools;
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
    private static ChatMessage Report(string subSessionId, string? replyTo = null)
    {
        ChatMessage message = new(ChatRole.User, "结论")
        {
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                [ChatMessageAnnotations.SubAgentReport] = subSessionId,
            },
        };
        if (replyTo != null) message.AdditionalProperties[ChatMessageAnnotations.SubAgentReplyTo] = replyTo; //不给即老数据
        return message;
    }

    private static ChatMessage Plain(ChatRole role) => new(role, "普通消息");

    [Fact]
    public void NoPreviousReport_Appends()
    {
        (int index, bool replace) = SubAgentReportHandoff.ResolveSlot([Plain(ChatRole.User)], "sub1", "");

        Assert.Equal(-1, index);
        Assert.False(replace);
    }

    [Fact]
    public void PreviousReportStillAtTail_IsReplaced()
    {
        List<ChatMessage> history = [Plain(ChatRole.User), Report("sub1")];

        (int index, bool replace) = SubAgentReportHandoff.ResolveSlot(history, "sub1", "");

        Assert.Equal(1, index);
        Assert.True(replace); //模型还没读过它,留着只是垃圾
    }

    [Fact]
    public void PreviousReportAlreadyAnswered_IsKeptAndSuperseded()
    {
        //报告之后又跑过一轮,说明派活者已经据它回应过
        List<ChatMessage> history = [Report("sub1"), Plain(ChatRole.Assistant)];

        (int index, bool replace) = SubAgentReportHandoff.ResolveSlot(history, "sub1", "");

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

        (int first, bool replaceFirst) = SubAgentReportHandoff.ResolveSlot(history, "sub1", "");
        (int second, bool replaceSecond) = SubAgentReportHandoff.ResolveSlot(history, "sub2", "");

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
        Assert.Contains("\n\n可以提交\n\n", text);
        Assert.EndsWith($"`{SubAgentTool.MessageToolName}`，to 填 [sub-session: sub1]；另开一位的话，对方什么都不知道。）", text); //读完要决定找谁的地方告诉它怎么接着谈
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
        Assert.Contains("改口了\n\n（接着谈同类主题", text); //附言在正文之后,不夹在信头与正文之间
        Assert.EndsWith("\n（你还在等 2 位的回信。）", text);
    }

    /// <summary>被打断是事实，不是命令：有内容就附上说到一半的，没有就明说没回</summary>
    [Fact]
    public void ReplyText_StatesTheInterruption()
    {
        string partial = SubAgentReportHandoff.BuildText("审查员", "sub1", "查一下", "查到一半", false,
            "在应用退出时被中止，没有跑完", 0);
        string empty = SubAgentReportHandoff.BuildText("审查员", "sub1", "查一下", "", false, "没写完回复就停下了", 0);

        Assert.Contains("对方在应用退出时被中止，没有跑完，以下是对方停下前说到的：", partial);
        Assert.Contains("查到一半\n\n", partial);
        Assert.Contains("对方没写完回复就停下了，没回任何内容。", empty);
    }

    /// <summary>插进醒着的派活者那一轮时：他醒着说明上一封（若有）已读过，有就写成更正</summary>
    [Fact]
    public void Compose_WhileParentAwake_MarksACorrectionOnlyIfAnEarlierReplyExists()
    {
        ChatSession parent = new() { IsTransient = true };
        ChatSession sub = new() { IsTransient = true, SubAgentRole = "审查员" };
        sub.History.Add(new ChatMessage(ChatRole.User, "看一下这个改动"));

        string first = SubAgentReportHandoff.Compose(parent, sub, null, "可以", 0)!.Text;
        parent.History.Add(Report(sub.SessionId));
        string second = SubAgentReportHandoff.Compose(parent, sub, null, "改口了", 0)!.Text;

        Assert.DoesNotContain("更正上一封", first);
        Assert.Contains("（更正上一封）", second);
        Assert.Null(SubAgentReportHandoff.Compose(parent, sub, null, "", 0)); //没话可插
    }

    /// <summary>
    /// 续聊问了新问题，第二封是新回答不是更正。实测踩过：只看「这人回过没有」，
    /// 续聊的回信一律被标成「（更正上一封）」
    /// </summary>
    [Fact]
    public void ReplyToANewMessage_IsNotACorrection()
    {
        ChatSession parent = new() { IsTransient = true };
        ChatSession sub = new() { IsTransient = true, SubAgentRole = "助手" };
        DateTimeOffset asked = DateTimeOffset.Now;
        sub.History.Add(new ChatMessage(ChatRole.User, "这是什么项目") { CreatedAt = asked });
        parent.History.Add(SubAgentReportHandoff.Compose(parent, sub, null, "一个桌面应用", 0)!);
        parent.History.Add(Plain(ChatRole.Assistant));

        string sameRequest = SubAgentReportHandoff.Compose(parent, sub, null, "改口：是个 CLI", 0)!.Text;
        sub.History.Add(new ChatMessage(ChatRole.User, "提到了哪些本地模型功能") { CreatedAt = asked.AddMinutes(1) });
        string newRequest = SubAgentReportHandoff.Compose(parent, sub, null, "llama.cpp 跑 GGUF", 0)!.Text;

        Assert.Contains("（更正上一封）", sameRequest);
        Assert.DoesNotContain("更正上一封", newRequest);
    }

    /// <summary>续聊那句带着给对方看的发信人前缀，引回给派活者时不该露出来</summary>
    [Fact]
    public void ReplyQuote_DropsTheSenderPrefix()
    {
        ChatSession parent = new() { IsTransient = true };
        ChatSession sub = new() { IsTransient = true, SubAgentRole = "助手" };
        sub.History.Add(new ChatMessage(ChatRole.User, SubAgentTool.ParentInterjectionPrefix + "接着说说本地模型"));

        string text = SubAgentReportHandoff.Compose(parent, sub, null, "llama.cpp", 0)!.Text;

        Assert.Contains("回你之前发的「接着说说本地模型」", text);
    }

    /// <summary>跑着时补的那句并进了同一封回信：信头引的仍是开启这一轮的那句</summary>
    [Fact]
    public void ReplyQuote_SkipsMidWorkAdditions()
    {
        ChatSession parent = new() { IsTransient = true };
        ChatSession sub = new() { IsTransient = true, SubAgentRole = "通读者" };
        sub.History.Add(new ChatMessage(ChatRole.User, "通读全部 ADR"));
        ChatMessage addition = new(ChatRole.User, SubAgentTool.ParentInterjectionPrefix + "顺便标出被推翻的");
        ChatMessageAnnotations.MarkParentInterjection(addition);
        sub.History.Add(addition);

        string text = SubAgentReportHandoff.Compose(parent, sub, null, "63 篇", 0)!.Text;

        Assert.Contains("回你之前发的「通读全部 ADR」", text);
    }

    /// <summary>新问题的回信也不得原地替换上一封——哪怕上一封还停在末尾</summary>
    [Fact]
    public void ReplyToANewMessage_DoesNotReplaceThePreviousOne()
    {
        List<ChatMessage> history = [Plain(ChatRole.User), Report("sub1", replyTo: "100")];

        Assert.Equal((-1, false), SubAgentReportHandoff.ResolveSlot(history, "sub1", "200"));
        Assert.Equal((1, true), SubAgentReportHandoff.ResolveSlot(history, "sub1", "100"));
    }
}
