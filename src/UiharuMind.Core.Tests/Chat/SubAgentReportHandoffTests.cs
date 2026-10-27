/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Core;
using UiharuMind.Core.AI.Execution.Tools;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Models;

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
    /// 回信写成对方发来的（ADR 0062）：「来自」起头，说清是谁、回的是你发的哪一句；
    /// 续聊方式与上下文占用合成一行附言坠在正文之后，不再出现「委派」「报告」这些调用的说法
    /// </summary>
    [Fact]
    public void ReplyText_SaysWhoSentItAndWhatItAnswers()
    {
        string text = SubAgentReportHandoff.BuildText("审查员", "sub1", "黑猫，提交前给你看账：", "可以提交",
            supersedes: false, interruption: null, othersPending: 0);

        Assert.StartsWith("来自 审查员 [sub-session: sub1]，回你之前发的「黑猫，提交前给你看账：」：\n\n可以提交", text);
        // 附言是系统话，用【】与正文分开；续聊方式在里面
        Assert.Contains($"【想接着聊：`{SubAgentTool.MessageToolName}` → [sub-session: sub1]】", text);
        // 不写「当前已进入空闲」这类状态断言：被用户终止时它与正文的「被终止」直接打架
        Assert.DoesNotContain("当前已进入空闲", text);
        Assert.EndsWith("】", text);
        Assert.DoesNotContain("委派", text);
        Assert.DoesNotContain("报告", text);
        Assert.DoesNotContain("还在等", text);
    }

    /// <summary>匿名、没给身份的：用「一位子代理」起头，不拿任务开头冒充名字</summary>
    [Fact]
    public void ReplyText_WithoutAName_SaysASubAgentSentIt()
    {
        string text = SubAgentReportHandoff.BuildText("", "sub1", "查一下", "好了", false, null, 0);

        Assert.StartsWith("来自 一位子代理 [sub-session: sub1]，回你之前发的「查一下」：", text);
    }

    /// <summary>更正标在信头末尾，等人数坠在附言之后</summary>
    [Fact]
    public void ReplyText_StatesCorrectionAndWhoElseIsPending()
    {
        string text = SubAgentReportHandoff.BuildText("审查员", "sub1", "查一下", "改口了", supersedes: true,
            interruption: null, othersPending: 2);

        Assert.Contains("【更正上一封】", text);
        Assert.Contains("改口了\n\n【", text); //附言在正文之后,不夹在信头与正文之间
        Assert.EndsWith("\n【另外还有 2 位没回信】", text);
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

    /// <summary>
    /// 上下文占用挂在附言里（`｜ 8k/1M · 模型名`），不再单起一行身份行：
    /// 主代理读完信要决定接着追问还是换人，这位还剩多少是硬判据
    /// </summary>
    [Fact]
    public void ReplyText_CarriesTheUsageInTheTail()
    {
        string text = SubAgentReportHandoff.BuildText("审查员", "sub1", "查一下", "可以提交",
            supersedes: false, interruption: null, othersPending: 0, usage: "8k/1M · qwen3");

        Assert.Contains("想接着聊：", text);
        Assert.Contains("｜ 8k/1M · qwen3】", text);
    }

    /// <summary>没有占用就不写那一段，也不留一个空的「｜」</summary>
    [Fact]
    public void ReplyText_WithoutUsage_LeavesNoEmptySeparator()
    {
        string text = SubAgentReportHandoff.BuildText("审查员", "sub1", "查一下", "可以提交", false, null, 0);

        Assert.DoesNotContain("｜", text);
        Assert.DoesNotContain("上下文", text);
    }

    /// <summary>Compose 这条真路径也把占用算进附言（不是只有直接调 BuildText 才带）</summary>
    [Fact]
    public void Reply_CarriesTheSubAgentContextUsage()
    {
        ChatSession parent = new() { IsTransient = true };
        ChatSession sub = new() { IsTransient = true, SubAgentRole = "审查员", LastInputTokens = 12_000 };
        sub.ChatModelRunningData = new ModelRunningData(new GGufModelInfo { ModelName = "qwen3" });
        sub.History.Add(new ChatMessage(ChatRole.User, "看一下这个改动"));

        string text = SubAgentReportHandoff.Compose(parent, sub, null, "可以", 0)!.Text;

        Assert.Contains("12k", text);
        Assert.Contains("qwen3", text);
    }

    /// <summary>从没跑过的子代理没有占用可报：附言里不留空分隔符</summary>
    [Fact]
    public void Reply_WithoutUsage_OmitsTheContextSegment()
    {
        ChatSession parent = new() { IsTransient = true };
        ChatSession sub = new() { IsTransient = true, SubAgentRole = "审查员" };
        sub.History.Add(new ChatMessage(ChatRole.User, "看一下这个改动"));

        string text = SubAgentReportHandoff.Compose(parent, sub, null, "可以", 0)!.Text;

        Assert.DoesNotContain("｜", text);
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
        Assert.Contains("【更正上一封】", second);
        Assert.Null(SubAgentReportHandoff.Compose(parent, sub, null, "", 0)); //没话可插
    }

    /// <summary>
    /// 续聊问了新问题，第二封是新回答不是更正。实测踩过：只看「这人回过没有」，
    /// 续聊的回信一律被标成「【更正上一封】」
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

        Assert.Contains("【更正上一封】", sameRequest);
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

    //================= 署名：谁 + 在做什么 =================

    /// <summary>
    /// 同一张角色卡可以被点名成多个子智能体（还可能和用户配的默认卡撞上），
    /// 署名只给名字就分不清是哪一位、在办哪件事——所以有名字就一定带主题
    /// </summary>
    [Fact]
    public void SenderOf_NamedAgent_CarriesTheTopic()
    {
        ChatSession session = new() { IsTransient = true, SubAgentName = "阿洛娜", SubAgentRole = "查日志" };

        Assert.Equal("阿洛娜（查日志）", SubAgentReportHandoff.SenderOf(session));
    }

    /// <summary>主代理没给主题：退到会话标题，不能只剩一个名字</summary>
    [Fact]
    public void SenderOf_WithoutTopic_FallsBackToTheTitle()
    {
        ChatSession session = new() { IsTransient = true, SubAgentName = "阿洛娜", Title = "构建失败排查" };

        Assert.Equal("阿洛娜（构建失败排查）", SubAgentReportHandoff.SenderOf(session));
    }

    /// <summary>兜底标题也要截短：一整句任务书不该把署名撑得比正文还长</summary>
    [Fact]
    public void SenderOf_LongTitle_IsClipped()
    {
        ChatSession session = new()
        {
            IsTransient = true,
            SubAgentName = "阿洛娜",
            Title = "看一下构建失败的原因并修掉它，顺便把日志也贴出来",
        };

        Assert.Equal("阿洛娜（看一下构建失败的原因并修掉它，顺便把日志…）", SubAgentReportHandoff.SenderOf(session));
    }

    /// <summary>匿名但配了默认角色卡：角色名当名字，同样带主题</summary>
    [Fact]
    public void SenderOf_DefaultCard_NamesTheCharacterAndKeepsTheTopic()
    {
        ChatSession session = new()
        {
            IsTransient = true,
            CharacterId = "AronaAgent",
            SubAgentRole = "子代理功能测试",
        };

        Assert.Equal("阿洛娜（子代理功能测试）", SubAgentReportHandoff.SenderOf(session));
    }

    /// <summary>匿名且没配默认卡：内置基座卡与 None 哨兵的名字都不算人设，只剩主题</summary>
    [Fact]
    public void SenderOf_AnonymousWithoutDefaultCard_UsesOnlyTheTopic()
    {
        ChatSession session = new() { IsTransient = true, SubAgentRole = "通读者" };

        Assert.Equal("通读者", SubAgentReportHandoff.SenderOf(session));
    }

    /// <summary>点名、没给主题、也没有标题：只剩名字，不硬编一个「（主题）」上去</summary>
    [Fact]
    public void SenderOf_WithoutAnyTopic_KeepsJustTheName()
    {
        ChatSession session = new() { IsTransient = true, SubAgentName = "阿洛娜" };

        Assert.Equal("阿洛娜", SubAgentReportHandoff.SenderOf(session));
    }
}
