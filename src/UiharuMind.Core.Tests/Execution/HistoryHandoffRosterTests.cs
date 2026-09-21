/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Execution.History;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// 交接文档里的委派清单。
///
/// 钉住的是一条很容易被「顺手简化」掉的取舍：这一段<b>不由模型写</b>，而是从会话索引现算。
/// 编号只出现在回执与报告里，压缩恰恰要吃掉它们——而一长串十六进制正是摘要最先丢的东西。
/// 少了这一段，压缩之后 <c>ContinueSubAgent</c> 就够不着了。
/// </summary>
public class HistoryHandoffRosterTests
{
    private static ChatSessionMeta Sub(string id, string title, string? description = null) =>
        new()
        {
            SessionId = id,
            Title = title,
            Description = description ?? title,
            ParentSessionId = "parent",
        };

    [Fact]
    public void NoDelegations_ProducesNothing()
    {
        //没派过活时整段不出现,不占交接文档的篇幅
        Assert.Equal(string.Empty, HistoryHandoff.BuildSubSessionRoster([]));
    }

    [Fact]
    public void Roster_CarriesIdAndTitle()
    {
        string roster = HistoryHandoff.BuildSubSessionRoster([Sub("abc123", "查配置加载")]);

        Assert.Contains("abc123", roster);
        Assert.Contains("查配置加载", roster);
    }

    /// <summary>
    /// 摘要取任务原文而不是标题：标题是界面用的、首行截到 40 字，实测经常看不出这次委派干什么，
    /// 而模型要判断的正是「该不该续这一个」
    /// </summary>
    [Fact]
    public void Roster_PrefersTheFullTaskOverTheDisplayTitle()
    {
        string roster = HistoryHandoff.BuildSubSessionRoster(
            [Sub("abc123", "查配置加载", "查清楚配置是怎么从 Excel 一路加载到运行时的，特别是热更那条路")]);

        Assert.Contains("热更那条路", roster);
    }

    [Fact]
    public void Roster_FlattensMultilineTasksOntoOneLine()
    {
        //一项占一行,换行会把清单冲散
        string roster = HistoryHandoff.BuildSubSessionRoster(
            [Sub("abc123", "标题", "第一行\n第二行")]);

        Assert.Contains("第一行 第二行", roster);
    }

    [Fact]
    public void Roster_MarksTheOnesStillRunning()
    {
        //续跑一个没跑完的与续跑一个已交回结论的,是两件事
        ChatSessionMeta running = Sub("abc123", "还在查");
        running.BackgroundReportPending = true;

        Assert.Contains("[still running]", HistoryHandoff.BuildSubSessionRoster([running]));
        Assert.DoesNotContain("[still running]", HistoryHandoff.BuildSubSessionRoster([Sub("d", "完事了")]));
    }

    [Fact]
    public void Roster_IsCappedAndKeepsTheRecentOnes()
    {
        //会话索引给的是最近在前;老的那些用户多半重新描述而不是点名续跑
        List<ChatSessionMeta> subs = [.. Enumerable.Range(0, 20).Select(i => Sub($"id{i}", $"任务{i}"))];

        string roster = HistoryHandoff.BuildSubSessionRoster(subs, max: 3);

        Assert.Contains("id0", roster);
        Assert.Contains("id2", roster);
        Assert.DoesNotContain("id3", roster);
    }
}
